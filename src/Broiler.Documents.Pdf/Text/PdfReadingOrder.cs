using System;
using System.Collections.Generic;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Graphics;

namespace Broiler.Documents.Pdf.Text;

/// <summary>A styled span within an assembled line.</summary>
internal sealed class PdfTextSpan
{
    public PdfTextSpan(string text, InlineStyle style)
    {
        Text = text;
        Style = style;
    }

    public string Text { get; }

    public InlineStyle Style { get; }
}

/// <summary>One assembled line of text, with the geometry paragraph grouping needs.</summary>
internal sealed class PdfTextLine
{
    public PdfTextLine(List<PdfTextSpan> spans, double left, double right, double baseline, double height)
    {
        Spans = spans;
        Left = left;
        Right = right;
        Baseline = baseline;
        Height = height;
    }

    public List<PdfTextSpan> Spans { get; }

    public double Left { get; }

    public double Right { get; }

    public double Baseline { get; }

    /// <summary>The largest font size on the line, used as its nominal height.</summary>
    public double Height { get; }

    public string Text
    {
        get
        {
            var builder = new StringBuilder();
            foreach (PdfTextSpan span in Spans)
                builder.Append(span.Text);
            return builder.ToString();
        }
    }

    public bool IsBlank => Text.Trim().Length == 0;
}

/// <summary>
/// Turns placed text runs into lines and blocks.
/// </summary>
/// <remarks>
/// <para>
/// A PDF says where glyphs are, not what they mean, so reading order here is a
/// geometric inference. It is a documented one: fragments are grouped into
/// columns by vertical gutters, into lines by shared baselines, and into
/// paragraphs by vertical spacing and indentation. Every document that goes
/// through this path is reported with
/// <see cref="PdfDiagnosticCodes.ReadingOrderHeuristic"/>, because geometry —
/// not trustworthy logical structure — determined the order.
/// </para>
/// <para>
/// Tagged PDF would supply real logical structure and is out of scope for this
/// release; when it arrives it belongs ahead of this pass, not inside it.
/// </para>
/// </remarks>
internal static class PdfReadingOrder
{
    private const double GutterWidth = 24;
    private const double MinimumColumnShare = 0.15;

    /// <summary>Assembles a page's fragments into lines in reading order.</summary>
    public static List<PdfTextLine> BuildLines(
        IReadOnlyList<PdfTextFragment> fragments,
        IReadOnlyList<PdfLinkRegion> links)
    {
        var lines = new List<PdfTextLine>();
        if (fragments.Count == 0)
            return lines;

        foreach (List<PdfTextFragment> column in SplitColumns(fragments))
            lines.AddRange(BuildColumnLines(column, links));

        return lines;
    }

    /// <summary>
    /// Assembles lines in the order a tagged document declares, rather than the
    /// order its geometry implies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the sequence of blocks comes from the structure tree. Within a block
    /// the geometric pass still runs, because the order of glyphs on a baseline
    /// is a geometric fact and no tagging changes it — what tagging settles is
    /// which block follows which, the question a gutter histogram can only guess
    /// at and gets wrong on a sidebar, a pull quote, or a table.
    /// </para>
    /// <para>
    /// Column splitting is deliberately not run here. It exists to recover an
    /// order the page did not state; a page that states one has already answered
    /// it, and re-deriving it could only disagree.
    /// </para>
    /// </remarks>
    public static List<PdfTextLine> BuildLinesInDeclaredOrder(
        IReadOnlyList<PdfTextFragment> fragments,
        IReadOnlyList<PdfLinkRegion> links,
        Func<PdfTextFragment, int> order)
    {
        var lines = new List<PdfTextLine>();
        if (fragments.Count == 0)
            return lines;

        // One group per marked-content item, in declared order. Fragments inside
        // a group keep their own relative order for the geometric pass to sort.
        var groups = new SortedDictionary<int, List<PdfTextFragment>>();
        foreach (PdfTextFragment fragment in fragments)
        {
            int at = order(fragment);
            if (!groups.TryGetValue(at, out List<PdfTextFragment>? group))
            {
                group = [];
                groups[at] = group;
            }

            group.Add(fragment);
        }

        foreach (List<PdfTextFragment> group in groups.Values)
            lines.AddRange(BuildColumnLines(group, links));

        return lines;
    }

