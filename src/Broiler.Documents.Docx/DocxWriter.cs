using Broiler.Documents.Resources;
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
using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx;

/// <summary>Serializes the rich-text document model to a minimal DOCX package.</summary>
public static class DocxWriter
{
    private static readonly DateTimeOffset ZipTimestamp = new(new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    /// <summary>The size an image with no stated display size is written at: one inch square.</summary>
    private const double DefaultImagePoints = 72.0;

    public static DocumentWriteResult Write(RichTextDocument document, Stream destination, DocumentWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        
        var context = new DocxWriteContext(
            document.Paragraphs.Any(static p => p.Style.ListKind != ListKind.None || p.Style.List is not null),
            (options ?? DocumentWriteOptions.Default).Resources);
        if (document.Sections.Any(section => section.Hidden))
            context.AddDiagnosticOnce("docx.section.hidden", "Hidden section visibility was not written; its retained content is exported as visible paragraphs.");
        BuildRunningParts(document, context);
        if (document.PageBackground?.HasExtendedPaint == true || document.Shapes.Any(shape => shape.Fill?.HasExtendedPaint == true))
            context.AddDiagnosticOnce("docx.fill.extended", "Bitmap and hatch fills, centred gradient geometry, gradient borders and page-fill bounds were approximated by their base colours or linear gradients.");
        if (document.Sections.Any(section => section.PageStyles is not null))
            context.AddDiagnosticOnce("docx.section.page-sequence", "An automatic page-style sequence was written using the first style of each section; subsequent alternating paper and margins were not written.");
        if (document.Sections.Any(section => !section.Background.IsEmpty && section.Background.A > 0))
            context.AddDiagnosticOnce("docx.section.background", "Section background colours have no direct DOCX section equivalent and were not written.");
        XDocument documentXml = BuildDocumentXml(document, context);
        XDocument? footnotesXml = BuildNotes(NoteKind.Footnote, document.NoteNumbering.RaisedLabels, context);
        XDocument? endnotesXml = BuildNotes(NoteKind.Endnote, document.NoteNumbering.RaisedLabels, context);
        if (document.NoteNumbering with { RaisedLabels = false } != NoteNumbering.Default)
            context.AddSettings(noteNumbering: document.NoteNumbering);
        if (!document.ShowsRevisions)
            context.AddSettings(hideRevisions: true);

        // The caller's metadata, never the source document's: this writer is
        // given an envelope or it emits no properties at all.
        DocumentMetadata metadata = (options ?? DocumentWriteOptions.Default).Metadata;
        XDocument? coreXml = DocxMetadata.BuildCore(metadata);
        XDocument? appXml = DocxMetadata.BuildApp(metadata);
        context.ReportMetadata(metadata);

        using var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddXmlEntry(archive, "[Content_Types].xml", BuildContentTypes(context, coreXml is not null, appXml is not null));
            AddXmlEntry(archive, "_rels/.rels", BuildPackageRelationships(coreXml is not null, appXml is not null));
            if (coreXml is not null)
                AddXmlEntry(archive, DocxMetadata.CorePart, coreXml);
            if (appXml is not null)
                AddXmlEntry(archive, DocxMetadata.AppPart, appXml);
            AddXmlEntry(archive, "word/document.xml", documentXml);
            if (context.HasDocumentRelationships)
                AddXmlEntry(archive, "word/_rels/document.xml.rels", BuildDocumentRelationships(context));
            if (context.HasNumbering)
                AddXmlEntry(archive, "word/numbering.xml", BuildNumbering(context));
            if (context.SettingsRelationshipId is not null)
                AddXmlEntry(archive, "word/settings.xml", BuildSettings(context));
            if (footnotesXml is not null)
            {
                AddXmlEntry(archive, "word/footnotes.xml", footnotesXml);
                AddXmlEntry(archive, "word/_rels/footnotes.xml.rels", BuildNoteRelationships(context));
            }

            if (endnotesXml is not null)
            {
                AddXmlEntry(archive, "word/endnotes.xml", endnotesXml);
                AddXmlEntry(archive, "word/_rels/endnotes.xml.rels", BuildNoteRelationships(context));
            }
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
    /// Turns the document's running content, and each section's own, into header
    /// and footer parts. Each part is a <c>w:hdr</c> or <c>w:ftr</c> holding the
    /// same paragraphs the body writer produces, so a header keeps its alignment,
    /// spacing and runs.
    /// </summary>
    /// <remarks>
    /// Every section's <c>w:sectPr</c> names its own parts. Where sections carry
    /// different sets, a type one set has and another lacks is written for the
    /// other as an empty part: ECMA-376 §17.10.5 continues a type a section does
    /// not name from the section before it, so leaving it out would hand the
    /// section a header it did not have.
    /// </remarks>
    private static void BuildRunningParts(RichTextDocument document, DocxWriteContext context)
    {
        var sets = new List<RunningContent>();
        foreach (RunningContent running in document.Sections
            .Select(static section => section.RunningContent)
            .OfType<RunningContent>()
            .Append(document.RunningContent))
        {
            if (!sets.Contains(running, ReferenceEqualityComparer.Instance))
                sets.Add(running);
        }

        var named = new HashSet<(bool IsHeader, PageSelection Selection)>();
        if (sets.Count > 1)
        {
            foreach (RunningContent running in sets)
            {
                foreach (PageSelection selection in new[] { PageSelection.Default, PageSelection.First, PageSelection.Even })
                {
                    if (running.Header(selection).Count > 0 || running.HeaderShapes(selection).Count > 0)
                        named.Add((true, selection));
                    if (running.Footer(selection).Count > 0 || running.FooterShapes(selection).Count > 0)
                        named.Add((false, selection));
                }
            }
        }

        foreach (RunningContent running in sets)
        {
            var parts = new List<DocxRunningPart>();
            foreach (PageSelection selection in new[] { PageSelection.Default, PageSelection.First, PageSelection.Even })
            {
                foreach (bool isHeader in new[] { true, false })
                {
                    // What the model draws where a set lacks a type: nothing for
                    // the default, nothing on a first page it makes different,
                    // and its default on an even page.
                    PageSelection source = selection;
                    bool missing = named.Contains((isHeader, selection)) &&
                        (isHeader ? running.Header(selection).Count + running.HeaderShapes(selection).Count
                                  : running.Footer(selection).Count + running.FooterShapes(selection).Count) == 0;
                    if (missing && selection == PageSelection.First && !running.DifferentFirstPage)
                        missing = false;
                    if (missing && selection == PageSelection.Even)
                        source = PageSelection.Default;

                    AddRunningPart(
                        isHeader ? running.Header(source) : running.Footer(source),
                        isHeader ? running.HeaderShapes(source) : running.FooterShapes(source),
                        isHeader, selection, context, parts, evenIfEmpty: missing);
                }
            }

            context.AddRunningSet(running, parts);
        }

        // An even part is drawn only where the settings turn even pages on, so
        // writing one without w:evenAndOddHeaders writes a part Word ignores.
        if (context.RunningParts.Any(static part => part.Selection == PageSelection.Even))
            context.AddSettings(evenAndOddHeaders: true);
    }

    /// <remarks>
    /// A shape rides in a run at the head of the part's first paragraph, which is
    /// where an anchor has to live - a w:hdr holds paragraphs, not drawings. A
    /// part that carries only a stripe therefore gets an empty paragraph to hang
    /// it from; without one there is nowhere in the part to put it. So does a
    /// part written empty to stop another section's continuing.
    /// </remarks>
    private static void AddRunningPart(
        IReadOnlyList<RichTextParagraph> paragraphs,
        IReadOnlyList<DocumentShape> shapes,
        bool isHeader,
        PageSelection selection,
        DocxWriteContext context,
        List<DocxRunningPart> parts,
        bool evenIfEmpty)
    {
        if (paragraphs.Count == 0 && shapes.Count == 0 && !evenIfEmpty)
            return;

        var root = new XElement(
            DocxNamespaces.Wordprocessing + (isHeader ? "hdr" : "ftr"),
            new XAttribute(XNamespace.Xmlns + "w", DocxNamespaces.Wordprocessing.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", DocxNamespaces.Relationships.NamespaceName));

        foreach (RichTextParagraph paragraph in paragraphs)
            root.Add(BuildParagraph(paragraph, context));

        if (root.Elements().FirstOrDefault() is not XElement first)
        {
            first = new XElement(DocxNamespaces.Wordprocessing + "p");
            root.Add(first);
        }

        foreach (DocumentShape shape in shapes)
            first.AddFirst(BuildShapeRun(shape, context, pageAnchored: true));

        parts.Add(context.AddRunningPart(isHeader, selection, new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root)));
    }

    /// <summary>
    /// A section's properties: a reference per header and footer part of
    /// <paramref name="running"/>, the section's own set or the document's.
    /// </summary>
    /// <remarks>
    /// A first-page header only takes effect when the section also says it wants a
    /// distinct one, so <c>w:titlePg</c> is written whenever a First part exists -
    /// without it Word reads the part and then draws the default over it - and
    /// whenever the model says the first page is different even though it names no
    /// part of its own. That second case is a letterhead: a first-page header, no
    /// first-page footer, and page one carrying none. Writing the references
    /// without the flag would put the default footer back under it.
    /// </remarks>
    private static XElement BuildSectionProperties(
        DocxWriteContext context,
        RunningContent running,
        PageGeometry? geometry,
        (int Count, double Gap)? columns = null,
        SectionBreakType? breakType = null,
        bool continuous = false)
    {
        var sectPr = new XElement(DocxNamespaces.Wordprocessing + "sectPr");
        bool hasFirst = false;

        foreach (DocxRunningPart part in context.RunningSet(running))
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

        // CT_SectPr is a sequence: the references, the type, the page size and
        // margins, then the columns, then w:titlePg. A consumer that validates
        // the order rejects the part otherwise, so the flag is held until the
        // geometry is written.
        if (breakType == SectionBreakType.OddPage)
            sectPr.Add(new XElement(DocxNamespaces.Wordprocessing + "type", WordAttribute("val", "oddPage")));
        else if (breakType == SectionBreakType.EvenPage)
            sectPr.Add(new XElement(DocxNamespaces.Wordprocessing + "type", WordAttribute("val", "evenPage")));
        else if (breakType == SectionBreakType.NextColumn)
            sectPr.Add(new XElement(DocxNamespaces.Wordprocessing + "type", WordAttribute("val", "nextColumn")));
        else if (continuous || breakType == SectionBreakType.Continuous)
            sectPr.Add(new XElement(DocxNamespaces.Wordprocessing + "type", WordAttribute("val", "continuous")));

        (int Count, double Gap) stated = columns ?? (geometry?.ColumnCount ?? 1, geometry?.ColumnGap ?? 0);
        if (geometry is not null && geometry.IsUsable)
        {
            var pgSz = new XElement(
                DocxNamespaces.Wordprocessing + "pgSz",
                WordAttribute("w", Twips(geometry.Width)),
                WordAttribute("h", Twips(geometry.Height)));
            if (geometry.Width > geometry.Height)
                pgSz.Add(WordAttribute("orient", "landscape"));
            sectPr.Add(pgSz);
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

        if (stated.Count > 1)
        {
            sectPr.Add(new XElement(
                DocxNamespaces.Wordprocessing + "cols",
                WordAttribute("num", stated.Count.ToString(CultureInfo.InvariantCulture)),
                WordAttribute("space", Twips(stated.Gap))));
        }

        if (hasFirst || running.DifferentFirstPage)
            sectPr.Add(new XElement(DocxNamespaces.Wordprocessing + "titlePg"));

        return sectPr;
    }

    private static XDocument BuildDocumentXml(RichTextDocument document, DocxWriteContext context)
    {
        var body = new XElement(DocxNamespaces.Wordprocessing + "body");
        foreach (XElement block in BuildBlocks(document, document.Tables, 0, document.ParagraphCount, context))
            body.Add(block);

        // The last section's columns are the body's own w:sectPr's, and it starts
        // continuously like the others, since w:type's default would start a
        // page the model did not ask for. Without sections that is the page's
        // columns and no type, as it always was.
        (int Count, double Gap)? lastColumns = document.Sections.Count == 0
            ? null
            : ColumnsAt(document, document.ParagraphCount - 1);
        DocumentSection? lastSection = DocumentSection.Containing(document.Sections, document.ParagraphCount - 1);
        SectionBreakType? lastBreak = lastSection?.BreakType;
        body.Add(BuildSectionProperties(
            context,
            RunningAt(document, document.ParagraphCount - 1),
            PageAt(document, document.ParagraphCount - 1),
            lastColumns,
            breakType: lastBreak,
            continuous: lastBreak == SectionBreakType.Continuous || (lastBreak == null && document.Sections.Count > 0)));

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

        // Before the body, as CT_Document orders it, and shown only where the
        // settings say so (ECMA-376 §17.15.1.25): a background Word is not told
        // to display is one it keeps and does not draw.
        if (document.PageBackground is ShapeFill background)
        {
            root.Add(BuildBackground(background, root));
            context.AddSettings(displayBackgroundShape: true);
        }

        root.Add(body);
        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
    }

    /// <summary>
    /// The page background as <c>w:background</c> (ECMA-376 §17.2.1): a colour
    /// alone, or for a gradient the VML shape Word draws it with, holding the
    /// colour Word falls back to beside it.
    /// </summary>
    /// <remarks>
    /// The VML is the reverse of <c>DocxVml.ReadFill</c>, which reads it the way
    /// LibreOffice does: a linear gradient starts from <c>color2</c>, a focus of
    /// 50% mirrors it with <c>fillcolor</c> at the edges, and the angle runs
    /// counter-clockwise from the bottom, a quarter turn from the model's.
    /// </remarks>
    private static XElement BuildBackground(ShapeFill fill, XElement root)
    {
        var background = new XElement(
            DocxNamespaces.Wordprocessing + "background",
            new XAttribute(DocxNamespaces.Wordprocessing + "color", HexColor(fill.ColorAt(0.5))));
        if (!fill.IsGradient)
            return background;

        root.Add(new XAttribute(XNamespace.Xmlns + "v", DocxNamespaces.Vml.NamespaceName));
        double vmlAngle = ((90 - fill.AngleDegrees) % 360 + 360) % 360;
        var vmlFill = new XElement(
            DocxNamespaces.Vml + "fill",
            new XAttribute("color2", "#" + HexColor(fill.Axial ? fill.End : fill.Start)),
            new XAttribute("angle", Math.Round(vmlAngle, 3).ToString(CultureInfo.InvariantCulture)),
            new XAttribute("type", "gradient"));
        if (fill.Axial)
            vmlFill.Add(new XAttribute("focus", "50%"));

        background.Add(new XElement(
            DocxNamespaces.Vml + "background",
            new XAttribute("fillcolor", "#" + HexColor(fill.Axial ? fill.Start : fill.End)),
            vmlFill));
        return background;
    }

    /// <summary>
    /// The block-level content of a paragraph range: paragraphs, and a table
    /// wherever one starts. A cell's content goes through here too, so a table
    /// inside a cell is written by the same walk that wrote the one around it.
    /// </summary>
    private static IEnumerable<XElement> BuildBlocks(
        RichTextDocument document,
        IReadOnlyList<DocumentTable> tables,
        int from,
        int to,
        DocxWriteContext context)
    {
        int index = from;
        while (index < to)
        {
            if (DocumentTable.StartingAt(tables, index) is DocumentTable table)
            {
                yield return BuildTable(document, table, context);
                index = table.ParagraphEnd;

                // A w:sectPr belongs in a paragraph, and the last paragraph of a
                // section that ends with a table is in a cell. Word puts an
                // empty paragraph after the table to hold it, and so does this.
                if (ReferenceEquals(tables, document.Tables) && EndsSection(document, index - 1))
                {
                    yield return new XElement(
                        DocxNamespaces.Wordprocessing + "p",
                        new XElement(DocxNamespaces.Wordprocessing + "pPr", SectionBreak(document, index - 1, context)));
                }

                continue;
            }

            XElement paragraph = BuildBodyParagraph(document, index, context);
            if (ReferenceEquals(tables, document.Tables) && EndsSection(document, index))
                PropertiesOf(paragraph).Add(SectionBreak(document, index, context));

            yield return paragraph;
            index++;
        }
    }

    /// <summary>
    /// The columns paragraph <paramref name="index"/> is laid out in: its
    /// section's, or the page's where no section holds it.
    /// </summary>
    private static (int Count, double Gap) ColumnsAt(RichTextDocument document, int index) =>
        DocumentSection.Containing(document.Sections, index) is DocumentSection section
            ? (section.ColumnCount, section.ColumnGap)
            : (document.PageGeometry?.ColumnCount ?? 1, document.PageGeometry?.ColumnGap ?? 0);

    /// <summary>
    /// The headers and footers of the section paragraph <paramref name="index"/>
    /// is in: its own, or the document's.
    /// </summary>
    private static RunningContent RunningAt(RichTextDocument document, int index) =>
        DocumentSection.Containing(document.Sections, index)?.RunningContent ?? document.RunningContent;

    /// <summary>
    /// The paper and margins paragraph <paramref name="index"/> is laid out
    /// within: its section's own, or the document's.
    /// </summary>
    private static PageGeometry? PageAt(RichTextDocument document, int index) =>
        DocumentSection.Containing(document.Sections, index)?.Page ?? document.PageGeometry;

    /// <summary>
    /// True when paragraph <paramref name="index"/> is the last of a stretch the
    /// next paragraph does not share a section with. The last paragraph of the
    /// document is not one: the body's own <c>w:sectPr</c> closes the last section.
    /// </summary>
    private static bool EndsSection(RichTextDocument document, int index) =>
        document.Sections.Count > 0 &&
        index >= 0 && index + 1 < document.ParagraphCount &&
        !ReferenceEquals(
            DocumentSection.Containing(document.Sections, index),
            DocumentSection.Containing(document.Sections, index + 1));

    /// <summary>
    /// The <c>w:sectPr</c> that closes the section paragraph
    /// <paramref name="index"/> ends: that section's page, or the document's,
    /// its running content, or the document's, that section's columns, and a
    /// continuous start for the next one. Whether
    /// the next section starts a page is its first paragraph's own
    /// <c>w:pageBreakBefore</c>, which is where the model keeps it, so stating
    /// it here as well would start two.
    /// </summary>
    private static XElement SectionBreak(RichTextDocument document, int index, DocxWriteContext context)
    {
        DocumentSection? section = DocumentSection.Containing(document.Sections, index);
        SectionBreakType? breakType = section?.BreakType;
        bool startsNextPage = index + 1 < document.ParagraphCount && document.Paragraphs[index + 1].Style.PageBreakBefore;

        SectionBreakType effectiveBreak = breakType switch
        {
            SectionBreakType.OddPage => SectionBreakType.OddPage,
            SectionBreakType.EvenPage => SectionBreakType.EvenPage,
            SectionBreakType.NextColumn => SectionBreakType.NextColumn,
            SectionBreakType.NextPage => SectionBreakType.NextPage,
            SectionBreakType.Continuous => startsNextPage ? SectionBreakType.NextPage : SectionBreakType.Continuous,
            _ => startsNextPage ? SectionBreakType.NextPage : SectionBreakType.Continuous,
        };

        return BuildSectionProperties(
            context,
            RunningAt(document, index),
            PageAt(document, index),
            ColumnsAt(document, index),
            breakType: effectiveBreak,
            continuous: effectiveBreak == SectionBreakType.Continuous);
    }

    /// <summary>A paragraph's <c>w:pPr</c>, made when it has none, and kept first among its children.</summary>
    private static XElement PropertiesOf(XElement paragraph)
    {
        XElement? properties = paragraph.Element(DocxNamespaces.Wordprocessing + "pPr");
        if (properties is null)
        {
            properties = new XElement(DocxNamespaces.Wordprocessing + "pPr");
            paragraph.AddFirst(properties);
        }

        return properties;
    }

    /// <summary>One body paragraph, carrying any shapes anchored to it.</summary>
    private static XElement BuildBodyParagraph(RichTextDocument document, int index, DocxWriteContext context)
    {
        XElement element = BuildParagraph(document.Paragraphs[index], context);

        // A shape is anchored to a paragraph, so it rides in a run at the head
        // of the one it belongs to - which is where Word puts it too.
        foreach (DocumentShape shape in document.Shapes)
        {
            if (shape.ParagraphIndex != index)
                continue;

            // WordprocessingML measures a drawing from the page but anchors it
            // to a paragraph, so a shape standing on a page by number keeps its
            // place on the page and loses the number.
            if (shape.IsPageAnchored)
            {
                context.AddDiagnosticOnce(
                    "docx.shape.page",
                    "A shape anchored to a page by number was written beside the paragraph it was read with, " +
                    "measured from the page; WordprocessingML cannot name the page.");
            }

            element.Add(BuildShapeRun(shape, context, shape.IsPageAnchored || shape.OffsetFromPage));
        }

        return element;
    }

    /// <summary>
    /// One table, as the <c>w:tbl</c> Word writes: its properties, its grid, and
    /// a row per row holding a cell per cell.
    /// </summary>
    private static XElement BuildTable(RichTextDocument document, DocumentTable table, DocxWriteContext context)
    {
        if (table.KeepWithNext)
            context.AddDiagnosticOnce("docx.table.keepnext",
                "A table's keep-with-next rule was not written; WordprocessingML expresses keeps on cell paragraphs rather than tables.");
        if (table.PageBreakBefore || table.ColumnBreakBefore)
            context.AddDiagnosticOnce("docx.table.break",
                "A table's leading page or column break was not written; WordprocessingML has no table-level break property.");

        var element = new XElement(
            DocxNamespaces.Wordprocessing + "tbl",
            BuildTableProperties(table),
            BuildTableGrid(table));

        int rowIndex = 0;
        int gridColumns = table.ColumnWidths.Count;
        foreach (TableRow gridRow in table.Rows)
            foreach (TableCell cell in gridRow.Cells)
                gridColumns = Math.Max(gridColumns, cell.ColumnIndex + cell.ColumnSpan);
        foreach (TableRow row in table.Rows)
        {
            var tr = new XElement(DocxNamespaces.Wordprocessing + "tr");

            // One trPr carries both, because a row may state a height and repeat.
            // Written with an explicit atLeast rather than bare: the model's height
            // is a minimum, and saying so removes the ambiguity the reader has to
            // resolve by convention when a producer omits the rule.
            // CT_TrPr is a choice, not a sequence, so the order here is free.
            var properties = new XElement(DocxNamespaces.Wordprocessing + "trPr");
            if (!row.CanSplit)
                properties.Add(new XElement(DocxNamespaces.Wordprocessing + "cantSplit"));
            if (row.IsHeader)
                properties.Add(new XElement(DocxNamespaces.Wordprocessing + "tblHeader"));
            if (row.MinHeight > 0)
            {
                properties.Add(new XElement(
                    DocxNamespaces.Wordprocessing + "trHeight",
                    new XAttribute(DocxNamespaces.Wordprocessing + "val",
                        ((int)Math.Round(row.MinHeight * 20)).ToString(CultureInfo.InvariantCulture)),
                    new XAttribute(DocxNamespaces.Wordprocessing + "hRule", row.ExactHeight ? "exact" : "atLeast")));
            }

            var writtenCells = new List<TableCell>(row.Cells);
            // ODT-style model tables omit cells covered by a vertical merge.
            // Word requires explicit continuation cells to keep later cells in their columns.
            for (int above = 0; above < rowIndex; above++)
                foreach (TableCell span in table.Rows[above].Cells)
                    if (!span.IsRowSpanContinuation && above + span.RowSpan > rowIndex &&
                        !writtenCells.Any(cell => cell.ColumnIndex < span.ColumnIndex + span.ColumnSpan && cell.ColumnIndex + cell.ColumnSpan > span.ColumnIndex))
                        writtenCells.Add(new TableCell(table.ParagraphEnd, 0, span.ColumnIndex, span.ColumnSpan,
                            shading: span.Shading, borders: span.Borders, isRowSpanContinuation: true,
                            padding: span.Padding, verticalAlignment: span.VerticalAlignment));
            if (writtenCells.Count > 0)
            {
                int before = writtenCells.Min(cell => cell.ColumnIndex);
                int after = Math.Max(0, gridColumns - writtenCells.Max(cell => cell.ColumnIndex + cell.ColumnSpan));
                if (before > 0) properties.Add(new XElement(DocxNamespaces.Wordprocessing + "gridBefore",
                    new XAttribute(DocxNamespaces.Wordprocessing + "val", before)));
                if (after > 0) properties.Add(new XElement(DocxNamespaces.Wordprocessing + "gridAfter",
                    new XAttribute(DocxNamespaces.Wordprocessing + "val", after)));
            }
            if (properties.HasElements) tr.Add(properties);
            foreach (TableCell cell in writtenCells.OrderBy(cell => cell.ColumnIndex))
                tr.Add(BuildTableCell(document, table, cell, context));

            element.Add(tr);
            rowIndex++;
        }

        return element;
    }

    private static XElement BuildTableProperties(DocumentTable table)
    {
        double total = table.PreferredWidth ?? table.TotalWidth;
        var properties = new XElement(DocxNamespaces.Wordprocessing + "tblPr");

        // CT_TblPr is a sequence, and the position comes before the width.
        if (table.Position is TablePosition position)
        {
            properties.Add(BuildTablePosition(position));
            if (!position.AllowOverlap)
                properties.Add(new XElement(DocxNamespaces.Wordprocessing + "tblOverlap", WordAttribute("val", "never")));
        }

        properties.Add(new XElement(
            DocxNamespaces.Wordprocessing + "tblW",
            WordAttribute("w", table.WidthPercent is > 0 ? (table.WidthPercent.Value * 50).ToString("0", CultureInfo.InvariantCulture) : total > 0 ? Twips(total) : "0"),
            WordAttribute("type", table.WidthPercent is > 0 ? "pct" : total > 0 ? "dxa" : "auto")));
        if (table.Alignment != TableAlignment.Start)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "jc",
                WordAttribute("val", table.Alignment == TableAlignment.Center ? "center" : "right")));
        if (table.LeftIndent != 0)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "tblInd",
                WordAttribute("w", Twips(table.LeftIndent)), WordAttribute("type", "dxa")));

        string padding = Twips(table.CellPadding);
        properties.Add(new XElement(
            DocxNamespaces.Wordprocessing + "tblCellMar",
            CellMargin("top", "0"),
            CellMargin("left", padding),
            CellMargin("bottom", "0"),
            CellMargin("right", padding)));

        return properties;
    }

