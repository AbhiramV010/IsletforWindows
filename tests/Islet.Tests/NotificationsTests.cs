using Islet.App.Media;
using Islet.App.Notifications;
using Islet.App.Clock;
using Islet.App.ViewModels;
using Islet.Hook.Protocol;

namespace Islet.Tests;

public class NotificationsTests
{
    private DateTime _now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_notification_shows_for_six_seconds_then_goes()
    {
        NotificationsViewModel notifications = New();
        notifications.Enqueue(Item("Teams", "Ann", "hello"));
        Assert.Equal("Ann", notifications.Current!.Title);

        _now += NotificationsViewModel.ShowFor - TimeSpan.FromMilliseconds(1);
        notifications.Tick();
        Assert.NotNull(notifications.Current);

        _now += TimeSpan.FromMilliseconds(1);
        notifications.Tick();
        Assert.Null(notifications.Current);
    }

    [Fact]
    public void Several_notifications_queue_in_order_with_a_badge()
    {
        NotificationsViewModel notifications = New();
        notifications.Enqueue(Item("A", "one", ""));
        notifications.Enqueue(Item("B", "two", ""));
        notifications.Enqueue(Item("C", "three", ""));

        Assert.Equal("one", notifications.Current!.Title);
        Assert.Equal("+2", notifications.MoreBadge);

        _now += NotificationsViewModel.ShowFor;
        notifications.Tick();
        Assert.Equal("two", notifications.Current!.Title);
        Assert.Equal("+1", notifications.MoreBadge);

        notifications.Dismiss();
        Assert.Equal("three", notifications.Current!.Title);
        Assert.Null(notifications.MoreBadge);

        notifications.Dismiss();
        Assert.Null(notifications.Current);
    }

    [Fact]
    public void The_same_banner_seen_twice_quickly_is_shown_once()
    {
        NotificationsViewModel notifications = New();
        notifications.Enqueue(Item("Teams", "Ann", "hello"));
        _now += TimeSpan.FromSeconds(1);
        notifications.Enqueue(Item("Teams", "Ann", "hello"));

        Assert.Null(notifications.MoreBadge);
        notifications.Dismiss();
        Assert.Null(notifications.Current);
    }

    [Fact]
    public void The_same_text_much_later_is_a_new_notification()
    {
        NotificationsViewModel notifications = New();
        notifications.Enqueue(Item("Teams", "Ann", "hello"));
        _now += NotificationsViewModel.DuplicateWindow + TimeSpan.FromSeconds(1);
        notifications.Tick();
        notifications.Enqueue(Item("Teams", "Ann", "hello"));

        Assert.NotNull(notifications.Current);
    }

    [Fact]
    public void Dismiss_with_nothing_showing_does_nothing()
    {
        NotificationsViewModel notifications = New();
        notifications.Dismiss();
        Assert.Null(notifications.Current);
    }

    [Fact]
    public void The_queue_is_capped()
    {
        NotificationsViewModel notifications = New();
        for (int i = 0; i < 100; i++)
        {
            notifications.Enqueue(Item("A", $"n{i}", ""));
        }

        Assert.Equal("+20", notifications.MoreBadge);
    }

    private NotificationsViewModel New() => new(() => _now);

    private static NotificationItem Item(string app, string title, string body) => new(app, title, body);
}

/// <summary>Which view wins when several things want the island.</summary>
public class OverlayPriorityTests
{
    private DateTime _now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ClockSnapshot RunningStopwatch =
        new(ClockPage.Stopwatch, TimeSpan.FromSeconds(30), true, [], []);

    [Fact]
    public void Idle_by_default() => Assert.Equal(OverlayView.Idle, Build().Overlay.View);

    [Fact]
    public void The_clock_line_shows_for_a_running_stopwatch_and_clears_when_clock_closes()
    {
        Rig rig = Build();
        rig.Clock.Apply(RunningStopwatch);
        Assert.Equal(OverlayView.ClockCompact, rig.Overlay.View);

        rig.Clock.Apply(null);
        Assert.Equal(OverlayView.Idle, rig.Overlay.View);
    }

