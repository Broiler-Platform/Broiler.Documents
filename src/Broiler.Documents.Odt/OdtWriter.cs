using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Broiler.Documents.Model;
using Broiler.Graphics;

namespace Broiler.Documents.Odt;

/// <summary>Serializes the rich-text document model to a minimal ODT package.</summary>
public static class OdtWriter
{
    /// <summary>
    /// The timestamp every entry is stamped with. A package written twice from
    /// the same document must be byte-for-byte identical, and a clock reading
    /// would be the one thing that is not.
    /// </summary>
    private static readonly DateTimeOffset ZipTimestamp =
        new(new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    /// <summary>The size an image with no stated display size is written at: one inch square.</summary>
    private const double DefaultImagePoints = 72.0;

    /// <summary>The deepest list nesting a written package uses, which is what the list styles declare.</summary>
    private const int MaxListLevels = 10;

    public static DocumentWriteResult Write(
        RichTextDocument document,
        Stream destination,
        DocumentWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        _ = options;

        var context = new OdtWriteContext();

        // The body is built first because it is what discovers the automatic
        // styles, and ODF requires those to be declared ahead of it.
        XElement body = BuildBody(document, context);
        XDocument content = BuildContent(body, context);

        using var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            // ODF 1.3 part 2 section 3.3: the mimetype entry comes first and is
            // stored uncompressed, so a consumer can identify the package by
            // reading its first bytes without inflating anything.
            AddStoredEntry(
                archive,
                OdtNamespaces.MimeTypePart,
                Encoding.ASCII.GetBytes(OdtNamespaces.PackageMediaType));
            AddXmlEntry(archive, OdtNamespaces.ContentPart, content);
            AddXmlEntry(archive, OdtNamespaces.StylesPart, BuildStyles(document.RunningContent, context));
            AddXmlEntry(archive, OdtNamespaces.MetaPart, BuildMeta());
            foreach (OdtPicturePart picture in context.Pictures)
                AddDeflatedEntry(archive, picture.PartPath, picture.Data.Span);
            AddXmlEntry(archive, OdtNamespaces.ManifestPart, BuildManifest(context));
        }

        byte[] bytes = package.ToArray();
        destination.Write(bytes, 0, bytes.Length);
        return new DocumentWriteResult(
            bytes.Length,
            context.Diagnostics,
            DocumentWriteResult.StatusFrom(context.Diagnostics));
    }

    public static byte[] WriteToArray(RichTextDocument document, DocumentWriteOptions? options = null)
    {
        using var stream = new MemoryStream();
        Write(document, stream, options);
        return stream.ToArray();
    }

