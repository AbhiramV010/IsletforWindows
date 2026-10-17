using Islet.App.ViewModels;
using Islet.Hook.Protocol;

namespace Islet.Tests;

public class AgentSessionsTests
{
    private DateTime _now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Post_tool_use_means_working_and_names_the_tool()
    {
        AgentSessionsViewModel sessions = New();
        sessions.Apply(Event("PostToolUse", tool: "Bash"));

        AgentSessionViewModel current = sessions.Current!;
        Assert.Equal(AgentState.Working, current.State);
        Assert.Equal("Claude Code", current.AgentName);
        Assert.Equal("repo", current.Repo);
        Assert.Equal("Working · Bash", current.Status);

        sessions.Apply(Event("PostToolUse", tool: "Edit"));
        Assert.Equal("Working · Edit", current.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("permission_prompt")]
    [InlineData("elicitation_dialog")]
    public void Blocking_notifications_mean_waiting(string? type)
    {
        AgentSessionsViewModel sessions = New();
        sessions.Apply(Event("PostToolUse", tool: "Bash"));
        sessions.Apply(Event("Notification", notificationType: type));

        Assert.Equal(AgentState.Waiting, sessions.Current!.State);
        Assert.Equal("Waiting for you", sessions.Current.Status);
    }

    [Theory]
    [InlineData("idle_prompt")]
    [InlineData("auth_success")]
    public void Idle_and_auth_notifications_are_not_progress(string type)
    {
        AgentSessionsViewModel sessions = New();
        sessions.Apply(Event("Notification", notificationType: type));
        Assert.Null(sessions.Current);

        sessions.Apply(Event("PostToolUse", tool: "Bash"));
        sessions.Apply(Event("Notification", notificationType: type));
        Assert.Equal(AgentState.Working, sessions.Current!.State);
    }

    [Fact]
    public void Working_again_after_waiting()
    {
        AgentSessionsViewModel sessions = New();
        sessions.Apply(Event("Notification"));
        sessions.Apply(Event("PostToolUse", tool: "Write"));
        Assert.Equal("Working · Write", sessions.Current!.Status);
    }

    [Fact]
    public void Stop_means_done_and_clears_after_the_done_window()
    {
        AgentSessionsViewModel sessions = New();
        sessions.Apply(Event("PostToolUse", tool: "Bash"));
        sessions.Apply(Event("Stop"));

        Assert.Equal(AgentState.Done, sessions.Current!.State);
        Assert.Equal("Done", sessions.Current.Status);

        Advance(AgentSessionsViewModel.DoneVisibleFor - TimeSpan.FromMilliseconds(1));
        sessions.Tick();
        Assert.NotNull(sessions.Current);

        Advance(TimeSpan.FromMilliseconds(1));
        sessions.Tick();
        Assert.Null(sessions.Current);
        Assert.Equal(0, sessions.Count);
    }

    [Theory]
    [InlineData("claude", "Claude is done")]
    [InlineData("codex", "Codex is done")]
    public void Done_headline_names_the_agent(string agent, string expected)
    {
        AgentSessionsViewModel sessions = New();
        HookMessage stop = Event("Stop");
        stop.Agent = agent;
        sessions.Apply(stop);

        Assert.Equal(expected, sessions.Current!.DoneHeadline);
    }

    [Fact]
    public void Dismiss_done_closes_finished_sessions_and_leaves_working_ones()
    {
        AgentSessionsViewModel sessions = New();
        sessions.Apply(Event("PostToolUse", sessionId: "busy", cwd: "C:/dev/busy", tool: "Bash"));
        Advance(TimeSpan.FromSeconds(1));
        sessions.Apply(Event("Stop", sessionId: "finished", cwd: "C:/dev/finished"));
        Assert.Equal("finished", sessions.Current!.Repo);

        sessions.DismissDone();

        Assert.Equal("busy", sessions.Current!.Repo);
        Assert.Equal(AgentState.Working, sessions.Current.State);
        Assert.Equal(1, sessions.Count);
    }

    [Fact]
    public void A_new_turn_cancels_the_pending_fade_out()
    {
        AgentSessionsViewModel sessions = New();
        sessions.Apply(Event("Stop"));
        Advance(TimeSpan.FromSeconds(3));
        sessions.Apply(Event("PostToolUse", tool: "Bash"));

        Advance(TimeSpan.FromSeconds(10));
        sessions.Tick();

        Assert.Equal(AgentState.Working, sessions.Current!.State);
    }

    [Fact]
    public void Quiet_sessions_are_dropped_as_stale()
    {
        AgentSessionsViewModel sessions = New();
        sessions.Apply(Event("PostToolUse", tool: "Bash"));

        Advance(AgentSessionsViewModel.StaleAfter);
        sessions.Tick();

        Assert.Null(sessions.Current);
    }

    [Fact]
    public void Most_recent_session_is_current_with_a_count_badge()
    {
        AgentSessionsViewModel sessions = New();
        sessions.Apply(Event("PostToolUse", sessionId: "a", cwd: @"C:\dev\first", tool: "Bash"));
        Advance(TimeSpan.FromSeconds(1));
        sessions.Apply(Event("PostToolUse", sessionId: "b", cwd: @"C:\dev\second", tool: "Edit"));

        Assert.Equal("second", sessions.Current!.Repo);
        Assert.Equal("+1", sessions.MoreBadge);

        Advance(TimeSpan.FromSeconds(1));
        sessions.Apply(Event("Stop", sessionId: "b", cwd: @"C:\dev\second"));
        Advance(AgentSessionsViewModel.DoneVisibleFor);
        sessions.Tick();

        Assert.Equal("first", sessions.Current!.Repo);
        Assert.Null(sessions.MoreBadge);
    }

    [Fact]
    public void Permission_requests_and_unknown_events_are_ignored()
    {
        AgentSessionsViewModel sessions = New();
        sessions.Apply(Event("PermissionRequest", tool: "Bash"));
        sessions.Apply(Event("SessionStart"));
        Assert.Null(sessions.Current);
    }

    private AgentSessionsViewModel New() => new(() => _now);

    private void Advance(TimeSpan by) => _now += by;

    private static HookMessage Event(
        string name, string sessionId = "s1", string cwd = @"C:\dev\repo", string? tool = null, string? notificationType = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Agent = "claude",
        Event = name,
        SessionId = sessionId,
        Cwd = cwd,
        Tool = tool,
        NotificationType = notificationType,
    };
}