    [Fact]
    public void A_running_agent_outranks_the_clock_line()
    {
        Rig rig = Build();
        rig.Clock.Apply(RunningStopwatch);
        rig.Agents.Apply(new HookMessage { Id = "1", Agent = "claude", Event = "PostToolUse", SessionId = "s", Cwd = "C:/r", Tool = "Bash" });
        Assert.Equal(OverlayView.AgentCompact, rig.Overlay.View);
    }

    [Fact]
    public void The_clock_line_outranks_the_music_line()
    {
        Rig rig = Build();
        rig.Media.ApplySession("Spotify.exe");
        rig.Media.Apply(new Islet.App.Media.MediaPlayback(true, true, true, true));
        Assert.Equal(OverlayView.MediaCompact, rig.Overlay.View);

        rig.Clock.Apply(RunningStopwatch);
        Assert.Equal(OverlayView.ClockCompact, rig.Overlay.View);
    }

    [Fact]
    public void A_finished_agent_opens_the_done_card_which_outranks_a_notification()
    {
        Rig rig = Build();
        rig.Agents.Apply(new HookMessage { Id = "1", Agent = "claude", Event = "PostToolUse", SessionId = "s", Cwd = "C:/r", Tool = "Bash" });
        Assert.Equal(OverlayView.AgentCompact, rig.Overlay.View);

        rig.Notifications.Enqueue(new NotificationItem("A", "t", "b"));
        Assert.Equal(OverlayView.Notification, rig.Overlay.View);

        rig.Agents.Apply(new HookMessage { Id = "2", Agent = "claude", Event = "Stop", SessionId = "s", Cwd = "C:/r" });
        Assert.Equal(OverlayView.AgentDone, rig.Overlay.View);

        rig.Agents.DismissDone();
        Assert.Equal(OverlayView.Notification, rig.Overlay.View);
    }

    [Fact]
    public void The_done_card_goes_away_by_itself()
    {
        Rig rig = Build();
        rig.Agents.Apply(new HookMessage { Id = "1", Agent = "claude", Event = "Stop", SessionId = "s", Cwd = "C:/r" });
        Assert.Equal(OverlayView.AgentDone, rig.Overlay.View);

        _now += AgentSessionsViewModel.DoneVisibleFor;
        rig.Agents.Tick();

        Assert.Equal(OverlayView.Idle, rig.Overlay.View);
    }

    [Fact]
    public void A_permission_card_outranks_everything()
    {
        Rig rig = Build();
        rig.Clock.Apply(RunningStopwatch);
        rig.Agents.Apply(new HookMessage { Id = "1", Agent = "claude", Event = "Stop", SessionId = "s", Cwd = "C:/r" });
        rig.Notifications.Enqueue(new NotificationItem("A", "t", "b"));
        rig.Permissions.Enqueue(new HookMessage { Id = "1", Agent = "claude", Event = "PermissionRequest", Tool = "Bash" });

        Assert.Equal(OverlayView.Permission, rig.Overlay.View);
    }

    [Fact]
    public void Hovering_opens_the_music_panel_only_when_there_is_music()
    {
        Rig rig = Build();
        rig.Overlay.IsHovered = true;
        Assert.Equal(OverlayView.Idle, rig.Overlay.View);

        rig.Media.ApplySession("Spotify.exe");
        Assert.Equal(OverlayView.MediaExpanded, rig.Overlay.View);

        rig.Overlay.IsHovered = false;
        Assert.Equal(OverlayView.Idle, rig.Overlay.View);
    }

    private Rig Build()
    {
        var media = new MediaViewModel(null);
        var permissions = new PermissionQueueViewModel(() => _now);
        var agents = new AgentSessionsViewModel(() => _now);
        var clock = new ClockViewModel(() => _now.ToLocalTime());
        var notifications = new NotificationsViewModel(() => _now);
        return new Rig(new OverlayViewModel(media, permissions, agents, clock, notifications), media, permissions, agents, clock, notifications);
    }

    private sealed record Rig(
        OverlayViewModel Overlay,
        MediaViewModel Media,
        PermissionQueueViewModel Permissions,
        AgentSessionsViewModel Agents,
        ClockViewModel Clock,
        NotificationsViewModel Notifications);
}
