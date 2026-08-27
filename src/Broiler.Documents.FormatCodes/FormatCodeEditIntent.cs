using System;
using Broiler.Documents.Model;
using Broiler.Graphics;

namespace Broiler.Documents.FormatCodes;

/// <summary>
/// A typed, model-level edit requested by the Formatting Codes UI. Canonical
/// bracket text is never parsed to create an intent.
/// </summary>
public abstract record FormatCodeEditIntent(RichTextRange Range);

/// <summary>Replaces ordinary document content represented by text tokens.</summary>
public sealed record ReplaceFormatCodeTextIntent(
    RichTextRange Range,
    string Text) : FormatCodeEditIntent(Range);

/// <summary>Applies an exact inline delta to an explicit source range.</summary>
public sealed record ApplyFormatCodeInlineIntent(
    RichTextRange Range,
    InlineStyleDelta Delta) : FormatCodeEditIntent(Range);

/// <summary>Applies an exact paragraph delta to an explicit source range.</summary>
public sealed record ApplyFormatCodeParagraphIntent(
    RichTextRange Range,
    ParagraphStyleDelta Delta) : FormatCodeEditIntent(Range);

/// <summary>The model property represented by an editable code token.</summary>
public enum FormatCodeProperty
{
    None = 0,
    Bold,
    Italic,
    Underline,
    Strikethrough,
    FontFamily,
    FontSize,
    Foreground,
    Background,
    Link,

    // Inline properties are projected by their offset from Bold, so a new one
    // belongs at the end of that contiguous run — before the paragraph
    // properties, which start at Alignment.
    Capitalization,
    Alignment,
    ListKind,
    IndentLevel,
    LineSpacing,
    SpacingBefore,
    SpacingAfter,
    Tab,
    LineBreak,
    ParagraphBreak,

    // The single character an embedded image occupies. Like the other structure
    // properties it is removed by deleting that character.
    Image,
}

/// <summary>
/// Typed editing metadata attached to a projected token. The removal intent is
/// the deliberate semantic action used by Backspace/Delete; it is not inferred
/// from the token's display spelling.
/// </summary>
public sealed record FormatCodeTokenEditDescriptor(
    FormatCodeProperty Property,
    FormatCodeEditIntent RemovalIntent);

/// <summary>Stable entries hosts may present in an Insert Code palette.</summary>
public enum FormatCodePaletteEntry
{
    Bold = 0,
    Italic,
    Underline,
    Strikethrough,
    FontFamily,
    FontSize,
    Foreground,
    Background,
    Link,
    AllCaps,
    SmallCaps,
    AlignLeft,
    AlignCenter,
    AlignRight,
    BulletList,
    NumberedList,
    Indent,
    LineSpacing,
    SpacingBefore,
    SpacingAfter,
    Tab,
    LineBreak,
    ParagraphBreak,
}

/// <summary>Validation limits for structured pane edits.</summary>
public sealed record FormatCodeEditLimits
{
    public static FormatCodeEditLimits Default { get; } = new();

    public int MaxInsertedCharacters { get; init; } = 1 << 20;

    public int MaxDocumentCharacters { get; init; } = 16 << 20;

    public int MaxParagraphs { get; init; } = 1 << 20;

    public int MaxFontFamilyCharacters { get; init; } = 256;

    public int MaxLinkCharacters { get; init; } = 2048;
}

/// <summary>Privacy-safe result from validating an edit intent.</summary>
public readonly record struct FormatCodeEditValidationResult(bool IsValid, string ErrorCode)
{
    public static FormatCodeEditValidationResult Valid => new(true, string.Empty);

    public static FormatCodeEditValidationResult Invalid(string errorCode) => new(false, errorCode);
}

/// <summary>Validates all untrusted values before they reach RichEdit.</summary>
public static class FormatCodeEditValidator
{
    public static FormatCodeEditValidationResult Validate(
        RichTextDocument document,
        FormatCodeEditIntent intent,
        FormatCodeEditLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(intent);
        limits ??= FormatCodeEditLimits.Default;

        if (!document.IsValid(intent.Range.Anchor) || !document.IsValid(intent.Range.Focus))
            return FormatCodeEditValidationResult.Invalid("FCEDIT001");

        return intent switch
        {
            ReplaceFormatCodeTextIntent replace => ValidateReplacement(document, replace, limits),
            ApplyFormatCodeInlineIntent inline => ValidateInline(inline.Delta, limits),
            ApplyFormatCodeParagraphIntent paragraph => ValidateParagraph(paragraph.Delta),
            _ => FormatCodeEditValidationResult.Invalid("FCEDIT002"),
        };
    }

