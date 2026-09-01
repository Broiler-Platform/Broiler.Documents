using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.Model;
using Broiler.Graphics;

namespace Broiler.Documents.Cli.Rendering;

/// <summary>
/// Turns a <see cref="RichTextDocument"/> into positioned lines on pages.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is.</b> A deterministic paragraph layout: word wrapping,
/// alignment, indents, list markers, line and paragraph spacing, inline images,
/// tables, and pagination. It measures through <see cref="BTextMeasurer"/>,
/// which is the same path the renderer advances its pen along, so what was
/// measured is what gets drawn.
/// </para>
/// <para>
/// <b>What this is not.</b> It is not a word processor's layout engine and does
/// not try to be one. There are no columns, floats, footnotes, hyphenation,
/// kerning pairs, or bidirectional reordering here - mostly because the document
/// model has no way to express them, so there is nothing to lay out. Tables
/// break between rows and never inside one. The shared paginator the PDF roadmap
/// tracks as
/// <c>Broiler.Documents.Pagination</c> is where a component-level version of
/// this belongs; until it exists this is an application head's own layout, and
/// its numbers are this tool's, not the component's.
/// </para>
/// <para>
/// <b>Why that is still useful for finding gaps.</b> A comparison between two
/// exports run through <em>this same</em> layout isolates the codecs: identical
/// geometry on both sides means every pixel that differs came from the document
/// model, which is to say from the reader or the writer under test.
/// </para>
/// </remarks>
public sealed class DocumentLayout
{
    private readonly LayoutSettings _settings;
    private readonly ImageStore _images;
    private readonly List<string> _notes = new();
    private RunningContent _running = RunningContent.Empty;
    private IReadOnlyList<DocumentShape> _documentShapes = [];
    private readonly Dictionary<int, double> _paragraphTops = [];

