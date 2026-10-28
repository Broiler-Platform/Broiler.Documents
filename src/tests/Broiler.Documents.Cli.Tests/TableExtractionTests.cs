using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Broiler.Documents.Cli.Documents;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.Model;
using Xunit;

namespace Broiler.Documents.Cli.Tests;

public sealed class TableExtractionTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public void Extract_SingleTable_ToCsv_DefaultStdout()
    {
        string csvFile = _cli.Path("products.csv");
        File.WriteAllText(csvFile, "Item,Qty,Price\nApple,10,1.50\nBanana,20,0.75\n");

        CliRun run = CliHarness.RunExpecting(ExitCode.Ok, "extract", csvFile);
        string output = run.Output.Replace("\r\n", "\n").TrimEnd();

        Assert.Equal("Item,Qty,Price\nApple,10,1.50\nBanana,20,0.75", output);
    }

    [Fact]
    public void Extract_ToCsv_CustomDelimiter()
    {
        string csvFile = _cli.Path("products.csv");
        File.WriteAllText(csvFile, "Item,Qty,Price\nApple,10,1.50\nBanana,20,0.75\n");

        // Semicolon
        CliRun semiRun = CliHarness.RunExpecting(ExitCode.Ok, "extract", csvFile, "--delimiter", "semicolon");
        string semiOutput = semiRun.Output.Replace("\r\n", "\n").TrimEnd();
        Assert.Equal("Item;Qty;Price\nApple;10;1.50\nBanana;20;0.75", semiOutput);

        // Pipe
        CliRun pipeRun = CliHarness.RunExpecting(ExitCode.Ok, "extract", csvFile, "--delimiter", "pipe");
        string pipeOutput = pipeRun.Output.Replace("\r\n", "\n").TrimEnd();
        Assert.Equal("Item|Qty|Price\nApple|10|1.50\nBanana|20|0.75", pipeOutput);
    }

    [Fact]
    public void Extract_ToTsv()
    {
        string csvFile = _cli.Path("products.csv");
        File.WriteAllText(csvFile, "Item,Qty,Price\nApple,10,1.50\nBanana,20,0.75\n");

        CliRun run = CliHarness.RunExpecting(ExitCode.Ok, "extract", csvFile, "--to", "tsv");
        string output = run.Output.Replace("\r\n", "\n").TrimEnd();
        Assert.Equal("Item\tQty\tPrice\nApple\t10\t1.50\nBanana\t20\t0.75", output);
    }

    [Fact]
    public void Extract_ToJson_DefaultGrid()
    {
        string csvFile = _cli.Path("products.csv");
        File.WriteAllText(csvFile, "Item,Qty,Price\nApple,10,1.50\nBanana,20,0.75\n");

        CliRun run = CliHarness.RunExpecting(ExitCode.Ok, "extract", csvFile, "--to", "json");
        var node = JsonNode.Parse(run.Output);
        Assert.NotNull(node);
        var array = Assert.IsType<JsonArray>(node);

        Assert.Equal(3, array.Count);
        Assert.Equal("Item", array[0]![0]!.GetValue<string>());
        Assert.Equal("Qty", array[0]![1]!.GetValue<string>());
        Assert.Equal("Price", array[0]![2]!.GetValue<string>());
        Assert.Equal("Apple", array[1]![0]!.GetValue<string>());
        Assert.Equal("10", array[1]![1]!.GetValue<string>());
        Assert.Equal("1.50", array[1]![2]!.GetValue<string>());
    }

    [Fact]
    public void Extract_ToJson_WithHeaders_EmitsArrayOfObjects()
    {
        string csvFile = _cli.Path("products.csv");
        File.WriteAllText(csvFile, "Item,Qty,Price\nApple,10,1.50\nBanana,20,0.75\n");

        CliRun run = CliHarness.RunExpecting(ExitCode.Ok, "extract", csvFile, "--to", "json", "--headers");
        var node = JsonNode.Parse(run.Output);
        Assert.NotNull(node);
        var array = Assert.IsType<JsonArray>(node);

        Assert.Equal(2, array.Count);
        JsonObject apple = array[0]!.AsObject();
        Assert.Equal("Apple", apple["Item"]!.GetValue<string>());
        Assert.Equal("10", apple["Qty"]!.GetValue<string>());
        Assert.Equal("1.50", apple["Price"]!.GetValue<string>());

        JsonObject banana = array[1]!.AsObject();
        Assert.Equal("Banana", banana["Item"]!.GetValue<string>());
        Assert.Equal("20", banana["Qty"]!.GetValue<string>());
        Assert.Equal("0.75", banana["Price"]!.GetValue<string>());
    }

    [Fact]
    public void Extract_ToMarkdown_RendersGfmTable()
    {
        string csvFile = _cli.Path("products.csv");
        File.WriteAllText(csvFile, "Item,Qty,Price\nApple,10,1.50\n");

        CliRun run = CliHarness.RunExpecting(ExitCode.Ok, "extract", csvFile, "--to", "markdown", "--headers");
        Assert.Contains("| Item | Qty | Price |", run.Output);
        Assert.Contains("| --- | --- | --- |", run.Output);
        Assert.Contains("| Apple | 10 | 1.50 |", run.Output);
    }

    [Fact]
    public void Extract_ToHtml_RendersHtmlTable()
    {
        string csvFile = _cli.Path("products.csv");
        File.WriteAllText(csvFile, "Item,Qty,Price\nApple,10,1.50\n");

        CliRun run = CliHarness.RunExpecting(ExitCode.Ok, "extract", csvFile, "--to", "html", "--headers");
        Assert.Contains("<table>", run.Output);
        Assert.Contains("<thead>", run.Output);
        Assert.Contains("<th>Item</th>", run.Output);
        Assert.Contains("<tbody>", run.Output);
        Assert.Contains("<td>Apple</td>", run.Output);
        Assert.Contains("</table>", run.Output);
    }

    [Fact]
    public void Extract_CellRange_SubsetsTable()
    {
        string csvFile = _cli.Path("data.csv");
        File.WriteAllText(csvFile,
            "Item,Qty,Price\n" +
            "Apple,10,1.50\n" +
            "Banana,20,0.75\n" +
            "Orange,15,2.00\n");

        // Rectangular range B2:C3 -> rows 1..2, cols 1..2
        CliRun rangeRun = CliHarness.RunExpecting(ExitCode.Ok, "extract", csvFile, "--range", "B2:C3", "--to", "csv");
        string rangeOutput = rangeRun.Output.Replace("\r\n", "\n").TrimEnd();
        Assert.Equal("10,1.50\n20,0.75", rangeOutput);

        // Column range B:C -> all 4 rows, cols 1..2
        CliRun colRun = CliHarness.RunExpecting(ExitCode.Ok, "extract", csvFile, "--range", "B:C", "--to", "csv");
        string colOutput = colRun.Output.Replace("\r\n", "\n").TrimEnd();
        Assert.Equal("Qty,Price\n10,1.50\n20,0.75\n15,2.00", colOutput);

        // Row range 2:3 -> rows 1..2, all 3 cols
        CliRun rowRun = CliHarness.RunExpecting(ExitCode.Ok, "extract", csvFile, "--range", "2:3", "--to", "csv");
        string rowOutput = rowRun.Output.Replace("\r\n", "\n").TrimEnd();
        Assert.Equal("Apple,10,1.50\nBanana,20,0.75", rowOutput);
    }

    [Fact]
    public void Extract_Formulas_Evaluate_EvaluatesValues()
    {
        string csvFile = _cli.Path("budget.csv");
        File.WriteAllText(csvFile,
            "Item,Qty,Price,Total\n" +
            "Apple,10,2,=B2*C2\n" +
            "Banana,5,3,=B3*C3\n" +
            "Total,=SUM(B2:B3),,=SUM(D2:D3)\n");

        CliRun run = CliHarness.RunExpecting(ExitCode.Ok, "extract", csvFile, "--evaluate", "--to", "csv");
        string output = run.Output.Replace("\r\n", "\n").TrimEnd();

        // Evaluated values: Apple Total is 20, Banana Total is 15, Total Qty is 15, Total Total is 35
        Assert.Contains("Apple,10,2,20", output);
        Assert.Contains("Banana,5,3,15", output);
        Assert.Contains("Total,15,,35", output);
    }

    [Fact]
    public void Extract_Formulas_Formatted_AppliesCellNumberFormat()
    {
        // Programmatic table with formula and number format
        var row0 = new TableRow([
            new TableCell(0, 1, 0),
            new TableCell(1, 1, 1),
            new TableCell(2, 1, 2, formula: "=A1+B1", numberFormat: "$#,##0.00")
        ]);

        var docTable = new DocumentTable(0, 3, [row0], columnWidths: [100, 100, 100]);
        var doc = RichTextDocument.FromPlainText("10\n20\n0\n").WithTables([docTable]);

        var options = new TableExtractOptions
        {
            Evaluate = true,
            Formatted = true,
            TargetFormat = "csv"
        };

        ExtractedTable table = TableExtractor.Extract(docTable, 0, doc, options);
        Assert.Equal("$30.00", table.Rows[0][2]);
    }

    [Fact]
    public void Extract_MergedCells_FillMerged_RepeatsRootValue()
    {
        // A1 spans 2x2 with "Group"
        var row0 = new TableRow([
            new TableCell(0, 1, 0, columnSpan: 2, rowSpan: 2),
            new TableCell(1, 1, 2)
        ]);
        var row1 = new TableRow([
            new TableCell(2, 1, 2)
        ]);

        var docTable = new DocumentTable(0, 3, [row0, row1], columnWidths: [100, 100, 100]);
        var doc = RichTextDocument.FromPlainText("Group\nC1\nC2\n").WithTables([docTable]);

        // Default (without FillMerged): continuation slots are empty
        var defaultOptions = new TableExtractOptions { FillMerged = false };
        ExtractedTable defTable = TableExtractor.Extract(docTable, 0, doc, defaultOptions);
        Assert.Equal("Group", defTable.Rows[0][0]);
        Assert.Equal("", defTable.Rows[0][1]);
        Assert.Equal("", defTable.Rows[1][0]);
        Assert.Equal("", defTable.Rows[1][1]);

        // With FillMerged: all 4 covered slots have "Group"
        var fillOptions = new TableExtractOptions { FillMerged = true };
        ExtractedTable fillTable = TableExtractor.Extract(docTable, 0, doc, fillOptions);
        Assert.Equal("Group", fillTable.Rows[0][0]);
        Assert.Equal("Group", fillTable.Rows[0][1]);
        Assert.Equal("Group", fillTable.Rows[1][0]);
        Assert.Equal("Group", fillTable.Rows[1][1]);
    }

    [Fact]
    public void Extract_CustomEmptyValue()
    {
        string csvFile = _cli.Path("sparse.csv");
        File.WriteAllText(csvFile, "A,,C\n1,,3\n");

        CliRun run = CliHarness.RunExpecting(ExitCode.Ok, "extract", csvFile, "--empty", "N/A", "--to", "csv");
        string output = run.Output.Replace("\r\n", "\n").TrimEnd();
        Assert.Equal("A,N/A,C\n1,N/A,3", output);
    }

    [Fact]
    public void Extract_MultiTable_WithTemplate_WritesSeparateFiles()
    {
        var t0 = new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])]);
        var t1 = new DocumentTable(2, 2, [new TableRow([new TableCell(2, 1, 0), new TableCell(3, 1, 1)])]);
        var doc = RichTextDocument.FromPlainText("A0\nB0\nA1\nB1\n").WithTables([t0, t1]);

        var options = new TableExtractOptions { TargetFormat = "csv" };
        var tables = TableExtractor.ExtractAll(doc, options);
        Assert.Equal(2, tables.Count);

        string csv0 = tables[0].RenderCsv();
        string csv1 = tables[1].RenderCsv();

        Assert.Equal("A0,B0\r\n", csv0);
        Assert.Equal("A1,B1\r\n", csv1);
    }

    [Fact]
    public void Extract_Aliases_ExtractTables_And_ExtractTable()
    {
        string csvFile = _cli.Path("products.csv");
        File.WriteAllText(csvFile, "Item,Qty\nApple,10\n");

        CliRun baseRun = CliHarness.RunExpecting(ExitCode.Ok, "extract", csvFile);
        CliRun alias1 = CliHarness.RunExpecting(ExitCode.Ok, "extract-tables", csvFile);
        CliRun alias2 = CliHarness.RunExpecting(ExitCode.Ok, "extract-table", csvFile);

        Assert.Equal(baseRun.Output, alias1.Output);
        Assert.Equal(baseRun.Output, alias2.Output);
    }

    [Fact]
    public void Extract_JsonFlag_StructuredMachineResult()
    {
        string csvFile = _cli.Path("products.csv");
        File.WriteAllText(csvFile, "Item,Qty\nApple,10\nBanana,20\n");

        JsonObject json = CliHarness.RunExpecting(ExitCode.Ok, "extract", csvFile, "--json").Json();

        Assert.Equal("extract", json["command"]!.GetValue<string>());
        Assert.Equal(1, json["extractedCount"]!.GetValue<int>());
        Assert.Equal(1, json["tableCount"]!.GetValue<int>());

        JsonObject table = json["tables"]![0]!.AsObject();
        Assert.Equal(0, table["index"]!.GetValue<int>());
        Assert.Equal(3, table["rowCount"]!.GetValue<int>());
        Assert.Equal(2, table["columnCount"]!.GetValue<int>());
        Assert.Equal("A1:B3", table["bounds"]!.GetValue<string>());

        JsonArray rows = table["rows"]!.AsArray();
        Assert.Equal(3, rows.Count);
        Assert.Equal("Item", rows[0]![0]!.GetValue<string>());
        Assert.Equal("Qty", rows[0]![1]!.GetValue<string>());

        JsonArray records = table["records"]!.AsArray();
        Assert.Equal(3, records.Count);
    }

    [Fact]
    public void Extract_ErrorHandling_InvalidTableIndex()
    {
        string csvFile = _cli.Path("products.csv");
        File.WriteAllText(csvFile, "A,B\n1,2\n");

        CliRun run = CliHarness.RunExpecting(ExitCode.Usage, "extract", csvFile, "--table", "99");
        Assert.Contains("out of range", run.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extract_ErrorHandling_InvalidRange()
    {
        string csvFile = _cli.Path("products.csv");
        File.WriteAllText(csvFile, "A,B\n1,2\n");

        CliRun run = CliHarness.RunExpecting(ExitCode.Usage, "extract", csvFile, "--range", "not-a-valid-range");
        Assert.Contains("invalid range", run.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extract_ErrorHandling_NoTablesInDocument()
    {
        string mdFile = _cli.Path("notables.md");
        File.WriteAllText(mdFile, "Just plain markdown paragraph without any tables.\n");

        // Requested specific table on document with 0 tables fails with usage
        CliRun runTable = CliHarness.RunExpecting(ExitCode.Usage, "extract", mdFile, "--table", "0");
        Assert.Contains("contains no tables", runTable.Error, StringComparison.OrdinalIgnoreCase);

        // General extract on document with 0 tables reports no tables found
        CliRun runGeneral = CliHarness.RunExpecting(ExitCode.Ok, "extract", mdFile);
        Assert.Contains("no tables found", runGeneral.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extract_ToFile_InfersFormatFromExtension()
    {
        string csvFile = _cli.Path("input.csv");
        File.WriteAllText(csvFile, "Col1,Col2\nVal1,Val2\n");

        string outJson = _cli.Path("out.json");
        CliHarness.RunExpecting(ExitCode.Ok, "extract", csvFile, "--out", outJson);

        Assert.True(File.Exists(outJson));
        string content = File.ReadAllText(outJson);
        var parsed = JsonNode.Parse(content);
        Assert.NotNull(parsed);
    }
}
