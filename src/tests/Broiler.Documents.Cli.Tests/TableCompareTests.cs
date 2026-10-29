using System.Globalization;
using System.Text.Json.Nodes;
using Broiler.Documents.Cli.Commands;
using Broiler.Documents.Cli.Comparison;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Cli.Tests;

public sealed class TableCompareTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public void Compare_Detects_Table_Count_Mismatch()
    {
        var docWithTable = RichTextDocument.FromPlainText("Cell 1\nCell 2")
            .WithTables([new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])])]);

        var docWithoutTable = RichTextDocument.FromPlainText("Cell 1\nCell 2");

        var comp = DocumentComparison.Compare(docWithTable, docWithoutTable, new ComparisonOptions());

        Assert.False(comp.Equal);
        var diff = Assert.Single(comp.Differences, d => d.Kind == "table.grid-mismatch");
        Assert.Contains("table count differs: 1 vs 0", diff.Detail);
        Assert.Null(diff.TableIndex);
        Assert.Null(diff.CellCoordinate);
        Assert.StartsWith("document:", diff.ToString());

        JsonObject json = diff.ToJson();
        Assert.Equal("table.grid-mismatch", json["kind"]!.GetValue<string>());
        Assert.False(json.ContainsKey("tableIndex"));
    }

    [Fact]
    public void Compare_Detects_Grid_Dimension_Mismatch()
    {
        // Left: 2 rows x 3 columns (6 cells)
        var leftTable = new DocumentTable(0, 6, [
            new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1), new TableCell(2, 1, 2)]),
            new TableRow([new TableCell(3, 1, 0), new TableCell(4, 1, 1), new TableCell(5, 1, 2)]),
        ]);
        var left = RichTextDocument.FromPlainText("A\nB\nC\nD\nE\nF").WithTables([leftTable]);

        // Right: 2 rows x 2 columns (4 cells)
        var rightTable = new DocumentTable(0, 4, [
            new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)]),
            new TableRow([new TableCell(2, 1, 0), new TableCell(3, 1, 1)]),
        ]);
        var right = RichTextDocument.FromPlainText("A\nB\nD\nE").WithTables([rightTable]);

        var comp = DocumentComparison.Compare(left, right, new ComparisonOptions());

        Assert.False(comp.Equal);
        var diff = Assert.Single(comp.Differences, d => d.Kind == "table.grid-mismatch");
        Assert.Equal(0, diff.TableIndex);
        Assert.Null(diff.CellCoordinate);
        Assert.Contains("grid dimensions differ: 2x3 vs 2x2", diff.Detail);
        Assert.StartsWith("table 0:", diff.ToString());

        JsonObject json = diff.ToJson();
        Assert.Equal(0, json["tableIndex"]!.GetValue<int>());
    }

    [Fact]
    public void Compare_Detects_Cell_Span_Mismatch_ColumnSpan()
    {
        // Left: 1 row, 2 cols, A1 spans 2 cols (colSpan: 2)
        var leftTable = new DocumentTable(0, 1, [
            new TableRow([new TableCell(0, 1, 0, columnSpan: 2)]),
        ]);
        var left = RichTextDocument.FromPlainText("Merged").WithTables([leftTable]);

        // Right: 1 row, 2 cols, unmerged (A1 colSpan: 1, B1 colSpan: 1)
        var rightTable = new DocumentTable(0, 2, [
            new TableRow([new TableCell(0, 1, 0, columnSpan: 1), new TableCell(1, 1, 1, columnSpan: 1)]),
        ]);
        var right = RichTextDocument.FromPlainText("Merged\nRight").WithTables([rightTable]);

        var comp = DocumentComparison.Compare(left, right, new ComparisonOptions());

        Assert.False(comp.Equal);
        var diff = Assert.Single(comp.Differences, d => d.Kind == "table.cell-span-mismatch");
        Assert.Equal(0, diff.TableIndex);
        Assert.Equal("A1", diff.CellCoordinate);
        Assert.Contains("column span 2 vs 1", diff.Detail);
        Assert.StartsWith("table 0, cell A1:", diff.ToString());

        JsonObject json = diff.ToJson();
        Assert.Equal("table.cell-span-mismatch", json["kind"]!.GetValue<string>());
        Assert.Equal(0, json["tableIndex"]!.GetValue<int>());
        Assert.Equal("A1", json["cellCoordinate"]!.GetValue<string>());
    }

    [Fact]
    public void Compare_Detects_Cell_Span_Mismatch_RowSpan()
    {
        // Left: 2 rows, 1 col, A1 spans 2 rows
        var leftTable = new DocumentTable(0, 1, [
            new TableRow([new TableCell(0, 1, 0, rowSpan: 2)]),
            new TableRow([]),
        ]);
        var left = RichTextDocument.FromPlainText("Merged").WithTables([leftTable]);

        // Right: 2 rows, 1 col, separate cells
        var rightTable = new DocumentTable(0, 2, [
            new TableRow([new TableCell(0, 1, 0, rowSpan: 1)]),
            new TableRow([new TableCell(1, 1, 0, rowSpan: 1)]),
        ]);
        var right = RichTextDocument.FromPlainText("Merged\nRow2").WithTables([rightTable]);

        var comp = DocumentComparison.Compare(left, right, new ComparisonOptions());

        Assert.False(comp.Equal);
        var diff = Assert.Single(comp.Differences, d => d.Kind == "table.cell-span-mismatch");
        Assert.Equal(0, diff.TableIndex);
        Assert.Equal("A1", diff.CellCoordinate);
        Assert.Contains("row span 2 vs 1", diff.Detail);
    }

    [Fact]
    public void Compare_Detects_Table_Layout_Differences_When_Not_Ignored()
    {
        var border = TableBorder.Solid(BColor.Black);

        // Left table: alignment Center, padding 10, column widths [100, 200], cell with borders
        var leftTable = new DocumentTable(
            0, 2,
            [new TableRow([
                new TableCell(0, 1, 0, borders: CellBorders.All(border)),
                new TableCell(1, 1, 1)
            ])],
            columnWidths: [100.0, 200.0],
            cellPadding: 10.0,
            alignment: TableAlignment.Center);
        var left = RichTextDocument.FromPlainText("A\nB").WithTables([leftTable]);

        // Right table: alignment Start, padding 5.4, column widths [150, 200], cell with no borders
        var rightTable = new DocumentTable(
            0, 2,
            [new TableRow([
                new TableCell(0, 1, 0, borders: default),
                new TableCell(1, 1, 1)
            ])],
            columnWidths: [150.0, 200.0],
            cellPadding: 5.4,
            alignment: TableAlignment.Start);
        var right = RichTextDocument.FromPlainText("A\nB").WithTables([rightTable]);

        var comp = DocumentComparison.Compare(left, right, new ComparisonOptions { IgnoreTableLayout = false });

        Assert.False(comp.Equal);
        var layoutDiffs = comp.Differences.Where(d => d.Kind == "table.cell-layout").ToList();
        Assert.True(layoutDiffs.Count >= 4);

        // Check alignment
        Assert.Contains(layoutDiffs, d => d.Detail.Contains("alignment Center vs Start"));
        // Check padding
        Assert.Contains(layoutDiffs, d => d.Detail.Contains("padding 10 vs 5.4"));
        // Check column width
        var colDiff = Assert.Single(layoutDiffs, d => d.CellCoordinate == "A");
        Assert.Contains("column A width 100 vs 150", colDiff.Detail);
        // Check borders
        Assert.Contains(layoutDiffs, d => d.CellCoordinate == "A1" && d.Detail == "borders differ");
        // Check cell padding
        Assert.Contains(layoutDiffs, d => d.CellCoordinate == "A1" && d.Detail == "cell padding differs");
    }

    [Fact]
    public void Compare_Ignores_Table_Padding_That_No_Cell_Takes()
    {
        // An ODT states no table default: it writes the default on each cell
        // and reads the table's back as 0, the cells' padding unchanged.
        var padding = new TableCellPadding(5.4, 0, 5.4, 0);
        DocumentTable Table(double cellPadding, TableCellPadding? cellOwn) => new(
            0, 2,
            [new TableRow([new TableCell(0, 1, 0, padding: cellOwn), new TableCell(1, 1, 1, padding: padding)])],
            columnWidths: [100.0, 200.0],
            cellPadding: cellPadding);
        RichTextDocument Document(DocumentTable table) => RichTextDocument.FromPlainText("A\nB").WithTables([table]);
        var options = new ComparisonOptions { IgnoreTableLayout = false };

        Assert.True(DocumentComparison.Compare(Document(Table(5.4, padding)), Document(Table(0, padding)), options).Equal);
        Assert.True(DocumentComparison.Compare(Document(Table(5.4, null)), Document(Table(0, padding)), options).Equal);

        DocumentComparison taken = DocumentComparison.Compare(Document(Table(5.4, null)), Document(Table(0, null)), options);
        Assert.Contains(taken.Differences, d => d.Detail == "padding 5.4 vs 0");
        Assert.Contains(taken.Differences, d => d.CellCoordinate == "A1" && d.Detail == "cell padding differs");
    }

    [Fact]
    public void Compare_Suppresses_Table_Layout_When_Ignore_Table_Layout_Is_True()
    {
        var border = TableBorder.Solid(BColor.Black);

        var leftTable = new DocumentTable(
            0, 2,
            [new TableRow([
                new TableCell(0, 1, 0, borders: CellBorders.All(border)),
                new TableCell(1, 1, 1)
            ])],
            columnWidths: [100.0, 200.0],
            cellPadding: 10.0,
            alignment: TableAlignment.Center);
        var left = RichTextDocument.FromPlainText("A\nB").WithTables([leftTable]);

        var rightTable = new DocumentTable(
            0, 2,
            [new TableRow([
                new TableCell(0, 1, 0, borders: default),
                new TableCell(1, 1, 1)
            ])],
            columnWidths: [150.0, 200.0],
            cellPadding: 5.4,
            alignment: TableAlignment.Start);
        var right = RichTextDocument.FromPlainText("A\nB").WithTables([rightTable]);

        var comp = DocumentComparison.Compare(left, right, new ComparisonOptions { IgnoreTableLayout = true });

        Assert.DoesNotContain(comp.Differences, d => d.Kind == "table.cell-layout");
        Assert.True(comp.Equal);
    }

    [Fact]
    public void Compare_Detects_Cell_Value_Difference_With_Coordinate_Notation()
    {
        // 4 rows x 3 columns table: row 3, col 2 is "C4"
        var leftRows = new List<TableRow>();
        var rightRows = new List<TableRow>();
        var leftParagraphs = new List<string>();
        var rightParagraphs = new List<string>();

        int p = 0;
        for (int r = 0; r < 4; r++)
        {
            var leftCells = new List<TableCell>();
            var rightCells = new List<TableCell>();
            for (int c = 0; c < 3; c++)
            {
                string text = $"R{r}C{c}";
                string leftVal = (r == 3 && c == 2) ? "TargetValue" : text;
                string rightVal = (r == 3 && c == 2) ? "ActualValue" : text;

                leftParagraphs.Add(leftVal);
                rightParagraphs.Add(rightVal);

                leftCells.Add(new TableCell(p, 1, c));
                rightCells.Add(new TableCell(p, 1, c));
                p++;
            }
            leftRows.Add(new TableRow(leftCells));
            rightRows.Add(new TableRow(rightCells));
        }

        var left = RichTextDocument.FromPlainText(string.Join("\n", leftParagraphs))
            .WithTables([new DocumentTable(0, 12, leftRows)]);
        var right = RichTextDocument.FromPlainText(string.Join("\n", rightParagraphs))
            .WithTables([new DocumentTable(0, 12, rightRows)]);

        var comp = DocumentComparison.Compare(left, right, new ComparisonOptions());

        Assert.False(comp.Equal);
        var diff = Assert.Single(comp.Differences);
        Assert.Equal("table.cell-value", diff.Kind);
        Assert.Equal(0, diff.TableIndex);
        Assert.Equal("C4", diff.CellCoordinate);
        Assert.Contains("text differs: \"TargetValue\" vs \"ActualValue\"", diff.Detail);
        Assert.Equal("table 0, cell C4: text differs: \"TargetValue\" vs \"ActualValue\"", diff.ToString());

        JsonObject json = diff.ToJson();
        Assert.Equal("table.cell-value", json["kind"]!.GetValue<string>());
        Assert.Equal(0, json["tableIndex"]!.GetValue<int>());
        Assert.Equal("C4", json["cellCoordinate"]!.GetValue<string>());
    }

    [Fact]
    public void Compare_Honors_Numeric_Tolerance_When_Within_Delta()
    {
        var leftTable = new DocumentTable(0, 2, [
            new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])
        ]);
        var rightTable = new DocumentTable(0, 2, [
            new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])
        ]);

        var left = RichTextDocument.FromPlainText("Label\n100.005").WithTables([leftTable]);
        var right = RichTextDocument.FromPlainText("Label\n100.008").WithTables([rightTable]);

        // Delta is 0.003
        var compWithTolerance = DocumentComparison.Compare(
            left, right, new ComparisonOptions { NumericTolerance = 0.01 });

        Assert.True(compWithTolerance.Equal);
        Assert.Empty(compWithTolerance.Differences);

        // Outside tighter tolerance 0.001
        var compStrict = DocumentComparison.Compare(
            left, right, new ComparisonOptions { NumericTolerance = 0.001 });

        Assert.False(compStrict.Equal);
        var diff = Assert.Single(compStrict.Differences);
        Assert.Equal("table.cell-value", diff.Kind);
        Assert.Equal("B1", diff.CellCoordinate);
        Assert.Contains("value differs", diff.Detail);
    }

    [Fact]
    public void Compare_Honors_Ignore_Whitespace_And_Case_In_Cell_Values()
    {
        var leftTable = new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)])]);
        var rightTable = new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)])]);

        var left = RichTextDocument.FromPlainText("  Hello   World  ").WithTables([leftTable]);
        var right = RichTextDocument.FromPlainText("hello world").WithTables([rightTable]);

        var compDefault = DocumentComparison.Compare(left, right, new ComparisonOptions());
        Assert.False(compDefault.Equal);

        var compIgnored = DocumentComparison.Compare(
            left, right, new ComparisonOptions { IgnoreWhitespace = true, IgnoreCase = true });
        Assert.True(compIgnored.Equal);
        Assert.Empty(compIgnored.Differences);
    }

    [Fact]
    public void Compare_Detects_Formula_Differences()
    {
        var leftTable = new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0, formula: "=SUM(A1:A5)")])]);
        var rightTable = new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0, formula: "=AVERAGE(A1:A5)")])]);

        var left = RichTextDocument.FromPlainText("50").WithTables([leftTable]);
        var right = RichTextDocument.FromPlainText("50").WithTables([rightTable]);

        var comp = DocumentComparison.Compare(left, right, new ComparisonOptions());

        Assert.False(comp.Equal);
        var diff = Assert.Single(comp.Differences);
        Assert.Equal("table.cell-value", diff.Kind);
        Assert.Equal("A1", diff.CellCoordinate);
        Assert.Contains("formula \"=SUM(A1:A5)\" vs \"=AVERAGE(A1:A5)\"", diff.Detail);
    }

    [Fact]
    public void Compare_Cli_Command_With_Table_Options()
    {
        string leftCsv = _cli.Path("left.csv");
        string rightCsv = _cli.Path("right.csv");

        File.WriteAllText(leftCsv, "Item|Price\r\nWidget|100.02\r\n");
        File.WriteAllText(rightCsv, "Item|Price\r\nWidget|100.05\r\n");

        // Delta is 0.03. With tolerance 0.05, they are equal!
        JsonObject passJson = CliHarness.RunExpecting(
            ExitCode.Ok,
            "compare", leftCsv, rightCsv,
            "--csv-read-delimiter", "|",
            "--numeric-tolerance", "0.05",
            "--json").Json();

        Assert.True(passJson["equal"]!.GetValue<bool>());

        // With tolerance 0.01, they differ!
        JsonObject failJson = CliHarness.RunExpecting(
            ExitCode.Different,
            "compare", leftCsv, rightCsv,
            "--csv-read-delimiter", "|",
            "--numeric-tolerance", "0.01",
            "--json").Json();

        Assert.False(failJson["equal"]!.GetValue<bool>());
        var diffs = failJson["document"]!["differences"]!.AsArray();
        var cellDiff = Assert.Single(diffs, d => d!["kind"]!.GetValue<string>() == "table.cell-value");
        Assert.Equal(0, cellDiff!["tableIndex"]!.GetValue<int>());
        Assert.Equal("B2", cellDiff!["cellCoordinate"]!.GetValue<string>());
    }
}