    /// <summary>
    /// Splits fragments into columns separated by a clear vertical gutter. A page
    /// with no such gutter yields one column, which is the common case and costs
    /// one histogram pass.
    /// </summary>
    private static List<List<PdfTextFragment>> SplitColumns(IReadOnlyList<PdfTextFragment> fragments)
    {
        var single = new List<List<PdfTextFragment>>();
        if (fragments.Count < 20)
        {
            single.Add([.. fragments]);
            return single;
        }

        double minX = double.MaxValue;
        double maxX = double.MinValue;
        foreach (PdfTextFragment fragment in fragments)
        {
            minX = Math.Min(minX, fragment.X);
            maxX = Math.Max(maxX, fragment.EndX);
        }

        if (!double.IsFinite(minX) || !double.IsFinite(maxX) || maxX - minX < GutterWidth * 3)
        {
            single.Add([.. fragments]);
            return single;
        }

        int binCount = (int)Math.Ceiling(maxX - minX) + 1;
        if (binCount is <= 0 or > 20000)
        {
            single.Add([.. fragments]);
            return single;
        }

        var occupied = new bool[binCount];
        foreach (PdfTextFragment fragment in fragments)
        {
            int start = (int)Math.Floor(fragment.X - minX);
            int end = (int)Math.Ceiling(fragment.EndX - minX);
            for (int i = Math.Max(0, start); i < Math.Min(binCount, Math.Max(end, start + 1)); i++)
                occupied[i] = true;
        }

        // Find the boundaries: empty runs at least a gutter wide, ignoring the
        // margins at either end.
        var boundaries = new List<double>();
        int emptyRun = 0;
        for (int i = 0; i < binCount; i++)
        {
            if (!occupied[i])
            {
                emptyRun++;
                continue;
            }

            if (emptyRun >= GutterWidth && i - emptyRun > 0)
                boundaries.Add(minX + i - (emptyRun / 2.0));
            emptyRun = 0;
        }

        if (boundaries.Count == 0)
        {
            single.Add([.. fragments]);
            return single;
        }

        var columns = new List<List<PdfTextFragment>>();
        for (int i = 0; i <= boundaries.Count; i++)
            columns.Add([]);

        foreach (PdfTextFragment fragment in fragments)
        {
            double centre = (fragment.X + fragment.EndX) / 2;
            int index = 0;
            while (index < boundaries.Count && centre > boundaries[index])
                index++;
            columns[index].Add(fragment);
        }

        // A "column" holding almost nothing is a stray element, not a column;
        // merging it back avoids inventing a reading order for a page header.
        int threshold = (int)Math.Ceiling(fragments.Count * MinimumColumnShare);
        var kept = new List<List<PdfTextFragment>>();
        foreach (List<PdfTextFragment> column in columns)
        {
            if (column.Count >= threshold)
                kept.Add(column);
        }

        if (kept.Count < 2)
        {
            single.Add([.. fragments]);
            return single;
        }

        // Anything filtered out still belongs somewhere: put it in the nearest kept column.
        foreach (List<PdfTextFragment> column in columns)
        {
            if (column.Count >= threshold || column.Count == 0)
                continue;
            kept[0].AddRange(column);
        }

        return kept;
    }

