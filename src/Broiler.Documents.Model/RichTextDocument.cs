using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;

namespace Broiler.Documents.Model;

/// <summary>
/// An immutable rich-text document: an ordered, non-empty list of paragraphs.
/// Every edit method returns a new document; unchanged paragraphs are shared with
/// the previous snapshot (copy-on-write), so snapshots are cheap to keep for
/// undo/redo. Positions produced by one document are only valid against it.
/// </summary>
public sealed class RichTextDocument
{
    private readonly RichTextParagraph[] _paragraphs;

    private RichTextDocument(
        IReadOnlyList<RichTextParagraph> paragraphs,
        RunningContent? runningContent = null,
        IReadOnlyList<DocumentShape>? shapes = null,
        PageGeometry? pageGeometry = null,
        IReadOnlyList<DocumentTable>? tables = null,
        DocumentStyleDefaults? styleDefaults = null,
        IReadOnlyList<DocumentSection>? sections = null,
        ShapeFill? pageBackground = null,
        NoteNumbering? noteNumbering = null,
        bool showsRevisions = true)
    {
        ShowsRevisions = showsRevisions;
        RunningContent = runningContent ?? RunningContent.Empty;
        NoteNumbering = noteNumbering ?? NoteNumbering.Default;
        PageGeometry = pageGeometry;
        PageBackground = pageBackground;
        StyleDefaults = styleDefaults ?? DocumentStyleDefaults.Default;
        Shapes = shapes is null || shapes.Count == 0 ? [] : [.. shapes];
        Tables = tables is null || tables.Count == 0 ? [] : [.. tables];
        Sections = sections is null || sections.Count == 0 ? [] : [.. sections];
        if (paragraphs is null || paragraphs.Count == 0)
        {
            _paragraphs = [RichTextParagraph.Empty];
            return;
        }

        var array = new RichTextParagraph[paragraphs.Count];
        for (int i = 0; i < paragraphs.Count; i++)
            array[i] = paragraphs[i] ?? RichTextParagraph.Empty;
        _paragraphs = array;
    }

    /// <summary>A document with a single empty paragraph.</summary>
    public static RichTextDocument Empty { get; } = new([RichTextParagraph.Empty]);

    public IReadOnlyList<RichTextParagraph> Paragraphs => _paragraphs;

    /// <summary>
    /// The headers and footers that repeat on the page rather than flowing with
    /// the body. Every edit carries it through unchanged: editing the body is not
    /// a reason to lose a letterhead.
    /// </summary>
    public RunningContent RunningContent { get; }

    /// <summary>
    /// The floating shapes anchored beside the body: a letterhead's stripe, a
    /// logo box. They are not in the paragraph flow, so reading or editing the
    /// text never moves through them.
    /// </summary>
    public IReadOnlyList<DocumentShape> Shapes { get; }

    /// <summary>
    /// The page this document says it is written for, or null when it says
    /// nothing. A renderer that has one can place the page the author chose
    /// rather than one of its own.
    /// </summary>
    public PageGeometry? PageGeometry { get; }

    /// <summary>
    /// What this document's formatting falls back to where a run states nothing.
    /// Never null: a document that named no default still has the shared one, so
    /// no consumer has to invent a size of its own.
    /// </summary>
    public DocumentStyleDefaults StyleDefaults { get; }

    /// <summary>
    /// The tables the body's paragraphs are arranged in, in the order they start.
    /// A table names the paragraphs of its cells rather than holding them, so the
    /// paragraph list still reads a table's text in row-major order whether or
    /// not anything looks at the grid. A table inside a cell is held by that cell
    /// (<see cref="TableCell.Tables"/>) and is not listed here.
    /// </summary>
    public IReadOnlyList<DocumentTable> Tables { get; }

    /// <summary>
    /// The stretches of the body laid out in text columns of their own, in the
    /// order they start; empty when the whole body takes the page's columns.
    /// See <see cref="DocumentSection"/>.
    /// </summary>
    public IReadOnlyList<DocumentSection> Sections { get; }

    /// <summary>
    /// What every page is painted with under its content, or null for a page
    /// left as the paper is. One for the whole document, because that is what
    /// DOCX and RTF state and what the page a viewer draws behind HTML is.
    /// </summary>
    public ShapeFill? PageBackground { get; }

    /// <summary>
    /// How the notes this body cites are numbered. Never null: a document that
    /// states nothing numbers footnotes 1, 2, 3 and endnotes i, ii, iii.
    /// </summary>
    public NoteNumbering NoteNumbering { get; }

    /// <summary>This document's body with different running content.</summary>
    public RichTextDocument WithRunningContent(RunningContent? runningContent) =>
        new(_paragraphs, runningContent, Shapes, PageGeometry, Tables, StyleDefaults, Sections, PageBackground, NoteNumbering, ShowsRevisions);

    /// <summary>This document's body with different floating shapes.</summary>
    public RichTextDocument WithShapes(IReadOnlyList<DocumentShape>? shapes) =>
        new(_paragraphs, RunningContent, shapes, PageGeometry, Tables, StyleDefaults, Sections, PageBackground, NoteNumbering, ShowsRevisions);

    /// <summary>This document's body on a different page.</summary>
    public RichTextDocument WithPageGeometry(PageGeometry? pageGeometry) =>
        new(_paragraphs, RunningContent, Shapes, pageGeometry, Tables, StyleDefaults, Sections, PageBackground, NoteNumbering, ShowsRevisions);

    /// <summary>This document's body arranged in different tables.</summary>
    public RichTextDocument WithTables(IReadOnlyList<DocumentTable>? tables) =>
        new(_paragraphs, RunningContent, Shapes, PageGeometry, tables, StyleDefaults, Sections, PageBackground, NoteNumbering, ShowsRevisions);

    /// <summary>This document's body divided into different sections.</summary>
    public RichTextDocument WithSections(IReadOnlyList<DocumentSection>? sections) =>
        new(_paragraphs, RunningContent, Shapes, PageGeometry, Tables, StyleDefaults, sections, PageBackground, NoteNumbering, ShowsRevisions);

    /// <summary>This document's body painted on a different page background.</summary>
    public RichTextDocument WithPageBackground(ShapeFill? pageBackground) =>
        new(_paragraphs, RunningContent, Shapes, PageGeometry, Tables, StyleDefaults, Sections, pageBackground, NoteNumbering, ShowsRevisions);

    /// <summary>
    /// True when the document's tracked changes are shown as changes - deleted
    /// text struck through, inserted text underlined - and false when it says
    /// they are hidden, which draws it as it reads with its deletions gone. It
    /// says nothing about whether the document has any; see
    /// <see cref="TextRevision"/> and <see cref="RevisionDisplay"/>.
    /// </summary>
    public bool ShowsRevisions { get; }

