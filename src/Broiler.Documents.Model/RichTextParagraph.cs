using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Broiler.Documents.Model;

/// <summary>
/// An immutable paragraph: its text, a paragraph style, and a normalized list of
/// inline style runs. Runs are contiguous, carry no zero-length entries, never
/// have equal-styled neighbors, and cover exactly <see cref="Text"/>. Every
/// mutating method returns a new instance (copy-on-write).
/// </summary>
public sealed class RichTextParagraph
{
    private RichTextParagraph(string text, ParagraphStyle style, IReadOnlyList<StyleRun> runs, InlineStyle markStyle = default)
    {
        Text = text;
        Style = style;
        Runs = runs;
        MarkStyle = markStyle;
    }

    /// <summary>An empty paragraph with default styling.</summary>
    public static RichTextParagraph Empty { get; } =
        new(string.Empty, ParagraphStyle.Default, []);

    public string Text { get; }

    public ParagraphStyle Style { get; }

    /// <summary>The normalized inline runs, in order, covering <see cref="Text"/>.</summary>
    public IReadOnlyList<StyleRun> Runs { get; }

    /// <summary>
    /// The character formatting of the paragraph's end, which is what an empty
    /// line in it is as tall as.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A paragraph with no text has no run to take a font from, and is still a
    /// line of the page. Word states the size of that line on the paragraph mark,
    /// <c>w:pPr/w:rPr</c>, and Word and LibreOffice both draw the empty paragraph
    /// at it. Without it the line came out at the document's default size, and a
    /// document whose blank lines are 10 points under a 12-point default drew
    /// each of them a fifth too tall.
    /// </para>
    /// <para>
    /// It belongs to the paragraph rather than to any run, so editing the text
    /// keeps it, and a split gives both halves the one mark. <see cref="StyleAt"/>
    /// still reports the default for an empty paragraph: what text typed into
    /// one takes is the editor's decision, and nothing here changes it.
    /// </para>
    /// </remarks>
    public InlineStyle MarkStyle { get; }

    public int Length => Text.Length;

    /// <summary>Creates a plain-text paragraph with default styling.</summary>
    public static RichTextParagraph Plain(string? text) =>
        Create(text, InlineStyle.Default, ParagraphStyle.Default);

    public static RichTextParagraph Create(string? text, InlineStyle style) =>
        Create(text, style, ParagraphStyle.Default);

    public static RichTextParagraph Create(string? text, InlineStyle style, ParagraphStyle paragraphStyle)
    {
        text ??= string.Empty;
        StyleRun[] runs = text.Length == 0
            ? []
            : [new StyleRun(text.Length, style)];
        return new RichTextParagraph(text, paragraphStyle, runs);
    }

    /// <summary>The inline style of the character at <paramref name="offset"/>.</summary>
    public InlineStyle StyleAt(int offset)
    {
        if (Runs.Count == 0)
            return InlineStyle.Default;

        int pos = 0;
        foreach (StyleRun run in Runs)
        {
            if (offset < pos + run.Length)
                return run.Style;
            pos += run.Length;
        }

        return Runs[^1].Style;
    }

    /// <summary>The inline style a caret at <paramref name="offset"/> would inherit for new text.</summary>
    public InlineStyle StyleBefore(int offset) => offset <= 0 ? StyleAt(0) : StyleAt(offset - 1);

    public RichTextParagraph WithParagraphStyle(ParagraphStyle style) => new(Text, style, Runs, MarkStyle);

    /// <summary>Returns this paragraph with <paramref name="style"/> as its <see cref="MarkStyle"/>.</summary>
    public RichTextParagraph WithMarkStyle(InlineStyle style) => new(Text, Style, Runs, style);