    private static XDocument BuildContent(XElement body, OdtWriteContext context)
    {
        var automaticStyles = new XElement(OdtNamespaces.Office + "automatic-styles");
        foreach (XElement style in context.AutomaticStyles)
            automaticStyles.Add(style);
        foreach (XElement listStyle in BuildListStyles(context))
            automaticStyles.Add(listStyle);
        if (context.Pictures.Count > 0)
            automaticStyles.Add(BuildFrameStyle());

        var root = new XElement(
            OdtNamespaces.Office + "document-content",
            new XAttribute(XNamespace.Xmlns + "office", OdtNamespaces.Office.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "text", OdtNamespaces.Text.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "style", OdtNamespaces.Style.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "fo", OdtNamespaces.Fo.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "svg", OdtNamespaces.Svg.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "draw", OdtNamespaces.Draw.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xlink", OdtNamespaces.XLink.NamespaceName),
            new XAttribute(OdtNamespaces.Office + "version", OdtNamespaces.WrittenVersion),
            automaticStyles,
            new XElement(OdtNamespaces.Office + "body", body));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    private static XElement BuildBody(RichTextDocument document, OdtWriteContext context)
    {
        var text = new XElement(OdtNamespaces.Office + "text");
        IReadOnlyList<RichTextParagraph> paragraphs = document.Paragraphs;

        int index = 0;
        while (index < paragraphs.Count)
        {
            ListKind kind = paragraphs[index].Style.ListKind;
            if (kind == ListKind.None)
            {
                text.Add(BuildParagraph(paragraphs[index], context, inList: false));
                index++;
                continue;
            }

            // One text:list per maximal run of same-kind list paragraphs. Starting
            // a fresh list for every item would restart the numbering at each one.
            int end = index;
            while (end < paragraphs.Count && paragraphs[end].Style.ListKind == kind)
                end++;

            text.Add(BuildList(paragraphs, index, end, kind, context));
            index = end;
        }

        return text;
    }

    /// <summary>
    /// Builds one <c>text:list</c> from a run of list paragraphs. ODF nests a
    /// deeper level inside the <c>text:list-item</c> it belongs to, so a level
    /// change is a push or a pop rather than a new sibling list.
    /// </summary>
    private static XElement BuildList(
        IReadOnlyList<RichTextParagraph> paragraphs,
        int start,
        int end,
        ListKind kind,
        OdtWriteContext context)
    {
        var root = new XElement(
            OdtNamespaces.Text + "list",
            new XAttribute(OdtNamespaces.Text + "style-name", context.UseListStyle(kind)));

        var open = new List<XElement> { root };
        for (int i = start; i < end; i++)
        {
            RichTextParagraph paragraph = paragraphs[i];
            int level = Math.Clamp(Math.Max(1, paragraph.Style.IndentLevel), 1, MaxListLevels);

            while (open.Count > level)
                open.RemoveAt(open.Count - 1);

            while (open.Count < level)
            {
                XElement parent = open[^1];
                XElement? host = parent.Elements(OdtNamespaces.Text + "list-item").LastOrDefault();
                if (host is null)
                {
                    // A list that starts below its own first level still needs an
                    // item to hang the nested list from.
                    host = new XElement(OdtNamespaces.Text + "list-item");
                    parent.Add(host);
                }

                var nested = new XElement(OdtNamespaces.Text + "list");
                host.Add(nested);
                open.Add(nested);
            }

            open[^1].Add(new XElement(
                OdtNamespaces.Text + "list-item",
                BuildParagraph(paragraph, context, inList: true)));
        }

        return root;
    }

    private static XElement BuildParagraph(
        RichTextParagraph paragraph,
        OdtWriteContext context,
        bool inList)
    {
        var element = new XElement(OdtNamespaces.Text + "p");
        string? styleName = context.GetParagraphStyleName(BuildParagraphProperties(paragraph.Style, inList));
        if (styleName is not null)
            element.Add(new XAttribute(OdtNamespaces.Text + "style-name", styleName));

        var state = new ParagraphState(paragraph.Text);
        int offset = 0;
        foreach (StyleRun run in paragraph.Runs)
        {
            string text = paragraph.Text.Substring(offset, run.Length);
            AddRun(element, text, offset, run.Style, state, context);
            offset += run.Length;
        }

        return element;
    }

    /// <summary>
    /// Writes one run, wrapping it in the <c>text:span</c> its formatting needs
    /// and the <c>text:a</c> its link needs. An unformatted run with no link goes
    /// straight into the paragraph, which is what keeps a plain document plain.
    /// </summary>
    private static void AddRun(
        XElement paragraph,
        string text,
        int offset,
        InlineStyle style,
        ParagraphState state,
        OdtWriteContext context)
    {
        XElement host = paragraph;

        if (!string.IsNullOrEmpty(style.LinkHref))
        {
            XElement? anchor = BuildAnchor(style.LinkHref, context);
            if (anchor is not null)
            {
                paragraph.Add(anchor);
                host = anchor;
            }
        }

        string? styleName = context.GetTextStyleName(BuildTextProperties(style, context));
        if (styleName is not null)
        {
            var span = new XElement(
                OdtNamespaces.Text + "span",
                new XAttribute(OdtNamespaces.Text + "style-name", styleName));
            host.Add(span);
            host = span;
        }

        AddInlineContent(host, text, offset, style, state, context);
    }

    private static XElement? BuildAnchor(string href, OdtWriteContext context)
    {
        if (!IsAllowedLink(href))
        {
            context.AddDiagnosticOnce(
                "odt.link",
                "A hyperlink with a disallowed or relative target was written as plain text.");
            return null;
        }

        return new XElement(
            OdtNamespaces.Text + "a",
            new XAttribute(OdtNamespaces.XLink + "href", href),
            new XAttribute(OdtNamespaces.XLink + "type", "simple"));
    }

    /// <summary>
    /// Writes a run's characters, turning the ones ODF does not keep in a text
    /// node back into their own elements: tabs, line breaks, the object
    /// replacement character that carries a picture, and every space a reader
    /// would otherwise collapse away.
    /// </summary>
    private static void AddInlineContent(
        XElement host,
        string text,
        int offset,
        InlineStyle style,
        ParagraphState state,
        OdtWriteContext context)
    {
        var pending = new StringBuilder();

        void Flush()
        {
            if (pending.Length == 0)
                return;

            host.Add(new XText(pending.ToString()));
            pending.Clear();
            state.HasContent = true;
        }

        int i = 0;
        while (i < text.Length)
        {
            char character = text[i];

            if (character == ' ')
            {
                int run = i;
                while (run < text.Length && text[run] == ' ')
                    run++;

                // A reader drops a space at either edge of a paragraph and folds
                // a run of them into one, so those spaces have to be written as
                // text:s to survive a round trip.
                bool atParagraphStart = !state.HasContent && pending.Length == 0;
                bool atParagraphEnd = offset + run >= state.Text.Length;
                int count = run - i;
                if (atParagraphStart || atParagraphEnd)
                {
                    Flush();
                    AddSpaces(host, count, state);
                }
                else
                {
                    pending.Append(' ');
                    if (count > 1)
                    {
                        Flush();
                        AddSpaces(host, count - 1, state);
                    }
                }

                i = run;
                continue;
            }

            if (character == '\t')
            {
                Flush();
                host.Add(new XElement(OdtNamespaces.Text + "tab"));
                state.HasContent = true;
                i++;
                continue;
            }

            if (character is (char)0x2028 or '\n' or '\r')
            {
                Flush();
                host.Add(new XElement(OdtNamespaces.Text + "line-break"));
                state.HasContent = true;
                i++;
                continue;
            }

            if (character == InlineImage.Placeholder)
            {
                Flush();
                AddPicture(host, style.Image, state, context);
                i++;
                continue;
            }

            if (char.IsHighSurrogate(character) &&
                i + 1 < text.Length &&
                char.IsLowSurrogate(text[i + 1]))
            {
                pending.Append(character).Append(text[i + 1]);
                i += 2;
                continue;
            }

            if (!XmlConvert.IsXmlChar(character))
            {
                // XML 1.0 has no representation for most control characters, not
                // even an escape, so there is nothing to write but a diagnostic.
                context.AddDiagnosticOnce(
                    "odt.text.control",
                    "A control character that XML cannot represent was dropped.");
                i++;
                continue;
            }

            pending.Append(character);
            i++;
        }

        Flush();
    }

    private static void AddSpaces(XElement host, int count, ParagraphState state)
    {
        if (count <= 0)
            return;

        var spaces = new XElement(OdtNamespaces.Text + "s");
        if (count > 1)
        {
            spaces.Add(new XAttribute(
                OdtNamespaces.Text + "c",
                count.ToString(CultureInfo.InvariantCulture)));
        }

        host.Add(spaces);
        state.HasContent = true;
    }

    /// <summary>
    /// Writes one picture as a character-anchored frame and registers its
    /// package entry. A placeholder character whose run carries no image is
    /// dropped rather than written through: a consumer would draw it as a
    /// missing-glyph box.
    /// </summary>
    private static void AddPicture(
        XElement host,
        InlineImage? image,
        ParagraphState state,
        OdtWriteContext context)
    {
        if (image is null)
        {
            context.AddDiagnosticOnce(
                "odt.image.placeholder",
                "An object replacement character with no image attached was dropped.");
            return;
        }

        OdtPicturePart part = context.GetPicturePart(image);
        double width = image.Width > 0 ? image.Width : DefaultImagePoints;
        double height = image.Height > 0 ? image.Height : DefaultImagePoints;
        if (!image.HasExplicitSize)
        {
            context.AddDiagnosticOnce(
                "odt.image.size",
                "An image carried no display size and was written one inch square.");
        }

        var frame = new XElement(
            OdtNamespaces.Draw + "frame",
            new XAttribute(OdtNamespaces.Draw + "style-name", "fr1"),
            new XAttribute(
                OdtNamespaces.Draw + "name",
                "Image" + part.Index.ToString(CultureInfo.InvariantCulture)),
            new XAttribute(OdtNamespaces.Text + "anchor-type", "as-char"),
            new XAttribute(OdtNamespaces.Svg + "width", OdtUnits.FormatInches(width)),
            new XAttribute(OdtNamespaces.Svg + "height", OdtUnits.FormatInches(height)),
            new XElement(
                OdtNamespaces.Draw + "image",
                new XAttribute(OdtNamespaces.XLink + "href", part.PartPath),
                new XAttribute(OdtNamespaces.XLink + "type", "simple"),
                new XAttribute(OdtNamespaces.XLink + "show", "embed"),
                new XAttribute(OdtNamespaces.XLink + "actuate", "onLoad")));

        if (image.AltText.Length > 0)
            frame.Add(new XElement(OdtNamespaces.Svg + "title", image.AltText));

        host.Add(frame);
        state.HasContent = true;
    }

    private static XElement BuildParagraphProperties(ParagraphStyle style, bool inList)
    {
        var properties = new XElement(OdtNamespaces.Style + "paragraph-properties");

        if (style.Alignment == TextAlignment.Center)
            properties.Add(new XAttribute(OdtNamespaces.Fo + "text-align", "center"));
        else if (style.Alignment == TextAlignment.Right)
            properties.Add(new XAttribute(OdtNamespaces.Fo + "text-align", "end"));
        else if (style.Alignment == TextAlignment.Justify)
            properties.Add(new XAttribute(OdtNamespaces.Fo + "text-align", "justify"));

        if (Math.Abs(style.SpacingBefore) > 0.001f)
        {
            properties.Add(new XAttribute(
                OdtNamespaces.Fo + "margin-top",
                OdtUnits.FormatPoints(style.SpacingBefore)));
        }

        if (Math.Abs(style.SpacingAfter) > 0.001f)
        {
            properties.Add(new XAttribute(
                OdtNamespaces.Fo + "margin-bottom",
                OdtUnits.FormatPoints(style.SpacingAfter)));
        }

        // Inside a list the nesting is the indent; adding a margin as well would
        // indent every item twice.
        if (style.IndentLevel > 0 && !inList)
        {
            properties.Add(new XAttribute(
                OdtNamespaces.Fo + "margin-left",
                OdtUnits.FormatInches(style.IndentLevel * OdtUnits.PointsPerIndentLevel)));
        }

        if (Math.Abs(style.LineSpacing - 1f) > 0.001f && style.LineSpacing > 0)
        {
            properties.Add(new XAttribute(
                OdtNamespaces.Fo + "line-height",
                OdtUnits.FormatPercentage(style.LineSpacing)));
        }

        return properties;
    }

    private static XElement BuildTextProperties(InlineStyle style, OdtWriteContext context)
    {
        var properties = new XElement(OdtNamespaces.Style + "text-properties");

        if (style.Bold)
            properties.Add(new XAttribute(OdtNamespaces.Fo + "font-weight", "bold"));
        if (style.Italic)
            properties.Add(new XAttribute(OdtNamespaces.Fo + "font-style", "italic"));
        if (style.Underline)
        {
            properties.Add(new XAttribute(OdtNamespaces.Style + "text-underline-style", "solid"));
            properties.Add(new XAttribute(OdtNamespaces.Style + "text-underline-width", "auto"));
            properties.Add(new XAttribute(OdtNamespaces.Style + "text-underline-color", "font-color"));
        }

        if (style.Strikethrough)
            properties.Add(new XAttribute(OdtNamespaces.Style + "text-line-through-style", "solid"));

        if (style.Capitalization == TextCapitalization.AllCaps)
            properties.Add(new XAttribute(OdtNamespaces.Fo + "text-transform", "uppercase"));
        else if (style.Capitalization == TextCapitalization.SmallCaps)
            properties.Add(new XAttribute(OdtNamespaces.Fo + "font-variant", "small-caps"));

        if (!string.IsNullOrWhiteSpace(style.FontFamily))
        {
            properties.Add(new XAttribute(
                OdtNamespaces.Fo + "font-family",
                QuoteFontFamily(style.FontFamily)));
        }

        if (style.FontSize is { } size && size > 0)
            properties.Add(new XAttribute(OdtNamespaces.Fo + "font-size", OdtUnits.FormatPoints(size)));

        if (!style.Foreground.IsEmpty)
        {
            properties.Add(new XAttribute(
                OdtNamespaces.Fo + "color",
                FormatColor(style.Foreground, context)));
        }

        if (!style.Background.IsEmpty)
        {
            properties.Add(new XAttribute(
                OdtNamespaces.Fo + "background-color",
                FormatColor(style.Background, context)));
        }

        return properties;
    }

    /// <summary>
    /// Quotes a family name for the CSS syntax <c>fo:font-family</c> uses. A name
    /// with a space in it is a syntax error unquoted, which is most of the names
    /// anyone actually types.
    /// </summary>
    private static string QuoteFontFamily(string family) =>
        family.AsSpan().IndexOfAny(" ,'\"") >= 0
            ? "'" + family.Replace("'", string.Empty, StringComparison.Ordinal) + "'"
            : family;

    private static string FormatColor(BColor color, OdtWriteContext context)
    {
        if (color.A != 255)
            context.AddDiagnosticOnce("odt.color.alpha", "ODT colors do not preserve alpha; RGB channels were written.");

        return OdtUnits.FormatColor(color);
    }

    /// <summary>
    /// The list styles the content refers to, one per kind that was used. Every
    /// level is declared, because a list style that stops short of the level a
    /// document reaches leaves that level undecorated.
    /// </summary>
    private static IEnumerable<XElement> BuildListStyles(OdtWriteContext context)
    {
        foreach (ListKind kind in context.ListKinds)
        {
            var listStyle = new XElement(
                OdtNamespaces.Text + "list-style",
                new XAttribute(OdtNamespaces.Style + "name", OdtWriteContext.ListStyleName(kind)));

            for (int level = 1; level <= MaxListLevels; level++)
            {
                XElement levelStyle = kind == ListKind.Numbered
                    ? new XElement(
                        OdtNamespaces.Text + "list-level-style-number",
                        new XAttribute(OdtNamespaces.Style + "num-suffix", "."),
                        new XAttribute(OdtNamespaces.Style + "num-format", "1"))
                    : new XElement(
                        OdtNamespaces.Text + "list-level-style-bullet",
                        new XAttribute(OdtNamespaces.Text + "bullet-char", "•"));

                levelStyle.Add(new XAttribute(
                    OdtNamespaces.Text + "level",
                    level.ToString(CultureInfo.InvariantCulture)));
                levelStyle.Add(new XElement(
                    OdtNamespaces.Style + "list-level-properties",
                    new XAttribute(
                        OdtNamespaces.Text + "space-before",
                        OdtUnits.FormatInches((level - 1) * OdtUnits.PointsPerIndentLevel)),
                    new XAttribute(
                        OdtNamespaces.Text + "min-label-width",
                        OdtUnits.FormatInches(OdtUnits.PointsPerIndentLevel))));
                listStyle.Add(levelStyle);
            }

            yield return listStyle;
        }
    }

    /// <summary>The graphic style every written frame uses: an inline picture with no border or padding.</summary>
    private static XElement BuildFrameStyle() =>
        new(
            OdtNamespaces.Style + "style",
            new XAttribute(OdtNamespaces.Style + "name", "fr1"),
            new XAttribute(OdtNamespaces.Style + "family", "graphic"),
            new XElement(
                OdtNamespaces.Style + "graphic-properties",
                new XAttribute(OdtNamespaces.Style + "vertical-pos", "middle"),
                new XAttribute(OdtNamespaces.Style + "vertical-rel", "text"),
                new XAttribute(OdtNamespaces.Fo + "padding", "0in"),
                new XAttribute(OdtNamespaces.Fo + "border", "none")));

    /// <summary>
    /// The styles part. It carries the page geometry a consumer needs to lay the
    /// document out at all; everything the model can express is an automatic
    /// style in <c>content.xml</c> instead.
    /// </summary>
    /// <summary>ODF §16.10: a master page's header and footer elements, left being the even page.</summary>
    private static readonly (string Element, bool IsHeader, PageSelection Selection)[] RunningParts =
    [
        ("header", true, PageSelection.Default),
        ("header-first", true, PageSelection.First),
        ("header-left", true, PageSelection.Even),
        ("footer", false, PageSelection.Default),
        ("footer-first", false, PageSelection.First),
        ("footer-left", false, PageSelection.Even),
    ];

    /// <summary>
    /// The header and footer elements for the master page, in the order ODF
    /// declares them. An unset selection contributes nothing, so a document with
    /// one header everywhere writes one element.
    /// </summary>
    private static IEnumerable<XElement> BuildRunningParts(RunningContent running, OdtWriteContext context)
    {
        if (running is null || running.IsEmpty)
            yield break;

        foreach ((string element, bool isHeader, PageSelection selection) in RunningParts)
        {
            IReadOnlyList<RichTextParagraph> paragraphs =
                isHeader ? running.Header(selection) : running.Footer(selection);
            if (paragraphs.Count == 0)
                continue;

            var part = new XElement(OdtNamespaces.Style + element);
            foreach (RichTextParagraph paragraph in paragraphs)
                part.Add(BuildParagraph(paragraph, context, inList: false));

            yield return part;
        }
    }

    private static XDocument BuildStyles(RunningContent running, OdtWriteContext context)
    {
        var root = new XElement(
            OdtNamespaces.Office + "document-styles",
            new XAttribute(XNamespace.Xmlns + "office", OdtNamespaces.Office.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "style", OdtNamespaces.Style.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "text", OdtNamespaces.Text.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "fo", OdtNamespaces.Fo.NamespaceName),
            new XAttribute(OdtNamespaces.Office + "version", OdtNamespaces.WrittenVersion),
            new XElement(
                OdtNamespaces.Office + "styles",
                new XElement(
                    OdtNamespaces.Style + "style",
                    new XAttribute(OdtNamespaces.Style + "name", "Standard"),
                    new XAttribute(OdtNamespaces.Style + "family", "paragraph"),
                    new XAttribute(OdtNamespaces.Style + "class", "text"))),
            new XElement(
                OdtNamespaces.Office + "automatic-styles",
                new XElement(
                    OdtNamespaces.Style + "page-layout",
                    new XAttribute(OdtNamespaces.Style + "name", "pm1"),
                    new XElement(
                        OdtNamespaces.Style + "page-layout-properties",
                        new XAttribute(OdtNamespaces.Fo + "page-width", "8.5in"),
                        new XAttribute(OdtNamespaces.Fo + "page-height", "11in"),
                        new XAttribute(OdtNamespaces.Style + "print-orientation", "portrait"),
                        new XAttribute(OdtNamespaces.Fo + "margin-top", "1in"),
                        new XAttribute(OdtNamespaces.Fo + "margin-bottom", "1in"),
                        new XAttribute(OdtNamespaces.Fo + "margin-left", "1in"),
                        new XAttribute(OdtNamespaces.Fo + "margin-right", "1in")))),
            new XElement(
                OdtNamespaces.Office + "master-styles",
                new XElement(
                    OdtNamespaces.Style + "master-page",
                    new XAttribute(OdtNamespaces.Style + "name", "Standard"),
                    new XAttribute(OdtNamespaces.Style + "page-layout-name", "pm1"),
                    BuildRunningParts(running, context))));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    /// <summary>
    /// The metadata part. It names the generator and nothing else: a creation
    /// date would make two writes of the same document differ, and any other
    /// field would be information this codec was never given.
    /// </summary>
    private static XDocument BuildMeta() =>
        new(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(
                OdtNamespaces.Office + "document-meta",
                new XAttribute(XNamespace.Xmlns + "office", OdtNamespaces.Office.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "meta", OdtNamespaces.Meta.NamespaceName),
                new XAttribute(OdtNamespaces.Office + "version", OdtNamespaces.WrittenVersion),
                new XElement(
                    OdtNamespaces.Office + "meta",
                    new XElement(OdtNamespaces.Meta + "generator", "Broiler.Documents.Odt"))));

    private static XDocument BuildManifest(OdtWriteContext context)
    {
        var root = new XElement(
            OdtNamespaces.Manifest + "manifest",
            new XAttribute(XNamespace.Xmlns + "manifest", OdtNamespaces.Manifest.NamespaceName),
            new XAttribute(OdtNamespaces.Manifest + "version", OdtNamespaces.WrittenVersion),
            ManifestEntry("/", OdtNamespaces.PackageMediaType, OdtNamespaces.WrittenVersion),
            ManifestEntry(OdtNamespaces.ContentPart, "text/xml"),
            ManifestEntry(OdtNamespaces.StylesPart, "text/xml"),
            ManifestEntry(OdtNamespaces.MetaPart, "text/xml"));

        foreach (OdtPicturePart picture in context.Pictures)
            root.Add(ManifestEntry(picture.PartPath, picture.ContentType));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    private static XElement ManifestEntry(string path, string mediaType, string? version = null)
    {
        var entry = new XElement(
            OdtNamespaces.Manifest + "file-entry",
            new XAttribute(OdtNamespaces.Manifest + "full-path", path),
            new XAttribute(OdtNamespaces.Manifest + "media-type", mediaType));
        if (version is not null)
            entry.Add(new XAttribute(OdtNamespaces.Manifest + "version", version));

        return entry;
    }

    private static void AddXmlEntry(ZipArchive archive, string path, XDocument document)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        entry.LastWriteTime = ZipTimestamp;
        using Stream stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            NewLineHandling = NewLineHandling.None,
        });
        document.Save(writer);
    }

    private static void AddDeflatedEntry(ZipArchive archive, string path, ReadOnlySpan<byte> data)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        entry.LastWriteTime = ZipTimestamp;
        using Stream stream = entry.Open();
        stream.Write(data);
    }

