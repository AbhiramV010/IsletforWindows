using System.Windows.Threading;
using Islet.App.ViewModels;

namespace Islet.App.Notifications;

/// <summary>A Windows toast, reduced to what the island shows.</summary>
public sealed record NotificationItem(string App, string Title, string Body);

/// <summary>
/// Notifications waiting to be shown, first in first out. Each one stays up for
/// <see cref="ShowFor"/>, or until it is dismissed with a click.
/// </summary>
public sealed class NotificationsViewModel : ObservableObject
{
    public static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(6);

    /// <summary>The same toast seen again this soon (a banner re-detected) is not shown twice.</summary>
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(3);

    private const int MaxQueued = 20;

    private readonly Queue<NotificationItem> _waiting = new();
    private readonly DispatcherTimer _timer;
    private readonly Func<DateTime> _utcNow;
    private NotificationItem? _current;
    private DateTime _shownAtUtc;
    private NotificationItem? _lastSeen;
    private DateTime _lastSeenAtUtc;

    public NotificationsViewModel(Func<DateTime>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Tick();
    }

    public NotificationItem? Current
    {
        get => _current;
        private set => SetField(ref _current, value);
    }

    /// <summary>"+2" when more are waiting behind the one on show.</summary>
    public string? MoreBadge => _waiting.Count > 0 ? $"+{_waiting.Count}" : null;

    public void Enqueue(NotificationItem item)
    {
        DateTime now = _utcNow();
        if (item == _lastSeen && now - _lastSeenAtUtc < DuplicateWindow)
        {
            return;
        }

        _lastSeen = item;
        _lastSeenAtUtc = now;

        if (Current is null)
        {
            Show(item, now);
            return;
        }

        if (_waiting.Count < MaxQueued)
        {
            _waiting.Enqueue(item);
            OnPropertyChanged(nameof(MoreBadge));
        }
    }

    /// <summary>Dismisses the one on show (a click). The next one, if any, takes its place.</summary>
    public void Dismiss()
    {
        if (Current is null)
        {
            return;
        }

        ShowNext(_utcNow());
    }

    /// <summary>Expires the notification on show. Called by the timer, and directly by tests.</summary>
    public void Tick()
    {
        if (Current is not null && _utcNow() - _shownAtUtc >= ShowFor)
        {
            ShowNext(_utcNow());
        }
    }

    private void ShowNext(DateTime now)
    {
        if (_waiting.Count > 0)
        {
            Show(_waiting.Dequeue(), now);
            OnPropertyChanged(nameof(MoreBadge));
            return;
        }

        Current = null;
        _timer.Stop();
    }

    private void Show(NotificationItem item, DateTime now)
    {
        _shownAtUtc = now;
        Current = item;
        _timer.Start();
    }
}
