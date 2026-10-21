using Broiler.Graphics.Color;

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

    /// <summary>Whether the paragraph starts a new page.</summary>
    public bool? PageBreakBefore { get; init; }

    /// <summary>Whether the paragraph starts the next text column.</summary>
    public bool? ColumnBreakBefore { get; init; }

    /// <summary>
    /// The paragraph's shading; <see cref="BColor.Empty"/> takes it away.
    /// </summary>
    public BColor? Background { get; init; }

    /// <summary>Applies this delta over <paramref name="style"/>.</summary>
    /// <remarks>
    /// Setting no list, or a list kind other than the one the paragraph has,
    /// takes it out of its numbered list: the list was the document's, and an
    /// edit that asks for bullets is not asking for the old list's numbers.
    /// </remarks>
    public ParagraphStyle Apply(ParagraphStyle style) => style with
    {
        Background = Background ?? style.Background,
        Alignment = Alignment ?? style.Alignment,
        LineSpacing = LineSpacing ?? style.LineSpacing,
        ListKind = ListKind ?? style.ListKind,
        List = ListKind is { } kind && (kind == Model.ListKind.None || kind != style.ListKind) ? null : style.List,
        IndentLevel = IndentLevel ?? style.IndentLevel,
        SpacingBefore = SpacingBefore ?? style.SpacingBefore,
        SpacingAfter = SpacingAfter ?? style.SpacingAfter,
        PageBreakBefore = PageBreakBefore ?? style.PageBreakBefore,
        ColumnBreakBefore = ColumnBreakBefore ?? style.ColumnBreakBefore,
    };

    public static ParagraphStyleDelta WithAlignment(TextAlignment alignment) => new() { Alignment = alignment };

    public static ParagraphStyleDelta WithListKind(ListKind kind) => new() { ListKind = kind };

    public static ParagraphStyleDelta WithIndentLevel(int level) => new() { IndentLevel = level };
}
