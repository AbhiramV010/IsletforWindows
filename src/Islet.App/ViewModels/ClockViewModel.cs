using Islet.App.Clock;

namespace Islet.App.ViewModels;

/// <summary>
/// What the island shows from the Windows Clock app. Nothing here keeps time itself: every value
/// is a reading of Clock's own window, so the numbers are Windows' own.
/// </summary>
public sealed class ClockViewModel : ObservableObject
{
    /// <summary>A stored alarm takes the compact line only when it is this close.</summary>
    public static readonly TimeSpan AlarmShownWithin = TimeSpan.FromMinutes(15);

    private readonly Func<DateTime> _now;
    private string _stopwatchText = "";
    private bool _stopwatchRunning;
    private string _timerText = "";
    private string? _timerBadge;
    private string _alarmText = "";
    private bool _isActive;

    public ClockViewModel(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.Now);

    /// <summary>"15:23", or empty when the stopwatch is at zero or Clock isn't showing it.</summary>
    public string StopwatchText
    {
        get => _stopwatchText;
        private set => SetField(ref _stopwatchText, value);
    }

    public bool StopwatchRunning
    {
        get => _stopwatchRunning;
        private set => SetField(ref _stopwatchRunning, value);
    }

    /// <summary>The running timer with the least time left, e.g. "4:12". Empty if none is running.</summary>
    public string TimerText
    {
        get => _timerText;
        private set => SetField(ref _timerText, value);
    }

    /// <summary>"+1" when more timers are running behind the one shown.</summary>
    public string? TimerBadge
    {
        get => _timerBadge;
        private set => SetField(ref _timerBadge, value);
    }

    /// <summary>"in 12m" for the next enabled alarm, when it is close.</summary>
    public string AlarmText
    {
        get => _alarmText;
        private set => SetField(ref _alarmText, value);
    }

    public bool HasStopwatch => StopwatchText.Length > 0;

    public bool HasTimer => TimerText.Length > 0;

    public bool HasAlarm => AlarmText.Length > 0;

    /// <summary>True when there is something worth a place on the compact line.</summary>
    public bool IsActive
    {
        get => _isActive;
        private set => SetField(ref _isActive, value);
    }

    /// <summary>Takes a reading of the Clock app, or null when it isn't open.</summary>
    public void Apply(ClockSnapshot? snapshot)
    {
        StopwatchText = "";
        StopwatchRunning = false;
        TimerText = "";
        TimerBadge = null;
        AlarmText = "";

        if (snapshot is not null)
        {
            ApplyStopwatch(snapshot);
            ApplyTimers(snapshot);
            ApplyAlarms(snapshot);
        }

        OnPropertyChanged(nameof(HasStopwatch));
        OnPropertyChanged(nameof(HasTimer));
        OnPropertyChanged(nameof(HasAlarm));
        IsActive = HasStopwatch || HasTimer || HasAlarm;
    }

    private void ApplyStopwatch(ClockSnapshot snapshot)
    {
        if (snapshot.Stopwatch is { } elapsed && (snapshot.StopwatchRunning || elapsed > TimeSpan.Zero))
        {
            StopwatchText = ClockText.FormatStopwatch(elapsed);
            StopwatchRunning = snapshot.StopwatchRunning;
        }
    }

    private void ApplyTimers(ClockSnapshot snapshot)
    {
        List<ClockTimerItem> running = snapshot.Timers.Where(timer => timer.Running).OrderBy(timer => timer.Remaining).ToList();
        if (running.Count == 0)
        {
            return;
        }

        TimerText = ClockText.FormatCountdown(running[0].Remaining);
        TimerBadge = running.Count > 1 ? $"+{running.Count - 1}" : null;
    }

    private void ApplyAlarms(ClockSnapshot snapshot)
    {
        DateTime now = _now();
        TimeSpan? soonest = null;
        foreach (TimeSpan timeOfDay in snapshot.EnabledAlarms)
        {
            TimeSpan until = ClockText.UntilNext(timeOfDay, now);
            if (soonest is null || until < soonest)
            {
                soonest = until;
            }
        }

        if (soonest is { } next && next <= AlarmShownWithin)
        {
            AlarmText = ClockText.FormatUntil(next);
        }
    }
}
