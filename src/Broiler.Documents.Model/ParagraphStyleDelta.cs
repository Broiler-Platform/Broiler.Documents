using Broiler.Graphics.Color;

namespace Broiler.Documents.Model;

/// <summary>
/// A partial change to a <see cref="ParagraphStyle"/>. Attributes left
/// <see langword="null"/> are unchanged.
/// </summary>
public readonly record struct ParagraphStyleDelta
{
    public TextAlignment? Alignment { get; init; }

    /// <summary>
    /// The line spacing multiplier. Setting it alone makes the lines
    /// proportional again: a paragraph set to double spacing is asking for its
    /// lines to follow its text, not for a fixed height it held before.
    /// </summary>
    public float? LineSpacing { get; init; }

    /// <summary>How the lines are made tall; see <see cref="ParagraphStyle.LineHeightRule"/>.</summary>
    public LineHeightRule? LineHeightRule { get; init; }

    /// <summary>The height a line is at least, or exactly, in points.</summary>
    public float? LineHeight { get; init; }

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
    public ParagraphStyle Apply(ParagraphStyle style) => style with
    {
        Background = Background ?? style.Background,
        Alignment = Alignment ?? style.Alignment,
        LineSpacing = LineSpacing ?? style.LineSpacing,
        LineHeightRule = LineHeightRule ?? (LineSpacing.HasValue ? Model.LineHeightRule.Proportional : style.LineHeightRule),
        LineHeight = LineHeight ?? (LineSpacing.HasValue && !LineHeightRule.HasValue ? 0f : style.LineHeight),
        ListKind = ListKind ?? style.ListKind,
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
