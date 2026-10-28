using System;
using System.Collections.Generic;
using System.Linq;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Model;

/// <summary>
/// Controls how boundary-coincident insertions affect a range during paragraph shifts.
/// </summary>
public enum RangeAffinity
{
    /// <summary>
    /// Default affinity: insertions at Start expand the range (unless empty); insertions at End do not expand.
    /// </summary>
    Default,

    /// <summary>
    /// Insertions at Start shift the range forward rather than expanding it.
    /// </summary>
    ShiftAtStart,

    /// <summary>
    /// Insertions at End expand the range rather than staying outside.
    /// </summary>
    ExpandAtEnd,

    /// <summary>
    /// Insertions at both boundaries expand the range.
    /// </summary>
    ExpandBoth,

    /// <summary>
    /// Strict interior: insertions at either boundary stay outside the range.
    /// </summary>
    StrictInterior
}

/// <summary>
/// A table over a run of the document's paragraphs: the grid they are arranged
/// in, and how each cell is bounded and painted.
/// </summary>
/// <remarks>
/// <para>
/// The cells hold no text of their own. A cell names a range of the document's
/// paragraphs, and those paragraphs are the ordinary body paragraphs they always
/// were - read in row-major order, which is the order a table's text reads in.
/// That is the whole of the design: a caret, a selection, a find, a style, and
/// every codec's text handling go on working through one flat list of
/// paragraphs, and the table says how to arrange what is already there.
/// </para>
/// <para>
/// The alternative was a block tree, where a paragraph is either a body
/// paragraph or lives inside a cell inside a row inside a table. That is what a
/// word processor does, and it means every position, every edit, every codec,
/// and every renderer learns to walk two shapes instead of one. This carries the
/// same information for reading, drawing, and writing a table back out.
/// </para>
/// <para>
/// What it gives up is what a paragraph-anchored object gives up here already
/// (see <see cref="DocumentShape"/>): the ranges are indices, so an edit that
/// adds or removes paragraphs moves them. <see cref="RichTextDocument"/> shifts
/// every range through the one place paragraph counts change, so typing in a
/// cell, splitting its paragraph, or deleting inside it keep the grid; an edit
/// that spans out of a cell and into the body does not, and cannot.
/// </para>
/// <para>
/// A table inside a cell is held by that cell, in <see cref="TableCell.Tables"/>.
/// Nesting is stated rather than worked out from the ranges, because the ranges
/// cannot always tell: a single-cell table holding a single-cell table covers
/// exactly the paragraphs the inner one does, and asking which table starts at a
/// paragraph would have no answer. A renderer walks a cell's tables the same way
/// it walks the document's, so it never has to know how deep it is.
/// </para>
/// </remarks>
public sealed class DocumentTable(
    int paragraphIndex,
    int paragraphCount,
    IReadOnlyList<TableRow> rows,
    IReadOnlyList<double>? columnWidths = null,
    double cellPadding = DocumentTable.DefaultCellPadding,
    TablePosition? position = null,
    bool keepWithNext = false,
    double? preferredWidth = null,
    double? widthPercent = null,
    TableAlignment alignment = TableAlignment.Start,
    double leftIndent = 0,
    bool pageBreakBefore = false,
    bool columnBreakBefore = false,
    bool autoFit = false,
    bool keepTogether = false,
    string? name = null)
{

    /// <summary>Word's own default cell margin: 0.08 inch on the left and right.</summary>
    public const double DefaultCellPadding = 5.4;

    /// <summary>The document paragraph the table starts at.</summary>
    public int ParagraphIndex { get; } = Math.Max(0, paragraphIndex);

    /// <summary>How many paragraphs the table covers, cells and nested tables together.</summary>
    public int ParagraphCount { get; } = Math.Max(0, paragraphCount);

    /// <summary>One past the last paragraph the table covers.</summary>
    public int ParagraphEnd => ParagraphIndex + ParagraphCount;

    public IReadOnlyList<TableRow> Rows { get; } = rows is null || rows.Count == 0 ? [] : [.. rows];

    /// <summary>
    /// The grid's column widths in points, empty when the document stated none.
    /// A renderer with no widths divides the space it has evenly, which is what a
    /// word processor does with a table that states no grid.
    /// </summary>
    public IReadOnlyList<double> ColumnWidths { get; } = columnWidths is null || columnWidths.Count == 0 ? [] : [.. columnWidths];

    /// <summary>The space between a cell's edge and its text, in points.</summary>
    public double CellPadding { get; } = Math.Max(0, cellPadding);

    /// <summary>
    /// Where the table floats, or null for a table in the text flow.
    /// </summary>
    /// <remarks>
    /// A floating table keeps the paragraphs it covers, as every table does; the
    /// position says only where they are drawn. See <see cref="TablePosition"/>.
    /// </remarks>
    public TablePosition? Position { get; } = position;

    /// <summary>Keep the table with the following block when both fit on a page.</summary>
    public bool KeepWithNext { get; } = keepWithNext;

    /// <summary>Preferred table width in points, or a percentage of the containing text area.</summary>
    public double? PreferredWidth { get; } = preferredWidth is > 0 && double.IsFinite(preferredWidth.Value) ? preferredWidth : null;
    public double? WidthPercent { get; } = widthPercent is > 0 && double.IsFinite(widthPercent.Value) ? widthPercent : null;
    public TableAlignment Alignment { get; } = alignment;
    public double LeftIndent { get; } = double.IsFinite(leftIndent) ? leftIndent : 0;
    public bool PageBreakBefore { get; } = pageBreakBefore;
    public bool ColumnBreakBefore { get; } = columnBreakBefore;

    /// <summary>Size columns from their text when no grid is stated, as HTML auto tables do.</summary>
    public bool AutoFit { get; } = autoFit;
    public bool KeepTogether { get; } = keepTogether;

    /// <summary>Optional sheet or table name (e.g. "Sheet1" or "Sales").</summary>
    public string? Name { get; } = name;

    /// <summary>Returns a copy of this table with the specified sheet or table name.</summary>
    public DocumentTable WithName(string? newName) =>
        new DocumentTable(
            ParagraphIndex,
            ParagraphCount,
            Rows,
            ColumnWidths,
            CellPadding,
            Position,
            KeepWithNext,
            PreferredWidth,
            WidthPercent,
            Alignment,
            LeftIndent,
            PageBreakBefore,
            ColumnBreakBefore,
            AutoFit,
            KeepTogether,
            newName);

    /// <summary>The inline table's width within its containing text area.</summary>
    public double WidthIn(double available)
    {
        int columns = ColumnWidths.Count;
        foreach (TableRow row in Rows)
            foreach (TableCell cell in row.Cells)
                columns = Math.Max(columns, cell.ColumnIndex + cell.ColumnSpan);
        bool incomplete = columns > ColumnWidths.Count;
        foreach (double width in ColumnWidths)
            incomplete |= !(width > 0);
        double requested = WidthPercent is > 0 ? available * WidthPercent.Value / 100 :
            PreferredWidth is > 0 ? PreferredWidth.Value : !incomplete && TotalWidth > 0 ? TotalWidth : available;
        return Math.Clamp(requested, 1, Math.Max(1, available));
    }

    /// <summary>A floating width is relative to its text area, but may exceed that area up to the paper width.</summary>
    public double FloatingWidthIn(double available, double paperWidth)
    {
        double requested = WidthPercent is > 0 ? available * WidthPercent.Value / 100 :
            PreferredWidth is > 0 ? PreferredWidth.Value : TotalWidth > 0 ? TotalWidth : available;
        return Math.Clamp(requested, 1, Math.Max(1, paperWidth));
    }

    /// <summary>The inline table's horizontal offset in its containing text area.</summary>
    public double OffsetIn(double available) => Alignment switch
    {
        TableAlignment.Center => Math.Max(0, (available - WidthIn(available)) / 2),
        TableAlignment.End or TableAlignment.Outside => Math.Max(0, available - WidthIn(available)),
        _ => LeftIndent,
    };

    /// <summary>True when the table stands outside the text flow.</summary>
    public bool IsFloating => Position is not null;

    /// <summary>The sum of the stated column widths; zero when there are none.</summary>
    public double TotalWidth
    {
        get
        {
            double total = 0;
            foreach (double width in ColumnWidths)
                total += Math.Max(0, width);
            return total;
        }
    }

    /// <summary>True when <paramref name="paragraphIndex"/> falls inside this table.</summary>
    public bool Covers(int paragraphIndex) =>
        paragraphIndex >= ParagraphIndex && paragraphIndex < ParagraphEnd;

    /// <summary>
    /// The table in <paramref name="tables"/> that starts at
    /// <paramref name="paragraphIndex"/>, or null when none does. This is the
    /// whole of what walking block content takes: at every paragraph, ask, and
    /// lay out a grid instead of a paragraph when the answer is not null.
    /// </summary>
    public static DocumentTable? StartingAt(IReadOnlyList<DocumentTable> tables, int paragraphIndex)
    {
        for (int i = 0; i < tables.Count; i++)
        {
            if (tables[i].ParagraphIndex == paragraphIndex && tables[i].ParagraphCount > 0)
                return tables[i];
        }

        return null;
    }

    /// <summary>
    /// This table with its paragraph ranges moved for an edit that replaced
    /// <paramref name="removed"/> paragraphs at <paramref name="at"/> with
    /// <paramref name="inserted"/> of them. Returns null when the edit removed the
    /// table outright.
    /// </summary>
    /// <remarks>
    /// A range grows or shrinks when the edit fell inside it and moves when the
    /// edit fell before it, which is the rule that keeps a cell around the
    /// paragraphs it held while they are typed in and split.
    /// </remarks>
    /// <summary>
    /// This table with its paragraph ranges moved for an edit that replaced
    /// <paramref name="removed"/> paragraphs at <paramref name="at"/> with
    /// <paramref name="inserted"/> of them. Returns null when the edit removed the
    /// table outright.
    /// </summary>
    /// <remarks>
    /// A range grows or shrinks when the edit fell inside it and moves when the
    /// edit fell before it, which is the rule that keeps a cell around the
    /// paragraphs it held while they are typed in and split.
    /// </remarks>
    public DocumentTable? Shifted(int at, int removed, int inserted) =>
        Shifted(at, removed, inserted, RangeAffinity.Default);

    /// <summary>
    /// This table with its paragraph ranges moved for an edit that replaced
    /// <paramref name="removed"/> paragraphs at <paramref name="at"/> with
    /// <paramref name="inserted"/>, taking <paramref name="affinity"/> into account
    /// for insertions directly on table or cell boundaries.
    /// </summary>
    public DocumentTable? Shifted(int at, int removed, int inserted, RangeAffinity affinity)
    {
        if (removed == inserted && removed == 0)
            return this;

        (int index, int count) = ShiftRange(ParagraphIndex, ParagraphCount, at, removed, inserted, affinity);
        if (count <= 0)
            return null;

        var rows = new List<TableRow>(Rows.Count);
        int held = 0;
        foreach (TableRow row in Rows)
        {
            TableRow shifted = row.Shifted(at, removed, inserted, affinity);
            foreach (TableCell cell in shifted.Cells)
                held += cell.ParagraphCount;

            rows.Add(shifted);
        }

        // An edit that took every paragraph out of every cell took the table with
        // them. What the edit put back is body text: leaving the grid over it
        // would draw an empty table and lay the paragraph out inside no cell,
        // which is to say nowhere.
        return held == 0 ? null : new DocumentTable(index, count, rows, ColumnWidths, CellPadding, Position,
            KeepWithNext, PreferredWidth, WidthPercent, Alignment, LeftIndent, PageBreakBefore, ColumnBreakBefore, AutoFit, KeepTogether, Name);
    }

    /// <summary>
    /// Returns this table with its paragraph ranges shifted by a fixed offset.
    /// Recursively shifts all rows, cells, and nested tables.
    /// </summary>
    public DocumentTable ShiftedBy(int paragraphOffset)
    {
        if (paragraphOffset == 0)
            return this;

        var rows = new List<TableRow>(Rows.Count);
        foreach (TableRow row in Rows)
            rows.Add(row.ShiftedBy(paragraphOffset));

        return new DocumentTable(
            ParagraphIndex + paragraphOffset,
            ParagraphCount,
            rows,
            ColumnWidths,
            CellPadding,
            Position,
            KeepWithNext,
            PreferredWidth,
            WidthPercent,
            Alignment,
            LeftIndent,
            PageBreakBefore,
            ColumnBreakBefore,
            AutoFit,
            KeepTogether,
            Name);
    }

    /// <summary>
    /// Relocates all paragraph ranges in this table when a block of paragraphs is moved.
    /// Recursively relocates all rows, cells, and nested tables.
    /// </summary>
    public DocumentTable? RelocateParagraphs(int sourceStart, int sourceCount, int destinationIndex)
    {
        if (sourceCount <= 0 || sourceStart == destinationIndex)
            return this;

        (int index, int count) = RelocateRange(ParagraphIndex, ParagraphCount, sourceStart, sourceCount, destinationIndex);
        if (count <= 0)
            return null;

        var rows = new List<TableRow>(Rows.Count);
        int held = 0;
        foreach (TableRow row in Rows)
        {
            TableRow relocated = row.RelocateParagraphs(sourceStart, sourceCount, destinationIndex);
            foreach (TableCell cell in relocated.Cells)
                held += cell.ParagraphCount;

            rows.Add(relocated);
        }

        return held == 0 ? null : new DocumentTable(
            index,
            count,
            rows,
            ColumnWidths,
            CellPadding,
            Position,
            KeepWithNext,
            PreferredWidth,
            WidthPercent,
            Alignment,
            LeftIndent,
            PageBreakBefore,
            ColumnBreakBefore,
            AutoFit,
            KeepTogether,
            Name);
    }

    /// <summary>
    /// Moves one paragraph range for an edit, by moving the two boundaries that
    /// define it. A range the edit fell inside grows or shrinks by what the edit
    /// changed; one it fell before slides; one it fell after does not move.
    /// </summary>
    public static (int Index, int Count) ShiftRange(
        int index,
        int count,
        int at,
        int removed,
        int inserted,
        RangeAffinity affinity = RangeAffinity.Default)
    {
        if (removed == inserted && removed == 0)
            return (index, count);

        if (count == 0)
        {
            if (at < index)
                return (index + inserted - removed, 0);
            if (at > index)
                return (index, 0);

            return affinity switch
            {
                RangeAffinity.ShiftAtStart or RangeAffinity.StrictInterior => (index + inserted - removed, 0),
                _ => (index, 0)
            };
        }

        int start = MapBoundaryStart(index, at, removed, inserted, affinity);
        int end = MapBoundaryEnd(index + count, at, removed, inserted, affinity);
        return (start, Math.Max(0, end - start));
    }

    private static int MapBoundaryStart(int position, int at, int removed, int inserted, RangeAffinity affinity)
    {
        if (position < at)
            return position;

        if (position == at)
        {
            if (removed > 0)
                return at;

            return affinity is RangeAffinity.ShiftAtStart or RangeAffinity.StrictInterior
                ? at + inserted
                : at;
        }

        return position >= at + removed ? position + inserted - removed : at;
    }

    private static int MapBoundaryEnd(int position, int at, int removed, int inserted, RangeAffinity affinity)
    {
        if (position < at)
            return position;

        if (position == at)
        {
            if (removed > 0)
                return at;

            return affinity is RangeAffinity.ExpandAtEnd or RangeAffinity.ExpandBoth
                ? at + inserted
                : at;
        }

        return position >= at + removed ? position + inserted - removed : at;
    }

    /// <summary>
    /// Relocates a paragraph range [index, index + count) when a block of paragraphs
    /// [sourceStart, sourceStart + sourceCount) is moved to destinationIndex.
    /// </summary>
    public static (int Index, int Count) RelocateRange(
        int index,
        int count,
        int sourceStart,
        int sourceCount,
        int destinationIndex)
    {
        if (sourceCount <= 0 || sourceStart == destinationIndex)
            return (index, count);

        int sourceEnd = sourceStart + sourceCount;
        int rangeEnd = index + count;

        // Case 1: The range is entirely within the moved source block
        if (index >= sourceStart && (count == 0 ? index <= sourceEnd : rangeEnd <= sourceEnd))
        {
            int offset = index - sourceStart;
            int newBlockStart = destinationIndex > sourceStart
                ? destinationIndex - sourceCount
                : destinationIndex;
            return (newBlockStart + offset, count);
        }

        if (destinationIndex > sourceStart)
        {
            // Range is between sourceEnd and destinationIndex: shifts left by sourceCount
            if (index >= sourceEnd && (count == 0 ? index <= destinationIndex : rangeEnd <= destinationIndex))
                return (index - sourceCount, count);

            // Range is completely outside
            if ((count == 0 ? index <= sourceStart : rangeEnd <= sourceStart) || index >= destinationIndex)
                return (index, count);
        }
        else
        {
            // Range is between destinationIndex and sourceStart: shifts right by sourceCount
            if (index >= destinationIndex && (count == 0 ? index <= sourceStart : rangeEnd <= sourceStart))
                return (index + sourceCount, count);

            // Range is completely outside
            if ((count == 0 ? index <= destinationIndex : rangeEnd <= destinationIndex) || index >= sourceEnd)
                return (index, count);
        }

        // Fallback for ranges straddling the boundary of the moved block
        if (index <= Math.Min(sourceStart, destinationIndex) && rangeEnd >= Math.Max(sourceEnd, destinationIndex))
            return (index, count);

        return (index, count);
    }

    /// <summary>
    /// Recalculates all cell formulas in this table, resolving cell references and dependencies.
    /// </summary>
    public TableEvaluationResult EvaluateFormulas(RichTextDocument? document = null) =>
        TableFormulaEngine.Evaluate(this, document);

    /// <summary>
    /// Validates internal consistency of cell paragraph ranges and nested table anchors.
    /// Returns true if valid, or false with an explanation in <paramref name="error"/>.
    /// </summary>
    public bool ValidateParagraphInvariants(out string? error)
    {
        int runningExpected = ParagraphIndex;
        for (int r = 0; r < Rows.Count; r++)
        {
            TableRow row = Rows[r];
            for (int c = 0; c < row.Cells.Count; c++)
            {
                TableCell cell = row.Cells[c];
                if (cell.IsRowSpanContinuation)
                {
                    if (cell.ParagraphCount != 0)
                    {
                        error = $"Row {r}, Cell {c} is continuation but has ParagraphCount = {cell.ParagraphCount} > 0";
                        return false;
                    }
                    continue;
                }

                if (cell.ParagraphIndex != runningExpected)
                {
                    error = $"Row {r}, Cell {c} starts at {cell.ParagraphIndex}, expected {runningExpected}";
                    return false;
                }

                foreach (DocumentTable nested in cell.Tables)
                {
                    if (nested.ParagraphIndex < cell.ParagraphIndex || nested.ParagraphEnd > cell.ParagraphEnd)
                    {
                        error = $"Nested table at {nested.ParagraphIndex}..{nested.ParagraphEnd} exceeds cell bounds {cell.ParagraphIndex}..{cell.ParagraphEnd}";
                        return false;
                    }

                    if (!nested.ValidateParagraphInvariants(out error))
                        return false;
                }

                runningExpected += cell.ParagraphCount;
            }
        }

        if (runningExpected - ParagraphIndex != ParagraphCount)
        {
            error = $"Table ParagraphCount is {ParagraphCount}, but sum of cells is {runningExpected - ParagraphIndex}";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Moves the row at <paramref name="fromIndex"/> to <paramref name="toIndex"/>,
    /// relocating paragraph indices of all rows, cells, and nested tables to preserve row-major order.
    /// </summary>
    public DocumentTable MoveRow(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Rows.Count || toIndex < 0 || toIndex >= Rows.Count || fromIndex == toIndex)
            return this;

        TableRow movingRow = Rows[fromIndex];
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

        TableRow targetRow = Rows[toIndex];
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
                destinationIndex = ParagraphIndex;
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
                destinationIndex = ParagraphEnd;
        }

        var newRows = new List<TableRow>(Rows);
        newRows.RemoveAt(fromIndex);
        newRows.Insert(toIndex, movingRow);

        if (sourceCount <= 0)
            return new DocumentTable(ParagraphIndex, ParagraphCount, newRows, ColumnWidths, CellPadding, Position,
                KeepWithNext, PreferredWidth, WidthPercent, Alignment, LeftIndent, PageBreakBefore, ColumnBreakBefore, AutoFit, KeepTogether);

        var remappedRows = new List<TableRow>(newRows.Count);
        foreach (TableRow row in newRows)
        {
            var remappedCells = new List<TableCell>(row.Cells.Count);
            foreach (TableCell cell in row.Cells)
            {
                (int newIdx, int newCnt) = RelocateRange(cell.ParagraphIndex, cell.ParagraphCount, sourceStart, sourceCount, destinationIndex);

                List<DocumentTable>? relocatedTables = null;
                if (cell.Tables.Count > 0)
                {
                    relocatedTables = new List<DocumentTable>(cell.Tables.Count);
                    foreach (DocumentTable nested in cell.Tables)
                    {
                        if (nested.RelocateParagraphs(sourceStart, sourceCount, destinationIndex) is DocumentTable relocated)
                            relocatedTables.Add(relocated);
                    }
                }

                remappedCells.Add(new TableCell(
                    newIdx,
                    newCnt,
                    cell.ColumnIndex,
                    cell.ColumnSpan,
                    cell.RowSpan,
                    cell.Shading,
                    cell.Borders,
                    cell.IsRowSpanContinuation,
                    relocatedTables,
                    cell.Padding,
                    cell.VerticalAlignment, cell.Formula, cell.NumberFormat));
            }
            remappedRows.Add(new TableRow(remappedCells, row.IsHeader, row.MinHeight, row.CanSplit, row.ExactHeight, row.KeepWithNext));
        }

        return new DocumentTable(
            ParagraphIndex,
            ParagraphCount,
            remappedRows,
            ColumnWidths,
            CellPadding,
            Position,
            KeepWithNext,
            PreferredWidth,
            WidthPercent,
            Alignment,
            LeftIndent,
            PageBreakBefore,
            ColumnBreakBefore,
            AutoFit,
            KeepTogether);
    }

    /// <summary>
    /// Inserts a new row at <paramref name="atIndex"/>, adjusting paragraph indices and expanding intersecting row spans.
    /// </summary>
    public DocumentTable InsertRow(int atIndex, TableRow? row = null, int paragraphsPerCell = 1)
    {
        int target = Math.Clamp(atIndex, 0, Rows.Count);
        int numCols = ColumnWidths.Count > 0
            ? ColumnWidths.Count
            : (Rows.Count > 0 ? Rows.Max(r => r.Cells.Count) : 1);

        int insertAt;
        if (target == 0)
            insertAt = ParagraphIndex;
        else if (target < Rows.Count)
        {
            insertAt = int.MaxValue;
            foreach (TableCell cell in Rows[target].Cells)
            {
                if (!cell.IsRowSpanContinuation)
                    insertAt = Math.Min(insertAt, cell.ParagraphIndex);
            }
            if (insertAt == int.MaxValue)
                insertAt = ParagraphIndex;
        }
        else
            insertAt = ParagraphEnd;

        TableRow rowToInsert;
        if (row is not null)
        {
            var anchoredCells = new List<TableCell>(row.Cells.Count);
            int running = insertAt;
            foreach (TableCell cell in row.Cells)
            {
                anchoredCells.Add(new TableCell(
                    running,
                    cell.ParagraphCount,
                    cell.ColumnIndex,
                    cell.ColumnSpan,
                    cell.RowSpan,
                    cell.Shading,
                    cell.Borders,
                    cell.IsRowSpanContinuation,
                    cell.Tables,
                    cell.Padding,
                    cell.VerticalAlignment, cell.Formula, cell.NumberFormat));
                if (!cell.IsRowSpanContinuation)
                    running += cell.ParagraphCount;
            }
            rowToInsert = new TableRow(anchoredCells, row.IsHeader, row.MinHeight, row.CanSplit, row.ExactHeight, row.KeepWithNext);
        }
        else
        {
            var defaultCells = new List<TableCell>(numCols);
            int running = insertAt;
            for (int c = 0; c < numCols; c++)
            {
                defaultCells.Add(new TableCell(running, paragraphsPerCell, columnIndex: c));
                running += paragraphsPerCell;
            }
            rowToInsert = new TableRow(defaultCells);
        }

        int totalInserted = rowToInsert.Cells.Where(c => !c.IsRowSpanContinuation).Sum(c => c.ParagraphCount);

        var newRows = new List<TableRow>(Rows.Count + 1);
        for (int r = 0; r < target; r++)
        {
            TableRow existingRow = Rows[r];
            var updatedCells = new List<TableCell>(existingRow.Cells.Count);
            foreach (TableCell cell in existingRow.Cells)
            {
                if (r + cell.RowSpan > target)
                {
                    updatedCells.Add(new TableCell(
                        cell.ParagraphIndex,
                        cell.ParagraphCount,
                        cell.ColumnIndex,
                        cell.ColumnSpan,
                        cell.RowSpan + 1,
                        cell.Shading,
                        cell.Borders,
                        cell.IsRowSpanContinuation,
                        cell.Tables,
                        cell.Padding,
                        cell.VerticalAlignment, cell.Formula, cell.NumberFormat));
                }
                else
                {
                    updatedCells.Add(cell);
                }
            }
            newRows.Add(new TableRow(updatedCells, existingRow.IsHeader, existingRow.MinHeight, existingRow.CanSplit, existingRow.ExactHeight, existingRow.KeepWithNext));
        }

        newRows.Add(rowToInsert);

        for (int r = target; r < Rows.Count; r++)
        {
            newRows.Add(Rows[r].ShiftedBy(totalInserted));
        }

        return new DocumentTable(
            ParagraphIndex,
            ParagraphCount + totalInserted,
            newRows,
            ColumnWidths,
            CellPadding,
            Position,
            KeepWithNext,
            PreferredWidth,
            WidthPercent,
            Alignment,
            LeftIndent,
            PageBreakBefore,
            ColumnBreakBefore,
            AutoFit,
            KeepTogether);
    }

    /// <summary>
    /// Deletes the row at <paramref name="atIndex"/>, reducing row spans and shifting subsequent paragraph indices.
    /// </summary>
    public DocumentTable? DeleteRow(int atIndex)
    {
        if (atIndex < 0 || atIndex >= Rows.Count)
            return this;

        if (Rows.Count == 1)
            return null;

        TableRow deletingRow = Rows[atIndex];
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

        var newRows = new List<TableRow>(Rows.Count - 1);
        for (int r = 0; r < atIndex; r++)
        {
            TableRow row = Rows[r];
            var updatedCells = new List<TableCell>(row.Cells.Count);
            foreach (TableCell cell in row.Cells)
            {
                if (r + cell.RowSpan > atIndex)
                {
                    updatedCells.Add(new TableCell(
                        cell.ParagraphIndex,
                        cell.ParagraphCount,
                        cell.ColumnIndex,
                        cell.ColumnSpan,
                        Math.Max(1, cell.RowSpan - 1),
                        cell.Shading,
                        cell.Borders,
                        cell.IsRowSpanContinuation,
                        cell.Tables,
                        cell.Padding,
                        cell.VerticalAlignment, cell.Formula, cell.NumberFormat));
                }
                else
                {
                    updatedCells.Add(cell);
                }
            }
            newRows.Add(new TableRow(updatedCells, row.IsHeader, row.MinHeight, row.CanSplit, row.ExactHeight, row.KeepWithNext));
        }

        for (int r = atIndex + 1; r < Rows.Count; r++)
        {
            TableRow row = Rows[r];
            var updatedCells = new List<TableCell>(row.Cells.Count);
            foreach (TableCell cell in row.Cells)
            {
                if (r == atIndex + 1 && cell.IsRowSpanContinuation)
                {
                    TableCell? parentCell = deletingRow.Cells.FirstOrDefault(c => c.ColumnIndex == cell.ColumnIndex);
                    if (parentCell is not null && parentCell.RowSpan > 1)
                    {
                        updatedCells.Add(new TableCell(
                            parentCell.ParagraphIndex,
                            parentCell.ParagraphCount,
                            parentCell.ColumnIndex,
                            parentCell.ColumnSpan,
                            parentCell.RowSpan - 1,
                            parentCell.Shading,
                            parentCell.Borders,
                            isRowSpanContinuation: false,
                            parentCell.Tables,
                            parentCell.Padding,
                            parentCell.VerticalAlignment, parentCell.Formula, parentCell.NumberFormat));
                        continue;
                    }
                }

                updatedCells.Add(cell.ShiftedBy(-totalRemoved));
            }
            newRows.Add(new TableRow(updatedCells, row.IsHeader, row.MinHeight, row.CanSplit, row.ExactHeight, row.KeepWithNext));
        }

        return new DocumentTable(
            ParagraphIndex,
            Math.Max(0, ParagraphCount - totalRemoved),
            newRows,
            ColumnWidths,
            CellPadding,
            Position,
            KeepWithNext,
            PreferredWidth,
            WidthPercent,
            Alignment,
            LeftIndent,
            PageBreakBefore,
            ColumnBreakBefore,
            AutoFit,
            KeepTogether);
    }

    /// <summary>
    /// Inserts a new column at <paramref name="atIndex"/>, adjusting column indices, spans, widths, and paragraph ranges.
    /// </summary>
    public DocumentTable InsertColumn(int atIndex, double width = 0, int paragraphsPerCell = 1)
    {
        int maxCols = ColumnWidths.Count > 0
            ? ColumnWidths.Count
            : (Rows.Count > 0 ? Rows.Max(r => r.Cells.Count) : 0);
        int target = Math.Clamp(atIndex, 0, maxCols);

        List<double>? newWidths = null;
        if (ColumnWidths.Count > 0)
        {
            newWidths = new List<double>(ColumnWidths);
            if (target <= newWidths.Count)
                newWidths.Insert(target, Math.Max(0, width));
        }

        var modifiedRows = new List<TableRow>(Rows.Count);
        foreach (TableRow row in Rows)
        {
            var newCells = new List<TableCell>(row.Cells.Count + 1);
            bool insertedInRow = false;

            foreach (TableCell cell in row.Cells)
            {
                int c0 = cell.ColumnIndex;
                int cs = cell.ColumnSpan;

                if (c0 < target && c0 + cs > target)
                {
                    newCells.Add(new TableCell(
                        cell.ParagraphIndex,
                        cell.ParagraphCount,
                        c0,
                        columnSpan: cs + 1,
                        cell.RowSpan,
                        cell.Shading,
                        cell.Borders,
                        cell.IsRowSpanContinuation,
                        cell.Tables,
                        cell.Padding,
                        cell.VerticalAlignment, cell.Formula, cell.NumberFormat));
                    insertedInRow = true;
                }
                else if (c0 >= target)
                {
                    if (!insertedInRow)
                    {
                        newCells.Add(new TableCell(0, paragraphsPerCell, columnIndex: target));
                        insertedInRow = true;
                    }
                    newCells.Add(new TableCell(
                        cell.ParagraphIndex,
                        cell.ParagraphCount,
                        columnIndex: c0 + 1,
                        cell.ColumnSpan,
                        cell.RowSpan,
                        cell.Shading,
                        cell.Borders,
                        cell.IsRowSpanContinuation,
                        cell.Tables,
                        cell.Padding,
                        cell.VerticalAlignment, cell.Formula, cell.NumberFormat));
                }
                else
                {
                    newCells.Add(cell);
                }
            }

            if (!insertedInRow)
            {
                newCells.Add(new TableCell(0, paragraphsPerCell, columnIndex: target));
            }

            modifiedRows.Add(new TableRow(newCells, row.IsHeader, row.MinHeight, row.CanSplit, row.ExactHeight, row.KeepWithNext));
        }

        int running = ParagraphIndex;
        var reanchoredRows = new List<TableRow>(modifiedRows.Count);
        foreach (TableRow row in modifiedRows)
        {
            var reanchoredCells = new List<TableCell>(row.Cells.Count);
            foreach (TableCell cell in row.Cells.OrderBy(c => c.ColumnIndex))
            {
                int cellCount = cell.IsRowSpanContinuation ? 0 : cell.ParagraphCount;
                int oldStart = cell.ParagraphIndex;
                int newStart = running;

                List<DocumentTable>? relocatedTables = null;
                if (cell.Tables.Count > 0)
                {
                    int delta = newStart - oldStart;
                    relocatedTables = cell.Tables.Select(t => t.ShiftedBy(delta)).ToList();
                }

                reanchoredCells.Add(new TableCell(
                    newStart,
                    cellCount,
                    cell.ColumnIndex,
                    cell.ColumnSpan,
                    cell.RowSpan,
                    cell.Shading,
                    cell.Borders,
                    cell.IsRowSpanContinuation,
                    relocatedTables,
                    cell.Padding,
                    cell.VerticalAlignment, cell.Formula, cell.NumberFormat));

                running += cellCount;
            }
            reanchoredRows.Add(new TableRow(reanchoredCells, row.IsHeader, row.MinHeight, row.CanSplit, row.ExactHeight, row.KeepWithNext));
        }

        return new DocumentTable(
            ParagraphIndex,
            running - ParagraphIndex,
            reanchoredRows,
            newWidths,
            CellPadding,
            Position,
            KeepWithNext,
            PreferredWidth,
            WidthPercent,
            Alignment,
            LeftIndent,
            PageBreakBefore,
            ColumnBreakBefore,
            AutoFit,
            KeepTogether);
    }

    /// <summary>
    /// Deletes the column at <paramref name="atIndex"/>, adjusting column spans, widths, and paragraph ranges.
    /// </summary>
    public DocumentTable? DeleteColumn(int atIndex)
    {
        int maxCols = ColumnWidths.Count > 0
            ? ColumnWidths.Count
            : (Rows.Count > 0 ? Rows.Max(r => r.Cells.Count) : 0);
        if (atIndex < 0 || atIndex >= maxCols)
            return this;

        if (maxCols <= 1)
            return null;

        List<double>? newWidths = null;
        if (ColumnWidths.Count > 0 && atIndex < ColumnWidths.Count)
        {
            newWidths = new List<double>(ColumnWidths);
            newWidths.RemoveAt(atIndex);
        }

        var modifiedRows = new List<TableRow>(Rows.Count);
        foreach (TableRow row in Rows)
        {
            var newCells = new List<TableCell>(row.Cells.Count);
            foreach (TableCell cell in row.Cells)
            {
                int c0 = cell.ColumnIndex;
                int cs = cell.ColumnSpan;

                if (c0 == atIndex && cs == 1)
                {
                    continue;
                }
                else if (c0 < atIndex && c0 + cs > atIndex)
                {
                    newCells.Add(new TableCell(
                        cell.ParagraphIndex,
                        cell.ParagraphCount,
                        c0,
                        columnSpan: cs - 1,
                        cell.RowSpan,
                        cell.Shading,
                        cell.Borders,
                        cell.IsRowSpanContinuation,
                        cell.Tables,
                        cell.Padding,
                        cell.VerticalAlignment, cell.Formula, cell.NumberFormat));
                }
                else if (c0 == atIndex && cs > 1)
                {
                    newCells.Add(new TableCell(
                        cell.ParagraphIndex,
                        cell.ParagraphCount,
                        c0,
                        columnSpan: cs - 1,
                        cell.RowSpan,
                        cell.Shading,
                        cell.Borders,
                        cell.IsRowSpanContinuation,
                        cell.Tables,
                        cell.Padding,
                        cell.VerticalAlignment, cell.Formula, cell.NumberFormat));
                }
                else if (c0 > atIndex)
                {
                    newCells.Add(new TableCell(
                        cell.ParagraphIndex,
                        cell.ParagraphCount,
                        columnIndex: c0 - 1,
                        cell.ColumnSpan,
                        cell.RowSpan,
                        cell.Shading,
                        cell.Borders,
                        cell.IsRowSpanContinuation,
                        cell.Tables,
                        cell.Padding,
                        cell.VerticalAlignment, cell.Formula, cell.NumberFormat));
                }
                else
                {
                    newCells.Add(cell);
                }
            }
            modifiedRows.Add(new TableRow(newCells, row.IsHeader, row.MinHeight, row.CanSplit, row.ExactHeight, row.KeepWithNext));
        }

        int running = ParagraphIndex;
        var reanchoredRows = new List<TableRow>(modifiedRows.Count);
        foreach (TableRow row in modifiedRows)
        {
            var reanchoredCells = new List<TableCell>(row.Cells.Count);
            foreach (TableCell cell in row.Cells.OrderBy(c => c.ColumnIndex))
            {
                int cellCount = cell.IsRowSpanContinuation ? 0 : cell.ParagraphCount;
                int oldStart = cell.ParagraphIndex;
                int newStart = running;

                List<DocumentTable>? relocatedTables = null;
                if (cell.Tables.Count > 0)
                {
                    int delta = newStart - oldStart;
                    relocatedTables = cell.Tables.Select(t => t.ShiftedBy(delta)).ToList();
                }

                reanchoredCells.Add(new TableCell(
                    newStart,
                    cellCount,
                    cell.ColumnIndex,
                    cell.ColumnSpan,
                    cell.RowSpan,
                    cell.Shading,
                    cell.Borders,
                    cell.IsRowSpanContinuation,
                    relocatedTables,
                    cell.Padding,
                    cell.VerticalAlignment, cell.Formula, cell.NumberFormat));

                running += cellCount;
            }
            reanchoredRows.Add(new TableRow(reanchoredCells, row.IsHeader, row.MinHeight, row.CanSplit, row.ExactHeight, row.KeepWithNext));
        }

        return new DocumentTable(
            ParagraphIndex,
            running - ParagraphIndex,
            reanchoredRows,
            newWidths,
            CellPadding,
            Position,
            KeepWithNext,
            PreferredWidth,
            WidthPercent,
            Alignment,
            LeftIndent,
            PageBreakBefore,
            ColumnBreakBefore,
            AutoFit,
            KeepTogether);
    }

    /// <summary>
    /// Merges a rectangular block of cells starting at (<paramref name="startRow"/>, <paramref name="startColumn"/>)
    /// across <paramref name="rowSpan"/> rows and <paramref name="columnSpan"/> columns.
    /// Preserves all cell paragraphs and nested tables inside the root cell.
    /// </summary>
    public DocumentTable MergeCells(int startRow, int startColumn, int rowSpan, int columnSpan)
    {
        if (startRow < 0 || startRow >= Rows.Count || startColumn < 0 || rowSpan < 1 || columnSpan < 1)
            return this;

        if (rowSpan == 1 && columnSpan == 1)
            return this;

        int endRow = Math.Min(Rows.Count, startRow + rowSpan);

        TableCell? rootCell = null;
        int totalParas = 0;
        var collectedTables = new List<DocumentTable>();

        for (int r = startRow; r < endRow; r++)
        {
            foreach (TableCell cell in Rows[r].Cells)
            {
                if (cell.ColumnIndex >= startColumn && cell.ColumnIndex < startColumn + columnSpan)
                {
                    if (r == startRow && cell.ColumnIndex == startColumn)
                        rootCell = cell;

                    if (!cell.IsRowSpanContinuation)
                    {
                        totalParas += cell.ParagraphCount;
                        if (cell.Tables.Count > 0)
                            collectedTables.AddRange(cell.Tables);
                    }
                }
            }
        }

        if (rootCell is null)
            return this;

        var newRows = new List<TableRow>(Rows.Count);
        for (int r = 0; r < Rows.Count; r++)
        {
            TableRow row = Rows[r];
            if (r < startRow || r >= endRow)
            {
                newRows.Add(row);
                continue;
            }

            var newCells = new List<TableCell>(row.Cells.Count);
            if (r == startRow)
            {
                foreach (TableCell cell in row.Cells)
                {
                    if (cell.ColumnIndex == startColumn)
                    {
                        newCells.Add(new TableCell(
                            cell.ParagraphIndex,
                            totalParas,
                            startColumn,
                            columnSpan: columnSpan,
                            rowSpan: rowSpan,
                            rootCell.Shading,
                            rootCell.Borders,
                            isRowSpanContinuation: false,
                            collectedTables.Count > 0 ? collectedTables : null,
                            rootCell.Padding,
                            rootCell.VerticalAlignment));
                    }
                    else if (cell.ColumnIndex > startColumn && cell.ColumnIndex < startColumn + columnSpan)
                    {
                        continue;
                    }
                    else
                    {
                        newCells.Add(cell);
                    }
                }
            }
            else
            {
                bool continuationAdded = false;
                foreach (TableCell cell in row.Cells)
                {
                    if (cell.ColumnIndex >= startColumn && cell.ColumnIndex < startColumn + columnSpan)
                    {
                        if (!continuationAdded)
                        {
                            newCells.Add(new TableCell(
                                rootCell.ParagraphIndex,
                                paragraphCount: 0,
                                startColumn,
                                columnSpan: columnSpan,
                                rowSpan: 1,
                                rootCell.Shading,
                                rootCell.Borders,
                                isRowSpanContinuation: true,
                                tables: null,
                                rootCell.Padding,
                                rootCell.VerticalAlignment));
                            continuationAdded = true;
                        }
                    }
                    else
                    {
                        newCells.Add(cell);
                    }
                }
            }
            newRows.Add(new TableRow(newCells, row.IsHeader, row.MinHeight, row.CanSplit, row.ExactHeight, row.KeepWithNext));
        }

        int running = ParagraphIndex;
        var reanchoredRows = new List<TableRow>(newRows.Count);
        foreach (TableRow row in newRows)
        {
            var reanchoredCells = new List<TableCell>(row.Cells.Count);
            foreach (TableCell cell in row.Cells.OrderBy(c => c.ColumnIndex))
            {
                int cellCount = cell.IsRowSpanContinuation ? 0 : cell.ParagraphCount;
                int oldStart = cell.ParagraphIndex;
                int newStart = running;

                List<DocumentTable>? relocatedTables = null;
                if (cell.Tables.Count > 0)
                {
                    int delta = newStart - oldStart;
                    relocatedTables = cell.Tables.Select(t => t.ShiftedBy(delta)).ToList();
                }

                reanchoredCells.Add(new TableCell(
                    newStart,
                    cellCount,
                    cell.ColumnIndex,
                    cell.ColumnSpan,
                    cell.RowSpan,
                    cell.Shading,
                    cell.Borders,
                    cell.IsRowSpanContinuation,
                    relocatedTables,
                    cell.Padding,
                    cell.VerticalAlignment, cell.Formula, cell.NumberFormat));

                running += cellCount;
            }
            reanchoredRows.Add(new TableRow(reanchoredCells, row.IsHeader, row.MinHeight, row.CanSplit, row.ExactHeight, row.KeepWithNext));
        }

        return new DocumentTable(
            ParagraphIndex,
            running - ParagraphIndex,
            reanchoredRows,
            ColumnWidths,
            CellPadding,
            Position,
            KeepWithNext,
            PreferredWidth,
            WidthPercent,
            Alignment,
            LeftIndent,
            PageBreakBefore,
            ColumnBreakBefore,
            AutoFit,
            KeepTogether);
    }

    /// <summary>
    /// Splits the cell at (<paramref name="row"/>, <paramref name="column"/>), restoring individual cells if previously merged.
    /// </summary>
    public DocumentTable SplitCell(int row, int column, int horizontalSplit = 1, int verticalSplit = 1, int newParagraphsPerSplit = 1)
    {
        if (row < 0 || row >= Rows.Count || column < 0)
            return this;

        TableCell? targetCell = null;
        foreach (TableCell c in Rows[row].Cells)
        {
            if (c.ColumnIndex == column && !c.IsRowSpanContinuation)
            {
                targetCell = c;
                break;
            }
        }

        if (targetCell is null)
            return this;

        int rs = targetCell.RowSpan;
        int cs = targetCell.ColumnSpan;

        if (rs == 1 && cs == 1 && horizontalSplit <= 1 && verticalSplit <= 1)
            return this;

        if (rs == 1 && cs == 1 && horizontalSplit > 1)
        {
            var newRows = new List<TableRow>(Rows.Count);
            for (int r = 0; r < Rows.Count; r++)
            {
                TableRow tableRow = Rows[r];
                if (r != row)
                {
                    newRows.Add(tableRow);
                    continue;
                }

                var newCells = new List<TableCell>(tableRow.Cells.Count + horizontalSplit - 1);
                foreach (TableCell cell in tableRow.Cells)
                {
                    if (cell.ColumnIndex == column && !cell.IsRowSpanContinuation)
                    {
                        newCells.Add(new TableCell(
                            cell.ParagraphIndex,
                            Math.Max(1, cell.ParagraphCount),
                            column,
                            columnSpan: 1,
                            rowSpan: 1,
                            cell.Shading,
                            cell.Borders,
                            isRowSpanContinuation: false,
                            cell.Tables,
                            cell.Padding,
                            cell.VerticalAlignment, cell.Formula, cell.NumberFormat));

                        for (int h = 1; h < horizontalSplit; h++)
                        {
                            newCells.Add(new TableCell(
                                cell.ParagraphIndex,
                                newParagraphsPerSplit,
                                column + h,
                                columnSpan: 1,
                                rowSpan: 1,
                                cell.Shading,
                                cell.Borders,
                                isRowSpanContinuation: false,
                                tables: null,
                                cell.Padding,
                                cell.VerticalAlignment, cell.Formula, cell.NumberFormat));
                        }
                    }
                    else if (cell.ColumnIndex > column)
                    {
                        newCells.Add(new TableCell(
                            cell.ParagraphIndex,
                            cell.ParagraphCount,
                            columnIndex: cell.ColumnIndex + horizontalSplit - 1,
                            cell.ColumnSpan,
                            cell.RowSpan,
                            cell.Shading,
                            cell.Borders,
                            cell.IsRowSpanContinuation,
                            cell.Tables,
                            cell.Padding,
                            cell.VerticalAlignment, cell.Formula, cell.NumberFormat));
                    }
                    else
                    {
                        newCells.Add(cell);
                    }
                }
                newRows.Add(new TableRow(newCells, tableRow.IsHeader, tableRow.MinHeight, tableRow.CanSplit, tableRow.ExactHeight, tableRow.KeepWithNext));
            }

            int running = ParagraphIndex;
            var reanchoredRows = new List<TableRow>(newRows.Count);
            foreach (TableRow tableRow in newRows)
            {
                var reanchoredCells = new List<TableCell>(tableRow.Cells.Count);
                foreach (TableCell cell in tableRow.Cells.OrderBy(c => c.ColumnIndex))
                {
                    int cellCount = cell.IsRowSpanContinuation ? 0 : cell.ParagraphCount;
                    int oldStart = cell.ParagraphIndex;
                    int newStart = running;

                    List<DocumentTable>? relocatedTables = null;
                    if (cell.Tables.Count > 0)
                    {
                        int delta = newStart - oldStart;
                        relocatedTables = cell.Tables.Select(t => t.ShiftedBy(delta)).ToList();
                    }

                    reanchoredCells.Add(new TableCell(
                        newStart,
                        cellCount,
                        cell.ColumnIndex,
                        cell.ColumnSpan,
                        cell.RowSpan,
                        cell.Shading,
                        cell.Borders,
                        cell.IsRowSpanContinuation,
                        relocatedTables,
                        cell.Padding,
                        cell.VerticalAlignment, cell.Formula, cell.NumberFormat));

                    running += cellCount;
                }
                reanchoredRows.Add(new TableRow(reanchoredCells, tableRow.IsHeader, tableRow.MinHeight, tableRow.CanSplit, tableRow.ExactHeight, tableRow.KeepWithNext));
            }

            return new DocumentTable(
                ParagraphIndex,
                running - ParagraphIndex,
                reanchoredRows,
                ColumnWidths,
                CellPadding,
                Position,
                KeepWithNext,
                PreferredWidth,
                WidthPercent,
                Alignment,
                LeftIndent,
                PageBreakBefore,
                ColumnBreakBefore,
                AutoFit,
                KeepTogether);
        }

        if (rs > 1 || cs > 1)
        {
            var newRows = new List<TableRow>(Rows.Count);
            for (int r = 0; r < Rows.Count; r++)
            {
                TableRow tableRow = Rows[r];
                if (r < row || r >= row + rs)
                {
                    newRows.Add(tableRow);
                    continue;
                }

                var newCells = new List<TableCell>(tableRow.Cells.Count + cs);
                foreach (TableCell cell in tableRow.Cells)
                {
                    if (r == row && cell.ColumnIndex == column)
                    {
                        newCells.Add(new TableCell(
                            cell.ParagraphIndex,
                            Math.Max(1, cell.ParagraphCount),
                            column,
                            columnSpan: 1,
                            rowSpan: 1,
                            cell.Shading,
                            cell.Borders,
                            isRowSpanContinuation: false,
                            cell.Tables,
                            cell.Padding,
                            cell.VerticalAlignment, cell.Formula, cell.NumberFormat));

                        for (int c = 1; c < cs; c++)
                        {
                            newCells.Add(new TableCell(
                                cell.ParagraphIndex,
                                newParagraphsPerSplit,
                                column + c,
                                columnSpan: 1,
                                rowSpan: 1,
                                cell.Shading,
                                cell.Borders,
                                isRowSpanContinuation: false,
                                tables: null,
                                cell.Padding,
                                cell.VerticalAlignment, cell.Formula, cell.NumberFormat));
                        }
                    }
                    else if (r > row && cell.ColumnIndex == column && cell.IsRowSpanContinuation)
                    {
                        for (int c = 0; c < cs; c++)
                        {
                            newCells.Add(new TableCell(
                                cell.ParagraphIndex,
                                newParagraphsPerSplit,
                                column + c,
                                columnSpan: 1,
                                rowSpan: 1,
                                targetCell.Shading,
                                targetCell.Borders,
                                isRowSpanContinuation: false,
                                tables: null,
                                targetCell.Padding,
                                targetCell.VerticalAlignment));
                        }
                    }
                    else
                    {
                        newCells.Add(cell);
                    }
                }
                newRows.Add(new TableRow(newCells, tableRow.IsHeader, tableRow.MinHeight, tableRow.CanSplit, tableRow.ExactHeight, tableRow.KeepWithNext));
            }

            int running = ParagraphIndex;
            var reanchoredRows = new List<TableRow>(newRows.Count);
            foreach (TableRow tableRow in newRows)
            {
                var reanchoredCells = new List<TableCell>(tableRow.Cells.Count);
                foreach (TableCell cell in tableRow.Cells.OrderBy(c => c.ColumnIndex))
                {
                    int cellCount = cell.IsRowSpanContinuation ? 0 : cell.ParagraphCount;
                    int oldStart = cell.ParagraphIndex;
                    int newStart = running;

                    List<DocumentTable>? relocatedTables = null;
                    if (cell.Tables.Count > 0)
                    {
                        int delta = newStart - oldStart;
                        relocatedTables = cell.Tables.Select(t => t.ShiftedBy(delta)).ToList();
                    }

                    reanchoredCells.Add(new TableCell(
                        newStart,
                        cellCount,
                        cell.ColumnIndex,
                        cell.ColumnSpan,
                        cell.RowSpan,
                        cell.Shading,
                        cell.Borders,
                        cell.IsRowSpanContinuation,
                        relocatedTables,
                        cell.Padding,
                        cell.VerticalAlignment, cell.Formula, cell.NumberFormat));

                    running += cellCount;
                }
                reanchoredRows.Add(new TableRow(reanchoredCells, tableRow.IsHeader, tableRow.MinHeight, tableRow.CanSplit, tableRow.ExactHeight, tableRow.KeepWithNext));
            }

            return new DocumentTable(
                ParagraphIndex,
                running - ParagraphIndex,
                reanchoredRows,
                ColumnWidths,
                CellPadding,
                Position,
                KeepWithNext,
                PreferredWidth,
                WidthPercent,
                Alignment,
                LeftIndent,
                PageBreakBefore,
                ColumnBreakBefore,
                AutoFit,
                KeepTogether);
        }

        return this;
    }

    private TableGrid? _grid;

    /// <summary>
    /// Gets the 2D grid transformation and sparse matrix representation of this table.
    /// </summary>
    public TableGrid Grid => ToGrid();

    /// <summary>
    /// Constructs or returns the cached 2D grid representation of this table.
    /// </summary>
    public TableGrid ToGrid() => _grid ??= TableGrid.FromTable(this);

    /// <summary>
    /// Returns the cell covering the 2D grid coordinates (row, column), or null if empty or out of bounds.
    /// </summary>
    public TableCell? GetCellAt(int row, int column) => ToGrid().GetCellAt(row, column);

    /// <summary>
    /// Returns the root merged cell and its top-left coordinates for the 2D grid position (row, column).
    /// </summary>
    public (TableCell? RootCell, int RootRow, int RootColumn) GetMergedRoot(int row, int column) =>
        ToGrid().GetMergedRoot(row, column);

    /// <summary>
    /// Computes the effective collapsed borders at 2D grid position (row, column).
    /// </summary>
    public CellBorders GetEffectiveBorders(int row, int column) =>
        ToGrid().GetEffectiveBorders(row, column);
}

