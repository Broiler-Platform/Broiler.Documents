using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Model.Spreadsheets;

/// <summary>
/// Represents a single cell in a spreadsheet worksheet.
/// </summary>
public sealed class SpreadsheetCell
{
    /// <summary>The grid coordinate of this cell.</summary>
    public CellAddress Address { get; }

    /// <summary>The typed value stored in this cell.</summary>
    public CellValue Value { get; }

    /// <summary>Optional formula for this cell (e.g. "=SUM(A1:B10)").</summary>
    public string? Formula { get; }

    /// <summary>Optional number or date format string (e.g. "$#,##0.00" or "yyyy-mm-dd").</summary>
    public string? NumberFormat { get; }

    /// <summary>The cached or formatted visual text representation of this cell.</summary>
    public string CachedText { get; }

    /// <summary>How many columns this cell spans.</summary>
    public int ColumnSpan { get; }

    /// <summary>How many rows this cell spans.</summary>
    public int RowSpan { get; }

    /// <summary>The cell's background shading color.</summary>
    public BColor Shading { get; }

    /// <summary>Initializes a new instance of <see cref="SpreadsheetCell"/>.</summary>
    public SpreadsheetCell(
        CellAddress address,
        CellValue value,
        string? formula = null,
        string? numberFormat = null,
        string? cachedText = null,
        int columnSpan = 1,
        int rowSpan = 1,
        BColor shading = default)
    {
        Address = address;
        Value = value;
        Formula = formula;
        NumberFormat = numberFormat;
        CachedText = cachedText ?? (value.IsText ? value.Text ?? string.Empty : value.ToString());
        ColumnSpan = Math.Max(1, columnSpan);
        RowSpan = Math.Max(1, rowSpan);
        Shading = shading;
    }
}

/// <summary>
/// Represents a row of cells in a spreadsheet worksheet.
/// </summary>
public sealed class SpreadsheetRow
{
    /// <summary>The 0-based row index within the worksheet.</summary>
    public int RowIndex { get; }

    /// <summary>The cells contained in this row.</summary>
    public IReadOnlyList<SpreadsheetCell> Cells { get; }

    /// <summary>Optional explicit row height in points.</summary>
    public double? Height { get; }

    /// <summary>Initializes a new instance of <see cref="SpreadsheetRow"/>.</summary>
    public SpreadsheetRow(int rowIndex, IReadOnlyList<SpreadsheetCell> cells, double? height = null)
    {
        RowIndex = Math.Max(0, rowIndex);
        Cells = cells ?? [];
        Height = height;
    }

    /// <summary>Gets the cell at the specified column index, or null if none exists.</summary>
    public SpreadsheetCell? this[int column] =>
        Cells.FirstOrDefault(c => c.Address.Column == column);
}

/// <summary>
/// Represents a worksheet in a spreadsheet workbook.
/// </summary>
public sealed class SpreadsheetWorksheet
{
    /// <summary>The name of the worksheet (e.g. "Sheet1").</summary>
    public string Name { get; }

    /// <summary>The rows contained in this worksheet.</summary>
    public IReadOnlyList<SpreadsheetRow> Rows { get; }

    /// <summary>Column widths in points.</summary>
    public IReadOnlyList<double> ColumnWidths { get; }

    /// <summary>Gets the total row count in this worksheet.</summary>
    public int RowCount => Rows.Count == 0 ? 0 : Rows.Max(r => r.RowIndex) + 1;

    /// <summary>Gets the total column count in this worksheet.</summary>
    public int ColumnCount
    {
        get
        {
            int max = 0;
            foreach (SpreadsheetRow row in Rows)
            {
                foreach (SpreadsheetCell cell in row.Cells)
                {
                    max = Math.Max(max, cell.Address.Column + cell.ColumnSpan);
                }
            }
            return max;
        }
    }

    /// <summary>Initializes a new instance of <see cref="SpreadsheetWorksheet"/>.</summary>
    public SpreadsheetWorksheet(string name, IReadOnlyList<SpreadsheetRow> rows, IReadOnlyList<double>? columnWidths = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "Sheet1" : name.Trim();
        Rows = rows ?? [];
        ColumnWidths = columnWidths ?? [];
    }

    /// <summary>Gets the cell at the specified 0-based row and column coordinate, or null if not found.</summary>
    public SpreadsheetCell? this[int row, int column]
    {
        get
        {
            SpreadsheetRow? targetRow = Rows.FirstOrDefault(r => r.RowIndex == row);
            return targetRow?[column];
        }
    }

    /// <summary>Gets the cell at the specified A1-notation cell address (e.g. "A1", "B3"), or null if not found.</summary>
    public SpreadsheetCell? this[string a1Address]
    {
        get
        {
            if (CellAddress.TryParse(a1Address, out CellAddress addr))
                return this[addr.Row, addr.Column];
            return null;
        }
    }
}

/// <summary>
/// Represents an in-memory workbook containing one or more worksheets.
/// </summary>
public sealed class SpreadsheetWorkbook
{
    /// <summary>The worksheets in this workbook.</summary>
    public IReadOnlyList<SpreadsheetWorksheet> Sheets { get; }

    /// <summary>Initializes a new instance of <see cref="SpreadsheetWorkbook"/>.</summary>
    public SpreadsheetWorkbook(IReadOnlyList<SpreadsheetWorksheet> sheets)
    {
        Sheets = sheets is null || sheets.Count == 0
            ? [new SpreadsheetWorksheet("Sheet1", [])]
            : [.. sheets];
    }

    /// <summary>Gets the worksheet with the specified name (case-insensitive), or null if not found.</summary>
    public SpreadsheetWorksheet? this[string name] =>
        Sheets.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Gets the worksheet at the specified 0-based index.</summary>
    public SpreadsheetWorksheet this[int index] => Sheets[index];

