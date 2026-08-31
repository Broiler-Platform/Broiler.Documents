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

namespace Broiler.Documents.Docx;

/// <summary>Serializes the rich-text document model to a minimal DOCX package.</summary>
public static class DocxWriter
{
    private static readonly DateTimeOffset ZipTimestamp =
        new(new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    /// <summary>The size an image with no stated display size is written at: one inch square.</summary>
    private const double DefaultImagePoints = 72.0;

    public static DocumentWriteResult Write(
        RichTextDocument document,
        Stream destination,
        DocumentWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        _ = options;

        var context = new DocxWriteContext(document.Paragraphs.Any(static p => p.Style.ListKind != ListKind.None));
        BuildRunningParts(document.RunningContent, context);
        XDocument documentXml = BuildDocumentXml(document, context);

        using var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddXmlEntry(archive, "[Content_Types].xml", BuildContentTypes(context));
            AddXmlEntry(archive, "_rels/.rels", BuildPackageRelationships());
            AddXmlEntry(archive, "word/document.xml", documentXml);
            if (context.HasDocumentRelationships)
                AddXmlEntry(archive, "word/_rels/document.xml.rels", BuildDocumentRelationships(context));
            if (context.HasNumbering)
                AddXmlEntry(archive, "word/numbering.xml", BuildNumbering());
            foreach (DocxRunningPart part in context.RunningParts)
                AddXmlEntry(archive, part.PartPath, part.Xml);
            foreach (DocxImagePart image in context.Images)
                AddBinaryEntry(archive, image.PartPath, image.Data.Span);
        }

        byte[] bytes = package.ToArray();
        destination.Write(bytes, 0, bytes.Length);
        return new DocumentWriteResult(bytes.Length, context.Diagnostics, DocumentWriteResult.StatusFrom(context.Diagnostics));
    }

    public static byte[] WriteToArray(RichTextDocument document, DocumentWriteOptions? options = null)
    {
        using var stream = new MemoryStream();
        Write(document, stream, options);
        return stream.ToArray();
    }

    /// <summary>
    /// Turns the document's running content into header and footer parts. Each
    /// part is a <c>w:hdr</c> or <c>w:ftr</c> holding the same paragraphs the body
    /// writer produces, so a header keeps its alignment, spacing and runs.
    /// </summary>
    private static void BuildRunningParts(RunningContent running, DocxWriteContext context)
    {
        if (running is null || running.IsEmpty)
            return;

        foreach (PageSelection selection in new[] { PageSelection.Default, PageSelection.First, PageSelection.Even })
        {
            AddRunningPart(running.Header(selection), isHeader: true, selection, context);
            AddRunningPart(running.Footer(selection), isHeader: false, selection, context);
        }
    }