    private static FormatCodeEditValidationResult ValidateReplacement(
        RichTextDocument document,
        ReplaceFormatCodeTextIntent intent,
        FormatCodeEditLimits limits)
    {
        if (intent.Text is null || intent.Text.Length > limits.MaxInsertedCharacters)
            return FormatCodeEditValidationResult.Invalid("FCEDIT010");
        if (ContainsUnpairedSurrogate(intent.Text))
            return FormatCodeEditValidationResult.Invalid("FCEDIT011");

        long currentCharacters = CharacterCount(document);
        long removedCharacters = FlatLength(document, intent.Range);
        if (currentCharacters - removedCharacters + intent.Text.Length > limits.MaxDocumentCharacters)
            return FormatCodeEditValidationResult.Invalid("FCEDIT012");

        int insertedBreaks = CountLineBreaks(intent.Text);
        int removedBreaks = Math.Abs(intent.Range.End.ParagraphIndex - intent.Range.Start.ParagraphIndex);
        if ((long)document.ParagraphCount - removedBreaks + insertedBreaks > limits.MaxParagraphs)
            return FormatCodeEditValidationResult.Invalid("FCEDIT013");

        return FormatCodeEditValidationResult.Valid;
    }

    private static FormatCodeEditValidationResult ValidateInline(
        InlineStyleDelta delta,
        FormatCodeEditLimits limits)
    {
        if (delta.SetFontFamily && delta.FontFamily is string family &&
            (family.Length > limits.MaxFontFamilyCharacters || ContainsControl(family)))
        {
            return FormatCodeEditValidationResult.Invalid("FCEDIT020");
        }

        if (delta.SetFontSize && delta.FontSize is float size &&
            (!float.IsFinite(size) || size < 1f || size > 512f))
        {
            return FormatCodeEditValidationResult.Invalid("FCEDIT021");
        }

        if (delta.SetLink && delta.LinkHref is string href)
        {
            if (href.Length > limits.MaxLinkCharacters || ContainsControl(href) || !IsAllowedLink(href))
                return FormatCodeEditValidationResult.Invalid("FCEDIT022");
        }

        return FormatCodeEditValidationResult.Valid;
    }

    private static FormatCodeEditValidationResult ValidateParagraph(ParagraphStyleDelta delta)
    {
        if (delta.Alignment is TextAlignment alignment &&
            alignment is not TextAlignment.Left and not TextAlignment.Center and not TextAlignment.Right)
        {
            return FormatCodeEditValidationResult.Invalid("FCEDIT034");
        }
        if (delta.ListKind is ListKind list &&
            list is not ListKind.None and not ListKind.Bullet and not ListKind.Numbered)
        {
            return FormatCodeEditValidationResult.Invalid("FCEDIT035");
        }
        if (delta.IndentLevel is int indent && (indent < 0 || indent > 100))
            return FormatCodeEditValidationResult.Invalid("FCEDIT030");
        if (delta.LineSpacing is float line && (!float.IsFinite(line) || line <= 0 || line > 100))
            return FormatCodeEditValidationResult.Invalid("FCEDIT031");
        if (delta.SpacingBefore is float before && (!float.IsFinite(before) || before < 0 || before > 10000))
            return FormatCodeEditValidationResult.Invalid("FCEDIT032");
        if (delta.SpacingAfter is float after && (!float.IsFinite(after) || after < 0 || after > 10000))
            return FormatCodeEditValidationResult.Invalid("FCEDIT033");
        return FormatCodeEditValidationResult.Valid;
    }

    private static bool IsAllowedLink(string href)
    {
        if (href.Length == 0)
            return true;
        return Uri.TryCreate(href, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsControl(string value)
    {
        foreach (char character in value)
        {
            if (char.IsControl(character))
                return true;
        }
        return false;
    }

    private static bool ContainsUnpairedSurrogate(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[++i]))
                    return true;
            }
            else if (char.IsLowSurrogate(value[i]))
            {
                return true;
            }
        }
        return false;
    }

    private static int CountLineBreaks(string value)
    {
        int count = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\r')
            {
                count++;
                if (i + 1 < value.Length && value[i + 1] == '\n')
                    i++;
            }
            else if (value[i] == '\n')
            {
                count++;
            }
        }
        return count;
    }

    private static long FlatLength(RichTextDocument document, RichTextRange range)
    {
        RichTextPosition start = range.Start;
        RichTextPosition end = range.End;
        if (start.ParagraphIndex == end.ParagraphIndex)
            return end.Offset - start.Offset;

        long length = document.Paragraphs[start.ParagraphIndex].Length - start.Offset;
        for (int i = start.ParagraphIndex + 1; i < end.ParagraphIndex; i++)
            length += document.Paragraphs[i].Length + 1L;
        return length + 1L + end.Offset;
    }

    private static long CharacterCount(RichTextDocument document)
    {
        long length = Math.Max(0, document.ParagraphCount - 1);
        foreach (RichTextParagraph paragraph in document.Paragraphs)
            length += paragraph.Length;
        return length;
    }
}

