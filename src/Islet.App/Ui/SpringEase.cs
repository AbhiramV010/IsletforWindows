using System.Windows;
using System.Windows.Media.Animation;

namespace Islet.App.Ui;

/// <summary>
/// A slightly underdamped spring squeezed into the animation's duration: it moves off fast,
/// overshoots and settles, the way the Dynamic Island changes shape.
/// </summary>
public sealed class SpringEase : EasingFunctionBase
{
    /// <summary>Decay over the whole duration: by the end the motion has died down to ~0.1%.</summary>
    private const double Settle = 6.75;

    /// <summary>Below 1 overshoots: 0.75 by about 3%, 0.4 by about 25% (a visible bounce).</summary>
    public static readonly DependencyProperty DampingProperty = DependencyProperty.Register(
        nameof(Damping), typeof(double), typeof(SpringEase), new PropertyMetadata(0.75));

    public SpringEase()
    {
        EasingMode = EasingMode.EaseIn;
    }

    public double Damping
    {
        get => (double)GetValue(DampingProperty);
        set => SetValue(DampingProperty, value);
    }

    /// <remarks>
    /// <see cref="EasingFunctionBase"/> mirrors this curve for EaseOut, so the spring is written
    /// as EaseIn and that is the default.
    /// </remarks>
    protected override double EaseInCore(double normalizedTime)
    {
        if (normalizedTime >= 1)
        {
            return 1;
        }

        // Stiffer for lower damping, so every spring has settled by the end of its duration.
        double damping = Math.Clamp(Damping, 0.1, 0.99);
        double frequency = Settle / damping;
        double dampedFrequency = frequency * Math.Sqrt(1 - (damping * damping));

        double t = normalizedTime;
        double decay = Math.Exp(-damping * frequency * t);
        double oscillation = Math.Cos(dampedFrequency * t) +
            (damping * frequency / dampedFrequency * Math.Sin(dampedFrequency * t));
        return 1 - (decay * oscillation);
    }

    protected override Freezable CreateInstanceCore() => new SpringEase();
}