    /// <summary>This document's body with its tracked changes shown or hidden.</summary>
    public RichTextDocument WithShowsRevisions(bool showsRevisions) =>
        new(_paragraphs, RunningContent, Shapes, PageGeometry, Tables, StyleDefaults, Sections, PageBackground, NoteNumbering, showsRevisions);

    /// <summary>This document's body with its notes numbered differently.</summary>
    public RichTextDocument WithNoteNumbering(NoteNumbering? noteNumbering) =>
        new(_paragraphs, RunningContent, Shapes, PageGeometry, Tables, StyleDefaults, Sections, PageBackground, noteNumbering, ShowsRevisions);

    /// <summary>The notes the body cites, in the order it cites them.</summary>
    public IReadOnlyList<DocumentNote> Notes => DocumentNote.CitedIn(_paragraphs);

    /// <summary>This document's body with different formatting fallbacks.</summary>
    public RichTextDocument WithStyleDefaults(DocumentStyleDefaults? styleDefaults) =>
        new(_paragraphs, RunningContent, Shapes, PageGeometry, Tables, styleDefaults, Sections, PageBackground, NoteNumbering, ShowsRevisions);

    /// <summary>
    /// The body table starting at <paramref name="paragraphIndex"/>, or null when
    /// none does. A renderer walking the body asks at each paragraph: the answer
    /// says whether to lay out a paragraph or a grid. Inside a cell it asks the
    /// same of <see cref="TableCell.Tables"/>, through
    /// <see cref="DocumentTable.StartingAt"/>.
    /// </summary>
    public DocumentTable? TableStartingAt(int paragraphIndex) =>
        DocumentTable.StartingAt(Tables, paragraphIndex);

    public int ParagraphCount => _paragraphs.Length;

    public static RichTextPosition Start => new(0, 0);

    public RichTextPosition End => new(_paragraphs.Length - 1, _paragraphs[^1].Length);

    /// <summary>Joins paragraph text with newlines.</summary>
    public string PlainText
    {
        get
        {
            var builder = new StringBuilder();
            for (int i = 0; i < _paragraphs.Length; i++)
            {
                if (i > 0)
                    builder.Append('\n');
                builder.Append(_paragraphs[i].Text);
            }

            return builder.ToString();
        }
    }

    /// <summary>Builds a document from plain text, splitting paragraphs on newlines.</summary>
    public static RichTextDocument FromPlainText(string? text)
    {
        text = NormalizeNewlines(text ?? string.Empty);
        string[] lines = text.Split('\n');
        var paragraphs = new RichTextParagraph[lines.Length];
        for (int i = 0; i < lines.Length; i++)
            paragraphs[i] = RichTextParagraph.Plain(lines[i]);
        return new RichTextDocument(paragraphs);
    }

    /// <summary>
    /// Builds a document from pre-formatted paragraphs (for example, from a
    /// document-format reader). A null or empty sequence yields <see cref="Empty"/>;
    /// null entries become empty paragraphs.
    /// </summary>
    public static RichTextDocument FromParagraphs(IEnumerable<RichTextParagraph> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        var list = new List<RichTextParagraph>();
        foreach (RichTextParagraph paragraph in paragraphs)
            list.Add(paragraph ?? RichTextParagraph.Empty);
        return new RichTextDocument(list);
    }

    public bool IsValid(RichTextPosition position) =>
        position.ParagraphIndex >= 0 &&
        position.ParagraphIndex < _paragraphs.Length &&
        position.Offset >= 0 &&
        position.Offset <= _paragraphs[position.ParagraphIndex].Length;

    public RichTextPosition ClampPosition(RichTextPosition position)
    {
        int paragraph = Math.Clamp(position.ParagraphIndex, 0, _paragraphs.Length - 1);
        int offset = Math.Clamp(position.Offset, 0, _paragraphs[paragraph].Length);
        return new RichTextPosition(paragraph, offset);
    }

    /// <summary>The inline style a caret at <paramref name="position"/> would inherit for new text.</summary>
    public InlineStyle InlineStyleAt(RichTextPosition position)
    {
        position = ClampPosition(position);
        // Text typed beside a tracked change is not part of it: this model
        // records no changes of its own.
        return _paragraphs[position.ParagraphIndex].StyleBefore(position.Offset) with { Revision = null };
    }

    public RichTextPosition PositionLeftOf(RichTextPosition position)
    {
        position = ClampPosition(position);
        if (position.Offset > 0)
            return new RichTextPosition(position.ParagraphIndex, PreviousBoundary(_paragraphs[position.ParagraphIndex].Text, position.Offset));
        if (position.ParagraphIndex > 0)
            return new RichTextPosition(position.ParagraphIndex - 1, _paragraphs[position.ParagraphIndex - 1].Length);
        return position;
    }

    public RichTextPosition PositionRightOf(RichTextPosition position)
    {
        position = ClampPosition(position);
        RichTextParagraph paragraph = _paragraphs[position.ParagraphIndex];
        if (position.Offset < paragraph.Length)
            return new RichTextPosition(position.ParagraphIndex, NextBoundary(paragraph.Text, position.Offset));
        if (position.ParagraphIndex < _paragraphs.Length - 1)
            return new RichTextPosition(position.ParagraphIndex + 1, 0);
        return position;
    }

    public RichTextPosition ParagraphStart(RichTextPosition position)
    {
        position = ClampPosition(position);
        return new RichTextPosition(position.ParagraphIndex, 0);
    }

    public RichTextPosition ParagraphEnd(RichTextPosition position)
    {
        position = ClampPosition(position);
        return new RichTextPosition(position.ParagraphIndex, _paragraphs[position.ParagraphIndex].Length);
    }

