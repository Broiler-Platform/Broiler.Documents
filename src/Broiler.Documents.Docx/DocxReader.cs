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
                sections);
            document = document.WithRunningContent(ReadRunningContent(
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
                settings.EvenAndOddHeaders));
            document = document.WithPageGeometry(page);
            document = document.WithSections(sections.ToModel(document.ParagraphCount, page));
            document = document.WithStyleDefaults(ReadStyleDefaults(styles));
            document = document.WithPageBackground(DocxVml.ReadBackground(
                documentXml.Root?.Element(DocxNamespaces.Wordprocessing + "background"),
                settings.DisplayBackgroundShape,
                (code, message) =>
                {
                    if (reported.Add(code))
                        diagnostics.Add(DocumentDiagnostic.Warning(code, message));
                }));
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
        DocxSections sections)
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
        var context = new DocxReadContext(relationships, numbering, styles, images, builder, page, Sections: sections);
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
    /// The model has one set, and it is the document's final section's: the
    /// body-level <c>w:sectPr</c>. A document split into sections by a
    /// <c>w:sectPr</c> inside a paragraph can give each section its own header,
    /// so the last section's wins and the rest raise a diagnostic rather than
    /// being silently preferred or silently lost.
    /// </para>
    /// <para>
    /// The last section's set is not only the references it holds. ECMA-376
    /// §17.10.5 says a section that names no header of a type continues the one
    /// the section before it had, so each type the last section leaves out is
    /// taken from the nearest section before it that names one. A contract
    /// whose first section names a header and footer and whose eight after it
    /// name none carries them on every page in Word and LibreOffice; read from
    /// the last section alone it carried neither.
    /// </para>
    /// <para>
    /// A part the document does not turn on is not read. A <c>first</c> part is
    /// drawn only in a section with <c>w:titlePg</c>, and an <c>even</c> part
    /// only where <c>w:settings</c> holds <c>w:evenAndOddHeaders</c>; without
    /// them Word puts the default on those pages, and the model, which uses a
    /// first or even part wherever there is one, would put the other there.
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
        XElement? sectPr = body?.Elements(DocxNamespaces.Wordprocessing + "sectPr").LastOrDefault();
        if (sectPr is null)
            return RunningContent.Empty;

        IReadOnlyList<XElement> sections = DocxSections.Properties(body);
        if (sections.Count > 1)
        {
            diagnostics.Add(DocumentDiagnostic.Info(
                "docx.section.multiple",
                "DOCX document has more than one section; the last section's header and footer were read, " +
                "each it does not name continuing from the section before it."));
        }

        // w:titlePg is what makes a `first` reference mean anything, and it is
        // also what makes a band the first page does not name empty there rather
        // than the default one. Read before the references, because a first-page
        // header with no first-page footer beside it is exactly the arrangement it
        // decides: LibreOffice draws no footer on page one of such a section, and
        // this reader drew the default one carrying a stale cached page number.
        // It is the last section's own: unlike a reference, it is not continued.
        XElement? titlePage = sectPr.Element(DocxNamespaces.Wordprocessing + "titlePg");
        bool differentFirstPage = titlePage is not null && ReadOnOff(titlePage);
        RunningContent content = RunningContent.Empty.WithDifferentFirstPage(differentFirstPage);

        foreach (XElement reference in ContinuedReferences(sections))
        {
            bool isHeader = reference.Name == DocxNamespaces.Wordprocessing + "headerReference";
            PageSelection selection = SelectionFor((string?)reference.Attribute(DocxNamespaces.Wordprocessing + "type"));
            if (selection == PageSelection.First && !differentFirstPage)
            {
                if (reported.Add("docx.header.first"))
                {
                    diagnostics.Add(DocumentDiagnostic.Info(
                        "docx.header.first",
                        "A DOCX first-page header or footer was not read: its section does not set w:titlePg, " +
                        "so the first page carries the default one."));
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

            var part = ReadPart(
                archive, relationship.Target, numbering, styles, images, limits, diagnostics, reported,
                isHeader ? RunningBand.Header : RunningBand.Footer,
                page);
            if (part is not var (paragraphs, shapes) || (paragraphs.Count == 0 && shapes.Count == 0))
                continue;

            content = isHeader
                ? content.WithHeader(selection, paragraphs, shapes)
                : content.WithFooter(selection, paragraphs, shapes);
        }

        return content;
    }

    /// <summary>
    /// The header and footer references that apply to the last of
    /// <paramref name="sections"/>: for each band and type, the last section's
    /// own, or else the nearest earlier section's.
    /// </summary>
    private static IEnumerable<XElement> ContinuedReferences(IReadOnlyList<XElement> sections)
    {
        foreach (string band in new[] { "headerReference", "footerReference" })
        {
            foreach (PageSelection selection in new[] { PageSelection.Default, PageSelection.First, PageSelection.Even })
            {
                for (int i = sections.Count - 1; i >= 0; i--)
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
    private readonly record struct DocxSettings(bool EvenAndOddHeaders, bool DisplayBackgroundShape);

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
            displayBackground is not null && ReadOnOff(displayBackground));
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
        IReadOnlyList<RichTextParagraph> partParagraphs = part.Paragraphs;

        // A single empty paragraph is what an empty part builds, and it is not
        // content: a header that holds only a stripe should not report a blank
        // line beside it.
        if (partParagraphs.Count == 1 && partParagraphs[0].Length == 0)
            partParagraphs = [];

        return (partParagraphs, part.Shapes);
    }

    private static void ReadParagraph(XElement paragraph, DocxReadContext context)
    {
        XElement? pPr = paragraph.Element(DocxNamespaces.Wordprocessing + "pPr");
        string? paragraphStyleId = DocxValueReader.WordValue(pPr?.Element(DocxNamespaces.Wordprocessing + "pStyle"));

        ParagraphStyle paragraphStyle = ReadParagraphStyle(pPr, paragraphStyleId, context);
        InlineStyle inherited = ReadInheritedRunStyle(paragraphStyleId, context);
        context.Builder.StartParagraph(paragraphStyle, ReadMarkStyle(pPr, inherited, context));
        ReadParagraphChildren(paragraph.Elements(), context, inherited);
        context.Builder.FinishParagraph();

        // A paragraph belongs to the section it ends, so the cursor moves only
        // once the paragraph's own breaks have been read against it. The next
        // section then says whether it starts a page; its first paragraph is
        // where that break lands, as if the paragraph before had ended on one.
        if (context.Sections is not null && DocxSections.EndsSection(paragraph))
        {
            context.Sections.Advance(context.Builder.CurrentParagraphIndex);
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
    }

    /// <summary>
    /// The character formatting a run inherits before its own <c>w:rPr</c>:
    /// document defaults, the table style's run properties inside a table, then
    /// the paragraph style chain's run properties.
    /// </summary>
    private static InlineStyle ReadInheritedRunStyle(string? paragraphStyleId, DocxReadContext context)
    {
        InlineStyle style = InlineStyle.Default;
        foreach (XElement rPr in context.Styles.RunPropertiesForParagraph(paragraphStyleId, context.TableStyle))
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
        _ = context.Builder;
        XElement? rPr = run.Element(DocxNamespaces.Wordprocessing + "rPr");

        // ECMA-376 §17.7.2: the character style named by w:rStyle applies over
        // the paragraph's inherited formatting, and the run's own w:rPr over that.
        InlineStyle style = inherited;
        string? characterStyleId = DocxValueReader.WordValue(rPr?.Element(DocxNamespaces.Wordprocessing + "rStyle"));
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

            // The break Ctrl+Enter writes is a run child, not a paragraph
            // property, and it sits at the end of the paragraph *before* the
            // one that starts the page. The builder holds it until then; see
            // DocxDocumentBuilder.NotePageBreak.
            if (child.Name == DocxNamespaces.Wordprocessing + "br" && IsPageBreak(child, context))
            {
                builder.NotePageBreak(style);
                continue;
            }

            // In a section of several columns the break moves to the next one,
            // and travels to the paragraph it starts exactly as a page break does.
            if (child.Name == DocxNamespaces.Wordprocessing + "br" && IsColumnBreak(child) && context.Sections is not null)
            {
                builder.NotePageBreak(style, column: true);
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
    /// kept on it, so a picture inside a hyperlink stays inside that link. An
    /// anchored picture goes to <see cref="ReadFloatingPicture"/> instead.
    /// </summary>
    private static void ReadPicture(XElement picture, DocxReadContext context, InlineStyle style)
    {
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

        (ShapeWrap Wrap, WrapSide Side, double Distance) = ReadWrap(anchor);
        (double offsetY, bool fromPage) = VerticalOffset(anchor, context);
        context.Builder.AddShape(new DocumentShape(
            context.Builder.CurrentParagraphIndex,
            HorizontalOffset(anchor, context),
            offsetY,
            width,
            height,
            fill,
            ReadOutline(spPr),
            paragraphs,
            image: null,
            behindText: BehindDoc(anchor),
            wrap: Wrap,
            wrapSide: Side,
            wrapDistance: Distance,
            zOrder: RelativeHeight(anchor),
            offsetFromPage: fromPage));
        return true;
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

        XElement? shape = picture.Elements().FirstOrDefault(static element =>
            element.Name == DocxNamespaces.Vml + "rect" ||
            element.Name == DocxNamespaces.Vml + "roundrect" ||
            element.Name == DocxNamespaces.Vml + "oval" ||
            element.Name == DocxNamespaces.Vml + "shape");
        if (shape is null || shape.Descendants(DocxNamespaces.Vml + "imagedata").Any())
            return false;

        Dictionary<string, string> style = DocxVml.ReadStyle((string?)shape.Attribute("style"));
        if (!DocxVml.TryLength(style.GetValueOrDefault("width"), out double width) ||
            !DocxVml.TryLength(style.GetValueOrDefault("height"), out double height) ||
            width <= 0 || height <= 0)
        {
            return false;
        }

        ShapeFill? fill = DocxVml.ReadFill(
            shape,
            BColor.Empty,
            (code, message) => context.Builder.AddDiagnosticOnce(code, message),
            "docx.shape.fill",
            "shape");
        IReadOnlyList<RichTextParagraph> paragraphs = ReadShapeText(shape, context);
        if (fill is null && paragraphs.Count == 0)
            return false;

        DocxVml.TryLength(style.GetValueOrDefault("left"), out double left);
        DocxVml.TryLength(style.GetValueOrDefault("margin-left"), out double marginLeft);
        DocxVml.TryLength(style.GetValueOrDefault("top"), out double top);
        DocxVml.TryLength(style.GetValueOrDefault("margin-top"), out double marginTop);
        // Across, every frame converts to the column, as a DrawingML anchor's does.
        double offsetX = left + marginLeft;
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
        double offsetY = top + marginTop;
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

        int zIndex = int.TryParse(style.GetValueOrDefault("z-index"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int z)
            ? z
            : 0;
        ShapeWrap wrap = (string?)shape.Element(DocxNamespaces.VmlWord + "wrap")?.Attribute("type") switch
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
            ReadVmlOutline(shape),
            paragraphs,
            image: null,
            behindText: zIndex < 0,
            wrap: wrap,
            zOrder: zIndex == int.MinValue ? int.MaxValue : Math.Abs(zIndex),
            offsetFromPage: fromPage));
        return true;
    }

    /// <summary>A VML shape's outline colour, or empty for none.</summary>
    private static BColor ReadVmlOutline(XElement shape)
    {
        XElement? stroke = shape.Element(DocxNamespaces.Vml + "stroke");
        string? stroked = (string?)shape.Attribute("stroked") ?? (string?)stroke?.Attribute("on");
        if (stroked?.Trim() is "f" or "false" or "0")
            return BColor.Empty;

        return DocxVml.TryColor((string?)shape.Attribute("strokecolor") ?? (string?)stroke?.Attribute("color"), out BColor color)
            ? color
            : BColor.FromArgb(0, 0, 0);
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

    private static ShapeFill? ReadFill(XElement? spPr, DocxDocumentBuilder builder)
    {
        if (spPr is null)
            return null;

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
                TryReadShapeColor(stops[0], out BColor outer) &&
                TryReadShapeColor(stops[1], out BColor inner) &&
                TryReadShapeColor(stops[2], out BColor last) &&
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
                TryReadShapeColor(stops[0], out BColor start) &&
                TryReadShapeColor(stops[^1], out BColor end))
            {
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
        DocxValueReader.TryParseHexColor(
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
            context.Relationships, context.Numbering, context.Styles, context.Images, builder,
            context.Page, context.Band);
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
        foreach (XElement styleParagraphProperties in context.Styles.ParagraphProperties(paragraphStyleId, context.TableStyle))
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

        XElement? spacing = pPr.Element(DocxNamespaces.Wordprocessing + "spacing");
        if (spacing is not null)
        {
            if (TryReadTwips(spacing.Attribute(DocxNamespaces.Wordprocessing + "before"), out float before))
                style = style with { SpacingBefore = before };
            if (TryReadTwips(spacing.Attribute(DocxNamespaces.Wordprocessing + "after"), out float after))
                style = style with { SpacingAfter = after };

            string? lineRule = (string?)spacing.Attribute(DocxNamespaces.Wordprocessing + "lineRule");
            if ((lineRule is null || lineRule == "auto") &&
                DocxValueReader.TryReadInt(spacing.Attribute(DocxNamespaces.Wordprocessing + "line"), out int line) &&
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
        if (DocxValueReader.TryReadInt(size?.Attribute(DocxNamespaces.Wordprocessing + "val"), out int halfPoints) && halfPoints > 0)
            style = style with { FontSize = halfPoints / 2f };

        XElement? color = rPr.Element(DocxNamespaces.Wordprocessing + "color");
        string? colorValue = DocxValueReader.WordValue(color);
        if (DocxValueReader.TryParseHexColor(colorValue, out BColor foreground))
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
        string? fill = (string?)shade?.Attribute(DocxNamespaces.Wordprocessing + "fill");
        if (DocxValueReader.TryParseHexColor(fill, out BColor shadeColor))
            style = style with { Background = shadeColor };

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
        DocxValueReader.TryReadInt(ind.Attribute(DocxNamespaces.Wordprocessing + "start"), out twips) ||
        DocxValueReader.TryReadInt(ind.Attribute(DocxNamespaces.Wordprocessing + "left"), out twips);

    private static bool TryReadTwips(XAttribute? attribute, out float points)
    {
        points = 0f;
        if (!DocxValueReader.TryReadInt(attribute, out int twips))
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
