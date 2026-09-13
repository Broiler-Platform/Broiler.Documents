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
            RichTextDocument document = ReadContent(content, styles, images, options.Limits, diagnostics);

            // One resolution, used by both. The page and the header that sits on
            // it belong to the same master page, and reading them from two
            // different ones would put a Letter header on an A4 page without
            // anything saying so.
            XElement? master = ResolveMasterPage(content, styles);
            document = document.WithRunningContent(
                ReadRunningContent(master, styles, images, options.Limits, diagnostics));
            document = document.WithPageGeometry(ReadPageGeometry(master, styles, diagnostics));
            document = document.WithStyleDefaults(ReadStyleDefaults(styles));
            return new DocumentReadResult(
                document,
                diagnostics,
                DocumentReadResult.StatusFrom(diagnostics),
                resources.Build(),
                OdtMetadata.Read(archive, options.Limits, diagnostics));
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

    private static RichTextDocument ReadContent(
        XDocument content,
        OdtStyles styles,
        OdtImageLoader images,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
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
        var context = new OdtReadContext(styles, images, builder);
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
        return builder.Build();
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
    /// Reads the paper the given master page is laid out on.
    /// </summary>
    /// <remarks>
    /// ODF keeps the paper in a style:page-layout and has the master page name it,
    /// so this follows that reference rather than guessing at the first layout -
    /// a document can define several, and only the named one is the page. Which
    /// master page arrives here is <see cref="ResolveMasterPage"/>'s decision.
    /// A layout that leaves no column to write on is dropped and reported.
    /// </remarks>
    private static PageGeometry? ReadPageGeometry(
        XElement? master, OdtStyles styles, List<DocumentDiagnostic> diagnostics)
    {
        string? layoutName = (string?)master?.Attribute(OdtNamespaces.Style + "page-layout-name");
        if (string.IsNullOrWhiteSpace(layoutName))
            return null;

        XElement? layout = null;
        foreach (XElement candidate in styles.PageLayouts)
        {
            if (string.Equals(
                    (string?)candidate.Attribute(OdtNamespaces.Style + "name"),
                    layoutName,
                    StringComparison.Ordinal))
            {
                layout = candidate;
                break;
            }
        }

        XElement? properties = layout?.Element(OdtNamespaces.Style + "page-layout-properties");
        if (properties is null)
            return null;

        if (!OdtUnits.TryParseLength((string?)properties.Attribute(OdtNamespaces.Fo + "page-width"), out double width) ||
            !OdtUnits.TryParseLength((string?)properties.Attribute(OdtNamespaces.Fo + "page-height"), out double height))
        {
            return null;
        }

        var geometry = new PageGeometry(
            width,
            height,
            Length(properties, "margin-left"),
            Length(properties, "margin-right"),
            Length(properties, "margin-top"),
            Length(properties, "margin-bottom"));

        if (geometry.IsUsable)
            return geometry;

        diagnostics.Add(DocumentDiagnostic.Warning(
            "odt.page.geometry",
            "An ODT page layout gave a page with no room to write on; the page was not read."));
        return null;
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
        if (!OdtUnits.TryParseLength((string?)drawing.Attribute(OdtNamespaces.Svg + "width"), out double width) ||
            !OdtUnits.TryParseLength((string?)drawing.Attribute(OdtNamespaces.Svg + "height"), out double height) ||
            width <= 0 || height <= 0)
        {
            return false;
        }

        string? styleName = (string?)drawing.Attribute(OdtNamespaces.Draw + "style-name");
        (ShapeFill? fill, BColor outline, bool behindText, ShapeWrap wrap, WrapSide wrapSide) =
            ReadShapeStyle(styleName, context.Styles);

        // A frame keeps its text in a draw:text-box; a custom shape carries the
        // paragraphs itself.
        XElement textSource = drawing.Element(OdtNamespaces.Draw + "text-box") ?? drawing;
        IReadOnlyList<RichTextParagraph> paragraphs = ReadShapeParagraphs(textSource, context, depth);

        if (fill is null && paragraphs.Count == 0)
            return false;

        double x = OdtUnits.TryParseLength((string?)drawing.Attribute(OdtNamespaces.Svg + "x"), out double left)
            ? left
            : 0;
        double y = OdtUnits.TryParseLength((string?)drawing.Attribute(OdtNamespaces.Svg + "y"), out double top)
            ? top
            : 0;

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
            zOrder: ZIndex(drawing)));
        return true;
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
        if (string.IsNullOrWhiteSpace(styleName) ||
            !styles.GraphicProperties.TryGetValue(styleName, out XElement? properties))
        {
            return (null, BColor.Empty, true, ShapeWrap.None, WrapSide.Largest);
        }

        ShapeFill? fill = null;
        string? kind = (string?)properties.Attribute(OdtNamespaces.Draw + "fill");
        if (string.Equals(kind, "gradient", StringComparison.Ordinal))
        {
            string? gradientName = (string?)properties.Attribute(OdtNamespaces.Draw + "fill-gradient-name");
            if (!string.IsNullOrWhiteSpace(gradientName) &&
                styles.Gradients.TryGetValue(gradientName, out XElement? gradient) &&
                TryColor((string?)gradient.Attribute(OdtNamespaces.Draw + "start-color"), out BColor start) &&
                TryColor((string?)gradient.Attribute(OdtNamespaces.Draw + "end-color"), out BColor end))
            {
                OdtUnits.TryParseAngle(
                    (string?)gradient.Attribute(OdtNamespaces.Draw + "angle"),
                    out double odfDegrees);
                fill = new ShapeFill(start, end, OdtGradientAngle.ToModel(odfDegrees));
            }
        }
        else if (string.Equals(kind, "solid", StringComparison.Ordinal) &&
                 TryColor((string?)properties.Attribute(OdtNamespaces.Draw + "fill-color"), out BColor solid))
        {
            fill = ShapeFill.Solid(solid);
        }

        BColor outline = BColor.Empty;
        string? stroke = (string?)properties.Attribute(OdtNamespaces.Draw + "stroke");
        if (!string.Equals(stroke, "none", StringComparison.Ordinal))
            TryColor((string?)properties.Attribute(OdtNamespaces.Svg + "stroke-color"), out outline);

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
            "none" => (ShapeWrap.TopAndBottom, WrapSide.Largest),
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

    /// <summary>A shape's own paragraphs, read with the same walker the body uses.</summary>
    private static IReadOnlyList<RichTextParagraph> ReadShapeParagraphs(
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
            textBox.Elements().Where(child => child.Name.Namespace == OdtNamespaces.Text),
            nested,
            list: null,
            depth + 1);
        nested.DropPageBreak();

        IReadOnlyList<RichTextParagraph> paragraphs = builder.Build().Paragraphs;
        return paragraphs.Count == 1 && paragraphs[0].Length == 0 ? [] : paragraphs;
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
    private sealed record OdtListContext(ListKind Kind, int Level, string? StyleName);

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

            if (name == OdtNamespaces.Text + "list")
            {
                ReadList(element, context, list, depth + 1);
                continue;
            }

            if (name == OdtNamespaces.Table + "table")
            {
                // A break held over from the paragraph above has nowhere to land:
                // the next thing to be laid out is a table, and this model states
                // a page break on a paragraph rather than on a table. Putting it
                // on the first paragraph of the first cell would break the page
                // inside the grid instead of before it.
                context.DropPageBreak();
                OdtTableReader.Read(element, context, depth + 1,
                    (children, childDepth) => ReadBlockContent(children, context, list: null, childDepth));
                continue;
            }

            // A section is a named region of ordinary body content; an index is a
            // generated one whose text:index-body holds real paragraphs. Both are
            // walked so the words inside them survive.
            if (name == OdtNamespaces.Text + "section")
            {
                ReadBlockContent(element.Elements(), context, list, depth + 1);
                continue;
            }

            if (name == OdtNamespaces.Text + "index-body" || IsIndexContainer(name))
            {
                ReadBlockContent(element.Elements(), context, list: null, depth + 1);
                continue;
            }

            // A drawing that carries paint or text of its own is a floating shape
            // beside the body, not body content.
            if (name == OdtNamespaces.Draw + "custom-shape" ||
                name == OdtNamespaces.Draw + "rect")
            {
                if (ReadShape(element, context, depth))
                    continue;
            }

            // A frame at block level is a text box, an image floating on the page,
            // or an embedded object. Only the text box holds body content.
            if (name == OdtNamespaces.Draw + "frame")
            {
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

            if (name == OdtNamespaces.Text + "tracked-changes")
            {
                builder.AddDiagnosticOnce(
                    "odt.revision.tracked",
                    "ODT tracked changes were not applied; the document is read as it stands.");
                continue;
            }

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

        string? styleName = (string?)list.Attribute(OdtNamespaces.Text + "style-name");
        if (string.IsNullOrEmpty(styleName))
            styleName = outer?.StyleName;

        int level = (outer?.Level ?? 0) + 1;
        ListKind kind = context.Styles.KindForList(styleName, level);
        var inner = new OdtListContext(kind, level, styleName);

        foreach (XElement child in list.Elements())
        {
            if (child.Name == OdtNamespaces.Text + "list-item")
            {
                ReadBlockContent(child.Elements(), context, inner, depth + 1);
                continue;
            }

            // A list header is the unnumbered lead-in paragraph of a list. It
            // keeps the level's indent but carries no bullet or number.
            if (child.Name == OdtNamespaces.Text + "list-header")
            {
                ReadBlockContent(
                    child.Elements(),
                    context,
                    inner with { Kind = ListKind.None },
                    depth + 1);
            }
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

        // A break handed over by the paragraph above is taken after this
        // paragraph's own properties have been applied, so it wins over an
        // fo:break-before of auto. That is the right way round: the two
        // attributes describe the same gap and either of them asking for a page
        // gets one, so a paragraph saying nothing stronger than "I do not start a
        // page" cannot talk the paragraph above it out of ending one.
        if (context.TakePageBreak())
            paragraphStyle = paragraphStyle with { PageBreakBefore = true };

        if (string.Equals(breakAfter, "column", StringComparison.Ordinal))
            ReportColumnBreak(context.Builder);

        context.HoldPageBreak(string.Equals(breakAfter, "page", StringComparison.Ordinal));

        // The list wins over whatever indent the paragraph style carried: inside a
        // list the nesting is the indent, and ODF list paragraph styles routinely
        // set fo:margin-left to zero precisely because the list supplies it.
        if (list is not null)
        {
            paragraphStyle = paragraphStyle with
            {
                ListKind = list.Kind,
                IndentLevel = Math.Max(list.Level, list.Kind == ListKind.None ? 0 : 1),
            };
        }

        InlineStyle inherited = InlineStyle.Default;
        foreach (XElement properties in context.Styles.TextPropertiesForParagraph(styleName))
            inherited = ApplyTextProperties(properties, inherited, context.Styles, context.Builder);

        context.Builder.StartParagraph(paragraphStyle);
        ReadInlineContent(paragraph, context, inherited, depth + 1);
        context.Builder.FinishParagraph();
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

            if (name == OdtNamespaces.Text + "a")
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

            if (name == OdtNamespaces.Draw + "custom-shape" ||
                name == OdtNamespaces.Draw + "rect")
            {
                ReadShape(element, context, depth: 0);
                continue;
            }

            if (name == OdtNamespaces.Draw + "frame")
            {
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
                builder.AddDiagnosticOnce(
                    "odt.note",
                    "ODT footnote and endnote bodies are not part of the paragraph and were skipped.");
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

    /// <summary>Inline elements that carry no text and are skipped silently.</summary>
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

    /// <summary>True for the two anchors that put a frame in the text rather than beside it.</summary>
    private static bool IsAnchoredInText(XElement frame)
    {
        string? anchor = (string?)frame.Attribute(OdtNamespaces.Text + "anchor-type");
        return anchor is null ||
            anchor.Equals("as-char", StringComparison.Ordinal) ||
            anchor.Equals("char", StringComparison.Ordinal);
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

        double x = OdtUnits.TryParseLength((string?)frame.Attribute(OdtNamespaces.Svg + "x"), out double left)
            ? left
            : 0;
        double y = OdtUnits.TryParseLength((string?)frame.Attribute(OdtNamespaces.Svg + "y"), out double top)
            ? top
            : 0;

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
            zOrder: ZIndex(frame)));
        return true;
    }

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
        // reported instead, because a model with no columns cannot hold it and a
        // construct that vanishes without a word is the defect this whole
        // property exists to close. Both of the other values still override what
        // the chain carried, since an inherited break has to be cancellable and
        // auto is what a producer writes to cancel it.
        switch ((string?)properties.Attribute(OdtNamespaces.Fo + "break-before"))
        {
            case "page":
                style = style with { PageBreakBefore = true };
                break;
            case "column":
                ReportColumnBreak(builder);
                style = style with { PageBreakBefore = false };
                break;
            case "auto":
                style = style with { PageBreakBefore = false };
                break;
        }

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
                SpacingBefore = (float)allEdges,
                SpacingAfter = (float)allEdges,
            };
        }

        if (OdtUnits.TryParseLength((string?)properties.Attribute(OdtNamespaces.Fo + "margin-left"), out double left))
            style = style with { IndentLevel = IndentLevelFor(left) };

        if (OdtUnits.TryParseLength((string?)properties.Attribute(OdtNamespaces.Fo + "margin-top"), out double top))
            style = style with { SpacingBefore = (float)Math.Max(0, top) };

        if (OdtUnits.TryParseLength((string?)properties.Attribute(OdtNamespaces.Fo + "margin-bottom"), out double bottom))
            style = style with { SpacingAfter = (float)Math.Max(0, bottom) };

        return ApplyLineHeight(properties, style, builder);
    }

    private static ParagraphStyle ApplyLineHeight(
        XElement properties,
        ParagraphStyle style,
        OdtDocumentBuilder builder)
    {
        string? lineHeight = (string?)properties.Attribute(OdtNamespaces.Fo + "line-height");
        if (lineHeight is not null)
        {
            if (lineHeight.Equals("normal", StringComparison.OrdinalIgnoreCase))
                return style with { LineSpacing = 1f };

            if (OdtUnits.TryParsePercentage(lineHeight, out double multiplier) && multiplier > 0)
                return style with { LineSpacing = (float)multiplier };

            // A fixed line height is a length, not a multiple of the font size,
            // and the model stores only the multiple.
            builder.AddDiagnosticOnce(
                "odt.linespacing.fixed",
                "A fixed ODT line height was not represented; the model stores a spacing multiplier.");
            return style;
        }

        if (properties.Attribute(OdtNamespaces.Style + "line-height-at-least") is not null ||
            properties.Attribute(OdtNamespaces.Style + "line-spacing") is not null)
        {
            builder.AddDiagnosticOnce(
                "odt.linespacing.fixed",
                "A fixed ODT line height was not represented; the model stores a spacing multiplier.");
        }

        return style;
    }

    /// <summary>
    /// Says that a column break was met and not kept. The model breaks pages and
    /// has no notion of a column, so there is nothing to write it into - but a
    /// column break is a real instruction in the document, and the reader that
    /// silently answered "no break" to it is the one that let every page break in
    /// every format disappear without trace.
    /// </summary>
    private static void ReportColumnBreak(OdtDocumentBuilder builder) =>
        builder.AddDiagnosticOnce(
            "odt.break.column",
            "A column break was not represented; this model breaks pages and has no columns.");

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
