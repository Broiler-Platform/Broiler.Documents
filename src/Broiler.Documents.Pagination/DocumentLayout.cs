using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;

namespace Broiler.Documents.Pagination;

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
/// not try to be one. There are no floats, footnotes, hyphenation, kerning
/// pairs, or bidirectional reordering here - mostly because the document model
/// has no way to express them, so there is nothing to lay out. A table row
/// breaks across a page cell by cell unless it says it may not, or holds a cell
/// that spans rows; a table nested in a cell breaks between its own rows. Text
/// columns are filled one after the
/// other. A section's columns are balanced where the next section follows on
/// the same page, as Word and LibreOffice balance them, a table row moving as
/// one piece, unless the section holds a column break there; the page's own
/// columns, and the last section's, are not, so the last page of a two-column
/// document can end with a full first column beside an empty second. A wrapping shape is measured as
/// though every column were the first. The shared paginator the PDF
/// roadmap tracks as
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
public sealed class DocumentLayout(LayoutSettings settings, ImageStore images)
{
    private readonly LayoutSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ImageStore _images = images ?? throw new ArgumentNullException(nameof(images));
    private readonly LineWrapper _lineWrapper = new(settings.TabStopPoints);
    private readonly List<string> _notes = [];
    private RunningContent _documentRunning = RunningContent.Empty;

    // The headers and footers of the page being filled - those of the section
    // that started it, or the document's - and the number of the first page
    // that section started, which is the page its first-page part is for.
    private RunningContent _running = RunningContent.Empty;
    private int _firstPage = 1;
    private double? _headerDistance;
    private double? _footerDistance;
    private PageFrame? _frame;
    private IReadOnlyList<DocumentShape> _documentShapes = [];
    private ShapeFill? _background;
    private TextWrapExclusions _wrap = new();
    private readonly Dictionary<int, double> _paragraphTops = [];
    private readonly Dictionary<DocumentShape, double> _grownHeights = [];
    private ListNumberer _lists = new();

    /// <summary>
    /// U+2028 LINE SEPARATOR, which is how the model spells a break inside a
    /// paragraph. Every codec reads and writes it - <c>text:line-break</c>,
    /// <c>w:br</c>, <c>\line</c>, <c>&lt;br&gt;</c> - and
    /// <c>RichTextEditor.InsertLineBreak</c> inserts exactly this.
    /// </summary>
    private const char ForcedLineBreak = '\u2028';

    /// <summary>Lays the document out onto pages of the given size.</summary>
    public LayoutResult Layout(RichTextDocument document, DocumentPageBox setup)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(setup);

        _notes.Clear();
        // Continuous mode collapses the document to one tall page, so there are no
        // pages for a header to repeat on and no bottom margin for a footer to sit
        // in. Drawing them there would put a page number in the middle of nothing.
        _documentRunning = setup.Continuous ? RunningContent.Empty : document.RunningContent;
        _running = _documentRunning;
        _firstPage = 1;
        _headerDistance = document.PageGeometry?.HeaderDistance;
        _footerDistance = document.PageGeometry?.FooterDistance;
        _frame = null;
        _documentShapes = document.Shapes;
        _background = document.PageBackground;
        _paragraphTops.Clear();
        _grownHeights.Clear();
        foreach (DocumentShape shape in _documentShapes)
            NoteGrownHeight(shape, setup);
        _wrap = new TextWrapExclusions();
        AddPageExclusions(1);

        // A continuous render has no page break to place, so it lays out against
        // an effectively unbounded column and then shrinks the page to the
        // content. That is the form to reach for when comparing two exports:
        // with pagination on, one extra line before a break moves every
        // subsequent page and a one-line difference reads as a whole-document one.
        double columnHeight = setup.Continuous ? double.MaxValue : setup.ContentHeightPoints;
        TextColumns pageColumns = TextColumns.For(document.PageGeometry, setup, _notes);
        var sectionColumns = new Dictionary<(DocumentSection Section, DocumentSection? Page), TextColumns>();

        // A section's own margins are measured on the document's own paper, so
        // they apply where the box being filled is that paper; a render asked
        // for other paper lays every section out within that paper's margins.
        bool sectionPages = document.PageGeometry is PageGeometry own && IsBoxOf(setup, own);
        if (!sectionPages && document.Sections.Any(section => section.Page is not null))
        {
            _notes.Add("laid every section out within the margins of the page asked for: " +
                "the margins some sections state are measured on the document's own paper.");
        }

        var pages = new List<LayoutPage>();
        var currentLines = new List<LayoutLine>();
        var currentCells = new List<LayoutCell>();
        double pageTop = setup.ContentTopPoints;
        double y = pageTop;
        double contentBottom = setup.ContentTopPoints + columnHeight;
        bool truncated = false;
        int ignoredBreaks = 0;

        // The top and foot of the text on a page, and the width its header is
        // set in: the margins of the section that started the page where it
        // states them, the box's otherwise, each moved clear of the header and
        // footer the page draws.
        (double Top, double Bottom) BandOf(DocumentSection? section)
        {
            if (setup.Continuous)
                return (setup.ContentTopPoints, setup.ContentTopPoints + columnHeight);

            if (!sectionPages || section?.Page is not PageGeometry page)
            {
                _frame = FrameFor(
                    pages.Count + 1, setup, setup.MarginTopPoints, setup.MarginBottomPoints,
                    _headerDistance, _footerDistance, setup.ContentLeftPoints, setup.ContentWidthPoints);
            }
            else
            {
                double left = Math.Clamp(page.MarginLeft, 0, setup.WidthPoints - 1);
                _frame = FrameFor(
                    pages.Count + 1, setup, Math.Clamp(page.MarginTop, 0, setup.HeightPoints - 1), page.MarginBottom,
                    page.HeaderDistance, page.FooterDistance, left, Math.Max(1, setup.WidthPoints - page.MarginRight - left));
            }

            return (_frame.TextTop, _frame.TextBottom);
        }

        // The section that started the page being filled, whose headers and
        // footers the page carries, and one that has asked to start the next.
        // A section starts a page where its first paragraph opens one; one that
        // follows on the same page starts nothing, and the pages its text runs
        // onto keep the section that did, as LibreOffice keeps the page style.
        bool hasOwner = false;
        DocumentSection? owner = null;
        DocumentSection? pendingOwner = null;
        bool hasPendingOwner = false;
        bool pendingSharesFirst = false;

        void TakeOwner(DocumentSection? section, bool sharesFirst = false)
        {
            hasOwner = true;
            owner = section;
            hasPendingOwner = false;
            pendingOwner = null;
            pendingSharesFirst = false;
            _running = setup.Continuous ? RunningContent.Empty : section?.RunningContent ?? _documentRunning;
            _firstPage = sharesFirst ? 0 : pages.Count + 1;
        }

        (pageTop, contentBottom) = BandOf(null);
        y = pageTop;

        // A section's columns are spread across the text width of the page it
        // is on: the margins of the section that started the page. A section
        // that follows on the same page - a continuous break - keeps that
        // page's margins rather than its own, as LibreOffice lays it out, where
        // such a section is a text section inside the page style of the one
        // before it. A narrow two-column section that a continuous break opens
        // used to set the whole page's header in its own narrow width.
        TextColumns ColumnsOf(DocumentSection? section, DocumentSection? pageSection)
        {
            if (section is null)
                return pageColumns;

            if (!sectionColumns.TryGetValue((section, pageSection), out TextColumns found))
            {
                double left = setup.ContentLeftPoints;
                double width = setup.ContentWidthPoints;
                if (sectionPages && pageSection?.Page is PageGeometry page)
                {
                    left = Math.Clamp(page.MarginLeft, 0, setup.WidthPoints - 1);
                    width = Math.Max(1, setup.WidthPoints - page.MarginRight - left);
                }

                found = TextColumns.For(
                    section.ColumnCount, section.ColumnGap, width, left - setup.ContentLeftPoints,
                    setup.Continuous, _notes, "a section's");
                sectionColumns.Add((section, pageSection), found);
            }

            return found;
        }

        // The stretch of the body being filled: a section with columns of its
        // own, or, where no section covers the text, the page's columns. A
        // section that follows another on the same page starts below it, so its
        // columns start there, and the text above is content its first column
        // already has beneath it when a break asks.
        DocumentSection? region = null;
        TextColumns columns = pageColumns;
        double regionTop = pageTop;
        double regionDepth = regionTop;
        var regionLines = new List<FlowItem>();
        bool regionUnbalanced = false;
        double spacingAfterPrevious = 0;

        // The column being filled, and how many of the page's lines were there
        // before it started: a column holds something once the page has more.
        // With one column that is "the page holds something", which is the test
        // every break below made before there were columns.
        int column = 0;
        int columnStart = 0;

        // Floating tables stand on the page apart from the flow: their lines and
        // boxes join the page when it is finished, the boxes they keep text out
        // of are the page's, and a table taller than the room left carries its
        // remaining rows to the next page.
        var floatLines = new List<LayoutLine>();
        var floatCells = new List<LayoutCell>();
        var floatBoxes = new List<FloatBox>();
        FloatCarry? floatCarry = null;

        bool ColumnHasContent() =>
            currentLines.Count > columnStart || (column == 0 && (regionTop > pageTop || floatBoxes.Count > 0));