    public DocumentLayout(LayoutSettings settings, ImageStore images)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _images = images ?? throw new ArgumentNullException(nameof(images));
    }

    /// <summary>Lays the document out onto pages of the given size.</summary>
    public LayoutResult Layout(RichTextDocument document, PageSetup setup)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(setup);

        _notes.Clear();
        // Continuous mode collapses the document to one tall page, so there are no
        // pages for a header to repeat on and no bottom margin for a footer to sit
        // in. Drawing them there would put a page number in the middle of nothing.
        _running = setup.Continuous ? RunningContent.Empty : document.RunningContent;
        _documentShapes = document.Shapes;
        _paragraphTops.Clear();

        // A continuous render has no page break to place, so it lays out against
        // an effectively unbounded column and then shrinks the page to the
        // content. That is the form to reach for when comparing two exports:
        // with pagination on, one extra line before a break moves every
        // subsequent page and a one-line difference reads as a whole-document one.
        double columnHeight = setup.Continuous ? double.MaxValue : setup.ContentHeightPoints;

        var pages = new List<LayoutPage>();
        var currentLines = new List<LayoutLine>();
        var currentCells = new List<LayoutCell>();
        double y = setup.ContentTopPoints;
        double contentBottom = setup.ContentTopPoints + columnHeight;
        bool truncated = false;

        var numbering = new ListNumbering();

        void BreakPage()
        {
            pages.Add(NewPage(pages.Count + 1, setup, currentLines, currentCells));
            currentLines = new List<LayoutLine>();
            currentCells = new List<LayoutCell>();
            y = setup.ContentTopPoints;
            if (pages.Count >= _settings.MaxPages)
                truncated = true;
        }

        int paragraphIndex = 0;
        while (paragraphIndex < document.ParagraphCount && !truncated)
        {
            if (DocumentTable.StartingAt(document.Tables, paragraphIndex) is DocumentTable table)
            {
                // A row is placed whole. Its cells are laid out beside each other,
                // so a row broken across a page would have to break every cell at
                // the same place - which is a table layout, and this is not one.
                foreach (RowBox row in ComposeTable(table, document, setup, setup.ContentLeftPoints, setup.ContentWidthPoints))
                {
                    if (y + row.Height > contentBottom && currentLines.Count > 0)
                    {
                        BreakPage();
                        if (truncated)
                            break;
                    }

                    row.PlaceAt(y, currentLines, currentCells, _paragraphTops);
                    y += row.Height;
                }

                paragraphIndex = table.ParagraphEnd;
                continue;
            }

            RichTextParagraph paragraph = document.Paragraphs[paragraphIndex];
            ParagraphStyle style = paragraph.Style;
            string? marker = numbering.Advance(style);

            ParagraphLines composed = ComposeParagraph(paragraph, marker, setup, paragraphIndex);
            _paragraphTops.TryAdd(paragraphIndex, y);

            // Space before never opens a page: a paragraph that starts a page
            // starts at the top margin, the way every page-based renderer does it.
            if (currentLines.Count > 0)
                y += Math.Max(0, style.SpacingBefore);

            foreach (LayoutLine line in composed.Lines)
            {
                if (y + line.Height > contentBottom && currentLines.Count > 0)
                {
                    BreakPage();
                    if (truncated)
                        break;
                }

                line.Top = y;
                currentLines.Add(line);
                y += line.Height;
            }

            y += Math.Max(0, style.SpacingAfter);
            paragraphIndex++;
        }

        if (currentLines.Count > 0 || currentCells.Count > 0 || pages.Count == 0)
            pages.Add(NewPage(pages.Count + 1, setup, currentLines, currentCells));

        if (truncated)
        {
            _notes.Add(string.Format(
                CultureInfo.InvariantCulture,
                "stopped at the --max-pages limit of {0}; the document has more content.",
                _settings.MaxPages));
        }

        PageSetup finalSetup = setup;
        if (setup.Continuous)
        {
            double used = pages[0].Lines.Count > 0
                ? pages[0].Lines[^1].Top + pages[0].Lines[^1].Height
                : setup.ContentTopPoints;
            finalSetup = setup.WithHeight(used + setup.MarginBottomPoints);
            pages = new List<LayoutPage>
            {
                new(
                    1,
                    finalSetup.WidthPoints,
                    finalSetup.HeightPoints,
                    pages[0].Lines.ToList(),
                    pages[0].Shapes.ToList(),
                    pages[0].Cells.ToList()),
            };
        }

        return new LayoutResult(pages, finalSetup, _notes, truncated);
    }

    private LayoutPage NewPage(int number, PageSetup setup, List<LayoutLine> lines, List<LayoutCell> cells)
    {
        // Page one takes the first-page selection and even-numbered pages the
        // even one, each falling back to the default.
        PageSelection selection = number == 1
            ? PageSelection.First
            : number % 2 == 0 ? PageSelection.Even : PageSelection.Default;

        var all = new List<LayoutLine>(lines);
        all.AddRange(RunningLines(
            _running.EffectiveHeader(selection),
            setup,
            top: 0,
            band: setup.MarginTopPoints));
        all.AddRange(RunningLines(
            _running.EffectiveFooter(selection),
            setup,
            top: setup.ContentTopPoints + setup.ContentHeightPoints,
            band: setup.MarginBottomPoints));

        List<LayoutShape> shapes = PlaceShapes(setup);
        shapes.AddRange(PlaceRunningShapes(_running.EffectiveHeaderShapes(selection), setup));
        shapes.AddRange(PlaceRunningShapes(_running.EffectiveFooterShapes(selection), setup));

        return new LayoutPage(number, setup.WidthPoints, setup.HeightPoints, all, shapes, cells);
    }

    /// <summary>
    /// Places a running band's shapes on every page it applies to. There is no
    /// paragraph to hang one from - a header repeats - so the offset is measured
    /// from the top of the page.
    /// </summary>
    private List<LayoutShape> PlaceRunningShapes(IReadOnlyList<DocumentShape> shapes, PageSetup setup)
    {
        var placed = new List<LayoutShape>();
        foreach (DocumentShape shape in shapes)
        {
            var bounds = new BRect(
                setup.ContentLeftPoints + shape.OffsetX,
                shape.OffsetY,
                Math.Max(0, shape.Width),
                Math.Max(0, shape.Height));
            if (bounds.Width <= 0 || bounds.Height <= 0)
                continue;

            placed.Add(new LayoutShape(
                bounds,
                shape.Fill,
                shape.Outline,
                PlaceShapeText(shape, bounds, setup),
                shape.Image,
                shape.BehindText));
        }

        return placed;
    }

    /// <summary>
    /// Places the document's floating shapes for a page.
    /// </summary>
    /// <remarks>
    /// A shape's x is measured from the text column's left edge - which is how a
    /// letterhead's stripe sits in the margin without any page geometry - and its
    /// y from the top of the paragraph it is anchored to. A shape whose paragraph
    /// has not been laid out on this page is not drawn on it.
    /// </remarks>
    private List<LayoutShape> PlaceShapes(PageSetup setup)
    {
        var placed = new List<LayoutShape>();
        if (_documentShapes.Count == 0)
            return placed;

        foreach (DocumentShape shape in _documentShapes)
        {
            if (!_paragraphTops.TryGetValue(shape.ParagraphIndex, out double anchorTop))
                continue;

            var bounds = new BRect(
                setup.ContentLeftPoints + shape.OffsetX,
                anchorTop + shape.OffsetY,
                Math.Max(0, shape.Width),
                Math.Max(0, shape.Height));
            if (bounds.Width <= 0 || bounds.Height <= 0)
                continue;

            placed.Add(new LayoutShape(
                bounds,
                shape.Fill,
                shape.Outline,
                PlaceShapeText(shape, bounds, setup),
                shape.Image,
                shape.BehindText));
        }

        return placed;
    }

    /// <summary>
    /// Lays a table out row by row, each row's lines and cell boxes positioned
    /// relative to the row's own top so the caller can place it wherever it lands
    /// on the page.
    /// </summary>
    /// <remarks>
    /// Every row is composed before any is placed, because a cell that spans rows
    /// needs the heights of the rows below it before its box can be drawn.
    /// </remarks>
    private List<RowBox> ComposeTable(
        DocumentTable table,
        RichTextDocument document,
        PageSetup setup,
        double left,
        double width)
    {
        double[] edges = ColumnEdges(table, left, width);
        var boxes = new List<RowBox>(table.Rows.Count);

        foreach (TableRow row in table.Rows)
        {
            var box = new RowBox();
            foreach (TableCell cell in row.Cells)
            {
                (double cellLeft, double cellWidth) = ColumnSpanBox(edges, cell);
                double textWidth = Math.Max(1.0, cellWidth - (table.CellPadding * 2));
                (List<LayoutLine> lines, List<LayoutCell> nested, double height) = ComposeBlocks(
                    document,
                    cell.Tables,
                    cell.ParagraphIndex,
                    cell.ParagraphEnd,
                    setup,
                    cellLeft + table.CellPadding,
                    textWidth);

                box.Lines.AddRange(lines);
                box.Cells.AddRange(nested);
                box.Height = Math.Max(box.Height, height);

                // A cell the merge above it covers is drawn by no one: the cell
                // that opened the merge draws the whole of it.
                if (!cell.IsRowSpanContinuation)
                    box.Boxes.Add(new CellBox(cellLeft, cellWidth, cell));
            }

            boxes.Add(box);
        }

        ExtendRowSpans(boxes);
        return boxes;
    }

    /// <summary>
    /// Grows a spanning cell's box down over the rows it covers, now that their
    /// heights are known.
    /// </summary>
    private static void ExtendRowSpans(List<RowBox> rows)
    {
        for (int r = 0; r < rows.Count; r++)
        {
            foreach (CellBox box in rows[r].Boxes)
            {
                if (box.Cell.RowSpan <= 1)
                    continue;

                double extra = 0;
                int last = Math.Min(rows.Count, r + box.Cell.RowSpan);
                for (int below = r + 1; below < last; below++)
                    extra += rows[below].Height;

                box.ExtraHeight = extra;
            }
        }
    }

    /// <summary>
    /// The x of every column boundary, left to right. A grid wider than the space
    /// it has is scaled to fit rather than drawn off the page, and a table that
    /// states no grid divides what it has evenly - which is what a word processor
    /// does with one.
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
            double column = i < table.ColumnWidths.Count && table.ColumnWidths[i] > 0
                ? table.ColumnWidths[i] * scale
                : width / columns;
            x += column;
            edges[i + 1] = x;
        }

        return edges;
    }

    /// <summary>How many columns the grid has: what it states, or what its widest row uses.</summary>
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
        return (edges[start], Math.Max(1.0, edges[end] - edges[start]));
    }

    /// <summary>
    /// Lays out a range of block content inside a box, with every top measured
    /// from the box rather than from the page. A table inside it goes through
    /// <see cref="ComposeTable"/>, which comes back here for its cells - so
    /// nesting costs nothing beyond the recursion.
    /// </summary>
    private (List<LayoutLine> Lines, List<LayoutCell> Cells, double Height) ComposeBlocks(
        RichTextDocument document,
        IReadOnlyList<DocumentTable> tables,
        int from,
        int to,
        PageSetup setup,
        double left,
        double width)
    {
        var lines = new List<LayoutLine>();
        var cells = new List<LayoutCell>();
        var numbering = new ListNumbering();
        double y = 0;

        int index = Math.Max(0, from);
        while (index < Math.Min(to, document.ParagraphCount))
        {
            if (DocumentTable.StartingAt(tables, index) is DocumentTable nested)
            {
                foreach (RowBox row in ComposeTable(nested, document, setup, left, width))
                {
                    row.PlaceAt(y, lines, cells, _paragraphTops);
                    y += row.Height;
                }

                index = nested.ParagraphEnd;
                continue;
            }

            RichTextParagraph paragraph = document.Paragraphs[index];
            ParagraphLines composed = ComposeParagraph(
                paragraph,
                numbering.Advance(paragraph.Style),
                setup,
                index,
                left,
                width);

            if (lines.Count > 0)
                y += Math.Max(0, paragraph.Style.SpacingBefore);

            foreach (LayoutLine line in composed.Lines)
            {
                line.Top = y;
                lines.Add(line);
                y += line.Height;
            }

            y += Math.Max(0, paragraph.Style.SpacingAfter);
            index++;
        }

        return (lines, cells, y);
    }

    /// <summary>A cell's box within its row, before the row is placed on a page.</summary>
    private sealed class CellBox
    {
        public CellBox(double left, double width, TableCell cell)
        {
            Left = left;
            Width = width;
            Cell = cell;
        }

        public double Left { get; }

        public double Width { get; }

        public TableCell Cell { get; }

        /// <summary>How far past its own row the box reaches, for a cell that spans rows.</summary>
        public double ExtraHeight { get; set; }
    }

    /// <summary>
    /// One composed table row: its lines and any nested cell boxes, positioned
    /// relative to the row's top, and the cells whose boxes this row draws.
    /// </summary>
    private sealed class RowBox
    {
        public double Height { get; set; }

        public List<LayoutLine> Lines { get; } = [];

        public List<LayoutCell> Cells { get; } = [];

        public List<CellBox> Boxes { get; } = [];

        /// <summary>Moves the row to <paramref name="top"/> and hands it to the page.</summary>
        public void PlaceAt(
            double top,
            List<LayoutLine> lines,
            List<LayoutCell> cells,
            Dictionary<int, double> paragraphTops)
        {
            foreach (CellBox box in Boxes)
            {
                cells.Add(new LayoutCell(
                    new BRect(box.Left, top, box.Width, Height + box.ExtraHeight),
                    box.Cell.Shading,
                    box.Cell.Borders));
            }

            foreach (LayoutCell cell in Cells)
            {
                cells.Add(new LayoutCell(
                    new BRect(cell.Bounds.Left, cell.Bounds.Top + top, cell.Bounds.Width, cell.Bounds.Height),
                    cell.Shading,
                    cell.Borders));
            }

            foreach (LayoutLine line in Lines)
            {
                line.Top += top;
                lines.Add(line);
                paragraphTops.TryAdd(line.ParagraphIndex, line.Top);
            }
        }
    }

    /// <summary>A shape's own text, laid out inside its box.</summary>
    private List<LayoutLine> PlaceShapeText(DocumentShape shape, BRect bounds, PageSetup setup)
    {
        var lines = new List<LayoutLine>();
        if (!shape.HasText || bounds.Width <= 0)
            return lines;

        double y = bounds.Top;
        foreach (RichTextParagraph paragraph in shape.Paragraphs)
        {
            ParagraphLines composed = ComposeParagraph(
                paragraph, marker: null, setup, -1, bounds.Left, bounds.Width);

            foreach (LayoutLine line in composed.Lines)
            {
                // Text that overflows its box is clipped rather than spilling
                // across the letter it sits beside.
                if (y + line.Height > bounds.Bottom)
                    break;

                line.Top = y;
                lines.Add(line);
                y += line.Height;
            }
        }

        return lines;
    }

    /// <summary>
    /// Lays a header or footer out inside the margin band it belongs to, centred
    /// in it. The model carries no header distance - no reader produces one - so
    /// this is a convention rather than a setting. A block taller than its band is
    /// reported instead of being drawn across the body.
    /// </summary>
    private List<LayoutLine> RunningLines(
        IReadOnlyList<RichTextParagraph> paragraphs,
        PageSetup setup,
        double top,
        double band)
    {
        var lines = new List<LayoutLine>();
        if (paragraphs.Count == 0)
            return lines;

        double height = 0;
        foreach (RichTextParagraph paragraph in paragraphs)
        {
            foreach (LayoutLine line in ComposeParagraph(paragraph, marker: null, setup, -1).Lines)
            {
                lines.Add(line);
                height += line.Height;
            }
        }

        if (height > band)
        {
            _notes.Add("a header or footer was taller than its page margin and was not drawn.");
            return new List<LayoutLine>();
        }

        double y = top + ((band - height) / 2);
        foreach (LayoutLine line in lines)
        {
            line.Top = y;
            y += line.Height;
        }

        return lines;
    }

    /// <summary>Wraps one paragraph into lines, without deciding which page they land on.</summary>
    private ParagraphLines ComposeParagraph(
        RichTextParagraph paragraph,
        string? marker,
        PageSetup setup,
        int paragraphIndex,
        double? columnLeftOverride = null,
        double? columnWidthOverride = null)
    {
        ParagraphStyle style = paragraph.Style;
        double indent = Math.Max(0, style.IndentLevel) * _settings.IndentStepPoints;
        // A shape's text is laid out inside the shape, not inside the page's text
        // column: a centred line in a logo box belongs on the box's midpoint.
        double columnLeft = (columnLeftOverride ?? setup.ContentLeftPoints) + indent;
        double columnWidth = Math.Max(
            1.0,
            (columnWidthOverride ?? setup.ContentWidthPoints) - indent);

        BFontStyle defaultFont = FontFor(InlineStyle.Default);
        LayoutPiece? markerPiece = null;
        double hang = 0;

        if (marker is not null)
        {
            InlineStyle markerStyle = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Style : InlineStyle.Default;
            markerPiece = MakeTextPiece(marker, markerStyle, decorateLink: false);
            hang = Math.Min(columnWidth * 0.5, markerPiece.Width + _settings.ListMarkerGapPoints);
        }

        double textLeft = columnLeft + hang;
        double textWidth = Math.Max(1.0, columnWidth - hang);

        List<Token> tokens = Tokenize(paragraph);
        List<List<LayoutPiece>> rows = Wrap(tokens, textWidth);

        var lines = new List<LayoutLine>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            List<LayoutPiece> pieces = rows[i];

            // The marker belongs to the first line only, and sits in the hanging
            // indent rather than in the text column, so wrapped lines align under
            // the text and not under the bullet.
            if (i == 0 && markerPiece is not null)
            {
                markerPiece.X = columnLeft;
                pieces.Insert(0, markerPiece);
            }

            lines.Add(PlaceLine(
                pieces,
                i == 0 && markerPiece is not null,
                textLeft,
                textWidth,
                style,
                defaultFont,
                paragraphIndex,
                i == rows.Count - 1));
        }

        return new ParagraphLines(lines);
    }

    /// <summary>The spaces a justified line can spend its slack on.</summary>
    private static int CountSpaces(List<LayoutPiece> pieces, int first)
    {
        int spaces = 0;
        for (int i = first; i < pieces.Count; i++)
            spaces += CountSpaces(pieces[i].Text);

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

    /// <summary>
    /// Positions one line's pieces, applies alignment, and computes the line box.
    /// </summary>
    private LayoutLine PlaceLine(
        List<LayoutPiece> pieces,
        bool hasMarker,
        double textLeft,
        double textWidth,
        ParagraphStyle style,
        BFontStyle defaultFont,
        int paragraphIndex,
        bool isLastLine)
    {
        double ascent = BTextMeasurer.Measure(string.Empty, defaultFont).Baseline;
        double descent = Math.Max(0, BTextMeasurer.GetLineHeight(defaultFont) - ascent);
        double used = 0;

        // The marker is placed already and sits outside the text column, so it
        // contributes to the line's height but not to the width alignment
        // distributes.
        int first = hasMarker ? 1 : 0;
        for (int i = 0; i < pieces.Count; i++)
        {
            ascent = Math.Max(ascent, pieces[i].Ascent);
            descent = Math.Max(descent, pieces[i].Descent);
            if (i >= first)
                used += pieces[i].Width;
        }

        double offset = style.Alignment switch
        {
            TextAlignment.Center => Math.Max(0, (textWidth - used) / 2),
            TextAlignment.Right => Math.Max(0, textWidth - used),
            _ => 0,
        };

        // Justification spends the slack on the line's own spaces instead of
        // moving the line. The last line of a paragraph keeps its slack: it ends
        // where the text ended, and stretching it would pull a short closing line
        // across the whole column. A line with nothing to stretch stays flush.
        double wordSpacing = 0;
        if (style.Alignment == TextAlignment.Justify && !isLastLine)
        {
            double slack = textWidth - used;
            int spaces = CountSpaces(pieces, first);
            if (slack > 0 && spaces > 0)
                wordSpacing = slack / spaces;
        }

        double x = textLeft + offset;
        for (int i = first; i < pieces.Count; i++)
        {
            pieces[i].X = x;
            // Widening the gap means starting the next piece further right; the
            // space glyph before it is drawn at its own width either way.
            x += pieces[i].Width + (CountSpaces(pieces[i].Text) * wordSpacing);
        }

        double natural = ascent + descent;
        double spacing = style.LineSpacing > 0 ? style.LineSpacing : 1f;

        // Extra leading goes below the baseline. Putting it above would push the
        // first line of every double-spaced paragraph down by half a line, which
        // is not what a document that says "line spacing 2" is asking for.
        return new LayoutLine(pieces, 0, natural * spacing, ascent, paragraphIndex);
    }

    /// <summary>
    /// Greedy first-fit wrapping. Break opportunities are whitespace runs; a
    /// single token wider than the column is split by character so that one long
    /// URL cannot push a page off its own right edge.
    /// </summary>
    private List<List<LayoutPiece>> Wrap(List<Token> tokens, double maxWidth)
    {
        var rows = new List<List<LayoutPiece>>();
        var current = new List<LayoutPiece>();
        var pendingSpace = new List<Token>();
        double currentWidth = 0;
        double pendingWidth = 0;

        void Flush()
        {
            rows.Add(current);
            current = new List<LayoutPiece>();
            currentWidth = 0;
            pendingSpace.Clear();
            pendingWidth = 0;
        }

        foreach (Token token in tokens)
        {
            if (token.IsWhitespace)
            {
                // Leading whitespace on a wrapped line is dropped; whitespace
                // inside a line is held back until a word arrives to justify it,
                // so a line never ends with a visible ragged space. A tab that
                // opens the paragraph is not that space — it is the indent the
                // author typed — so it is the one kind of leading gap that stays.
                if (current.Count == 0 && !(token.IsTab && rows.Count == 0))
                    continue;

                if (token.IsTab)
                {
                    double reached = currentWidth + pendingWidth;
                    token.ResolveTabWidth(NextTabStop(reached) - reached);
                }

                pendingSpace.Add(token);
                pendingWidth += token.Width;
                continue;
            }

            if (current.Count > 0 && currentWidth + pendingWidth + token.Width > maxWidth)
                Flush();

            if (current.Count == 0 && token.Width > maxWidth)
            {
                foreach (Token chunk in BreakToken(token, maxWidth))
                {
                    if (current.Count > 0 && currentWidth + chunk.Width > maxWidth)
                        Flush();

                    current.AddRange(chunk.Pieces);
                    currentWidth += chunk.Width;
                }

                continue;
            }

            foreach (Token space in pendingSpace)
            {
                current.AddRange(space.Pieces);
                currentWidth += space.Width;
            }

            pendingSpace.Clear();
            pendingWidth = 0;

            current.AddRange(token.Pieces);
            currentWidth += token.Width;
        }

        rows.Add(current);
        return rows;
    }

    /// <summary>Splits an over-wide token into chunks that fit, one character at a time.</summary>
    private IEnumerable<Token> BreakToken(Token token, double maxWidth)
    {
        foreach (LayoutPiece piece in token.Pieces)
        {
            if (piece.IsImage)
            {
                // An image cannot be broken, so an over-wide one is scaled to the
                // column instead. Letting it keep its size would put pixels past
                // the right margin, where the page clip silently eats them.
                yield return Token.Single(piece.Width > maxWidth ? ScaleToWidth(piece, maxWidth) : piece);
                continue;
            }

            if (piece.Width <= maxWidth)
            {
                yield return Token.Single(piece);
                continue;
            }

            var builder = new StringBuilder();
            double width = 0;

            foreach (char character in piece.Text)
            {
                double advance = BTextMeasurer.MeasureAdvance(character.ToString(), piece.Font);
                if (builder.Length > 0 && width + advance > maxWidth)
                {
                    yield return Token.Single(Retext(piece, builder.ToString(), width));
                    builder.Clear();
                    width = 0;
                }

                builder.Append(character);
                width += advance;
            }

            if (builder.Length > 0)
                yield return Token.Single(Retext(piece, builder.ToString(), width));
        }
    }

    /// <summary>The same image piece drawn narrower, keeping its aspect ratio.</summary>
    private static LayoutPiece ScaleToWidth(LayoutPiece piece, double width)
    {
        double factor = width / piece.Width;
        return new LayoutPiece(
            piece.Text,
            piece.Font,
            piece.Color,
            piece.Highlight,
            piece.Underline,
            piece.Strikethrough,
            piece.Link,
            piece.Image,
            width,
            piece.Ascent * factor,
            piece.Descent * factor);
    }

    private static LayoutPiece Retext(LayoutPiece source, string text, double width) => new(
        text,
        source.Font,
        source.Color,
        source.Highlight,
        source.Underline,
        source.Strikethrough,
        source.Link,
        null,
        width,
        source.Ascent,
        source.Descent);

    /// <summary>
    /// Splits a paragraph into wrap tokens. A word that spans two runs - "very"
    /// in one and "**bold**" in the next - stays one token, because a break
    /// between them would be a break in the middle of a word.
    /// </summary>
    private List<Token> Tokenize(RichTextParagraph paragraph)
    {
        var tokens = new List<Token>();
        Token? word = null;
        int offset = 0;

        foreach (StyleRun run in paragraph.Runs)
        {
            int length = Math.Min(run.Length, Math.Max(0, paragraph.Length - offset));
            if (length <= 0)
            {
                offset += run.Length;
                continue;
            }

            string text = paragraph.Text.Substring(offset, length);
            offset += run.Length;

            foreach ((string fragment, bool whitespace, bool image, bool tab) in Fragments(text, run.Style))
            {
                if (image)
                {
                    LayoutPiece piece = MakeImagePiece(run.Style);
                    word ??= Token.Empty();
                    word.Add(piece);
                    tokens.Add(word);
                    word = null;
                    continue;
                }

                if (tab)
                {
                    if (word is not null)
                    {
                        tokens.Add(word);
                        word = null;
                    }

                    tokens.Add(Token.Single(MakeTabPiece(run.Style), isWhitespace: true));
                    continue;
                }

                if (whitespace)
                {
                    if (word is not null)
                    {
                        tokens.Add(word);
                        word = null;
                    }

                    Token space = Token.Empty(isWhitespace: true);
                    foreach (LayoutPiece piece in MakePieces(fragment, run.Style))
                        space.Add(piece);
                    tokens.Add(space);
                    continue;
                }

                word ??= Token.Empty();
                foreach (LayoutPiece piece in MakePieces(fragment, run.Style))
                    word.Add(piece);
            }
        }

        if (word is not null)
            tokens.Add(word);

        return tokens;
    }

    /// <summary>Splits run text into whitespace runs, tabs, image placeholders, and words.</summary>
    private static IEnumerable<Fragment> Fragments(string text, InlineStyle style)
    {
        var builder = new StringBuilder();
        bool? whitespace = null;

        foreach (char character in text)
        {
            // A tab is neither a word nor part of a whitespace run: how wide it is
            // depends on where along its line it falls, so it stands alone, the way
            // an image placeholder does.
            bool isTab = character == '\t';
            if (isTab || (character == InlineImage.Placeholder && style.IsImage))
            {
                if (builder.Length > 0)
                {
                    yield return new Fragment(builder.ToString(), whitespace ?? false, false, false);
                    builder.Clear();
                }

                whitespace = null;
                yield return isTab
                    ? new Fragment(string.Empty, Whitespace: true, Image: false, Tab: true)
                    : new Fragment(string.Empty, Whitespace: false, Image: true, Tab: false);
                continue;
            }

            bool isSpace = char.IsWhiteSpace(character);
            if (whitespace is not null && isSpace != whitespace)
            {
                yield return new Fragment(builder.ToString(), whitespace.Value, false, false);
                builder.Clear();
            }

            whitespace = isSpace;
            builder.Append(character);
        }

        if (builder.Length > 0)
            yield return new Fragment(builder.ToString(), whitespace ?? false, false, false);
    }

    private readonly record struct Fragment(string Text, bool Whitespace, bool Image, bool Tab);

    /// <summary>
    /// The drawable pieces for a fragment. Usually one; small capitals produce
    /// two sizes and therefore more than one.
    /// </summary>
    private IEnumerable<LayoutPiece> MakePieces(string text, InlineStyle style)
    {
        if (style.Capitalization != TextCapitalization.SmallCaps)
        {
            yield return MakeTextPiece(Transform(text, style.Capitalization), style, _settings.DecorateLinks);
            yield break;
        }

        // Small capitals: letters the author typed in lower case are drawn as
        // capitals at a reduced size, everything else at full size. Splitting at
        // that boundary is what lets both halves be measured in the size they
        // are actually drawn in.
        int start = 0;
        bool? small = null;

        for (int i = 0; i <= text.Length; i++)
        {
            bool? current = i < text.Length ? char.IsLower(text[i]) : null;
            if (i == text.Length || (small is not null && current != small))
            {
                string slice = text[start..i].ToUpperInvariant();
                if (slice.Length > 0)
                    yield return MakeTextPiece(slice, style, _settings.DecorateLinks, small == true ? 0.8 : 1.0);
                start = i;
            }

            small = current;
        }
    }

    private LayoutPiece MakeTextPiece(string text, InlineStyle style, bool decorateLink, double sizeScale = 1.0)
    {
        BFontStyle font = FontFor(style, sizeScale);
        BColor color = ColorText.Or(style.Foreground, _settings.DefaultForeground);
        bool underline = style.Underline;

        if (decorateLink && style.IsLink)
        {
            underline = true;
            if (style.Foreground.IsEmpty)
                color = _settings.LinkColor;
        }

        double width = BTextMeasurer.MeasureAdvance(text, font);
        double ascent = font.SizeInPixels * 0.8;
        double descent = Math.Max(0, BTextMeasurer.GetLineHeight(font) - ascent);

        // Shearing is a fallback for a family with no designed italic face. It
        // does not change the advance, so nothing in the layout moves either way.
        bool oblique = style.Italic &&
            _settings.SynthesizeItalic &&
            !(_settings.ItalicFaceAvailable?.Invoke(font.FamilyName) ?? false);

        return new LayoutPiece(
            text,
            font,
            color,
            style.Background,
            underline,
            style.Strikethrough,
            style.LinkHref,
            null,
            width,
            ascent,
            descent,
            oblique);
    }

    /// <summary>
    /// A tab: a gap of no width yet, carrying its run's font so the line it lands
    /// on is as tall as the run and any highlight behind it is the run's colour.
    /// Wrapping fills the width in once it knows where the tab starts.
    /// </summary>
    private LayoutPiece MakeTabPiece(InlineStyle style)
    {
        BFontStyle font = FontFor(style);
        double ascent = font.SizeInPixels * 0.8;

        return new LayoutPiece(
            string.Empty,
            font,
            ColorText.Or(style.Foreground, _settings.DefaultForeground),
            style.Background,
            underline: false,
            strikethrough: false,
            style.LinkHref,
            null,
            0,
            ascent,
            Math.Max(0, BTextMeasurer.GetLineHeight(font) - ascent),
            oblique: false,
            isTab: true);
    }

    /// <summary>
    /// The width a line has used once a tab reaching <paramref name="used"/> has
    /// landed: the first tab stop strictly past it, so a tab always moves the text
    /// along even when it starts exactly on a stop.
    /// </summary>
    private double NextTabStop(double used)
    {
        double stop = _settings.TabStopPoints > 0 ? _settings.TabStopPoints : 36.0;
        return (Math.Floor(Math.Max(0, used) / stop) + 1) * stop;
    }

    private LayoutPiece MakeImagePiece(InlineStyle style)
    {
        InlineImage image = style.Image!;
        (double width, double height) = _images.MeasurePoints(image);

        return new LayoutPiece(
            string.Empty,
            FontFor(style),
            ColorText.Or(style.Foreground, _settings.DefaultForeground),
            style.Background,
            false,
            false,
            style.LinkHref,
            image,
            Math.Max(1, width),
            Math.Max(1, height),
            0);
    }

    private BFontStyle FontFor(InlineStyle style, double sizeScale = 1.0)
    {
        double size = style.FontSize is > 0 ? style.FontSize.Value : _settings.DefaultFontSizePoints;
        string family = string.IsNullOrWhiteSpace(style.FontFamily)
            ? _settings.DefaultFontFamily
            : style.FontFamily!;

        return new BFontStyle(
            family,
            Math.Max(1.0, size * sizeScale),
            style.Bold ? BFontWeight.Bold : BFontWeight.Normal,
            style.Italic ? BFontSlant.Italic : BFontSlant.Normal);
    }

    private static string Transform(string text, TextCapitalization capitalization) => capitalization switch
    {
        TextCapitalization.AllCaps => text.ToUpperInvariant(),
        _ => text,
    };

    /// <summary>The lines one paragraph produced, before pagination places them.</summary>
    private sealed class ParagraphLines
    {
        public ParagraphLines(List<LayoutLine> lines) => Lines = lines;

        public List<LayoutLine> Lines { get; }
    }

    /// <summary>An unbreakable run of pieces: one word, one whitespace gap, one tab, or one image.</summary>
    private sealed class Token
    {
        private Token(bool isWhitespace) => IsWhitespace = isWhitespace;

        public bool IsWhitespace { get; }

        public List<LayoutPiece> Pieces { get; } = new();

        public double Width { get; private set; }

        /// <summary>True for the single-piece token a tab makes.</summary>
        public bool IsTab => Pieces.Count == 1 && Pieces[0].IsTab;

        public static Token Empty(bool isWhitespace = false) => new(isWhitespace);

        public static Token Single(LayoutPiece piece, bool isWhitespace = false)
        {
            var token = new Token(isWhitespace);
            token.Add(piece);
            return token;
        }

        public void Add(LayoutPiece piece)
        {
            Pieces.Add(piece);
            Width += piece.Width;
        }

        /// <summary>
        /// Sets a tab's width once wrapping knows where on its line it starts.
        /// The piece is the same object the line will place, so both agree.
        /// </summary>
        public void ResolveTabWidth(double width)
        {
            Pieces[0].Width = width;
            Width = width;
        }
    }

    /// <summary>
    /// Tracks list counters across paragraphs and produces the marker text.
    /// </summary>
    /// <remarks>
    /// The model records that a paragraph is in a numbered list at a given
    /// indent level and nothing else - there is no list identity, no start
    /// number, and no restart flag. So the rule here is the simple one those
    /// facts support: a counter per level, deeper levels reset when a shallower
    /// item appears, and every counter resets at the first paragraph that is not
    /// a list item. Documents whose original numbering said otherwise lost that
    /// on the way into the model, not here.
    /// </remarks>
    private sealed class ListNumbering
    {
        private const string Bullets = "•◦▪";

        private readonly List<int> _counters = new();

        public string? Advance(ParagraphStyle style)
        {
            int level = Math.Max(0, style.IndentLevel);

            if (style.ListKind == ListKind.None)
            {
                _counters.Clear();
                return null;
            }

            if (style.ListKind == ListKind.Bullet)
            {
                Truncate(level);
                return Bullets[level % Bullets.Length].ToString();
            }

            Truncate(level);
            while (_counters.Count <= level)
                _counters.Add(0);

            _counters[level]++;
            return Format(_counters[level], level) + ".";
        }

        private void Truncate(int level)
        {
            if (_counters.Count > level + 1)
                _counters.RemoveRange(level + 1, _counters.Count - level - 1);
        }

        private static string Format(int value, int level) => (level % 3) switch
        {
            0 => value.ToString(CultureInfo.InvariantCulture),
            1 => Alphabetic(value),
            _ => Roman(value),
        };

        private static string Alphabetic(int value)
        {
            var builder = new StringBuilder();
            while (value > 0)
            {
                value--;
                builder.Insert(0, (char)('a' + (value % 26)));
                value /= 26;
            }

            return builder.Length == 0 ? "a" : builder.ToString();
        }

        private static string Roman(int value)
        {
            if (value <= 0 || value >= 4000)
                return value.ToString(CultureInfo.InvariantCulture);

            int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] symbols = { "m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i" };

            var builder = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                while (value >= values[i])
                {
                    builder.Append(symbols[i]);
                    value -= values[i];
                }
            }

            return builder.ToString();
        }
    }
}
