using System;
using System.Collections.Generic;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Rtf;

/// <summary>
/// Collects an RTF table as the reader walks it: the row definitions
/// <c>\trowd</c> and <c>\cellx</c> state, the body paragraphs each <c>\cell</c>
/// closes, and the rows <c>\row</c> closes. Turned into a
/// <see cref="DocumentTable"/> over those paragraphs when the table ends.
/// </summary>
/// <remarks>
/// <para>
/// None of this is group-scoped, and that is the point of holding it here rather
/// than in the state RTF pushes and pops with its braces. A table is a run of
/// paragraphs, and the specification puts the row's properties at the row, not
/// in a group: writers wrap the cells and the row end in groups of their own -
/// <c>{\b A1 \cell}</c>, <c>{\row}</c>, a whole table in <c>{\trowd ... \pard}</c>
/// - and a row definition that vanished when such a group closed would leave
/// the cells after it with no table to be in. What is group-scoped is
/// <c>\intbl</c>, which is a paragraph property like any other, and the reader
/// keeps that one in its state.
/// </para>
/// <para>
/// A row without a <c>\trowd</c> of its own takes the definition of the row
/// before, which is what Word does with one and what holding the definition
/// outside the group state gives for nothing.
/// </para>
/// </remarks>
internal sealed class RtfTableBuilder
{
    /// <summary>RTF 1.9.1: <c>\brdrwN</c> cannot be greater than 75 twips.</summary>
    private const int MaxBorderTwips = 75;

    /// <summary>What a border that states a kind and no width is drawn as: a hairline.</summary>
    private const double DefaultBorderWidth = 0.5;

    private readonly RtfColorTable _colors;

    // The row definition in effect: the cells \cellx has closed so far, and the
    // properties the next \cellx will close with.
    private readonly List<CellDefinition> _definitions = [];
    private CellDefinition _next = new();
    private BorderSide _side;
    private int _rowLeft;
    private int _rowGap = -1;
    private double _rowHeight;
    private bool _rowExactHeight;
    private bool _rowHeader;
    private bool _rowKeep;

    // The table being read.
    private readonly List<RowDraft> _rows = [];
    private readonly List<CellDraft> _cells = [];
    private int _tableStart = -1;
    private int _cellStart = -1;
    private int _cellEnd;

    public RtfTableBuilder(RtfColorTable colors) => _colors = colors;

    /// <summary>True from the table's first paragraph until <see cref="Finish"/>.</summary>
    public bool IsOpen => _tableStart >= 0;

    /// <summary>
    /// Takes a row or cell definition control word, and says whether it was one.
    /// </summary>
    public bool HandleDefinitionWord(string keyword, int parameter, bool hasParameter)
    {
        switch (keyword)
        {
            case "trowd":
                _definitions.Clear();
                _next = new CellDefinition();
                _side = BorderSide.None;
                _rowLeft = 0;
                _rowGap = -1;
                _rowHeight = 0;
                _rowExactHeight = false;
                _rowHeader = false;
                _rowKeep = false;
                return true;
            case "trleft": if (hasParameter) _rowLeft = parameter; return true;
            // Half the space between two cells, which is the padding each side
            // of a cell takes. LibreOffice reads a table that states none with
            // no padding at all, the text against the cell's edge, and so does
            // this.
            case "trgaph": if (hasParameter) _rowGap = Math.Max(0, parameter); return true;
            // Positive is at least that tall and negative exactly that tall.
            // Preserve the sign's meaning separately from the height magnitude.
            case "trrh":
                _rowHeight = hasParameter ? Math.Abs((double)parameter) / 20d : 0;
                _rowExactHeight = hasParameter && parameter < 0;
                return true;
            case "trhdr": _rowHeader = !(hasParameter && parameter == 0); return true;
            case "trkeep": _rowKeep = !(hasParameter && parameter == 0); return true;

            case "clbrdrt": _side = BorderSide.Top; return true;
            case "clbrdrb": _side = BorderSide.Bottom; return true;
            case "clbrdrl": _side = BorderSide.Left; return true;
            case "clbrdrr": _side = BorderSide.Right; return true;
            case "clcbpat": if (hasParameter) _next.Shading = _colors.Get(parameter); return true;

            // The words that point the border words at something other than a
            // cell: a paragraph's borders, and the row's. What follows them is not
            // this cell's, and the reader does not draw it.
            case "brdrt":
            case "brdrb":
            case "brdrl":
            case "brdrr":
            case "brdrbtw":
            case "brdrbar":
            case "box":
            case "trbrdrt":
            case "trbrdrb":
            case "trbrdrl":
            case "trbrdrr":
            case "trbrdrh":
            case "trbrdrv":
                _side = BorderSide.None;
                return false;

            case "cellx":
                _next.Right = hasParameter ? parameter : 0;
                _definitions.Add(_next);
                _next = new CellDefinition();
                _side = BorderSide.None;
                return true;
        }

        if (_side == BorderSide.None || !keyword.StartsWith("brdr", StringComparison.Ordinal))
            return false;

        ApplyBorderWord(keyword, parameter, hasParameter);
        return true;
    }

