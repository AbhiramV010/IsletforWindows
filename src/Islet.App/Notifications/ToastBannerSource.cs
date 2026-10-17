using System.Diagnostics;
using System.Windows.Automation;

namespace Islet.App.Notifications;

/// <summary>
/// Reads Windows toast banners as they pop up on screen, through UI Automation. The proper
/// notification-listener API needs an MSIX package identity, which this app doesn't have.
///
/// Limits: it only sees banners that actually appear. Toasts held back by Focus Assist or a
/// fullscreen app, and toasts that already vanished, are missed. It reads text only, and
/// can't click through to the sender.
///
/// Banners belong to the shell, so only shell windows are inspected. What is logged is the
/// window structure (class, automation ids), never the notification text.
/// </summary>
public sealed class ToastBannerSource : IDisposable
{
    private static readonly string[] ShellProcesses = ["ShellExperienceHost", "ShellHost"];

    private const int ContentRetries = 6;
    private const int ContentRetryDelayMs = 100;
    private const int MaxStructureLogs = 6;

    private readonly Action<NotificationItem> _onToast;
    private AutomationEventHandler? _handler;
    private int _structureLogs;
    private bool _disposed;

    public ToastBannerSource(Action<NotificationItem> onToast) => _onToast = onToast;

    /// <summary>UI Automation event handlers must be added from an MTA thread, so this hops to the pool.</summary>
    public void Start() => Task.Run(() =>
    {
        try
        {
            _handler = OnWindowOpened;
            Automation.AddAutomationEventHandler(
                WindowPattern.WindowOpenedEvent, AutomationElement.RootElement, TreeScope.Children, _handler);
            FileLog.Write("Toast capture on (UI Automation window events)");
        }
        catch (Exception exception)
        {
            FileLog.Write("Toast capture unavailable", exception);
        }
    });

    public void Dispose()
    {
        _disposed = true;
        if (_handler is null)
        {
            return;
        }

        try
        {
            Automation.RemoveAutomationEventHandler(WindowPattern.WindowOpenedEvent, AutomationElement.RootElement, _handler);
        }
        catch (Exception exception)
        {
            FileLog.Write("Toast capture shutdown failed", exception);
        }

        _handler = null;
    }

    private void OnWindowOpened(object sender, AutomationEventArgs e)
    {
        try
        {
            if (_disposed || sender is not AutomationElement window || !IsShellWindow(window))
            {
                return;
            }

            NotificationItem? item = ReadToast(window);
            if (item is not null)
            {
                FileLog.Write($"Toast captured from '{item.App}'");
                _onToast(item);
            }
        }
        catch (ElementNotAvailableException)
        {
            // The banner closed while it was being read.
        }
        catch (Exception exception)
        {
            FileLog.Write("Toast read failed", exception);
        }
    }

    private static bool IsShellWindow(AutomationElement window)
    {
        try
        {
            using Process process = Process.GetProcessById(window.Current.ProcessId);
            return ShellProcesses.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private NotificationItem? ReadToast(AutomationElement window)
    {
        // The banner's text fills in a moment after the window appears.
        for (int attempt = 0; attempt < ContentRetries; attempt++)
        {
            AutomationElementCollection all = window.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            AutomationElement? toast = FindToastRoot(window, all);
            if (toast is not null)
            {
                List<string> texts = TextsUnder(toast);
                if (texts.Count > 0)
                {
                    return ToastText.Parse(texts);
                }
            }

            Thread.Sleep(ContentRetryDelayMs);
        }

        LogStructure(window);
        return null;
    }

    /// <summary>The element that holds the banner: an automation id naming a toast, or a "notification" window.</summary>
    private static AutomationElement? FindToastRoot(AutomationElement window, AutomationElementCollection descendants)
    {
        foreach (AutomationElement element in descendants)
        {
            if (element.Current.AutomationId.Contains("Toast", StringComparison.OrdinalIgnoreCase))
            {
                return element;
            }
        }

        string name = window.Current.Name;
        bool looksLikeBanner =
            name.Contains("notification", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("center", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("centre", StringComparison.OrdinalIgnoreCase);
        return looksLikeBanner ? window : null;
    }

    private static List<string> TextsUnder(AutomationElement root)
    {
        var texts = new List<string>();
        foreach (AutomationElement element in root.FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            if (element.Current.ControlType == ControlType.Text)
            {
                string name = element.Current.Name;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    texts.Add(name);
                }
            }
        }

        return texts;
    }

    private void LogStructure(AutomationElement window)
    {
        if (Interlocked.Increment(ref _structureLogs) > MaxStructureLogs)
        {
            return;
        }

        try
        {
            var ids = new List<string>();
            foreach (AutomationElement element in window.FindAll(TreeScope.Descendants, Condition.TrueCondition))
            {
                string id = element.Current.AutomationId;
                if (id.Length > 0 && ids.Count < 12)
                {
                    ids.Add($"{element.Current.ControlType.ProgrammaticName.Replace("ControlType.", "")}:{id}");
                }
            }

            FileLog.Write($"Shell window with no banner text: class='{window.Current.ClassName}' ids=[{string.Join(", ", ids)}]");
        }
        catch (Exception)
        {
            // Diagnostics only.
        }
    }
}
