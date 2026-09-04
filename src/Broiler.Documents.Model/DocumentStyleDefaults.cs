namespace Broiler.Documents.Model;

/// <summary>
/// What a document's formatting falls back to where a run states nothing.
/// </summary>
/// <remarks>
/// <para>
/// Every consumer needs an answer for a run with no size and no family, and
/// before this each invented its own: the CLI renderer said 11 points of
/// sans-serif, the PDF writer said 12 points, and a document rendered one way and
/// written the other changed size on the way through. The document is the right
/// place for that answer, because it is the same answer whatever reads it
/// (PDF roadmap §6.4).
/// </para>
/// <para>
/// <strong>The size always resolves; the family may not.</strong> Twelve points
/// is a real default rather than a placeholder — a document that states no size
/// anywhere still has one, and every reader and writer agrees which. A family is
/// different: there is no neutral typeface, and inventing one is how a document
/// silently changes appearance between machines. So the family may be absent, and
/// what an absent family means depends on who is asking.
/// </para>
/// <para>
/// <strong>Display may guess; deterministic output may not.</strong> A UI drawing
/// text on screen falls back to its own documented display face, because showing
/// something is better than showing nothing and the user can see the result. A
/// paginator or a writer may not: its output is a file that has to be the same
/// everywhere, and adopting whatever face the converting machine happens to have
/// installed would make it depend on that machine. Those paths require an
/// explicitly provisioned font and report when they have none — never the UI's
/// choice and never the OS's.
/// </para>
/// </remarks>
public sealed record DocumentStyleDefaults
{
    /// <summary>The point size a run inherits when it states none.</summary>
    public const float FallbackFontSizePoints = 12f;

    /// <summary>Twelve points, and no family stated.</summary>
    public static DocumentStyleDefaults Default { get; } = new();

    /// <summary>
    /// The document's default point size. Never null and never zero: a document
    /// always has a size, even when no part of it says so.
    /// </summary>
    public float FontSizePoints { get; init; } = FallbackFontSizePoints;

    /// <summary>
    /// The document's logical family, or <see langword="null"/> when it states
    /// none. Logical rather than resolved — <c>sans-serif</c> or a family name
    /// the document used, not a file on this machine.
    /// </summary>
    public string? FontFamily { get; init; }

    /// <summary>True when this states nothing a consumer would not have assumed.</summary>
    public bool IsDefault =>
        FontSizePoints == FallbackFontSizePoints && FontFamily is null;

    /// <summary>
    /// The point size to draw or write a run at: its own, or the document's.
    /// </summary>
    /// <remarks>
    /// A non-positive or non-finite stated size is treated as absent rather than
    /// honoured. Zero-point text is not a thing an author asks for, and a NaN
    /// would propagate into every measurement downstream of it.
    /// </remarks>
    public float FontSizeOf(InlineStyle style) =>
        style.FontSize is float size && float.IsFinite(size) && size > 0
            ? size
            : FontSizePoints;

    /// <summary>
    /// The family to draw or write a run in, or <see langword="null"/> when
    /// neither the run nor the document states one.
    /// </summary>
    /// <remarks>
    /// Null is a real answer and the caller has to handle it: a UI substitutes
    /// its documented display face, and a paginator or writer reports that it has
    /// no font rather than choosing one. Returning a hard-coded family here would
    /// take that decision away from both of them and make the wrong one for the
    /// second.
    /// </remarks>
    public string? FontFamilyOf(InlineStyle style) =>
        string.IsNullOrEmpty(style.FontFamily) ? FontFamily : style.FontFamily;
}
