using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
using Xunit;

namespace Broiler.Documents.Xlsx.Tests;

public sealed class XlsxCodecTests
{
    private readonly XlsxDocumentCodec _codec = new();

    [Fact]
    public void Format_Descriptor_Matches_Expected_Extensions_And_Mime_Type()
    {
        Assert.Equal("XLSX", _codec.Descriptor.Name);
        Assert.True(_codec.Descriptor.MatchesExtension(".xlsx"));
        Assert.True(_codec.Descriptor.MatchesExtension(".xlsm"));
        Assert.True(_codec.Descriptor.MatchesExtension(".xltx"));
        Assert.True(_codec.Descriptor.MatchesMimeType("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
    }

    [Fact]
    public void Probe_Recognizes_Zip_Containing_Workbook_Part()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("xl/workbook.xml");
            using var s = entry.Open();
            s.Write(Encoding.UTF8.GetBytes("<workbook/>"));
        }
        ms.Position = 0;

        byte[] prefix = new byte[Math.Min(ms.Length, 4096)];
        ms.ReadExactly(prefix);

        var request = new DocumentProbeRequest(prefix, DocumentSourceHints.Empty);
        DocumentProbeResult result = _codec.Probe(request);

        Assert.True(result.IsMatch);
        Assert.Equal(DocumentProbeConfidence.High, result.Confidence);
        Assert.Equal("XLSX", result.FormatName);
    }

    [Fact]
    public void Probe_Matches_With_Extension_Hint_On_Zip_Header()
    {
        byte[] zipHeader = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00];
        var hints = new DocumentSourceHints("financials.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var request = new DocumentProbeRequest(zipHeader, hints);

        DocumentProbeResult result = _codec.Probe(request);

        Assert.True(result.IsMatch);
        Assert.Equal(DocumentProbeConfidence.High, result.Confidence);
    }

    [Fact]
    public void Roundtrip_Single_Sheet_With_Mixed_Cell_Types_And_Formulas()
    {
        var cells = new List<SpreadsheetCell>
        {
            new(new CellAddress(0, 0), CellValue.FromText("Product")),
            new(new CellAddress(0, 1), CellValue.FromText("Price")),
            new(new CellAddress(0, 2), CellValue.FromText("In Stock")),
            new(new CellAddress(1, 0), CellValue.FromText("Apples")),
            new(new CellAddress(1, 1), CellValue.FromNumber(2.50), numberFormat: "$#,##0.00"),
            new(new CellAddress(1, 2), CellValue.FromBoolean(true)),
            new(new CellAddress(2, 0), CellValue.FromText("Total")),
            new(new CellAddress(2, 1), CellValue.FromNumber(2.50), formula: "=SUM(B2:B2)")
        };

        var row0 = new SpreadsheetRow(0, cells.GetRange(0, 3));
        var row1 = new SpreadsheetRow(1, cells.GetRange(3, 3));
        var row2 = new SpreadsheetRow(2, cells.GetRange(6, 2));

        var sheet = new SpreadsheetWorksheet("Inventory", [row0, row1, row2], [120.0, 80.0, 60.0]);
        var workbook = new SpreadsheetWorkbook([sheet]);

        RichTextDocument originalDoc = workbook.ToRichTextDocument();

        // Write to stream
        using var stream = new MemoryStream();
        DocumentWriteResult writeResult = _codec.Write(originalDoc, stream);
        Assert.Equal(DocumentResultStatus.Success, writeResult.Status);
        Assert.True(stream.Length > 0);

        // Read back from stream
        stream.Position = 0;
        DocumentReadResult readResult = _codec.Read(stream);

        Assert.Equal(DocumentResultStatus.Success, readResult.Status);
        Assert.IsType<XlsxReadResult>(readResult);

        var xlsxResult = (XlsxReadResult)readResult;
        SpreadsheetWorkbook readWb = xlsxResult.Workbook;

        Assert.Single(readWb.Sheets);
        SpreadsheetWorksheet readSheet = readWb.Sheets[0];
        Assert.Equal("Inventory", readSheet.Name);
        Assert.Equal(3, readSheet.RowCount);

        Assert.Equal("Apples", readSheet[1, 0]?.CachedText);
        Assert.Equal(2.50, readSheet[1, 1]?.Value.Number);
        Assert.True(readSheet[1, 2]?.Value.Boolean);
        Assert.Equal("=SUM(B2:B2)", readSheet[2, 1]?.Formula);

        // Verify document table mapping
        Assert.Single(readResult.Document.Tables);
        DocumentTable table = readResult.Document.Tables[0];
        Assert.Equal("Inventory", table.Name);
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal("=SUM(B2:B2)", table.Rows[2].Cells[1].Formula);
    }

    [Fact]
    public void Roundtrip_Multiple_Sheets_With_Merged_Cells()
    {
        var cells1 = new List<SpreadsheetCell>
        {
            new(new CellAddress(0, 0), CellValue.FromText("Quarterly Report"), columnSpan: 3, rowSpan: 1),
            new(new CellAddress(1, 0), CellValue.FromText("Q1")),
            new(new CellAddress(1, 1), CellValue.FromNumber(100.0)),
            new(new CellAddress(1, 2), CellValue.FromNumber(150.0))
        };
        var sheet1 = new SpreadsheetWorksheet("Summary", [
            new SpreadsheetRow(0, [cells1[0]]),
            new SpreadsheetRow(1, cells1.GetRange(1, 3))
        ]);

        var cells2 = new List<SpreadsheetCell>
        {
            new(new CellAddress(0, 0), CellValue.FromText("Notes")),
            new(new CellAddress(1, 0), CellValue.FromText("Preliminary numbers subject to audit."))
        };
        var sheet2 = new SpreadsheetWorksheet("Disclaimer", [
            new SpreadsheetRow(0, [cells2[0]]),
            new SpreadsheetRow(1, [cells2[1]])
        ]);

        var workbook = new SpreadsheetWorkbook([sheet1, sheet2]);
        RichTextDocument doc = workbook.ToRichTextDocument();

        using var stream = new MemoryStream();
        _codec.Write(doc, stream);

        stream.Position = 0;
        var readResult = (XlsxReadResult)_codec.Read(stream);

        Assert.Equal(2, readResult.Workbook.Sheets.Count);
        Assert.Equal("Summary", readResult.Workbook.Sheets[0].Name);
        Assert.Equal("Disclaimer", readResult.Workbook.Sheets[1].Name);

        SpreadsheetCell? mergedCell = readResult.Workbook.Sheets[0][0, 0];
        Assert.NotNull(mergedCell);
        Assert.Equal("Quarterly Report", mergedCell.CachedText);
        Assert.Equal(3, mergedCell.ColumnSpan);
        Assert.Equal(1, mergedCell.RowSpan);

        Assert.Equal(2, readResult.Document.Tables.Count);
        Assert.Equal("Summary", readResult.Document.Tables[0].Name);
        Assert.Equal("Disclaimer", readResult.Document.Tables[1].Name);
    }

    [Fact]
    public void Read_Corrupt_Zip_Returns_Rejected_Status_With_Diagnostic()
    {
        byte[] junk = [0x50, 0x4B, 0x03, 0x04, 0xFF, 0xFF, 0xFF, 0xFF];
        using var stream = new MemoryStream(junk);

        DocumentReadResult result = _codec.Read(stream);

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, d => d.Code == "xlsx.corrupt");
    }
}
