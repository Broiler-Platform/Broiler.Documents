using System;
using System.Text.Json.Nodes;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.Model;

namespace Broiler.Documents.Cli.Documents;

/// <summary>
/// A complete, deterministic JSON view of the document model.
/// </summary>
/// <remarks>
/// <para>
/// This is the structural counterpart to a rendered image: where a PNG diff says
/// two exports look different, this says exactly which paragraph, which run, and
/// which property they disagree on. Reaching for it first is usually the faster
/// route to a gap, and it is immune to the font and layout differences that make
/// a pixel comparison noisy.
/// </para>
/// <para>
/// It is deliberately lossless with respect to the model rather than pretty:
/// property order is fixed, every run is emitted with its resolved style, and
/// nothing is omitted for being at its default. Two dumps of equal documents are
/// byte-identical, which is what makes <c>diff</c> on the output meaningful.
/// </para>
/// <para>
/// Image bytes are summarized, not embedded. A base64 payload would dominate the
/// diff of any document containing a picture and tell the reader nothing they
/// could act on; the content type, declared size, and byte length identify the
/// image, and <c>render</c> is where the pixels get looked at.
/// </para>
/// </remarks>
public static class ModelJson
{
    public static JsonObject Describe(RichTextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var paragraphs = new JsonArray();
        for (int i = 0; i < document.ParagraphCount; i++)
            paragraphs.Add(Describe(document.Paragraphs[i], i));

        return new JsonObject
        {
            ["paragraphCount"] = document.ParagraphCount,
            ["paragraphs"] = paragraphs,
        };
    }

    public static JsonObject Describe(RichTextParagraph paragraph, int index)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        var runs = new JsonArray();
        int offset = 0;
        foreach (StyleRun run in paragraph.Runs)
        {
            runs.Add(Describe(run, offset, paragraph.Text));
            offset += run.Length;
        }

        return new JsonObject
        {
            ["index"] = index,
            ["length"] = paragraph.Length,
            ["text"] = paragraph.Text,
            ["style"] = Describe(paragraph.Style),
            ["runs"] = runs,
        };
    }

    public static JsonObject Describe(ParagraphStyle style) => new()
    {
        ["alignment"] = style.Alignment.ToString().ToLowerInvariant(),
        ["lineSpacing"] = style.LineSpacing,
        ["lineHeightRule"] = style.LineHeightRule switch
        {
            LineHeightRule.AtLeast => "atLeast",
            LineHeightRule.Exact => "exact",
            _ => "proportional",
        },
        ["lineHeight"] = style.LineHeight,
        ["listKind"] = style.ListKind.ToString().ToLowerInvariant(),
        ["indentLevel"] = style.IndentLevel,
        ["leftIndent"] = style.LeftIndent,
        ["rightIndent"] = style.RightIndent,
        ["firstLineIndent"] = style.FirstLineIndent,
        ["keepWithNext"] = style.KeepWithNext,
        ["keepTogether"] = style.KeepTogether,
        ["orphanLines"] = style.OrphanLines,
        ["widowLines"] = style.WidowLines,
        ["spacingBefore"] = style.SpacingBefore,
        ["spacingAfter"] = style.SpacingAfter,
    };

    public static JsonObject Describe(StyleRun run, int offset, string paragraphText)
    {
        ArgumentNullException.ThrowIfNull(paragraphText);

        int length = Math.Min(run.Length, Math.Max(0, paragraphText.Length - offset));
        var entry = new JsonObject
        {
            ["start"] = offset,
            ["length"] = run.Length,
            ["text"] = paragraphText.Substring(Math.Min(offset, paragraphText.Length), length),
        };

        foreach (System.Collections.Generic.KeyValuePair<string, JsonNode?> property in Describe(run.Style))
            entry[property.Key] = property.Value?.DeepClone();

        return entry;
    }

    /// <summary>A tracked change: what it did, who made it, when, and whether it is one side of a move.</summary>
    public static JsonObject Describe(TextRevision revision) => new()
    {
        ["kind"] = revision.Kind.ToString().ToLowerInvariant(),
        ["author"] = revision.Author,
        ["date"] = revision.Date?.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
        ["moved"] = revision.Moved,
    };

    public static JsonObject Describe(InlineStyle style)
    {
        var entry = new JsonObject
        {
            ["fontFamily"] = style.FontFamily,
            ["fontSize"] = style.FontSize,
            ["bold"] = style.Bold,
            ["italic"] = style.Italic,
            ["underline"] = style.Underline,
            ["strikethrough"] = style.Strikethrough,
            ["hidden"] = style.Hidden,
            ["capitalization"] = style.Capitalization.ToString().ToLowerInvariant(),
            ["foreground"] = ColorText.Format(style.Foreground),
            ["background"] = ColorText.Format(style.Background),
            ["link"] = style.LinkHref,
        };

        entry["image"] = style.Image is null ? null : Describe(style.Image);

        // Only on text a tracked change marks, so every other run is described as it was.
        if (style.Revision is TextRevision revision)
            entry["revision"] = Describe(revision);

        // Only on a page field, so the description of every other run is as it was.
        if (style.PageField is PageField field)
        {
            entry["pageField"] = new JsonObject
            {
                ["kind"] = field.Kind == PageFieldKind.PageCount ? "pageCount" : "pageNumber",
                ["format"] = field.Format.ToString().ToLowerInvariant(),
                ["adjust"] = field.Adjust,
            };
        }

        // Only on a style reference and on text a character style formats, so
        // the description of every other run is as it was.
        if (style.StyleReference is StyleReferenceField reference)
        {
            entry["styleReference"] = new JsonObject
            {
                ["style"] = reference.StyleName,
                ["fromBottom"] = reference.FromBottom,
            };
        }

        if (style.CharacterStyle is string characterStyle)
            entry["characterStyle"] = characterStyle;

        // Only on a citation, so the description of every other run is as it was.
        if (style.Note is DocumentNote note)
        {
            var paragraphs = new JsonArray();
            foreach (RichTextParagraph paragraph in note.Paragraphs)
                paragraphs.Add(paragraph.Text);

            entry["note"] = new JsonObject
            {
                ["kind"] = note.Kind.ToString().ToLowerInvariant(),
                ["customMark"] = note.CustomMark,
                ["paragraphs"] = paragraphs,
            };
        }

        return entry;
    }

    public static JsonObject Describe(InlineImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        return new JsonObject
        {
            ["name"] = image.Name,
            ["contentType"] = image.ContentType,
            ["byteLength"] = image.Data.Length,
            ["widthPoints"] = image.Width,
            ["heightPoints"] = image.Height,
            ["hasExplicitSize"] = image.HasExplicitSize,
            ["altText"] = image.AltText,
        };
    }
}
