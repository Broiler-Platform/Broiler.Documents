using System;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Model;

/// <summary>
/// A fully resolved paragraph style. Use <see cref="Default"/> rather than
/// <c>default(ParagraphStyle)</c> so line spacing is single-spaced.
/// </summary>
/// <remarks>
/// The attribute set is not frozen. This comment used to say it was fixed by
/// ADR 0014, which is a citation that does not hold up: that ADR is about taking
/// members out and says nothing about the shape of this type. What actually
/// governs it is ADR 0002, which promoted the model, and the roadmap, which
/// still lists freezing the public names as work not yet done.
/// </remarks>
public readonly record struct ParagraphStyle
{
    public TextAlignment Alignment { get; init; }

    /// <summary>
    /// Line spacing multiplier; <c>1</c> is single-spaced. Applies where
    /// <see cref="LineHeightRule"/> is <see cref="Model.LineHeightRule.Proportional"/>.
    /// </summary>
    public float LineSpacing { get; init; }

    /// <summary>
    /// Whether the lines are as tall as their text times <see cref="LineSpacing"/>,
    /// at least <see cref="LineHeight"/>, or exactly that.
    /// </summary>
    /// <remarks>
    /// A form drawn in frames sets every line at ten points whatever its font
    /// (RTF's <c>\sl-200</c>), and a letter keeps its lines at least as tall as
    /// its body text with a smaller footnote face among them. Neither is a
    /// multiplier of the line's own height, and both were read as single
    /// spacing.
    /// </remarks>
    public LineHeightRule LineHeightRule { get; init; }

    /// <summary>
    /// The height a line is at least, or exactly, in points; nought where
    /// <see cref="LineHeightRule"/> is proportional.
    /// </summary>
    public float LineHeight { get; init; }

    /// <summary>
    /// The largest line height a reader keeps, in points: 1584, Word's limit
    /// for an exact or at-least height. A file stating more is clamped to it, so
    /// a hostile height cannot lay every line out a page tall.
    /// </summary>
    public const float MaxLineHeight = 1584f;

    /// <summary>
    /// The height of a line box whose text is <paramref name="natural"/> points
    /// tall, under this paragraph's rule.
    /// </summary>
    public double LineBoxHeight(double natural) => LineHeightRule switch
    {
        LineHeightRule.Exact when LineHeight > 0 => LineHeight,
        LineHeightRule.AtLeast => Math.Max(natural, LineHeight),
        _ => natural * (LineSpacing > 0 ? LineSpacing : 1f),
    };

    public ListKind ListKind { get; init; }

    /// <summary>Indent depth in list/indent levels; never negative.</summary>
    public int IndentLevel { get; init; }

    /// <summary>Space above the paragraph, in points.</summary>
    public float SpacingBefore { get; init; }

    /// <summary>Space below the paragraph, in points.</summary>
    public float SpacingAfter { get; init; }

    /// <summary>
    /// Whether the paragraph starts a new page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A property of the paragraph rather than a character in the text, which is
    /// the choice every format here makes too: DOCX has
    /// <c>w:pageBreakBefore</c>, ODF <c>fo:break-before</c>, and RTF's
    /// <c>\page</c> is read as one on the paragraph that follows it. Modelling it
    /// as content would mean a break could sit inside a run and be styled bold,
    /// which is not a thing a break can be.
    /// </para>
    /// <para>
    /// Before, on purpose, and not after. The two are not symmetric in a model
    /// holding one flag: a break stated after paragraph five and a break stated
    /// before paragraph six are the same break, and admitting both spellings
    /// would let a document say it twice and mean once.
    /// </para>
    /// <para>
    /// Nothing about how it is drawn belongs here. A continuous render has no
    /// pages, so it ignores this, and says as much rather than pretending it
    /// honoured it.
    /// </para>
    /// </remarks>
    public bool PageBreakBefore { get; init; }

    /// <summary>
    /// Whether the paragraph starts the next text column.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A newsletter ends a column early this way: DOCX's
    /// <c>w:br w:type="column"</c>, ODF's <c>fo:break-before="column"</c>, RTF's
    /// <c>\column</c>. It is held as <see cref="PageBreakBefore"/> is, on the
    /// paragraph that starts after it, for the same reasons.
    /// </para>
    /// <para>
    /// Where the page has one column there is no next column on it, and the
    /// break starts the next page instead. That is what ECMA-376 says of
    /// <c>ST_BrType</c> and what Word and LibreOffice draw, so a reader that knows
    /// the paragraph is in a section of one column states a page break, and a
    /// layout that finds this on a page of one column takes it as one.
    /// <see cref="PageBreakBefore"/> wins where both are set.
    /// </para>
    /// </remarks>
    public bool ColumnBreakBefore { get; init; }

    /// <summary>
    /// The colour the paragraph's area is shaded with, or
    /// <see cref="BColor.Empty"/> for none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A title bar, a boxed note, a coloured band behind a heading: DOCX's
    /// <c>w:pPr/w:shd</c>, ODF's <c>fo:background-color</c> on a paragraph, RTF's
    /// <c>\cbpat</c>. It belongs to the paragraph rather than to its runs, which
    /// shade only the words: the paragraph's colour runs from its indent to the
    /// column's edge whatever the line holds.
    /// </para>
    /// <para>
    /// How it is drawn is LibreOffice's, measured: the box covers the lines,
    /// between the indents, and not the space before or after - except where the
    /// next paragraph is shaded the same, when the space between them is shaded
    /// too and the two read as one block.
    /// </para>
    /// </remarks>
    public BColor Background { get; init; }

    /// <summary>Left-aligned, single-spaced, no list, no indent.</summary>
    public static ParagraphStyle Default => new() { LineSpacing = 1f };
}