    public RichTextEditResult InsertText(RichTextPosition at, string text, InlineStyle? style = null)
    {
        at = ClampPosition(at);
        if (string.IsNullOrEmpty(text))
            return new RichTextEditResult(this, at);

        text = NormalizeNewlines(text);
        RichTextParagraph target = _paragraphs[at.ParagraphIndex];
        // Text typed after a citation is text, not a second citation of the
        // same note: the style it inherits is the citation's without the note.
        InlineStyle inherited = style ?? target.StyleBefore(at.Offset) with { Note = null };

        if (text.IndexOf('\n') < 0)
        {
            RichTextParagraph updated = target.InsertText(at.Offset, text, inherited);
            return new RichTextEditResult(
                WithParagraph(at.ParagraphIndex, updated),
                new RichTextPosition(at.ParagraphIndex, at.Offset + text.Length));
        }

        string[] lines = text.Split('\n');
        (RichTextParagraph head, RichTextParagraph tail) = target.SplitAt(at.Offset);

        var replacement = new List<RichTextParagraph>(lines.Length)
        {
            head.InsertText(head.Length, lines[0], inherited),
        };
        for (int i = 1; i < lines.Length - 1; i++)
            replacement.Add(RichTextParagraph.Create(lines[i], inherited, target.Style).WithMarkStyle(target.MarkStyle));
        replacement.Add(tail.InsertText(0, lines[^1], inherited));

        RichTextDocument document = ReplaceRange(at.ParagraphIndex, 1, replacement);
        var caret = new RichTextPosition(at.ParagraphIndex + lines.Length - 1, lines[^1].Length);
        return new RichTextEditResult(document, caret);
    }

    public RichTextEditResult SplitParagraph(RichTextPosition at)
    {
        at = ClampPosition(at);
        (RichTextParagraph head, RichTextParagraph tail) = _paragraphs[at.ParagraphIndex].SplitAt(at.Offset);
        RichTextDocument document = ReplaceRange(at.ParagraphIndex, 1, [head, tail]);
        return new RichTextEditResult(document, new RichTextPosition(at.ParagraphIndex + 1, 0));
    }

    public RichTextEditResult MergeParagraphs(int firstParagraphIndex)
    {
        if (firstParagraphIndex < 0 || firstParagraphIndex + 1 >= _paragraphs.Length)
        {
            int safe = Math.Clamp(firstParagraphIndex, 0, _paragraphs.Length - 1);
            return new RichTextEditResult(this, new RichTextPosition(safe, _paragraphs[safe].Length));
        }

        RichTextParagraph first = _paragraphs[firstParagraphIndex];
        int seam = first.Length;
        RichTextParagraph merged = first.Append(_paragraphs[firstParagraphIndex + 1]);
        RichTextDocument document = ReplaceRange(firstParagraphIndex, 2, [merged]);
        return new RichTextEditResult(document, new RichTextPosition(firstParagraphIndex, seam));
    }

    public RichTextEditResult DeleteRange(RichTextRange range)
    {
        RichTextPosition start = ClampPosition(range.Start);
        RichTextPosition end = ClampPosition(range.End);
        if (start == end)
            return new RichTextEditResult(this, start);

        if (start.ParagraphIndex == end.ParagraphIndex)
        {
            RichTextParagraph updated = _paragraphs[start.ParagraphIndex].RemoveRange(start.Offset, end.Offset - start.Offset);
            return new RichTextEditResult(WithParagraph(start.ParagraphIndex, updated), start);
        }

        RichTextParagraph head = _paragraphs[start.ParagraphIndex].SplitAt(start.Offset).Head;
        RichTextParagraph tail = _paragraphs[end.ParagraphIndex].SplitAt(end.Offset).Tail;
        RichTextParagraph merged = head.Append(tail);
        RichTextDocument document = ReplaceRange(start.ParagraphIndex, end.ParagraphIndex - start.ParagraphIndex + 1, [merged]);
        return new RichTextEditResult(document, start);
    }

    public RichTextDocument ApplyInlineStyle(RichTextRange range, InlineStyleDelta delta)
    {
        RichTextPosition start = ClampPosition(range.Start);
        RichTextPosition end = ClampPosition(range.End);
        if (start == end)
            return this;

        var paragraphs = new List<RichTextParagraph>(_paragraphs.Length);
        for (int i = 0; i < _paragraphs.Length; i++)
        {
            if (i < start.ParagraphIndex || i > end.ParagraphIndex)
            {
                paragraphs.Add(_paragraphs[i]);
                continue;
            }

            int startOffset = i == start.ParagraphIndex ? start.Offset : 0;
            int endOffset = i == end.ParagraphIndex ? end.Offset : _paragraphs[i].Length;
            paragraphs.Add(_paragraphs[i].ApplyInlineStyle(startOffset, endOffset - startOffset, delta));
        }

        return new RichTextDocument(paragraphs, RunningContent, Shapes, PageGeometry, Tables, StyleDefaults, Sections, PageBackground, NoteNumbering, ShowsRevisions);
    }

    public RichTextDocument ApplyParagraphStyle(RichTextRange range, ParagraphStyleDelta delta)
    {
        RichTextPosition start = ClampPosition(range.Start);
        RichTextPosition end = ClampPosition(range.End);

        var paragraphs = new List<RichTextParagraph>(_paragraphs.Length);
        for (int i = 0; i < _paragraphs.Length; i++)
        {
            if (i >= start.ParagraphIndex && i <= end.ParagraphIndex)
                paragraphs.Add(_paragraphs[i].WithParagraphStyle(delta.Apply(_paragraphs[i].Style)));
            else
                paragraphs.Add(_paragraphs[i]);
        }

        return new RichTextDocument(paragraphs, RunningContent, Shapes, PageGeometry, Tables, StyleDefaults, Sections, PageBackground, NoteNumbering, ShowsRevisions);
    }

    /// <summary>
    /// Extracts the content of <paramref name="range"/> as a standalone document,
    /// preserving inline runs and paragraph styles. An empty range yields
    /// <see cref="Empty"/>. Used by rich copy.
    /// </summary>
    public RichTextDocument Slice(RichTextRange range)
    {
        RichTextPosition start = ClampPosition(range.Start);
        RichTextPosition end = ClampPosition(range.End);
        if (start == end)
            return Empty;

        if (start.ParagraphIndex == end.ParagraphIndex)
        {
            RichTextParagraph head = _paragraphs[start.ParagraphIndex].SplitAt(end.Offset).Head;
            RichTextParagraph slice = head.SplitAt(start.Offset).Tail;
            return new RichTextDocument([slice]);
        }

        var paragraphs = new List<RichTextParagraph>(end.ParagraphIndex - start.ParagraphIndex + 1)
        {
            _paragraphs[start.ParagraphIndex].SplitAt(start.Offset).Tail,
        };
        for (int i = start.ParagraphIndex + 1; i < end.ParagraphIndex; i++)
            paragraphs.Add(_paragraphs[i]);
        paragraphs.Add(_paragraphs[end.ParagraphIndex].SplitAt(end.Offset).Head);
        return new RichTextDocument(paragraphs);
    }

