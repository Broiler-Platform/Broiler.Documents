using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Broiler.Documents.Model;
using Broiler.Graphics;

namespace Broiler.Documents.FormatCodes;

/// <summary>Projects normalized Broiler rich-text state into grammar-version 1 tokens.</summary>
public sealed class FormatCodeProjector
{
    private const int InlinePropertyCount = 10;

    public FormatCodeProjection Project(
        RichTextDocument document,
        FormatCodeProjectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= FormatCodeProjectionOptions.Default;
        ValidateOptions(options);

        var builder = new ProjectionBuilder(document, options, cancellationToken);
        for (int paragraphIndex = 0; paragraphIndex < document.ParagraphCount; paragraphIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RichTextParagraph paragraph = document.Paragraphs[paragraphIndex];
            RichTextPosition paragraphStart = new(paragraphIndex, 0);
            RichTextPosition paragraphEnd = new(paragraphIndex, paragraph.Length);
            var paragraphRange = new RichTextRange(paragraphStart, paragraphEnd);

            EmitParagraphStyle(builder, paragraph.Style, paragraphStart, paragraphRange);

            InlineStyle previousStyle = InlineStyle.Default;
            RichTextRange previousRange = RichTextRange.Caret(paragraphStart);
            int offset = 0;
            foreach (StyleRun run in paragraph.Runs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RichTextPosition runStart = new(paragraphIndex, offset);
                RichTextPosition runEnd = new(paragraphIndex, offset + run.Length);
                var currentRange = new RichTextRange(runStart, runEnd);

                EmitInlineTransition(
                    builder,
                    previousStyle,
                    run.Style,
                    runStart,
                    previousRange,
                    currentRange);
                EmitContent(builder, paragraph.Text, paragraphIndex, offset, run.Length, currentRange);

                previousStyle = run.Style;
                previousRange = currentRange;
                offset += run.Length;
            }

            EmitInlineTransition(
                builder,
                previousStyle,
                InlineStyle.Default,
                paragraphEnd,
                previousRange,
                RichTextRange.Caret(paragraphEnd));

            if (paragraph.Length == 0)
            {
                builder.AddToken(
                    FormatCodeTokenKind.StructureCode,
                    "[Empty Paragraph]",
                    paragraphStart,
                    paragraphStart,
                    RichTextRange.Caret(paragraphStart),
                    FormatCodeMappingMode.Boundary);
            }

            if (paragraphIndex < document.ParagraphCount - 1)
            {
                RichTextPosition nextStart = new(paragraphIndex + 1, 0);
                builder.AddToken(
                    FormatCodeTokenKind.StructureCode,
                    "[Paragraph Break]\n",
                    paragraphEnd,
                    nextStart,
                    new RichTextRange(paragraphEnd, nextStart),
                    FormatCodeMappingMode.Expanded,
                    StructureDescriptor(
                        FormatCodeProperty.ParagraphBreak,
                        new RichTextRange(paragraphEnd, nextStart)));
            }
        }

        FormatCodeProjection canonical = builder.Build(Array.Empty<FormatCodeToken>());
        if (options.PendingStyle is not FormatCodePendingStyle pending)
            return canonical;
        if (!document.IsValid(pending.Caret))
            throw new ArgumentOutOfRangeException(nameof(options), "Pending caret is not valid for the document.");

        IReadOnlyList<FormatCodeToken> pendingTokens = BuildPendingTokens(canonical, document, pending, options);
        return new FormatCodeProjection(
            document,
            canonical.Text,
            canonical.Tokens,
            pendingTokens,
            canonical.Diagnostics);
    }