        // One count for the body, which the cells of its tables share: a list
        // that runs through a table counts on through it.
        var numbering = new ListNumberer();
        _lists = numbering;

        void BreakPage()
        {
            currentLines.AddRange(floatLines);
            currentCells.AddRange(floatCells);
            pages.Add(NewPage(pages.Count + 1, setup, currentLines, currentCells));
            currentLines = [];
            currentCells = [];
            floatLines = [];
            floatCells = [];
            floatBoxes.Clear();
            column = 0;
            columnStart = 0;

            // Both of these are page-local and neither was being emptied, which is
            // the same defect twice. An anchor top is a y within the page it was
            // measured on, so carrying it forward drew a letterhead's logo box on
            // every page after the one its paragraph is on; an exclusion is a band
            // in the same coordinates, so carrying it forward pushed text aside on
            // a page with nothing beside it. A shape whose box runs past the page
            // bottom stops excluding there, which is an approximation this model
            // cannot avoid - it anchors a shape to a paragraph and the paragraph is
            // on one page.
            _paragraphTops.Clear();
            _wrap = new TextWrapExclusions();
            AddPageExclusions(pages.Count + 1);

            if (hasPendingOwner)
            {
                TakeOwner(pendingOwner, pendingSharesFirst);
                if (ReferenceEquals(region, owner))
                    columns = ColumnsOf(region, owner);
            }

            (pageTop, contentBottom) = BandOf(owner);
            y = pageTop;
            regionTop = y;
            regionDepth = y;
            regionLines.Clear();
            regionUnbalanced = false;
            if (pages.Count >= _settings.MaxPages)
                truncated = true;

            if (floatCarry is FloatCarry carry && !truncated)
            {
                floatCarry = null;
                PlaceFloating(carry.Rows, carry.Left, pageTop, carry.Width, carry.Position);
            }
        }

        // The page's floating boxes as exclusions, for when the exclusions are
        // made again part way down a page.
        void AddFloatExclusions()
        {
            foreach (FloatBox box in floatBoxes)
            {
                _wrap.AddBox(box.Top, box.Bottom,
                    box.Left - setup.ContentLeftPoints, box.Right - setup.ContentLeftPoints, box.Wrap);
            }
        }

        // Places a floating table's rows from top down, out of the flow: y does
        // not move. Rows that run past the foot of the page are cut where a row
        // may be cut, and what is left goes to the top of the next page, as
        // LibreOffice splits the frame it imports such a table into.
        void PlaceFloating(List<RowBox> rows, double left, double top, double width, TablePosition position)
        {
            double rowTop = top;
            bool placed = false;
            for (int i = 0; i < rows.Count; i++)
            {
                RowBox row = rows[i];
                if (rowTop + row.Height > contentBottom)
                {
                    if (row.CanSplit && row.SplitAt(contentBottom - rowTop) is (RowBox fitted, var rest))
                    {
                        fitted.PlaceAt(rowTop, 0, floatLines, floatCells, _paragraphTops);
                        rowTop += fitted.Height;
                        placed = true;
                        if (rest is null)
                            continue;

                        var carried = new List<RowBox> { rest };
                        carried.AddRange(rows.Skip(i + 1));
                        floatCarry = new FloatCarry(carried, left, width, position);
                        break;
                    }

                    if (placed)
                    {
                        floatCarry = new FloatCarry(rows.GetRange(i, rows.Count - i), left, width, position);
                        break;
                    }
                }

                row.PlaceAt(rowTop, 0, floatLines, floatCells, _paragraphTops);
                rowTop += row.Height;
                placed = true;
            }

            // Text runs beside the table where a side of the column has room for
            // it, and below it where neither does.
            double boxLeft = left - position.LeftFromText;
            double boxRight = left + width + position.RightFromText;
            double columnLeft = setup.ContentLeftPoints + columns.Offset(column);
            double room = Math.Max(boxLeft - columnLeft, columnLeft + columns.Width - boxRight);
            var box = new FloatBox(
                top - position.TopFromText,
                rowTop + position.BottomFromText,
                boxLeft,
                boxRight,
                room >= MinimumWrapRoom ? ShapeWrap.Square : ShapeWrap.TopAndBottom);
            floatBoxes.Add(box);
            _wrap.AddBox(box.Top, box.Bottom,
                box.Left - setup.ContentLeftPoints, box.Right - setup.ContentLeftPoints, box.Wrap);
        }

        // Where a table in the flow can start: below every floating table on the
        // page that it would run into, as LibreOffice moves an inline table down
        // past one (testInlineTableShiftDown) rather than drawing the two over
        // each other. The whole table moves, not the rows that meet it, so a
        // table is not cut in two around the one beside it.
        double BelowFloats(double top, List<RowBox> rows, double shift)
        {
            if (floatBoxes.Count == 0 || !rows.Any(row => row.Boxes.Count > 0))
                return top;

            double left = rows.SelectMany(row => row.Boxes).Min(box => box.Left) + shift;
            double right = rows.SelectMany(row => row.Boxes).Max(box => box.Left + box.Width) + shift;
            double height = rows.Sum(row => row.Height);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                double moved = top;
                double bottom = Math.Min(top + height, contentBottom);
                foreach (FloatBox box in floatBoxes)
                {
                    if (box.Left < right && box.Right > left && box.Top < bottom && box.Bottom > top)
                        moved = Math.Max(moved, box.Bottom);
                }

                if (moved <= top)
                    break;

                top = moved;
            }