    /// <summary>
    /// Inserts <paramref name="content"/>'s paragraphs at <paramref name="at"/>. The
    /// first inserted paragraph merges into the paragraph at <paramref name="at"/>
    /// (keeping that paragraph's style) and the last merges the split-off remainder.
    /// Returns the new document and the caret at the end of the inserted content.
    /// Used by rich paste.
    /// </summary>
    public RichTextEditResult InsertDocument(RichTextPosition at, RichTextDocument content)
    {
        at = ClampPosition(at);
        if (content is null || (content.ParagraphCount == 1 && content._paragraphs[0].Length == 0))
            return new RichTextEditResult(this, at);

        (RichTextParagraph head, RichTextParagraph tail) = _paragraphs[at.ParagraphIndex].SplitAt(at.Offset);

        if (content.ParagraphCount == 1)
        {
            RichTextParagraph merged = head.Append(content._paragraphs[0]).Append(tail);
            var caret = new RichTextPosition(at.ParagraphIndex, head.Length + content._paragraphs[0].Length);
            return new RichTextEditResult(WithParagraph(at.ParagraphIndex, merged), caret);
        }

        RichTextParagraph lastContent = content._paragraphs[content.ParagraphCount - 1];
        var replacement = new List<RichTextParagraph>(content.ParagraphCount)
        {
            head.Append(content._paragraphs[0]),
        };
        for (int i = 1; i < content.ParagraphCount - 1; i++)
            replacement.Add(content._paragraphs[i]);
        replacement.Add(lastContent.Append(tail));

        RichTextDocument document = ReplaceRange(at.ParagraphIndex, 1, replacement);
        var lastCaret = new RichTextPosition(at.ParagraphIndex + content.ParagraphCount - 1, lastContent.Length);
        return new RichTextEditResult(document, lastCaret);
    }

    /// <summary>This document with <paramref name="paragraph"/> in place of the one at <paramref name="index"/>.</summary>
    internal RichTextDocument ReplaceParagraph(int index, RichTextParagraph paragraph) => WithParagraph(index, paragraph);

    /// <summary>
    /// This document without <paramref name="count"/> whole paragraphs from
    /// <paramref name="index"/>, its tables and sections moved to follow. A
    /// document is never left without a paragraph.
    /// </summary>
    internal RichTextDocument RemoveParagraphs(int index, int count)
    {
        if (count <= 0 || index < 0 || index + count > _paragraphs.Length)
            return this;

        return count == _paragraphs.Length
            ? ReplaceRange(0, count, [RichTextParagraph.Empty])
            : ReplaceRange(index, count, []);
    }

    private RichTextDocument WithParagraph(int index, RichTextParagraph paragraph)
    {
        var paragraphs = new RichTextParagraph[_paragraphs.Length];
        Array.Copy(_paragraphs, paragraphs, _paragraphs.Length);
        paragraphs[index] = paragraph;
        return new RichTextDocument(paragraphs, RunningContent, Shapes, PageGeometry, Tables, StyleDefaults, Sections, PageBackground, NoteNumbering, ShowsRevisions);
    }

    /// <summary>
    /// Replaces a run of paragraphs. This is the one place a paragraph count
    /// changes, which is why it is the one place a table's ranges are moved to
    /// follow: typing in a cell, splitting its paragraph, and deleting inside it
    /// all arrive here, and the grid comes out the other side still around the
    /// paragraphs it was around.
    /// </summary>
    private RichTextDocument ReplaceRange(int index, int removeCount, List<RichTextParagraph> insert)
    {
        var paragraphs = new List<RichTextParagraph>(_paragraphs.Length - removeCount + insert.Count);
        for (int i = 0; i < index; i++)
            paragraphs.Add(_paragraphs[i]);
        foreach (RichTextParagraph paragraph in insert)
            paragraphs.Add(paragraph);
        for (int i = index + removeCount; i < _paragraphs.Length; i++)
            paragraphs.Add(_paragraphs[i]);

        return new RichTextDocument(
            paragraphs,
            RunningContent,
            Shapes,
            PageGeometry,
            ShiftTables(index, removeCount, insert.Count),
            StyleDefaults,
            ShiftSections(index, removeCount, insert.Count),
            PageBackground,
            NoteNumbering,
            ShowsRevisions);
    }

    /// <summary>This document's tables, moved for an edit at <paramref name="at"/>.</summary>
    private IReadOnlyList<DocumentTable> ShiftTables(int at, int removed, int inserted)
    {
        if (Tables.Count == 0 || removed == inserted)
            return Tables;

        var tables = new List<DocumentTable>(Tables.Count);
        foreach (DocumentTable table in Tables)
        {
            if (table.Shifted(at, removed, inserted) is DocumentTable shifted)
                tables.Add(shifted);
        }

        return tables;
    }

    /// <summary>This document's sections, moved for an edit at <paramref name="at"/>.</summary>
    private IReadOnlyList<DocumentSection> ShiftSections(int at, int removed, int inserted)
    {
        if (Sections.Count == 0 || removed == inserted)
            return Sections;

        var sections = new List<DocumentSection>(Sections.Count);
        foreach (DocumentSection section in Sections)
        {
            if (section.Shifted(at, removed, inserted) is DocumentSection shifted)
                sections.Add(shifted);
        }

        return sections;
    }