    private static void EmitParagraphStyle(
        ProjectionBuilder builder,
        ParagraphStyle style,
        RichTextPosition boundary,
        RichTextRange paragraphRange)
    {
        if (style.Alignment != TextAlignment.Left)
        {
            string value = style.Alignment switch
            {
                TextAlignment.Center => "CENTER",
                TextAlignment.Right => "RIGHT",
                _ => $"UNKNOWN {((int)style.Alignment).ToString(CultureInfo.InvariantCulture)}",
            };
            builder.AddToken(
                FormatCodeTokenKind.ParagraphCode,
                $"[Align {value}]",
                boundary,
                boundary,
                paragraphRange,
                FormatCodeMappingMode.Boundary,
                ParagraphDescriptor(FormatCodeProperty.Alignment, paragraphRange,
                    ParagraphStyleDelta.WithAlignment(TextAlignment.Left)));
            if (style.Alignment is not TextAlignment.Center and not TextAlignment.Right)
                builder.AddDiagnostic("FC1001", "Unknown text-alignment value was projected numerically.", paragraphRange);
        }

        if (style.ListKind != ListKind.None)
        {
            string value = style.ListKind switch
            {
                ListKind.Bullet => "BULLET",
                ListKind.Numbered => "NUMBERED",
                _ => $"UNKNOWN {((int)style.ListKind).ToString(CultureInfo.InvariantCulture)}",
            };
            builder.AddToken(
                FormatCodeTokenKind.ParagraphCode,
                $"[List {value}]",
                boundary,
                boundary,
                paragraphRange,
                FormatCodeMappingMode.Boundary,
                ParagraphDescriptor(FormatCodeProperty.ListKind, paragraphRange,
                    ParagraphStyleDelta.WithListKind(ListKind.None)));
            if (style.ListKind is not ListKind.Bullet and not ListKind.Numbered)
                builder.AddDiagnostic("FC1002", "Unknown list-kind value was projected numerically.", paragraphRange);
        }

        AddParagraphNumber(builder, "Indent", FormatCodeProperty.IndentLevel,
            style.IndentLevel, 0, boundary, paragraphRange);
        AddParagraphNumber(builder, "Line Spacing", FormatCodeProperty.LineSpacing,
            style.LineSpacing, 1f, boundary, paragraphRange);
        AddParagraphNumber(builder, "Space Before", FormatCodeProperty.SpacingBefore,
            style.SpacingBefore, 0f, boundary, paragraphRange);
        AddParagraphNumber(builder, "Space After", FormatCodeProperty.SpacingAfter,
            style.SpacingAfter, 0f, boundary, paragraphRange);

        if (style.IndentLevel < 0 || style.LineSpacing <= 0 ||
            style.SpacingBefore < 0 || style.SpacingAfter < 0)
        {
            builder.AddDiagnostic("FC1003", "Out-of-domain paragraph metrics were preserved in canonical output.", paragraphRange);
        }
    }

    private static void AddParagraphNumber(
        ProjectionBuilder builder,
        string name,
        FormatCodeProperty property,
        int value,
        int defaultValue,
        RichTextPosition boundary,
        RichTextRange paragraphRange)
    {
        if (value == defaultValue)
            return;
        builder.AddToken(
            FormatCodeTokenKind.ParagraphCode,
            $"[{name} {value.ToString(CultureInfo.InvariantCulture)}]",
            boundary,
            boundary,
            paragraphRange,
            FormatCodeMappingMode.Boundary,
            ParagraphDescriptor(property, paragraphRange,
                new ParagraphStyleDelta { IndentLevel = defaultValue }));
    }

    private static void AddParagraphNumber(
        ProjectionBuilder builder,
        string name,
        FormatCodeProperty property,
        float value,
        float defaultValue,
        RichTextPosition boundary,
        RichTextRange paragraphRange)
    {
        if (value.Equals(defaultValue))
            return;
        builder.AddToken(
            FormatCodeTokenKind.ParagraphCode,
            $"[{name} {FormatNumber(value)}]",
            boundary,
            boundary,
            paragraphRange,
            FormatCodeMappingMode.Boundary,
            ParagraphDescriptor(property, paragraphRange, property switch
            {
                FormatCodeProperty.LineSpacing => new ParagraphStyleDelta { LineSpacing = defaultValue },
                FormatCodeProperty.SpacingBefore => new ParagraphStyleDelta { SpacingBefore = defaultValue },
                FormatCodeProperty.SpacingAfter => new ParagraphStyleDelta { SpacingAfter = defaultValue },
                _ => throw new ArgumentOutOfRangeException(nameof(property)),
            }));
        if (!float.IsFinite(value))
            builder.AddDiagnostic("FC1004", "A non-finite paragraph metric was projected canonically.", paragraphRange);
    }

