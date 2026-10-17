using Islet.App.ViewModels;

namespace Islet.App.Timers;

/// <summary>A stopwatch driven by the wall clock, so it stays right across a busy UI thread or sleep.</summary>
public sealed class StopwatchViewModel : ObservableObject
{
    private readonly Func<DateTime> _utcNow;
    private TimeSpan _accumulated;
    private DateTime? _startedAtUtc;

    public StopwatchViewModel(Func<DateTime>? utcNow = null) => _utcNow = utcNow ?? (() => DateTime.UtcNow);

    public bool IsRunning => _startedAtUtc is not null;

    public TimeSpan Elapsed => _accumulated + (_startedAtUtc is DateTime started ? _utcNow() - started : TimeSpan.Zero);

    /// <summary>True while running, or paused with a time on it. A reset stopwatch has none.</summary>
    public bool HasTime => IsRunning || _accumulated > TimeSpan.Zero;

    public string Display => Format(Elapsed);

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _startedAtUtc = _utcNow();
        RaiseAll();
    }

    public void Pause()
    {
        if (_startedAtUtc is not DateTime started)
        {
            return;
        }

        _accumulated += _utcNow() - started;
        _startedAtUtc = null;
        RaiseAll();
    }

    public void Toggle()
    {
        if (IsRunning)
        {
            Pause();
        }
        else
        {
            Start();
        }
    }

    public void Reset()
    {
        _accumulated = TimeSpan.Zero;
        _startedAtUtc = null;
        RaiseAll();
    }

    /// <summary>Re-reads the time. The timer calls this while the stopwatch runs.</summary>
    public void Refresh() => OnPropertyChanged(nameof(Display));

    /// <summary>"01:23.4" under an hour, "1:02:03" after.</summary>
    public static string Format(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds / 100}";
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(HasTime));
        OnPropertyChanged(nameof(Display));
    }
}