    private int IndexOfTable(DocumentTable table)
    {
        for (int i = 0; i < Tables.Count; i++)
        {
            if (ReferenceEquals(Tables[i], table) ||
                (Tables[i].ParagraphIndex == table.ParagraphIndex && Tables[i].ParagraphCount == table.ParagraphCount))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Moves a row in <paramref name="table"/> from <paramref name="fromIndex"/> to <paramref name="toIndex"/>,
    /// rearranging the document's paragraph array and updating all table, cell, and section anchors.
    /// </summary>
    public RichTextDocument MoveTableRow(DocumentTable table, int fromIndex, int toIndex)
    {
        ArgumentNullException.ThrowIfNull(table);
        int tableIndex = IndexOfTable(table);
        if (tableIndex < 0 || fromIndex < 0 || fromIndex >= table.Rows.Count || toIndex < 0 || toIndex >= table.Rows.Count || fromIndex == toIndex)
            return this;

        TableRow movingRow = table.Rows[fromIndex];
        int sourceStart = int.MaxValue;
        int sourceEnd = -1;
        foreach (TableCell cell in movingRow.Cells)
        {
            if (!cell.IsRowSpanContinuation && cell.ParagraphCount > 0)
            {
                sourceStart = Math.Min(sourceStart, cell.ParagraphIndex);
                sourceEnd = Math.Max(sourceEnd, cell.ParagraphEnd);
            }
        }

        int sourceCount = sourceEnd > sourceStart ? sourceEnd - sourceStart : 0;

        TableRow targetRow = table.Rows[toIndex];
        int destinationIndex;
        if (toIndex < fromIndex)
        {
            destinationIndex = int.MaxValue;
            foreach (TableCell cell in targetRow.Cells)
            {
                if (!cell.IsRowSpanContinuation && cell.ParagraphCount > 0)
                    destinationIndex = Math.Min(destinationIndex, cell.ParagraphIndex);
            }
            if (destinationIndex == int.MaxValue)
                destinationIndex = table.ParagraphIndex;
        }
        else
        {
            destinationIndex = -1;
            foreach (TableCell cell in targetRow.Cells)
            {
                if (!cell.IsRowSpanContinuation && cell.ParagraphCount > 0)
                    destinationIndex = Math.Max(destinationIndex, cell.ParagraphEnd);
            }
            if (destinationIndex == -1)
                destinationIndex = table.ParagraphEnd;
        }

        DocumentTable updatedTable = table.MoveRow(fromIndex, toIndex);

        var newParas = new List<RichTextParagraph>(_paragraphs);
        if (sourceCount > 0 && sourceStart != destinationIndex)
        {
            var slice = newParas.GetRange(sourceStart, sourceCount);
            newParas.RemoveRange(sourceStart, sourceCount);
            int insertTarget = destinationIndex > sourceStart ? destinationIndex - sourceCount : destinationIndex;
            newParas.InsertRange(insertTarget, slice);
        }

        var newTables = new List<DocumentTable>(Tables.Count);
        for (int i = 0; i < Tables.Count; i++)
        {
            if (i == tableIndex)
            {
                newTables.Add(updatedTable);
            }
            else
            {
                if (Tables[i].RelocateParagraphs(sourceStart, sourceCount, destinationIndex) is DocumentTable relocated)
                    newTables.Add(relocated);
            }
        }

        var newSections = new List<DocumentSection>(Sections.Count);
        foreach (DocumentSection section in Sections)
        {
            if (section.RelocateParagraphs(sourceStart, sourceCount, destinationIndex) is DocumentSection relocated)
                newSections.Add(relocated);
        }

        return new RichTextDocument(newParas, RunningContent, Shapes, PageGeometry, newTables, StyleDefaults, newSections, PageBackground, NoteNumbering, ShowsRevisions);
    }

    /// <summary>
    /// Inserts a new row into <paramref name="table"/> at <paramref name="atIndex"/>,
    /// inserting empty paragraphs into the document and shifting subsequent anchors.
    /// </summary>
    public RichTextDocument InsertTableRow(
        DocumentTable table,
        int atIndex,
        TableRow? row = null,
        int paragraphsPerCell = 1,
        Func<int, RichTextParagraph>? paragraphFactory = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        int tableIndex = IndexOfTable(table);
        if (tableIndex < 0)
            return this;

        DocumentTable updatedTable = table.InsertRow(atIndex, row, paragraphsPerCell);
        int totalInserted = updatedTable.ParagraphCount - table.ParagraphCount;

        int target = Math.Clamp(atIndex, 0, updatedTable.Rows.Count - 1);
        int insertAt = updatedTable.Rows[target].Cells.FirstOrDefault(c => !c.IsRowSpanContinuation)?.ParagraphIndex ?? table.ParagraphEnd;

        var newParas = new List<RichTextParagraph>(_paragraphs.Length + totalInserted);
        for (int i = 0; i < insertAt; i++)
            newParas.Add(_paragraphs[i]);

        for (int i = 0; i < totalInserted; i++)
            newParas.Add(paragraphFactory?.Invoke(i) ?? RichTextParagraph.Empty);

        for (int i = insertAt; i < _paragraphs.Length; i++)
            newParas.Add(_paragraphs[i]);

        var newTables = new List<DocumentTable>(Tables.Count);
        for (int i = 0; i < Tables.Count; i++)
        {
            if (i == tableIndex)
            {
                newTables.Add(updatedTable);
            }
            else
            {
                if (Tables[i].ParagraphIndex >= insertAt)
                {
                    if (Tables[i].Shifted(insertAt, 0, totalInserted, RangeAffinity.ShiftAtStart) is DocumentTable shifted)
                        newTables.Add(shifted);
                }
                else
                {
                    newTables.Add(Tables[i]);
                }
            }
        }

        var newSections = new List<DocumentSection>(Sections.Count);
        foreach (DocumentSection section in Sections)
        {
            if (section.ParagraphIndex >= insertAt)
            {
                if (section.Shifted(insertAt, 0, totalInserted, RangeAffinity.ShiftAtStart) is DocumentSection shifted)
                    newSections.Add(shifted);
            }
            else
            {
                newSections.Add(section);
            }
        }

        return new RichTextDocument(newParas, RunningContent, Shapes, PageGeometry, newTables, StyleDefaults, newSections, PageBackground, NoteNumbering, ShowsRevisions);
    }

    /// <summary>
    /// Deletes the row at <paramref name="atIndex"/> from <paramref name="table"/>,
    /// removing its paragraphs and adjusting spans and subsequent anchors.
    /// </summary>
    public RichTextDocument DeleteTableRow(DocumentTable table, int atIndex)
    {
        ArgumentNullException.ThrowIfNull(table);
        int tableIndex = IndexOfTable(table);
        if (tableIndex < 0 || atIndex < 0 || atIndex >= table.Rows.Count)
            return this;

        TableRow deletingRow = table.Rows[atIndex];
        int removeStart = int.MaxValue;
        int removeEnd = -1;
        foreach (TableCell cell in deletingRow.Cells)
        {
            if (!cell.IsRowSpanContinuation && cell.ParagraphCount > 0)
            {
                removeStart = Math.Min(removeStart, cell.ParagraphIndex);
                removeEnd = Math.Max(removeEnd, cell.ParagraphEnd);
            }
        }

        int totalRemoved = removeEnd > removeStart ? removeEnd - removeStart : 0;
        DocumentTable? updatedTable = table.DeleteRow(atIndex);

        var newParas = new List<RichTextParagraph>(Math.Max(1, _paragraphs.Length - totalRemoved));
        if (totalRemoved > 0)
        {
            for (int i = 0; i < removeStart; i++)
                newParas.Add(_paragraphs[i]);
            for (int i = removeStart + totalRemoved; i < _paragraphs.Length; i++)
                newParas.Add(_paragraphs[i]);
        }
        else
        {
            newParas.AddRange(_paragraphs);
        }

        if (newParas.Count == 0)
            newParas.Add(RichTextParagraph.Empty);

        var newTables = new List<DocumentTable>(Tables.Count);
        for (int i = 0; i < Tables.Count; i++)
        {
            if (i == tableIndex)
            {
                if (updatedTable is not null)
                    newTables.Add(updatedTable);
            }
            else
            {
                if (totalRemoved > 0 && Tables[i].ParagraphIndex >= removeStart)
                {
                    if (Tables[i].Shifted(removeStart, totalRemoved, 0) is DocumentTable shifted)
                        newTables.Add(shifted);
                }
                else
                {
                    newTables.Add(Tables[i]);
                }
            }
        }

        var newSections = new List<DocumentSection>(Sections.Count);
        foreach (DocumentSection section in Sections)
        {
            if (totalRemoved > 0 && section.ParagraphIndex >= removeStart)
            {
                if (section.Shifted(removeStart, totalRemoved, 0) is DocumentSection shifted)
                    newSections.Add(shifted);
            }
            else
            {
                newSections.Add(section);
            }
        }

        return new RichTextDocument(newParas, RunningContent, Shapes, PageGeometry, newTables, StyleDefaults, newSections, PageBackground, NoteNumbering, ShowsRevisions);
    }

    /// <summary>
    /// Inserts a new column into <paramref name="table"/> at <paramref name="atIndex"/>,
    /// inserting empty paragraphs into the document and adjusting table and subsequent anchors.
    /// </summary>
    public RichTextDocument InsertTableColumn(
        DocumentTable table,
        int atIndex,
        double width = 0,
        int paragraphsPerCell = 1,
        Func<int, int, RichTextParagraph>? paragraphFactory = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        int tableIndex = IndexOfTable(table);
        if (tableIndex < 0)
            return this;

        DocumentTable updatedTable = table.InsertColumn(atIndex, width, paragraphsPerCell);
        int totalInserted = updatedTable.ParagraphCount - table.ParagraphCount;

        var newParas = new List<RichTextParagraph>(_paragraphs.Length + totalInserted);
        for (int i = 0; i < table.ParagraphIndex; i++)
            newParas.Add(_paragraphs[i]);

        for (int r = 0; r < updatedTable.Rows.Count; r++)
        {
            TableRow row = updatedTable.Rows[r];
            foreach (TableCell cell in row.Cells)
            {
                if (cell.IsRowSpanContinuation)
                    continue;

                TableCell? originalCell = r < table.Rows.Count
                    ? table.Rows[r].Cells.FirstOrDefault(c => c.ColumnIndex == cell.ColumnIndex && !c.IsRowSpanContinuation)
                    : null;

                if (cell.ColumnIndex == atIndex && originalCell is null)
                {
                    for (int p = 0; p < cell.ParagraphCount; p++)
                        newParas.Add(paragraphFactory?.Invoke(r, atIndex) ?? RichTextParagraph.Empty);
                }
                else
                {
                    TableCell? sourceCell = r < table.Rows.Count
                        ? table.Rows[r].Cells.FirstOrDefault(c =>
                            (c.ColumnIndex == (cell.ColumnIndex > atIndex ? cell.ColumnIndex - 1 : cell.ColumnIndex)))
                        : null;

                    if (sourceCell is not null && !sourceCell.IsRowSpanContinuation)
                    {
                        for (int p = 0; p < sourceCell.ParagraphCount; p++)
                        {
                            int srcIdx = sourceCell.ParagraphIndex + p;
                            newParas.Add(srcIdx < _paragraphs.Length ? _paragraphs[srcIdx] : RichTextParagraph.Empty);
                        }
                    }
                    else
                    {
                        for (int p = 0; p < cell.ParagraphCount; p++)
                            newParas.Add(RichTextParagraph.Empty);
                    }
                }
            }
        }

        for (int i = table.ParagraphEnd; i < _paragraphs.Length; i++)
            newParas.Add(_paragraphs[i]);

        var newTables = new List<DocumentTable>(Tables.Count);
        for (int i = 0; i < Tables.Count; i++)
        {
            if (i == tableIndex)
            {
                newTables.Add(updatedTable);
            }
            else
            {
                if (Tables[i].ParagraphIndex >= table.ParagraphEnd)
                {
                    if (Tables[i].Shifted(table.ParagraphEnd, 0, totalInserted, RangeAffinity.ShiftAtStart) is DocumentTable shifted)
                        newTables.Add(shifted);
                }
                else
                {
                    newTables.Add(Tables[i]);
                }
            }
        }

        var newSections = new List<DocumentSection>(Sections.Count);
        foreach (DocumentSection section in Sections)
        {
            if (section.ParagraphIndex >= table.ParagraphEnd)
            {
                if (section.Shifted(table.ParagraphEnd, 0, totalInserted, RangeAffinity.ShiftAtStart) is DocumentSection shifted)
                    newSections.Add(shifted);
            }
            else
            {
                newSections.Add(section);
            }
        }

        return new RichTextDocument(newParas, RunningContent, Shapes, PageGeometry, newTables, StyleDefaults, newSections, PageBackground, NoteNumbering, ShowsRevisions);
    }

    /// <summary>
    /// Deletes column <paramref name="atIndex"/> from <paramref name="table"/>,
    /// removing its paragraphs and adjusting column spans, widths, and subsequent anchors.
    /// </summary>
    public RichTextDocument DeleteTableColumn(DocumentTable table, int atIndex)
    {
        ArgumentNullException.ThrowIfNull(table);
        int tableIndex = IndexOfTable(table);
        if (tableIndex < 0)
            return this;

        DocumentTable? updatedTable = table.DeleteColumn(atIndex);
        if (updatedTable is null)
            return DeleteTableRow(table, 0);

        int totalRemoved = table.ParagraphCount - updatedTable.ParagraphCount;

        var newParas = new List<RichTextParagraph>(Math.Max(1, _paragraphs.Length - totalRemoved));
        for (int i = 0; i < table.ParagraphIndex; i++)
            newParas.Add(_paragraphs[i]);

        for (int r = 0; r < updatedTable.Rows.Count; r++)
        {
            TableRow row = updatedTable.Rows[r];
            foreach (TableCell cell in row.Cells)
            {
                if (cell.IsRowSpanContinuation)
                    continue;

                TableCell? sourceCell = r < table.Rows.Count
                    ? table.Rows[r].Cells.FirstOrDefault(c =>
                        (c.ColumnIndex == (cell.ColumnIndex >= atIndex ? cell.ColumnIndex + 1 : cell.ColumnIndex)))
                    : null;

                if (sourceCell is not null && !sourceCell.IsRowSpanContinuation)
                {
                    for (int p = 0; p < sourceCell.ParagraphCount; p++)
                    {
                        int srcIdx = sourceCell.ParagraphIndex + p;
                        newParas.Add(srcIdx < _paragraphs.Length ? _paragraphs[srcIdx] : RichTextParagraph.Empty);
                    }
                }
                else
                {
                    for (int p = 0; p < cell.ParagraphCount; p++)
                        newParas.Add(RichTextParagraph.Empty);
                }
            }
        }

        for (int i = table.ParagraphEnd; i < _paragraphs.Length; i++)
            newParas.Add(_paragraphs[i]);

        if (newParas.Count == 0)
            newParas.Add(RichTextParagraph.Empty);

        var newTables = new List<DocumentTable>(Tables.Count);
        for (int i = 0; i < Tables.Count; i++)
        {
            if (i == tableIndex)
            {
                newTables.Add(updatedTable);
            }
            else
            {
                if (totalRemoved > 0 && Tables[i].ParagraphIndex >= table.ParagraphEnd)
                {
                    if (Tables[i].Shifted(table.ParagraphEnd, totalRemoved, 0) is DocumentTable shifted)
                        newTables.Add(shifted);
                }
                else
                {
                    newTables.Add(Tables[i]);
                }
            }
        }

        var newSections = new List<DocumentSection>(Sections.Count);
        foreach (DocumentSection section in Sections)
        {
            if (totalRemoved > 0 && section.ParagraphIndex >= table.ParagraphEnd)
            {
                if (section.Shifted(table.ParagraphEnd, totalRemoved, 0) is DocumentSection shifted)
                    newSections.Add(shifted);
            }
            else
            {
                newSections.Add(section);
            }
        }

        return new RichTextDocument(newParas, RunningContent, Shapes, PageGeometry, newTables, StyleDefaults, newSections, PageBackground, NoteNumbering, ShowsRevisions);
    }

    /// <summary>
    /// Merges a block of cells in <paramref name="table"/>, collecting all cell content into the root cell.
    /// </summary>
    public RichTextDocument MergeTableCells(DocumentTable table, int startRow, int startColumn, int rowSpan, int columnSpan)
    {
        ArgumentNullException.ThrowIfNull(table);
        int tableIndex = IndexOfTable(table);
        if (tableIndex < 0)
            return this;

        DocumentTable updatedTable = table.MergeCells(startRow, startColumn, rowSpan, columnSpan);
        var newTables = new List<DocumentTable>(Tables);
        newTables[tableIndex] = updatedTable;

        return new RichTextDocument(_paragraphs, RunningContent, Shapes, PageGeometry, newTables, StyleDefaults, Sections, PageBackground, NoteNumbering, ShowsRevisions);
    }

    /// <summary>
    /// Splits a cell in <paramref name="table"/>, restoring unmerged cells or splitting dimensions.
    /// </summary>
    public RichTextDocument SplitTableCell(DocumentTable table, int row, int column, int horizontalSplit = 1, int verticalSplit = 1, int newParagraphsPerSplit = 1)
    {
        ArgumentNullException.ThrowIfNull(table);
        int tableIndex = IndexOfTable(table);
        if (tableIndex < 0)
            return this;

        DocumentTable updatedTable = table.SplitCell(row, column, horizontalSplit, verticalSplit, newParagraphsPerSplit);
        int totalInserted = updatedTable.ParagraphCount - table.ParagraphCount;

        if (totalInserted <= 0)
        {
            var updatedTablesOnly = new List<DocumentTable>(Tables);
            updatedTablesOnly[tableIndex] = updatedTable;
            return new RichTextDocument(_paragraphs, RunningContent, Shapes, PageGeometry, updatedTablesOnly, StyleDefaults, Sections, PageBackground, NoteNumbering, ShowsRevisions);
        }

        var newParas = new List<RichTextParagraph>(_paragraphs.Length + totalInserted);
        for (int i = 0; i < table.ParagraphIndex; i++)
            newParas.Add(_paragraphs[i]);

        for (int r = 0; r < updatedTable.Rows.Count; r++)
        {
            TableRow tableRow = updatedTable.Rows[r];
            foreach (TableCell cell in tableRow.Cells)
            {
                if (cell.IsRowSpanContinuation)
                    continue;

                if (cell.ParagraphIndex < table.ParagraphEnd)
                {
                    int oldStart = cell.ParagraphIndex;
                    for (int p = 0; p < cell.ParagraphCount; p++)
                    {
                        int src = oldStart + p;
                        newParas.Add(src < _paragraphs.Length ? _paragraphs[src] : RichTextParagraph.Empty);
                    }
                }
                else
                {
                    for (int p = 0; p < cell.ParagraphCount; p++)
                        newParas.Add(RichTextParagraph.Empty);
                }
            }
        }

        for (int i = table.ParagraphEnd; i < _paragraphs.Length; i++)
            newParas.Add(_paragraphs[i]);

        var newTables = new List<DocumentTable>(Tables.Count);
        for (int i = 0; i < Tables.Count; i++)
        {
            if (i == tableIndex)
            {
                newTables.Add(updatedTable);
            }
            else
            {
                if (Tables[i].ParagraphIndex >= table.ParagraphEnd)
                {
                    if (Tables[i].Shifted(table.ParagraphEnd, 0, totalInserted, RangeAffinity.ShiftAtStart) is DocumentTable shifted)
                        newTables.Add(shifted);
                }
                else
                {
                    newTables.Add(Tables[i]);
                }
            }
        }

        var newSections = new List<DocumentSection>(Sections.Count);
        foreach (DocumentSection section in Sections)
        {
            if (section.ParagraphIndex >= table.ParagraphEnd)
            {
                if (section.Shifted(table.ParagraphEnd, 0, totalInserted, RangeAffinity.ShiftAtStart) is DocumentSection shifted)
                    newSections.Add(shifted);
            }
            else
            {
                newSections.Add(section);
            }
        }

        return new RichTextDocument(newParas, RunningContent, Shapes, PageGeometry, newTables, StyleDefaults, newSections, PageBackground, NoteNumbering, ShowsRevisions);
    }

    /// <summary>
    /// Evaluates all formulas in <paramref name="table"/>, updating the document paragraphs with evaluated
    /// results while preserving paragraph and run styling.
    /// </summary>
    public (RichTextDocument Document, TableEvaluationResult Result) EvaluateTableFormulas(
        DocumentTable table,
        Func<CellValue, string>? valueFormatter = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        int tableIndex = IndexOfTable(table);
        var result = TableFormulaEngine.Evaluate(table, this);
        var currentDoc = this;

        var updatedRows = new List<TableRow>(table.Rows.Count);
        for (int r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            var updatedCells = new List<TableCell>(row.Cells.Count);
            for (int c = 0; c < row.Cells.Count; c++)
            {
                var cell = row.Cells[c];
                int gridRow = r;
                int gridCol = cell.ColumnIndex;

                string? formula = cell.Formula;
                bool textHadFormula = false;
                if (string.IsNullOrWhiteSpace(formula) && cell.ParagraphCount > 0 && cell.ParagraphIndex < currentDoc.Paragraphs.Count)
                {
                    string currentText = currentDoc.Paragraphs[cell.ParagraphIndex].Text.TrimStart();
                    if (currentText.StartsWith('='))
                    {
                        formula = currentText;
                        textHadFormula = true;
                    }
                }

                if (!string.IsNullOrWhiteSpace(formula))
                {
                    var cellVal = result.GetValue(gridRow, gridCol);
                    FormattedCellResult formattedResult = valueFormatter != null
                        ? new FormattedCellResult(valueFormatter(cellVal))
                        : CellNumberFormatter.Format(cellVal, cell.NumberFormat);
                    string formattedText = formattedResult.Text;

                    if (cell.ParagraphCount > 0 && cell.ParagraphIndex < currentDoc.Paragraphs.Count)
                    {
                        var p = currentDoc.Paragraphs[cell.ParagraphIndex];
                        var style = p.Runs.Count > 0 ? p.Runs[0].Style : p.MarkStyle;
                        if (formattedResult.Color.HasValue)
                        {
                            style = style with { Foreground = formattedResult.Color.Value };
                        }
                        var newP = RichTextParagraph.Create(formattedText, style, p.Style);
                        currentDoc = currentDoc.ReplaceParagraph(cell.ParagraphIndex, newP);

                        for (int pi = 1; pi < cell.ParagraphCount; pi++)
                        {
                            int pIdx = cell.ParagraphIndex + pi;
                            if (pIdx < currentDoc.Paragraphs.Count)
                            {
                                var emptyP = RichTextParagraph.Create(string.Empty, style, currentDoc.Paragraphs[pIdx].Style);
                                currentDoc = currentDoc.ReplaceParagraph(pIdx, emptyP);
                            }
                        }
                    }

                    if (cell.Formula == null && textHadFormula)
                    {
                        cell = cell.WithFormula(formula);
                    }
                }

                updatedCells.Add(cell);
            }

            updatedRows.Add(new TableRow(
                updatedCells,
                row.IsHeader,
                row.MinHeight,
                row.CanSplit,
                row.ExactHeight,
                row.KeepWithNext));
        }

        var updatedTable = new DocumentTable(
            table.ParagraphIndex,
            table.ParagraphCount,
            updatedRows,
            table.ColumnWidths,
            table.CellPadding,
            table.Position,
            table.KeepWithNext,
            table.PreferredWidth,
            table.WidthPercent,
            table.Alignment,
            table.LeftIndent,
            table.PageBreakBefore,
            table.ColumnBreakBefore,
            table.AutoFit,
            table.KeepTogether);

        var newTables = new List<DocumentTable>(currentDoc.Tables.Count);
        if (tableIndex >= 0 && tableIndex < currentDoc.Tables.Count)
        {
            for (int i = 0; i < currentDoc.Tables.Count; i++)
                newTables.Add(i == tableIndex ? updatedTable : currentDoc.Tables[i]);
        }
        else
        {
            newTables.AddRange(currentDoc.Tables);
            newTables.Add(updatedTable);
        }

        var finalDoc = new RichTextDocument(
            [.. currentDoc.Paragraphs],
            currentDoc.RunningContent,
            currentDoc.Shapes,
            currentDoc.PageGeometry,
            newTables,
            currentDoc.StyleDefaults,
            currentDoc.Sections,
            currentDoc.PageBackground,
            currentDoc.NoteNumbering,
            currentDoc.ShowsRevisions);

        return (finalDoc, result);
    }

    /// <summary>
    /// Recalculates formulas for all tables in this document, updating paragraph text and styles.
    /// </summary>
    public (RichTextDocument Document, IReadOnlyList<TableEvaluationResult> Results) EvaluateAllTableFormulas(
        Func<CellValue, string>? valueFormatter = null)
    {
        var currentDoc = this;
        var results = new List<TableEvaluationResult>(Tables.Count);
        for (int i = 0; i < currentDoc.Tables.Count; i++)
        {
            var table = currentDoc.Tables[i];
            var (updatedDoc, res) = currentDoc.EvaluateTableFormulas(table, valueFormatter);
            currentDoc = updatedDoc;
            results.Add(res);
        }
        return (currentDoc, results);
    }

    /// <summary>
    /// Applies <see cref="TableCell.NumberFormat"/> formatting and text color to all cells in <paramref name="table"/>.
    /// </summary>
    public RichTextDocument FormatTableCells(DocumentTable table, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        int tableIndex = IndexOfTable(table);
        if (tableIndex < 0) return this;

        var currentDoc = this;
        for (int r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            for (int c = 0; c < row.Cells.Count; c++)
            {
                var cell = row.Cells[c];
                if (cell.IsRowSpanContinuation || string.IsNullOrWhiteSpace(cell.NumberFormat) || cell.ParagraphCount == 0)
                    continue;

                if (cell.ParagraphIndex >= currentDoc.Paragraphs.Count)
                    continue;

                string currentText = currentDoc.Paragraphs[cell.ParagraphIndex].Text;
                var rawVal = CellValue.Parse(currentText);
                var formatted = CellNumberFormatter.Format(rawVal, cell.NumberFormat, culture);

                var p = currentDoc.Paragraphs[cell.ParagraphIndex];
                var style = p.Runs.Count > 0 ? p.Runs[0].Style : p.MarkStyle;
                if (formatted.Color.HasValue)
                    style = style with { Foreground = formatted.Color.Value };

                var newP = RichTextParagraph.Create(formatted.Text, style, p.Style);
                currentDoc = currentDoc.ReplaceParagraph(cell.ParagraphIndex, newP);
            }
        }

        return currentDoc;
    }

    /// <summary>
    /// Applies <see cref="TableCell.NumberFormat"/> formatting and text color to all tables in this document.
    /// </summary>
    public RichTextDocument FormatAllTableCells(CultureInfo? culture = null)
    {
        var currentDoc = this;
        foreach (var table in Tables)
            currentDoc = currentDoc.FormatTableCells(table, culture);
        return currentDoc;
    }

    private static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static int PreviousBoundary(string text, int offset)
    {
        int index = offset - 1;
        if (index > 0 && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1]))
            index--;
        return index;
    }

    private static int NextBoundary(string text, int offset)
    {
        int index = offset + 1;
        if (char.IsHighSurrogate(text[offset]) && index < text.Length && char.IsLowSurrogate(text[index]))
            index++;
        return index;
    }
}
