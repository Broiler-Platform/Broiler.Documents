using System;
using System.Collections.Generic;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Pdf.Text;

/// <summary>
/// One painted path, reduced to the box it covered and how it was painted.
/// </summary>
/// <remarks>
/// The interpreter classifies a path and then drops it. This keeps the geometry
/// of the two classes that can still mean something — a thin bar and an
/// axis-aligned area — because a table's rules and cell shades are exactly those
/// two, and a grid of them is the one arrangement a logical model can carry.
/// Coordinates are the same device space text fragments use, so a rule and a
/// line of text can be compared without transforming either again.
/// </remarks>
internal readonly record struct PdfPaintedPath(
    double MinX,
    double MinY,
    double MaxX,
    double MaxY,
    PdfArtworkKind Kind,
    bool Filled,
    BColor Color)
{
    public double Width => MaxX - MinX;

    public double Height => MaxY - MinY;
}

/// <summary>
/// A lattice of rules found on one page: the grid a ruled table was drawn as.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Fully ruled only.</strong> A grid is claimed when every edge of every
/// cell was actually painted — the outer boundary and each interior segment. A
/// table that omits some of its rules is not reconstructed, and is reported as
/// dropped artwork exactly as before.
/// </para>
/// <para>
/// That is deliberate and it is the same trade the structure-tree pass makes: a
/// partial answer is worse than an honest heuristic. Column alignment alone
/// would find more tables, and would also turn every two-column page, every
/// figure caption beside an image, and every run of tab-aligned text into a
/// table that the document never drew. A complete lattice is a statement the
/// document made in ink; alignment is an inference about intent.
/// </para>
/// <para>
/// Two columns and two rows are the minimum. A single column of ruled bands is a
/// list of boxes as often as it is a table, and a single row is a header bar; at
/// that size the lattice stops being evidence.
/// </para>
/// </remarks>
internal sealed class PdfTableGrid
{
    /// <summary>How far apart two coordinates may be and still be the same edge.</summary>
    private const double EdgeTolerance = 2.5;

    /// <summary>Slack allowed at each end when asking whether a rule covers a span.</summary>
    private const double CoverTolerance = 2.5;

    /// <summary>Smallest grid worth claiming, in columns and in rows.</summary>
    private const int MinimumCells = 2;

    /// <summary>Most lattice lines considered on one page, in each direction.</summary>
    private const int MaxEdges = 256;

    /// <summary>Parallel rules a region must stack before its divisions may be inferred.</summary>
    private const int MinimumAnchorRules = 3;

    /// <summary>How much of their length stacked rules must share to be one table's.</summary>
    private const double MinimumOverlapShare = 0.6;

    /// <summary>Narrowest region worth reading a grid out of, in points.</summary>
    private const double MinimumSpan = 24;

    /// <summary>How far apart two baselines may be and still be one line of a row.</summary>
    private const double BaselineTolerance = 2.0;

    /// <summary>Narrowest empty lane that may be read as a column boundary, in points.</summary>
    private const int MinimumCorridor = 4;

    /// <summary>Widest region the occupancy scan will bin, in points.</summary>
    private const int MaxBins = 20_000;

    private readonly double[] _columns;
    private readonly double[] _rows;
    private readonly BColor[] _shading;
    private readonly CellBorders[] _borders;

    private PdfTableGrid(
        double[] columns,
        double[] rows,
        BColor[] shading,
        CellBorders[] borders,
        bool inferred)
    {
        _columns = columns;
        _rows = rows;
        _shading = shading;
        _borders = borders;
        IsInferred = inferred;
    }

    /// <summary>
    /// True where some of the grid's divisions were read off the text rather
    /// than off a painted rule. A reader that wants only what the document drew
    /// can tell the two apart, and the diagnostic reports them separately.
    /// </summary>
    public bool IsInferred { get; }

    /// <summary>Column boundaries, left to right. One more than <see cref="Columns"/>.</summary>
    public IReadOnlyList<double> ColumnEdges => _columns;

