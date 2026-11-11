using System;

namespace Broiler.Documents.Model;

/// <summary>When the count of numbered lines starts again.</summary>
public enum LineNumberRestart
{
    /// <summary>On every page: the first numbered line of each page is the start number.</summary>
    NewPage = 0,

    /// <summary>At the start of each section.</summary>
    NewSection = 1,

    /// <summary>Never: the count runs on through the document.</summary>
    Continuous = 2,
}

/// <summary>
/// Numbers set in the margin beside the lines of the body text, as a page or
/// section asks for them: DOCX's <c>w:lnNumType</c> (ECMA-376 §17.6.8).
/// </summary>
/// <remarks>
/// <para>
/// Every line of a body paragraph counts, an empty paragraph's line included;
/// lines in tables, headers, footers, notes and text boxes do not. A number is
/// shown only on a line whose count is a multiple of <see cref="CountBy"/>.
/// </para>
/// <para>
/// The number is set in the document's default face and size, its right edge
/// <see cref="Distance"/> to the left of the text column, on the line's
/// baseline - where LibreOffice draws it.
/// </para>
/// </remarks>
public sealed record LineNumbering
{
    /// <summary>
    /// The distance LibreOffice sets the numbers from the text at when the
    /// document leaves it to the application: 0.5 cm.
    /// </summary>
    public const double DefaultDistance = 72 / 2.54 / 2;

    /// <summary>Creates line numbering.</summary>
    public LineNumbering(int countBy, int start = 1, double? distance = null,
        LineNumberRestart restart = LineNumberRestart.NewPage)
    {
        CountBy = Math.Max(1, countBy);
        Start = Math.Max(1, start);
        Distance = distance is double value && double.IsFinite(value) && value > 0 ? value : DefaultDistance;
        Restart = restart;
    }

    /// <summary>Which lines show their number: every line whose count is a multiple of this.</summary>
    public int CountBy { get; }

    /// <summary>The number of the first line counted.</summary>
    public int Start { get; }

    /// <summary>Points between a number's right edge and the text column.</summary>
    public double Distance { get; }

    /// <summary>When the count starts again at <see cref="Start"/>.</summary>
    public LineNumberRestart Restart { get; }
}