/// <summary>One row of a <see cref="DocumentTable"/>.</summary>
public sealed class TableRow(IReadOnlyList<TableCell> cells, bool isHeader = false, double minHeight = 0, bool canSplit = true,
    bool exactHeight = false, bool keepWithNext = false)
{
    public IReadOnlyList<TableCell> Cells { get; } = cells is null || cells.Count == 0 ? [] : [.. cells];

    /// <summary>True for a row the document marks as repeating at the top of a page.</summary>
    public bool IsHeader { get; } = isHeader;

    /// <summary>
    /// The height in points the row asks for, or zero when it asks for none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A minimum unless <see cref="ExactHeight"/> is true. Fixed-height rows
    /// retain their full editable content while clipping its rendered overflow.
    /// </para>
    /// <para>
    /// It matters more than a row of a data table suggests. The page-layout
    /// tables every CV and letterhead template is built from use a tall first row
    /// to place the block beneath it; without the height the row collapses to its
    /// content and everything below rides up.
    /// </para>
    /// </remarks>
    public double MinHeight { get; } = double.IsFinite(minHeight) && minHeight > 0 ? minHeight : 0;

    /// <summary>True when MinHeight is a fixed height and overflowing content is clipped visually.</summary>
    public bool ExactHeight { get; } = exactHeight && double.IsFinite(minHeight) && minHeight > 0;

    /// <summary>
    /// True when the row may break across a page: the lines of its cells that fit
    /// stay on the page and the rest continue at the top of the next one.
    /// </summary>
    /// <remarks>
    /// True by default, because it is the default of every format that states it
    /// - DOCX's <c>w:cantSplit</c>, ODF's <c>fo:keep-together</c>, RTF's
    /// <c>\trkeep</c> - and of every word processor that lays one out. A row of a
    /// long comment table can be taller than a page, and one placed whole runs
    /// off the foot of it. False keeps the row on one page whenever it fits on
    /// one.
    /// </remarks>
    public bool CanSplit { get; } = canSplit;

    /// <summary>Prevent page breaks between this row and the next row when both fit on a page.</summary>
    public bool KeepWithNext { get; } = keepWithNext;

    internal TableRow Shifted(int at, int removed, int inserted, RangeAffinity affinity = RangeAffinity.Default)
    {
        var cells = new List<TableCell>(Cells.Count);
        foreach (TableCell cell in Cells)
            cells.Add(cell.Shifted(at, removed, inserted, affinity));
        return new TableRow(cells, IsHeader, MinHeight, CanSplit, ExactHeight, KeepWithNext);
    }

    internal TableRow ShiftedBy(int paragraphOffset)
    {
        if (paragraphOffset == 0)
            return this;

        var cells = new List<TableCell>(Cells.Count);
        foreach (TableCell cell in Cells)
            cells.Add(cell.ShiftedBy(paragraphOffset));

        return new TableRow(cells, IsHeader, MinHeight, CanSplit, ExactHeight, KeepWithNext);
    }

    internal TableRow RelocateParagraphs(int sourceStart, int sourceCount, int destinationIndex)
    {
        var cells = new List<TableCell>(Cells.Count);
        foreach (TableCell cell in Cells)
            cells.Add(cell.RelocateParagraphs(sourceStart, sourceCount, destinationIndex));

        return new TableRow(cells, IsHeader, MinHeight, CanSplit, ExactHeight, KeepWithNext);
    }
}