    /// <summary>
    /// ECMA-376 §17.4.57's <c>w:tblpPr</c>, with both anchors written out: the
    /// defaults for one left out are where readers disagree.
    /// </summary>
    private static XElement BuildTablePosition(TablePosition position)
    {
        var element = new XElement(
            DocxNamespaces.Wordprocessing + "tblpPr",
            WordAttribute("leftFromText", Twips(position.LeftFromText)),
            WordAttribute("rightFromText", Twips(position.RightFromText)),
            WordAttribute("topFromText", Twips(position.TopFromText)),
            WordAttribute("bottomFromText", Twips(position.BottomFromText)),
            WordAttribute("vertAnchor", AnchorName(position.VerticalAnchor)),
            WordAttribute("horzAnchor", AnchorName(position.HorizontalAnchor)));

        if (AlignmentName(position.HorizontalAlignment, horizontal: true) is string x)
            element.Add(WordAttribute("tblpXSpec", x));
        else
            element.Add(WordAttribute("tblpX", Twips(position.X)));

        if (AlignmentName(position.VerticalAlignment, horizontal: false) is string y)
            element.Add(WordAttribute("tblpYSpec", y));
        else
            element.Add(WordAttribute("tblpY", Twips(position.Y)));

        if (position.ZOrder != 0)
            element.Add(WordAttribute("zOrder", position.ZOrder.ToString(CultureInfo.InvariantCulture)));
        if (position.BehindText)
            element.Add(WordAttribute("behindText", "1"));

        return element;

        static string AnchorName(TableAnchor anchor) => anchor switch
        {
            TableAnchor.Margin => "margin",
            TableAnchor.Page => "page",
            _ => "text",
        };

        static string? AlignmentName(TableAlignment alignment, bool horizontal) => alignment switch
        {
            TableAlignment.Start => horizontal ? "left" : "top",
            TableAlignment.Center => "center",
            TableAlignment.End => horizontal ? "right" : "bottom",
            TableAlignment.Inside => "inside",
            TableAlignment.Outside => "outside",
            _ => null,
        };
    }

