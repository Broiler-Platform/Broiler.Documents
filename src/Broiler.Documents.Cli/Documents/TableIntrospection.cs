using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Broiler.Documents.Model;

namespace Broiler.Documents.Cli.Documents;

/// <summary>
/// A single merged region in a table.
/// </summary>
public sealed class TableMergedRegion
{
    public string Root { get; init; } = string.Empty;
    public string Range { get; init; } = string.Empty;
    public int StartRow { get; init; }
    public int StartColumn { get; init; }
    public int RowSpan { get; init; }
    public int ColumnSpan { get; init; }
    public string Description => $"{Range} (span {RowSpan}x{ColumnSpan})";

    public JsonObject ToJson() => new()
    {
        ["root"] = Root,
        ["range"] = Range,
        ["row"] = StartRow,
        ["column"] = StartColumn,
        ["rowSpan"] = RowSpan,
        ["columnSpan"] = ColumnSpan,
    };
}

/// <summary>
/// Floating position metrics for a table.
/// </summary>
public sealed class TableFloatingPositionReport
{
    public string HorizontalAnchor { get; init; } = string.Empty;
    public double X { get; init; }
    public string VerticalAnchor { get; init; } = string.Empty;
    public double Y { get; init; }
    public string HorizontalAlignment { get; init; } = "none";
    public string VerticalAlignment { get; init; } = "none";
    public bool AllowOverlap { get; init; }
    public int ZOrder { get; init; }
    public bool BehindText { get; init; }

    public JsonObject ToJson() => new()
    {
        ["horizontalAnchor"] = HorizontalAnchor,
        ["x"] = X,
        ["verticalAnchor"] = VerticalAnchor,
        ["y"] = Y,
        ["horizontalAlignment"] = HorizontalAlignment,
        ["verticalAlignment"] = VerticalAlignment,
        ["allowOverlap"] = AllowOverlap,
        ["zOrder"] = ZOrder,
        ["behindText"] = BehindText,
    };

    public override string ToString()
    {
        string horiz = HorizontalAlignment != "none" ? HorizontalAlignment : X.ToString("0.#", CultureInfo.InvariantCulture) + " pt";
        string vert = VerticalAlignment != "none" ? VerticalAlignment : Y.ToString("0.#", CultureInfo.InvariantCulture) + " pt";
        return $"horiz: {HorizontalAnchor} {horiz}, vert: {VerticalAnchor} {vert}";
    }
}

/// <summary>
/// Cell data type distribution across a table.
/// </summary>
public sealed class TableDataTypeCounts
{
    public int Empty { get; set; }
    public int Text { get; set; }
    public int Number { get; set; }
    public int Boolean { get; set; }
    public int Date { get; set; }
    public int Time { get; set; }
    public int Currency { get; set; }
    public int Percentage { get; set; }
    public int Formula { get; set; }
    public int Error { get; set; }

    public JsonObject ToJson() => new()
    {
        ["empty"] = Empty,
        ["text"] = Text,
        ["number"] = Number,
        ["boolean"] = Boolean,
        ["date"] = Date,
        ["time"] = Time,
        ["currency"] = Currency,
        ["percentage"] = Percentage,
        ["formula"] = Formula,
        ["error"] = Error,
    };

    public string FormatSummary()
    {
        var parts = new List<string>();
        if (Text > 0) parts.Add($"{Text} text");
        if (Number > 0) parts.Add($"{Number} number");
        if (Formula > 0) parts.Add($"{Formula} formula");
        if (Boolean > 0) parts.Add($"{Boolean} boolean");
        if (Currency > 0) parts.Add($"{Currency} currency");
        if (Percentage > 0) parts.Add($"{Percentage} percentage");
        if (Date > 0) parts.Add($"{Date} date");
        if (Time > 0) parts.Add($"{Time} time");
        if (Error > 0) parts.Add($"{Error} error");
        if (Empty > 0) parts.Add($"{Empty} empty");
        return parts.Count > 0 ? string.Join(", ", parts) : "0 cells";
    }
}

