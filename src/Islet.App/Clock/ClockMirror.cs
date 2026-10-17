namespace Islet.App.Clock;

/// <summary>
/// Watches the Windows Clock app and hands each reading to <paramref name="publish"/> (on a pool
/// thread; marshal to the UI yourself). Windows raises no event for a timer ticking or for the
/// Clock app opening, so this polls: about twice a second while Clock is open, and a cheap
/// process lookup every few seconds while it isn't.
/// </summary>
public sealed class ClockMirror : IDisposable
{
    private static readonly TimeSpan OpenInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ClosedInterval = TimeSpan.FromSeconds(3);

    private readonly ClockReader _reader = new();
    private readonly Action<ClockSnapshot?> _publish;
    private Timer? _timer;
    private bool _disposed;
    private string? _lastState;

    public ClockMirror(Action<ClockSnapshot?> publish) => _publish = publish;

    public void Start() => _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Logs Clock opening, changing page and closing. The page name only, never what it shows.</summary>
    private void LogChange(ClockSnapshot? snapshot)
    {
        string state = snapshot is null ? "not readable" : $"{snapshot.Page} page";
        if (state != _lastState)
        {
            _lastState = state;
            FileLog.Write($"Clock: {state}");
        }
    }

    private void Poll()
    {
        ClockSnapshot? snapshot = null;
        try
        {
            snapshot = ClockReader.IsRunning() ? _reader.Read() : null;
            LogChange(snapshot);
            _publish(snapshot);
        }
        catch (Exception exception)
        {
            FileLog.Write("Clock mirror poll failed", exception);
        }
        finally
        {
            // One shot at a time, so a slow read can never overlap the next one.
            if (!_disposed)
            {
                try
                {
                    _timer?.Change(snapshot is null ? ClosedInterval : OpenInterval, Timeout.InfiniteTimeSpan);
                }
                catch (ObjectDisposedException)
                {
                    // Shut down while polling.
                }
            }
        }
    }
}
