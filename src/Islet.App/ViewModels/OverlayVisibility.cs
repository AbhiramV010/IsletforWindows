namespace Islet.App.ViewModels;

/// <summary>When the island steps aside for a fullscreen app.</summary>
public static class OverlayVisibility
{
    /// <summary>
    /// Views that are worth breaking through a fullscreen app: a permission request is blocking an
    /// agent, and "Claude is done" is the cue to go back to the terminal. Music, timers,
    /// notifications and the idle tab stay hidden.
    /// </summary>
    public static bool IsUrgent(OverlayView view) => view is OverlayView.Permission or OverlayView.AgentDone;

    public static bool ShouldHide(bool hideInFullscreen, bool fullscreenActive, OverlayView view) =>
        hideInFullscreen && fullscreenActive && !IsUrgent(view);
}
