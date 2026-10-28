using System.Collections.Generic;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Model.Pagination;

/// <summary>
/// Engine-neutral contract for a fully paginated document layout artifact.
/// Consumers (e.g. preview, printing, PDF export) consume this resolved geometry
/// without remeasuring fonts or rebreaking lines.
/// </summary>
public interface IPaginatedDocumentArtifact
{
    /// <summary>The resolved pages of the document in reading order.</summary>
    IReadOnlyList<IPaginatedPageArtifact> Pages { get; }
}

/// <summary>
/// Represents a single fixed page within a paginated document artifact.
/// </summary>
public interface IPaginatedPageArtifact
{
    /// <summary>One-based page number.</summary>
    int PageNumber { get; }

    /// <summary>Page width in typographical points.</summary>
    double WidthPoints { get; }

    /// <summary>Page height in typographical points.</summary>
    double HeightPoints { get; }

    /// <summary>Laid-out lines on this page, positioned in points from page top-left.</summary>
    IReadOnlyList<IPaginatedLineArtifact> Lines { get; }

    /// <summary>Shapes, borders, and backgrounds placed on this page.</summary>
    IReadOnlyList<IPaginatedShapeArtifact> Shapes { get; }

    /// <summary>Page background fill, or null if unpainted.</summary>
    ShapeFill? Background { get; }
}

/// <summary>
/// Represents a single laid-out line on a page.
/// </summary>
public interface IPaginatedLineArtifact
{
    /// <summary>Top boundary of the line in points from page top.</summary>
    double Top { get; }

    /// <summary>Height of the line in points.</summary>
    double Height { get; }

    /// <summary>Text baseline coordinate in points from page top.</summary>
    double Baseline { get; }

    /// <summary>Index of the source paragraph this line belongs to.</summary>
    int ParagraphIndex { get; }

    /// <summary>The positioned drawable pieces that compose this line.</summary>
    IReadOnlyList<IPaginatedPieceArtifact> Pieces { get; }
}

/// <summary>
/// Represents a single styled piece of text or inline image within a line.
/// </summary>
public interface IPaginatedPieceArtifact
{
    /// <summary>The text content of this piece.</summary>
    string Text { get; }

    /// <summary>Horizontal start coordinate in points from page left edge.</summary>
    double X { get; }

    /// <summary>Width advance in typographical points.</summary>
    double Width { get; }

    /// <summary>Ascent above baseline in typographical points.</summary>
    double Ascent { get; }

    /// <summary>Descent below baseline in typographical points.</summary>
    double Descent { get; }

    /// <summary>Rise above baseline (for superscript/citations) in points.</summary>
    double Rise { get; }

    /// <summary>Font family name.</summary>
    string FontFamily { get; }

    /// <summary>Font size in typographical points.</summary>
    double FontSizePoints { get; }

    /// <summary>True if bold weight.</summary>
    bool Bold { get; }

    /// <summary>True if italic slant.</summary>
    bool Italic { get; }

    /// <summary>Text foreground color.</summary>
    BColor Color { get; }

    /// <summary>Background highlight color, or empty if none.</summary>
    BColor Highlight { get; }

    /// <summary>True if underlined.</summary>
    bool Underline { get; }

    /// <summary>True if strikethrough.</summary>
    bool Strikethrough { get; }

    /// <summary>Target URI string if this piece represents a hyperlink, or null.</summary>
    string? Link { get; }

    /// <summary>True if this piece represents an inline image rather than text.</summary>
    bool IsImage { get; }

    /// <summary>True if this piece represents a tab stop gap.</summary>
    bool IsTab { get; }
}

/// <summary>
/// Represents a filled or stroked geometric shape, background, or cell on a page.
/// </summary>
public interface IPaginatedShapeArtifact
{
    /// <summary>Left coordinate in points from page left edge.</summary>
    double X { get; }

    /// <summary>Top coordinate in points from page top edge.</summary>
    double Y { get; }

    /// <summary>Width in points.</summary>
    double Width { get; }

    /// <summary>Height in points.</summary>
    double Height { get; }

    /// <summary>Shape fill, or null if transparent.</summary>
    ShapeFill? Fill { get; }

    /// <summary>Outline stroke color.</summary>
    BColor Outline { get; }

    /// <summary>Outline thickness in points.</summary>
    double OutlineWidth { get; }
}
