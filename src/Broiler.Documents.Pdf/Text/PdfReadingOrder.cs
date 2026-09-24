using System;
using System.Collections.Generic;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Pdf.Text;

/// <summary>A styled span within an assembled line.</summary>
internal sealed class PdfTextSpan(string text, InlineStyle style)
{
    public string Text { get; } = text;

    public InlineStyle Style { get; } = style;
}

/// <summary>One assembled line of text, with the geometry paragraph grouping needs.</summary>
internal sealed class PdfTextLine(List<PdfTextSpan> spans, double left, double right, double baseline, double height)
{
    public List<PdfTextSpan> Spans { get; } = spans;

    public double Left { get; } = left;

    public double Right { get; } = right;

    public double Baseline { get; } = baseline;

    /// <summary>The largest font size on the line, used as its nominal height.</summary>
    public double Height { get; } = height;

    /// <summary>
    /// The block of a tagged document's structure tree the line was declared
    /// in, or -1 where the order was not declared. Two lines in different blocks
    /// are two paragraphs, whatever their spacing says.
    /// </summary>
    public int Block { get; set; } = -1;

    /// <summary>
    /// About how wide the line's first word is, in points: the share of its
    /// first run's advance that word's letters take up. It is what decides
    /// whether the line before could have held it.
    /// </summary>
    public double FirstWordWidth { get; init; }

    /// <summary>The width of a space in the line's first run.</summary>
    public double SpaceWidth { get; init; }

    /// <summary>
    /// The size most of the line's letters are set in. A heading is its own
    /// size throughout; one larger word inside a line of text leaves the line at
    /// the text's size, which <see cref="Height"/> - the largest size on it -
    /// cannot say.
    /// </summary>
    public double DominantSize { get; init; }

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
/// A tagged document supplies the sequence instead of leaving it to be inferred,
/// and that path sits ahead of this one rather than inside it: see
/// <see cref="BuildLinesInDeclaredOrder"/>. This pass still runs for every page
/// the tree does not fully account for, and for the artifacts it never accounts
/// for anywhere.
/// </para>
/// </remarks>
internal static class PdfReadingOrder
{
    private const double GutterWidth = 24;
    private const double MinimumColumnShare = 0.15;

    /// <summary>
    /// How many of its lines a column below <see cref="MinimumColumnShare"/>
    /// has to set between the other columns' lines to be a column of its own.
    /// </summary>
    private const int MinimumLinesBetween = 2;

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
    /// A block is a marked-content item, and one line of text can be several of
    /// them: a producer that tags each line, and a link in the middle of one,
    /// hands over "(", the link and ") and more" as three items on one baseline.
    /// An item that carries on the line the one before it stopped on, just to
    /// its right, is that line's continuation and joins it. Each piece used to
    /// become a line - and then a paragraph - of its own.
    /// </para>
    /// <para>
    /// Column splitting is deliberately not run here. It exists to recover an
    /// order the page did not state; a page that states one has already answered
    /// it, and re-deriving it could only disagree.
    /// </para>
    /// <para>
    /// Artifacts are the exception, because the tree declares nothing about them
    /// — by specification it cannot. They are kept, because a running head is
    /// text the document draws and a reader expects to find, and they are placed
    /// geometrically: above the declared body if they sit above its topmost
    /// baseline, below it otherwise. That reproduces a header and a footer
    /// exactly, which is what almost every artifact is, and it never interleaves
    /// furniture into a sequence the document stated.
    /// </para>
    /// </remarks>
    public static List<PdfTextLine> BuildLinesInDeclaredOrder(
        IReadOnlyList<PdfTextFragment> fragments,
        IReadOnlyList<PdfLinkRegion> links,
        Func<PdfTextFragment, int> order,
        Func<PdfTextFragment, int>? block = null)
    {
        List<(PdfTextLine Line, int Order)> keyed = BuildKeyedLinesInDeclaredOrder(fragments, links, order, block);
        var lines = new List<PdfTextLine>(keyed.Count);
        foreach ((PdfTextLine line, _) in keyed)
            lines.Add(line);

        return lines;
    }

