using System.ComponentModel;
using Islet.App.Notifications;

namespace Islet.App.ViewModels;

/// <summary>What the island is showing. Each view has its own size.</summary>
public enum OverlayView
{
    /// <summary>Nothing to show: a thin tab at the top of the screen with a red line.</summary>
    Idle,

    /// <summary>One line: art, title, artist.</summary>
    MediaCompact,

    /// <summary>One line: agent, repo, and what it is doing.</summary>
    AgentCompact,

    /// <summary>An agent just finished: "Claude is done", for a few seconds.</summary>
    AgentDone,

    /// <summary>One line: the Windows Clock app's stopwatch, timer or next alarm.</summary>
    ClockCompact,

    /// <summary>Art, title, artist and transport controls, while hovered.</summary>
    MediaExpanded,

    /// <summary>A Windows toast, for a few seconds.</summary>
    Notification,

    /// <summary>The permission card. Takes priority over everything else.</summary>
    Permission,
}

/// <summary>
/// Picks the view. From the top: a permission card, a finished agent, a notification, a debug
/// override, the music panel on hover, a running agent, the Clock app's timers, music, and
/// finally idle.
/// </summary>
public sealed class OverlayViewModel : ObservableObject
{
    private static readonly OverlayView?[] DebugCycle =
        [null, OverlayView.Idle, OverlayView.MediaCompact, OverlayView.MediaExpanded, OverlayView.ClockCompact];

    private OverlayView _view = OverlayView.Idle;
    private bool _isHovered;
    private OverlayView? _forcedView;

    public OverlayViewModel(
        MediaViewModel media,
        PermissionQueueViewModel permissions,
        AgentSessionsViewModel agents,
        ClockViewModel clock,
        NotificationsViewModel notifications)
    {
        Media = media;
        Permissions = permissions;
        Agents = agents;
        Clock = clock;
        Notifications = notifications;

        Media.PropertyChanged += OnChildChanged;
        Permissions.PropertyChanged += OnChildChanged;
        Agents.PropertyChanged += OnChildChanged;
        Clock.PropertyChanged += OnChildChanged;
        Notifications.PropertyChanged += OnChildChanged;
    }

    public MediaViewModel Media { get; }

    public AgentSessionsViewModel Agents { get; }

    public PermissionQueueViewModel Permissions { get; }

    public ClockViewModel Clock { get; }

    public NotificationsViewModel Notifications { get; }

    public OverlayView View
    {
        get => _view;
        private set => SetField(ref _view, value);
    }

    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (SetField(ref _isHovered, value))
            {
                UpdateView();
            }
        }
    }

    /// <summary>Debug override from Ctrl+Alt+I or --demo. Null means automatic.</summary>
    public OverlayView? ForcedView
    {
        get => _forcedView;
        private set
        {
            if (SetField(ref _forcedView, value))
            {
                UpdateView();
            }
        }
    }

    /// <summary>Debug: automatic → idle → compact → expanded → clock → automatic.</summary>
    public void CycleForcedView()
    {
        int index = Array.IndexOf(DebugCycle, ForcedView);
        ForcedView = DebugCycle[(index + 1) % DebugCycle.Length];
    }

    private void OnChildChanged(object? sender, PropertyChangedEventArgs e) => UpdateView();

    private void UpdateView() => View = ComputeView();

    private OverlayView ComputeView()
    {
        if (Permissions.Current is not null)
        {
            return OverlayView.Permission;
        }

        // A finished agent is worth interrupting for: it is the cue to go back to the terminal.
        if (Agents.Current is { State: AgentState.Done })
        {
            return OverlayView.AgentDone;
        }

        if (Notifications.Current is not null)
        {
            return OverlayView.Notification;
        }

        if (ForcedView is { } forced)
        {
            return forced;
        }

        if (IsHovered && Media.HasSession)
        {
            return OverlayView.MediaExpanded;
        }

        // A running agent outranks the Clock app's timers, which outrank the music line.
        if (Agents.Current is not null)
        {
            return OverlayView.AgentCompact;
        }

        if (Clock.IsActive)
        {
            return OverlayView.ClockCompact;
        }

        return Media.HasSession && Media.IsPlaying ? OverlayView.MediaCompact : OverlayView.Idle;
    }
}
