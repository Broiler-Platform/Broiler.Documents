using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Broiler.Documents.Model;

namespace Broiler.Documents.Docx;

internal sealed class DocxDocumentBuilder : IDocxImageDiagnostics
{
    private readonly DocumentLimits _limits;
    private readonly List<DocumentDiagnostic> _diagnostics;
    private readonly List<RichTextParagraph> _paragraphs = [];
    private readonly List<Segment> _segments = [];
    private readonly HashSet<string> _diagnosticOnce;
    private readonly List<DocumentShape> _shapes = [];
    private readonly List<DocumentTable> _tables = [];
    private readonly Stack<List<DocumentTable>> _tableSinks = new();
    private ParagraphStyle _paragraphStyle = ParagraphStyle.Default;
    private InlineStyle _pageBreakStyle = InlineStyle.Default;
    private bool _pageBreakSeen;
    private bool _pageBreakStartsNextParagraph;
    private int _tableCount;
    private int _unsupportedBlockCount;

    /// <summary>
    /// A document is read by more than one builder - the body and each header
    /// or footer part - so the set of already-reported codes is passed in.
    /// Without it "once" would mean once per part, and a shape in the body and
    /// a shape in the header would report the same gap twice.
    /// </summary>
    public DocxDocumentBuilder(
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics,
        HashSet<string>? reported = null)
    {
        _limits = limits;
        _diagnostics = diagnostics;
        _diagnosticOnce = reported ?? new HashSet<string>(StringComparer.Ordinal);
    }

    public DocumentLimits Limits => _limits;

    /// <summary>Shared with the builders a nested part or shape is read with.</summary>
    public List<DocumentDiagnostic> Diagnostics => _diagnostics;

    /// <summary>The codes already reported, so once means once per document.</summary>
    public HashSet<string> Reported => _diagnosticOnce;

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
            _diagnostics.Add(DocumentDiagnostic.Warning(
                "docx.document.empty",
                "DOCX body contained block-level content but produced no paragraphs."));
        }

        _diagnostics.Add(DocumentDiagnostic.Info(
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
    /// The run's style is kept because the break may turn out not to be the
    /// last thing in its paragraph. Word honours a break with text after it
    /// by splitting the paragraph across the boundary, and a paragraph
    /// carrying one flag cannot say "the first half of me is on the page
    /// before". That case is demoted to the line break every other
    /// <c>w:br</c> becomes, and reported, rather than pulling the text that
    /// followed the break back onto the previous page.
    /// </remarks>
    public void NotePageBreak(InlineStyle style)
    {
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
        _pageBreakStyle = style;
    }

    /// <summary>
    /// Drops a page break that reached the end of a table cell. Word splits
    /// the row a break inside a table falls in; a row is placed whole here,
    /// so there is no boundary inside one to move it to, and the paragraph
    /// that follows in the flat list is a page's worth of layout away from
    /// where the break was written. It is dropped and said out loud rather
    /// than landing somewhere the document never asked for.
    /// </summary>
    public void DiscardPageBreakAtCellEnd()
    {
        if (!_pageBreakSeen && !_pageBreakStartsNextParagraph)
            return;

        _pageBreakSeen = false;
        _pageBreakStartsNextParagraph = false;
        AddDiagnosticOnce(
            "docx.pagebreak.table",
            "A page break at the end of a table cell was dropped; a row is placed whole, so it has no page boundary inside it.");
    }

    public void StartParagraph(ParagraphStyle style)
    {
        _segments.Clear();

        // The two spellings mean one break. A paragraph reached by a
        // w:br that also states w:pageBreakBefore starts one page, not two,
        // so this sets the flag rather than counting anything.
        if (_pageBreakStartsNextParagraph)
        {
            style = style with { PageBreakBefore = true };
            _pageBreakStartsNextParagraph = false;
        }

        _paragraphStyle = style;
    }

    public void AppendText(string text, InlineStyle style)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // Text after a page break run is the case NotePageBreak cannot
        // carry: the break is inside the paragraph rather than at the end
        // of it. Demoting it here, on the first content that follows,
        // keeps the reader a single forward pass.
        if (_pageBreakSeen)
            DemotePageBreakToLineBreak();

        if (text.Length > _limits.MaxRunLength)
        {
            text = text[.._limits.MaxRunLength];
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
    /// Turns a page break that content followed into a line break, because
    /// the paragraph it is in cannot be split. The flag is cleared first:
    /// the line break goes in through <see cref="AppendText"/>, which is
    /// where the demotion is triggered from.
    /// </summary>
    private void DemotePageBreakToLineBreak()
    {
        InlineStyle style = _pageBreakStyle;
        _pageBreakSeen = false;
        AddDiagnosticOnce(
            "docx.pagebreak.split",
            "A page break with text after it in the same paragraph was read as a line break; pages here break between paragraphs, not inside one.");
        AppendText(((char)0x2028).ToString(), style);
    }

    public void FinishParagraph()
    {
        // A page break that reached the end of its paragraph is the break
        // the next paragraph starts with, whatever that paragraph's own
        // w:pPr says.
        if (_pageBreakSeen)
        {
            _pageBreakSeen = false;
            _pageBreakStartsNextParagraph = true;
        }

        if (_paragraphs.Count >= _limits.MaxParagraphCount)
        {
            AddDiagnosticOnce("docx.limit.paragraphs", "DOCX input exceeded MaxParagraphCount; remaining paragraphs were dropped.");
            _segments.Clear();
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
        _paragraphStyle = ParagraphStyle.Default;
    }

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
            _diagnostics.Add(DocumentDiagnostic.Warning(code, message));
    }

    private readonly record struct Segment(string Text, InlineStyle Style);
}