    internal RichTextParagraph MapInlineStyles(Func<InlineStyle, InlineStyle> map)
    {
        List<StyleRun>? updated = null;
        for (int index = 0; index < Runs.Count; index++)
        {
            StyleRun run = Runs[index];
            InlineStyle style = map(run.Style);
            if (!style.Equals(run.Style))
            {
                updated ??= [.. Runs];
                updated[index] = new StyleRun(run.Length, style);
            }
        }

        InlineStyle mark = map(MarkStyle);
        return updated is null && mark.Equals(MarkStyle)
            ? this
            : new RichTextParagraph(Text, Style, updated is null ? Runs : Normalize(updated), mark);
    }

    public RichTextParagraph InsertText(int offset, string text, InlineStyle style)
    {
        if (string.IsNullOrEmpty(text))
            return this;

        offset = Math.Clamp(offset, 0, Text.Length);
        var runs = new List<StyleRun>(Runs.Count + 2);
        int pos = 0;
        bool inserted = false;
        foreach (StyleRun run in Runs)
        {
            if (!inserted && offset < pos + run.Length)
            {
                int left = offset - pos;
                if (left > 0)
                    runs.Add(new StyleRun(left, run.Style));
                runs.Add(new StyleRun(text.Length, style));
                if (run.Length - left > 0)
                    runs.Add(new StyleRun(run.Length - left, run.Style));
                inserted = true;
            }
            else
            {
                runs.Add(run);
            }

            pos += run.Length;
        }

        if (!inserted)
            runs.Add(new StyleRun(text.Length, style));

        string newText = string.Concat(Text.AsSpan(0, offset), text, Text.AsSpan(offset));
        var styleAfterInsert = Style.SoftPageMarkerOffsets.IsDefaultOrEmpty ? Style : Style with
        {
            SoftPageMarkerOffsets = [.. Style.SoftPageMarkerOffsets.Select(marker => marker >= offset ? marker + text.Length : marker)],
        };
        return new RichTextParagraph(newText, styleAfterInsert, Normalize(runs), MarkStyle);
    }

    public RichTextParagraph RemoveRange(int start, int length)
    {
        start = Math.Clamp(start, 0, Text.Length);
        length = Math.Clamp(length, 0, Text.Length - start);
        if (length == 0)
            return this;

        int end = start + length;
        var runs = new List<StyleRun>(Runs.Count);
        int pos = 0;
        foreach (StyleRun run in Runs)
        {
            int runEnd = pos + run.Length;
            int keepLeft = Math.Max(0, Math.Min(runEnd, start) - pos);
            int keepRight = Math.Max(0, runEnd - Math.Max(pos, end));
            int keep = keepLeft + keepRight;
            if (keep > 0)
                runs.Add(new StyleRun(keep, run.Style));
            pos = runEnd;
        }

        var styleAfterRemove = Style.SoftPageMarkerOffsets.IsDefaultOrEmpty ? Style : Style with
        {
            SoftPageMarkerOffsets = [.. Style.SoftPageMarkerOffsets.Select(marker => marker <= start ? marker : Math.Max(start, marker - length))],
        };
        return new RichTextParagraph(Text.Remove(start, length), styleAfterRemove, Normalize(runs), MarkStyle);
    }

    public RichTextParagraph ApplyInlineStyle(int start, int length, InlineStyleDelta delta)
    {
        start = Math.Clamp(start, 0, Text.Length);
        length = Math.Clamp(length, 0, Text.Length - start);
        if (length == 0)
            return this;

        int end = start + length;
        var runs = new List<StyleRun>(Runs.Count + 2);
        int pos = 0;
        foreach (StyleRun run in Runs)
        {
            int runEnd = pos + run.Length;
            int affStart = Math.Max(pos, start);
            int affEnd = Math.Min(runEnd, end);
            if (affStart >= affEnd)
            {
                runs.Add(run);
            }
            else
            {
                int leftLen = affStart - pos;
                int midLen = affEnd - affStart;
                int rightLen = runEnd - affEnd;
                if (leftLen > 0)
                    runs.Add(new StyleRun(leftLen, run.Style));
                if (midLen > 0)
                    runs.Add(new StyleRun(midLen, delta.Apply(run.Style)));
                if (rightLen > 0)
                    runs.Add(new StyleRun(rightLen, run.Style));
            }

            pos = runEnd;
        }

        return new RichTextParagraph(Text, Style, Normalize(runs), MarkStyle);
    }