/// <summary>
/// Introspection report for a single formula cell.
/// </summary>
public sealed class TableFormulaReport
{
    public string Coordinate { get; init; } = string.Empty;
    public int Row { get; init; }
    public int Column { get; init; }
    public string Expression { get; init; } = string.Empty;
    public IReadOnlyList<string> Dependencies { get; init; } = [];
    public bool HasError { get; init; }
    public string? Error { get; init; }
    public bool IsCyclic { get; init; }
    public object? EvaluatedValue { get; init; }
    public string? FormattedValue { get; init; }

    public JsonObject ToJson()
    {
        var deps = new JsonArray();
        foreach (string dep in Dependencies)
            deps.Add(dep);

        var obj = new JsonObject
        {
            ["coordinate"] = Coordinate,
            ["row"] = Row,
            ["column"] = Column,
            ["expression"] = Expression,
            ["dependencies"] = deps,
            ["hasError"] = HasError,
            ["error"] = Error,
            ["isCyclic"] = IsCyclic,
        };

        if (EvaluatedValue is not null)
        {
            obj["evaluatedValue"] = EvaluatedValue switch
            {
                double d => d,
                bool b => b,
                string s => s,
                _ => EvaluatedValue.ToString()
            };
        }
        else
        {
            obj["evaluatedValue"] = null;
        }

        if (FormattedValue is not null)
            obj["formattedValue"] = FormattedValue;

        return obj;
    }
}

/// <summary>
/// Detailed inspection for an individual table slot / cell.
/// </summary>
public sealed class TableCellReport
{
    public string Coordinate { get; init; } = string.Empty;
    public int Row { get; init; }
    public int Column { get; init; }
    public int RowSpan { get; init; } = 1;
    public int ColumnSpan { get; init; } = 1;
    public bool IsMerged => RowSpan > 1 || ColumnSpan > 1;
    public bool IsContinuation { get; init; }
    public bool IsEmpty { get; init; }
    public string Text { get; init; } = string.Empty;
    public string DataType { get; init; } = "empty";
    public string? NumberFormat { get; init; }
    public string? Formula { get; init; }
    public object? EvaluatedValue { get; init; }
    public string? FormattedValue { get; init; }

    public JsonObject ToJson()
    {
        var obj = new JsonObject
        {
            ["coordinate"] = Coordinate,
            ["row"] = Row,
            ["column"] = Column,
            ["rowSpan"] = RowSpan,
            ["columnSpan"] = ColumnSpan,
            ["isMerged"] = IsMerged,
            ["isContinuation"] = IsContinuation,
            ["isEmpty"] = IsEmpty,
            ["text"] = Text,
            ["dataType"] = DataType,
            ["numberFormat"] = NumberFormat,
            ["formula"] = Formula,
        };

        if (EvaluatedValue is not null)
        {
            obj["evaluatedValue"] = EvaluatedValue switch
            {
                double d => d,
                bool b => b,
                string s => s,
                _ => EvaluatedValue.ToString()
            };
        }
        else
        {
            obj["evaluatedValue"] = null;
        }

        if (FormattedValue is not null)
            obj["formattedValue"] = FormattedValue;

        return obj;
    }
}

/// <summary>
/// Complete introspection report for a single table or sheet.
/// </summary>
public sealed class TableIntrospectionReport
{
    public int TableIndex { get; init; }
    public string Bounds { get; init; } = string.Empty;
    public int RowCount { get; init; }
    public int ColumnCount { get; init; }
    public int ParagraphIndex { get; init; }
    public int ParagraphCount { get; init; }
    public int ParagraphEnd => ParagraphIndex + ParagraphCount;

    public int TotalSlots { get; init; }
    public int RootCells { get; init; }
    public int ContinuationSlots { get; init; }
    public int EmptySlots { get; init; }

    public IReadOnlyList<TableMergedRegion> MergedRegions { get; init; } = [];
    public IReadOnlyList<int> HeaderRows { get; init; } = [];
    public int NestedTableCount { get; init; }