    private static void AddRunningPart(
        IReadOnlyList<RichTextParagraph> paragraphs,
        bool isHeader,
        PageSelection selection,
        DocxWriteContext context)
    {
        if (paragraphs.Count == 0)
            return;

        var root = new XElement(
            DocxNamespaces.Wordprocessing + (isHeader ? "hdr" : "ftr"),
            new XAttribute(XNamespace.Xmlns + "w", DocxNamespaces.Wordprocessing.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", DocxNamespaces.Relationships.NamespaceName));

        foreach (RichTextParagraph paragraph in paragraphs)
            root.Add(BuildParagraph(paragraph, context));

        context.AddRunningPart(isHeader, selection, new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root));
    }

    /// <summary>
    /// The body's section properties: a reference per header and footer part.
    /// </summary>
    /// <remarks>
    /// A first-page header only takes effect when the section also says it wants a
    /// distinct one, so <c>w:titlePg</c> is written whenever a First part exists.
    /// Without it Word reads the part and then draws the default over it.
    /// </remarks>
    private static XElement BuildSectionProperties(DocxWriteContext context, PageGeometry? geometry)
    {
        var sectPr = new XElement(DocxNamespaces.Wordprocessing + "sectPr");
        bool hasFirst = false;

        foreach (DocxRunningPart part in context.RunningParts)
        {
            hasFirst |= part.Selection == PageSelection.First;
            var reference = new XElement(
                DocxNamespaces.Wordprocessing + (part.IsHeader ? "headerReference" : "footerReference"),
                new XAttribute(DocxNamespaces.Relationships + "id", part.RelationshipId));

            string? type = part.Selection switch
            {
                PageSelection.First => "first",
                PageSelection.Even => "even",
                _ => "default",
            };
            reference.Add(WordAttribute("type", type));
            sectPr.Add(reference);
        }

        if (hasFirst)
            sectPr.Add(new XElement(DocxNamespaces.Wordprocessing + "titlePg"));

        if (geometry is not null && geometry.IsUsable)
        {
            sectPr.Add(new XElement(
                DocxNamespaces.Wordprocessing + "pgSz",
                WordAttribute("w", Twips(geometry.Width)),
                WordAttribute("h", Twips(geometry.Height))));
            sectPr.Add(new XElement(
                DocxNamespaces.Wordprocessing + "pgMar",
                WordAttribute("left", Twips(geometry.MarginLeft)),
                WordAttribute("right", Twips(geometry.MarginRight)),
                WordAttribute("top", Twips(geometry.MarginTop)),
                WordAttribute("bottom", Twips(geometry.MarginBottom)),
                WordAttribute("header", Twips(geometry.HeaderDistance)),
                WordAttribute("footer", Twips(geometry.FooterDistance)),
                WordAttribute("gutter", "0")));
        }

        return sectPr;
    }

    private static XDocument BuildDocumentXml(RichTextDocument document, DocxWriteContext context)
    {
        var body = new XElement(DocxNamespaces.Wordprocessing + "body");
        for (int i = 0; i < document.Paragraphs.Count; i++)
        {
            XElement element = BuildParagraph(document.Paragraphs[i], context);
            // A shape is anchored to a paragraph, so it rides in a run at the head
            // of the one it belongs to - which is where Word puts it too.
            foreach (DocumentShape shape in document.Shapes)
            {
                if (shape.ParagraphIndex == i)
                    element.Add(BuildShapeRun(shape, context));
            }

            body.Add(element);
        }

        body.Add(BuildSectionProperties(context, document.PageGeometry));

        var root = new XElement(
            DocxNamespaces.Wordprocessing + "document",
            new XAttribute(XNamespace.Xmlns + "w", DocxNamespaces.Wordprocessing.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", DocxNamespaces.Relationships.NamespaceName));

        // Declared at the root only when used, so a text-only document keeps the
        // same minimal document.xml it has always produced.
        if (context.Images.Count > 0)
        {
            root.Add(
                new XAttribute(XNamespace.Xmlns + "wp", DocxNamespaces.WordDrawing.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", DocxNamespaces.Drawing.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "pic", DocxNamespaces.Picture.NamespaceName));
        }

        root.Add(body);
        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
    }

    private static XElement BuildParagraph(RichTextParagraph paragraph, DocxWriteContext context)
    {
        var element = new XElement(DocxNamespaces.Wordprocessing + "p");
        XElement? properties = BuildParagraphProperties(paragraph.Style);
        if (properties is not null)
            element.Add(properties);

        int offset = 0;
        foreach (StyleRun run in paragraph.Runs)
        {
            string text = paragraph.Text.Substring(offset, run.Length);
            offset += run.Length;
            AddRun(element, text, run.Style, context);
        }

        return element;
    }

    private static XElement? BuildParagraphProperties(ParagraphStyle style)
    {
        var properties = new XElement(DocxNamespaces.Wordprocessing + "pPr");

        if (style.ListKind != ListKind.None)
        {
            int level = Math.Clamp(style.IndentLevel <= 0 ? 0 : style.IndentLevel - 1, 0, 8);
            int numId = style.ListKind == ListKind.Numbered ? 2 : 1;
            properties.Add(new XElement(
                DocxNamespaces.Wordprocessing + "numPr",
                new XElement(DocxNamespaces.Wordprocessing + "ilvl", WordAttribute("val", level.ToString(CultureInfo.InvariantCulture))),
                new XElement(DocxNamespaces.Wordprocessing + "numId", WordAttribute("val", numId.ToString(CultureInfo.InvariantCulture)))));
        }

        if (style.Alignment == TextAlignment.Center)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "jc", WordAttribute("val", "center")));
        else if (style.Alignment == TextAlignment.Right)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "jc", WordAttribute("val", "right")));
        else if (style.Alignment == TextAlignment.Justify)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "jc", WordAttribute("val", "both")));