    private static XElement CellMargin(string edge, string twips) =>
        new(
            DocxNamespaces.Wordprocessing + edge,
            WordAttribute("w", twips),
            WordAttribute("type", "dxa"));

    private static XElement BuildTableGrid(DocumentTable table)
    {
        var grid = new XElement(DocxNamespaces.Wordprocessing + "tblGrid");
        foreach (double width in table.ColumnWidths)
            grid.Add(new XElement(DocxNamespaces.Wordprocessing + "gridCol", WordAttribute("w", Twips(width))));

        return grid;
    }

    /// <summary>
    /// One cell: what the grid says about it, then its paragraphs. A cell with no
    /// paragraphs of its own is written with an empty one, because a
    /// <c>w:tc</c> that holds no block content is a document Word will not open.
    /// </summary>
    private static XElement BuildTableCell(
        RichTextDocument document,
        DocumentTable table,
        TableCell cell,
        DocxWriteContext context)
    {
        var element = new XElement(
            DocxNamespaces.Wordprocessing + "tc",
            BuildCellProperties(table, cell));

        bool empty = true;
        foreach (XElement block in BuildBlocks(document, cell.Tables, cell.ParagraphIndex, cell.ParagraphEnd, context))
        {
            element.Add(block);
            empty = false;
        }

        // A table may not end on a table either: Word requires a paragraph after
        // a nested one, and after the last block of every cell.
        if (empty || element.Elements(DocxNamespaces.Wordprocessing + "tbl").Any(IsLastBlock))
            element.Add(new XElement(DocxNamespaces.Wordprocessing + "p"));

        return element;
    }

    private static bool IsLastBlock(XElement element) => element.NextNode is null;

