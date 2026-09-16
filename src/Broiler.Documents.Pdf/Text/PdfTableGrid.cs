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

    private readonly double[] _columns;
    private readonly double[] _rows;
    private readonly BColor[] _shading;
    private readonly CellBorders[] _borders;

    private PdfTableGrid(double[] columns, double[] rows, BColor[] shading, CellBorders[] borders)
    {
        _columns = columns;
        _rows = rows;
        _shading = shading;
        _borders = borders;
    }

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
    /// Finds every fully ruled grid on a page. Grids are returned top to bottom,
    /// which is the order their text is read in.
    /// </summary>
    public static List<PdfTableGrid> Detect(IReadOnlyList<PdfPaintedPath> paths)
    {
        var grids = new List<PdfTableGrid>();
        if (paths is null || paths.Count == 0)
            return grids;

        var vertical = new List<Segment>();
        var horizontal = new List<Segment>();
        var fills = new List<PdfPaintedPath>();
        Collect(paths, vertical, horizontal, fills);

        if (vertical.Count < 2 || horizontal.Count < 2)
            return grids;

        // One lattice per page is the case that matters and the case that is
        // safe: two tables side by side share no edges, so a single lattice over
        // all of them would claim cells neither drew. Candidate edges are taken
        // from the whole page and then the lattice is required to be complete,
        // which a pair of separate tables fails.
        double[] columns = Cluster(vertical, s => s.At);
        double[] rows = Cluster(horizontal, s => s.At);
        Array.Reverse(rows);

        if (columns.Length - 1 < MinimumCells || rows.Length - 1 < MinimumCells)
            return grids;

        if (!IsComplete(columns, rows, vertical, horizontal))
            return grids;

        int cells = (columns.Length - 1) * (rows.Length - 1);
        var shading = new BColor[cells];
        var borders = new CellBorders[cells];
        var grid = new PdfTableGrid(columns, rows, shading, borders);

        for (int row = 0; row < grid.Rows; row++)
        {
            for (int column = 0; column < grid.Columns; column++)
            {
                double left = columns[column];
                double right = columns[column + 1];
                double top = rows[row];
                double bottom = rows[row + 1];

                borders[(row * grid.Columns) + column] = new CellBorders(
                    Edge(vertical, left, bottom, top),
                    Edge(horizontal, top, left, right),
                    Edge(vertical, right, bottom, top),
                    Edge(horizontal, bottom, left, right));

                shading[(row * grid.Columns) + column] =
                    ShadeOf(fills, left, bottom, right, top, grid);
            }
        }

        grids.Add(grid);
        return grids;
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