    public string Alignment { get; init; } = "start";
    public double? PreferredWidth { get; init; }
    public double? WidthPercent { get; init; }
    public double TotalWidth { get; init; }
    public IReadOnlyList<double> ColumnWidths { get; init; } = [];
    public double CellPadding { get; init; }
    public bool AutoFit { get; init; }
    public bool KeepWithNext { get; init; }
    public bool KeepTogether { get; init; }
    public TableFloatingPositionReport? Position { get; init; }

    public TableDataTypeCounts DataTypeCounts { get; init; } = new();
    public IReadOnlyList<TableFormulaReport> Formulas { get; init; } = [];
    public IReadOnlyList<TableCellReport> Cells { get; init; } = [];

    public JsonObject ToJson(bool includeCells = false)
    {
        var colWidths = new JsonArray();
        foreach (double w in ColumnWidths)
            colWidths.Add(w);

        var merged = new JsonArray();
        foreach (var m in MergedRegions)
            merged.Add(m.ToJson());

        var headers = new JsonArray();
        foreach (int h in HeaderRows)
            headers.Add(h);

        var formulas = new JsonArray();
        foreach (var f in Formulas)
            formulas.Add(f.ToJson());

        var obj = new JsonObject
        {
            ["index"] = TableIndex,
            ["bounds"] = Bounds,
            ["rowCount"] = RowCount,
            ["columnCount"] = ColumnCount,
            ["paragraphIndex"] = ParagraphIndex,
            ["paragraphCount"] = ParagraphCount,
            ["slots"] = new JsonObject
            {
                ["total"] = TotalSlots,
                ["root"] = RootCells,
                ["continuation"] = ContinuationSlots,
                ["empty"] = EmptySlots,
            },
            ["mergedRegions"] = merged,
            ["headerRows"] = headers,
            ["nestedTableCount"] = NestedTableCount,
            ["layout"] = new JsonObject
            {
                ["alignment"] = Alignment,
                ["preferredWidth"] = PreferredWidth,
                ["widthPercent"] = WidthPercent,
                ["totalWidth"] = TotalWidth,
                ["columnWidths"] = colWidths,
                ["cellPadding"] = CellPadding,
                ["autoFit"] = AutoFit,
                ["keepWithNext"] = KeepWithNext,
                ["keepTogether"] = KeepTogether,
                ["position"] = Position?.ToJson(),
            },
            ["dataTypes"] = DataTypeCounts.ToJson(),
            ["formulas"] = formulas,
        };

        if (includeCells)
        {
            var cellArray = new JsonArray();
            foreach (var c in Cells)
                cellArray.Add(c.ToJson());
            obj["cells"] = cellArray;
        }

        return obj;
    }
}

/// <summary>
/// Introspects tabular and spreadsheet structures in documents.
/// </summary>
public static class TableIntrospection
{
    /// <summary>
    /// Inspects all tables in a document.
    /// </summary>
    public static IReadOnlyList<TableIntrospectionReport> InspectAll(
        RichTextDocument document,
        bool includeCells = false,
        bool evaluate = false,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var results = new List<TableIntrospectionReport>();
        for (int i = 0; i < document.Tables.Count; i++)
        {
            results.Add(Inspect(document.Tables[i], i, document, includeCells, evaluate, culture));
        }

        return results;
    }

