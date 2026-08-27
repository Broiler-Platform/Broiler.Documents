using System;
using System.Collections.Generic;

namespace Broiler.Documents.Model;

/// <summary>
/// An immutable paragraph: its text, a paragraph style, and a normalized list of
/// inline style runs. Runs are contiguous, carry no zero-length entries, never
/// have equal-styled neighbors, and cover exactly <see cref="Text"/>. Every
/// mutating method returns a new instance (copy-on-write).
/// </summary>
public sealed class RichTextParagraph
{
    private RichTextParagraph(string text, ParagraphStyle style, IReadOnlyList<StyleRun> runs)
    {
        Text = text;
        Style = style;
        Runs = runs;
    }

    /// <summary>An empty paragraph with default styling.</summary>
    public static RichTextParagraph Empty { get; } =
        new(string.Empty, ParagraphStyle.Default, Array.Empty<StyleRun>());

    public string Text { get; }

    public ParagraphStyle Style { get; }

    /// <summary>The normalized inline runs, in order, covering <see cref="Text"/>.</summary>
    public IReadOnlyList<StyleRun> Runs { get; }

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
            ? Array.Empty<StyleRun>()
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

    public RichTextParagraph WithParagraphStyle(ParagraphStyle style) => new(Text, style, Runs);

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
        return new RichTextParagraph(newText, Style, Normalize(runs));
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

        return new RichTextParagraph(Text.Remove(start, length), Style, Normalize(runs));
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

        return new RichTextParagraph(Text, Style, Normalize(runs));
    }

    /// <summary>Splits into a head (before <paramref name="offset"/>) and tail (from it).</summary>
    public (RichTextParagraph Head, RichTextParagraph Tail) SplitAt(int offset)
    {
        offset = Math.Clamp(offset, 0, Text.Length);
        var head = new RichTextParagraph(Text[..offset], Style, SliceRuns(0, offset));
        var tail = new RichTextParagraph(Text[offset..], Style, SliceRuns(offset, Text.Length - offset));
        return (head, tail);
    }

    /// <summary>Concatenates <paramref name="other"/> onto this paragraph, keeping this paragraph's style.</summary>
    public RichTextParagraph Append(RichTextParagraph other)
    {
        var combined = new List<StyleRun>(Runs.Count + other.Runs.Count);
        combined.AddRange(Runs);
        combined.AddRange(other.Runs);
        return new RichTextParagraph(Text + other.Text, Style, Normalize(combined));
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

        return result.ToArray();
    }
}
