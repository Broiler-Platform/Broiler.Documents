using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Broiler.Documents.Model;

namespace Broiler.Documents.Odt;

internal sealed class OdtDocumentBuilder : IOdtImageDiagnostics
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
    private InlineStyle _pendingSpaceStyle = InlineStyle.Default;
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
        _pendingSpaceStyle = InlineStyle.Default;
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

                // The style is recorded with the flag, not read off whatever
                // arrives next. The space belongs to the text node it was
                // written in, and the next thing to arrive is often a
                // <text:span> - so taking the style from there moved the run
                // boundary and swallowed the space into the span.
                _pendingSpace = true;
                _pendingSpaceStyle = style;
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
        //
        // It is flushed with the style it was written with, which is not
        // always the style of the text it precedes: in
        // `<text:span>Bold</text:span> and <text:span>italic</text:span>`
        // the spaces around "and" are the paragraph's, and only the word is
        // the span's.
        if (_pendingSpace)
        {
            _pendingSpace = false;
            if (_length > 0)
                AppendCore(" ", _pendingSpaceStyle);
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
