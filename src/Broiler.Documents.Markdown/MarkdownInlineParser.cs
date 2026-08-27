using System;
using System.Collections.Generic;
using System.Text;
using Broiler.Documents.Model;

namespace Broiler.Documents.Markdown;

internal static class MarkdownInlineParser
{
    public static IReadOnlyList<MarkdownSegment> Parse(
        string text,
        InlineStyle baseStyle,
        ICollection<DocumentDiagnostic> diagnostics)
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

            if (text[i] == '[' && TryParseLink(text, i, out string label, out string href, out int consumed))
            {
                Flush(segments, plain, style);
                InlineStyle linkStyle = style;
                if (IsAllowedLink(href))
                    linkStyle = style with { LinkHref = href };
                else
                    diagnostics.Add(DocumentDiagnostic.Warning("markdown.link", "A hyperlink with a disallowed scheme was dropped."));

                foreach (MarkdownSegment segment in Parse(label, linkStyle, diagnostics))
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

    private static bool TryParseLink(
        string text,
        int start,
        out string label,
        out string href,
        out int consumed)
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
        return label.Length > 0 && href.Length > 0;
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

    private static bool IsAllowedLink(string href)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out Uri? uri))
            return false;

        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase);
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
