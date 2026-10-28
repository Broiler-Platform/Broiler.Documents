using System;
using System.Collections.Generic;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Documents.Resources;
using Broiler.Graphics.Imaging;

namespace Broiler.Documents.Markdown;

internal static class MarkdownInlineParser
{
    public static IReadOnlyList<MarkdownSegment> Parse(string text, InlineStyle baseStyle, ICollection<DocumentDiagnostic> diagnostics) =>
        Parse(text, baseStyle, diagnostics, DocumentLimits.Default, new DocumentConversionContextBuilder(DocumentResourcePolicy.Default));

    public static IReadOnlyList<MarkdownSegment> Parse(
        string text,
        InlineStyle baseStyle,
        ICollection<DocumentDiagnostic> diagnostics,
        DocumentLimits limits,
        DocumentConversionContextBuilder resources,
        IReadOnlyDictionary<string, DocumentNote>? footnotes = null)
    {
        var segments = new List<MarkdownSegment>();
        var plain = new StringBuilder();
        InlineStyle style = baseStyle;

        for (int i = 0; i < text.Length;)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                plain.Append(text[i + 1]);
                i += 2;
                continue;
            }

            if (StartsWith(text, i, "**") || StartsWith(text, i, "__"))
            {
                Flush(segments, plain, style);
                style = style with { Bold = !style.Bold };
                i += 2;
                continue;
            }

            if (StartsWith(text, i, "~~"))
            {
                Flush(segments, plain, style);
                style = style with { Strikethrough = !style.Strikethrough };
                i += 2;
                continue;
            }

            if (text[i] == '*' || text[i] == '_')
            {
                Flush(segments, plain, style);
                style = style with { Italic = !style.Italic };
                i++;
                continue;
            }

