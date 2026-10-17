using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Islet.App.Agents;
using Islet.Hook;
using Islet.Hook.Protocol;

namespace Islet.Tests;

/// <summary>The app's pipe server, including a full round trip through the real shim logic.</summary>
public class PipeServerTests
{
    private readonly string _pipeName = TestPipes.NewName();

    [Fact]
    public void Security_grants_only_the_current_user()
    {
        SecurityIdentifier me = WindowsIdentity.GetCurrent().User!;
        PipeSecurity security = PipeServer.CreateSecurity();

        AuthorizationRuleCollection rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
        PipeAccessRule rule = Assert.Single(rules.Cast<PipeAccessRule>());
        Assert.Equal(me, rule.IdentityReference);
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(me, security.GetOwner(typeof(SecurityIdentifier)));
    }

    [Fact]
    public async Task Live_pipe_carries_the_user_only_acl()
    {
        var handler = new RecordingHandler();
        using var server = new PipeServer(_pipeName, handler);
        server.Start();
        await TestPipes.WaitUntilListeningAsync(_pipeName);

        await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(2000);

        SecurityIdentifier me = WindowsIdentity.GetCurrent().User!;
        PipeSecurity live = client.GetAccessControl();
        IEnumerable<PipeAccessRule> rules = live.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>();
        Assert.All(rules, rule => Assert.Equal(me, rule.IdentityReference));
        Assert.NotEmpty(rules);
    }

    [Theory]
    [InlineData(Decisions.Allow, """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"allow"}}}""")]
    [InlineData(Decisions.Deny, """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"deny","message":"Denied from Islet"}}}""")]
    [InlineData(Decisions.Passthrough, "")]
    public async Task Shim_and_server_round_trip(string decision, string expectedStdout)
    {
        var handler = new RecordingHandler { Decision = decision };
        using var server = new PipeServer(_pipeName, handler);
        server.Start();
        await TestPipes.WaitUntilListeningAsync(_pipeName);

        string stdout = await RunShimAsync("claude/permission-request-bash.json");

        Assert.Equal(expectedStdout, stdout);
        HookMessage request = Assert.Single(handler.PermissionRequests);
        Assert.Equal("Bash", request.Tool);
    }

    [Fact]
    public async Task Fire_and_forget_events_reach_the_handler()
    {
        var handler = new RecordingHandler();
        using var server = new PipeServer(_pipeName, handler);
        server.Start();
        await TestPipes.WaitUntilListeningAsync(_pipeName);

        Assert.Equal("", await RunShimAsync("claude/stop.json"));

        HookMessage stop = await handler.NextEvent.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Stop", stop.Event);
        Assert.Empty(handler.PermissionRequests);
    }

    [Fact]
    public async Task Client_hanging_up_cancels_the_pending_request()
    {
        var handler = new RecordingHandler { WaitForever = true };
        using var server = new PipeServer(_pipeName, handler);
        server.Start();
        await TestPipes.WaitUntilListeningAsync(_pipeName);

        var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(2000);
        string line = PipeProtocol.Serialize(new HookMessage { Id = "r1", Agent = "claude", Event = "PermissionRequest", Tool = "Bash" });
        await PipeProtocol.WriteLineAsync(client, line, CancellationToken.None);

        await handler.RequestArrived.WaitAsync(TimeSpan.FromSeconds(5));
        await client.DisposeAsync();

        await handler.RequestCancelled.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Malformed_lines_are_dropped()
    {
        var handler = new RecordingHandler();
        using var server = new PipeServer(_pipeName, handler);
        server.Start();
        await TestPipes.WaitUntilListeningAsync(_pipeName);

        await using (var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(2000);
            await client.WriteAsync(Encoding.UTF8.GetBytes("{\"v\":1,\"id\":\"x\"\n"));
            await client.FlushAsync();
        }

        // A valid event afterwards still gets through, and is the first one the handler sees.
        Assert.Equal("", await RunShimAsync("claude/notification.json"));
        HookMessage first = await handler.NextEvent.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Notification", first.Event);
    }

    private async Task<string> RunShimAsync(string fixture)
    {
        var runner = new HookRunner(_pipeName, TimeSpan.FromSeconds(2));
        using var stdout = new MemoryStream();
        await runner.RunAsync(["claude"], Fixtures.Stream(fixture), stdout, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        return Encoding.UTF8.GetString(stdout.ToArray());
    }

    private sealed class RecordingHandler : IHookMessageHandler
    {
        private readonly TaskCompletionSource<HookMessage> _event = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Decision { get; init; } = Decisions.Allow;

        public bool WaitForever { get; init; }

        public List<HookMessage> PermissionRequests { get; } = [];

        public Task<HookMessage> NextEvent => _event.Task;

        public Task RequestArrived => _arrived.Task;

        public Task RequestCancelled => _cancelled.Task;

        public void OnEvent(HookMessage message) => _event.TrySetResult(message);

        public async Task<HookReply> OnPermissionRequestAsync(HookMessage message, CancellationToken clientGone)
        {
            lock (PermissionRequests)
            {
                PermissionRequests.Add(message);
            }

            _arrived.TrySetResult();
            if (WaitForever)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, clientGone);
                }
                catch (OperationCanceledException)
                {
                    _cancelled.TrySetResult();
                }
            }

            return new HookReply { Decision = Decision };
        }
    }
}
