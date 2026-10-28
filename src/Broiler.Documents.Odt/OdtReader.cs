using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Broiler.Documents.Model;
using Broiler.Documents.Packaging;
using Broiler.Documents.Resources;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Odt;

internal static class OdtReader
{
    public static DocumentReadResult Read(byte[] bytes, DocumentReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<DocumentDiagnostic>();
        try
        {
            return OdtFlatDocument.LooksLikeXml(bytes)
                ? ReadFlat(bytes, options, diagnostics)
                : ReadPackage(bytes, options, diagnostics);
        }
        catch (InvalidDataException ex)
        {
            diagnostics.Add(DocumentDiagnostic.Error(
                "odt.package.zip",
                "The ODT ZIP package could not be opened: " + ex.GetType().Name + "."));
            return new DocumentReadResult(RichTextDocument.Empty, diagnostics, DocumentResultStatus.Rejected);
        }
        catch (XmlException ex)
        {
            diagnostics.Add(DocumentDiagnostic.Error(
                "odt.xml",
                "The ODT XML could not be parsed: " + ex.GetType().Name + "."));
            return new DocumentReadResult(RichTextDocument.Empty, diagnostics, DocumentResultStatus.Rejected);
        }
    }

    private static DocumentReadResult ReadPackage(
        byte[] bytes,
        DocumentReadOptions options,
        List<DocumentDiagnostic> diagnostics)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        OdtManifest manifest = OdtPackage.ReadManifest(archive, options.Limits, diagnostics);
        if (manifest.IsEncrypted)
        {
            // ODF password protection really encrypts the parts. There is
            // nothing to read and no key this codec may ask for.
            diagnostics.Add(DocumentDiagnostic.Error(
                "odt.package.encrypted",
                "The ODT package is encrypted; this codec does not decrypt packages."));
            return new DocumentReadResult(RichTextDocument.Empty, diagnostics, DocumentResultStatus.Rejected);
        }

        ZipArchiveEntry? contentEntry = OdtPackage.FindEntry(archive, OdtNamespaces.ContentPart);
        if (contentEntry is null)
        {
            diagnostics.Add(DocumentDiagnostic.Error(
                "odt.package.content",
                "The ODT package did not contain a content.xml part."));
            return new DocumentReadResult(RichTextDocument.Empty, diagnostics, DocumentResultStatus.Rejected);
        }

        XDocument? content = OdtPackage.LoadEntryXml(
            contentEntry,
            options.Limits,
            diagnostics,
            "odt.content.xml",
            LoadOptions.PreserveWhitespace);
        if (content is null)
            return new DocumentReadResult(RichTextDocument.Empty, diagnostics, DocumentResultStatus.Rejected);

