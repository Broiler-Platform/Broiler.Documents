using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Broiler.Documents.Model;
using Broiler.Documents.Resources;
using Broiler.Graphics.Color;

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

            var resources = new DocumentConversionContextBuilder(options.ResourcePolicy);
            var images = new DocxImageLoader(archive, options.Limits, resources);
            var reported = new HashSet<string>(StringComparer.Ordinal);
            // Read before the content, not after it: an anchor states which frame
            // its offsets are measured from, and converting one to the column the
            // model anchors against needs the margins first. The sections come
            // first of all, because a section's properties follow its content.
            DocxSettings settings = ReadSettings(archive, documentRelationships, baseDirectory, options.Limits, diagnostics);
            DocxSections sections = DocxSections.Read(documentXml.Root?.Element(DocxNamespaces.Wordprocessing + "body"));
            PageGeometry? page = ReadPageGeometry(documentXml, sections, diagnostics);
            DocxNotes notes = DocxNotes.Load(
                archive,
                documentRelationships,
                baseDirectory,
                settings.Root,
                documentXml.Root?.Element(DocxNamespaces.Wordprocessing + "body")?.Element(DocxNamespaces.Wordprocessing + "sectPr"),
                options.Limits,
                diagnostics);
            RichTextDocument document = ReadDocumentXml(
                documentXml,
                documentRelationships,
                numbering,
                styles,
                images,
                options.Limits,
                diagnostics,
                reported,
                page,
                sections,
                notes);
            IReadOnlyList<RunningContent> running = ReadRunningContent(
                archive,
                documentXml,
                documentRelationships,
                numbering,
                styles,
                images,
                options.Limits,
                diagnostics,
                reported,
                page,
                settings.EvenAndOddHeaders);
            document = document.WithRunningContent(running[^1]);
            document = document.WithPageGeometry(page);
            document = document.WithSections(sections.ToModel(document.ParagraphCount, page, running));
            document = document.WithStyleDefaults(ReadStyleDefaults(styles));
            document = document.WithNoteNumbering(notes.Numbering with { RaisedLabels = notes.LabelsRaised ?? false });
            document = document.WithShowsRevisions(DocxRevisions.Shown(settings.Root));
            void ReportBackground(string code, string message)
            {
                if (reported.Add(code))
                    diagnostics.Add(DocumentDiagnostic.Warning(code, message));
            }
            document = document.WithPageBackground(DocxVml.ReadBackground(
                documentXml.Root?.Element(DocxNamespaces.Wordprocessing + "background"),
                settings.DisplayBackgroundShape,
                ReportBackground,
                fill => images.ReadFillPicture(fill, documentRelationships, ReportBackground)));
            return new DocumentReadResult(
                document,
                diagnostics,
                DocumentReadResult.StatusFrom(diagnostics),
                resources.Build(),
                DocxMetadata.Read(archive, options.Limits, diagnostics));
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
        HashSet<string> reported,
        PageGeometry? page,
        DocxSections sections,
        DocxNotes notes)
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
        var context = new DocxReadContext(relationships, numbering, styles, images, builder, page, Sections: sections, Notes: notes);
        ReadBlockContent(body.Elements(), context, depth: 0);
        builder.ReportReadSummary(body.Elements().Any(IsContentBlock), styles.Count, images.ImageCount);
        return builder.Build();
    }

    /// <summary>
    /// Walks WordprocessingML block-level content — the <c>EG_BlockLevelElts</c>
    /// group. Every container contributes its paragraphs in document order rather
    /// than being dropped; a table also records the grid they are arranged in.
    /// Anything genuinely not understood raises a diagnostic instead of vanishing.
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
                DocxTableReader.Read(element, context, depth + 1, ReadBlockContent);
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

            // A tracked change around whole paragraphs marks everything in them;
            // the deleted side is kept, as LibreOffice keeps it.
            if (DocxRevisions.Of(element) is TextRevision blockRevision)
            {
                ReadBlockContent(element.Elements(), context with { Revision = blockRevision }, depth + 1);
                continue;
            }

            if (name == DocxNamespaces.Wordprocessing + "customXml" ||
                name == DocxNamespaces.Wordprocessing + "smartTag")
            {
                ReadBlockContent(element.Elements(), context, depth + 1);
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
    /// Reads the header and footer parts each section's properties name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ECMA-376 §17.10.5 gives every section its own references, and a section
    /// that names no header of a type continues the one the section before it
    /// had, so each type a section leaves out is taken from the nearest section
    /// before it that names one. A contract whose first section names a header
    /// and footer and whose eight after it name none carries them on every page
    /// in Word and LibreOffice. The result holds one set per section, in the
    /// order of <see cref="DocxSections.Properties"/>; the last is the document's.
    /// </para>
    /// <para>
    /// A part a section does not turn on is not read for it. A <c>first</c> part
    /// is drawn only in a section with <c>w:titlePg</c> (§17.10.6), which is the
    /// section's own and not continued, and an <c>even</c> part only where
    /// <c>w:settings</c> holds <c>w:evenAndOddHeaders</c>; without them Word puts
    /// the default on those pages, and the model, which uses a first or even
    /// part wherever there is one, would put the other there.
    /// </para>
    /// <para>
    /// Each part carries its own relationships, so a picture in a header resolves
    /// against <c>word/_rels/header1.xml.rels</c> and not against the document's.
    /// A part several sections name is read once.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<RunningContent> ReadRunningContent(
        ZipArchive archive,
        XDocument documentXml,
        DocxRelationships documentRelationships,
        DocxNumbering numbering,
        DocxStyles styles,
        DocxImageLoader images,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics,
        HashSet<string> reported,
        PageGeometry? page,
        bool evenAndOddHeaders)
    {
        XElement? body = documentXml.Root?.Element(DocxNamespaces.Wordprocessing + "body");
        if (body?.Elements(DocxNamespaces.Wordprocessing + "sectPr").LastOrDefault() is null)
            return [RunningContent.Empty];

        IReadOnlyList<XElement> sections = DocxSections.Properties(body);
        var parts = new Dictionary<(string Id, bool IsHeader), (IReadOnlyList<RichTextParagraph> Paragraphs, IReadOnlyList<DocumentShape> Shapes)?>();
        var contents = new List<RunningContent>(sections.Count);
        for (int index = 0; index < sections.Count; index++)
        {
            // w:titlePg is what makes a `first` reference mean anything, and it is
            // also what makes a band the first page does not name empty there rather
            // than the default one. Read before the references, because a first-page
            // header with no first-page footer beside it is exactly the arrangement it
            // decides: LibreOffice draws no footer on page one of such a section, and
            // this reader drew the default one carrying a stale cached page number.
            XElement? titlePage = sections[index].Element(DocxNamespaces.Wordprocessing + "titlePg");
            bool differentFirstPage = titlePage is not null && ReadOnOff(titlePage);
            RunningContent content = RunningContent.Empty.WithDifferentFirstPage(differentFirstPage);

            foreach (XElement reference in ContinuedReferences(sections, index))
            {
                bool isHeader = reference.Name == DocxNamespaces.Wordprocessing + "headerReference";
                PageSelection selection = SelectionFor((string?)reference.Attribute(DocxNamespaces.Wordprocessing + "type"));
                if (selection == PageSelection.First && !differentFirstPage)
                {
                    if (reported.Add("docx.header.first"))
                    {
                        diagnostics.Add(DocumentDiagnostic.Info(
                            "docx.header.first",
                            "A DOCX section has a first-page header or footer but does not set w:titlePg, " +
                            "so the part was not read for it and its first page carries the default one."));
                    }

                    continue;
                }

                if (selection == PageSelection.Even && !evenAndOddHeaders)
                {
                    if (reported.Add("docx.header.even"))
                    {
                        diagnostics.Add(DocumentDiagnostic.Info(
                            "docx.header.even",
                            "A DOCX even-page header or footer was not read: the document does not set " +
                            "w:evenAndOddHeaders, so even pages carry the default one."));
                    }

                    continue;
                }

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

                if (!parts.TryGetValue((relationshipId, isHeader), out var part))
                {
                    part = ReadPart(
                        archive, relationship.Target, numbering, styles, images, limits, diagnostics, reported,
                        isHeader ? RunningBand.Header : RunningBand.Footer,
                        page);
                    parts.Add((relationshipId, isHeader), part);
                }

                if (part is not var (paragraphs, shapes) || (paragraphs.Count == 0 && shapes.Count == 0))
                    continue;

                content = isHeader
                    ? content.WithHeader(selection, paragraphs, shapes)
                    : content.WithFooter(selection, paragraphs, shapes);
            }

            contents.Add(content);
        }

        return contents;
    }

    /// <summary>
    /// The header and footer references that apply to section
    /// <paramref name="index"/> of <paramref name="sections"/>: for each band and
    /// type, the section's own, or else the nearest earlier section's.
    /// </summary>
    private static IEnumerable<XElement> ContinuedReferences(IReadOnlyList<XElement> sections, int index)
    {
        foreach (string band in new[] { "headerReference", "footerReference" })
        {
            foreach (PageSelection selection in new[] { PageSelection.Default, PageSelection.First, PageSelection.Even })
            {
                for (int i = index; i >= 0; i--)
                {
                    XElement? reference = sections[i]
                        .Elements(DocxNamespaces.Wordprocessing + band)
                        .FirstOrDefault(candidate =>
                            SelectionFor((string?)candidate.Attribute(DocxNamespaces.Wordprocessing + "type")) == selection);
                    if (reference is not null)
                    {
                        yield return reference;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>The settings (ECMA-376 §17.15) that change what this reader reads.</summary>
    /// <param name="EvenAndOddHeaders">
    /// Whether distinct even-page headers and footers are on
    /// (<c>w:evenAndOddHeaders</c>, §17.15.1.35).
    /// </param>
    /// <param name="DisplayBackgroundShape">
    /// Whether the page background is shown (<c>w:displayBackgroundShape</c>,
    /// §17.15.1.25).
    /// </param>
    private readonly record struct DocxSettings(bool EvenAndOddHeaders, bool DisplayBackgroundShape, XElement? Root = null);

    /// <summary>
    /// The settings part's switches this reader acts on. A package with no
    /// settings part turns none of them on.
    /// </summary>
    private static DocxSettings ReadSettings(
        ZipArchive archive,
        DocxRelationships documentRelationships,
        string baseDirectory,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        string settingsPath = DocxPackage.ResolvePartPath(documentRelationships, DocxNamespaces.SettingsRelationship,
            baseDirectory, "settings.xml");
        ZipArchiveEntry? entry = DocxPackage.FindEntry(archive, settingsPath);
        if (entry is null)
            return default;

        XElement? root = DocxPackage.LoadEntryXml(entry, limits, diagnostics, "docx.settings")?.Root;
        XElement? evenAndOdd = root?.Element(DocxNamespaces.Wordprocessing + "evenAndOddHeaders");
        XElement? displayBackground = root?.Element(DocxNamespaces.Wordprocessing + "displayBackgroundShape");
        return new DocxSettings(
            evenAndOdd is not null && ReadOnOff(evenAndOdd),
            displayBackground is not null && ReadOnOff(displayBackground),
            root);
    }

    /// <summary>
    /// Reads the page the document says it is written for from its section
    /// properties.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The body-level w:sectPr is the document's final section. A document split
    /// into several can give each its own page; the last one is the document's -
    /// the same choice the header and footer reader makes, and for the same
    /// reason: it is the section the running content belongs to - and a section
    /// that states another keeps it as its own <see cref="DocumentSection.Page"/>.
    /// Geometry a producer states nonsensically is dropped rather than honoured.
    /// </para>
    /// <para>
    /// The columns are the exception, and are taken from the section that holds
    /// the most paragraphs; <see cref="DocxSections.Dominant"/> says why.
    /// </para>
    /// </remarks>
    private static PageGeometry? ReadPageGeometry(
        XDocument documentXml,
        DocxSections sections,
        List<DocumentDiagnostic> diagnostics)
    {
        XElement? sectPr = documentXml.Root
            ?.Element(DocxNamespaces.Wordprocessing + "body")
            ?.Elements(DocxNamespaces.Wordprocessing + "sectPr")
            .LastOrDefault();

        if (sectPr?.Element(DocxNamespaces.Wordprocessing + "pgSz") is null)
            return null;

        if (DocxSections.ReadPage(sectPr) is not PageGeometry geometry)
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                "docx.section.geometry",
                "DOCX section properties gave a page with no room to write on; the page was not read."));
            return null;
        }

        return ReadColumns(geometry, sections, diagnostics);
    }

    /// <summary>Divides the page into the columns the dominant section states.</summary>
    private static PageGeometry ReadColumns(
        PageGeometry geometry,
        DocxSections sections,
        List<DocumentDiagnostic> diagnostics)
    {
        if (sections.ColumnsDiffer)
        {
            diagnostics.Add(DocumentDiagnostic.Info(
                "docx.section.columns",
                "DOCX sections differ in their columns; each section keeps its own, and the page records " +
                "those of the section holding the most paragraphs."));
        }

        if (sections.HasUnequalColumns)
        {
            diagnostics.Add(DocumentDiagnostic.Info(
                "docx.columns.unequal",
                "A DOCX section listed columns of unequal width; they were read as equal columns."));
        }

        DocxColumns columns = sections.Dominant;
        if (columns.Count <= 1)
            return geometry;

        PageGeometry divided = geometry.WithColumns(columns.Count, columns.Gap);
        if (!divided.HasColumns)
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                "docx.columns.geometry",
                "DOCX section columns left no room to write in a column; the page was read as one column."));
        }

        return divided;
    }

    /// <summary>ECMA-376 §17.10.1: the header/footer types, defaulting to the one for every page.</summary>
    private static PageSelection SelectionFor(string? type) => type switch
    {
        "first" => PageSelection.First,
        "even" => PageSelection.Even,
        _ => PageSelection.Default,
    };

    /// <summary>
    /// Reads one header or footer part into the paragraphs and the shapes it
    /// holds, or null when the part is missing or unreadable.
    /// </summary>
    /// <remarks>
    /// A part that holds only a stripe and no words is still a part, so the
    /// shapes come back beside the paragraphs rather than being reported through
    /// an empty paragraph list.
    /// </remarks>
    private static (IReadOnlyList<RichTextParagraph> Paragraphs, IReadOnlyList<DocumentShape> Shapes)? ReadPart(
        ZipArchive archive,
        string partPath,
        DocxNumbering numbering,
        DocxStyles styles,
        DocxImageLoader images,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics,
        HashSet<string> reported,
        RunningBand band,
        PageGeometry? page)
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
        var context = new DocxReadContext(
            partRelationships, numbering, styles, images, builder, page, band);
        ReadBlockContent(partXml.Root.Elements(), context, depth: 0);

        RichTextDocument part = builder.Build();
        return (part.Paragraphs, part.Shapes);
    }

    private static void ReadParagraph(XElement paragraph, DocxReadContext context)
    {
        // An empty paragraph that holds nothing but its section's properties is
        // the section mark, not a line: the section ends at the paragraph
        // before it. LibreOffice's import drops it the same way (writerfilter's
        // DomainMapper, keeping the section's properties). tdf170602_exportContext.docx
        // ends a paragraph on a page break and puts the next-page section mark
        // in an empty Heading 1 after it, which drew as a page holding only a
        // heading number where LibreOffice starts the next chapter.
        if (context.Sections is not null && IsDroppableSectionMark(paragraph, context))
        {
            EndSection(context);
            return;
        }

        XElement? pPr = paragraph.Element(DocxNamespaces.Wordprocessing + "pPr");
        string? paragraphStyleId = DocxValueReader.WordValue(pPr?.Element(DocxNamespaces.Wordprocessing + "pStyle"));

        ParagraphStyle paragraphStyle = ReadParagraphStyle(pPr, paragraphStyleId, context);

        // An empty paragraph that is there only to end a section is not an item
        // of the list its style numbers. Word writes the section break on a
        // paragraph of the style it was typed in, a heading's often, and
        // LibreOffice, which keeps no such paragraph, gives it no number; counted,
        // it would take "1" from the heading after it.
        if (paragraphStyle.List is not null && IsBareSectionBreak(paragraph, pPr))
            paragraphStyle = paragraphStyle with { List = null, ListKind = ListKind.None };

        // A change of the paragraph's own properties is drawn on its text, as
        // LibreOffice draws its ParagraphFormat change: as a change of
        // formatting, where no insertion or deletion marks the text already.
        InlineStyle inherited = ReadInheritedRunStyle(paragraphStyleId, context);
        if (context.Revision is not null)
            inherited = inherited with { Revision = context.Revision };
        else if (DocxRevisions.OfParagraphProperties(pPr) is TextRevision paragraphChange)
            inherited = inherited with { Revision = paragraphChange };

        // A paragraph mark a change inserted or deleted says so in its own w:rPr.
        InlineStyle markStyle = ReadMarkStyle(pPr, inherited, context);
        if (DocxRevisions.OfProperties(pPr?.Element(DocxNamespaces.Wordprocessing + "rPr"), includeFormat: false)
            is TextRevision markRevision)
        {
            markStyle = markStyle with { Revision = markRevision };
        }

        context.Builder.StartParagraph(paragraphStyle, markStyle);
        ReadParagraphChildren(paragraph.Elements(), context, inherited);
        context.Builder.FinishParagraph();

        // A paragraph belongs to the section it ends, so the cursor moves only
        // once the paragraph's own breaks have been read against it.
        if (context.Sections is not null && DocxSections.EndsSection(paragraph))
            EndSection(context);
    }

    /// <summary>
    /// Moves the read into the next section. It says whether it starts a page;
    /// its first paragraph is where that break lands, as if the paragraph before
    /// had ended on one, and a page break that paragraph did end on is the same
    /// boundary rather than a second.
    /// </summary>
    private static void EndSection(DocxReadContext context)
    {
        context.Sections!.Advance(context.Builder.CurrentParagraphIndex);
        if (context.Sections.CurrentStartsPage)
            context.Builder.HoldPageBreak();

        if (context.Sections.CurrentWantsParity)
        {
            context.Builder.AddDiagnosticOnce(
                "docx.section.parity",
                "A DOCX section starting on an odd or even page was read as starting on the next page; " +
                "the blank page it can put before itself is not made.");
        }
    }

    /// <summary>
    /// True when <paramref name="paragraph"/> is a body paragraph with nothing in
    /// it but a <c>w:sectPr</c>, which LibreOffice does not make a paragraph of.
    /// </summary>
    /// <remarks>
    /// As there, it is kept where it is all its section holds, which would
    /// otherwise vanish, and where no paragraph follows it for the section after
    /// it to start on. A column break, a field, a picture or any text keeps it:
    /// those are content, and the conditions LibreOffice keeps one under. So
    /// does a table just before it, below which it closes the section.
    /// </remarks>
    private static bool IsDroppableSectionMark(XElement paragraph, DocxReadContext context)
    {
        if (!DocxSections.EndsSection(paragraph) || paragraph.Parent?.Name != DocxNamespaces.Wordprocessing + "body")
            return false;

        // After a table it is what closes the section below the table, which is
        // Word's own shape for a section ending in one; it stays.
        if (paragraph.ElementsBeforeSelf().LastOrDefault()?.Name == DocxNamespaces.Wordprocessing + "tbl")
            return false;

        foreach (XElement child in paragraph.Elements())
        {
            if (child.Name.Namespace != DocxNamespaces.Wordprocessing)
                return false;

            switch (child.Name.LocalName)
            {
                case "pPr" or "bookmarkStart" or "bookmarkEnd" or "proofErr" or "permStart" or "permEnd":
                    continue;
                case "r" when child.Elements().All(static part =>
                    part.Name == DocxNamespaces.Wordprocessing + "rPr" ||
                    part.Name == DocxNamespaces.Wordprocessing + "lastRenderedPageBreak"):
                    continue;
                default:
                    return false;
            }
        }

        return context.Sections!.CurrentHasParagraphs(context.Builder.CurrentParagraphIndex) &&
            context.Sections.NextHasParagraphs;
    }

    /// <summary>
    /// True when a paragraph holds a section's <c>w:sectPr</c> and nothing a run
    /// draws.
    /// </summary>
    private static bool IsBareSectionBreak(XElement paragraph, XElement? pPr)
    {
        XNamespace w = DocxNamespaces.Wordprocessing;
        if (pPr?.Element(w + "sectPr") is null)
            return false;

        foreach (XElement child in paragraph.Elements())
        {
            if (child == pPr || child.Name == w + "bookmarkStart" || child.Name == w + "bookmarkEnd" ||
                child.Name == w + "proofErr")
            {
                continue;
            }

            if (child.Name != w + "r" ||
                child.Elements().Any(e => e.Name != w + "rPr" && e.Name != w + "lastRenderedPageBreak"))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The character formatting a run inherits before its own <c>w:rPr</c>:
    /// document defaults, the table style's run properties inside a table, then
    /// the paragraph style chain's run properties.
    /// </summary>
    private static InlineStyle ReadInheritedRunStyle(string? paragraphStyleId, DocxReadContext context)
    {
        InlineStyle style = InlineStyle.Default;
        foreach (XElement rPr in context.Styles.RunPropertiesForParagraph(paragraphStyleId, context.TableStyle, context.TableConditions))
            style = ApplyRunProperties(rPr, style, context.Styles.Theme);

        return style;
    }

    /// <summary>
    /// The paragraph mark's formatting: what a run would inherit, then the
    /// <c>w:rPr</c> inside <c>w:pPr</c>, which ECMA-376 §17.3.1.29 says applies to
    /// the mark and to nothing else.
    /// </summary>
    /// <remarks>
    /// It is read as a run's is, character style first, so a mark that names
    /// one gets what the style says.
    /// </remarks>
    private static InlineStyle ReadMarkStyle(XElement? pPr, InlineStyle inherited, DocxReadContext context)
    {
        XElement? rPr = pPr?.Element(DocxNamespaces.Wordprocessing + "rPr");
        if (rPr is null)
            return inherited;

        InlineStyle style = inherited;
        string? characterStyleId = DocxValueReader.WordValue(rPr.Element(DocxNamespaces.Wordprocessing + "rStyle"));
        foreach (XElement styleRunProperties in context.Styles.RunPropertiesForCharacterStyle(characterStyleId))
            style = ApplyRunProperties(styleRunProperties, style, context.Styles.Theme);

        return ApplyRunProperties(rPr, style, context.Styles.Theme);
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

            if (DocxRevisions.Of(element) is TextRevision revision)
            {
                ReadParagraphChildren(element.Elements(), context, style with { Revision = revision });
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
        _ = context.Builder;
        XElement? rPr = run.Element(DocxNamespaces.Wordprocessing + "rPr");

        // ECMA-376 §17.7.2: the character style named by w:rStyle applies over
        // the paragraph's inherited formatting, and the run's own w:rPr over that.
        InlineStyle style = inherited;
        string? characterStyleId = DocxValueReader.WordValue(rPr?.Element(DocxNamespaces.Wordprocessing + "rStyle"));
        foreach (XElement styleRunProperties in context.Styles.RunPropertiesForCharacterStyle(characterStyleId))
            style = ApplyRunProperties(styleRunProperties, style, context.Styles.Theme);

        style = ApplyRunProperties(rPr, style, context.Styles.Theme);

        // A run inserted or deleted is that change whatever else happened to
        // it; one that is neither may record that its formatting changed.
        if (style.Revision is null &&
            DocxRevisions.OfProperties(rPr, includeFormat: true) is TextRevision change)
        {
            style = style with { Revision = change };
        }

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
        DocumentNote? awaitingMark = null;
        InlineStyle awaitingStyle = style;
        foreach (XElement child in elements)
        {
            if (child.Name == DocxNamespaces.Wordprocessing + "rPr")
                continue;

            if (child.Name == DocxNamespaces.Wordprocessing + "t")
            {
                if (awaitingMark is not null)
                {
                    // w:customMarkFollows: the text after the reference is the
                    // note's own label, not body text.
                    var marked = new DocumentNote(awaitingMark.Kind, awaitingMark.Paragraphs, child.Value.Trim());
                    builder.AppendText(DocumentNote.PlaceholderText, awaitingStyle with { Note = marked });
                    awaitingMark = null;
                    continue;
                }

                builder.AppendText(child.Value, style);
                continue;
            }

            if (child.Name == DocxNamespaces.Wordprocessing + "footnoteReference" ||
                child.Name == DocxNamespaces.Wordprocessing + "endnoteReference")
            {
                DocumentNote? note = ReadNote(child, context);
                if (note is null)
                    continue;

                if (child.Attribute(DocxNamespaces.Wordprocessing + "customMarkFollows") is XAttribute follows &&
                    (string?)follows is "1" or "true" or "on")
                {
                    awaitingMark = note;
                    awaitingStyle = style with { Image = null };
                    continue;
                }

                builder.AppendText(DocumentNote.PlaceholderText, style with { Note = note, Image = null });
                continue;
            }

            // The label a note's own text repeats: drawn from the citation, so
            // not read as text.
            if (child.Name == DocxNamespaces.Wordprocessing + "footnoteRef" ||
                child.Name == DocxNamespaces.Wordprocessing + "endnoteRef")
            {
                context.SkipNoteLabel?.Invoke(child.Parent);
                continue;
            }

            if (child.Name == DocxNamespaces.Wordprocessing + "tab")
            {
                builder.AppendText("\t", style);
                continue;
            }

            // The break Ctrl+Enter writes is a run child, not a paragraph
            // property, and it sits at the end of the paragraph *before* the
            // one that starts the page. The builder holds it until then; see
            // DocxDocumentBuilder.NotePageBreak.
            if (child.Name == DocxNamespaces.Wordprocessing + "br" && IsPageBreak(child, context))
            {
                builder.NotePageBreak(style, sourceColumn: IsColumnBreak(child));
                continue;
            }

            // In a section of several columns the break moves to the next one,
            // and travels to the paragraph it starts exactly as a page break does.
            if (child.Name == DocxNamespaces.Wordprocessing + "br" && IsColumnBreak(child) && context.Sections is not null)
            {
                builder.NotePageBreak(style, column: true, sourceColumn: true);
                continue;
            }

            if (child.Name == DocxNamespaces.Wordprocessing + "br" ||
                child.Name == DocxNamespaces.Wordprocessing + "cr")
            {
                builder.AppendText(((char)0x2028).ToString(), style);
                continue;
            }

            if (child.Name == DocxNamespaces.Wordprocessing + "sym")
            {
                // Symbol fonts use the private-use code point stated by w:char;
                // keep that code point together with its font, rather than
                // interpreting its low byte as ordinary Unicode text.
                string? hex = (string?)child.Attribute(DocxNamespaces.Wordprocessing + "char");
                if (ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort symbol) &&
                    !char.IsSurrogate((char)symbol) && !char.IsControl((char)symbol))
                {
                    string? font = (string?)child.Attribute(DocxNamespaces.Wordprocessing + "font");
                    builder.AppendText(((char)symbol).ToString(),
                        string.IsNullOrWhiteSpace(font) ? style : style with { FontFamily = font });
                }
                else
                {
                    builder.AddDiagnosticOnce("docx.symbol", "A DOCX symbol had an invalid character code and was skipped.");
                }
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

            // A deleted run's text: kept, marked by the w:del around the run.
            if (child.Name == DocxNamespaces.Wordprocessing + "delText")
            {
                builder.AppendText(child.Value, style);
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

        // A reference whose custom mark never came is numbered after all.
        if (awaitingMark is not null)
            builder.AppendText(DocumentNote.PlaceholderText, awaitingStyle with { Note = awaitingMark });
    }

    /// <summary>
    /// Reads the note a <c>w:footnoteReference</c> or <c>w:endnoteReference</c>
    /// names, from its part, into a note of its own paragraphs.
    /// </summary>
    /// <remarks>
    /// The note's first paragraph opens with <c>w:footnoteRef</c>, Word's
    /// repetition of the label, and then the space Word puts between label and
    /// text. The label is drawn from the citation, and the space with it, so
    /// both are left out and the note starts at its first word, as an ODT note
    /// does. A reference in a note, or one naming no note, is reported and
    /// dropped.
    /// </remarks>
    private static DocumentNote? ReadNote(XElement reference, DocxReadContext context)
    {
        NoteKind kind = reference.Name.LocalName == "endnoteReference" ? NoteKind.Endnote : NoteKind.Footnote;
        if (context.Notes is null || context.InNote)
        {
            context.Builder.AddDiagnosticOnce(
                "docx.note.placement",
                "A DOCX note reference outside the body, or inside a note, was skipped.");
            return null;
        }

        XElement? note = DocxValueReader.TryReadInt(reference.Attribute(DocxNamespaces.Wordprocessing + "id"), out int id)
            ? context.Notes.Find(kind, id)
            : null;
        if (note is null)
        {
            context.Builder.AddDiagnosticOnce(
                "docx.note.missing",
                "A DOCX note reference named a note its part does not hold, and was skipped.");
            return null;
        }

        var builder = new DocxDocumentBuilder(
            context.Builder.Limits,
            context.Builder.Diagnostics,
            context.Builder.Reported);
        bool skipping = false;
        DocxNotes notes = context.Notes;
        var nested = new DocxReadContext(
            kind == NoteKind.Endnote ? context.Notes.EndnoteRelationships : context.Notes.FootnoteRelationships,
            context.Numbering,
            context.Styles,
            context.Images,
            builder,
            context.Page,
            context.Band)
        {
            InNote = true,
            SkipNoteLabel = run =>
            {
                skipping = true;
                notes.LabelsRaised ??= IsRaisedRun(run, context.Styles);
            },
        };
        ReadBlockContent(note.Elements(), nested, depth: 0);

        RichTextDocument built = builder.Build();
        if (built.Tables.Count > 0 || built.Shapes.Count > 0)
        {
            context.Builder.AddDiagnosticOnce(
                "docx.note.content",
                "A table or shape in a DOCX note was read as its text; a note holds paragraphs only.");
        }

        IReadOnlyList<RichTextParagraph> paragraphs = built.Paragraphs;
        if (skipping && paragraphs.Count > 0)
            paragraphs = [TrimLeadingSpace(paragraphs[0]), .. paragraphs.Skip(1)];

        return new DocumentNote(kind, paragraphs);
    }

    /// <summary>
    /// Whether a run is superscript, by its own <c>w:vertAlign</c> or its
    /// character style's - Word's FootnoteReference style raises the label at
    /// the head of a note that way.
    /// </summary>
    private static bool IsRaisedRun(XElement? run, DocxStyles styles)
    {
        XElement? rPr = run?.Element(DocxNamespaces.Wordprocessing + "rPr");
        string? own = DocxValueReader.WordValue(rPr?.Element(DocxNamespaces.Wordprocessing + "vertAlign"));
        if (own is not null)
            return own == "superscript";

        string? styleId = DocxValueReader.WordValue(rPr?.Element(DocxNamespaces.Wordprocessing + "rStyle"));
        bool raised = false;
        foreach (XElement properties in styles.RunPropertiesForCharacterStyle(styleId))
        {
            if (DocxValueReader.WordValue(properties.Element(DocxNamespaces.Wordprocessing + "vertAlign")) is string value)
                raised = value == "superscript";
        }

        return raised;
    }

    private static RichTextParagraph TrimLeadingSpace(RichTextParagraph paragraph)
    {
        int count = 0;
        while (count < paragraph.Length && paragraph.Text[count] is ' ' or '\t')
            count++;

        return count == 0 ? paragraph : paragraph.RemoveRange(0, count);
    }

    /// <summary>
    /// Appends a picture as one <see cref="InlineImage.Placeholder"/> character
    /// carrying the image on its style. The run's own character formatting is
    /// kept on it, so a picture inside a hyperlink stays inside that link. An
    /// anchored picture goes to <see cref="ReadFloatingPicture"/> instead.
    /// </summary>
    private static void ReadPicture(XElement picture, DocxReadContext context, InlineStyle style)
    {
        if (ReadDiagram(picture, context)) return;

        // A w:drawing is either a picture or a shape. A shape reports nothing when
        // it does not match, so a drawing that is neither still reaches the picture
        // path and raises its diagnostic.
        if (ReadShape(picture, context) || ReadVmlShape(picture, context))
            return;

        InlineImage? image = context.Images.Read(picture, context.Relationships, context.Builder);
        if (image is null)
            return;

        XElement? anchor = picture.Element(DocxNamespaces.WordDrawing + "anchor");
        if (anchor is not null && ReadFloatingPicture(anchor, image, context))
            return;

        context.Builder.AppendText(InlineImage.PlaceholderText, style with { Image = image });
    }

    private static bool ReadDiagram(XElement drawing, DocxReadContext context)
    {
        XElement? references = drawing.Descendants(DocxDiagram.Diagram + "relIds").FirstOrDefault();
        if (references is null) return false;
        XElement? anchor = drawing.Element(DocxNamespaces.WordDrawing + "anchor") ?? drawing.Element(DocxNamespaces.WordDrawing + "inline");
        XElement? extent = anchor?.Element(DocxNamespaces.WordDrawing + "extent");
        double width = EmuToPoints((string?)extent?.Attribute("cx")), height = EmuToPoints((string?)extent?.Attribute("cy"));
        if (anchor is null || width <= 0 || height <= 0 || width > 20000 || height > 20000)
        {
            context.Builder.AddDiagnosticOnce("docx.diagram.extent", "A saved diagram had an unsupported outer extent and was skipped.");
            return true;
        }
        DocxDiagram.Cache? cache = context.Images.ReadDiagram(references, anchor, context.Relationships, context.Builder, context.Styles.Theme);
        if (cache is null) return true;
        XElement placement = new(anchor);
        if (context.Builder.InTableCell)
            context.Builder.AddDiagnosticOnce("docx.diagram.inline", "A saved diagram in a table cell was approximated using its paragraph anchor; cell-relative placement is not fully retained.");
        if (anchor.Name == DocxNamespaces.WordDrawing + "inline")
        {
            XElement? paragraph = drawing.Ancestors(DocxNamespaces.Wordprocessing + "p").FirstOrDefault();
            bool block = paragraph is not null && paragraph.Descendants(DocxNamespaces.Wordprocessing + "drawing").Count() == 1 &&
                !paragraph.Descendants().Any(e => e.Name == DocxNamespaces.Wordprocessing + "t" && !string.IsNullOrWhiteSpace(e.Value) ||
                    e.Name.Namespace == DocxNamespaces.Wordprocessing && e.Name.LocalName is "br" or "tab" or "sym" or "pict" or "footnoteReference" or "endnoteReference" or "instrText" or "fldChar" or "delText");
            if (block)
            {
                double x = context.Builder.ReserveDiagramBlock(width, height, context.Page);
                placement.Add(new XElement(DocxNamespaces.WordDrawing + "positionH", new XAttribute("relativeFrom", "column"),
                    new XElement(DocxNamespaces.WordDrawing + "posOffset", Math.Round(x * 12700))));
            }
            else context.Builder.AddDiagnosticOnce("docx.diagram.inline", "A diagram mixed with inline text was approximated at its paragraph origin without reserving inline flow.");
        }
        XElement? cachedExtent = cache.Group.Element(DocxNamespaces.WordGroup + "grpSpPr")?.Element(DocxNamespaces.Drawing + "xfrm")?.Element(DocxNamespaces.Drawing + "ext");
        double cachedWidth = EmuToPoints((string?)cachedExtent?.Attribute("cx")), cachedHeight = EmuToPoints((string?)cachedExtent?.Attribute("cy"));
        if (cachedWidth > 20000 || cachedHeight > 20000)
        {
            context.Builder.AddDiagnosticOnce("docx.diagram.extent", "A saved diagram's cached root extent exceeded the supported display size and was skipped.");
            return true;
        }
        // A saved diagram's explicit root extent is already in display EMUs;
        // the Word inline extent reserves flow but does not rescale that cache.
        if (!ReadShapeGroup(cache.Group, placement, cachedWidth > 0 ? cachedWidth : width, cachedHeight > 0 ? cachedHeight : height,
            context with { Relationships = cache.Relationships }))
            context.Builder.AddDiagnosticOnce("docx.diagram.empty", "A saved diagram contained no supported painted shapes or text.");
        return true;
    }

    /// <summary>
    /// Reads an anchored picture as a floating shape rather than a character in
    /// the text: the logo a letterhead hangs over its stripe belongs beside the
    /// letter, not in its first line, where it pushed every paragraph down by its
    /// own height.
    /// </summary>
    /// <remarks>
    /// The box comes from <c>wp:extent</c>, which is also where the image's size
    /// was read from. A picture that states no box has nothing to float at, so it
    /// is left in the text and drawn at whatever size the renderer decodes - the
    /// answer this reader gave every anchored picture before floats existed.
    /// Returns false in that case, and the caller appends it inline.
    /// </remarks>
    private static bool ReadFloatingPicture(XElement anchor, InlineImage image, DocxReadContext context)
    {
        // Said whether it floats or not. Position, stacking and wrapping are all
        // kept now, so the note names what is still approximate rather than
        // claiming the arrangement was lost.
        context.Builder.AddDiagnosticOnce(
            "docx.image.anchored",
            "A floating DOCX picture was anchored to its paragraph; text wraps " +
            "around the box it states rather than around the picture's outline.");

        // A floating frame needs a real box, and the resolved size supplies one
        // whenever the picture states a dimension or its own pixels do.
        if (!image.TryGetDisplaySize(out double boxWidth, out double boxHeight))
            return false;

        (ShapeWrap Wrap, WrapSide Side, double Distance) = ReadWrap(anchor);
        (double offsetY, bool fromPage) = VerticalOffset(anchor, context);
        context.Builder.AddShape(new DocumentShape(
            context.Builder.CurrentParagraphIndex,
            HorizontalOffset(anchor, context),
            offsetY,
            boxWidth,
            boxHeight,
            fill: null,
            outline: default,
            paragraphs: null,
            image: image,
            behindText: BehindDoc(anchor),
            wrap: Wrap,
            wrapSide: Side,
            wrapDistance: Distance,
            zOrder: RelativeHeight(anchor),
            offsetFromPage: fromPage));
        return true;
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
        XElement? group = drawing.Descendants(DocxNamespaces.WordGroup + "wgp").FirstOrDefault();
        XElement? wsp = group is null ? drawing.Descendants(DocxNamespaces.WordShape + "wsp").FirstOrDefault() : null;
        if (group is null && wsp is null)
            return false;

        XElement? anchor =
            drawing.Element(DocxNamespaces.WordDrawing + "anchor") ??
            drawing.Element(DocxNamespaces.WordDrawing + "inline");
        if (anchor is null)
            return false;

        XElement? extent = anchor.Element(DocxNamespaces.WordDrawing + "extent");
        double width = EmuToPoints((string?)extent?.Attribute("cx"));
        double height = EmuToPoints((string?)extent?.Attribute("cy"));
        if (width < 0 || height < 0 || (width == 0 && height == 0))
            return false;

        if (group is not null)
        {
            // Picture-only groups retain the existing picture fallback. Their
            // native bounding-box normalization is not the mixed-shape mapping.
            if (!group.Descendants(DocxNamespaces.WordShape + "wsp").Any())
            {
                context.Builder.AddDiagnosticOnce("docx.shape.group",
                    "A picture-only DOCX group was approximated by its first image at the group's size.");
                return false;
            }
            return ReadShapeGroup(group, anchor, width, height, context);
        }

        return ReadDrawnShape(wsp!, anchor, 0, 0, width, height, context);
    }

    /// <summary>
    /// Reads a <c>wpg:wgp</c> - several drawn boxes under one anchor - as a
    /// floating shape for each box in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A group states its children in a coordinate space of its own: its
    /// <c>a:xfrm</c> says which rectangle of that space, <c>a:chOff</c> and
    /// <c>a:chExt</c>, is drawn into the box it occupies, <c>a:off</c> and
    /// <c>a:ext</c> (ECMA-376 §20.1.7.6, §20.5.2.17). The outermost group's box is
    /// the anchor's extent; a group inside it states its box in its parent's
    /// child space, and so on down. Each child's own <c>a:xfrm</c> is mapped
    /// through every group above it into points from the anchor's corner, and the
    /// child becomes a shape at the anchor's offset plus that.
    /// </para>
    /// <para>
    /// The group was read as its first box before, at the size of the whole
    /// group, which made a backdrop of a hatched rectangle and a fade over it
    /// into the hatch alone, and lost a title bar's text box under it. The
    /// children keep the group's anchor, stacking and wrapping, and draw in the
    /// order the group lists them. Embedded pictures retain their crop and mask.
    /// Group rotation and flips are not applied.
    /// </para>
    /// </remarks>
    private static bool ReadShapeGroup(XElement group, XElement anchor, double width, double height, DocxReadContext context)
    {
        XElement? transform = group.Element(DocxNamespaces.WordGroup + "grpSpPr")?.Element(DocxNamespaces.Drawing + "xfrm");
        (double childX, double childY, double childWidth, double childHeight) = ChildSpace(transform);
        var frame = new GroupFrame(
            0,
            0,
            childWidth > 0 ? width / childWidth : 1 / 12700d,
            childHeight > 0 ? height / childHeight : 1 / 12700d,
            childX,
            childY);

        int read = 0;
        ReadGroupChildren(group, frame, anchor, context, ref read, depth: 0);
        return read > 0;
    }

    private static void ReadGroupChildren(
        XElement group, GroupFrame frame, XElement anchor, DocxReadContext context, ref int read, int depth)
    {
        foreach (XElement child in group.Elements())
        {
            if (child.Name == DocxNamespaces.WordShape + "wsp")
            {
                XElement? transform = child.Element(DocxNamespaces.WordShape + "spPr")?.Element(DocxNamespaces.Drawing + "xfrm");
                (double x, double y, double w, double h) = frame.Map(transform);
                if (ReadDrawnShape(child, anchor, x, y, w, h, context))
                    read++;
            }
            else if (child.Name == DocxNamespaces.Picture + "pic")
            {
                XElement? transform = child.Element(DocxNamespaces.Picture + "spPr")?.Element(DocxNamespaces.Drawing + "xfrm");
                (double x, double y, double w, double h) = frame.Map(transform);
                // This is handled even when resource admission fails: falling through
                // to the ordinary picture path would incorrectly use the whole group's box.
                read++;
                if ((string?)transform?.Attribute("rot") is string rotation && rotation != "0" ||
                    (string?)transform?.Attribute("flipH") is "1" or "true" ||
                    (string?)transform?.Attribute("flipV") is "1" or "true")
                {
                    context.Builder.AddDiagnosticOnce("docx.shape.group", "A rotated or flipped grouped DOCX picture was skipped.");
                    continue;
                }
                InlineImage? image = context.Images.ReadGroupedPicture(child, w, h, context.Relationships, context.Builder);
                if (image is null)
                    continue;
                (ShapeWrap Wrap, WrapSide Side, double Distance) = ReadWrap(anchor);
                (double offsetY, bool fromPage) = VerticalOffset(anchor, context);
                context.Builder.AddShape(new DocumentShape(context.Builder.CurrentParagraphIndex,
                    HorizontalOffset(anchor, context) + x, offsetY + y, w, h,
                    fill: null, outline: default, paragraphs: null, image: image,
                    behindText: BehindDoc(anchor), wrap: Wrap, wrapSide: Side, wrapDistance: Distance,
                    zOrder: RelativeHeight(anchor), offsetFromPage: fromPage));
            }
            else if (child.Name == DocxNamespaces.WordGroup + "grpSp" && depth < MaxGroupDepth)
            {
                XElement? transform = child.Element(DocxNamespaces.WordGroup + "grpSpPr")?.Element(DocxNamespaces.Drawing + "xfrm");
                (double x, double y, double w, double h) = frame.Map(transform);
                (double childX, double childY, double childWidth, double childHeight) = ChildSpace(transform);
                var nested = new GroupFrame(
                    x,
                    y,
                    childWidth > 0 ? w / childWidth : frame.ScaleX,
                    childHeight > 0 ? h / childHeight : frame.ScaleY,
                    childX,
                    childY);
                ReadGroupChildren(child, nested, anchor, context, ref read, depth + 1);
            }
            else if (child.Name == DocxNamespaces.WordGroup + "grpSp")
            {
                context.Builder.AddDiagnosticOnce(
                    "docx.shape.group",
                    "A group nested too deep inside a DOCX drawing group was not read.");
            }
        }
    }

    /// <summary>
    /// The rectangle of a group's own coordinate space its box draws, in EMU:
    /// <c>a:chOff</c> and <c>a:chExt</c>, or its <c>a:off</c> and <c>a:ext</c>
    /// where it states no child space, which is a space one to one with its box.
    /// </summary>
    private static (double X, double Y, double Width, double Height) ChildSpace(XElement? transform)
    {
        XElement? offset = transform?.Element(DocxNamespaces.Drawing + "chOff") ?? transform?.Element(DocxNamespaces.Drawing + "off");
        XElement? extent = transform?.Element(DocxNamespaces.Drawing + "chExt") ?? transform?.Element(DocxNamespaces.Drawing + "ext");
        return (
            Emu((string?)offset?.Attribute("x")),
            Emu((string?)offset?.Attribute("y")),
            Emu((string?)extent?.Attribute("cx")),
            Emu((string?)extent?.Attribute("cy")));
    }

    private static double Emu(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long emu) ? emu : 0;

    /// <summary>How deep groups nest before the reader stops following them.</summary>
    private const int MaxGroupDepth = 16;

    /// <summary>
    /// One group's coordinate space against the anchor's box: a point of the
    /// group's child space, in EMU, is this many points from the anchor's corner.
    /// </summary>
    private readonly record struct GroupFrame(
        double OffsetX, double OffsetY, double ScaleX, double ScaleY, double ChildX, double ChildY)
    {
        /// <summary>A child's <c>a:xfrm</c> as a box in points from the anchor's corner.</summary>
        public (double X, double Y, double Width, double Height) Map(XElement? transform)
        {
            XElement? offset = transform?.Element(DocxNamespaces.Drawing + "off");
            XElement? extent = transform?.Element(DocxNamespaces.Drawing + "ext");
            return (
                OffsetX + ((Emu((string?)offset?.Attribute("x")) - ChildX) * ScaleX),
                OffsetY + ((Emu((string?)offset?.Attribute("y")) - ChildY) * ScaleY),
                Emu((string?)extent?.Attribute("cx")) * ScaleX,
                Emu((string?)extent?.Attribute("cy")) * ScaleY);
        }
    }

    /// <summary>
    /// Adds one <c>wps:wsp</c> as a floating shape, its box
    /// <paramref name="left"/> and <paramref name="top"/> points from the corner
    /// of its anchor's: nought for a shape that is the whole drawing, more for a
    /// box inside a group.
    /// </summary>
    private static bool ReadDrawnShape(
        XElement wsp, XElement anchor, double left, double top, double width, double height, DocxReadContext context)
    {
        if (width < 0 || height < 0 || (width == 0 && height == 0))
            return false;

        XElement spPr = DocxShapeStyle.Resolve(wsp, context.Styles.Theme);
        ShapeFill? fill = ReadFill(spPr, context);
        BColor outline = ReadOutline(spPr, context.Styles.Theme);
        string? preset = (string?)spPr?.Element(DocxNamespaces.Drawing + "prstGeom")?.Attribute("prst");
        XElement? transform = spPr?.Element(DocxNamespaces.Drawing + "xfrm");
        bool flipped = ((string?)transform?.Attribute("flipH") is "1" or "true") != ((string?)transform?.Attribute("flipV") is "1" or "true");
        ShapeGeometry geometry = preset == "ellipse" ? ShapeGeometry.Ellipse : preset is "line" or "straightConnector1"
            ? (flipped ? ShapeGeometry.LineAscending : ShapeGeometry.Line) : ShapeGeometry.Rectangle;
        RichTextDocument shapeContent = ReadShapeText(wsp, context);
        IReadOnlyList<RichTextParagraph> paragraphs = ShapeParagraphs(shapeContent);
        if (fill is null && paragraphs.Count == 0 && outline.IsEmpty)
            return false;

        (ShapeWrap Wrap, WrapSide Side, double Distance) = ReadWrap(anchor);
        (double offsetY, bool fromPage) = VerticalOffset(anchor, context);
        context.Builder.AddShape(new DocumentShape(
            context.Builder.CurrentParagraphIndex,
            HorizontalOffset(anchor, context) + left,
            offsetY + top,
            width,
            height,
            fill,
            outline,
            paragraphs,
            image: null,
            behindText: BehindDoc(anchor),
            wrap: Wrap,
            wrapSide: Side,
            wrapDistance: Distance,
            zOrder: RelativeHeight(anchor),
            offsetFromPage: fromPage,
            growsToText: wsp.Element(DocxNamespaces.WordShape + "bodyPr")?.Element(DocxNamespaces.Drawing + "spAutoFit") is not null,
            tables: shapeContent.Tables,
            textPadding: ReadShapePadding(wsp.Element(DocxNamespaces.WordShape + "bodyPr")),
            textVerticalAlignment: (string?)wsp.Element(DocxNamespaces.WordShape + "bodyPr")?.Attribute("anchor") switch
            {
                "ctr" => CellVerticalAlignment.Center,
                "b" => CellVerticalAlignment.Bottom,
                _ => CellVerticalAlignment.Top,
            },
            textOverflow: (string?)wsp.Element(DocxNamespaces.WordShape + "bodyPr")?.Attribute("vertOverflow") == "overflow",
            geometry: geometry,
            outlineWidth: long.TryParse((string?)spPr?.Element(DocxNamespaces.Drawing + "ln")?.Attribute("w"), out long lineWidth) && lineWidth >= 0 ? lineWidth / 12700d : 0.75,
            offsetXFromCell: context.Builder.InTableCell && geometry is ShapeGeometry.Line or ShapeGeometry.LineAscending &&
                RelativeFrom(anchor, "positionH") == "column"));
        return true;
    }

    private static TableCellPadding ReadShapePadding(XElement? body)
    {
        double Length(string attribute, double fallback) =>
            long.TryParse((string?)body?.Attribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
                ? Math.Clamp(value / 12700d, 0, 20000) : fallback;
        return new TableCellPadding(Length("lIns", 7.2), Length("tIns", 3.6), Length("rIns", 7.2), Length("bIns", 3.6));
    }

    /// <summary>
    /// Reads a VML shape in a <c>w:pict</c> - a rectangle, a rounded one, an
    /// oval or a freeform - as a floating shape with its fill, outline and text
    /// box. Returns false for anything else, a picture included, which the
    /// picture path reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is how Word wrote a text box before 2010, and how other producers
    /// still write one; read as nothing, the text inside was lost with the box.
    /// The box is the shape's CSS <c>style</c>: its width and height, and its
    /// <c>margin-left</c> and <c>margin-top</c> from the frames
    /// <c>mso-position-horizontal-relative</c> and
    /// <c>mso-position-vertical-relative</c> name. They default to the column
    /// and the paragraph, the frames the model measures from; a vertical frame
    /// on the page or a margin is measured from the page's top, as a DrawingML
    /// anchor's is.
    /// </para>
    /// <para>
    /// A negative <c>z-index</c> stacks the shape behind the text and any other
    /// in front of it, which is VML's spelling of <c>behindDoc</c>. The outline
    /// is drawn unless <c>stroked</c> or <c>v:stroke</c> turns it off, in
    /// <c>strokecolor</c> or VML's default black.
    /// </para>
    /// </remarks>
    private static bool ReadVmlShape(XElement picture, DocxReadContext context)
    {
        if (picture.Name != DocxNamespaces.Wordprocessing + "pict")
            return false;

        bool read = false;
        bool hasImages = false;
        foreach (XElement shape in DocxVmlShapeWalker.Shapes(picture, context.Builder.Limits.MaxGroupDepth,
            (code, message) => context.Builder.AddDiagnosticOnce(code, message)))
        {
            if (shape.Descendants(DocxNamespaces.Vml + "imagedata").Any())
            {
                hasImages = true;
                continue;
            }
            read |= ReadVmlShapeElement(shape, context);
        }
        // Preserve the established first-image fallback for mixed blocks.
        return read && !hasImages;
    }

    private static bool ReadVmlShapeElement(XElement shape, DocxReadContext context)
    {
        Dictionary<string, string> style = DocxVml.ReadStyle((string?)shape.Attribute("style"));
        bool segment = shape.Name == DocxNamespaces.Vml + "line" || shape.Name == DocxNamespaces.Vml + "polyline";
        double lineLeft = 0, lineTop = 0, width, height;
        ShapeGeometry geometry = shape.Name == DocxNamespaces.Vml + "oval" ? ShapeGeometry.Ellipse : ShapeGeometry.Rectangle;
        if (segment)
        {
            double[]? points = ReadVmlSegmentPoints(shape);
            if (points is null || style.ContainsKey("rotation"))
            {
                context.Builder.AddDiagnosticOnce("docx.shape.geometry",
                    "A VML line used unsupported coordinates, rotation or more than two points and was skipped.");
                return true;
            }
            lineLeft = Math.Min(points[0], points[2]);
            lineTop = Math.Min(points[1], points[3]);
            width = Math.Abs(points[2] - points[0]);
            height = Math.Abs(points[3] - points[1]);
            bool flip = style.GetValueOrDefault("flip")?.Contains('x') == true ^ style.GetValueOrDefault("flip")?.Contains('y') == true;
            geometry = ((points[2] - points[0]) * (points[3] - points[1]) < 0) ^ flip
                ? ShapeGeometry.LineAscending : ShapeGeometry.Line;
        }
        else if (!DocxVml.TryLength(style.GetValueOrDefault("width"), out width) ||
            !DocxVml.TryLength(style.GetValueOrDefault("height"), out height) ||
            width <= 0 || height <= 0)
        {
            return false;
        }

        ShapeFill? fill = segment ? null : DocxVml.ReadFill(
            shape,
            BColor.Empty,
            (code, message) => context.Builder.AddDiagnosticOnce(code, message),
            "docx.shape.fill",
            "shape");
        RichTextDocument shapeContent = ReadShapeText(shape, context);
        IReadOnlyList<RichTextParagraph> paragraphs = ShapeParagraphs(shapeContent);
        BColor outline = ReadVmlOutline(shape);
        if (segment && outline.IsEmpty)
            return true;
        string? dash = (string?)shape.Element(DocxNamespaces.Vml + "stroke")?.Attribute("dashstyle");
        if (segment && dash is not null && !string.Equals(dash.Trim(), "solid", StringComparison.OrdinalIgnoreCase))
            context.Builder.AddDiagnosticOnce("docx.shape.stroke",
                "A VML line dash pattern was approximated by a solid stroke.");
        if (fill is null && paragraphs.Count == 0 && outline.IsEmpty)
            return false;

        DocxVml.TryLength(style.GetValueOrDefault("left"), out double left);
        DocxVml.TryLength(style.GetValueOrDefault("margin-left"), out double marginLeft);
        DocxVml.TryLength(style.GetValueOrDefault("top"), out double top);
        DocxVml.TryLength(style.GetValueOrDefault("margin-top"), out double marginTop);
        // Across, every frame converts to the column, as a DrawingML anchor's does.
        double offsetX = segment ? lineLeft + (shape.Name.LocalName == "polyline" ? left + marginLeft : 0) : left + marginLeft;
        if (context.Page is PageGeometry page)
        {
            offsetX += style.GetValueOrDefault("mso-position-horizontal-relative") switch
            {
                "page" or "left-margin-area" => -page.MarginLeft,
                "right-margin-area" => page.Width - page.MarginRight - page.MarginLeft,
                _ => 0,
            };
        }

        // VML names DrawingML's vertical frames in its own words; the paragraph
        // and the line stay where they are, the rest measure from the page.
        double offsetY = segment ? lineTop + (shape.Name.LocalName == "polyline" ? top + marginTop : 0) : top + marginTop;
        string? verticalFrom = style.GetValueOrDefault("mso-position-vertical-relative") switch
        {
            "page" => "page",
            "margin" => "margin",
            "top-margin-area" => "topMargin",
            "bottom-margin-area" => "bottomMargin",
            "inner-margin-area" => "insideMargin",
            "outer-margin-area" => "outsideMargin",
            _ => null,
        };
        bool fromPage = false;
        if (verticalFrom is not null)
        {
            if (FromPageTop(verticalFrom, offsetY, context.Page) is double fromTop)
            {
                (offsetY, fromPage) = (fromTop, true);
            }
            else
            {
                context.Builder.AddDiagnosticOnce(
                    "docx.anchor.relativefrom:v",
                    "docx.anchor.relativefrom",
                    "A floating DOCX object stated its vertical position against a margin this reader could not place " +
                    "on the page; the offset was kept as stated and is measured from its paragraph instead.");
            }
        }

        if (context.Page is PageGeometry alignmentPage)
        {
            string? horizontal = style.GetValueOrDefault("mso-position-horizontal");
            string? horizontalFrame = style.GetValueOrDefault("mso-position-horizontal-relative");
            double frameWidth = horizontalFrame == "page" ? alignmentPage.Width : alignmentPage.Width - alignmentPage.MarginLeft - alignmentPage.MarginRight;
            double frameStart = horizontalFrame == "page" ? -alignmentPage.MarginLeft : 0;
            if (horizontal is "left" or "center" or "right")
                offsetX = frameStart + (horizontal == "center" ? (frameWidth - width) / 2 : horizontal == "right" ? frameWidth - width : 0);
            string? vertical = style.GetValueOrDefault("mso-position-vertical");
            string? verticalFrame = style.GetValueOrDefault("mso-position-vertical-relative");
            if (vertical is "top" or "center" or "bottom" && verticalFrame is "page" or "margin")
            {
                double frameHeight = verticalFrame == "page" ? alignmentPage.Height : alignmentPage.Height - alignmentPage.MarginTop - alignmentPage.MarginBottom;
                offsetY = (verticalFrame == "page" ? 0 : alignmentPage.MarginTop) +
                    (vertical == "center" ? (frameHeight - height) / 2 : vertical == "bottom" ? frameHeight - height : 0);
                fromPage = true;
            }
        }

        int zIndex = int.TryParse(style.GetValueOrDefault("z-index"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int z)
            ? z
            : 0;
        XElement? wrapping = shape.Element(DocxNamespaces.VmlWord + "wrap");
        ShapeWrap wrap = (string?)wrapping?.Attribute("type") switch
        {
            "square" or "tight" or "through" => ShapeWrap.Square,
            "topAndBottom" => ShapeWrap.TopAndBottom,
            _ => ShapeWrap.None,
        };

        context.Builder.AddShape(new DocumentShape(
            context.Builder.CurrentParagraphIndex,
            offsetX,
            offsetY,
            width,
            height,
            fill,
            outline,
            paragraphs,
            image: null,
            behindText: zIndex < 0,
            wrap: wrap,
            wrapSide: (string?)wrapping?.Attribute("side") switch { "left" => WrapSide.Left, "right" => WrapSide.Right, _ => WrapSide.Largest },
            wrapDistance: new[] { "mso-wrap-distance-left", "mso-wrap-distance-right", "mso-wrap-distance-top", "mso-wrap-distance-bottom" }
                .Select(key => DocxVml.TryLength(style.GetValueOrDefault(key), out double distance) ? Math.Clamp(distance, 0, 20000) : 0).Max(),
            // Keep VML's signed order within the behind-text layer. Absolute
            // values reverse negative levels and paint backgrounds over shapes.
            zOrder: zIndex < 0 ? zIndex - int.MinValue : zIndex,
            offsetFromPage: fromPage,
            growsToText: DocxVml.ReadStyle((string?)shape.Element(DocxNamespaces.Vml + "textbox")?.Attribute("style"))
                .GetValueOrDefault("mso-fit-shape-to-text")?.Trim().ToLowerInvariant() is "t" or "true" or "1",
            tables: shapeContent.Tables,
            textPadding: ReadVmlPadding(shape.Element(DocxNamespaces.Vml + "textbox")),
            textVerticalAlignment: style.GetValueOrDefault("v-text-anchor") switch
            {
                "middle" or "middle-center" => CellVerticalAlignment.Center,
                "bottom" or "bottom-center" => CellVerticalAlignment.Bottom,
                _ => CellVerticalAlignment.Top,
            },
            textOverflow: style.GetValueOrDefault("overflow") == "visible",
            geometry: geometry,
            outlineWidth: DocxVml.TryLength((string?)shape.Attribute("strokeweight") ?? (string?)shape.Element(DocxNamespaces.Vml + "stroke")?.Attribute("weight"), out double strokeWidth) ? strokeWidth : 0.75,
            offsetXFromCell: segment && context.Builder.InTableCell && style.GetValueOrDefault("mso-position-horizontal-relative") == "text"));
        return true;
    }

    private static double[]? ReadVmlSegmentPoints(XElement shape)
    {
        if (shape.Name == DocxNamespaces.Vml + "line" &&
            (((string?)shape.Attribute("from"))?.Count(c => c == ',') != 1 ||
             ((string?)shape.Attribute("to"))?.Count(c => c == ',') != 1))
            return null;
        string? value = shape.Name == DocxNamespaces.Vml + "line"
            ? (string?)shape.Attribute("from") + "," + (string?)shape.Attribute("to")
            : (string?)shape.Attribute("points");
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
            return null;
        string[] coordinates = value.Split(',', StringSplitOptions.TrimEntries);
        if (coordinates.Length != 4)
            return null;
        var points = new double[4];
        for (int i = 0; i < 4; i++)
        {
            if (!DocxVml.TryLength(coordinates[i], out points[i]) || Math.Abs(points[i]) > 10000 ||
                (shape.Attribute("coordsize") is not null && points[i] != 0 && !char.IsAsciiLetter(coordinates[i][^1])))
                return null;
            if (shape.Name == DocxNamespaces.Vml + "line" && double.TryParse(coordinates[i], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                points[i] *= 72d / 96;
        }
        return points[0] == points[2] && points[1] == points[3] ? null : points;
    }

    private static TableCellPadding ReadVmlPadding(XElement? textBox)
    {
        string[] values = ((string?)textBox?.Attribute("inset") ?? "").Split(',');
        double Length(int index, double fallback) => index < values.Length && DocxVml.TryLength(values[index].Trim(), out double value)
            ? Math.Clamp(value, 0, 20000) : fallback;
        return new TableCellPadding(Length(0, 7.2), Length(1, 3.6), Length(2, 7.2), Length(3, 3.6));
    }

    /// <summary>A VML shape's outline colour, or empty for none.</summary>
    private static BColor ReadVmlOutline(XElement shape)
    {
        XElement? stroke = shape.Element(DocxNamespaces.Vml + "stroke");
        string? stroked = (string?)shape.Attribute("stroked") ?? (string?)stroke?.Attribute("on");
        if (stroked?.Trim() is "f" or "false" or "0")
            return BColor.Empty;

        BColor color = DocxVml.TryColor((string?)shape.Attribute("strokecolor") ?? (string?)stroke?.Attribute("color"), out BColor stated)
            ? stated : BColor.Black;
        return DocxVml.ApplyOpacity(color, (string?)stroke?.Attribute("opacity"));
    }

    /// <summary>
    /// The <c>wp:anchor</c> attribute saying whether the anchored object is
    /// displayed behind the document text. It is all of the stacking the model
    /// keeps, and the difference between a letterhead's stripe and a stamp over
    /// the letter.
    /// </summary>
    /// <remarks>
    /// Absent - which is what a <c>wp:inline</c> reaching here has, the attribute
    /// being required only on <c>wp:anchor</c> - reads as behind, so a producer
    /// that omits it gets the letterhead answer rather than a box over the text.
    /// </remarks>
    private static bool BehindDoc(XElement anchor) =>
        (string?)anchor.Attribute("behindDoc") switch
        {
            "0" or "false" => false,
            _ => true,
        };

    /// <summary>
    /// The <c>wp:anchor</c> attribute ordering one anchored object against
    /// another: higher draws later, and therefore on top.
    /// </summary>
    /// <remarks>
    /// One number for the whole document rather than one per part, which is what
    /// makes a letterhead legible: the stripe is anchored in the header and the
    /// logo box in the body, and their <c>relativeHeight</c> values are the only
    /// thing saying which of the two the reader sees. It is required on a
    /// <c>wp:anchor</c>, so one that states none is a producer leaving it out and
    /// reads as zero - the order the shapes were read in, which is the answer
    /// every anchor got before this was read at all. A <c>wp:inline</c> reaching
    /// here has no such attribute and gets the same answer for the same reason.
    /// </remarks>
    private static int RelativeHeight(XElement anchor) =>
        uint.TryParse(
            (string?)anchor.Attribute("relativeHeight"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out uint height)
            ? (int)Math.Min(height, int.MaxValue)
            : 0;

    private static string? PositionOffset(XElement anchor, string axis) =>
        (string?)anchor.Element(DocxNamespaces.WordDrawing + axis)
            ?.Element(DocxNamespaces.WordDrawing + "posOffset");

    /// <summary>
    /// The wrap an anchor states, as the three modes layout distinguishes.
    /// </summary>
    /// <remarks>
    /// <c>wrapTight</c> and <c>wrapThrough</c> follow the picture's own outline;
    /// they are read as square, which follows its box instead. The element is
    /// required on a <c>wp:anchor</c>, so one that states none is a producer
    /// leaving it out and reads as no wrap, which is what every anchor got before
    /// any of them were read.
    /// </remarks>
    private static (ShapeWrap Wrap, WrapSide Side, double Distance) ReadWrap(XElement anchor)
    {
        foreach (XElement child in anchor.Elements())
        {
            if (child.Name.Namespace != DocxNamespaces.WordDrawing)
                continue;

            ShapeWrap wrap = child.Name.LocalName switch
            {
                "wrapSquare" or "wrapTight" or "wrapThrough" => ShapeWrap.Square,
                "wrapTopAndBottom" => ShapeWrap.TopAndBottom,
                _ => ShapeWrap.None,
            };

            if (wrap == ShapeWrap.None)
                continue;

            WrapSide side = (string?)child.Attribute("wrapText") switch
            {
                "left" => WrapSide.Left,
                "right" => WrapSide.Right,
                // "bothSides" asks for text down both sides of the shape. A line
                // here is one span, so it gets the side with more room instead.
                _ => WrapSide.Largest,
            };

            return (wrap, side, WrapDistance(anchor));
        }

        return (ShapeWrap.None, WrapSide.Largest, 0);
    }

    /// <summary>
    /// The clearance the text keeps beside a wrapping shape, as the wider of the
    /// two horizontal distances the anchor states.
    /// </summary>
    private static double WrapDistance(XElement anchor) => Math.Max(
        EmuToPoints((string?)anchor.Attribute("distL")),
        EmuToPoints((string?)anchor.Attribute("distR")));

    /// <summary>The frame an axis states its offset against, or null for none.</summary>
    private static string? RelativeFrom(XElement anchor, string axis) =>
        (string?)anchor.Element(DocxNamespaces.WordDrawing + axis)?.Attribute("relativeFrom");

    /// <summary>
    /// The horizontal offset in points from the text column's left edge, which is
    /// what <see cref="DocumentShape.OffsetX"/> measures, whichever frame the
    /// anchor stated it against.
    /// </summary>
    /// <remarks>
    /// Every horizontal frame is a fixed distance from the column, so the page
    /// geometry converts them exactly: a stripe stated 0 from the left margin and
    /// one stated -MarginLeft from the column are the same stripe, and only one of
    /// them used to arrive in the right place. A document that states no usable
    /// page has nothing to convert with, so the offset is taken as column-relative
    /// - the reading every anchor used to get.
    /// </remarks>
    private static double HorizontalOffset(XElement anchor, DocxReadContext context)
    {
        double offset = EmuToPoints(PositionOffset(anchor, "positionH"));
        string? from = RelativeFrom(anchor, "positionH");
        if (context.Page is not PageGeometry page)
            return offset;

        switch (from)
        {
            // The page's left edge and the left margin's both sit MarginLeft to
            // the left of the column.
            case "page":
            case "leftMargin":
                return offset - page.MarginLeft;

            // The right margin starts where the column ends.
            case "rightMargin":
                return offset + page.Width - page.MarginRight - page.MarginLeft;

            // Which side these name depends on whether the page is odd or even,
            // and a paragraph-anchored model does not know what page it lands on.
            // Read as an odd page, which is the one a first page always is.
            case "insideMargin":
                context.Builder.AddDiagnosticOnce(
                    "docx.anchor.relativefrom:h",
                    "docx.anchor.relativefrom",
                    "A floating DOCX object was positioned against the inside or outside margin, " +
                    "which side depends on the page it falls on; it was placed as though on an odd page.");
                return offset - page.MarginLeft;

            case "outsideMargin":
                context.Builder.AddDiagnosticOnce(
                    "docx.anchor.relativefrom:h",
                    "docx.anchor.relativefrom",
                    "A floating DOCX object was positioned against the inside or outside margin, " +
                    "which side depends on the page it falls on; it was placed as though on an odd page.");
                return offset + page.Width - page.MarginRight - page.MarginLeft;

            // "margin" is the text area, whose left edge is the column's. "column"
            // is the column. "character" is where the run sits, which is as good
            // as the column here and the answer this reader always gave.
            default:
                return offset;
        }
    }

    /// <summary>
    /// The vertical offset in points, and whether it is measured from the top of
    /// the page the anchoring paragraph lands on rather than from the paragraph:
    /// the two things <see cref="DocumentShape.OffsetY"/> and
    /// <see cref="DocumentShape.OffsetFromPage"/> hold.
    /// </summary>
    /// <remarks>
    /// A frame on the paragraph or its line is kept as stated. One on the page or
    /// a margin is turned into an offset from the page's top edge, through the
    /// geometry for the margins. Where the paragraph sits on that page is a layout
    /// result this reader does not have, which is why the model holds the frame
    /// rather than an offset converted by a guess. Only the mirrored margins,
    /// whose side depends on the page's parity, and a margin with no page to
    /// measure it on are kept as stated and reported.
    /// </remarks>
    private static (double Offset, bool FromPage) VerticalOffset(XElement anchor, DocxReadContext context)
    {
        double offset = EmuToPoints(PositionOffset(anchor, "positionV"));
        string? from = RelativeFrom(anchor, "positionV");

        // A header's shapes are placed against the page, which is the one place
        // every vertical frame does convert: the page's top is a number the
        // geometry states rather than a layout result. The band the running
        // content occupies is where its own paragraphs are drawn, so an offset
        // stated against those lands with them.
        if (context.Band != RunningBand.Body)
        {
            if (context.Page is not PageGeometry running)
                return (offset, false);

            return (from switch
            {
                "margin" => offset + running.MarginTop,
                "bottomMargin" => offset + running.Height - running.MarginBottom,
                _ when context.Band == RunningBand.Footer && from is null or "paragraph" or "line" =>
                    offset + running.Height - running.MarginBottom,
                // "page" and "topMargin" both measure from the page's top edge,
                // and a header's own band starts there too.
                _ => offset,
            }, false);
        }

        if (from is null or "paragraph" or "line")
            return (offset, false);

        if (FromPageTop(from, offset, context.Page) is double fromPage)
            return (fromPage, true);

        context.Builder.AddDiagnosticOnce(
            "docx.anchor.relativefrom:v",
            "docx.anchor.relativefrom",
            "A floating DOCX object stated its vertical position against a margin this reader could not place " +
            "on the page; the offset was kept as stated and is measured from its paragraph instead.");
        return (offset, false);
    }

    /// <summary>
    /// An offset stated against the page or one of its margins, as points from
    /// the page's top edge, or null for a frame that cannot be placed so: a
    /// mirrored margin, whose side depends on the page's parity, or a margin of a
    /// page the document does not state.
    /// </summary>
    private static double? FromPageTop(string from, double offset, PageGeometry? page) => from switch
    {
        "page" or "topMargin" => offset,
        "margin" when page is not null => offset + page.MarginTop,
        "bottomMargin" when page is not null => offset + page.Height - page.MarginBottom,
        _ => null,
    };

    /// <summary>English Metric Units to points: 12700 EMU to the point.</summary>
    private static double EmuToPoints(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long emu)
            ? emu / 12700d
            : 0;

    private static ShapeFill? ReadFill(XElement? spPr, DocxReadContext context)
    {
        if (spPr is null)
            return null;

        DocxDocumentBuilder builder = context.Builder;
        DocxTheme theme = context.Styles.Theme;

        XElement? gradient = spPr.Element(DocxNamespaces.Drawing + "gradFill");
        if (gradient is not null)
        {
            List<XElement> stops = [.. gradient
                .Elements(DocxNamespaces.Drawing + "gsLst")
                .Elements(DocxNamespaces.Drawing + "gs")];
            double angle = long.TryParse(
                (string?)gradient.Element(DocxNamespaces.Drawing + "lin")?.Attribute("ang"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long ang)
                ? ang / 60000d
                : 0;

            // Three stops whose first and last agree are how DrawingML spells a
            // mirrored gradient; Word writes VML's 50% focus that way.
            if (stops.Count == 3 &&
                TryReadShapeColor(stops[0], theme, out BColor outer) &&
                TryReadShapeColor(stops[1], theme, out BColor inner) &&
                TryReadShapeColor(stops[2], theme, out BColor last) &&
                outer == last)
            {
                return new ShapeFill(outer, inner, angle, Axial: true);
            }

            if (stops.Count > 2)
            {
                builder.AddDiagnosticOnce(
                    "docx.shape.gradient",
                    "A DOCX shape gradient had more than two stops; the first and last were kept.");
            }

            if (stops.Count >= 2 &&
                TryReadShapeColor(stops[0], theme, out BColor start) &&
                TryReadShapeColor(stops[^1], theme, out BColor end))
            {
                return new ShapeFill(start, end, angle);
            }
        }

        // A pattern is two colours the model cannot hold together: hatch lines in
        // the foreground colour over the background one, as LibreOffice draws it.
        // The lines are a pixel or two wide, so the background is nearly all of
        // what shows - LibreOffice draws a wdUpDiag of 5EAD35 over 007929 as
        // 007929 over nine-tenths of the box - and it is the colour held.
        XElement? pattern = spPr.Element(DocxNamespaces.Drawing + "pattFill");
        if (pattern is not null)
        {
            builder.AddDiagnosticOnce(
                "docx.shape.fill",
                "A DOCX shape's pattern fill was read as its background colour; the pattern's lines are not drawn.");
            return TryReadShapeColor(pattern.Element(DocxNamespaces.Drawing + "bgClr"), theme, out BColor background) ||
                TryReadShapeColor(pattern.Element(DocxNamespaces.Drawing + "fgClr"), theme, out background)
                ? ShapeFill.Solid(background)
                : null;
        }

        XElement? solid = spPr.Element(DocxNamespaces.Drawing + "solidFill");
        return solid is not null && TryReadShapeColor(solid, theme, out BColor color)
            ? ShapeFill.Solid(color)
            : null;
    }

    private static BColor ReadOutline(XElement? spPr, DocxTheme theme)
    {
        XElement? line = spPr?.Element(DocxNamespaces.Drawing + "ln");
        if (line is null || line.Element(DocxNamespaces.Drawing + "noFill") is not null)
            return BColor.Empty;

        XElement? solid = line.Element(DocxNamespaces.Drawing + "solidFill");
        return solid is not null && TryReadShapeColor(solid, theme, out BColor color) ? color : BColor.Empty;
    }

    /// <summary>A DrawingML colour, a theme's included; see <see cref="DocxDrawingColor"/>.</summary>
    private static bool TryReadShapeColor(XElement? parent, DocxTheme theme, out BColor color) =>
        DocxDrawingColor.TryRead(parent, theme, out color);

    /// <summary>A text box paragraphs, read with the same walker the body uses.</summary>
    private static RichTextDocument ReadShapeText(XElement wsp, DocxReadContext context)
    {
        XElement? content = wsp
            .Descendants(DocxNamespaces.Wordprocessing + "txbxContent")
            .FirstOrDefault();
        if (content is null)
            return RichTextDocument.Empty;

        var builder = new DocxDocumentBuilder(
            context.Builder.Limits,
            context.Builder.Diagnostics,
            context.Builder.Reported);
        var nested = new DocxReadContext(
            context.Relationships, context.Numbering, context.Styles, context.Images, builder,
            context.Page, context.Band);
        ReadBlockContent(content.Elements(), nested, depth: 0);

        return builder.Build();
    }

    private static IReadOnlyList<RichTextParagraph> ShapeParagraphs(RichTextDocument content) =>
        content.ParagraphCount == 1 && content.Paragraphs[0].Length == 0 && content.Tables.Count == 0
            ? [] : content.Paragraphs;

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
        IReadOnlyList<XElement> chain = context.Styles.ParagraphProperties(paragraphStyleId, context.TableStyle, context.TableConditions);
        foreach (XElement styleParagraphProperties in chain)
            style = ApplyParagraphProperties(styleParagraphProperties, context.Numbering, style, context.Styles.Theme);

        style = ApplyParagraphProperties(pPr, context.Numbering, style, context.Styles.Theme);
        return ApplyList(style, chain, pPr, paragraphStyleId, context.Numbering);
    }

    /// <summary>
    /// Puts the paragraph in the list its <c>w:numPr</c> names (ECMA-376
    /// §17.9.18), its own or its style chain's, with the level's indent where
    /// the paragraph states none of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The list and the level are found separately, each from the nearest
    /// place that states it: a heading style names the list, and the abstract
    /// numbering's <c>w:pStyle</c> says which level that style is when no
    /// <c>w:ilvl</c> does. A <c>w:numId</c> of nought takes the paragraph out of
    /// the list its style put it in.
    /// </para>
    /// <para>
    /// The model's indent is where the label starts, in 18-point steps. A
    /// level states where its text starts and how far the label hangs back from
    /// there, so the label starts at the difference: Word's first level, text at
    /// 720 twips hanging 360, starts its label one step in. The paragraph's own
    /// <c>w:ind</c> overrides the level's, attribute by attribute, as it does in
    /// Word and LibreOffice.
    /// </para>
    /// </remarks>
    private static ParagraphStyle ApplyList(
        ParagraphStyle style,
        IReadOnlyList<XElement> chain,
        XElement? pPr,
        string? paragraphStyleId,
        DocxNumbering numbering)
    {
        XNamespace w = DocxNamespaces.Wordprocessing;
        int? numId = null;
        int? ilvl = null;
        foreach (XElement properties in pPr is null ? chain : chain.Append(pPr))
        {
            XElement? numPr = properties.Element(w + "numPr");
            if (numPr is null)
                continue;

            if (DocxValueReader.TryReadInt(numPr.Element(w + "numId")?.Attribute(w + "val"), out int id))
                numId = id;
            if (DocxValueReader.TryReadInt(numPr.Element(w + "ilvl")?.Attribute(w + "val"), out int level))
                ilvl = level;
        }

        if (numId is not int listId || listId == 0 ||
            numbering.Reference(listId, ilvl, paragraphStyleId) is not ListReference list)
        {
            return style;
        }

        style = style with { List = list, ListKind = list.Kind };

        DocxNumbering.LevelIndent? levelIndent = numbering.IndentFor(listId, list.Level);
        if (levelIndent is null)
            return style;

        // Paragraph/style indents override numbering attributes independently.
        int? ownLeft = null;
        int? ownFirst = null;
        foreach (XElement properties in pPr is null ? chain : chain.Append(pPr))
        {
            XElement? indentation = properties.Element(w + "ind");
            ownLeft = ReadIndentTwips(indentation, "start", "left") ?? ownLeft;
            ownFirst = ReadIndentTwips(indentation, "hanging") is int hang ? -hang :
                ReadIndentTwips(indentation, "firstLine") ?? ownFirst;
        }
        int left = ownLeft ?? levelIndent?.Left ?? 0;
        int hanging = -(ownFirst ?? -(levelIndent?.Hanging ?? 0));
        // Every reader puts a list item at indent level one at least, which is
        // where every writer and the layout expect a top-level item.
        return style with
        {
            IndentLevel = Math.Max(1, (int)Math.Round((left - hanging) / 360f)),
            LeftIndent = Math.Clamp(left / 20f, -20000, 20000),
            FirstLineIndent = Math.Clamp(-hanging / 20f, -20000, 20000),
        };
    }

    /// <summary>The first of <paramref name="names"/> a <c>w:ind</c> states, in twips.</summary>
    private static int? ReadIndentTwips(XElement? ind, params string[] names)
    {
        if (ind is null)
            return null;

        foreach (string name in names)
        {
            if (DocxValueReader.TryReadInt(ind.Attribute(DocxNamespaces.Wordprocessing + name), out int twips))
                return twips;
        }

        return null;
    }

    /// <summary>Applies paragraph properties, resolving shading against the document theme.</summary>
    private static ParagraphStyle ApplyParagraphProperties(
        XElement? pPr,
        DocxNumbering numbering,
        ParagraphStyle style, DocxTheme theme)
    {
        if (pPr is null)
            return style;

        if (pPr.Element(DocxNamespaces.Wordprocessing + "tabs") is XElement tabs)
        {
            var resolved = style.TabStops?.ToDictionary(stop => stop.Position) ?? new Dictionary<double, ParagraphTabStop>();
            foreach (XElement tab in tabs.Elements(DocxNamespaces.Wordprocessing + "tab").Take(256))
            {
                if (!DocxValueReader.TryReadTwips(tab.Attribute(DocxNamespaces.Wordprocessing + "pos"), out int twips) ||
                    twips < 0 || twips > 400000)
                    continue;
                double position = twips / 20d;
                string? kind = DocxValueReader.WordValue(tab);
                if (kind == "clear")
                    resolved.Remove(position);
                else if (kind is "left" or "start" or "center" or "right" or "end" or "decimal" or null)
                    resolved[position] = new ParagraphTabStop(position, kind switch
                    { "center" => TabAlignment.Center, "right" or "end" => TabAlignment.Right, "decimal" => TabAlignment.Decimal, _ => TabAlignment.Left },
                    (string?)tab.Attribute(DocxNamespaces.Wordprocessing + "leader") switch
                    { "dot" => ".", "hyphen" => "-", "underscore" or "heavy" => "_", "middleDot" => "·", _ => null });
            }
            style = style with { TabStops = new ParagraphTabStops(resolved.Values) };
        }

        // A w:jc names an alignment outright, including the default one. Leaving
        // "left" to fall through would make it mean "whatever was inherited",
        // so a paragraph that says left inside a right-aligned style would stay
        // right-aligned - the one case where direct formatting is ignored. An
        // absent w:jc, and only an absent one, keeps what the style chain set.
        XElement? jc = pPr.Element(DocxNamespaces.Wordprocessing + "jc");
        string? alignment = DocxValueReader.WordValue(jc);
        style = alignment switch
        {
            "left" or "start" => style with { Alignment = TextAlignment.Left },
            "center" => style with { Alignment = TextAlignment.Center },
            "right" or "end" => style with { Alignment = TextAlignment.Right },
            "both" or "distribute" => style with { Alignment = TextAlignment.Justify },
            _ => style,
        };

        // w:pageBreakBefore is an on/off property, so it follows the same rule
        // w:b and w:caps do: present with no w:val means on, and the file has
        // to spell "false" to turn one off. It matters here more than it does
        // for bold, because a template that puts a break before every heading
        // states it in the style and the one heading that must not start a
        // page turns it off in its own w:pPr. Read as "present means on" that
        // heading would open a page of its own.
        XElement? pageBreakBefore = pPr.Element(DocxNamespaces.Wordprocessing + "pageBreakBefore");
        if (pageBreakBefore is not null)
            style = style with { PageBreakBefore = ReadOnOff(pageBreakBefore) };

        // Paragraph shading (ECMA-376 §17.3.1.31). The fill is the colour behind
        // the pattern, and a "clear" pattern - nearly every one Word writes -
        // shows nothing else. "solid" shows the pattern's own colour. A fill of
        // "auto" with no pattern is no shading, which is how a paragraph takes
        // away the shading its style gave.
        if (pPr.Element(DocxNamespaces.Wordprocessing + "keepNext") is XElement keepNext)
            style = style with { KeepWithNext = ReadOnOff(keepNext) };
        if (pPr.Element(DocxNamespaces.Wordprocessing + "keepLines") is XElement keepLines)
            style = style with { KeepTogether = ReadOnOff(keepLines) };
        if (pPr.Element(DocxNamespaces.Wordprocessing + "widowControl") is XElement widowControl)
        {
            int count = ReadOnOff(widowControl) ? 2 : 0;
            style = style with { OrphanLines = count, WidowLines = count };
        }

        XElement? shading = pPr.Element(DocxNamespaces.Wordprocessing + "shd");
        if (shading is not null)
            style = style with { Background = DocxWordColor.Shading(shading, theme) };

        XElement? spacing = pPr.Element(DocxNamespaces.Wordprocessing + "spacing");
        if (spacing is not null)
        {
            if (TryReadTwips(spacing.Attribute(DocxNamespaces.Wordprocessing + "before"), out float before))
                style = style with { SpacingBefore = before };
            if (TryReadTwips(spacing.Attribute(DocxNamespaces.Wordprocessing + "after"), out float after))
                style = style with { SpacingAfter = after };

            // w:line is 240ths of a line under the auto rule, which is the one
            // an omitted w:lineRule means, and twips under the other two
            // (ECMA-376 §17.3.1.33). Each replaces what a style below stated.
            // Strict writes an auto line as if it were twips too: single
            // spacing is w:line="12pt", which is 240.
            string? lineRule = (string?)spacing.Attribute(DocxNamespaces.Wordprocessing + "lineRule");
            if (DocxValueReader.TryReadTwips(spacing.Attribute(DocxNamespaces.Wordprocessing + "line"), out int line) &&
                line != 0)
            {
                style = lineRule switch
                {
                    "exact" => style with
                    {
                        LineSpacing = 1f, LineHeightRule = LineHeightRule.Exact, LineHeight = FixedLineHeight(line),
                    },
                    "atLeast" => style with
                    {
                        LineSpacing = 1f, LineHeightRule = LineHeightRule.AtLeast, LineHeight = FixedLineHeight(line),
                    },
                    _ when line > 0 => style with
                    {
                        LineSpacing = line / 240f, LineHeightRule = LineHeightRule.Proportional, LineHeight = 0f,
                    },
                    _ => style,
                };
            }
        }

        XElement? ind = pPr.Element(DocxNamespaces.Wordprocessing + "ind");
        if (ind is not null && TryReadStartIndent(ind, out int startTwips))
            style = style with { IndentLevel = Math.Max(0, (int)Math.Round(startTwips / 360f)), LeftIndent = Math.Clamp(startTwips / 20f, -20000, 20000) };
        if (ReadIndentTwips(ind, "end", "right") is int rightTwips)
            style = style with { RightIndent = Math.Clamp(rightTwips / 20f, -20000, 20000) };
        if (ReadIndentTwips(ind, "hanging") is int hangingTwips)
            style = style with { FirstLineIndent = Math.Clamp(-hangingTwips / 20f, -20000, 20000) };
        else if (ReadIndentTwips(ind, "firstLine") is int firstTwips)
            style = style with { FirstLineIndent = Math.Clamp(firstTwips / 20f, -20000, 20000) };

        XElement? numPr = pPr.Element(DocxNamespaces.Wordprocessing + "numPr");
        if (numPr is not null)
        {
            bool hasNumId = DocxValueReader.TryReadInt(
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
                if (DocxValueReader.TryReadInt(numPr.Element(DocxNamespaces.Wordprocessing + "ilvl")?.Attribute(DocxNamespaces.Wordprocessing + "val"), out int ilvl))
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
    /// <summary>
    /// The formatting a run inherits when it and its paragraph name no style of
    /// their own: <c>w:docDefaults</c> plus the default paragraph style.
    /// </summary>
    /// <remarks>
    /// Resolved through the same chain a real run goes through, rather than by
    /// reading <c>w:docDefaults</c> directly, so a document whose default style
    /// overrides the document defaults reports what a run would actually get.
    /// Word writes both, and they disagree more often than not.
    /// </remarks>
    private static DocumentStyleDefaults ReadStyleDefaults(DocxStyles styles)
    {
        InlineStyle style = InlineStyle.Default;
        foreach (XElement rPr in styles.RunPropertiesForParagraph(null))
            style = ApplyRunProperties(rPr, style, styles.Theme);

        return new DocumentStyleDefaults
        {
            FontSizePoints = style.FontSize is float size && size > 0
                ? size
                : DocumentStyleDefaults.FallbackFontSizePoints,
            FontFamily = string.IsNullOrWhiteSpace(style.FontFamily) ? null : style.FontFamily,
        };
    }

    private static InlineStyle ApplyRunProperties(XElement? rPr, InlineStyle style, DocxTheme theme)
    {
        if (rPr is null)
            return style;

        style = ApplyOnOff(rPr, "b", style, static (s, v) => s with { Bold = v });
        style = ApplyOnOff(rPr, "i", style, static (s, v) => s with { Italic = v });
        style = ApplyOnOff(rPr, "strike", style, static (s, v) => s with { Strikethrough = v });
        style = ApplyOnOff(rPr, "dstrike", style, static (s, v) => s with { Strikethrough = v });
        style = ApplyOnOff(rPr, "vanish", style, static (s, v) => s with { Hidden = v });

        // w:smallCaps first: when a run turns both on, Word draws all caps. An
        // explicit "off" clears only the kind it names, so a run can drop the
        // small caps its style applied without disturbing anything else.
        style = ApplyCapitalization(rPr, "smallCaps", TextCapitalization.SmallCaps, style);
        style = ApplyCapitalization(rPr, "caps", TextCapitalization.AllCaps, style);

        XElement? underline = rPr.Element(DocxNamespaces.Wordprocessing + "u");
        if (underline is not null)
        {
            string? value = DocxValueReader.WordValue(underline);
            style = style with { Underline = !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) };
        }

        // The model holds one family, and it is the one the run's Latin text is
        // drawn in: w:ascii and w:hAnsi, or the theme fonts they name. w:cs is
        // for complex-script text and w:eastAsia for East Asian text (ECMA-376
        // §17.3.2.26), and a style that names only those leaves the Latin font
        // it inherited, as Word and LibreOffice draw it. Heading styles that say
        // only w:cs="Arial" were drawn in Arial over a Times New Roman document.
        // They still give a family to a run that has inherited none, which is
        // the East Asian document whose fonts are all w:eastAsia.
        XElement? fonts = rPr.Element(DocxNamespaces.Wordprocessing + "rFonts");
        string? fontFamily =
            (string?)fonts?.Attribute(DocxNamespaces.Wordprocessing + "ascii") ??
            (string?)fonts?.Attribute(DocxNamespaces.Wordprocessing + "hAnsi") ??
            // Styles usually name fonts through the theme rather than directly.
            theme.Resolve((string?)fonts?.Attribute(DocxNamespaces.Wordprocessing + "asciiTheme")) ??
            theme.Resolve((string?)fonts?.Attribute(DocxNamespaces.Wordprocessing + "hAnsiTheme"));
        if (string.IsNullOrWhiteSpace(fontFamily) && string.IsNullOrWhiteSpace(style.FontFamily))
        {
            fontFamily =
                (string?)fonts?.Attribute(DocxNamespaces.Wordprocessing + "eastAsia") ??
                (string?)fonts?.Attribute(DocxNamespaces.Wordprocessing + "cs") ??
                theme.Resolve((string?)fonts?.Attribute(DocxNamespaces.Wordprocessing + "eastAsiaTheme")) ??
                theme.Resolve((string?)fonts?.Attribute(DocxNamespaces.Wordprocessing + "cstheme"));
        }

        if (!string.IsNullOrWhiteSpace(fontFamily))
            style = style with { FontFamily = fontFamily };

        XElement? size = rPr.Element(DocxNamespaces.Wordprocessing + "sz");
        if (DocxValueReader.TryReadHalfPoints(size?.Attribute(DocxNamespaces.Wordprocessing + "val"), out int halfPoints) && halfPoints > 0)
            style = style with { FontSize = halfPoints / 2f };

        XElement? color = rPr.Element(DocxNamespaces.Wordprocessing + "color");
        string? colorValue = DocxValueReader.WordValue(color);
        if (DocxWordColor.TryRead(color, theme, fill: false, out BColor foreground))
        {
            style = style with { Foreground = foreground };
        }
        else if (string.Equals(colorValue, "auto", StringComparison.OrdinalIgnoreCase))
        {
            // "auto" is a colour, not the absence of one (ECMA-376 §17.3.2.6):
            // the automatic colour Word and LibreOffice draw black. Stated on a
            // run it overrides the colour its style gave; a document whose Normal
            // style is blue and whose every run says auto is black text.
            style = style with { Foreground = BColor.Empty };
        }

        XElement? shade = rPr.Element(DocxNamespaces.Wordprocessing + "shd");
        if (shade is not null)
            style = style with { Background = DocxWordColor.Shading(shade, theme) };

        XElement? highlight = rPr.Element(DocxNamespaces.Wordprocessing + "highlight");
        string? highlightValue = DocxValueReader.WordValue(highlight);
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

        if (!DocumentLinkTarget.IsAllowed(href))
        {
            builder.AddDiagnosticOnce("docx.link", "A hyperlink with a disallowed or relative target was dropped.");
            return style;
        }

        return style with { LinkHref = href };
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

    /// <summary>
    /// Whether a <c>w:br</c> starts a page. The kind lives in <c>w:type</c>,
    /// which is an enumeration and not one of the on/off attributes, so it is
    /// matched exactly: an absent type is a line break, and <c>textWrapping</c>
    /// is a break with no page in it.
    /// </summary>
    /// <remarks>
    /// <c>column</c> is a page break only where there is no next column to move
    /// to. ECMA-376 says so of <c>ST_BrType</c>: when the section is not
    /// divided into columns, the text restarts on the next page, and Word and
    /// LibreOffice both start one. A document that opens each of its blocks with
    /// one - LibreOffice's <c>FDO77715.docx</c> is forty pages of them - ran
    /// every block on from the last while it was read as a line break. In a
    /// section of several columns it moves to the next column, which is not a
    /// page boundary at all, and is read as
    /// <see cref="ParagraphStyle.ColumnBreakBefore"/>. Outside the body's flow -
    /// a header, a text box - there are no sections and it stays a line break.
    /// </remarks>
    private static bool IsPageBreak(XElement br, DocxReadContext context) =>
        string.Equals(
            (string?)br.Attribute(DocxNamespaces.Wordprocessing + "type"),
            "page",
            StringComparison.Ordinal) ||
        (IsColumnBreak(br) && context.Sections is { CurrentColumnCount: <= 1 });

    private static bool IsColumnBreak(XElement br) =>
        string.Equals(
            (string?)br.Attribute(DocxNamespaces.Wordprocessing + "type"),
            "column",
            StringComparison.Ordinal);

    private static bool ReadOnOff(XElement element)
    {
        string? value = DocxValueReader.WordValue(element);
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
        DocxValueReader.TryReadTwips(ind.Attribute(DocxNamespaces.Wordprocessing + "start"), out twips) ||
        DocxValueReader.TryReadTwips(ind.Attribute(DocxNamespaces.Wordprocessing + "left"), out twips);

    /// <summary>An exact or at-least w:line in twips, as points, within Word's limit.</summary>
    private static float FixedLineHeight(int twips) =>
        (float)Math.Min(Math.Abs((long)twips) / 20d, ParagraphStyle.MaxLineHeight);

    private static bool TryReadTwips(XAttribute? attribute, out float points)
    {
        points = 0f;
        if (!DocxValueReader.TryReadTwips(attribute, out int twips))
            return false;

        points = twips / 20f;
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

}