    /// <summary>
    /// <see cref="BuildLinesInDeclaredOrder"/>, with each line's place in the
    /// declared order: the position of the first item it was read from.
    /// Furniture above the body sorts before every position, and the rest of it
    /// after.
    /// </summary>
    /// <remarks>
    /// The key is what lets something that is not a line - a ruled table read
    /// out of the page's artwork - take its place among the lines where the
    /// document put its text, rather than wherever its top edge falls.
    /// </remarks>
    /// <param name="block">
    /// The block each run was declared in, where the tree says; each line
    /// carries the block of the item it started in.
    /// </param>
    public static List<(PdfTextLine Line, int Order)> BuildKeyedLinesInDeclaredOrder(
        IReadOnlyList<PdfTextFragment> fragments,
        IReadOnlyList<PdfLinkRegion> links,
        Func<PdfTextFragment, int> order,
        Func<PdfTextFragment, int>? block = null)
    {
        var lines = new List<(PdfTextLine Line, int Order)>();
        if (fragments.Count == 0)
            return lines;

        // One group per marked-content item, in declared order. Fragments inside
        // a group keep their own relative order for the geometric pass to sort.
        var groups = new SortedDictionary<int, List<PdfTextFragment>>();
        var artifacts = new List<PdfTextFragment>();
        double bodyTop = double.NegativeInfinity;

        foreach (PdfTextFragment fragment in fragments)
        {
            if (fragment.IsArtifact)
            {
                artifacts.Add(fragment);
                continue;
            }

            bodyTop = Math.Max(bodyTop, fragment.Y);

            int at = order(fragment);
            if (!groups.TryGetValue(at, out List<PdfTextFragment>? group))
            {
                group = [];
                groups[at] = group;
            }

            group.Add(fragment);
        }

        // Items that continue one line are read as that line, and the line
        // keeps the position, and the block, of the item it started in.
        List<PdfTextFragment>? current = null;
        int currentOrder = 0;
        int currentBlock = -1;
        foreach ((int at, List<PdfTextFragment> group) in groups)
        {
            if (current is not null && ContinuesLine(current, group))
            {
                current.AddRange(group);
                continue;
            }

            if (current is not null)
                AddKeyed(lines, BuildColumnLines(current, links), currentOrder, currentBlock);

            current = [.. group];
            currentOrder = at;
            currentBlock = block?.Invoke(group[0]) ?? -1;
        }

        if (current is not null)
            AddKeyed(lines, BuildColumnLines(current, links), currentOrder, currentBlock);

        if (artifacts.Count == 0)
            return lines;

        // y increases upward, so an artifact above the body's topmost baseline is
        // a running head and everything else — folios, footers, furniture level
        // with the text — reads after it.
        var above = new List<PdfTextFragment>();
        var below = new List<PdfTextFragment>();
        foreach (PdfTextFragment fragment in artifacts)
            (fragment.Y > bodyTop ? above : below).Add(fragment);

        var placed = new List<(PdfTextLine Line, int Order)>(lines.Count + artifacts.Count);
        AddKeyed(placed, BuildLines(above, links), int.MinValue);
        placed.AddRange(lines);
        AddKeyed(placed, BuildLines(below, links), int.MaxValue);
        return placed;
    }

    private static void AddKeyed(List<(PdfTextLine Line, int Order)> keyed, List<PdfTextLine> lines, int order, int block = -1)
    {
        foreach (PdfTextLine line in lines)
        {
            line.Block = block;
            keyed.Add((line, order));
        }
    }