    private static List<PdfTextLine> BuildColumnLines(
        List<PdfTextFragment> fragments,
        IReadOnlyList<PdfLinkRegion> links)
    {
        var lines = new List<PdfTextLine>();
        if (fragments.Count == 0)
            return lines;

        // Top to bottom, then left to right. Sorting on the baseline alone would
        // interleave superscripts, so ties break on x.
        fragments.Sort(static (left, right) =>
        {
            int byBaseline = right.Y.CompareTo(left.Y);
            return byBaseline != 0 ? byBaseline : left.X.CompareTo(right.X);
        });

        var current = new List<PdfTextFragment>();
        double currentBaseline = fragments[0].Y;

        foreach (PdfTextFragment fragment in fragments)
        {
            double tolerance = Math.Max(1.0, fragment.FontSize * 0.35);
            if (current.Count > 0 && Math.Abs(fragment.Y - currentBaseline) > tolerance)
            {
                lines.Add(Assemble(current, links));
                current = [];
            }

            if (current.Count == 0)
                currentBaseline = fragment.Y;
            current.Add(fragment);
        }

        if (current.Count > 0)
            lines.Add(Assemble(current, links));

        return lines;
    }

    private static PdfTextLine Assemble(List<PdfTextFragment> fragments, IReadOnlyList<PdfLinkRegion> links)
    {
        fragments.Sort(static (left, right) => left.X.CompareTo(right.X));

        var spans = new List<PdfTextSpan>();
        double left = double.MaxValue;
        double right = double.MinValue;
        double height = 0;
        double previousEnd = double.NaN;
        double previousSpaceWidth = 0;

        foreach (PdfTextFragment fragment in fragments)
        {
            left = Math.Min(left, fragment.X);
            right = Math.Max(right, fragment.EndX);
            height = Math.Max(height, fragment.FontSize);

            string text = fragment.Text;
            if (!double.IsNaN(previousEnd))
            {
                // The interpreter breaks a run when the pen jumps; a jump wider
                // than a quarter of a space is where a word boundary belongs.
                double gap = fragment.X - previousEnd;
                double reference = Math.Max(previousSpaceWidth, fragment.SpaceWidth);
                if (gap > reference * 0.25 && !text.StartsWith(' ') && spans.Count > 0 && !spans[^1].Text.EndsWith(' '))
                    text = " " + text;
            }

            spans.Add(new PdfTextSpan(text, StyleFor(fragment, links)));
            previousEnd = fragment.EndX;
            previousSpaceWidth = fragment.SpaceWidth;
        }

        return new PdfTextLine(
            Merge(spans),
            double.IsFinite(left) ? left : 0,
            double.IsFinite(right) ? right : 0,
            fragments[0].Y,
            height);
    }

    // Adjacent spans that agree on style become one, so the model gets the
    // minimal set of runs rather than one per show-text operator.
    private static List<PdfTextSpan> Merge(List<PdfTextSpan> spans)
    {
        var merged = new List<PdfTextSpan>(spans.Count);
        foreach (PdfTextSpan span in spans)
        {
            if (merged.Count > 0 && merged[^1].Style.Equals(span.Style))
            {
                merged[^1] = new PdfTextSpan(merged[^1].Text + span.Text, span.Style);
                continue;
            }

            merged.Add(span);
        }

        return merged;
    }

    private static InlineStyle StyleFor(PdfTextFragment fragment, IReadOnlyList<PdfLinkRegion> links)
    {
        string? href = null;
        double midX = (fragment.X + fragment.EndX) / 2;
        foreach (PdfLinkRegion region in links)
        {
            if (region.Contains(midX, fragment.Y))
            {
                href = region.Href;
                break;
            }
        }

        return new InlineStyle
        {
            FontFamily = string.IsNullOrEmpty(fragment.FontFamily) ? null : fragment.FontFamily,
            FontSize = fragment.FontSize > 0 ? (float)Math.Round(fragment.FontSize, 2) : null,
            Bold = fragment.Bold,
            Italic = fragment.Italic,
            // Black is the initial fill colour and carries no authorial intent, so
            // it stays the model's "no explicit colour" rather than an explicit one.
            Foreground = fragment.Color == BColor.Black ? BColor.Empty : fragment.Color,
            LinkHref = href,
        };
    }
}
