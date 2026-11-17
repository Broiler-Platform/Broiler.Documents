using System;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Model;

/// <summary>
/// A partial change to a <see cref="ParagraphStyle"/>. Attributes left
/// <see langword="null"/> are unchanged.
/// </summary>
public readonly record struct ParagraphStyleDelta
{
    public NamedParagraphStyle? DefineStyle { get; init; }
    public bool SetStyleName { get; init; }
    public string? StyleName { get; init; }

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
    public bool SetLeftIndent { get; init; }
    public bool SetRightIndent { get; init; }
    public bool SetFirstLineIndent { get; init; }
    public float? LeftIndent { get; init; }
    public float? RightIndent { get; init; }
    public float? FirstLineIndent { get; init; }
    public bool SetTabStops { get; init; }
    public ParagraphTabStops? TabStops { get; init; }
    public bool SetHeadingLevel { get; init; }
    public int? HeadingLevel { get; init; }
    public bool SetBorder { get; init; }
    public ParagraphBorder? Border { get; init; }
    public bool SetListReference { get; init; }
    public ListReference? ListReference { get; init; }
    public InlineStyleDelta? InlinePreset { get; init; }

    /// <summary>Whether the paragraph starts a new page.</summary>
    public bool? PageBreakBefore { get; init; }

    /// <summary>Whether the paragraph starts the next text column.</summary>
    public bool? ColumnBreakBefore { get; init; }
    /// <summary>Set or clear sequential soft page hints in the selected body paragraphs.</summary>
    public bool? SoftPageBreak { get; init; }
    public bool? KeepWithNext { get; init; }
    public bool? KeepTogether { get; init; }
    public bool SetLineCounts { get; init; }
    public int? WidowLines { get; init; }
    public int? OrphanLines { get; init; }

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
    public ParagraphStyle Apply(ParagraphStyle style)
    {
        ListReference? list = SetListReference ? ListReference :
            ListKind is { } kind && (kind == Model.ListKind.None || kind != style.ListKind) ? null : style.List;
        if (!SetListReference && list is { } current && IndentLevel is int indent && indent != style.IndentLevel)
            list = current with { Level = Math.Clamp(current.Level + indent - style.IndentLevel, 0, ListDefinition.MaxLevels - 1), RestartAt = null };

        return style with
        {
            Background = Background ?? style.Background,
            Alignment = Alignment ?? style.Alignment,
            LineSpacing = LineSpacing ?? style.LineSpacing,
            LineHeightRule = LineHeightRule ?? (LineSpacing.HasValue ? Model.LineHeightRule.Proportional : style.LineHeightRule),
            LineHeight = LineHeight ?? (LineSpacing.HasValue && !LineHeightRule.HasValue ? 0f : style.LineHeight),
            ListKind = list?.Kind ?? ListKind ?? style.ListKind,
            List = list,
            IndentLevel = IndentLevel ?? style.IndentLevel,
            LeftIndent = SetLeftIndent ? LeftIndent : IndentLevel.HasValue ? null : style.LeftIndent,
            RightIndent = SetRightIndent ? RightIndent : style.RightIndent,
            FirstLineIndent = SetFirstLineIndent ? FirstLineIndent : IndentLevel.HasValue ? null : style.FirstLineIndent,
            TabStops = SetTabStops ? TabStops : style.TabStops,
            HeadingLevel = SetHeadingLevel ? HeadingLevel : style.HeadingLevel,
            Border = SetBorder ? Border : style.Border,
            SpacingBefore = SpacingBefore ?? style.SpacingBefore,
            SpacingAfter = SpacingAfter ?? style.SpacingAfter,
            PageBreakBefore = PageBreakBefore ?? style.PageBreakBefore,
            ColumnBreakBefore = ColumnBreakBefore ?? style.ColumnBreakBefore,
            KeepWithNext = KeepWithNext ?? style.KeepWithNext,
            KeepTogether = KeepTogether ?? style.KeepTogether,
            WidowLines = SetLineCounts ? WidowLines : style.WidowLines,
            OrphanLines = SetLineCounts ? OrphanLines : style.OrphanLines,
        };
    }

    public static ParagraphStyleDelta WithAlignment(TextAlignment alignment) => new() { Alignment = alignment };

    public static ParagraphStyleDelta WithListKind(ListKind kind) => new() { ListKind = kind };

    public static ParagraphStyleDelta WithIndentLevel(int level) => new() { IndentLevel = level };
}
