using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Broiler.Documents.Cli.Documents;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;
using Xunit;

namespace Broiler.Documents.Cli.Tests;

public sealed class TableIntrospectionTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public void TablesCommand_EmptyDocument_ReportsNoTables()
    {
        string mdFile = _cli.Path("plain.md");
        File.WriteAllText(mdFile, "Just plain text\nSecond line\n");

        CliRun run = CliHarness.RunExpecting(ExitCode.Ok, "tables", mdFile);
        Assert.Contains("no tables found", run.Output, StringComparison.OrdinalIgnoreCase);

        JsonObject json = CliHarness.RunExpecting(ExitCode.Ok, "tables", mdFile, "--json").Json();
        Assert.Equal(0, json["tableCount"]!.GetValue<int>());
        Assert.Empty(json["tables"]!.AsArray());
    }

    [Fact]
    public void TablesCommand_CsvFile_ReportsDimensionsAndBounds()
    {
        string csvFile = _cli.Path("products.csv");
        File.WriteAllText(csvFile, "Item,Qty,Price\nApple,10,1.50\nBanana,20,0.75\nOrange,15,2.00\n");

        JsonObject json = CliHarness.RunExpecting(ExitCode.Ok, "tables", csvFile, "--json").Json();

        Assert.Equal(1, json["tableCount"]!.GetValue<int>());
        JsonNode table = json["tables"]![0]!;
        Assert.Equal(0, table["index"]!.GetValue<int>());
        Assert.Equal(4, table["rowCount"]!.GetValue<int>());
        Assert.Equal(3, table["columnCount"]!.GetValue<int>());
        Assert.Equal("A1:C4", table["bounds"]!.GetValue<string>());

        JsonObject slots = table["slots"]!.AsObject();
        Assert.Equal(12, slots["total"]!.GetValue<int>());
        Assert.Equal(12, slots["root"]!.GetValue<int>());
        Assert.Equal(0, slots["continuation"]!.GetValue<int>());
        Assert.Equal(0, slots["empty"]!.GetValue<int>());
    }

    [Fact]
    public void TablesCommand_DataTypes_ClassifiesInferredTypes()
    {
        string csvFile = _cli.Path("types.csv");
        // Rows with text, integer, decimal, boolean, currency, percentage, date, time, empty
        File.WriteAllText(csvFile,
            "Item,Count,Ratio,Valid,Price,Tax,Date,Time\n" +
            "Widget,42,3.14,TRUE,$99.99,15.5%,2026-10-27,14:30:00\n" +
            ",,,,,\n");

        JsonObject json = CliHarness.RunExpecting(ExitCode.Ok, "tables", csvFile, "--json").Json();
        JsonObject types = json["tables"]![0]!["dataTypes"]!.AsObject();

        Assert.True(types["text"]!.GetValue<int>() >= 1);
        Assert.True(types["number"]!.GetValue<int>() >= 2);
        Assert.True(types["boolean"]!.GetValue<int>() >= 1);
        Assert.True(types["currency"]!.GetValue<int>() >= 1);
        Assert.True(types["percentage"]!.GetValue<int>() >= 1);
        Assert.True(types["date"]!.GetValue<int>() >= 1);
        Assert.True(types["time"]!.GetValue<int>() >= 1);
        Assert.True(types["empty"]!.GetValue<int>() >= 1);
    }

    [Fact]
    public void TablesCommand_Formulas_DependenciesAndEvaluation()
    {
        string csvFile = _cli.Path("budget.csv");
        File.WriteAllText(csvFile,
            "Item,Qty,Price,Total\n" +
            "Apple,10,2,=B2*C2\n" +
            "Banana,5,3,=B3*C3\n" +
            "Total,=SUM(B2:B3),,=SUM(D2:D3)\n");

        JsonObject json = CliHarness.RunExpecting(
            ExitCode.Ok,
            "tables", csvFile,
            "--formulas",
            "--evaluate",
            "--json").Json();

        var formulas = json["tables"]![0]!["formulas"]!.AsArray();
        Assert.Equal(4, formulas.Count);

        // Check D2: =B2*C2
        JsonObject d2 = Assert.Single(formulas, f => f!["coordinate"]!.GetValue<string>() == "D2")!.AsObject();
        Assert.Equal("=B2*C2", d2["expression"]!.GetValue<string>());
        Assert.Equal(20.0, d2["evaluatedValue"]!.GetValue<double>());
        var d2Deps = d2["dependencies"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Contains("B2", d2Deps);
        Assert.Contains("C2", d2Deps);

        // Check D3: =B3*C3
        JsonObject d3 = Assert.Single(formulas, f => f!["coordinate"]!.GetValue<string>() == "D3")!.AsObject();
        Assert.Equal(15.0, d3["evaluatedValue"]!.GetValue<double>());

        // Check B4: =SUM(B2:B3)
        JsonObject b4 = Assert.Single(formulas, f => f!["coordinate"]!.GetValue<string>() == "B4")!.AsObject();
        Assert.Equal(15.0, b4["evaluatedValue"]!.GetValue<double>());

        // Check D4: =SUM(D2:D3) -> 20 + 15 = 35
        JsonObject d4 = Assert.Single(formulas, f => f!["coordinate"]!.GetValue<string>() == "D4")!.AsObject();
        Assert.Equal(35.0, d4["evaluatedValue"]!.GetValue<double>());
    }

    [Fact]
    public void TablesCommand_CycleDetection_ReportsCycleError()
    {
        string csvFile = _cli.Path("cycle.csv");
        File.WriteAllText(csvFile, "A,B\n=B2+1,=A2+1\n");

        JsonObject json = CliHarness.RunExpecting(
            ExitCode.Ok,
            "tables", csvFile,
            "--formulas",
            "--evaluate",
            "--json").Json();

        var formulas = json["tables"]![0]!["formulas"]!.AsArray();
        var a2 = Assert.Single(formulas, f => f!["coordinate"]!.GetValue<string>() == "A2")!.AsObject();
        Assert.True(a2["isCyclic"]!.GetValue<bool>());
        Assert.Equal("#CYCLE!", a2["error"]!.GetValue<string>());
    }

    [Fact]
    public void TablesCommand_MergedCells_ReportsSpansAndSlots()
    {
        // Programmatic table with merged cells: A1 has rowSpan 2, colSpan 2
        var row0 = new TableRow([
            new TableCell(0, 1, 0, columnSpan: 2, rowSpan: 2),
            new TableCell(1, 1, 2)
        ]);
        var row1 = new TableRow([
            new TableCell(2, 1, 2)
        ]);
        var row2 = new TableRow([
            new TableCell(3, 1, 0),
            new TableCell(4, 1, 1),
            new TableCell(5, 1, 2)
        ]);

        var docTable = new DocumentTable(0, 6, [row0, row1, row2], columnWidths: [100, 100, 100]);
        var doc = RichTextDocument.FromPlainText("Merged\nC1\nC2\nA3\nB3\nC3\n").WithTables([docTable]);

        TableIntrospectionReport report = TableIntrospection.Inspect(docTable, 0, doc, includeCells: true);

        Assert.Equal(3, report.RowCount);
        Assert.Equal(3, report.ColumnCount);
        Assert.Equal(9, report.TotalSlots);
        Assert.Equal(6, report.RootCells);
        Assert.Equal(3, report.ContinuationSlots);
        Assert.Equal(0, report.EmptySlots);

        TableMergedRegion region = Assert.Single(report.MergedRegions);
        Assert.Equal("A1", region.Root);
        Assert.Equal("A1:B2", region.Range);
        Assert.Equal(2, region.RowSpan);
        Assert.Equal(2, region.ColumnSpan);
    }

    [Fact]
    public void TablesCommand_TableFilter_SelectsSpecificIndex()
    {
        // 2 tables in document
        var t1 = new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])]);
        var t2 = new DocumentTable(2, 2, [new TableRow([new TableCell(2, 1, 0), new TableCell(3, 1, 1)])]);
        var doc = RichTextDocument.FromPlainText("T1A\nT1B\nT2A\nT2B\n").WithTables([t1, t2]);

        var reports = TableIntrospection.InspectAll(doc);
        Assert.Equal(2, reports.Count);

        // Now test through CLI with multiple tables via CSV / file
        string csv1 = _cli.Path("t1.csv");
        File.WriteAllText(csv1, "A,B\n1,2\n");

        JsonObject json = CliHarness.RunExpecting(ExitCode.Ok, "tables", csv1, "--table", "0", "--json").Json();
        Assert.Single(json["tables"]!.AsArray());
        Assert.Equal(0, json["tables"]![0]!["index"]!.GetValue<int>());

        // Requesting out-of-range table index fails with usage code
        CliRun invalidRun = CliHarness.RunExpecting(ExitCode.Usage, "tables", csv1, "--table", "5");
        Assert.Contains("out of range", invalidRun.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TablesCommand_Cells_IncludesDetailedGridAndListing()
    {
        string csvFile = _cli.Path("small.csv");
        File.WriteAllText(csvFile, "X,Y\n10,20\n");

        JsonObject json = CliHarness.RunExpecting(ExitCode.Ok, "tables", csvFile, "--cells", "--json").Json();
        var cells = json["tables"]![0]!["cells"]!.AsArray();
        Assert.Equal(4, cells.Count);

        JsonObject a1 = Assert.Single(cells, c => c!["coordinate"]!.GetValue<string>() == "A1")!.AsObject();
        Assert.Equal("X", a1["text"]!.GetValue<string>());
        Assert.Equal("text", a1["dataType"]!.GetValue<string>());

        JsonObject a2 = Assert.Single(cells, c => c!["coordinate"]!.GetValue<string>() == "A2")!.AsObject();
        Assert.Equal("10", a2["text"]!.GetValue<string>());
        Assert.Equal("number", a2["dataType"]!.GetValue<string>());
    }

    [Fact]
    public void InfoCommand_ReportsTableStatistics()
    {
        string csvFile = _cli.Path("info_test.csv");
        File.WriteAllText(csvFile, "A,B,C\n1,2,3\n4,5,6\n");

        JsonObject json = CliHarness.RunExpecting(ExitCode.Ok, "info", csvFile, "--json").Json();
        JsonObject stats = json["statistics"]!.AsObject();

        Assert.Equal(1, stats["tables"]!.GetValue<int>());
        Assert.Equal(3, stats["tableRows"]!.GetValue<int>());
        Assert.Equal(3, stats["tableColumns"]!.GetValue<int>());
        Assert.Equal(9, stats["tableCells"]!.GetValue<int>());
    }

    [Fact]
    public void DumpCommand_AsGrid_RendersAsciiBox()
    {
        string csvFile = _cli.Path("grid_test.csv");
        File.WriteAllText(csvFile, "Name,Score\nAlice,95\nBob,88\n");

        CliRun gridRun = CliHarness.RunExpecting(ExitCode.Ok, "dump", csvFile, "--as", "grid");
        Assert.Contains("┌", gridRun.Output);
        Assert.Contains("┐", gridRun.Output);
        Assert.Contains("└", gridRun.Output);
        Assert.Contains("┘", gridRun.Output);
        Assert.Contains("Alice", gridRun.Output);
        Assert.Contains("Bob", gridRun.Output);

        CliRun tableRun = CliHarness.RunExpecting(ExitCode.Ok, "dump", csvFile, "--as", "table");
        Assert.Equal(gridRun.Output, tableRun.Output);
    }

    [Fact]
    public void TablesCommand_LayoutAndFloatingPosition_ReportsProperProperties()
    {
        var floatingPos = new TablePosition(
            HorizontalAnchor: TableAnchor.Margin,
            X: 50.0,
            VerticalAnchor: TableAnchor.Page,
            Y: 100.0,
            HorizontalAlignment: TableAlignment.Center,
            VerticalAlignment: TableAlignment.Start,
            AllowOverlap: false,
            ZOrder: 3,
            BehindText: true);

        var docTable = new DocumentTable(
            0, 2,
            [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])],
            columnWidths: [150.0, 200.0],
            cellPadding: 7.2,
            position: floatingPos,
            alignment: TableAlignment.Center,
            autoFit: true);

        var doc = RichTextDocument.FromPlainText("Cell 1\nCell 2\n").WithTables([docTable]);

        TableIntrospectionReport report = TableIntrospection.Inspect(docTable, 0, doc);

        Assert.Equal("center", report.Alignment);
        Assert.Equal(7.2, report.CellPadding);
        Assert.True(report.AutoFit);
        Assert.Equal(350.0, report.TotalWidth);
        Assert.Equal(2, report.ColumnWidths.Count);

        Assert.NotNull(report.Position);
        Assert.Equal("margin", report.Position.HorizontalAnchor);
        Assert.Equal("page", report.Position.VerticalAnchor);
        Assert.Equal("center", report.Position.HorizontalAlignment);
        Assert.Equal("start", report.Position.VerticalAlignment);
        Assert.False(report.Position.AllowOverlap);
        Assert.Equal(3, report.Position.ZOrder);
        Assert.True(report.Position.BehindText);

        JsonObject json = report.ToJson();
        Assert.Equal("center", json["layout"]!["alignment"]!.GetValue<string>());
        Assert.Equal(7.2, json["layout"]!["cellPadding"]!.GetValue<double>());
        Assert.True(json["layout"]!["autoFit"]!.GetValue<bool>());
        Assert.NotNull(json["layout"]!["position"]);
    }
}
