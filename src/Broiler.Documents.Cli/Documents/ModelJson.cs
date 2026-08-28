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
        ["listKind"] = style.ListKind.ToString().ToLowerInvariant(),
        ["indentLevel"] = style.IndentLevel,
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
            ["capitalization"] = style.Capitalization.ToString().ToLowerInvariant(),
            ["foreground"] = ColorText.Format(style.Foreground),
            ["background"] = ColorText.Format(style.Background),
            ["link"] = style.LinkHref,
        };

        entry["image"] = style.Image is null ? null : Describe(style.Image);
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
