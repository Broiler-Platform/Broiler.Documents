using System.Collections.Generic;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
using Xunit;

namespace Broiler.Documents.Model.Tests;

public sealed class SpreadsheetModelTests
{
    [Fact]
    public void SpreadsheetWorkbook_Converts_To_RichTextDocument_And_Back()
    {
        var cells1 = new List<SpreadsheetCell>
        {
            new(new CellAddress(0, 0), CellValue.FromText("Product")),
            new(new CellAddress(0, 1), CellValue.FromText("Price")),
            new(new CellAddress(1, 0), CellValue.FromText("Widget")),
            new(new CellAddress(1, 1), CellValue.FromNumber(19.99), numberFormat: "$#,##0.00"),
            new(new CellAddress(2, 0), CellValue.FromText("Total")),
            new(new CellAddress(2, 1), CellValue.FromNumber(19.99), formula: "=SUM(B2:B2)")
        };

        var row0 = new SpreadsheetRow(0, cells1.GetRange(0, 2));
        var row1 = new SpreadsheetRow(1, cells1.GetRange(2, 2));
        var row2 = new SpreadsheetRow(2, cells1.GetRange(4, 2));

        var sheet1 = new SpreadsheetWorksheet("Sales", [row0, row1, row2], [100.0, 80.0]);

        var cells2 = new List<SpreadsheetCell>
        {
            new(new CellAddress(0, 0), CellValue.FromText("Note"), columnSpan: 2, rowSpan: 1)
        };
        var sheet2 = new SpreadsheetWorksheet("Notes", [new SpreadsheetRow(0, cells2)]);

        var workbook = new SpreadsheetWorkbook([sheet1, sheet2]);

        Assert.Equal(2, workbook.Sheets.Count);
        Assert.NotNull(workbook["sales"]);
        Assert.NotNull(workbook["notes"]);
        Assert.Equal(3, sheet1.RowCount);
        Assert.Equal(2, sheet1.ColumnCount);
        Assert.Equal("Widget", sheet1[1, 0]?.Value.Text);
        Assert.Equal(19.99, sheet1["B2"]?.Value.Number);

        // Convert to RichTextDocument
        RichTextDocument doc = workbook.ToRichTextDocument();

        Assert.Equal(2, doc.Tables.Count);
        Assert.Equal("Sales", doc.Tables[0].Name);
        Assert.Equal("Notes", doc.Tables[1].Name);
        Assert.Equal("=SUM(B2:B2)", doc.Tables[0].Rows[2].Cells[1].Formula);
        Assert.Equal("$#,##0.00", doc.Tables[0].Rows[1].Cells[1].NumberFormat);

        // Convert back
        SpreadsheetWorkbook restored = SpreadsheetWorkbook.FromRichTextDocument(doc);

        Assert.Equal(2, restored.Sheets.Count);
        Assert.Equal("Sales", restored.Sheets[0].Name);
        Assert.Equal("Notes", restored.Sheets[1].Name);
        Assert.Equal(3, restored.Sheets[0].RowCount);
        Assert.Equal("Widget", restored.Sheets[0][1, 0]?.CachedText);
        Assert.Equal("=SUM(B2:B2)", restored.Sheets[0][2, 1]?.Formula);
        Assert.Equal("$#,##0.00", restored.Sheets[0][1, 1]?.NumberFormat);
    }

    [Fact]
    public void AutoFilter_And_HiddenRows_Preserved_In_RichTextDocument_Roundtrip()
    {
        var row0 = new SpreadsheetRow(0, [
            new SpreadsheetCell(new CellAddress(0, 0), CellValue.FromText("Category")),
            new SpreadsheetCell(new CellAddress(0, 1), CellValue.FromText("Value"))
        ]);
        var row1 = new SpreadsheetRow(1, [
            new SpreadsheetCell(new CellAddress(1, 0), CellValue.FromText("A")),
            new SpreadsheetCell(new CellAddress(1, 1), CellValue.FromNumber(10))
        ], hidden: false);
        var row2 = new SpreadsheetRow(2, [
            new SpreadsheetCell(new CellAddress(2, 0), CellValue.FromText("B")),
            new SpreadsheetCell(new CellAddress(2, 1), CellValue.FromNumber(20))
        ], hidden: true);

        var filterRange = new CellRangeAddress(new CellAddress(0, 0), new CellAddress(2, 1));
        var filter = new SpreadsheetAutoFilter(filterRange, [
            new SpreadsheetFilterColumn(0, ["A"], includeBlanks: false)
        ]);

        var sheet = new SpreadsheetWorksheet("Data", [row0, row1, row2])
        {
            AutoFilter = filter
        };
        var workbook = new SpreadsheetWorkbook([sheet]);

        var doc = workbook.ToRichTextDocument();
        Assert.NotNull(doc.Tables[0].AutoFilter);
        Assert.Equal("A1:B3", doc.Tables[0].AutoFilter!.Range.Format());
        Assert.False(doc.Tables[0].Rows[1].IsHidden);
        Assert.True(doc.Tables[0].Rows[2].IsHidden);

        var restored = SpreadsheetWorkbook.FromRichTextDocument(doc);
        var restoredSheet = restored.Sheets[0];
        Assert.NotNull(restoredSheet.AutoFilter);
        Assert.Equal("A1:B3", restoredSheet.AutoFilter!.Range.Format());
        Assert.Single(restoredSheet.AutoFilter.Columns);
        Assert.Equal(0, restoredSheet.AutoFilter.Columns[0].ColumnIndex);
        Assert.Equal(["A"], restoredSheet.AutoFilter.Columns[0].Values);
        Assert.False(restoredSheet.AutoFilter.Columns[0].IncludeBlanks);

        Assert.False(restoredSheet.Rows[1].Hidden);
        Assert.True(restoredSheet.Rows[2].Hidden);
    }
}
