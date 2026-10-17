using System.Diagnostics;
using System.Text;
using Islet.Hook;
using Islet.Hook.Protocol;

namespace Islet.Tests;

/// <summary>The shim's logic in-process, against a fake app on a private pipe.</summary>
public class HookRunnerTests
{
    private const string AllowJson =
        """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"allow"}}}""";

    private const string DenyJson =
        """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"deny","message":"Denied from Islet"}}}""";

    private readonly string _pipeName = TestPipes.NewName();

    [Fact]
    public async Task Allow_from_the_app_prints_allow()
    {
        await using var app = new FakeApp(_pipeName, m => Reply(m.Id, "allow"));

        (int exitCode, string stdout) = await RunAsync("claude", "claude/permission-request-bash.json");

        Assert.Equal(0, exitCode);
        Assert.Equal(AllowJson, stdout);

        HookMessage sent = PipeProtocol.ParseMessage(await app.Received)!;
        Assert.Equal("claude", sent.Agent);
        Assert.Equal("PermissionRequest", sent.Event);
        Assert.Equal("Bash", sent.Tool);
        Assert.Equal("npm test", sent.Input!.Value.GetProperty("command").GetString());
        Assert.Equal(32, sent.Id.Length);
    }

    [Fact]
    public async Task Deny_from_the_app_prints_deny()
    {
        await using var app = new FakeApp(_pipeName, m => Reply(m.Id, "deny"));
        (int exitCode, string stdout) = await RunAsync("claude", "claude/permission-request-bash.json");
        Assert.Equal(0, exitCode);
        Assert.Equal(DenyJson, stdout);
    }

    [Fact]
    public async Task Passthrough_prints_nothing()
    {
        await using var app = new FakeApp(_pipeName, m => Reply(m.Id, "passthrough"));
        (int exitCode, string stdout) = await RunAsync("claude", "claude/permission-request-bash.json");
        Assert.Equal(0, exitCode);
        Assert.Equal("", stdout);
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("""{"decision":"allow"}""")]
    [InlineData("""{"id":"someone-else","decision":"allow"}""")]
    [InlineData("""{"id":"{ID}","decision":"yes"}""")]
    public async Task Malformed_or_mismatched_replies_print_nothing(string reply)
    {
        await using var app = new FakeApp(_pipeName, m => reply.Replace("{ID}", m.Id));
        (int exitCode, string stdout) = await RunAsync("claude", "claude/permission-request-bash.json");
        Assert.Equal(0, exitCode);
        Assert.Equal("", stdout);
    }

    [Fact]
    public async Task App_hanging_up_without_a_reply_prints_nothing()
    {
        await using var app = new FakeApp(_pipeName, _ => null);
        (int exitCode, string stdout) = await RunAsync("claude", "claude/permission-request-bash.json");
        Assert.Equal(0, exitCode);
        Assert.Equal("", stdout);
    }

    [Fact]
    public async Task App_not_running_prints_nothing_within_the_connect_timeout()
    {
        var stopwatch = Stopwatch.StartNew();
        (int exitCode, string stdout) = await RunAsync("claude", "claude/permission-request-bash.json");
        stopwatch.Stop();

        Assert.Equal(0, exitCode);
        Assert.Equal("", stdout);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"took {stopwatch.Elapsed}");
    }

    [Theory]
    [InlineData("claude/notification.json", "Notification")]
    [InlineData("claude/post-tool-use.json", "PostToolUse")]
    [InlineData("claude/stop.json", "Stop")]
    public async Task Other_events_are_fire_and_forget(string fixture, string eventName)
    {
        // The fake app never replies and keeps the pipe open: the shim must not wait.
        await using var app = new FakeApp(_pipeName, _ => null, holdOpenWithoutReply: true);

        var stopwatch = Stopwatch.StartNew();
        (int exitCode, string stdout) = await RunAsync("claude", fixture);
        stopwatch.Stop();

        Assert.Equal(0, exitCode);
        Assert.Equal("", stdout);
        Assert.Equal(eventName, PipeProtocol.ParseMessage(await app.Received)!.Event);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task Malformed_payload_prints_nothing_and_sends_nothing()
    {
        await using var app = new FakeApp(_pipeName, m => Reply(m.Id, "allow"));
        (int exitCode, string stdout) = await RunAsync("claude", "claude/malformed.json");

        Assert.Equal(0, exitCode);
        Assert.Equal("", stdout);
        Assert.False(app.Received.IsCompleted);
    }

    [Theory]
    [InlineData("opencode")]
    [InlineData("")]
    public async Task Unknown_agent_prints_nothing(string agent)
    {
        await using var app = new FakeApp(_pipeName, m => Reply(m.Id, "allow"));
        (int exitCode, string stdout) = await RunAsync(agent, "claude/permission-request-bash.json");
        Assert.Equal(0, exitCode);
        Assert.Equal("", stdout);
    }

    [Fact]
    public async Task No_arguments_prints_nothing()
    {
        var runner = new HookRunner(_pipeName, HookRunner.DefaultConnectTimeout);
        using var stdout = new MemoryStream();
        Assert.Equal(0, await runner.RunAsync([], Fixtures.Stream("claude/permission-request-bash.json"), stdout, CancellationToken.None));
        Assert.Equal(0, stdout.Length);
    }

    private static string Reply(string id, string decision) => PipeProtocol.Serialize(new HookReply { Id = id, Decision = decision });

    private async Task<(int ExitCode, string Stdout)> RunAsync(string agent, string fixture)
    {
        var runner = new HookRunner(_pipeName, HookRunner.DefaultConnectTimeout);
        using var stdout = new MemoryStream();
        int exitCode = await runner.RunAsync([agent], Fixtures.Stream(fixture), stdout, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));
        return (exitCode, Encoding.UTF8.GetString(stdout.ToArray()));
    }
}
