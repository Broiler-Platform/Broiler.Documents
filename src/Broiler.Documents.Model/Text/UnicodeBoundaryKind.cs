namespace Broiler.Documents.Model.Text;

/// <summary>
/// Classification of a boundary between characters in source-text coordinates per Unicode UAX #29 and UAX #14.
/// </summary>
public enum UnicodeBoundaryKind
{
    /// <summary>No boundary permitted (prohibited line break, e.g. within an indivisible grapheme cluster or non-breaking space).</summary>
    None = 0,

    /// <summary>
    /// Boundary between extended grapheme clusters per UAX #29. Indivisible by emergency text fitting.
    /// </summary>
    GraphemeBoundary = 1,

    /// <summary>
    /// Legal line break opportunity per UAX #14 (e.g. after a breakable space, zero-width space, soft hyphen, or CJK ideograph).
    /// </summary>
    WordBreakOpportunity = 2,

    /// <summary>
    /// Mandatory line break per UAX #14 (e.g. U+2028 LINE SEPARATOR, CRLF, LF, CR, U+2029).
    /// </summary>
    MandatoryBreak = 3,
}