    private static void AddStoredEntry(ZipArchive archive, string path, ReadOnlySpan<byte> data)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        entry.LastWriteTime = ZipTimestamp;
        using Stream stream = entry.Open();
        stream.Write(data);
    }

    private static bool IsAllowedLink(string href)
    {
        if (href.StartsWith("#", StringComparison.Ordinal))
            return href.Length > 1;
        if (!Uri.TryCreate(href, UriKind.Absolute, out Uri? uri))
            return false;

        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What the space rules need to know while a paragraph is being written: its
    /// full text, so the end can be recognized, and whether anything has been
    /// written into it yet.
    /// </summary>
    private sealed class ParagraphState
    {
        public ParagraphState(string text) => Text = text;

        public string Text { get; }

        public bool HasContent { get; set; }
    }

    private sealed class OdtWriteContext
    {
        private readonly Dictionary<string, string> _paragraphStyleNames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _textStyleNames = new(StringComparer.Ordinal);
        private readonly List<XElement> _automaticStyles = [];
        private readonly Dictionary<InlineImage, OdtPicturePart> _pictures =
            new(ReferenceEqualityComparer.Instance);
        private readonly List<OdtPicturePart> _pictureOrder = [];
        private readonly List<ListKind> _listKinds = [];
        private readonly List<DocumentDiagnostic> _diagnostics = [];
        private readonly HashSet<string> _diagnosticOnce = new(StringComparer.Ordinal);

        /// <summary>The <c>style:style</c> elements to declare, in the order they were first needed.</summary>
        public IReadOnlyList<XElement> AutomaticStyles => _automaticStyles;

        /// <summary>The pictures to write, in the order they were first used.</summary>
        public IReadOnlyList<OdtPicturePart> Pictures => _pictureOrder;

        /// <summary>The list kinds the document used, so only those get a list style.</summary>
        public IReadOnlyList<ListKind> ListKinds => _listKinds;

        public IReadOnlyList<DocumentDiagnostic> Diagnostics => _diagnostics;

        /// <summary>
        /// The automatic paragraph style for these properties, created on first
        /// use. Null when there are no properties at all: a paragraph that names
        /// no style is the shortest and the most faithful way to say "default".
        /// </summary>
        public string? GetParagraphStyleName(XElement properties) =>
            GetStyleName(properties, _paragraphStyleNames, "P", OdtStyles.ParagraphFamily, "Standard");

        /// <summary>The automatic text style for these properties, created on first use.</summary>
        public string? GetTextStyleName(XElement properties) =>
            GetStyleName(properties, _textStyleNames, "T", OdtStyles.TextFamily, parentStyleName: null);

        private string? GetStyleName(
            XElement properties,
            Dictionary<string, string> names,
            string prefix,
            string family,
            string? parentStyleName)
        {
            if (!properties.HasAttributes)
                return null;

            string key = string.Join(
                "",
                properties.Attributes().Select(attribute => attribute.Name + "=" + attribute.Value));
            if (names.TryGetValue(key, out string? existing))
                return existing;

            string name = prefix + (names.Count + 1).ToString(CultureInfo.InvariantCulture);
            names[key] = name;

            var style = new XElement(
                OdtNamespaces.Style + "style",
                new XAttribute(OdtNamespaces.Style + "name", name),
                new XAttribute(OdtNamespaces.Style + "family", family));
            if (parentStyleName is not null)
                style.Add(new XAttribute(OdtNamespaces.Style + "parent-style-name", parentStyleName));

            style.Add(properties);
            _automaticStyles.Add(style);
            return name;
        }

        /// <summary>
        /// Names the list style for <paramref name="kind"/> and records that the
        /// document needs it declared.
        /// </summary>
        public string UseListStyle(ListKind kind)
        {
            if (!_listKinds.Contains(kind))
                _listKinds.Add(kind);

            return ListStyleName(kind);
        }

        /// <summary>The name of a list style, without asking for it to be declared.</summary>
        public static string ListStyleName(ListKind kind) => kind == ListKind.Numbered ? "L2" : "L1";

        /// <summary>
        /// The package entry for <paramref name="image"/>, created on first use.
        /// Keyed by identity, so a document that shows the same image object in
        /// several places stores its bytes once.
        /// </summary>
        public OdtPicturePart GetPicturePart(InlineImage image)
        {
            if (_pictures.TryGetValue(image, out OdtPicturePart? existing))
                return existing;

            int index = _pictureOrder.Count + 1;
            string extension = OdtImageFormats.ExtensionForContentType(image.ContentType);
            var part = new OdtPicturePart(
                index,
                OdtNamespaces.PicturesDirectory + "image" +
                    index.ToString(CultureInfo.InvariantCulture) + "." + extension,
                image.ContentType,
                image.Data);
            _pictures[image] = part;
            _pictureOrder.Add(part);
            return part;
        }

        public void AddDiagnosticOnce(string code, string message)
        {
            if (_diagnosticOnce.Add(code))
                _diagnostics.Add(DocumentDiagnostic.Warning(code, message));
        }
    }

    /// <summary>One <c>Pictures</c> entry: where it lives and how the frame refers to it.</summary>
    private sealed record OdtPicturePart(
        int Index,
        string PartPath,
        string ContentType,
        ReadOnlyMemory<byte> Data);
}
