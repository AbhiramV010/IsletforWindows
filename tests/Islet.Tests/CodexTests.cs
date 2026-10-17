using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Islet.App.Agents;
using Islet.App.ViewModels;
using Islet.Hook;
using Islet.Hook.Agents;
using Islet.Hook.Protocol;

namespace Islet.Tests;

public class CodexAdapterTests
{
    private const string AllowJson =
        """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"allow"}}}""";

    private readonly CodexAdapter _adapter = new();

    [Fact]
    public void Maps_a_bash_permission_request()
    {
        HookMessage message = Map("codex/permission-request-bash.json");

        Assert.Equal("codex", message.Agent);
        Assert.True(message.IsPermissionRequest);
        Assert.Equal("019a1b2c-3d4e-7f80-9a1b-2c3d4e5f6a7b", message.SessionId);
        Assert.Equal(@"C:\dev\repo", message.Cwd);
        Assert.Equal("Bash", message.Tool);
        Assert.Equal("npm test", message.Input!.Value.GetProperty("command").GetString());
        Assert.Equal("Run the test suite", message.Input!.Value.GetProperty("description").GetString());
    }

    [Fact]
    public void Maps_apply_patch_and_mcp_requests()
    {
        Assert.Equal("apply_patch", Map("codex/permission-request-apply-patch.json").Tool);
        Assert.Equal("mcp__filesystem__read_file", Map("codex/permission-request-mcp.json").Tool);
    }

