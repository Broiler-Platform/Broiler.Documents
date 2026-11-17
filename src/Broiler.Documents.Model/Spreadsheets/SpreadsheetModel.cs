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

    /// <summary>The vertical alignment of the cell content.</summary>
    public CellVerticalAlignment VerticalAlignment { get; init; }
    public TextAlignment Alignment { get; init; }
    public InlineStyle Font { get; init; } = InlineStyle.Default;
    public CellBorders Borders { get; init; }
    public bool WrapText { get; init; }

    /// <summary>Optional user note or comment attached to this cell.</summary>
    public CellNote? Note { get; init; }

    /// <summary>Optional hyperlink attached to this cell.</summary>
    public CellHyperlink? Hyperlink { get; init; }

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

    /// <summary>Whether this row is hidden.</summary>
    public bool Hidden { get; init; }

    /// <summary>The outline level (0 for ungrouped, 1-8 for grouped levels).</summary>
    public int OutlineLevel { get; init; }

    /// <summary>Whether this row or its group is collapsed.</summary>
    public bool Collapsed { get; init; }

    /// <summary>Initializes a new instance of <see cref="SpreadsheetRow"/>.</summary>
    public SpreadsheetRow(int rowIndex, IReadOnlyList<SpreadsheetCell> cells, double? height = null, bool hidden = false, int outlineLevel = 0, bool collapsed = false)
    {
        RowIndex = Math.Max(0, rowIndex);
        Cells = cells ?? [];
        Height = height;
        Hidden = hidden;
        OutlineLevel = Math.Clamp(outlineLevel, 0, 8);
        Collapsed = collapsed;
    }

    /// <summary>Gets the cell at the specified column index, or null if none exists.</summary>
    public SpreadsheetCell? this[int column] =>
        Cells.FirstOrDefault(c => c.Address.Column == column);
}

/// <summary>
/// Represents column formatting, hidden state, and outline properties in a spreadsheet worksheet.
/// </summary>
public sealed class SpreadsheetColumn
{
    /// <summary>The 0-based column index.</summary>
    public int ColumnIndex { get; }

    /// <summary>Optional column width in points.</summary>
    public double? Width { get; }

    /// <summary>Whether this column is hidden.</summary>
    public bool Hidden { get; init; }

    /// <summary>The outline level (0 for ungrouped, 1-8 for grouped levels).</summary>
    public int OutlineLevel { get; init; }

    /// <summary>Whether this column or its group is collapsed.</summary>
    public bool Collapsed { get; init; }

    public SpreadsheetColumn(int columnIndex, double? width = null, bool hidden = false, int outlineLevel = 0, bool collapsed = false)
    {
        ColumnIndex = Math.Max(0, columnIndex);
        Width = width;
        Hidden = hidden;
        OutlineLevel = Math.Clamp(outlineLevel, 0, 8);
        Collapsed = collapsed;
    }
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

    /// <summary>Column properties (widths, hidden, outline levels).</summary>
    public IReadOnlyList<SpreadsheetColumn>? Columns { get; init; }

    /// <summary>Whether summary rows are positioned below detail rows in outlines (default true).</summary>
    public bool SummaryRowsBelow { get; init; } = true;

    /// <summary>Whether summary columns are positioned to the right of detail columns in outlines (default true).</summary>
    public bool SummaryColumnsRight { get; init; } = true;

    /// <summary>Whether gridlines are visible for this worksheet (default true).</summary>
    public bool ShowGridlines { get; init; } = true;

    /// <summary>The number of frozen top rows (0 for none).</summary>
    public int FrozenRows { get; init; }

    /// <summary>The number of frozen left columns (0 for none).</summary>
    public int FrozenColumns { get; init; }

    /// <summary>The number of rows at which the sheet viewport is split (0 for none).</summary>
    public int SplitRows { get; init; }

    /// <summary>The number of columns at which the sheet viewport is split (0 for none).</summary>
    public int SplitColumns { get; init; }

    /// <summary>Whether this worksheet has an active viewport split.</summary>
    public bool HasSplit => SplitRows > 0 || SplitColumns > 0;

    /// <summary>Conditional formatting rules applied to this worksheet.</summary>
    public IReadOnlyList<ConditionalFormat>? ConditionalFormats { get; init; }

    /// <summary>Data validation rules applied to this worksheet.</summary>
    public IReadOnlyList<DataValidation>? DataValidations { get; init; }

