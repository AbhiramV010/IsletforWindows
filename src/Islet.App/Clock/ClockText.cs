using System.Globalization;
using System.Text.RegularExpressions;

namespace Islet.App.Clock;

/// <summary>Parsing and formatting for the times the Windows Clock app puts on screen.</summary>
public static partial class ClockText
{
    /// <summary>"15 minutes 23 seconds 80 centiseconds" and the like, which is how Clock's accessible text reads.</summary>
    [GeneratedRegex(@"(\d+)\s*(hour|minute|second|centisecond|millisecond)s?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpokenPart();

    /// <summary>"04:12", "1:02:03", "00:04:12.5".</summary>
    [GeneratedRegex(@"^\s*(?:(\d+):)?(\d{1,2}):(\d{2})(?:[.,](\d{1,3}))?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ClockFace();

    /// <summary>"07:30", "7:30 AM", "7:30 pm".</summary>
    [GeneratedRegex(@"^\s*(\d{1,2}):(\d{2})\s*([AaPp])?\.?[Mm]?\.?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex AlarmFace();

    public static TimeSpan? Parse(string? text) => ParseSpoken(text) ?? ParseClockFace(text);

    public static TimeSpan? ParseSpoken(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        MatchCollection matches = SpokenPart().Matches(text);
        if (matches.Count == 0)
        {
            return null;
        }

        var total = TimeSpan.Zero;
        foreach (Match match in matches)
        {
            double amount = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            total += match.Groups[2].Value.ToLowerInvariant() switch
            {
                "hour" => TimeSpan.FromHours(amount),
                "minute" => TimeSpan.FromMinutes(amount),
                "second" => TimeSpan.FromSeconds(amount),
                "centisecond" => TimeSpan.FromMilliseconds(amount * 10),
                _ => TimeSpan.FromMilliseconds(amount),
            };
        }

        return total;
    }

    /// <summary>Two parts are minutes and seconds ("04:12"), three are hours first ("1:02:03").</summary>
    public static TimeSpan? ParseClockFace(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Match match = ClockFace().Match(text);
        if (!match.Success)
        {
            return null;
        }

        int hours = match.Groups[1].Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        int minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        int seconds = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        if (minutes > 59 && hours == 0 && !match.Groups[1].Success)
        {
            return null;
        }

        int millis = match.Groups[4].Success
            ? int.Parse(match.Groups[4].Value.PadRight(3, '0'), CultureInfo.InvariantCulture)
            : 0;
        return new TimeSpan(0, hours, minutes, seconds, millis);
    }

    /// <summary>The time of day an alarm is set for, from "07:30", "19:45" or "7:30 PM". Null if it isn't a time of day.</summary>
    public static TimeSpan? ParseAlarmTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Match match = AlarmFace().Match(text);
        if (!match.Success)
        {
            return null;
        }

        int hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        int minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        if (minutes > 59)
        {
            return null;
        }

        if (match.Groups[3].Success)
        {
            // 12-hour clock: 12 AM is midnight, 12 PM is noon.
            if (hours is < 1 or > 12)
            {
                return null;
            }

            bool pm = char.ToUpperInvariant(match.Groups[3].Value[0]) == 'P';
            hours = (hours % 12) + (pm ? 12 : 0);
        }
        else if (hours > 23)
        {
            return null;
        }

        return new TimeSpan(hours, minutes, 0);
    }

    /// <summary>How long until an alarm set for <paramref name="timeOfDay"/> next goes off, today or tomorrow.</summary>
    public static TimeSpan UntilNext(TimeSpan timeOfDay, DateTime now)
    {
        TimeSpan until = timeOfDay - now.TimeOfDay;
        return until < TimeSpan.Zero ? until + TimeSpan.FromDays(1) : until;
    }

    /// <summary>Whole seconds: "15:23" under an hour, "1:02:03" after. The mirror samples about twice a second.</summary>
    public static string FormatStopwatch(TimeSpan elapsed)
    {
        elapsed = Clamp(elapsed);
        return elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    /// <summary>"4:12", or "1:02:03" past an hour. Rounds up so the last second reads 0:01.</summary>
    public static string FormatCountdown(TimeSpan remaining)
    {
        remaining = TimeSpan.FromSeconds(Math.Ceiling(Clamp(remaining).TotalSeconds));
        return remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"{remaining.Minutes}:{remaining.Seconds:00}";
    }

    /// <summary>"in 12m", "in 1h 5m", "in 45s".</summary>
    public static string FormatUntil(TimeSpan until)
    {
        until = Clamp(until);
        if (until.TotalMinutes < 1)
        {
            return $"in {Math.Max(1, (int)until.TotalSeconds)}s";
        }

        int minutes = (int)Math.Ceiling(until.TotalMinutes);
        return minutes >= 60 ? $"in {minutes / 60}h {minutes % 60}m" : $"in {minutes}m";
    }

    private static TimeSpan Clamp(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}