        if (Math.Abs(style.LineSpacing - 1f) > 0.001f ||
            Math.Abs(style.SpacingBefore) > 0.001f ||
            Math.Abs(style.SpacingAfter) > 0.001f)
        {
            var spacing = new XElement(DocxNamespaces.Wordprocessing + "spacing");
            if (Math.Abs(style.SpacingBefore) > 0.001f)
                spacing.Add(WordAttribute("before", Twips(style.SpacingBefore).ToString(CultureInfo.InvariantCulture)));
            if (Math.Abs(style.SpacingAfter) > 0.001f)
                spacing.Add(WordAttribute("after", Twips(style.SpacingAfter).ToString(CultureInfo.InvariantCulture)));
            if (Math.Abs(style.LineSpacing - 1f) > 0.001f)
            {
                spacing.Add(WordAttribute("line", Math.Max(1, (int)Math.Round(style.LineSpacing * 240f)).ToString(CultureInfo.InvariantCulture)));
                spacing.Add(WordAttribute("lineRule", "auto"));
            }

            properties.Add(spacing);
        }

        if (style.IndentLevel > 0)
        {
            properties.Add(new XElement(
                DocxNamespaces.Wordprocessing + "ind",
                WordAttribute("left", (style.IndentLevel * 360).ToString(CultureInfo.InvariantCulture))));
        }