    /// <summary>
    /// Inspects a single <see cref="DocumentTable"/>.
    /// </summary>
    public static TableIntrospectionReport Inspect(
        DocumentTable table,
        int tableIndex,
        RichTextDocument? document = null,
        bool includeCells = false,
        bool evaluate = false,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        culture ??= CultureInfo.InvariantCulture;

        TableGrid grid = TableGrid.FromTable(table);
        int rowCount = grid.RowCount;
        int colCount = grid.ColumnCount;
        string bounds = rowCount > 0 && colCount > 0
            ? $"A1:{new CellAddress(rowCount - 1, colCount - 1).Format()}"
            : "empty";

        int totalSlots = rowCount * colCount;
        int rootCells = 0;
        int continuationSlots = 0;
        int emptySlots = 0;

        TableEvaluationResult eval = TableFormulaEngine.Evaluate(table, document);

        var mergedRegions = new List<TableMergedRegion>();
        var seenMergedRoots = new HashSet<TableCell>();

        foreach (TableCell cell in grid.Cells)
        {
            if ((cell.RowSpan > 1 || cell.ColumnSpan > 1) && seenMergedRoots.Add(cell))
            {
                TableCellRange? range = grid.GetCellRange(cell);
                int r0 = range?.StartRow ?? 0;
                int c0 = range?.StartColumn ?? cell.ColumnIndex;
                int r1 = r0 + cell.RowSpan - 1;
                int c1 = c0 + cell.ColumnSpan - 1;
                string rootAddr = new CellAddress(r0, c0).Format();
                string endAddr = new CellAddress(r1, c1).Format();
                mergedRegions.Add(new TableMergedRegion
                {
                    Root = rootAddr,
                    Range = $"{rootAddr}:{endAddr}",
                    StartRow = r0,
                    StartColumn = c0,
                    RowSpan = cell.RowSpan,
                    ColumnSpan = cell.ColumnSpan,
                });
            }
        }

        var headerRows = new List<int>();
        for (int r = 0; r < table.Rows.Count; r++)
        {
            if (table.Rows[r].IsHeader)
                headerRows.Add(r);
        }

        int nestedCount = 0;
        foreach (TableCell cell in grid.Cells)
        {
            if (cell.Tables is { Count: > 0 } nested)
                nestedCount += nested.Count;
        }

        TableFloatingPositionReport? posReport = null;
        if (table.Position is TablePosition pos)
        {
            posReport = new TableFloatingPositionReport
            {
                HorizontalAnchor = pos.HorizontalAnchor.ToString().ToLowerInvariant(),
                X = pos.X,
                VerticalAnchor = pos.VerticalAnchor.ToString().ToLowerInvariant(),
                Y = pos.Y,
                HorizontalAlignment = pos.HorizontalAlignment != TableAlignment.None ? pos.HorizontalAlignment.ToString().ToLowerInvariant() : "none",
                VerticalAlignment = pos.VerticalAlignment != TableAlignment.None ? pos.VerticalAlignment.ToString().ToLowerInvariant() : "none",
                AllowOverlap = pos.AllowOverlap,
                ZOrder = pos.ZOrder,
                BehindText = pos.BehindText,
            };
        }

        var dataTypeCounts = new TableDataTypeCounts();
        var formulas = new List<TableFormulaReport>();
        var cells = new List<TableCellReport>();

        for (int r = 0; r < rowCount; r++)
        {
            for (int c = 0; c < colCount; c++)
            {
                TableGridSlot slot = grid.GetSlot(r, c);
                string coord = new CellAddress(r, c).Format();
                var cellAddress = new CellAddress(r, c);

                if (slot.IsEmpty)
                {
                    emptySlots++;
                    dataTypeCounts.Empty++;

                    if (includeCells)
                    {
                        cells.Add(new TableCellReport
                        {
                            Coordinate = coord,
                            Row = r,
                            Column = c,
                            RowSpan = 1,
                            ColumnSpan = 1,
                            IsContinuation = false,
                            IsEmpty = true,
                            Text = string.Empty,
                            DataType = "empty",
                        });
                    }
                    continue;
                }

                if (slot.IsContinuation)
                {
                    continuationSlots++;

                    if (includeCells)
                    {
                        cells.Add(new TableCellReport
                        {
                            Coordinate = coord,
                            Row = r,
                            Column = c,
                            RowSpan = slot.RowSpan,
                            ColumnSpan = slot.ColumnSpan,
                            IsContinuation = true,
                            IsEmpty = false,
                            Text = string.Empty,
                            DataType = "empty",
                        });
                    }
                    continue;
                }

                rootCells++;
                TableCell rootCell = slot.RootCell!;
                string cellText = GetCellText(rootCell, document);

                string? formulaExpr = !string.IsNullOrWhiteSpace(rootCell.Formula)
                    ? rootCell.Formula
                    : cellText.Trim().StartsWith('=')
                        ? cellText.Trim()
                        : null;

                bool isFormula = formulaExpr is not null;
                string dataType;
                object? evalValue = null;
                string? fmtValue = null;

                if (isFormula)
                {
                    var deps = eval.Dependencies.TryGetValue(cellAddress, out var depSet)
                        ? depSet
                        : TableFormulaEngine.ExtractDependencies(formulaExpr!, cellAddress, rowCount, colCount, grid);

                    bool hasErr = eval.TryGetError(cellAddress, out FormulaError formulaErr);
                    bool isCyclic = eval.CyclicCells.Contains(cellAddress);
                    string? errStr = isCyclic ? "#CYCLE!" : hasErr ? formulaErr.ToErrorString() : null;

                    CellValue cv = eval.GetValue(cellAddress);
                    if (evaluate)
                    {
                        evalValue = cv.Type switch
                        {
                            CellValueType.Number => cv.Number,
                            CellValueType.Boolean => cv.Boolean,
                            CellValueType.Text => cv.Text,
                            CellValueType.Error => cv.Error.ToErrorString(),
                            _ => null
                        };
                        fmtValue = eval.GetFormattedValue(cellAddress, culture).Text;
                    }

                    formulas.Add(new TableFormulaReport
                    {
                        Coordinate = coord,
                        Row = r,
                        Column = c,
                        Expression = formulaExpr!,
                        Dependencies = deps.Select(d => d.Format()).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                        HasError = hasErr || isCyclic,
                        Error = errStr,
                        IsCyclic = isCyclic,
                        EvaluatedValue = evalValue,
                        FormattedValue = fmtValue,
                    });

                    if (evaluate)
                    {
                        if (hasErr || isCyclic)
                        {
                            dataType = "error";
                            dataTypeCounts.Error++;
                        }
                        else
                        {
                            dataType = ClassifyEvaluatedValue(cv, rootCell.NumberFormat, dataTypeCounts);
                        }
                    }
                    else
                    {
                        dataType = "formula";
                        dataTypeCounts.Formula++;
                    }
                }
                else
                {
                    if (rootCell.ParagraphCount == 0 || string.IsNullOrWhiteSpace(cellText))
                    {
                        dataType = "empty";
                        dataTypeCounts.Empty++;
                    }
                    else
                    {
                        CellDataType inferred = CellCoercion.InferType(cellText, culture);
                        dataType = MapInferredType(inferred, dataTypeCounts);
                    }
                }

                if (includeCells)
                {
                    cells.Add(new TableCellReport
                    {
                        Coordinate = coord,
                        Row = r,
                        Column = c,
                        RowSpan = rootCell.RowSpan,
                        ColumnSpan = rootCell.ColumnSpan,
                        IsContinuation = false,
                        IsEmpty = false,
                        Text = cellText,
                        DataType = dataType,
                        NumberFormat = rootCell.NumberFormat,
                        Formula = formulaExpr,
                        EvaluatedValue = evalValue,
                        FormattedValue = fmtValue,
                    });
                }
            }
        }

        return new TableIntrospectionReport
        {
            TableIndex = tableIndex,
            Bounds = bounds,
            RowCount = rowCount,
            ColumnCount = colCount,
            ParagraphIndex = table.ParagraphIndex,
            ParagraphCount = table.ParagraphCount,
            TotalSlots = totalSlots,
            RootCells = rootCells,
            ContinuationSlots = continuationSlots,
            EmptySlots = emptySlots,
            MergedRegions = mergedRegions,
            HeaderRows = headerRows,
            NestedTableCount = nestedCount,
            Alignment = table.Alignment.ToString().ToLowerInvariant(),
            PreferredWidth = table.PreferredWidth,
            WidthPercent = table.WidthPercent,
            TotalWidth = grid.TotalWidth,
            ColumnWidths = grid.ColumnWidths,
            CellPadding = table.CellPadding,
            AutoFit = table.AutoFit,
            KeepWithNext = table.KeepWithNext,
            KeepTogether = table.KeepTogether,
            Position = posReport,
            DataTypeCounts = dataTypeCounts,
            Formulas = formulas,
            Cells = cells,
        };
    }