    /// <summary>
    /// Whether the next marked-content item carries on the line the current one
    /// stopped on: it starts on the same baseline, to the right of where the
    /// line stopped and no further off than a wide word space.
    /// </summary>
    /// <remarks>
    /// The limit is what keeps two cells of a borderless table, which also sit
    /// side by side on one baseline and follow one another in declared order,
    /// from being run together. Their gap is a gutter, not a word space.
    /// </remarks>
    private static bool ContinuesLine(List<PdfTextFragment> current, List<PdfTextFragment> next)
    {
        // Where the current item stopped: the right end of its lowest line.
        PdfTextFragment last = current[0];
        foreach (PdfTextFragment fragment in current)
        {
            double tolerance = Math.Max(1.0, Math.Max(fragment.FontSize, last.FontSize) * 0.35);
            if (fragment.Y < last.Y - tolerance ||
                (Math.Abs(fragment.Y - last.Y) <= tolerance && fragment.EndX > last.EndX))
            {
                last = fragment;
            }
        }

        // Where the next one starts: the left end of its highest line.
        PdfTextFragment first = next[0];
        foreach (PdfTextFragment fragment in next)
        {
            double tolerance = Math.Max(1.0, Math.Max(fragment.FontSize, first.FontSize) * 0.35);
            if (fragment.Y > first.Y + tolerance ||
                (Math.Abs(fragment.Y - first.Y) <= tolerance && fragment.X < first.X))
            {
                first = fragment;
            }
        }

        double size = Math.Max(last.FontSize, first.FontSize);
        if (Math.Abs(first.Y - last.Y) > Math.Max(1.0, size * 0.35))
            return false;

        double gap = first.X - last.EndX;
        return gap >= -Math.Max(last.SpaceWidth, first.SpaceWidth) && gap <= size;
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
        // One whose lines keep their own spacing beside the text is a column
        // however little it holds.
        int threshold = (int)Math.Ceiling(fragments.Count * MinimumColumnShare);
        bool[] ownLines = ColumnsWithOwnLines(columns, threshold);
        var kept = new List<List<PdfTextFragment>>();
        for (int i = 0; i < columns.Count; i++)
        {
            if (columns[i].Count >= threshold || ownLines[i])
                kept.Add(columns[i]);
        }

        if (kept.Count < 2)
        {
            single.Add([.. fragments]);
            return single;
        }

        // Anything filtered out still belongs somewhere: put it in the nearest kept column.
        for (int i = 0; i < columns.Count; i++)
        {
            if (columns[i].Count >= threshold || ownLines[i] || columns[i].Count == 0)
                continue;
            kept[0].AddRange(columns[i]);
        }

        return kept;
    }