    /// <summary>Row boundaries, top to bottom. One more than <see cref="Rows"/>.</summary>
    public IReadOnlyList<double> RowEdges => _rows;

    public int Columns => _columns.Length - 1;

    public int Rows => _rows.Length - 1;

    public double Left => _columns[0];

    public double Right => _columns[^1];

    /// <summary>The top edge. Device space has y increasing upward, so this is the largest.</summary>
    public double Top => _rows[0];

    public double Bottom => _rows[^1];

    public BColor ShadingAt(int row, int column) => _shading[(row * Columns) + column];

    public CellBorders BordersAt(int row, int column) => _borders[(row * Columns) + column];

    /// <summary>The width of each column, for the model's grid.</summary>
    public List<double> ColumnWidths()
    {
        var widths = new List<double>(Columns);
        for (int i = 0; i < Columns; i++)
            widths.Add(_columns[i + 1] - _columns[i]);
        return widths;
    }

    /// <summary>
    /// Which cell a point falls in, or null where it falls outside the grid. A
    /// point exactly on an edge belongs to the cell below and to the right of it,
    /// which is where a glyph sitting on a rule was drawn.
    /// </summary>
    public (int Row, int Column)? CellAt(double x, double y)
    {
        if (x < Left - EdgeTolerance || x > Right + EdgeTolerance) return null;
        if (y > Top + EdgeTolerance || y < Bottom - EdgeTolerance) return null;

        int column = Band(_columns, x, ascending: true);
        int row = Band(_rows, y, ascending: false);
        return column < 0 || row < 0 ? null : (row, column);
    }

