namespace Islet.App.Ui;

/// <summary>
/// One value driven by a damped spring, stepped each frame by <see cref="SpringAnimator"/>.
/// Retargeting mid-flight keeps the current velocity, so an interrupted change bends into
/// the next one instead of restarting from rest. Tuned like SwiftUI's springs: response is
/// roughly the period in seconds, and damping below 1 overshoots.
/// </summary>
public sealed class Spring
{
    /// <summary>Integration step; small enough to stay stable for any response we use.</summary>
    private const double MaxStep = 1.0 / 240;
    private const double RestDistance = 0.02;
    private const double RestVelocity = 0.2;

    private readonly SpringAnimator _animator;
    private readonly Action<double> _apply;
    private double _response = 0.5;
    private double _damping = 1;

    internal Spring(SpringAnimator animator, double value, Action<double> apply)
    {
        _animator = animator;
        _apply = apply;
        Value = value;
        Target = value;
    }

    public double Value { get; private set; }

    public double Velocity { get; private set; }

    public double Target { get; private set; }

    internal bool IsSettled => Value == Target && Velocity == 0;

    public void AnimateTo(double target, double response, double damping)
    {
        Target = target;
        _response = response;
        _damping = damping;
        _animator.Start();
    }

    /// <summary>Jumps straight to <paramref name="value"/> and stops there.</summary>
    public void Snap(double value)
    {
        Value = value;
        Target = value;
        Velocity = 0;
        _apply(value);
    }

    internal void Step(double seconds)
    {
        if (IsSettled)
        {
            return;
        }

        double stiffness = Math.Pow(2 * Math.PI / _response, 2);
        double friction = 4 * Math.PI * _damping / _response;
        for (double left = seconds; left > 0; left -= MaxStep)
        {
            double dt = Math.Min(left, MaxStep);
            double force = (-stiffness * (Value - Target)) - (friction * Velocity);
            Velocity += force * dt;
            Value += Velocity * dt;
        }

        if (Math.Abs(Value - Target) < RestDistance && Math.Abs(Velocity) < RestVelocity)
        {
            Value = Target;
            Velocity = 0;
        }

        _apply(Value);
    }
}
