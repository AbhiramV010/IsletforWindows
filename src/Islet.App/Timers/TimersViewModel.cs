using System.ComponentModel;
using System.Windows.Threading;
using Islet.App.ViewModels;

namespace Islet.App.Timers;

/// <summary>
/// The stopwatch and the alarms, plus the one UI timer that drives both. The timer runs only
/// while something needs it: every 100 ms for a running stopwatch, once a second for alarms.
/// </summary>
public sealed class TimersViewModel : ObservableObject
{
    /// <summary>A pending alarm takes over the compact line only when it is this close.</summary>
    public static readonly TimeSpan AlarmShownWithin = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan FastInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan SlowInterval = TimeSpan.FromSeconds(1);

    private readonly Func<DateTime> _utcNow;
    private readonly DispatcherTimer _timer;
    private bool _isCompactActive;

    public TimersViewModel(Func<DateTime>? utcNow = null, IAlarmStore? store = null)
    {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        Stopwatch = new StopwatchViewModel(_utcNow);
        Alarms = new AlarmsViewModel(_utcNow, store);

        _timer = new DispatcherTimer();
        _timer.Tick += (_, _) => Tick();

        Stopwatch.PropertyChanged += OnChildChanged;
        Alarms.PropertyChanged += OnChildChanged;
        Evaluate();
    }

    public StopwatchViewModel Stopwatch { get; }

    public AlarmsViewModel Alarms { get; }

    /// <summary>True when the compact line should show a timer: a stopwatch with a time, or an alarm soon.</summary>
    public bool IsCompactActive
    {
        get => _isCompactActive;
        private set => SetField(ref _isCompactActive, value);
    }

    public bool HasAlarmSoon => Alarms.NextRemaining is { } remaining && remaining <= AlarmShownWithin;

    public void AddAlarm(int minutes) => Alarms.AddIn(TimeSpan.FromMinutes(minutes));

    /// <summary>Advances alarms and the stopwatch display. Called by the UI timer, and directly by tests.</summary>
    public void Tick()
    {
        Alarms.Tick();
        if (Stopwatch.IsRunning)
        {
            Stopwatch.Refresh();
        }

        Evaluate();
    }

    private void OnChildChanged(object? sender, PropertyChangedEventArgs e) => Evaluate();

    private void Evaluate()
    {
        OnPropertyChanged(nameof(HasAlarmSoon));
        IsCompactActive = Stopwatch.HasTime || HasAlarmSoon;

        bool needsTimer = Stopwatch.IsRunning || Alarms.PendingCount > 0 || Alarms.Ringing is not null;
        TimeSpan interval = Stopwatch.IsRunning ? FastInterval : SlowInterval;
        if (_timer.Interval != interval)
        {
            _timer.Interval = interval;
        }

        if (needsTimer && !_timer.IsEnabled)
        {
            _timer.Start();
        }
        else if (!needsTimer && _timer.IsEnabled)
        {
            _timer.Stop();
        }
    }
}
