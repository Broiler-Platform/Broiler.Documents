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

    /// <summary>The name as written, aliases after a comma included, which is what writerfilter matches against Writer's own.</summary>
    public string FullName { get; set; } = string.Empty;
    public int? BasedOn { get; set; }
    public int? Next { get; set; }
    public bool Additive { get; set; }

    // Paragraph formatting
    public TextAlignment? Alignment { get; set; }
    public float? LeftIndent { get; set; }
    public float? FirstLineIndent { get; set; }
    public float? RightIndent { get; set; }
    public int? IndentLevel { get; set; }
    public float? SpacingBefore { get; set; }
    public float? SpacingAfter { get; set; }
    public int? LineSpacing { get; set; }
    public bool? MultipleLineSpacing { get; set; }
    public int? ParagraphBackgroundIndex { get; set; }
    public BColor? ParagraphBackground { get; set; }
    public bool? PageBreakBefore { get; set; }
    public bool? KeepWithNext { get; set; }
    public bool? AutoSpaceBefore { get; set; }
    public bool? AutoSpaceAfter { get; set; }
    public List<ParagraphTabStop>? TabStops { get; set; }

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

    /// <summary>
    /// The formatting this style's own entry states, before anything is
    /// inherited, among the properties a paragraph reset to its default when it
    /// does not repeat them.
    /// </summary>
    public RtfStyleOwnFormatting Own { get; set; }

    // Precomputed resolved structures
    public ParagraphStyle ResolvedParagraph { get; set; } = ParagraphStyle.Default;
    public InlineStyle ResolvedInline { get; set; } = InlineStyle.Default;
}