    /// <summary>
    /// Projects this workbook into a <see cref="RichTextDocument"/> where each sheet is a <see cref="DocumentTable"/>.
    /// </summary>
    public RichTextDocument ToRichTextDocument()
    {
        var paragraphs = new List<RichTextParagraph>();
        var tables = new List<DocumentTable>();

        foreach (SpreadsheetWorksheet sheet in Sheets)
        {
            int tableStartParagraph = paragraphs.Count;
            var tableRows = new List<TableRow>();

            foreach (SpreadsheetRow sRow in sheet.Rows)
            {
                var tableCells = new List<TableCell>();
                foreach (SpreadsheetCell sCell in sRow.Cells)
                {
                    int cellStart = paragraphs.Count;
                    string text = sCell.CachedText ?? string.Empty;
                    string[] lines = text.Split('\n');
                    if (lines.Length == 0 || (lines.Length == 1 && lines[0].Length == 0))
                    {
                        paragraphs.Add(RichTextParagraph.Empty);
                    }
                    else
                    {
                        foreach (string line in lines)
                        {
                            paragraphs.Add(RichTextParagraph.Plain(line.TrimEnd('\r')));
                        }
                    }
                    int cellCount = paragraphs.Count - cellStart;

                    var tableCell = new TableCell(
                        paragraphIndex: cellStart,
                        paragraphCount: cellCount,
                        columnIndex: sCell.Address.Column,
                        columnSpan: sCell.ColumnSpan,
                        rowSpan: sCell.RowSpan,
                        shading: sCell.Shading,
                        formula: sCell.Formula,
                        numberFormat: sCell.NumberFormat,
                        value: sCell.Value);

                    tableCells.Add(tableCell);
                }

                tableRows.Add(new TableRow(tableCells, minHeight: sRow.Height ?? 0, exactHeight: sRow.Height.HasValue));
            }

            int tableParagraphCount = paragraphs.Count - tableStartParagraph;
            if (tableParagraphCount == 0)
            {
                paragraphs.Add(RichTextParagraph.Empty);
                tableParagraphCount = 1;
            }

            var table = new DocumentTable(
                paragraphIndex: tableStartParagraph,
                paragraphCount: tableParagraphCount,
                rows: tableRows,
                columnWidths: sheet.ColumnWidths,
                name: sheet.Name);

            tables.Add(table);
        }

        if (paragraphs.Count == 0)
            paragraphs.Add(RichTextParagraph.Empty);

        return RichTextDocument.FromParagraphs(paragraphs).WithTables(tables);
    }

    /// <summary>
    /// Reconstructs a <see cref="SpreadsheetWorkbook"/> from the tables in a <see cref="RichTextDocument"/>.
    /// </summary>
    public static SpreadsheetWorkbook FromRichTextDocument(RichTextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Tables.Count == 0)
        {
            var rows = new List<SpreadsheetRow>();
            for (int i = 0; i < document.Paragraphs.Count; i++)
            {
                string text = document.Paragraphs[i].Text;
                var cell = new SpreadsheetCell(
                    new CellAddress(i, 0),
                    CellValue.Parse(text),
                    cachedText: text);
                rows.Add(new SpreadsheetRow(i, [cell]));
            }
            return new SpreadsheetWorkbook([new SpreadsheetWorksheet("Sheet1", rows)]);
        }

        var sheets = new List<SpreadsheetWorksheet>();
        for (int sheetIndex = 0; sheetIndex < document.Tables.Count; sheetIndex++)
        {
            DocumentTable table = document.Tables[sheetIndex];
            string sheetName = !string.IsNullOrWhiteSpace(table.Name)
                ? table.Name
                : (table.ParagraphIndex > 0 && sheetIndex > 0
                    ? document.Paragraphs[table.ParagraphIndex - 1].Text.Trim()
                    : $"Sheet{sheetIndex + 1}");

            if (string.IsNullOrWhiteSpace(sheetName))
                sheetName = $"Sheet{sheetIndex + 1}";

            var rows = new List<SpreadsheetRow>();
            for (int r = 0; r < table.Rows.Count; r++)
            {
                TableRow tableRow = table.Rows[r];
                var cells = new List<SpreadsheetCell>();

                for (int c = 0; c < tableRow.Cells.Count; c++)
                {
                    TableCell tCell = tableRow.Cells[c];
                    if (tCell.IsRowSpanContinuation)
                        continue;

                    var sb = new StringBuilder();
                    for (int p = 0; p < tCell.ParagraphCount; p++)
                    {
                        int pIdx = tCell.ParagraphIndex + p;
                        if (pIdx < document.ParagraphCount)
                        {
                            if (sb.Length > 0) sb.Append('\n');
                            sb.Append(document.Paragraphs[pIdx].Text);
                        }
                    }
                    string cellText = sb.ToString();

                    CellValue val = tCell.Value ?? CellValue.Parse(cellText);
                    var cell = new SpreadsheetCell(
                        new CellAddress(r, tCell.ColumnIndex),
                        val,
                        formula: tCell.Formula,
                        numberFormat: tCell.NumberFormat,
                        cachedText: cellText,
                        columnSpan: tCell.ColumnSpan,
                        rowSpan: tCell.RowSpan,
                        shading: tCell.Shading);

                    cells.Add(cell);
                }

                rows.Add(new SpreadsheetRow(r, cells, tableRow.MinHeight > 0 ? tableRow.MinHeight : null));
            }

            sheets.Add(new SpreadsheetWorksheet(sheetName, rows, table.ColumnWidths));
        }

        return new SpreadsheetWorkbook(sheets);
    }
}