    /// <summary>
    /// Which of the columns too slight to count by their share of the page set
    /// lines of their own beside the other columns' text: at least
    /// <see cref="MinimumLinesBetween"/> of those lines fall between the others'
    /// lines rather than level with one, and they are no fewer than a third of
    /// the lines the column sets beside that text at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The share is counted in runs, and a run is whatever the producer made it.
    /// One that draws every glyph on its own hands over a run per letter, and a
    /// narrow panel of a few short lines beside a column of small print - a
    /// voucher's value, code and greeting beside its instructions - held a few
    /// percent of them. It was merged back as a stray element and its lines
    /// were read in among the other column's by height: between them, and into
    /// the middle of a sentence wherever two stood level.
    /// </para>
    /// <para>
    /// What the merge is for still merges. A running head and a folio sit above
    /// or below the text rather than beside it, so none of their lines counts.
    /// A table's narrow column sets its values level with the rows they belong
    /// to, and line numbers stand level with the lines they number; merged, each
    /// is read across with its row or its line, which is the reading they have.
    /// A panel's lines keep a spacing of their own, and it is lines between the
    /// others' - not a share of the runs - that say a column was set there.
    /// </para>
    /// <para>
    /// Two lines stand level where they are within the reach
    /// <see cref="BuildColumnLines"/> gives a baseline, measured at the smaller
    /// of their two sizes. Every column's lines go into one list by height, once,
    /// and each is compared with the nearest line of another column above it and
    /// below it, so the cost is the sort's however many gutters a page has.
    /// </para>
    /// </remarks>
    private static bool[] ColumnsWithOwnLines(List<List<PdfTextFragment>> columns, int threshold)
    {
        var own = new bool[columns.Count];

        bool slight = false;
        foreach (List<PdfTextFragment> column in columns)
            slight |= column.Count > 0 && column.Count < threshold;

        if (!slight)
            return own;

        // Each column's lines as the line pass forms them: a run within reach of
        // the baseline above it is on that line.
        var lines = new List<ColumnLine>();
        for (int column = 0; column < columns.Count; column++)
        {
            var sorted = new List<PdfTextFragment>(columns[column]);
            sorted.Sort(static (left, right) => right.Y.CompareTo(left.Y));

            double baseline = double.NaN;
            foreach (PdfTextFragment fragment in sorted)
            {
                if (!double.IsNaN(baseline) && Math.Abs(fragment.Y - baseline) <= LineReach(fragment.FontSize))
                    continue;

                baseline = fragment.Y;
                lines.Add(new ColumnLine(fragment.Y, fragment.FontSize, column));
            }
        }

        lines.Sort(static (left, right) => left.Y.CompareTo(right.Y));
        int count = lines.Count;

        // The nearest line of another column below and above each line. Lines of
        // one column that follow each other share the answer, so a sweep each
        // way finds every one.
        var below = new int[count];
        var above = new int[count];
        for (int i = 0; i < count; i++)
            below[i] = i == 0 ? -1 : lines[i - 1].Column != lines[i].Column ? i - 1 : below[i - 1];
        for (int i = count - 1; i >= 0; i--)
            above[i] = i == count - 1 ? -1 : lines[i + 1].Column != lines[i].Column ? i + 1 : above[i + 1];

        var level = new int[columns.Count];
        var between = new int[columns.Count];
        for (int i = 0; i < count; i++)
        {
            ColumnLine line = lines[i];
            if (columns[line.Column].Count >= threshold)
                continue;

            // The other columns' lowest and highest lines: the ends of the list,
            // or the nearest line past an end that this column holds.
            int lowest = lines[0].Column != line.Column ? 0 : above[0];
            int highest = lines[count - 1].Column != line.Column ? count - 1 : below[count - 1];
            if (lowest < 0 || highest < 0)
                continue;

            // Above or below all of their text, the line is furniture - a head
            // or a folio - and says nothing about a column.
            double reach = LineReach(line.Size);
            if (line.Y > lines[highest].Y + reach || line.Y < lines[lowest].Y - reach)
                continue;

            if (StandsLevel(lines, i, below[i]) || StandsLevel(lines, i, above[i]))
                level[line.Column]++;
            else
                between[line.Column]++;
        }

        for (int column = 0; column < columns.Count; column++)
            own[column] = between[column] >= MinimumLinesBetween && between[column] * 2 >= level[column];

        return own;
    }

    /// <summary>Whether two lines sit on one baseline, within the smaller size's reach.</summary>
    private static bool StandsLevel(List<ColumnLine> lines, int line, int other) =>
        other >= 0 &&
        Math.Abs(lines[other].Y - lines[line].Y) <= LineReach(Math.Min(lines[line].Size, lines[other].Size));

    /// <summary>
    /// How far from a baseline a run set at this size may sit and still be on
    /// that line: the reach <see cref="BuildColumnLines"/> joins runs within.
    /// </summary>
    private static double LineReach(double size) => Math.Max(1.0, size * 0.35);

    /// <summary>One line of one column: its baseline, the size of its first run, and the column.</summary>
    private readonly record struct ColumnLine(double Y, double Size, int Column);

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
        double inkLeft = double.MaxValue;
        double inkRight = double.MinValue;
        double height = 0;
        double previousEnd = double.NaN;
        double previousSpaceWidth = 0;

