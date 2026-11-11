using System;

namespace Broiler.Documents.Model;

/// <summary>
/// The opening characters of a paragraph set large and sunk into its first
/// lines, which wrap beside them: a drop cap.
/// </summary>
/// <remarks>
/// <para>
/// DOCX states one as a paragraph of its own before the one it opens
/// (<c>w:framePr w:dropCap="drop"</c>, ECMA-376 §17.3.1.11). Word and
/// LibreOffice both hold the characters in the opened paragraph's text, so a
/// text export reads "Word", not "W" and "ord"; the model keeps them there
/// too and says only how many of them are set apart and how.
/// </para>
/// <para>
/// The characters are drawn at their runs' own size, which is what DOCX
/// writes: Word stores the size it chose for the drop. Their baseline is the
/// baseline of the last line they span, and the lines they span start past
/// them.
/// </para>
/// </remarks>
/// <param name="Lines">How many lines the characters span and indent.</param>
/// <param name="Length">How many characters of the paragraph's text the drop cap is.</param>
/// <param name="Distance">The gap between the drop cap and the text beside it, in points.</param>
public readonly record struct DropCap(int Lines, int Length, float Distance)
{
    /// <summary>
    /// True when the characters hang in the margin left of the paragraph
    /// (DOCX's <c>w:dropCap="margin"</c>) and the lines beside them keep
    /// their place, rather than starting past them.
    /// </summary>
    public bool InMargin { get; init; }

    /// <summary>The most lines a drop may span, Word's limit.</summary>
    public const int MaxLines = 10;

    /// <summary><see cref="Lines"/> held to between one and <see cref="MaxLines"/>.</summary>
    public int SpannedLines => Math.Clamp(Lines, 1, MaxLines);

    /// <summary><see cref="Distance"/> as a finite, non-negative length in points.</summary>
    public double DistanceIn => float.IsFinite(Distance) ? Math.Clamp(Distance, 0, 20000) : 0;
}
