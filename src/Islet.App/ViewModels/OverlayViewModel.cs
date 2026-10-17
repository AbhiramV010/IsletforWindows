using System.ComponentModel;

namespace Islet.App.ViewModels;

/// <summary>What the island is showing. Each view has its own size.</summary>
public enum OverlayView
{
    /// <summary>Nothing to show: a thin tab at the top of the screen.</summary>
    Idle,

    /// <summary>One line: art, title, artist.</summary>
    MediaCompact,

    /// <summary>One line: agent, repo, and what it is doing.</summary>
    AgentCompact,

    /// <summary>Art, title, artist and transport controls, while hovered.</summary>
    MediaExpanded,

    /// <summary>The permission card. Takes priority over everything else.</summary>
    Permission,
}

/// <summary>Picks the view from media state, pending permission requests and hover.</summary>
public sealed class OverlayViewModel : ObservableObject
{
    private static readonly OverlayView?[] DebugCycle =
        [null, OverlayView.Idle, OverlayView.MediaCompact, OverlayView.MediaExpanded];

    private OverlayView _view = OverlayView.Idle;
    private bool _isHovered;
    private OverlayView? _forcedView;

    public OverlayViewModel(MediaViewModel media, PermissionQueueViewModel permissions, AgentSessionsViewModel agents)
    {
        Media = media;
        Permissions = permissions;
        Agents = agents;
        Media.PropertyChanged += OnChildChanged;
        Permissions.PropertyChanged += OnChildChanged;
        Agents.PropertyChanged += OnChildChanged;
    }

    public MediaViewModel Media { get; }

    public AgentSessionsViewModel Agents { get; }

    public PermissionQueueViewModel Permissions { get; }

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

    /// <summary>Debug: automatic → idle → compact → expanded → automatic.</summary>
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

        if (ForcedView is { } forced)
        {
            return forced;
        }

        if (IsHovered && Media.HasSession)
        {
            return OverlayView.MediaExpanded;
        }

        // A running agent outranks the music line; hovering still opens the media controls above.
        if (Agents.Current is not null)
        {
            return OverlayView.AgentCompact;
        }

        return Media.HasSession && Media.IsPlaying ? OverlayView.MediaCompact : OverlayView.Idle;
    }
}
