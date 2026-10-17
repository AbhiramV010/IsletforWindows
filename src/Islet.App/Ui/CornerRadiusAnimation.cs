using System.Windows;
using System.Windows.Media.Animation;

namespace Islet.App.Ui;

/// <summary>
/// Animates a <see cref="CornerRadius"/>. WPF ships no built-in animation for that
/// type, so the pill's corner radius would otherwise pop instead of ease.
/// Used like <c>DoubleAnimation</c>, but always supplies From and To.
/// </summary>
public sealed class CornerRadiusAnimation : AnimationTimeline
{
    public static readonly DependencyProperty FromProperty = DependencyProperty.Register(
        nameof(From), typeof(CornerRadius), typeof(CornerRadiusAnimation), new PropertyMetadata(default(CornerRadius)));

    public static readonly DependencyProperty ToProperty = DependencyProperty.Register(
        nameof(To), typeof(CornerRadius), typeof(CornerRadiusAnimation), new PropertyMetadata(default(CornerRadius)));

    public static readonly DependencyProperty EasingFunctionProperty = DependencyProperty.Register(
        nameof(EasingFunction), typeof(IEasingFunction), typeof(CornerRadiusAnimation), new PropertyMetadata(null));

    public CornerRadius From
    {
        get => (CornerRadius)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public CornerRadius To
    {
        get => (CornerRadius)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    public override Type TargetPropertyType => typeof(CornerRadius);

    protected override Freezable CreateInstanceCore() => new CornerRadiusAnimation();

    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        double progress = animationClock.CurrentProgress ?? 1d;
        if (EasingFunction is not null)
        {
            progress = EasingFunction.Ease(progress);
        }

        CornerRadius from = From;
        CornerRadius to = To;
        return new CornerRadius(
            Lerp(from.TopLeft, to.TopLeft, progress),
            Lerp(from.TopRight, to.TopRight, progress),
            Lerp(from.BottomRight, to.BottomRight, progress),
            Lerp(from.BottomLeft, to.BottomLeft, progress));
    }

    private static double Lerp(double from, double to, double progress) => from + ((to - from) * progress);
}