/// <summary>
/// One cell: which paragraphs it holds, where it sits in the grid, and how it is
/// bounded and painted.
/// </summary>
public sealed class TableCell(
    int paragraphIndex,
    int paragraphCount,
    int columnIndex,
    int columnSpan = 1,
    int rowSpan = 1,
    BColor shading = default,
    CellBorders borders = default,
    bool isRowSpanContinuation = false,
    IReadOnlyList<DocumentTable>? tables = null,
    TableCellPadding? padding = null,
    CellVerticalAlignment verticalAlignment = CellVerticalAlignment.Top,
    string? formula = null,
    string? numberFormat = null,
    CellValue? value = null)
{

    /// <summary>The document paragraph this cell's text starts at.</summary>
    public int ParagraphIndex { get; } = Math.Max(0, paragraphIndex);

    /// <summary>How many paragraphs the cell holds; zero for a cell with no content of its own.</summary>
    public int ParagraphCount { get; } = Math.Max(0, paragraphCount);

    /// <summary>One past the last paragraph the cell holds.</summary>
    public int ParagraphEnd => ParagraphIndex + ParagraphCount;

    /// <summary>The grid column the cell starts in.</summary>
    public int ColumnIndex { get; } = Math.Max(0, columnIndex);

    /// <summary>How many grid columns the cell covers.</summary>
    public int ColumnSpan { get; } = Math.Max(1, columnSpan);

    /// <summary>How many rows the cell covers, counting its own.</summary>
    public int RowSpan { get; } = Math.Max(1, rowSpan);

    /// <summary>The cell's background, or <see cref="BColor.Empty"/> for none.</summary>
    public BColor Shading { get; } = shading;

    public CellBorders Borders { get; } = borders;

    /// <summary>Explicit cell insets; null uses the table's horizontal padding and no vertical inset.</summary>
    public TableCellPadding? Padding { get; } = padding;
    public CellVerticalAlignment VerticalAlignment { get; } = verticalAlignment;

    public TableCellPadding PaddingFor(DocumentTable table) =>
        Padding ?? new TableCellPadding(table.CellPadding, 0, table.CellPadding, 0);

    /// <summary>
    /// True for the lower half of a vertical merge: a cell the document writes so
    /// the row has the right number of columns, whose box the cell above covers.
    /// It is drawn by no one and holds, by the format's own rule, no text.
    /// </summary>
    public bool IsRowSpanContinuation { get; } = isRowSpanContinuation;

    /// <summary>The tables nested directly in this cell, in the order they start.</summary>
    public IReadOnlyList<DocumentTable> Tables { get; } = tables is null || tables.Count == 0 ? [] : [.. tables];

    /// <summary>Optional spreadsheet or table formula (e.g. "=SUM(ABOVE)" or "=A1+B1").</summary>
    public string? Formula { get; } = formula;

    /// <summary>Optional cell number or date format string (e.g. "$#,##0.00" or "yyyy-mm-dd").</summary>
    public string? NumberFormat { get; } = numberFormat;

    /// <summary>Optional strongly-typed cell value (number, text, boolean, error, or empty).</summary>
    public CellValue? Value { get; } = value;

    /// <summary>Returns a copy of this cell with the specified formula.</summary>
    public TableCell WithFormula(string? newFormula) =>
        new TableCell(
            ParagraphIndex,
            ParagraphCount,
            ColumnIndex,
            ColumnSpan,
            RowSpan,
            Shading,
            Borders,
            IsRowSpanContinuation,
            Tables,
            Padding,
            VerticalAlignment,
            newFormula,
            NumberFormat,
            Value);

    /// <summary>Returns a copy of this cell with the specified number format string.</summary>
    public TableCell WithNumberFormat(string? newFormat) =>
        new TableCell(
            ParagraphIndex,
            ParagraphCount,
            ColumnIndex,
            ColumnSpan,
            RowSpan,
            Shading,
            Borders,
            IsRowSpanContinuation,
            Tables,
            Padding,
            VerticalAlignment,
            Formula,
            newFormat,
            Value);

    /// <summary>Returns a copy of this cell with the specified strongly-typed cell value.</summary>
    public TableCell WithValue(CellValue? newValue) =>
        new TableCell(
            ParagraphIndex,
            ParagraphCount,
            ColumnIndex,
            ColumnSpan,
            RowSpan,
            Shading,
            Borders,
            IsRowSpanContinuation,
            Tables,
            Padding,
            VerticalAlignment,
            Formula,
            NumberFormat,
            newValue);

    internal TableCell Shifted(int at, int removed, int inserted, RangeAffinity affinity = RangeAffinity.Default)
    {
        (int index, int count) = DocumentTable.ShiftRange(ParagraphIndex, ParagraphCount, at, removed, inserted, affinity);

        List<DocumentTable>? tables = null;
        if (Tables.Count > 0)
        {
            tables = new List<DocumentTable>(Tables.Count);
            foreach (DocumentTable table in Tables)
            {
                if (table.Shifted(at, removed, inserted, affinity) is DocumentTable shifted)
                    tables.Add(shifted);
            }
        }

        return new TableCell(
            index,
            count,
            ColumnIndex,
            ColumnSpan,
            RowSpan,
            Shading,
            Borders,
            IsRowSpanContinuation,
            tables,
            Padding,
            VerticalAlignment,
            Formula,
            NumberFormat,
            Value);
    }

    internal TableCell ShiftedBy(int paragraphOffset)
    {
        if (paragraphOffset == 0)
            return this;

        List<DocumentTable>? tables = null;
        if (Tables.Count > 0)
        {
            tables = new List<DocumentTable>(Tables.Count);
            foreach (DocumentTable table in Tables)
                tables.Add(table.ShiftedBy(paragraphOffset));
        }

        return new TableCell(
            ParagraphIndex + paragraphOffset,
            ParagraphCount,
            ColumnIndex,
            ColumnSpan,
            RowSpan,
            Shading,
            Borders,
            IsRowSpanContinuation,
            tables,
            Padding,
            VerticalAlignment,
            Formula,
            NumberFormat,
            Value);
    }

    internal TableCell RelocateParagraphs(int sourceStart, int sourceCount, int destinationIndex)
    {
        (int index, int count) = DocumentTable.RelocateRange(ParagraphIndex, ParagraphCount, sourceStart, sourceCount, destinationIndex);

        List<DocumentTable>? tables = null;
        if (Tables.Count > 0)
        {
            tables = new List<DocumentTable>(Tables.Count);
            foreach (DocumentTable table in Tables)
            {
                if (table.RelocateParagraphs(sourceStart, sourceCount, destinationIndex) is DocumentTable relocated)
                    tables.Add(relocated);
            }
        }

        return new TableCell(
            index,
            count,
            ColumnIndex,
            ColumnSpan,
            RowSpan,
            Shading,
            Borders,
            IsRowSpanContinuation,
            tables,
            Padding,
            VerticalAlignment,
            Formula,
            NumberFormat,
            Value);
    }
}

