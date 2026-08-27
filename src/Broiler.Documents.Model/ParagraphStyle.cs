namespace Broiler.Documents.Model;

/// <summary>
/// A fully resolved paragraph style. The first-release attribute set is fixed by
/// ADR 0014. Use <see cref="Default"/> rather than <c>default(ParagraphStyle)</c>
/// so line spacing is single-spaced.
/// </summary>
public readonly record struct ParagraphStyle
{
    public TextAlignment Alignment { get; init; }

    /// <summary>Line spacing multiplier; <c>1</c> is single-spaced.</summary>
    public float LineSpacing { get; init; }

    public ListKind ListKind { get; init; }

    /// <summary>Indent depth in list/indent levels; never negative.</summary>
    public int IndentLevel { get; init; }

    public float SpacingBefore { get; init; }

    public float SpacingAfter { get; init; }

    /// <summary>Left-aligned, single-spaced, no list, no indent.</summary>
    public static ParagraphStyle Default => new() { LineSpacing = 1f };
}
