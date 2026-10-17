using System.Text.Json;
using Islet.Hook.Agents;
using Islet.Hook.Protocol;

namespace Islet.Tests;

public class ClaudeAdapterTests
{
    private const string AllowJson =
        """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"allow"}}}""";

    private const string DenyJson =
        """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"deny","message":"Denied from Islet"}}}""";

    private readonly ClaudeAdapter _adapter = new();

    [Fact]
    public void Maps_a_bash_permission_request()
    {
        HookMessage message = Map("claude/permission-request-bash.json");

        Assert.Equal("claude", message.Agent);
        Assert.Equal("PermissionRequest", message.Event);
        Assert.True(message.IsPermissionRequest);
        Assert.Equal("abc123", message.SessionId);
        Assert.Equal(@"C:\dev\repo", message.Cwd);
        Assert.Equal("Bash", message.Tool);
        Assert.Equal("npm test", message.Input!.Value.GetProperty("command").GetString());
    }

    [Fact]
    public void Maps_an_edit_permission_request()
    {
        HookMessage message = Map("claude/permission-request-edit.json");
        Assert.Equal("Edit", message.Tool);
        Assert.Equal(@"C:\dev\repo\src\app.ts", message.Input!.Value.GetProperty("file_path").GetString());
    }

    [Fact]
    public void Maps_a_notification()
    {
        HookMessage message = Map("claude/notification.json");
        Assert.Equal("Notification", message.Event);
        Assert.Equal("Claude needs your permission to use Bash", message.Message);
        Assert.Equal("permission_prompt", message.NotificationType);
        Assert.Null(message.Tool);
        Assert.Null(message.Input);
    }

    [Fact]
    public void Maps_post_tool_use_and_stop()
    {
        HookMessage post = Map("claude/post-tool-use.json");
        Assert.Equal("PostToolUse", post.Event);
        Assert.Equal("Write", post.Tool);

        HookMessage stop = Map("claude/stop.json");
        Assert.Equal("Stop", stop.Event);
        Assert.False(stop.IsPermissionRequest);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"PermissionRequest\"")]
    [InlineData("{}")]
    [InlineData("""{"hook_event_name":42}""")]
    public void Rejects_payloads_without_an_event(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Null(_adapter.Map(document.RootElement));
    }

    [Fact]
    public void Allow_matches_the_hooks_reference() =>
        Assert.Equal(AllowJson, _adapter.FormatDecision(new HookReply { Id = "x", Decision = Decisions.Allow }));

    [Fact]
    public void Deny_uses_the_default_message() =>
        Assert.Equal(DenyJson, _adapter.FormatDecision(new HookReply { Id = "x", Decision = Decisions.Deny }));

    [Fact]
    public void Deny_passes_the_app_message_through() =>
        Assert.Equal(
            """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"deny","message":"Not now"}}}""",
            _adapter.FormatDecision(new HookReply { Id = "x", Decision = Decisions.Deny, Message = "Not now" }));

    [Theory]
    [InlineData(Decisions.Passthrough)]
    [InlineData("")]
    [InlineData("approve")]
    public void Anything_else_prints_nothing(string decision) =>
        Assert.Null(_adapter.FormatDecision(new HookReply { Id = "x", Decision = decision }));

    private HookMessage Map(string fixture)
    {
        using JsonDocument document = JsonDocument.Parse(Fixtures.Bytes(fixture));
        return _adapter.Map(document.RootElement) ?? throw new InvalidOperationException("Map returned null");
    }
}
