using System.Diagnostics;
using System.Windows.Automation;

namespace Islet.App.Clock;

/// <summary>
/// Reads the Windows Clock app's window through UI Automation. Windows has no API for the Clock
/// app's timers, stopwatch or alarms, so this is the only way to see them. Strictly read-only:
/// it never invokes, selects or toggles anything in the app.
///
/// Clock only builds the page you are on, so only that page can be read. The window is hosted by
/// the app frame process, and the content inside it belongs to <c>Time.exe</c>.
/// </summary>
public sealed class ClockReader
{
    public const string ProcessName = "Time";

    private const string FrameClass = "ApplicationFrameWindow";
    private const int MaxStructureLogs = 3;

    private int _structureLogs;

    /// <summary>True while the Clock app has a process. A closed Clock has nothing to read.</summary>
    public static bool IsRunning()
    {
        Process[] processes = Process.GetProcessesByName(ProcessName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    /// <summary>Null when Clock isn't running, has no window, or the window can't be read right now.</summary>
    public ClockSnapshot? Read()
    {
        try
        {
            AutomationElement? content = FindContent();
            return content is null ? null : ReadPage(content);
        }
        catch (ElementNotAvailableException)
        {
            // The window closed mid-read.
            return null;
        }
        catch (Exception exception)
        {
            FileLog.Write("Clock read failed", exception);
            return null;
        }
    }

    private static AutomationElement? FindContent()
    {
        Process[] processes = Process.GetProcessesByName(ProcessName);
        try
        {
            if (processes.Length == 0)
            {
                return null;
            }

            int pid = processes[0].Id;
            var pidCondition = new PropertyCondition(AutomationElement.ProcessIdProperty, pid);

            foreach (AutomationElement frame in AutomationElement.RootElement.FindAll(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ClassNameProperty, FrameClass)))
            {
                // The frame belongs to the app frame host; the Clock's own content is inside it.
                AutomationElement? content = frame.FindFirst(TreeScope.Descendants, pidCondition);
                if (content is not null)
                {
                    return frame;
                }
            }

            return null;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    private ClockSnapshot ReadPage(AutomationElement window)
    {
        ClockPage page = SelectedPage(window);
        return page switch
        {
            ClockPage.Stopwatch => ReadStopwatch(window),
            ClockPage.Timer => ReadTimers(window),
            ClockPage.Alarm => ReadAlarms(window),
            _ => ClockSnapshot.Empty(page),
        };
    }

    /// <summary>Which navigation entry is selected: Clock marks the open page's entry as selected.</summary>
    private static ClockPage SelectedPage(AutomationElement window)
    {
        (string Id, ClockPage Page)[] entries =
        [
            ("TimerButton", ClockPage.Timer),
            ("AlarmButton", ClockPage.Alarm),
            ("StopwatchButton", ClockPage.Stopwatch),
        ];

        foreach ((string id, ClockPage page) in entries)
        {
            AutomationElement? item = ById(window, id);
            if (item is not null &&
                item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object pattern) &&
                ((SelectionItemPattern)pattern).Current.IsSelected)
            {
                return page;
            }
        }

        return ClockPage.Other;
    }

    private static ClockSnapshot ReadStopwatch(AutomationElement window)
    {
        TimeSpan? elapsed = ClockText.Parse(ById(window, "StopwatchTimerText")?.Current.Name);

        // The play button reads "Pause" while the stopwatch runs, and "Start" or "Resume" when it doesn't.
        string button = ById(window, "StopwatchPlayPauseButton")?.Current.Name ?? "";
        bool running = button.Contains("Pause", StringComparison.OrdinalIgnoreCase);

        return new ClockSnapshot(ClockPage.Stopwatch, elapsed, running, [], []);
    }

    /// <summary>
    /// Timer cards: each time on the page is paired with its card, which is running when it has a
    /// Pause button. The card layout isn't documented, so cards are found by shape, not by id.
    /// </summary>
    private ClockSnapshot ReadTimers(AutomationElement window)
    {
        var timers = new List<ClockTimerItem>();
        foreach (AutomationElement text in TextsOf(window))
        {
            string name = text.Current.Name;
            if (ClockText.Parse(name) is not { } remaining || IsPageChrome(text))
            {
                continue;
            }

            AutomationElement card = CardOf(text);
            bool running = HasButton(card, "Pause");
            timers.Add(new ClockTimerItem(CardTitle(card, name), remaining, running));
        }

        if (timers.Count == 0)
        {
            LogStructureIfUnexpected(window, "timer");
        }

        return new ClockSnapshot(ClockPage.Timer, null, false, timers, []);
    }

    /// <summary>Alarm rows: a time of day, switched on. A row with no readable switch is treated as off.</summary>
    private ClockSnapshot ReadAlarms(AutomationElement window)
    {
        var alarms = new List<TimeSpan>();
        foreach (AutomationElement text in TextsOf(window))
        {
            if (ClockText.ParseAlarmTime(text.Current.Name) is not { } timeOfDay || IsPageChrome(text))
            {
                continue;
            }

            if (IsSwitchedOn(CardOf(text)))
            {
                alarms.Add(timeOfDay);
            }
        }

        return new ClockSnapshot(ClockPage.Alarm, null, false, [], alarms);
    }

    /// <summary>Navigation, title bar and account texts are not content.</summary>
    private static bool IsPageChrome(AutomationElement element)
    {
        string id = element.Current.AutomationId;
        return id is "AppName" or "TipBlock" or "UserNameBlock" or "TitleBar";
    }

    private static IEnumerable<AutomationElement> TextsOf(AutomationElement window)
    {
        AutomationElementCollection texts = window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
        foreach (AutomationElement text in texts)
        {
            yield return text;
        }
    }

    /// <summary>
    /// The smallest ancestor that holds this one time and no other, i.e. the row or card it sits
    /// in. Capped at four levels so a page with one row doesn't swallow the whole window.
    /// </summary>
    private static AutomationElement CardOf(AutomationElement text)
    {
        AutomationElement card = text;
        AutomationElement current = text;
        for (int level = 0; level < 4; level++)
        {
            AutomationElement? parent = TreeWalker.ControlViewWalker.GetParent(current);
            if (parent is null || ReferenceEquals(parent, AutomationElement.RootElement))
            {
                break;
            }

            if (CountTimes(parent) > 1)
            {
                break;
            }

            card = parent;
            current = parent;
        }

        return card;
    }

    private static int CountTimes(AutomationElement scope)
    {
        int count = 0;
        foreach (AutomationElement text in TextsOf(scope))
        {
            string name = text.Current.Name;
            if (ClockText.Parse(name) is not null || ClockText.ParseAlarmTime(name) is not null)
            {
                count++;
            }
        }

        return count;
    }

    private static string CardTitle(AutomationElement card, string timeText)
    {
        foreach (AutomationElement text in TextsOf(card))
        {
            string name = text.Current.Name;
            if (!string.IsNullOrWhiteSpace(name) &&
                name != timeText &&
                ClockText.Parse(name) is null &&
                name.Length > 3 &&
                !IsPageChrome(text))
            {
                return name;
            }
        }

        return "Timer";
    }

    private static bool HasButton(AutomationElement scope, string nameContains)
    {
        foreach (AutomationElement button in scope.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
        {
            if (button.Current.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSwitchedOn(AutomationElement row)
    {
        foreach (AutomationElement element in row.FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            if (element.TryGetCurrentPattern(TogglePattern.Pattern, out object pattern))
            {
                return ((TogglePattern)pattern).Current.ToggleState == ToggleState.On;
            }
        }

        return false;
    }

    private static AutomationElement? ById(AutomationElement scope, string id) =>
        scope.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id));

    /// <summary>
    /// A timer page with nothing readable is normal when there are no timers. Otherwise log what
    /// the page is made of (types and ids, never text) so the reader can be fixed.
    /// </summary>
    private void LogStructureIfUnexpected(AutomationElement window, string page)
    {
        if (ById(window, "TimerListEmptyMessage") is not null || Interlocked.Increment(ref _structureLogs) > MaxStructureLogs)
        {
            return;
        }

        var ids = new List<string>();
        foreach (AutomationElement element in window.FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            string id = element.Current.AutomationId;
            if (id.Length > 0 && ids.Count < 25)
            {
                ids.Add($"{element.Current.ControlType.ProgrammaticName.Replace("ControlType.", "")}:{id}");
            }
        }

        FileLog.Write($"Clock {page} page: no times read. ids=[{string.Join(", ", ids)}]");
    }
}
