using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Broiler.Documents.Model;
using Broiler.Graphics;

namespace Broiler.Documents.Docx;

internal static class DocxReader
{
    public static DocumentReadResult Read(byte[] bytes, DocumentReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<DocumentDiagnostic>();
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            string documentPart = FindMainDocumentPart(archive, options.Limits, diagnostics) ?? "word/document.xml";
            ZipArchiveEntry? documentEntry = DocxPackage.FindEntry(archive, documentPart);
            if (documentEntry is null)
            {
                diagnostics.Add(DocumentDiagnostic.Error(
                    "docx.package.document",
                    "DOCX package did not contain a main word/document.xml part."));
                return new DocumentReadResult(RichTextDocument.Empty, diagnostics, DocumentResultStatus.Rejected);
            }

            XDocument? documentXml = DocxPackage.LoadEntryXml(documentEntry, options.Limits, diagnostics, "docx.document.xml");
            if (documentXml is null)
                return new DocumentReadResult(RichTextDocument.Empty, diagnostics, DocumentResultStatus.Rejected);

            string baseDirectory = DocxPackage.BasePartDirectory(documentPart);
            DocxRelationships documentRelationships = DocxPackage.ReadRelationships(
                archive,
                DocxPackage.RelationshipsPartPath(documentPart),
                baseDirectory,
                options.Limits,
                diagnostics);
            DocxNumbering numbering = DocxNumbering.Load(
                archive,
                documentRelationships,
                baseDirectory,
                options.Limits,
                diagnostics);
            DocxStyles styles = DocxStyles.Load(
                archive,
                documentRelationships,
                baseDirectory,
                options.Limits,
                diagnostics);

            var images = new DocxImageLoader(archive, options.Limits);
            var reported = new HashSet<string>(StringComparer.Ordinal);
            RichTextDocument document = ReadDocumentXml(
                documentXml,
                documentRelationships,
                numbering,
                styles,
                images,
                options.Limits,
                diagnostics,
                reported);
            var partShapes = new List<DocumentShape>();
            document = document.WithRunningContent(ReadRunningContent(
                archive,
                documentXml,
                documentRelationships,
                baseDirectory,
                numbering,
                styles,
                images,
                options.Limits,
                diagnostics,
                reported,
                partShapes));
            document = document.WithPageGeometry(ReadPageGeometry(documentXml, diagnostics));
            if (partShapes.Count > 0)
                // A header shape is page decoration, so it belongs behind the
                // shapes the body anchors - a stripe painted over a logo box
                // would hide it.
                document = document.WithShapes([.. partShapes, .. document.Shapes]);
            return new DocumentReadResult(document, diagnostics, DocumentReadResult.StatusFrom(diagnostics));
        }
        catch (InvalidDataException ex)
        {
            diagnostics.Add(DocumentDiagnostic.Error(
                "docx.package.zip",
                "DOCX ZIP package could not be opened: " + ex.GetType().Name + "."));
            return new DocumentReadResult(RichTextDocument.Empty, diagnostics, DocumentResultStatus.Rejected);
        }
        catch (XmlException ex)
        {
            diagnostics.Add(DocumentDiagnostic.Error(
                "docx.xml",
                "DOCX XML could not be parsed: " + ex.GetType().Name + "."));
            return new DocumentReadResult(RichTextDocument.Empty, diagnostics, DocumentResultStatus.Rejected);
        }
    }

    private static RichTextDocument ReadDocumentXml(
        XDocument documentXml,
        DocxRelationships relationships,
        DocxNumbering numbering,
        DocxStyles styles,
        DocxImageLoader images,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics,
        HashSet<string> reported)
    {
        XElement? body = documentXml.Root?.Element(DocxNamespaces.Wordprocessing + "body");
        if (body is null)
        {
            diagnostics.Add(DocumentDiagnostic.Error(
                "docx.document.body",
                "DOCX document.xml did not contain a WordprocessingML body."));
            return RichTextDocument.Empty;
        }

        var builder = new DocxDocumentBuilder(limits, diagnostics, reported);
        var context = new DocxReadContext(relationships, numbering, styles, images, builder);
        ReadBlockContent(body.Elements(), context, depth: 0);
        builder.ReportReadSummary(body.Elements().Any(IsContentBlock), styles.Count, images.ImageCount);
        return builder.Build();
    }

    /// <summary>Everything a read needs to turn one element into document content.</summary>
    private sealed record DocxReadContext(
        DocxRelationships Relationships,
        DocxNumbering Numbering,
        DocxStyles Styles,
        DocxImageLoader Images,
        DocxDocumentBuilder Builder);

    /// <summary>
    /// Walks WordprocessingML block-level content — the <c>EG_BlockLevelElts</c>
    /// group. Paragraphs are the only shape <see cref="RichTextDocument"/> can
    /// represent, so tables and other block containers are flattened into their
    /// paragraphs in document order rather than dropped. Anything genuinely not
    /// understood raises a diagnostic instead of vanishing.
    /// </summary>
    private static void ReadBlockContent(
        IEnumerable<XElement> elements,
        DocxReadContext context,
        int depth)
    {
        DocxDocumentBuilder builder = context.Builder;
        if (depth > builder.Limits.MaxGroupDepth)
        {
            builder.AddDiagnosticOnce(
                "docx.limit.depth",
                "DOCX block nesting exceeded MaxGroupDepth; the deepest content was skipped.");
            return;
        }

        foreach (XElement element in elements)
        {
            XName name = element.Name;

            if (name == DocxNamespaces.Wordprocessing + "p")
            {
                ReadParagraph(element, context);
                continue;
            }

            if (name == DocxNamespaces.Wordprocessing + "tbl")
            {
                ReadTable(element, context, depth + 1);
                continue;
            }

            // A block-level structured document tag (content control) wraps real
            // block content in w:sdtContent; the surrounding w:sdtPr is metadata.
            if (name == DocxNamespaces.Wordprocessing + "sdt")
            {
                XElement? content = element.Element(DocxNamespaces.Wordprocessing + "sdtContent");
                if (content is not null)
                    ReadBlockContent(content.Elements(), context, depth + 1);
                continue;
            }

            // Accepted revisions and move destinations are live content; the
            // deleted/moved-from side is not.
            if (name == DocxNamespaces.Wordprocessing + "ins" ||
                name == DocxNamespaces.Wordprocessing + "moveTo" ||
                name == DocxNamespaces.Wordprocessing + "customXml" ||
                name == DocxNamespaces.Wordprocessing + "smartTag")
            {
                ReadBlockContent(element.Elements(), context, depth + 1);
                continue;
            }

            if (name == DocxNamespaces.Wordprocessing + "del" ||
                name == DocxNamespaces.Wordprocessing + "moveFrom")
            {
                builder.AddDiagnosticOnce("docx.revision.delete", "Deleted revision content was skipped.");
                continue;
            }

            if (name == DocxNamespaces.MarkupCompatibility + "AlternateContent")
            {
                ReadAlternateContent(element, context, depth + 1);
                continue;
            }

            if (IsIgnorableBlock(name))
                continue;

            builder.AddUnsupportedBlock(name);
        }
    }

    /// <summary>
    /// Flattens a table into the paragraphs of its cells, read left-to-right and
    /// top-to-bottom. <see cref="RichTextDocument"/> has no table shape, so the
    /// alternative would be losing every word the table holds — which is exactly
    /// what a CV or letterhead template puts there.
    /// </summary>
    private static void ReadTable(XElement table, DocxReadContext context, int depth)
    {
        context.Builder.NoteTable();
        foreach (XElement row in EnumerateTableChildren(table, "tr"))
        {
            foreach (XElement cell in EnumerateTableChildren(row, "tc"))
                ReadBlockContent(cell.Elements(), context, depth + 1);
        }
    }

    /// <summary>
    /// Yields the <paramref name="localName"/> children of a table or row,
    /// looking through the content controls and revision markers Word may wrap
    /// rows and cells in.
    /// </summary>
    private static IEnumerable<XElement> EnumerateTableChildren(XElement parent, string localName)
    {
        foreach (XElement child in parent.Elements())
        {
            if (child.Name == DocxNamespaces.Wordprocessing + localName)
            {
                yield return child;
                continue;
            }

            if (child.Name == DocxNamespaces.Wordprocessing + "sdt")
            {
                XElement? content = child.Element(DocxNamespaces.Wordprocessing + "sdtContent");
                if (content is null)
                    continue;

                foreach (XElement nested in EnumerateTableChildren(content, localName))
                    yield return nested;
                continue;
            }

            if (child.Name == DocxNamespaces.Wordprocessing + "ins" ||
                child.Name == DocxNamespaces.Wordprocessing + "customXml")
            {
                foreach (XElement nested in EnumerateTableChildren(child, localName))
                    yield return nested;
            }
        }
    }

    /// <summary>
    /// Reads an <c>mc:AlternateContent</c> block: the first <c>mc:Choice</c>,
    /// falling back to <c>mc:Fallback</c>. Only one branch may contribute, or the
    /// same content would be read twice.
    /// </summary>
    private static void ReadAlternateContent(XElement alternate, DocxReadContext context, int depth)
    {
        XElement? branch =
            alternate.Element(DocxNamespaces.MarkupCompatibility + "Choice") ??
            alternate.Element(DocxNamespaces.MarkupCompatibility + "Fallback");
        if (branch is not null)
            ReadBlockContent(branch.Elements(), context, depth + 1);
    }

    /// <summary>Block-level elements that carry no text and are skipped silently.</summary>
    private static bool IsIgnorableBlock(XName name)
    {
        if (name.Namespace != DocxNamespaces.Wordprocessing)
            return false;

        return name.LocalName is
            "sectPr" or
            "tblPr" or "tblGrid" or "trPr" or "tcPr" or "sdtPr" or "sdtEndPr" or
            "bookmarkStart" or "bookmarkEnd" or
            "commentRangeStart" or "commentRangeEnd" or
            "proofErr" or "permStart" or "permEnd";
    }

    /// <summary>True when a body child could contribute text, used to tell an empty document from a dropped one.</summary>
    private static bool IsContentBlock(XElement element) =>
        element.Name != DocxNamespaces.Wordprocessing + "sectPr";

    /// <summary>
    /// Reads the header and footer parts the body's section properties name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the body-level <c>w:sectPr</c> is read - the document's final
    /// section. A document split into sections by a <c>w:sectPr</c> inside a
    /// paragraph can give each section its own header, and the model has one set,
    /// so the last section's wins and the rest raise a diagnostic rather than
    /// being silently preferred or silently lost.
    /// </para>
    /// <para>
    /// Each part carries its own relationships, so a picture in a header resolves
    /// against <c>word/_rels/header1.xml.rels</c> and not against the document's.
    /// </para>
    /// </remarks>
    private static RunningContent ReadRunningContent(
        ZipArchive archive,
        XDocument documentXml,
        DocxRelationships documentRelationships,
        string baseDirectory,
        DocxNumbering numbering,
        DocxStyles styles,
        DocxImageLoader images,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics,
        HashSet<string> reported,
        List<DocumentShape> partShapes)
    {
        XElement? body = documentXml.Root?.Element(DocxNamespaces.Wordprocessing + "body");
        XElement? sectPr = body?.Elements(DocxNamespaces.Wordprocessing + "sectPr").LastOrDefault();
        if (sectPr is null)
            return RunningContent.Empty;

        if (body!.Descendants(DocxNamespaces.Wordprocessing + "pPr")
                .Any(pPr => pPr.Element(DocxNamespaces.Wordprocessing + "sectPr") is not null))
        {
            diagnostics.Add(DocumentDiagnostic.Info(
                "docx.section.multiple",
                "DOCX document has more than one section; the last section's header and footer were read."));
        }

        RunningContent content = RunningContent.Empty;
        foreach (XElement reference in sectPr.Elements())
        {
            bool isHeader = reference.Name == DocxNamespaces.Wordprocessing + "headerReference";
            bool isFooter = reference.Name == DocxNamespaces.Wordprocessing + "footerReference";
            if (!isHeader && !isFooter)
                continue;

            string? relationshipId = (string?)reference.Attribute(DocxNamespaces.Relationships + "id");
            if (string.IsNullOrWhiteSpace(relationshipId) ||
                !documentRelationships.TryGet(relationshipId, out DocxRelationship? relationship) ||
                relationship is null)
            {
                diagnostics.Add(DocumentDiagnostic.Warning(
                    "docx.part.reference",
                    "A DOCX header or footer named a relationship the package does not define."));
                continue;
            }

            IReadOnlyList<RichTextParagraph>? paragraphs = ReadPartParagraphs(
                archive, relationship.Target, numbering, styles, images, limits, diagnostics, reported, partShapes);
            if (paragraphs is null)
                continue;

            PageSelection selection = SelectionFor((string?)reference.Attribute(DocxNamespaces.Wordprocessing + "type"));
            content = isHeader
                ? content.WithHeader(selection, paragraphs)
                : content.WithFooter(selection, paragraphs);
        }

        return content;
    }

    /// <summary>
    /// Reads the page the document says it is written for from its section
    /// properties.
    /// </summary>
    /// <remarks>
    /// The body-level w:sectPr is the document's final section. A document split
    /// into several can give each its own page, and the model holds one, so the
    /// last one wins - the same choice the header and footer reader makes, and
    /// for the same reason: it is the section the running content belongs to.
    /// Geometry a producer states nonsensically is dropped rather than honoured.
    /// </remarks>
    private static PageGeometry? ReadPageGeometry(
        XDocument documentXml,
        List<DocumentDiagnostic> diagnostics)
    {
        XElement? sectPr = documentXml.Root
            ?.Element(DocxNamespaces.Wordprocessing + "body")
            ?.Elements(DocxNamespaces.Wordprocessing + "sectPr")
            .LastOrDefault();

        XElement? size = sectPr?.Element(DocxNamespaces.Wordprocessing + "pgSz");
        if (size is null)
            return null;

        XElement? margin = sectPr!.Element(DocxNamespaces.Wordprocessing + "pgMar");
        var geometry = new PageGeometry(
            Twips(size, "w"),
            Twips(size, "h"),
            Twips(margin, "left"),
            Twips(margin, "right"),
            Twips(margin, "top"),
            Twips(margin, "bottom"),
            Twips(margin, "header"),
            Twips(margin, "footer"));

        if (geometry.IsUsable)
            return geometry;

        diagnostics.Add(DocumentDiagnostic.Warning(
            "docx.section.geometry",
            "DOCX section properties gave a page with no room to write on; the page was not read."));
        return null;
    }

    /// <summary>A twip attribute in points: 20 twips to the point.</summary>
    private static double Twips(XElement? element, string name) =>
        TryReadInt(element?.Attribute(DocxNamespaces.Wordprocessing + name), out int twips)
            ? twips / 20d
            : 0;

    /// <summary>ECMA-376 §17.10.1: the header/footer types, defaulting to the one for every page.</summary>
    private static PageSelection SelectionFor(string? type) => type switch
    {
        "first" => PageSelection.First,
        "even" => PageSelection.Even,
        _ => PageSelection.Default,
    };

    /// <summary>Reads one header or footer part into paragraphs, or null when it holds nothing.</summary>
    private static IReadOnlyList<RichTextParagraph>? ReadPartParagraphs(
        ZipArchive archive,
        string partPath,
        DocxNumbering numbering,
        DocxStyles styles,
        DocxImageLoader images,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics,
        HashSet<string> reported,
        List<DocumentShape> partShapes)
    {
        ZipArchiveEntry? entry = DocxPackage.FindEntry(archive, partPath);
        if (entry is null)
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                "docx.part.missing",
                "A DOCX header or footer part named by the section was not in the package."));
            return null;
        }

        XDocument? partXml = DocxPackage.LoadEntryXml(entry, limits, diagnostics, "docx.part.xml");
        if (partXml?.Root is null)
            return null;

        DocxRelationships partRelationships = DocxPackage.ReadRelationships(
            archive,
            DocxPackage.RelationshipsPartPath(partPath),
            DocxPackage.BasePartDirectory(partPath),
            limits,
            diagnostics);

        var builder = new DocxDocumentBuilder(limits, diagnostics, reported);
        var context = new DocxReadContext(partRelationships, numbering, styles, images, builder);
        ReadBlockContent(partXml.Root.Elements(), context, depth: 0);

        RichTextDocument part = builder.Build();
        // A letterhead keeps its coloured stripe in the header, so a header's
        // shapes are the ones most worth having. RunningContent holds paragraphs
        // only, so they are handed back to the body to anchor - which is an
        // approximation the reader says out loud rather than making quietly.
        foreach (DocumentShape shape in part.Shapes)
        {
            partShapes.Add(shape);
            diagnostics.Add(DocumentDiagnostic.Info(
                "docx.shape.fromheader",
                "A shape in a DOCX header or footer was anchored to the start of the body; " +
                "the model places a shape against a paragraph, not against the page."));
        }

        IReadOnlyList<RichTextParagraph> paragraphs = part.Paragraphs;
        return paragraphs.Count == 0 ? null : paragraphs;
    }

    private static void ReadParagraph(XElement paragraph, DocxReadContext context)
    {
        XElement? pPr = paragraph.Element(DocxNamespaces.Wordprocessing + "pPr");
        string? paragraphStyleId = WordValue(pPr?.Element(DocxNamespaces.Wordprocessing + "pStyle"));

        ParagraphStyle paragraphStyle = ReadParagraphStyle(pPr, paragraphStyleId, context);
        context.Builder.StartParagraph(paragraphStyle);
        ReadParagraphChildren(paragraph.Elements(), context, ReadInheritedRunStyle(paragraphStyleId, context));
        context.Builder.FinishParagraph();
    }

    /// <summary>
    /// The character formatting a run inherits before its own <c>w:rPr</c>:
    /// document defaults followed by the paragraph style chain's run properties.
    /// </summary>
    private static InlineStyle ReadInheritedRunStyle(string? paragraphStyleId, DocxReadContext context)
    {
        InlineStyle style = InlineStyle.Default;
        foreach (XElement rPr in context.Styles.RunPropertiesForParagraph(paragraphStyleId))
            style = ApplyRunProperties(rPr, style, context.Styles.Theme);

        return style;
    }

    private static void ReadParagraphChildren(
        IEnumerable<XElement> elements,
        DocxReadContext context,
        InlineStyle style)
    {
        foreach (XElement element in elements)
        {
            if (element.Name == DocxNamespaces.Wordprocessing + "pPr")
                continue;

            if (element.Name == DocxNamespaces.Wordprocessing + "r")
            {
                ReadRun(element, context, style);
                continue;
            }

            if (element.Name == DocxNamespaces.Wordprocessing + "hyperlink")
            {
                InlineStyle hyperlinkStyle = ApplyHyperlinkStyle(element, context.Relationships, style, context.Builder);
                ReadParagraphChildren(element.Elements(), context, hyperlinkStyle);
                continue;
            }

            if (element.Name == DocxNamespaces.Wordprocessing + "del")
            {
                context.Builder.AddDiagnosticOnce("docx.revision.delete", "Deleted revision content was skipped.");
                continue;
            }

            if (element.Name == DocxNamespaces.Wordprocessing + "sdt")
            {
                XElement? content = element.Element(DocxNamespaces.Wordprocessing + "sdtContent");
                if (content is not null)
                    ReadParagraphChildren(content.Elements(), context, style);
                continue;
            }

            if (element.HasElements)
                ReadParagraphChildren(element.Elements(), context, style);
        }
    }

    private static void ReadRun(XElement run, DocxReadContext context, InlineStyle inherited)
    {
        DocxDocumentBuilder builder = context.Builder;
        XElement? rPr = run.Element(DocxNamespaces.Wordprocessing + "rPr");

        // ECMA-376 §17.7.2: the character style named by w:rStyle applies over
        // the paragraph's inherited formatting, and the run's own w:rPr over that.
        InlineStyle style = inherited;
        string? characterStyleId = WordValue(rPr?.Element(DocxNamespaces.Wordprocessing + "rStyle"));
        foreach (XElement styleRunProperties in context.Styles.RunPropertiesForCharacterStyle(characterStyleId))
            style = ApplyRunProperties(styleRunProperties, style, context.Styles.Theme);

        style = ApplyRunProperties(rPr, style, context.Styles.Theme);
        ReadRunContent(run.Elements(), context, style);
    }

    /// <summary>
    /// Reads the content children of a run, with the run's resolved character
    /// style already in hand.
    /// </summary>
    private static void ReadRunContent(
        IEnumerable<XElement> elements,
        DocxReadContext context,
        InlineStyle style)
    {
        DocxDocumentBuilder builder = context.Builder;
        foreach (XElement child in elements)
        {
            if (child.Name == DocxNamespaces.Wordprocessing + "rPr")
                continue;

            if (child.Name == DocxNamespaces.Wordprocessing + "t")
            {
                builder.AppendText(child.Value, style);
                continue;
            }

            if (child.Name == DocxNamespaces.Wordprocessing + "tab")
            {
                builder.AppendText("\t", style);
                continue;
            }

            if (child.Name == DocxNamespaces.Wordprocessing + "br" ||
                child.Name == DocxNamespaces.Wordprocessing + "cr")
            {
                builder.AppendText(((char)0x2028).ToString(), style);
                continue;
            }

            if (child.Name == DocxNamespaces.Wordprocessing + "noBreakHyphen")
            {
                builder.AppendText("\u2011", style);
                continue;
            }

            if (child.Name == DocxNamespaces.Wordprocessing + "softHyphen")
            {
                builder.AppendText("\u00AD", style);
                continue;
            }

            if (child.Name == DocxNamespaces.Wordprocessing + "delText")
            {
                builder.AddDiagnosticOnce("docx.revision.delete", "Deleted revision content was skipped.");
                continue;
            }

            if (child.Name == DocxNamespaces.Wordprocessing + "drawing" ||
                child.Name == DocxNamespaces.Wordprocessing + "pict")
            {
                ReadPicture(child, context, style);
                continue;
            }

            // Word wraps a picture that needs a legacy fallback in
            // mc:AlternateContent. Exactly one branch may contribute, or the same
            // picture is read twice.
            if (child.Name == DocxNamespaces.MarkupCompatibility + "AlternateContent")
            {
                XElement? branch =
                    child.Element(DocxNamespaces.MarkupCompatibility + "Choice") ??
                    child.Element(DocxNamespaces.MarkupCompatibility + "Fallback");
                if (branch is not null)
                    ReadRunContent(branch.Elements(), context, style);
                continue;
            }

            if (child.Name == DocxNamespaces.Wordprocessing + "object")
                builder.AddDiagnosticOnce("docx.skip.embedded", "Embedded DOCX object content was skipped.");
        }
    }

    /// <summary>
    /// Appends a picture as one <see cref="InlineImage.Placeholder"/> character
    /// carrying the image on its style. The run's own character formatting is
    /// kept on it, so a picture inside a hyperlink stays inside that link.
    /// </summary>
    private static void ReadPicture(XElement picture, DocxReadContext context, InlineStyle style)
    {
        // A w:drawing is either a picture or a shape. A shape reports nothing when
        // it does not match, so a drawing that is neither still reaches the picture
        // path and raises its diagnostic.
        if (ReadShape(picture, context))
            return;

        InlineImage? image = context.Images.Read(picture, context.Relationships, context.Builder);
        if (image is null)
            return;

        context.Builder.AppendText(InlineImage.PlaceholderText, style with { Image = image });
    }

    /// <summary>
    /// Reads a <c>wps:wsp</c> shape - the coloured box and the text box a
    /// letterhead template is built from - into a floating shape beside the body.
    /// </summary>
    /// <remarks>
    /// The anchor offsets are relative to the text column, which is what the model
    /// records, so a stripe in the left margin needs only a negative offset rather
    /// than any page geometry. A shape holding neither paint nor text is left for
    /// the picture path to report.
    /// </remarks>
    private static bool ReadShape(XElement drawing, DocxReadContext context)
    {
        XElement? wsp = drawing.Descendants(DocxNamespaces.WordShape + "wsp").FirstOrDefault();
        if (wsp is null)
            return false;

        XElement? anchor =
            drawing.Element(DocxNamespaces.WordDrawing + "anchor") ??
            drawing.Element(DocxNamespaces.WordDrawing + "inline");
        if (anchor is null)
            return false;

        XElement? extent = anchor.Element(DocxNamespaces.WordDrawing + "extent");
        double width = EmuToPoints((string?)extent?.Attribute("cx"));
        double height = EmuToPoints((string?)extent?.Attribute("cy"));
        if (width <= 0 || height <= 0)
            return false;

        XElement? spPr = wsp.Element(DocxNamespaces.WordShape + "spPr");
        ShapeFill? fill = ReadFill(spPr, context.Builder);
        IReadOnlyList<RichTextParagraph> paragraphs = ReadShapeText(wsp, context);
        if (fill is null && paragraphs.Count == 0)
            return false;

        context.Builder.AddShape(new DocumentShape(
            context.Builder.CurrentParagraphIndex,
            EmuToPoints(PositionOffset(anchor, "positionH")),
            EmuToPoints(PositionOffset(anchor, "positionV")),
            width,
            height,
            fill,
            ReadOutline(spPr),
            paragraphs));
        return true;
    }

    private static string? PositionOffset(XElement anchor, string axis) =>
        (string?)anchor.Element(DocxNamespaces.WordDrawing + axis)
            ?.Element(DocxNamespaces.WordDrawing + "posOffset");

    /// <summary>English Metric Units to points: 12700 EMU to the point.</summary>
    private static double EmuToPoints(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long emu)
            ? emu / 12700d
            : 0;

    private static ShapeFill? ReadFill(XElement? spPr, DocxDocumentBuilder builder)
    {
        if (spPr is null)
            return null;

        XElement? gradient = spPr.Element(DocxNamespaces.Drawing + "gradFill");
        if (gradient is not null)
        {
            List<XElement> stops = gradient
                .Elements(DocxNamespaces.Drawing + "gsLst")
                .Elements(DocxNamespaces.Drawing + "gs")
                .ToList();
            if (stops.Count > 2)
            {
                builder.AddDiagnosticOnce(
                    "docx.shape.gradient",
                    "A DOCX shape gradient had more than two stops; the first and last were kept.");
            }

            if (stops.Count >= 2 &&
                TryReadShapeColor(stops[0], out BColor start) &&
                TryReadShapeColor(stops[^1], out BColor end))
            {
                double angle = long.TryParse(
                    (string?)gradient.Element(DocxNamespaces.Drawing + "lin")?.Attribute("ang"),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long ang)
                    ? ang / 60000d
                    : 0;
                return new ShapeFill(start, end, angle);
            }
        }

        XElement? solid = spPr.Element(DocxNamespaces.Drawing + "solidFill");
        return solid is not null && TryReadShapeColor(solid, out BColor color)
            ? ShapeFill.Solid(color)
            : null;
    }

    private static BColor ReadOutline(XElement? spPr)
    {
        XElement? line = spPr?.Element(DocxNamespaces.Drawing + "ln");
        if (line is null || line.Element(DocxNamespaces.Drawing + "noFill") is not null)
            return BColor.Empty;

        XElement? solid = line.Element(DocxNamespaces.Drawing + "solidFill");
        return solid is not null && TryReadShapeColor(solid, out BColor color) ? color : BColor.Empty;
    }

    private static bool TryReadShapeColor(XElement parent, out BColor color) =>
        TryParseHexColor(
            (string?)parent.Element(DocxNamespaces.Drawing + "srgbClr")?.Attribute("val"),
            out color);

    /// <summary>A text box paragraphs, read with the same walker the body uses.</summary>
    private static IReadOnlyList<RichTextParagraph> ReadShapeText(XElement wsp, DocxReadContext context)
    {
        XElement? content = wsp
            .Descendants(DocxNamespaces.Wordprocessing + "txbxContent")
            .FirstOrDefault();
        if (content is null)
            return [];

        var builder = new DocxDocumentBuilder(
            context.Builder.Limits,
            context.Builder.Diagnostics,
            context.Builder.Reported);
        var nested = new DocxReadContext(
            context.Relationships, context.Numbering, context.Styles, context.Images, builder);
        ReadBlockContent(content.Elements(), nested, depth: 0);

        IReadOnlyList<RichTextParagraph> paragraphs = builder.Build().Paragraphs;
        return paragraphs.Count == 1 && paragraphs[0].Length == 0 ? [] : paragraphs;
    }

    /// <summary>
    /// Resolves a paragraph's style: document defaults, then the
    /// <c>w:pStyle</c> chain from its root down, then the paragraph's own
    /// <c>w:pPr</c>. A template paragraph often carries no direct formatting at
    /// all, so skipping the chain leaves every heading looking like body text.
    /// </summary>
    private static ParagraphStyle ReadParagraphStyle(
        XElement? pPr,
        string? paragraphStyleId,
        DocxReadContext context)
    {
        ParagraphStyle style = ParagraphStyle.Default;
        foreach (XElement styleParagraphProperties in context.Styles.ParagraphProperties(paragraphStyleId))
            style = ApplyParagraphProperties(styleParagraphProperties, context.Numbering, style);

        return ApplyParagraphProperties(pPr, context.Numbering, style);
    }

    private static ParagraphStyle ApplyParagraphProperties(
        XElement? pPr,
        DocxNumbering numbering,
        ParagraphStyle style)
    {
        if (pPr is null)
            return style;

        // A w:jc names an alignment outright, including the default one. Leaving
        // "left" to fall through would make it mean "whatever was inherited",
        // so a paragraph that says left inside a right-aligned style would stay
        // right-aligned - the one case where direct formatting is ignored. An
        // absent w:jc, and only an absent one, keeps what the style chain set.
        XElement? jc = pPr.Element(DocxNamespaces.Wordprocessing + "jc");
        string? alignment = WordValue(jc);
        style = alignment switch
        {
            "left" or "start" => style with { Alignment = TextAlignment.Left },
            "center" => style with { Alignment = TextAlignment.Center },
            "right" or "end" => style with { Alignment = TextAlignment.Right },
            "both" or "distribute" => style with { Alignment = TextAlignment.Justify },
            _ => style,
        };

        XElement? spacing = pPr.Element(DocxNamespaces.Wordprocessing + "spacing");
        if (spacing is not null)
        {
            if (TryReadTwips(spacing.Attribute(DocxNamespaces.Wordprocessing + "before"), out float before))
                style = style with { SpacingBefore = before };
            if (TryReadTwips(spacing.Attribute(DocxNamespaces.Wordprocessing + "after"), out float after))
                style = style with { SpacingAfter = after };

            string? lineRule = (string?)spacing.Attribute(DocxNamespaces.Wordprocessing + "lineRule");
            if ((lineRule is null || lineRule == "auto") &&
                TryReadInt(spacing.Attribute(DocxNamespaces.Wordprocessing + "line"), out int line) &&
                line > 0)
            {
                style = style with { LineSpacing = line / 240f };
            }
        }

        XElement? ind = pPr.Element(DocxNamespaces.Wordprocessing + "ind");
        if (ind is not null && TryReadStartIndent(ind, out int startTwips))
            style = style with { IndentLevel = Math.Max(style.IndentLevel, (int)Math.Round(startTwips / 360f)) };

        XElement? numPr = pPr.Element(DocxNamespaces.Wordprocessing + "numPr");
        if (numPr is not null)
        {
            bool hasNumId = TryReadInt(
                numPr.Element(DocxNamespaces.Wordprocessing + "numId")?.Attribute(DocxNamespaces.Wordprocessing + "val"),
                out int numId);

            // numId 0 is how a paragraph opts out of a list its style applied.
            if (hasNumId && numId == 0)
            {
                style = style with { ListKind = ListKind.None };
            }
            else
            {
                int indent = style.IndentLevel;
                if (TryReadInt(numPr.Element(DocxNamespaces.Wordprocessing + "ilvl")?.Attribute(DocxNamespaces.Wordprocessing + "val"), out int ilvl))
                    indent = Math.Max(indent, ilvl + 1);

                ListKind kind = hasNumId ? numbering.KindFor(numId) : ListKind.Bullet;
                style = style with { ListKind = kind, IndentLevel = Math.Max(1, indent) };
            }
        }

        return style;
    }

    /// <summary>
    /// Applies one <c>w:rPr</c> over an inherited style. Called once per link in
    /// the style chain and finally for the run's own properties, so only the
    /// elements actually present override what came before.
    /// </summary>
    private static InlineStyle ApplyRunProperties(XElement? rPr, InlineStyle style, DocxTheme theme)
    {
        if (rPr is null)
            return style;

        style = ApplyOnOff(rPr, "b", style, static (s, v) => s with { Bold = v });
        style = ApplyOnOff(rPr, "i", style, static (s, v) => s with { Italic = v });
        style = ApplyOnOff(rPr, "strike", style, static (s, v) => s with { Strikethrough = v });
        style = ApplyOnOff(rPr, "dstrike", style, static (s, v) => s with { Strikethrough = v });

        // w:smallCaps first: when a run turns both on, Word draws all caps. An
        // explicit "off" clears only the kind it names, so a run can drop the
        // small caps its style applied without disturbing anything else.
        style = ApplyCapitalization(rPr, "smallCaps", TextCapitalization.SmallCaps, style);
        style = ApplyCapitalization(rPr, "caps", TextCapitalization.AllCaps, style);

        XElement? underline = rPr.Element(DocxNamespaces.Wordprocessing + "u");
        if (underline is not null)
        {
            string? value = WordValue(underline);
            style = style with { Underline = !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) };
        }

        XElement? fonts = rPr.Element(DocxNamespaces.Wordprocessing + "rFonts");
        string? fontFamily =
            (string?)fonts?.Attribute(DocxNamespaces.Wordprocessing + "ascii") ??
            (string?)fonts?.Attribute(DocxNamespaces.Wordprocessing + "hAnsi") ??
            (string?)fonts?.Attribute(DocxNamespaces.Wordprocessing + "cs") ??
            (string?)fonts?.Attribute(DocxNamespaces.Wordprocessing + "eastAsia") ??
            // Styles usually name fonts through the theme rather than directly.
            theme.Resolve((string?)fonts?.Attribute(DocxNamespaces.Wordprocessing + "asciiTheme")) ??
            theme.Resolve((string?)fonts?.Attribute(DocxNamespaces.Wordprocessing + "hAnsiTheme")) ??
            theme.Resolve((string?)fonts?.Attribute(DocxNamespaces.Wordprocessing + "cstheme"));
        if (!string.IsNullOrWhiteSpace(fontFamily))
            style = style with { FontFamily = fontFamily };

        XElement? size = rPr.Element(DocxNamespaces.Wordprocessing + "sz");
        if (TryReadInt(size?.Attribute(DocxNamespaces.Wordprocessing + "val"), out int halfPoints) && halfPoints > 0)
            style = style with { FontSize = halfPoints / 2f };

        XElement? color = rPr.Element(DocxNamespaces.Wordprocessing + "color");
        string? colorValue = WordValue(color);
        if (TryParseHexColor(colorValue, out BColor foreground))
            style = style with { Foreground = foreground };

        XElement? shade = rPr.Element(DocxNamespaces.Wordprocessing + "shd");
        string? fill = (string?)shade?.Attribute(DocxNamespaces.Wordprocessing + "fill");
        if (TryParseHexColor(fill, out BColor shadeColor))
            style = style with { Background = shadeColor };

        XElement? highlight = rPr.Element(DocxNamespaces.Wordprocessing + "highlight");
        string? highlightValue = WordValue(highlight);
        if (TryParseHighlight(highlightValue, out BColor highlightColor))
            style = style with { Background = highlightColor };

        return style;
    }

    private static InlineStyle ApplyHyperlinkStyle(
        XElement hyperlink,
        DocxRelationships relationships,
        InlineStyle style,
        DocxDocumentBuilder builder)
    {
        string? id = (string?)hyperlink.Attribute(DocxNamespaces.Relationships + "id");
        string? href = null;
        if (!string.IsNullOrWhiteSpace(id) &&
            relationships.TryGet(id, out DocxRelationship? relationship) &&
            relationship is not null)
        {
            href = relationship.TargetModeExternal ? relationship.Target : null;
        }

        string? anchor = (string?)hyperlink.Attribute(DocxNamespaces.Wordprocessing + "anchor");
        if (string.IsNullOrWhiteSpace(href) && !string.IsNullOrWhiteSpace(anchor))
            href = "#" + anchor;

        if (string.IsNullOrWhiteSpace(href))
            return style;

        if (!IsAllowedLink(href))
        {
            builder.AddDiagnosticOnce("docx.link", "A hyperlink with a disallowed scheme was dropped.");
            return style;
        }

        return style with { LinkHref = href };
    }

    private static bool IsAllowedLink(string href)
    {
        if (href.StartsWith("#", StringComparison.Ordinal))
            return true;
        if (!Uri.TryCreate(href, UriKind.Absolute, out Uri? uri))
            return false;

        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase);
    }

    private static InlineStyle ApplyCapitalization(
        XElement rPr,
        string localName,
        TextCapitalization kind,
        InlineStyle style)
    {
        XElement? element = rPr.Element(DocxNamespaces.Wordprocessing + localName);
        if (element is null)
            return style;

        if (ReadOnOff(element))
            return style with { Capitalization = kind };

        // Turning one kind off leaves the other alone.
        return style.Capitalization == kind
            ? style with { Capitalization = TextCapitalization.None }
            : style;
    }

    private static InlineStyle ApplyOnOff(
        XElement parent,
        string localName,
        InlineStyle style,
        Func<InlineStyle, bool, InlineStyle> apply)
    {
        XElement? element = parent.Element(DocxNamespaces.Wordprocessing + localName);
        return element is null ? style : apply(style, ReadOnOff(element));
    }

    private static bool ReadOnOff(XElement element)
    {
        string? value = WordValue(element);
        return value is null ||
            !(value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
              value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
              value.Equals("off", StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindMainDocumentPart(
        ZipArchive archive,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        DocxRelationships packageRelationships = DocxPackage.ReadRelationships(
            archive,
            "_rels/.rels",
            string.Empty,
            limits,
            diagnostics);
        foreach (DocxRelationship relationship in packageRelationships.All)
        {
            if (relationship.Type.Equals(DocxNamespaces.OfficeDocumentRelationship, StringComparison.Ordinal))
                return relationship.Target;
        }

        return null;
    }

    private static string? WordValue(XElement? element) =>
        (string?)element?.Attribute(DocxNamespaces.Wordprocessing + "val");

    private static bool TryReadInt(XAttribute? attribute, out int value) =>
        int.TryParse((string?)attribute, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// Reads a <c>w:ind</c>'s leading indent under either of its legal names.
    /// <c>w:start</c> is the writing-direction name ISO 29500 strict gave
    /// <c>w:left</c>, and the transitional schema every .docx uses allows both.
    /// Which name a file carries is a property of the filter that wrote it, not
    /// of its content: LibreOffice writes <c>w:start</c> through its "Office
    /// Open XML Text" export and <c>w:left</c> through "Word 2007-365", and
    /// Word writes <c>w:start</c> only in strict mode. Reading one name alone
    /// silently flattens every indent the other wrote. When a file carries
    /// both, the newer name wins.
    /// </summary>
    private static bool TryReadStartIndent(XElement ind, out int twips) =>
        TryReadInt(ind.Attribute(DocxNamespaces.Wordprocessing + "start"), out twips) ||
        TryReadInt(ind.Attribute(DocxNamespaces.Wordprocessing + "left"), out twips);

    private static bool TryReadTwips(XAttribute? attribute, out float points)
    {
        points = 0f;
        if (!TryReadInt(attribute, out int twips))
            return false;

        points = twips / 20f;
        return true;
    }

    private static bool TryParseHexColor(string? value, out BColor color)
    {
        color = BColor.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            value.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
            value.Length != 6)
        {
            return false;
        }

        if (!int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            return false;

        color = BColor.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        return true;
    }

    private static bool TryParseHighlight(string? value, out BColor color)
    {
        color = value?.ToLowerInvariant() switch
        {
            "black" => BColor.Black,
            "blue" => BColor.Blue,
            "cyan" => BColor.FromArgb(0, 255, 255),
            "green" => BColor.Green,
            "magenta" => BColor.FromArgb(255, 0, 255),
            "red" => BColor.Red,
            "yellow" => BColor.FromArgb(255, 255, 0),
            "white" => BColor.White,
            "darkblue" => BColor.FromArgb(0, 0, 128),
            "darkcyan" => BColor.FromArgb(0, 128, 128),
            "darkgreen" => BColor.FromArgb(0, 100, 0),
            "darkmagenta" => BColor.FromArgb(128, 0, 128),
            "darkred" => BColor.FromArgb(128, 0, 0),
            "darkyellow" => BColor.FromArgb(128, 128, 0),
            "darkgray" => BColor.FromArgb(128, 128, 128),
            "lightgray" => BColor.FromArgb(211, 211, 211),
            _ => BColor.Empty,
        };

        return !color.IsEmpty;
    }

    private sealed class DocxDocumentBuilder : IDocxImageDiagnostics
    {
        private readonly DocumentLimits _limits;
        private readonly List<DocumentDiagnostic> _diagnostics;
        private readonly List<RichTextParagraph> _paragraphs = [];
        private readonly List<Segment> _segments = [];
        private readonly HashSet<string> _diagnosticOnce;
        private readonly List<DocumentShape> _shapes = [];
        private ParagraphStyle _paragraphStyle = ParagraphStyle.Default;
        private int _tableCount;
        private int _unsupportedBlockCount;

        /// <summary>
        /// A document is read by more than one builder - the body and each header
        /// or footer part - so the set of already-reported codes is passed in.
        /// Without it "once" would mean once per part, and a shape in the body and
        /// a shape in the header would report the same gap twice.
        /// </summary>
        public DocxDocumentBuilder(
            DocumentLimits limits,
            List<DocumentDiagnostic> diagnostics,
            HashSet<string>? reported = null)
        {
            _limits = limits;
            _diagnostics = diagnostics;
            _diagnosticOnce = reported ?? new HashSet<string>(StringComparer.Ordinal);
        }

        public DocumentLimits Limits => _limits;

        /// <summary>Shared with the builders a nested part or shape is read with.</summary>
        public List<DocumentDiagnostic> Diagnostics => _diagnostics;

        /// <summary>The codes already reported, so once means once per document.</summary>
        public HashSet<string> Reported => _diagnosticOnce;

        public void NoteTable()
        {
            _tableCount++;
            AddDiagnosticOnce(
                "docx.table.flattened",
                "DOCX tables were flattened into their cell paragraphs, in row order.");
        }

        /// <summary>
        /// Records a block-level element the reader does not understand. Keyed by
        /// element name so each distinct construct is reported once — the name is
        /// markup structure, never document text (ADR 0004 privacy rule).
        /// </summary>
        public void AddUnsupportedBlock(XName name)
        {
            _unsupportedBlockCount++;
            AddDiagnosticOnce(
                "docx.block.unsupported:" + name.LocalName,
                "docx.block.unsupported",
                "An unsupported DOCX block-level element was skipped: " + name.LocalName + ".");
        }

        /// <summary>
        /// Emits the read summary. The counts make a silent content loss visible:
        /// a body with block content that yields no paragraphs is a reader bug,
        /// not an empty file, and it should say so rather than open blank.
        /// </summary>
        public void ReportReadSummary(bool bodyHadContentBlocks, int styleCount, int imageCount)
        {
            if (_paragraphs.Count == 0 && bodyHadContentBlocks)
            {
                _diagnostics.Add(DocumentDiagnostic.Warning(
                    "docx.document.empty",
                    "DOCX body contained block-level content but produced no paragraphs."));
            }

            _diagnostics.Add(DocumentDiagnostic.Info(
                "docx.read.summary",
                "DOCX read produced " + _paragraphs.Count.ToString(CultureInfo.InvariantCulture) +
                " paragraph(s), flattened " + _tableCount.ToString(CultureInfo.InvariantCulture) +
                " table(s), loaded " + styleCount.ToString(CultureInfo.InvariantCulture) +
                " style(s), embedded " + imageCount.ToString(CultureInfo.InvariantCulture) +
                " image(s), and skipped " + _unsupportedBlockCount.ToString(CultureInfo.InvariantCulture) +
                " unsupported block(s)."));
        }

        public void StartParagraph(ParagraphStyle style)
        {
            _segments.Clear();
            _paragraphStyle = style;
        }

        public void AppendText(string text, InlineStyle style)
        {
            if (string.IsNullOrEmpty(text))
                return;

            if (text.Length > _limits.MaxRunLength)
            {
                text = text[.._limits.MaxRunLength];
                AddDiagnosticOnce("docx.limit.run", "A DOCX text run exceeded MaxRunLength and was truncated.");
            }

            if (_segments.Count > 0 && _segments[^1].Style.Equals(style))
            {
                Segment previous = _segments[^1];
                _segments[^1] = new Segment(previous.Text + text, style);
                return;
            }

            _segments.Add(new Segment(text, style));
        }

        public void FinishParagraph()
        {
            if (_paragraphs.Count >= _limits.MaxParagraphCount)
            {
                AddDiagnosticOnce("docx.limit.paragraphs", "DOCX input exceeded MaxParagraphCount; remaining paragraphs were dropped.");
                _segments.Clear();
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
            _paragraphStyle = ParagraphStyle.Default;
        }

        /// <summary>The paragraph a shape met right now would be anchored to.</summary>
        public int CurrentParagraphIndex => _paragraphs.Count;

        public void AddShape(DocumentShape shape) => _shapes.Add(shape);

        public RichTextDocument Build()
        {
            RichTextDocument document = _paragraphs.Count == 0
                ? RichTextDocument.Empty
                : RichTextDocument.FromParagraphs(_paragraphs);

            return _shapes.Count == 0 ? document : document.WithShapes(_shapes);
        }

        public void AddDiagnosticOnce(string code, string message) =>
            AddDiagnosticOnce(code, code, message);

        public void AddDiagnosticOnce(string key, string code, string message)
        {
            if (_diagnosticOnce.Add(key))
                _diagnostics.Add(DocumentDiagnostic.Warning(code, message));
        }

        private readonly record struct Segment(string Text, InlineStyle Style);
    }

    private sealed class DocxNumbering
    {
        private readonly Dictionary<int, ListKind> _numKinds;

        private DocxNumbering(Dictionary<int, ListKind> numKinds) => _numKinds = numKinds;

        public static DocxNumbering Empty { get; } = new(new Dictionary<int, ListKind>());

        public static DocxNumbering Load(
            ZipArchive archive,
            DocxRelationships documentRelationships,
            string documentBaseDirectory,
            DocumentLimits limits,
            List<DocumentDiagnostic> diagnostics)
        {
            string numberingPath = DocxPackage.ResolvePartPath(
                documentRelationships,
                DocxNamespaces.NumberingRelationship,
                documentBaseDirectory,
                "numbering.xml");

            ZipArchiveEntry? entry = DocxPackage.FindEntry(archive, numberingPath);
            if (entry is null)
                return Empty;

            XDocument? xml = DocxPackage.LoadEntryXml(entry, limits, diagnostics, "docx.numbering");
            if (xml?.Root is null)
                return Empty;

            var abstractKinds = new Dictionary<int, ListKind>();
            foreach (XElement abstractNum in xml.Root.Elements(DocxNamespaces.Wordprocessing + "abstractNum"))
            {
                if (!TryReadInt(abstractNum.Attribute(DocxNamespaces.Wordprocessing + "abstractNumId"), out int abstractId))
                    continue;

                XElement? level = abstractNum.Elements(DocxNamespaces.Wordprocessing + "lvl").FirstOrDefault();
                string? format = WordValue(level?.Element(DocxNamespaces.Wordprocessing + "numFmt"));
                abstractKinds[abstractId] = format is "decimal" or "decimalZero" or "upperRoman" or "lowerRoman" or "upperLetter" or "lowerLetter"
                    ? ListKind.Numbered
                    : ListKind.Bullet;
            }

            var numKinds = new Dictionary<int, ListKind>();
            foreach (XElement num in xml.Root.Elements(DocxNamespaces.Wordprocessing + "num"))
            {
                if (!TryReadInt(num.Attribute(DocxNamespaces.Wordprocessing + "numId"), out int numId))
                    continue;
                if (!TryReadInt(num.Element(DocxNamespaces.Wordprocessing + "abstractNumId")?.Attribute(DocxNamespaces.Wordprocessing + "val"), out int abstractId))
                    continue;
                if (abstractKinds.TryGetValue(abstractId, out ListKind kind))
                    numKinds[numId] = kind;
            }

            return new DocxNumbering(numKinds);
        }

        public ListKind KindFor(int numId) =>
            _numKinds.TryGetValue(numId, out ListKind kind) ? kind : ListKind.Bullet;
    }
}