    private static void EmitInlineTransition(
        ProjectionBuilder builder,
        InlineStyle from,
        InlineStyle to,
        RichTextPosition boundary,
        RichTextRange closingRange,
        RichTextRange openingRange)
    {
        for (int property = InlinePropertyCount - 1; property >= 0; property--)
        {
            if (InlinePropertyChanged(property, from, to) && InlinePropertyIsActive(property, from))
            {
                builder.AddToken(
                    FormatCodeTokenKind.InlineCode,
                    InlineCloseToken(property),
                    boundary,
                    boundary,
                    closingRange,
                    FormatCodeMappingMode.Boundary,
                    InlineDescriptor(property, closingRange));
            }
        }

        for (int property = 0; property < InlinePropertyCount; property++)
        {
            if (InlinePropertyChanged(property, from, to) && InlinePropertyIsActive(property, to))
            {
                builder.AddToken(
                    FormatCodeTokenKind.InlineCode,
                    InlineOpenToken(property, to, builder),
                    boundary,
                    boundary,
                    openingRange,
                    FormatCodeMappingMode.Boundary,
                    InlineDescriptor(property, openingRange));
                if (property == 5 && to.FontSize is float size && !float.IsFinite(size))
                {
                    builder.AddDiagnostic(
                        "FC1006",
                        "A non-finite inline font size was projected canonically.",
                        openingRange);
                }
            }
        }
    }

    private static void EmitContent(
        ProjectionBuilder builder,
        string text,
        int paragraphIndex,
        int start,
        int length,
        RichTextRange runRange)
    {
        int end = start + length;
        int offset = start;
        while (offset < end)
        {
            builder.CancellationToken.ThrowIfCancellationRequested();
            int literalStart = offset;
            while (offset < end && !RequiresSpecialToken(text, offset))
                offset++;

            if (offset > literalStart)
            {
                RichTextPosition before = new(paragraphIndex, literalStart);
                RichTextPosition after = new(paragraphIndex, offset);
                builder.AddToken(
                    FormatCodeTokenKind.Text,
                    text.Substring(literalStart, offset - literalStart),
                    before,
                    after,
                    new RichTextRange(before, after),
                    FormatCodeMappingMode.Linear);
            }

            if (offset >= end)
                break;

            char character = text[offset];
            RichTextPosition sourceBefore = new(paragraphIndex, offset);
            RichTextPosition sourceAfter = new(paragraphIndex, offset + 1);
            var affected = new RichTextRange(sourceBefore, sourceAfter);
            switch (character)
            {
                case '\t':
                    builder.AddToken(FormatCodeTokenKind.StructureCode, "[Tab]", sourceBefore, sourceAfter,
                        affected, FormatCodeMappingMode.Expanded,
                        StructureDescriptor(FormatCodeProperty.Tab, affected));
                    break;
                case '\u2028':
                    builder.AddToken(FormatCodeTokenKind.StructureCode, "[Line Break]", sourceBefore, sourceAfter,
                        affected, FormatCodeMappingMode.Expanded,
                        StructureDescriptor(FormatCodeProperty.LineBreak, affected));
                    break;
                case InlineImage.Placeholder:
                    // The character an image occupies. Removing the token
                    // removes that character, which is what deletes the picture.
                    builder.AddToken(FormatCodeTokenKind.StructureCode, "[Image]", sourceBefore, sourceAfter,
                        affected, FormatCodeMappingMode.Expanded,
                        StructureDescriptor(FormatCodeProperty.Image, affected));
                    break;
                case '\\':
                    builder.AddToken(FormatCodeTokenKind.Escape, "\\\\", sourceBefore, sourceAfter, affected, FormatCodeMappingMode.Expanded);
                    break;
                case '[':
                    builder.AddToken(FormatCodeTokenKind.Escape, "\\[", sourceBefore, sourceAfter, affected, FormatCodeMappingMode.Expanded);
                    break;
                case ']':
                    builder.AddToken(FormatCodeTokenKind.Escape, "\\]", sourceBefore, sourceAfter, affected, FormatCodeMappingMode.Expanded);
                    break;
                default:
                    builder.AddToken(
                        FormatCodeTokenKind.Escape,
                        $"\\u{{{((int)character).ToString("X4", CultureInfo.InvariantCulture)}}}",
                        sourceBefore,
                        sourceAfter,
                        affected,
                        FormatCodeMappingMode.Expanded);
                    if (char.IsSurrogate(character))
                        builder.AddDiagnostic("FC1005", "An unpaired UTF-16 surrogate was escaped.", affected);
                    break;
            }

            offset++;
        }
    }