        return properties.HasElements ? properties : null;
    }

    private static void AddRun(XElement parent, string text, InlineStyle style, DocxWriteContext context)
    {
        if (!string.IsNullOrEmpty(style.LinkHref))
        {
            XElement? hyperlink = BuildHyperlink(text, style, context);
            if (hyperlink is not null)
            {
                parent.Add(hyperlink);
                return;
            }
        }

        parent.Add(BuildRun(text, style, context));
    }

    private static XElement? BuildHyperlink(string text, InlineStyle style, DocxWriteContext context)
    {
        string href = style.LinkHref ?? string.Empty;
        if (href.StartsWith("#", StringComparison.Ordinal) && href.Length > 1)
        {
            return new XElement(
                DocxNamespaces.Wordprocessing + "hyperlink",
                WordAttribute("anchor", href[1..]),
                BuildRun(text, style with { LinkHref = null }, context));
        }

        if (!IsExternalLink(href))
        {
            context.AddDiagnosticOnce("docx.link", "A hyperlink with a disallowed or relative target was written as plain text.");
            return null;
        }

        string relationshipId = context.GetHyperlinkRelationshipId(href);
        return new XElement(
            DocxNamespaces.Wordprocessing + "hyperlink",
            new XAttribute(DocxNamespaces.Relationships + "id", relationshipId),
            new XAttribute(DocxNamespaces.Wordprocessing + "history", "1"),
            BuildRun(text, style with { LinkHref = null }, context));
    }

    private static XElement BuildRun(string text, InlineStyle style, DocxWriteContext context)
    {
        var run = new XElement(DocxNamespaces.Wordprocessing + "r");
        XElement? properties = BuildRunProperties(style, context);
        if (properties is not null)
            run.Add(properties);
        AddRunContent(run, text, style, context);
        return run;
    }

    private static XElement? BuildRunProperties(InlineStyle style, DocxWriteContext context)
    {
        var properties = new XElement(DocxNamespaces.Wordprocessing + "rPr");
        if (style.Bold)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "b"));
        if (style.Italic)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "i"));
        if (style.Underline)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "u", WordAttribute("val", "single")));
        if (style.Strikethrough)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "strike"));
        if (style.Capitalization == TextCapitalization.AllCaps)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "caps"));
        else if (style.Capitalization == TextCapitalization.SmallCaps)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "smallCaps"));

        if (!string.IsNullOrWhiteSpace(style.FontFamily))
        {
            properties.Add(new XElement(
                DocxNamespaces.Wordprocessing + "rFonts",
                WordAttribute("ascii", style.FontFamily),
                WordAttribute("hAnsi", style.FontFamily),
                WordAttribute("cs", style.FontFamily),
                WordAttribute("eastAsia", style.FontFamily)));
        }

        if (style.FontSize.HasValue)
        {
            int halfPoints = Math.Max(1, (int)Math.Round(style.FontSize.Value * 2f));
            properties.Add(new XElement(
                DocxNamespaces.Wordprocessing + "sz",
                WordAttribute("val", halfPoints.ToString(CultureInfo.InvariantCulture))));
        }

        if (!style.Foreground.IsEmpty)
        {
            properties.Add(new XElement(
                DocxNamespaces.Wordprocessing + "color",
                WordAttribute("val", FormatColor(style.Foreground, context))));
        }

        if (!style.Background.IsEmpty)
        {
            properties.Add(new XElement(
                DocxNamespaces.Wordprocessing + "shd",
                WordAttribute("val", "clear"),
                WordAttribute("color", "auto"),
                WordAttribute("fill", FormatColor(style.Background, context))));
        }

        return properties.HasElements ? properties : null;
    }

    /// <summary>
    /// Writes a run's characters, turning the ones the model stores as single
    /// code points back into their WordprocessingML elements: tab, soft line
    /// break, and the object replacement character that carries a picture.
    /// </summary>
    private static void AddRunContent(XElement run, string text, InlineStyle style, DocxWriteContext context)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            if (character != '\t' && character != (char)0x2028 && character != InlineImage.Placeholder)
                continue;

            if (i > start)
                run.Add(TextElement(text[start..i]));

            switch (character)
            {
                case '\t':
                    run.Add(new XElement(DocxNamespaces.Wordprocessing + "tab"));
                    break;
                case (char)0x2028:
                    run.Add(new XElement(DocxNamespaces.Wordprocessing + "br"));
                    break;
                default:
                    AddPicture(run, style.Image, context);
                    break;
            }

            start = i + 1;
        }

        if (start < text.Length)
            run.Add(TextElement(text[start..]));
    }

    /// <summary>
    /// Writes one picture as an inline DrawingML frame and registers its media
    /// part. A placeholder character whose run carries no image is dropped rather
    /// than written through — Word would draw it as a missing-glyph box.
    /// </summary>
    private static void AddPicture(XElement run, InlineImage? image, DocxWriteContext context)
    {
        if (image is null)
        {
            context.AddDiagnosticOnce(
                "docx.image.placeholder",
                "An object replacement character with no image attached was dropped.");
            return;
        }

        DocxImagePart part = context.GetImagePart(image);
        long widthEmus = Emus(image.Width > 0 ? image.Width : DefaultImagePoints);
        long heightEmus = Emus(image.Height > 0 ? image.Height : DefaultImagePoints);
        if (!image.HasExplicitSize)
        {
            context.AddDiagnosticOnce(
                "docx.image.size",
                "An image carried no display size and was written one inch square.");
        }

        string name = "Picture " + part.Index.ToString(CultureInfo.InvariantCulture);
        var pictureProperties = new XElement(
            DocxNamespaces.Picture + "cNvPr",
            new XAttribute("id", part.Index.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("name", name));
        var frameProperties = new XElement(
            DocxNamespaces.WordDrawing + "docPr",
            new XAttribute("id", part.Index.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("name", name));
        if (image.AltText.Length > 0)
        {
            pictureProperties.Add(new XAttribute("descr", image.AltText));
            frameProperties.Add(new XAttribute("descr", image.AltText));
        }

        var extent = new XElement(
            DocxNamespaces.Drawing + "ext",
            new XAttribute("cx", widthEmus.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("cy", heightEmus.ToString(CultureInfo.InvariantCulture)));

        run.Add(new XElement(
            DocxNamespaces.Wordprocessing + "drawing",
            new XElement(
                DocxNamespaces.WordDrawing + "inline",
                new XAttribute("distT", "0"),
                new XAttribute("distB", "0"),
                new XAttribute("distL", "0"),
                new XAttribute("distR", "0"),
                new XElement(
                    DocxNamespaces.WordDrawing + "extent",
                    new XAttribute("cx", widthEmus.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("cy", heightEmus.ToString(CultureInfo.InvariantCulture))),
                frameProperties,
                new XElement(
                    DocxNamespaces.WordDrawing + "cNvGraphicFramePr",
                    new XElement(
                        DocxNamespaces.Drawing + "graphicFrameLocks",
                        new XAttribute("noChangeAspect", "1"))),
                new XElement(
                    DocxNamespaces.Drawing + "graphic",
                    new XElement(
                        DocxNamespaces.Drawing + "graphicData",
                        new XAttribute("uri", DocxNamespaces.Picture.NamespaceName),
                        new XElement(
                            DocxNamespaces.Picture + "pic",
                            new XElement(
                                DocxNamespaces.Picture + "nvPicPr",
                                pictureProperties,
                                new XElement(DocxNamespaces.Picture + "cNvPicPr")),
                            new XElement(
                                DocxNamespaces.Picture + "blipFill",
                                new XElement(
                                    DocxNamespaces.Drawing + "blip",
                                    new XAttribute(DocxNamespaces.Relationships + "embed", part.RelationshipId)),
                                new XElement(
                                    DocxNamespaces.Drawing + "stretch",
                                    new XElement(DocxNamespaces.Drawing + "fillRect"))),
                            new XElement(
                                DocxNamespaces.Picture + "spPr",
                                new XElement(
                                    DocxNamespaces.Drawing + "xfrm",
                                    new XElement(
                                        DocxNamespaces.Drawing + "off",
                                        new XAttribute("x", "0"),
                                        new XAttribute("y", "0")),
                                    extent),
                                new XElement(
                                    DocxNamespaces.Drawing + "prstGeom",
                                    new XAttribute("prst", "rect"),
                                    new XElement(DocxNamespaces.Drawing + "avLst")))))))));
    }

    private static XElement TextElement(string value) =>
        new(
            DocxNamespaces.Wordprocessing + "t",
            new XAttribute(DocxNamespaces.Xml + "space", "preserve"),
            value);

    /// <summary>
    /// One anchored shape, as the DrawingML a word processor writes: a wps:wsp
    /// with its geometry, its fill, and its text box when it holds text.
    /// </summary>
    private static XElement BuildShapeRun(DocumentShape shape, DocxWriteContext context)
    {
        var properties = new XElement(
            DocxNamespaces.WordShape + "spPr",
            new XElement(
                DocxNamespaces.Drawing + "xfrm",
                new XElement(
                    DocxNamespaces.Drawing + "off",
                    new XAttribute("x", "0"),
                    new XAttribute("y", "0")),
                new XElement(
                    DocxNamespaces.Drawing + "ext",
                    new XAttribute("cx", PointsToEmu(shape.Width)),
                    new XAttribute("cy", PointsToEmu(shape.Height)))),
            new XElement(
                DocxNamespaces.Drawing + "prstGeom",
                new XAttribute("prst", "rect"),
                new XElement(DocxNamespaces.Drawing + "avLst")));

        if (shape.Fill is ShapeFill fill)
            properties.Add(BuildShapeFill(fill));

        properties.Add(shape.Outline.IsEmpty
            ? new XElement(DocxNamespaces.Drawing + "ln", new XElement(DocxNamespaces.Drawing + "noFill"))
            : new XElement(
                DocxNamespaces.Drawing + "ln",
                new XElement(
                    DocxNamespaces.Drawing + "solidFill",
                    new XElement(
                        DocxNamespaces.Drawing + "srgbClr",
                        new XAttribute("val", HexColor(shape.Outline))))));

        var wsp = new XElement(DocxNamespaces.WordShape + "wsp", properties);
        if (shape.HasText)
        {
            var content = new XElement(DocxNamespaces.Wordprocessing + "txbxContent");
            foreach (RichTextParagraph paragraph in shape.Paragraphs)
                content.Add(BuildParagraph(paragraph, context));

            wsp.Add(new XElement(DocxNamespaces.WordShape + "txbx", content));
            wsp.Add(new XElement(DocxNamespaces.WordShape + "bodyPr"));
        }

        var anchor = new XElement(
            DocxNamespaces.WordDrawing + "anchor",
            new XAttribute("distT", "0"),
            new XAttribute("distB", "0"),
            new XAttribute("distL", "0"),
            new XAttribute("distR", "0"),
            new XAttribute("simplePos", "0"),
            new XAttribute("relativeHeight", "2"),
            new XAttribute("behindDoc", "0"),
            new XAttribute("locked", "0"),
            new XAttribute("layoutInCell", "1"),
            new XAttribute("allowOverlap", "1"),
            new XElement(
                DocxNamespaces.WordDrawing + "simplePos",
                new XAttribute("x", "0"),
                new XAttribute("y", "0")),
            new XElement(
                DocxNamespaces.WordDrawing + "positionH",
                new XAttribute("relativeFrom", "column"),
                new XElement(DocxNamespaces.WordDrawing + "posOffset", PointsToEmu(shape.OffsetX))),
            new XElement(
                DocxNamespaces.WordDrawing + "positionV",
                new XAttribute("relativeFrom", "paragraph"),
                new XElement(DocxNamespaces.WordDrawing + "posOffset", PointsToEmu(shape.OffsetY))),
            new XElement(
                DocxNamespaces.WordDrawing + "extent",
                new XAttribute("cx", PointsToEmu(shape.Width)),
                new XAttribute("cy", PointsToEmu(shape.Height))),
            new XElement(DocxNamespaces.WordDrawing + "wrapNone"),
            new XElement(
                DocxNamespaces.WordDrawing + "docPr",
                new XAttribute("id", "1"),
                new XAttribute("name", "Shape")),
            new XElement(
                DocxNamespaces.Drawing + "graphic",
                new XElement(
                    DocxNamespaces.Drawing + "graphicData",
                    new XAttribute("uri", DocxNamespaces.WordShape.NamespaceName),
                    wsp)));

        return new XElement(
            DocxNamespaces.Wordprocessing + "r",
            new XElement(DocxNamespaces.Wordprocessing + "drawing", anchor));
    }

    private static XElement BuildShapeFill(ShapeFill fill)
    {
        if (!fill.IsGradient)
        {
            return new XElement(
                DocxNamespaces.Drawing + "solidFill",
                new XElement(
                    DocxNamespaces.Drawing + "srgbClr",
                    new XAttribute("val", HexColor(fill.Start))));
        }

        return new XElement(
            DocxNamespaces.Drawing + "gradFill",
            new XElement(
                DocxNamespaces.Drawing + "gsLst",
                new XElement(
                    DocxNamespaces.Drawing + "gs",
                    new XAttribute("pos", "0"),
                    new XElement(
                        DocxNamespaces.Drawing + "srgbClr",
                        new XAttribute("val", HexColor(fill.Start)))),
                new XElement(
                    DocxNamespaces.Drawing + "gs",
                    new XAttribute("pos", "100000"),
                    new XElement(
                        DocxNamespaces.Drawing + "srgbClr",
                        new XAttribute("val", HexColor(fill.End))))),
            new XElement(
                DocxNamespaces.Drawing + "lin",
                new XAttribute(
                    "ang",
                    ((long)Math.Round(fill.AngleDegrees * 60000)).ToString(CultureInfo.InvariantCulture))));
    }

    /// <summary>Points to twips: 20 to the point, which is what a section states.</summary>
    private static string Twips(double points) =>
        ((long)Math.Round(points * 20)).ToString(CultureInfo.InvariantCulture);

    /// <summary>Points to English Metric Units: 12700 to the point.</summary>
    private static string PointsToEmu(double points) =>
        ((long)Math.Round(points * 12700)).ToString(CultureInfo.InvariantCulture);

    private static string HexColor(BColor color) =>
        color.R.ToString("X2", CultureInfo.InvariantCulture) +
        color.G.ToString("X2", CultureInfo.InvariantCulture) +
        color.B.ToString("X2", CultureInfo.InvariantCulture);

    private static XDocument BuildContentTypes(DocxWriteContext context)
    {
        var types = new XElement(
            DocxNamespaces.ContentTypes + "Types",
            new XElement(
                DocxNamespaces.ContentTypes + "Default",
                new XAttribute("Extension", "rels"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(
                DocxNamespaces.ContentTypes + "Default",
                new XAttribute("Extension", "xml"),
                new XAttribute("ContentType", "application/xml")),
            new XElement(
                DocxNamespaces.ContentTypes + "Override",
                new XAttribute("PartName", "/word/document.xml"),
                new XAttribute("ContentType", DocxNamespaces.DocumentContentType)));

        if (context.HasNumbering)
        {
            types.Add(new XElement(
                DocxNamespaces.ContentTypes + "Override",
                new XAttribute("PartName", "/word/numbering.xml"),
                new XAttribute("ContentType", DocxNamespaces.NumberingContentType)));
        }

        foreach (DocxRunningPart part in context.RunningParts)
        {
            types.Add(new XElement(
                DocxNamespaces.ContentTypes + "Override",
                new XAttribute("PartName", "/" + part.PartPath),
                new XAttribute(
                    "ContentType",
                    part.IsHeader ? DocxNamespaces.HeaderContentType : DocxNamespaces.FooterContentType)));
        }

        // One Default per distinct media extension, which is how OPC types the
        // binary parts under word/media.
        foreach (KeyValuePair<string, string> media in context.MediaContentTypes)
        {
            types.Add(new XElement(
                DocxNamespaces.ContentTypes + "Default",
                new XAttribute("Extension", media.Key),
                new XAttribute("ContentType", media.Value)));
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), types);
    }

    private static XDocument BuildPackageRelationships() =>
        new(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(
                DocxNamespaces.PackageRelationships + "Relationships",
                new XElement(
                    DocxNamespaces.PackageRelationships + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", DocxNamespaces.OfficeDocumentRelationship),
                    new XAttribute("Target", "word/document.xml"))));

    private static XDocument BuildDocumentRelationships(DocxWriteContext context)
    {
        var root = new XElement(DocxNamespaces.PackageRelationships + "Relationships");

        if (context.HasNumbering)
        {
            root.Add(new XElement(
                DocxNamespaces.PackageRelationships + "Relationship",
                new XAttribute("Id", context.NumberingRelationshipId),
                new XAttribute("Type", DocxNamespaces.NumberingRelationship),
                new XAttribute("Target", "numbering.xml")));
        }

        foreach (KeyValuePair<string, string> relationship in context.HyperlinkRelationships.OrderBy(pair => pair.Value, StringComparer.Ordinal))
        {
            root.Add(new XElement(
                DocxNamespaces.PackageRelationships + "Relationship",
                new XAttribute("Id", relationship.Value),
                new XAttribute("Type", DocxNamespaces.HyperlinkRelationship),
                new XAttribute("Target", relationship.Key),
                new XAttribute("TargetMode", "External")));
        }

        foreach (DocxRunningPart part in context.RunningParts)
        {
            root.Add(new XElement(
                DocxNamespaces.PackageRelationships + "Relationship",
                new XAttribute("Id", part.RelationshipId),
                new XAttribute(
                    "Type",
                    part.IsHeader ? DocxNamespaces.HeaderRelationship : DocxNamespaces.FooterRelationship),
                new XAttribute("Target", part.RelativeTarget)));
        }

        foreach (DocxImagePart image in context.Images)
        {
            root.Add(new XElement(
                DocxNamespaces.PackageRelationships + "Relationship",
                new XAttribute("Id", image.RelationshipId),
                new XAttribute("Type", DocxNamespaces.ImageRelationship),
                new XAttribute("Target", image.RelativeTarget)));
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
    }

    private static XDocument BuildNumbering()
    {
        var root = new XElement(
            DocxNamespaces.Wordprocessing + "numbering",
            new XAttribute(XNamespace.Xmlns + "w", DocxNamespaces.Wordprocessing.NamespaceName));

        root.Add(BuildAbstractNumber(1, "bullet", "\u2022"));
        root.Add(BuildAbstractNumber(2, "decimal", "%1."));
        root.Add(new XElement(
            DocxNamespaces.Wordprocessing + "num",
            WordAttribute("numId", "1"),
            new XElement(DocxNamespaces.Wordprocessing + "abstractNumId", WordAttribute("val", "1"))));
        root.Add(new XElement(
            DocxNamespaces.Wordprocessing + "num",
            WordAttribute("numId", "2"),
            new XElement(DocxNamespaces.Wordprocessing + "abstractNumId", WordAttribute("val", "2"))));

        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
    }

    private static XElement BuildAbstractNumber(int id, string format, string levelText)
    {
        var abstractNum = new XElement(
            DocxNamespaces.Wordprocessing + "abstractNum",
            WordAttribute("abstractNumId", id.ToString(CultureInfo.InvariantCulture)));

        for (int level = 0; level < 9; level++)
        {
            abstractNum.Add(new XElement(
                DocxNamespaces.Wordprocessing + "lvl",
                WordAttribute("ilvl", level.ToString(CultureInfo.InvariantCulture)),
                new XElement(DocxNamespaces.Wordprocessing + "start", WordAttribute("val", "1")),
                new XElement(DocxNamespaces.Wordprocessing + "numFmt", WordAttribute("val", format)),
                new XElement(DocxNamespaces.Wordprocessing + "lvlText", WordAttribute("val", levelText)),
                new XElement(
                    DocxNamespaces.Wordprocessing + "pPr",
                    new XElement(
                        DocxNamespaces.Wordprocessing + "ind",
                        WordAttribute("left", ((level + 1) * 360).ToString(CultureInfo.InvariantCulture)),
                        WordAttribute("hanging", "360")))));
        }

        return abstractNum;
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

    private static void AddBinaryEntry(ZipArchive archive, string path, ReadOnlySpan<byte> data)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        entry.LastWriteTime = ZipTimestamp;
        using Stream stream = entry.Open();
        stream.Write(data);
    }

    private static bool IsExternalLink(string href)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out Uri? uri))
            return false;

        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase);
    }

    private static XAttribute WordAttribute(string name, string value) =>
        new(DocxNamespaces.Wordprocessing + name, value);

    private static int Twips(float points) => (int)Math.Round(points * 20f);

    /// <summary>English Metric Units for a length in points: 914400 per inch over 72 points.</summary>
    private static long Emus(double points) => (long)Math.Round(points * 12700.0);

    private static string FormatColor(BColor color, DocxWriteContext context)
    {
        if (color.A != 255)
            context.AddDiagnosticOnce("docx.color.alpha", "DOCX colors do not preserve alpha; RGB channels were written.");

        return string.Create(CultureInfo.InvariantCulture, $"{color.R:X2}{color.G:X2}{color.B:X2}");
    }

    private sealed class DocxWriteContext
    {
        private readonly Dictionary<string, string> _hyperlinks = new(StringComparer.Ordinal);
        private readonly Dictionary<InlineImage, DocxImagePart> _images = new(ReferenceEqualityComparer.Instance);
        private readonly List<DocxImagePart> _imageOrder = [];
        private readonly Dictionary<string, string> _mediaContentTypes = new(StringComparer.Ordinal);
        private readonly List<DocumentDiagnostic> _diagnostics = [];
        private readonly HashSet<string> _diagnosticOnce = new(StringComparer.Ordinal);
        private readonly List<DocxRunningPart> _runningParts = [];
        private int _nextRelationshipId;

        public DocxWriteContext(bool hasNumbering)
        {
            HasNumbering = hasNumbering;
            _nextRelationshipId = hasNumbering ? 2 : 1;
        }

        public bool HasNumbering { get; }

        public bool HasDocumentRelationships =>
            HasNumbering || _hyperlinks.Count > 0 || _imageOrder.Count > 0 || _runningParts.Count > 0;

        public string NumberingRelationshipId => "rId1";

        public IReadOnlyDictionary<string, string> HyperlinkRelationships => _hyperlinks;

        /// <summary>The header and footer parts to write, in the order they were declared.</summary>
        public IReadOnlyList<DocxRunningPart> RunningParts => _runningParts;

        /// <summary>
        /// Registers one header or footer part and returns the relationship the
        /// section properties refer to it by.
        /// </summary>
        public DocxRunningPart AddRunningPart(bool isHeader, PageSelection selection, XDocument xml)
        {
            string id = "rId" + _nextRelationshipId.ToString(CultureInfo.InvariantCulture);
            _nextRelationshipId++;

            int ordinal = _runningParts.Count(part => part.IsHeader == isHeader) + 1;
            string name = (isHeader ? "header" : "footer") + ordinal.ToString(CultureInfo.InvariantCulture) + ".xml";
            var part = new DocxRunningPart(id, "word/" + name, name, isHeader, selection, xml);
            _runningParts.Add(part);
            return part;
        }

        /// <summary>The media parts to write, in the order they were first used.</summary>
        public IReadOnlyList<DocxImagePart> Images => _imageOrder;

        /// <summary>Media extension to content type, for the package's content-type defaults.</summary>
        public IReadOnlyDictionary<string, string> MediaContentTypes => _mediaContentTypes;

        public IReadOnlyList<DocumentDiagnostic> Diagnostics => _diagnostics;

        public string GetHyperlinkRelationshipId(string href)
        {
            if (_hyperlinks.TryGetValue(href, out string? existing))
                return existing;

            string id = "rId" + _nextRelationshipId.ToString(CultureInfo.InvariantCulture);
            _nextRelationshipId++;
            _hyperlinks[href] = id;
            return id;
        }

        /// <summary>
        /// The media part for <paramref name="image"/>, created on first use.
        /// Keyed by identity, so a document that shows the same image object in
        /// several places stores its bytes once.
        /// </summary>
        public DocxImagePart GetImagePart(InlineImage image)
        {
            if (_images.TryGetValue(image, out DocxImagePart? existing))
                return existing;

            int index = _imageOrder.Count + 1;
            string extension = DocxImageFormats.ExtensionForContentType(image.ContentType);
            string fileName = "image" + index.ToString(CultureInfo.InvariantCulture) + "." + extension;
            var part = new DocxImagePart(
                index,
                "rId" + _nextRelationshipId.ToString(CultureInfo.InvariantCulture),
                "word/media/" + fileName,
                "media/" + fileName,
                image.Data);
            _nextRelationshipId++;
            _images[image] = part;
            _imageOrder.Add(part);
            _mediaContentTypes[extension] = image.ContentType;
            return part;
        }

        public void AddDiagnosticOnce(string code, string message)
        {
            if (_diagnosticOnce.Add(code))
                _diagnostics.Add(DocumentDiagnostic.Warning(code, message));
        }
    }

    /// <summary>One <c>word/media</c> part: where it lives and how the document refers to it.</summary>
    private sealed record DocxImagePart(
        int Index,
        string RelationshipId,
        string PartPath,
        string RelativeTarget,
        ReadOnlyMemory<byte> Data);

    /// <summary>One header or footer part: its own XML, plus how the section refers to it.</summary>
    private sealed record DocxRunningPart(
        string RelationshipId,
        string PartPath,
        string RelativeTarget,
        bool IsHeader,
        PageSelection Selection,
        XDocument Xml);
}
