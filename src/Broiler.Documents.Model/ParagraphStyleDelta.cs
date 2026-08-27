namespace Broiler.Documents.Model;

/// <summary>
/// A partial change to a <see cref="ParagraphStyle"/>. Attributes left
/// <see langword="null"/> are unchanged.
/// </summary>
public readonly record struct ParagraphStyleDelta
{
    public TextAlignment? Alignment { get; init; }

    public float? LineSpacing { get; init; }

    public ListKind? ListKind { get; init; }

    public int? IndentLevel { get; init; }

    public float? SpacingBefore { get; init; }

    public float? SpacingAfter { get; init; }

    /// <summary>Applies this delta over <paramref name="style"/>.</summary>
    public ParagraphStyle Apply(ParagraphStyle style) => style with
    {
        Alignment = Alignment ?? style.Alignment,
        LineSpacing = LineSpacing ?? style.LineSpacing,
        ListKind = ListKind ?? style.ListKind,
        IndentLevel = IndentLevel ?? style.IndentLevel,
        SpacingBefore = SpacingBefore ?? style.SpacingBefore,
        SpacingAfter = SpacingAfter ?? style.SpacingAfter,
    };

    public static ParagraphStyleDelta WithAlignment(TextAlignment alignment) => new() { Alignment = alignment };

    public static ParagraphStyleDelta WithListKind(ListKind kind) => new() { ListKind = kind };

    public static ParagraphStyleDelta WithIndentLevel(int level) => new() { IndentLevel = level };
}