    private static int Band(double[] edges, double value, bool ascending)
    {
        for (int i = 0; i < edges.Length - 1; i++)
        {
            double low = ascending ? edges[i] : edges[i + 1];
            double high = ascending ? edges[i + 1] : edges[i];
            if (value >= low - EdgeTolerance && value <= high + EdgeTolerance)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Finds the grid a page drew, if it drew one. A fully ruled lattice is
    /// preferred; where the rules only partly divide the table, the rest is read
    /// off the text under the conditions <see cref="Infer"/> sets out.
    /// </summary>
    public static List<PdfTableGrid> Detect(
        IReadOnlyList<PdfPaintedPath> paths,
        IReadOnlyList<PdfTextFragment> fragments)
    {
        var grids = new List<PdfTableGrid>();
        if (paths is null || paths.Count == 0)
            return grids;

        var vertical = new List<Segment>();
        var horizontal = new List<Segment>();
        var fills = new List<PdfPaintedPath>();
        Collect(paths, vertical, horizontal, fills);

        if (horizontal.Count < 2 && vertical.Count < 2)
            return grids;

        PdfTableGrid? grid = Lattice(vertical, horizontal, fills)
            ?? Infer(vertical, horizontal, fills, fragments);

        if (grid is not null)
            grids.Add(grid);

        return grids;
    }

    /// <summary>
    /// The fully ruled case: every edge of every cell was painted.
    /// </summary>
    /// <remarks>
    /// One lattice per page is the case that matters and the case that is safe:
    /// two tables side by side share no edges, so a single lattice over all of
    /// them would claim cells neither drew. Candidate edges are taken from the
    /// whole page and the lattice is then required to be complete, which a pair
    /// of separate tables fails.
    /// </remarks>
    private static PdfTableGrid? Lattice(
        List<Segment> vertical,
        List<Segment> horizontal,
        List<PdfPaintedPath> fills)
    {
        if (vertical.Count < 2 || horizontal.Count < 2)
            return null;

        double[] columns = Cluster(vertical, s => s.At);
        double[] rows = Cluster(horizontal, s => s.At);
        Array.Reverse(rows);

        if (columns.Length - 1 < MinimumCells || rows.Length - 1 < MinimumCells)
            return null;

        return IsComplete(columns, rows, vertical, horizontal)
            ? Build(columns, rows, vertical, horizontal, fills, inferred: false)
            : null;
    }

    /// <summary>Fills in a grid's borders and shading from what was painted.</summary>
    private static PdfTableGrid Build(
        double[] columns,
        double[] rows,
        List<Segment> vertical,
        List<Segment> horizontal,
        List<PdfPaintedPath> fills,
        bool inferred)
    {
        int cells = (columns.Length - 1) * (rows.Length - 1);
        var shading = new BColor[cells];
        var borders = new CellBorders[cells];
        var grid = new PdfTableGrid(columns, rows, shading, borders, inferred);

        for (int row = 0; row < grid.Rows; row++)
        {
            for (int column = 0; column < grid.Columns; column++)
            {
                double left = columns[column];
                double right = columns[column + 1];
                double top = rows[row];
                double bottom = rows[row + 1];

                // An unruled edge gets no border, which is what the page shows.
                // Inferring a division is not the same as inventing a line.
                borders[(row * grid.Columns) + column] = new CellBorders(
                    Edge(vertical, left, bottom, top),
                    Edge(horizontal, top, left, right),
                    Edge(vertical, right, bottom, top),
                    Edge(horizontal, bottom, left, right));

                shading[(row * grid.Columns) + column] =
                    ShadeOf(fills, left, bottom, right, top, grid);
            }
        }

        return grid;
    }

    /// <summary>
    /// The partly ruled case: the document drew enough rules to say where a
    /// table is and how it divides in one direction, and the other direction is
    /// read off the text inside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>What makes this safe is the anchor, not the inference.</strong>
    /// Alignment alone finds tables everywhere - every two-column page, every
    /// caption beside a figure, every tab-aligned list - which is why the fully
    /// ruled path refuses to use it. What is required here instead is a stack of
    /// at least three parallel rules that overlap each other along their length,
    /// which puts at least one of them strictly inside the region. That is the
    /// visual signature of a table and it is drawn in ink: a rule above and
    /// below an article is two, a box around a callout is a rectangle, and
    /// neither becomes a table. Only once the document has divided a region does
    /// this read the divisions it left out.
    /// </para>
    /// <para>
    /// Inferred divisions carry no borders. An edge gets a border where a rule
    /// was painted along it and none otherwise, so a header-ruled table comes
    /// back with the rule under its header and nothing between its columns,
    /// which is what the page shows. Inferring where a column starts is not the
    /// same as inventing a line down it.
    /// </para>
    /// <para>
    /// The residual risk is honest rather than eliminated: a page that stacks
    /// three full-width rules across ordinary two-column prose can still be read
    /// as a table. <see cref="IsInferred"/> and a diagnostic of its own say
    /// which grids were arrived at this way, so a host that wants only what was
    /// drawn can tell them apart.
    /// </para>
    /// </remarks>
    private static PdfTableGrid? Infer(
        List<Segment> vertical,
        List<Segment> horizontal,
        List<PdfPaintedPath> fills,
        IReadOnlyList<PdfTextFragment> fragments)
    {
        if (fragments is null || fragments.Count == 0)
            return null;

        double[] bands = Stack(horizontal);
        if (bands.Length < MinimumAnchorRules)
            return null;

        Array.Reverse(bands);
        double top = bands[0];
        double bottom = bands[^1];

        (double left, double right) = Extent(horizontal, bands);
        if (right - left < MinimumSpan)
            return null;

        var inside = new List<PdfTextFragment>();
        foreach (PdfTextFragment fragment in fragments)
        {
            if (fragment.Y <= top + EdgeTolerance && fragment.Y >= bottom - EdgeTolerance &&
                fragment.EndX >= left - EdgeTolerance && fragment.X <= right + EdgeTolerance)
            {
                inside.Add(fragment);
            }
        }

        if (inside.Count == 0)
            return null;

        double[] rows = InferRows(bands, inside);
        double[] columns = InferColumns(vertical, inside, left, right, top, bottom);

        return rows.Length - 1 < MinimumCells || columns.Length - 1 < MinimumCells
            ? null
            : Build(columns, rows, vertical, horizontal, fills, inferred: true);
    }

    /// <summary>
    /// The largest set of parallel rules that all overlap one another along
    /// their length, as their clustered positions. Overlap is what separates a
    /// stack of dividers from rules that merely share a page.
    /// </summary>
    private static double[] Stack(List<Segment> segments)
    {
        if (segments.Count < MinimumAnchorRules)
            return [];

        double[] positions = Cluster(segments, s => s.At);
        if (positions.Length < MinimumAnchorRules)
            return [];

        // The common run every rule in the stack covers, against the full run
        // they cover between them. Rules that barely meet are not a table's.
        (double from, double to) = Overlap(segments, positions);
        (double left, double right) = Extent(segments, positions);
        double span = right - left;

        return span > 0 && (to - from) >= span * MinimumOverlapShare ? positions : [];
    }

    /// <summary>The run shared by every rule sitting on one of these positions.</summary>
    private static (double From, double To) Overlap(List<Segment> segments, double[] positions)
    {
        double from = double.MinValue;
        double to = double.MaxValue;

        foreach (double at in positions)
        {
            double widest = double.MaxValue;
            double narrowest = double.MinValue;
            foreach (Segment segment in segments)
            {
                if (Math.Abs(segment.At - at) > EdgeTolerance)
                    continue;

                widest = Math.Min(widest, segment.From);
                narrowest = Math.Max(narrowest, segment.To);
            }

            if (widest == double.MaxValue)
                continue;

            from = Math.Max(from, widest);
            to = Math.Min(to, narrowest);
        }

        return (from, to);
    }

    /// <summary>The full run the rules on these positions cover between them.</summary>
    private static (double Left, double Right) Extent(List<Segment> segments, double[] positions)
    {
        double left = double.MaxValue;
        double right = double.MinValue;

        foreach (double at in positions)
        {
            foreach (Segment segment in segments)
            {
                if (Math.Abs(segment.At - at) > EdgeTolerance)
                    continue;

                left = Math.Min(left, segment.From);
                right = Math.Max(right, segment.To);
            }
        }

        return (left, right);
    }

    /// <summary>
    /// Row boundaries: every rule the document drew, plus a split between each
    /// pair of text baselines that a rule did not already separate. A band of
    /// several lines is several rows, which is what a table ruled only under its
    /// header actually is.
    /// </summary>
    private static double[] InferRows(double[] bands, List<PdfTextFragment> inside)
    {
        var baselines = new List<double>(inside.Count);
        foreach (PdfTextFragment fragment in inside)
            baselines.Add(fragment.Y);

        baselines.Sort();
        baselines.Reverse();

        var lines = new List<double>();
        foreach (double baseline in baselines)
        {
            if (lines.Count == 0 || lines[^1] - baseline > BaselineTolerance)
                lines.Add(baseline);
        }

        // One row per line of text, and the boundary between two of them is the
        // rule the document drew there if it drew one. Taking the rule rather
        // than adding it is what keeps a rule and the midpoint beside it from
        // becoming two boundaries with an empty sliver of a row between them.
        var edges = new List<double> { bands[0] };
        for (int i = 1; i < lines.Count && edges.Count < MaxEdges; i++)
        {
            double above = lines[i - 1];
            double below = lines[i];
            double boundary = (above + below) / 2;

            foreach (double band in bands)
            {
                if (band < above && band > below)
                {
                    boundary = band;
                    break;
                }
            }

            if (edges[^1] - boundary > EdgeTolerance && boundary - bands[^1] > EdgeTolerance)
                edges.Add(boundary);
        }

        edges.Add(bands[^1]);
        return [.. edges];
    }

    /// <summary>
    /// Column boundaries: the interior vertical rules if the document drew any,
    /// and otherwise the vertical corridors no glyph crosses. A corridor has to
    /// run the whole height of the region, which is what makes it a column
    /// boundary rather than a gap between two words.
    /// </summary>
    private static double[] InferColumns(
        List<Segment> vertical,
        List<PdfTextFragment> inside,
        double left,
        double right,
        double top,
        double bottom)
    {
        var edges = new List<double> { left };

        foreach (Segment segment in vertical)
        {
            if (segment.At <= left + EdgeTolerance || segment.At >= right - EdgeTolerance)
                continue;
            if (segment.From > bottom + CoverTolerance || segment.To < top - CoverTolerance)
                continue;

            edges.Add(segment.At);
        }

        if (edges.Count == 1)
            edges.AddRange(Corridors(inside, left, right));

        edges.Sort();

        var distinct = new List<double> { edges[0] };
        for (int i = 1; i < edges.Count && distinct.Count < MaxEdges; i++)
        {
            if (edges[i] - distinct[^1] > EdgeTolerance)
                distinct.Add(edges[i]);
        }

        if (right - distinct[^1] > EdgeTolerance)
            distinct.Add(right);

        return [.. distinct];
    }

    /// <summary>
    /// The middles of the vertical lanes no glyph occupies. Occupancy is counted
    /// in one-point bins, which is finer than any gap that could be a column
    /// boundary and coarse enough that a page of text costs a few thousand.
    /// </summary>
    private static List<double> Corridors(List<PdfTextFragment> inside, double left, double right)
    {
        var found = new List<double>();
        int width = (int)Math.Ceiling(right - left);
        if (width <= 0 || width > MaxBins)
            return found;

        var occupied = new bool[width + 1];
        foreach (PdfTextFragment fragment in inside)
        {
            int from = (int)Math.Floor(Math.Max(0, fragment.X - left));
            int to = (int)Math.Ceiling(Math.Min(width, fragment.EndX - left));
            for (int i = from; i <= to && i < occupied.Length; i++)
            {
                if (i >= 0)
                    occupied[i] = true;
            }
        }

        int run = 0;
        for (int i = 0; i <= width; i++)
        {
            if (!occupied[i])
            {
                run++;
                continue;
            }

            // A lane touching either end is the table's margin, not a division.
            if (run >= MinimumCorridor && i - run > 0)
                found.Add(left + i - (run / 2.0));

            run = 0;
        }

        return found;
    }

    /// <summary>
    /// Reduces painted paths to the segments a lattice can be built from. A thin
    /// bar is one segment. A rectangle that was stroked and not filled is its
    /// four edges, which is how a table drawn as one box per cell arrives.
    /// </summary>
    private static void Collect(
        IReadOnlyList<PdfPaintedPath> paths,
        List<Segment> vertical,
        List<Segment> horizontal,
        List<PdfPaintedPath> fills)
    {
        foreach (PdfPaintedPath path in paths)
        {
            if (!double.IsFinite(path.MinX) || !double.IsFinite(path.MinY) ||
                !double.IsFinite(path.MaxX) || !double.IsFinite(path.MaxY))
            {
                continue;
            }

            switch (path.Kind)
            {
                case PdfArtworkKind.Rule:
                    if (path.Height >= path.Width)
                        vertical.Add(new Segment(Middle(path.MinX, path.MaxX), path.MinY, path.MaxY, path.Color));
                    else
                        horizontal.Add(new Segment(Middle(path.MinY, path.MaxY), path.MinX, path.MaxX, path.Color));
                    break;

                case PdfArtworkKind.Block when !path.Filled:
                    // An outline: the box is not the shape, its four sides are.
                    vertical.Add(new Segment(path.MinX, path.MinY, path.MaxY, path.Color));
                    vertical.Add(new Segment(path.MaxX, path.MinY, path.MaxY, path.Color));
                    horizontal.Add(new Segment(path.MinY, path.MinX, path.MaxX, path.Color));
                    horizontal.Add(new Segment(path.MaxY, path.MinX, path.MaxX, path.Color));
                    break;

                case PdfArtworkKind.Block:
                    fills.Add(path);
                    break;
            }

            if (vertical.Count > MaxEdges * 4 || horizontal.Count > MaxEdges * 4)
                return;
        }
    }

    private static double Middle(double low, double high) => (low + high) / 2;

    /// <summary>
    /// Collapses near-equal coordinates into one edge each, ascending. The
    /// representative is the mean of its cluster, so a rule drawn a fraction off
    /// does not shift the grid.
    /// </summary>
    private static double[] Cluster(List<Segment> segments, Func<Segment, double> coordinate)
    {
        var values = new List<double>(segments.Count);
        foreach (Segment segment in segments)
            values.Add(coordinate(segment));

        values.Sort();

        var edges = new List<double>();
        int index = 0;
        while (index < values.Count && edges.Count < MaxEdges)
        {
            double sum = values[index];
            int count = 1;
            int next = index + 1;
            while (next < values.Count && values[next] - values[index] <= EdgeTolerance)
            {
                sum += values[next];
                count++;
                next++;
            }

            edges.Add(sum / count);
            index = next;
        }

        return [.. edges];
    }

    /// <summary>
    /// Whether every edge of every cell was painted. This is the whole test that
    /// separates a table from an accident of alignment, so it is required in
    /// full rather than by proportion.
    /// </summary>
    private static bool IsComplete(
        double[] columns,
        double[] rows,
        List<Segment> vertical,
        List<Segment> horizontal)
    {
        for (int row = 0; row < rows.Length - 1; row++)
        {
            double top = rows[row];
            double bottom = rows[row + 1];
            foreach (double x in columns)
            {
                if (!Covers(vertical, x, bottom, top))
                    return false;
            }
        }

        for (int column = 0; column < columns.Length - 1; column++)
        {
            double left = columns[column];
            double right = columns[column + 1];
            foreach (double y in rows)
            {
                if (!Covers(horizontal, y, left, right))
                    return false;
            }
        }

        return true;
    }

    /// <summary>Whether one painted segment at <paramref name="at"/> spans [from, to].</summary>
    private static bool Covers(List<Segment> segments, double at, double from, double to)
    {
        foreach (Segment segment in segments)
        {
            if (Math.Abs(segment.At - at) <= EdgeTolerance &&
                segment.From <= from + CoverTolerance &&
                segment.To >= to - CoverTolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static TableBorder Edge(List<Segment> segments, double at, double from, double to)
    {
        foreach (Segment segment in segments)
        {
            if (Math.Abs(segment.At - at) <= EdgeTolerance &&
                segment.From <= from + CoverTolerance &&
                segment.To >= to - CoverTolerance)
            {
                return TableBorder.Solid(segment.Color);
            }
        }

        return TableBorder.None;
    }

    /// <summary>
    /// The fill behind a cell, or none. The smallest fill that covers the cell
    /// wins, so a per-cell shade beats the row band it sits in, and a fill that
    /// reaches outside the table is a page background rather than a cell shade.
    /// </summary>
    private static BColor ShadeOf(
        List<PdfPaintedPath> fills,
        double left,
        double bottom,
        double right,
        double top,
        PdfTableGrid grid)
    {
        BColor shade = default;
        double best = double.MaxValue;

        foreach (PdfPaintedPath fill in fills)
        {
            if (fill.MinX > left + CoverTolerance || fill.MaxX < right - CoverTolerance ||
                fill.MinY > bottom + CoverTolerance || fill.MaxY < top - CoverTolerance)
            {
                continue;
            }

            if (fill.MinX < grid.Left - CoverTolerance || fill.MaxX > grid.Right + CoverTolerance ||
                fill.MinY < grid.Bottom - CoverTolerance || fill.MaxY > grid.Top + CoverTolerance)
            {
                continue;
            }

            double area = fill.Width * fill.Height;
            if (area < best)
            {
                best = area;
                shade = fill.Color;
            }
        }

        return shade;
    }

    /// <summary>A painted straight edge: where it sits, and how far it runs.</summary>
    private readonly record struct Segment(double At, double From, double To, BColor Color);
}
