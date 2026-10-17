using Islet.App.Timers;

namespace Islet.Tests;

public class StopwatchTests
{
    private DateTime _now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Starts_at_zero_and_has_no_time()
    {
        StopwatchViewModel stopwatch = New();
        Assert.False(stopwatch.IsRunning);
        Assert.False(stopwatch.HasTime);
        Assert.Equal("00:00.0", stopwatch.Display);
    }

    [Fact]
    public void Counts_while_running_and_holds_while_paused()
    {
        StopwatchViewModel stopwatch = New();
        stopwatch.Start();
        Advance(TimeSpan.FromSeconds(65.4));
        Assert.Equal("01:05.4", stopwatch.Display);
        Assert.True(stopwatch.HasTime);

        stopwatch.Pause();
        Advance(TimeSpan.FromMinutes(10));
        Assert.Equal("01:05.4", stopwatch.Display);
        Assert.False(stopwatch.IsRunning);
        Assert.True(stopwatch.HasTime);
    }

    [Fact]
    public void Resumes_from_where_it_paused()
    {
        StopwatchViewModel stopwatch = New();
        stopwatch.Start();
        Advance(TimeSpan.FromSeconds(10));
        stopwatch.Pause();
        Advance(TimeSpan.FromSeconds(100));
        stopwatch.Start();
        Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(15), stopwatch.Elapsed);
    }

    [Fact]
    public void Toggle_flips_between_running_and_paused()
    {
        StopwatchViewModel stopwatch = New();
        stopwatch.Toggle();
        Assert.True(stopwatch.IsRunning);
        stopwatch.Toggle();
        Assert.False(stopwatch.IsRunning);
    }

    [Fact]
    public void Reset_clears_time_and_stops()
    {
        StopwatchViewModel stopwatch = New();
        stopwatch.Start();
        Advance(TimeSpan.FromSeconds(30));
        stopwatch.Reset();

        Assert.False(stopwatch.IsRunning);
        Assert.False(stopwatch.HasTime);
        Assert.Equal(TimeSpan.Zero, stopwatch.Elapsed);
    }

    [Fact]
    public void Start_while_running_does_not_restart_it()
    {
        StopwatchViewModel stopwatch = New();
        stopwatch.Start();
        Advance(TimeSpan.FromSeconds(10));
        stopwatch.Start();
        Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(20), stopwatch.Elapsed);
    }

    [Theory]
    [InlineData(0, "00:00.0")]
    [InlineData(59.99, "00:59.9")]
    [InlineData(60, "01:00.0")]
    [InlineData(3599, "59:59.0")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3723, "1:02:03")]
    [InlineData(-5, "00:00.0")]
    public void Formats_the_display(double seconds, string expected) =>
        Assert.Equal(expected, StopwatchViewModel.Format(TimeSpan.FromSeconds(seconds)));

    private StopwatchViewModel New() => new(() => _now);

    private void Advance(TimeSpan by) => _now += by;
}

public class AlarmsTests
{
    private DateTime _now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Alarm_rings_when_due_and_not_before()
    {
        AlarmsViewModel alarms = New();
        alarms.AddIn(TimeSpan.FromMinutes(5));

        Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        alarms.Tick();
        Assert.Null(alarms.Ringing);
        Assert.Equal(1, alarms.PendingCount);

        Advance(TimeSpan.FromSeconds(1));
        alarms.Tick();
        Assert.NotNull(alarms.Ringing);
        Assert.Equal(0, alarms.PendingCount);
        Assert.Null(alarms.Next);
    }

    [Fact]
    public void Next_is_the_soonest_alarm()
    {
        AlarmsViewModel alarms = New();
        alarms.AddIn(TimeSpan.FromMinutes(30));
        alarms.AddIn(TimeSpan.FromMinutes(5));
        alarms.AddIn(TimeSpan.FromMinutes(15));

        Assert.Equal(TimeSpan.FromMinutes(5), alarms.NextRemaining);
        Assert.Equal("5:00", alarms.NextText);
    }

    [Fact]
    public void Dismiss_stops_ringing_and_the_next_due_alarm_takes_over()
    {
        AlarmsViewModel alarms = New();
        alarms.AddIn(TimeSpan.FromMinutes(1));
        alarms.AddIn(TimeSpan.FromMinutes(2));
        Advance(TimeSpan.FromMinutes(3));

        alarms.Tick();
        AlarmItem first = alarms.Ringing!;
        alarms.Dismiss();

        Assert.NotNull(alarms.Ringing);
        Assert.NotEqual(first, alarms.Ringing);

        alarms.Dismiss();
        Assert.Null(alarms.Ringing);
    }

    [Fact]
    public void Snooze_rings_again_in_five_minutes()
    {
        AlarmsViewModel alarms = New();
        alarms.AddIn(TimeSpan.FromMinutes(1));
        Advance(TimeSpan.FromMinutes(1));
        alarms.Tick();

        alarms.Snooze();
        Assert.Null(alarms.Ringing);
        Assert.Equal(AlarmsViewModel.SnoozeFor, alarms.NextRemaining);

        Advance(AlarmsViewModel.SnoozeFor);
        alarms.Tick();
        Assert.NotNull(alarms.Ringing);
    }

