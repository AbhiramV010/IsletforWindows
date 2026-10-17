using Islet.App.Clock;
using Islet.App.ViewModels;

namespace Islet.Tests;

public class ClockTextTests
{
    [Theory]
    [InlineData("15 minutes 23 seconds 80 centiseconds", 923.8)]
    [InlineData("1 hour 2 minutes 3 seconds", 3723)]
    [InlineData("1 minute 1 second", 61)]
    [InlineData("45 seconds", 45)]
    [InlineData("0 minutes 0 seconds 0 centiseconds", 0)]
    [InlineData("2 hours", 7200)]
    public void Spoken_times_parse(string text, double seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), ClockText.ParseSpoken(text));

    [Theory]
    [InlineData("04:12", 252)]
    [InlineData("4:12", 252)]
    [InlineData("1:02:03", 3723)]
    [InlineData("00:04:12", 252)]
    [InlineData("00:04:12.5", 252.5)]
    [InlineData("12:34", 754)]
    public void Clock_faces_parse(string text, double seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), ClockText.ParseClockFace(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Timer 1")]
    [InlineData("hr")]
    [InlineData("99:99")]
    [InlineData("You don’t have any timers.")]
    public void Anything_else_does_not_parse_as_a_duration(string? text) => Assert.Null(ClockText.Parse(text));

    [Fact]
    public void Parse_tries_spoken_then_clock_face()
    {
        Assert.Equal(TimeSpan.FromSeconds(75), ClockText.Parse("1 minute 15 seconds"));
        Assert.Equal(TimeSpan.FromSeconds(75), ClockText.Parse("01:15"));
    }

    [Theory]
    [InlineData("07:30", 7, 30)]
    [InlineData("19:45", 19, 45)]
    [InlineData("7:30 AM", 7, 30)]
    [InlineData("7:30 pm", 19, 30)]
    [InlineData("12:00 AM", 0, 0)]
    [InlineData("12:15 PM", 12, 15)]
    [InlineData("11:59 PM", 23, 59)]
    public void Alarm_times_parse(string text, int hours, int minutes) =>
        Assert.Equal(new TimeSpan(hours, minutes, 0), ClockText.ParseAlarmTime(text));

    [Theory]
    [InlineData("25:00")]
    [InlineData("07:75")]
    [InlineData("13:00 PM")]
    [InlineData("0:30 AM")]
    [InlineData("Wake up")]
    [InlineData("")]
    [InlineData(null)]
    public void Non_times_are_not_alarm_times(string? text) => Assert.Null(ClockText.ParseAlarmTime(text));

    [Theory]
    [InlineData(7, 30, 7, 0, 30)]
    [InlineData(7, 0, 7, 30, 23 * 60 + 30)]
    [InlineData(0, 10, 23, 50, 20)]
    [InlineData(12, 0, 12, 0, 0)]
    public void Until_next_wraps_past_midnight(int alarmH, int alarmM, int nowH, int nowM, int expectedMinutes)
    {
        DateTime now = new(2026, 10, 17, nowH, nowM, 0);
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), ClockText.UntilNext(new TimeSpan(alarmH, alarmM, 0), now));
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(923.8, "15:23")]
    [InlineData(3599.9, "59:59")]
    [InlineData(3723, "1:02:03")]
    [InlineData(-4, "00:00")]
    public void Stopwatch_shows_whole_seconds(double seconds, string expected) =>
        Assert.Equal(expected, ClockText.FormatStopwatch(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(0.2, "0:01")]
    [InlineData(59, "0:59")]
    [InlineData(252, "4:12")]
    [InlineData(3600, "1:00:00")]
    [InlineData(-3, "0:00")]
    public void Countdown_rounds_up(double seconds, string expected) =>
        Assert.Equal(expected, ClockText.FormatCountdown(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(20, "in 20s")]
    [InlineData(0, "in 1s")]
    [InlineData(60, "in 1m")]
    [InlineData(61, "in 2m")]
    [InlineData(12 * 60, "in 12m")]
    [InlineData(65 * 60, "in 1h 5m")]
    public void Until_reads_naturally(double seconds, string expected) =>
        Assert.Equal(expected, ClockText.FormatUntil(TimeSpan.FromSeconds(seconds)));
}

public class ClockViewModelTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0);

    [Fact]
    public void Nothing_when_clock_is_closed()
    {
        var clock = new ClockViewModel(() => Now);
        clock.Apply(null);

        Assert.False(clock.IsActive);
        Assert.False(clock.HasStopwatch);
    }

    [Fact]
    public void A_running_stopwatch_shows_its_time()
    {
        var clock = new ClockViewModel(() => Now);
        clock.Apply(new ClockSnapshot(ClockPage.Stopwatch, TimeSpan.FromSeconds(923.8), true, [], []));

        Assert.True(clock.IsActive);
        Assert.Equal("15:23", clock.StopwatchText);
        Assert.True(clock.StopwatchRunning);
    }

    [Fact]
    public void A_paused_stopwatch_with_a_time_still_shows_it_but_a_zero_one_does_not()
    {
        var clock = new ClockViewModel(() => Now);
        clock.Apply(new ClockSnapshot(ClockPage.Stopwatch, TimeSpan.FromSeconds(30), false, [], []));
        Assert.Equal("00:30", clock.StopwatchText);
        Assert.False(clock.StopwatchRunning);

        clock.Apply(new ClockSnapshot(ClockPage.Stopwatch, TimeSpan.Zero, false, [], []));
        Assert.False(clock.HasStopwatch);
        Assert.False(clock.IsActive);
    }

    [Fact]
    public void The_running_timer_with_the_least_time_left_wins_with_a_badge()
    {
        var clock = new ClockViewModel(() => Now);
        clock.Apply(new ClockSnapshot(ClockPage.Timer, null, false,
        [
            new ClockTimerItem("Tea", TimeSpan.FromMinutes(5), true),
            new ClockTimerItem("Pasta", TimeSpan.FromSeconds(252), true),
            new ClockTimerItem("Paused", TimeSpan.FromSeconds(10), false),
        ], []));

        Assert.Equal("4:12", clock.TimerText);
        Assert.Equal("+1", clock.TimerBadge);
        Assert.True(clock.IsActive);
    }

    [Fact]
    public void Timers_that_are_not_running_do_not_take_the_line()
    {
        var clock = new ClockViewModel(() => Now);
        clock.Apply(new ClockSnapshot(ClockPage.Timer, null, false, [new ClockTimerItem("Tea", TimeSpan.FromMinutes(5), false)], []));

        Assert.False(clock.HasTimer);
        Assert.False(clock.IsActive);
    }

    [Fact]
    public void An_alarm_shows_only_when_it_is_close()
    {
        var clock = new ClockViewModel(() => Now);

        clock.Apply(new ClockSnapshot(ClockPage.Alarm, null, false, [], [new TimeSpan(12, 12, 0)]));
        Assert.Equal("in 12m", clock.AlarmText);
        Assert.True(clock.IsActive);

        clock.Apply(new ClockSnapshot(ClockPage.Alarm, null, false, [], [new TimeSpan(14, 0, 0)]));
        Assert.False(clock.HasAlarm);
        Assert.False(clock.IsActive);
    }

    [Fact]
    public void The_soonest_alarm_wins_including_one_after_midnight()
    {
        var clock = new ClockViewModel(() => new DateTime(2026, 10, 17, 23, 55, 0));
        clock.Apply(new ClockSnapshot(ClockPage.Alarm, null, false, [], [new TimeSpan(7, 0, 0), new TimeSpan(0, 5, 0)]));

        Assert.Equal("in 10m", clock.AlarmText);
    }

    [Fact]
    public void Readings_clear_when_clock_closes()
    {
        var clock = new ClockViewModel(() => Now);
        clock.Apply(new ClockSnapshot(ClockPage.Stopwatch, TimeSpan.FromSeconds(30), true, [], []));
        Assert.True(clock.IsActive);

        clock.Apply(null);
        Assert.False(clock.IsActive);
        Assert.Equal("", clock.StopwatchText);
    }

    [Fact]
    public void Another_page_shows_nothing()
    {
        var clock = new ClockViewModel(() => Now);
        clock.Apply(ClockSnapshot.Empty(ClockPage.Other));
        Assert.False(clock.IsActive);
    }
}
