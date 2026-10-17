using Islet.App.Media;
using Islet.App.Notifications;
using Islet.App.Timers;
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

public class ToastTextTests
{
    [Fact]
    public void App_title_and_body()
    {
        NotificationItem item = ToastText.Parse(["Microsoft Teams", "Ann Lee", "Are you free at 3?"])!;
        Assert.Equal(new NotificationItem("Microsoft Teams", "Ann Lee", "Are you free at 3?"), item);
    }

    [Fact]
    public void Extra_lines_join_into_the_body()
    {
        NotificationItem item = ToastText.Parse(["Mail", "Subject", "line one", "line two"])!;
        Assert.Equal("line one line two", item.Body);
    }

    [Fact]
    public void Two_texts_mean_title_and_body_with_no_app()
    {
        NotificationItem item = ToastText.Parse(["Title", "Body"])!;
        Assert.Equal(new NotificationItem("Notification", "Title", "Body"), item);
    }

    [Fact]
    public void One_text_is_just_a_title() =>
        Assert.Equal(new NotificationItem("Notification", "Only", ""), ToastText.Parse(["Only"]));

    [Fact]
    public void Blank_text_and_back_to_back_repeats_are_dropped()
    {
        NotificationItem item = ToastText.Parse(["  ", "Mail", "Mail", "Subject", "", "Body"])!;
        Assert.Equal(new NotificationItem("Mail", "Subject", "Body"), item);
    }

    [Fact]
    public void Nothing_to_show_is_null()
    {
        Assert.Null(ToastText.Parse([]));
        Assert.Null(ToastText.Parse(["", "   "]));
    }

    [Fact]
    public void Very_long_text_is_clipped()
    {
        NotificationItem item = ToastText.Parse(["App", "Title", new string('x', 2000)])!;
        Assert.Equal(300, item.Body.Length);
        Assert.EndsWith("…", item.Body);
    }
}

/// <summary>Which view wins when several things want the island.</summary>
public class OverlayPriorityTests
{
    private DateTime _now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Idle_by_default() => Assert.Equal(OverlayView.Idle, Build().Overlay.View);

    [Fact]
    public void Timer_line_shows_for_a_running_stopwatch()
    {
        Rig rig = Build();
        rig.Timers.Stopwatch.Start();
        Assert.Equal(OverlayView.TimersCompact, rig.Overlay.View);
    }

    [Fact]
    public void A_running_agent_outranks_a_timer()
    {
        Rig rig = Build();
        rig.Timers.Stopwatch.Start();
        rig.Agents.Apply(new HookMessage { Id = "1", Agent = "claude", Event = "PostToolUse", SessionId = "s", Cwd = "C:/r", Tool = "Bash" });
        Assert.Equal(OverlayView.AgentCompact, rig.Overlay.View);
    }

    [Fact]
    public void A_notification_outranks_agents_and_timers_but_not_an_alarm()
    {
        Rig rig = Build();
        rig.Timers.Stopwatch.Start();
        rig.Notifications.Enqueue(new NotificationItem("A", "t", "b"));
        Assert.Equal(OverlayView.Notification, rig.Overlay.View);

        rig.Timers.AddAlarm(1);
        _now += TimeSpan.FromMinutes(1);
        rig.Timers.Tick();
        Assert.Equal(OverlayView.Alert, rig.Overlay.View);

        rig.Timers.Alarms.Dismiss();
        Assert.Equal(OverlayView.Notification, rig.Overlay.View);
    }

    [Fact]
    public void A_finished_agent_opens_the_done_card_which_outranks_a_notification_but_not_an_alarm()
    {
        Rig rig = Build();
        rig.Agents.Apply(new HookMessage { Id = "1", Agent = "claude", Event = "PostToolUse", SessionId = "s", Cwd = "C:/r", Tool = "Bash" });
        Assert.Equal(OverlayView.AgentCompact, rig.Overlay.View);

        rig.Notifications.Enqueue(new NotificationItem("A", "t", "b"));
        rig.Agents.Apply(new HookMessage { Id = "2", Agent = "claude", Event = "Stop", SessionId = "s", Cwd = "C:/r" });
        Assert.Equal(OverlayView.AgentDone, rig.Overlay.View);

        rig.Timers.AddAlarm(1);
        _now += TimeSpan.FromMinutes(1);
        rig.Timers.Tick();
        Assert.Equal(OverlayView.Alert, rig.Overlay.View);

        rig.Timers.Alarms.Dismiss();
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
        rig.Timers.AddAlarm(1);
        _now += TimeSpan.FromMinutes(1);
        rig.Timers.Tick();
        rig.Permissions.Enqueue(new HookMessage { Id = "1", Agent = "claude", Event = "PermissionRequest", Tool = "Bash" });

        Assert.Equal(OverlayView.Permission, rig.Overlay.View);
    }

    [Fact]
    public void Hovering_with_no_music_opens_the_timers_panel_and_unhovering_closes_it()
    {
        Rig rig = Build();
        rig.Overlay.IsHovered = true;
        Assert.Equal(OverlayView.TimersExpanded, rig.Overlay.View);

        rig.Overlay.IsHovered = false;
        Assert.Equal(OverlayView.Idle, rig.Overlay.View);
    }

    [Fact]
    public void Hovering_with_music_opens_media_and_the_timers_button_switches_only_for_that_hover()
    {
        Rig rig = Build();
        rig.Media.ApplySession("Spotify.exe");
        rig.Overlay.IsHovered = true;
        Assert.Equal(OverlayView.MediaExpanded, rig.Overlay.View);

        rig.Overlay.ShowTimersPanel();
        Assert.Equal(OverlayView.TimersExpanded, rig.Overlay.View);

        rig.Overlay.ShowMediaPanel();
        Assert.Equal(OverlayView.MediaExpanded, rig.Overlay.View);

        rig.Overlay.ShowTimersPanel();
        rig.Overlay.IsHovered = false;
        rig.Overlay.IsHovered = true;
        Assert.Equal(OverlayView.MediaExpanded, rig.Overlay.View);
    }

    private Rig Build()
    {
        var media = new MediaViewModel(null);
        var permissions = new PermissionQueueViewModel(() => _now);
        var agents = new AgentSessionsViewModel(() => _now);
        var timers = new TimersViewModel(() => _now);
        var notifications = new NotificationsViewModel(() => _now);
        return new Rig(new OverlayViewModel(media, permissions, agents, timers, notifications), media, permissions, agents, timers, notifications);
    }

    private sealed record Rig(
        OverlayViewModel Overlay,
        MediaViewModel Media,
        PermissionQueueViewModel Permissions,
        AgentSessionsViewModel Agents,
        TimersViewModel Timers,
        NotificationsViewModel Notifications);
}
