using System;
using System.Collections.Generic;
using System.Linq;
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
    private BorderSide _rowSide;
    private int _rowLeft;
    private int _rowGap = -1;
    private double _rowHeight;
    private bool _rowExactHeight;
    private bool _rowHeader;
    private bool _rowKeep;
    private double? _rowPaddingLeft;
    private double? _rowPaddingTop;
    private double? _rowPaddingRight;
    private double? _rowPaddingBottom;
    private RowBorders _rowBorders = new();

    // Floating table positioning
    private bool _isPositioned;
    private TableAnchor _hAnchor = TableAnchor.Text;
    private TableAnchor _vAnchor = TableAnchor.Text;
    private double _posX;
    private double _posY;
    private TableAlignment _hAlign = TableAlignment.None;
    private TableAlignment _vAlign = TableAlignment.None;
    private double _leftFromText;
    private double _rightFromText;
    private double _topFromText;
    private double _bottomFromText;
    private bool _allowOverlap = true;

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
                _rowSide = BorderSide.None;
                _rowLeft = 0;
                _rowGap = -1;
                _rowHeight = 0;
                _rowExactHeight = false;
                _rowHeader = false;
                _rowKeep = false;
                _rowPaddingLeft = null;
                _rowPaddingTop = null;
                _rowPaddingRight = null;
                _rowPaddingBottom = null;
                _rowBorders = new RowBorders();
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

            // Positioning
            case "tpvpg": _vAnchor = TableAnchor.Page; _isPositioned = true; return true;
            case "tpvmrg": _vAnchor = TableAnchor.Margin; _isPositioned = true; return true;
            case "tpvpara": _vAnchor = TableAnchor.Text; _isPositioned = true; return true;
            case "tphpg": _hAnchor = TableAnchor.Page; _isPositioned = true; return true;
            case "tphmrg": _hAnchor = TableAnchor.Margin; _isPositioned = true; return true;
            case "tphcol": _hAnchor = TableAnchor.Text; _isPositioned = true; return true;
            case "tposx": if (hasParameter) { _posX = parameter / 20.0; _isPositioned = true; } return true;
            case "tposy": if (hasParameter) { _posY = parameter / 20.0; _isPositioned = true; } return true;
            case "tposxl": _hAlign = TableAlignment.Start; _isPositioned = true; return true;
            case "tposxc": _hAlign = TableAlignment.Center; _isPositioned = true; return true;
            case "tposxr": _hAlign = TableAlignment.End; _isPositioned = true; return true;
            case "tposxi": _hAlign = TableAlignment.Inside; _isPositioned = true; return true;
            case "tposxo": _hAlign = TableAlignment.Outside; _isPositioned = true; return true;
            case "tposyt": _vAlign = TableAlignment.Start; _isPositioned = true; return true;
            case "tposyc": _vAlign = TableAlignment.Center; _isPositioned = true; return true;
            case "tposyb": _vAlign = TableAlignment.End; _isPositioned = true; return true;
            case "tposyin":
            case "tposyinline": _vAlign = TableAlignment.Inside; _isPositioned = true; return true;
            case "tposyout": _vAlign = TableAlignment.Outside; _isPositioned = true; return true;
            case "tdfrmtxtLeft":
            case "tdfrmtxtl": if (hasParameter) { _leftFromText = parameter / 20.0; _isPositioned = true; } return true;
            case "tdfrmtxtRight":
            case "tdfrmtxtr": if (hasParameter) { _rightFromText = parameter / 20.0; _isPositioned = true; } return true;
            case "tdfrmtxtTop":
            case "tdfrmtxtt": if (hasParameter) { _topFromText = parameter / 20.0; _isPositioned = true; } return true;
            case "tdfrmtxtBottom":
            case "tdfrmtxtb": if (hasParameter) { _bottomFromText = parameter / 20.0; _isPositioned = true; } return true;
            case "tbloverlap":
            case "ttbloverlap": _allowOverlap = !(hasParameter && parameter == 0); _isPositioned = true; return true;

            // Merges
            case "clmgf": _next.HorizontalMerge = MergeKind.First; return true;
            case "clmrg": _next.HorizontalMerge = MergeKind.Merged; return true;
            case "clvmgf": _next.VerticalMerge = MergeKind.First; return true;
            case "clvmrg": _next.VerticalMerge = MergeKind.Merged; return true;

            // Padding
            case "trpaddl": if (hasParameter) _rowPaddingLeft = parameter / 20.0; return true;
            case "trpaddt": if (hasParameter) _rowPaddingTop = parameter / 20.0; return true;
            case "trpaddr": if (hasParameter) _rowPaddingRight = parameter / 20.0; return true;
            case "trpaddb": if (hasParameter) _rowPaddingBottom = parameter / 20.0; return true;
            case "trpaddfl":
            case "trpaddft":
            case "trpaddfr":
            case "trpaddfb": return true;

            case "clpadl": if (hasParameter) _next.PaddingLeft = parameter / 20.0; return true;
            case "clpadt": if (hasParameter) _next.PaddingTop = parameter / 20.0; return true;
            case "clpadr": if (hasParameter) _next.PaddingRight = parameter / 20.0; return true;
            case "clpadb": if (hasParameter) _next.PaddingBottom = parameter / 20.0; return true;
            case "clpadfl":
            case "clpadft":
            case "clpadfr":
            case "clpadfb": return true;

            case "clbrdrt": _side = BorderSide.Top; _rowSide = BorderSide.None; return true;
            case "clbrdrb": _side = BorderSide.Bottom; _rowSide = BorderSide.None; return true;
            case "clbrdrl": _side = BorderSide.Left; _rowSide = BorderSide.None; return true;
            case "clbrdrr": _side = BorderSide.Right; _rowSide = BorderSide.None; return true;
            case "clcbpat": if (hasParameter) _next.Shading = _colors.Get(parameter); return true;

            case "trbrdrt": _side = BorderSide.None; _rowSide = BorderSide.Top; return true;
            case "trbrdrb": _side = BorderSide.None; _rowSide = BorderSide.Bottom; return true;
            case "trbrdrl": _side = BorderSide.None; _rowSide = BorderSide.Left; return true;
            case "trbrdrr": _side = BorderSide.None; _rowSide = BorderSide.Right; return true;
            case "trbrdrh": _side = BorderSide.None; _rowSide = BorderSide.Horizontal; return true;
            case "trbrdrv": _side = BorderSide.None; _rowSide = BorderSide.Vertical; return true;

            case "brdrt":
            case "brdrb":
            case "brdrl":
            case "brdrr":
            case "brdrbtw":
            case "brdrbar":
            case "box":
                _side = BorderSide.None;
                _rowSide = BorderSide.None;
                return false;

            case "cellx":
                _next.Right = hasParameter ? parameter : 0;
                _definitions.Add(_next);
                _next = new CellDefinition();
                _side = BorderSide.None;
                _rowSide = BorderSide.None;
                return true;
        }

        if (_side == BorderSide.None && _rowSide == BorderSide.None)
            return false;
        if (!keyword.StartsWith("brdr", StringComparison.Ordinal))
            return false;

        if (_side != BorderSide.None)
            ApplyBorderWord(keyword, parameter, hasParameter);
        else if (_rowSide != BorderSide.None)
            ApplyRowBorderWord(keyword, parameter, hasParameter);
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

    private void ApplyRowBorderWord(string keyword, int parameter, bool hasParameter)
    {
        Border border = _rowBorders.Get(_rowSide);
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

        _rowBorders.Set(_rowSide, border);
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

        TableCellPadding? rowPadding = (_rowPaddingLeft.HasValue || _rowPaddingTop.HasValue || _rowPaddingRight.HasValue || _rowPaddingBottom.HasValue)
            ? new TableCellPadding(_rowPaddingLeft ?? 0, _rowPaddingTop ?? 0, _rowPaddingRight ?? 0, _rowPaddingBottom ?? 0)
            : null;

        _rows.Add(new RowDraft(
            [.. _cells],
            [.. _definitions],
            _rowLeft,
            _rowGap,
            _rowHeight,
            _rowHeader,
            _rowKeep,
            _rowExactHeight,
            rowPadding,
            _rowBorders));
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

        // Pre-build drafts for vertical merge tracking
        var rowCellDrafts = new List<List<CellBuilderDraft>>(rows.Count);
        for (int r = 0; r < rows.Count; r++)
        {
            RowDraft row = rows[r];
            var drafts = new List<CellBuilderDraft>();
            for (int c = 0; c < row.Cells.Count; c++)
            {
                CellDraft cell = row.Cells[c];
                (int column, int span) = placement is null ? (c, 1) : placement[r][c];
                CellDefinition? definition = c < row.Definitions.Count ? row.Definitions[c] : null;

                TableCellPadding? padding = null;
                if (definition is not null && (definition.PaddingLeft.HasValue || definition.PaddingTop.HasValue || definition.PaddingRight.HasValue || definition.PaddingBottom.HasValue))
                {
                    padding = new TableCellPadding(
                        definition.PaddingLeft ?? row.RowPadding?.Left ?? 0,
                        definition.PaddingTop ?? row.RowPadding?.Top ?? 0,
                        definition.PaddingRight ?? row.RowPadding?.Right ?? 0,
                        definition.PaddingBottom ?? row.RowPadding?.Bottom ?? 0);
                }
                else if (row.RowPadding is not null)
                {
                    padding = row.RowPadding;
                }

                CellBorders borders = definition is null ? default :
                    definition.ResolveBorders(r, rows.Count, column, widths?.Count ?? row.Cells.Count, row.Borders);

                drafts.Add(new CellBuilderDraft(
                    cell.ParagraphIndex,
                    cell.ParagraphCount,
                    column,
                    span,
                    definition?.Shading ?? BColor.Empty,
                    borders,
                    padding,
                    definition?.HorizontalMerge ?? MergeKind.None,
                    definition?.VerticalMerge ?? MergeKind.None));
            }
            rowCellDrafts.Add(drafts);
        }

        // Resolve Horizontal Merges
        for (int r = 0; r < rowCellDrafts.Count; r++)
        {
            var drafts = rowCellDrafts[r];
            for (int c = 0; c < drafts.Count; c++)
            {
                if (drafts[c].HorizontalMerge == MergeKind.First)
                {
                    int spanSum = drafts[c].ColumnSpan;
                    int paraCount = drafts[c].ParagraphCount;
                    int k = c + 1;
                    while (k < drafts.Count && drafts[k].HorizontalMerge == MergeKind.Merged)
                    {
                        spanSum += drafts[k].ColumnSpan;
                        paraCount += drafts[k].ParagraphCount;
                        drafts[k].IsMergedAway = true;
                        k++;
                    }
                    drafts[c].ColumnSpan = spanSum;
                    drafts[c].ParagraphCount = paraCount;
                }
            }
        }

        // Resolve Vertical Merges
        for (int r = 1; r < rowCellDrafts.Count; r++)
        {
            for (int c = 0; c < rowCellDrafts[r].Count; c++)
            {
                var current = rowCellDrafts[r][c];
                if (current.IsMergedAway) continue;
                if (current.VerticalMerge == MergeKind.Merged)
                {
                    current.IsRowSpanContinuation = true;
                    // Find origin above
                    for (int prevR = r - 1; prevR >= 0; prevR--)
                    {
                        var above = rowCellDrafts[prevR].FirstOrDefault(cd => !cd.IsMergedAway && cd.ColumnIndex == current.ColumnIndex);
                        if (above is not null)
                        {
                            above.RowSpan++;
                            break;
                        }
                    }
                }
            }
        }

        // Build TableRows and TableCells
        var built = new List<TableRow>(rows.Count);
        for (int r = 0; r < rows.Count; r++)
        {
            RowDraft row = rows[r];
            var cells = new List<TableCell>();
            foreach (CellBuilderDraft cd in rowCellDrafts[r])
            {
                if (cd.IsMergedAway)
                    continue;

                cells.Add(new TableCell(
                    cd.ParagraphIndex,
                    cd.IsRowSpanContinuation ? 0 : cd.ParagraphCount,
                    cd.ColumnIndex,
                    cd.ColumnSpan,
                    rowSpan: cd.RowSpan,
                    shading: cd.Shading,
                    borders: cd.Borders,
                    isRowSpanContinuation: cd.IsRowSpanContinuation,
                    padding: cd.Padding));
            }

            built.Add(new TableRow(cells, row.Header, row.Height, !row.Keep, row.ExactHeight));
        }

        int gap = rows[0].Gap;
        TablePosition? pos = _isPositioned
            ? new TablePosition(_hAnchor, _posX, _vAnchor, _posY, _hAlign, _vAlign, _leftFromText, _rightFromText, _topFromText, _bottomFromText, _allowOverlap)
            : null;

        return new DocumentTable(start, end - start, built, widths, gap < 0 ? 0 : gap / 20d, position: pos);
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
        Horizontal,
        Vertical,
    }

    private enum MergeKind
    {
        None,
        First,
        Merged,
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

    private struct RowBorders
    {
        public Border Top;
        public Border Bottom;
        public Border Left;
        public Border Right;
        public Border Horizontal;
        public Border Vertical;

        public Border Get(BorderSide side) => side switch
        {
            BorderSide.Left => Left,
            BorderSide.Top => Top,
            BorderSide.Right => Right,
            BorderSide.Bottom => Bottom,
            BorderSide.Horizontal => Horizontal,
            BorderSide.Vertical => Vertical,
            _ => default,
        };

        public void Set(BorderSide side, Border border)
        {
            switch (side)
            {
                case BorderSide.Left: Left = border; break;
                case BorderSide.Top: Top = border; break;
                case BorderSide.Right: Right = border; break;
                case BorderSide.Bottom: Bottom = border; break;
                case BorderSide.Horizontal: Horizontal = border; break;
                case BorderSide.Vertical: Vertical = border; break;
            }
        }
    }

    /// <summary>One cell as the row definition states it, closed by its <c>\cellx</c>.</summary>
    private sealed class CellDefinition
    {
        private Border _left;
        private Border _top;
        private Border _right;
        private Border _bottom;
        private bool _hasExplicitLeft;
        private bool _hasExplicitTop;
        private bool _hasExplicitRight;
        private bool _hasExplicitBottom;

        /// <summary>The cell's right edge, in twips from the row's origin.</summary>
        public int Right;

        public BColor Shading = BColor.Empty;
        public MergeKind HorizontalMerge = MergeKind.None;
        public MergeKind VerticalMerge = MergeKind.None;

        public double? PaddingLeft;
        public double? PaddingTop;
        public double? PaddingRight;
        public double? PaddingBottom;

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
                case BorderSide.Left: _left = border; _hasExplicitLeft = true; break;
                case BorderSide.Top: _top = border; _hasExplicitTop = true; break;
                case BorderSide.Right: _right = border; _hasExplicitRight = true; break;
                default: _bottom = border; _hasExplicitBottom = true; break;
            }
        }

        public CellBorders ResolveBorders(int rowIndex, int rowCount, int colIndex, int colCount, RowBorders rowBorders)
        {
            Border left = _hasExplicitLeft ? _left :
                (colIndex == 0 && rowBorders.Left.Drawn ? rowBorders.Left : (rowBorders.Vertical.Drawn ? rowBorders.Vertical : default));
            Border right = _hasExplicitRight ? _right :
                (colIndex == colCount - 1 && rowBorders.Right.Drawn ? rowBorders.Right : (rowBorders.Vertical.Drawn ? rowBorders.Vertical : default));
            Border top = _hasExplicitTop ? _top :
                (rowIndex == 0 && rowBorders.Top.Drawn ? rowBorders.Top : (rowBorders.Horizontal.Drawn ? rowBorders.Horizontal : default));
            Border bottom = _hasExplicitBottom ? _bottom :
                (rowIndex == rowCount - 1 && rowBorders.Bottom.Drawn ? rowBorders.Bottom : (rowBorders.Horizontal.Drawn ? rowBorders.Horizontal : default));

            return new CellBorders(
                left.ToTableBorder(), top.ToTableBorder(), right.ToTableBorder(), bottom.ToTableBorder());
        }
    }

    private sealed class CellBuilderDraft(
        int paragraphIndex,
        int paragraphCount,
        int columnIndex,
        int columnSpan,
        BColor shading,
        CellBorders borders,
        TableCellPadding? padding,
        MergeKind horizontalMerge,
        MergeKind verticalMerge)
    {
        public int ParagraphIndex { get; set; } = paragraphIndex;
        public int ParagraphCount { get; set; } = paragraphCount;
        public int ColumnIndex { get; set; } = columnIndex;
        public int ColumnSpan { get; set; } = columnSpan;
        public int RowSpan { get; set; } = 1;
        public BColor Shading { get; set; } = shading;
        public CellBorders Borders { get; set; } = borders;
        public TableCellPadding? Padding { get; set; } = padding;
        public MergeKind HorizontalMerge { get; set; } = horizontalMerge;
        public MergeKind VerticalMerge { get; set; } = verticalMerge;
        public bool IsMergedAway { get; set; }
        public bool IsRowSpanContinuation { get; set; }
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
        bool ExactHeight,
        TableCellPadding? RowPadding,
        RowBorders Borders);
}