    private static bool RequiresSpecialToken(string text, int index)
    {
        char character = text[index];
        if (character is '\t' or '\u2028' or InlineImage.Placeholder or '\\' or '[' or ']')
            return true;
        if (char.IsHighSurrogate(character))
            return index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]);
        if (char.IsLowSurrogate(character))
            return index == 0 || !char.IsHighSurrogate(text[index - 1]);

        UnicodeCategory category = char.GetUnicodeCategory(character);
        return category is UnicodeCategory.Control or UnicodeCategory.Format or
            UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
    }

    private static IReadOnlyList<FormatCodeToken> BuildPendingTokens(
        FormatCodeProjection canonical,
        RichTextDocument document,
        FormatCodePendingStyle pending,
        FormatCodeProjectionOptions options)
    {
        InlineStyle current = document.InlineStyleAt(pending.Caret);
        FormatCodeCaret caret = canonical.MapDocumentPosition(pending.Caret, FormatCodeBoundaryAffinity.After);
        int projectedStart = canonical.GetProjectedOffset(caret);
        var range = RichTextRange.Caret(pending.Caret);
        var result = new List<FormatCodeToken>();

        for (int property = 0; property < InlinePropertyCount; property++)
        {
            if (!InlinePropertyChanged(property, current, pending.Style))
                continue;

            string canonicalToken = InlinePropertyIsActive(property, pending.Style)
                ? InlineOpenToken(property, pending.Style, options)
                : InlineCloseToken(property);
            string display = $"[Pending {canonicalToken[1..^1]}]";
            if (canonical.Tokens.Count + result.Count >= options.MaxTokens)
                throw new FormatCodeProjectionLimitException($"Token count exceeds limit {options.MaxTokens}.");
            result.Add(new FormatCodeToken(
                FormatCodeTokenKind.PendingCode,
                display,
                projectedStart,
                0,
                pending.Caret,
                pending.Caret,
                range,
                FormatCodeEditCapabilities.Navigate | FormatCodeEditCapabilities.ChangeFormatting,
                InlineDescriptor(property, range),
                FormatCodeMappingMode.Boundary));
        }

        return result;
    }

    private static bool InlinePropertyChanged(int property, InlineStyle left, InlineStyle right) => property switch
    {
        0 => left.Bold != right.Bold,
        1 => left.Italic != right.Italic,
        2 => left.Underline != right.Underline,
        3 => left.Strikethrough != right.Strikethrough,
        4 => !string.Equals(left.FontFamily, right.FontFamily, StringComparison.Ordinal),
        5 => !Nullable.Equals(left.FontSize, right.FontSize),
        6 => left.Foreground != right.Foreground,
        7 => left.Background != right.Background,
        8 => !string.Equals(NormalizeLink(left.LinkHref), NormalizeLink(right.LinkHref), StringComparison.Ordinal),
        9 => left.Capitalization != right.Capitalization,
        _ => throw new ArgumentOutOfRangeException(nameof(property)),
    };

    private static FormatCodeTokenEditDescriptor InlineDescriptor(int property, RichTextRange range)
    {
        InlineStyleDelta delta = property switch
        {
            0 => InlineStyleDelta.ToggleBold(false),
            1 => InlineStyleDelta.ToggleItalic(false),
            2 => InlineStyleDelta.ToggleUnderline(false),
            3 => InlineStyleDelta.ToggleStrikethrough(false),
            4 => InlineStyleDelta.WithFontFamily(null),
            5 => InlineStyleDelta.WithFontSize(null),
            6 => InlineStyleDelta.WithForeground(BColor.Empty),
            7 => InlineStyleDelta.WithBackground(BColor.Empty),
            8 => InlineStyleDelta.WithLink(null),
            9 => InlineStyleDelta.WithCapitalization(TextCapitalization.None),
            _ => throw new ArgumentOutOfRangeException(nameof(property)),
        };
        return new FormatCodeTokenEditDescriptor(
            (FormatCodeProperty)((int)FormatCodeProperty.Bold + property),
            new ApplyFormatCodeInlineIntent(range, delta));
    }

    private static FormatCodeTokenEditDescriptor ParagraphDescriptor(
        FormatCodeProperty property,
        RichTextRange range,
        ParagraphStyleDelta delta) =>
        new(property, new ApplyFormatCodeParagraphIntent(range, delta));

    private static FormatCodeTokenEditDescriptor StructureDescriptor(
        FormatCodeProperty property,
        RichTextRange range) =>
        new(property, new ReplaceFormatCodeTextIntent(range, string.Empty));

    private static bool InlinePropertyIsActive(int property, InlineStyle style) => property switch
    {
        0 => style.Bold,
        1 => style.Italic,
        2 => style.Underline,
        3 => style.Strikethrough,
        4 => style.FontFamily is not null,
        5 => style.FontSize is not null,
        6 => !style.Foreground.IsEmpty,
        7 => !style.Background.IsEmpty,
        8 => NormalizeLink(style.LinkHref) is not null,
        9 => style.Capitalization != TextCapitalization.None,
        _ => throw new ArgumentOutOfRangeException(nameof(property)),
    };

    private static string InlineCloseToken(int property) => property switch
    {
        0 => "[Bold OFF]",
        1 => "[Italic OFF]",
        2 => "[Underline OFF]",
        3 => "[Strike OFF]",
        4 => "[Font DEFAULT]",
        5 => "[Size DEFAULT]",
        6 => "[Text Color DEFAULT]",
        7 => "[Highlight NONE]",
        8 => "[Link OFF]",
        9 => "[Caps OFF]",
        _ => throw new ArgumentOutOfRangeException(nameof(property)),
    };

    private static string InlineOpenToken(int property, InlineStyle style, ProjectionBuilder builder) =>
        InlineOpenToken(property, style, builder.Options);

    private static string InlineOpenToken(int property, InlineStyle style, FormatCodeProjectionOptions options) => property switch
    {
        0 => "[Bold ON]",
        1 => "[Italic ON]",
        2 => "[Underline ON]",
        3 => "[Strike ON]",
        4 => $"[Font \"{EscapeQuoted(style.FontFamily!, options)}\"]",
        5 => $"[Size {FormatNumber(style.FontSize!.Value)}]",
        6 => $"[Text Color {FormatColor(style.Foreground)}]",
        7 => $"[Highlight {FormatColor(style.Background)}]",
        8 => $"[Link \"{EscapeQuoted(NormalizeLink(style.LinkHref)!, options)}\"]",
        9 => style.Capitalization == TextCapitalization.SmallCaps ? "[Small Caps ON]" : "[All Caps ON]",
        _ => throw new ArgumentOutOfRangeException(nameof(property)),
    };

    private static string? NormalizeLink(string? link) => string.IsNullOrEmpty(link) ? null : link;

    private static string FormatColor(BColor color) => string.Create(
        CultureInfo.InvariantCulture,
        $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}");

    private static string FormatNumber(float value)
    {
        if (float.IsNaN(value))
            return "NAN";
        if (float.IsPositiveInfinity(value))
            return "POSITIVE_INFINITY";
        if (float.IsNegativeInfinity(value))
            return "NEGATIVE_INFINITY";
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string EscapeQuoted(string value, FormatCodeProjectionOptions options)
    {
        if (value.Length > options.MaxQuotedValueCharacters)
        {
            throw new FormatCodeProjectionLimitException(
                $"Quoted value length {value.Length} exceeds limit {options.MaxQuotedValueCharacters}.");
        }

        var result = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (character is '\\' or '[' or ']' or '"')
            {
                result.Append('\\').Append(character);
            }
            else if (char.IsHighSurrogate(character) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                result.Append(character).Append(value[++i]);
            }
            else if (char.IsSurrogate(character) || RequiresQuotedEscape(character))
            {
                result.Append("\\u{")
                    .Append(((int)character).ToString("X4", CultureInfo.InvariantCulture))
                    .Append('}');
            }
            else
            {
                result.Append(character);
            }
        }

        return result.ToString();
    }

    private static bool RequiresQuotedEscape(char character)
    {
        UnicodeCategory category = char.GetUnicodeCategory(character);
        return category is UnicodeCategory.Control or UnicodeCategory.Format or
            UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
    }

    private static void ValidateOptions(FormatCodeProjectionOptions options)
    {
        if (options.MaxOutputCharacters <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxOutputCharacters must be positive.");
        if (options.MaxTokens <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxTokens must be positive.");
        if (options.MaxQuotedValueCharacters <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxQuotedValueCharacters must be positive.");
    }

    private sealed class ProjectionBuilder
    {
        private readonly RichTextDocument _document;
        private readonly StringBuilder _text = new();
        private readonly List<FormatCodeToken> _tokens = new();
        private readonly List<FormatCodeDiagnostic> _diagnostics = new();

        public ProjectionBuilder(
            RichTextDocument document,
            FormatCodeProjectionOptions options,
            CancellationToken cancellationToken)
        {
            _document = document;
            Options = options;
            CancellationToken = cancellationToken;
        }

        public FormatCodeProjectionOptions Options { get; }

        public CancellationToken CancellationToken { get; }

        public void AddToken(
            FormatCodeTokenKind kind,
            string displayText,
            RichTextPosition sourceBefore,
            RichTextPosition sourceAfter,
            RichTextRange? affectedRange,
            FormatCodeMappingMode mappingMode,
            FormatCodeTokenEditDescriptor? editDescriptor = null)
        {
            CancellationToken.ThrowIfCancellationRequested();
            if (_tokens.Count >= Options.MaxTokens)
                throw new FormatCodeProjectionLimitException($"Token count exceeds limit {Options.MaxTokens}.");
            if (displayText.Length > Options.MaxOutputCharacters - _text.Length)
            {
                throw new FormatCodeProjectionLimitException(
                    $"Projected text exceeds limit {Options.MaxOutputCharacters} UTF-16 characters.");
            }

            int start = _text.Length;
            _text.Append(displayText);
            FormatCodeEditCapabilities capabilities =
                FormatCodeEditCapabilities.Navigate | FormatCodeEditCapabilities.Copy;
            if (kind is FormatCodeTokenKind.Text or FormatCodeTokenKind.Escape)
                capabilities |= FormatCodeEditCapabilities.EditText;
            if (editDescriptor is not null)
                capabilities |= FormatCodeEditCapabilities.ChangeFormatting | FormatCodeEditCapabilities.Remove;
            _tokens.Add(new FormatCodeToken(
                kind,
                displayText,
                start,
                displayText.Length,
                sourceBefore,
                sourceAfter,
                affectedRange,
                capabilities,
                editDescriptor,
                mappingMode));
        }

        public void AddDiagnostic(string code, string message, RichTextRange affectedRange) =>
            _diagnostics.Add(new FormatCodeDiagnostic(
                code,
                FormatCodeDiagnosticSeverity.Warning,
                message,
                affectedRange));

        public FormatCodeProjection Build(IReadOnlyList<FormatCodeToken> pendingTokens) =>
            new(_document, _text.ToString(), _tokens, pendingTokens, _diagnostics);
    }
}
