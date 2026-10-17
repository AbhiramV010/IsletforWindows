using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Islet.Hook.Agents;
using Islet.Hook.Protocol;

namespace Islet.Hook;

/// <summary>
/// One hook invocation: read the agent's payload, forward it to the app, and for a
/// permission request print the app's decision.
///
/// Fail-safe: every path that isn't an explicit allow or deny from the app ends with
/// exit 0 and empty stdout, so the agent shows its normal prompt.
/// </summary>
internal sealed class HookRunner(string pipeName, TimeSpan connectTimeout)
{
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>Hook payloads are small. Anything past this is not a payload we want.</summary>
    private const int MaxPayloadBytes = 8 * 1024 * 1024;

    public async Task<int> RunAsync(string[] args, Stream stdin, Stream stdout, CancellationToken cancellationToken)
    {
        try
        {
            string? output = await GetOutputAsync(args, stdin, cancellationToken).ConfigureAwait(false);
            if (output is not null)
            {
                // One write, so a failure can't leave half a JSON object on stdout.
                byte[] bytes = Encoding.UTF8.GetBytes(output);
                await stdout.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stdout.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            HookLog.Write($"Failed, falling back to the agent's prompt: {exception.GetType().Name}: {exception.Message}");
        }

        return 0;
    }

    private async Task<string?> GetOutputAsync(string[] args, Stream stdin, CancellationToken cancellationToken)
    {
        IAgentAdapter? adapter = args.Length > 0 ? AgentAdapters.Find(args[0]) : null;
        if (adapter is null)
        {
            HookLog.Write($"Unknown agent argument '{(args.Length > 0 ? args[0] : "")}'");
            return null;
        }

        byte[] payload = await ReadPayloadAsync(stdin, cancellationToken).ConfigureAwait(false);
        HookMessage? message;
        using (JsonDocument document = JsonDocument.Parse(payload))
        {
            message = adapter.Map(document.RootElement);
        }

        if (message is null)
        {
            HookLog.Write($"{adapter.Name}: payload not recognised");
            return null;
        }

        message.Id = Guid.NewGuid().ToString("N");

        if (!PipeProbe.Exists(pipeName))
        {
            HookLog.Write($"{adapter.Name} {message.Event}: app not running");
            return null;
        }

        using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            // CurrentUserOnly refuses a pipe owned by anyone else, so another user can't
            // squat the name and answer for the app.
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        try
        {
            await pipe.ConnectAsync((int)connectTimeout.TotalMilliseconds, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            HookLog.Write($"{adapter.Name} {message.Event}: app not running");
            return null;
        }

        await PipeProtocol.WriteLineAsync(pipe, PipeProtocol.Serialize(message), cancellationToken).ConfigureAwait(false);

        if (!message.IsPermissionRequest)
        {
            return null;
        }

        string? line = await PipeProtocol.ReadLineAsync(pipe, cancellationToken).ConfigureAwait(false);
        HookReply? reply = PipeProtocol.ParseReply(line, message.Id);
        string decision = reply?.Decision ?? (line is null ? "no reply" : "malformed reply");
        HookLog.Write($"{adapter.Name} {message.Event} {message.Tool}: {decision}");

        return reply is { Decision: Decisions.Allow or Decisions.Deny } ? adapter.FormatDecision(reply) : null;
    }

    private static async Task<byte[]> ReadPayloadAsync(Stream stdin, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[16 * 1024];
        int read;
        while ((read = await stdin.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxPayloadBytes)
            {
                throw new InvalidDataException("Payload too large");
            }
        }

        return buffer.ToArray();
    }
}