    /// <summary>AutoFilter configuration applied to this worksheet.</summary>
    public SpreadsheetAutoFilter? AutoFilter { get; init; }

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
            if (Columns != null && Columns.Count > 0)
            {
                max = Math.Max(max, Columns.Max(c => c.ColumnIndex) + 1);
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
    public RichTextDocument ToRichTextDocument() => ToRichTextDocument(null, true, false);

    /// <summary>
    /// Projects this workbook into a <see cref="RichTextDocument"/> where each sheet is a <see cref="DocumentTable"/>.
    /// </summary>
    public RichTextDocument ToRichTextDocument(
        PageGeometry? pageGeometry,
        bool printsSheets = true,
        bool printGridlines = false)
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
                    InlineStyle font = sCell.Font;
                    if (sCell.Hyperlink is { } hl && !font.IsLink)
                        font = font with { LinkHref = hl.Target };
                    foreach (string line in lines)
                        paragraphs.Add(RichTextParagraph.Create(line.TrimEnd('\r'), font,
                            ParagraphStyle.Default with { Alignment = sCell.Alignment }).WithMarkStyle(font));
                    int cellCount = paragraphs.Count - cellStart;

                    var borders = sCell.Borders;
                    if (printGridlines)
                    {
                        var gridline = TableBorder.Solid(new BColor(255, 218, 220, 224));
                        borders = new CellBorders(
                            Left: borders.Left.IsVisible ? borders.Left : gridline,
                            Top: borders.Top.IsVisible ? borders.Top : gridline,
                            Right: borders.Right.IsVisible ? borders.Right : gridline,
                            Bottom: borders.Bottom.IsVisible ? borders.Bottom : gridline);
                    }

                    var tableCell = new TableCell(
                        paragraphIndex: cellStart,
                        paragraphCount: cellCount,
                        columnIndex: sCell.Address.Column,
                        columnSpan: sCell.ColumnSpan,
                        rowSpan: sCell.RowSpan,
                        shading: sCell.Shading,
                        borders: borders,
                        verticalAlignment: sCell.VerticalAlignment,
                        formula: sCell.Formula,
                        numberFormat: sCell.NumberFormat,
                        value: sCell.Value,
                        wrapText: sCell.WrapText,
                        note: sCell.Note,
                        hyperlink: sCell.Hyperlink);

                    tableCells.Add(tableCell);
                }

                tableRows.Add(new TableRow(tableCells, minHeight: sRow.Height ?? 0, exactHeight: sRow.Height.HasValue, isHidden: sRow.Hidden, outlineLevel: sRow.OutlineLevel, isCollapsed: sRow.Collapsed));
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
                pageBreakBefore: tables.Count > 0,
                name: sheet.Name)
            {
                ShowGridlines = sheet.ShowGridlines,
                FrozenRows = sheet.FrozenRows,
                FrozenColumns = sheet.FrozenColumns,
                SplitRows = sheet.SplitRows,
                SplitColumns = sheet.SplitColumns,
                ConditionalFormats = sheet.ConditionalFormats,
                DataValidations = sheet.DataValidations,
                AutoFilter = sheet.AutoFilter,
                SummaryRowsBelow = sheet.SummaryRowsBelow,
                SummaryColumnsRight = sheet.SummaryColumnsRight,
                Columns = sheet.Columns,
            };

            tables.Add(table);
        }

        if (paragraphs.Count == 0)
            paragraphs.Add(RichTextParagraph.Empty);

        PageGeometry geometry = pageGeometry ?? PageGeometry.A4;
        RichTextDocument doc = RichTextDocument.FromParagraphs(paragraphs)
            .WithTables(tables)
            .WithPageGeometry(geometry)
            .WithStyleDefaults(new DocumentStyleDefaults
            {
                PrintsSheets = printsSheets,
            });

        if (tables.Count > 1)
        {
            for (int i = 1; i < tables.Count; i++)
            {
                int pIndex = tables[i].ParagraphIndex;
                if (pIndex < paragraphs.Count)
                {
                    paragraphs[pIndex] = paragraphs[pIndex].WithParagraphStyle(paragraphs[pIndex].Style with { PageBreakBefore = true });
                }
            }
            doc = RichTextDocument.FromParagraphs(paragraphs)
                .WithTables(tables)
                .WithPageGeometry(geometry)
                .WithStyleDefaults(new DocumentStyleDefaults
                {
                    PrintsSheets = printsSheets,
                });
        }

        return doc;
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
                        shading: tCell.Shading)
                    {
                        VerticalAlignment = tCell.VerticalAlignment,
                        Borders = tCell.Borders,
                        Alignment = tCell.ParagraphIndex < document.ParagraphCount
                            ? document.Paragraphs[tCell.ParagraphIndex].Style.Alignment : default,
                        Font = tCell.ParagraphIndex < document.ParagraphCount
                            ? (document.Paragraphs[tCell.ParagraphIndex].Length == 0
                                ? document.Paragraphs[tCell.ParagraphIndex].MarkStyle
                                : document.Paragraphs[tCell.ParagraphIndex].StyleAt(0)) : InlineStyle.Default,
                        WrapText = tCell.WrapText,
                        Note = tCell.Note,
                        Hyperlink = tCell.Hyperlink ?? (tCell.ParagraphIndex < document.ParagraphCount && document.Paragraphs[tCell.ParagraphIndex].StyleAt(0).IsLink
                            ? new CellHyperlink(document.Paragraphs[tCell.ParagraphIndex].StyleAt(0).LinkHref!)
                            : null),
                    };

                    cells.Add(cell);
                }

                rows.Add(new SpreadsheetRow(r, cells, tableRow.MinHeight > 0 ? tableRow.MinHeight : null, hidden: tableRow.IsHidden, outlineLevel: tableRow.OutlineLevel, collapsed: tableRow.IsCollapsed));
            }

            sheets.Add(new SpreadsheetWorksheet(sheetName, rows, table.ColumnWidths)
            {
                ShowGridlines = table.ShowGridlines,
                FrozenRows = table.FrozenRows,
                FrozenColumns = table.FrozenColumns,
                SplitRows = table.SplitRows,
                SplitColumns = table.SplitColumns,
                ConditionalFormats = table.ConditionalFormats,
                DataValidations = table.DataValidations,
                AutoFilter = table.AutoFilter,
                SummaryRowsBelow = table.SummaryRowsBelow,
                SummaryColumnsRight = table.SummaryColumnsRight,
                Columns = table.Columns,
            });
        }

        return new SpreadsheetWorkbook(sheets);
    }
}
