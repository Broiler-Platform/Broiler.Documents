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
            var images = new OdtImageLoader(archive, manifest, options.Limits);
            RichTextDocument document = ReadContent(content, styles, images, options.Limits, diagnostics);
            document = document.WithRunningContent(
                ReadRunningContent(styles, images, options.Limits, diagnostics));
            document = document.WithPageGeometry(ReadPageGeometry(styles, diagnostics));
            return new DocumentReadResult(document, diagnostics, DocumentReadResult.StatusFrom(diagnostics));
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
        builder.ReportReadSummary(
            body.Elements().Any(IsContentBlock),
            styles.Count,
            styles.ListStyleCount,
            images.ImageCount);
        return builder.Build();
    }

    /// <summary>
    /// Reads the headers and footers hanging off the first master page.
    /// </summary>
    /// <remarks>
    /// ODF keeps them in <c>styles.xml</c> under <c>office:master-styles</c>, one
    /// set per master page, and a document can define several. The model has one
    /// set, so the first master page is read - which is the one the default body
    /// style points at in every document a word processor writes.
    /// </remarks>
    private static RunningContent ReadRunningContent(
        OdtStyles styles,
        OdtImageLoader images,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        XElement? master = styles.MasterStyles
            ?.Elements(OdtNamespaces.Style + "master-page")
            .FirstOrDefault();
        if (master is null)
            return RunningContent.Empty;

        RunningContent content = RunningContent.Empty;
        foreach ((string element, bool isHeader, PageSelection selection) in RunningParts)
        {
            XElement? part = master.Element(OdtNamespaces.Style + element);
            if (part is null)
                continue;

            IReadOnlyList<RichTextParagraph>? paragraphs =
                ReadPartParagraphs(part, styles, images, limits, diagnostics);
            if (paragraphs is null)
                continue;

            content = isHeader
                ? content.WithHeader(selection, paragraphs)
                : content.WithFooter(selection, paragraphs);
        }

        return content;
    }

    /// <summary>
    /// Reads the page the first master page is laid out on.
    /// </summary>
    /// <remarks>
    /// ODF keeps the paper in a style:page-layout and has the master page name it,
    /// so this follows that reference rather than guessing at the first layout -
    /// a document can define several, and only the named one is the page.
    /// A layout that leaves no column to write on is dropped and reported.
    /// </remarks>
    private static PageGeometry? ReadPageGeometry(OdtStyles styles, List<DocumentDiagnostic> diagnostics)
    {
        XElement? master = styles.MasterStyles
            ?.Elements(OdtNamespaces.Style + "master-page")
            .FirstOrDefault();
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
        (ShapeFill? fill, BColor outline) = ReadShapeStyle(styleName, context.Styles);

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
            paragraphs));
        return true;
    }

    /// <summary>The fill and outline a graphic style states, resolving a named gradient.</summary>
    private static (ShapeFill? Fill, BColor Outline) ReadShapeStyle(string? styleName, OdtStyles styles)
    {
        if (string.IsNullOrWhiteSpace(styleName) ||
            !styles.GraphicProperties.TryGetValue(styleName, out XElement? properties))
        {
            return (null, BColor.Empty);
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
                double angle = double.TryParse(
                    (string?)gradient.Attribute(OdtNamespaces.Draw + "angle"),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double tenths)
                    ? tenths / 10d
                    : 0;
                fill = new ShapeFill(start, end, angle);
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

        return (fill, outline);
    }

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

    private static IReadOnlyList<RichTextParagraph>? ReadPartParagraphs(
        XElement part,
        OdtStyles styles,
        OdtImageLoader images,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        var builder = new OdtDocumentBuilder(limits, diagnostics);
        var context = new OdtReadContext(styles, images, builder);
        ReadBlockContent(part.Elements(), context, list: null, depth: 0);

        IReadOnlyList<RichTextParagraph> paragraphs = builder.Build().Paragraphs;
        return paragraphs.Count == 0 ? null : paragraphs;
    }

    /// <summary>Everything a read needs to turn one element into document content.</summary>
    private sealed record OdtReadContext(
        OdtStyles Styles,
        OdtImageLoader Images,
        OdtDocumentBuilder Builder);

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
                ReadTable(element, context, depth + 1);
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

    /// <summary>
    /// Reads a table: its cells' paragraphs into the body, left-to-right and
    /// top-to-bottom, and the grid they are arranged in into a
    /// <see cref="DocumentTable"/> over the range they occupy.
    /// </summary>
    /// <remarks>
    /// ODF states a merge on the cell that opens it and writes a
    /// <c>table:covered-table-cell</c> for every grid position it covers. Those
    /// hold nothing and are drawn by nobody, so they are counted for the column
    /// they occupy and otherwise passed over - the span on the opening cell is
    /// the whole of what the model needs.
    /// </remarks>
    private static void ReadTable(XElement table, OdtReadContext context, int depth)
    {
        int start = context.Builder.CurrentParagraphIndex;
        var rows = new List<TableRow>();

        foreach (XElement row in EnumerateRows(table, depth))
        {
            var cells = new List<TableCell>();
            int column = 0;

            // Columns a span in this row has already claimed. The covered cells
            // that follow it stand for those, so they take no column of their
            // own; a covered cell with none pending is the lower half of a merge
            // from the row above, and does take one.
            int claimed = 0;
            foreach (XElement cell in row.Elements())
            {
                bool covered = cell.Name == OdtNamespaces.Table + "covered-table-cell";
                if (!covered && cell.Name != OdtNamespaces.Table + "table-cell")
                    continue;

                int repeat = Math.Clamp(TableInt(cell, "number-columns-repeated", 1), 1, MaxColumnRepeat);
                if (covered)
                {
                    for (int i = 0; i < repeat; i++)
                    {
                        if (claimed > 0)
                            claimed--;
                        else
                            column++;
                    }

                    continue;
                }

                int span = Math.Max(1, TableInt(cell, "number-columns-spanned", 1));
                claimed = span - 1;
                int rowSpan = Math.Max(1, TableInt(cell, "number-rows-spanned", 1));
                XElement? properties = CellProperties(cell, context.Styles);

                for (int i = 0; i < repeat; i++)
                {
                    int cellStart = context.Builder.CurrentParagraphIndex;
                    var nested = new List<DocumentTable>();
                    context.Builder.PushTableSink(nested);
                    ReadBlockContent(cell.Elements(), context, list: null, depth + 1);
                    context.Builder.PopTableSink();

                    cells.Add(new TableCell(
                        cellStart,
                        context.Builder.CurrentParagraphIndex - cellStart,
                        column,
                        span,
                        rowSpan,
                        ReadCellShading(properties),
                        ReadCellBorders(properties),
                        isRowSpanContinuation: false,
                        tables: nested));
                    column += span;
                }
            }

            rows.Add(new TableRow(
                cells,
                row.Parent?.Name == OdtNamespaces.Table + "table-header-rows"));
        }

        context.Builder.AddTable(new DocumentTable(
            start,
            context.Builder.CurrentParagraphIndex - start,
            rows,
            ReadColumnWidths(table, context.Styles, depth),
            DocumentTable.DefaultCellPadding));

        context.Builder.NoteTable();
    }

    /// <summary>A repeat count beyond this is a spreadsheet's empty tail, not a table.</summary>
    private const int MaxColumnRepeat = 64;

    private static int TableInt(XElement element, string attribute, int fallback) =>
        int.TryParse(
            (string?)element.Attribute(OdtNamespaces.Table + attribute),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int value)
            ? value
            : fallback;

    /// <summary>
    /// The grid's column widths in points, from each <c>table:table-column</c>'s
    /// style. A column that states none contributes zero, which a renderer reads
    /// as "share what is left".
    /// </summary>
    private static IReadOnlyList<double> ReadColumnWidths(XElement table, OdtStyles styles, int depth)
    {
        var widths = new List<double>();
        foreach (XElement column in EnumerateColumns(table, depth))
        {
            int repeat = Math.Clamp(TableInt(column, "number-columns-repeated", 1), 1, MaxColumnRepeat);
            string? styleName = (string?)column.Attribute(OdtNamespaces.Table + "style-name");
            double width = 0;
            if (styleName is not null &&
                styles.TableColumnProperties.TryGetValue(styleName, out XElement? properties) &&
                OdtUnits.TryParseLength((string?)properties.Attribute(OdtNamespaces.Style + "column-width"), out double points))
            {
                width = points;
            }

            for (int i = 0; i < repeat; i++)
                widths.Add(width);
        }

        return widths;
    }

    /// <summary>A table's columns, looking through the groups ODF wraps them in.</summary>
    private static IEnumerable<XElement> EnumerateColumns(XElement parent, int depth)
    {
        if (depth > MaxColumnRepeat)
            yield break;

        foreach (XElement child in parent.Elements())
        {
            if (child.Name == OdtNamespaces.Table + "table-column")
            {
                yield return child;
                continue;
            }

            if (child.Name == OdtNamespaces.Table + "table-columns" ||
                child.Name == OdtNamespaces.Table + "table-column-group" ||
                child.Name == OdtNamespaces.Table + "table-header-columns")
            {
                foreach (XElement nested in EnumerateColumns(child, depth + 1))
                    yield return nested;
            }
        }
    }

    private static XElement? CellProperties(XElement cell, OdtStyles styles)
    {
        string? styleName = (string?)cell.Attribute(OdtNamespaces.Table + "style-name");
        return styleName is not null && styles.TableCellProperties.TryGetValue(styleName, out XElement? properties)
            ? properties
            : null;
    }

    private static BColor ReadCellShading(XElement? properties)
    {
        string? color = (string?)properties?.Attribute(OdtNamespaces.Fo + "background-color");
        if (color is null || color.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            return BColor.Empty;

        return OdtUnits.TryParseColor(color, out BColor parsed) ? parsed : BColor.Empty;
    }

    /// <summary>
    /// A cell's four edges. ODF writes them as CSS shorthand - a width, a style,
    /// and a colour - either once for all four or once per side, with the side
    /// winning where both are stated.
    /// </summary>
    private static CellBorders ReadCellBorders(XElement? properties)
    {
        if (properties is null)
            return default;

        TableBorder all = ReadBorderEdge(properties, "border");
        return new CellBorders(
            FirstStated(ReadBorderEdge(properties, "border-left"), all),
            FirstStated(ReadBorderEdge(properties, "border-top"), all),
            FirstStated(ReadBorderEdge(properties, "border-right"), all),
            FirstStated(ReadBorderEdge(properties, "border-bottom"), all));
    }

    private static TableBorder FirstStated(TableBorder edge, TableBorder fallback) =>
        edge.Width > 0 || edge.Color.A > 0 ? edge : fallback;

    /// <summary>
    /// One <c>fo:border</c> value: <c>0.5pt solid #000000</c>. The keyword
    /// <c>none</c> is a border turned off, and anything unparseable is left off
    /// rather than guessed at.
    /// </summary>
    private static TableBorder ReadBorderEdge(XElement properties, string attribute)
    {
        string? value = (string?)properties.Attribute(OdtNamespaces.Fo + attribute);
        if (string.IsNullOrWhiteSpace(value) || value.Equals("none", StringComparison.OrdinalIgnoreCase))
            return TableBorder.None;

        double width = 0;
        BColor color = BColor.Black;
        foreach (string part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (OdtUnits.TryParseLength(part, out double points))
                width = points;
            else if (OdtUnits.TryParseColor(part, out BColor parsed))
                color = parsed;
        }

        return width > 0 ? new TableBorder(color, width) : TableBorder.None;
    }

    /// <summary>
    /// Yields a table's rows, looking through the row groups ODF wraps them in:
    /// header rows, row groups, and the row-level equivalents of a column split.
    /// </summary>
    private static IEnumerable<XElement> EnumerateRows(XElement parent, int depth)
    {
        if (depth > 64)
            yield break;

        foreach (XElement child in parent.Elements())
        {
            if (child.Name == OdtNamespaces.Table + "table-row")
            {
                yield return child;
                continue;
            }

            if (child.Name == OdtNamespaces.Table + "table-header-rows" ||
                child.Name == OdtNamespaces.Table + "table-row-group" ||
                child.Name == OdtNamespaces.Table + "table-rows")
            {
                foreach (XElement nested in EnumerateRows(child, depth + 1))
                    yield return nested;
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
        foreach (XElement properties in context.Styles.ParagraphProperties(styleName))
            paragraphStyle = ApplyParagraphProperties(properties, paragraphStyle, context.Builder);

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
                "A floating ODT picture was anchored to its paragraph; " +
                "text wrapping and z-order are not represented.");

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
        (ShapeFill? fill, BColor outline) = ReadShapeStyle(styleName, context.Styles);

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
            image: image));
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
        if (!IsAllowedLink(href))
        {
            context.Builder.AddDiagnosticOnce("odt.link", "A hyperlink with a disallowed or relative target was dropped.");
            return style;
        }

        return style with { LinkHref = href };
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

    private static int IndentLevelFor(double points) =>
        Math.Max(0, (int)Math.Round(points / OdtUnits.PointsPerIndentLevel, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Applies one <c>style:text-properties</c> over an inherited style. Called
    /// once per link in the style chain and finally for the span's own style, so
    /// only the attributes actually present override what came before.
    /// </summary>
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

    private sealed class OdtDocumentBuilder : IOdtImageDiagnostics
    {
        private readonly DocumentLimits _limits;
        private readonly List<DocumentDiagnostic> _diagnostics;
        private readonly List<RichTextParagraph> _paragraphs = [];
        private readonly List<Segment> _segments = [];
        private readonly HashSet<string> _diagnosticOnce = new(StringComparer.Ordinal);
        private readonly List<DocumentShape> _shapes = [];
        private readonly List<DocumentTable> _tables = [];
        private readonly Stack<List<DocumentTable>> _tableSinks = new();
        private ParagraphStyle _paragraphStyle = ParagraphStyle.Default;
        private bool _pendingSpace;
        private int _length;
        private int _tableCount;
        private int _unsupportedBlockCount;

        public OdtDocumentBuilder(DocumentLimits limits, List<DocumentDiagnostic> diagnostics)
        {
            _limits = limits;
            _diagnostics = diagnostics;
        }

        public void AddTable(DocumentTable table)
        {
            if (table.ParagraphCount <= 0 || table.Rows.Count == 0)
                return;

            (_tableSinks.Count > 0 ? _tableSinks.Peek() : _tables).Add(table);
        }

        /// <summary>Collects the tables read from here until the matching pop.</summary>
        public void PushTableSink(List<DocumentTable> sink) => _tableSinks.Push(sink);

        public void PopTableSink()
        {
            if (_tableSinks.Count > 0)
                _tableSinks.Pop();
        }

        /// <summary>Counts a table for the read summary.</summary>
        public void NoteTable() => _tableCount++;

        /// <summary>
        /// Records a block-level element the reader does not understand. Keyed by
        /// element name so each distinct construct is reported once; the name is
        /// markup structure, never document text (ADR 0004 privacy rule).
        /// </summary>
        public void AddUnsupportedBlock(XName name)
        {
            _unsupportedBlockCount++;
            AddDiagnosticOnce(
                "odt.block.unsupported:" + name.LocalName,
                "odt.block.unsupported",
                "An unsupported ODT block-level element was skipped: " + name.LocalName + ".");
        }

        /// <summary>
        /// Emits the read summary. The counts make a silent content loss visible:
        /// a body with block content that yields no paragraphs is a reader bug,
        /// not an empty file, and it should say so rather than open blank.
        /// </summary>
        public void ReportReadSummary(
            bool bodyHadContentBlocks,
            int styleCount,
            int listStyleCount,
            int imageCount)
        {
            if (_paragraphs.Count == 0 && bodyHadContentBlocks)
            {
                _diagnostics.Add(DocumentDiagnostic.Warning(
                    "odt.document.empty",
                    "The ODT body contained block-level content but produced no paragraphs."));
            }

            _diagnostics.Add(DocumentDiagnostic.Info(
                "odt.read.summary",
                "ODT read produced " + _paragraphs.Count.ToString(CultureInfo.InvariantCulture) +
                " paragraph(s), flattened " + _tableCount.ToString(CultureInfo.InvariantCulture) +
                " table(s), loaded " + styleCount.ToString(CultureInfo.InvariantCulture) +
                " style(s) and " + listStyleCount.ToString(CultureInfo.InvariantCulture) +
                " list style(s), embedded " + imageCount.ToString(CultureInfo.InvariantCulture) +
                " image(s), and skipped " + _unsupportedBlockCount.ToString(CultureInfo.InvariantCulture) +
                " unsupported block(s)."));
        }

        public void StartParagraph(ParagraphStyle style)
        {
            _segments.Clear();
            _length = 0;
            _pendingSpace = false;
            _paragraphStyle = style;
        }

        /// <summary>
        /// Appends text from an XML text node under the ODF white-space rule: a
        /// run of white space is one space, a space at the start of a paragraph is
        /// nothing, and a space at the end is dropped when the paragraph closes.
        /// A producer that means several spaces writes <c>text:s</c>, which
        /// arrives through <see cref="AppendLiteral"/> instead.
        /// </summary>
        public void AppendCollapsed(string text, InlineStyle style)
        {
            if (string.IsNullOrEmpty(text))
                return;

            int start = -1;
            for (int i = 0; i < text.Length; i++)
            {
                if (IsCollapsibleWhiteSpace(text[i]))
                {
                    if (start >= 0)
                    {
                        Append(text[start..i], style);
                        start = -1;
                    }

                    _pendingSpace = true;
                    continue;
                }

                if (start < 0)
                    start = i;
            }

            if (start >= 0)
                Append(text[start..], style);
        }

        /// <summary>
        /// Appends characters that stand for themselves: the spaces of a
        /// <c>text:s</c>, a tab, a line break, or the placeholder of a picture.
        /// </summary>
        public void AppendLiteral(string text, InlineStyle style)
        {
            if (string.IsNullOrEmpty(text))
                return;

            Append(text, style);
        }

        private void Append(string text, InlineStyle style)
        {
            // A pending space only survives if something precedes it. That single
            // condition is both halves of the ODF rule: no leading space, and no
            // doubled space, because the flag is set rather than counted.
            if (_pendingSpace)
            {
                _pendingSpace = false;
                if (_length > 0)
                    AppendCore(" ", style);
            }

            AppendCore(text, style);
        }

        private void AppendCore(string text, InlineStyle style)
        {
            if (_length >= _limits.MaxRunLength)
            {
                AddDiagnosticOnce("odt.limit.run", "An ODT paragraph exceeded MaxRunLength and was truncated.");
                return;
            }

            if (_length + text.Length > _limits.MaxRunLength)
            {
                text = text[..(_limits.MaxRunLength - _length)];
                AddDiagnosticOnce("odt.limit.run", "An ODT paragraph exceeded MaxRunLength and was truncated.");
            }

            _length += text.Length;
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
            // Trailing white space is dropped: the pending space is simply never
            // flushed.
            _pendingSpace = false;

            if (_paragraphs.Count >= _limits.MaxParagraphCount)
            {
                AddDiagnosticOnce(
                    "odt.limit.paragraphs",
                    "ODT input exceeded MaxParagraphCount; remaining paragraphs were dropped.");
                _segments.Clear();
                _length = 0;
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
            _length = 0;
            _paragraphStyle = ParagraphStyle.Default;
        }

        /// <summary>The paragraph a shape met right now would be anchored to.</summary>
        public int CurrentParagraphIndex => _paragraphs.Count;

        public DocumentLimits Limits => _limits;

        /// <summary>Shared with the builder a shape's own text is read with.</summary>
        public List<DocumentDiagnostic> Diagnostics => _diagnostics;

        public void AddShape(DocumentShape shape) => _shapes.Add(shape);

        public RichTextDocument Build()
        {
            RichTextDocument document = _paragraphs.Count == 0
                ? RichTextDocument.Empty
                : RichTextDocument.FromParagraphs(_paragraphs);

            if (_shapes.Count > 0)
                document = document.WithShapes(_shapes);

            return _tables.Count == 0 ? document : document.WithTables(_tables);
        }

        public void AddDiagnosticOnce(string code, string message) =>
            AddDiagnosticOnce(code, code, message);

        public void AddDiagnosticOnce(string key, string code, string message)
        {
            if (_diagnosticOnce.Add(key))
                _diagnostics.Add(DocumentDiagnostic.Warning(code, message));
        }

        /// <summary>
        /// The characters ODF collapses. A non-breaking space is not one of them:
        /// it is a character the author chose, not layout white space.
        /// </summary>
        private static bool IsCollapsibleWhiteSpace(char character) =>
            character is ' ' or '\t' or '\r' or '\n';

        private readonly record struct Segment(string Text, InlineStyle Style);
    }
}
