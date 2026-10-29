using System;

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

    /// <summary>
    /// The extra width, in ems, each space in a run of two or more takes where
    /// <see cref="WidensSpaceRuns"/> holds: a six-per-em space.
    /// </summary>
    public const double SpaceRunExtraEm = 1.0 / 6;

    /// <summary>
    /// Whether each space in a run of two or more is laid out a sixth of an em
    /// wider than its font makes it. A lone space keeps its own width.
    /// </summary>
    /// <remarks>
    /// Only RTF says it. LibreOffice reads it from an RTF document that does not
    /// say it came from somewhere else, as Word's layout of RTF, by putting a
    /// U+2006 in front of each such space (tdf#123703). Without it, a line padded
    /// out with spaces fits where LibreOffice pushes the next word onto a line of
    /// its own.
    /// </remarks>
    public bool WidensSpaceRuns { get; init; }

    /// <summary>
    /// How much wider than its column a justified line may be set, for each
    /// space it holds, as a fraction of a space: a line that would break may
    /// take more text where its spaces can shrink to make room.
    /// </summary>
    /// <remarks>
    /// A quarter, LibreOffice's allowance (<c>SwTextGuess::Guess</c> in
    /// <c>guess.cxx</c>, the same in 24.2 and since), counted over the spaces
    /// before the break and the one before the next word.
    /// </remarks>
    public const double JustifiedSpaceShrink = 0.25;

    /// <summary>
    /// Whether justified lines are broken as Word 2013 and later break them:
    /// a line that would break may take more text by shrinking its spaces
    /// (<see cref="JustifiedSpaceShrink"/>), and is then justified with them
    /// narrower than the font makes them.
    /// </summary>
    /// <remarks>
    /// A DOCX says it with <c>compatibilityMode</c> 15 or later, which
    /// LibreOffice reads as its <c>JustifyLinesWithShrinking</c> setting
    /// (tdf#119908, tdf#130088). Without it a justified line breaks before the
    /// word Word fits, and a long justified document runs to more pages.
    /// </remarks>
    public bool ShrinksJustifiedLines { get; init; }

    /// <summary>True when this states nothing a consumer would not have assumed.</summary>
    public bool IsDefault =>
        FontSizePoints == FallbackFontSizePoints && FontFamily is null && !WidensSpaceRuns && !ShrinksJustifiedLines;

    /// <summary>
    /// What <see cref="WidensSpaceRuns"/> adds to the width of
    /// <paramref name="text"/> drawn in <paramref name="family"/> at
    /// <paramref name="fontSizePoints"/>: a sixth of an em for each space that
    /// has another space beside it, and zero where spaces keep their own width.
    /// </summary>
    /// <remarks>
    /// Courier is left alone, as LibreOffice leaves it: a monospaced font's
    /// columns are what its spaces are for. Only U+0020 counts; a non-breaking
    /// space or a tab beside a space does not make a run of it.
    /// </remarks>
    public double SpaceRunExtraWidth(string text, string? family, double fontSizePoints)
    {
        if (!WidensSpaceRuns || string.IsNullOrEmpty(text) ||
            !double.IsFinite(fontSizePoints) || fontSizePoints <= 0 ||
            (family is not null && family.Contains("Courier", StringComparison.OrdinalIgnoreCase)))
            return 0;

        int spaces = 0;
        int run = 0;
        foreach (char character in text)
        {
            if (character == ' ')
            {
                run++;
                continue;
            }

            if (run > 1)
                spaces += run;
            run = 0;
        }

        if (run > 1)
            spaces += run;

        return spaces * fontSizePoints * SpaceRunExtraEm;
    }

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
