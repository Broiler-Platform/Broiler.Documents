using Broiler.Graphics.Color;

namespace Broiler.Documents.Model;

/// <summary>
/// How a shape's box, or a page, is painted: colour, a two-stop gradient,
/// hatching or an embedded image pattern.
/// </summary>
/// <remarks>
/// <para>
/// Two stops rather than a list. Every shape a word processor's own templates
/// produce uses two, and a reader that met more would have to choose which to
/// keep - so this says what it carries rather than pretending to a generality it
/// does not have. A producer's extra stops raise a diagnostic.
/// </para>
/// <para>
/// An <see cref="Axial"/> gradient runs from <see cref="Start"/> at both edges to
/// <see cref="End"/> across the middle: the mirrored gradient every format names
/// (VML's focus of 50%, ODF's <c>axial</c>, a DrawingML gradient whose first and
/// last stops are the same colour). It is still two colours, so it costs the
/// model nothing the two-stop rule above does not already allow.
/// </para>
/// </remarks>
public sealed record ShapeFill(BColor Start, BColor End, double AngleDegrees, bool Axial = false)
{
    public GradientGeometry Geometry { get; init; }
    public double CenterX { get; init; } = 0.5;
    public double CenterY { get; init; } = 0.5;
    public double Border { get; init; }
    public HatchFill? Hatch { get; init; }
    public ImageFill? Image { get; init; }
    /// <summary>Paint within the page margins instead of across the paper.</summary>
    public bool ContentOnly { get; init; }
    public bool HasExtendedPaint => Geometry != GradientGeometry.Linear || Hatch is not null || Image is not null || Border != 0 || ContentOnly;
    /// <summary>A single-colour fill.</summary>
    public static ShapeFill Solid(BColor color) => new(color, color, 0);

    /// <summary>True when the two stops differ, so the fill is worth interpolating.</summary>
    public bool IsGradient => Start != End;

    /// <summary>
    /// The colour at <paramref name="t"/>, from 0 where the gradient starts to 1
    /// where it ends. Every painter asks this, so a page drawn on screen and the
    /// same page written to PDF band the gradient alike.
    /// </summary>
    public BColor ColorAt(double t)
    {
        t = double.IsFinite(t) ? System.Math.Clamp(t, 0, 1) : 0;
        if (Axial)
            t = 1 - System.Math.Abs((2 * t) - 1);
        double border = double.IsFinite(Border) ? System.Math.Clamp(Border, 0, 1) : 0;
        t = border >= 1 ? 0 : System.Math.Clamp((t - border) / (1 - border), 0, 1);

        return new BColor(
            Channel(Start.R, End.R, t),
            Channel(Start.G, End.G, t),
            Channel(Start.B, End.B, t),
            Channel(Start.A, End.A, t));
    }

    /// <summary>Samples a centred ODF gradient; its start colour is at the border.</summary>
    public BColor ColorAt(double x, double y, double width, double height)
    {
        double cx = System.Math.Clamp(double.IsFinite(CenterX) ? CenterX : 0.5, 0, 1) * width;
        double cy = System.Math.Clamp(double.IsFinite(CenterY) ? CenterY : 0.5, 0, 1) * height;
        double dx = x - cx, dy = y - cy;
        double radiusX = width / 2, radiusY = height / 2;
        if (Geometry != GradientGeometry.Radial)
        {
            double angle = (90 - AngleDegrees) * System.Math.PI / 180;
            (dx, dy) = (dx * System.Math.Cos(angle) - dy * System.Math.Sin(angle), dx * System.Math.Sin(angle) + dy * System.Math.Cos(angle));
        }
        double distance = Geometry switch
        {
            GradientGeometry.Radial => System.Math.Sqrt(dx * dx + dy * dy) / System.Math.Max(0.01, System.Math.Sqrt(radiusX * radiusX + radiusY * radiusY)),
            GradientGeometry.Ellipsoid => System.Math.Sqrt(dx * dx / System.Math.Max(0.01, radiusX * radiusX) + dy * dy / System.Math.Max(0.01, radiusY * radiusY)),
            GradientGeometry.Square => System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) / System.Math.Max(0.01, System.Math.Min(radiusX, radiusY)),
            _ => System.Math.Max(System.Math.Abs(dx) / System.Math.Max(0.01, radiusX), System.Math.Abs(dy) / System.Math.Max(0.01, radiusY)),
        };
        return ColorAt(1 - distance);
    }

    private static byte Channel(byte from, byte to, double t) =>
        (byte)System.Math.Round(from + ((to - from) * t));
}

public enum GradientGeometry { Linear, Radial, Ellipsoid, Square, Rectangular }

/// <summary>Parallel strokes, optionally crossed at right angles and at 45 degrees.</summary>
public sealed record HatchFill(BColor Color, double Distance, double AngleDegrees, int Directions = 1);

public enum ImageFillRepeat { Tile, Stretch, Single }

/// <summary>Image pattern dimensions are points, with zero choosing the intrinsic size.</summary>
public sealed record ImageFill(InlineImage Picture, ImageFillRepeat Repeat = ImageFillRepeat.Tile,
    double Width = 0, double Height = 0, double AlignX = 0.5, double AlignY = 0.5);
