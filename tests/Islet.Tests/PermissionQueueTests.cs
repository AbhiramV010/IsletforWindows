using System.Text.Json;
using Islet.App.Agents;
using Islet.App.ViewModels;
using Islet.Hook.Protocol;

namespace Islet.Tests;

public class PermissionQueueTests
{
    private DateTime _now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Allow_resolves_the_head_of_the_queue()
    {
        PermissionQueueViewModel queue = NewQueue();
        PermissionRequestViewModel first = queue.Enqueue(Request("npm test"));
        PermissionRequestViewModel second = queue.Enqueue(Request("npm run lint"));
        Advance(PermissionQueueViewModel.InputGuard);

        Assert.Same(first, queue.Current);
        Assert.Equal("+1", queue.MoreBadge);
        Assert.True(queue.Allow());

        Assert.Equal(Decisions.Allow, (await first.Reply).Decision);
        Assert.Same(second, queue.Current);
        Assert.Null(queue.MoreBadge);
        Assert.False(second.Reply.IsCompleted);
    }

    [Fact]
    public async Task Deny_resolves_with_deny()
    {
        PermissionQueueViewModel queue = NewQueue();
        PermissionRequestViewModel request = queue.Enqueue(Request("rm -rf build"));
        Advance(PermissionQueueViewModel.InputGuard);

        Assert.True(queue.Deny());
        Assert.Equal(Decisions.Deny, (await request.Reply).Decision);
        Assert.Null(queue.Current);
    }

    [Fact]
    public void Input_is_ignored_right_after_a_card_appears()
    {
        PermissionQueueViewModel queue = NewQueue();
        PermissionRequestViewModel first = queue.Enqueue(Request("npm test"));
        PermissionRequestViewModel second = queue.Enqueue(Request("npm run lint"));

        Assert.False(queue.Allow());
        Assert.False(first.Reply.IsCompleted);

        Advance(PermissionQueueViewModel.InputGuard);
        Assert.True(queue.Allow());

        // The next card has just appeared, so an immediate second click is ignored too.
        Assert.False(queue.Allow());
        Assert.False(second.Reply.IsCompleted);
    }

    [Fact]
    public void Allow_with_nothing_pending_does_nothing() => Assert.False(NewQueue().Allow());

    [Fact]
    public async Task Cancel_passes_through_and_removes()
    {
        PermissionQueueViewModel queue = NewQueue();
        PermissionRequestViewModel first = queue.Enqueue(Request("npm test"));
        PermissionRequestViewModel second = queue.Enqueue(Request("npm run lint"));

        queue.Cancel(first);

        Assert.Equal(Decisions.Passthrough, (await first.Reply).Decision);
        Assert.Same(second, queue.Current);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public async Task CancelAll_passes_everything_through()
    {
        PermissionQueueViewModel queue = NewQueue();
        PermissionRequestViewModel first = queue.Enqueue(Request("a"));
        PermissionRequestViewModel second = queue.Enqueue(Request("b"));

        queue.CancelAll();

        Assert.Equal(Decisions.Passthrough, (await first.Reply).Decision);
        Assert.Equal(Decisions.Passthrough, (await second.Reply).Decision);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Requests_go_overdue_after_30_seconds()
    {
        PermissionQueueViewModel queue = NewQueue();
        PermissionRequestViewModel first = queue.Enqueue(Request("npm test"));
        Assert.False(first.IsOverdue);

        Advance(PermissionQueueViewModel.OverdueAfter);
        queue.Enqueue(Request("npm run lint"));

        Assert.True(first.IsOverdue);
    }

    [Fact]
    public void Card_shows_agent_repo_tool_and_preview()
    {
        PermissionRequestViewModel request = NewQueue().Enqueue(Request("npm test", cwd: @"C:\dev\my-repo\"));
        Assert.Equal("Claude Code", request.AgentName);
        Assert.Equal("my-repo", request.Repo);
        Assert.Equal("Bash", request.Tool);
        Assert.Equal("npm test", request.Preview);
    }

    private PermissionQueueViewModel NewQueue() => new(() => _now);

    private void Advance(TimeSpan by) => _now += by;

    private static HookMessage Request(string command, string cwd = "C:/dev/repo")
    {
        using JsonDocument input = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, string> { ["command"] = command }));
        return new HookMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Agent = "claude",
            Event = PipeProtocol.PermissionRequestEvent,
            Cwd = cwd,
            Tool = "Bash",
            Input = input.RootElement.Clone(),
        };
    }
}

public class PermissionPreviewTests
{
    [Fact]
    public void Bash_shows_the_command()
    {
        PermissionPreview preview = PermissionPreview.From("Bash", Json("""{"command":"npm test","description":"Run tests"}"""));
        Assert.Equal("npm test", preview.Line);
        Assert.Equal("npm test", preview.Full);
    }

    [Fact]
    public void Multi_line_commands_fold_onto_one_line()
    {
        PermissionPreview preview = PermissionPreview.From("PowerShell", Json("""{"command":"cd src\r\n  dotnet build\n"}"""));
        Assert.Equal("cd src ⏎ dotnet build ⏎ ", preview.Line);
        Assert.Contains("\n", preview.Full);
    }

    [Theory]
    [InlineData("Edit")]
    [InlineData("Write")]
    public void File_tools_show_the_path(string tool)
    {
        PermissionPreview preview = PermissionPreview.From(tool, Json("""{"file_path":"C:\\dev\\repo\\a.ts","content":"x"}"""));
        Assert.Equal(@"C:\dev\repo\a.ts", preview.Line);
        Assert.StartsWith(@"C:\dev\repo\a.ts", preview.Full);
        Assert.Contains("\"content\"", preview.Full);
    }

    [Fact]
    public void Unknown_tools_show_compact_json() =>
        Assert.Equal("""{"server":"x","n":1}""", PermissionPreview.From("mcp__thing", Json("""{"server":"x","n":1}""")).Line);

    [Fact]
    public void Long_previews_are_truncated_with_an_ellipsis()
    {
        string command = new('a', 1000);
        PermissionPreview preview = PermissionPreview.From("Bash", Json($$"""{"command":"{{command}}"}"""));
        Assert.True(preview.Line.Length <= 300);
        Assert.EndsWith("…", preview.Line);
    }

    [Theory]
    [InlineData(@"C:\dev\repo", "repo")]
    [InlineData("C:/dev/repo/", "repo")]
    [InlineData("/home/me/project", "project")]
    [InlineData("repo", "repo")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Repo_is_the_last_path_segment(string? cwd, string expected) =>
        Assert.Equal(expected, PermissionPreview.RepoName(cwd));

    private static JsonElement Json(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