    private static XElement BuildCellProperties(DocumentTable table, TableCell cell)
    {
        var properties = new XElement(DocxNamespaces.Wordprocessing + "tcPr");

        double width = CellWidth(table, cell);
        properties.Add(new XElement(
            DocxNamespaces.Wordprocessing + "tcW",
            WordAttribute("w", width > 0 ? Twips(width) : "0"),
            WordAttribute("type", width > 0 ? "dxa" : "auto")));

        if (cell.ColumnSpan > 1)
        {
            properties.Add(new XElement(
                DocxNamespaces.Wordprocessing + "gridSpan",
                WordAttribute("val", cell.ColumnSpan.ToString(CultureInfo.InvariantCulture))));
        }

        // The merge is written the way the format holds it: the cell that starts
        // it says "restart", and the cells it covers say nothing at all.
        if (cell.IsRowSpanContinuation)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "vMerge"));
        else if (cell.RowSpan > 1)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "vMerge", WordAttribute("val", "restart")));

        if (cell.Borders.IsVisible)
            properties.Add(BuildCellBorders(cell.Borders));

        if (!cell.Shading.IsEmpty && cell.Shading.A > 0)
        {
            properties.Add(new XElement(
                DocxNamespaces.Wordprocessing + "shd",
                WordAttribute("val", "clear"),
                WordAttribute("color", "auto"),
                WordAttribute("fill", HexColor(cell.Shading))));
        }

        if (cell.Padding is TableCellPadding padding)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "tcMar",
                CellMargin("top", Twips(padding.Top)), CellMargin("left", Twips(padding.Left)),
                CellMargin("bottom", Twips(padding.Bottom)), CellMargin("right", Twips(padding.Right))));

        if (cell.VerticalAlignment != CellVerticalAlignment.Top)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "vAlign",
                WordAttribute("val", cell.VerticalAlignment == CellVerticalAlignment.Center ? "center" : "bottom")));

        return properties;
    }

    /// <summary>The width a cell covers: the columns it spans, added up.</summary>
    private static double CellWidth(DocumentTable table, TableCell cell)
    {
        double width = 0;
        for (int column = cell.ColumnIndex; column < cell.ColumnIndex + cell.ColumnSpan; column++)
        {
            if (column < table.ColumnWidths.Count)
                width += Math.Max(0, table.ColumnWidths[column]);
        }

        return width;
    }

    private static XElement BuildCellBorders(CellBorders borders)
    {
        var element = new XElement(
            DocxNamespaces.Wordprocessing + "tcBorders",
            BuildBorderEdge("top", borders.Top),
            BuildBorderEdge("left", borders.Left),
            BuildBorderEdge("bottom", borders.Bottom),
            BuildBorderEdge("right", borders.Right));
        if (borders.DiagonalDown.IsVisible)
            element.Add(BuildBorderEdge("tl2br", borders.DiagonalDown));
        if (borders.DiagonalUp.IsVisible)
            element.Add(BuildBorderEdge("tr2bl", borders.DiagonalUp));
        return element;
    }

    /// <summary>
    /// One border edge. <c>w:sz</c> is in eighths of a point, and an edge that
    /// draws nothing is written as <c>none</c> rather than left out, so a cell
    /// that turns a border off keeps it off through a round trip.
    /// </summary>
    private static XElement BuildBorderEdge(string edge, TableBorder border)
    {
        if (!border.IsVisible)
            return new XElement(DocxNamespaces.Wordprocessing + edge, WordAttribute("val", "none"));

        string val = border.Style switch
        {
            TableBorderStyle.Dashed => "dashed",
            TableBorderStyle.Dotted => "dotted",
            TableBorderStyle.Double => "double",
            TableBorderStyle.Groove => "groove",
            TableBorderStyle.Ridge => "ridge",
            TableBorderStyle.Inset => "inset",
            TableBorderStyle.Outset => "outset",
            TableBorderStyle.None => "none",
            _ => "single",
        };

        return new XElement(
            DocxNamespaces.Wordprocessing + edge,
            WordAttribute("val", val),
            WordAttribute("sz", Math.Max(1, (int)Math.Round(border.Width * 8)).ToString(CultureInfo.InvariantCulture)),
            WordAttribute("space", "0"),
            WordAttribute("color", HexColor(border.Color)));
    }

    private static XElement BuildParagraph(RichTextParagraph paragraph, DocxWriteContext context)
    {
        var element = new XElement(DocxNamespaces.Wordprocessing + "p");
        XElement? properties = BuildParagraphProperties(paragraph.Style, context);

        // The mark's w:rPr closes CT_PPr's base properties and comes before a
        // w:sectPr, which is added after this. A mark carries no link and no
        // picture, so those parts of the model's style are not its to write.
        // A mark a change inserted or deleted says so first in its w:rPr
        // (CT_ParaRPr); a change of formatting is not recorded on a mark.
        InlineStyle markStyle = paragraph.MarkStyle with { LinkHref = null, Image = null, Revision = null };
        XElement? mark = BuildRunProperties(markStyle, context);
        if (paragraph.MarkStyle.Revision is { Kind: not RevisionKind.Format } markRevision)
        {
            mark ??= new XElement(DocxNamespaces.Wordprocessing + "rPr");
            mark.AddFirst(RevisionElement(markRevision, context));
        }

        if (mark is not null)
        {
            properties ??= new XElement(DocxNamespaces.Wordprocessing + "pPr");
            properties.Add(mark);
        }

        if (properties is not null)
            element.Add(properties);

        // A column break has no paragraph property to be; it is a break run,
        // and it opens the paragraph it starts, which a reader takes as that
        // paragraph's own break. A page break, where both are set, is the one
        // kept.
        if (paragraph.Style.ColumnBreakBefore && !paragraph.Style.PageBreakBefore)
        {
            element.Add(new XElement(
                DocxNamespaces.Wordprocessing + "r",
                new XElement(DocxNamespaces.Wordprocessing + "br", WordAttribute("type", "column"))));
        }

        // Runs an insertion or a deletion marks go inside its w:ins or w:del,
        // consecutive runs of one change inside one.
        int offset = 0;
        XElement? change = null;
        TextRevision? changeRevision = null;
        foreach (StyleRun run in paragraph.Runs)
        {
            string text = paragraph.Text.Substring(offset, run.Length);
            offset += run.Length;

            XElement parent = element;
            if (run.Style.Revision is { Kind: not RevisionKind.Format } revision)
            {
                if (change is null || !Equals(changeRevision, revision))
                {
                    change = RevisionElement(revision, context);
                    changeRevision = revision;
                    element.Add(change);
                }

                parent = change;
            }
            else
            {
                change = null;
                changeRevision = null;
            }

            context.InDeletion = run.Style.IsDeleted;
            AddRun(parent, text, run.Style, context);
            context.InDeletion = false;
        }

        return element;
    }

    /// <summary>
    /// The element that records an insertion or a deletion (ECMA-376
    /// §17.13.5): <c>w:ins</c>, <c>w:del</c>, or for one side of a move
    /// <c>w:moveTo</c> and <c>w:moveFrom</c>, with its author and date.
    /// </summary>
    private static XElement RevisionElement(TextRevision revision, DocxWriteContext context)
    {
        string name = (revision.Kind, revision.Moved) switch
        {
            (RevisionKind.Deletion, true) => "moveFrom",
            (RevisionKind.Deletion, false) => "del",
            (_, true) => "moveTo",
            _ => "ins",
        };
        return new XElement(DocxNamespaces.Wordprocessing + name, RevisionAttributes(revision, context));
    }

    /// <summary>
    /// A change's <c>w:id</c>, unique in the document, its <c>w:author</c> -
    /// required, so empty for a change that names nobody - and its
    /// <c>w:date</c> where it has one.
    /// </summary>
    private static IEnumerable<XAttribute> RevisionAttributes(TextRevision revision, DocxWriteContext context)
    {
        yield return WordAttribute("id", context.NextRevisionId().ToString(CultureInfo.InvariantCulture));
        yield return WordAttribute("author", XmlText(revision.Author ?? string.Empty, context));
        if (revision.Date is DateTimeOffset date)
            yield return WordAttribute("date", date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Writes a paragraph's properties in the order CT_PPr fixes rather than
    /// the order the model happens to list them in. CT_PPr is an xsd:sequence,
    /// so each of these has exactly one legal position, and the positions here
    /// are the ones ECMA-376 Part 1 section 17.3.1.26 gives:
    /// <c>w:pageBreakBefore</c> 4th, <c>w:numPr</c> 7th, <c>w:shd</c> 10th, <c>w:spacing</c> 22nd,
    /// <c>w:ind</c> 23rd, <c>w:jc</c> 27th. Word does not shrug at an
    /// out-of-order pPr the way it does at an element it has never heard of; it
    /// can refuse the file outright, and a refusal is the whole document rather
    /// than the one property.
    /// <para>
    /// This method emitted <c>w:jc</c> before <c>w:spacing</c> and
    /// <c>w:ind</c> for as long as it had written all three, so any centred
    /// paragraph that also carried spacing or an indent - a heading, in other
    /// words - left here out of sequence. What makes that worth recording is
    /// that <c>w:pageBreakBefore</c>, added long after the other four, was
    /// placed correctly and carried a comment naming this exact hazard. The
    /// hazard was already being run into a dozen lines below the comment
    /// warning about it.
    /// </para>
    /// </summary>
    private static XElement? BuildParagraphProperties(ParagraphStyle style, DocxWriteContext context)
    {
        var properties = new XElement(DocxNamespaces.Wordprocessing + "pPr");
        if (style.TabStops is ParagraphTabStops tabs)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "tabs", tabs.Select(tab =>
                new XElement(DocxNamespaces.Wordprocessing + "tab",
                    WordAttribute("pos", Twips(tab.Position)),
                    WordAttribute("val", tab.Alignment switch
                    { TabAlignment.Center => "center", TabAlignment.Right => "right", TabAlignment.Decimal => "decimal", _ => "left" }),
                    tab.Leader is null ? null : WordAttribute("leader", tab.Leader switch
                    { "-" => "hyphen", "_" => "underscore", "·" => "middleDot", _ => "dot" })))));
        if (style.KeepWithNext)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "keepNext"));
        if (style.KeepTogether)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "keepLines"));

        // The property form rather than a run holding w:br w:type="page",
        // because the property is what the model holds. A break written into a
        // run would have to go at the end of the *previous* paragraph, which
        // means a paragraph's markup would depend on the one after it, and the
        // first paragraph of a document would have nowhere to put its break at
        // all.
        if (style.PageBreakBefore)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "pageBreakBefore"));

        if (style.OrphanLines is not null || style.WidowLines is not null)
        {
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "widowControl",
                WordAttribute("val", style.OrphanLines is > 0 || style.WidowLines is > 0 ? "1" : "0")));
            if (style.OrphanLines is not (null or 0 or 2) || style.WidowLines is not (null or 0 or 2) ||
                (style.OrphanLines ?? style.WidowLines) != (style.WidowLines ?? style.OrphanLines))
                context.AddDiagnosticOnce("docx.paragraph.pagination",
                    "DOCX widow control supports two lines on both sides; custom widow/orphan counts were reduced to on/off.");
        }

        if (style.List is ListReference list)
        {
            if (list.RestartAt is not null)
            {
                context.AddDiagnosticOnce(
                    "docx.list.restart",
                    "A list item that restarts its list's count was written without the restart; DOCX restarts a list only through a list of its own.");
            }

            properties.Add(new XElement(
                DocxNamespaces.Wordprocessing + "numPr",
                new XElement(DocxNamespaces.Wordprocessing + "ilvl", WordAttribute("val", list.Level.ToString(CultureInfo.InvariantCulture))),
                new XElement(DocxNamespaces.Wordprocessing + "numId", WordAttribute("val", context.NumIdFor(list.Definition).ToString(CultureInfo.InvariantCulture)))));
        }
        else if (style.ListKind != ListKind.None)
        {
            int level = Math.Clamp(style.IndentLevel <= 0 ? 0 : style.IndentLevel - 1, 0, 8);
            int numId = style.ListKind == ListKind.Numbered ? context.LegacyNumberedId() : 1;
            properties.Add(new XElement(
                DocxNamespaces.Wordprocessing + "numPr",
                new XElement(DocxNamespaces.Wordprocessing + "ilvl", WordAttribute("val", level.ToString(CultureInfo.InvariantCulture))),
                new XElement(DocxNamespaces.Wordprocessing + "numId", WordAttribute("val", numId.ToString(CultureInfo.InvariantCulture)))));
        }
        else
        {
            context.EndLegacyList();
        }

        // w:shd is 10th, after w:numPr and before w:spacing.
        if (!style.Background.IsEmpty && style.Background.A > 0)
        {
            properties.Add(new XElement(
                DocxNamespaces.Wordprocessing + "shd",
                WordAttribute("val", "clear"),
                WordAttribute("color", "auto"),
                WordAttribute("fill", HexColor(style.Background))));
        }

        bool fixedHeight = style.LineHeightRule != LineHeightRule.Proportional && style.LineHeight > 0;
        if (Math.Abs(style.LineSpacing - 1f) > 0.001f ||
            fixedHeight ||
            Math.Abs(style.SpacingBefore) > 0.001f ||
            Math.Abs(style.SpacingAfter) > 0.001f)
        {
            var spacing = new XElement(DocxNamespaces.Wordprocessing + "spacing");
            if (Math.Abs(style.SpacingBefore) > 0.001f)
                spacing.Add(WordAttribute("before", Twips(style.SpacingBefore).ToString(CultureInfo.InvariantCulture)));
            if (Math.Abs(style.SpacingAfter) > 0.001f)
                spacing.Add(WordAttribute("after", Twips(style.SpacingAfter).ToString(CultureInfo.InvariantCulture)));
            if (fixedHeight)
            {
                // A height in twips, under the rule that says what it is.
                spacing.Add(WordAttribute("line", Math.Max(1, Twips(style.LineHeight)).ToString(CultureInfo.InvariantCulture)));
                spacing.Add(WordAttribute("lineRule", style.LineHeightRule == LineHeightRule.Exact ? "exact" : "atLeast"));
            }
            else if (Math.Abs(style.LineSpacing - 1f) > 0.001f)
            {
                spacing.Add(WordAttribute("line", Math.Max(1, (int)Math.Round(style.LineSpacing * 240f)).ToString(CultureInfo.InvariantCulture)));
                spacing.Add(WordAttribute("lineRule", "auto"));
            }

            properties.Add(spacing);
        }

        // A numbered paragraph's indent is where its label starts, and DOCX
        // states where the text starts and how far the label hangs back from
        // it, so it is written as both.
        if (style.LeftIndent.HasValue || style.RightIndent.HasValue || style.FirstLineIndent.HasValue)
        {
            var indentation = new XElement(DocxNamespaces.Wordprocessing + "ind",
                WordAttribute("left", ((int)Math.Round(style.LeftIndentIn() * 20)).ToString(CultureInfo.InvariantCulture)));
            if (style.RightIndent.HasValue)
                indentation.Add(WordAttribute("right", ((int)Math.Round(style.RightIndentIn * 20)).ToString(CultureInfo.InvariantCulture)));
            if (style.FirstLineIndent.HasValue)
                indentation.Add(WordAttribute(style.FirstLineIndentIn < 0 ? "hanging" : "firstLine",
                    ((int)Math.Round(Math.Abs(style.FirstLineIndentIn) * 20)).ToString(CultureInfo.InvariantCulture)));
            properties.Add(indentation);
        }
        else if (style.List is not null || style.ListKind != ListKind.None)
        {
            properties.Add(new XElement(
                DocxNamespaces.Wordprocessing + "ind",
                WordAttribute("left", ((Math.Max(0, style.IndentLevel) + 1) * 360).ToString(CultureInfo.InvariantCulture)),
                WordAttribute("hanging", "360")));
        }
        else if (style.IndentLevel > 0)
        {
            properties.Add(new XElement(
                DocxNamespaces.Wordprocessing + "ind",
                WordAttribute("left", (style.IndentLevel * 360).ToString(CultureInfo.InvariantCulture))));
        }

        if (style.Alignment == TextAlignment.Center)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "jc", WordAttribute("val", "center")));
        else if (style.Alignment == TextAlignment.Right)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "jc", WordAttribute("val", "right")));
        else if (style.Alignment == TextAlignment.Justify)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "jc", WordAttribute("val", "both")));

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
        if (!DocumentLinkTarget.IsAllowed(href))
        {
            context.AddDiagnosticOnce("docx.link", "A hyperlink with a disallowed or relative target was written as plain text.");
            return null;
        }

        // A same-document reference is its own element in WordprocessingML: an
        // anchor naming a bookmark rather than a relationship to a URL. What the
        // anchor points at is not carried - nothing here reads or writes a
        // bookmark - so this preserves the reference the source made and not a
        // jump that works.
        if (href[0] == '#')
        {
            return new XElement(
                DocxNamespaces.Wordprocessing + "hyperlink",
                WordAttribute("anchor", href[1..]),
                BuildRun(text, style with { LinkHref = null }, context));
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

    /// <summary>
    /// The run's half of the same rule <see cref="BuildParagraphProperties"/>
    /// obeys. CT_RPr is an xsd:sequence as well, and this one had drifted
    /// further from it than the paragraph properties had, because the emission
    /// followed the order a person lists formatting in - bold, italic,
    /// underline, then everything else - which is nothing like the order
    /// ECMA-376 Part 1 section 17.3.2.28 fixes. The positions written below are
    /// that section's: <c>w:rFonts</c> 2nd, <c>w:b</c> 3rd, <c>w:i</c> 5th,
    /// <c>w:caps</c> 7th, <c>w:smallCaps</c> 8th, <c>w:strike</c> 9th,
    /// <c>w:color</c> 19th, <c>w:sz</c> 24th, <c>w:u</c> 27th, <c>w:shd</c>
    /// 30th. Underline in particular sat third and belongs second from last, so
    /// almost any underlined run left here out of sequence.
    /// </summary>
    private static XElement? BuildRunProperties(InlineStyle style, DocxWriteContext context)
    {
        var properties = new XElement(DocxNamespaces.Wordprocessing + "rPr");

        if (!string.IsNullOrWhiteSpace(style.FontFamily))
        {
            // A family name is a model string and reaches an attribute, so it
            // needs the same filter run text and a description do.
            string family = XmlText(style.FontFamily, context);
            properties.Add(new XElement(
                DocxNamespaces.Wordprocessing + "rFonts",
                WordAttribute("ascii", family),
                WordAttribute("hAnsi", family),
                WordAttribute("cs", family),
                WordAttribute("eastAsia", family)));
        }

        if (style.Bold)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "b"));
        if (style.Italic)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "i"));

        // Adjacent in the sequence as well as exclusive in the model, so the
        // either/or costs this one nothing: whichever arm runs, it lands
        // between w:i and w:strike either way.
        if (style.Capitalization == TextCapitalization.AllCaps)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "caps"));
        else if (style.Capitalization == TextCapitalization.SmallCaps)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "smallCaps"));

        if (style.Strikethrough)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "strike"));

        if (style.Hidden)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "vanish"));

        if (!style.Foreground.IsEmpty)
        {
            properties.Add(new XElement(
                DocxNamespaces.Wordprocessing + "color",
                WordAttribute("val", FormatColor(style.Foreground, context))));
        }

        if (style.FontSize.HasValue)
        {
            int halfPoints = Math.Max(1, (int)Math.Round(style.FontSize.Value * 2f));
            properties.Add(new XElement(
                DocxNamespaces.Wordprocessing + "sz",
                WordAttribute("val", halfPoints.ToString(CultureInfo.InvariantCulture))));
        }

        if (style.Underline)
            properties.Add(new XElement(DocxNamespaces.Wordprocessing + "u", WordAttribute("val", "single")));

        if (!style.Background.IsEmpty)
        {
            properties.Add(new XElement(
                DocxNamespaces.Wordprocessing + "shd",
                WordAttribute("val", "clear"),
                WordAttribute("color", "auto"),
                WordAttribute("fill", FormatColor(style.Background, context))));
        }

        // A citation is raised, as Word's own FootnoteReference style raises
        // it; w:vertAlign is 32nd, after w:shd.
        if (style.IsNote)
            properties.Add(SuperscriptElement());

        // A change of formatting closes the sequence. What the run looked like
        // before is not held, so the earlier properties are written empty, as
        // LibreOffice writes a change it holds no old formatting for.
        if (style.Revision is { Kind: RevisionKind.Format } format)
        {
            properties.Add(new XElement(
                DocxNamespaces.Wordprocessing + "rPrChange",
                RevisionAttributes(format, context),
                new XElement(DocxNamespaces.Wordprocessing + "rPr")));
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
                run.Add(TextElement(text[start..i], context));

            switch (character)
            {
                case '\t':
                    run.Add(new XElement(DocxNamespaces.Wordprocessing + "tab"));
                    break;
                case (char)0x2028:
                    run.Add(new XElement(DocxNamespaces.Wordprocessing + "br"));
                    break;
                default:
                    if (style.Note is DocumentNote note)
                        AddNoteReference(run, note, context);
                    else
                        AddPicture(run, style.Image, context);
                    break;
            }

            start = i + 1;
        }

        if (start < text.Length)
            run.Add(TextElement(text[start..], context));
    }

    /// <summary>
    /// Writes a citation as the reference that names its note, and registers
    /// the note for its part. A note's own label follows the reference as text,
    /// which is what <c>w:customMarkFollows</c> says.
    /// </summary>
    private static void AddNoteReference(XElement run, DocumentNote note, DocxWriteContext context)
    {
        if (context.InNote)
        {
            context.AddDiagnosticOnce("docx.note.nested", "A note inside a note was dropped: WordprocessingML cannot nest them.");
            return;
        }

        int id = context.AddNote(note);
        var reference = new XElement(
            DocxNamespaces.Wordprocessing + (note.Kind == NoteKind.Endnote ? "endnoteReference" : "footnoteReference"));
        if (note.CustomMark is not null)
            reference.Add(WordAttribute("customMarkFollows", "1"));
        reference.Add(WordAttribute("id", id.ToString(CultureInfo.InvariantCulture)));
        run.Add(reference);
        if (note.CustomMark is not null)
            run.Add(TextElement(note.CustomMark, context));
    }

    private static XElement SuperscriptElement() =>
        new(DocxNamespaces.Wordprocessing + "vertAlign", WordAttribute("val", "superscript"));

    /// <summary>
    /// The footnotes or endnotes part, or null when the body cites none of that
    /// kind: Word's two separators, then each note with its label repeated at
    /// the head of its first paragraph, as Word writes one.
    /// </summary>
    private static XDocument? BuildNotes(NoteKind kind, bool raisedLabels, DocxWriteContext context)
    {
        IReadOnlyList<DocumentNote> notes = context.NotesOf(kind);
        if (notes.Count == 0)
            return null;

        string element = kind == NoteKind.Endnote ? "endnote" : "footnote";
        var root = new XElement(
            DocxNamespaces.Wordprocessing + element + "s",
            new XAttribute(XNamespace.Xmlns + "w", DocxNamespaces.Wordprocessing.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", DocxNamespaces.Relationships.NamespaceName),
            Separator(element, "separator", -1),
            Separator(element, "continuationSeparator", 0));

        context.InNote = true;
        for (int i = 0; i < notes.Count; i++)
        {
            var written = new XElement(
                DocxNamespaces.Wordprocessing + element,
                WordAttribute("id", (i + 1).ToString(CultureInfo.InvariantCulture)));
            foreach (RichTextParagraph paragraph in notes[i].Paragraphs)
                written.Add(BuildParagraph(paragraph, context));

            XElement first = written.Elements().First();
            var label = new XElement(
                DocxNamespaces.Wordprocessing + "r",
                raisedLabels ? new XElement(DocxNamespaces.Wordprocessing + "rPr", SuperscriptElement()) : null,
                new XElement(DocxNamespaces.Wordprocessing + element + "Ref"));
            var gap = new XElement(DocxNamespaces.Wordprocessing + "r", TextElement(" ", context));
            if (first.Element(DocxNamespaces.Wordprocessing + "pPr") is XElement pPr)
                pPr.AddAfterSelf(label, gap);
            else
                first.AddFirst(label, gap);

            root.Add(written);
        }

        context.InNote = false;
        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
    }

    private static XElement Separator(string element, string type, int id) =>
        new(
            DocxNamespaces.Wordprocessing + element,
            WordAttribute("type", type),
            WordAttribute("id", id.ToString(CultureInfo.InvariantCulture)),
            new XElement(
                DocxNamespaces.Wordprocessing + "p",
                new XElement(
                    DocxNamespaces.Wordprocessing + "r",
                    new XElement(DocxNamespaces.Wordprocessing + type))));

    /// <summary>
    /// A notes part's relationships: the hyperlinks and pictures, under the
    /// ids the body's part gives them, because a note's paragraphs were written
    /// by the same context and name those ids.
    /// </summary>
    private static XDocument BuildNoteRelationships(DocxWriteContext context)
    {
        var root = new XElement(DocxNamespaces.PackageRelationships + "Relationships");
        foreach (KeyValuePair<string, string> relationship in context.HyperlinkRelationships.OrderBy(pair => pair.Value, StringComparer.Ordinal))
        {
            root.Add(new XElement(
                DocxNamespaces.PackageRelationships + "Relationship",
                new XAttribute("Id", relationship.Value),
                new XAttribute("Type", DocxNamespaces.HyperlinkRelationship),
                new XAttribute("Target", relationship.Key),
                new XAttribute("TargetMode", "External")));
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

        if (!DocumentResourceGate.TryTakeEncodedBytes(
                image,
                context.Resources,
                DocumentResourceOperations.ByteTransfer,
                out ReadOnlyMemory<byte> data,
                out string? contentType,
                out string? denial))
        {
            context.AddDiagnosticOnce(
                "docx.image.omitted",
                "An image was left out of the DOCX output because " + denial + ".");
            return;
        }

        // A size that resolves from the resource's own pixels beats the square
        // inch this used to invent, and the fallback is now only for a picture
        // whose intrinsic size nothing established.
        if (!image.TryGetDisplaySize(out double pointsWide, out double pointsHigh))
        {
            pointsWide = DefaultImagePoints;
            pointsHigh = DefaultImagePoints;
            context.AddDiagnosticOnce(
                "docx.image.size",
                "An image carried no display size and none could be inspected, so it was written one inch square.");
        }

        long widthEmus = Emus(pointsWide);
        long heightEmus = Emus(pointsHigh);

        (XElement graphic, XElement frameProperties) = BuildPicture(image, data, contentType, widthEmus, heightEmus, context);

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
                graphic)));
    }

    /// <summary>
    /// The <c>a:graphic</c> holding one picture, and the <c>wp:docPr</c> that
    /// names it. Both the inline frame and the anchored one wrap the same pair,
    /// so a picture writes the same whether it sits in the text or floats beside
    /// it — only the wrapper differs.
    /// </summary>
    private static (XElement Graphic, XElement FrameProperties) BuildPicture(
        InlineImage image,
        ReadOnlyMemory<byte> data,
        string contentType,
        long widthEmus,
        long heightEmus,
        DocxWriteContext context)
    {
        DocxImagePart part = context.GetImagePart(image, data, contentType);
        string index = part.Index.ToString(CultureInfo.InvariantCulture);
        string name = "Picture " + index;
        var pictureProperties = new XElement(
            DocxNamespaces.Picture + "cNvPr",
            new XAttribute("id", index),
            new XAttribute("name", name));
        var frameProperties = new XElement(
            DocxNamespaces.WordDrawing + "docPr",
            new XAttribute("id", index),
            new XAttribute("name", name));
        // An attribute is as much XML as an element, and a description arrives
        // from the same document the run text did.
        string description = XmlText(image.AltText, context);
        if (description.Length > 0)
        {
            pictureProperties.Add(new XAttribute("descr", description));
            frameProperties.Add(new XAttribute("descr", description));
        }

        var graphic = new XElement(
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
                        BuildSourceRect(image.Presentation),
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
                            new XElement(
                                DocxNamespaces.Drawing + "ext",
                                new XAttribute("cx", widthEmus.ToString(CultureInfo.InvariantCulture)),
                                new XAttribute("cy", heightEmus.ToString(CultureInfo.InvariantCulture)))),
                        new XElement(
                            DocxNamespaces.Drawing + "prstGeom",
                            new XAttribute("prst", PresetFor(image.Presentation.Mask)),
                            new XElement(DocxNamespaces.Drawing + "avLst"))))));

        return (graphic, frameProperties);
    }

    /// <summary>The preset geometry a mask is written as.</summary>
    private static string PresetFor(ImageMask mask) =>
        mask == ImageMask.Ellipse ? "ellipse" : "rect";

    /// <summary>
    /// The <c>a:srcRect</c> for a crop, or null when the picture uses all of its
    /// source. Written in thousandths of a percent, which is what the attribute
    /// states and what the reader converts back from.
    /// </summary>
    private static XElement? BuildSourceRect(ImagePresentation presentation)
    {
        if (!presentation.HasCrop)
            return null;

        var element = new XElement(DocxNamespaces.Drawing + "srcRect");
        Add("l", presentation.CropLeft);
        Add("t", presentation.CropTop);
        Add("r", presentation.CropRight);
        Add("b", presentation.CropBottom);
        return element;

        void Add(string edge, double fraction)
        {
            int value = (int)Math.Round(Math.Clamp(fraction, 0, 1) * 100000);
            if (value > 0)
                element.Add(new XAttribute(edge, value.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static XElement TextElement(string value, DocxWriteContext context) =>
        new(
            DocxNamespaces.Wordprocessing + (context.InDeletion ? "delText" : "t"),
            new XAttribute(DocxNamespaces.Xml + "space", "preserve"),
            XmlText(value, context));

    /// <summary>
    /// Text as XML can carry it: the characters XML 1.0 cannot represent are
    /// dropped, and a valid surrogate pair is kept whole.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XML has no representation for most control characters, not even an
    /// escape, so there is nothing to write but a diagnostic. Handing one to the
    /// serializer throws, and the tool turns that into exit 70 - which
    /// docs/guides/cli.md defines as always a defect in the tool. It is reachable from
    /// ordinary input: the HTML reader decodes <c>&amp;#7;</c> into the model
    /// and the RTF reader passes <c>\u7</c> through, so a document that reads
    /// cleanly could not be written.
    /// </para>
    /// <para>
    /// Every string that comes from the model passes through here, not only run
    /// text. An attribute is as much XML as an element, and a picture's
    /// description arrives from the same document the text did.
    /// </para>
    /// </remarks>
    private static string XmlText(string value, DocxWriteContext context)
    {
        int first = -1;
        for (int i = 0; i < value.Length; i++)
        {
            if (!XmlConvert.IsXmlChar(value[i]))
            {
                first = i;
                break;
            }
        }

        // Nothing to drop, which is every ordinary document: no allocation and
        // the original string is handed straight on.
        if (first < 0)
            return value;

        var clean = new StringBuilder(value.Length);
        clean.Append(value, 0, first);

        for (int i = first; i < value.Length; i++)
        {
            char character = value[i];

            // A surrogate is not an XML character on its own and a valid pair
            // is, so the pair is recognized before the test rejects its halves.
            if (char.IsHighSurrogate(character) &&
                i + 1 < value.Length &&
                char.IsLowSurrogate(value[i + 1]))
            {
                clean.Append(character).Append(value[i + 1]);
                i++;
                continue;
            }

            if (XmlConvert.IsXmlChar(character))
                clean.Append(character);
            else
                context.AddDiagnosticOnce("docx.text.control", "A control character that XML cannot represent was dropped.");
        }

        return clean.ToString();
    }

    /// <summary>
    /// One anchored shape, as the DrawingML a word processor writes: a picture
    /// when the shape carries one, else a wps:wsp with its geometry, its fill,
    /// and its text box when it holds text.
    /// </summary>
    /// <remarks>
    /// The wrapper is the same <c>wp:anchor</c> either way, so a floating picture
    /// goes back out as the anchored picture it was read from rather than as a
    /// shape filled with an image, which is a construct Word does not write.
    /// </remarks>
    private static XElement? BuildShapeRun(
        DocumentShape shape,
        DocxWriteContext context,
        bool pageAnchored = false)
    {
        if (shape.CellHorizontalAlignment is not null)
            context.AddDiagnosticOnce("docx.shape.cell-alignment", "Cell-relative shape alignment was written using its numeric offset; dynamic alignment within the cell was not retained.");
        if (shape.NextFrameName is not null || shape.GrowsWidthToText)
            context.AddDiagnosticOnce("docx.shape.flow", "Linked text-frame flow and minimum-width auto-sizing were written as independent fixed-width boxes.");
        if (shape.Fill is { } targetFill && (targetFill.Border != 0 || targetFill.ContentOnly))
            context.AddDiagnosticOnce("docx.fill.extended", "Gradient borders and page-fill bounds were approximated by their base colours or linear gradients.");
        if (shape.Image is InlineImage image)
        {
            if (!DocumentResourceGate.TryTakeEncodedBytes(
                    image,
                    context.Resources,
                    DocumentResourceOperations.ByteTransfer,
                    out ReadOnlyMemory<byte> data,
                    out string? contentType,
                    out string? denial))
            {
                context.AddDiagnosticOnce(
                    "docx.image.omitted",
                    "A floating picture was left out of the DOCX output because " + denial + ".");
                return null;
            }

            (XElement pictureGraphic, XElement pictureFrame) = BuildPicture(
                image,
                data,
                contentType,
                Emus(shape.Width),
                Emus(shape.Height),
                context);
            return BuildAnchorRun(shape, pictureFrame, pictureGraphic, pageAnchored);
        }

        var properties = new XElement(
            DocxNamespaces.WordShape + "spPr",
            new XElement(
                DocxNamespaces.Drawing + "xfrm",
                shape.Geometry == ShapeGeometry.LineAscending ? new XAttribute("flipV", "1") : null,
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
                new XAttribute("prst", shape.Geometry == ShapeGeometry.Ellipse ? "ellipse" : shape.Geometry is ShapeGeometry.Line or ShapeGeometry.LineAscending ? "line" : "rect"),
                new XElement(DocxNamespaces.Drawing + "avLst")));

        if (shape.Fill is ShapeFill fill)
            properties.Add(BuildShapeFill(fill, context));
        else properties.Add(new XElement(DocxNamespaces.Drawing + "noFill"));

        properties.Add(shape.Outline.IsEmpty
            ? new XElement(DocxNamespaces.Drawing + "ln", new XElement(DocxNamespaces.Drawing + "noFill"))
            : new XElement(
                DocxNamespaces.Drawing + "ln",
                new XAttribute("w", PointsToEmu(shape.OutlineWidth)),
                new XElement(DocxNamespaces.Drawing + "solidFill", DrawingColor(shape.Outline))));

        var wsp = new XElement(DocxNamespaces.WordShape + "wsp", properties);
        if (shape.HasText)
        {
            var content = new XElement(DocxNamespaces.Wordprocessing + "txbxContent");
            RichTextDocument shapeDocument = RichTextDocument.FromParagraphs(shape.Paragraphs).WithTables(shape.Tables);
            foreach (XElement block in BuildBlocks(shapeDocument, shape.Tables, 0, shapeDocument.ParagraphCount, context))
                content.Add(block);

            wsp.Add(new XElement(DocxNamespaces.WordShape + "txbx", content));
            wsp.Add(new XElement(DocxNamespaces.WordShape + "bodyPr",
                new XAttribute("lIns", Math.Round(shape.TextPadding.Left * 12700)),
                new XAttribute("tIns", Math.Round(shape.TextPadding.Top * 12700)),
                new XAttribute("rIns", Math.Round(shape.TextPadding.Right * 12700)),
                new XAttribute("bIns", Math.Round(shape.TextPadding.Bottom * 12700)),
                new XAttribute("anchor", shape.TextVerticalAlignment switch { CellVerticalAlignment.Center => "ctr", CellVerticalAlignment.Bottom => "b", _ => "t" }),
                new XAttribute("vertOverflow", shape.TextOverflow ? "overflow" : "clip"),
                shape.GrowsToText ? new XElement(DocxNamespaces.Drawing + "spAutoFit") : null));
        }

        return BuildAnchorRun(
            shape,
            new XElement(
                DocxNamespaces.WordDrawing + "docPr",
                new XAttribute("id", "1"),
                new XAttribute("name", "Shape")),
            new XElement(
                DocxNamespaces.Drawing + "graphic",
                new XElement(
                    DocxNamespaces.Drawing + "graphicData",
                    new XAttribute("uri", DocxNamespaces.WordShape.NamespaceName),
                    wsp)),
            pageAnchored);
    }

    /// <summary>
    /// The run holding one <c>wp:anchor</c>: the shape's box against the column
    /// and its paragraph, wrapped around whichever graphic it carries.
    /// </summary>
    /// <remarks>
    /// <c>behindDoc</c> is written from the shape rather than fixed, because it is
    /// read that way: a stripe read from behind the text and saved as "0" would
    /// come back from Word painted over the letter. <c>relativeHeight</c> is the
    /// shape's own <see cref="DocumentShape.ZOrder"/>, which is the same number
    /// this attribute was read into, so a letterhead's stripe and the logo box on
    /// top of it come back stacked the way they arrived.
    /// </para>
    /// <para>
    /// A running shape's vertical offset is measured from the top of the page
    /// rather than from a paragraph, so its <c>positionV</c> says <c>page</c>.
    /// Writing <c>paragraph</c> there would hand the header's own paragraph an
    /// offset that was never measured from it.
    /// </para>
    /// </remarks>
    private static XElement BuildAnchorRun(
        DocumentShape shape,
        XElement frameProperties,
        XElement graphic,
        bool pageAnchored = false)
    {
        var anchor = new XElement(
            DocxNamespaces.WordDrawing + "anchor",
            new XAttribute("distT", "0"),
            new XAttribute("distB", "0"),
            new XAttribute("distL", PointsToEmu(shape.WrapDistance)),
            new XAttribute("distR", PointsToEmu(shape.WrapDistance)),
            new XAttribute("simplePos", "0"),
            new XAttribute(
                "relativeHeight",
                shape.ZOrder.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("behindDoc", shape.BehindText ? "1" : "0"),
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
                new XAttribute("relativeFrom", pageAnchored ? "page" : "paragraph"),
                new XElement(DocxNamespaces.WordDrawing + "posOffset", PointsToEmu(shape.OffsetY))),
            new XElement(
                DocxNamespaces.WordDrawing + "extent",
                new XAttribute("cx", PointsToEmu(shape.Width)),
                new XAttribute("cy", PointsToEmu(shape.Height))),
            BuildWrap(shape),
            frameProperties,
            graphic);

        return new XElement(
            DocxNamespaces.Wordprocessing + "r",
            new XElement(DocxNamespaces.Wordprocessing + "drawing", anchor));
    }

    /// <summary>
    /// The wrap element, which <c>wp:anchor</c> requires exactly one of. It was
    /// fixed at <c>wrapNone</c> whatever the shape said, so a picture read with
    /// text flowing around it was saved as one the text runs under - the same
    /// arrangement lost on every save.
    /// </summary>
    /// <remarks>
    /// <c>wrapTight</c> and <c>wrapThrough</c> are read as square and written
    /// back as square, because the model follows the shape's box rather than its
    /// outline and writing back a mode that promises the outline would claim
    /// more than the document now holds.
    /// </remarks>
    private static XElement BuildWrap(DocumentShape shape) => shape.Wrap switch
    {
        ShapeWrap.Square => new XElement(
            DocxNamespaces.WordDrawing + "wrapSquare",
            new XAttribute("wrapText", shape.WrapSide switch
            {
                WrapSide.Left => "left",
                WrapSide.Right => "right",
                _ => "bothSides",
            })),
        ShapeWrap.TopAndBottom => new XElement(DocxNamespaces.WordDrawing + "wrapTopAndBottom"),
        _ => new XElement(DocxNamespaces.WordDrawing + "wrapNone"),
    };

    private static XElement BuildShapeFill(ShapeFill fill, DocxWriteContext context)
    {
        if (fill.Image is ImageFill imageFill)
        {
            if (DocumentResourceGate.TryTakeEncodedBytes(
                    imageFill.Picture,
                    context.Resources,
                    DocumentResourceOperations.ByteTransfer,
                    out ReadOnlyMemory<byte> data,
                    out string? contentType,
                    out string? denial))
            {
                DocxImagePart part = context.GetImagePart(imageFill.Picture, data, contentType);
                var blipFill = new XElement(
                    DocxNamespaces.Drawing + "blipFill",
                    new XElement(
                        DocxNamespaces.Drawing + "blip",
                        new XAttribute(DocxNamespaces.Relationships + "embed", part.RelationshipId)),
                    BuildSourceRect(imageFill.Picture.Presentation));

                if (imageFill.Repeat == ImageFillRepeat.Tile)
                {
                    blipFill.Add(new XElement(DocxNamespaces.Drawing + "tile"));
                }
                else
                {
                    blipFill.Add(new XElement(
                        DocxNamespaces.Drawing + "stretch",
                        new XElement(DocxNamespaces.Drawing + "fillRect")));
                }
                return blipFill;
            }

            context.AddDiagnosticOnce(
                "docx.image.omitted",
                "A shape bitmap fill was left out of the DOCX output because " + denial + ".");
            return new XElement(DocxNamespaces.Drawing + "noFill");
        }

        if (fill.Hatch is HatchFill hatch)
        {
            string preset = hatch.Directions switch
            {
                2 => IsDiagonalHatch(hatch.AngleDegrees) ? "diagCross" : "cross",
                3 => "diagCross",
                _ => IsHorizontalHatch(hatch.AngleDegrees) ? "horz" :
                     IsVerticalHatch(hatch.AngleDegrees) ? "vert" :
                     IsNegativeDiagonalHatch(hatch.AngleDegrees) ? "dnDiag" : "upDiag"
            };

            return new XElement(
                DocxNamespaces.Drawing + "pattFill",
                new XAttribute("prst", preset),
                new XElement(DocxNamespaces.Drawing + "fgClr", DrawingColor(hatch.Color)),
                new XElement(DocxNamespaces.Drawing + "bgClr", DrawingColor(fill.Start)));
        }

        if (fill.IsGradient || fill.Geometry != GradientGeometry.Linear)
        {
            var stops = new XElement(DocxNamespaces.Drawing + "gsLst", GradientStop(0, fill.Start));
            if (fill.Axial)
                stops.Add(GradientStop(50000, fill.End), GradientStop(100000, fill.Start));
            else
                stops.Add(GradientStop(100000, fill.End));

            var gradFill = new XElement(DocxNamespaces.Drawing + "gradFill", stops);
            if (fill.Geometry == GradientGeometry.Linear)
            {
                gradFill.Add(new XElement(
                    DocxNamespaces.Drawing + "lin",
                    new XAttribute(
                        "ang",
                        ((long)Math.Round(fill.AngleDegrees * 60000)).ToString(CultureInfo.InvariantCulture))));
            }
            else
            {
                string pathType = fill.Geometry == GradientGeometry.Radial ? "circle" : "rect";
                long l = (long)Math.Round(Math.Clamp(fill.CenterX, 0, 1) * 100000);
                long r = 100000 - l;
                long t = (long)Math.Round(Math.Clamp(fill.CenterY, 0, 1) * 100000);
                long b = 100000 - t;
                gradFill.Add(new XElement(
                    DocxNamespaces.Drawing + "path",
                    new XAttribute("path", pathType),
                    new XElement(
                        DocxNamespaces.Drawing + "fillToRect",
                        new XAttribute("l", l.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("t", t.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("r", r.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("b", b.ToString(CultureInfo.InvariantCulture)))));
            }
            return gradFill;
        }

        if (fill.Start.A == 0)
            return new XElement(DocxNamespaces.Drawing + "noFill");

        return new XElement(DocxNamespaces.Drawing + "solidFill", DrawingColor(fill.Start));
    }

    private static bool IsHorizontalHatch(double angle)
    {
        double norm = ((angle % 180) + 180) % 180;
        return norm is <= 15 or >= 165;
    }

    private static bool IsVerticalHatch(double angle)
    {
        double norm = ((angle % 180) + 180) % 180;
        return norm is >= 75 and <= 105;
    }

    private static bool IsDiagonalHatch(double angle)
    {
        double norm = ((angle % 180) + 180) % 180;
        return norm is >= 30 and <= 60 or >= 120 and <= 150;
    }

    private static bool IsNegativeDiagonalHatch(double angle)
    {
        double norm = ((angle % 180) + 180) % 180;
        return norm is >= 120 and <= 150;
    }

    private static XElement GradientStop(int position, BColor color) =>
        new(
            DocxNamespaces.Drawing + "gs",
            new XAttribute("pos", position.ToString(CultureInfo.InvariantCulture)),
            DrawingColor(color));

    /// <summary>
    /// A DrawingML colour: <c>a:srgbClr</c>, with an <c>a:alpha</c> when the
    /// colour is not opaque, as a theme colour read under one may not be.
    /// </summary>
    private static XElement DrawingColor(BColor color)
    {
        var element = new XElement(DocxNamespaces.Drawing + "srgbClr", new XAttribute("val", HexColor(color)));
        if (color.A < 255)
        {
            element.Add(new XElement(
                DocxNamespaces.Drawing + "alpha",
                new XAttribute("val", ((int)Math.Round(color.A / 255d * 100000)).ToString(CultureInfo.InvariantCulture))));
        }

        return element;
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

    private static XDocument BuildContentTypes(DocxWriteContext context, bool hasCore, bool hasApp)
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

        if (context.SettingsRelationshipId is not null)
        {
            types.Add(new XElement(
                DocxNamespaces.ContentTypes + "Override",
                new XAttribute("PartName", "/word/settings.xml"),
                new XAttribute("ContentType", DocxNamespaces.SettingsContentType)));
        }

        if (context.NotesOf(NoteKind.Footnote).Count > 0)
        {
            types.Add(new XElement(
                DocxNamespaces.ContentTypes + "Override",
                new XAttribute("PartName", "/word/footnotes.xml"),
                new XAttribute("ContentType", DocxNamespaces.FootnotesContentType)));
        }

        if (context.NotesOf(NoteKind.Endnote).Count > 0)
        {
            types.Add(new XElement(
                DocxNamespaces.ContentTypes + "Override",
                new XAttribute("PartName", "/word/endnotes.xml"),
                new XAttribute("ContentType", DocxNamespaces.EndnotesContentType)));
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

        if (hasCore)
        {
            types.Add(new XElement(
                DocxNamespaces.ContentTypes + "Override",
                new XAttribute("PartName", "/" + DocxMetadata.CorePart),
                new XAttribute("ContentType", DocxMetadata.CoreContentType)));
        }

        if (hasApp)
        {
            types.Add(new XElement(
                DocxNamespaces.ContentTypes + "Override",
                new XAttribute("PartName", "/" + DocxMetadata.AppPart),
                new XAttribute("ContentType", DocxMetadata.AppContentType)));
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

    /// <summary>
    /// The package-level relationships. The property parts hang off the package
    /// root rather than off the document part, which is where a consumer looks
    /// for them.
    /// </summary>
    private static XDocument BuildPackageRelationships(bool hasCore, bool hasApp)
    {
        var root = new XElement(
            DocxNamespaces.PackageRelationships + "Relationships",
            new XElement(
                DocxNamespaces.PackageRelationships + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", DocxNamespaces.OfficeDocumentRelationship),
                new XAttribute("Target", "word/document.xml")));

        if (hasCore)
        {
            root.Add(new XElement(
                DocxNamespaces.PackageRelationships + "Relationship",
                new XAttribute("Id", "rId2"),
                new XAttribute("Type", DocxMetadata.CoreRelationship),
                new XAttribute("Target", DocxMetadata.CorePart)));
        }

        if (hasApp)
        {
            root.Add(new XElement(
                DocxNamespaces.PackageRelationships + "Relationship",
                new XAttribute("Id", "rId3"),
                new XAttribute("Type", DocxMetadata.AppRelationship),
                new XAttribute("Target", DocxMetadata.AppPart)));
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
    }

    private static XDocument BuildDocumentRelationships(DocxWriteContext context)
    {
        var root = new XElement(DocxNamespaces.PackageRelationships + "Relationships");

        if (context.HasNumbering)
        {
            root.Add(new XElement(
                DocxNamespaces.PackageRelationships + "Relationship",
                new XAttribute("Id", DocxWriteContext.NumberingRelationshipId),
                new XAttribute("Type", DocxNamespaces.NumberingRelationship),
                new XAttribute("Target", "numbering.xml")));
        }

        if (context.SettingsRelationshipId is string settingsId)
        {
            root.Add(new XElement(
                DocxNamespaces.PackageRelationships + "Relationship",
                new XAttribute("Id", settingsId),
                new XAttribute("Type", DocxNamespaces.SettingsRelationship),
                new XAttribute("Target", "settings.xml")));
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

        if (context.FootnotesRelationshipId is string footnotesId)
        {
            root.Add(new XElement(
                DocxNamespaces.PackageRelationships + "Relationship",
                new XAttribute("Id", footnotesId),
                new XAttribute("Type", DocxNamespaces.FootnotesRelationship),
                new XAttribute("Target", "footnotes.xml")));
        }

        if (context.EndnotesRelationshipId is string endnotesId)
        {
            root.Add(new XElement(
                DocxNamespaces.PackageRelationships + "Relationship",
                new XAttribute("Id", endnotesId),
                new XAttribute("Type", DocxNamespaces.EndnotesRelationship),
                new XAttribute("Target", "endnotes.xml")));
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
    }

    /// <summary>
    /// The settings part, written only to turn on what the model holds: the
    /// page background's display and distinct even-page headers and footers,
    /// in the order CT_Settings gives them.
    /// </summary>
    private static XDocument BuildSettings(DocxWriteContext context)
    {
        var settings = new XElement(
            DocxNamespaces.Wordprocessing + "settings",
            new XAttribute(XNamespace.Xmlns + "w", DocxNamespaces.Wordprocessing.NamespaceName));
        if (context.DisplayBackgroundShape)
            settings.Add(new XElement(DocxNamespaces.Wordprocessing + "displayBackgroundShape"));
        // w:revisionView sits between the two in CT_Settings. Turning
        // insertions and deletions off is what LibreOffice reads as changes
        // hidden.
        if (context.HideRevisions)
        {
            settings.Add(new XElement(
                DocxNamespaces.Wordprocessing + "revisionView",
                WordAttribute("insDel", "0"),
                WordAttribute("formatting", "0")));
        }

        if (context.EvenAndOddHeaders)
            settings.Add(new XElement(DocxNamespaces.Wordprocessing + "evenAndOddHeaders"));

        // After w:evenAndOddHeaders in CT_Settings, footnotes before endnotes.
        if (context.NoteNumbering is NoteNumbering numbering)
        {
            settings.Add(NoteProperties("footnotePr", numbering.FootnoteFormat, numbering.FootnoteStart, numbering.FootnoteRestart, numbering.FootnotePosition));
            settings.Add(NoteProperties("endnotePr", numbering.EndnoteFormat, numbering.EndnoteStart));
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), settings);
    }

    private static XElement NoteProperties(
        string name,
        ListNumberFormat format,
        int start,
        NoteNumberingRestart restart = NoteNumberingRestart.Continuous,
        FootnotePosition position = FootnotePosition.PageBottom)
    {
        var element = new XElement(
            DocxNamespaces.Wordprocessing + name,
            new XElement(DocxNamespaces.Wordprocessing + "numFmt", WordAttribute("val", NumberFormat(format))),
            new XElement(
                DocxNamespaces.Wordprocessing + "numStart",
                WordAttribute("val", start.ToString(CultureInfo.InvariantCulture))));

        if (name == "footnotePr")
        {
            if (position != FootnotePosition.PageBottom)
            {
                string posVal = position switch
                {
                    FootnotePosition.BeneathText => "beneathText",
                    FootnotePosition.DocumentEnd => "docEnd",
                    _ => "pageBottom",
                };
                element.Add(new XElement(DocxNamespaces.Wordprocessing + "pos", WordAttribute("val", posVal)));
            }

            if (restart != NoteNumberingRestart.Continuous)
            {
                string restartVal = restart switch
                {
                    NoteNumberingRestart.EachPage => "eachPage",
                    NoteNumberingRestart.EachSection => "eachSect",
                    _ => "continuous",
                };
                element.Add(new XElement(DocxNamespaces.Wordprocessing + "numRestart", WordAttribute("val", restartVal)));
            }
        }

        return element;
    }

    /// <summary>
    /// The numbering part: an abstract numbering and a <c>w:num</c> for each
    /// list the document's paragraphs are numbered in (ECMA-376 §17.9), after the
    /// two plain lists every paragraph that names no list is written in.
    /// </summary>
    private static XDocument BuildNumbering(DocxWriteContext context)
    {
        var root = new XElement(
            DocxNamespaces.Wordprocessing + "numbering",
            new XAttribute(XNamespace.Xmlns + "w", DocxNamespaces.Wordprocessing.NamespaceName));

        var lists = context.Lists.ToList();
        root.Add(BuildAbstractNumber(1, ListDefinition.PlainBullets, context));
        root.Add(BuildAbstractNumber(2, ListDefinition.PlainNumbers, context));
        var groups = new Dictionary<ListDefinition, int>(ReferenceEqualityComparer.Instance);
        foreach ((ListDefinition definition, int numId) in lists)
        {
            if (groups.TryAdd(definition.CounterGroup, numId))
                root.Add(BuildAbstractNumber(numId, definition.CounterGroup, context));
        }

        root.Add(BuildNum(1, 1));
        root.Add(BuildNum(2, 2));
        foreach (int restart in context.LegacyRestarts)
        {
            root.Add(BuildNum(restart, 2, new XElement(
                DocxNamespaces.Wordprocessing + "lvlOverride",
                WordAttribute("ilvl", "0"),
                new XElement(DocxNamespaces.Wordprocessing + "startOverride", WordAttribute("val", "1")))));
        }

        foreach ((ListDefinition definition, int numId) in lists)
        {
            XElement num = BuildNum(numId, groups[definition.CounterGroup]);
            if (!ReferenceEquals(definition, definition.CounterGroup))
            {
                foreach (XElement level in BuildAbstractNumber(0, definition, context).Elements(DocxNamespaces.Wordprocessing + "lvl"))
                    num.Add(new XElement(DocxNamespaces.Wordprocessing + "lvlOverride",
                        WordAttribute("ilvl", (string)level.Attribute(DocxNamespaces.Wordprocessing + "ilvl")!),
                        new XElement(DocxNamespaces.Wordprocessing + "startOverride",
                            WordAttribute("val", DocxValueReader.WordValue(level.Element(DocxNamespaces.Wordprocessing + "start")) ?? "1")),
                        new XElement(level)));
            }
            root.Add(num);
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
    }

    private static XElement BuildNum(int numId, int abstractId, XElement? lvlOverride = null) =>
        new(
            DocxNamespaces.Wordprocessing + "num",
            WordAttribute("numId", numId.ToString(CultureInfo.InvariantCulture)),
            new XElement(DocxNamespaces.Wordprocessing + "abstractNumId", WordAttribute("val", abstractId.ToString(CultureInfo.InvariantCulture))),
            lvlOverride);

    private static XElement BuildAbstractNumber(int id, ListDefinition definition, DocxWriteContext context)
    {
        var abstractNum = new XElement(
            DocxNamespaces.Wordprocessing + "abstractNum",
            WordAttribute("abstractNumId", id.ToString(CultureInfo.InvariantCulture)));

        for (int level = 0; level < ListDefinition.MaxLevels; level++)
        {
            ListLevel style = definition.LevelAt(level);
            var lvl = new XElement(
                DocxNamespaces.Wordprocessing + "lvl",
                WordAttribute("ilvl", level.ToString(CultureInfo.InvariantCulture)),
                new XElement(DocxNamespaces.Wordprocessing + "start", WordAttribute("val", style.Start.ToString(CultureInfo.InvariantCulture))),
                new XElement(DocxNamespaces.Wordprocessing + "numFmt", WordAttribute("val", NumberFormat(style.Format))));

            // CT_Lvl is a sequence: w:lvlRestart, w:pStyle and w:isLgl come
            // between w:numFmt and w:lvlText.
            if (style.RestartAfter is int restartAfter)
            {
                lvl.Add(new XElement(
                    DocxNamespaces.Wordprocessing + "lvlRestart",
                    WordAttribute("val", (restartAfter < 0 ? 0 : restartAfter + 1).ToString(CultureInfo.InvariantCulture))));
            }

            if (style.Legal)
                lvl.Add(new XElement(DocxNamespaces.Wordprocessing + "isLgl"));

            lvl.Add(new XElement(DocxNamespaces.Wordprocessing + "lvlText", WordAttribute("val", style.Template)));
            lvl.Add(new XElement(
                DocxNamespaces.Wordprocessing + "pPr",
                new XElement(
                    DocxNamespaces.Wordprocessing + "ind",
                    WordAttribute("left", ((level + 1) * 360).ToString(CultureInfo.InvariantCulture)),
                    WordAttribute("hanging", "360"))));
            abstractNum.Add(lvl);
        }

        if (definition.Name is string name)
            abstractNum.AddFirst(new XElement(DocxNamespaces.Wordprocessing + "name", WordAttribute("val", name)));

        return abstractNum;
    }

    private static string NumberFormat(ListNumberFormat format) => format switch
    {
        ListNumberFormat.None => "none",
        ListNumberFormat.DecimalZero => "decimalZero",
        ListNumberFormat.LowerLetter => "lowerLetter",
        ListNumberFormat.UpperLetter => "upperLetter",
        ListNumberFormat.LowerRoman => "lowerRoman",
        ListNumberFormat.UpperRoman => "upperRoman",
        ListNumberFormat.Bullet => "bullet",
        _ => "decimal",
    };

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

    private sealed class DocxWriteContext(bool hasNumbering, DocumentConversionContext resources)
    {
        private readonly Dictionary<string, string> _hyperlinks = new(StringComparer.Ordinal);
        private readonly Dictionary<InlineImage, DocxImagePart> _images = new(ReferenceEqualityComparer.Instance);
        private readonly List<DocxImagePart> _imageOrder = [];
        private readonly Dictionary<string, string> _mediaContentTypes = new(StringComparer.Ordinal);
        private readonly List<DocumentDiagnostic> _diagnostics = [];
        private readonly HashSet<string> _diagnosticOnce = new(StringComparer.Ordinal);
        private readonly List<DocxRunningPart> _runningParts = [];
        private readonly Dictionary<RunningContent, IReadOnlyList<DocxRunningPart>> _runningSets =
            new(ReferenceEqualityComparer.Instance);
        private int _nextRelationshipId = hasNumbering ? 2 : 1;

        public bool HasNumbering { get; } = hasNumbering;

        /// <summary>
        /// What the caller's policy decided about this document's resources. A
        /// picture is not written unless this says it may be.
        /// </summary>
        public DocumentConversionContext Resources { get; } = resources;

        public bool HasDocumentRelationships =>
            HasNumbering || _hyperlinks.Count > 0 || _imageOrder.Count > 0 || _runningParts.Count > 0 ||
            SettingsRelationshipId is not null || FootnotesRelationshipId is not null || EndnotesRelationshipId is not null;

        /// <summary>The settings part's relationship, or null when none is written.</summary>
        public string? SettingsRelationshipId { get; private set; }

        /// <summary>Whether the settings part turns on the page background's display.</summary>
        public bool DisplayBackgroundShape { get; private set; }

        /// <summary>Whether the settings part turns on distinct even-page headers and footers.</summary>
        public bool EvenAndOddHeaders { get; private set; }

        /// <summary>How the settings part numbers notes, or null to leave a reader's default.</summary>
        public NoteNumbering? NoteNumbering { get; private set; }

        private readonly List<DocumentNote> _footnotes = [];
        private readonly List<DocumentNote> _endnotes = [];

        /// <summary>True while a note's own paragraphs are being written.</summary>
        public bool InNote { get; set; }

        /// <summary>The footnotes part's relationship, or null when no footnote is cited.</summary>
        public string? FootnotesRelationshipId { get; private set; }

        /// <summary>The endnotes part's relationship, or null when no endnote is cited.</summary>
        public string? EndnotesRelationshipId { get; private set; }

        /// <summary>The notes of one kind, in the order cited.</summary>
        public IReadOnlyList<DocumentNote> NotesOf(NoteKind kind) => kind == NoteKind.Endnote ? _endnotes : _footnotes;

        /// <summary>
        /// Registers a cited note and returns its id in its part: 1 for the first
        /// of its kind, after the two separators at -1 and 0.
        /// </summary>
        public int AddNote(DocumentNote note)
        {
            List<DocumentNote> notes = note.Kind == NoteKind.Endnote ? _endnotes : _footnotes;
            int index = notes.FindIndex(existing => ReferenceEquals(existing, note));
            if (index >= 0)
                return index + 1;

            notes.Add(note);
            if (note.Kind == NoteKind.Endnote)
                EndnotesRelationshipId ??= NextRelationshipId();
            else
                FootnotesRelationshipId ??= NextRelationshipId();

            return notes.Count;
        }

        private string NextRelationshipId()
        {
            string id = "rId" + _nextRelationshipId.ToString(CultureInfo.InvariantCulture);
            _nextRelationshipId++;
            return id;
        }

        /// <summary>Registers the settings part, once, with the switches named turned on.</summary>
        public void AddSettings(
            bool displayBackgroundShape = false,
            bool evenAndOddHeaders = false,
            NoteNumbering? noteNumbering = null,
            bool hideRevisions = false)
        {
            DisplayBackgroundShape |= displayBackgroundShape;
            EvenAndOddHeaders |= evenAndOddHeaders;
            HideRevisions |= hideRevisions;
            NoteNumbering ??= noteNumbering;
            if (SettingsRelationshipId is not null)
                return;

            SettingsRelationshipId = "rId" + _nextRelationshipId.ToString(CultureInfo.InvariantCulture);
            _nextRelationshipId++;
        }

        public static string NumberingRelationshipId => "rId1";

        /// <summary>Whether the settings part says the document's changes are hidden.</summary>
        public bool HideRevisions { get; private set; }

        /// <summary>True while a deleted run is written, whose text is <c>w:delText</c>.</summary>
        public bool InDeletion { get; set; }

        private int _nextRevisionId;

        /// <summary>The next <c>w:id</c> for a tracked change.</summary>
        public int NextRevisionId() => _nextRevisionId++;

        private readonly Dictionary<ListDefinition, int> _listNumIds = new(ReferenceEqualityComparer.Instance);
        private readonly List<ListDefinition> _listOrder = [];
        private readonly List<int> _legacyRestarts = [];
        private int _nextNumId = 3;
        private bool _legacyRunOpen;
        private bool _legacyRunSeen;

        /// <summary>The lists the document numbers paragraphs in, each with its <c>w:numId</c>, in the order first used.</summary>
        public IEnumerable<(ListDefinition Definition, int NumId)> Lists =>
            _listOrder.Select(definition => (definition, _listNumIds[definition]));

        /// <summary>The <c>w:num</c>s that restart the plain numbered list, one per run of it after the first.</summary>
        public IReadOnlyList<int> LegacyRestarts => _legacyRestarts;

        /// <summary>The <c>w:numId</c> a list is written under, given on first use.</summary>
        public int NumIdFor(ListDefinition definition)
        {
            if (_listNumIds.TryGetValue(definition, out int numId))
                return numId;

            numId = _nextNumId++;
            _listNumIds[definition] = numId;
            _listOrder.Add(definition);
            return numId;
        }

        /// <summary>
        /// The <c>w:numId</c> for a numbered paragraph that names no list. Such a
        /// list counts from one again after every paragraph that is not in it,
        /// and in DOCX a list counts on unless it is a list of its own, so each
        /// run after the first gets a <c>w:num</c> that starts over.
        /// </summary>
        public int LegacyNumberedId()
        {
            if (_legacyRunOpen)
                return _legacyRestarts.Count == 0 ? 2 : _legacyRestarts[^1];

            _legacyRunOpen = true;
            if (!_legacyRunSeen)
            {
                _legacyRunSeen = true;
                return 2;
            }

            int numId = _nextNumId++;
            _legacyRestarts.Add(numId);
            return numId;
        }

        /// <summary>Notes a paragraph in no list, which ends a run of the plain numbered list.</summary>
        public void EndLegacyList() => _legacyRunOpen = false;

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

        /// <summary>Records the parts written for one set of headers and footers.</summary>
        public void AddRunningSet(RunningContent running, IReadOnlyList<DocxRunningPart> parts) =>
            _runningSets[running] = parts;

        /// <summary>The parts written for <paramref name="running"/>, which a section's properties name.</summary>
        public IReadOnlyList<DocxRunningPart> RunningSet(RunningContent running) =>
            _runningSets.TryGetValue(running, out IReadOnlyList<DocxRunningPart>? parts) ? parts : [];

        /// <summary>The media parts to write, in the order they were first used.</summary>
        public IReadOnlyList<DocxImagePart> Images => _imageOrder;

        /// <summary>Media extension to content type, for the package's content-type defaults.</summary>
        public IReadOnlyDictionary<string, string> MediaContentTypes => _mediaContentTypes;

        public IReadOnlyList<DocumentDiagnostic> Diagnostics => _diagnostics;

        /// <summary>
        /// Says what became of the caller's metadata. OOXML states one producing
        /// application and no separate authoring one, so a CreatorApplication the
        /// caller set has nowhere to go and is reported rather than folded into
        /// the Application element, which would assert something else.
        /// </summary>
        public void ReportMetadata(DocumentMetadata metadata)
        {
            // The two property parts are written exactly when they have content,
            // so what a caller set either reached one of them or is on this list.
            DocumentMetadataReport.Describe(
                metadata,
                [nameof(metadata.CreatorApplication)],
                _diagnostics);
        }

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
        public DocxImagePart GetImagePart(InlineImage image, ReadOnlyMemory<byte> data, string contentType)
        {
            if (_images.TryGetValue(image, out DocxImagePart? existing))
                return existing;

            int index = _imageOrder.Count + 1;
            string extension = DocumentImageFormats.ExtensionForContentType(contentType);
            string fileName = "image" + index.ToString(CultureInfo.InvariantCulture) + "." + extension;
            var part = new DocxImagePart(
                index,
                "rId" + _nextRelationshipId.ToString(CultureInfo.InvariantCulture),
                "word/media/" + fileName,
                "media/" + fileName,
                data);
            _nextRelationshipId++;
            _images[image] = part;
            _imageOrder.Add(part);
            _mediaContentTypes[extension] = contentType;
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
