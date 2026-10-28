using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Rtf;

/// <summary>The kind of style defined in an RTF style sheet.</summary>
internal enum RtfStyleKind
{
    Paragraph,
    Character,
}

/// <summary>A style parsed from an RTF <c>\stylesheet</c> table.</summary>
internal sealed class RtfStyle
{
    public int Id { get; set; }
    public RtfStyleKind Kind { get; set; } = RtfStyleKind.Paragraph;
    public string Name { get; set; } = string.Empty;
    public int? BasedOn { get; set; }
    public int? Next { get; set; }
    public bool Additive { get; set; }

    // Paragraph formatting
    public TextAlignment? Alignment { get; set; }
    public float? LeftIndent { get; set; }
    public int? IndentLevel { get; set; }
    public float? SpacingBefore { get; set; }
    public float? SpacingAfter { get; set; }
    public int? LineSpacing { get; set; }
    public bool? MultipleLineSpacing { get; set; }
    public int? ParagraphBackgroundIndex { get; set; }
    public BColor? ParagraphBackground { get; set; }
    public bool? PageBreakBefore { get; set; }

    // Character formatting
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public bool? Underline { get; set; }
    public bool? Strikethrough { get; set; }
    public bool? Hidden { get; set; }
    public TextCapitalization? Capitalization { get; set; }
    public float? FontSize { get; set; }
    public int? FontIndex { get; set; }
    public string? FontFamily { get; set; }
    public int? ForegroundColorIndex { get; set; }
    public BColor? Foreground { get; set; }
    public int? BackgroundColorIndex { get; set; }
    public BColor? Background { get; set; }

    // Precomputed resolved structures
    public ParagraphStyle ResolvedParagraph { get; set; } = ParagraphStyle.Default;
    public InlineStyle ResolvedInline { get; set; } = InlineStyle.Default;
}

/// <summary>
/// The style sheet read from an RTF document's <c>\stylesheet</c> destination.
/// </summary>
internal sealed class RtfStyleSheet
{
    private readonly Dictionary<int, RtfStyle> _paragraphStyles = [];
    private readonly Dictionary<int, RtfStyle> _characterStyles = [];

    public void AddParagraphStyle(RtfStyle style) => _paragraphStyles[style.Id] = style;

    public void AddCharacterStyle(RtfStyle style) => _characterStyles[style.Id] = style;

    public bool TryGetParagraphStyle(int id, [NotNullWhen(true)] out RtfStyle? style) =>
        _paragraphStyles.TryGetValue(id, out style);

    public bool TryGetCharacterStyle(int id, [NotNullWhen(true)] out RtfStyle? style) =>
        _characterStyles.TryGetValue(id, out style);

    public void ResolveInheritance(RtfFontTable fonts, RtfColorTable colors)
    {
        // 1. Resolve font names and colors from the document's resource tables
        foreach (RtfStyle style in _paragraphStyles.Values)
            ResolveResources(style, fonts, colors);
        foreach (RtfStyle style in _characterStyles.Values)
            ResolveResources(style, fonts, colors);

        // 2. Resolve inheritance chains for paragraph styles
        var visited = new HashSet<int>();
        foreach (RtfStyle style in _paragraphStyles.Values)
        {
            visited.Clear();
            ResolveParagraphStyleInheritance(style, visited);
        }

        // 3. Resolve inheritance chains for character styles
        foreach (RtfStyle style in _characterStyles.Values)
        {
            visited.Clear();
            ResolveCharacterStyleInheritance(style, visited);
        }

        // 4. Precompute ParagraphStyle and InlineStyle instances
        foreach (RtfStyle style in _paragraphStyles.Values)
            ComputeParagraphStyle(style);
        foreach (RtfStyle style in _characterStyles.Values)
            ComputeCharacterStyle(style);
    }

    private static void ResolveResources(RtfStyle style, RtfFontTable fonts, RtfColorTable colors)
    {
        if (style.FontIndex is int fi)
            style.FontFamily = fonts.GetName(fi);
        if (style.ForegroundColorIndex is int ci)
            style.Foreground = colors.Get(ci);
        if (style.BackgroundColorIndex is int bi)
            style.Background = colors.Get(bi);
        if (style.ParagraphBackgroundIndex is int pbi)
            style.ParagraphBackground = colors.Get(pbi);
    }

    private void ResolveParagraphStyleInheritance(RtfStyle style, HashSet<int> visited)
    {
        if (!visited.Add(style.Id))
            return; // Cycle protection

        if (style.BasedOn is int baseId && baseId != style.Id && _paragraphStyles.TryGetValue(baseId, out RtfStyle? baseStyle))
        {
            ResolveParagraphStyleInheritance(baseStyle, visited);

            // Inherit unset paragraph properties
            style.Alignment ??= baseStyle.Alignment;
            style.IndentLevel ??= baseStyle.IndentLevel;
            style.LeftIndent ??= baseStyle.LeftIndent;
            style.SpacingBefore ??= baseStyle.SpacingBefore;
            style.SpacingAfter ??= baseStyle.SpacingAfter;
            style.LineSpacing ??= baseStyle.LineSpacing;
            style.MultipleLineSpacing ??= baseStyle.MultipleLineSpacing;
            style.ParagraphBackground ??= baseStyle.ParagraphBackground;
            style.PageBreakBefore ??= baseStyle.PageBreakBefore;

            // Inherit unset character formatting
            style.FontFamily ??= baseStyle.FontFamily;
            style.FontSize ??= baseStyle.FontSize;
            style.Bold ??= baseStyle.Bold;
            style.Italic ??= baseStyle.Italic;
            style.Underline ??= baseStyle.Underline;
            style.Strikethrough ??= baseStyle.Strikethrough;
            style.Foreground ??= baseStyle.Foreground;
            style.Background ??= baseStyle.Background;
            style.Capitalization ??= baseStyle.Capitalization;
            style.Hidden ??= baseStyle.Hidden;
        }
    }

