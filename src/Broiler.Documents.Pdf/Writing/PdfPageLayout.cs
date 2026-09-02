using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Broiler.Documents.Model;
using Broiler.Documents.Pdf.Text;
using Broiler.Graphics;

namespace Broiler.Documents.Pdf.Writing;

/// <summary>A run of text placed at a definite position on a page.</summary>
internal sealed class PdfPlacedRun
{
    public PdfPlacedRun(
        string text,
        double x,
        double baseline,
        double width,
        double fontSize,
        PdfStandardFont font,
        BColor color,
        BColor background,
        bool underline,
        bool strikethrough,
        string? linkHref,
        double wordSpacing = 0)
    {
        Text = text;
        X = x;
        Baseline = baseline;
        Width = width;
        FontSize = fontSize;
        Font = font;
        Color = color;
        Background = background;
        Underline = underline;
        Strikethrough = strikethrough;
        LinkHref = linkHref;
        WordSpacing = wordSpacing;
    }

    public string Text { get; }

    public double X { get; }

    public double Baseline { get; }

    public double Width { get; }

    public double FontSize { get; }

    public PdfStandardFont Font { get; }

    public BColor Color { get; }

    public BColor Background { get; }

    public bool Underline { get; }

    public bool Strikethrough { get; }

    /// <summary>The admitted link target, or null. Revalidated again at emission.</summary>
    public string? LinkHref { get; }

    /// <summary>
    /// Extra width given to every space in this run, as PDF's <c>Tw</c>. Non-zero
    /// only on a justified line, where the slack is spread into the spaces rather
    /// than left at one end.
    /// </summary>
    public double WordSpacing { get; }
}

/// <summary>A floating shape's painted box, in PDF user space.</summary>
internal sealed record PdfPlacedShape(
    double X,
    double Y,
    double Width,
    double Height,
    ShapeFill? Fill,
    BColor Outline);

/// <summary>One laid-out page.</summary>
internal sealed class PdfLayoutPage
{
    /// <summary>The boxes painted under this page's text.</summary>
    public List<PdfPlacedShape> Shapes { get; } = [];
    public List<PdfPlacedRun> Runs { get; } = [];
}

/// <summary>
/// Breaks a rich-text document into lines and pages.
/// </summary>
/// <remarks>
/// <para>
/// Layout is resolved exactly once, here, and the serializer consumes the result
/// without measuring, shaping, or re-breaking anything. That separation is what
/// the roadmap's paginated-artifact boundary is for, and it is why the writer can
/// be deterministic: nothing downstream can reach for a host font or a DPI.
/// </para>
/// <para>
/// Measurement goes through <see cref="IPdfFontMetricsProvider"/>. With the
/// built-in approximate model the line breaks are consistent and reproducible but
/// not metrically exact, which the writer reports once per document.
/// </para>
/// </remarks>
internal sealed class PdfPageLayout
{
    /// <summary>The width of one indent level, in points.</summary>
    private const double IndentWidth = 24d;

    /// <summary>
    /// The distance between the default tab stops, in points, measured from where
    /// the paragraph's text starts. It is the tab stop the RichEdit control lays
    /// out with, so a tabbed paragraph prints where it sits on screen.
    /// </summary>
    private const double TabStopWidth = 48d;

    private readonly PdfWriteOptions _options;
    private readonly IPdfFontMetricsProvider _metrics;
    private readonly PdfUriPolicy _uriPolicy;
    private readonly PdfDiagnosticSink _diagnostics;
    private readonly CancellationToken _cancellationToken;

    /// <summary>The boxes wrapping shapes keep this layout's lines out of.</summary>
    private readonly TextWrapExclusions _wrap = new();

    public PdfPageLayout(
        PdfWriteOptions options,
        IPdfFontMetricsProvider metrics,
        PdfUriPolicy uriPolicy,
        PdfDiagnosticSink diagnostics,
        CancellationToken cancellationToken)
    {
        _options = options;
        _metrics = metrics;
        _uriPolicy = uriPolicy;
        _diagnostics = diagnostics;
        _cancellationToken = cancellationToken;
    }

