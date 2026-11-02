using System.Windows.Media;

namespace Islet.App.Ui;

/// <summary>Steps a set of <see cref="Spring"/>s once per rendered frame, only while one of them is moving.</summary>
public sealed class SpringAnimator
{
    private const double FirstFrame = 1.0 / 60;

    /// <summary>A stalled frame (a GC, a busy UI thread) advances at most this far, so nothing jumps.</summary>
    private const double MaxFrame = 1.0 / 30;

    private readonly List<Spring> _springs = new();
    private TimeSpan? _lastFrame;
    private bool _running;

    /// <summary>Raised once every spring has come to rest.</summary>
    public event Action? Settled;

    public Spring Add(double value, Action<double> apply)
    {
        var spring = new Spring(this, value, apply);
        _springs.Add(spring);
        return spring;
    }

    internal void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _lastFrame = null;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Rendering can fire more than once for the same frame.
        TimeSpan now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame)
        {
            return;
        }

        double seconds = _lastFrame is { } last ? Math.Min((now - last).TotalSeconds, MaxFrame) : FirstFrame;
        _lastFrame = now;

        bool moving = false;
        foreach (Spring spring in _springs)
        {
            spring.Step(seconds);
            moving |= !spring.IsSettled;
        }

        if (!moving)
        {
            CompositionTarget.Rendering -= OnRendering;
            _running = false;
            Settled?.Invoke();
        }
    }
}