    private static string ClassifyEvaluatedValue(CellValue cv, string? numberFormat, TableDataTypeCounts counts)
    {
        switch (cv.Type)
        {
            case CellValueType.Empty:
                counts.Empty++;
                return "empty";
            case CellValueType.Boolean:
                counts.Boolean++;
                return "boolean";
            case CellValueType.Error:
                counts.Error++;
                return "error";
            case CellValueType.Number:
                if (numberFormat is not null)
                {
                    if (numberFormat.Contains('%'))
                    {
                        counts.Percentage++;
                        return "percentage";
                    }
                    if (numberFormat.Contains('$') || numberFormat.Contains('€') || numberFormat.Contains('£'))
                    {
                        counts.Currency++;
                        return "currency";
                    }
                    if (numberFormat.Contains("yy") || numberFormat.Contains("mm") || numberFormat.Contains("dd"))
                    {
                        counts.Date++;
                        return "date";
                    }
                    if (numberFormat.Contains("hh") || numberFormat.Contains("ss"))
                    {
                        counts.Time++;
                        return "time";
                    }
                }
                counts.Number++;
                return "number";
            default:
                counts.Text++;
                return "text";
        }
    }

    private static string MapInferredType(CellDataType inferred, TableDataTypeCounts counts)
    {
        switch (inferred)
        {
            case CellDataType.Empty:
                counts.Empty++;
                return "empty";
            case CellDataType.Error:
                counts.Error++;
                return "error";
            case CellDataType.Boolean:
                counts.Boolean++;
                return "boolean";
            case CellDataType.Integer:
            case CellDataType.Decimal:
            case CellDataType.Scientific:
                counts.Number++;
                return "number";
            case CellDataType.Currency:
                counts.Currency++;
                return "currency";
            case CellDataType.Percentage:
                counts.Percentage++;
                return "percentage";
            case CellDataType.Date:
            case CellDataType.DateTime:
                counts.Date++;
                return "date";
            case CellDataType.Time:
                counts.Time++;
                return "time";
            default:
                counts.Text++;
                return "text";
        }
    }

