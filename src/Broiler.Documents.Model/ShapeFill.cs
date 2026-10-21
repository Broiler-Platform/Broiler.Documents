using Broiler.Graphics.Color;

namespace Broiler.Documents.Model;

/// <summary>
/// How a shape's box, or a page, is painted: one colour, or a linear gradient
/// between two.
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

        return new BColor(
            Channel(Start.R, End.R, t),
            Channel(Start.G, End.G, t),
            Channel(Start.B, End.B, t),
            Channel(Start.A, End.A, t));
    }

    private static byte Channel(byte from, byte to, double t) =>
        (byte)System.Math.Round(from + ((to - from) * t));
}