        OdtStyles styles = OdtStyles.Load(archive, content, options.Limits, diagnostics);
        var resources = new DocumentConversionContextBuilder(options.ResourcePolicy);
        var images = new OdtImageLoader(archive, manifest, options.Limits, resources);
        return ReadDocument(
            content,
            styles,
            images,
            resources,
            () => OdtMetadata.Read(archive, options.Limits, diagnostics),
            ReadSettings(archive, options.Limits, diagnostics),
            options,
            diagnostics);
    }

    /// <summary>
    /// Reads a flat document: the same ODF content in one XML file rather than
    /// a package of parts, under an <c>office:document</c> root.
    /// </summary>
    /// <remarks>
    /// The root holds everything <c>content.xml</c>, <c>styles.xml</c> and
    /// <c>meta.xml</c> hold between them, as siblings, so the read is the
    /// package read pointed at one element instead of three parts. It is loaded
    /// with white space preserved, as <c>content.xml</c> is, because the body
    /// is in it. There is no manifest to declare encryption - a flat document
    /// cannot be encrypted - and no package to hold pictures, so a picture is
    /// either inline <c>office:binary-data</c> or a link this codec does not
    /// follow. The whole file is bounded by MaxDocumentBytes before it gets
    /// here, and each inline picture by MaxBinBytes as it is decoded.
    /// </remarks>
    private static DocumentReadResult ReadFlat(
        byte[] bytes,
        DocumentReadOptions options,
        List<DocumentDiagnostic> diagnostics)
    {
        XDocument? document = DocumentPackage.LoadXml(
            bytes,
            diagnostics,
            "odt.xml",
            "The flat ODT document",
            LoadOptions.PreserveWhitespace,
            OdtNamespaces.ImpliedPrefixes);
        XElement? root = document?.Root;
        if (document is null || root is null)
            return new DocumentReadResult(RichTextDocument.Empty, diagnostics, DocumentResultStatus.Rejected);

        if (root.Name != OdtNamespaces.Office + "document")
        {
            diagnostics.Add(DocumentDiagnostic.Error(
                "odt.flat.root",
                "The XML document was not a flat ODT document: its root element was not office:document."));
            return new DocumentReadResult(RichTextDocument.Empty, diagnostics, DocumentResultStatus.Rejected);
        }

        string? mediaType = ((string?)root.Attribute(OdtNamespaces.Office + "mimetype"))?.Trim();
        if (mediaType is not null && !OdtFlatDocument.IsTextMediaType(mediaType))
        {
            // A flat spreadsheet or presentation has the same root and says what
            // it is here, as a package says it in its mimetype entry.
            diagnostics.Add(DocumentDiagnostic.Error(
                "odt.flat.media-type",
                "The flat OpenDocument file declared a non-text media type."));
            return new DocumentReadResult(RichTextDocument.Empty, diagnostics, DocumentResultStatus.Rejected);
        }

        OdtStyles styles = OdtStyles.Load(root, contentRoot: null, options.Limits, diagnostics);
        var resources = new DocumentConversionContextBuilder(options.ResourcePolicy);
        var images = new OdtImageLoader(archive: null, OdtManifest.Empty, options.Limits, resources);
        return ReadDocument(
            document,
            styles,
            images,
            resources,
            () => OdtMetadata.Read(root.Element(OdtNamespaces.Office + "meta"), diagnostics),
            root.Element(OdtNamespaces.Office + "settings"),
            options,
            diagnostics);
    }

    /// <summary>The part of a read that is the same for a package and a flat document.</summary>
    private static DocumentReadResult ReadDocument(
        XDocument content,
        OdtStyles styles,
        OdtImageLoader images,
        DocumentConversionContextBuilder resources,
        Func<DocumentMetadata> readMetadata,
        XElement? settings,
        DocumentReadOptions options,
        List<DocumentDiagnostic> diagnostics)
    {
        // One resolution, used by both. The page and the header that sits on
        // it belong to the same master page, and reading them from two
        // different ones would put a Letter header on an A4 page without
        // anything saying so. The page is read before the content, because a
        // frame anchored to a page states its position from the page's edges.
        styles.ReadFillImage = definition => images.Read(new XElement(OdtNamespaces.Draw + "frame",
            new XElement(OdtNamespaces.Draw + "image", definition.Attributes().Where(attribute => attribute.Name.Namespace == OdtNamespaces.XLink),
                definition.Elements(OdtNamespaces.Office + "binary-data"))), styles);
        XElement? master = ResolveMasterPage(content, styles);
        PageGeometry? page = ReadPageGeometry(master, styles, diagnostics);
        RichTextDocument document = ReadContent(content, styles, images, page, options.Limits, diagnostics, master);
        document = document.WithRunningContent(
            ReadRunningContent(master, styles, images, options.Limits, diagnostics));
        document = document.WithPageGeometry(page);
        document = document.WithStyleDefaults(ReadStyleDefaults(styles));
        document = document.WithPageBackground(
            ReadPageBackground(master, PageLayoutProperties(master, styles), styles));
        document = document.WithNoteNumbering(ReadNoteNumbering(styles));
        document = document.WithShowsRevisions(OdtRevisions.Shown(settings));
        DocumentMetadata metadata = readMetadata();
        return new DocumentReadResult(
            document,
            diagnostics,
            DocumentReadResult.StatusFrom(diagnostics),
            resources.Build(),
            metadata);
    }

    /// <summary>
    /// The package's <c>office:settings</c>, or null where it has none or it
    /// cannot be read. Settings only say how the document is viewed, so one
    /// that does not parse costs the view and nothing else: a warning, where a
    /// content part that does not parse is an error.
    /// </summary>
    private static XElement? ReadSettings(ZipArchive archive, DocumentLimits limits, List<DocumentDiagnostic> diagnostics)
    {
        ZipArchiveEntry? entry = OdtPackage.FindEntry(archive, OdtNamespaces.SettingsPart);
        if (entry is null)
            return null;

        var failures = new List<DocumentDiagnostic>();
        XElement? root = OdtPackage.LoadEntryXml(entry, limits, failures, "odt.settings.xml")?.Root;
        if (failures.Count > 0)
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                "odt.settings.xml",
                "The ODT settings part could not be read; the document is viewed as by default, its tracked changes shown."));
        }

        return root;
    }

    private static RichTextDocument ReadContent(
        XDocument content,
        OdtStyles styles,
        OdtImageLoader images,
        PageGeometry? page,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics,
        XElement? initialMaster)
    {
        XElement? body = content.Root
            ?.Element(OdtNamespaces.Office + "body")
            ?.Element(OdtNamespaces.Office + "text");
        if (body is null)
        {
            diagnostics.Add(DocumentDiagnostic.Error(
                "odt.document.body",
                "The ODT content.xml did not contain an office:text body."));
            return RichTextDocument.Empty;
        }

        var builder = new OdtDocumentBuilder(limits, diagnostics);
        var starts = new List<(int Index, PageGeometry? Page, RunningContent Running, PageStyleSequence? Sequence)>();
        var masters = styles.MasterStyles?.Elements(OdtNamespaces.Style + "master-page")
            .Where(master => master.Attribute(OdtNamespaces.Style + "name") is not null)
            .GroupBy(master => (string)master.Attribute(OdtNamespaces.Style + "name")!)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        PageStyleSequence? SequenceFor(XElement? first)
        {
            if (first is null)
                return null;
            var seen = new Dictionary<XElement, int>();
            var sequence = new List<PageStyle>();
            XElement? master = first;
            int repeatFrom = 0;
            while (master is not null && sequence.Count < limits.MaxGroupDepth)
            {
                if (seen.TryGetValue(master, out repeatFrom))
                    return new PageStyleSequence(sequence, repeatFrom);
                seen.Add(master, sequence.Count);
                RunningContent running = ReadParts(master, RunningContent.Empty, false, styles, images, limits, diagnostics);
                sequence.Add(new PageStyle(ReadPageGeometry(master, styles, diagnostics) ?? page, running,
                    ReadPageBackground(master, PageLayoutProperties(master, styles), styles)));
                master = NextMasterPage(master, styles);
            }
            return new PageStyleSequence(sequence, sequence.Count - 1);
        }
        starts.Add((0, page, ReadRunningContent(initialMaster, styles, images, limits, diagnostics), SequenceFor(initialMaster)));
        OdtReadContext? context = null;
        context = new OdtReadContext(styles, images, builder, page)
        {
            Regions = OdtRevisions.ReadRegions(body),
            LinkedFrameTargets = content.Descendants(OdtNamespaces.Draw + "text-box")
                .Select(box => (string?)box.Attribute(OdtNamespaces.Draw + "chain-next-name"))
                .Where(name => !string.IsNullOrEmpty(name)).Select(name => name!).ToHashSet(StringComparer.Ordinal),
            SelectPageStyle = (family, style, index) =>
            {
                if (context!.TableDepth > 0 || context.HiddenSectionDepth > 0)
                    return false;
                string? name = styles.MasterPageName(family, style);
                if (string.IsNullOrEmpty(name))
                    return false;
                // Naming a master page implies a boundary even in an incomplete
                // document whose master definition is absent. Keep the active
                // paper/running content when there is no replacement to read.
                if (masters is null || !masters.TryGetValue(name, out XElement? master))
                    return index > 0;
                PageGeometry? selected = ReadPageGeometry(master, styles, diagnostics) ?? page;
                context!.ActivePage = selected;
                if (index == 0)
                    return false;
                if (starts[^1].Index == index)
                    starts.RemoveAt(starts.Count - 1);
                starts.Add((index, selected, ReadRunningContent(master, styles, images, limits, diagnostics), SequenceFor(master)));
                return true;
            },
        };
        ReadBlockContent(body.Elements(), context, list: null, depth: 0);

        // A break after the last paragraph asks for a page with nothing on it,
        // and a document is a sequence of paragraphs here rather than a sequence
        // of pages, so there is no empty page to add.
        context.DropPageBreak();

        builder.ReportReadSummary(
            body.Elements().Any(IsContentBlock),
            styles.Count,
            styles.ListStyleCount,
            images.ImageCount);
        RichTextDocument built = builder.Build();
        if (starts.Count == 1 && (starts[0].Sequence is null || starts[0].Sequence!.Styles.Count == 1))
            return built;
        int[] edges = [.. starts.Select(start => start.Index)
            .Concat(built.Sections.SelectMany(section => new[] { section.ParagraphIndex, section.ParagraphEnd }))
            .Append(built.ParagraphCount).Distinct().Order()];
        var sections = new List<DocumentSection>();
        for (int i = 0; i + 1 < edges.Length; i++)
        {
            int index = edges[i];
            var selected = starts.Last(start => start.Index <= index);
            DocumentSection? textSection = DocumentSection.Containing(built.Sections, index);
            PageGeometry? sectionGeometry = textSection?.Page ?? selected.Page;
            sections.Add(new DocumentSection(index, edges[i + 1] - index,
                textSection?.ColumnCount ?? selected.Page?.ColumnCount ?? 1,
                textSection?.ColumnGap ?? selected.Page?.ColumnGap ?? 0,
                sectionGeometry, selected.Running, textSection?.Background ?? default, selected.Sequence,
                textSection?.Hidden ?? false,
                textSection?.DontBalanceColumns ?? false,
                textSection?.BreakType ?? SectionBreakType.NextPage));
        }
        return built.WithSections(sections);
    }

    /// <summary>
    /// The master page the document actually begins on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to be the first <c>style:master-page</c> in <c>styles.xml</c>,
    /// on the reasoning that it is the one the default body style points at in
    /// every document a word processor writes. That is true of documents a word
    /// processor writes from scratch and false of ones it converts. LibreOffice
    /// converting HTML to ODF emits two master pages - <c>Standard</c> carrying
    /// its own default paper, and <c>HTML</c> carrying the page the source
    /// actually asked for - and lays the body out on the second. Reading the
    /// first gave a document whose stated page was A4 while every other reader
    /// of the same file, including the one that wrote it, saw US Letter.
    /// </para>
    /// <para>
    /// ODF states the link the other way round from where a reader looks for it.
    /// A master page does not say that it is the first; the <em>content</em>
    /// says which master page it is on, by way of the
    /// <c>style:master-page-name</c> of the style on its first block. So that is
    /// what is followed here, and the search stops at the first block: a
    /// non-empty value further down the document starts a later page, and the
    /// model has one page geometry rather than a sequence of them.
    /// </para>
    /// <para>
    /// A document whose first block names nothing falls back to the master page
    /// called <c>Standard</c>, which is ODF's own default name for it, and then
    /// to the first one defined. The last of those three is the old behaviour,
    /// kept as the floor rather than as the rule.
    /// </para>
    /// </remarks>
    private static XElement? ResolveMasterPage(XDocument content, OdtStyles styles)
    {
        XElement? masterStyles = styles.MasterStyles;
        if (masterStyles is null)
            return null;

        string? named = FirstBlockMasterPageName(content, styles);
        if (!string.IsNullOrEmpty(named))
        {
            foreach (XElement candidate in masterStyles.Elements(OdtNamespaces.Style + "master-page"))
            {
                if (string.Equals(
                        (string?)candidate.Attribute(OdtNamespaces.Style + "name"),
                        named,
                        StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
        }

        XElement? standard = null;
        XElement? first = null;
        foreach (XElement candidate in masterStyles.Elements(OdtNamespaces.Style + "master-page"))
        {
            first ??= candidate;
            if (standard is null &&
                string.Equals(
                    (string?)candidate.Attribute(OdtNamespaces.Style + "name"),
                    "Standard",
                    StringComparison.Ordinal))
            {
                standard = candidate;
            }
        }

        return standard ?? first;
    }

    /// <summary>
    /// The master page named by the style on the body's first block, or null.
    /// </summary>
    /// <remarks>
    /// Descendants rather than children, because the first block is not always a
    /// child of <c>office:text</c> - a document may open with a
    /// <c>text:section</c>, and one written by a word processor opens with
    /// <c>text:sequence-decls</c>, which is not a block at all. Taking the first
    /// block in document order steps over both without needing a list of the
    /// things that are not content.
    /// </remarks>
    private static string? FirstBlockMasterPageName(XDocument content, OdtStyles styles)
    {
        XElement? body = content.Root
            ?.Element(OdtNamespaces.Office + "body")
            ?.Element(OdtNamespaces.Office + "text");
        if (body is null)
            return null;

        foreach (XElement block in body.Descendants())
        {
            // Text inside a positioned drawing belongs to that object, not to
            // the body's first page. Its paragraph style must not select it.
            if (block.Ancestors().Any(ancestor => ancestor.Name.Namespace == OdtNamespaces.Draw))
                continue;
            if (block.Ancestors(OdtNamespaces.Text + "section")
                .Any(section => (string?)section.Attribute(OdtNamespaces.Text + "display") == "none"))
                continue;
            if (block.Name == OdtNamespaces.Text + "p" || block.Name == OdtNamespaces.Text + "h")
            {
                return styles.MasterPageName(
                    OdtStyles.ParagraphFamily,
                    (string?)block.Attribute(OdtNamespaces.Text + "style-name"));
            }

            if (block.Name == OdtNamespaces.Table + "table")
            {
                return styles.MasterPageName(
                    OdtStyles.TableFamily,
                    (string?)block.Attribute(OdtNamespaces.Table + "style-name"));
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the headers and footers hanging off the master page the document
    /// begins on, and off the one that follows it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ODF keeps them in <c>styles.xml</c> under <c>office:master-styles</c>, one
    /// set per master page, and a document can define several. Which one the body
    /// begins on is <see cref="ResolveMasterPage"/>'s decision, and for a document
    /// with one master page that is the whole answer: its parts are the running
    /// content of every page.
    /// </para>
    /// <para>
    /// A letterhead is not that document. It puts the first page on a master page
    /// of its own carrying the band, and names another in
    /// <c>style:next-style-name</c> carrying the page number - and reading only
    /// the first put the band on every page and the number on none. So the chain
    /// is followed by one link: the master page the body starts on supplies the
    /// <em>first</em> page and the one it names supplies the rest. One link and
    /// not the whole chain, because the model holds three selections rather than a
    /// sequence of pages; a document whose third master page differs again is
    /// past what can be represented, and following further would only choose a
    /// different page to be wrong about.
    /// </para>
    /// <para>
    /// A first page of its own also means a band it does not state is empty
    /// there rather than the default one - the letterhead states a header and no
    /// footer, and LibreOffice draws no footer on its first page. That is what
    /// <see cref="RunningContent.DifferentFirstPage"/> carries.
    /// </para>
    /// </remarks>
    private static RunningContent ReadRunningContent(
        XElement? master,
        OdtStyles styles,
        OdtImageLoader images,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        if (master is null)
            return RunningContent.Empty;

        XElement? next = NextMasterPage(master, styles);
        RunningContent content = RunningContent.Empty.WithDifferentFirstPage(next is not null);

        // The page after the first supplies the default and even bands, and it is
        // read first so that the first page's own parts land on top of it. Its
        // First parts are deliberately not read: it is not the first page.
        if (next is not null)
            content = ReadParts(next, content, first: false, styles, images, limits, diagnostics);

        return ReadParts(master, content, first: next is not null, styles, images, limits, diagnostics);
    }

    /// <summary>
    /// One master page's parts folded into <paramref name="content"/>.
    /// </summary>
    /// <param name="first">
    /// True when this master page is the first page rather than the rest of the
    /// document, which moves its plain <c>style:header</c> and <c>style:footer</c>
    /// into the First selection. Its own <c>-first</c> and <c>-left</c> parts keep
    /// the selections they name either way: ODF lets one master page state all
    /// three, and a document that does is saying something more specific than the
    /// chain is.
    /// </param>
    private static RunningContent ReadParts(
        XElement master,
        RunningContent content,
        bool first,
        OdtStyles styles,
        OdtImageLoader images,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        foreach ((string element, bool isHeader, PageSelection selection) in RunningParts)
        {
            if (first && selection == PageSelection.Even)
                continue;

            XElement? part = master.Element(OdtNamespaces.Style + element);
            if (part is null)
                continue;

            var (paragraphs, shapes) = ReadPart(part, styles, images, limits, diagnostics);
            if (paragraphs.Count == 0 && shapes.Count == 0)
            {
                // An empty part is an answer for the two selections that would
                // otherwise inherit. ODF states a first or even page that carries
                // nothing by writing the element and leaving it empty - which is
                // the other way a document says "no footer on the first page",
                // beside the master-page chain - and skipping it would send the
                // selection back to the default it was written to escape. The
                // default band has nothing to inherit from, so an empty one there
                // is still nothing.
                if (selection == PageSelection.Default)
                    continue;

                paragraphs = [RichTextParagraph.Empty];
            }

            PageSelection target = first && selection == PageSelection.Default
                ? PageSelection.First
                : selection;

            content = isHeader
                ? content.WithHeader(target, paragraphs, shapes)
                : content.WithFooter(target, paragraphs, shapes);
        }

        return content;
    }

    /// <summary>
    /// The master page <paramref name="master"/> hands over to, or null when it
    /// names none, names itself, or names one the document does not define.
    /// </summary>
    /// <remarks>
    /// Naming itself is the case worth guarding rather than the case worth
    /// reporting: it is what a document with one master page and a redundant
    /// <c>style:next-style-name</c> says, it means "every page is this one", and
    /// treating it as a chain would split one master page into a first page and a
    /// rest that are identical - and then claim the first page is different.
    /// </remarks>
    private static XElement? NextMasterPage(XElement master, OdtStyles styles)
    {
        string? name = (string?)master.Attribute(OdtNamespaces.Style + "next-style-name");
        if (string.IsNullOrEmpty(name) ||
            string.Equals(name, (string?)master.Attribute(OdtNamespaces.Style + "name"), StringComparison.Ordinal))
        {
            return null;
        }

        XElement? masterStyles = styles.MasterStyles;
        if (masterStyles is null)
            return null;

        foreach (XElement candidate in masterStyles.Elements(OdtNamespaces.Style + "master-page"))
        {
            if (string.Equals((string?)candidate.Attribute(OdtNamespaces.Style + "name"), name, StringComparison.Ordinal))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// The <c>style:page-layout-properties</c> of the layout
    /// <paramref name="master"/> names, or null when it names none the document
    /// defines.
    /// </summary>
    private static XElement? PageLayoutProperties(XElement? master, OdtStyles styles)
    {
        string? layoutName = (string?)master?.Attribute(OdtNamespaces.Style + "page-layout-name");
        if (string.IsNullOrWhiteSpace(layoutName))
            return null;

        foreach (XElement candidate in styles.PageLayouts)
        {
            if (string.Equals(
                    (string?)candidate.Attribute(OdtNamespaces.Style + "name"),
                    layoutName,
                    StringComparison.Ordinal))
            {
                return candidate.Element(OdtNamespaces.Style + "page-layout-properties");
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the paper the given master page is laid out on.
    /// </summary>
    /// <remarks>
    /// ODF keeps the paper in a style:page-layout and has the master page name it,
    /// so this follows that reference rather than guessing at the first layout -
    /// a document can define several, and only the named one is the page. Which
    /// master page arrives here is <see cref="ResolveMasterPage"/>'s decision.
    /// A layout that leaves no column to write on is dropped and reported. The
    /// text columns the layout divides the page into are read with it; see
    /// <see cref="ReadColumns"/>.
    /// </remarks>
    private static PageGeometry? ReadPageGeometry(
        XElement? master, OdtStyles styles, List<DocumentDiagnostic> diagnostics)
    {
        XElement? properties = PageLayoutProperties(master, styles);
        if (properties is null)
            return null;

        if (!OdtUnits.TryParseLength((string?)properties.Attribute(OdtNamespaces.Fo + "page-width"), out double width) ||
            !OdtUnits.TryParseLength((string?)properties.Attribute(OdtNamespaces.Fo + "page-height"), out double height))
        {
            return null;
        }

        double uniformMargin = Length(properties, "margin");
        double Margin(string side) => OdtUnits.TryParseLength(
            (string?)properties.Attribute(OdtNamespaces.Fo + side), out double value) ? value : uniformMargin;
        double outerTop = Margin("margin-top");
        bool hasHeader = master?.Elements().Any(element =>
            (element.Name == OdtNamespaces.Style + "header" || element.Name == OdtNamespaces.Style + "header-left" ||
             element.Name == OdtNamespaces.Style + "header-first") &&
            (string?)element.Attribute(OdtNamespaces.Style + "display") != "false") == true;
        XElement? header = properties.Parent?.Element(OdtNamespaces.Style + "header-style")
            ?.Element(OdtNamespaces.Style + "header-footer-properties");
        double headerHeight = hasHeader && header is not null
            ? Math.Max(Length(header, "height"), Length(header, "min-height")) : 0;
        var geometry = new PageGeometry(
            width,
            height,
            Margin("margin-left"),
            Margin("margin-right"),
            outerTop + Math.Max(0, headerHeight),
            Margin("margin-bottom"),
            HeaderDistance: hasHeader ? Math.Max(0, outerTop) : 0);

        if (geometry.IsUsable)
            return ReadColumns(properties, geometry, diagnostics);

        diagnostics.Add(DocumentDiagnostic.Warning(
            "odt.page.geometry",
            "An ODT page layout gave a page with no room to write on; the page was not read."));
        return null;
    }

    /// <summary>
    /// ODF's <c>style:columns</c>: the text columns a page layout divides its text
    /// area into.
    /// </summary>
    /// <remarks>
    /// <c>fo:column-count</c> says how many, and <c>fo:column-gap</c> how far
    /// apart when the columns are equal. A producer that lists the columns
    /// states each gap instead, as the end indent of one column and the start
    /// indent of the next, and LibreOffice writes both forms; the listed one is
    /// read when the attribute is absent. Listed columns of different relative
    /// widths are held as equal ones, which keeps every line in the column it was
    /// written in, and reported.
    /// </remarks>
    private static PageGeometry ReadColumns(
        XElement properties, PageGeometry geometry, List<DocumentDiagnostic> diagnostics)
    {
        if (TryReadColumns(properties, diagnostics.Add) is not (int count, double gap))
            return geometry;

        PageGeometry divided = geometry.WithColumns(count, gap);
        if (!divided.HasColumns)
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                "odt.columns.geometry",
                "An ODT page layout's columns left no room to write in a column; the page was read as one column."));
        }

        return divided;
    }

    /// <summary>
    /// The columns a <c>style:columns</c> under <paramref name="properties"/>
    /// states - a page layout's or a section's - or null for one column.
    /// </summary>
    private static (int Count, double Gap)? TryReadColumns(XElement properties, Action<DocumentDiagnostic> report)
    {
        XElement? columns = properties.Element(OdtNamespaces.Style + "columns");
        if (columns is null ||
            !int.TryParse(
                (string?)columns.Attribute(OdtNamespaces.Fo + "column-count"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int count) ||
            count <= 1)
        {
            return null;
        }

        List<XElement> listed = [.. columns.Elements(OdtNamespaces.Style + "column")];
        if (!OdtUnits.TryParseLength((string?)columns.Attribute(OdtNamespaces.Fo + "column-gap"), out double gap))
            gap = listed.Count >= 2 ? Length(listed[0], "end-indent") + Length(listed[1], "start-indent") : 0;

        if (HasUnequalWidths(listed))
        {
            report(DocumentDiagnostic.Info(
                "odt.columns.unequal",
                "ODT columns were listed with unequal widths; they were read as equal columns."));
        }

        return (count, Math.Max(0, gap));
    }

    /// <summary>
    /// True when listed columns differ in <c>style:rel-width</c> by more than a
    /// percent - more than the rounding a producer leaves when it shares a width
    /// out in whole units, as LibreOffice's 10922 against 10925 does.
    /// </summary>
    private static bool HasUnequalWidths(List<XElement> listed)
    {
        var widths = new List<double>();
        foreach (XElement column in listed)
        {
            string? relative = ((string?)column.Attribute(OdtNamespaces.Style + "rel-width"))?.TrimEnd('*');
            if (double.TryParse(relative, NumberStyles.Float, CultureInfo.InvariantCulture, out double width) && width > 0)
                widths.Add(width);
        }

        return widths.Count >= 2 && (widths.Max() - widths.Min()) / widths.Max() > 0.01;
    }

    private static double Length(XElement properties, string name) =>
        OdtUnits.TryParseLength((string?)properties.Attribute(OdtNamespaces.Fo + name), out double points)
            ? points
            : 0;

    /// <summary>
    /// Reads a drawing into a floating shape: its box, how it is painted, and any
    /// text it carries.
    /// </summary>
    /// <remarks>
    /// ODF puts the box on the drawing and the paint in a graphic style it names,
    /// with a gradient named once more beyond that. A drawing with neither paint
    /// nor text is left to the caller, which is what keeps a picture frame going
    /// down the picture path rather than becoming an empty box.
    /// </remarks>
    private static bool ReadShape(XElement drawing, OdtReadContext context, int depth)
    {
        if (drawing.Name == OdtNamespaces.Draw + "line")
            return ReadShapeLine(drawing, context);
        if (!TryReadFrameWidth(drawing, out double width) || width <= 0)
            return false;

        // A text box that states fo:min-height grows to its text from there
        // (ODF 1.3, `fo:min-height` on a text box), whether or not the frame states a height too; one
        // that states neither is sized by its text alone.
        XElement? textBox = drawing.Element(OdtNamespaces.Draw + "text-box");
        bool statedHeight = OdtUnits.TryParseLength(
            (string?)drawing.Attribute(OdtNamespaces.Svg + "height"), out double height);
        bool grows = OdtUnits.TryParseLength(
            (string?)textBox?.Attribute(OdtNamespaces.Fo + "min-height"), out double minHeight);
        if (grows)
            height = statedHeight ? Math.Max(height, minHeight) : minHeight;
        else if (!statedHeight && textBox is not null)
            (grows, height) = (true, 0);

        if (height < 0 || (height == 0 && !grows))
            return false;

        string? styleName = (string?)drawing.Attribute(OdtNamespaces.Draw + "style-name");
        XElement? graphic = context.Styles.GraphicPropertiesFor(styleName);
        (ShapeFill? fill, BColor outline, bool behindText, ShapeWrap wrap, WrapSide wrapSide) =
            ReadShapeStyle(styleName, context.Styles);
        double outlineWidth = OdtUnits.TryParseLength((string?)graphic?.Attribute(OdtNamespaces.Svg + "stroke-width"), out double strokeWidth) ? strokeWidth : 0.75;
        if (drawing.Name == OdtNamespaces.Draw + "frame" && graphic?.Attribute(OdtNamespaces.Fo + "border") is not null)
        {
            TableBorder border = OdtTableReader.ReadBorderEdge(graphic, "border");
            outline = border.Width > 0 ? border.Color : BColor.Empty;
            outlineWidth = border.Width;
        }

        // A frame keeps its text in a draw:text-box; a custom shape carries the
        // paragraphs itself.
        XElement textSource = drawing.Element(OdtNamespaces.Draw + "text-box") ?? drawing;
        RichTextDocument shapeContent = ReadShapeParagraphs(textSource, context, depth);
        IReadOnlyList<RichTextParagraph> paragraphs = shapeContent.ParagraphCount == 1 &&
            shapeContent.Paragraphs[0].Length == 0 && shapeContent.Tables.Count == 0 ? [] : shapeContent.Paragraphs;
        string? frameName = (string?)drawing.Attribute(OdtNamespaces.Draw + "name");
        string? nextFrameName = (string?)textBox?.Attribute(OdtNamespaces.Draw + "chain-next-name");
        bool linked = !string.IsNullOrEmpty(nextFrameName) || (frameName is not null && context.LinkedFrameTargets.Contains(frameName));

        // A valid empty text frame is still a positioned object, not a body
        // paragraph. A nested drawing that this text path did not represent is
        // not evidence of emptiness: leave it to the existing fallback walk.
        bool unrepresentedDrawing = textBox?.Descendants().Any(element => element.Name.Namespace == OdtNamespaces.Draw) == true;
        if (fill is null && paragraphs.Count == 0 && outline.IsEmpty && !linked &&
            (textBox is null || unrepresentedDrawing) && drawing.Element(OdtNamespaces.Draw + "image") is null)
            return false;

        // Image frames must still go through the image admission path.
        if (drawing.Element(OdtNamespaces.Draw + "image") is not null && paragraphs.Count == 0)
            return false;

        // Nothing to grow to, and nothing to draw at no height.
        if (height == 0 && paragraphs.Count == 0 && !linked)
            return false;

        double x = OdtUnits.TryParseLength((string?)drawing.Attribute(OdtNamespaces.Svg + "x"), out double left)
            ? left
            : 0;
        double y = OdtUnits.TryParseLength((string?)drawing.Attribute(OdtNamespaces.Svg + "y"), out double top)
            ? top
            : 0;
        (int anchorPage, x, y, bool fromPage) = PlaceOnPage(drawing, styleName, x, y, width, height, context);

        context.Builder.AddShape(new DocumentShape(
            context.Builder.CurrentParagraphIndex,
            x,
            y,
            width,
            height,
            fill,
            outline,
            paragraphs,
            image: null,
            behindText: behindText,
            wrap: wrap,
            wrapSide: wrapSide,
            zOrder: ZIndex(drawing),
            anchorPage: anchorPage,
            offsetFromPage: fromPage,
            growsToText: grows,
            tables: shapeContent.Tables,
            textPadding: ReadShapePadding(graphic),
            textVerticalAlignment: (string?)graphic?.Attribute(OdtNamespaces.Draw + "textarea-vertical-align") switch
            {
                "middle" => CellVerticalAlignment.Center,
                "bottom" => CellVerticalAlignment.Bottom,
                _ => CellVerticalAlignment.Top,
            },
            growsWidthToText: (string?)graphic?.Attribute(OdtNamespaces.Draw + "auto-grow-width") == "true" ||
                (drawing.Attribute(OdtNamespaces.Svg + "width") is null &&
                textBox?.Attribute(OdtNamespaces.Fo + "min-width") is not null &&
                (string?)graphic?.Attribute(OdtNamespaces.Draw + "auto-grow-width") != "false" &&
                (string?)graphic?.Attribute(OdtNamespaces.Fo + "wrap-option") == "no-wrap"),
            frameName: linked ? frameName : null,
            nextFrameName: nextFrameName,
            geometry: drawing.Name == OdtNamespaces.Draw + "ellipse" ||
                (string?)drawing.Element(OdtNamespaces.Draw + "enhanced-geometry")?.Attribute(OdtNamespaces.Draw + "type") is "ellipse" ? ShapeGeometry.Ellipse : ShapeGeometry.Rectangle,
            outlineWidth: outlineWidth,
            offsetXFromCell: context.TableDepth > 0 &&
                ((string?)graphic?.Attribute(OdtNamespaces.Style + "horizontal-rel") is null or "paragraph" or "paragraph-content"),
            cellHorizontalAlignment: ReadCellHorizontalAlignment(graphic)));
        return true;
    }

    private static bool ReadShapeLine(XElement drawing, OdtReadContext context)
    {
        double Point(string name) => OdtUnits.TryParseLength((string?)drawing.Attribute(OdtNamespaces.Svg + name), out double value) ? value : 0;
        double x1 = Point("x1"), y1 = Point("y1"), x2 = Point("x2"), y2 = Point("y2");
        double width = Math.Abs(x2 - x1), height = Math.Abs(y2 - y1);
        if (width == 0 && height == 0) return false;
        string? name = (string?)drawing.Attribute(OdtNamespaces.Draw + "style-name");
        XElement? properties = context.Styles.GraphicPropertiesFor(name);
        var paint = ReadShapeStyle(name, context.Styles);
        if ((string?)properties?.Attribute(OdtNamespaces.Draw + "stroke") == "none") return true;
        BColor outline = paint.Outline.IsEmpty ? BColor.Black : paint.Outline;
        double thickness = OdtUnits.TryParseLength((string?)properties?.Attribute(OdtNamespaces.Svg + "stroke-width"), out double value) ? value : 0.75;
        (int page, double x, double y, bool fromPage) = PlaceOnPage(drawing, name, Math.Min(x1, x2), Math.Min(y1, y2), width, height, context);
        context.Builder.AddShape(new DocumentShape(context.Builder.CurrentParagraphIndex, x, y, width, height,
            outline: outline, behindText: paint.BehindText, wrap: paint.Wrap, wrapSide: paint.Side,
            zOrder: ZIndex(drawing), anchorPage: page, offsetFromPage: fromPage,
            geometry: (x2 - x1) * (y2 - y1) < 0 ? ShapeGeometry.LineAscending : ShapeGeometry.Line,
            outlineWidth: thickness,
            offsetXFromCell: context.TableDepth > 0 &&
                ((string?)properties?.Attribute(OdtNamespaces.Style + "horizontal-rel") is null or "paragraph")));
        return true;
    }

    private static TableCellPadding ReadShapePadding(XElement? properties)
    {
        double Length(string name, double fallback = 0) => OdtUnits.TryParseLength(
            (string?)properties?.Attribute(OdtNamespaces.Fo + name), out double value) ? Math.Clamp(value, 0, 20000) : fallback;
        double all = Length("padding");
        return new TableCellPadding(Length("padding-left", all), Length("padding-top", all),
            Length("padding-right", all), Length("padding-bottom", all));
    }

    /// <summary>
    /// A drawing's width: <c>svg:width</c>, or for a frame that states none, the
    /// <c>fo:min-width</c> of its text box.
    /// </summary>
    /// <remarks>
    /// LibreOffice writes a text box that grows to its text that way, and the
    /// width is what its lines wrap to. Without it the frame was read as body
    /// text, wherever it was anchored. A box sized only by <c>fo:min-height</c>
    /// was read that way too, because its height is what its text needs and a
    /// box of the minimum would have clipped it; it is now a shape that grows
    /// to its text (<see cref="DocumentShape.GrowsToText"/>), and the layout
    /// measures it. A book LibreOffice saved with three hundred such boxes on
    /// the pages they name, <c>tdf146955.odt</c>, had them all poured into its
    /// first sixteen pages of body.
    /// </remarks>
    private static bool TryReadFrameWidth(XElement drawing, out double width) =>
        OdtUnits.TryParseLength((string?)drawing.Attribute(OdtNamespaces.Svg + "width"), out width) ||
        OdtUnits.TryParseLength(
            (string?)drawing.Element(OdtNamespaces.Draw + "text-box")?.Attribute(OdtNamespaces.Fo + "min-width"),
            out width);

    /// <summary>
    /// The page a drawing is anchored to by number, and its position in the
    /// model's terms: x from the text column's left edge, y from the page's top
    /// edge. A paragraph's drawing whose style measures it down the page
    /// (<c>style:vertical-rel</c> of <c>page</c> or <c>page-content</c>) gets its y
    /// from the page's top edge too, and no number: it is on whichever page its
    /// paragraph is. A drawing anchored any other way comes back as it went in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ODF's <c>text:anchor-page-number</c> names the physical page a
    /// page-anchored drawing stands on. Without a number the drawing stands on
    /// whatever page its position in the text reaches, which is the paragraph
    /// anchor the model already has.
    /// </para>
    /// <para>
    /// The graphic style says which area the position is measured in
    /// (<c>style:horizontal-rel</c>, <c>style:vertical-rel</c>: the whole page,
    /// or its content area) and how (<c>style:horizontal-pos</c>,
    /// <c>style:vertical-pos</c>: from the stated offset, or at an edge or the
    /// middle of the area). A style that says nothing measures from the page,
    /// which is the area a page anchor names. Without a page to measure against
    /// the margins are taken as zero.
    /// </para>
    /// </remarks>
    private static (int Page, double X, double Y, bool FromPage) PlaceOnPage(
        XElement drawing, string? styleName, double x, double y, double width, double height, OdtReadContext context)
    {
        XElement? properties = context.Styles.GraphicPropertiesFor(styleName);

        string? anchorType = (string?)drawing.Attribute(OdtNamespaces.Text + "anchor-type");
        if (!string.Equals(anchorType, "page", StringComparison.Ordinal) ||
            !int.TryParse(
                (string?)drawing.Attribute(OdtNamespaces.Text + "anchor-page-number"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int page) ||
            page < 1)
        {
            // A paragraph/character anchor moves with its paragraph, but its
            // horizontal coordinates can still refer to the whole page.
            string? horizontalRel = (string?)properties?.Attribute(OdtNamespaces.Style + "horizontal-rel");
            if (anchorType is "paragraph" or "char" && horizontalRel is "page" or "page-content")
            {
                double horizontalLeft = horizontalRel == "page" ? -(context.ActivePage?.MarginLeft ?? 0) : 0;
                double horizontalWidth = horizontalRel == "page" ? context.ActivePage?.Width ?? 0 : context.ActivePage?.ContentWidth ?? 0;
                x = (string?)properties?.Attribute(OdtNamespaces.Style + "horizontal-pos") switch
                {
                    "left" or "inside" => horizontalLeft,
                    "center" => horizontalLeft + (horizontalWidth - width) / 2,
                    "right" or "outside" => horizontalLeft + horizontalWidth - width,
                    _ => horizontalLeft + x,
                };
            }
            string? verticalRel = (string?)properties?.Attribute(OdtNamespaces.Style + "vertical-rel");
            if (anchorType is "paragraph" or "char" && verticalRel is "page" or "page-content")
            {
                double frameTop = verticalRel == "page-content" ? context.ActivePage?.MarginTop ?? 0 : 0;
                double frameHeight = verticalRel == "page-content"
                    ? context.ActivePage?.ContentHeight ?? 0
                    : context.ActivePage?.Height ?? 0;
                double fromTop = (string?)properties?.Attribute(OdtNamespaces.Style + "vertical-pos") switch
                {
                    "top" => frameTop,
                    "middle" => frameTop + ((frameHeight - height) / 2),
                    "bottom" => frameTop + frameHeight - height,
                    _ => frameTop + y,
                };
                return (0, x, fromTop, true);
            }

            return (0, x, y, false);
        }

        PageGeometry? geometry = context.ActivePage;
        double pageWidth = geometry?.Width ?? 0;
        double pageHeight = geometry?.Height ?? 0;
        double marginLeft = geometry?.MarginLeft ?? 0;
        double marginTop = geometry?.MarginTop ?? 0;

        // The area each offset is measured in, as its left (or top) edge from the
        // page's, and its extent.
        bool contentWide = string.Equals(
            (string?)properties?.Attribute(OdtNamespaces.Style + "horizontal-rel"), "page-content", StringComparison.Ordinal);
        bool contentHigh = string.Equals(
            (string?)properties?.Attribute(OdtNamespaces.Style + "vertical-rel"), "page-content", StringComparison.Ordinal);
        double areaLeft = contentWide ? marginLeft : 0;
        double areaWidth = contentWide ? geometry?.ContentWidth ?? 0 : pageWidth;
        double areaTop = contentHigh ? marginTop : 0;
        double areaHeight = contentHigh ? geometry?.ContentHeight ?? 0 : pageHeight;

        double left = (string?)properties?.Attribute(OdtNamespaces.Style + "horizontal-pos") switch
        {
            "left" or "inside" => areaLeft,
            "center" => areaLeft + ((areaWidth - width) / 2),
            "right" or "outside" => areaLeft + areaWidth - width,
            _ => areaLeft + x,
        };
        double top = (string?)properties?.Attribute(OdtNamespaces.Style + "vertical-pos") switch
        {
            "top" => areaTop,
            "middle" => areaTop + ((areaHeight - height) / 2),
            "bottom" => areaTop + areaHeight - height,
            _ => areaTop + y,
        };

        // Writer brings page-anchored frames back from the right and bottom
        // edges. A negative vertical offset still permits a picture to bleed
        // above the page. Paragraph anchors and drawing objects keep their offsets.
        if (geometry is { IsUsable: true } && drawing.Name == OdtNamespaces.Draw + "frame")
        {
            left = Math.Clamp(left, 0, Math.Max(0, pageWidth - width));
            top = Math.Min(top, Math.Max(0, pageHeight - height));
        }

        return (page, left - marginLeft, top, false);
    }

    /// <summary>
    /// ODF's <c>draw:z-index</c>: which of two overlapping drawings the reader
    /// sees, with the higher number on top.
    /// </summary>
    /// <remarks>
    /// One order for the whole document and not one per master page, which is
    /// what a letterhead needs: the stripe is anchored in the first page's header
    /// and the logo box in the body, and the two indices are the only statement
    /// of which wins where they overlap. A drawing that states none reads as
    /// zero, which leaves the order it was read in - the answer this reader gave
    /// before the attribute was read at all.
    /// </remarks>
    private static int ZIndex(XElement drawing) =>
        int.TryParse(
            (string?)drawing.Attribute(OdtNamespaces.Draw + "z-index"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int index)
            ? index
            : 0;

    /// <summary>
    /// The fill, outline, and stacking a graphic style states, resolving a named
    /// gradient.
    /// </summary>
    /// <remarks>
    /// A drawing that names no style, or names one the document does not define,
    /// is unpainted and behind the text - the same answer an absent
    /// <c>style:run-through</c> gets, and for the same reason.
    /// </remarks>
    private static (ShapeFill? Fill, BColor Outline, bool BehindText, ShapeWrap Wrap, WrapSide Side)
        ReadShapeStyle(string? styleName, OdtStyles styles)
    {
        XElement? properties = styles.GraphicPropertiesFor(styleName);
        if (properties is null)
        {
            return (null, BColor.Empty, true, ShapeWrap.TopAndBottom, WrapSide.Largest);
        }

        ShapeFill? fill = ReadDrawFill(properties, styles);

        BColor outline = BColor.Empty;
        string? stroke = (string?)properties.Attribute(OdtNamespaces.Draw + "stroke");
        if (!string.Equals(stroke, "none", StringComparison.Ordinal))
            if (!TryColor((string?)properties.Attribute(OdtNamespaces.Svg + "stroke-color"), out outline) && stroke is not null)
                outline = BColor.Black;

        (ShapeWrap wrap, WrapSide side) = ReadWrap(properties);
        return (fill, outline, BehindText(properties), wrap, side);
    }

    /// <summary>
    /// ODF's <c>style:run-through</c>: whether the drawing is painted under the
    /// text (<c>background</c>) or over it (<c>foreground</c>). It is all of the
    /// stacking the model keeps, and the difference between a letterhead's stripe
    /// and a stamp over the letter.
    /// </summary>
    /// <remarks>
    /// A style that states nothing reads as behind. That is not ODF's own default,
    /// which puts a drawing in front; it is the answer this reader has always
    /// given, and the safe half of the choice - a box wrongly in front hides the
    /// text under it, while one wrongly behind leaves the document readable. The
    /// writer states the attribute either way, so a round trip does not lean on it.
    /// </remarks>
    /// <summary>
    /// ODF's <c>style:wrap</c>, as the three modes layout distinguishes.
    /// </summary>
    /// <remarks>
    /// The two values that read backwards are the whole reason this is its own
    /// method. <c>run-through</c> is the one where text ignores the shape and
    /// runs through it - our no-wrap - while <c>none</c> means no text alongside
    /// it at all, which pushes the text below and is top-and-bottom. Reading
    /// <c>none</c> as no wrap would put the text straight back through the
    /// picture the document asked it to clear.
    /// </remarks>
    private static (ShapeWrap Wrap, WrapSide Side) ReadWrap(XElement graphicProperties) =>
        (string?)graphicProperties.Attribute(OdtNamespaces.Style + "wrap") switch
        {
            null or "none" => (ShapeWrap.TopAndBottom, WrapSide.Largest),
            "left" => (ShapeWrap.Square, WrapSide.Left),
            "right" => (ShapeWrap.Square, WrapSide.Right),
            "parallel" or "dynamic" or "biggest" => (ShapeWrap.Square, WrapSide.Largest),
            _ => (ShapeWrap.None, WrapSide.Largest),
        };

    private static bool BehindText(XElement graphicProperties) =>
        (string?)graphicProperties.Attribute(OdtNamespaces.Style + "run-through") switch
        {
            "foreground" => false,
            _ => true,
        };

    private static bool TryColor(string? value, out BColor color) =>
        OdtUnits.TryParseColor(value, out color);

    /// <summary>
    /// The fill a set of properties states with <c>draw:fill</c> (ODF 1.3
    /// §20.106): a colour, a named gradient, or null for none or a fill this
    /// does not read.
    /// </summary>
    private static ShapeFill? ReadDrawFill(XElement properties, OdtStyles styles)
    {
        ShapeFill? fill = ReadDrawFillCore(properties, styles);
        if (fill is null) return null;
        double opacity = ReadFillPercent(properties, "opacity", 1);
        if (fill.Image is not null && opacity < 1)
            styles.Report("odt.fill.opacity", "odt.fill.opacity", "Bitmap fill opacity is not applied by this painter; the bitmap is retained at full opacity.");
        BColor Alpha(BColor color) => new(color.R, color.G, color.B, (byte)Math.Round(color.A * opacity));
        return fill with { Start = Alpha(fill.Start), End = Alpha(fill.End),
            Hatch = fill.Hatch is HatchFill hatch ? hatch with { Color = Alpha(hatch.Color) } : null,
            ContentOnly = (string?)properties.Attribute(OdtNamespaces.Draw + "background-size") == "border" };
    }

    private static double ReadFillPercent(XElement element, string name, double fallback)
    {
        string? value = (string?)element.Attribute(OdtNamespaces.Draw + name);
        return value is not null && value.EndsWith('%') && double.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Float,
            CultureInfo.InvariantCulture, out double percent) && double.IsFinite(percent) ? Math.Clamp(percent / 100, 0, 1) : fallback;
    }

    private static ShapeFill? ReadDrawFillCore(XElement properties, OdtStyles styles)
    {
        string? kind = (string?)properties.Attribute(OdtNamespaces.Draw + "fill");
        if (string.Equals(kind, "gradient", StringComparison.Ordinal))
        {
            string? gradientName = (string?)properties.Attribute(OdtNamespaces.Draw + "fill-gradient-name");
            return !string.IsNullOrWhiteSpace(gradientName) &&
                styles.Gradients.TryGetValue(gradientName, out XElement? gradient)
                    ? ReadGradient(gradient, styles)
                    : null;
        }

        if (kind == "hatch")
        {
            string? name = (string?)properties.Attribute(OdtNamespaces.Draw + "fill-hatch-name");
            if (name is not null && styles.FillDefinitions.TryGetValue(name, out XElement? hatch) &&
                TryColor((string?)hatch.Attribute(OdtNamespaces.Draw + "color"), out BColor color))
            {
                _ = OdtUnits.TryParseLength((string?)hatch.Attribute(OdtNamespaces.Draw + "distance"), out double distance);
                _ = OdtUnits.TryParseAngle((string?)hatch.Attribute(OdtNamespaces.Draw + "rotation"), out double angle);
                BColor background = (string?)properties.Attribute(OdtNamespaces.Draw + "fill-hatch-solid") == "true" &&
                    TryColor((string?)properties.Attribute(OdtNamespaces.Draw + "fill-color"), out BColor hatchBackground) ? hatchBackground : BColor.Empty;
                return ShapeFill.Solid(background) with { Hatch = new HatchFill(color, distance, -angle,
                    (string?)hatch.Attribute(OdtNamespaces.Draw + "style") switch { "double" => 2, "triple" => 3, _ => 1 }) };
            }
        }
        if (kind == "bitmap")
        {
            string? name = (string?)properties.Attribute(OdtNamespaces.Draw + "fill-image-name");
            XElement? definition = name is not null && styles.FillDefinitions.TryGetValue(name, out XElement? found) ? found :
                properties.Element(OdtNamespaces.Style + "background-image");
            if (definition is not null && styles.ReadFillImage?.Invoke(definition) is InlineImage image)
            {
                _ = OdtUnits.TryParseLength((string?)properties.Attribute(OdtNamespaces.Draw + "fill-image-width"), out double width);
                _ = OdtUnits.TryParseLength((string?)properties.Attribute(OdtNamespaces.Draw + "fill-image-height"), out double height);
                string position = (string?)properties.Attribute(OdtNamespaces.Draw + "fill-image-ref-point") ?? "center";
                var repeat = (string?)properties.Attribute(OdtNamespaces.Style + "repeat") switch
                    { "stretch" => ImageFillRepeat.Stretch, "no-repeat" => ImageFillRepeat.Single, _ => ImageFillRepeat.Tile };
                return ShapeFill.Solid(BColor.Empty) with { Image = new ImageFill(image, repeat, width, height,
                    position.Contains("left", StringComparison.Ordinal) ? 0 : position.Contains("right", StringComparison.Ordinal) ? 1 : 0.5,
                    position.Contains("top", StringComparison.Ordinal) ? 0 : position.Contains("bottom", StringComparison.Ordinal) ? 1 : 0.5) };
            }
        }
        if (kind is "bitmap" or "hatch")
        {
            BColor fallback = BColor.Empty;
            bool hasFallback = kind == "bitmap" &&
                TryColor((string?)properties.Attribute(OdtNamespaces.Draw + "fill-color"), out fallback);
            styles.Report(
                "odt.fill:" + kind,
                "odt.fill",
                "A " + kind + " fill could not be resolved" +
                (hasFallback ? "; its declared fallback colour was drawn." : " and was not drawn."));
            return hasFallback ? ShapeFill.Solid(fallback) : null;
        }

        return string.Equals(kind, "solid", StringComparison.Ordinal) &&
            TryColor((string?)properties.Attribute(OdtNamespaces.Draw + "fill-color"), out BColor solid)
                ? ShapeFill.Solid(solid)
                : null;
    }

    /// <summary>A <c>draw:gradient</c> as the model's two-colour fill.</summary>
    /// <remarks>
    /// <para>
    /// An <c>axial</c> gradient puts its start colour along the middle and its
    /// end colour at both edges, which is what LibreOffice draws: a page it
    /// exported with a start of lime and an end of fuchsia renders lime across
    /// its centre and fuchsia in its corners. The model's axial fill names the
    /// edges first, so the two are exchanged.
    /// </para>
    /// <para>
    /// Centred styles retain their geometry, origin and border. Endpoint
    /// intensities are folded into the two colours; the shared and PDF painters
    /// sample the same bounded geometry instead of flattening it to one colour.
    /// </para>
    /// </remarks>
    private static ShapeFill? ReadGradient(XElement gradient, OdtStyles styles)
    {
        if (!TryColor((string?)gradient.Attribute(OdtNamespaces.Draw + "start-color"), out BColor start) ||
            !TryColor((string?)gradient.Attribute(OdtNamespaces.Draw + "end-color"), out BColor end))
        {
            return null;
        }

        _ = OdtUnits.TryParseAngle((string?)gradient.Attribute(OdtNamespaces.Draw + "angle"), out double odfDegrees);
        BColor Intensity(BColor color, string name)
        {
            double intensity = ReadFillPercent(gradient, name, 1);
            return new BColor((byte)Math.Round(color.R * intensity), (byte)Math.Round(color.G * intensity), (byte)Math.Round(color.B * intensity), color.A);
        }
        start = Intensity(start, "start-intensity");
        end = Intensity(end, "end-intensity");
        double angle = OdtGradientAngle.ToModel(odfDegrees);
        string style = (string?)gradient.Attribute(OdtNamespaces.Draw + "style") ?? "linear";
        if (style is not ("linear" or "axial" or "radial" or "ellipsoid" or "square" or "rectangular"))
        {
            styles.Report("odt.gradient:" + style, "odt.gradient", "An unknown gradient style was approximated by its start colour.");
            return ShapeFill.Solid(start);
        }
        ShapeFill result = style == "axial"
            ? new ShapeFill(end, start, angle, Axial: true)
            : new ShapeFill(start, end, angle);
        return result with { Geometry = style switch { "radial" => GradientGeometry.Radial, "ellipsoid" => GradientGeometry.Ellipsoid,
            "square" => GradientGeometry.Square, "rectangular" => GradientGeometry.Rectangular, _ => GradientGeometry.Linear },
            CenterX = ReadFillPercent(gradient, "cx", 0.5), CenterY = ReadFillPercent(gradient, "cy", 0.5), Border = ReadFillPercent(gradient, "border", 0) };
    }

    /// <summary>
    /// What the master page paints its page with, or null for nothing.
    /// </summary>
    /// <remarks>
    /// ODF states it in two places. The newer is a <c>drawing-page</c> style the
    /// master page names with <c>draw:style-name</c> (ODF 1.3 §16.10); the older
    /// is the page layout's own properties, with <c>draw:fill</c> or with
    /// <c>fo:background-color</c> (§20.183). LibreOffice writes all three, with
    /// the background colour flattened to the average of a gradient, so they are
    /// read in that order and the first that says anything is the answer. A
    /// <c>draw:fill</c> of <c>none</c> says there is no fill, and stops the search.
    /// </remarks>
    private static ShapeFill? ReadPageBackground(XElement? master, XElement? layoutProperties, OdtStyles styles)
    {
        string? drawingStyle = (string?)master?.Attribute(OdtNamespaces.Draw + "style-name");
        if (!string.IsNullOrWhiteSpace(drawingStyle) &&
            styles.DrawingPageProperties.TryGetValue(drawingStyle, out XElement? drawingPage) &&
            drawingPage.Attribute(OdtNamespaces.Draw + "fill") is not null)
        {
            return ReadDrawFill(drawingPage, styles);
        }

        if (layoutProperties is null)
            return null;

        if (layoutProperties.Attribute(OdtNamespaces.Draw + "fill") is not null)
            return ReadDrawFill(layoutProperties, styles);

        if (layoutProperties.Element(OdtNamespaces.Style + "background-image") is XElement legacy &&
            (!string.IsNullOrWhiteSpace((string?)legacy.Attribute(OdtNamespaces.XLink + "href")) ||
             legacy.Element(OdtNamespaces.Office + "binary-data") is not null))
        {
            var properties = new XElement(layoutProperties);
            properties.SetAttributeValue(OdtNamespaces.Draw + "fill", "bitmap");
            properties.SetAttributeValue(OdtNamespaces.Draw + "background-size", "border");
            properties.SetAttributeValue(OdtNamespaces.Style + "repeat", (string?)legacy.Attribute(OdtNamespaces.Style + "repeat") ?? "repeat");
            string position = (string?)legacy.Attribute(OdtNamespaces.Style + "position") ?? "center";
            properties.SetAttributeValue(OdtNamespaces.Draw + "fill-image-ref-point", position.Replace(' ', '-'));
            return ReadDrawFill(properties, styles);
        }

        return TryColor((string?)layoutProperties.Attribute(OdtNamespaces.Fo + "background-color"), out BColor color) &&
            !color.IsEmpty && color.A > 0
                // The legacy fo:background-color paints the page's content
                // rectangle, leaving its margins clear. Drawing-page fills
                // above use their explicit draw:background-size instead.
                ? ShapeFill.Solid(color) with { ContentOnly = true }
                : null;
    }

    /// <summary>A shape's own paragraphs, read with the same walker the body uses.</summary>
    private static RichTextDocument ReadShapeParagraphs(
        XElement textBox,
        OdtReadContext context,
        int depth)
    {
        var builder = new OdtDocumentBuilder(context.Builder.Limits, context.Builder.Diagnostics);
        var nested = new OdtReadContext(context.Styles, context.Images, builder);
        // A custom shape holds its geometry alongside its text. The geometry is
        // the shape's form, not content, and walking it as a block reports an
        // unsupported element for something the reader is deliberately not using.
        ReadBlockContent(
            textBox.Elements().Where(child => child.Name.Namespace == OdtNamespaces.Text || child.Name.Namespace == OdtNamespaces.Table),
            nested,
            list: null,
            depth + 1);
        nested.DropPageBreak();

        return builder.Build();
    }

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

    /// <remarks>
    /// The shapes come back beside the paragraphs rather than being dropped with
    /// the rest of the built document, which is what used to happen: a master
    /// page whose header is a coloured stripe and nothing else arrived as no
    /// header at all. They are placed against the page, so a header's
    /// <c>svg:y</c> is read from the top of the page - the same reading the DOCX
    /// side gives a header's own band.
    /// </remarks>
    private static (IReadOnlyList<RichTextParagraph> Paragraphs, IReadOnlyList<DocumentShape> Shapes) ReadPart(
        XElement part,
        OdtStyles styles,
        OdtImageLoader images,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        var builder = new OdtDocumentBuilder(limits, diagnostics);
        var context = new OdtReadContext(styles, images, builder);
        ReadBlockContent(part.Elements(), context, list: null, depth: 0);
        context.DropPageBreak();

        RichTextDocument built = builder.Build();
        IReadOnlyList<RichTextParagraph> paragraphs = built.Paragraphs;
        if (paragraphs.Count == 1 && paragraphs[0].Length == 0)
            paragraphs = [];

        return (paragraphs, built.Shapes);
    }

    /// <summary>
    /// The list a paragraph is inside: which kind its level draws as, and how
    /// deep that level is. Null outside a list.
    /// </summary>
    private sealed record OdtListContext(
        ListKind Kind,
        int Level,
        string? StyleName,
        ListDefinition? Definition = null,
        OdtItemStart? Start = null,
        OdtItemLabel? Label = null,
        bool Continues = false);

    private sealed class OdtItemLabel
    {
        private bool _first = true;
        public bool HasParagraph => !_first;
        public bool Take()
        {
            bool first = _first;
            _first = false;
            return first;
        }
    }

    /// <summary>
    /// A list item's <c>text:start-value</c>, which its first paragraph takes and
    /// no other paragraph of the item does.
    /// </summary>
    private sealed class OdtItemStart(int value)
    {
        private int? _value = value;

        public int? Take()
        {
            int? value = _value;
            _value = null;
            return value;
        }
    }

    /// <summary>
    /// Walks ODF text block content. Paragraphs are the only shape
    /// <see cref="RichTextDocument"/> can represent, so lists, tables, sections,
    /// and indexes are flattened into their paragraphs in document order rather
    /// than dropped. Anything genuinely not understood raises a diagnostic
    /// instead of vanishing.
    /// </summary>
    private static void ReadBlockContent(
        IEnumerable<XElement> elements,
        OdtReadContext context,
        OdtListContext? list,
        int depth)
    {
        OdtDocumentBuilder builder = context.Builder;
        if (depth > builder.Limits.MaxGroupDepth)
        {
            builder.AddDiagnosticOnce(
                "odt.limit.depth",
                "ODT block nesting exceeded MaxGroupDepth; the deepest content was skipped.");
            return;
        }

        foreach (XElement element in elements)
        {
            XName name = element.Name;

            if (name == OdtNamespaces.Text + "p" || name == OdtNamespaces.Text + "h")
            {
                ReadParagraph(element, context, list, depth);
                continue;
            }

            if (name == OdtNamespaces.Text + "numbered-paragraph")
            {
                string? styleName = (string?)element.Attribute(OdtNamespaces.Text + "style-name");
                int level = int.TryParse((string?)element.Attribute(OdtNamespaces.Text + "level"), out int stated)
                    ? Math.Clamp(stated, 1, ListDefinition.MaxLevels) : 1;
                ListDefinition? definition = context.NumberedParagraphList(styleName,
                    (string?)element.Attribute(OdtNamespaces.Text + "list-id"));
                OdtItemStart? start = int.TryParse((string?)element.Attribute(OdtNamespaces.Text + "start-value"), out int value)
                    ? new OdtItemStart(value) : null;
                var numbered = new OdtListContext(context.Styles.KindForList(styleName, level), level,
                    styleName, definition, start, new OdtItemLabel());
                ReadBlockContent(element.Elements().Where(child => child.Name != OdtNamespaces.Text + "number"),
                    context, numbered, depth + 1);
                continue;
            }

            if (name == OdtNamespaces.Text + "list")
            {
                ReadList(element, context, list, depth + 1);
                continue;
            }

            if (name == OdtNamespaces.Table + "table")
            {
                OdtBreak precedingBreak = context.TakeBreak();
                if (context.SelectPageStyle?.Invoke("table", (string?)element.Attribute(OdtNamespaces.Table + "style-name"),
                    builder.CurrentParagraphIndex) == true)
                    precedingBreak = OdtBreak.Page;
                OdtTableReader.Read(element, context, depth + 1,
                    (children, childDepth) => ReadBlockContent(children, context, list: null, childDepth), precedingBreak);
                continue;
            }

            // A section is a named region of ordinary body content; an index is a
            // generated one whose text:index-body holds real paragraphs. Both are
            // walked so the words inside them survive.
            // Column/background styles and unconditional visibility are kept
            // over the paragraph range; hidden content remains in the model.
            if (name == OdtNamespaces.Text + "section")
            {
                int sectionStart = builder.CurrentParagraphIndex;
                bool hidden = (string?)element.Attribute(OdtNamespaces.Text + "display") == "none";
                OdtBreak pending = hidden ? context.TakeBreak() : OdtBreak.None;
                if (hidden) context.HiddenSectionDepth++;
                try
                {
                    ReadBlockContent(element.Elements(), context, list, depth + 1);
                }
                finally
                {
                    if (hidden)
                    {
                        context.HiddenSectionDepth--;
                        context.HoldBreak(pending);
                    }
                }
                string? sectionStyle = (string?)element.Attribute(OdtNamespaces.Text + "style-name");
                if (sectionStyle is not null &&
                    context.Styles.SectionProperties.TryGetValue(sectionStyle, out XElement? sectionProperties))
                {
                    (int count, double gap) = TryReadColumns(sectionProperties, builder.AddDiagnosticOnce) ?? (1, 0);
                    TryColor((string?)sectionProperties.Attribute(OdtNamespaces.Fo + "background-color"), out BColor background);
                    bool dontBalance = string.Equals((string?)sectionProperties.Attribute(OdtNamespaces.Text + "dont-balance-text-columns"), "true", StringComparison.OrdinalIgnoreCase);

                    PageGeometry? sectionPage = null;
                    double uniformMargin = Length(sectionProperties, "margin");
                    double ml = OdtUnits.TryParseLength((string?)sectionProperties.Attribute(OdtNamespaces.Fo + "margin-left"), out double leftVal) ? leftVal : uniformMargin;
                    double mr = OdtUnits.TryParseLength((string?)sectionProperties.Attribute(OdtNamespaces.Fo + "margin-right"), out double rightVal) ? rightVal : uniformMargin;
                    PageGeometry basePage = context.ActivePage ?? PageGeometry.A4;
                    if (ml > 0 || mr > 0)
                    {
                        sectionPage = basePage with
                        {
                            MarginLeft = basePage.MarginLeft + ml,
                            MarginRight = basePage.MarginRight + mr,
                        };
                    }


                    if (count > 1 || (!background.IsEmpty && background.A > 0) || sectionPage is not null || dontBalance || hidden)
                    {
                        builder.AddSection(new DocumentSection(
                            sectionStart, builder.CurrentParagraphIndex - sectionStart, count, gap,
                            page: sectionPage,
                            background: background,
                            hidden: hidden,
                            dontBalanceColumns: dontBalance));
                    }
                }
                else if (hidden)
                    builder.AddSection(new DocumentSection(sectionStart, builder.CurrentParagraphIndex - sectionStart, 1, 0, hidden: true));

                continue;
            }

            if (IsIndexContainer(name))
            {
                // The source describes how an application regenerates the
                // index, not displayed content. Read the cached body and keep
                // reporting genuinely unknown children instead of treating the
                // generation instructions as lost paragraphs.
                XName source = OdtNamespaces.Text + (name.LocalName + "-source");
                ReadBlockContent(element.Elements().Where(child => child.Name != source), context, list: null, depth + 1);
                continue;
            }

            if (name == OdtNamespaces.Text + "index-body" ||
                name == OdtNamespaces.Text + "index-title")
            {
                ReadBlockContent(element.Elements(), context, list: null, depth + 1);
                continue;
            }

            // A drawing that carries paint or text of its own is a floating shape
            // beside the body, not body content.
            if (name == OdtNamespaces.Draw + "custom-shape" || name == OdtNamespaces.Draw + "line" ||
                name == OdtNamespaces.Draw + "ellipse" || name == OdtNamespaces.Draw + "rect")
            {
                if (ReadShape(element, context, depth))
                    continue;
            }

            // A frame at block level is a floating table, a text box, an image floating on the page,
            // or an embedded object. Only the table or text box holds body content.
            if (name == OdtNamespaces.Draw + "frame")
            {
                if (ReadFloatingTable(element, context, depth))
                    continue;

                XElement? textBox = element.Element(OdtNamespaces.Draw + "text-box");
                if (textBox is not null)
                {
                    if (ReadShape(element, context, depth))
                        continue;

                    builder.AddDiagnosticOnce(
                        "odt.frame.textbox",
                        "An ODT text box was read as body content; its frame position is not represented.");
                    ReadBlockContent(textBox.Elements(), context, list: null, depth + 1);
                }
                else if (!ReadFloatingPicture(element, context))
                {
                    builder.AddDiagnosticOnce(
                        "odt.frame.block",
                        "A page-anchored ODT frame held no body text and was skipped.");
                }

                continue;
            }

            if (name == OdtNamespaces.Office + "annotation" ||
                name == OdtNamespaces.Office + "annotation-end")
            {
                builder.AddDiagnosticOnce("odt.annotation", "ODT comment content is not part of the body and was skipped.");
                continue;
            }

            // The regions were read before the body; the marks in the body say
            // where each one is.
            if (name == OdtNamespaces.Text + "tracked-changes")
                continue;

            if (ReadChangeMark(element, context, depth))
                continue;

            if (IsIgnorableBlock(name))
                continue;

            builder.AddUnsupportedBlock(name);
        }
    }

    /// <summary>
    /// Reads a <c>text:list</c>. ODF nests a sub-list inside the
    /// <c>text:list-item</c> it belongs to, so the level is the nesting depth and
    /// a nested list inherits the outer list style when it names none.
    /// </summary>
    private static void ReadList(
        XElement list,
        OdtReadContext context,
        OdtListContext? outer,
        int depth)
    {
        OdtDocumentBuilder builder = context.Builder;
        if (depth > builder.Limits.MaxGroupDepth)
        {
            builder.AddDiagnosticOnce(
                "odt.limit.depth",
                "ODT block nesting exceeded MaxGroupDepth; the deepest content was skipped.");
            return;
        }

        foreach ((XElement child, OdtListContext item) in ListItems(list, context, outer))
            ReadBlockContent(child.Elements(), context, item, depth + 1);
    }

    /// <summary>
    /// The items of a <c>text:list</c>, each with the list context its content
    /// is read in: its level, the kind of label that level draws, the list it
    /// counts in and its own start value. A list header is the unnumbered
    /// lead-in paragraph of a list; it keeps the level's indent but carries no
    /// bullet or number.
    /// </summary>
    private static IEnumerable<(XElement Item, OdtListContext List)> ListItems(
        XElement list,
        OdtReadContext context,
        OdtListContext? outer)
    {
        string? styleName = (string?)list.Attribute(OdtNamespaces.Text + "style-name");
        if (string.IsNullOrEmpty(styleName))
            styleName = outer?.StyleName;

        int level = (outer?.Level ?? 0) + 1;
        ListKind kind = context.Styles.KindForList(styleName, level);

        // A sub-list is a level of the list it is nested in and counts in it;
        // only a sub-list in a style of its own is a list of its own.
        ListDefinition? definition = outer?.Definition is not null && styleName == outer.StyleName
            ? outer.Definition
            : context.StartList(
                context.Styles.DefinitionForList(styleName),
                styleName,
                (string?)list.Attribute(OdtNamespaces.Xml + "id"),
                string.Equals((string?)list.Attribute(OdtNamespaces.Text + "continue-numbering"), "true", StringComparison.Ordinal),
                (string?)list.Attribute(OdtNamespaces.Text + "continue-list"));
        bool shared = outer?.Definition is not null && ReferenceEquals(definition, outer.Definition);
        bool continues = string.Equals((string?)list.Attribute(OdtNamespaces.Text + "continue-numbering"), "true", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty((string?)list.Attribute(OdtNamespaces.Text + "continue-list")) ||
            (shared && outer!.Continues && outer.Label?.HasParagraph == false);
        var inner = new OdtListContext(kind, level, styleName, definition, Continues: continues);
        // A real new sub-list restarts its own level, while structural wrappers
        // propagate a containing list's continuation without inventing an item.
        OdtItemStart? nestedStart = shared && !continues
            ? new OdtItemStart(definition!.LevelAt(level - 1).Start) : null;

        foreach (XElement child in list.Elements())
        {
            if (child.Name == OdtNamespaces.Text + "list-item")
            {
                OdtListContext item = inner with { Label = new OdtItemLabel(), Start = nestedStart };
                if (int.TryParse((string?)child.Attribute(OdtNamespaces.Text + "start-value"),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out int start))
                {
                    nestedStart?.Take();
                    item = item with { Start = new OdtItemStart(start) };
                }

                yield return (child, item);
                continue;
            }

            if (child.Name == OdtNamespaces.Text + "list-header")
                yield return (child, inner with { Kind = ListKind.None, Definition = null });
        }
    }

    private static bool IsIndexContainer(XName name)
    {
        if (name.Namespace != OdtNamespaces.Text)
            return false;

        return name.LocalName is
            "table-of-content" or
            "illustration-index" or
            "table-index" or
            "object-index" or
            "user-index" or
            "alphabetical-index" or
            "bibliography";
    }

    /// <summary>Block-level elements that carry no body text and are skipped silently.</summary>
    private static bool IsIgnorableBlock(XName name)
    {
        if (name.Namespace == OdtNamespaces.Table)
        {
            return name.LocalName is
                "table-column" or "table-columns" or "table-column-group" or
                "table-header-columns" or "calculation-settings" or
                "named-expressions" or "shapes";
        }

        if (name.Namespace == OdtNamespaces.Office)
            return name.LocalName is "forms" or "event-listeners";

        if (name.Namespace != OdtNamespaces.Text)
            return false;

        return name.LocalName is
            "sequence-decls" or "variable-decls" or "user-field-decls" or
            "dde-connection-decls" or "alphabetical-index-auto-mark-file" or
            "soft-page-break" or "index-title" or "index-title-template" or
            "index-body-template" or "bookmark" or "bookmark-start" or "bookmark-end" or
            "change" or "change-start" or "change-end";
    }

    /// <summary>True when a body child could contribute text, used to tell an empty document from a dropped one.</summary>
    private static bool IsContentBlock(XElement element) => !IsIgnorableBlock(element.Name);

    private static void ReadParagraph(
        XElement paragraph,
        OdtReadContext context,
        OdtListContext? list,
        int depth)
    {
        string? styleName = (string?)paragraph.Attribute(OdtNamespaces.Text + "style-name");

        ParagraphStyle paragraphStyle = ParagraphStyle.Default;
        string? breakAfter = null;
        foreach (XElement properties in context.Styles.ParagraphProperties(styleName))
        {
            paragraphStyle = ApplyParagraphProperties(properties, paragraphStyle, context.Builder);

            // fo:break-after is resolved here rather than inside
            // ApplyParagraphProperties because it is not a property of this
            // paragraph at all: it is the next paragraph's page break, stated
            // from the far end. The chain arrives root-first, so the last
            // declaration seen is the most specific one, which is the same rule
            // every other attribute in it follows.
            string? after = (string?)properties.Attribute(OdtNamespaces.Fo + "break-after");
            if (after is not null)
                breakAfter = after;
        }

        if (context.SelectPageStyle?.Invoke("paragraph", styleName, context.Builder.CurrentParagraphIndex) == true)
            paragraphStyle = paragraphStyle with { PageBreakBefore = true, ColumnBreakBefore = false };

        // A break handed over by the paragraph above is taken after this
        // paragraph's own properties have been applied, so it wins over an
        // fo:break-before of auto. That is the right way round: the two
        // attributes describe the same gap and either of them asking for a page
        // gets one, so a paragraph saying nothing stronger than "I do not start a
        // page" cannot talk the paragraph above it out of ending one.
        switch (context.TakeBreak())
        {
            case OdtBreak.Page:
                paragraphStyle = paragraphStyle with { PageBreakBefore = true, ColumnBreakBefore = false };
                break;
            case OdtBreak.Column when !paragraphStyle.PageBreakBefore:
                paragraphStyle = paragraphStyle with { ColumnBreakBefore = true };
                break;
        }

        context.HoldBreak(breakAfter switch
        {
            "page" => OdtBreak.Page,
            "column" => OdtBreak.Column,
            _ => OdtBreak.None,
        });

        // List-level alignment wins over the paragraph's margin. Nesting supplies
        // the fallback only when the list states no explicit alignment.
        if (list is not null)
        {
            (float? left, float? first) = context.Styles.IndentsForList(list.StyleName, list.Level);
            bool labelled = list.Label?.Take() != false && list.Kind != ListKind.None;
            paragraphStyle = paragraphStyle with
            {
                ListKind = labelled ? list.Kind : ListKind.None,
                IndentLevel = Math.Max(list.Level, list.Kind == ListKind.None ? 0 : 1),
                LeftIndent = left,
                FirstLineIndent = labelled ? first : left.HasValue ? 0 : null,
            };

            if (paragraphStyle.ListKind != ListKind.None && list.Definition is ListDefinition definition)
            {
                var reference = new ListReference(definition, list.Level - 1, list.Start?.Take());
                paragraphStyle = paragraphStyle with { List = reference, ListKind = reference.Kind };
            }
        }
        else if (paragraph.Name == OdtNamespaces.Text + "h" && OutlineReference(paragraph, context) is ListReference outline)
        {
            paragraphStyle = paragraphStyle with { List = outline, ListKind = outline.Kind };
        }

        InlineStyle inherited = InlineStyle.Default;
        foreach (XElement properties in context.Styles.TextPropertiesForParagraph(styleName))
            inherited = ApplyTextProperties(properties, inherited, context.Styles, context.Builder);

        // ODF has no paragraph mark of its own: what an empty paragraph is as
        // tall as is its paragraph style's text properties, the formatting every
        // run in it starts from.
        context.Builder.StartParagraph(paragraphStyle, inherited);
        ReadInlineContent(paragraph, context, inherited, depth + 1);
        context.Builder.FinishParagraph();
    }

    /// <summary>
    /// The place a heading outside any list has in the document's outline
    /// numbering, or null when the document numbers no outline.
    /// </summary>
    /// <remarks>
    /// <c>text:outline-level</c> is the level, from one (ODF 1.3 §5.1.2); a
    /// heading that is a list header (<c>text:is-list-header</c>) carries no
    /// number, and <c>text:restart-numbering</c> starts the count again, at
    /// <c>text:start-value</c> if it gives one. A heading inside a
    /// <c>text:list</c> is numbered by that list instead.
    /// </remarks>
    private static ListReference? OutlineReference(XElement heading, OdtReadContext context)
    {
        if (context.Styles.OutlineDefinition is not ListDefinition outline ||
            string.Equals((string?)heading.Attribute(OdtNamespaces.Text + "is-list-header"), "true", StringComparison.Ordinal))
        {
            return null;
        }

        int level = int.TryParse((string?)heading.Attribute(OdtNamespaces.Text + "outline-level"),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out int stated) && stated > 0
            ? stated
            : 1;
        if (level > ListDefinition.MaxLevels)
            return null;

        int? restart = null;
        if (string.Equals((string?)heading.Attribute(OdtNamespaces.Text + "restart-numbering"), "true", StringComparison.Ordinal))
        {
            restart = int.TryParse((string?)heading.Attribute(OdtNamespaces.Text + "start-value"),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int start)
                ? start
                : outline.LevelAt(level - 1).Start;
        }

        return new ListReference(outline, level - 1, restart);
    }

    /// <summary>
    /// Reads the inline content of a paragraph or span. Text nodes go through the
    /// ODF white-space rule (a run of white space is one space, and a space at
    /// the paragraph edge is nothing); everything significant is an element.
    /// </summary>
    private static void ReadInlineContent(
        XElement parent,
        OdtReadContext context,
        InlineStyle style,
        int depth)
    {
        OdtDocumentBuilder builder = context.Builder;
        if (depth > builder.Limits.MaxGroupDepth)
        {
            builder.AddDiagnosticOnce(
                "odt.limit.depth",
                "ODT inline nesting exceeded MaxGroupDepth; the deepest content was skipped.");
            return;
        }

        foreach (XNode node in parent.Nodes())
        {
            if (node is XText text)
            {
                builder.AppendCollapsed(text.Value, style);
                continue;
            }

            if (node is not XElement element)
                continue;

            XName name = element.Name;

            if (name == OdtNamespaces.Text + "span")
            {
                InlineStyle spanStyle = style;
                string? spanStyleName = (string?)element.Attribute(OdtNamespaces.Text + "style-name");
                foreach (XElement properties in context.Styles.TextPropertiesForSpan(spanStyleName))
                    spanStyle = ApplyTextProperties(properties, spanStyle, context.Styles, builder);

                ReadInlineContent(element, context, spanStyle, depth + 1);
                continue;
            }

            if (name == OdtNamespaces.Text + "a" || name == OdtNamespaces.Draw + "a")
            {
                ReadInlineContent(element, context, ApplyAnchorStyle(element, context, style), depth + 1);
                continue;
            }

            if (name == OdtNamespaces.Text + "s")
            {
                builder.AppendLiteral(new string(' ', ReadSpaceCount(element, builder)), style);
                continue;
            }

            if (name == OdtNamespaces.Text + "tab")
            {
                builder.AppendLiteral("\t", style);
                continue;
            }

            if (name == OdtNamespaces.Text + "line-break")
            {
                builder.AppendLiteral(((char)0x2028).ToString(), style);
                continue;
            }

            if (name == OdtNamespaces.Draw + "custom-shape" || name == OdtNamespaces.Draw + "line" ||
                name == OdtNamespaces.Draw + "ellipse" || name == OdtNamespaces.Draw + "rect")
            {
                ReadShape(element, context, depth: 0);
                continue;
            }

            if (name == OdtNamespaces.Draw + "frame")
            {
                if (ReadFloatingTable(element, context, depth: 0))
                    continue;

                // A frame holding a text box is a shape; one holding an image is
                // a picture. Try the shape first and fall through when it is not.
                if (element.Element(OdtNamespaces.Draw + "text-box") is not null &&
                    ReadShape(element, context, depth: 0))
                {
                    continue;
                }

                ReadPicture(element, context, style);
                continue;
            }

            if (name == OdtNamespaces.Text + "note")
            {
                ReadNote(element, context, style, depth);
                continue;
            }

            if (name == OdtNamespaces.Office + "annotation" ||
                name == OdtNamespaces.Office + "annotation-end")
            {
                builder.AddDiagnosticOnce("odt.annotation", "ODT comment content is not part of the body and was skipped.");
                continue;
            }

            if (name == OdtNamespaces.Text + "ruby")
            {
                XElement? rubyBase = element.Element(OdtNamespaces.Text + "ruby-base");
                if (rubyBase is not null)
                    ReadInlineContent(rubyBase, context, style, depth + 1);
                continue;
            }

            if (ReadChangeMark(element, context, depth))
                continue;

            if (IsIgnorableInline(name))
                continue;

            // Every remaining text: element is a field, a mark, or a wrapper, and
            // a field carries its last computed value as its text content. Walking
            // into it is how the date in a letterhead survives; refusing to would
            // lose the words a reader can plainly see.
            if (element.HasElements || !string.IsNullOrEmpty(element.Value))
                ReadInlineContent(element, context, style, depth + 1);
        }
    }

    /// <summary>
    /// Acts on a tracked change's mark, and says whether <paramref name="element"/>
    /// was one: <c>text:change-start</c> and <c>text:change-end</c> open and close
    /// an insertion or a change of formatting, and <c>text:change</c> puts a
    /// deletion's text back where it was.
    /// </summary>
    private static bool ReadChangeMark(XElement element, OdtReadContext context, int depth)
    {
        if (element.Name.Namespace != OdtNamespaces.Text)
            return false;

        string? id = (string?)element.Attribute(OdtNamespaces.Text + "change-id");
        switch (element.Name.LocalName)
        {
            case "change-start":
                context.OpenChange(id);
                return true;
            case "change-end":
                context.CloseChange(id);
                return true;
            case "change":
                if (id is not null && context.Regions.TryGetValue(id, out OdtChangedRegion? region) && region.Revision.IsDeletion)
                    ReadDeletion(region, context, depth);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Puts a deletion's text back at its <c>text:change</c>, marked as deleted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mark is a point inside the paragraph being read, and the deletion
    /// holds paragraphs: ODF joins the first of them to the text before the
    /// mark and the last to the text after it, and keeps any between as
    /// paragraphs of their own, so a deleted paragraph break comes back as a
    /// break - with its mark deleted, which is what joins the two again when the
    /// change is hidden. LibreOffice writes a deleted whole paragraph as that
    /// paragraph followed by an empty one, the empty one being the half that
    /// joins the text after the mark.
    /// </para>
    /// <para>
    /// The paragraph the mark is in keeps its style for the text before the
    /// mark and the text after it. Where nothing came before the mark, the
    /// first deleted paragraph keeps its own style instead, and the paragraphs
    /// between keep theirs. A table or a list in the deletion is read as block
    /// content between them.
    /// </para>
    /// </remarks>
    private static void ReadDeletion(OdtChangedRegion region, OdtReadContext context, int depth)
    {
        OdtDocumentBuilder builder = context.Builder;
        if (region.Deleted.Count == 0 || depth > builder.Limits.MaxGroupDepth)
            return;

        TextRevision? outer = builder.Revision;
        builder.Revision = region.Revision;

        // A break the paragraph before handed over belongs to the text after
        // the mark, not to a paragraph the deletion took away, and one a
        // deleted paragraph asked for went with it.
        OdtBreak heldBreak = context.TakeBreak();

        OdtDocumentBuilder.OpenParagraph original = builder.Suspend();
        OdtDocumentBuilder.OpenParagraph? open = original;
        bool originalWritten = false;

        List<(XElement Element, OdtListContext? List)> items = [.. DeletedItems(region.Deleted, context, outer: null, depth)];
        for (int i = 0; i < items.Count; i++)
        {
            (XElement item, OdtListContext? list) = items[i];
            bool isLast = i == items.Count - 1;

            if (item.Name == OdtNamespaces.Text + "p" || item.Name == OdtNamespaces.Text + "h")
            {
                (ParagraphStyle style, InlineStyle inherited) = DeletedParagraphStyle(item, list, context);
                if (open is not null)
                {
                    builder.Resume(open);
                    if (ReferenceEquals(open, original) && original.IsEmpty)
                        builder.Restyle(style, inherited);
                }
                else
                {
                    builder.StartParagraph(style, inherited);
                }

                ReadInlineContent(item, context, inherited, depth + 1);
                if (isLast)
                {
                    open = builder.Suspend();
                    continue;
                }

                originalWritten |= ReferenceEquals(open, original) && !original.IsEmpty;
                builder.FinishParagraph();
                open = null;
                continue;
            }

            // A table or a list is block content: the paragraph before it ends
            // there, unless nothing was read into it.
            if (open is not null)
            {
                builder.Resume(open);
                if (!(ReferenceEquals(open, original) && original.IsEmpty))
                {
                    originalWritten |= ReferenceEquals(open, original);
                    builder.FinishParagraph();
                }

                open = null;
            }

            ReadBlockContent([item], context, list, depth + 1);
        }

        builder.Revision = outer;
        context.TakeBreak();
        context.HoldBreak(heldBreak);

        // The text after the mark joins the last deleted paragraph, in the
        // style of the paragraph the mark is in. A page break it opened with
        // has been taken by the text before the mark if that was written.
        ParagraphStyle continued = originalWritten
            ? original.Style with { PageBreakBefore = false, ColumnBreakBefore = false }
            : original.Style;
        if (open is not null)
        {
            builder.Resume(open);
            builder.Restyle(continued, original.MarkStyle);
        }
        else
        {
            builder.StartParagraph(continued, original.MarkStyle);
        }
    }

    /// <summary>
    /// A deletion's content as the paragraphs and blocks it is read as, in
    /// order: a list's paragraphs are paragraphs of the deletion, each with its
    /// item's list context, so the first and last of them join the text around
    /// the mark as any other paragraph does, as LibreOffice joins them.
    /// </summary>
    private static IEnumerable<(XElement Element, OdtListContext? List)> DeletedItems(
        IEnumerable<XElement> elements,
        OdtReadContext context,
        OdtListContext? outer,
        int depth)
    {
        if (depth > context.Builder.Limits.MaxGroupDepth)
            yield break;

        foreach (XElement element in elements)
        {
            if (element.Name != OdtNamespaces.Text + "list")
            {
                yield return (element, outer);
                continue;
            }

            foreach ((XElement item, OdtListContext list) in ListItems(element, context, outer))
            {
                foreach ((XElement Element, OdtListContext? List) nested in DeletedItems(item.Elements(), context, list, depth + 1))
                    yield return nested;
            }
        }
    }

    /// <summary>
    /// A deleted paragraph's own paragraph style, with the list it is an item
    /// of, and the formatting its runs start from. Its breaks are not taken: it
    /// is read where the deletion's mark is, not where it once stood.
    /// </summary>
    private static (ParagraphStyle Style, InlineStyle Inherited) DeletedParagraphStyle(
        XElement paragraph,
        OdtListContext? list,
        OdtReadContext context)
    {
        string? styleName = (string?)paragraph.Attribute(OdtNamespaces.Text + "style-name");
        ParagraphStyle style = ParagraphStyle.Default;
        foreach (XElement properties in context.Styles.ParagraphProperties(styleName))
            style = ApplyParagraphProperties(properties, style, context.Builder);

        if (list is not null)
        {
            style = style with
            {
                ListKind = list.Label?.Take() == false ? ListKind.None : list.Kind,
                IndentLevel = Math.Max(list.Level, list.Kind == ListKind.None ? 0 : 1),
                LeftIndent = null,
                FirstLineIndent = null,
            };

            if (style.ListKind != ListKind.None && list.Definition is ListDefinition definition)
            {
                var reference = new ListReference(definition, list.Level - 1, list.Start?.Take());
                style = style with { List = reference, ListKind = reference.Kind };
            }
        }
        else if (paragraph.Name == OdtNamespaces.Text + "h" && OutlineReference(paragraph, context) is ListReference outline)
        {
            style = style with { List = outline, ListKind = outline.Kind };
        }

        InlineStyle inherited = InlineStyle.Default;
        foreach (XElement properties in context.Styles.TextPropertiesForParagraph(styleName))
            inherited = ApplyTextProperties(properties, inherited, context.Styles, context.Builder);

        return (style with { PageBreakBefore = false, ColumnBreakBefore = false }, inherited);
    }

    /// <summary>
    /// Reads a <c>text:note</c> into a citation: one placeholder in the
    /// paragraph, holding the note's own paragraphs.
    /// </summary>
    /// <remarks>
    /// The citation's number is not read. LibreOffice writes the number it
    /// last drew into <c>text:note-citation</c>, and the model numbers notes in
    /// the order they are cited, which gives the same number for every note
    /// that has one. A <c>text:label</c> is a mark the author typed instead,
    /// and is kept. A note inside a note is not valid ODF; one found there is
    /// read as the note's text and nothing more.
    /// </remarks>
    private static void ReadNote(XElement note, OdtReadContext context, InlineStyle style, int depth)
    {
        if (context.InNote)
        {
            context.Builder.AddDiagnosticOnce(
                "odt.note.nested",
                "An ODT note inside a note was read as plain text of the outer note.");
            return;
        }

        NoteKind kind = string.Equals(
            (string?)note.Attribute(OdtNamespaces.Text + "note-class"), "endnote", StringComparison.Ordinal)
            ? NoteKind.Endnote
            : NoteKind.Footnote;
        string? label = (string?)note.Element(OdtNamespaces.Text + "note-citation")?.Attribute(OdtNamespaces.Text + "label");

        var builder = new OdtDocumentBuilder(context.Builder.Limits, context.Builder.Diagnostics)
        {
            Revision = context.Builder.Revision,
        };
        var nested = new OdtReadContext(context.Styles, context.Images, builder) { InNote = true, Regions = context.Regions };
        XElement? body = note.Element(OdtNamespaces.Text + "note-body");
        if (body is not null)
            ReadBlockContent(body.Elements(), nested, list: null, depth + 1);
        nested.DropPageBreak();

        RichTextDocument built = builder.Build();
        if (built.Tables.Count > 0 || built.Shapes.Count > 0)
        {
            context.Builder.AddDiagnosticOnce(
                "odt.note.content",
                "A table or shape in an ODT note was read as its text; a note holds paragraphs only.");
        }

        var documentNote = new DocumentNote(kind, built.Paragraphs, label?.Trim());
        context.Builder.AppendLiteral(DocumentNote.PlaceholderText, style with { Note = documentNote, Image = null });
    }

    /// <summary>
    /// How the document numbers its notes, from the
    /// <c>text:notes-configuration</c> of each class in <c>office:styles</c>.
    /// </summary>
    /// <remarks>
    /// LibreOffice writes <c>text:start-value</c> counting from zero - the
    /// default first note is 1 and is written <c>0</c> - and reads it that way.
    /// </remarks>
    private static NoteNumbering ReadNoteNumbering(OdtStyles styles)
    {
        NoteNumbering numbering = NoteNumbering.Default;
        foreach (XElement configuration in styles.NotesConfigurations)
        {
            string? numFormat = (string?)configuration.Attribute(OdtNamespaces.Style + "num-format");
            int? start = int.TryParse(
                (string?)configuration.Attribute(OdtNamespaces.Text + "start-value"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value) && value >= 0 && value < 100000
                ? value + 1
                : null;

            if (string.Equals((string?)configuration.Attribute(OdtNamespaces.Text + "note-class"), "endnote", StringComparison.Ordinal))
            {
                numbering = numbering with
                {
                    EndnoteFormat = numFormat is null ? numbering.EndnoteFormat : OdtStyles.NumberFormat(numFormat),
                    EndnoteStart = start ?? numbering.EndnoteStart,
                };
            }
            else
            {
                string? startNumberingAt = (string?)configuration.Attribute(OdtNamespaces.Text + "start-numbering-at");
                NoteNumberingRestart restart = startNumberingAt switch
                {
                    "page" => NoteNumberingRestart.EachPage,
                    "chapter" => NoteNumberingRestart.EachSection,
                    _ => NoteNumberingRestart.Continuous,
                };
                string? footPos = (string?)configuration.Attribute(OdtNamespaces.Text + "footnotes-position");
                FootnotePosition position = footPos switch
                {
                    "document" => FootnotePosition.DocumentEnd,
                    _ => FootnotePosition.PageBottom,
                };
                numbering = numbering with
                {
                    FootnoteFormat = numFormat is null ? numbering.FootnoteFormat : OdtStyles.NumberFormat(numFormat),
                    FootnoteStart = start ?? numbering.FootnoteStart,
                    RaisedLabels = IsRaised(styles, (string?)configuration.Attribute(OdtNamespaces.Text + "citation-style-name")),
                    FootnoteRestart = restart,
                    FootnotePosition = position,
                };
            }
        }

        return numbering;
    }

    /// <summary>
    /// Whether a text style raises its text: a <c>style:text-position</c> of
    /// <c>super</c> or a positive percentage. That is how ODF states the
    /// character style of the label at the head of a note.
    /// </summary>
    private static bool IsRaised(OdtStyles styles, string? styleName)
    {
        if (string.IsNullOrEmpty(styleName))
            return false;

        foreach (XElement properties in styles.TextPropertiesForSpan(styleName).Reverse())
        {
            string? position = ((string?)properties.Attribute(OdtNamespaces.Style + "text-position"))?.Trim();
            if (string.IsNullOrEmpty(position))
                continue;

            string first = position.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            return first == "super" ||
                (first.EndsWith('%') &&
                 double.TryParse(first[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double percent) &&
                 percent > 0);
        }

        return false;
    }

    private static bool IsIgnorableInline(XName name)
    {
        // draw:a is not listed: a clickable picture wraps its frame in one, and
        // skipping it would lose the picture rather than the link.
        if (name.Namespace == OdtNamespaces.Draw)
            return name.LocalName is "g" or "custom-shape" or "rect" or "line" or "polyline";

        if (name.Namespace != OdtNamespaces.Text)
            return true;

        return name.LocalName is
            "soft-page-break" or
            "bookmark" or "bookmark-start" or "bookmark-end" or
            "reference-mark" or "reference-mark-start" or "reference-mark-end" or
            "toc-mark" or "toc-mark-start" or "toc-mark-end" or
            "alphabetical-index-mark" or
            "alphabetical-index-mark-start" or "alphabetical-index-mark-end" or
            "user-index-mark" or "user-index-mark-start" or "user-index-mark-end" or
            "change" or "change-start" or "change-end" or
            "sequence-decls" or "note-citation";
    }

    /// <summary>
    /// The number of spaces a <c>text:s</c> stands for. The count is clamped: a
    /// hostile <c>text:c</c> would otherwise allocate a run of arbitrary length
    /// from three bytes of markup.
    /// </summary>
    private static int ReadSpaceCount(XElement element, OdtDocumentBuilder builder)
    {
        string? value = (string?)element.Attribute(OdtNamespaces.Text + "c");
        if (value is null)
            return 1;

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count <= 0)
            return 1;

        if (count > builder.Limits.MaxRunLength)
        {
            builder.AddDiagnosticOnce(
                "odt.limit.spaces",
                "A text:s run exceeded MaxRunLength and was truncated.");
            return builder.Limits.MaxRunLength;
        }

        return count;
    }

    /// <summary>
    /// Appends a picture as one <see cref="InlineImage.Placeholder"/> character
    /// carrying the image on its style. The run's own character formatting is
    /// kept on it, so a picture inside a hyperlink stays inside that link.
    /// </summary>
    private static bool ReadFloatingTable(XElement frame, OdtReadContext context, int depth)
    {
        XElement? tableElement = frame.Element(OdtNamespaces.Table + "table");
        if (tableElement is null)
            return false;

        string? styleName = (string?)frame.Attribute(OdtNamespaces.Draw + "style-name");
        XElement? graphic = context.Styles.GraphicPropertiesFor(styleName);
        string? anchorType = (string?)frame.Attribute(OdtNamespaces.Text + "anchor-type");
        string? hRel = (string?)graphic?.Attribute(OdtNamespaces.Style + "horizontal-rel");
        string? vRel = (string?)graphic?.Attribute(OdtNamespaces.Style + "vertical-rel");

        TableAnchor hAnchor = hRel switch
        {
            "page" => TableAnchor.Page,
            "page-content" => TableAnchor.Margin,
            _ => anchorType == "page" ? TableAnchor.Page : TableAnchor.Text
        };
        TableAnchor vAnchor = vRel switch
        {
            "page" => TableAnchor.Page,
            "page-content" => TableAnchor.Margin,
            _ => anchorType == "page" ? TableAnchor.Page : TableAnchor.Text
        };

        string? hPos = (string?)graphic?.Attribute(OdtNamespaces.Style + "horizontal-pos");
        string? vPos = (string?)graphic?.Attribute(OdtNamespaces.Style + "vertical-pos");

        TableAlignment hAlign = hPos switch
        {
            "left" => TableAlignment.Start,
            "center" => TableAlignment.Center,
            "right" => TableAlignment.End,
            "inside" => TableAlignment.Inside,
            "outside" => TableAlignment.Outside,
            _ => TableAlignment.None
        };
        TableAlignment vAlign = vPos switch
        {
            "top" => TableAlignment.Start,
            "middle" => TableAlignment.Center,
            "bottom" => TableAlignment.End,
            "inside" => TableAlignment.Inside,
            "outside" => TableAlignment.Outside,
            _ => TableAlignment.None
        };

        double x = OdtUnits.TryParseLength((string?)frame.Attribute(OdtNamespaces.Svg + "x"), out double left) ? left : 0;
        double y = OdtUnits.TryParseLength((string?)frame.Attribute(OdtNamespaces.Svg + "y"), out double top) ? top : 0;

        double leftFromText = OdtUnits.TryParseLength((string?)graphic?.Attribute(OdtNamespaces.Fo + "margin-left"), out double ml) ? ml : 0;
        double rightFromText = OdtUnits.TryParseLength((string?)graphic?.Attribute(OdtNamespaces.Fo + "margin-right"), out double mr) ? mr : 0;
        double topFromText = OdtUnits.TryParseLength((string?)graphic?.Attribute(OdtNamespaces.Fo + "margin-top"), out double mt) ? mt : 0;
        double bottomFromText = OdtUnits.TryParseLength((string?)graphic?.Attribute(OdtNamespaces.Fo + "margin-bottom"), out double mb) ? mb : 0;

        string? wrap = (string?)graphic?.Attribute(OdtNamespaces.Style + "wrap");
        bool allowOverlap = wrap != "none";

        var position = new TablePosition(
            hAnchor,
            x,
            vAnchor,
            y,
            hAlign,
            vAlign,
            leftFromText,
            rightFromText,
            topFromText,
            bottomFromText,
            allowOverlap,
            ZIndex(frame));

        OdtBreak precedingBreak = context.TakeBreak();
        OdtTableReader.Read(tableElement, context, depth + 1,
            (children, childDepth) => ReadBlockContent(children, context, list: null, childDepth),
            precedingBreak,
            position: position);

        return true;
    }

    private static void ReadPicture(XElement frame, OdtReadContext context, InlineStyle style)
    {
        if (!IsAnchoredInText(frame))
        {
            context.Builder.AddDiagnosticOnce(
                "odt.image.anchored",
                "A floating ODT picture was anchored to its paragraph; text wraps " +
                "around the box it states rather than around the picture's outline.");

            if (ReadFloatingPicture(frame, context))
                return;
        }

        InlineImage? image = context.Images.Read(frame, context.Builder);
        if (image is null)
            return;

        context.Builder.AppendLiteral(InlineImage.PlaceholderText, style with { Image = image });
    }

    /// <summary>As-char occupies a line character; char floats beside its anchor.
    /// Missing anchor-type keeps the legacy inline fallback.</summary>
    private static bool IsAnchoredInText(XElement frame)
    {
        string? anchor = (string?)frame.Attribute(OdtNamespaces.Text + "anchor-type");
        return anchor is null || anchor.Equals("as-char", StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads a frame that stands beside the text as a floating picture, the way
    /// <see cref="ReadShape"/> reads one that carries paint or words.
    /// </summary>
    /// <remarks>
    /// The box is read first and the picture only after it: a frame with no box
    /// has nothing to float at, and the caller then places the picture in the
    /// text - so nothing is loaded, or counted, twice over.
    /// </remarks>
    private static bool ReadFloatingPicture(XElement frame, OdtReadContext context)
    {
        if (!OdtUnits.TryParseLength((string?)frame.Attribute(OdtNamespaces.Svg + "width"), out double width) ||
            !OdtUnits.TryParseLength((string?)frame.Attribute(OdtNamespaces.Svg + "height"), out double height) ||
            width <= 0 || height <= 0)
        {
            return false;
        }

        InlineImage? image = context.Images.Read(frame, context.Builder);
        if (image is null)
            return false;

        string? styleName = (string?)frame.Attribute(OdtNamespaces.Draw + "style-name");
        (ShapeFill? fill, BColor outline, bool behindText, ShapeWrap wrap, WrapSide wrapSide) =
            ReadShapeStyle(styleName, context.Styles);
        XElement? graphic = context.Styles.GraphicPropertiesFor(styleName);
        double outlineWidth = OdtUnits.TryParseLength((string?)graphic?.Attribute(OdtNamespaces.Svg + "stroke-width"), out double lineWidth) ? lineWidth : 0.75;
        if (graphic?.Attribute(OdtNamespaces.Fo + "border") is not null)
        {
            TableBorder border = OdtTableReader.ReadBorderEdge(graphic, "border");
            outline = border.Width > 0 ? border.Color : BColor.Empty;
            outlineWidth = border.Width;
        }

        double x = OdtUnits.TryParseLength((string?)frame.Attribute(OdtNamespaces.Svg + "x"), out double left)
            ? left
            : 0;
        double y = OdtUnits.TryParseLength((string?)frame.Attribute(OdtNamespaces.Svg + "y"), out double top)
            ? top
            : 0;
        (int anchorPage, x, y, bool fromPage) = PlaceOnPage(frame, styleName, x, y, width, height, context);

        context.Builder.AddShape(new DocumentShape(
            context.Builder.CurrentParagraphIndex,
            x,
            y,
            width,
            height,
            fill,
            outline,
            paragraphs: null,
            image: image,
            behindText: behindText,
            wrap: wrap,
            wrapSide: wrapSide,
            zOrder: ZIndex(frame),
            anchorPage: anchorPage,
            offsetFromPage: fromPage,
            outlineWidth: outlineWidth,
            offsetXFromCell: context.TableDepth > 0 &&
                ((string?)graphic?.Attribute(OdtNamespaces.Style + "horizontal-rel") is null or "paragraph" or "paragraph-content"),
            cellHorizontalAlignment: ReadCellHorizontalAlignment(graphic)));
        return true;
    }

    private static TextAlignment? ReadCellHorizontalAlignment(XElement? properties) =>
        (string?)properties?.Attribute(OdtNamespaces.Style + "horizontal-pos") switch
        {
            "left" => TextAlignment.Left,
            "center" => TextAlignment.Center,
            "right" => TextAlignment.Right,
            _ => null,
        };

    /// <summary>
    /// Resolves a <c>text:a</c> into a link style: the anchor's own character
    /// style, then its target under the shared URI policy.
    /// </summary>
    private static InlineStyle ApplyAnchorStyle(XElement anchor, OdtReadContext context, InlineStyle style)
    {
        string? styleName = (string?)anchor.Attribute(OdtNamespaces.Text + "style-name");
        foreach (XElement properties in context.Styles.TextPropertiesForSpan(styleName))
            style = ApplyTextProperties(properties, style, context.Styles, context.Builder);

        string? href = (string?)anchor.Attribute(OdtNamespaces.XLink + "href");
        if (string.IsNullOrWhiteSpace(href))
            return style;

        href = href.Trim();
        if (!DocumentLinkTarget.IsAllowed(href))
        {
            context.Builder.AddDiagnosticOnce("odt.link", "A hyperlink with a disallowed or relative target was dropped.");
            return style;
        }

        return style with { LinkHref = href };
    }

    /// <summary>
    /// Applies one <c>style:paragraph-properties</c> over an inherited style.
    /// Called once per link in the style chain, so only the attributes actually
    /// present override what came before.
    /// </summary>
    private static ParagraphStyle ApplyParagraphProperties(
        XElement? properties,
        ParagraphStyle style,
        OdtDocumentBuilder builder)
    {
        if (properties is null)
            return style;

        if (properties.Element(OdtNamespaces.Style + "tab-stops") is XElement tabs)
        {
            var stops = new List<ParagraphTabStop>();
            foreach (XElement tab in tabs.Elements(OdtNamespaces.Style + "tab-stop").Take(256))
            {
                if (!OdtUnits.TryParseLength((string?)tab.Attribute(OdtNamespaces.Style + "position"), out double position) || position < 0)
                    continue;
                string? decimalChar = (string?)tab.Attribute(OdtNamespaces.Style + "char");
                stops.Add(new ParagraphTabStop(position, (string?)tab.Attribute(OdtNamespaces.Style + "type") switch
                    { "center" => TabAlignment.Center, "right" => TabAlignment.Right, "char" => TabAlignment.Decimal, _ => TabAlignment.Left },
                    (string?)tab.Attribute(OdtNamespaces.Style + "leader-style") == "none"
                        ? null : (string?)tab.Attribute(OdtNamespaces.Style + "leader-text"),
                    string.IsNullOrEmpty(decimalChar) ? '.' : decimalChar[0]));
            }
            style = style with { TabStops = new ParagraphTabStops(stops) };
        }

        string? alignment = (string?)properties.Attribute(OdtNamespaces.Fo + "text-align");
        switch (alignment)
        {
            case "center":
                style = style with { Alignment = TextAlignment.Center };
                break;
            case "end":
            case "right":
                style = style with { Alignment = TextAlignment.Right };
                break;
            case "start":
            case "left":
                style = style with { Alignment = TextAlignment.Left };
                break;
            case "justify":
                style = style with { Alignment = TextAlignment.Justify };
                break;
        }

        // ODF gives fo:break-before three values and only one of them is a page.
        // A column break moves to the next column of the same page, so reading it
        // as a page break would add a page the document never asked for; it is
        // the model's column break instead, which a layout takes as a page break
        // only where the page has one column. Each value overrides what the chain
        // carried, since an inherited break has to be cancellable and auto is
        // what a producer writes to cancel it.
        switch ((string?)properties.Attribute(OdtNamespaces.Fo + "break-before"))
        {
            case "page":
                style = style with { PageBreakBefore = true, ColumnBreakBefore = false };
                break;
            case "column":
                style = style with { PageBreakBefore = false, ColumnBreakBefore = true };
                break;
            case "auto":
                style = style with { PageBreakBefore = false, ColumnBreakBefore = false };
                break;
        }

        if ((string?)properties.Attribute(OdtNamespaces.Fo + "keep-with-next") is string keepNext)
            style = style with { KeepWithNext = keepNext == "always" };
        if ((string?)properties.Attribute(OdtNamespaces.Fo + "keep-together") is string keepTogether)
            style = style with { KeepTogether = keepTogether == "always" };
        if (int.TryParse((string?)properties.Attribute(OdtNamespaces.Fo + "orphans"),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int orphans) && orphans >= 0)
            style = style with { OrphanLines = orphans };
        if (int.TryParse((string?)properties.Attribute(OdtNamespaces.Fo + "widows"),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int widows) && widows >= 0)
            style = style with { WidowLines = widows };

        // fo:margin is the shorthand a producer writes to reset all four edges at
        // once. Only the single-value form is unambiguous without a box model.
        string? margin = (string?)properties.Attribute(OdtNamespaces.Fo + "margin");
        if (margin is not null &&
            !margin.Contains(' ', StringComparison.Ordinal) &&
            OdtUnits.TryParseLength(margin, out double allEdges))
        {
            style = style with
            {
                IndentLevel = IndentLevelFor(allEdges),
                LeftIndent = (float)allEdges,
                RightIndent = (float)allEdges,
                SpacingBefore = (float)allEdges,
                SpacingAfter = (float)allEdges,
            };
        }

        if (OdtUnits.TryParseLength((string?)properties.Attribute(OdtNamespaces.Fo + "margin-left"), out double left))
            style = style with { IndentLevel = IndentLevelFor(left), LeftIndent = (float)left };
        if (OdtUnits.TryParseLength((string?)properties.Attribute(OdtNamespaces.Fo + "margin-right"), out double right))
            style = style with { RightIndent = (float)right };
        if (OdtUnits.TryParseLength((string?)properties.Attribute(OdtNamespaces.Fo + "text-indent"), out double first))
            style = style with { FirstLineIndent = (float)first };

        if (OdtUnits.TryParseLength((string?)properties.Attribute(OdtNamespaces.Fo + "margin-top"), out double top))
            style = style with { SpacingBefore = (float)Math.Max(0, top) };

        if (OdtUnits.TryParseLength((string?)properties.Attribute(OdtNamespaces.Fo + "margin-bottom"), out double bottom))
            style = style with { SpacingAfter = (float)Math.Max(0, bottom) };

        // The paragraph's shading (§20.183); "transparent" takes away what the
        // chain gave, as an explicit value has to be able to.
        string? background = (string?)properties.Attribute(OdtNamespaces.Fo + "background-color");
        if (background is not null)
        {
            style = OdtUnits.TryParseColor(background, out BColor fill)
                ? style with { Background = fill }
                : style with { Background = BColor.Empty };
        }

        return ApplyLineHeight(properties, style, builder);
    }

    private static ParagraphStyle ApplyLineHeight(
        XElement properties,
        ParagraphStyle style,
        OdtDocumentBuilder builder)
    {
        // Each form replaces the others where a style states it: a percentage
        // is proportional, a length exact (ODF 1.3 fo:line-height), and
        // style:line-height-at-least the least a line is.
        string? lineHeight = (string?)properties.Attribute(OdtNamespaces.Fo + "line-height");
        if (lineHeight is not null)
        {
            if (lineHeight.Equals("normal", StringComparison.OrdinalIgnoreCase))
                return Proportional(style, 1f);

            if (OdtUnits.TryParsePercentage(lineHeight, out double multiplier) && multiplier > 0)
            {
                float projected = (float)multiplier;
                if (float.IsFinite(projected) && projected > 0)
                    return Proportional(style, projected);

                builder.AddDiagnosticOnce(
                    "odt.linespacing.range",
                    "An ODT line-spacing percentage was outside the model's representable range and was ignored.");
                return style;
            }

            if (TryLineHeight(lineHeight, out float exact))
                return style with { LineSpacing = 1f, LineHeightRule = LineHeightRule.Exact, LineHeight = exact };

            return style;
        }

        if (TryLineHeight((string?)properties.Attribute(OdtNamespaces.Style + "line-height-at-least"), out float least))
            return style with { LineSpacing = 1f, LineHeightRule = LineHeightRule.AtLeast, LineHeight = least };

        // Leading added between lines is a third kind, which the model does not
        // hold.
        if (properties.Attribute(OdtNamespaces.Style + "line-spacing") is not null)
        {
            builder.AddDiagnosticOnce(
                "odt.linespacing.leading",
                "ODT line spacing stated as leading between lines was not represented.");
        }

        return style;

        static ParagraphStyle Proportional(ParagraphStyle style, float spacing) =>
            style with { LineSpacing = spacing, LineHeightRule = LineHeightRule.Proportional, LineHeight = 0f };

        static bool TryLineHeight(string? value, out float height)
        {
            height = 0;
            if (!OdtUnits.TryParseLength(value, out double points) || !(points > 0))
                return false;

            height = (float)Math.Min(points, ParagraphStyle.MaxLineHeight);
            return true;
        }
    }

    private static int IndentLevelFor(double points) =>
        Math.Max(0, (int)Math.Round(points / OdtUnits.PointsPerIndentLevel, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Applies one <c>style:text-properties</c> over an inherited style. Called
    /// once per link in the style chain and finally for the span's own style, so
    /// only the attributes actually present override what came before.
    /// </summary>
    /// <summary>
    /// The formatting a paragraph inherits when it names no style of its own:
    /// ODF's <c>style:default-style</c> for the paragraph family.
    /// </summary>
    /// <remarks>
    /// Resolved through the same chain a real paragraph goes through rather than
    /// by reading the default style directly, so what this reports is what a
    /// paragraph would actually get.
    /// </remarks>
    private static DocumentStyleDefaults ReadStyleDefaults(OdtStyles styles)
    {
        InlineStyle style = InlineStyle.Default;
        foreach (XElement properties in styles.TextPropertiesForParagraph(null))
        {
            string? family =
                styles.ResolveFontName((string?)properties.Attribute(OdtNamespaces.Style + "font-name")) ??
                NormalizeFontFamily((string?)properties.Attribute(OdtNamespaces.Fo + "font-family"));
            if (!string.IsNullOrWhiteSpace(family))
                style = style with { FontFamily = NormalizeFontFamily(family) };

            style = ApplyFontSize(properties, style);
        }

        return new DocumentStyleDefaults
        {
            FontSizePoints = style.FontSize is float size && size > 0
                ? size
                : DocumentStyleDefaults.FallbackFontSizePoints,
            FontFamily = string.IsNullOrWhiteSpace(style.FontFamily) ? null : style.FontFamily,
        };
    }

    private static InlineStyle ApplyTextProperties(
        XElement? properties,
        InlineStyle style,
        OdtStyles styles,
        OdtDocumentBuilder builder)
    {
        if (properties is null)
            return style;

        string? weight = (string?)properties.Attribute(OdtNamespaces.Fo + "font-weight");
        if (weight is not null)
            style = style with { Bold = IsBoldWeight(weight) };

        string? slant = (string?)properties.Attribute(OdtNamespaces.Fo + "font-style");
        if (slant is not null)
        {
            style = style with
            {
                Italic = slant.Equals("italic", StringComparison.OrdinalIgnoreCase) ||
                    slant.Equals("oblique", StringComparison.OrdinalIgnoreCase),
            };
        }

        style = ApplyLineDecoration(properties, "text-underline", style, static (s, v) => s with { Underline = v });
        style = ApplyLineDecoration(properties, "text-line-through", style, static (s, v) => s with { Strikethrough = v });
        if ((string?)properties.Attribute(OdtNamespaces.Text + "display") is string display)
            style = style with { Hidden = display == "none" };

        string? family =
            styles.ResolveFontName((string?)properties.Attribute(OdtNamespaces.Style + "font-name")) ??
            NormalizeFontFamily((string?)properties.Attribute(OdtNamespaces.Fo + "font-family"));
        if (!string.IsNullOrWhiteSpace(family))
            style = style with { FontFamily = NormalizeFontFamily(family) };

        style = ApplyFontSize(properties, style);

        if (OdtUnits.TryParseColor((string?)properties.Attribute(OdtNamespaces.Fo + "color"), out BColor foreground))
            style = style with { Foreground = foreground };

        string? background = (string?)properties.Attribute(OdtNamespaces.Fo + "background-color");
        if (background is not null)
        {
            style = OdtUnits.TryParseColor(background, out BColor fill)
                ? style with { Background = fill }
                : style with { Background = BColor.Empty };
        }

        // Small caps first, then the transform: a style that asks for both draws
        // as all capitals, which is what an ODF consumer does.
        string? variant = (string?)properties.Attribute(OdtNamespaces.Fo + "font-variant");
        if (variant is not null)
        {
            style = variant.Equals("small-caps", StringComparison.OrdinalIgnoreCase)
                ? style with { Capitalization = TextCapitalization.SmallCaps }
                : ClearCapitalization(style, TextCapitalization.SmallCaps);
        }

        return ApplyTextTransform(properties, style, builder);
    }

    private static InlineStyle ApplyTextTransform(
        XElement properties,
        InlineStyle style,
        OdtDocumentBuilder builder)
    {
        string? transform = (string?)properties.Attribute(OdtNamespaces.Fo + "text-transform");
        if (transform is null)
            return style;

        if (transform.Equals("uppercase", StringComparison.OrdinalIgnoreCase))
            return style with { Capitalization = TextCapitalization.AllCaps };

        if (transform.Equals("lowercase", StringComparison.OrdinalIgnoreCase) ||
            transform.Equals("capitalize", StringComparison.OrdinalIgnoreCase))
        {
            builder.AddDiagnosticOnce(
                "odt.text.transform",
                "An ODT lowercase or capitalize text transform was dropped; the model draws upper case only.");
        }

        return ClearCapitalization(style, TextCapitalization.AllCaps);
    }

    /// <summary>Turning one kind of capitalization off leaves the other alone.</summary>
    private static InlineStyle ClearCapitalization(InlineStyle style, TextCapitalization kind) =>
        style.Capitalization == kind ? style with { Capitalization = TextCapitalization.None } : style;

    private static InlineStyle ApplyFontSize(XElement properties, InlineStyle style)
    {
        string? size = (string?)properties.Attribute(OdtNamespaces.Fo + "font-size");
        if (size is null)
            return style;

        if (OdtUnits.TryParseLength(size, out double points) && points > 0)
            return style with { FontSize = (float)points };

        // A percentage is relative to the size this style inherited, which is the
        // one already resolved into the style being built up.
        if (OdtUnits.TryParsePercentage(size, out double multiplier) &&
            multiplier > 0 &&
            style.FontSize is { } inherited)
        {
            return style with { FontSize = (float)(inherited * multiplier) };
        }

        return style;
    }

    /// <summary>
    /// Reads one of the paired line decorations. ODF splits each into a style and
    /// a type attribute, and either one set to <c>none</c> turns the decoration
    /// off, so both are consulted.
    /// </summary>
    private static InlineStyle ApplyLineDecoration(
        XElement properties,
        string prefix,
        InlineStyle style,
        Func<InlineStyle, bool, InlineStyle> apply)
    {
        string? lineStyle = (string?)properties.Attribute(OdtNamespaces.Style + prefix + "-style");
        string? lineType = (string?)properties.Attribute(OdtNamespaces.Style + prefix + "-type");
        if (lineStyle is null && lineType is null)
            return style;

        bool off =
            string.Equals(lineStyle, "none", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(lineType, "none", StringComparison.OrdinalIgnoreCase);
        return apply(style, !off);
    }

    private static bool IsBoldWeight(string weight)
    {
        if (weight.Equals("bold", StringComparison.OrdinalIgnoreCase))
            return true;
        if (weight.Equals("normal", StringComparison.OrdinalIgnoreCase))
            return false;

        // The numeric form: 400 is normal and 700 is bold, so the boundary that
        // matters is 600, exactly as CSS defines it.
        return int.TryParse(weight, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numeric) &&
            numeric >= 600;
    }

    /// <summary>
    /// Reduces a CSS-style font family list to one family name: the first entry,
    /// unquoted. ODF writes <c>fo:font-family</c> in CSS syntax, so a name with a
    /// space arrives quoted and a fallback list arrives comma-separated.
    /// </summary>
    private static string? NormalizeFontFamily(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string first = value.Split(',', 2)[0].Trim();
        if (first.Length >= 2 &&
            ((first[0] == '\'' && first[^1] == '\'') || (first[0] == '"' && first[^1] == '"')))
        {
            first = first[1..^1].Trim();
        }

        return first.Length == 0 ? null : first;
    }

}