/// <summary>
/// Which of the properties writerfilter puts back to their RTF defaults a
/// style's own entry states.
/// </summary>
/// <remarks>
/// An RTF writer repeats a style's formatting in every paragraph and run that
/// uses it, and writerfilter reads the style sheet that way: a paragraph or run
/// that names a style but not one of these properties its entry states gets the
/// property's default as direct formatting (<c>RTFSprms::cloneAndDeduplicate</c>,
/// "not found - try to override style with default", tdf#118047). soffice
/// 24.2 shows it: a paragraph <c>\pard\plain\s1</c> whose style states
/// <c>\qc\sb\sa\li\ri\fi\cbpat\b\i\ul\cf\f\fs40</c> is set left aligned, unindented,
/// unspaced and unshaded in 12 point, regular, black text in the default font,
/// while the style's <c>\sl</c>, <c>\keepn</c>, <c>\strike</c>, <c>\caps</c> and
/// <c>\highlight</c> stay. What a style inherits through <c>\sbasedon</c> is not
/// put back: a style stating nothing of its own shows its parent's formatting.
/// A character style is read the same way.
/// </remarks>
internal readonly record struct RtfStyleOwnFormatting(
    bool Alignment,
    bool LeftIndent,
    bool FirstLineIndent,
    bool RightIndent,
    bool SpacingBefore,
    bool SpacingAfter,
    bool Background,
    bool Font,
    bool FontSize,
    bool Bold,
    bool Italic,
    bool Underline,
    bool Foreground)
{
    public static RtfStyleOwnFormatting Of(RtfStyle style) => new(
        style.Alignment.HasValue,
        style.LeftIndent.HasValue || style.IndentLevel.HasValue,
        style.FirstLineIndent.HasValue,
        style.RightIndent.HasValue,
        style.SpacingBefore.HasValue,
        style.SpacingAfter.HasValue,
        style.ParagraphBackgroundIndex.HasValue,
        style.FontIndex.HasValue,
        style.FontSize.HasValue,
        style.Bold.HasValue,
        style.Italic.HasValue,
        style.Underline.HasValue,
        style.ForegroundColorIndex.HasValue);

    /// <summary>The paragraph formatting a paragraph naming the style is set in.</summary>
    public ParagraphStyle Reset(ParagraphStyle para)
    {
        ParagraphStyle d = ParagraphStyle.Default;
        if (Alignment) para = para with { Alignment = d.Alignment };
        if (LeftIndent) para = para with { IndentLevel = d.IndentLevel, LeftIndent = d.LeftIndent };
        if (FirstLineIndent) para = para with { FirstLineIndent = d.FirstLineIndent };
        if (RightIndent) para = para with { RightIndent = d.RightIndent };
        if (SpacingBefore) para = para with { SpacingBefore = d.SpacingBefore };
        if (SpacingAfter) para = para with { SpacingAfter = d.SpacingAfter };
        if (Background) para = para with { Background = d.Background };
        return para;
    }

    /// <summary>The character formatting a run naming the style is set in.</summary>
    public InlineStyle Reset(InlineStyle inline)
    {
        InlineStyle d = InlineStyle.Default;
        if (Font) inline = inline with { FontFamily = d.FontFamily };
        if (FontSize) inline = inline with { FontSize = d.FontSize };
        if (Bold) inline = inline with { Bold = d.Bold };
        if (Italic) inline = inline with { Italic = d.Italic };
        if (Underline) inline = inline with { Underline = d.Underline };
        if (Foreground) inline = inline with { Foreground = d.Foreground };
        return inline;
    }
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

    public string? CharacterStyleCalled(string name)
    {
        foreach (RtfStyle style in _characterStyles.Values)
            if (string.Equals(style.Name, name, StringComparison.OrdinalIgnoreCase))
                return style.Name;
        return null;
    }

    public bool TryGetCharacterStyle(int id, [NotNullWhen(true)] out RtfStyle? style) =>
        _characterStyles.TryGetValue(id, out style);

    /// <summary>
    /// Resolves the styles' fonts and colours from the document's tables and
    /// their inheritance chains.
    /// </summary>
    /// <param name="fonts">The document's font table.</param>
    /// <param name="colors">The document's colour table.</param>
    /// <param name="defaultFontFamily">
    /// The family <c>\deff</c> names in the font table, or null where it names
    /// none there. writerfilter applies that font to all the text, so a style
    /// based on Writer's "Heading" that states no font of its own is drawn in
    /// it rather than in Heading's Liberation Sans: soffice 24.2 sets
    /// <c>{\rtf1\deff0{\fonttbl{\f0 Courier New;}}{\stylesheet{\s1 heading 1;}}\s1 H\par}</c>
    /// in Liberation Mono, Courier New's stand-in, at Heading's 14 points.
    /// </param>
    public void ResolveInheritance(RtfFontTable fonts, RtfColorTable colors, string? defaultFontFamily = null)
    {
        _defaultFontFamily = defaultFontFamily;
        // 1. Resolve font names and colors from the document's resource tables
        foreach (RtfStyle style in _paragraphStyles.Values)
        {
            style.Own = RtfStyleOwnFormatting.Of(style);
            ResolveResources(style, fonts, colors);
        }
        foreach (RtfStyle style in _characterStyles.Values)
        {
            style.Own = RtfStyleOwnFormatting.Of(style);
            ResolveResources(style, fonts, colors);
        }

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
            style.FontFamily = fonts.Resolve(fi);
        if (style.ForegroundColorIndex is int ci)
            style.Foreground = colors.Get(ci);
        if (style.BackgroundColorIndex is int bi)
            style.Background = colors.Get(bi);
        if (style.ParagraphBackgroundIndex is int pbi)
            style.ParagraphBackground = colors.Get(pbi);
    }

    /// <summary>
    /// Writer's "Heading" paragraph style, as LibreOffice 24.2's default
    /// template has it: Liberation Sans 14 point, 0.423 cm above, 0.212 cm
    /// below, kept with the next paragraph.
    /// </summary>
    /// <remarks>
    /// writerfilter gives an RTF style named after one of Writer's own its
    /// Writer name (<c>StyleSheetTable::ConvertStyleName</c>: "heading 1" is
    /// "Heading 1"), clears that style's own formatting and applies the RTF
    /// entry's, and keeps the Writer style's parent unless the entry states
    /// <c>\sbasedon</c>. Heading 1 to 9, Title and Subtitle are based on
    /// "Heading", so a paragraph in such a style is set in Heading's font,
    /// size and spacing (soffice 24.2, FODT export of
    /// <c>{\s3 heading 3;}</c>: "Heading 3", no formatting of its own, parent
    /// "Heading"; with <c>\sbasedon0</c> the parent is "Standard").
    /// </remarks>
    private static readonly RtfStyle WriterHeading = new()
    {
        Name = "Heading",
        FontFamily = "Liberation Sans",
        FontSize = 14f,
        SpacingBefore = 0.423f / 2.54f * 72f,
        SpacingAfter = 0.212f / 2.54f * 72f,
        KeepWithNext = true,
    };

    /// <summary>Whether Writer bases the style an RTF entry of this name maps to on "Heading".</summary>
    internal static bool IsBasedOnWriterHeading(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Equals("Title", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Subtitle", StringComparison.OrdinalIgnoreCase))
            return true;
        return trimmed.Length == 9 &&
            trimmed.StartsWith("heading ", StringComparison.OrdinalIgnoreCase) &&
            trimmed[8] is >= '1' and <= '9';
    }

    private string? _defaultFontFamily;

    private void ResolveParagraphStyleInheritance(RtfStyle style, HashSet<int> visited)
    {
        if (!visited.Add(style.Id))
            return; // Cycle protection

        if (style.BasedOn is null && IsBasedOnWriterHeading(style.FullName))
        {
            style.FontFamily ??= _defaultFontFamily ?? WriterHeading.FontFamily;
            style.FontSize ??= WriterHeading.FontSize;
            style.SpacingBefore ??= WriterHeading.SpacingBefore;
            style.SpacingAfter ??= WriterHeading.SpacingAfter;
            style.KeepWithNext ??= WriterHeading.KeepWithNext;
            return;
        }

        if (style.BasedOn is int baseId && baseId != style.Id && _paragraphStyles.TryGetValue(baseId, out RtfStyle? baseStyle))
        {
            ResolveParagraphStyleInheritance(baseStyle, visited);

            // Inherit unset paragraph properties
            style.Alignment ??= baseStyle.Alignment;
            style.IndentLevel ??= baseStyle.IndentLevel;
            style.LeftIndent ??= baseStyle.LeftIndent;
            style.FirstLineIndent ??= baseStyle.FirstLineIndent;
            style.RightIndent ??= baseStyle.RightIndent;
            style.SpacingBefore ??= baseStyle.SpacingBefore;
            style.SpacingAfter ??= baseStyle.SpacingAfter;
            style.LineSpacing ??= baseStyle.LineSpacing;
            style.MultipleLineSpacing ??= baseStyle.MultipleLineSpacing;
            style.ParagraphBackground ??= baseStyle.ParagraphBackground;
            style.PageBreakBefore ??= baseStyle.PageBreakBefore;
            style.KeepWithNext ??= baseStyle.KeepWithNext;
            style.AutoSpaceBefore ??= baseStyle.AutoSpaceBefore;
            style.AutoSpaceAfter ??= baseStyle.AutoSpaceAfter;
            style.TabStops ??= baseStyle.TabStops;

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
        if (style.FirstLineIndent is float first && first != 0) para = para with { FirstLineIndent = first };
        if (style.RightIndent is float right && right != 0) para = para with { RightIndent = right };
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
        if (style.KeepWithNext == true)
            para = para with { KeepWithNext = true };
        if (style.TabStops is { Count: > 0 })
            para = para with { TabStops = new ParagraphTabStops(style.TabStops) };

        style.ResolvedParagraph = style.Own.Reset(para);

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

        style.ResolvedInline = style.Own.Reset(inline);
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