    /// <summary>
    /// One of the border words, applied to the side of the next cell that the
    /// last <c>\clbrdr</c> named. Every kind but none draws: the model has one
    /// kind of line, so a double or a dotted border is drawn as a single line
    /// of the width it states.
    /// </summary>
    private void ApplyBorderWord(string keyword, int parameter, bool hasParameter)
    {
        Border border = _next.Get(_side);
        switch (keyword)
        {
            case "brdrw":
                if (hasParameter)
                    border.Width = Math.Clamp(parameter, 0, MaxBorderTwips) / 20d;
                break;
            case "brdrcf":
                if (hasParameter)
                    border.Color = _colors.Get(parameter);
                break;
            case "brdrnone":
            case "brdrnil":
                border.Drawn = false;
                break;
            case "brdrsp":
            case "brdrart":
                break;
            default:
                border.Drawn = true;
                break;
        }

        _next.Set(_side, border);
    }

    /// <summary>
    /// Notes that a paragraph of the table is about to close at
    /// <paramref name="paragraphIndex"/>: the first one opens the table, and the
    /// first after a <c>\cell</c> opens the next cell.
    /// </summary>
    public void NoteParagraph(int paragraphIndex)
    {
        if (_tableStart < 0)
            _tableStart = paragraphIndex;
        if (_cellStart < 0)
            _cellStart = paragraphIndex;

        _cellEnd = paragraphIndex + 1;
    }

    /// <summary>Closes the cell whose paragraphs end at <paramref name="paragraphEnd"/>.</summary>
    public void EndCell(int paragraphEnd)
    {
        int start = _cellStart < 0 ? paragraphEnd : _cellStart;
        _cells.Add(new CellDraft(start, Math.Max(0, paragraphEnd - start)));
        _cellStart = -1;
        _cellEnd = paragraphEnd;
    }

    /// <summary>
    /// Closes the row the cells since the last one make, under the definition in
    /// effect now. A <c>\row</c> with no cell before it closes nothing.
    /// </summary>
    public void EndRow()
    {
        if (_cells.Count == 0)
            return;

        _rows.Add(new RowDraft(
            [.. _cells],
            [.. _definitions],
            _rowLeft,
            _rowGap,
            _rowHeight,
            _rowHeader,
            _rowKeep,
            _rowExactHeight));
        _cells.Clear();
    }

    /// <summary>
    /// The table read since it opened, or null when nothing in it held a
    /// paragraph; the builder is empty again afterwards.
    /// </summary>
    /// <remarks>
    /// Paragraphs that were in the table and no <c>\cell</c> closed, and cells no
    /// <c>\row</c> closed, are kept as a last cell and a last row: the document
    /// put them in the table, and the text is worth more than the missing mark.
    /// </remarks>
    public DocumentTable? Finish()
    {
        if (_tableStart < 0)
            return null;

        if (_cellStart >= 0)
            EndCell(_cellEnd);
        EndRow();

        int start = _tableStart;
        int end = _cellEnd;
        List<RowDraft> rows = [.. _rows];
        _rows.Clear();
        _tableStart = -1;
        _cellStart = -1;

        if (rows.Count == 0 || end <= start)
            return null;

        IReadOnlyList<double>? widths = BuildGrid(rows, out List<List<(int Column, int Span)>>? placement);
        var built = new List<TableRow>(rows.Count);
        for (int r = 0; r < rows.Count; r++)
        {
            RowDraft row = rows[r];
            var cells = new List<TableCell>(row.Cells.Count);
            for (int c = 0; c < row.Cells.Count; c++)
            {
                CellDraft cell = row.Cells[c];
                (int column, int span) = placement is null ? (c, 1) : placement[r][c];
                CellDefinition? definition = c < row.Definitions.Count ? row.Definitions[c] : null;
                cells.Add(new TableCell(
                    cell.ParagraphIndex,
                    cell.ParagraphCount,
                    column,
                    span,
                    shading: definition?.Shading ?? BColor.Empty,
                    borders: definition is null ? default : definition.Borders()));
            }

            built.Add(new TableRow(cells, row.Header, row.Height, !row.Keep, row.ExactHeight));
        }

        int gap = rows[0].Gap;
        return new DocumentTable(start, end - start, built, widths, gap < 0 ? 0 : gap / 20d);
    }

