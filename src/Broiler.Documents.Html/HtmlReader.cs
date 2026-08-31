using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Dom;
using Broiler.Dom.Html;
using Broiler.Graphics;

namespace Broiler.Documents.Html;

internal static class HtmlReader
{
    private static readonly HashSet<string> ParagraphElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "li", "h1", "h2", "h3", "h4", "h5", "h6",
    };

    private static readonly HashSet<string> BlockContainers = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "blockquote", "body", "dd", "details",
        "dialog", "div", "dl", "dt", "fieldset", "figcaption", "figure",
        "footer", "form", "header", "main", "nav", "section", "table",
        "tbody", "td", "tfoot", "th", "thead", "tr",
    };

    private static readonly HashSet<string> SkippedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "base", "canvas", "embed", "head", "iframe", "img", "input", "link",
        "meta", "noscript", "object", "script", "style", "svg", "template",
        "title",
    };

    public static DocumentReadResult Read(byte[] bytes, DocumentReadOptions options, bool truncated)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<DocumentDiagnostic>();
        if (truncated)
        {
            diagnostics.Add(DocumentDiagnostic.Error(
                "html.limit.bytes",
                "HTML input exceeded MaxDocumentBytes and was truncated before parsing."));
        }

        string html = DecodeUtf8(bytes);
        HtmlDocumentParseResult parse;
        try
        {
            parse = HtmlDocumentParser.ParseDocument(html);
        }
        catch (Exception ex)
        {
            diagnostics.Add(DocumentDiagnostic.Error(
                "html.parse",
                $"HTML parser recovered by returning an empty document: {ex.GetType().Name}."));
            return new DocumentReadResult(RichTextDocument.Empty, diagnostics, DocumentResultStatus.Rejected);
        }

        foreach (HtmlParseDiagnostic diagnostic in parse.Diagnostics)
            diagnostics.Add(DocumentDiagnostic.Warning("html.parse", diagnostic.Message));

        var builder = new HtmlDocumentBuilder(options.Limits, diagnostics);
        DomNode root = parse.Document.Body is not null
            ? parse.Document.Body
            : parse.Document.DocumentElement is not null ? parse.Document.DocumentElement : parse.Document;
        ReadChildren(root, builder, InlineStyle.Default, ParagraphStyle.Default, preserveWhitespace: false);
        return new DocumentReadResult(builder.Build(), diagnostics, DocumentReadResult.StatusFrom(diagnostics));
    }

    private static void ReadChildren(
        DomNode parent,
        HtmlDocumentBuilder builder,
        InlineStyle inlineStyle,
        ParagraphStyle paragraphStyle,
        bool preserveWhitespace)
    {
        foreach (DomNode child in parent.ChildNodes)
            ReadNode(child, builder, inlineStyle, paragraphStyle, preserveWhitespace);
    }

    private static void ReadNode(
        DomNode node,
        HtmlDocumentBuilder builder,
        InlineStyle inlineStyle,
        ParagraphStyle paragraphStyle,
        bool preserveWhitespace)
    {
        if (node is DomText text)
        {
            builder.AppendText(text.Data, inlineStyle, preserveWhitespace);
            return;
        }

        if (node is not DomElement element)
            return;

        string tag = element.LocalName;
        if (SkippedElements.Contains(tag))
        {
            if (tag is "img" or "object" or "embed" or "iframe")
                builder.AddDiagnosticOnce("html.skip.external", "External or embedded HTML content was skipped.");
            return;
        }

        if (tag.Equals("br", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendSoftBreak(inlineStyle);
            return;
        }

        InlineStyle childInline = ApplyInlineElement(element, inlineStyle, builder.Diagnostics);
        ParagraphStyle childParagraph = ApplyParagraphElement(element, paragraphStyle);
        bool childPreserveWhitespace =
            preserveWhitespace ||
            tag.Equals("pre", StringComparison.OrdinalIgnoreCase) ||
            PreservesWhitespace(element);

        if (tag.Equals("ul", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("ol", StringComparison.OrdinalIgnoreCase))
        {
            ParagraphStyle listStyle = childParagraph with
            {
                ListKind = tag.Equals("ol", StringComparison.OrdinalIgnoreCase) ? ListKind.Numbered : ListKind.Bullet,
                IndentLevel = Math.Max(1, childParagraph.IndentLevel + 1),
            };
            builder.FinishParagraph(force: false);
            ReadChildren(element, builder, childInline, listStyle, childPreserveWhitespace);
            builder.FinishParagraph(force: false);
            return;
        }

        if (ParagraphElements.Contains(tag))
        {
            if (tag.Equals("li", StringComparison.OrdinalIgnoreCase) && childParagraph.ListKind == ListKind.None)
                childParagraph = childParagraph with { ListKind = ListKind.Bullet, IndentLevel = Math.Max(1, childParagraph.IndentLevel) };

            if (tag.StartsWith("h", StringComparison.OrdinalIgnoreCase) && tag.Length == 2 && char.IsDigit(tag[1]))
                childInline = ApplyHeadingInline(tag, childInline);

            builder.StartParagraph(childParagraph);
            ReadChildren(element, builder, childInline, childParagraph, childPreserveWhitespace);
            builder.FinishParagraph(force: true);
            return;
        }

        if (BlockContainers.Contains(tag))
        {
            builder.FinishParagraph(force: false);
            ReadChildren(element, builder, childInline, childParagraph, childPreserveWhitespace);
            builder.FinishParagraph(force: false);
            return;
        }

        ReadChildren(element, builder, childInline, childParagraph, childPreserveWhitespace);
    }

    private static InlineStyle ApplyInlineElement(
        DomElement element,
        InlineStyle style,
        ICollection<DocumentDiagnostic> diagnostics)
    {
        string tag = element.LocalName;
        switch (tag)
        {
            case "b":
            case "strong":
                style = style with { Bold = true };
                break;
            case "i":
            case "em":
                style = style with { Italic = true };
                break;
            case "u":
                style = style with { Underline = true };
                break;
            case "s":
            case "strike":
            case "del":
                style = style with { Strikethrough = true };
                break;
            case "code":
            case "kbd":
            case "samp":
            case "tt":
                style = style with { FontFamily = "monospace" };
                break;
            case "a":
                style = ApplyLink(element, style, diagnostics);
                break;
            case "font":
                style = ApplyFontElement(element, style);
                break;
        }

        IReadOnlyDictionary<string, string> declarations = HtmlCss.ParseDeclarations(element.GetAttribute("style"));
        foreach (KeyValuePair<string, string> declaration in declarations)
            style = ApplyInlineCss(style, declaration.Key, declaration.Value);

        return style;
    }

    private static InlineStyle ApplyHeadingInline(string tag, InlineStyle style)
    {
        float size = tag switch
        {
            "h1" => 24f,
            "h2" => 20f,
            "h3" => 17f,
            "h4" => 15f,
            "h5" => 13f,
            _ => 12f,
        };

        return style with { Bold = true, FontSize = size };
    }

    private static InlineStyle ApplyLink(
        DomElement element,
        InlineStyle style,
        ICollection<DocumentDiagnostic> diagnostics)
    {
        string? href = WebUtility.HtmlDecode(element.GetAttribute("href"))?.Trim();
        if (string.IsNullOrEmpty(href))
            return style;

        if (IsAllowedLink(href))
            return style with { LinkHref = href };

        diagnostics.Add(DocumentDiagnostic.Warning(
            "html.link",
            "A hyperlink with a disallowed scheme was dropped."));
        return style;
    }

    private static bool IsAllowedLink(string href)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out Uri? uri))
            return false;

        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase);
    }

    private static InlineStyle ApplyFontElement(DomElement element, InlineStyle style)
    {
        string? face = HtmlCss.ParseFontFamily(WebUtility.HtmlDecode(element.GetAttribute("face")));
        if (face is not null)
            style = style with { FontFamily = face };

        if (HtmlCss.TryParseColor(WebUtility.HtmlDecode(element.GetAttribute("color")), out BColor color))
            style = style with { Foreground = color };

        return style;
    }

    private static InlineStyle ApplyInlineCss(InlineStyle style, string property, string value)
    {
        switch (property)
        {
            case "font-weight":
                if (value.Equals("normal", StringComparison.OrdinalIgnoreCase) || value == "400")
                    return style with { Bold = false };
                if (value.Equals("bold", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("bolder", StringComparison.OrdinalIgnoreCase) ||
                    (int.TryParse(value, out int weight) && weight >= 600))
                    return style with { Bold = true };
                break;
            case "font-style":
                if (value.Equals("normal", StringComparison.OrdinalIgnoreCase))
                    return style with { Italic = false };
                if (value.Equals("italic", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("oblique", StringComparison.OrdinalIgnoreCase))
                    return style with { Italic = true };
                break;
            case "text-decoration":
            case "text-decoration-line":
                return ApplyTextDecoration(style, value);
            case "color":
                if (HtmlCss.TryParseColor(value, out BColor foreground))
                    return style with { Foreground = foreground };
                break;
            case "background":
            case "background-color":
                if (HtmlCss.TryParseColor(value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out BColor background))
                    return style with { Background = background };
                break;
            case "text-transform":
                if (value.Equals("uppercase", StringComparison.OrdinalIgnoreCase))
                    return style with { Capitalization = TextCapitalization.AllCaps };
                if (value.Equals("none", StringComparison.OrdinalIgnoreCase) &&
                    style.Capitalization == TextCapitalization.AllCaps)
                {
                    return style with { Capitalization = TextCapitalization.None };
                }
                break;
            case "font-variant":
            case "font-variant-caps":
                if (value.Equals("small-caps", StringComparison.OrdinalIgnoreCase))
                    return style with { Capitalization = TextCapitalization.SmallCaps };
                if (value.Equals("normal", StringComparison.OrdinalIgnoreCase) &&
                    style.Capitalization == TextCapitalization.SmallCaps)
                {
                    return style with { Capitalization = TextCapitalization.None };
                }
                break;
            case "font-family":
                return style with { FontFamily = HtmlCss.ParseFontFamily(value) };
            case "font-size":
                if (HtmlCss.TryParseFontSize(value, out float fontSize))
                    return style with { FontSize = fontSize };
                break;
        }

        return style;
    }

    private static InlineStyle ApplyTextDecoration(InlineStyle style, string value)
    {
        string lower = value.ToLowerInvariant();
        if (lower.Contains("none", StringComparison.Ordinal))
            return style with { Underline = false, Strikethrough = false };

        if (lower.Contains("underline", StringComparison.Ordinal))
            style = style with { Underline = true };
        if (lower.Contains("line-through", StringComparison.Ordinal))
            style = style with { Strikethrough = true };
        return style;
    }

    /// <summary>
    /// Whether an element's own <c>white-space</c> declaration keeps the tabs and
    /// runs of spaces inside it, the way <c>pre</c> does. It is how a browser is
    /// told to show a tab as a tab, and so how a tab reaches this reader intact.
    /// <c>pre-line</c> is not one of them: it keeps line breaks and collapses
    /// everything else, tabs included.
    /// </summary>
    private static bool PreservesWhitespace(DomElement element)
    {
        IReadOnlyDictionary<string, string> declarations = HtmlCss.ParseDeclarations(element.GetAttribute("style"));
        return declarations.TryGetValue("white-space", out string? value) &&
               value.Trim().ToLowerInvariant() is "pre" or "pre-wrap" or "break-spaces";
    }

    private static ParagraphStyle ApplyParagraphElement(DomElement element, ParagraphStyle style)
    {
        string tag = element.LocalName;
        if (tag.Equals("blockquote", StringComparison.OrdinalIgnoreCase))
            style = style with { IndentLevel = style.IndentLevel + 1 };

        string? align = element.GetAttribute("align");
        if (!string.IsNullOrWhiteSpace(align))
            style = ApplyAlignment(style, align);

        IReadOnlyDictionary<string, string> declarations = HtmlCss.ParseDeclarations(element.GetAttribute("style"));
        foreach (KeyValuePair<string, string> declaration in declarations)
        {
            switch (declaration.Key)
            {
                case "text-align":
                    style = ApplyAlignment(style, declaration.Value);
                    break;
                case "line-height":
                    if (HtmlCss.TryParseLineSpacing(declaration.Value, out float lineSpacing))
                        style = style with { LineSpacing = lineSpacing };
                    break;
                case "margin-top":
                    if (HtmlCss.TryParsePoints(declaration.Value, out float before))
                        style = style with { SpacingBefore = before };
                    break;
                case "margin-bottom":
                    if (HtmlCss.TryParsePoints(declaration.Value, out float after))
                        style = style with { SpacingAfter = after };
                    break;
                case "margin-left":
                case "padding-left":
                    if (HtmlCss.TryParsePoints(declaration.Value, out float left))
                        style = style with { IndentLevel = Math.Max(style.IndentLevel, (int)Math.Round(left / 18f)) };
                    break;
            }
        }

        return style;
    }

    private static ParagraphStyle ApplyAlignment(ParagraphStyle style, string value)
    {
        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "center" => style with { Alignment = TextAlignment.Center },
            "right" or "end" => style with { Alignment = TextAlignment.Right },
            "justify" => style with { Alignment = TextAlignment.Justify },
            _ => style with { Alignment = TextAlignment.Left },
        };
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        string text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
            .GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    private sealed class HtmlDocumentBuilder
    {
        private readonly DocumentLimits _limits;
        private readonly List<RichTextParagraph> _paragraphs = [];
        private readonly List<Segment> _segments = [];
        private ParagraphStyle _paragraphStyle = ParagraphStyle.Default;
        private bool _paragraphOpen;
        private readonly HashSet<string> _diagnosticOnce = new(StringComparer.Ordinal);

        public HtmlDocumentBuilder(DocumentLimits limits, List<DocumentDiagnostic> diagnostics)
        {
            _limits = limits;
            Diagnostics = diagnostics;
        }

        public List<DocumentDiagnostic> Diagnostics { get; }

        public void AddDiagnosticOnce(string code, string message)
        {
            if (_diagnosticOnce.Add(code))
                Diagnostics.Add(DocumentDiagnostic.Warning(code, message));
        }

        public void StartParagraph(ParagraphStyle style)
        {
            if (_segments.Count > 0)
                FinishParagraph(force: false);

            _paragraphStyle = style;
            _paragraphOpen = true;
        }

        public void AppendText(string text, InlineStyle style, bool preserveWhitespace)
        {
            string normalized = NormalizeText(WebUtility.HtmlDecode(text) ?? string.Empty, preserveWhitespace);
            if (normalized.Length == 0)
                return;

            if (!_paragraphOpen && string.IsNullOrWhiteSpace(normalized))
                return;

            if (_segments.Count == 0)
                normalized = normalized.TrimStart();
            else if (EndsWithWhitespace(_segments[^1].Text) && normalized.Length > 0 && char.IsWhiteSpace(normalized[0]))
                normalized = normalized.TrimStart();

            if (normalized.Length == 0)
                return;

            if (normalized.Length > _limits.MaxRunLength)
            {
                normalized = normalized[.._limits.MaxRunLength];
                AddDiagnosticOnce("html.limit.run", "An HTML text run exceeded MaxRunLength and was truncated.");
            }

            EnsureParagraph();
            AddSegment(normalized, style);
        }

        public void AppendSoftBreak(InlineStyle style)
        {
            EnsureParagraph();
            AddSegment(((char)0x2028).ToString(), style);
        }

        public void FinishParagraph(bool force)
        {
            if (!_paragraphOpen && _segments.Count == 0)
                return;
            if (!force && _segments.Count == 0)
            {
                _paragraphOpen = false;
                _paragraphStyle = ParagraphStyle.Default;
                return;
            }

            if (_paragraphs.Count >= _limits.MaxParagraphCount)
            {
                AddDiagnosticOnce("html.limit.paragraphs", "HTML input exceeded MaxParagraphCount; remaining paragraphs were dropped.");
                _segments.Clear();
                _paragraphOpen = false;
                _paragraphStyle = ParagraphStyle.Default;
                return;
            }

            RichTextParagraph paragraph = RichTextParagraph.Empty.WithParagraphStyle(_paragraphStyle);
            int offset = 0;
            foreach (Segment segment in _segments)
            {
                paragraph = paragraph.InsertText(offset, segment.Text, segment.Style);
                offset += segment.Text.Length;
            }

            _paragraphs.Add(paragraph);
            _segments.Clear();
            _paragraphOpen = false;
            _paragraphStyle = ParagraphStyle.Default;
        }

        public RichTextDocument Build()
        {
            FinishParagraph(force: false);
            return _paragraphs.Count == 0
                ? RichTextDocument.Empty
                : RichTextDocument.FromParagraphs(_paragraphs);
        }

        private void EnsureParagraph()
        {
            if (!_paragraphOpen)
                StartParagraph(ParagraphStyle.Default);
        }

        private void AddSegment(string text, InlineStyle style)
        {
            if (_segments.Count > 0 && _segments[^1].Style.Equals(style))
            {
                Segment previous = _segments[^1];
                _segments[^1] = new Segment(previous.Text + text, style);
                return;
            }

            _segments.Add(new Segment(text, style));
        }

        private static bool EndsWithWhitespace(string text) =>
            text.Length > 0 && char.IsWhiteSpace(text[^1]);

        private static string NormalizeText(string text, bool preserveWhitespace)
        {
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            if (preserveWhitespace)
                return text;

            var builder = new StringBuilder(text.Length);
            bool inWhitespace = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!inWhitespace)
                        builder.Append(' ');
                    inWhitespace = true;
                }
                else
                {
                    builder.Append(c);
                    inWhitespace = false;
                }
            }

            return builder.ToString();
        }

        private readonly record struct Segment(string Text, InlineStyle Style);
    }
}
