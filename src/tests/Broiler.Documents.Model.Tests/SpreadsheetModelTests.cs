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
}