    [Fact]
    public void Maps_post_tool_use_and_stop()
    {
        HookMessage post = Map("codex/post-tool-use.json");
        Assert.Equal("PostToolUse", post.Event);
        Assert.Equal("Bash", post.Tool);

        HookMessage stop = Map("codex/stop.json");
        Assert.Equal("Stop", stop.Event);
        Assert.False(stop.IsPermissionRequest);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"hook_event_name":7}""")]
    public void Rejects_payloads_without_an_event(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Null(_adapter.Map(document.RootElement));
    }

    [Fact]
    public void Allow_is_the_bare_decision_with_nothing_Codex_would_reject() =>
        Assert.Equal(AllowJson, _adapter.FormatDecision(new HookReply { Id = "x", Decision = Decisions.Allow }));

    [Fact]
    public void Deny_always_carries_a_message()
    {
        Assert.Equal(
            """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"deny","message":"Denied from Islet"}}}""",
            _adapter.FormatDecision(new HookReply { Id = "x", Decision = Decisions.Deny }));
        Assert.Equal(
            """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"deny","message":"Not now"}}}""",
            _adapter.FormatDecision(new HookReply { Id = "x", Decision = Decisions.Deny, Message = "Not now" }));
    }

    [Fact]
    public void Output_never_contains_the_fields_Codex_fails_closed_on()
    {
        string allow = _adapter.FormatDecision(new HookReply { Id = "x", Decision = Decisions.Allow })!;
        string deny = _adapter.FormatDecision(new HookReply { Id = "x", Decision = Decisions.Deny })!;

        foreach (string field in new[] { "updatedInput", "updatedPermissions", "interrupt" })
        {
            Assert.DoesNotContain(field, allow);
            Assert.DoesNotContain(field, deny);
        }
    }

    [Theory]
    [InlineData(Decisions.Passthrough)]
    [InlineData("")]
    [InlineData("approve")]
    public void Anything_else_prints_nothing(string decision) =>
        Assert.Null(_adapter.FormatDecision(new HookReply { Id = "x", Decision = decision }));

    [Fact]
    public void Agent_argument_selects_the_adapter()
    {
        Assert.IsType<CodexAdapter>(AgentAdapters.Find("codex"));
        Assert.IsType<CodexAdapter>(AgentAdapters.Find("CODEX"));
        Assert.IsType<ClaudeAdapter>(AgentAdapters.Find("claude"));
    }

    private HookMessage Map(string fixture)
    {
        using JsonDocument document = JsonDocument.Parse(Fixtures.Bytes(fixture));
        return _adapter.Map(document.RootElement) ?? throw new InvalidOperationException("Map returned null");
    }
}

public class CodexHookRunnerTests
{
    private readonly string _pipeName = TestPipes.NewName();

    [Theory]
    [InlineData("allow", """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"allow"}}}""")]
    [InlineData("deny", """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"deny","message":"Denied from Islet"}}}""")]
    [InlineData("passthrough", "")]
    public async Task Round_trip_prints_what_the_app_decided(string decision, string expectedStdout)
    {
        await using var app = new FakeApp(_pipeName, m => PipeProtocol.Serialize(new HookReply { Id = m.Id, Decision = decision }));

        (int exitCode, string stdout) = await RunAsync("codex/permission-request-bash.json");

        Assert.Equal(0, exitCode);
        Assert.Equal(expectedStdout, stdout);
        HookMessage sent = PipeProtocol.ParseMessage(await app.Received)!;
        Assert.Equal("codex", sent.Agent);
        Assert.Equal("Bash", sent.Tool);
    }

    [Theory]
    [InlineData("codex/post-tool-use.json", "PostToolUse")]
    [InlineData("codex/stop.json", "Stop")]
    public async Task Other_events_are_fire_and_forget_and_print_nothing(string fixture, string eventName)
    {
        await using var app = new FakeApp(_pipeName, _ => null, holdOpenWithoutReply: true);

        var stopwatch = Stopwatch.StartNew();
        (int exitCode, string stdout) = await RunAsync(fixture);

        Assert.Equal(0, exitCode);
        Assert.Equal("", stdout);
        Assert.Equal(eventName, PipeProtocol.ParseMessage(await app.Received)!.Event);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData("codex/permission-request-bash.json")]
    [InlineData("codex/stop.json")]
    [InlineData("codex/malformed.json")]
    public async Task Without_the_app_it_exits_0_silently(string fixture)
    {
        (int exitCode, string stdout) = await RunAsync(fixture);
        Assert.Equal(0, exitCode);
        Assert.Equal("", stdout);
    }

    [Fact]
    public async Task A_malformed_reply_prints_nothing()
    {
        await using var app = new FakeApp(_pipeName, m => """{"id":"someone-else","decision":"allow"}""");
        (int exitCode, string stdout) = await RunAsync("codex/permission-request-bash.json");
        Assert.Equal(0, exitCode);
        Assert.Equal("", stdout);
    }

    private async Task<(int ExitCode, string Stdout)> RunAsync(string fixture)
    {
        var runner = new HookRunner(_pipeName, HookRunner.DefaultConnectTimeout);
        using var stdout = new MemoryStream();
        int exitCode = await runner.RunAsync(["codex"], Fixtures.Stream(fixture), stdout, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));
        return (exitCode, Encoding.UTF8.GetString(stdout.ToArray()));
    }
}

public class CodexConfigAndPreviewTests
{
    [Fact]
    public void Print_config_has_the_three_codex_events_and_both_command_forms()
    {
        string json = HookConfig.Build("codex", @"C:\tools\Islet.Hook.exe")!;
        using JsonDocument config = JsonDocument.Parse(json);
        JsonElement hooks = config.RootElement.GetProperty("hooks");

        Assert.Equal(new[] { "PermissionRequest", "PostToolUse", "Stop" }, hooks.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.False(hooks.TryGetProperty("Notification", out _));

        JsonElement permission = hooks.GetProperty("PermissionRequest")[0].GetProperty("hooks")[0];
        Assert.Equal("command", permission.GetProperty("type").GetString());
        Assert.Equal("\"C:/tools/Islet.Hook.exe\" codex", permission.GetProperty("command").GetString());
        Assert.Equal(@"C:\tools\Islet.Hook.exe codex", permission.GetProperty("commandWindows").GetString());
        Assert.Equal(600, permission.GetProperty("timeout").GetInt32());

        foreach (string eventName in new[] { "PostToolUse", "Stop" })
        {
            JsonElement hook = hooks.GetProperty(eventName)[0].GetProperty("hooks")[0];
            Assert.Equal(10, hook.GetProperty("timeout").GetInt32());
        }
    }

    [Theory]
    [InlineData(@"C:\tools\Islet.Hook.exe", @"C:\tools\Islet.Hook.exe codex")]
    [InlineData("C:/tools/Islet.Hook.exe", @"C:\tools\Islet.Hook.exe codex")]
    [InlineData(@"C:\Program Files\Islet\Islet.Hook.exe", "\"C:\\Program Files\\Islet\\Islet.Hook.exe\" codex")]
    public void Windows_command_is_bare_unless_the_path_has_spaces(string exe, string expected) =>
        Assert.Equal(expected, HookConfig.CommandWindowsFor(exe, "codex"));

    [Fact]
    public void Apply_patch_shows_the_files_it_touches()
    {
        using JsonDocument document = JsonDocument.Parse(Fixtures.Bytes("codex/permission-request-apply-patch.json"));
        JsonElement input = document.RootElement.GetProperty("tool_input").Clone();

        PermissionPreview preview = PermissionPreview.From("apply_patch", input);

        Assert.Equal("Update src/app.ts, Add notes.txt, Delete old.txt", preview.Line);
        Assert.Contains("*** Begin Patch", preview.Full);
    }

    [Fact]
    public void Apply_patch_without_file_headers_falls_back_to_the_description()
    {
        using JsonDocument document = JsonDocument.Parse("""{"command":"garbled","description":"Apply a patch"}""");
        Assert.Equal("Apply a patch", PermissionPreview.From("apply_patch", document.RootElement.Clone()).Line);
    }

    [Fact]
    public void Unknown_tools_show_the_description_when_there_is_one()
    {
        using JsonDocument document = JsonDocument.Parse("""{"path":"a.txt","description":"Read a.txt"}""");
        Assert.Equal("Read a.txt", PermissionPreview.From("mcp__filesystem__read_file", document.RootElement.Clone()).Line);
    }

    [Fact]
    public void A_codex_request_is_labelled_Codex_with_its_own_colour()
    {
        using JsonDocument input = JsonDocument.Parse(Fixtures.Bytes("codex/permission-request-bash.json"));
        var message = new HookMessage
        {
            Id = "1",
            Agent = "codex",
            Event = "PermissionRequest",
            Cwd = @"C:\dev\repo",
            Tool = "Bash",
            Input = input.RootElement.GetProperty("tool_input").Clone(),
        };

        PermissionRequestViewModel card = new PermissionQueueViewModel().Enqueue(message);

        Assert.Equal("Codex", card.AgentName);
        Assert.Equal("npm test", card.Preview);
        Assert.NotSame(AgentColors.For("claude"), card.AgentBrush);
        Assert.Same(AgentColors.For("codex"), card.AgentBrush);
    }

    [Fact]
    public void Codex_progress_events_drive_the_same_session_line()
    {
        var sessions = new AgentSessionsViewModel();
        sessions.Apply(new HookMessage { Id = "1", Agent = "codex", Event = "PostToolUse", SessionId = "s", Cwd = "C:/dev/repo", Tool = "Bash" });
        Assert.Equal("Working · Bash", sessions.Current!.Status);
        Assert.Equal("Codex", sessions.Current.AgentName);

        sessions.Apply(new HookMessage { Id = "2", Agent = "codex", Event = "Stop", SessionId = "s", Cwd = "C:/dev/repo" });
        Assert.Equal("Codex is done", sessions.Current!.DoneHeadline);
    }
}
