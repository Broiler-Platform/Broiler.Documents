namespace Broiler.Documents.Model;

/// <summary>
/// How the body text behaves around a floating shape.
/// </summary>
/// <remarks>
/// Three modes rather than the seven the formats name, because they are the
/// three that differ in what layout has to do. The polygon-precise ones -
/// DOCX's <c>wrapTight</c> and <c>wrapThrough</c>, which follow the picture's
/// own outline - are read as <see cref="Square"/>: the box is followed instead
/// of the outline, which is the same arrangement at a coarser edge. The mode
/// itself round-trips, so a document keeps what it said either way.
/// </remarks>
public enum ShapeWrap
{
    /// <summary>Text ignores the shape and runs under or over it.</summary>
    None,

    /// <summary>Text flows beside the shape, on whichever side has more room.</summary>
    Square,

    /// <summary>Text keeps clear of the shape's whole band and resumes below it.</summary>
    TopAndBottom,
}

/// <summary>Which side of a wrapping shape the text is allowed to run down.</summary>
public enum WrapSide
{
    /// <summary>Whichever side leaves more room, which is what a full-width shape has none of.</summary>
    Largest,

    /// <summary>The text keeps to the left of the shape.</summary>
    Left,

    /// <summary>The text keeps to the right of the shape.</summary>
    Right,
}