    public List<PdfLayoutPage> Build(RichTextDocument document)
    {
        var pages = new List<PdfLayoutPage>();
        var page = new PdfLayoutPage();
        PdfPageSetup setup = SetupFor(document);

        double top = setup.Height - setup.MarginTop;
        double bottom = setup.MarginBottom;
        double y = top;
        int listNumber = 1;
        ListKind previousList = ListKind.None;

        var anchors = new Dictionary<int, (PdfLayoutPage Page, double Top)>();
        for (int paragraphIndex = 0; paragraphIndex < document.ParagraphCount; paragraphIndex++)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            if (DocumentTable.StartingAt(document.Tables, paragraphIndex) is DocumentTable table)
            {
                // A row is placed whole: its cells sit beside each other, and
                // breaking one across a page would mean breaking all of them at
                // the same line.
                foreach (CellContent row in ComposeTable(document, table, setup.MarginLeft, setup.ContentWidth))
                {
                    if (y - row.Height < bottom && page.Runs.Count > 0)
                    {
                        pages.Add(page);
                        page = new PdfLayoutPage();
                        y = top;
                    }

                    row.PlaceOn(page, y, this, anchors);
                    y -= row.Height;
                }

                paragraphIndex = table.ParagraphEnd - 1;
                previousList = ListKind.None;
                continue;
            }

            RichTextParagraph paragraph = document.Paragraphs[paragraphIndex];
            ParagraphStyle style = paragraph.Style;
            double lineSpacing = style.LineSpacing > 0 ? style.LineSpacing : 1f;

            if (style.ListKind == ListKind.Numbered && previousList == ListKind.Numbered)
                listNumber++;
            else if (style.ListKind == ListKind.Numbered)
                listNumber = 1;
            previousList = style.ListKind;

            y -= style.SpacingBefore;

            double indent = style.IndentLevel * IndentWidth;
            double left = setup.MarginLeft + indent;
            double available = setup.Width - setup.MarginRight - left;
            if (available <= 0)
            {
                _diagnostics.Warning(
                    PdfDiagnosticCodes.WriteOverflow,
                    "A paragraph's indent left no usable line width; the indent was clamped to the page margin.");
                left = setup.MarginLeft;
                available = setup.ContentWidth;
            }

            anchors[paragraphIndex] = (page, y);

            // A shape's box is known once the paragraph it hangs from has a top.
            // PDF counts up from the foot of the page and the exclusions count
            // down from its head, so the y is negated on the way in and back out.
            foreach (DocumentShape shape in document.Shapes)
            {
                if (shape.Wraps && shape.ParagraphIndex == paragraphIndex)
                    _wrap.Add(shape, -(y - shape.OffsetY));
            }

            string marker = PdfModelProjector.FormatListMarker(style.ListKind, listNumber);
            var bands = new List<TextBand>();
            List<LayoutLine> lines;

            if (_wrap.IsEmpty)
            {
                lines = BreakParagraph(paragraph, available, marker);
            }
            else
            {
                // The y is advanced by an empty line's height rather than each
                // line's own, which is not known until it has been wrapped to a
                // width: a line of unusually tall text can reach a little into a
                // shape it only just cleared.
                double lineY = y;
                double estimate = EmptyLineHeight() * lineSpacing;
                lines = BreakParagraph(
                    paragraph,
                    _ =>
                    {
                        TextBand row = WrapBand(ref lineY, estimate, available);
                        lineY -= estimate;
                        return row;
                    },
                    marker,
                    bands);
            }

            if (lines.Count == 0)
            {
                // An empty paragraph still advances by one line so blank lines and
                // opt-in page boundaries survive a round trip.
                y -= EmptyLineHeight() * lineSpacing;
                y -= SpacingAfter(style, EmptyLineHeight());
                if (y < bottom)
                {
                    pages.Add(page);
                    page = new PdfLayoutPage();
                    y = top;
                }

                continue;
            }

            double lastLineHeight = 0;
            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                LayoutLine line = lines[lineIndex];
                TextBand band = lineIndex < bands.Count ? bands[lineIndex] : new TextBand(0, available);
                double lineHeight = line.Height * lineSpacing;
                if (y - lineHeight < bottom && page.Runs.Count > 0)
                {
                    pages.Add(page);
                    page = new PdfLayoutPage();
                    y = top;
                }

                y -= lineHeight;
                lastLineHeight = lineHeight;
                Place(
                    page,
                    line,
                    left + band.Left,
                    Math.Max(1, band.Width),
                    y,
                    style.Alignment,
                    line == lines[^1]);
            }