    /// <summary>
    /// Extracts the text content of a cell from the parent document.
    /// </summary>
    public static string GetCellText(TableCell cell, RichTextDocument? document)
    {
        if (document is null || cell.ParagraphCount <= 0 || cell.ParagraphIndex >= document.Paragraphs.Count)
            return string.Empty;

        if (cell.ParagraphCount == 1)
            return document.Paragraphs[cell.ParagraphIndex].Text;

        var sb = new StringBuilder();
        for (int i = 0; i < cell.ParagraphCount; i++)
        {
            int pIdx = cell.ParagraphIndex + i;
            if (pIdx < document.Paragraphs.Count)
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(document.Paragraphs[pIdx].Text);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Formats all tables in a document as a sequence of clean ASCII/Unicode text grids.
    /// </summary>
    public static string RenderAllGrids(RichTextDocument document, bool evaluate = false, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Tables.Count == 0)
            return "No tables found in document.";

        var sb = new StringBuilder();
        for (int i = 0; i < document.Tables.Count; i++)
        {
            if (i > 0)
                sb.AppendLine();

            DocumentTable table = document.Tables[i];
            TableGrid grid = TableGrid.FromTable(table);
            sb.AppendLine($"Table {i} ({grid.RowCount}x{grid.ColumnCount}):");
            sb.Append(RenderGrid(table, document, evaluate, culture));
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Renders a single table as a tabular ASCII/Unicode box-drawing grid.
    /// </summary>
    public static string RenderGrid(DocumentTable table, RichTextDocument? document = null, bool evaluate = false, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        culture ??= CultureInfo.InvariantCulture;

        TableGrid grid = TableGrid.FromTable(table);
        int rowCount = grid.RowCount;
        int colCount = grid.ColumnCount;

        if (rowCount == 0 || colCount == 0)
            return "(empty table)\n";

        TableEvaluationResult? eval = evaluate ? TableFormulaEngine.Evaluate(table, document) : null;

        // Determine column widths
        // Row number column width
        int rowHeaderWidth = Math.Max(2, rowCount.ToString(CultureInfo.InvariantCulture).Length);
        int[] colWidths = new int[colCount];

        for (int c = 0; c < colCount; c++)
        {
            string colLetter = CellAddress.ColumnToLetters(c);
            colWidths[c] = Math.Max(3, colLetter.Length);
        }

        for (int r = 0; r < rowCount; r++)
        {
            for (int c = 0; c < colCount; c++)
            {
                TableGridSlot slot = grid.GetSlot(r, c);
                string text = "";
                if (slot.IsRoot && slot.RootCell is TableCell cell)
                {
                    if (evaluate && eval is not null)
                        text = eval.GetFormattedValue(r, c, culture).Text;
                    else
                        text = GetCellText(cell, document).Replace("\r", "").Replace("\n", " ");
                }
                else if (slot.IsContinuation)
                {
                    text = "";
                }

                if (text.Length > 40)
                    text = text[..37] + "...";

                if (text.Length > colWidths[c])
                    colWidths[c] = text.Length;
            }
        }

        var sb = new StringBuilder();

        // Top line
        sb.Append("┌").Append(new string('─', rowHeaderWidth + 2));
        for (int c = 0; c < colCount; c++)
            sb.Append('┬').Append(new string('─', colWidths[c] + 2));
        sb.AppendLine("┐");

        // Column letters header row
        sb.Append("│ ").Append(new string(' ', rowHeaderWidth)).Append(" ");
        for (int c = 0; c < colCount; c++)
        {
            string colLetter = CellAddress.ColumnToLetters(c);
            sb.Append("│ ").Append(colLetter.PadRight(colWidths[c])).Append(' ');
        }
        sb.AppendLine("│");

        // Header separator
        sb.Append("├").Append(new string('─', rowHeaderWidth + 2));
        for (int c = 0; c < colCount; c++)
            sb.Append('┼').Append(new string('─', colWidths[c] + 2));
        sb.AppendLine("┤");

        // Data rows
        for (int r = 0; r < rowCount; r++)
        {
            string rowNum = (r + 1).ToString(CultureInfo.InvariantCulture).PadLeft(rowHeaderWidth);
            sb.Append("│ ").Append(rowNum).Append(' ');

            for (int c = 0; c < colCount; c++)
            {
                TableGridSlot slot = grid.GetSlot(r, c);
                string text = "";
                if (slot.IsRoot && slot.RootCell is TableCell cell)
                {
                    if (evaluate && eval is not null)
                        text = eval.GetFormattedValue(r, c, culture).Text;
                    else
                        text = GetCellText(cell, document).Replace("\r", "").Replace("\n", " ");
                }

                if (text.Length > 40)
                    text = text[..37] + "...";

                sb.Append("│ ").Append(text.PadRight(colWidths[c])).Append(' ');
            }
            sb.AppendLine("│");

            // Row separator if header row or if last row
            if (r < rowCount - 1 && table.Rows.Count > r && table.Rows[r].IsHeader)
            {
                sb.Append("╞").Append(new string('═', rowHeaderWidth + 2));
                for (int c = 0; c < colCount; c++)
                    sb.Append('╪').Append(new string('═', colWidths[c] + 2));
                sb.AppendLine("╡");
            }
        }

        // Bottom line
        sb.Append("└").Append(new string('─', rowHeaderWidth + 2));
        for (int c = 0; c < colCount; c++)
            sb.Append('┴').Append(new string('─', colWidths[c] + 2));
        sb.AppendLine("┘");

        return sb.ToString();
    }
}
