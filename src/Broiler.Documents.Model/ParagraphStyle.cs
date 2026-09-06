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

    /// <summary>Line spacing multiplier; <c>1</c> is single-spaced.</summary>
    public float LineSpacing { get; init; }

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

    /// <summary>Left-aligned, single-spaced, no list, no indent.</summary>
    public static ParagraphStyle Default => new() { LineSpacing = 1f };
}