            if (text[i] == '`')
            {
                int end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    Flush(segments, plain, style);
                    Add(segments, text[(i + 1)..end], style with { FontFamily = "monospace" });
                    i = end + 1;
                    continue;
                }
            }

            // CommonMark image syntax, recognized so that it can be dropped
            // deliberately. This reader builds no model image - images are
            // outside the subset, and the writer's own data URI is not something
            // it will read back - and the format nominates the description as
            // the fallback text, so that is what is kept.
            //
            // Without this branch the `!` was not special, so the `[...](...)`
            // after it went through the link path below: the marker was left
            // behind in the prose, the description came through as a label, and
            // the loss was reported as a dropped hyperlink, which it was not.
            if (text[i] == '!' && i + 1 < text.Length && text[i + 1] == '[' &&
                TryParseLink(text, i + 1, out string description, out string imageHref, out int imageConsumed,
                    labelMayBeEmpty: true))
            {
                Flush(segments, plain, style);

                if (imageHref.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!DataUriParser.TryParse(imageHref, out string mediaType, out ReadOnlyMemory<byte> data, out _))
                    {
                        diagnostics.Add(DocumentDiagnostic.Warning(
                            "markdown.image.malformed",
                            "An embedded Markdown image data URI was malformed and could not be decoded."));
                        foreach (MarkdownSegment segment in Parse(description, style, diagnostics, limits, resources))
                            Add(segments, segment.Text, segment.Style);
                    }
                    else if (data.Length > limits.MaxBinBytes)
                    {
                        diagnostics.Add(DocumentDiagnostic.Warning(
                            "markdown.image.limit",
                            "A Markdown embedded image exceeded MaxBinBytes and was skipped."));
                        foreach (MarkdownSegment segment in Parse(description, style, diagnostics, limits, resources))
                            Add(segments, segment.Text, segment.Style);
                    }
                    else
                    {
                        BImageResource? resource = null;
                        try
                        {
                            resource = BImageResource.FromEncoded(data, mediaType);
                        }
                        catch (Exception ex)
                        {
                            diagnostics.Add(DocumentDiagnostic.Warning(
                                "markdown.image.malformed",
                                $"An embedded image payload could not be loaded: {ex.Message}"));
                        }

                        if (resource is not null)
                        {
                            var request = new DocumentResourceRequest(
                                resource,
                                DocumentResourceProvenance.ReadFromSource,
                                DocumentResourceDisposition.Embedded,
                                name: description,
                                sourceFormat: "Markdown");

                            if (!resources.TryAdmit(request, DocumentResourceOperations.ExtractToModel, out DocumentResourceId id, out string? denial))
                            {
                                diagnostics.Add(DocumentDiagnostic.Warning(
                                    "markdown.image.denied",
                                    "A picture was not read into the document because " + denial + "."));
                                foreach (MarkdownSegment segment in Parse(description, style, diagnostics, limits, resources))
                                    Add(segments, segment.Text, segment.Style);
                            }
                            else
                            {
                                var image = new InlineImage(resource, id, width: null, height: null, altText: description, name: description);
                                Add(segments, InlineImage.PlaceholderText, style with { Image = image });
                            }
                        }
                        else
                        {
                            foreach (MarkdownSegment segment in Parse(description, style, diagnostics, limits, resources))
                                Add(segments, segment.Text, segment.Style);
                        }
                    }
                }
                else
                {
                    diagnostics.Add(DocumentDiagnostic.Warning(
                        "markdown.image.dropped",
                        "An image was replaced by its description text; this reader does not build images."));

                    foreach (MarkdownSegment segment in Parse(description, style, diagnostics, limits, resources))
                        Add(segments, segment.Text, segment.Style);
                }

                i += imageConsumed + 1;
                continue;
            }

            if (i + 2 < text.Length && text[i] == '[' && text[i + 1] == '^')
            {
                int close = text.IndexOf(']', i + 2);
                if (close > i + 2)
                {
                    string footnoteLabel = text.Substring(i + 2, close - (i + 2));
                    if (footnotes is not null && footnotes.TryGetValue(footnoteLabel, out DocumentNote? note))
                    {
                        Flush(segments, plain, style);
                        Add(segments, DocumentNote.PlaceholderText, style with { Note = note });
                        i = close + 1;
                        continue;
                    }
                }
            }

            if (text[i] == '[' && TryParseLink(text, i, out string label, out string href, out int consumed))
            {
                Flush(segments, plain, style);
                InlineStyle linkStyle = style;
                if (DocumentLinkTarget.IsAllowed(href))
                    linkStyle = style with { LinkHref = href };
                else
                    diagnostics.Add(DocumentDiagnostic.Warning("markdown.link", "A hyperlink with a disallowed or relative target was dropped."));

                foreach (MarkdownSegment segment in Parse(label, linkStyle, diagnostics, limits, resources))
                    Add(segments, segment.Text, segment.Style);
                i += consumed;
                continue;
            }

            plain.Append(text[i]);
            i++;
        }

        Flush(segments, plain, style);
        return segments;
    }

    /// <param name="labelMayBeEmpty">
    /// True for an image, whose description is optional - a decorative image is
    /// written <c>![](…)</c> and that is not a malformed one. A link keeps the
    /// requirement: one with no text has nothing to click.
    /// </param>
    private static bool TryParseLink(
        string text,
        int start,
        out string label,
        out string href,
        out int consumed,
        bool labelMayBeEmpty = false)
    {
        label = string.Empty;
        href = string.Empty;
        consumed = 0;

        int labelEnd = text.IndexOf(']', start + 1);
        if (labelEnd < 0 || labelEnd + 1 >= text.Length || text[labelEnd + 1] != '(')
            return false;

        int hrefStart = labelEnd + 2;
        int hrefEnd = FindLinkDestinationEnd(text, hrefStart);
        if (hrefEnd < 0)
            return false;

        label = text[(start + 1)..labelEnd];
        href = text[hrefStart..hrefEnd].Trim();
        consumed = hrefEnd - start + 1;
        return (labelMayBeEmpty || label.Length > 0) && href.Length > 0;
    }

    private static int FindLinkDestinationEnd(string text, int start)
    {
        int depth = 0;
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                i++;
                continue;
            }

            if (text[i] == '(')
            {
                depth++;
                continue;
            }

            if (text[i] == ')')
            {
                if (depth == 0)
                    return i;
                depth--;
            }
        }

        return -1;
    }

    private static bool StartsWith(string text, int index, string marker) =>
        index + marker.Length <= text.Length &&
        text.AsSpan(index, marker.Length).SequenceEqual(marker.AsSpan());

    private static void Flush(List<MarkdownSegment> segments, StringBuilder plain, InlineStyle style)
    {
        if (plain.Length == 0)
            return;

        Add(segments, plain.ToString(), style);
        plain.Clear();
    }

    private static void Add(List<MarkdownSegment> segments, string text, InlineStyle style)
    {
        if (text.Length == 0)
            return;

        if (segments.Count > 0 && segments[^1].Style.Equals(style))
        {
            MarkdownSegment previous = segments[^1];
            segments[^1] = new MarkdownSegment(previous.Text + text, style);
            return;
        }

        segments.Add(new MarkdownSegment(text, style));
    }
}