            y -= SpacingAfter(style, lastLineHeight);
        }

        pages.Add(page);
        PlaceShapes(document.Shapes, anchors, setup);
        PlaceRunningContent(pages, document.RunningContent, setup, document.PageGeometry);
        return pages;
    }

    /// <summary>
    /// Draws the header and footer on every page, once the body has decided how
    /// many pages there are.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The model carries no header distance - no reader produces one - so this
    /// picks a convention rather than inventing a setting: the block sits in the
    /// middle of the margin it belongs to. A header taller than its margin would
    /// run into the body, so it is reported rather than drawn over the text.
    /// </para>
    /// <para>
    /// Page one takes the first-page selection and even-numbered pages the even
    /// one, each falling back to the default, which is what
    /// <see cref="RunningContent.EffectiveHeader"/> resolves.
    /// </para>
    /// </remarks>
    private void PlaceRunningContent(
        List<PdfLayoutPage> pages,
        RunningContent running,
        PdfPageSetup setup,
        PageGeometry? geometry)
    {
        if (running is null || running.IsEmpty)
            return;

        double left = setup.MarginLeft;
        double available = setup.ContentWidth;

        // A document that states how far its header sits from the edge gets that;
        // one that states nothing keeps the old convention of halfway up the
        // margin, which is the best guess available without a number.
        double headerBaseline = geometry is not null && geometry.HeaderDistance > 0
            ? setup.Height - geometry.HeaderDistance
            : setup.Height - (setup.MarginTop / 2);
        double footerBaseline = geometry is not null && geometry.FooterDistance > 0
            ? geometry.FooterDistance
            : setup.MarginBottom / 2;

        for (int i = 0; i < pages.Count; i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            PageSelection selection = SelectionForPage(i);

            PlaceRunningBlock(
                pages[i],
                running.EffectiveHeader(selection),
                left,
                available,
                headerBaseline,
                setup.MarginTop,
                isHeader: true);

            PlaceRunningBlock(
                pages[i],
                running.EffectiveFooter(selection),
                left,
                available,
                footerBaseline,
                setup.MarginBottom,
                isHeader: false);

            PlaceRunningShapes(pages[i], running.EffectiveHeaderShapes(selection), setup);
            PlaceRunningShapes(pages[i], running.EffectiveFooterShapes(selection), setup);
        }
    }

    /// <summary>
    /// Places a running band's shapes on one page. Unlike a body shape there is
    /// no paragraph to hang from: the offset is measured from the top of the page,
    /// which in PDF's upward user space is a subtraction from its height.
    /// </summary>
    private void PlaceRunningShapes(
        PdfLayoutPage page,
        IReadOnlyList<DocumentShape> shapes,
        PdfPageSetup setup)
    {
        foreach (DocumentShape shape in shapes)
        {
            if (shape.Width <= 0 || shape.Height <= 0)
                continue;

            if (shape.HasImage)
            {
                _diagnostics.Skipped(
                    PdfDiagnosticCodes.WriteImageNotComposed,
                    "A floating image was dropped. This build composes no image emitter, so images are omitted rather than rasterized or transcoded.");
            }

            double left = setup.MarginLeft + shape.OffsetX;
            double top = setup.Height - shape.OffsetY;
            page.Shapes.Add(new PdfPlacedShape(
                left,
                top - shape.Height,
                shape.Width,
                shape.Height,
                shape.Fill,
                shape.Outline));

            if (shape.HasText)
                PlaceRunningBlock(page, shape.Paragraphs, left, shape.Width, top, shape.Height, isHeader: true);
        }
    }

    /// <summary>
    /// Lays a table out row by row, each row's content measured from the row's
    /// own top so the caller can place it wherever it lands on the page.
    /// </summary>
    /// <remarks>
    /// Every row is composed before any is placed: a cell that spans rows needs
    /// the heights of the rows below it before its box can be drawn.
    /// </remarks>
    private List<CellContent> ComposeTable(
        RichTextDocument document,
        DocumentTable table,
        double left,
        double width)
    {
        double[] edges = ColumnEdges(table, left, width);
        var rows = new List<CellContent>(table.Rows.Count);
        var spans = new List<(int Row, CellBoxDraft Box)>();

        foreach (TableRow row in table.Rows)
        {
            var composed = new CellContent();
            foreach (TableCell cell in row.Cells)
            {
                (double cellLeft, double cellWidth) = ColumnSpanBox(edges, cell);
                double textWidth = Math.Max(1, cellWidth - (table.CellPadding * 2));
                CellContent content = ComposeBlocks(
                    document,
                    cell.Tables,
                    cell.ParagraphIndex,
                    cell.ParagraphEnd,
                    cellLeft + table.CellPadding,
                    textWidth);

                composed.Absorb(content);
                composed.Height = Math.Max(composed.Height, content.Height);

                if (cell.IsRowSpanContinuation)
                    continue;

                var box = new CellBoxDraft(cellLeft, cellWidth, cell);
                spans.Add((rows.Count, box));
                composed.Boxes.Add(box);
            }

            // A row is never shorter than a line, so an empty one is still a row.
            composed.Height = Math.Max(composed.Height, EmptyLineHeight());
            foreach (CellBoxDraft box in composed.Boxes)
                box.RowHeight = composed.Height;

            rows.Add(composed);
        }

        foreach ((int index, CellBoxDraft box) in spans)
        {
            double extra = 0;
            for (int r = index + 1; r < Math.Min(rows.Count, index + Math.Max(1, box.Cell.RowSpan)); r++)
                extra += rows[r].Height;

            box.ExtraHeight = extra;
        }

        return rows;
    }

    /// <summary>
    /// Lays out a range of block content inside a box, with every offset measured
    /// down from the box's top. A table inside it goes through
    /// <see cref="ComposeTable"/>, which comes back here for its cells.
    /// </summary>
    private CellContent ComposeBlocks(
        RichTextDocument document,
        IReadOnlyList<DocumentTable> tables,
        int from,
        int to,
        double left,
        double width)
    {
        var content = new CellContent();
        int listNumber = 1;
        ListKind previousList = ListKind.None;
        int index = Math.Max(0, from);
        int end = Math.Min(to, document.ParagraphCount);

        while (index < end)
        {
            if (DocumentTable.StartingAt(tables, index) is DocumentTable nested)
            {
                foreach (CellContent row in ComposeTable(document, nested, left, width))
                {
                    content.AbsorbAt(row, content.Height);
                    content.Height += row.Height;
                }

                index = nested.ParagraphEnd;
                previousList = ListKind.None;
                continue;
            }

            RichTextParagraph paragraph = document.Paragraphs[index];
            ParagraphStyle style = paragraph.Style;
            double lineSpacing = style.LineSpacing > 0 ? style.LineSpacing : 1f;

            if (style.ListKind == ListKind.Numbered && previousList == ListKind.Numbered)
                listNumber++;
            else if (style.ListKind == ListKind.Numbered)
                listNumber = 1;
            previousList = style.ListKind;

            if (content.Height > 0)
                content.Height += style.SpacingBefore;

            List<LayoutLine> lines = BreakParagraph(
                paragraph,
                width,
                PdfModelProjector.FormatListMarker(style.ListKind, listNumber));

            if (lines.Count == 0)
            {
                content.Height += EmptyLineHeight() * lineSpacing;
                content.Height += SpacingAfter(style, EmptyLineHeight());
                content.Anchors.Add((index, content.Height));
                index++;
                continue;
            }

            content.Anchors.Add((index, content.Height));
            double lastLineHeight = 0;
            foreach (LayoutLine line in lines)
            {
                double lineHeight = line.Height * lineSpacing;
                content.Height += lineHeight;
                lastLineHeight = lineHeight;
                content.Lines.Add(new PlacedLine(
                    line,
                    left,
                    width,
                    content.Height,
                    style.Alignment,
                    line == lines[^1]));
            }

            content.Height += SpacingAfter(style, lastLineHeight);
            index++;
        }

        return content;
    }

    /// <summary>
    /// The x of every column boundary, left to right. A grid wider than the space
    /// it has is scaled to fit rather than drawn off the page, and one that states
    /// no widths divides the space evenly.
    /// </summary>
    private static double[] ColumnEdges(DocumentTable table, double left, double width)
    {
        int columns = ColumnCount(table);
        var edges = new double[columns + 1];
        double total = table.TotalWidth;
        double scale = total > 0 && total > width ? width / total : 1.0;

        double x = left;
        edges[0] = x;
        for (int i = 0; i < columns; i++)
        {
            x += i < table.ColumnWidths.Count && table.ColumnWidths[i] > 0
                ? table.ColumnWidths[i] * scale
                : width / columns;
            edges[i + 1] = x;
        }

        return edges;
    }

    private static int ColumnCount(DocumentTable table)
    {
        int columns = table.ColumnWidths.Count;
        foreach (TableRow row in table.Rows)
        {
            foreach (TableCell cell in row.Cells)
                columns = Math.Max(columns, cell.ColumnIndex + cell.ColumnSpan);
        }

        return Math.Max(1, columns);
    }

    private static (double Left, double Width) ColumnSpanBox(double[] edges, TableCell cell)
    {
        int start = Math.Clamp(cell.ColumnIndex, 0, edges.Length - 1);
        int end = Math.Clamp(cell.ColumnIndex + cell.ColumnSpan, start + 1, edges.Length - 1);
        return (edges[start], Math.Max(1, edges[end] - edges[start]));
    }

    /// <summary>One line composed inside a box, at an offset down from its top.</summary>
    private readonly record struct PlacedLine(
        LayoutLine Line,
        double Left,
        double Available,
        double Offset,
        TextAlignment Alignment,
        bool IsLastLine);

    /// <summary>A cell's box, before the row it belongs to is placed on a page.</summary>
    private sealed class CellBoxDraft
    {
        public CellBoxDraft(double left, double width, TableCell cell, double offset = 0)
        {
            Left = left;
            Width = width;
            Cell = cell;
            Offset = offset;
        }

        public double Left { get; }

        public double Width { get; }

        public TableCell Cell { get; }

        /// <summary>How far below the content's top the box starts.</summary>
        public double Offset { get; }

        /// <summary>The height of the row the box is in, known once the row is composed.</summary>
        public double RowHeight { get; set; }

        /// <summary>How much further the box reaches, for a cell that spans rows.</summary>
        public double ExtraHeight { get; set; }

        public CellBoxDraft WithOffset(double offset) =>
            new(Left, Width, Cell, Offset + offset)
            {
                RowHeight = RowHeight,
                ExtraHeight = ExtraHeight,
            };

        /// <summary>
        /// Paints the box on a page whose content top is at <paramref name="top"/>:
        /// the shading, then each edge the cell states as a thin filled box. PDF
        /// measures up from the bottom of the page, so a box's y is its foot.
        /// </summary>
        public void Draw(PdfLayoutPage page, double top)
        {
            double height = RowHeight + ExtraHeight;
            if (Width <= 0 || height <= 0)
                return;

            double boxTop = top - Offset;
            if (!Cell.Shading.IsEmpty && Cell.Shading.A > 0)
            {
                page.Shapes.Add(new PdfPlacedShape(
                    Left,
                    boxTop - height,
                    Width,
                    height,
                    ShapeFill.Solid(Cell.Shading),
                    BColor.Empty));
            }

            CellBorders borders = Cell.Borders;
            AddEdge(page, borders.Top, Left, boxTop - borders.Top.Width, Width, borders.Top.Width);
            AddEdge(page, borders.Bottom, Left, boxTop - height, Width, borders.Bottom.Width);
            AddEdge(page, borders.Left, Left, boxTop - height, borders.Left.Width, height);
            AddEdge(
                page,
                borders.Right,
                Left + Width - borders.Right.Width,
                boxTop - height,
                borders.Right.Width,
                height);
        }

        private static void AddEdge(
            PdfLayoutPage page,
            TableBorder border,
            double x,
            double y,
            double width,
            double height)
        {
            if (!border.IsVisible || width <= 0 || height <= 0)
                return;

            page.Shapes.Add(new PdfPlacedShape(x, y, width, height, ShapeFill.Solid(border.Color), BColor.Empty));
        }
    }

    /// <summary>
    /// Block content composed inside a box: its lines and cell boxes at offsets
    /// down from the top, and how tall it came out.
    /// </summary>
    private sealed class CellContent
    {
        public double Height { get; set; }

        public List<PlacedLine> Lines { get; } = [];

        public List<CellBoxDraft> Boxes { get; } = [];

        /// <summary>Where each paragraph started, so a shape can still anchor to it.</summary>
        public List<(int ParagraphIndex, double Offset)> Anchors { get; } = [];

        /// <summary>Takes another box's content at the same top as this one.</summary>
        public void Absorb(CellContent other) => AbsorbAt(other, 0);

        /// <summary>Takes another box's content, <paramref name="offset"/> further down.</summary>
        public void AbsorbAt(CellContent other, double offset)
        {
            foreach (PlacedLine line in other.Lines)
                Lines.Add(line with { Offset = line.Offset + offset });

            foreach (CellBoxDraft box in other.Boxes)
                Boxes.Add(box.WithOffset(offset));

            foreach ((int paragraphIndex, double anchor) in other.Anchors)
                Anchors.Add((paragraphIndex, anchor + offset));
        }

        /// <summary>
        /// Draws the boxes and places the lines on a page, with the box's top at
        /// <paramref name="top"/> in PDF's upward user space.
        /// </summary>
        public void PlaceOn(
            PdfLayoutPage page,
            double top,
            PdfPageLayout layout,
            Dictionary<int, (PdfLayoutPage Page, double Top)> anchors)
        {
            foreach (CellBoxDraft box in Boxes)
                box.Draw(page, top);

            foreach ((int paragraphIndex, double offset) in Anchors)
                anchors[paragraphIndex] = (page, top - offset);

            foreach (PlacedLine line in Lines)
            {
                layout.Place(
                    page,
                    line.Line,
                    line.Left,
                    line.Available,
                    top - line.Offset,
                    line.Alignment,
                    line.IsLastLine);
            }
        }
    }

    /// <summary>
    /// Places the document's floating shapes against the paragraphs they anchor to.
    /// </summary>
    /// <remarks>
    /// A shape's x is measured from the text column's left edge, so a letterhead's
    /// stripe sits in the margin without any page geometry; its y runs down from
    /// the top of its paragraph, which in PDF's upward user space is a subtraction.
    /// The shape's own text becomes ordinary placed runs, so it draws through the
    /// same path as everything else and lands above the box.
    /// </remarks>
    private void PlaceShapes(
        IReadOnlyList<DocumentShape> shapes,
        Dictionary<int, (PdfLayoutPage Page, double Top)> anchors,
        PdfPageSetup setup)
    {
        foreach (DocumentShape shape in shapes)
        {
            if (!anchors.TryGetValue(shape.ParagraphIndex, out (PdfLayoutPage Page, double Top) anchor))
                continue;

            if (shape.Width <= 0 || shape.Height <= 0)
                continue;

            if (shape.HasImage)
            {
                // The box is still placed, so a bordered picture leaves its frame
                // on the page rather than nothing at all.
                _diagnostics.Skipped(
                    PdfDiagnosticCodes.WriteImageNotComposed,
                    "A floating image was dropped. This build composes no image emitter, so images are omitted rather than rasterized or transcoded.");
            }

            double left = setup.MarginLeft + shape.OffsetX;
            double top = anchor.Top - shape.OffsetY;
            anchor.Page.Shapes.Add(new PdfPlacedShape(
                left,
                top - shape.Height,
                shape.Width,
                shape.Height,
                shape.Fill,
                shape.Outline));

            if (shape.HasText)
                PlaceRunningBlock(anchor.Page, shape.Paragraphs, left, shape.Width, top, shape.Height, isHeader: true);
        }
    }

    /// <summary>Page one is the first page; pages two, four, six are the even ones.</summary>
    private static PageSelection SelectionForPage(int index) => index switch
    {
        0 => PageSelection.First,
        _ => (index + 1) % 2 == 0 ? PageSelection.Even : PageSelection.Default,
    };

    private void PlaceRunningBlock(
        PdfLayoutPage page,
        IReadOnlyList<RichTextParagraph> paragraphs,
        double left,
        double available,
        double firstBaseline,
        double margin,
        bool isHeader)
    {
        if (paragraphs.Count == 0)
            return;

        var lines = new List<(LayoutLine Line, ParagraphStyle Style, bool IsLast)>();
        double height = 0;
        foreach (RichTextParagraph paragraph in paragraphs)
        {
            List<LayoutLine> broken = BreakParagraph(paragraph, available, marker: string.Empty);
            double spacing = paragraph.Style.LineSpacing > 0 ? paragraph.Style.LineSpacing : 1f;
            for (int i = 0; i < broken.Count; i++)
            {
                lines.Add((broken[i], paragraph.Style, i == broken.Count - 1));
                height += broken[i].Height * spacing;
            }

            if (broken.Count == 0)
                height += EmptyLineHeight() * spacing;
        }

        if (height > margin)
        {
            _diagnostics.Warning(
                PdfDiagnosticCodes.WriteOverflow,
                isHeader
                    ? "A DOCX header was taller than the page's top margin and was not drawn."
                    : "A DOCX footer was taller than the page's bottom margin and was not drawn.");
            return;
        }

        double y = firstBaseline + (height / 2);
        foreach ((LayoutLine line, ParagraphStyle style, bool isLast) in lines)
        {
            double spacing = style.LineSpacing > 0 ? style.LineSpacing : 1f;
            y -= line.Height * spacing;
            Place(page, line, left, available, y, style.Alignment, isLast);
        }
    }

    /// <summary>
    /// The page to lay out on: the one the document states, else the one the
    /// caller asked for.
    /// </summary>
    /// <remarks>
    /// The document wins because printing an A4 letter on US Letter, when the
    /// letter says A4 and nobody said otherwise, is the writer overruling the
    /// author. A document that states nothing, or states nonsense, still gets the
    /// caller's page.
    /// </remarks>
    internal PdfPageSetup SetupFor(RichTextDocument document)
    {
        if (document.PageGeometry is not PageGeometry geometry || !geometry.IsUsable)
            return _options.PageSetup;

        return new PdfPageSetup(
            geometry.Width,
            geometry.Height,
            geometry.MarginLeft,
            geometry.MarginRight,
            geometry.MarginTop,
            geometry.MarginBottom);
    }

    private double EmptyLineHeight() => _options.DefaultFontSize * 1.2;

    /// <summary>
    /// The gap after a paragraph. A paragraph the model does not space explicitly
    /// still gets a default gap, scaled to its own line height.
    /// </summary>
    /// <remarks>
    /// This is not only a typographic default. PDF records no paragraph structure,
    /// so a reader — ours included — has to infer it from vertical spacing. Setting
    /// consecutive paragraphs solid would make them indistinguishable from the
    /// wrapped lines of one paragraph, and a write-then-read round trip would
    /// silently merge them.
    /// </remarks>
    private static double SpacingAfter(ParagraphStyle style, double lineHeight) =>
        style.SpacingAfter > 0 ? style.SpacingAfter : lineHeight * 0.55;

    /// <summary>
    /// Places one laid-out line at <paramref name="baseline"/>. Center and right
    /// move the whole line by the slack; justification spreads that slack into
    /// the line's own spaces instead, as PDF word spacing.
    /// </summary>
    /// <remarks>
    /// The last line of a paragraph is never justified: the slack there is
    /// whatever the text happened to leave, and stretching a two-word closing
    /// line across the column is the one thing every typesetter agrees is wrong.
    /// A line with no spaces to stretch — one long word — is left flush too,
    /// rather than having its glyphs pulled apart.
    /// </remarks>
    /// <summary>
    /// The band a line has at <paramref name="y"/>, moved below anything that
    /// leaves it no room. Bounded rather than repeated until it settles, because
    /// shapes covering the column would otherwise push a line down forever.
    /// </summary>
    private TextBand WrapBand(ref double y, double height, double width)
    {
        // The exclusions count down from the head of the page and PDF counts up
        // from its foot, so the axis is flipped on the way in and back out.
        double down = -y;
        TextBand band = _wrap.Resolve(ref down, height, width, out _);
        y = -down;
        return band;
    }

    private void Place(
        PdfLayoutPage page,
        LayoutLine line,
        double left,
        double available,
        double baseline,
        TextAlignment alignment,
        bool isLastLine)
    {
        double slack = available - line.Width;
        double wordSpacing = 0;
        if (alignment == TextAlignment.Justify && !isLastLine && slack > 0)
        {
            int spaces = CountSpaces(line);
            if (spaces > 0)
                wordSpacing = slack / spaces;
        }

        double x = alignment switch
        {
            TextAlignment.Center => left + (slack / 2),
            TextAlignment.Right => left + slack,
            _ => left,
        };

        // Never start left of the margin, however the alignment arithmetic came out.
        if (x < left)
            x = left;

        foreach (LayoutPiece piece in line.Pieces)
        {
            int spacesHere = CountSpaces(piece.Text);
            if (piece.Text.Length > 0)
            {
                page.Runs.Add(new PdfPlacedRun(
                    piece.Text,
                    x,
                    baseline,
                    piece.Width + (spacesHere * wordSpacing),
                    piece.FontSize,
                    piece.Font,
                    piece.Color,
                    piece.Background,
                    piece.Underline,
                    piece.Strikethrough,
                    piece.LinkHref,
                    wordSpacing));
            }

            // Each run carries an absolute x, so a run has to start past the extra
            // width word spacing gave the spaces before it on this line.
            x += piece.Width + (spacesHere * wordSpacing);
        }
    }

    /// <summary>The spaces a line can stretch: PDF word spacing applies to the space byte.</summary>
    private static int CountSpaces(LayoutLine line)
    {
        int spaces = 0;
        foreach (LayoutPiece piece in line.Pieces)
            spaces += CountSpaces(piece.Text);

        return spaces;
    }

    private static int CountSpaces(string text)
    {
        int spaces = 0;
        foreach (char c in text)
        {
            if (c == ' ')
                spaces++;
        }

        return spaces;
    }

    // ---- line breaking --------------------------------------------------------

    private List<LayoutLine> BreakParagraph(RichTextParagraph paragraph, double available, string marker) =>
        BreakParagraph(paragraph, _ => new TextBand(0, available), marker, bands: null);

    /// <remarks>
    /// The width is asked for per line rather than given once, because a wrapping
    /// shape leaves each line a different amount of room depending on where it
    /// lands. <paramref name="bands"/> collects what each line was given, so the
    /// caller can place it at the edge it was wrapped to.
    /// </remarks>
    private List<LayoutLine> BreakParagraph(
        RichTextParagraph paragraph,
        Func<int, TextBand> bandFor,
        string marker,
        List<TextBand>? bands)
    {
        var lines = new List<LayoutLine>();
        var current = new LayoutLine();
        double used = 0;

        TextBand band = bandFor(0);
        bands?.Add(band);
        double available = Math.Max(1, band.Width);

        void StartLine()
        {
            band = bandFor(lines.Count);
            bands?.Add(band);
            available = Math.Max(1, band.Width);
        }

        foreach (Word enumerated in EnumerateWords(paragraph, marker))
        {
            _cancellationToken.ThrowIfCancellationRequested();

            // What a tab is worth is the distance to the stop it lands on, so it
            // can only be measured here, where the line's used width is known.
            Word word = enumerated.IsTab
                ? enumerated.WithText(string.Empty, NextTabStop(used) - used)
                : enumerated;

            double wordWidth = word.Width;
            bool fits = used + wordWidth <= available || current.Pieces.Count == 0;

            if (!fits)
            {
                TrimTrailingSpace(current);
                lines.Add(current);
                current = new LayoutLine();
                used = 0;
                StartLine();

                // A word wider than the whole line is broken by character; the
                // alternative is a run that overflows the page.
                if (wordWidth > available)
                {
                    foreach (Word part in SplitOversizedWord(word, available))
                    {
                        if (used + part.Width > available && current.Pieces.Count > 0)
                        {
                            lines.Add(current);
                            current = new LayoutLine();
                            used = 0;
                            StartLine();
                        }

                        Append(current, part);
                        used += part.Width;
                    }

                    continue;
                }

                if (word.IsSpace)
                    continue; // do not start a line with the space that wrapped
            }

            Append(current, word);
            used += wordWidth;
        }

        TrimTrailingSpace(current);
        if (current.Pieces.Count > 0)
            lines.Add(current);

        return lines;
    }

    private static void Append(LayoutLine line, Word word)
    {
        LayoutPiece? last = line.Pieces.Count > 0 ? line.Pieces[^1] : null;

        // A tab is a gap, not glyphs, so it stays its own piece: merging it into
        // its neighbours would fold its width into a run the viewer sets from the
        // font's own advances, and the text after it would close the gap up.
        if (last is not null && !last.IsTab && !word.IsTab && last.SameStyleAs(word))
        {
            line.Pieces[^1] = last.Extend(word.Text, word.Width);
        }
        else
        {
            line.Pieces.Add(word.ToPiece());
        }

        line.Width += word.Width;
        line.Height = Math.Max(line.Height, word.FontSize * 1.2);
    }

    // Trailing spaces are dropped one at a time so alignment measures the line's
    // visible width rather than the width of the space that ended it.
    private void TrimTrailingSpace(LayoutLine line)
    {
        while (line.Pieces.Count > 0)
        {
            LayoutPiece last = line.Pieces[^1];
            if (last.IsTab)
            {
                line.Width -= last.Width;
                line.Pieces.RemoveAt(line.Pieces.Count - 1);
                continue;
            }

            if (last.Text.Length == 0 || !IsBreakSpace(last.Text[^1]))
                return;

            double spaceWidth = CharacterWidth(last.Font, last.Text[^1], last.FontSize);
            LayoutPiece trimmed = last.WithoutLastCharacter(spaceWidth);
            line.Width -= spaceWidth;
            if (trimmed.Text.Length == 0)
                line.Pieces.RemoveAt(line.Pieces.Count - 1);
            else
                line.Pieces[^1] = trimmed;
        }
    }

    private IEnumerable<Word> SplitOversizedWord(Word word, double available)
    {
        var builder = new StringBuilder();
        double width = 0;

        foreach (char c in word.Text)
        {
            double advance = CharacterWidth(word.Font, c, word.FontSize);
            if (builder.Length > 0 && width + advance > available)
            {
                yield return word.WithText(builder.ToString(), width);
                builder.Clear();
                width = 0;
            }

            builder.Append(c);
            width += advance;
        }

        if (builder.Length > 0)
            yield return word.WithText(builder.ToString(), width);
    }

    /// <summary>
    /// Walks a paragraph's runs and yields words, keeping each word's style. A
    /// trailing space belongs to the word before it, so a line break falls between
    /// words rather than in front of a space.
    /// </summary>
    private IEnumerable<Word> EnumerateWords(RichTextParagraph paragraph, string marker)
    {
        string text = paragraph.Text;
        int offset = 0;

        if (marker.Length > 0)
        {
            InlineStyle markerStyle = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Style : InlineStyle.Default;
            RunStyle resolved = Resolve(markerStyle);
            yield return MakeWord(marker, resolved, isSpace: false);
        }

        foreach (StyleRun run in paragraph.Runs)
        {
            if (offset >= text.Length)
                break;

            int length = Math.Min(run.Length, text.Length - offset);
            string runText = text.Substring(offset, length);
            offset += length;

            if (run.Style.IsImage)
            {
                _diagnostics.Skipped(
                    PdfDiagnosticCodes.WriteImageNotComposed,
                    "An inline image was dropped. This build composes no image emitter, so images are omitted rather than rasterized or transcoded.");
                continue;
            }

            RunStyle style = Resolve(run.Style);
            foreach (Word word in SplitWords(runText, style))
                yield return word;
        }
    }

    private IEnumerable<Word> SplitWords(string text, RunStyle style)
    {
        int index = 0;
        while (index < text.Length)
        {
            // A tab is its own word: its width is the distance to the tab stop it
            // reaches, so it can be neither absorbed into the word in front of it
            // nor measured from the font.
            if (text[index] == '\t')
            {
                index++;
                yield return Word.Tab(style);
                continue;
            }

            int start = index;
            while (index < text.Length && !IsBreakSpace(text[index]))
                index++;

            // Absorb the run of spaces that follows the word.
            int wordEnd = index;
            while (index < text.Length && text[index] == ' ')
                index++;

            if (index == start)
            {
                index++;
                continue;
            }

            string piece = text[start..index];
            yield return MakeWord(piece, style, isSpace: wordEnd == start);
        }
    }

    // A non-breaking space is deliberately not a break opportunity: it is the one
    // space a document uses to say "do not wrap here".
    private static bool IsBreakSpace(char c) => c is ' ' or '\t';

    /// <summary>
    /// The width a line has used once the tab reaching <paramref name="used"/> has
    /// landed: the first tab stop strictly past it, so a tab always moves the text
    /// along even when it starts exactly on a stop.
    /// </summary>
    private static double NextTabStop(double used) =>
        (Math.Floor(Math.Max(0, used) / TabStopWidth) + 1) * TabStopWidth;

    private Word MakeWord(string text, RunStyle style, bool isSpace)
    {
        string encodable = MakeEncodable(text);
        double width = 0;
        foreach (char c in encodable)
            width += CharacterWidth(style.Font, c, style.FontSize);
        return new Word(encodable, width, style, isSpace);
    }

    private double CharacterWidth(PdfStandardFont font, char character, double fontSize) =>
        _metrics.GetAdvanceWidth(font, character) / 1000d * fontSize;

    /// <summary>
    /// Replaces characters the writer's WinAnsi encoding cannot represent. A
    /// Unicode-capable writer needs embedded composite fonts, which is a separate
    /// reviewed step; until then the substitution is reported rather than silent.
    /// </summary>
    private string MakeEncodable(string text)
    {
        StringBuilder? builder = null;
        for (int i = 0; i < text.Length; i++)
        {
            if (PdfWinAnsiEncoder.CanEncode(text[i]))
            {
                builder?.Append(text[i]);
                continue;
            }

            builder ??= new StringBuilder(text.Length).Append(text, 0, i);
            builder.Append('?');

            // Two different failures wearing one symptom. Nothing provisioned is
            // something the caller can fix by provisioning; a provisioned set
            // that still cannot carry the text is a limit of this build. §11.3
            // asks for the first to be said out loud, because a preflight that
            // reports only "characters dropped" tells nobody what to do.
            if (_options.Fonts.IsEmpty)
            {
                _diagnostics.Skipped(
                    PdfDiagnosticCodes.WriteNoFontConfigured,
                    "Some characters are outside the writer's WinAnsi encoding and were replaced. " +
                    "No font was provisioned for this write, and this build bundles none: writing them " +
                    "needs a caller-supplied font, supplied through the write options.");
            }
            else
            {
                _diagnostics.Skipped(
                    PdfDiagnosticCodes.WriteCharacterUnsupported,
                    "Some characters are outside the writer's WinAnsi encoding and were replaced. Writing them needs an embedded composite font, which this build does not compose.");
            }
        }

        return builder?.ToString() ?? text;
    }

    private RunStyle Resolve(InlineStyle style)
    {
        PdfFontFamilyKind family = ClassifyFamily(style.FontFamily);
        PdfStandardFont font = PdfStandardFonts.Select(family, style.Bold, style.Italic);
        double size = style.FontSize is > 0 ? style.FontSize.Value : _options.DefaultFontSize;

        string? href = null;
        if (style.IsLink)
        {
            if (_uriPolicy.TryAdmit(style.LinkHref, out string canonical, out string? reason))
            {
                href = canonical;
            }
            else
            {
                _diagnostics.Skipped(
                    PdfDiagnosticCodes.UriRejected,
                    $"A link target was not emitted because {reason ?? "it failed the active URI policy"}. The text was written without an annotation.");
            }
        }

        BColor foreground = style.Foreground.IsEmpty ? BColor.Black : style.Foreground;
        return new RunStyle(font, size, foreground, style.Background, style.Underline, style.Strikethrough, href);
    }

    /// <summary>
    /// Maps a model family name onto one of the three logical families the
    /// standard fonts cover. The match is on the family the document names, not
    /// on any font installed on the machine — nothing here consults the host.
    /// </summary>
    private PdfFontFamilyKind ClassifyFamily(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
            return _options.DefaultFamily;

        string name = family.Trim();
        if (PdfStandardFonts.TryParse(name, out PdfStandardFont standard))
        {
            return standard switch
            {
                PdfStandardFont.TimesRoman or PdfStandardFont.TimesBold
                    or PdfStandardFont.TimesItalic or PdfStandardFont.TimesBoldItalic => PdfFontFamilyKind.Serif,
                PdfStandardFont.Courier or PdfStandardFont.CourierBold
                    or PdfStandardFont.CourierOblique or PdfStandardFont.CourierBoldOblique => PdfFontFamilyKind.Monospace,
                _ => PdfFontFamilyKind.SansSerif,
            };
        }

        if (name.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Console", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Courier", StringComparison.OrdinalIgnoreCase))
            return PdfFontFamilyKind.Monospace;

        if (name.Contains("Serif", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("Sans", StringComparison.OrdinalIgnoreCase))
            return PdfFontFamilyKind.Serif;

        if (name.Contains("Times", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Georgia", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Garamond", StringComparison.OrdinalIgnoreCase))
            return PdfFontFamilyKind.Serif;

        return _options.DefaultFamily;
    }

    // ---- layout value types ---------------------------------------------------

    private readonly struct RunStyle
    {
        public RunStyle(
            PdfStandardFont font,
            double fontSize,
            BColor color,
            BColor background,
            bool underline,
            bool strikethrough,
            string? linkHref)
        {
            Font = font;
            FontSize = fontSize;
            Color = color;
            Background = background;
            Underline = underline;
            Strikethrough = strikethrough;
            LinkHref = linkHref;
        }

        public PdfStandardFont Font { get; }

        public double FontSize { get; }

        public BColor Color { get; }

        public BColor Background { get; }

        public bool Underline { get; }

        public bool Strikethrough { get; }

        public string? LinkHref { get; }

        public bool Matches(RunStyle other) =>
            Font == other.Font && FontSize.Equals(other.FontSize) && Color == other.Color &&
            Background == other.Background && Underline == other.Underline &&
            Strikethrough == other.Strikethrough &&
            string.Equals(LinkHref, other.LinkHref, StringComparison.Ordinal);
    }

    private readonly struct Word
    {
        public Word(string text, double width, RunStyle style, bool isSpace, bool isTab = false)
        {
            Text = text;
            Width = width;
            Style = style;
            IsSpace = isSpace;
            IsTab = isTab;
        }

        /// <summary>
        /// A tab, carrying its style and no width yet: the line it lands on is what
        /// decides how far it reaches.
        /// </summary>
        public static Word Tab(RunStyle style) => new(string.Empty, 0, style, isSpace: true, isTab: true);

        public string Text { get; }

        public double Width { get; }

        public RunStyle Style { get; }

        /// <summary>True when the word is only whitespace.</summary>
        public bool IsSpace { get; }

        /// <summary>True for a tab: a gap of measured width that draws no glyphs.</summary>
        public bool IsTab { get; }

        public PdfStandardFont Font => Style.Font;

        public double FontSize => Style.FontSize;

        public Word WithText(string text, double width) => new(text, width, Style, IsSpace, IsTab);

        public LayoutPiece ToPiece() => new(Text, Width, Style, IsTab);
    }

    private sealed class LayoutPiece
    {
        public LayoutPiece(string text, double width, RunStyle style, bool isTab = false)
        {
            Text = text;
            Width = width;
            Style = style;
            IsTab = isTab;
        }

        public string Text { get; }

        /// <summary>True for a tab: a gap of measured width that draws no glyphs.</summary>
        public bool IsTab { get; }

        public double Width { get; }

        public RunStyle Style { get; }

        public PdfStandardFont Font => Style.Font;

        public double FontSize => Style.FontSize;

        public BColor Color => Style.Color;

        public BColor Background => Style.Background;

        public bool Underline => Style.Underline;

        public bool Strikethrough => Style.Strikethrough;

        public string? LinkHref => Style.LinkHref;

        public bool SameStyleAs(Word word) => Style.Matches(word.Style);

        public LayoutPiece Extend(string text, double width) =>
            new(Text + text, Width + width, Style);

        public LayoutPiece WithoutLastCharacter(double width) =>
            new(Text[..^1], Width - width, Style);
    }

    private sealed class LayoutLine
    {
        public List<LayoutPiece> Pieces { get; } = [];

        public double Width { get; set; }

        public double Height { get; set; }
    }
}