    private void ResolveCharacterStyleInheritance(RtfStyle style, HashSet<int> visited)
    {
        if (!visited.Add(style.Id))
            return; // Cycle protection

        if (style.BasedOn is int baseId && baseId != style.Id && _characterStyles.TryGetValue(baseId, out RtfStyle? baseStyle))
        {
            ResolveCharacterStyleInheritance(baseStyle, visited);

            style.FontFamily ??= baseStyle.FontFamily;
            style.FontSize ??= baseStyle.FontSize;
            style.Bold ??= baseStyle.Bold;
            style.Italic ??= baseStyle.Italic;
            style.Underline ??= baseStyle.Underline;
            style.Strikethrough ??= baseStyle.Strikethrough;
            style.Foreground ??= baseStyle.Foreground;
            style.Background ??= baseStyle.Background;
            style.Capitalization ??= baseStyle.Capitalization;
            style.Hidden ??= baseStyle.Hidden;
        }
    }

    private static void ComputeParagraphStyle(RtfStyle style)
    {
        ParagraphStyle para = ParagraphStyle.Default;
        if (style.Alignment.HasValue) para = para with { Alignment = style.Alignment.Value };
        if (style.IndentLevel.HasValue) para = para with { IndentLevel = style.IndentLevel.Value };
        if (style.LeftIndent.HasValue) para = para with { LeftIndent = style.LeftIndent.Value };
        if (style.SpacingBefore.HasValue) para = para with { SpacingBefore = style.SpacingBefore.Value };
        if (style.SpacingAfter.HasValue) para = para with { SpacingAfter = style.SpacingAfter.Value };
        if (style.LineSpacing.HasValue)
        {
            long amount = style.LineSpacing.Value;
            para = amount == 0
                ? para with { LineSpacing = 1f, LineHeightRule = LineHeightRule.Proportional, LineHeight = 0f }
                : (style.MultipleLineSpacing == true)
                    ? para with { LineSpacing = (float)Math.Min(Math.Abs(amount) / 240d, 100d), LineHeightRule = LineHeightRule.Proportional, LineHeight = 0f }
                    : para with
                    {
                        LineSpacing = 1f,
                        LineHeightRule = amount < 0 ? LineHeightRule.Exact : LineHeightRule.AtLeast,
                        LineHeight = Math.Min(ParagraphStyle.MaxLineHeight, Math.Abs(amount) / 20f),
                    };
        }
        if (style.ParagraphBackground.HasValue && !style.ParagraphBackground.Value.IsEmpty)
            para = para with { Background = style.ParagraphBackground.Value };
        if (style.PageBreakBefore.HasValue)
            para = para with { PageBreakBefore = style.PageBreakBefore.Value };

        style.ResolvedParagraph = para;

        InlineStyle inline = InlineStyle.Default;
        if (style.FontFamily is not null) inline = inline with { FontFamily = style.FontFamily };
        if (style.FontSize.HasValue) inline = inline with { FontSize = style.FontSize.Value };
        if (style.Bold == true) inline = inline with { Bold = true };
        if (style.Italic == true) inline = inline with { Italic = true };
        if (style.Underline == true) inline = inline with { Underline = true };
        if (style.Strikethrough == true) inline = inline with { Strikethrough = true };
        if (style.Foreground.HasValue && !style.Foreground.Value.IsEmpty) inline = inline with { Foreground = style.Foreground.Value };
        if (style.Background.HasValue && !style.Background.Value.IsEmpty) inline = inline with { Background = style.Background.Value };
        if (style.Capitalization.HasValue && style.Capitalization.Value != TextCapitalization.None)
            inline = inline with { Capitalization = style.Capitalization.Value };
        if (style.Hidden == true) inline = inline with { Hidden = true };

        style.ResolvedInline = inline;
    }

    private static void ComputeCharacterStyle(RtfStyle style)
    {
        InlineStyle inline = InlineStyle.Default;
        if (style.FontFamily is not null) inline = inline with { FontFamily = style.FontFamily };
        if (style.FontSize.HasValue) inline = inline with { FontSize = style.FontSize.Value };
        if (style.Bold == true) inline = inline with { Bold = true };
        if (style.Italic == true) inline = inline with { Italic = true };
        if (style.Underline == true) inline = inline with { Underline = true };
        if (style.Strikethrough == true) inline = inline with { Strikethrough = true };
        if (style.Foreground.HasValue && !style.Foreground.Value.IsEmpty) inline = inline with { Foreground = style.Foreground.Value };
        if (style.Background.HasValue && !style.Background.Value.IsEmpty) inline = inline with { Background = style.Background.Value };
        if (style.Capitalization.HasValue && style.Capitalization.Value != TextCapitalization.None)
            inline = inline with { Capitalization = style.Capitalization.Value };
        if (style.Hidden == true) inline = inline with { Hidden = true };

        style.ResolvedInline = inline;
    }
}
