namespace Islet.App.Clock;

/// <summary>Which page of the Windows Clock app is open. Only that page's data can be read.</summary>
public enum ClockPage
{
    Other,
    Timer,
    Alarm,
    Stopwatch,
}

/// <summary>One timer card on Clock's Timer page.</summary>
public sealed record ClockTimerItem(string Name, TimeSpan Remaining, bool Running);

/// <summary>
/// What the open Windows Clock app is showing right now. Clock only builds the page you are on,
/// so at most one of the three sections has data.
/// </summary>
public sealed record ClockSnapshot(
    ClockPage Page,
    TimeSpan? Stopwatch,
    bool StopwatchRunning,
    IReadOnlyList<ClockTimerItem> Timers,
    IReadOnlyList<TimeSpan> EnabledAlarms)
{
    public static ClockSnapshot Empty(ClockPage page) => new(page, null, false, [], []);
}
