using System.Windows.Threading;
using Islet.Hook.Protocol;

namespace Islet.App.ViewModels;

/// <summary>
/// Pending permission requests, first in first out. The card shows the head of the queue.
///
/// Allow and Deny exist only as <see cref="Allow"/> and <see cref="Deny"/>, which the window
/// calls from a click or from Enter/Esc while the pointer is on the card. Nothing else
/// produces an allow.
/// </summary>
public sealed class PermissionQueueViewModel : ObservableObject
{
    public static readonly TimeSpan OverdueAfter = TimeSpan.FromSeconds(30);

    /// <summary>Ignore input this soon after a card appears, so a click aimed at something else can't land on Allow.</summary>
    public static readonly TimeSpan InputGuard = TimeSpan.FromMilliseconds(600);

    private readonly List<PermissionRequestViewModel> _pending = [];
    private readonly DispatcherTimer _overdueTimer;
    private readonly Func<DateTime> _utcNow;

    public PermissionQueueViewModel(Func<DateTime>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _overdueTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _overdueTimer.Tick += (_, _) => UpdateOverdue();
    }

    public PermissionRequestViewModel? Current => _pending.Count > 0 ? _pending[0] : null;

    public int Count => _pending.Count;

    /// <summary>Badge text for requests waiting behind the current one, e.g. "+2".</summary>
    public string? MoreBadge => _pending.Count > 1 ? $"+{_pending.Count - 1}" : null;

    public PermissionRequestViewModel Enqueue(HookMessage message)
    {
        var request = new PermissionRequestViewModel(message, _utcNow());
        _pending.Add(request);
        if (_pending.Count == 1)
        {
            request.ShownAtUtc = _utcNow();
            _overdueTimer.Start();
        }

        RaiseQueueChanged();
        return request;
    }

    /// <summary>True when the current card has been up long enough to take input.</summary>
    public bool AcceptsInput =>
        Current?.ShownAtUtc is DateTime shown && _utcNow() - shown >= InputGuard;

    public bool Allow() => ResolveCurrent(Decisions.Allow, message: null);

    public bool Deny() => ResolveCurrent(Decisions.Deny, message: null);

    /// <summary>The shim hung up (agent timed out or closed). Drop the card without answering.</summary>
    public void Cancel(PermissionRequestViewModel request)
    {
        request.Resolve(new HookReply { Decision = Decisions.Passthrough });
        Remove(request);
    }

    /// <summary>App is closing: let every waiting agent fall back to its own prompt.</summary>
    public void CancelAll()
    {
        foreach (PermissionRequestViewModel request in _pending.ToArray())
        {
            Cancel(request);
        }
    }

    private bool ResolveCurrent(string decision, string? message)
    {
        if (Current is not { } request || !AcceptsInput)
        {
            return false;
        }

        request.Resolve(new HookReply { Decision = decision, Message = message });
        Remove(request);
        return true;
    }

    private void Remove(PermissionRequestViewModel request)
    {
        bool wasCurrent = ReferenceEquals(Current, request);
        if (!_pending.Remove(request))
        {
            return;
        }

        if (wasCurrent && Current is { } next)
        {
            next.ShownAtUtc = _utcNow();
        }

        if (_pending.Count == 0)
        {
            _overdueTimer.Stop();
        }

        RaiseQueueChanged();
    }

    private void UpdateOverdue()
    {
        DateTime now = _utcNow();
        foreach (PermissionRequestViewModel request in _pending)
        {
            request.IsOverdue = now - request.ArrivedAtUtc >= OverdueAfter;
        }
    }

    private void RaiseQueueChanged()
    {
        UpdateOverdue();
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(MoreBadge));
    }
}
