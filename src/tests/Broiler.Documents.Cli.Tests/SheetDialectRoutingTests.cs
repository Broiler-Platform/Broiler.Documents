using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Broiler.Documents.Cli.Composition;
using Broiler.Documents.Cli.Documents;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.Docx;
using Broiler.Documents.Model;
using Broiler.Documents.Ods;
using Broiler.Documents.Xlsx;
using Xunit;

namespace Broiler.Documents.Cli.Tests;

public sealed class SheetDialectRoutingTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private string CreateMultiTableDocx()
    {
        string docxPath = _cli.Path("multitable.docx");
        var t0 = new DocumentTable(1, 2, [
            new TableRow([new TableCell(1, 1, 0), new TableCell(2, 1, 1)])
        ]);
        var t1 = new DocumentTable(4, 2, [
            new TableRow([new TableCell(4, 1, 0), new TableCell(5, 1, 1)])
        ]);
        var doc = RichTextDocument.FromPlainText("Table One\nA\nB\nTable Two\nX\nY\n")
            .WithTables([t0, t1]);
        DocumentIo.Save(doc, docxPath, new DocxDocumentCodec(), DocumentWriteOptions.Default);
        return docxPath;
    }

    private string CreateSimpleCsv(string name = "simple.csv")
    {
        string csvPath = _cli.Path(name);
        File.WriteAllText(csvPath, "ColA,ColB\nAlpha,=1+2\nBeta,Gamma\n");
        return csvPath;
    }

    [Fact]
    public void Convert_SelectSheet_ByIndex_MultiTable()
    {
        string docxPath = CreateMultiTableDocx();
        string out0 = _cli.Path("t0.csv");
        string out1 = _cli.Path("t1.csv");

        CliHarness.RunExpecting(ExitCode.Ok, "convert", docxPath, "--out", out0, "--sheet", "0");
        CliHarness.RunExpecting(ExitCode.Ok, "convert", docxPath, "--out", out1, "--sheet", "1");

        string text0 = File.ReadAllText(out0).Replace("\r\n", "\n");
        string text1 = File.ReadAllText(out1).Replace("\r\n", "\n");

        Assert.Contains("A,B", text0);
        Assert.DoesNotContain("X,Y", text0);

        Assert.Contains("X,Y", text1);
        Assert.DoesNotContain("A,B", text1);
    }

    [Fact]
    public void Convert_SelectTable_ByIndex_MultiTable()
    {
        string docxPath = CreateMultiTableDocx();
        string out0 = _cli.Path("table0.csv");
        string out1 = _cli.Path("table1.csv");

        CliHarness.RunExpecting(ExitCode.Ok, "convert", docxPath, "--out", out0, "--table", "0");
        CliHarness.RunExpecting(ExitCode.Ok, "convert", docxPath, "--out", out1, "--table", "1");

        string text0 = File.ReadAllText(out0).Replace("\r\n", "\n");
        string text1 = File.ReadAllText(out1).Replace("\r\n", "\n");

        Assert.Contains("A,B", text0);
        Assert.DoesNotContain("X,Y", text0);

        Assert.Contains("X,Y", text1);
        Assert.DoesNotContain("A,B", text1);
    }

    [Fact]
    public void Convert_SelectSheet_ByName_Sheet1_Sheet2()
    {
        string docxPath = CreateMultiTableDocx();
        string out0 = _cli.Path("s1.csv");
        string out1 = _cli.Path("s2.csv");

        CliHarness.RunExpecting(ExitCode.Ok, "convert", docxPath, "--out", out0, "--sheet", "Sheet1");
        CliHarness.RunExpecting(ExitCode.Ok, "convert", docxPath, "--out", out1, "--sheet", "Sheet 2");

        string text0 = File.ReadAllText(out0).Replace("\r\n", "\n");
        string text1 = File.ReadAllText(out1).Replace("\r\n", "\n");

        Assert.Contains("A,B", text0);
        Assert.Contains("X,Y", text1);
    }

    [Fact]
    public void Convert_SelectSheet_ByPrecedingHeading()
    {
        string docxPath = CreateMultiTableDocx();
        string out0 = _cli.Path("h1.csv");
        string out1 = _cli.Path("h2.csv");

        CliHarness.RunExpecting(ExitCode.Ok, "convert", docxPath, "--out", out0, "--sheet", "Table One");
        CliHarness.RunExpecting(ExitCode.Ok, "convert", docxPath, "--out", out1, "--sheet", "Table Two");

        string text0 = File.ReadAllText(out0).Replace("\r\n", "\n");
        string text1 = File.ReadAllText(out1).Replace("\r\n", "\n");

        Assert.Contains("A,B", text0);
        Assert.Contains("X,Y", text1);
    }

    [Fact]
    public void Convert_Dialect_Tsv()
    {
        string csvPath = CreateSimpleCsv("test_tsv.csv");
        string outTsv = _cli.Path("out.tsv");

        CliHarness.RunExpecting(ExitCode.Ok, "convert", csvPath, "--out", outTsv, "--dialect", "tsv");
        string content = File.ReadAllText(outTsv).Replace("\r\n", "\n");

        Assert.Contains("ColA\tColB", content);
        Assert.Contains("Alpha\t=1+2", content);
    }

    [Fact]
    public void Convert_Dialect_Excel_AddsBomAndEscapesFormulas()
    {
        string csvPath = CreateSimpleCsv("test_excel.csv");
        string outExcel = _cli.Path("out_excel.csv");

        CliHarness.RunExpecting(ExitCode.Ok, "convert", csvPath, "--out", outExcel, "--dialect", "excel");
        byte[] bytes = File.ReadAllBytes(outExcel);

        // UTF-8 BOM: 0xEF, 0xBB, 0xBF
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);

        string text = Encoding.UTF8.GetString(bytes[3..]).Replace("\r\n", "\n");
        Assert.Contains("ColA,ColB", text);
        Assert.Contains("Alpha,'=1+2", text);
    }

    [Fact]
    public void Convert_Dialect_Unix_NoBomAndNoFormulaEscaping()
    {
        string csvPath = CreateSimpleCsv("test_unix.csv");
        string outUnix = _cli.Path("out_unix.csv");

        CliHarness.RunExpecting(ExitCode.Ok, "convert", csvPath, "--out", outUnix, "--dialect", "unix");
        byte[] bytes = File.ReadAllBytes(outUnix);

        // No BOM
        Assert.NotEqual(0xEF, bytes[0]);

        string text = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n");
        Assert.Contains("ColA,ColB", text);
        Assert.Contains("Alpha,=1+2", text);
    }

    [Fact]
    public void Convert_Dialect_Semicolon()
    {
        string csvPath = CreateSimpleCsv("test_semi.csv");
        string outSemi = _cli.Path("out_semi.csv");

        CliHarness.RunExpecting(ExitCode.Ok, "convert", csvPath, "--out", outSemi, "--dialect", "semicolon");
        string text = File.ReadAllText(outSemi).Replace("\r\n", "\n");

        Assert.Contains("ColA;ColB", text);
        Assert.Contains("Alpha;=1+2", text);
    }

    [Fact]
    public void Convert_Dialect_Pipe()
    {
        string csvPath = CreateSimpleCsv("test_pipe.csv");
        string outPipe = _cli.Path("out_pipe.csv");

        CliHarness.RunExpecting(ExitCode.Ok, "convert", csvPath, "--out", outPipe, "--dialect", "pipe");
        string text = File.ReadAllText(outPipe).Replace("\r\n", "\n");

        Assert.Contains("ColA|ColB", text);
        Assert.Contains("Alpha|=1+2", text);
    }

    [Fact]
    public void Convert_TsvExtension_DefaultsToTabDelimiter()
    {
        string csvPath = CreateSimpleCsv("test_auto_tsv.csv");
        string outTsv = _cli.Path("auto.tsv");

        CliHarness.RunExpecting(ExitCode.Ok, "convert", csvPath, "--out", outTsv);
        string text = File.ReadAllText(outTsv).Replace("\r\n", "\n");

        Assert.Contains("ColA\tColB", text);
        Assert.Contains("Alpha\t=1+2", text);
    }

    [Fact]
    public void Convert_Delimiter_Overrides_Separator()
    {
        string csvPath = CreateSimpleCsv("test_delim.csv");
        string outPath = _cli.Path("custom_delim.csv");

        CliHarness.RunExpecting(ExitCode.Ok, "convert", csvPath, "--out", outPath, "--delimiter", "pipe");
        string text = File.ReadAllText(outPath).Replace("\r\n", "\n");

        Assert.Contains("ColA|ColB", text);
    }

    [Fact]
    public void Roundtrip_SelectSheet_MultiTable()
    {
        string docxPath = CreateMultiTableDocx();

        // Roundtrip each sheet independently through CSV
        CliRun run0 = CliHarness.RunExpecting(ExitCode.Ok, "roundtrip", docxPath, "--sheet", "0", "--via", "csv");
        Assert.Contains("selected sheet/table 0", run0.Output);

        CliRun run1 = CliHarness.RunExpecting(ExitCode.Ok, "roundtrip", docxPath, "--sheet", "1", "--via", "csv");
        Assert.Contains("selected sheet/table 1", run1.Output);
    }

    [Fact]
    public void Roundtrip_ViaTsv()
    {
        string csvPath = _cli.Path("rt_tsv.csv");
        File.WriteAllText(csvPath, "ColA,ColB\nVal1,Val2\n");

        CliRun run = CliHarness.RunExpecting(ExitCode.Ok, "roundtrip", csvPath, "--via", "tsv");
        Assert.Contains("via TSV", run.Output);
        Assert.Contains("verdict: every format round-tripped without a difference", run.Output);
    }

    [Fact]
    public void Roundtrip_ViaCsv_DialectSemicolon()
    {
        string csvPath = _cli.Path("rt_semi.csv");
        File.WriteAllText(csvPath, "ColA,ColB\nVal1,Val2\n");

        CliRun run = CliHarness.RunExpecting(ExitCode.Ok, "roundtrip", csvPath, "--via", "csv", "--dialect", "semicolon");
        Assert.Contains("verdict: every format round-tripped without a difference", run.Output);
    }

    [Fact]
    public void Roundtrip_TableAware_IgnoreTableLayout()
    {
        string docxPath = CreateMultiTableDocx();

        // Testing table 0 roundtrip via CSV ignoring table layout properties
        CliRun run = CliHarness.RunExpecting(ExitCode.Ok, "roundtrip", docxPath, "--table", "0", "--via", "csv", "--ignore-table-layout");
        Assert.Contains("selected sheet/table 0", run.Output);
        Assert.Contains("verdict: every format round-tripped without a difference", run.Output);
    }

    [Fact]
    public void Dump_SelectsSheet()
    {
        string docxPath = CreateMultiTableDocx();

        CliRun run0 = CliHarness.RunExpecting(ExitCode.Ok, "dump", docxPath, "--sheet", "0", "--as", "grid");
        Assert.DoesNotContain("X", run0.Output);
        Assert.DoesNotContain("Y", run0.Output);

        CliRun run1 = CliHarness.RunExpecting(ExitCode.Ok, "dump", docxPath, "--table", "1", "--as", "grid");
        Assert.Contains("X", run1.Output);
        Assert.Contains("Y", run1.Output);
    }

    [Fact]
    public void Error_SheetAndTable_MutuallyExclusive()
    {
        string docxPath = CreateMultiTableDocx();
        string outPath = _cli.Path("out.csv");

        CliRun run = CliHarness.RunExpecting(ExitCode.Usage, "convert", docxPath, "--sheet", "0", "--table", "0", "--out", outPath);
        Assert.Contains("--sheet and --table are alternatives", run.Error);
    }

    [Fact]
    public void Error_Table_NonInteger()
    {
        string docxPath = CreateMultiTableDocx();
        string outPath = _cli.Path("out.csv");

        CliRun run = CliHarness.RunExpecting(ExitCode.Usage, "convert", docxPath, "--table", "invalid", "--out", outPath);
        Assert.Contains("requires an integer index", run.Error);
    }

    [Fact]
    public void Error_Table_OutOfRange()
    {
        string docxPath = CreateMultiTableDocx();
        string outPath = _cli.Path("out.csv");

        CliRun run = CliHarness.RunExpecting(ExitCode.Usage, "convert", docxPath, "--table", "99", "--out", outPath);
        Assert.Contains("out of range", run.Error);
    }

    [Fact]
    public void Error_Sheet_NotFound()
    {
        string docxPath = CreateMultiTableDocx();
        string outPath = _cli.Path("out.csv");

        CliRun run = CliHarness.RunExpecting(ExitCode.Usage, "convert", docxPath, "--sheet", "NonExistentSheet", "--out", outPath);
        Assert.Contains("not found", run.Error);
    }

    [Fact]
    public void Error_NoTablesInDocument()
    {
        string mdPath = _cli.Path("plain.md");
        File.WriteAllText(mdPath, "Just plain text without tables.\n");
        string outPath = _cli.Path("out.csv");

        CliRun run = CliHarness.RunExpecting(ExitCode.Usage, "convert", mdPath, "--sheet", "0", "--out", outPath);
        Assert.Contains("contains no tables or sheets", run.Error);
    }

    [Fact]
    public void Error_UnknownDialect()
    {
        string csvPath = CreateSimpleCsv("err_dialect.csv");
        string outPath = _cli.Path("out.csv");

        CliRun run = CliHarness.RunExpecting(ExitCode.Usage, "convert", csvPath, "--dialect", "magic_dialect", "--out", outPath);
        Assert.Contains("Unknown dialect \"magic_dialect\"", run.Error);
    }

    [Fact]
    public void Error_InvalidDelimiter()
    {
        string csvPath = CreateSimpleCsv("err_delim.csv");
        string outPath = _cli.Path("out.csv");

        CliRun run = CliHarness.RunExpecting(ExitCode.Usage, "convert", csvPath, "--delimiter", "\"", "--out", outPath);
        Assert.Contains("expects one character other than a quote", run.Error);
    }

    [Fact]
    public void Json_Output_IncludesSelectedSheetAndDialect()
    {
        string docxPath = CreateMultiTableDocx();
        string outPath = _cli.Path("out_json.csv");

        CliRun run = CliHarness.RunExpecting(ExitCode.Ok, "convert", docxPath, "--sheet", "0", "--dialect", "excel", "--out", outPath, "--json");
        JsonObject json = run.Json();

        Assert.Equal(0, json["selectedSheet"]!.GetValue<int>());
        Assert.Equal("excel", json["dialect"]!.GetValue<string>());
    }

    [Fact]
    public void Convert_SelectSheet_ByName_Xlsx()
    {
        string xlsxPath = _cli.Path("multisheet.xlsx");
        var t0 = new DocumentTable(0, 2, [
            new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])
        ], name: "Revenue");
        var t1 = new DocumentTable(2, 2, [
            new TableRow([new TableCell(2, 1, 0), new TableCell(3, 1, 1)])
        ], name: "Costs");

        var doc = RichTextDocument.FromPlainText("RevQ1\n100\nCostQ1\n80\n")
            .WithTables([t0, t1]);
        DocumentIo.Save(doc, xlsxPath, new XlsxDocumentCodec(), DocumentWriteOptions.Default);

        string outCsv = _cli.Path("costs.csv");
        CliHarness.RunExpecting(ExitCode.Ok, "convert", xlsxPath, "--out", outCsv, "--sheet", "Costs");

        string text = File.ReadAllText(outCsv).Replace("\r\n", "\n");
        Assert.Contains("CostQ1", text);
        Assert.DoesNotContain("RevQ1", text);
    }

    [Fact]
    public void Convert_SelectSheet_ByName_Ods()
    {
        string odsPath = _cli.Path("multisheet.ods");
        var t0 = new DocumentTable(0, 2, [
            new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])
        ], name: "Revenue");
        var t1 = new DocumentTable(2, 2, [
            new TableRow([new TableCell(2, 1, 0), new TableCell(3, 1, 1)])
        ], name: "Costs");

        var doc = RichTextDocument.FromPlainText("RevQ1\n100\nCostQ1\n80\n")
            .WithTables([t0, t1]);
        DocumentIo.Save(doc, odsPath, new OdsDocumentCodec(), DocumentWriteOptions.Default);

        string outCsv = _cli.Path("costs_ods.csv");
        CliHarness.RunExpecting(ExitCode.Ok, "convert", odsPath, "--out", outCsv, "--sheet", "Costs");

        string text = File.ReadAllText(outCsv).Replace("\r\n", "\n");
        Assert.Contains("CostQ1", text);
        Assert.DoesNotContain("RevQ1", text);
    }
}
