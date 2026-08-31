using Broiler.Graphics;

namespace Broiler.Documents.Model;

/// <summary>
/// How a shape's box is painted: one colour, or a linear gradient between two.
/// </summary>
/// <remarks>
/// Two stops rather than a list. Every shape a word processor's own templates
/// produce uses two, and a reader that met more would have to choose which to
/// keep - so this says what it carries rather than pretending to a generality it
/// does not have. A producer's extra stops raise a diagnostic.
/// </remarks>
public sealed record ShapeFill(BColor Start, BColor End, double AngleDegrees)
{
    /// <summary>A single-colour fill.</summary>
    public static ShapeFill Solid(BColor color) => new(color, color, 0);

    /// <summary>True when the two stops differ, so the fill is worth interpolating.</summary>
    public bool IsGradient => Start != End;
}