            return top;
        }

        // Where a floating table stands, and the rows it is drawn as.
        void LayFloatingTable(DocumentTable table, TablePosition position)
        {
            double width = table.TotalWidth > 0 ? Math.Min(table.TotalWidth, setup.WidthPoints) : columns.Width;
            (double boxLeft, double boxWidth) = position.HorizontalAnchor switch
            {
                TableAnchor.Page => (0, setup.WidthPoints),
                TableAnchor.Margin => (_frame?.Left ?? setup.ContentLeftPoints, _frame?.Width ?? setup.ContentWidthPoints),
                _ => (setup.ContentLeftPoints + columns.Offset(column), columns.Width),
            };
            int pageNumber = pages.Count + 1;
            double left = position.LeftIn(boxLeft, boxWidth, width, pageNumber);
            List<RowBox> rows = ComposeTable(table, document, setup, left, width);
            double height = rows.Sum(row => row.Height);

            double marginTop = _frame?.MarginTop ?? setup.MarginTopPoints;
            double marginBottom = setup.HeightPoints - (_frame?.MarginBottom ?? setup.MarginBottomPoints);
            (double boxTop, double boxHeight) = position.VerticalAnchor switch
            {
                TableAnchor.Page => (0, setup.HeightPoints),
                TableAnchor.Margin => (marginTop, Math.Max(0, marginBottom - marginTop)),
                _ => (y, Math.Max(0, contentBottom - y)),
            };
            double top = setup.Continuous && position.VerticalAnchor != TableAnchor.Text
                ? y
                : position.TopIn(boxTop, boxHeight, height, pageNumber);
            PlaceFloating(rows, left, top, width, position);
        }

        void NextColumn()
        {
            regionDepth = Math.Max(regionDepth, y);
            column++;
            columnStart = currentLines.Count;
            y = regionTop;
        }

        // A full column moves on to the next one at the top of the same stretch,
        // and the last column of a page to the next page. So does a column that
        // holds nothing yet: it starts below a section above it, too low on the
        // page for even one line, and the next column starts just as low.
        void BreakColumn()
        {
            if (column + 1 >= columns.Count || currentLines.Count == columnStart)
            {
                BreakPage();
                return;
            }

            NextColumn();
        }

        // A section that follows on a page with headers of its own takes the
        // page a hard page break inside it starts, its first page sharing the
        // others' parts. That is LibreOffice's reading of such a document, the
        // "nightmare scenario" its writerfilter names: the section's page style
        // goes on the paragraph that breaks the page, with the first page
        // shared, because there is no page break of the section's own to put
        // it on. tdf90697_complexBreaksHeaders.docx is fifteen continuous
        // sections, each naming its own header, that change at page breaks.
        void OwnBreak()
        {
            if (hasPendingOwner || region is null || ReferenceEquals(region, owner))
                return;

            RunningContent own = region.RunningContent ?? _documentRunning;
            if (setup.Continuous || ReferenceEquals(own, _running))
                return;

            pendingOwner = region;
            hasPendingOwner = true;
            pendingSharesFirst = true;
        }

        // Where one stretch gives way to the next on the same page, the one above
        // ends below its deepest column - or, for a section the next one follows
        // on from, at the height that shares its lines out evenly between its
        // columns, which is how Word ends a section at a continuous break and
        // LibreOffice ends a text:section - and the next starts there, in its
        // first column. Where the next starts a page, the page break that follows
        // starts it at the top.
        void EnterRegion(DocumentSection? next, bool startsPage, bool opensPage)
        {
            if (!startsPage)
            {
                // The space after the last paragraph, measured in the column it
                // is in, before y moves down to the deepest one.
                FlowItem? last = regionLines.Count > 0 ? regionLines[^1] : null;
                double trailing = last is null ? 0 : Math.Max(0, y - (last.Top + last.Height));
                y = Math.Max(regionDepth, y);
                if (region is not null && columns.Count > 1 && !regionUnbalanced && last is not null)
                {
                    if (BalanceColumns(regionLines, columns, regionTop, contentBottom - regionTop, _paragraphTops) is double height)
                    {
                        y = regionTop + height + trailing;

                        // A wrapping shape hangs from its paragraph, and the
                        // paragraphs moved: the page's exclusions are made again
                        // from where they are now, or the text below would wrap
                        // around boxes that are no longer there.
                        _wrap = new TextWrapExclusions();
                        AddPageExclusions(pages.Count + 1);
                        foreach (DocumentShape shape in _documentShapes)
                        {
                            if (shape.Wraps && !shape.IsPageAnchored &&
                                _paragraphTops.TryGetValue(shape.ParagraphIndex, out double anchor))
                            {
                                _wrap.Add(shape, shape.TopOnPage(anchor), height: BoxHeight(shape));
                            }
                        }

                        AddFloatExclusions();
                    }
                }
            }

            region = next;

            // A section that opens a page, the first included, sets its top and
            // foot; one that follows on a page keeps those of the page it is on.
            if (currentLines.Count == 0 && currentCells.Count == 0)
            {
                if (!hasOwner || opensPage)
                    TakeOwner(next);

                (pageTop, contentBottom) = BandOf(owner);
                y = pageTop;
            }
            else if (opensPage)
            {
                // The page break that follows - or, for a section that opens
                // with a table, which breaks no page here, the next one there is
                // - starts the page this section owns.
                pendingOwner = next;
                hasPendingOwner = true;
            }

            columns = ColumnsOf(next, owner);

            column = 0;
            columnStart = currentLines.Count;
            regionTop = y;
            regionDepth = y;
            regionLines.Clear();
            regionUnbalanced = false;
        }

        // A row is placed whole, and is shared out between balanced columns as
        // one piece, with its cell boxes.
        void PlaceRow(RowBox row)
        {
            int linesFrom = currentLines.Count;
            int cellsFrom = currentCells.Count;
            row.PlaceAt(y, columns.Offset(column), currentLines, currentCells, _paragraphTops);
            if (region is not null && columns.Count > 1)
            {
                regionLines.Add(new FlowItem(
                    currentLines.GetRange(linesFrom, currentLines.Count - linesFrom),
                    currentCells.GetRange(cellsFrom, currentCells.Count - cellsFrom),
                    y, row.Height, column, GapBetween: 0, GapAtTop: 0, StartsParagraph: false));
            }
        }

        int paragraphIndex = 0;
        while (paragraphIndex < document.ParagraphCount && !truncated)
        {
            DocumentTable? table = DocumentTable.StartingAt(document.Tables, paragraphIndex);
            DocumentSection? section = DocumentSection.Containing(document.Sections, paragraphIndex);
            if (!ReferenceEquals(section, region))
            {
                bool opensPage = document.Paragraphs[paragraphIndex].Style.PageBreakBefore;
                bool startsPage = table is null && !setup.Continuous && currentLines.Count > 0 && opensPage;
                EnterRegion(section, startsPage, opensPage);
            }

            if (table?.Position is TablePosition floating)
            {
                LayFloatingTable(table, floating);
                paragraphIndex = table.ParagraphEnd;
                continue;
            }

            if (table is not null)
            {
                // A row that does not fit where it is breaks across the page when
                // it may: what fits stays, and the rest of each cell carries on in
                // the next column. One that may not moves on whole, and one that
                // cannot fit even a fresh column is placed there anyway and runs
                // off its foot, because a row that waited for room it will never
                // have would stop the layout.
                List<RowBox> composedRows = ComposeTable(table, document, setup, setup.ContentLeftPoints, columns.Width);
                y = BelowFloats(y, composedRows, columns.Offset(column));

                // The header rows repeat at the top of every column the table
                // continues into. headerOnly says the column holds nothing else
                // yet, so a row that does not fit beneath them is placed there
                // rather than sent on to a column that would repeat them again.
                int headerCount = HeaderRowCount(table);
                bool headerOnly = false;

                void ContinueTable(bool inHeader)
                {
                    BreakColumn();
                    if (inHeader || truncated || setup.Continuous)
                        return;

                    List<RowBox> repeated = ComposeTable(
                        new DocumentTable(table.ParagraphIndex, table.ParagraphCount, [.. table.Rows.Take(headerCount)],
                            table.ColumnWidths, table.CellPadding),
                        document, setup, setup.ContentLeftPoints, columns.Width);

                    // Header rows as tall as the page leave nothing for the rows
                    // they head, so they are not repeated.
                    if (y + repeated.Sum(header => header.Height) >= contentBottom)
                        return;

                    foreach (RowBox header in repeated)
                    {
                        PlaceRow(header);
                        y += header.Height;
                    }

                    headerOnly = true;
                }

                int rowNumber = 0;
                foreach (RowBox composedRow in composedRows)
                {
                    bool inHeader = rowNumber++ < headerCount;
                    RowBox? row = composedRow;
                    while (row is not null && !truncated)
                    {
                        if (y + row.Height > contentBottom)
                        {
                            if (row.CanSplit && row.SplitAt(contentBottom - y) is (RowBox fitted, var rest))
                            {
                                PlaceRow(fitted);
                                y += fitted.Height;
                                headerOnly = false;
                                row = rest;
                                if (row is not null)
                                    ContinueTable(inHeader);
                                continue;
                            }

                            if (ColumnHasContent() && !headerOnly)
                            {
                                ContinueTable(inHeader);
                                continue;
                            }
                        }

                        PlaceRow(row);
                        y += row.Height;
                        headerOnly = false;
                        row = null;
                    }

                    if (truncated)
                        break;
                }

                spacingAfterPrevious = 0;
                paragraphIndex = table.ParagraphEnd;
                continue;
            }

            RichTextParagraph paragraph = document.Paragraphs[paragraphIndex];
            ParagraphStyle style = paragraph.Style;
            string? marker = numbering.Advance(style);

            // A break the document asked for, taken before the paragraph is
            // measured. The guard on currentLines is what stops a break on the
            // first paragraph emitting an empty page in front of it - a document
            // that opens with one is asking to start on a fresh page, and it is
            // already on one.
            //
            // Not in continuous mode, where there are no pages to break between.
            // Silently doing nothing would be the wrong kind of quiet, so the
            // note below says the breaks were ignored and why.
            if (style.PageBreakBefore && !setup.Continuous && (currentLines.Count > 0 || floatLines.Count > 0))
            {
                OwnBreak();
                BreakPage();
                if (truncated)
                    break;
            }
            else if (style.ColumnBreakBefore && !style.PageBreakBefore && !setup.Continuous && ColumnHasContent())
            {
                // A column break moves to the top of the next column of the
                // stretch being filled, and from its last column, or where it has
                // only one, to the next page.
                regionUnbalanced = true;
                if (column + 1 < columns.Count)
                {
                    NextColumn();
                }
                else
                {
                    BreakPage();
                    if (truncated)
                        break;
                }
            }
            else if ((style.PageBreakBefore || style.ColumnBreakBefore) && setup.Continuous)
            {
                ignoredBreaks++;
            }

            _paragraphTops.TryAdd(paragraphIndex, y);

            // A shape's box is known once the paragraph it hangs from has a top,
            // so this paragraph's shapes join the exclusions before its own lines
            // are wrapped. One anchored further down cannot narrow a line above
            // it, which is what a single forward pass can honestly say.
            foreach (DocumentShape shape in _documentShapes)
            {
                if (shape.Wraps && !shape.IsPageAnchored && shape.ParagraphIndex == paragraphIndex)
                    _wrap.Add(shape, shape.TopOnPage(y), height: BoxHeight(shape));
            }

            // Space before never opens a page, or a column: a paragraph that
            // starts one starts at the top margin, the way every page-based
            // renderer does it.
            double spacingBefore = ColumnHasContent() ? Math.Max(0, style.SpacingBefore) : 0;

            // Composed against the first column and moved across as it is placed:
            // the columns are equally wide, so a line wraps the same in any of
            // them, and a paragraph that runs out of room part way through
            // continues in the next one with nothing composed again.
            ParagraphLines composed = ComposeParagraph(
                paragraph, marker, setup, paragraphIndex,
                columnWidthOverride: columns.Width, wrapTop: y + spacingBefore);
            y += spacingBefore;

            bool first = true;
            foreach (LayoutLine line in composed.Lines)
            {
                y += line.LeadingSkip;
                if (y + line.Height > contentBottom && ColumnHasContent())
                {
                    BreakColumn();
                    if (truncated)
                        break;
                }

                line.Top = y;
                line.Shift(columns.Offset(column));
                currentLines.Add(line);
                y += line.Height;

                // What balancing needs to share the lines out again: the space
                // each leaves above it below another line, and at a column's top.
                if (region is not null && columns.Count > 1)
                {
                    double between = line.LeadingSkip +
                        (first ? spacingAfterPrevious + Math.Max(0, style.SpacingBefore) : 0);
                    regionLines.Add(new FlowItem(
                        [line], [], line.Top, line.Height, column, between, line.LeadingSkip, first));
                }

                first = false;
            }

            spacingAfterPrevious = Math.Max(0, style.SpacingAfter);
            y += spacingAfterPrevious;
            paragraphIndex++;
        }

        // A floating table that ran past the last page of text still has rows
        // to stand on the pages after it.
        while (floatCarry is not null && !truncated)
            BreakPage();

        currentLines.AddRange(floatLines);
        currentCells.AddRange(floatCells);
        if (currentLines.Count > 0 || currentCells.Count > 0 || pages.Count == 0)
            pages.Add(NewPage(pages.Count + 1, setup, currentLines, currentCells));

        // A shape anchored to a page the text never reached still has to stand
        // on it, so the pages up to it are made, empty but for their running
        // bands, as LibreOffice makes them. A continuous render has one page and
        // says what it left off instead.
        int lastAnchor = _documentShapes.Count == 0 ? 0 : _documentShapes.Max(shape => shape.AnchorPage);
        if (setup.Continuous)
        {
            int unplaced = _documentShapes.Count(shape => shape.AnchorPage > 1);
            if (unplaced > 0)
            {
                _notes.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "left out {0} shape(s) anchored to a page after the first: --continuous renders one tall page.",
                    unplaced));
            }
        }
        else
        {
            _paragraphTops.Clear();
            while (pages.Count < lastAnchor && !truncated)
            {
                if (pages.Count >= _settings.MaxPages)
                {
                    truncated = true;
                    break;
                }

                pages.Add(NewPage(pages.Count + 1, setup, [], []));
            }
        }

        if (ignoredBreaks > 0)
        {
            _notes.Add(string.Format(
                CultureInfo.InvariantCulture,
                "ignored {0} page break(s) the document states: --continuous renders one tall page, " +
                "so there is nothing to break between.",
                ignoredBreaks));
        }

        if (truncated)
        {
            _notes.Add(string.Format(
                CultureInfo.InvariantCulture,
                "stopped at the --max-pages limit of {0}; the document has more content.",
                _settings.MaxPages));
        }

        DocumentPageBox finalSetup = setup;
        if (setup.Continuous)
        {
            double used = pages[0].Lines.Count > 0
                ? pages[0].Lines[^1].Top + pages[0].Lines[^1].Height
                : setup.ContentTopPoints;
            finalSetup = setup.WithHeight(used + setup.MarginBottomPoints);
            pages =
            [
                new(
                    1,
                    finalSetup.WidthPoints,
                    finalSetup.HeightPoints,
                    [.. pages[0].Lines],
                    [.. pages[0].Shapes],
                    [.. pages[0].Cells],
                    _background),
            ];
        }

        return new LayoutResult(pages, finalSetup, _settings, _notes, truncated);
    }

    private LayoutPage NewPage(int number, DocumentPageBox setup, List<LayoutLine> lines, List<LayoutCell> cells)
    {
        PageSelection selection = SelectionFor(number);

        // The frame the page's text was laid out in. A page made after the text
        // ran out, to stand a page-anchored shape on, has none of its own and
        // takes the last page's margins with its own header and footer.
        PageFrame frame = _frame is { } current
            ? current.Number == number ? current : FrameFor(number, setup, current)
            : FrameFor(
                number, setup, setup.MarginTopPoints, setup.MarginBottomPoints,
                _headerDistance, _footerDistance, setup.ContentLeftPoints, setup.ContentWidthPoints);

        var all = new List<LayoutLine>(lines);
        all.AddRange(RunningLines(
            _running.EffectiveHeader(selection),
            setup,
            frame,
            top: 0,
            band: frame.TextTop,
            topDistance: frame.HeaderDistance));
        all.AddRange(RunningLines(
            _running.EffectiveFooter(selection),
            setup,
            frame,
            top: frame.TextBottom,
            band: setup.HeightPoints - frame.TextBottom,
            bottomDistance: frame.FooterDistance));

        // One list and one ordering across the body and the running bands, because
        // that is how the formats state it: a letterhead's stripe is anchored in
        // the header and the box over it in the body, and their z-orders are the
        // only thing saying which the reader sees. Appending the bands after the
        // body - which is what this did - painted the header's stripe last and
        // over anything the body put on top of it.
        //
        // OrderBy rather than Sort, because it is stable: shapes that state no
        // order all sit at zero and keep the order they were read in, which is the
        // answer this gave before z-order was modelled.
        List<LayoutShape> shapes = PlaceShapes(setup, number);
        shapes.AddRange(PlaceRunningShapes(_running.EffectiveHeaderShapes(selection), setup));
        shapes.AddRange(PlaceRunningShapes(_running.EffectiveFooterShapes(selection), setup));
        shapes = [.. shapes.OrderBy(shape => shape.ZOrder)];

        return new LayoutPage(number, setup.WidthPoints, setup.HeightPoints, all, shapes, cells, _background);
    }

    /// <summary>
    /// The first page of the section that started it takes the first-page
    /// selection - page one, where no section starts another - and
    /// even-numbered pages the even one, each falling back to the default.
    /// </summary>
    private PageSelection SelectionFor(int number) =>
        number == _firstPage
            ? PageSelection.First
            : number % 2 == 0 ? PageSelection.Even : PageSelection.Default;

    /// <summary>
    /// The margins, header and footer distances and text band of page
    /// <paramref name="number"/>, its text moved clear of the header and footer
    /// that page draws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A header sits at its declared distance from the top edge, and where it
    /// runs past the top margin the text starts below it rather than under it:
    /// ECMA-376 §17.6.11 says a positive top margin pushes the text down to keep
    /// it off the header, and a positive bottom margin up off the footer, and
    /// Word and LibreOffice both draw it so. This layout used to leave such a
    /// header out. A contract whose last sections set the header at the top
    /// margin itself, with no room between, drew no header on any page.
    /// </para>
    /// <para>
    /// A band with no declared distance is centred in its margin and moves
    /// nothing, and a band that would take more than half of the text's height
    /// moves nothing either: it is reported and left out, as one taller than
    /// its margin always was, rather than squeezing the text to a sliver.
    /// </para>
    /// </remarks>
    private PageFrame FrameFor(int number, DocumentPageBox setup, double marginTop, double marginBottom,
        double? headerDistance, double? footerDistance, double left, double width)
    {
        double top = marginTop;
        double bottom = Math.Max(top + 1, setup.HeightPoints - marginBottom);
        PageSelection selection = SelectionFor(number);

        if (headerDistance is double fromTop && double.IsFinite(fromTop) && fromTop >= 0)
        {
            double header = RunningHeight(_running.EffectiveHeader(selection), setup, left, width);
            double overrun = fromTop + header - top;
            if (header > 0 && overrun > 0 && overrun <= (bottom - top) / 2)
                top += overrun;
        }

        if (footerDistance is double fromBottom && double.IsFinite(fromBottom) && fromBottom >= 0)
        {
            double footer = RunningHeight(_running.EffectiveFooter(selection), setup, left, width);
            double overrun = fromBottom + footer - (setup.HeightPoints - bottom);
            if (footer > 0 && overrun > 0 && overrun <= (bottom - top) / 2)
                bottom -= overrun;
        }

        return new PageFrame(number, marginTop, marginBottom, headerDistance, footerDistance, left, width, top, bottom);
    }

    /// <summary>Page <paramref name="number"/>'s frame on the paper and margins of <paramref name="like"/>.</summary>
    private PageFrame FrameFor(int number, DocumentPageBox setup, PageFrame like) =>
        FrameFor(number, setup, like.MarginTop, like.MarginBottom, like.HeaderDistance, like.FooterDistance, like.Left, like.Width);

    /// <summary>
    /// The paper a page is laid out on: the margins and band distances it takes
    /// them from, the text column its header and footer are set in, and where its
    /// text starts and ends.
    /// </summary>
    private sealed record PageFrame(
        int Number,
        double MarginTop,
        double MarginBottom,
        double? HeaderDistance,
        double? FooterDistance,
        double Left,
        double Width,
        double TextTop,
        double TextBottom);

    /// <summary>
    /// Adds the wrapping shapes anchored to page <paramref name="number"/> to the
    /// exclusions, which are page-local and were just emptied for it.
    /// </summary>
    private void AddPageExclusions(int number)
    {
        foreach (DocumentShape shape in _documentShapes)
        {
            if (shape.Wraps && shape.AnchorPage == number)
                _wrap.Add(shape, shape.OffsetY, height: BoxHeight(shape));
        }
    }

    /// <summary>True when <paramref name="setup"/> is the paper and margins <paramref name="page"/> states.</summary>
    private static bool IsBoxOf(DocumentPageBox setup, PageGeometry page) =>
        Math.Abs(setup.WidthPoints - page.Width) < 0.01 &&
        Math.Abs(setup.HeightPoints - page.Height) < 0.01 &&
        Math.Abs(setup.MarginLeftPoints - page.MarginLeft) < 0.01 &&
        Math.Abs(setup.MarginRightPoints - page.MarginRight) < 0.01 &&
        Math.Abs(setup.MarginTopPoints - page.MarginTop) < 0.01 &&
        Math.Abs(setup.MarginBottomPoints - page.MarginBottom) < 0.01;

    /// <summary>
    /// Places a running band's shapes on every page it applies to. There is no
    /// paragraph to hang one from - a header repeats - so the offset is measured
    /// from the top of the page.
    /// </summary>
    private List<LayoutShape> PlaceRunningShapes(IReadOnlyList<DocumentShape> shapes, DocumentPageBox setup)
    {
        var placed = new List<LayoutShape>();
        foreach (DocumentShape shape in shapes)
        {
            NoteGrownHeight(shape, setup);
            var bounds = new BRect(
                setup.ContentLeftPoints + shape.OffsetX,
                shape.OffsetY,
                Math.Max(0, shape.Width),
                BoxHeight(shape));
            if ((bounds.Width <= 0 || bounds.Height <= 0) && !shape.IsRule)
                continue;

            placed.Add(new LayoutShape(
                bounds,
                shape.Fill,
                shape.Outline,
                PlaceShapeText(shape, bounds, setup),
                shape.Image,
                shape.BehindText,
                shape.ZOrder));
        }

        return placed;
    }

    /// <summary>
    /// Places the document's floating shapes for page <paramref name="number"/>.
    /// </summary>
    /// <remarks>
    /// A shape's x is measured from the text column's left edge - which is how a
    /// letterhead's stripe sits in the margin without any page geometry - and its
    /// y from the top of the paragraph it is anchored to. A shape whose paragraph
    /// has not been laid out on this page is not drawn on it. A shape anchored to
    /// a page is drawn on that page, its y measured from the page's top edge.
    /// </remarks>
    private List<LayoutShape> PlaceShapes(DocumentPageBox setup, int number)
    {
        var placed = new List<LayoutShape>();
        if (_documentShapes.Count == 0)
            return placed;

        foreach (DocumentShape shape in _documentShapes)
        {
            double anchorTop;
            if (shape.IsPageAnchored)
            {
                if (shape.AnchorPage != number)
                    continue;

                anchorTop = 0;
            }
            else if (!_paragraphTops.TryGetValue(shape.ParagraphIndex, out anchorTop))
            {
                continue;
            }

            var bounds = new BRect(setup.ContentLeftPoints + shape.OffsetX, shape.TopOnPage(anchorTop),
                Math.Max(0, shape.Width), BoxHeight(shape));

            if ((bounds.Width <= 0 || bounds.Height <= 0) && !shape.IsRule)
                continue;

            placed.Add(new LayoutShape(bounds, shape.Fill, shape.Outline, PlaceShapeText(shape, bounds, setup),
                shape.Image, shape.BehindText, shape.ZOrder));
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
    private List<RowBox> ComposeTable(DocumentTable table, RichTextDocument document, DocumentPageBox setup, double left, double width)
    {
        double[] edges = ColumnEdges(table, left, width);
        var boxes = new List<RowBox>(table.Rows.Count);

        foreach (TableRow row in table.Rows)
        {
            // A row may break across a page where every cell can be cut on its
            // own line boundary, or between the rows of a table nested in it. A
            // box that spans rows would need cutting with the rows it spans, so a
            // row holding one stays whole.
            var box = new RowBox
            {
                CanSplit = row.CanSplit &&
                    row.Cells.All(cell => cell.RowSpan == 1 && !cell.IsRowSpanContinuation),
            };
            foreach (TableCell cell in row.Cells)
            {
                (double cellLeft, double cellWidth) = ColumnSpanBox(edges, cell);
                double textWidth = Math.Max(1.0, cellWidth - (table.CellPadding * 2));

                (List<LayoutLine> lines, List<LayoutCell> nested, List<CutPiece> pieces, double height) =
                    ComposeBlocks(document, cell.Tables, cell.ParagraphIndex, cell.ParagraphEnd, setup, cellLeft + table.CellPadding, textWidth);

                box.Lines.AddRange(lines);
                box.Cells.AddRange(nested);
                box.Contents.Add(new CellLines(lines, nested, pieces, height));
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
    /// <returns>
    /// The lines and nested cell boxes, the pieces a page may cut the range
    /// between, and the height it takes.
    /// </returns>
    private (List<LayoutLine> Lines, List<LayoutCell> Cells, List<CutPiece> Pieces, double Height) ComposeBlocks(
        RichTextDocument document, IReadOnlyList<DocumentTable> tables, int from, int to, DocumentPageBox setup,
        double left, double width)
    {
        var lines = new List<LayoutLine>();
        var cells = new List<LayoutCell>();
        var pieces = new List<CutPiece>();
        ListNumberer numbering = _lists.ForNestedFlow();
        double y = 0;

        int index = Math.Max(0, from);
        while (index < Math.Min(to, document.ParagraphCount))
        {
            if (DocumentTable.StartingAt(tables, index) is DocumentTable nested)
            {
                // A nested row is one piece: a page breaks between two of them,
                // never through one. Its paragraphs' tops here are measured from
                // the box, so they are not the page's; the row that holds the box
                // records them where it is placed.
                foreach (RowBox row in ComposeTable(nested, document, setup, left, width))
                {
                    row.PlaceAt(y, 0, lines, cells, new Dictionary<int, double>());
                    pieces.Add(new CutPiece(y, y + row.Height));
                    y += row.Height;
                }

                index = nested.ParagraphEnd;
                continue;
            }

            RichTextParagraph paragraph = document.Paragraphs[index];
            ParagraphLines composed = ComposeParagraph(paragraph, numbering.Advance(paragraph.Style), setup, index, left, width);

            if (lines.Count > 0)
                y += Math.Max(0, paragraph.Style.SpacingBefore);

            foreach (LayoutLine line in composed.Lines)
            {
                line.Top = y;
                lines.Add(line);
                pieces.Add(new CutPiece(y, y + line.Height));
                y += line.Height;
            }

            y += Math.Max(0, paragraph.Style.SpacingAfter);
            index++;
        }

        return (lines, cells, pieces, y);
    }

    /// <summary>
    /// How many rows at the top of <paramref name="table"/> repeat on every page
    /// it continues onto: the header rows it opens with, and none when every row
    /// is one, since a table of nothing but its heading has nothing to head.
    /// </summary>
    /// <remarks>
    /// Only the rows it opens with: Word repeats a <c>w:tblHeader</c> row only
    /// in the unbroken run from the first row, and ODF's
    /// <c>table:table-header-rows</c> can stand only there.
    /// </remarks>
    private static int HeaderRowCount(DocumentTable table)
    {
        int count = 0;
        while (count < table.Rows.Count && table.Rows[count].IsHeader)
            count++;

        return count == table.Rows.Count ? 0 : count;
    }

    /// <summary>
    /// The least room, in points, a side of the column must have beside a
    /// floating table for text to run down it; with less on both sides the text
    /// goes below the table.
    /// </summary>
    private const double MinimumWrapRoom = 72;

    /// <summary>
    /// The box a floating table keeps text out of on its page, its clearance
    /// included, in page coordinates.
    /// </summary>
    private readonly record struct FloatBox(double Top, double Bottom, double Left, double Right, ShapeWrap Wrap);

    /// <summary>The rows of a floating table still to stand on the next page, and where across.</summary>
    private sealed record FloatCarry(List<RowBox> Rows, double Left, double Width, TablePosition Position);

    /// <summary>
    /// A stretch of a cell a page may break before or after but not inside: one
    /// of its lines, or a whole row of a table nested in it. Measured from the
    /// top of the cell.
    /// </summary>
    private readonly record struct CutPiece(double Top, double Bottom);

    /// <summary>A cell's box within its row, before the row is placed on a page.</summary>
    private sealed class CellBox(double left, double width, TableCell cell)
    {
        public double Left { get; } = left;

        public double Width { get; } = width;

        public TableCell Cell { get; } = cell;

        /// <summary>How far past its own row the box reaches, for a cell that spans rows.</summary>
        public double ExtraHeight { get; set; }
    }

    /// <summary>
    /// One cell's composed lines, in order, the boxes of any table nested in it,
    /// the pieces it may be cut between, and the height they take.
    /// </summary>
    private sealed record CellLines(List<LayoutLine> Lines, List<LayoutCell> Cells, List<CutPiece> Pieces, double Height)
    {
        public static CellLines Empty => new([], [], [], 0);
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

        /// <summary>Each cell's own lines, so the row can be cut cell by cell.</summary>
        public List<CellLines> Contents { get; } = [];

        /// <summary>True when the row may break across a page; see <see cref="TableRow.CanSplit"/>.</summary>
        public bool CanSplit { get; init; }

        /// <summary>
        /// Cuts the row where <paramref name="room"/> runs out: each cell keeps the
        /// lines, and the rows of a table nested in it, that end within it, and
        /// the rest of that cell continues at the top of the second part, its
        /// first piece where the cut is.
        /// </summary>
        /// <returns>
        /// Null when no piece of any cell fits, so there is nothing to leave
        /// behind; the whole row, and no second part, when every piece fits and
        /// only the space after the last paragraph does not.
        /// </returns>
        /// <remarks>
        /// Each cell is cut on its own boundary, as Word and LibreOffice cut one,
        /// rather than at one height for the whole row: a line across the cut
        /// would be drawn in half on two pages. A nested table is cut between
        /// its rows, each of which goes to one page whole. The first part's boxes
        /// run down to the cut, so a cell whose text ended higher still reaches
        /// the foot of the page, and both parts draw their borders.
        /// </remarks>
        public (RowBox Fitted, RowBox? Remainder)? SplitAt(double room)
        {
            var cuts = new double?[Contents.Count];
            bool kept = false;
            bool carried = false;
            for (int i = 0; i < Contents.Count; i++)
            {
                foreach (CutPiece piece in Contents[i].Pieces)
                {
                    if (piece.Bottom <= room)
                    {
                        kept = true;
                        continue;
                    }

                    cuts[i] = piece.Top;
                    carried = true;
                    break;
                }
            }

            if (!kept)
                return null;
            if (!carried)
                return (this, null);

            var fitted = new RowBox { Height = room, CanSplit = true };
            var rest = new RowBox { CanSplit = true };
            fitted.Boxes.AddRange(Boxes);
            rest.Boxes.AddRange(Boxes);
            for (int i = 0; i < Contents.Count; i++)
            {
                CellLines content = Contents[i];
                if (cuts[i] is not double cut)
                {
                    fitted.Lines.AddRange(content.Lines);
                    fitted.Cells.AddRange(content.Cells);
                    fitted.Contents.Add(content);
                    rest.Contents.Add(CellLines.Empty);
                    continue;
                }

                var before = new List<LayoutLine>();
                var after = new List<LayoutLine>();
                foreach (LayoutLine line in content.Lines)
                {
                    if (line.Top < cut)
                    {
                        before.Add(line);
                        continue;
                    }

                    line.Top -= cut;
                    after.Add(line);
                }

                // A nested box above the cut stays and one below it moves up
                // with its lines. Only a box that spans nested rows can reach
                // across the cut; it is drawn to the foot of the page and
                // continues from the top of the next, as the row around it does.
                var boxesBefore = new List<LayoutCell>();
                var boxesAfter = new List<LayoutCell>();
                foreach (LayoutCell box in content.Cells)
                {
                    BRect bounds = box.Bounds;
                    if (bounds.Top < cut)
                    {
                        boxesBefore.Add(new LayoutCell(
                            new BRect(bounds.Left, bounds.Top, bounds.Width, Math.Min(bounds.Height, cut - bounds.Top)),
                            box.Shading, box.Borders));
                    }

                    if (bounds.Bottom > cut)
                    {
                        double top = Math.Max(bounds.Top, cut);
                        boxesAfter.Add(new LayoutCell(
                            new BRect(bounds.Left, top - cut, bounds.Width, bounds.Bottom - top),
                            box.Shading, box.Borders));
                    }
                }

                var piecesAfter = new List<CutPiece>();
                foreach (CutPiece piece in content.Pieces)
                {
                    if (piece.Top >= cut)
                        piecesAfter.Add(new CutPiece(piece.Top - cut, piece.Bottom - cut));
                }

                double remaining = Math.Max(0, content.Height - cut);
                fitted.Lines.AddRange(before);
                fitted.Cells.AddRange(boxesBefore);
                fitted.Contents.Add(new CellLines(before, boxesBefore, [.. content.Pieces.Where(piece => piece.Top < cut)], cut));
                rest.Lines.AddRange(after);
                rest.Cells.AddRange(boxesAfter);
                rest.Contents.Add(new CellLines(after, boxesAfter, piecesAfter, remaining));
                rest.Height = Math.Max(rest.Height, remaining);
            }

            return (fitted, rest);
        }

        /// <summary>
        /// Moves the row to <paramref name="top"/>, and <paramref name="shift"/>
        /// points across into the text column it lands in, and hands it to the page.
        /// </summary>
        public void PlaceAt(double top, double shift, List<LayoutLine> lines, List<LayoutCell> cells, Dictionary<int, double> paragraphTops)
        {
            foreach (CellBox box in Boxes)
                cells.Add(new LayoutCell(new BRect(box.Left + shift, top, box.Width, Height + box.ExtraHeight), box.Cell.Shading, box.Cell.Borders));

            foreach (LayoutCell cell in Cells)
                cells.Add(new LayoutCell(new BRect(cell.Bounds.Left + shift, cell.Bounds.Top + top, cell.Bounds.Width, cell.Bounds.Height), cell.Shading, cell.Borders));

            foreach (LayoutLine line in Lines)
            {
                line.Top += top;
                line.Shift(shift);
                lines.Add(line);
                paragraphTops.TryAdd(line.ParagraphIndex, line.Top);
            }
        }
    }

    /// <summary>A shape's own text, laid out inside its box.</summary>
    /// <summary>
    /// Measures a shape that grows to hold its text, once, at its own width:
    /// its lines laid one under another, as <see cref="PlaceShapeText"/> lays
    /// them, or its stated height where that is more.
    /// </summary>
    private void NoteGrownHeight(DocumentShape shape, DocumentPageBox setup)
    {
        if (!shape.GrowsToText || !shape.HasText || shape.Width <= 0 || _grownHeights.ContainsKey(shape))
            return;

        double height = 0;
        foreach (RichTextParagraph paragraph in shape.Paragraphs)
        {
            foreach (LayoutLine line in ComposeParagraph(paragraph, marker: null, setup, -1, 0, shape.Width).Lines)
                height += line.Height;
        }

        _grownHeights[shape] = Math.Max(Math.Max(0, shape.Height), height);
    }

    /// <summary>The height a shape's box is drawn at, grown to its text where it grows.</summary>
    private double BoxHeight(DocumentShape shape) =>
        _grownHeights.TryGetValue(shape, out double grown) ? grown : Math.Max(0, shape.Height);

    private List<LayoutLine> PlaceShapeText(DocumentShape shape, BRect bounds, DocumentPageBox setup)
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
                if (y + line.Height > bounds.Bottom + 0.01)
                    break;

                line.Top = y;
                lines.Add(line);
                y += line.Height;
            }
        }

        return lines;
    }

    /// <summary>
    /// Lays running content inside its margin band. A declared distance anchors
    /// the top of a header block or the bottom of a footer block; otherwise the
    /// block is centred. Content that cannot fit is reported, not drawn across
    /// the body.
    /// </summary>
    private List<LayoutLine> RunningLines(IReadOnlyList<RichTextParagraph> paragraphs, DocumentPageBox setup, PageFrame frame,
        double top, double band, double? bottomDistance = null, double? topDistance = null)
    {
        (List<LayoutLine> lines, double height) = ComposeRunning(paragraphs, setup, frame.Left, frame.Width);
        if (lines.Count == 0)
            return lines;

        if (height > band)
        {
            _notes.Add("a header or footer was taller than its page margin and was not drawn.");
            return [];
        }

        if (bottomDistance is double distance &&
            (!double.IsFinite(distance) || distance < 0 || distance + height > band))
        {
            _notes.Add("a footer could not fit at its declared distance inside the bottom margin and was not drawn.");
            return [];
        }

        if (topDistance is double headerDistance &&
            (!double.IsFinite(headerDistance) || headerDistance < 0 || headerDistance + height > band))
        {
            _notes.Add("a header could not fit at its declared distance inside the top margin and was not drawn.");
            return [];
        }

        double y = topDistance is double headerOffset
            ? top + headerOffset
            : bottomDistance is double offset
            ? top + band - offset - height
            : top + ((band - height) / 2);
        foreach (LayoutLine line in lines)
        {
            line.Top += y;
        }

        return lines;
    }

    /// <summary>
    /// A running band's lines, each placed from the top of the band, and the
    /// band's height with its paragraphs' spacing.
    /// </summary>
    private (List<LayoutLine> Lines, double Height) ComposeRunning(
        IReadOnlyList<RichTextParagraph> paragraphs, DocumentPageBox setup, double left, double width)
    {
        var lines = new List<LayoutLine>();
        double height = 0;
        foreach (RichTextParagraph paragraph in paragraphs)
        {
            if (lines.Count > 0)
                height += Math.Max(0, paragraph.Style.SpacingBefore);

            foreach (LayoutLine line in ComposeParagraph(paragraph, marker: null, setup, -1, left, width).Lines)
            {
                line.Top = height;
                lines.Add(line);
                height += line.Height;
            }

            height += Math.Max(0, paragraph.Style.SpacingAfter);
        }

        return (lines, height);
    }

    /// <summary>How tall a running band is, or zero for one with nothing in it.</summary>
    private double RunningHeight(IReadOnlyList<RichTextParagraph> paragraphs, DocumentPageBox setup, double left, double width) =>
        paragraphs.Count == 0 ? 0 : ComposeRunning(paragraphs, setup, left, width).Height;

    /// <summary>Wraps one paragraph into lines, without deciding which page they land on.</summary>
    private ParagraphLines ComposeParagraph(RichTextParagraph paragraph, string? marker, DocumentPageBox setup, int paragraphIndex,
        double? columnLeftOverride = null, double? columnWidthOverride = null, double? wrapTop = null)
    {
        ParagraphStyle style = paragraph.Style;
        double indent = Math.Max(0, style.IndentLevel) * _settings.IndentStepPoints;
        // A shape's text is laid out inside the shape, not inside the page's text
        // column: a centred line in a logo box belongs on the box's midpoint.
        double columnLeft = (columnLeftOverride ?? setup.ContentLeftPoints) + indent;
        double columnWidth = Math.Max(
            1.0,
            (columnWidthOverride ?? setup.ContentWidthPoints) - indent);

        // An empty line has no run to measure, so it is as tall as the paragraph
        // mark says - the document's default only where the mark says nothing.
        BFontStyle defaultFont = FontFor(paragraph.MarkStyle);
        LayoutPiece? markerPiece = null;
        double hang = 0;

        if (marker is not null)
        {
            InlineStyle markerStyle = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Style : paragraph.MarkStyle;
            markerPiece = MakeTextPiece(marker, markerStyle, decorateLink: false);
            hang = Math.Min(columnWidth * 0.5, markerPiece.Width + _settings.ListMarkerGapPoints);
        }

        double textLeft = columnLeft + hang;
        double textWidth = Math.Max(1.0, columnWidth - hang);

        List<LayoutToken> tokens = Tokenize(paragraph);
        var bands = new List<TextBand>();
        var skips = new List<double>();
        List<List<LayoutPiece>> rows;

        if (wrapTop is double top && !_wrap.IsEmpty)
        {
            // Each row is measured where it lands. The y is advanced by the
            // default line height rather than the row's own, which is not known
            // until the row has been wrapped to a width - so a row of unusually
            // tall text can reach a little into a shape it only just cleared.
            double lineY = top;
            var estimatedMetrics = VerticalMetrics(defaultFont);
            double estimate = estimatedMetrics.Ascent + estimatedMetrics.Descent;
            rows = _lineWrapper.Wrap(
                tokens,
                _ =>
                {
                    double skip = 0;
                    TextBand row = BandFor(ref lineY, estimate, textWidth, ref skip);
                    skips.Add(skip);
                    lineY += estimate;
                    return row;
                },
                bands);
        }
        else
        {
            rows = _lineWrapper.Wrap(tokens, textWidth);
        }

        var lines = new List<LayoutLine>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            List<LayoutPiece> pieces = rows[i];
            TextBand band = i < bands.Count ? bands[i] : new TextBand(0, textWidth);

            // The marker belongs to the first line only, and sits in the hanging
            // indent rather than in the text column, so wrapped lines align under
            // the text and not under the bullet.
            if (i == 0 && markerPiece is not null)
            {
                markerPiece.X = columnLeft + band.Left;
                pieces.Insert(0, markerPiece);
            }

            LayoutLine line = PlaceLine(
                pieces,
                i == 0 && markerPiece is not null,
                textLeft + band.Left,
                Math.Max(1, band.Width),
                style,
                defaultFont,
                paragraphIndex,
                i == rows.Count - 1);
            line.LeadingSkip = i < skips.Count ? skips[i] : 0;

            // The shading runs from the indent to the column's edge, whatever
            // the line holds and whatever a wrapping shape cut from it.
            if (!style.Background.IsEmpty && style.Background.A > 0)
            {
                line.Background = style.Background;
                line.BackgroundLeft = columnLeft;
                line.BackgroundWidth = columnWidth;
            }

            lines.Add(line);
        }

        return new ParagraphLines(lines);
    }

    /// <summary>
    /// The band a line has at <paramref name="y"/>, moved below anything that
    /// leaves it no room, with how far it had to move recorded.
    /// </summary>
    /// <remarks>
    /// Bounded rather than repeated until it settles: clearing one shape can move
    /// a line into the next, and shapes covering the column would push a line
    /// down forever. After the last try the line takes the full column and draws
    /// through, which is visible and finite where a hang is neither.
    /// </remarks>
    private TextBand BandFor(ref double y, double height, double width, ref double skip)
    {
        TextBand band = _wrap.Resolve(ref y, height, width, out double moved);
        skip += moved;
        return band;
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
    private LayoutLine PlaceLine(List<LayoutPiece> pieces, bool hasMarker, double textLeft, double textWidth,
        ParagraphStyle style, BFontStyle defaultFont, int paragraphIndex, bool isLastLine)
    {
        // A populated line is measured from the fonts it actually draws. The
        // fallback face is only for empty lines; including it in every maximum
        // silently adds leading when all runs use a smaller or shorter face.
        var emptyMetrics = pieces.Count == 0 ? VerticalMetrics(defaultFont) : (Ascent: 0.0, Descent: 0.0, LineGap: 0.0);
        double ascent = emptyMetrics.Ascent;
        double descent = emptyMetrics.Descent;
        double lineGap = emptyMetrics.LineGap;
        double used = 0;

        // The marker is placed already and sits outside the text column, so it
        // contributes to the line's height but not to the width alignment
        // distributes.
        int first = hasMarker ? 1 : 0;
        for (int i = 0; i < pieces.Count; i++)
        {
            ascent = Math.Max(ascent, pieces[i].Ascent);
            descent = Math.Max(descent, pieces[i].Descent);
            lineGap = Math.Max(lineGap, pieces[i].LineGap);
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

        double natural = ascent + descent + lineGap;

        // An exact height puts the baseline four fifths of the way down the
        // line, whatever the text's own ascent, which is LibreOffice's rule
        // (SwTextFormatter::CalcRealHeight); text taller than the line runs
        // past it rather than moving it.
        if (style.LineHeightRule == LineHeightRule.Exact && style.LineHeight > 0)
            return new LayoutLine(pieces, 0, style.LineHeight, style.LineHeight * 0.8, paragraphIndex);

        // Extra leading goes below the baseline. Putting it above would push the
        // first line of every double-spaced paragraph down by half a line, which
        // is not what a document that says "line spacing 2" is asking for. A
        // line made taller by an at-least height keeps its text at the top too,
        // as LibreOffice draws it.
        return new LayoutLine(pieces, 0, style.LineBoxHeight(natural), ascent, paragraphIndex);
    }

    /// <summary>
    /// Splits a paragraph into wrap tokens. A word that spans two runs - "very"
    /// in one and "**bold**" in the next - stays one token, because a break
    /// between them would be a break in the middle of a word.
    /// </summary>
    private List<LayoutToken> Tokenize(RichTextParagraph paragraph)
    {
        var tokens = new List<LayoutToken>();
        LayoutToken? word = null;
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

            foreach ((string fragment, bool whitespace, bool image, bool tab, bool forcedBreak) in Fragments(text, run.Style))
            {
                if (forcedBreak)
                {
                    if (word is not null)
                    {
                        tokens.Add(word);
                        word = null;
                    }

                    tokens.Add(LayoutToken.ForcedBreak());
                    continue;
                }

                if (image)
                {
                    LayoutPiece piece = MakeImagePiece(run.Style);
                    word ??= LayoutToken.Empty();
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

                    tokens.Add(LayoutToken.Single(MakeTabPiece(run.Style), isWhitespace: true));
                    continue;
                }

                if (whitespace)
                {
                    if (word is not null)
                    {
                        tokens.Add(word);
                        word = null;
                    }

                    LayoutToken space = LayoutToken.Empty(isWhitespace: true);
                    foreach (LayoutPiece piece in MakePieces(fragment, run.Style))
                        space.Add(piece);
                    tokens.Add(space);
                    continue;
                }

                word ??= LayoutToken.Empty();
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
            // A forced line break is not whitespace and not a word: it is an
            // instruction about where the line ends. It has to be pulled out here,
            // before the whitespace test below, because char.IsWhiteSpace is true
            // for U+2028 - which is how a break spent this engine's whole life
            // arriving as an ordinary space and reflowing the address block it was
            // written to hold apart.
            if (character == ForcedLineBreak)
            {
                if (builder.Length > 0)
                {
                    yield return new Fragment(builder.ToString(), whitespace ?? false, false, false, false);
                    builder.Clear();
                }

                whitespace = null;
                yield return new Fragment(string.Empty, Whitespace: false, Image: false, Tab: false, Break: true);
                continue;
            }

            // A tab is neither a word nor part of a whitespace run: how wide it is
            // depends on where along its line it falls, so it stands alone, the way
            // an image placeholder does.
            bool isTab = character == '\t';
            if (isTab || (character == InlineImage.Placeholder && style.IsImage))
            {
                if (builder.Length > 0)
                {
                    yield return new Fragment(builder.ToString(), whitespace ?? false, false, false, false);
                    builder.Clear();
                }

                whitespace = null;
                yield return isTab
                    ? new Fragment(string.Empty, Whitespace: true, Image: false, Tab: true, Break: false)
                    : new Fragment(string.Empty, Whitespace: false, Image: true, Tab: false, Break: false);
                continue;
            }

            bool isSpace = char.IsWhiteSpace(character);
            if (whitespace is not null && isSpace != whitespace)
            {
                yield return new Fragment(builder.ToString(), whitespace.Value, false, false, false);
                builder.Clear();
            }

            whitespace = isSpace;
            builder.Append(character);
        }

        if (builder.Length > 0)
            yield return new Fragment(builder.ToString(), whitespace ?? false, false, false, false);
    }

    private readonly record struct Fragment(string Text, bool Whitespace, bool Image, bool Tab, bool Break);

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
        BColor color = LayoutColors.Or(style.Foreground, _settings.DefaultForeground);
        bool underline = style.Underline;

        if (decorateLink && style.IsLink)
        {
            underline = true;
            if (style.Foreground.IsEmpty)
                color = _settings.LinkColor;
        }

        double width = BTextMeasurer.MeasureAdvance(text, font);
        var (ascent, descent, lineGap) = VerticalMetrics(font);

        // Shearing is a fallback for a family with no designed italic face. It
        // does not change the advance, so nothing in the layout moves either way.
        bool oblique = style.Italic &&
            _settings.SynthesizeItalic &&
            !(_settings.ItalicFaceAvailable?.Invoke(font.FamilyName) ?? false);

        return new LayoutPiece(text, font, color, style.Background, underline, style.Strikethrough, style.LinkHref, null,
            width, ascent, descent, oblique: oblique, lineGap: lineGap);
    }

    /// <summary>
    /// A tab: a gap of no width yet, carrying its run's font so the line it lands
    /// on is as tall as the run and any highlight behind it is the run's colour.
    /// Wrapping fills the width in once it knows where the tab starts.
    /// </summary>
    private LayoutPiece MakeTabPiece(InlineStyle style)
    {
        BFontStyle font = FontFor(style);
        var (ascent, descent, lineGap) = VerticalMetrics(font);

        return new LayoutPiece(string.Empty, font, LayoutColors.Or(style.Foreground, _settings.DefaultForeground), style.Background, underline: false,
            strikethrough: false, style.LinkHref, null, 0, ascent, descent, oblique: false, isTab: true, lineGap: lineGap);
    }

    private (double Ascent, double Descent, double LineGap) VerticalMetrics(BFontStyle font)
    {
        double lineGap = _settings.FontLineGap?.Invoke(font) is double gap && double.IsFinite(gap) && gap >= 0
            ? gap
            : 0;
        if (_settings.FontVerticalMetrics?.Invoke(font) is { } metrics &&
            double.IsFinite(metrics.Ascent) && double.IsFinite(metrics.Descent) &&
            metrics.Ascent > 0 && metrics.Descent >= 0 && double.IsFinite(metrics.Ascent + metrics.Descent))
            return (metrics.Ascent, metrics.Descent, lineGap);
        double ascent = font.Size * 0.8;
        return (ascent, Math.Max(0, BTextMeasurer.GetLineHeight(font) - ascent), 0);
    }

    private LayoutPiece MakeImagePiece(InlineStyle style)
    {
        InlineImage image = style.Image!;
        (double width, double height) = _images.MeasurePoints(image);

        return new LayoutPiece(string.Empty, FontFor(style), LayoutColors.Or(style.Foreground, _settings.DefaultForeground), style.Background, false,
            false, style.LinkHref, image, Math.Max(1, width), Math.Max(1, height), 0);
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

    /// <summary>
    /// The text columns a page is divided into: how many, how wide, and how far
    /// each one sits from the first.
    /// </summary>
    /// <remarks>
    /// The width comes from the box being laid out, not from the document's own
    /// page, because a render can ask for different paper and the columns then
    /// share out what that paper leaves. A continuous render has no page to fill
    /// column by column and lays the text out as one; so does a page whose
    /// columns would come out narrower than <see cref="MinimumWidth"/>, which a
    /// word cannot be fitted into and the layout would otherwise spend a line
    /// per character on.
    /// </remarks>
    private readonly record struct TextColumns(int Count, double Width, double Stride, double Left = 0)
    {
        /// <summary>A column narrower than this, in points, is not laid out as one.</summary>
        private const double MinimumWidth = 18;

        public static TextColumns For(PageGeometry? geometry, DocumentPageBox setup, List<string> notes)
        {
            if (geometry is null || !geometry.HasColumns || !geometry.IsUsable)
                return new TextColumns(1, setup.ContentWidthPoints, 0);

            return For(geometry.ColumnCount, geometry.ColumnGap, setup.ContentWidthPoints, 0, setup.Continuous, notes, "the document's");
        }

        /// <summary>
        /// The columns <paramref name="count"/> and <paramref name="gap"/> make
        /// of a text area <paramref name="width"/> wide, starting
        /// <paramref name="left"/> points right of the box's own,
        /// <paramref name="owner"/> naming whose they are in a note that says
        /// they were laid out as one. A note is made once, however many sections
        /// it applies to.
        /// </summary>
        public static TextColumns For(
            int count, double gap, double width, double left, bool continuous, List<string> notes, string owner)
        {
            var single = new TextColumns(1, width, 0, left);
            if (count <= 1)
                return single;

            if (continuous)
            {
                Note(notes, string.Format(
                    CultureInfo.InvariantCulture,
                    "laid out {0} {1} text columns as one: --continuous renders one tall page, " +
                    "so there is no column bottom to move on from.",
                    owner,
                    count));
                return single;
            }

            double each = (width - (gap * (count - 1))) / count;
            if (!(each >= MinimumWidth))
            {
                Note(notes, string.Format(
                    CultureInfo.InvariantCulture,
                    "laid out {0} {1} text columns as one: on this page each would be narrower than {2} points.",
                    owner,
                    count,
                    MinimumWidth));
                return single;
            }

            return new TextColumns(count, each, each + gap, left);
        }

        private static void Note(List<string> notes, string note)
        {
            if (!notes.Contains(note))
                notes.Add(note);
        }

        /// <summary>How far right of the box's text area <paramref name="column"/> starts.</summary>
        public double Offset(int column) => Left + (column * Stride);
    }

    /// <summary>
    /// A piece of a section's columns on the page being filled - a line, or a
    /// table row with its lines and cell boxes - with what balancing needs to
    /// place it again: where it is, the column it is in, the space it leaves
    /// above it below another piece of the same column, and the space it leaves
    /// at the top of a column, where space before a paragraph is dropped.
    /// </summary>
    private sealed record FlowItem(
        List<LayoutLine> Lines,
        List<LayoutCell> Cells,
        double Top,
        double Height,
        int Column,
        double GapBetween,
        double GapAtTop,
        bool StartsParagraph);

    /// <summary>
    /// Shares <paramref name="flow"/> out between the columns again, each column
    /// filled in turn to the least height that still takes every line, and
    /// returns that height; or returns null, moving nothing, when the lines do
    /// not fit in <paramref name="room"/> however they are shared.
    /// </summary>
    /// <remarks>
    /// The pieces stay in order and each column takes as many as fit before the
    /// next one starts, which is how Word and LibreOffice balance the columns of
    /// a section that ends on a page with text after it; a table row is one
    /// piece. The first keeps the space it was placed with below the section
    /// above. A taller height never needs more columns, so the least one is found
    /// by halving the room until what is left is too small to see.
    /// </remarks>
    private static double? BalanceColumns(
        List<FlowItem> flow, TextColumns columns, double top, double room, Dictionary<int, double> paragraphTops)
    {
        double lead = Math.Max(0, flow[0].Top - top);
        if (Pack(flow, columns.Count, room, lead, null) is null)
            return null;

        double low = flow.Max(item => item.GapAtTop + item.Height);
        double high = room;
        while (high - low > 0.01)
        {
            double middle = (low + high) / 2;
            if (Pack(flow, columns.Count, middle, lead, null) is null)
                low = middle;
            else
                high = middle;
        }

        var placement = new (int Column, double Top)[flow.Count];
        double used = Pack(flow, columns.Count, high, lead, placement) ?? room;
        for (int i = 0; i < flow.Count; i++)
        {
            FlowItem item = flow[i];
            double down = top + placement[i].Top - item.Top;
            double across = columns.Offset(placement[i].Column) - columns.Offset(item.Column);
            foreach (LayoutLine line in item.Lines)
            {
                // A paragraph's anchor moves with its first line: the line that
                // starts it, or, in a cell, the line its top was taken from.
                if (paragraphTops.TryGetValue(line.ParagraphIndex, out double anchor) &&
                    (item.StartsParagraph || Math.Abs(anchor - line.Top) < 0.001))
                {
                    paragraphTops[line.ParagraphIndex] = anchor + down;
                }

                line.Top += down;
                line.Shift(across);
            }

            foreach (LayoutCell cell in item.Cells)
                cell.Bounds = new BRect(cell.Bounds.Left + across, cell.Bounds.Top + down, cell.Bounds.Width, cell.Bounds.Height);
        }

        return used;
    }

    /// <summary>
    /// Fills <paramref name="count"/> columns of <paramref name="height"/> with
    /// <paramref name="flow"/> in order and returns the depth of the deepest, or
    /// null when the lines need more columns than there are.
    /// </summary>
    private static double? Pack(List<FlowItem> flow, int count, double height, double lead, (int Column, double Top)[]? placement)
    {
        int column = 0;
        double y = 0;
        double used = 0;
        for (int i = 0; i < flow.Count; i++)
        {
            FlowItem item = flow[i];
            double gap = i == 0 ? lead : item.GapBetween;
            if (i > 0 && y + gap + item.Height > height)
            {
                if (++column >= count)
                    return null;

                y = 0;
                gap = item.GapAtTop;
            }

            if (placement is not null)
                placement[i] = (column, y + gap);

            y += gap + item.Height;
            used = Math.Max(used, y);
        }

        return used;
    }

    /// <summary>The lines one paragraph produced, before pagination places them.</summary>
    private sealed class ParagraphLines(List<LayoutLine> lines)
    {
        public List<LayoutLine> Lines { get; } = lines;
    }
}