/// <summary>Creates typed intents for the host's Insert Code palette.</summary>
public static class FormatCodeInsertPalette
{
    public static FormatCodeEditIntent Create(
        FormatCodePaletteEntry entry,
        RichTextRange range,
        object? value = null) => entry switch
    {
        FormatCodePaletteEntry.Bold =>
            new ApplyFormatCodeInlineIntent(range, InlineStyleDelta.ToggleBold(true)),
        FormatCodePaletteEntry.Italic =>
            new ApplyFormatCodeInlineIntent(range, InlineStyleDelta.ToggleItalic(true)),
        FormatCodePaletteEntry.Underline =>
            new ApplyFormatCodeInlineIntent(range, InlineStyleDelta.ToggleUnderline(true)),
        FormatCodePaletteEntry.Strikethrough =>
            new ApplyFormatCodeInlineIntent(range, InlineStyleDelta.ToggleStrikethrough(true)),
        FormatCodePaletteEntry.FontFamily when value is string family =>
            new ApplyFormatCodeInlineIntent(range, InlineStyleDelta.WithFontFamily(family.Trim())),
        FormatCodePaletteEntry.FontSize when TryFloat(value, out float size) =>
            new ApplyFormatCodeInlineIntent(range, InlineStyleDelta.WithFontSize(size)),
        FormatCodePaletteEntry.Foreground when value is BColor foreground =>
            new ApplyFormatCodeInlineIntent(range, InlineStyleDelta.WithForeground(foreground)),
        FormatCodePaletteEntry.Background when value is BColor background =>
            new ApplyFormatCodeInlineIntent(range, InlineStyleDelta.WithBackground(background)),
        FormatCodePaletteEntry.Link when value is string href =>
            new ApplyFormatCodeInlineIntent(range, InlineStyleDelta.WithLink(href.Trim())),
        FormatCodePaletteEntry.AllCaps =>
            new ApplyFormatCodeInlineIntent(range, InlineStyleDelta.WithCapitalization(TextCapitalization.AllCaps)),
        FormatCodePaletteEntry.SmallCaps =>
            new ApplyFormatCodeInlineIntent(range, InlineStyleDelta.WithCapitalization(TextCapitalization.SmallCaps)),
        FormatCodePaletteEntry.AlignLeft =>
            new ApplyFormatCodeParagraphIntent(range, ParagraphStyleDelta.WithAlignment(TextAlignment.Left)),
        FormatCodePaletteEntry.AlignCenter =>
            new ApplyFormatCodeParagraphIntent(range, ParagraphStyleDelta.WithAlignment(TextAlignment.Center)),
        FormatCodePaletteEntry.AlignRight =>
            new ApplyFormatCodeParagraphIntent(range, ParagraphStyleDelta.WithAlignment(TextAlignment.Right)),
        FormatCodePaletteEntry.BulletList =>
            new ApplyFormatCodeParagraphIntent(range, ParagraphStyleDelta.WithListKind(ListKind.Bullet)),
        FormatCodePaletteEntry.NumberedList =>
            new ApplyFormatCodeParagraphIntent(range, ParagraphStyleDelta.WithListKind(ListKind.Numbered)),
        FormatCodePaletteEntry.Indent when TryInt(value, out int indent) =>
            new ApplyFormatCodeParagraphIntent(range, new ParagraphStyleDelta { IndentLevel = indent }),
        FormatCodePaletteEntry.LineSpacing when TryFloat(value, out float line) =>
            new ApplyFormatCodeParagraphIntent(range, new ParagraphStyleDelta { LineSpacing = line }),
        FormatCodePaletteEntry.SpacingBefore when TryFloat(value, out float before) =>
            new ApplyFormatCodeParagraphIntent(range, new ParagraphStyleDelta { SpacingBefore = before }),
        FormatCodePaletteEntry.SpacingAfter when TryFloat(value, out float after) =>
            new ApplyFormatCodeParagraphIntent(range, new ParagraphStyleDelta { SpacingAfter = after }),
        FormatCodePaletteEntry.Tab => new ReplaceFormatCodeTextIntent(range, "\t"),
        FormatCodePaletteEntry.LineBreak => new ReplaceFormatCodeTextIntent(range, "\u2028"),
        FormatCodePaletteEntry.ParagraphBreak => new ReplaceFormatCodeTextIntent(range, "\n"),
        _ => throw new ArgumentException("The palette entry requires a typed value.", nameof(value)),
    };

    private static bool TryFloat(object? value, out float result)
    {
        result = value switch
        {
            float single => single,
            double dbl when dbl >= float.MinValue && dbl <= float.MaxValue => (float)dbl,
            int integer => integer,
            _ => float.NaN,
        };
        return !float.IsNaN(result);
    }

    private static bool TryInt(object? value, out int result)
    {
        if (value is int integer)
        {
            result = integer;
            return true;
        }
        result = 0;
        return false;
    }
}