    [Fact]
    public void Dismiss_or_clear_dismisses_first_then_clears_the_rest()
    {
        AlarmsViewModel alarms = New();
        alarms.AddIn(TimeSpan.FromMinutes(1));
        alarms.AddIn(TimeSpan.FromHours(1));
        Advance(TimeSpan.FromMinutes(2));
        alarms.Tick();

        alarms.DismissOrClear();
        Assert.Null(alarms.Ringing);
        Assert.Equal(1, alarms.PendingCount);

        alarms.DismissOrClear();
        Assert.Equal(0, alarms.PendingCount);
        Assert.False(alarms.HasPending);
    }

    [Fact]
    public void Dismiss_and_snooze_with_nothing_ringing_do_nothing()
    {
        AlarmsViewModel alarms = New();
        alarms.AddIn(TimeSpan.FromMinutes(10));
        alarms.Dismiss();
        alarms.Snooze();
        Assert.Equal(1, alarms.PendingCount);
    }

    [Theory]
    [InlineData(0.2, "0:01")]
    [InlineData(59, "0:59")]
    [InlineData(252, "4:12")]
    [InlineData(3600, "1:00:00")]
    [InlineData(-3, "0:00")]
    public void Formats_the_countdown_rounding_up(double seconds, string expected) =>
        Assert.Equal(expected, AlarmsViewModel.FormatRemaining(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Alarms_survive_a_restart()
    {
        var store = new MemoryStore();
        AlarmsViewModel first = New(store);
        first.AddIn(TimeSpan.FromMinutes(10));
        first.AddIn(TimeSpan.FromMinutes(20));

        AlarmsViewModel second = New(store);

        Assert.Equal(2, second.PendingCount);
        Assert.Equal(TimeSpan.FromMinutes(10), second.NextRemaining);
    }

    [Fact]
    public void Dismissed_alarms_are_not_restored()
    {
        var store = new MemoryStore();
        AlarmsViewModel first = New(store);
        first.AddIn(TimeSpan.FromMinutes(1));
        Advance(TimeSpan.FromMinutes(1));
        first.Tick();
        first.Dismiss();

        Assert.Empty(store.Saved);
        Assert.Equal(0, New(store).PendingCount);
    }

    [Fact]
    public void Alarm_missed_a_little_while_closed_rings_straight_away_but_a_stale_one_is_dropped()
    {
        var store = new MemoryStore();
        store.Saved = [_now - TimeSpan.FromMinutes(5), _now - TimeSpan.FromHours(3)];

        AlarmsViewModel alarms = New(store);
        Assert.Equal(1, alarms.PendingCount);

        alarms.Tick();
        Assert.NotNull(alarms.Ringing);
    }

    [Fact]
    public void File_store_round_trips_and_survives_a_missing_or_corrupt_file()
    {
        string path = Path.Combine(Path.GetTempPath(), $"islet-alarms-{Guid.NewGuid():N}.json");
        try
        {
            var store = new FileAlarmStore(path);
            Assert.Empty(store.Load());

            DateTime due = new(2026, 10, 17, 13, 0, 0, DateTimeKind.Utc);
            store.Save([due]);
            Assert.Equal([due], store.Load());

            File.WriteAllText(path, "not json");
            Assert.Empty(store.Load());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private AlarmsViewModel New(IAlarmStore? store = null) => new(() => _now, store);

    private void Advance(TimeSpan by) => _now += by;

    private sealed class MemoryStore : IAlarmStore
    {
        public IReadOnlyList<DateTime> Saved { get; set; } = [];

        public IReadOnlyList<DateTime> Load() => Saved;

        public void Save(IReadOnlyList<DateTime> dueUtc) => Saved = dueUtc;
    }
}

public class TimersViewModelTests
{
    private DateTime _now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Idle_timers_do_not_take_the_compact_line()
    {
        TimersViewModel timers = New();
        Assert.False(timers.IsCompactActive);
    }

    [Fact]
    public void A_stopwatch_with_a_time_takes_the_line_until_reset()
    {
        TimersViewModel timers = New();
        timers.Stopwatch.Start();
        Assert.True(timers.IsCompactActive);

        _now += TimeSpan.FromSeconds(5);
        timers.Stopwatch.Pause();
        Assert.True(timers.IsCompactActive);

        timers.Stopwatch.Reset();
        Assert.False(timers.IsCompactActive);
    }

    [Fact]
    public void An_alarm_takes_the_line_only_when_it_is_close()
    {
        TimersViewModel timers = New();
        timers.AddAlarm(60);
        Assert.False(timers.IsCompactActive);

        _now += TimeSpan.FromMinutes(46);
        timers.Tick();
        Assert.True(timers.IsCompactActive);
        Assert.True(timers.HasAlarmSoon);
    }

    [Fact]
    public void Quick_alarm_helper_adds_that_many_minutes()
    {
        TimersViewModel timers = New();
        timers.AddAlarm(15);
        Assert.Equal(TimeSpan.FromMinutes(15), timers.Alarms.NextRemaining);
        Assert.True(timers.IsCompactActive);
    }

    private TimersViewModel New() => new(() => _now);
}
