using System.IO.Pipes;
using System.Text;
using Islet.Hook;
using Islet.Hook.Protocol;

namespace Islet.Tests;

internal static class Fixtures
{
    public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    public static byte[] Bytes(string name) => File.ReadAllBytes(PathOf(name));

    public static MemoryStream Stream(string name) => new(Bytes(name));
}

internal static class TestPipes
{
    /// <summary>A private pipe name the shim accepts through <c>ISLET_PIPE_NAME</c>.</summary>
    public static string NewName() => PipeProtocol.TestPipePrefix + Guid.NewGuid().ToString("N");

    /// <summary>The server creates its pipe on a background task; wait for it like a running app.</summary>
    public static async Task WaitUntilListeningAsync(string pipeName)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!PipeProbe.Exists(pipeName))
        {
            Assert.True(DateTime.UtcNow < deadline, $"pipe {pipeName} never appeared");
            await Task.Delay(10);
        }
    }
}

/// <summary>
/// Stands in for the app: accepts one connection, records the line, and answers with
/// whatever <paramref name="respond"/> returns (null means hang up without replying).
/// </summary>
internal sealed class FakeApp : IAsyncDisposable
{
    private readonly NamedPipeServerStream _server;
    private readonly Task _run;
    private readonly TaskCompletionSource<string?> _received = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeApp(string pipeName, Func<HookMessage, string?> respond, bool holdOpenWithoutReply = false)
    {
        _server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        _run = RunAsync(respond, holdOpenWithoutReply);
    }

    /// <summary>The raw line the shim sent.</summary>
    public Task<string?> Received => _received.Task;

    /// <summary>Completes once the shim hangs up.</summary>
    public Task Closed => _closed.Task;

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        try
        {
            await _run.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // The server was torn down mid-read; the test already has what it needs.
        }
    }

    private async Task RunAsync(Func<HookMessage, string?> respond, bool holdOpenWithoutReply)
    {
        try
        {
            await _server.WaitForConnectionAsync();
            string? line = await PipeProtocol.ReadLineAsync(_server, CancellationToken.None);
            _received.TrySetResult(line);

            HookMessage? message = PipeProtocol.ParseMessage(line);
            string? reply = message is null ? null : respond(message);
            if (reply is not null)
            {
                await _server.WriteAsync(Encoding.UTF8.GetBytes(reply + "\n"));
                await _server.FlushAsync();
                _server.WaitForPipeDrain();
            }

            if (reply is not null || holdOpenWithoutReply)
            {
                // Wait for the shim to hang up.
                byte[] buffer = new byte[1];
                await _server.ReadAsync(buffer);
            }
        }
        catch (Exception exception)
        {
            _received.TrySetException(exception);
        }
        finally
        {
            // Hang up, so a shim still waiting for a reply sees the pipe close.
            await _server.DisposeAsync();
            _closed.TrySetResult();
        }
    }
}
