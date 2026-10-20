using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Islet.App.Ui;

/// <summary>
/// A Dynamic Island style capsule hanging from the top edge: straight sides, softly
/// rounded bottom corners (fully round when the radius reaches half the height), and
/// small concave fillets where the top corners meet the screen edge. The top edge is
/// meant to sit flush with the screen edge, so it gets no stroke. As
/// <see cref="Detachment"/> goes to 1 the fillets shrink away and the top corners round
/// over, leaving a free-floating capsule. The child is clipped to the shape.
/// </summary>
public sealed class IslandBorder : Decorator
{
    /// <summary>Circle approximation for a cubic Bézier quarter turn.</summary>
    private const double Kappa = 0.5523;

    /// <summary>How far a smoothed corner starts before the plain arc would, as in Apple's continuous corners.</summary>
    private const double Smoothing = 1.28;

    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(IslandBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush), typeof(Brush), typeof(IslandBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness), typeof(double), typeof(IslandBorder),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(IslandBorder),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShoulderRadiusProperty = DependencyProperty.Register(
        nameof(ShoulderRadius), typeof(double), typeof(IslandBorder),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DetachmentProperty = DependencyProperty.Register(
        nameof(Detachment), typeof(double), typeof(IslandBorder),
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

    /// <summary>Radius of the two bottom corners, in DIPs. Capped at half the height (a full capsule end).</summary>
    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>Radius of the concave fillets that flare the top corners into the screen edge. They draw outside the bounds.</summary>
    public double ShoulderRadius
    {
        get => (double)GetValue(ShoulderRadiusProperty);
        set => SetValue(ShoulderRadiusProperty, value);
    }

    /// <summary>0 hangs from the screen edge; 1 floats free with all four corners rounded.</summary>
    public double Detachment
    {
        get => (double)GetValue(DetachmentProperty);
        set => SetValue(DetachmentProperty, value);
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
            // Sides and bottom only while the top edge is the screen edge; all the way round once floating.
            // Not frozen: freezing the pen would freeze the brush, which animates when a request is overdue.
            var pen = new Pen(BorderBrush, BorderThickness) { LineJoin = PenLineJoin.Round };
            drawingContext.DrawGeometry(null, pen, BuildGeometry(size, closed: false));
        }
    }

    private StreamGeometry BuildGeometry(Size size, bool closed)
    {
        double width = size.Width;
        double height = size.Height;
        double radius = Math.Clamp(CornerRadius, 0, Math.Min(width, height) / 2);

        // First half of detaching retracts the fillets, second half rounds the top corners,
        // so the two never overlap.
        double detachment = Math.Clamp(Detachment, 0, 1);
        double shoulder = Math.Clamp(ShoulderRadius * Math.Max(0, 1 - (2 * detachment)), 0, height - radius);
        double topRadius = radius * Math.Max(0, (2 * detachment) - 1);

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            if (topRadius > 0)
            {
                context.BeginFigure(new Point(width / 2, 0), isFilled: closed, isClosed: true);
                Corner(context, new Point(0, 0), new Vector(1, 0), new Vector(0, 1), topRadius, width, height);
                Corner(context, new Point(0, height), new Vector(0, -1), new Vector(1, 0), radius, width, height);
                Corner(context, new Point(width, height), new Vector(-1, 0), new Vector(0, -1), radius, width, height);
                Corner(context, new Point(width, 0), new Vector(0, 1), new Vector(-1, 0), topRadius, width, height);
                context.LineTo(new Point(width / 2, 0), isStroked: true, isSmoothJoin: true);
                geometry.Freeze();
                return geometry;
            }

            // Left shoulder: from out on the screen edge, curving in and down the side.
            context.BeginFigure(new Point(-shoulder, 0), isFilled: closed, isClosed: closed);
            context.BezierTo(
                new Point(-shoulder * (1 - Kappa), 0),
                new Point(0, shoulder * Kappa),
                new Point(0, shoulder),
                isStroked: true,
                isSmoothJoin: true);

            Corner(context, new Point(0, height), new Vector(0, -1), new Vector(1, 0), radius, width, height);
            Corner(context, new Point(width, height), new Vector(-1, 0), new Vector(0, -1), radius, width, height);

            context.LineTo(new Point(width, shoulder), isStroked: true, isSmoothJoin: true);
            context.BezierTo(
                new Point(width, shoulder * Kappa),
                new Point(width + (shoulder * (1 - Kappa)), 0),
                new Point(width + shoulder, 0),
                isStroked: true,
                isSmoothJoin: true);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Draws up to and around <paramref name="corner"/>. <paramref name="towardIn"/> points back
    /// along the incoming edge, <paramref name="towardOut"/> along the outgoing one. Where there is
    /// room, the curve starts early and eases in, so it reads as one continuous sweep, not an arc
    /// glued to a line.
    /// </summary>
    private static void Corner(
        StreamGeometryContext context, Point corner, Vector towardIn, Vector towardOut, double radius, double width, double height)
    {
        if (radius <= 0)
        {
            context.LineTo(corner, isStroked: true, isSmoothJoin: true);
            return;
        }

        double reach = Math.Min(radius * Smoothing, Math.Min(width, height) / 2);
        double smoothing = Math.Clamp(((reach / radius) - 1) / (Smoothing - 1), 0, 1);
        double handle = reach * (1 - Kappa - (0.2 * smoothing));

        Point start = corner + (towardIn * reach);
        Point end = corner + (towardOut * reach);
        context.LineTo(start, isStroked: true, isSmoothJoin: true);
        context.BezierTo(corner + (towardIn * handle), corner + (towardOut * handle), end, isStroked: true, isSmoothJoin: true);
    }
}
