using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Islet.App.Ui;

/// <summary>
/// An inverted isosceles trapezoid: the full width runs along the top edge and the
/// bottom edge is narrower by <see cref="BottomInset"/> on each side. The top edge is
/// meant to sit flush with the screen edge, so it gets no stroke. The child is clipped
/// to the shape.
/// </summary>
public sealed class TrapezoidBorder : Decorator
{
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(TrapezoidBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush), typeof(Brush), typeof(TrapezoidBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness), typeof(double), typeof(TrapezoidBorder),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BottomInsetProperty = DependencyProperty.Register(
        nameof(BottomInset), typeof(double), typeof(TrapezoidBorder),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BottomCornerRadiusProperty = DependencyProperty.Register(
        nameof(BottomCornerRadius), typeof(double), typeof(TrapezoidBorder),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public Brush? BorderBrush
    {
        get => (Brush?)GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    public double BorderThickness
    {
        get => (double)GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>How far each bottom corner sits in from the top corners, in DIPs.</summary>
    public double BottomInset
    {
        get => (double)GetValue(BottomInsetProperty);
        set => SetValue(BottomInsetProperty, value);
    }

    /// <summary>Softens the two bottom corners. The top corners stay sharp.</summary>
    public double BottomCornerRadius
    {
        get => (double)GetValue(BottomCornerRadiusProperty);
        set => SetValue(BottomCornerRadiusProperty, value);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Size size = base.ArrangeOverride(arrangeSize);
        if (Child is not null)
        {
            Child.Clip = BuildGeometry(arrangeSize, closed: true);
        }

        return size;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        Size size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        drawingContext.DrawGeometry(Background, null, BuildGeometry(size, closed: true));

        if (BorderBrush is not null && BorderThickness > 0)
        {
            // Sides and bottom only: the top edge is the screen edge.
            // Not frozen: freezing the pen would freeze the brush, which animates when a request is overdue.
            var pen = new Pen(BorderBrush, BorderThickness) { LineJoin = PenLineJoin.Round };
            drawingContext.DrawGeometry(null, pen, BuildGeometry(size, closed: false));
        }
    }

    private StreamGeometry BuildGeometry(Size size, bool closed)
    {
        double width = size.Width;
        double height = size.Height;
        double inset = Math.Clamp(BottomInset, 0, width / 2);

        var topLeft = new Point(0, 0);
        var bottomLeft = new Point(inset, height);
        var bottomRight = new Point(width - inset, height);
        var topRight = new Point(width, 0);

        // Keep the radius short enough to fit on both edges that meet at each corner.
        double sideLength = Math.Sqrt((inset * inset) + (height * height));
        double radius = Math.Min(BottomCornerRadius, Math.Min(sideLength, bottomRight.X - bottomLeft.X) / 2);

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(topLeft, isFilled: closed, isClosed: closed);
            RoundedCorner(context, topLeft, bottomLeft, bottomRight, radius);
            RoundedCorner(context, bottomLeft, bottomRight, topRight, radius);
            context.LineTo(topRight, isStroked: true, isSmoothJoin: true);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>Draws up to <paramref name="corner"/>, rounding it with a quadratic curve.</summary>
    private static void RoundedCorner(StreamGeometryContext context, Point from, Point corner, Point to, double radius)
    {
        if (radius <= 0)
        {
            context.LineTo(corner, isStroked: true, isSmoothJoin: true);
            return;
        }

        context.LineTo(PointToward(corner, from, radius), isStroked: true, isSmoothJoin: true);
        context.QuadraticBezierTo(corner, PointToward(corner, to, radius), isStroked: true, isSmoothJoin: true);
    }

    private static Point PointToward(Point origin, Point target, double distance)
    {
        Vector direction = target - origin;
        direction.Normalize();
        return origin + (direction * distance);
    }
}
