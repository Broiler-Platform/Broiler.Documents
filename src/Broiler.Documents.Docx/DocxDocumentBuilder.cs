using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Broiler.Documents.Model;

namespace Broiler.Documents.Docx;

/// <summary>
/// A document is read by more than one builder - the body and each header
/// or footer part - so the set of already-reported codes is passed in.
/// Without it "once" would mean once per part, and a shape in the body and
/// a shape in the header would report the same gap twice.
/// </summary>
internal sealed class DocxDocumentBuilder(
    DocumentLimits limits,
    List<DocumentDiagnostic> diagnostics,
    HashSet<string>? reported = null) : IDocxImageDiagnostics
{
    private readonly List<RichTextParagraph> _paragraphs = [];
    private readonly List<Segment> _segments = [];
    private readonly HashSet<string> _diagnosticOnce = reported ?? new HashSet<string>(StringComparer.Ordinal);
    private readonly List<DocumentShape> _shapes = [];
    private readonly List<DocumentTable> _tables = [];
    private readonly Stack<List<DocumentTable>> _tableSinks = new();
    private ParagraphStyle _paragraphStyle = ParagraphStyle.Default;
    private InlineStyle _markStyle = InlineStyle.Default;
    private bool _pageBreakSeen;
    private bool _cellSplitPending;
    private bool _pageBreakLeadsParagraph;
    private bool _pageBreakStartsNextParagraph;

    // Whether the break being carried is a column break rather than a page
    // break. Both travel the same way, so one set of flags carries either.
    private bool _breakIsColumn;
    private bool _breakKeepsLeadingLabel;
    private bool _heldBreakIsColumn;
    private int _tableCount;
    private int _unsupportedBlockCount;

    public DocumentLimits Limits => limits;

    /// <summary>The complex fields open in the flow this builds.</summary>
    public DocxFields Fields { get; } = new();

    /// <summary>Shared with the builders a nested part or shape is read with.</summary>
    public List<DocumentDiagnostic> Diagnostics => diagnostics;

    /// <summary>The codes already reported, so once means once per document.</summary>
    public HashSet<string> Reported => _diagnosticOnce;

    /// <summary>
    /// Whether a paragraph in a table cell loses the spaces, tabs and no-break
    /// spaces it ends with, as LibreOffice reads a document of Word 2010 or
    /// earlier (<c>compatibilityMode</c> 11 to 14). See
    /// <see cref="TrimCellParagraphEnd"/>.
    /// </summary>
    public bool TrimsCellParagraphEnds => WordCompatibilityMode is > 0 and <= 14;

    /// <summary>
    /// The Word version whose layout the document asks for, its
    /// <c>compatibilityMode</c>, or -1 where it states none. A builder for a
    /// nested flow takes it from the one it is nested in.
    /// </summary>
    public int WordCompatibilityMode { get; init; } = -1;

    /// <summary>Counts a table for the read summary.</summary>
    public void NoteTable() => _tableCount++;

    /// <summary>
    /// Records a table. It lands in the cell being read, when one is - which
    /// is what makes a table inside a cell the cell's rather than the body's,
    /// without the block walk having to know where it is.
    /// </summary>
    public void AddTable(DocumentTable table)
    {
        if (table.ParagraphCount <= 0 || table.Rows.Count == 0)
            return;

        (_tableSinks.Count > 0 ? _tableSinks.Peek() : _tables).Add(table);
    }

    /// <summary>Collects the tables read from here until the matching pop.</summary>
    public void PushTableSink(List<DocumentTable> sink) => _tableSinks.Push(sink);

    public bool InTableCell => _tableSinks.Count > 0;

    public void PopTableSink()
    {
        if (_tableSinks.Count > 0)
            _tableSinks.Pop();
    }

    /// <summary>
    /// Records a block-level element the reader does not understand. Keyed by
    /// element name so each distinct construct is reported once — the name is
    /// markup structure, never document text (ADR 0004 privacy rule).
    /// </summary>
    public void AddUnsupportedBlock(XName name)
    {
        _unsupportedBlockCount++;
        AddDiagnosticOnce(
            "docx.block.unsupported:" + name.LocalName,
            "docx.block.unsupported",
            "An unsupported DOCX block-level element was skipped: " + name.LocalName + ".");
    }

    /// <summary>
    /// Emits the read summary. The counts make a silent content loss visible:
    /// a body with block content that yields no paragraphs is a reader bug,
    /// not an empty file, and it should say so rather than open blank.
    /// </summary>
    public void ReportReadSummary(bool bodyHadContentBlocks, int styleCount, int imageCount)
    {
        if (_paragraphs.Count == 0 && bodyHadContentBlocks)
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                "docx.document.empty",
                "DOCX body contained block-level content but produced no paragraphs."));
        }

        diagnostics.Add(DocumentDiagnostic.Info(
            "docx.read.summary",
            "DOCX read produced " + _paragraphs.Count.ToString(CultureInfo.InvariantCulture) +
            " paragraph(s), read " + _tableCount.ToString(CultureInfo.InvariantCulture) +
            " table(s), loaded " + styleCount.ToString(CultureInfo.InvariantCulture) +
            " style(s), embedded " + imageCount.ToString(CultureInfo.InvariantCulture) +
            " image(s), and skipped " + _unsupportedBlockCount.ToString(CultureInfo.InvariantCulture) +
            " unsupported block(s)."));
    }

    /// <summary>
    /// Holds the page break a <c>w:br w:type="page"</c> states until the
    /// paragraph it starts. The run form sits at the end of the paragraph
    /// before the break - it is what Word writes when a user presses
    /// Ctrl+Enter - so nothing is appended here and nothing is decided
    /// until the paragraph ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The run's style is kept because the break may turn out not to be the
    /// last thing in its paragraph. Word honours a break with text after it
    /// by splitting the paragraph across the boundary, and a paragraph
    /// carrying one flag cannot say "the first half of me is on the page
    /// before". Body paragraphs are therefore split into fragments at that
    /// boundary.
    /// </para>
    /// <para>
    /// A page break inside a table cell is dropped, leaving nothing in its
    /// place - no line break and no space - as LibreOffice 24.2's DOCX import
    /// drops it (<c>DomainMapper_Impl::deferBreak</c>, after the binary Word
    /// filter's <c>HandlePageBreakChar</c>). Word splits the row there; a row
    /// breaks here only where the page runs out, and a line break in its place
    /// made every such cell taller than either application draws it. A column
    /// break there splits its paragraph where text follows text, as
    /// LibreOffice 24.2 splits one (<c>DomainMapper</c>'s <c>m_bIsSplitPara</c>),
    /// and starts nothing: Writer does not break a column inside a cell.
    /// </para>
    /// <para>
    /// A leading break moves the paragraph to the far side of the boundary.
    /// A leading column break in a list is different: the label before the
    /// break is a numbered fragment of its own, as in the native import.
    /// </para>
    /// <para>
    /// A column break in a section of several columns travels exactly the
    /// same way and lands as <see cref="ParagraphStyle.ColumnBreakBefore"/>;
    /// <paramref name="column"/> says which this is.
    /// </para>
    /// </remarks>
    public void NotePageBreak(bool column = false, bool sourceColumn = false)
    {
        if (InTableCell)
        {
            _cellSplitPending |= sourceColumn && _segments.Count > 0;
            AddDiagnosticOnce(
                "docx.pagebreak.table",
                "A page or column break inside a table cell started no page or column; a row breaks only where the page runs out, not where a paragraph in it asks.");
            return;
        }

        // Two breaks with nothing between them are two page boundaries and
        // a blank page in the middle. The flag holds one, and the one kept
        // is the first: honouring the later one instead would move the
        // paragraph a page further on than the document put it.
        if (_pageBreakSeen)
        {
            AddDiagnosticOnce(
                "docx.pagebreak.repeated",
                "A paragraph stated more than one page break; the extra breaks, and the blank pages they make, were dropped.");
            return;
        }

        _pageBreakSeen = true;
        _pageBreakLeadsParagraph = _segments.Count == 0;
        _breakIsColumn = column;
        _breakKeepsLeadingLabel = sourceColumn;
    }

    /// <summary>
    /// Starts the next paragraph on a new page: a section that begins on a page
    /// of its own, whose break is stated in no run at all. A break the paragraph
    /// just ended with already does that, and one boundary is one boundary; a
    /// column break held over becomes the page break, which goes further.
    /// </summary>
    public void HoldPageBreak()
    {
        _pageBreakStartsNextParagraph = true;
        _heldBreakIsColumn = false;
    }

    /// <summary>Transfers a break between blocks onto a following table, before
    /// its first cell paragraph can consume it.</summary>
    public (bool Page, bool Column) TakeBlockBreak()
    {
        bool held = _pageBreakStartsNextParagraph;
        bool column = _heldBreakIsColumn;
        _pageBreakStartsNextParagraph = false;
        _heldBreakIsColumn = false;
        return (held && !column, held && column);
    }

    /// <summary>
    /// Drops a page break still held at the end of a table cell - a section
    /// that starts a page, ended inside the cell. A run's break there never
    /// gets this far (<see cref="NotePageBreak"/>). The paragraph that follows
    /// in the flat list is the neighbouring cell's or the body's past the
    /// table, so the break is dropped and said out loud rather than landing
    /// somewhere the document never asked for.
    /// </summary>
    public void DiscardPageBreakAtCellEnd()
    {
        if (!_pageBreakSeen && !_pageBreakStartsNextParagraph)
            return;

        _pageBreakSeen = false;
        _pageBreakStartsNextParagraph = false;
        AddDiagnosticOnce(
            "docx.pagebreak.table",
            "A page or column break inside a table cell started no page or column; a row breaks only where the page runs out, not where a paragraph in it asks.");
    }

    public void StartParagraph(ParagraphStyle style, InlineStyle markStyle = default)
    {
        _segments.Clear();
        _markStyle = markStyle;

        // The two spellings mean one break. A paragraph reached by a
        // w:br that also states w:pageBreakBefore starts one page, not two,
        // so this sets the flag rather than counting anything.
        if (_pageBreakStartsNextParagraph)
        {
            style = _heldBreakIsColumn
                ? style with { ColumnBreakBefore = true }
                : style with { PageBreakBefore = true };
            _pageBreakStartsNextParagraph = false;
            _heldBreakIsColumn = false;
        }

        _paragraphStyle = style;
    }

    /// <summary>Reserves the line occupied by a diagram that is the paragraph's only content.</summary>
    public double ReserveDiagramBlock(double width, double height, PageGeometry? page)
    {
        double left = _paragraphStyle.LeftIndentIn(), right = _paragraphStyle.RightIndentIn;
        double available = page is null ? width : Math.Max(0, page.Width - page.MarginLeft - page.MarginRight - left - right);
        double offset = left + (_paragraphStyle.Alignment switch
        {
            TextAlignment.Center => (available - width) / 2,
            TextAlignment.Right => available - width,
            _ => 0,
        });
        if (_paragraphStyle.LineHeightRule != LineHeightRule.Exact || _paragraphStyle.LineHeight <= 0)
            _paragraphStyle = _paragraphStyle with { LineHeightRule = LineHeightRule.Exact, LineHeight = (float)Math.Max(height, _paragraphStyle.LineHeight) };
        return offset;
    }

    public void AppendText(string text, InlineStyle style)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (_cellSplitPending)
        {
            _cellSplitPending = false;
            SplitAtPageBreak(breaks: false);
        }

        // Text after a page break run is the case NotePageBreak cannot
        // carry: the break is inside the paragraph rather than at the end
        // of it. Deciding here, on the first content that follows, keeps the
        // reader a single forward pass.
        if (_pageBreakSeen)
        {
            if (_pageBreakLeadsParagraph && _breakKeepsLeadingLabel &&
                _paragraphStyle.ListKind != ListKind.None)
                SplitAtPageBreak(keepList: true);
            else if (_pageBreakLeadsParagraph)
                StartParagraphOnNewPage();
            else
                SplitAtPageBreak();
        }

        if (text.Length > limits.MaxRunLength)
        {
            text = text[..limits.MaxRunLength];
            AddDiagnosticOnce("docx.limit.run", "A DOCX text run exceeded MaxRunLength and was truncated.");
        }

        if (_segments.Count > 0 && _segments[^1].Style.Equals(style))
        {
            Segment previous = _segments[^1];
            _segments[^1] = new Segment(previous.Text + text, style);
            return;
        }

        _segments.Add(new Segment(text, style));
    }

    /// <summary>
    /// Turns a page break that opened its paragraph into the paragraph's own
    /// break: nothing of the paragraph was before it, so nothing is left on
    /// the page before, and the whole paragraph starts the next one. A
    /// paragraph that already starts a page - a break held over from the one
    /// before, or its style's <c>w:pageBreakBefore</c> - keeps the one page
    /// boundary and reports the blank page the second would make.
    /// </summary>
    private void StartParagraphOnNewPage()
    {
        _pageBreakSeen = false;
        if (_paragraphStyle.PageBreakBefore || (_breakIsColumn && _paragraphStyle.ColumnBreakBefore))
        {
            AddDiagnosticOnce(
                "docx.pagebreak.repeated",
                "A paragraph stated more than one page break; the extra breaks, and the blank pages they make, were dropped.");
            return;
        }

        _paragraphStyle = _breakIsColumn
            ? _paragraphStyle with { ColumnBreakBefore = true }
            : _paragraphStyle with { PageBreakBefore = true };
    }

    /// <summary>Preserves an inline boundary as two paragraph fragments.
    /// Ordinary continuations have no repeated label or first-line indent;
    /// keepList retains the item after a leading column break. A column
    /// break in a table cell splits without <paramref name="breaks"/>.</summary>
    private void SplitAtPageBreak(bool keepList = false, bool breaks = true)
    {
        bool column = _breakIsColumn;
        _pageBreakSeen = false;
        ParagraphStyle original = _paragraphStyle;
        InlineStyle mark = _markStyle;
        _paragraphStyle = original with { SpacingAfter = 0, KeepWithNext = false };
        FinishParagraph();
        StartParagraph(original with
        {
            PageBreakBefore = breaks && !column,
            ColumnBreakBefore = breaks && column,
            SpacingBefore = 0,
            FirstLineIndent = keepList ? original.FirstLineIndent : 0,
            ListKind = keepList ? original.ListKind : ListKind.None,
            List = keepList ? original.List : null,
        }, mark);
        AddDiagnosticOnce("docx.pagebreak.fragment",
            "An inline page or column break was preserved as paragraph fragments; the source paragraph structure is approximated.");
    }

    public void FinishParagraph()
    {
        // A column break that ends a cell's paragraph splits nothing.
        _cellSplitPending = false;

        // A page break that reached the end of its paragraph is the break
        // the next paragraph starts with, whatever that paragraph's own
        // w:pPr says.
        if (_pageBreakSeen)
        {
            _pageBreakSeen = false;
            _pageBreakStartsNextParagraph = true;
            _heldBreakIsColumn = _breakIsColumn;
        }

        if (_paragraphs.Count >= limits.MaxParagraphCount)
        {
            AddDiagnosticOnce("docx.limit.paragraphs", "DOCX input exceeded MaxParagraphCount; remaining paragraphs were dropped.");
            _segments.Clear();
            return;
        }

        if (TrimsCellParagraphEnds && InTableCell)
            TrimCellParagraphEnd();

        RichTextParagraph paragraph = RichTextParagraph.Empty
            .WithParagraphStyle(_paragraphStyle)
            .WithMarkStyle(_markStyle);
        int offset = 0;
        foreach (Segment segment in _segments)
        {
            paragraph = paragraph.InsertText(offset, segment.Text, segment.Style);
            offset += segment.Text.Length;
        }

        _paragraphs.Add(paragraph);
        _segments.Clear();
        _paragraphStyle = ParagraphStyle.Default;
        _markStyle = InlineStyle.Default;
    }

    /// <summary>
    /// Drops the spaces, tabs and no-break spaces at the end of the paragraph
    /// being finished. LibreOffice does this to every paragraph in a table
    /// cell of a document stating Word 2010's <c>compatibilityMode</c> or an
    /// earlier one (tdf#77417, writerfilter's
    /// <c>DomainMapper_Impl::finishParagraph</c>), because Word ignores white
    /// space ending a cell's paragraph there. Left in, a cell ending in a tab
    /// wraps it onto a line of its own: <c>tdf77417.docx</c>'s tables of
    /// amounts, each ending in a tab and a few no-break spaces, ran to three
    /// pages instead of one. The search stops at anything else, such as an
    /// image's or a note's object replacement character.
    /// </summary>
    private void TrimCellParagraphEnd()
    {
        bool trimmed = false;
        while (_segments.Count > 0)
        {
            Segment last = _segments[^1];
            string kept = last.Text.TrimEnd(CellEndWhiteSpace);
            if (kept.Length == last.Text.Length)
                break;

            trimmed = true;
            if (kept.Length > 0)
            {
                _segments[^1] = last with { Text = kept };
                break;
            }

            _segments.RemoveAt(_segments.Count - 1);
        }

        if (trimmed && _diagnosticOnce.Add("docx.table.trailingspace"))
        {
            diagnostics.Add(DocumentDiagnostic.Info(
                "docx.table.trailingspace",
                "Spaces, tabs or no-break spaces ending a paragraph in a table cell were dropped, " +
                "as Word 2010 and earlier ignore them and LibreOffice removes them."));
        }
    }

    private static readonly char[] CellEndWhiteSpace = [' ', '\t', (char)0x00A0];

    /// <summary>The paragraph a shape met right now would be anchored to.</summary>
    public int CurrentParagraphIndex => _paragraphs.Count;

    public void AddShape(DocumentShape shape) => _shapes.Add(shape);

    public RichTextDocument Build()
    {
        // A break stated in the last paragraph of a part has no paragraph
        // to start. Word ends such a document on a blank page, and nothing
        // in this model says "and then a page with nothing on it", so the
        // break is dropped - out loud, because the page count is what the
        // reader just changed.
        if (_pageBreakStartsNextParagraph)
        {
            _pageBreakStartsNextParagraph = false;
            AddDiagnosticOnce(
                "docx.pagebreak.trailing",
                "A page break after the last paragraph was dropped; there is no following paragraph for it to start.");
        }

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
            diagnostics.Add(DocumentDiagnostic.Warning(code, message));
    }

    private readonly record struct Segment(string Text, InlineStyle Style);
}