/// <summary>Cell insets in points, measured inward from each border.</summary>
public readonly record struct TableCellPadding(double Left, double Top, double Right, double Bottom);

/// <summary>Where a cell places content when its row has spare height.</summary>
public enum CellVerticalAlignment { Top, Center, Bottom }

/// <summary>Style of line used to stroke a table border edge.</summary>
public enum TableBorderStyle
{
    Solid,
    Dashed,
    Dotted,
    Double,
    Groove,
    Ridge,
    Inset,
    Outset,
    None
}

/// <summary>One edge of a cell's border: its colour, thickness, and line style.</summary>
public readonly record struct TableBorder(BColor Color, double Width, TableBorderStyle Style = TableBorderStyle.Solid)
{
    /// <summary>No border at all.</summary>
    public static TableBorder None => default;

    /// <summary>A hairline border in one colour, which is what most tables state.</summary>
    public static TableBorder Solid(BColor color) => new(color, 0.5, TableBorderStyle.Solid);

    public static TableBorder Dashed(BColor color, double width = 0.5) => new(color, width, TableBorderStyle.Dashed);
    public static TableBorder Dotted(BColor color, double width = 0.5) => new(color, width, TableBorderStyle.Dotted);
    public static TableBorder Double(BColor color, double width = 1.5) => new(color, width, TableBorderStyle.Double);
    public static TableBorder Groove(BColor color, double width = 1.5) => new(color, width, TableBorderStyle.Groove);
    public static TableBorder Ridge(BColor color, double width = 1.5) => new(color, width, TableBorderStyle.Ridge);
    public static TableBorder Inset(BColor color, double width = 0.5) => new(color, width, TableBorderStyle.Inset);
    public static TableBorder Outset(BColor color, double width = 0.5) => new(color, width, TableBorderStyle.Outset);

    /// <summary>True when the edge would actually draw something.</summary>
    public bool IsVisible => Style != TableBorderStyle.None && Width > 0 && !Color.IsEmpty && Color.A > 0;

    /// <summary>
    /// Arbitrates between two competing border edges at a shared seam (border-collapse).
    /// Returns the winning border according to CSS / Table Border Collapse specification:
    /// thicker border wins; equal thickness uses style rank; tie breaks to the first border.
    /// </summary>
    public static TableBorder Arbitrate(TableBorder a, TableBorder b)
    {
        if (!a.IsVisible) return b;
        if (!b.IsVisible) return a;

        if (a.Width > b.Width + 0.01) return a;
        if (b.Width > a.Width + 0.01) return b;

        int rankA = StyleRank(a.Style);
        int rankB = StyleRank(b.Style);
        if (rankA > rankB) return a;
        if (rankB > rankA) return b;

        return a;
    }

    private static int StyleRank(TableBorderStyle style) => style switch
    {
        TableBorderStyle.Double => 8,
        TableBorderStyle.Solid => 7,
        TableBorderStyle.Dashed => 6,
        TableBorderStyle.Dotted => 5,
        TableBorderStyle.Ridge => 4,
        TableBorderStyle.Outset => 3,
        TableBorderStyle.Groove => 2,
        TableBorderStyle.Inset => 1,
        _ => 0,
    };
}

/// <summary>The four edges of a cell and optional diagonal dividing lines.</summary>
public readonly record struct CellBorders(
    TableBorder Left,
    TableBorder Top,
    TableBorder Right,
    TableBorder Bottom,
    TableBorder DiagonalDown = default,
    TableBorder DiagonalUp = default)
{
    /// <summary>The same edge on all four sides.</summary>
    public static CellBorders All(TableBorder border) => new(border, border, border, border);

    /// <summary>True when any edge would draw something.</summary>
    public bool IsVisible => Left.IsVisible || Top.IsVisible || Right.IsVisible || Bottom.IsVisible || DiagonalDown.IsVisible || DiagonalUp.IsVisible;
}
