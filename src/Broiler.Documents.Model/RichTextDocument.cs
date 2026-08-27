using System;
using System.Collections.Generic;
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

    private RichTextDocument(IReadOnlyList<RichTextParagraph> paragraphs)
    {
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
    public static RichTextDocument Empty { get; } = new(new[] { RichTextParagraph.Empty });

    public IReadOnlyList<RichTextParagraph> Paragraphs => _paragraphs;

    public int ParagraphCount => _paragraphs.Length;

    public RichTextPosition Start => new(0, 0);

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
        return _paragraphs[position.ParagraphIndex].StyleBefore(position.Offset);
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
        InlineStyle inherited = style ?? target.StyleBefore(at.Offset);

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
            replacement.Add(RichTextParagraph.Create(lines[i], inherited, target.Style));
        replacement.Add(tail.InsertText(0, lines[^1], inherited));

        RichTextDocument document = ReplaceRange(at.ParagraphIndex, 1, replacement);
        var caret = new RichTextPosition(at.ParagraphIndex + lines.Length - 1, lines[^1].Length);
        return new RichTextEditResult(document, caret);
    }

    public RichTextEditResult SplitParagraph(RichTextPosition at)
    {
        at = ClampPosition(at);
        (RichTextParagraph head, RichTextParagraph tail) = _paragraphs[at.ParagraphIndex].SplitAt(at.Offset);
        RichTextDocument document = ReplaceRange(at.ParagraphIndex, 1, new[] { head, tail });
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
        RichTextDocument document = ReplaceRange(firstParagraphIndex, 2, new[] { merged });
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
        RichTextDocument document = ReplaceRange(start.ParagraphIndex, end.ParagraphIndex - start.ParagraphIndex + 1, new[] { merged });
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

        return new RichTextDocument(paragraphs);
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

        return new RichTextDocument(paragraphs);
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
            return new RichTextDocument(new[] { slice });
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

    private RichTextDocument WithParagraph(int index, RichTextParagraph paragraph)
    {
        var paragraphs = new RichTextParagraph[_paragraphs.Length];
        Array.Copy(_paragraphs, paragraphs, _paragraphs.Length);
        paragraphs[index] = paragraph;
        return new RichTextDocument(paragraphs);
    }

    private RichTextDocument ReplaceRange(int index, int removeCount, IReadOnlyList<RichTextParagraph> insert)
    {
        var paragraphs = new List<RichTextParagraph>(_paragraphs.Length - removeCount + insert.Count);
        for (int i = 0; i < index; i++)
            paragraphs.Add(_paragraphs[i]);
        foreach (RichTextParagraph paragraph in insert)
            paragraphs.Add(paragraph);
        for (int i = index + removeCount; i < _paragraphs.Length; i++)
            paragraphs.Add(_paragraphs[i]);
        return new RichTextDocument(paragraphs);
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