    /// <summary>
    /// The grid every row's cell boundaries fall on, and where each cell sits in
    /// it; null for both when some row does not state a boundary for every cell
    /// it holds, or states one that goes backwards.
    /// </summary>
    /// <remarks>
    /// RTF states each row's cells by their right edges, and rows of one table
    /// need not agree: fdo66565.rtf has a row of seven cells over a row of
    /// eighteen. The grid is every edge any row states, and a cell spans the
    /// columns between its own two edges - which is what LibreOffice's import
    /// does, and what keeps each cell as wide as its row says. A table without
    /// that is given no widths, and is divided evenly as one that states no grid
    /// is.
    /// </remarks>
    private static IReadOnlyList<double>? BuildGrid(
        List<RowDraft> rows, out List<List<(int Column, int Span)>>? placement)
    {
        placement = null;
        var edges = new SortedSet<int>();
        foreach (RowDraft row in rows)
        {
            if (row.Definitions.Count < row.Cells.Count)
                return null;

            int left = row.Left;
            edges.Add(left);
            for (int c = 0; c < row.Cells.Count; c++)
            {
                int right = row.Definitions[c].Right;
                if (right <= left)
                    return null;

                edges.Add(right);
                left = right;
            }
        }

        var grid = new List<int>(edges);
        var widths = new List<double>(grid.Count - 1);
        for (int i = 1; i < grid.Count; i++)
            widths.Add((grid[i] - grid[i - 1]) / 20d);

        placement = new List<List<(int Column, int Span)>>(rows.Count);
        foreach (RowDraft row in rows)
        {
            var cells = new List<(int Column, int Span)>(row.Cells.Count);
            int left = grid.BinarySearch(row.Left);
            for (int c = 0; c < row.Cells.Count; c++)
            {
                int right = grid.BinarySearch(row.Definitions[c].Right);
                cells.Add((left, right - left));
                left = right;
            }

            placement.Add(cells);
        }

        return widths;
    }

    private enum BorderSide
    {
        None,
        Left,
        Top,
        Right,
        Bottom,
    }

    private struct Border
    {
        public bool Drawn;
        public double Width;
        public BColor Color;

        /// <summary>
        /// The edge drawn: black where the document gives no colour or the auto
        /// one, as Word and LibreOffice draw it.
        /// </summary>
        public readonly TableBorder ToTableBorder() => Drawn
            ? new TableBorder(Color.IsEmpty ? BColor.Black : Color, Width > 0 ? Width : DefaultBorderWidth)
            : TableBorder.None;
    }

    /// <summary>One cell as the row definition states it, closed by its <c>\cellx</c>.</summary>
    private sealed class CellDefinition
    {
        private Border _left;
        private Border _top;
        private Border _right;
        private Border _bottom;

        /// <summary>The cell's right edge, in twips from the row's origin.</summary>
        public int Right;

        public BColor Shading = BColor.Empty;

        public Border Get(BorderSide side) => side switch
        {
            BorderSide.Left => _left,
            BorderSide.Top => _top,
            BorderSide.Right => _right,
            _ => _bottom,
        };

        public void Set(BorderSide side, Border border)
        {
            switch (side)
            {
                case BorderSide.Left: _left = border; break;
                case BorderSide.Top: _top = border; break;
                case BorderSide.Right: _right = border; break;
                default: _bottom = border; break;
            }
        }

        public CellBorders Borders() => new(
            _left.ToTableBorder(), _top.ToTableBorder(), _right.ToTableBorder(), _bottom.ToTableBorder());
    }

    private readonly record struct CellDraft(int ParagraphIndex, int ParagraphCount);

    private sealed record RowDraft(
        List<CellDraft> Cells,
        List<CellDefinition> Definitions,
        int Left,
        int Gap,
        double Height,
        bool Header,
        bool Keep,
        bool ExactHeight);
}