    /// <summary>Splits into a head (before <paramref name="offset"/>) and tail (from it).</summary>
    public (RichTextParagraph Head, RichTextParagraph Tail) SplitAt(int offset)
    {
        offset = Math.Clamp(offset, 0, Text.Length);

        // The tail does not inherit the page or column break. Every other paragraph
        // property describes how the paragraph looks and belongs to both halves;
        // a break describes where the paragraph starts, and there is only one
        // start. Copying it would mean that pressing Enter inside a paragraph
        // that begins a page silently added a second page break to the document -
        // an edit inventing content nobody typed, which is the worst kind.
        //
        // Merge needs no equivalent care: Append keeps the first paragraph's
        // style, so the surviving break is the one that was already there.
        var headStyle = Style;
        var tailStyle = Style with { PageBreakBefore = false, ColumnBreakBefore = false, SoftPageBreakBefore = 0 };
        if (!Style.SoftPageMarkerOffsets.IsDefaultOrEmpty)
        {
            headStyle = headStyle with { SoftPageMarkerOffsets = [.. Style.SoftPageMarkerOffsets.Where(marker => marker <= offset)] };
            tailStyle = tailStyle with { SoftPageMarkerOffsets = [.. Style.SoftPageMarkerOffsets.Where(marker => marker > offset).Select(marker => marker - offset)] };
        }
        var head = new RichTextParagraph(Text[..offset], headStyle, SliceRuns(0, offset), MarkStyle);
        var tail = new RichTextParagraph(
            Text[offset..],
            tailStyle,
            SliceRuns(offset, Text.Length - offset),
            MarkStyle);
        return (head, tail);
    }

    /// <summary>Concatenates <paramref name="other"/> onto this paragraph, keeping this paragraph's style and mark.</summary>
    public RichTextParagraph Append(RichTextParagraph other)
    {
        var combined = new List<StyleRun>(Runs.Count + other.Runs.Count);
        combined.AddRange(Runs);
        combined.AddRange(other.Runs);
        var style = Style;
        if (other.Style.SoftPageBreakBefore > 0 || !other.Style.SoftPageMarkerOffsets.IsDefaultOrEmpty)
        {
            var markers = Style.SoftPageMarkerOffsets.IsDefault ? ImmutableArray<int>.Empty : Style.SoftPageMarkerOffsets;
            if (other.Style.SoftPageBreakBefore > 0)
                markers = markers.Add(Length);
            if (!other.Style.SoftPageMarkerOffsets.IsDefaultOrEmpty)
                markers = markers.AddRange(other.Style.SoftPageMarkerOffsets.Select(marker => marker + Length));
            style = Style with { SoftPageMarkerOffsets = markers };
        }
        return new RichTextParagraph(Text + other.Text, style, Normalize(combined), MarkStyle);
    }

    private StyleRun[] SliceRuns(int start, int length)
    {
        int end = start + length;
        var result = new List<StyleRun>();
        int pos = 0;
        foreach (StyleRun run in Runs)
        {
            int s = Math.Max(pos, start);
            int e = Math.Min(pos + run.Length, end);
            if (e > s)
                result.Add(new StyleRun(e - s, run.Style));
            pos += run.Length;
        }

        return Normalize(result);
    }

    private static StyleRun[] Normalize(List<StyleRun> runs)
    {
        var result = new List<StyleRun>(runs.Count);
        foreach (StyleRun run in runs)
        {
            if (run.Length <= 0)
                continue;
            if (result.Count > 0 && result[^1].Style.Equals(run.Style))
                result[^1] = new StyleRun(result[^1].Length + run.Length, run.Style);
            else
                result.Add(run);
        }

        return [.. result];
    }
}
