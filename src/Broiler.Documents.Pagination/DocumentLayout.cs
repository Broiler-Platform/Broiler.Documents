using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Text;
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
/// not try to be one. There is no hyphenation, kerning, or bidirectional
/// reordering here - mostly because the document model has no way to express
/// them, so there is nothing to lay out. A footnote is set at the foot of the
/// page its citation is on and is not split across pages; endnotes follow the
/// body. A table row
/// breaks across a page cell by cell unless it says it may not, or holds a cell
/// that spans rows; a nested table also keeps the safe cuts its rows permit. Text
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
    private readonly LineWrapper _lineWrapper = new(settings.TabStopPoints, settings.TextMeasurer);
    private readonly List<string> _notes = [];
    private RunningContent _documentRunning = RunningContent.Empty;

    // The headers and footers of the page being filled - those of the section
    // that started it, or the document's - and the number of the first page
    // that section started, which is the page its first-page part is for.
    private RunningContent _running = RunningContent.Empty;
    private int _firstPage = 1;

    // How pages are numbered: the page that last restarted the count and the
    // number it took, the sections that have restarted it, and the document's
    // page count once a first pass has found it, for a page-count field.
    private (int Page, int Number) _numbering = (1, 1);
    private readonly HashSet<DocumentSection> _restartedNumbering = [];
    private int? _pageCount;

    // Where each page's body text starts, as the pass laying it out found it,
    // and as a first pass found it, for a header that shows text found on its
    // page; with the paragraphs those positions are in.
    private readonly List<(int Paragraph, int Offset)?> _pageStartsFound = [];
    private IReadOnlyList<(int Paragraph, int Offset)?>? _pageStarts;
    private IReadOnlyList<RichTextParagraph> _bodyParagraphs = [];
    private double? _headerDistance;
    private double? _footerDistance;
    private (bool Header, bool Footer) _fixedBands;
    private PageFrame? _frame;
    private IReadOnlyList<DocumentShape> _documentShapes = [];
    private ShapeFill? _background;
    private DocumentStyleDefaults _styleDefaults = DocumentStyleDefaults.Default;
    private TextWrapExclusions _wrap = new();
    private readonly Dictionary<int, double> _paragraphTops = [];
    private readonly Dictionary<DocumentShape, double> _grownHeights = [];
    private readonly Dictionary<DocumentShape, double> _grownWidths = [];
    private readonly Dictionary<DocumentShape, IReadOnlyList<RichTextParagraph>> _linkedParagraphs = [];
    private readonly Dictionary<DocumentShape, ListNumberer> _linkedStarts = [];
    private ListNumberer _lists = new();

    // How the body's notes are labelled, and each footnote's lines, composed
    // once against the page's text width.
    private NoteNumberer _noteLabels = new();

    /// <summary>How the document being laid out draws its tracked changes.</summary>
    private RevisionDisplay _revisions = RevisionDisplay.None;

    /// <summary>
    /// How far left of its text frame a changed line's bar is drawn: a quarter
    /// of a centimetre, LibreOffice's REDLINE_DISTANCE (frmpaint.cxx).
    /// </summary>
    private const double ChangeBarDistance = 567.0 / 4 / 20;
    private bool _raisedNoteLabels;
    private readonly Dictionary<DocumentNote, NoteBlock> _noteBlocks = new(ReferenceEqualityComparer.Instance);

    /// <summary>A citation's size against its text, and how far it is raised: LibreOffice's superscript.</summary>
    private const double CitationScale = 0.58;

    private const double CitationRise = 0.33;

    /// <summary>
    /// The line above a page's footnotes: LibreOffice's default, a quarter of
    /// the text's width, 0.018 cm thick, 0.101 cm clear of the text above and
    /// of the notes below.
    /// </summary>
    private const double SeparatorWidthRatio = 0.25;

    private const double SeparatorThickness = 0.51;

    private const double SeparatorGap = 2.86;

    private const double SeparatorBlock = SeparatorGap + SeparatorThickness + SeparatorGap;

    /// <summary>
    /// U+2028 LINE SEPARATOR, which is how the model spells a break inside a
    /// paragraph. Every codec reads and writes it - <c>text:line-break</c>,
    /// <c>w:br</c>, <c>\line</c>, <c>&lt;br&gt;</c> - and
    /// <c>RichTextEditor.InsertLineBreak</c> inserts exactly this.
    /// </summary>
    private const char ForcedLineBreak = '\u2028';

    /// <summary>Lays the document out onto pages of the given size.</summary>
    /// <remarks>
    /// A header or footer that shows the page count, or text found on its page
    /// (<see cref="StyleReferenceField"/>), is drawn from a second pass, which
    /// knows the count and where each page's text starts from the first. Both
    /// passes size the bands from the text the document saved for those
    /// fields, so the body is laid out the same both times. Only such a
    /// document pays for it.
    /// </remarks>
    public LayoutResult Layout(RichTextDocument document, DocumentPageBox setup)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(setup);

        _pageCount = null;
        _pageStarts = null;
        LayoutResult result = LayoutOnce(document, setup);
        if (!setup.Continuous && NeedsSecondPass(document))
        {
            _pageCount = result.Pages.Count;
            _pageStarts = [.. _pageStartsFound];
            result = LayoutOnce(document, setup);
        }

        return result;
    }

    /// <summary>
    /// True when a header or footer of <paramref name="document"/> needs what
    /// only a first pass finds: the page count, to show it or to know whether
    /// the page an adjusted page number points at is there, or where each
    /// page's text starts, to show text found on the page.
    /// </summary>
    private static bool NeedsSecondPass(RichTextDocument document)
    {
        static bool Shows(RunningContent running) =>
            Enum.GetValues<PageSelection>().Any(selection =>
                running.Header(selection).Concat(running.Footer(selection))
                    .Any(paragraph => paragraph.Runs.Any(run =>
                        run.Style.StyleReference is not null ||
                        (run.Style.PageField is PageField field && (field.Kind == PageFieldKind.PageCount || field.Adjust != 0)))));

        return Shows(document.RunningContent) ||
            document.Sections.Any(section =>
                (section.RunningContent is RunningContent running && Shows(running)) ||
                (section.PageStyles?.Styles.Any(style => Shows(style.RunningContent)) ?? false));
    }

    private LayoutResult LayoutOnce(RichTextDocument document, DocumentPageBox setup)
    {
        _numbering = (1, 1);
        _restartedNumbering.Clear();

        // A document that hides its changes is laid out as it reads with its
        // deletions gone; one that shows them draws each in its author's colour.
        document = RevisionDisplay.HideDeletions(document);
        _revisions = RevisionDisplay.For(document);
        _bodyParagraphs = document.Paragraphs;
        _pageStartsFound.Clear();

        _notes.Clear();
        // Continuous mode collapses the document to one tall page, so there are no
        // pages for a header to repeat on and no bottom margin for a footer to sit
        // in. Drawing them there would put a page number in the middle of nothing.
        _documentRunning = setup.Continuous ? RunningContent.Empty : document.RunningContent;
        _running = _documentRunning;
        _firstPage = 1;
        _headerDistance = document.PageGeometry?.HeaderDistance;
        _footerDistance = document.PageGeometry?.FooterDistance;
        _fixedBands = (document.PageGeometry?.FixedHeaderHeight == true, document.PageGeometry?.FixedFooterHeight == true);
        _frame = null;
        bool IsHidden(int index) => DocumentSection.Containing(document.Sections, index)?.Hidden == true;
        _documentShapes = document.Shapes.Where(shape => !IsHidden(shape.ParagraphIndex)).ToArray();
        _background = document.PageBackground;
        _styleDefaults = document.StyleDefaults;
        _noteLabels = NoteNumberer.For(document.Paragraphs.Where((_, index) => !IsHidden(index)).ToArray(), document.NoteNumbering);
        _raisedNoteLabels = document.NoteNumbering.RaisedLabels;
        _noteBlocks.Clear();
        _paragraphTops.Clear();
        _grownHeights.Clear();
        _grownWidths.Clear();
        _linkedParagraphs.Clear();
        _linkedStarts.Clear();
        foreach (DocumentShape shape in _documentShapes)
            NoteGrownHeight(shape, setup);
        PrepareLinkedFrames(setup);
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
        DocumentPageBox requestedSetup = setup;
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
        int ownerFirstPage = 1;
        (double Top, double Bottom) BandOf(DocumentSection? section)
        {
            PageGeometry? selectedPage = section?.Page;
            if (section?.PageStyles is PageStyleSequence sequence && !setup.Continuous)
            {
                PageStyle style = sequence.At(pages.Count + 1 - ownerFirstPage);
                selectedPage = style.Page ?? selectedPage;
                _running = style.RunningContent;
                _background = style.Background;
                if (sectionPages && selectedPage is not null)
                    setup = requestedSetup.WithGeometry(selectedPage);
                sectionColumns.Clear();
            }
            if (setup.Continuous)
                return (setup.ContentTopPoints, setup.ContentTopPoints + columnHeight);

            if (!sectionPages || selectedPage is not PageGeometry page)
            {
                _frame = FrameFor(
                    pages.Count + 1, setup, setup.MarginTopPoints, setup.MarginBottomPoints,
                    _headerDistance, _footerDistance, setup.ContentLeftPoints, setup.ContentWidthPoints, _fixedBands);
            }
            else
            {
                double left = Math.Clamp(page.MarginLeft, 0, setup.WidthPoints - 1);
                _frame = FrameFor(
                    pages.Count + 1, setup, Math.Clamp(page.MarginTop, 0, setup.HeightPoints - 1), page.MarginBottom,
                    page.HeaderDistance, page.FooterDistance, left, Math.Max(1, setup.WidthPoints - page.MarginRight - left),
                    (page.FixedHeaderHeight, page.FixedFooterHeight));
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
            ownerFirstPage = pages.Count + 1;
            // Paper belongs to the section that opens a page. Keep an explicit
            // caller override, and keep continuous output on its original box.
            if (sectionPages && !requestedSetup.Continuous)
                setup = requestedSetup.WithGeometry(section?.Page ?? document.PageGeometry!);
            hasPendingOwner = false;
            pendingOwner = null;
            pendingSharesFirst = false;
            // The page this section starts takes the number it restarts the
            // count at, once: a section runs onto later pages as their owner too.
            if (section?.PageNumberStart is int restart && _restartedNumbering.Add(section))
                _numbering = (pages.Count + 1, restart);
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
                PageGeometry? ownPage = pageSection?.PageStyles?.At(pages.Count + 1 - ownerFirstPage).Page ?? pageSection?.Page;
                if (sectionPages && ownPage is PageGeometry page)
                {
                    left = Math.Clamp(page.MarginLeft, 0, setup.WidthPoints - 1);
                    width = Math.Max(1, setup.WidthPoints - page.MarginRight - left);
                }

                // The section's own indents apply within that text area on any
                // page, where its page margins applied only on a page it starts.
                left += section.LeftIndent;
                width = Math.Max(1, width - section.LeftIndent - section.RightIndent);

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
        var sectionPaint = new List<LayoutShape>();
        DocumentSection? paintedSection = null;
        double paintTop = y;

        void PaintSectionUntil(double bottom)
        {
            if (paintedSection is { } painted && !painted.Background.IsEmpty && painted.Background.A > 0 && bottom > paintTop)
                sectionPaint.Add(new LayoutShape(
                    new BRect(setup.ContentLeftPoints, paintTop, setup.ContentWidthPoints, bottom - paintTop),
                    ShapeFill.Solid(painted.Background), BColor.Empty, [], behindText: true, zOrder: int.MinValue));
            paintTop = bottom;
        }

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
        var pageFloatingTables = new List<LayoutFloatingTable>();
        var floatCarry = new Queue<FloatCarry>();

        bool ColumnHasContent() =>
            currentLines.Count > columnStart || (column == 0 && (regionTop > pageTop || floatBoxes.Count > 0 ||
                _documentShapes.Any(shape => shape.Wraps && (shape.AnchorPage == pages.Count + 1 ||
                    (!shape.IsPageAnchored && _paragraphTops.ContainsKey(shape.ParagraphIndex))))));

        // One count for the body, which the cells of its tables share: a list
        // that runs through a table counts on through it.
        var numbering = new ListNumberer();
        _lists = numbering;

        // The footnotes cited on the page being filled, and how much of the
        // foot of its text they take. The text's foot moves up by that much as
        // each is cited, so everything below that measures room against
        // contentBottom leaves room for them. A note is set on the page where
        // it is first cited; in a continuous render, which has no page foot,
        // footnotes follow the body with the endnotes.
        var pageNotes = new List<DocumentNote>();
        var placedNotes = new HashSet<DocumentNote>(ReferenceEqualityComparer.Instance);
        double noteArea = 0;

        // The height the footnotes cited in these lines would add to the page.
        double NotesHeight(IEnumerable<LayoutLine> lines)
        {
            if (setup.Continuous)
                return 0;

            double height = 0;
            bool first = pageNotes.Count == 0;
            foreach (DocumentNote note in CitedNotes(lines))
            {
                if (note.Kind != NoteKind.Footnote || placedNotes.Contains(note))
                    continue;

                if (first)
                {
                    height += SeparatorBlock;
                    first = false;
                }

                height += NoteBlockFor(note, setup).Height;
            }

            return height;
        }

        // Takes the footnotes cited in lines just placed onto the page.
        void TakeNotes(IEnumerable<LayoutLine> lines)
        {
            if (setup.Continuous)
                return;

            foreach (DocumentNote note in CitedNotes(lines))
            {
                if (note.Kind != NoteKind.Footnote || !placedNotes.Add(note))
                    continue;

                double height = NoteBlockFor(note, setup).Height + (pageNotes.Count == 0 ? SeparatorBlock : 0);
                pageNotes.Add(note);
                noteArea += height;
                contentBottom -= height;
            }
        }

        // Sets the page's footnotes at the foot of its text, under their line.
        void SetNotes()
        {
            if (pageNotes.Count == 0)
                return;

            double foot = contentBottom + noteArea;
            double top = foot - noteArea;
            double left = _frame?.Left ?? setup.ContentLeftPoints;
            double width = _frame?.Width ?? setup.ContentWidthPoints;
            currentCells.Add(new LayoutCell(
                new BRect(left, top + SeparatorGap, width * SeparatorWidthRatio, SeparatorThickness),
                _settings.DefaultForeground,
                default));

            double noteTop = top + SeparatorBlock;
            foreach (DocumentNote note in pageNotes)
            {
                NoteBlock block = NoteBlockFor(note, setup);
                foreach ((LayoutLine line, double offset) in block.Lines)
                {
                    line.Top = noteTop + offset;
                    line.Shift(left - setup.ContentLeftPoints);
                    currentLines.Add(line);
                }

                noteTop += block.Height;
            }

            contentBottom = foot;
            noteArea = 0;
            pageNotes.Clear();
        }

        void BreakPage()
        {
            SetNotes();
            PaintSectionUntil(contentBottom);
            var inlineLines = new List<LayoutLine>(currentLines);
            var inlineCells = new List<LayoutCell>(currentCells);
            currentLines.AddRange(floatLines);
            currentCells.AddRange(floatCells);
            pages.Add(NewPage(pages.Count + 1, setup, currentLines, currentCells, sectionPaint,
                pageFloatingTables, inlineLines, inlineCells));
            sectionPaint = [];
            currentLines = [];
            currentCells = [];
            floatLines = [];
            floatCells = [];
            pageFloatingTables = [];
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
            // BandOf can select different paper/margins in a page-style
            // sequence. Rebuild the text columns after that selection.
            columns = ColumnsOf(region, owner);
            y = pageTop;
            paintedSection = region;
            paintTop = y;
            regionTop = y;
            regionDepth = y;
            regionLines.Clear();
            regionUnbalanced = false;
            if (pages.Count >= _settings.MaxPages)
                truncated = true;

            if (!truncated)
            {
                int pending = floatCarry.Count;
                while (pending-- > 0)
                {
                    FloatCarry carry = floatCarry.Dequeue();
                    PlaceFloating(carry.Rows, carry.Left, pageTop, carry.Width, carry.Table, carry.HeaderStart, carry.RowOffset);
                }
            }
        }

        // The page's floating boxes as exclusions, for when the exclusions are
        // made again part way down a page.
        void AddFloatExclusions()
        {
            foreach (FloatBox box in floatBoxes)
            {
                if (!box.BehindText)
                {
                    _wrap.AddBox(box.Top, box.Bottom,
                        box.Left - setup.ContentLeftPoints, box.Right - setup.ContentLeftPoints, box.Wrap);
                }
            }
        }

        // Places a floating table's rows from top down, out of the flow: y does
        // not move. Rows that run past the foot of the page are cut where a row
        // may be cut, and what is left goes to the top of the next page, as
        // LibreOffice splits the frame it imports such a table into.
        void PlaceFloating(List<RowBox> rows, double left, double top, double width, DocumentTable table, ListNumberer headerStart, int rowOffset = 0)
        {
            TablePosition position = table.Position!;
            double remainingHeight = rows.Sum(row => row.Height);
            int pageNumber = pages.Count + 1;
            var wrappingShapes = GetPageWrappingShapes(pageNumber, setup).ToList();

            for (int attempt = 0; attempt < 16; attempt++)
            {
                double moved = top;
                double tableLeft = left - position.LeftFromText;
                double tableRight = left + width + position.RightFromText;
                double tableTop = moved - position.TopFromText;
                double tableBottom = moved + remainingHeight;

                foreach (FloatBox other in floatBoxes)
                {
                    if ((!position.AllowOverlap || !other.AllowOverlap) &&
                        tableLeft < other.Right &&
                        tableRight > other.Left &&
                        tableTop < other.Bottom &&
                        tableBottom > other.Top)
                    {
                        moved = Math.Max(moved, other.Bottom + position.TopFromText);
                        tableTop = moved - position.TopFromText;
                        tableBottom = moved + remainingHeight;
                    }
                }

                if (!position.AllowOverlap)
                {
                    foreach (var shapeBox in wrappingShapes)
                    {
                        if (tableLeft < shapeBox.Right &&
                            tableRight > shapeBox.Left &&
                            tableTop < shapeBox.Bottom &&
                            tableBottom > shapeBox.Top)
                        {
                            moved = Math.Max(moved, shapeBox.Bottom + position.TopFromText);
                            tableTop = moved - position.TopFromText;
                            tableBottom = moved + remainingHeight;
                        }
                    }
                }

                if (moved <= top + 0.01)
                    break;
                top = moved;
            }

            if (top >= contentBottom - 0.01)
            {
                floatCarry.Enqueue(new FloatCarry(rows, left, width, table, headerStart, rowOffset));
                return;
            }

            int floatLinesStart = floatLines.Count;
            int floatCellsStart = floatCells.Count;
            double rowTop = top;
            int headerCount = HeaderRowCount(table);
            if (rowOffset >= headerCount && rowOffset > 0 && headerCount > 0)
            {
                ListNumberer saved = _lists;
                List<RowBox> repeated;
                try
                {
                    _lists = headerStart.Snapshot();
                    repeated = ComposeTable(new DocumentTable(table.ParagraphIndex, table.ParagraphCount,
                        [.. table.Rows.Take(headerCount)], table.ColumnWidths, table.CellPadding, position,
                        preferredWidth: table.PreferredWidth, widthPercent: table.WidthPercent), document, setup, left, width);
                }
                finally { _lists = saved; }
                // Leave space for at least a line of body content; an oversized header must not prevent progress.
                double required = rows[0].CanSplit ? Math.Max(rows[0].MinimumHeight,
                    rows[0].Contents.SelectMany(content => content.Pieces.Take(1)).Select(piece => piece.Bottom).DefaultIfEmpty(rows[0].Height).Min()) : rows[0].Height;
                if (repeated.Sum(row => row.Height) + required <= contentBottom - rowTop)
                    foreach (RowBox header in repeated)
                    {
                        header.PlaceAt(rowTop, 0, floatLines, floatCells, _paragraphTops);
                        rowTop += header.Height;
                    }
            }
            bool placed = false;
            double totalFloatingHeight = rows.Sum(r => r.Height);
            for (int i = 0; i < rows.Count; i++)
            {
                RowBox row = rows[i];
                int actualRowIndex = rowOffset + i;
                bool rowKeepWithNext = actualRowIndex < table.Rows.Count && table.Rows[actualRowIndex].KeepWithNext;
                double nextOpening = i + 1 < rows.Count
                    ? (rows[i + 1].CanSplit ? rows[i + 1].NestedPieces().FirstOrDefault().Bottom : rows[i + 1].Height)
                    : 0;

                if (table.KeepTogether && totalFloatingHeight <= contentBottom - pageTop && rowTop + totalFloatingHeight > contentBottom)
                {
                    if (placed || rowTop > pageTop + 0.01)
                    {
                        floatCarry.Enqueue(new FloatCarry(rows.GetRange(i, rows.Count - i), left, width, table, headerStart, actualRowIndex));
                        break;
                    }
                }

                if (rowKeepWithNext && i + 1 < rows.Count && rowTop + row.Height + nextOpening > contentBottom)
                {
                    if (placed || rowTop > pageTop + 0.01)
                    {
                        floatCarry.Enqueue(new FloatCarry(rows.GetRange(i, rows.Count - i), left, width, table, headerStart, actualRowIndex));
                        break;
                    }
                }

                if (rowTop + row.Height > contentBottom)
                {
                    bool canSplitRow = row.CanSplit && !(table.KeepTogether && totalFloatingHeight <= contentBottom - pageTop);
                    if (canSplitRow && row.SplitAt(contentBottom - rowTop, allowStranding: !placed) is (RowBox fitted, var rest))
                    {
                        fitted.PlaceAt(rowTop, 0, floatLines, floatCells, _paragraphTops);
                        rowTop += fitted.Height;
                        placed = true;
                        if (rest is null)
                            continue;

                        var carried = new List<RowBox> { rest };
                        carried.AddRange(rows.Skip(i + 1));
                        floatCarry.Enqueue(new FloatCarry(carried, left, width, table, headerStart, rowOffset + i));
                        break;
                    }

                    if (placed || (row.Height <= contentBottom - pageTop && rowTop > pageTop + 0.01))
                    {
                        floatCarry.Enqueue(new FloatCarry(rows.GetRange(i, rows.Count - i), left, width, table, headerStart, rowOffset + i));
                        break;
                    }
                }

                row.PlaceAt(rowTop, 0, floatLines, floatCells, _paragraphTops);
                rowTop += row.Height;
                placed = true;
            }

            // Text runs beside the table where a side of the column has room for
            // it, and below it where neither does.
            if (!placed) return;

            var thisTableCells = floatCells.GetRange(floatCellsStart, floatCells.Count - floatCellsStart);
            var thisTableLines = floatLines.GetRange(floatLinesStart, floatLines.Count - floatLinesStart);
            var tableBounds = new BRect(left, top, width, Math.Max(0, rowTop - top));
            pageFloatingTables.Add(new LayoutFloatingTable(
                tableBounds,
                thisTableCells,
                thisTableLines,
                position.ZOrder,
                position.BehindText,
                position.AllowOverlap));

            double boxLeft = left - position.LeftFromText;
            double boxRight = left + width + position.RightFromText;
            double columnLeft = setup.ContentLeftPoints + columns.Offset(column);
            double room = Math.Max(boxLeft - columnLeft, columnLeft + columns.Width - boxRight);
            var box = new FloatBox(
                top - position.TopFromText,
                rowTop + position.BottomFromText,
                boxLeft,
                boxRight,
                room >= MinimumWrapRoom ? ShapeWrap.Square : ShapeWrap.TopAndBottom,
                position.AllowOverlap,
                position.ZOrder,
                position.BehindText);
            floatBoxes.Add(box);
            if (!position.BehindText)
            {
                _wrap.AddBox(box.Top, box.Bottom,
                    box.Left - setup.ContentLeftPoints, box.Right - setup.ContentLeftPoints, box.Wrap);
            }
        }

        // Where a table in the flow can start: below every floating table on the
        // page that it would run into, as LibreOffice moves an inline table down
        // past one (testInlineTableShiftDown) rather than drawing the two over
        // each other. The whole table moves, not the rows that meet it, so a
        // table is not cut in two around the one beside it.
        double BelowFloats(double top, List<RowBox> rows, double shift)
        {
            if (!rows.Any(row => row.Boxes.Count > 0))
                return top;

            int pageNumber = pages.Count + 1;
            var wrappingShapes = GetPageWrappingShapes(pageNumber, setup).ToList();
            if (floatBoxes.Count == 0 && wrappingShapes.Count == 0)
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
                    if (!box.BehindText && box.Left < right && box.Right > left && box.Top < bottom && box.Bottom > top)
                        moved = Math.Max(moved, box.Bottom);
                }

                foreach (var shapeBox in wrappingShapes)
                {
                    if (shapeBox.Left < right && shapeBox.Right > left && shapeBox.Top < bottom && shapeBox.Bottom > top)
                        moved = Math.Max(moved, shapeBox.Bottom);
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
            double width = table.FloatingWidthIn(_frame?.Width ?? setup.ContentWidthPoints, setup.WidthPoints);
            (double boxLeft, double boxWidth) = position.HorizontalAnchor switch
            {
                TableAnchor.Page => (0, setup.WidthPoints),
                TableAnchor.Margin => (_frame?.Left ?? setup.ContentLeftPoints, _frame?.Width ?? setup.ContentWidthPoints),
                _ => (setup.ContentLeftPoints + columns.Offset(column), columns.Width),
            };
            int pageNumber = pages.Count + 1;
            double left = position.LeftIn(boxLeft, boxWidth, width, pageNumber);
            ListNumberer headerStart = _lists.Snapshot();
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
            PlaceFloating(rows, left, top, width, table, headerStart);
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
        void EnsureParity(DocumentSection? s)
        {
            if (setup.Continuous || s is null)
                return;

            // Odd and even are the page's number, not its place: a section whose
            // count restarts at 2 starts on an even page with no blank one
            // before it, in Word and LibreOffice alike. The section has already
            // taken the page over here, so a restart it states is counted.
            int number = _numbering.Number + (pages.Count + 1) - _numbering.Page;
            bool isOdd = number % 2 != 0;
            bool needsBlank = (s.BreakType == SectionBreakType.OddPage && !isOdd) ||
                              (s.BreakType == SectionBreakType.EvenPage && isOdd);

            if (needsBlank)
            {
                RunningContent savedRunning = _running;
                _running = RunningContent.Empty;
                BreakPage();
                _running = savedRunning;
                _firstPage = pages.Count + 1;
                ownerFirstPage = pages.Count + 1;
            }
        }

        void EnterRegion(DocumentSection? next, bool startsPage, bool opensPage)
        {
            if (!startsPage)
            {
                // The space after the last paragraph, measured in the column it
                // is in, before y moves down to the deepest one.
                FlowItem? last = regionLines.Count > 0 ? regionLines[^1] : null;
                double trailing = last is null ? 0 : Math.Max(0, y - (last.Top + last.Height));
                y = Math.Max(regionDepth, y);
                if (region is not null && columns.Count > 1 && !regionUnbalanced && !region.DontBalanceColumns && last is not null)
                {
                    if (BalanceColumns(regionLines, columns, regionTop, contentBottom - regionTop, _paragraphTops) is double height)
                    {
                        y = regionTop + height + trailing;
                        regionDepth = y;

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
                                _wrap.Add(shape, shape.TopOnPage(anchor), height: BoxHeight(shape), width: BoxWidth(shape));
                            }
                        }

                        AddFloatExclusions();
                    }
                }
            }

            PaintSectionUntil(startsPage ? contentBottom : Math.Max(regionDepth, y));
            region = next;
            bool pageHasContent = currentLines.Count > 0 || currentCells.Count > 0 || floatLines.Count > 0 || floatCells.Count > 0;
            paintedSection = opensPage && pageHasContent ? null : next;
            paintTop = y;

            // A section that opens a page, the first included, sets its top and
            // foot; one that follows on a page keeps those of the page it is on.
            if (!pageHasContent)
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
            paintTop = y;
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
            TakeNotes(currentLines.Skip(linesFrom));
            if (region is not null && columns.Count > 1)
            {
                regionLines.Add(new FlowItem(
                    currentLines.GetRange(linesFrom, currentLines.Count - linesFrom),
                    currentCells.GetRange(cellsFrom, currentCells.Count - cellsFrom),
                    y, row.Height, column, GapBetween: 0, GapAtTop: 0, StartsParagraph: false));
            }
        }

        void ReanchorParagraph(int index)
        {
            _paragraphTops[index] = y;
            _wrap = new TextWrapExclusions();
            AddPageExclusions(pages.Count + 1);
            foreach (DocumentShape shape in _documentShapes)
            {
                if (shape.Wraps && !shape.IsPageAnchored &&
                    _paragraphTops.TryGetValue(shape.ParagraphIndex, out double anchor))
                    _wrap.Add(shape, shape.TopOnPage(anchor), height: BoxHeight(shape), width: BoxWidth(shape));
            }
            AddFloatExclusions();
        }

        int paragraphIndex = 0;
        while (paragraphIndex < document.ParagraphCount && !truncated)
        {
            DocumentTable? table = DocumentTable.StartingAt(document.Tables, paragraphIndex);
            DocumentSection? section = DocumentSection.Containing(document.Sections, paragraphIndex);
            if (section?.Hidden == true && (table is null || table.ParagraphEnd <= section.ParagraphEnd))
            {
                paragraphIndex = section.ParagraphEnd;
                continue;
            }
            bool isSectionStart = section is not null && section.ParagraphIndex == paragraphIndex;
            bool sectionBreaksPage = isSectionStart && section!.BreakType is SectionBreakType.NextPage or SectionBreakType.OddPage or SectionBreakType.EvenPage;

            if (!ReferenceEquals(section, region))
            {
                bool opensPage = (table?.PageBreakBefore ?? document.Paragraphs[paragraphIndex].Style.PageBreakBefore) || sectionBreaksPage;
                // Finish floating content on its owning section's paper before a
                // following section changes the page geometry or running content.
                if (opensPage && !setup.Continuous)
                    while (floatCarry.Count > 0 && !truncated) BreakPage();
                if (truncated) break;
                bool startsPage = !setup.Continuous && (currentLines.Count > 0 || currentCells.Count > 0 || floatLines.Count > 0 || floatCells.Count > 0) && opensPage;
                EnterRegion(section, startsPage, opensPage);
            }

            if (table is not null && (table.PageBreakBefore || table.ColumnBreakBefore || sectionBreaksPage))
            {
                bool pageBreak = table.PageBreakBefore || sectionBreaksPage;
                if (setup.Continuous)
                    ignoredBreaks++;
                else if (pageBreak && (currentLines.Count > 0 || floatLines.Count > 0 || currentCells.Count > 0 || floatCells.Count > 0))
                {
                    OwnBreak();
                    BreakPage();
                }
                else if (!pageBreak && table.ColumnBreakBefore && ColumnHasContent())
                {
                    regionUnbalanced = true;
                    BreakColumn();
                }
                if (truncated)
                    break;
            }

            if (isSectionStart)
            {
                EnsureParity(section);
                if (truncated)
                    break;
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
                ListNumberer headerStart = numbering.Snapshot();

                // Space above a table, as above a paragraph, opens no page or
                // column: a table that starts one starts at its top.
                if (ColumnHasContent())
                    y += table.SpacingBefore;
                List<RowBox> composedRows = ComposeTable(table, document, setup, setup.ContentLeftPoints, columns.Width);
                y = BelowFloats(y, composedRows, columns.Offset(column));
                double totalTableHeight = composedRows.Sum(row => row.Height);
                if (table.KeepTogether && !setup.Continuous && totalTableHeight <= contentBottom - pageTop && y + totalTableHeight > contentBottom && ColumnHasContent())
                {
                    if (region is not null && columns.Count > 1)
                        regionUnbalanced = true;
                    BreakColumn();
                    y = BelowFloats(y, composedRows, columns.Offset(column));
                }
                if (table.KeepWithNext && region is not null && columns.Count > 1)
                    regionUnbalanced = true;
                if (table.KeepWithNext && !setup.Continuous && table.ParagraphEnd < document.ParagraphCount &&
                    DocumentTable.StartingAt(document.Tables, table.ParagraphEnd) is null)
                {
                    RichTextParagraph following = document.Paragraphs[table.ParagraphEnd];
                    if (!following.Style.PageBreakBefore && !following.Style.ColumnBreakBefore &&
                        ReferenceEquals(DocumentSection.Containing(document.Sections, table.ParagraphEnd), region))
                    {
                        ListNumberer probe = numbering.Snapshot();
                        ParagraphLines next = ComposeParagraph(following, probe.Advance(following.Style), setup,
                            table.ParagraphEnd, columnWidthOverride: columns.Width);
                        double nextHeight = next.Lines.Sum(line => line.Height);
                        if (nextHeight > contentBottom - pageTop)
                            nextHeight = next.Lines.Take(Math.Max(1, following.Style.OrphanLines ?? 1)).Sum(line => line.Height);
                        double required = composedRows.Sum(row => row.Height) + Math.Max(0, following.Style.SpacingBefore) + nextHeight;
                        if (required <= contentBottom - pageTop && y + required > contentBottom && ColumnHasContent())
                        {
                            regionUnbalanced = true;
                            BreakColumn();
                        }
                    }
                }


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

                    ListNumberer saved = _lists;
                    List<RowBox> repeated;
                    try
                    {
                        _lists = headerStart.Snapshot();
                        repeated = ComposeTable(
                            new DocumentTable(table.ParagraphIndex, table.ParagraphCount, [.. table.Rows.Take(headerCount)],
                                table.ColumnWidths, table.CellPadding, preferredWidth: table.PreferredWidth,
                                widthPercent: table.WidthPercent, alignment: table.Alignment, leftIndent: table.LeftIndent),
                            document, setup, setup.ContentLeftPoints, columns.Width);
                    }
                    finally { _lists = saved; }

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
                    int groupEnd = rowNumber;
                    for (int index = rowNumber - 1; index < groupEnd && index < table.Rows.Count; index++)
                    {
                        int spanEnd = index + table.Rows[index].Cells.Select(cell => cell.RowSpan).DefaultIfEmpty(1).Max();
                        int keepEnd = table.Rows[index].KeepWithNext ? index + 2 : index + 1;
                        groupEnd = Math.Min(table.Rows.Count, Math.Max(groupEnd, Math.Max(spanEnd, keepEnd)));
                    }
                    if (table.KeepTogether && totalTableHeight <= contentBottom - pageTop)
                        groupEnd = table.Rows.Count;

                    double groupHeight = composedRows.Skip(rowNumber - 1).Take(groupEnd - rowNumber + 1).Sum(item => item.Height);
                    double minRequired = composedRows.Skip(rowNumber - 1).Take(groupEnd - rowNumber).Sum(r => r.Height) +
                        (composedRows[groupEnd - 1].CanSplit ? composedRows[groupEnd - 1].NestedPieces().FirstOrDefault().Bottom : composedRows[groupEnd - 1].Height);
                    double requiredToKeep = groupHeight <= contentBottom - pageTop ? groupHeight : minRequired;

                    if (groupEnd > rowNumber && !setup.Continuous && requiredToKeep <= contentBottom - pageTop && y + requiredToKeep > contentBottom && ColumnHasContent())
                        ContinueTable(inHeader);
                    RowBox? row = composedRow;
                    while (row is not null && !truncated)
                    {
                        if (y + row.Height > contentBottom - NotesHeight(row.Lines))
                        {
                            bool canSplitRow = row.CanSplit && !(table.KeepTogether && totalTableHeight <= contentBottom - pageTop);
                            if (canSplitRow && row.SplitAt(contentBottom - y, allowStranding: !ColumnHasContent() || headerOnly) is (RowBox fitted, var rest))
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

                spacingAfterPrevious = table.SpacingAfter;
                y += spacingAfterPrevious;
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
            bool styleBreaksPage = style.PageBreakBefore || sectionBreaksPage;
            if (styleBreaksPage && !setup.Continuous && (currentLines.Count > 0 || floatLines.Count > 0))
            {
                OwnBreak();
                BreakPage();
                if (truncated)
                    break;
            }
            else if (style.ColumnBreakBefore && !styleBreaksPage && !setup.Continuous && ColumnHasContent())
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
            else if ((styleBreaksPage || style.ColumnBreakBefore) && setup.Continuous)
            {
                ignoredBreaks++;
            }

            if (!sectionBreaksPage && isSectionStart && section!.BreakType == SectionBreakType.NextColumn && ColumnHasContent())
            {
                regionUnbalanced = true;
                if (column + 1 < columns.Count)
                    NextColumn();
                else
                    BreakPage();
                if (truncated)
                    break;
            }

            if (isSectionStart)
            {
                EnsureParity(section);
                if (truncated)
                    break;
            }

            // Move a wrapping frame and its anchor together when it cannot fit
            // below preceding content. An oversized frame is allowed once on a
            // fresh page rather than repeatedly making empty pages.
            if (!setup.Continuous && ColumnHasContent() && _documentShapes.Any(shape =>
                shape.Wraps && !shape.IsPageAnchored && !shape.OffsetFromPage && shape.ParagraphIndex == paragraphIndex &&
                shape.TopOnPage(y) + BoxHeight(shape) > contentBottom))
            {
                BreakColumn();
                if (truncated)
                    break;
            }
            _paragraphTops.TryAdd(paragraphIndex, y);

            // A paragraph whose text and mark are all hidden takes no room, as
            // LibreOffice hides it; the break it asked for was taken above.
            if (IsWhollyHidden(paragraph) && !_documentShapes.Any(shape => shape.ParagraphIndex == paragraphIndex))
            {
                paragraphIndex++;
                continue;
            }

            // A shape's box is known once the paragraph it hangs from has a top,
            // so this paragraph's shapes join the exclusions before its own lines
            // are wrapped. One anchored further down cannot narrow a line above
            // it, which is what a single forward pass can honestly say.
            foreach (DocumentShape shape in _documentShapes)
            {
                if (shape.Wraps && !shape.IsPageAnchored && shape.ParagraphIndex == paragraphIndex)
                    _wrap.Add(shape, shape.TopOnPage(y), height: BoxHeight(shape), width: BoxWidth(shape));
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
            if (!setup.Continuous)
            {
                double capacity = contentBottom - pageTop;
                double required = composed.Lines.Sum(line => line.Height + line.LeadingSkip);
                if (style.KeepWithNext)
                {
                    ListNumberer probe = numbering.Snapshot();
                    ParagraphStyle previous = style;
                    for (int next = paragraphIndex + 1; previous.KeepWithNext && next < document.ParagraphCount; next++)
                    {
                        RichTextParagraph following = document.Paragraphs[next];
                        DocumentTable? followingTable = DocumentTable.StartingAt(document.Tables, next);
                        if ((followingTable is null && (following.Style.PageBreakBefore || following.Style.ColumnBreakBefore)) ||
                            !ReferenceEquals(DocumentSection.Containing(document.Sections, next), section))
                            break;
                        if (followingTable is not null)
                        {
                            if (followingTable.IsFloating || followingTable.PageBreakBefore || followingTable.ColumnBreakBefore)
                                break;
                            ListNumberer saved = _lists;
                            List<RowBox> rows;
                            try
                            {
                                _lists = probe;
                                rows = ComposeTable(followingTable, document, setup, setup.ContentLeftPoints, columns.Width);
                            }
                            finally { _lists = saved; }
                            if (rows.Count > 0)
                            {
                                // Keep with the start of a splittable row, not
                                // necessarily the entire row. Requiring a whole
                                // row can move a heading that already has room
                                // for the table's opening on its current page.
                                double opening = rows[0].CanSplit
                                    ? rows[0].NestedPieces().FirstOrDefault().Bottom : rows[0].Height;
                                if (followingTable.KeepTogether && rows.Sum(r => r.Height) <= capacity)
                                    opening = rows.Sum(r => r.Height);
                                else if (followingTable.Rows.Count > 0 && followingTable.Rows[0].KeepWithNext && rows.Count > 1)
                                    opening += rows[1].CanSplit ? rows[1].NestedPieces().FirstOrDefault().Bottom : rows[1].Height;
                                required += Math.Max(0, previous.SpacingAfter) + opening;
                            }
                            break;
                        }
                        ParagraphLines measured = ComposeParagraph(following, probe.Advance(following.Style),
                            setup, next, columnWidthOverride: columns.Width);
                        double followingHeight = measured.Lines.Sum(line => line.Height);
                        if (!following.Style.KeepWithNext && followingHeight > capacity)
                            followingHeight = measured.Lines.Take(Math.Max(1, following.Style.OrphanLines ?? 1))
                                .Sum(line => line.Height);
                        required += Math.Max(0, previous.SpacingAfter) + Math.Max(0, following.Style.SpacingBefore) + followingHeight;
                        previous = following.Style;
                        if (required > capacity)
                            break;
                    }
                }

                if ((style.KeepTogether || style.KeepWithNext) && required <= capacity &&
                    y + spacingBefore + required > contentBottom - NotesHeight(composed.Lines) && ColumnHasContent())
                {
                    regionUnbalanced = true;
                    BreakColumn();
                    spacingBefore = 0;
                    ReanchorParagraph(paragraphIndex);
                    composed = ComposeParagraph(paragraph, marker, setup, paragraphIndex,
                        columnWidthOverride: columns.Width, wrapTop: y);
                }
            }
            if (region is not null && columns.Count > 1 &&
                (style.KeepTogether || style.KeepWithNext || style.OrphanLines is > 0 || style.WidowLines is > 0))
                regionUnbalanced = true;
            y += spacingBefore;

            double[] lineHeights = [.. composed.Lines.Select(line => line.Height + line.LeadingSkip)];
            int segmentEnd = -1;
            bool first = true;
            for (int lineIndex = 0; lineIndex < composed.Lines.Count; lineIndex++)
            {
                LayoutLine line = composed.Lines[lineIndex];
                if (!setup.Continuous && (style.OrphanLines is > 0 || style.WidowLines is > 0) && lineIndex >= segmentEnd)
                {
                    if (segmentEnd >= 0 && ColumnHasContent())
                    {
                        regionUnbalanced = true;
                        BreakColumn();
                    }
                    int fit = ParagraphPagination.LinesThatFit(lineHeights, lineIndex,
                        contentBottom - y, style.OrphanLines ?? 0, style.WidowLines ?? 0);
                    if (fit == 0 && ColumnHasContent())
                    {
                        regionUnbalanced = true;
                        BreakColumn();
                        if (first)
                        {
                            ReanchorParagraph(paragraphIndex);
                            composed = ComposeParagraph(paragraph, marker, setup, paragraphIndex,
                                columnWidthOverride: columns.Width, wrapTop: y);
                            lineHeights = [.. composed.Lines.Select(item => item.Height + item.LeadingSkip)];
                            line = composed.Lines[lineIndex];
                        }
                        fit = ParagraphPagination.LinesThatFit(lineHeights, lineIndex,
                            contentBottom - y, style.OrphanLines ?? 0, style.WidowLines ?? 0);
                    }
                    // Impossible constraints on an empty column must never
                    // produce an endless series of blank pages.
                    if (fit == 0)
                        fit = ParagraphPagination.LinesThatFit(lineHeights, lineIndex, contentBottom - y, 0, 0);
                    segmentEnd = lineIndex + Math.Max(1, fit);
                }
                y += line.LeadingSkip;

                // A line that cites a footnote needs room for the note at the
                // foot of the page as well as for itself.
                if (y + line.Height > contentBottom - NotesHeight([line]) && ColumnHasContent())
                {
                    BreakColumn();
                    if (truncated)
                        break;
                    if (first)
                    {
                        // No line of this paragraph was placed yet. Its new
                        // page can have different margins/paper or exclusions,
                        // so measure it there before placing its first line.
                        composed = ComposeParagraph(paragraph, marker, setup, paragraphIndex,
                            columnWidthOverride: columns.Width, wrapTop: y);
                        lineHeights = [.. composed.Lines.Select(item => item.Height + item.LeadingSkip)];
                        lineIndex = -1;
                        segmentEnd = -1;
                        continue;
                    }
                }

                line.Top = y;
                line.Shift(columns.Offset(column));
                currentLines.Add(line);
                TakeNotes([line]);
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

        PaintSectionUntil(Math.Max(regionDepth, y));
        paintedSection = null;

        // Endnotes follow the body, under a line of their own, in the order
        // they are cited; in a continuous render the footnotes go with them.
        List<DocumentNote> trailing = [.. DocumentNote.CitedIn(document.Paragraphs)
            .Distinct<DocumentNote>(ReferenceEqualityComparer.Instance)
            .Where(note => note.Kind == NoteKind.Endnote || setup.Continuous)];
        if (trailing.Count > 0 && !truncated)
        {
            region = null;
            columns = pageColumns;
            column = 0;
            columnStart = currentLines.Count;
            if (y + SeparatorBlock > contentBottom && currentLines.Count > 0)
                BreakPage();

            currentCells.Add(new LayoutCell(
                new BRect(_frame?.Left ?? setup.ContentLeftPoints, y + SeparatorGap,
                    (_frame?.Width ?? setup.ContentWidthPoints) * SeparatorWidthRatio, SeparatorThickness),
                _settings.DefaultForeground,
                default));
            y += SeparatorBlock;

            foreach (DocumentNote note in trailing)
            {
                for (int i = 0; i < note.Paragraphs.Count && !truncated; i++)
                {
                    RichTextParagraph paragraph = note.Paragraphs[i];
                    ParagraphLines composed = ComposeNoteParagraph(note, i, setup);
                    y += i == 0 ? 0 : Math.Max(0, paragraph.Style.SpacingBefore);
                    foreach (LayoutLine line in composed.Lines)
                    {
                        if (y + line.Height > contentBottom && currentLines.Count > columnStart)
                        {
                            BreakPage();
                            if (truncated)
                                break;
                            columnStart = 0;
                        }

                        line.Top = y;
                        currentLines.Add(line);
                        y += line.Height;
                    }

                    y += Math.Max(0, paragraph.Style.SpacingAfter);
                }
            }
        }

        // A floating table that ran past the last page of text still has rows
        // to stand on the pages after it.
        while (floatCarry.Count > 0 && !truncated)
            BreakPage();

        SetNotes();
        var finalInlineLines = new List<LayoutLine>(currentLines);
        var finalInlineCells = new List<LayoutCell>(currentCells);
        currentLines.AddRange(floatLines);
        currentCells.AddRange(floatCells);
        if (currentLines.Count > 0 || currentCells.Count > 0 || pages.Count == 0)
            pages.Add(NewPage(pages.Count + 1, setup, currentLines, currentCells, sectionPaint,
                pageFloatingTables, finalInlineLines, finalInlineCells));

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

    private LayoutPage NewPage(int number, DocumentPageBox setup, List<LayoutLine> lines, List<LayoutCell> cells,
        List<LayoutShape>? sectionPaint = null,
        List<LayoutFloatingTable>? floatingTables = null,
        List<LayoutLine>? inlineLines = null,
        List<LayoutCell>? inlineCells = null)
    {
        PageSelection selection = SelectionFor(number);
        NotePageStart(number, lines);

        // The frame the page's text was laid out in. A page made after the text
        // ran out, to stand a page-anchored shape on, has none of its own and
        // takes the last page's margins with its own header and footer.
        PageFrame frame = _frame is { } current
            ? current.Number == number ? current : FrameFor(number, setup, current)
            : FrameFor(
                number, setup, setup.MarginTopPoints, setup.MarginBottomPoints,
                _headerDistance, _footerDistance, setup.ContentLeftPoints, setup.ContentWidthPoints, _fixedBands);

        int pageNumber = _numbering.Number + number - _numbering.Page;
        var headerLines = RunningLines(
            WithPageFields(_running.EffectiveHeader(selection), pageNumber, number),
            setup,
            frame,
            top: 0,
            band: frame.TextTop,
            topDistance: frame.HeaderDistance);
        var footerLines = RunningLines(
            WithPageFields(_running.EffectiveFooter(selection), pageNumber, number),
            setup,
            frame,
            top: frame.TextBottom,
            band: setup.HeightPoints - frame.TextBottom,
            bottomDistance: frame.FooterDistance);

        var all = new List<LayoutLine>(lines);
        all.AddRange(headerLines);
        all.AddRange(footerLines);

        var allInline = inlineLines is not null ? new List<LayoutLine>(inlineLines) : new List<LayoutLine>(lines);
        allInline.AddRange(headerLines);
        allInline.AddRange(footerLines);

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
        List<LayoutShape> shapes = PlaceShapes(setup, number, lines);
        if (sectionPaint is not null)
            shapes.AddRange(sectionPaint);
        shapes.AddRange(PlaceRunningShapes(_running.EffectiveHeaderShapes(selection), setup));
        shapes.AddRange(PlaceRunningShapes(_running.EffectiveFooterShapes(selection), setup));
        shapes = [.. shapes.OrderBy(shape => shape.ZOrder)];

        return new LayoutPage(number, setup.WidthPoints, setup.HeightPoints, all, shapes, cells, _background,
            _background?.ContentOnly == true ? new BRect(setup.ContentLeftPoints, setup.ContentTopPoints,
                setup.ContentWidthPoints, setup.ContentHeightPoints) : null,
            floatingTables: floatingTables,
            inlineLines: allInline,
            inlineCells: inlineCells ?? cells);
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
        double? headerDistance, double? footerDistance, double left, double width, (bool Header, bool Footer) fixedBands = default)
    {
        double top = marginTop;
        double bottom = Math.Max(top + 1, setup.HeightPoints - marginBottom);
        PageSelection selection = SelectionFor(number);

        // A band of fixed height keeps the text where the margin puts it,
        // however much its header or footer holds.
        if (!fixedBands.Header && headerDistance is double fromTop && double.IsFinite(fromTop) && fromTop >= 0)
        {
            IReadOnlyList<RichTextParagraph> headerParagraphs = _running.EffectiveHeader(selection);
            if (headerParagraphs.Count == 0 && _running.HasAnyHeader && fromTop >= top)
                headerParagraphs = [RichTextParagraph.Empty];
            double header = RunningHeight(headerParagraphs, setup, left, width);
            double overrun = fromTop + header - top;
            if (header > 0 && overrun > 0 && overrun <= (bottom - top) / 2)
                top += overrun;
        }

        if (!fixedBands.Footer && footerDistance is double fromBottom && double.IsFinite(fromBottom) && fromBottom >= 0)
        {
            double footer = RunningHeight(_running.EffectiveFooter(selection), setup, left, width);
            double overrun = fromBottom + footer - (setup.HeightPoints - bottom);
            if (footer > 0 && overrun > 0 && overrun <= (bottom - top) / 2)
                bottom -= overrun;
        }

        return new PageFrame(number, marginTop, marginBottom, headerDistance, footerDistance, left, width, top, bottom, fixedBands);
    }

    /// <summary>Page <paramref name="number"/>'s frame on the paper and margins of <paramref name="like"/>.</summary>
    private PageFrame FrameFor(int number, DocumentPageBox setup, PageFrame like) =>
        FrameFor(number, setup, like.MarginTop, like.MarginBottom, like.HeaderDistance, like.FooterDistance, like.Left, like.Width, like.FixedBands);

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
        double TextBottom,
        (bool Header, bool Footer) FixedBands = default);

    /// <summary>
    /// Adds the wrapping shapes anchored to page <paramref name="number"/> to the
    /// exclusions, which are page-local and were just emptied for it.
    /// </summary>
    private void AddPageExclusions(int number)
    {
        foreach (DocumentShape shape in _documentShapes)
        {
            if (shape.Wraps && shape.AnchorPage == number)
                _wrap.Add(shape, shape.OffsetY, height: BoxHeight(shape), width: BoxWidth(shape));
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
    /// True when <paramref name="shape"/> is drawn on the page: always, unless
    /// the document keeps it off the printed page and the host has not asked to
    /// see such shapes. Only the drawing is skipped; the exclusions that keep
    /// text clear of it were taken from every shape.
    /// </summary>
    private bool Draws(DocumentShape shape) => shape.Printable || _settings.DrawNonPrintingShapes;

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
            if (!Draws(shape))
                continue;
            var bounds = new BRect(
                setup.ContentLeftPoints + shape.OffsetX,
                shape.OffsetY,
                BoxWidth(shape),
                BoxHeight(shape));
            if ((bounds.Width <= 0 || bounds.Height <= 0) && !shape.IsRule)
                continue;

            (List<LayoutLine> text, List<LayoutCell> cells) = PlaceShapeContent(shape, bounds, setup);
            placed.Add(new LayoutShape(
                bounds,
                shape.Fill,
                shape.Outline,
                text,
                shape.Image,
                shape.BehindText,
                shape.ZOrder,
                cells, shape.Geometry, shape.OutlineWidth));
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
    private List<LayoutShape> PlaceShapes(DocumentPageBox setup, int number, IReadOnlyList<LayoutLine> sourceLines)
    {
        var placed = new List<LayoutShape>();
        if (_documentShapes.Count == 0)
            return placed;

        var cellTextAreas = new Dictionary<int, (double Left, double Width)>();
        if (_documentShapes.Any(shape => shape.OffsetXFromCell))
            foreach (LayoutLine line in sourceLines)
                cellTextAreas.TryAdd(line.ParagraphIndex, (line.TextAreaLeft, line.TextAreaWidth));

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

            var area = shape.OffsetXFromCell && cellTextAreas.TryGetValue(shape.ParagraphIndex, out var cellArea)
                ? cellArea : (Left: setup.ContentLeftPoints, Width: setup.ContentWidthPoints);
            double offset = shape.CellHorizontalAlignment switch
            {
                TextAlignment.Left => 0,
                TextAlignment.Center => (area.Width - BoxWidth(shape)) / 2,
                TextAlignment.Right => area.Width - BoxWidth(shape),
                _ => shape.OffsetX,
            };
            var bounds = new BRect(area.Left + offset, shape.TopOnPage(anchorTop),
                BoxWidth(shape), BoxHeight(shape));

            if (((bounds.Width <= 0 || bounds.Height <= 0) && !shape.IsRule) || !Draws(shape))
                continue;

            (List<LayoutLine> text, List<LayoutCell> cells) = PlaceShapeContent(shape, bounds, setup);
            placed.Add(new LayoutShape(bounds, shape.Fill, shape.Outline, text,
                shape.Image, shape.BehindText, shape.ZOrder, cells, shape.Geometry, shape.OutlineWidth));
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
        double[] edges = table.AutoFit
            ? AutoColumnEdges(table, document, left, width) : ColumnEdges(table, left, width);
        var boxes = new List<RowBox>(table.Rows.Count);

        foreach (TableRow row in table.Rows)
        {
            // A row may break across a page where every cell can be cut on its
            // own line boundary, or between the rows of a table nested in it. A
            // box that spans rows would need cutting with the rows it spans, so a
            // row holding one stays whole.
            var box = new RowBox
            {
                MinimumHeight = row.MinHeight,
                CanSplit = !row.ExactHeight && row.CanSplit &&
                    row.Cells.All(cell => cell.RowSpan == 1 && !cell.IsRowSpanContinuation),
                ExactHeight = row.ExactHeight,
            };
            foreach (TableCell cell in row.Cells)
            {
                (double cellLeft, double cellWidth) = ColumnSpanBox(edges, cell);
                TableCellPadding padding = cell.PaddingFor(table);
                double textWidth = Math.Max(1.0, cellWidth - padding.Left - padding.Right);

                (List<LayoutLine> lines, List<LayoutCell> nested, List<CutPiece> pieces, double height) =
                    ComposeBlocks(document, cell.Tables, cell.ParagraphIndex, cell.ParagraphEnd, setup, cellLeft + padding.Left, textWidth);

                // Text that runs past the cell is cut at its edge, as Writer
                // cuts a text portion; the innermost cell's edge wins for a
                // nested table. A picture is a frame of its own there, drawn
                // whole, so a line holding one is left as it is. So is a line
                // a tab carried over the margin (TabsOverMargin): Writer paints
                // such a line to the page's edge (SwTextPainter::DrawTextLine).
                double cellRight = cellLeft + cellWidth;
                foreach (LayoutLine line in lines)
                {
                    line.Top += padding.Top;
                    if (line.Pieces.Any(piece => piece.X + piece.Width > cellRight + 0.01) &&
                        !line.Pieces.Any(piece => piece.IsImage) &&
                        !(_styleDefaults.TabsOverMargin && line.Pieces.Any(piece => piece.IsTab)))
                        line.ClipRight = Math.Min(line.ClipRight ?? cellRight, cellRight);
                }
                foreach (LayoutCell inner in nested)
                    inner.Bounds = new BRect(inner.Bounds.Left, inner.Bounds.Top + padding.Top, inner.Bounds.Width, inner.Bounds.Height);
                for (int piece = 0; piece < pieces.Count; piece++)
                    pieces[piece] = new CutPiece(pieces[piece].Top + padding.Top, pieces[piece].Bottom + padding.Top);
                height += padding.Top + padding.Bottom;
                if (pieces.Count > 0)
                {
                    pieces[0] = pieces[0] with { Top = 0 };
                    pieces[^1] = pieces[^1] with { Bottom = height };
                }

                box.Lines.AddRange(lines);
                box.Cells.AddRange(nested);
                box.Contents.Add(new CellLines(lines, nested, pieces, height, cell));
                if (cell.RowSpan <= 1 && !cell.IsRowSpanContinuation)
                    box.Height = Math.Max(box.Height, height);

                // A cell the merge above it covers is drawn by no one: the cell
                // that opened the merge draws the whole of it.
                if (!cell.IsRowSpanContinuation)
                    box.Boxes.Add(new CellBox(cellLeft, cellWidth, cell));
            }

            // The codecs already preserve row heights. Apply the floor before
            // extending merged cells or deciding where a row breaks.
            box.Height = row.ExactHeight ? row.MinHeight : Math.Max(box.Height, row.MinHeight);
            boxes.Add(box);
        }

        for (int index = 0; index < boxes.Count; index++)
        {
            foreach (CellLines content in boxes[index].Contents.Where(content => content.Cell is { RowSpan: > 1 }))
            {
                int end = Math.Min(boxes.Count, index + content.Cell!.RowSpan);
                double deficit = content.Height - boxes.Skip(index).Take(end - index).Sum(row => row.Height);
                for (int last = end - 1; deficit > 0 && last >= index; last--)
                {
                    if (table.Rows[last].ExactHeight)
                        continue;
                    boxes[last].Height += deficit;
                    break;
                }
            }
        }
        ExtendRowSpans(boxes);
        for (int index = 0; index < boxes.Count; index++)
        {
            if (!table.Rows[index].ExactHeight)
                continue;
            RowBox row = boxes[index];
            foreach (CellLines content in row.Contents)
            {
                double limit = row.Height + (row.Boxes.FirstOrDefault(box => ReferenceEquals(box.Cell, content.Cell))?.ExtraHeight ?? 0);
                foreach (LayoutLine line in content.Lines)
                {
                    if (line.Top >= limit)
                        row.Lines.Remove(line);
                    else if (line.Top + line.Height > limit)
                        line.ClipHeight = Math.Min(line.ClipHeight ?? line.Height, limit - line.Top);
                }
                foreach (LayoutCell nested in content.Cells)
                    nested.Bounds = new BRect(nested.Bounds.Left, nested.Bounds.Top, nested.Bounds.Width,
                        Math.Max(0, Math.Min(nested.Bounds.Height, limit - nested.Bounds.Top)));
            }
            row.Cells.RemoveAll(cell => cell.Bounds.Height <= 0);
        }
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
    /// Content-based column sizes for an auto-fit table using a two-pass algorithm:
    /// Pass 1: Longest unbreakable tokens, nested tables, paragraph indents, and cell padding/borders
    ///         supply minimum widths, and unwrapped lines supply preferred widths. Spanning cells
    ///         distribute deficits proportionally across their spanned columns.
    /// Pass 2: Constrains and distributes column widths against the target width across deficit,
    ///         flexible intermediate, and expansion regimes, respecting alignment and margins.
    /// </summary>
    private double[] AutoColumnEdges(DocumentTable table, RichTextDocument document, double left, double available)
    {
        int columns = ColumnCount(table);
        (double[] minimum, double[] preferred) = MeasureColumnExtents(table, document, 0);

        double minTotal = minimum.Sum();
        double maxTotal = preferred.Sum();

        bool explicitWidth = table.PreferredWidth is > 0 || table.WidthPercent is > 0;
        double target = table.IsFloating
            ? (explicitWidth ? table.FloatingWidthIn(available, available) : Math.Min(available, maxTotal))
            : (explicitWidth ? table.WidthIn(available) : Math.Min(available, maxTotal));

        double[] widths = new double[columns];

        if (target <= minTotal)
        {
            // Deficit / overflow: compress columns proportionally down to target
            double scale = minTotal > 0 ? target / minTotal : 1.0;
            for (int c = 0; c < columns; c++)
                widths[c] = Math.Max(1.0, minimum[c] * scale);
        }
        else if (target < maxTotal && maxTotal > minTotal)
        {
            // Intermediate regime: allocate minimum plus a share of the column's flexible range
            double flexTotal = maxTotal - minTotal;
            double flexUsed = target - minTotal;
            for (int c = 0; c < columns; c++)
            {
                double flex = preferred[c] - minimum[c];
                widths[c] = minimum[c] + (flex * flexUsed / flexTotal);
            }
        }
        else
        {
            // Expansion / underflow: expand proportionally to preferred widths
            if (maxTotal > 0)
            {
                double scale = target / maxTotal;
                for (int c = 0; c < columns; c++)
                    widths[c] = preferred[c] * scale;
            }
            else
            {
                double equal = target / columns;
                for (int c = 0; c < columns; c++)
                    widths[c] = equal;
            }
        }

        double actualTotal = widths.Sum();
        double offset = 0;
        if (!table.IsFloating)
        {
            offset = table.Alignment switch
            {
                TableAlignment.Center => Math.Max(0, (available - actualTotal) / 2),
                TableAlignment.End or TableAlignment.Outside => Math.Max(0, available - actualTotal),
                _ => table.LeftIndent,
            };
        }

        var edges = new double[columns + 1];
        edges[0] = left + offset;
        for (int c = 0; c < columns; c++)
            edges[c + 1] = edges[c] + widths[c];

        return edges;
    }

    /// <summary>
    /// Pass 1 of the auto-fit algorithm: computes content-based minimum and preferred widths for each column.
    /// </summary>
    private (double[] Minimum, double[] Preferred) MeasureColumnExtents(DocumentTable table, RichTextDocument document, int depth)
    {
        int columns = ColumnCount(table);
        var minimum = Enumerable.Repeat(1.0, columns).ToArray();
        var preferred = Enumerable.Repeat(1.0, columns).ToArray();

        // Seed with explicit column width hints if available
        for (int i = 0; i < columns; i++)
        {
            if (i < table.ColumnWidths.Count && table.ColumnWidths[i] > 0)
                preferred[i] = Math.Max(preferred[i], table.ColumnWidths[i]);
        }

        foreach (TableCell cell in table.Rows.SelectMany(row => row.Cells).OrderBy(cell => cell.ColumnSpan))
        {
            if (cell.IsRowSpanContinuation)
                continue;

            double min = 0, max = 0;
            int p = cell.ParagraphIndex;
            while (p < Math.Min(cell.ParagraphEnd, document.Paragraphs.Count))
            {
                if (DocumentTable.StartingAt(cell.Tables, p) is DocumentTable nested)
                {
                    if (depth < 16)
                    {
                        (double[] nestedMinCols, double[] nestedPrefCols) = MeasureColumnExtents(nested, document, depth + 1);
                        double nestedMin = nestedMinCols.Sum();
                        double nestedPref = nestedPrefCols.Sum();
                        if (nested.PreferredWidth is > 0)
                            nestedPref = Math.Max(nestedPref, nested.PreferredWidth.Value);
                        if (nested.TotalWidth > 0)
                            nestedPref = Math.Max(nestedPref, nested.TotalWidth);

                        min = Math.Max(min, nestedMin);
                        max = Math.Max(max, nestedPref);
                    }

                    p = nested.ParagraphEnd;
                    continue;
                }

                RichTextParagraph paragraph = document.Paragraphs[p];
                double line = 0;
                double pMin = 0;
                double pMax = 0;
                foreach (LayoutToken token in Tokenize(paragraph))
                {
                    if (token.IsForcedBreak)
                    {
                        pMax = Math.Max(pMax, line);
                        line = 0;
                    }
                    else
                    {
                        line += token.Width;
                        if (!token.IsWhitespace)
                            pMin = Math.Max(pMin, token.Width);
                    }
                }
                pMax = Math.Max(pMax, line);

                double pIndents = Math.Max(0, paragraph.Style.LeftIndentIn(_settings.IndentStepPoints)) + Math.Max(0, paragraph.Style.RightIndentIn);
                min = Math.Max(min, pMin + pIndents);
                max = Math.Max(max, pMax + pIndents);
                p++;
            }

            TableCellPadding padding = cell.PaddingFor(table);
            double hPadding = padding.Left + padding.Right;
            double hBorders = (cell.Borders.Left.IsVisible ? cell.Borders.Left.Width : 0) +
                              (cell.Borders.Right.IsVisible ? cell.Borders.Right.Width : 0);

            min += hPadding + hBorders;
            max = Math.Max(min, max + hPadding + hBorders);

            int end = Math.Min(columns, cell.ColumnIndex + cell.ColumnSpan);
            int span = end - cell.ColumnIndex;
            if (span <= 0)
                continue;

            double currentMin = 0;
            double currentPref = 0;
            for (int c = cell.ColumnIndex; c < end; c++)
            {
                currentMin += minimum[c];
                currentPref += preferred[c];
            }

            double minDeficit = Math.Max(0, min - currentMin);
            double maxDeficit = Math.Max(0, max - currentPref);

            if (minDeficit > 0)
            {
                if (currentMin > 0)
                {
                    for (int c = cell.ColumnIndex; c < end; c++)
                        minimum[c] += minDeficit * (minimum[c] / currentMin);
                }
                else
                {
                    for (int c = cell.ColumnIndex; c < end; c++)
                        minimum[c] += minDeficit / span;
                }
            }

            if (maxDeficit > 0)
            {
                if (currentPref > 0)
                {
                    for (int c = cell.ColumnIndex; c < end; c++)
                        preferred[c] = Math.Max(minimum[c], preferred[c] + maxDeficit * (preferred[c] / currentPref));
                }
                else
                {
                    for (int c = cell.ColumnIndex; c < end; c++)
                        preferred[c] = Math.Max(minimum[c], preferred[c] + maxDeficit / span);
                }
            }
        }

        for (int c = 0; c < columns; c++)
            preferred[c] = Math.Max(preferred[c], minimum[c]);

        return (minimum, preferred);
    }

    private static double[] ColumnEdges(DocumentTable table, double left, double width)
    {
        if (!table.IsFloating)
        {
            left += table.OffsetIn(width);
            width = table.WidthIn(width);
        }
        int columns = ColumnCount(table);
        var edges = new double[columns + 1];
        double total = table.TotalWidth;
        int unspecified = Enumerable.Range(0, columns).Count(i => i >= table.ColumnWidths.Count || !(table.ColumnWidths[i] > 0));
        double scale = total > 0 && (total > width || (unspecified == 0 &&
            (table.PreferredWidth is > 0 || table.WidthPercent is > 0))) ? width / total : 1.0;
        double shared = unspecified > 0 ? Math.Max(0, width - total * scale) / unspecified : 0;

        double x = left;
        edges[0] = x;
        for (int i = 0; i < columns; i++)
        {
            double column = i < table.ColumnWidths.Count && table.ColumnWidths[i] > 0
                ? table.ColumnWidths[i] * scale
                : table.IsFloating ? width / columns : shared;
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
        double capacity = setup.ContentHeightPoints;
        bool keepPrevious = false;

        int index = Math.Max(0, from);
        while (index < Math.Min(to, document.ParagraphCount))
        {
            DocumentTable? nested = DocumentTable.StartingAt(tables, index);
            DocumentSection? section = DocumentSection.Containing(document.Sections, index);
            if (section?.Hidden == true && (nested is null || nested.ParagraphEnd <= section.ParagraphEnd))
            {
                index = Math.Min(to, section.ParagraphEnd);
                continue;
            }
            if (nested is not null)
            {
                // Keep the nested row's legal cuts. Treating every nested row
                // as indivisible lets a long cell run off the page even when
                // both its row and the outer row permit splitting.
                List<RowBox> nestedRows = ComposeTable(nested, document, setup, left, width);
                double nestedHeight = nestedRows.Sum(row => row.Height);
                if (nested.KeepTogether && nestedHeight <= capacity)
                {
                    double startY = y;
                    foreach (RowBox row in nestedRows)
                    {
                        row.PlaceAt(y, 0, lines, cells, new Dictionary<int, double>());
                        y += row.Height;
                    }
                    pieces.Add(new CutPiece(startY, y));
                }
                else
                {
                    bool keepWithNextRow = false;
                    for (int r = 0; r < nestedRows.Count; r++)
                    {
                        RowBox row = nestedRows[r];
                        var rowPieces = row.NestedPieces().Select(piece => new CutPiece(y + piece.Top, y + piece.Bottom)).ToList();
                        if (keepWithNextRow && pieces.Count > 0 && rowPieces.Count > 0)
                        {
                            pieces[^1] = new CutPiece(pieces[^1].Top, rowPieces[0].Bottom);
                            rowPieces.RemoveAt(0);
                        }
                        pieces.AddRange(rowPieces);
                        row.PlaceAt(y, 0, lines, cells, new Dictionary<int, double>());
                        y += row.Height;
                        keepWithNextRow = r < nested.Rows.Count && nested.Rows[r].KeepWithNext;
                    }
                }

                // A table kept with what follows is held to it as a paragraph is.
                keepPrevious = nested.KeepWithNext;
                index = nested.ParagraphEnd;
                continue;
            }

            RichTextParagraph paragraph = document.Paragraphs[index];
            if (IsWhollyHidden(paragraph) && !document.Shapes.Any(shape => shape.ParagraphIndex == index))
            {
                numbering.Advance(paragraph.Style);
                index++;
                continue;
            }

            ParagraphLines composed = ComposeParagraph(paragraph, numbering.Advance(paragraph.Style), setup, index, left, width);

            y += Math.Max(0, paragraph.Style.SpacingBefore);

            double anchorTop = y;
            double height = composed.Lines.Sum(line => line.Height);
            int count = composed.Lines.Count;
            int orphans = Math.Clamp(paragraph.Style.OrphanLines ?? 1, 1, Math.Max(1, count));
            int widows = Math.Clamp(paragraph.Style.WidowLines ?? 1, 1, Math.Max(1, count));
            if (composed.Lines.Take(orphans).Sum(line => line.Height) > capacity ||
                composed.Lines.TakeLast(widows).Sum(line => line.Height) > capacity ||
                (orphans + widows > count && height > capacity))
                orphans = widows = 1;
            bool keepAll = paragraph.Style.KeepTogether && height <= capacity;
            var paragraphPieces = new List<CutPiece>();
            double cutTop = y;
            for (int lineIndex = 0; lineIndex < count; lineIndex++)
            {
                LayoutLine line = composed.Lines[lineIndex];
                line.Top = y;
                lines.Add(line);
                y += line.Height;
                bool last = lineIndex == count - 1;
                if (last || (!keepAll && lineIndex + 1 >= orphans && count - lineIndex - 1 >= widows))
                {
                    paragraphPieces.Add(new CutPiece(cutTop, y));
                    cutTop = y;
                }
            }
            // A wrapping object anchored inside a cell belongs to that cell's
            // flow. Reserve its height before the next paragraph (often its
            // caption), and keep its anchor with the object at a page cut.
            // Use this document's shapes: text boxes have their own paragraph
            // indices and must not pick up the body's objects by coincidence.
            double objectBottom = document.Shapes.Where(shape => shape.ParagraphIndex == index &&
                    shape.OffsetXFromCell && shape.Wraps && !shape.IsPageAnchored && !shape.OffsetFromPage)
                .Select(shape => anchorTop + shape.OffsetY + BoxHeight(shape)).DefaultIfEmpty(anchorTop).Max();
            if (objectBottom > anchorTop && paragraphPieces.Count > 0)
            {
                int covered = paragraphPieces.FindLastIndex(piece => piece.Top < objectBottom) + 1;
                double bottom = Math.Max(objectBottom, paragraphPieces[covered - 1].Bottom);
                paragraphPieces.RemoveRange(0, covered);
                paragraphPieces.Insert(0, new CutPiece(anchorTop, bottom));
                y = Math.Max(y, objectBottom);
            }
            if (keepPrevious && pieces.Count > 0 && paragraphPieces.Count > 0 &&
                paragraphPieces[0].Bottom - pieces[^1].Top <= capacity)
            {
                pieces[^1] = new CutPiece(pieces[^1].Top, paragraphPieces[0].Bottom);
                paragraphPieces.RemoveAt(0);
            }
            pieces.AddRange(paragraphPieces);
            keepPrevious = paragraph.Style.KeepWithNext;

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
    private IEnumerable<(double Top, double Bottom, double Left, double Right)> GetPageWrappingShapes(int pageNumber, DocumentPageBox setup)
    {
        if (_documentShapes.Count == 0)
            yield break;

        foreach (DocumentShape shape in _documentShapes)
        {
            if (!shape.Wraps)
                continue;

            double anchorTop;
            if (shape.IsPageAnchored)
            {
                if (shape.AnchorPage != pageNumber)
                    continue;
                anchorTop = 0;
            }
            else if (!_paragraphTops.TryGetValue(shape.ParagraphIndex, out anchorTop))
            {
                continue;
            }

            double shapeTop = shape.TopOnPage(anchorTop);
            double shapeLeft = setup.ContentLeftPoints + shape.OffsetX;
            double shapeWidth = BoxWidth(shape);
            double shapeHeight = BoxHeight(shape);
            if (shapeWidth <= 0 || shapeHeight <= 0)
                continue;

            double margin = Math.Max(0, shape.WrapDistance);
            yield return (shapeTop - margin, shapeTop + shapeHeight + margin, shapeLeft - margin, shapeLeft + shapeWidth + margin);
        }
    }

    private readonly record struct FloatBox(double Top, double Bottom, double Left, double Right, ShapeWrap Wrap, bool AllowOverlap = true, int ZOrder = 0, bool BehindText = false);

    /// <summary>The rows of a floating table still to stand on the next page, and where across.</summary>
    private sealed record FloatCarry(List<RowBox> Rows, double Left, double Width, DocumentTable Table, ListNumberer HeaderStart, int RowOffset);

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
    private sealed record CellLines(List<LayoutLine> Lines, List<LayoutCell> Cells, List<CutPiece> Pieces, double Height, TableCell? Cell = null, bool IsSplit = false)
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
        public double MinimumHeight { get; set; }
        public bool ExactHeight { get; init; }

        public List<LayoutLine> Lines { get; } = [];

        public List<LayoutCell> Cells { get; } = [];

        public List<CellBox> Boxes { get; } = [];

        /// <summary>Each cell's own lines, so the row can be cut cell by cell.</summary>
        public List<CellLines> Contents { get; } = [];

        /// <summary>True when the row may break across a page; see <see cref="TableRow.CanSplit"/>.</summary>
        public bool CanSplit { get; init; }

        /// <summary>Common safe cut boundaries when this row is inside another cell.</summary>
        public IReadOnlyList<CutPiece> NestedPieces()
        {
            if (!CanSplit)
                return [new CutPiece(0, Height)];

            var intervals = new List<CutPiece>();
            foreach (CellLines content in Contents)
            {
                double spare = Math.Max(0, Height - content.Height);
                double down = content.IsSplit ? 0 : content.Cell?.VerticalAlignment switch
                {
                    CellVerticalAlignment.Center => spare / 2,
                    CellVerticalAlignment.Bottom => spare,
                    _ => 0,
                };
                intervals.AddRange(content.Pieces.Select(piece => new CutPiece(piece.Top + down, piece.Bottom + down)));
            }
            if (intervals.Count == 0)
                return [new CutPiece(0, Height)];

            // Overlapping atomic pieces in different cells cannot be separated
            // by a common horizontal cut. Touching pieces can. Sweep the sorted
            // intervals rather than testing every boundary against every cell.
            intervals.Sort((a, b) => a.Top.CompareTo(b.Top));
            var merged = new List<CutPiece>();
            foreach (CutPiece piece in intervals)
            {
                if (merged.Count > 0 && (piece.Top < merged[^1].Bottom - 0.001 || merged[^1].Bottom < MinimumHeight))
                    merged[^1] = new CutPiece(merged[^1].Top, Math.Max(merged[^1].Bottom, piece.Bottom));
                else
                    merged.Add(piece);
            }
            merged[0] = merged[0] with { Top = 0 };
            merged[^1] = merged[^1] with { Bottom = Math.Max(Height, merged[^1].Bottom) };
            return merged;
        }

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
        /// its rows or at common legal boundaries inside a splittable row. The first part's boxes
        /// run down to the cut, so a cell whose text ended higher still reaches
        /// the foot of the page, and both parts draw their borders.
        /// </remarks>
        public (RowBox Fitted, RowBox? Remainder)? SplitAt(double room, bool allowStranding = false)
        {
            // A row whose stated floor fits on a fresh page must move there,
            // even when its few text lines would fit in the space left here.
            if (room < MinimumHeight)
                return null;

            var cuts = new double?[Contents.Count];
            bool kept = false;
            bool carried = false;
            bool anyCellStranded = false;
            for (int i = 0; i < Contents.Count; i++)
            {
                CellLines content = Contents[i];
                if (content.Pieces.Count == 0)
                    continue;

                bool cellKept = false;
                foreach (CutPiece piece in content.Pieces)
                {
                    if (piece.Bottom <= room)
                    {
                        cellKept = true;
                        kept = true;
                        continue;
                    }

                    cuts[i] = piece.Top;
                    carried = true;
                    break;
                }

                if (!cellKept)
                    anyCellStranded = true;
            }

            if (!kept)
                return null;
            if (!allowStranding && anyCellStranded)
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
                if (piecesAfter.Count > 0)
                {
                    piecesAfter[0] = piecesAfter[0] with { Top = 0 };
                    piecesAfter[^1] = piecesAfter[^1] with { Bottom = remaining };
                }

                fitted.Lines.AddRange(before);
                fitted.Cells.AddRange(boxesBefore);
                fitted.Contents.Add(new CellLines(before, boxesBefore, [.. content.Pieces.Where(piece => piece.Top < cut)], cut, content.Cell, IsSplit: true));
                rest.Lines.AddRange(after);
                rest.Cells.AddRange(boxesAfter);
                rest.Contents.Add(new CellLines(after, boxesAfter, piecesAfter, remaining, content.Cell, IsSplit: content.IsSplit || cut > 0));
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
            foreach (CellLines content in Contents)
            {
                if (content.IsSplit || content.Cell is not TableCell cell || cell.VerticalAlignment == CellVerticalAlignment.Top)
                    continue;
                double extra = Boxes.FirstOrDefault(box => ReferenceEquals(box.Cell, cell))?.ExtraHeight ?? 0;
                double spare = Math.Max(0, Height + extra - content.Height);
                double down = cell.VerticalAlignment == CellVerticalAlignment.Center ? spare / 2 : spare;
                foreach (LayoutLine line in content.Lines)
                    line.Top += down;
                foreach (LayoutCell nested in content.Cells)
                    nested.Bounds = new BRect(nested.Bounds.Left, nested.Bounds.Top + down, nested.Bounds.Width, nested.Bounds.Height);
            }

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

    private IReadOnlyList<RichTextParagraph> ShapeParagraphs(DocumentShape shape) =>
        _linkedParagraphs.GetValueOrDefault(shape, shape.Paragraphs);

    private void PrepareLinkedFrames(DocumentPageBox setup)
    {
        var named = _documentShapes.Where(shape => shape.FrameName is not null)
            .GroupBy(shape => shape.FrameName!, StringComparer.Ordinal).Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var incoming = _documentShapes.Where(shape => shape.NextFrameName is not null)
            .Select(shape => shape.NextFrameName!).ToHashSet(StringComparer.Ordinal);
        var done = new HashSet<DocumentShape>();
        foreach (DocumentShape root in _documentShapes.Where(shape => shape.NextFrameName is not null)
            .OrderBy(shape => shape.FrameName is not null && incoming.Contains(shape.FrameName)))
        {
            if (done.Contains(root))
                continue;
            var chain = new List<DocumentShape>();
            var seen = new HashSet<DocumentShape>();
            DocumentShape? current = root;
            while (current is not null && seen.Add(current) && !done.Contains(current))
            {
                chain.Add(current);
                current = current.NextFrameName is string next && named.TryGetValue(next, out DocumentShape? target) ? target : null;
            }
            if (current is not null)
                _notes.Add("stopped a cyclic or shared linked text-frame chain after visiting each frame once.");
            foreach (DocumentShape shape in chain)
                done.Add(shape);
            if (chain.Any(shape => shape.Tables.Count > 0))
            {
                _notes.Add("linked text frames containing tables retain their model content, but their table overflow is not routed to following frames.");
                continue;
            }
            var pending = new LinkedList<RichTextParagraph>(chain.SelectMany(shape => shape.Paragraphs));
            var lists = new ListNumberer();
            for (int frame = 0; frame < chain.Count; frame++)
            {
                DocumentShape shape = chain[frame];
                var placed = new List<RichTextParagraph>();
                _linkedParagraphs[shape] = placed;
                _linkedStarts[shape] = lists.Snapshot();
                double available = Math.Max(0, shape.Height - shape.TextPadding.Top - shape.TextPadding.Bottom);
                double width = Math.Max(1, BoxWidth(shape) - shape.TextPadding.Left - shape.TextPadding.Right);
                double Height(RichTextParagraph paragraph, string? marker) => Math.Max(0, paragraph.Style.SpacingBefore) +
                    ComposeParagraph(paragraph, marker, setup, -1, 0, width).Lines.Sum(line => line.Height) + Math.Max(0, paragraph.Style.SpacingAfter);
                while (pending.First is not null)
                {
                    RichTextParagraph paragraph = pending.First.Value;
                    string? marker = lists.Snapshot().Advance(paragraph.Style);
                    double height = Height(paragraph, marker);
                    if (shape.GrowsToText || height <= available + 0.01)
                    {
                        placed.Add(paragraph);
                        pending.RemoveFirst();
                        lists.Advance(paragraph.Style);
                        available -= height;
                        continue;
                    }
                    // Find a source prefix that fits, preserving its styled runs.
                    // Prefer a word boundary; over-wide individual words can split.
                    int low = 1, high = paragraph.Length - 1, best = 0;
                    while (low <= high)
                    {
                        int middle = low + (high - low) / 2;
                        var prefix = paragraph.SplitAt(middle).Head.WithParagraphStyle(paragraph.Style with { SpacingAfter = 0 });
                        if (Height(prefix, marker) <= available + 0.01)
                        {
                            best = middle;
                            low = middle + 1;
                        }
                        else
                            high = middle - 1;
                    }
                    if (best > 0 && best < paragraph.Length && !char.IsWhiteSpace(paragraph.Text[best - 1]) && !char.IsWhiteSpace(paragraph.Text[best]))
                    {
                        int boundary = paragraph.Text.LastIndexOfAny([' ', '\t', '\u2028'], best - 1, best);
                        if (boundary >= 0)
                            best = boundary + 1;
                    }
                    if (best > 0 && char.IsHighSurrogate(paragraph.Text[best - 1]))
                        best--;
                    if (best > 0)
                    {
                        var split = paragraph.SplitAt(best);
                        placed.Add(split.Head.WithParagraphStyle(paragraph.Style with { SpacingAfter = 0 }));
                        pending.RemoveFirst();
                        RichTextParagraph tail = split.Tail.WithParagraphStyle(paragraph.Style with
                            { SpacingBefore = 0, ListKind = ListKind.None, List = null });
                        if (tail.Text.StartsWith('\u2028'))
                            tail = tail.RemoveRange(0, 1);
                        pending.AddFirst(tail);
                        lists.Advance(paragraph.Style);
                    }
                    break;
                }
                if (frame == chain.Count - 1 && pending.Count > 0)
                    placed.AddRange(pending);
            }
        }
    }

    /// <summary>
    /// Measures a shape that grows to hold its text, once, at its own width:
    /// its blocks, paragraph spacing and inner padding, or its stated height
    /// where that is more.
    /// </summary>
    private void NoteGrownHeight(DocumentShape shape, DocumentPageBox setup)
    {
        if (shape.GrowsWidthToText && !_grownWidths.ContainsKey(shape))
        {
            double width = 0;
            foreach (RichTextParagraph paragraph in shape.Paragraphs)
                foreach (LayoutLine line in ComposeParagraph(paragraph, null, setup, -1, 0, 20000).Lines)
                    width = Math.Max(width, line.Pieces.Sum(piece => piece.Width) + paragraph.Style.LeftIndentIn(_settings.IndentStepPoints) + paragraph.Style.RightIndentIn);
            width = Math.Max(width, shape.Tables.Select(table => table.TotalWidth).DefaultIfEmpty(0).Max());
            _grownWidths[shape] = Math.Max(Math.Max(0, shape.Width), Math.Min(20000, width + shape.TextPadding.Left + shape.TextPadding.Right));
        }
        if (!shape.GrowsToText || ShapeParagraphs(shape).Count == 0 || shape.Width <= 0 || _grownHeights.ContainsKey(shape))
            return;

        double height = ComposeShapeBlocks(shape, setup, 0).Height + shape.TextPadding.Top + shape.TextPadding.Bottom;
        _grownHeights[shape] = Math.Max(Math.Max(0, shape.Height), height);
    }

    /// <summary>The height a shape's box is drawn at, grown to its text where it grows.</summary>
    private double BoxHeight(DocumentShape shape) =>
        _grownHeights.TryGetValue(shape, out double grown) ? grown : Math.Max(0, shape.Height);

    private double BoxWidth(DocumentShape shape) => _grownWidths.GetValueOrDefault(shape, Math.Max(0, shape.Width));

    private (List<LayoutLine> Lines, List<LayoutCell> Cells, List<CutPiece> Pieces, double Height) ComposeShapeBlocks(
        DocumentShape shape, DocumentPageBox setup, double left)
    {
        ListNumberer bodyLists = _lists;
        _lists = _linkedStarts.TryGetValue(shape, out ListNumberer? start) ? start.Snapshot() : new ListNumberer();
        try
        {
            RichTextDocument content = RichTextDocument.FromParagraphs(ShapeParagraphs(shape)).WithTables(shape.Tables);
            return ComposeBlocks(content, shape.Tables, 0, content.ParagraphCount, setup, left,
                Math.Max(1, BoxWidth(shape) - shape.TextPadding.Left - shape.TextPadding.Right));
        }
        finally
        {
            _lists = bodyLists;
        }
    }

    private (List<LayoutLine> Lines, List<LayoutCell> Cells) PlaceShapeContent(DocumentShape shape, BRect bounds, DocumentPageBox setup)
    {
        if (ShapeParagraphs(shape).Count == 0)
            return ([], []);
        bounds = new BRect(bounds.Left + shape.TextPadding.Left, bounds.Top + shape.TextPadding.Top,
            Math.Max(0, bounds.Width - shape.TextPadding.Left - shape.TextPadding.Right),
            Math.Max(0, bounds.Height - shape.TextPadding.Top - shape.TextPadding.Bottom));
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return ([], []);
        var content = ComposeShapeBlocks(shape, setup, bounds.Left);
        double spare = Math.Max(0, bounds.Height - content.Height);
        double down = shape.TextVerticalAlignment switch { CellVerticalAlignment.Center => spare / 2, CellVerticalAlignment.Bottom => spare, _ => 0 };
        var lines = new List<LayoutLine>();
        var cells = new List<LayoutCell>();
        foreach (LayoutLine line in content.Lines)
        {
            line.Top += bounds.Top + down;
            if (shape.TextOverflow || (line.Top >= bounds.Top && line.Top + (line.ClipHeight ?? line.Height) <= bounds.Bottom + 0.01))
                lines.Add(line);
        }
        foreach (LayoutCell cell in content.Cells)
        {
            double top = bounds.Top + down + cell.Bounds.Top;
            double bottom = bounds.Top + down + cell.Bounds.Bottom;
            if (!shape.TextOverflow)
            {
                top = Math.Max(bounds.Top, top);
                bottom = Math.Min(bounds.Bottom, bottom);
            }
            if (bottom <= top)
                continue;
            cell.Bounds = new BRect(cell.Bounds.Left, top, cell.Bounds.Width, bottom - top);
            cells.Add(cell);
        }
        return (lines, cells);
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

        // A band of fixed height that its content overruns is cut at its
        // edge, as LibreOffice cuts it, rather than left out: the header from
        // its distance down to the text, the footer from the text down to its
        // distance from the foot.
        bool fixedBand = topDistance is not null ? frame.FixedBands.Header : bottomDistance is not null && frame.FixedBands.Footer;
        double distanceIn = topDistance ?? bottomDistance ?? 0;
        if (fixedBand && double.IsFinite(distanceIn) && distanceIn >= 0 && distanceIn < band && distanceIn + height > band)
        {
            double start = topDistance is not null ? top + distanceIn : top;
            double limit = topDistance is not null ? top + band : top + band - distanceIn;
            var kept = new List<LayoutLine>();
            foreach (LayoutLine line in lines)
            {
                line.Top += start;
                if (line.Top + line.Height <= limit + 0.01)
                    kept.Add(line);
            }

            return kept;
        }

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
    /// True for a paragraph with nothing to show: its mark and all its text are
    /// hidden. LibreOffice takes no room for one (<c>SwTextFrame::IsHiddenNow</c>),
    /// where a paragraph with hidden text and a shown mark is still an empty line.
    /// </summary>
    private static bool IsWhollyHidden(RichTextParagraph paragraph) =>
        paragraph.MarkStyle.Hidden && paragraph.Runs.All(run => run.Style.Hidden);

    /// <summary>
    /// <paramref name="paragraphs"/> with each page field showing its value on
    /// the page numbered <paramref name="pageNumber"/>, the
    /// <paramref name="page"/>th of the document; a page count not known yet
    /// keeps the text the document saved.
    /// </summary>
    /// <remarks>
    /// A number adjusted to another page shows nothing where that page is not
    /// there, as LibreOffice's does: a "continued on page 2" footer is blank on
    /// the last page.
    /// </remarks>
    private IReadOnlyList<RichTextParagraph> WithPageFields(IReadOnlyList<RichTextParagraph> paragraphs, int pageNumber, int page)
    {
        List<RichTextParagraph>? filled = null;
        for (int i = 0; i < paragraphs.Count; i++)
        {
            RichTextParagraph paragraph = paragraphs[i];
            if (!paragraph.Runs.Any(run => run.Style.PageField is not null || run.Style.StyleReference is not null))
                continue;

            // From the last run back, so each replacement leaves the offsets of
            // the runs still to do where they were.
            int end = paragraph.Length;
            for (int r = paragraph.Runs.Count - 1; r >= 0; r--)
            {
                StyleRun run = paragraph.Runs[r];
                int start = end - run.Length;
                string? text = run.Style.StyleReference is StyleReferenceField reference
                    ? StyleReferenceText(reference, page)
                    : run.Style.PageField is PageField field && field.Kind == PageFieldKind.PageNumber && field.Adjust != 0 &&
                        (page + field.Adjust < 1 || (_pageCount is int count && page + field.Adjust > count))
                        ? ""
                        : run.Style.PageField?.Text(pageNumber, _pageCount);
                if (text is not null && text != paragraph.Text.Substring(start, run.Length))
                {
                    paragraph = paragraph.RemoveRange(start, run.Length);
                    if (text.Length > 0)
                        paragraph = paragraph.InsertText(start, text, run.Style);
                }

                end = start;
            }

            filled ??= [.. paragraphs];
            filled[i] = paragraph;
        }

        return filled ?? paragraphs;
    }

    /// <summary>
    /// Notes where page <paramref name="page"/>'s body text starts: the
    /// earliest of its <paramref name="lines"/> in the text, or nothing for a
    /// page with none. Lines in table cells count; a header's, a note's and a
    /// shape's carry no paragraph and do not.
    /// </summary>
    private void NotePageStart(int page, List<LayoutLine> lines)
    {
        (int Paragraph, int Offset)? start = null;
        foreach (LayoutLine line in lines)
        {
            if (line.ParagraphIndex < 0)
                continue;

            int offset = line.Pieces.FirstOrDefault(piece => piece.SourceOffset >= 0)?.SourceOffset ?? 0;
            if (start is not { } earliest || line.ParagraphIndex < earliest.Paragraph ||
                (line.ParagraphIndex == earliest.Paragraph && offset < earliest.Offset))
            {
                start = (line.ParagraphIndex, offset);
            }
        }

        while (_pageStartsFound.Count < page)
            _pageStartsFound.Add(null);
        _pageStartsFound[page - 1] = start;
    }

    /// <summary>
    /// A style reference's text on page <paramref name="page"/>, whose body runs
    /// from where it starts to where the next page with text starts; null on a
    /// first pass, which keeps the text the document saved. A page with no text
    /// of its own searches from the start of the document, as LibreOffice's
    /// search from the header itself does.
    /// </summary>
    private string? StyleReferenceText(StyleReferenceField reference, int page)
    {
        if (_pageStarts is null)
            return null;

        (int Paragraph, int Offset)? start = page - 1 < _pageStarts.Count ? _pageStarts[page - 1] : null;
        if (start is not { } from)
            return reference.Text(_bodyParagraphs, (0, 0), (0, 0));

        (int Paragraph, int Offset) to = (_bodyParagraphs.Count, 0);
        for (int next = page; next < _pageStarts.Count; next++)
        {
            if (_pageStarts[next] is { } found)
            {
                to = found;
                break;
            }
        }

        return reference.Text(_bodyParagraphs, from, to);
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
            if (IsWhollyHidden(paragraph))
                continue;

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

    /// <summary>The notes cited in <paramref name="lines"/>, in the order they are cited.</summary>
    private static IEnumerable<DocumentNote> CitedNotes(IEnumerable<LayoutLine> lines)
    {
        foreach (LayoutLine line in lines)
        {
            foreach (LayoutPiece piece in line.Pieces)
            {
                if (piece.Note is DocumentNote note)
                    yield return note;
            }
        }
    }

    /// <summary>
    /// A footnote's lines, each with its top below the note's own, and how tall
    /// the note is. Composed once: a note is set on one page.
    /// </summary>
    /// <remarks>
    /// The label opens the first paragraph as a list marker does, in the
    /// hanging indent, and the text wraps beneath the text rather than beneath
    /// the label - LibreOffice's footnote paragraph style. The width is the
    /// page's text width whatever columns the citation is in.
    /// </remarks>
    private NoteBlock NoteBlockFor(DocumentNote note, DocumentPageBox setup)
    {
        if (_noteBlocks.TryGetValue(note, out NoteBlock? block))
            return block;

        var lines = new List<(LayoutLine, double)>();
        double y = 0;
        for (int i = 0; i < note.Paragraphs.Count; i++)
        {
            RichTextParagraph paragraph = note.Paragraphs[i];
            if (i > 0)
                y += Math.Max(0, paragraph.Style.SpacingBefore);

            ParagraphLines composed = ComposeNoteParagraph(note, i, setup);
            foreach (LayoutLine line in composed.Lines)
            {
                lines.Add((line, y));
                y += line.Height;
            }

            if (i < note.Paragraphs.Count - 1)
                y += Math.Max(0, paragraph.Style.SpacingAfter);
        }

        block = new NoteBlock(lines, y);
        _noteBlocks.Add(note, block);
        return block;
    }

    /// <summary>
    /// One paragraph of a note, the first opened with the note's label: raised
    /// and followed by a space where the document sets labels as Word does, in
    /// the hanging indent where it sets them as LibreOffice does.
    /// </summary>
    private ParagraphLines ComposeNoteParagraph(DocumentNote note, int index, DocumentPageBox setup)
    {
        RichTextParagraph paragraph = note.Paragraphs[index];
        if (index > 0)
            return ComposeParagraph(paragraph, marker: null, setup, -1);

        if (!_raisedNoteLabels)
            return ComposeParagraph(paragraph, _noteLabels.Label(note), setup, -1);

        InlineStyle style = (paragraph.Length > 0 ? paragraph.StyleAt(0) : paragraph.MarkStyle) with { Note = null, Image = null };
        RichTextParagraph labelled = paragraph
            .InsertText(0, " ", style)
            .InsertText(0, DocumentNote.PlaceholderText, style with { Note = note });
        return ComposeParagraph(labelled, marker: null, setup, -1);
    }

    /// <summary>A note's composed lines, each with its offset from the note's top, and the note's height.</summary>
    private sealed record NoteBlock(List<(LayoutLine Line, double Offset)> Lines, double Height);

    /// <summary>Wraps one paragraph into lines, without deciding which page they land on.</summary>
    private ParagraphLines ComposeParagraph(RichTextParagraph paragraph, string? marker, DocumentPageBox setup, int paragraphIndex,
        double? columnLeftOverride = null, double? columnWidthOverride = null, double? wrapTop = null)
    {
        ParagraphStyle style = paragraph.Style;
        double indent = style.LeftIndentIn(_settings.IndentStepPoints);
        // A shape's text is laid out inside the shape, not inside the page's text
        // column: a centred line in a logo box belongs on the box's midpoint.
        double columnLeft = (columnLeftOverride ?? setup.ContentLeftPoints) + indent;
        double columnWidth = Math.Max(
            1.0,
            (columnWidthOverride ?? setup.ContentWidthPoints) - indent - style.RightIndentIn);

        // An empty line has no run to measure, so it is as tall as the paragraph
        // mark says - the document's default only where the mark says nothing.
        BFontStyle defaultFont = FontFor(paragraph.MarkStyle);
        LayoutPiece? markerPiece = null;
        double hang = 0;

        if (marker is not null)
        {
            InlineStyle markerStyle = (paragraph.Runs.Count > 0 ? paragraph.Runs[0].Style : paragraph.MarkStyle) with { Revision = null };
            markerPiece = MakeTextPiece(marker, markerStyle, decorateLink: false);
            hang = Math.Min(columnWidth * 0.5, markerPiece.Width + _settings.ListMarkerGapPoints);
        }

        double textLeft = columnLeft + hang;
        double textWidth = Math.Max(1.0, columnWidth - hang);
        bool exactList = markerPiece is not null && (style.LeftIndent.HasValue || style.FirstLineIndent.HasValue);
        if (exactList)
        {
            textLeft = columnLeft;
            textWidth = columnWidth;
        }
        double firstIndent = exactList
            ? Math.Max(0, style.FirstLineIndentIn + markerPiece!.Width + _settings.ListMarkerGapPoints)
            : style.FirstLineIndentIn;

        TextBand IndentedBand(TextBand band, int index)
        {
            double offset = index == 0 ? firstIndent : 0;
            return new TextBand(band.Left + offset, Math.Max(1, band.Width - offset));
        }

        List<LayoutToken> tokens = Tokenize(paragraph);
        bool shrinks = _styleDefaults.ShrinksJustifiedLines && style.Alignment == TextAlignment.Justify;
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
                index =>
                {
                    double skip = 0;
                    TextBand row = BandFor(ref lineY, estimate, textWidth, ref skip);
                    skips.Add(skip);
                    lineY += estimate;
                    return IndentedBand(row, index);
                },
                bands, style.TabStops, indent + hang, shrinks, _styleDefaults.TabsOverMargin);
        }
        else
        {
            rows = _lineWrapper.Wrap(tokens, index => IndentedBand(new TextBand(0, textWidth), index), bands,
                style.TabStops, indent + hang, shrinks, _styleDefaults.TabsOverMargin);
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
                markerPiece.X = columnLeft + band.Left + (exactList ? style.FirstLineIndentIn - firstIndent : 0);
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

            // A line that holds a change is marked in the margin, left of the
            // frame it is set in, as LibreOffice marks it; the last line also
            // for a paragraph mark a change inserted or deleted.
            if (_revisions.Shows &&
                (pieces.Any(piece => piece.Revised) ||
                 (i == rows.Count - 1 && paragraph.MarkStyle.Revision is { Kind: not RevisionKind.Format })))
            {
                line.ChangeBarX = columnLeft - indent - ChangeBarDistance;
            }

            // The shading runs from the indent to the column's edge, whatever
            // the line holds and whatever a wrapping shape cut from it.
            if (!style.Background.IsEmpty && style.Background.A > 0)
            {
                line.Background = style.Background;
                line.BackgroundLeft = columnLeft;
                line.BackgroundWidth = columnWidth;
            }

            line.TextAreaLeft = columnLeft - indent;
            line.TextAreaWidth = columnWidthOverride ?? setup.ContentWidthPoints;
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
        // A line that took more text by shrinking its spaces is narrowed to the
        // column the same way, the last line too, which would otherwise overhang.
        double wordSpacing = 0;
        if (style.Alignment == TextAlignment.Justify)
        {
            double slack = textWidth - used;
            int spaces = CountSpaces(pieces, first);
            bool shrunk = slack < 0 && _styleDefaults.ShrinksJustifiedLines;
            if (spaces > 0 && ((slack > 0 && !isLastLine) || shrunk))
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
            int runStart = offset;
            offset += run.Length;
            if (run.Style.Hidden)
                continue;

            // A citation is its note's label, raised and smaller, and part of
            // the word it follows: no line breaks between "text" and its "1".
            if (run.Style.Note is DocumentNote note)
            {
                word ??= LayoutToken.Empty();
                for (int i = 0; i < length; i++)
                {
                    LayoutPiece citation = MakeCitationPiece(note, run.Style);
                    citation.SourceOffset = runStart + i;
                    word.Add(citation);
                }
                continue;
            }

            // Each piece notes where its text starts in the paragraph's.
            int at = runStart;
            foreach ((string fragment, bool whitespace, bool image, bool tab, bool forcedBreak) in Fragments(text, run.Style))
            {
                int fragmentStart = at;
                at += forcedBreak || image || tab ? 1 : fragment.Length;
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
                    piece.SourceOffset = fragmentStart;
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

                    LayoutPiece tabPiece = MakeTabPiece(run.Style);
                    tabPiece.SourceOffset = fragmentStart;
                    tokens.Add(LayoutToken.Single(tabPiece, isWhitespace: true));
                    continue;
                }

                if (whitespace && fragment == SoftHyphenText)
                {
                    if (word is not null)
                    {
                        tokens.Add(word);
                        word = null;
                    }

                    LayoutPiece hold = MakeTextPiece(string.Empty, run.Style, _settings.DecorateLinks);
                    LayoutPiece hyphen = MakeTextPiece("-", run.Style, _settings.DecorateLinks);
                    hold.SourceOffset = fragmentStart;
                    hyphen.SourceOffset = fragmentStart;
                    tokens.Add(LayoutToken.SoftHyphen(hold, hyphen));
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
                    {
                        piece.Width += SpaceRunExtraWidth(piece.Text, run.Style, piece.Font.Size);
                        piece.SourceOffset = fragmentStart;
                        space.Add(piece);
                    }

                    tokens.Add(space);
                    continue;
                }

                word ??= LayoutToken.Empty();
                int pieceStart = fragmentStart;
                foreach (LayoutPiece piece in MakePieces(fragment, run.Style))
                {
                    piece.SourceOffset = pieceStart;
                    pieceStart += piece.Text.Length;
                    word.Add(piece);
                }
            }
        }

        if (word is not null)
            tokens.Add(word);

        return tokens;
    }

    /// <summary>
    /// What the spaces in <paramref name="text"/> that stand in runs of two or
    /// more add to its width, in a document that lays such runs out wider
    /// (<see cref="DocumentStyleDefaults.WidensSpaceRuns"/>). The space is still
    /// drawn as the font's; the gap after it is what grows.
    /// </summary>
    private double SpaceRunExtraWidth(string text, InlineStyle style, double fontSize) =>
        _styleDefaults.SpaceRunExtraWidth(text, _styleDefaults.FontFamilyOf(style), fontSize);

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

            // A soft hyphen stands alone: it draws nothing where its line goes
            // on, and a hyphen where the line breaks at it (LineWrapper).
            if (character == UnicodeBoundaryClassifier.SoftHyphen)
            {
                if (builder.Length > 0)
                {
                    yield return new Fragment(builder.ToString(), whitespace ?? false, false, false, false);
                    builder.Clear();
                }

                whitespace = null;
                yield return new Fragment(SoftHyphenText, Whitespace: true, Image: false, Tab: false, Break: false);
                continue;
            }

            bool isSpace = UnicodeBoundaryClassifier.IsBreakableWhitespace(character);
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

    private static readonly string SoftHyphenText = UnicodeBoundaryClassifier.SoftHyphen.ToString();

    /// <summary>
    /// The drawable pieces for a fragment. Usually one; small capitals produce
    /// two sizes and therefore more than one.
    /// </summary>
    private IEnumerable<LayoutPiece> MakePieces(string text, InlineStyle style)
    {
        // Text set in Symbol is drawn as the characters it stands for, in the
        // document's own face, as LibreOffice draws it where Symbol is replaced.
        if (SymbolFontText.IsSymbolFamily(style.FontFamily))
        {
            text = SymbolFontText.Recode(text);
            style = style with { FontFamily = null };
        }

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
        bool revised = _revisions.Shows && style.Revision is not null;
        style = _revisions.Decorate(style, out bool doubled);
        BFontStyle font = FontFor(style, sizeScale);
        BColor color = LayoutColors.Or(style.Foreground, _settings.DefaultForeground);
        bool underline = style.Underline;

        if (decorateLink && style.IsLink && !_styleDefaults.UndecoratedLinks)
        {
            underline = true;
            if (style.Foreground.IsEmpty)
                color = _settings.LinkColor;
        }

        double width = _settings.TextMeasurer is not null
            ? _settings.TextMeasurer.MeasureAdvance(text, font)
            : BTextMeasurer.MeasureAdvance(text, font);
        var (ascent, descent, lineGap) = VerticalMetrics(font);

        // Shearing is a fallback for a family with no designed italic face. It
        // does not change the advance, so nothing in the layout moves either way.
        bool italicFaceAvailable = _settings.TextMeasurer is not null
            ? _settings.TextMeasurer.HasItalicFace(font.FamilyName)
            : (_settings.ItalicFaceAvailable?.Invoke(font.FamilyName) ?? false);
        bool oblique = style.Italic && _settings.SynthesizeItalic && !italicFaceAvailable;

        return new LayoutPiece(text, font, color, style.Background, underline, style.Strikethrough, style.LinkHref, null,
            width, ascent, descent, oblique: oblique, lineGap: lineGap)
        {
            Revised = revised,
            Doubled = doubled,
        };
    }

    /// <summary>
    /// A note's citation: its label at <see cref="CitationScale"/> of the run's
    /// size, its baseline <see cref="CitationRise"/> of that size above the line's.
    /// </summary>
    private LayoutPiece MakeCitationPiece(DocumentNote note, InlineStyle style)
    {
        InlineStyle plain = style with { Note = null, Image = null };
        LayoutPiece small = MakeTextPiece(_noteLabels.Label(note), plain, _settings.DecorateLinks, CitationScale);
        double rise = FontFor(plain).Size * CitationRise;
        return new LayoutPiece(small.Text, small.Font, small.Color, small.Highlight, small.Underline, small.Strikethrough,
            small.Link, null, small.Width, small.Ascent + rise, Math.Max(0, small.Descent - rise), small.Oblique,
            lineGap: small.LineGap)
        {
            Rise = rise,
            Note = note,
            Revised = small.Revised,
            Doubled = small.Doubled,
        };
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

        if (_settings.TextMeasurer is not null)
        {
            var measured = _settings.TextMeasurer.GetVerticalMetrics(font);
            if (double.IsFinite(measured.Ascent) && double.IsFinite(measured.Descent) &&
                measured.Ascent > 0 && measured.Descent >= 0 && double.IsFinite(measured.Ascent + measured.Descent))
            {
                double mGap = double.IsFinite(measured.LineGap) && measured.LineGap >= 0 ? measured.LineGap : 0;
                return (measured.Ascent, measured.Descent, mGap);
            }
        }
        double ascent = font.Size * 0.8;
        return (ascent, Math.Max(0, BTextMeasurer.GetLineHeight(font) - ascent), 0);
    }

    private LayoutPiece MakeImagePiece(InlineStyle style)
    {
        InlineImage image = style.Image!;
        (double width, double height) = _images.MeasurePoints(image);

        return new LayoutPiece(string.Empty, FontFor(style), LayoutColors.Or(style.Foreground, _settings.DefaultForeground), style.Background, false,
            false, style.LinkHref, image, Math.Max(1, width), Math.Max(1, height), 0)
        {
            Revised = _revisions.Shows && style.Revision is not null,
        };
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
