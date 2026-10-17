using System.ComponentModel;
using Islet.App.Notifications;
using Islet.App.Timers;

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

    /// <summary>One line: the stopwatch and the next alarm.</summary>
    TimersCompact,

    /// <summary>Art, title, artist and transport controls, while hovered.</summary>
    MediaExpanded,

    /// <summary>Stopwatch and alarm controls, while hovered.</summary>
    TimersExpanded,

    /// <summary>A Windows toast, for a few seconds.</summary>
    Notification,

    /// <summary>An alarm that is going off. Dismiss or snooze.</summary>
    Alert,

    /// <summary>The permission card. Takes priority over everything else.</summary>
    Permission,
}

/// <summary>
/// Picks the view. From the top: a permission card, a ringing alarm, a finished agent, a
/// notification, a debug override, the hover panel, a running agent, a running timer, music, and finally idle.
/// </summary>
public sealed class OverlayViewModel : ObservableObject
{
    private static readonly OverlayView?[] DebugCycle =
        [null, OverlayView.Idle, OverlayView.MediaCompact, OverlayView.MediaExpanded, OverlayView.TimersExpanded];

    private OverlayView _view = OverlayView.Idle;
    private bool _isHovered;
    private bool _showTimersPanel;
    private OverlayView? _forcedView;

    public OverlayViewModel(
        MediaViewModel media,
        PermissionQueueViewModel permissions,
        AgentSessionsViewModel agents,
        TimersViewModel timers,
        NotificationsViewModel notifications)
    {
        Media = media;
        Permissions = permissions;
        Agents = agents;
        Timers = timers;
        Notifications = notifications;

        Media.PropertyChanged += OnChildChanged;
        Permissions.PropertyChanged += OnChildChanged;
        Agents.PropertyChanged += OnChildChanged;
        Timers.PropertyChanged += OnChildChanged;
        Timers.Alarms.PropertyChanged += OnChildChanged;
        Notifications.PropertyChanged += OnChildChanged;
    }

    public MediaViewModel Media { get; }

    public AgentSessionsViewModel Agents { get; }

    public PermissionQueueViewModel Permissions { get; }

    public TimersViewModel Timers { get; }

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
            if (!SetField(ref _isHovered, value))
            {
                return;
            }

            // Each hover opens on media (or timers, with no music); a switch only lasts for that hover.
            if (!value)
            {
                _showTimersPanel = false;
            }

            UpdateView();
        }
    }

    /// <summary>True when the hover panel is the timers one rather than media.</summary>
    public bool IsTimersPanelOpen => View == OverlayView.TimersExpanded;

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

    public void ShowTimersPanel()
    {
        _showTimersPanel = true;
        UpdateView();
    }

    public void ShowMediaPanel()
    {
        _showTimersPanel = false;
        UpdateView();
    }

    /// <summary>Debug: automatic → idle → compact → expanded → timers → automatic.</summary>
    public void CycleForcedView()
    {
        int index = Array.IndexOf(DebugCycle, ForcedView);
        ForcedView = DebugCycle[(index + 1) % DebugCycle.Length];
    }

    private void OnChildChanged(object? sender, PropertyChangedEventArgs e) => UpdateView();

    private void UpdateView()
    {
        View = ComputeView();
        OnPropertyChanged(nameof(IsTimersPanelOpen));
    }

    private OverlayView ComputeView()
    {
        if (Permissions.Current is not null)
        {
            return OverlayView.Permission;
        }

        if (Timers.Alarms.Ringing is not null)
        {
            return OverlayView.Alert;
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

        if (IsHovered)
        {
            return _showTimersPanel || !Media.HasSession ? OverlayView.TimersExpanded : OverlayView.MediaExpanded;
        }

        // A running agent outranks a timer, which outranks the music line.
        if (Agents.Current is not null)
        {
            return OverlayView.AgentCompact;
        }

        if (Timers.IsCompactActive)
        {
            return OverlayView.TimersCompact;
        }

        return Media.HasSession && Media.IsPlaying ? OverlayView.MediaCompact : OverlayView.Idle;
    }
}