        foreach (PdfTextFragment fragment in fragments)
        {
            left = Math.Min(left, fragment.X);
            right = Math.Max(right, fragment.EndX);
            height = Math.Max(height, fragment.FontSize);

            // Where the line's ink is. A run of spaces paints nothing, and a
            // title set after three spaces at 48 points would otherwise start at
            // the page edge - and take the left edge of every line stacked under
            // it with it.
            if (!string.IsNullOrWhiteSpace(fragment.Text))
            {
                inkLeft = Math.Min(inkLeft, fragment.X);
                inkRight = Math.Max(inkRight, fragment.EndX);
            }

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

        if (inkLeft <= inkRight)
        {
            left = inkLeft;
            right = inkRight;
        }

        (double firstWord, double space) = FirstWord(fragments);

        return new PdfTextLine(
            Merge(spans),
            double.IsFinite(left) ? left : 0,
            double.IsFinite(right) ? right : 0,
            fragments[0].Y,
            height)
        {
            FirstWordWidth = firstWord,
            SpaceWidth = space,
            DominantSize = DominantSizeOf(fragments, height),
        };
    }

    /// <summary>
    /// The size that sets the most letters on a line - the larger of two that
    /// set as many - or <paramref name="fallback"/> on a line of spaces only.
    /// </summary>
    private static double DominantSizeOf(List<PdfTextFragment> fragments, double fallback)
    {
        var letters = new Dictionary<double, int>();
        foreach (PdfTextFragment fragment in fragments)
        {
            int count = 0;
            foreach (char character in fragment.Text)
            {
                if (!char.IsWhiteSpace(character))
                    count++;
            }

            if (count == 0)
                continue;

            // Rounded as a run's style is, so one size is one entry.
            double size = Math.Round(fragment.FontSize, 2);
            letters[size] = letters.GetValueOrDefault(size) + count;
        }

        double dominant = fallback;
        int most = 0;
        foreach ((double size, int count) in letters)
        {
            if (count > most || (count == most && size > dominant))
            {
                dominant = size;
                most = count;
            }
        }

        return dominant;
    }

    /// <summary>
    /// The first word's width and the width of a space where it starts. A
    /// word's letters are measured as their share of the run's advance, and a
    /// word runs on into the next run where the two abut - "(" set apart from
    /// the address it opens is still one word with it.
    /// </summary>
    private static (double Width, double Space) FirstWord(List<PdfTextFragment> fragments)
    {
        double start = double.NaN;
        double space = 0;

        for (int i = 0; i < fragments.Count; i++)
        {
            PdfTextFragment fragment = fragments[i];
            string text = fragment.Text;
            double advance = Math.Max(0, fragment.EndX - fragment.X);
            int index = 0;

            if (double.IsNaN(start))
            {
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                    index++;
                if (index == text.Length)
                    continue;

                start = fragment.X + (text.Length == 0 ? 0 : advance * index / text.Length);
                space = fragment.SpaceWidth;
            }

            while (index < text.Length && !char.IsWhiteSpace(text[index]))
                index++;

            // The word ends inside this run, or where the run ends and a gap
            // wide enough to be a word space opens before the next one.
            if (index < text.Length)
                return (fragment.X + (advance * index / text.Length) - start, space);

            if (i + 1 == fragments.Count ||
                fragments[i + 1].X - fragment.EndX > Math.Max(fragment.SpaceWidth, fragments[i + 1].SpaceWidth) * 0.25 ||
                (fragments[i + 1].Text.Length > 0 && char.IsWhiteSpace(fragments[i + 1].Text[0])))
            {
                return (fragment.EndX - start, space);
            }
        }

        return (0, space);
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
            Underline = fragment.Underline,
            Strikethrough = fragment.Strikethrough,
            // Black is the initial fill colour and carries no authorial intent, so
            // it stays the model's "no explicit colour" rather than an explicit one.
            Foreground = fragment.Color == BColor.Black ? BColor.Empty : fragment.Color,
            Background = fragment.Background,
            LinkHref = href,
        };
    }
}
