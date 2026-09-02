using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Dom;
using Broiler.Dom.Html;

namespace Broiler.Documents.Html;

/// <summary>Serializes the rich-text document model to deterministic UTF-8 HTML.</summary>
public static class HtmlWriter
{
    /// <summary>
    /// What a paragraph holding a tab declares so the tab survives HTML's
    /// whitespace collapsing. <see cref="HtmlReader"/> reads the same declaration
    /// back, so the tab returns as a tab rather than as the space a browser would
    /// otherwise show.
    /// </summary>
    internal const string PreserveWhitespaceDeclaration = "white-space: pre-wrap";

    public static DocumentWriteResult Write(
        RichTextDocument document,
        Stream destination,
        DocumentWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        DocumentConversionContext resources = (options ?? DocumentWriteOptions.Default).Resources;

        var diagnostics = new List<DocumentDiagnostic>();
        if (document.Tables.Count > 0)
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                "html.table.flattened",
                "A table was written as its cell paragraphs, in row order; " +
                "this writer emits no table markup."));
        }

        DomDocument dom = BuildDom(document, resources, diagnostics);
        string html = HtmlSerializer.Serialize(
            dom.DocumentElement!,
            new HtmlSerializationOptions(IncludeHtmlDoctype: true));

        byte[] bytes = Encoding.UTF8.GetBytes(html);
        destination.Write(bytes, 0, bytes.Length);
        return new DocumentWriteResult(bytes.Length, diagnostics, DocumentWriteResult.StatusFrom(diagnostics));
    }

    public static byte[] WriteToArray(RichTextDocument document, DocumentWriteOptions? options = null)
    {
        using var stream = new MemoryStream();
        Write(document, stream, options);
        return stream.ToArray();
    }

    private static DomDocument BuildDom(
        RichTextDocument rich,
        DocumentConversionContext resources,
        List<DocumentDiagnostic> diagnostics)
    {
        var document = new DomDocument();
        DomElement html = document.CreateElement("html");
        DomElement head = document.CreateElement("head");
        DomElement meta = document.CreateElement("meta");
        DomElement body = document.CreateElement("body");

        meta.SetAttribute("charset", "utf-8");
        document.AppendChild(html);
        html.AppendChild(head);
        html.AppendChild(body);
        head.AppendChild(meta);

        foreach (RichTextParagraph paragraph in rich.Paragraphs)
        {
            DomElement p = document.CreateElement("p");
            string paragraphStyle = FormatParagraphStyle(paragraph.Style, diagnostics);

            // HTML collapses a tab to a space unless the paragraph asks it not to,
            // so a paragraph that holds one carries the declaration that keeps it.
            // pre-wrap rather than pre: the paragraph must still wrap at the window.
            if (paragraph.Text.Contains('\t', StringComparison.Ordinal))
            {
                paragraphStyle = paragraphStyle.Length > 0
                    ? paragraphStyle + "; " + PreserveWhitespaceDeclaration
                    : PreserveWhitespaceDeclaration;
            }

            if (paragraphStyle.Length > 0)
                p.SetAttribute("style", paragraphStyle);

            body.AppendChild(p);
            AppendRuns(document, p, paragraph, resources, diagnostics);
        }

        return document;
    }

    private static void AppendRuns(
        DomDocument document,
        DomNode parent,
        RichTextParagraph paragraph,
        DocumentConversionContext resources,
        List<DocumentDiagnostic> diagnostics)
    {
        int offset = 0;
        foreach (StyleRun run in paragraph.Runs)
        {
            string text = paragraph.Text.Substring(offset, run.Length);
            offset += run.Length;

            DomNode target = CreateRunContainer(document, run.Style);
            if (target is DomElement element)
            {
                AppendRunContent(document, element, text, run.Style, resources, diagnostics);
                parent.AppendChild(element);
            }
            else
            {
                AppendRunContent(document, parent, text, run.Style, resources, diagnostics);
            }
        }
    }

    /// <summary>
    /// Appends a run's characters, turning each image placeholder into an
    /// <c>img</c> whose source is a data URI. HTML has nowhere else to keep the
    /// bytes, and an external file beside the document would make a single-file
    /// export no longer single-file.
    /// </summary>
    private static void AppendRunContent(
        DomDocument document,
        DomNode parent,
        string text,
        InlineStyle style,
        DocumentConversionContext resources,
        List<DocumentDiagnostic> diagnostics)
    {
        if (style.Image is not InlineImage image)
        {
            AppendTextWithBreaks(document, parent, text);
            return;
        }

        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != InlineImage.Placeholder)
                continue;

            if (i > start)
                AppendTextWithBreaks(document, parent, text[start..i]);

            DomElement? img = CreateImageElement(document, image, resources, diagnostics);
            if (img is not null)
                parent.AppendChild(img);
            start = i + 1;
        }

        if (start < text.Length)
            AppendTextWithBreaks(document, parent, text[start..]);
    }

    private static DomElement? CreateImageElement(
        DomDocument document,
        InlineImage image,
        DocumentConversionContext resources,
        List<DocumentDiagnostic> diagnostics)
    {
        if (!DocumentResourceGate.TryTakeEncodedBytes(
                image,
                resources,
                DocumentResourceOperations.ByteTransfer,
                out ReadOnlyMemory<byte> data,
                out string? contentType,
                out string? denial))
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                "html.image.omitted",
                "An image was left out of the HTML output because " + denial + "."));
            return null;
        }

        DomElement img = document.CreateElement("img");
        img.SetAttribute("src", "data:" + contentType + ";base64," + Convert.ToBase64String(data.Span));
        img.SetAttribute("alt", image.AltText);
        if (image.TryGetDisplaySize(out double width, out double height))
        {
            img.SetAttribute(
                "style",
                "width: " + FormatLength(width) + "; height: " + FormatLength(height));
        }

        diagnostics.Add(DocumentDiagnostic.Info(
            "html.image.datauri",
            "An embedded image was written as a base64 data URI."));
        return img;
    }

    private static string FormatLength(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture) + "pt";

    private static DomNode CreateRunContainer(DomDocument document, InlineStyle style)
    {
        string css = FormatInlineStyle(style);
        if (!string.IsNullOrEmpty(style.LinkHref))
        {
            DomElement link = document.CreateElement("a");
            link.SetAttribute("href", style.LinkHref);
            if (css.Length > 0)
                link.SetAttribute("style", css);
            return link;
        }

        if (css.Length == 0)
            return document.CreateDocumentFragment();

        DomElement span = document.CreateElement("span");
        span.SetAttribute("style", css);
        return span;
    }

    private static void AppendTextWithBreaks(DomDocument document, DomNode parent, string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != (char)0x2028)
                continue;

            if (i > start)
                parent.AppendChild(document.CreateTextNode(text[start..i]));
            parent.AppendChild(document.CreateElement("br"));
            start = i + 1;
        }

        if (start < text.Length)
            parent.AppendChild(document.CreateTextNode(text[start..]));
    }

    private static string FormatInlineStyle(InlineStyle style)
    {
        var declarations = new List<string>();
        if (style.Bold)
            declarations.Add("font-weight: bold");
        if (style.Italic)
            declarations.Add("font-style: italic");

        string decoration = FormatTextDecoration(style);
        if (decoration.Length > 0)
            declarations.Add("text-decoration: " + decoration);

        if (style.Capitalization == TextCapitalization.AllCaps)
            declarations.Add("text-transform: uppercase");
        else if (style.Capitalization == TextCapitalization.SmallCaps)
            declarations.Add("font-variant: small-caps");

        if (style.FontFamily is not null)
            declarations.Add("font-family: " + QuoteCssString(style.FontFamily));
        if (style.FontSize.HasValue)
            declarations.Add("font-size: " + HtmlCss.FormatPoints(style.FontSize.Value));
        if (!style.Foreground.IsEmpty)
            declarations.Add("color: " + HtmlCss.FormatColor(style.Foreground));
        if (!style.Background.IsEmpty)
            declarations.Add("background-color: " + HtmlCss.FormatColor(style.Background));

        return string.Join("; ", declarations);
    }

    private static string FormatParagraphStyle(ParagraphStyle style, List<DocumentDiagnostic> diagnostics)
    {
        var declarations = new List<string>();
        if (style.Alignment == TextAlignment.Center)
            declarations.Add("text-align: center");
        else if (style.Alignment == TextAlignment.Right)
            declarations.Add("text-align: right");
        else if (style.Alignment == TextAlignment.Justify)
            declarations.Add("text-align: justify");

        if (Math.Abs(style.LineSpacing - 1f) > 0.001f)
            declarations.Add("line-height: " + style.LineSpacing.ToString("0.###", CultureInfo.InvariantCulture));
        if (style.IndentLevel > 0)
            declarations.Add("margin-left: " + HtmlCss.FormatPoints(style.IndentLevel * 18f));
        if (Math.Abs(style.SpacingBefore) > 0.001f)
            declarations.Add("margin-top: " + HtmlCss.FormatPoints(style.SpacingBefore));
        if (Math.Abs(style.SpacingAfter) > 0.001f)
            declarations.Add("margin-bottom: " + HtmlCss.FormatPoints(style.SpacingAfter));

        if (style.ListKind != ListKind.None)
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                "html.list",
                "ListKind is not written by the first HTML codec subset; indentation is preserved."));
        }

        return string.Join("; ", declarations);
    }

    private static string FormatTextDecoration(InlineStyle style)
    {
        if (style.Underline && style.Strikethrough)
            return "underline line-through";
        if (style.Underline)
            return "underline";
        return style.Strikethrough ? "line-through" : string.Empty;
    }

    private static string QuoteCssString(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
