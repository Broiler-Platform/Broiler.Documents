using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
using Xunit;

namespace Broiler.Documents.Ods.Tests;

public sealed class OdsCodecTests
{
    private readonly OdsDocumentCodec _codec = new();

    [Fact]
    public void Format_Descriptor_Matches_Expected_Extensions_And_Mime_Type()
    {
        Assert.Equal("ODS", _codec.Descriptor.Name);
        Assert.True(_codec.Descriptor.MatchesExtension(".ods"));
        Assert.True(_codec.Descriptor.MatchesExtension(".ots"));
        Assert.True(_codec.Descriptor.MatchesMimeType("application/vnd.oasis.opendocument.spreadsheet"));
    }

    [Fact]
    public void Probe_Recognizes_Uncompressed_MimeType_Entry()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using var s = entry.Open();
            s.Write(Encoding.ASCII.GetBytes("application/vnd.oasis.opendocument.spreadsheet"));
        }
        ms.Position = 0;

        byte[] prefix = new byte[Math.Min(ms.Length, 4096)];
        ms.ReadExactly(prefix);

        var request = new DocumentProbeRequest(prefix, DocumentSourceHints.Empty);
        DocumentProbeResult result = _codec.Probe(request);

        Assert.True(result.IsMatch);
        Assert.Equal(DocumentProbeConfidence.Certain, result.Confidence);
        Assert.Equal("ODS", result.FormatName);
    }

    [Fact]
    public void Probe_Matches_With_Extension_Hint_On_Zip_Header()
    {
        byte[] zipHeader = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00];
        var hints = new DocumentSourceHints("budget.ods", "application/vnd.oasis.opendocument.spreadsheet");
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
            new(new CellAddress(0, 0), CellValue.FromText("Category")),
            new(new CellAddress(0, 1), CellValue.FromText("Amount")),
            new(new CellAddress(0, 2), CellValue.FromText("Approved")),
            new(new CellAddress(1, 0), CellValue.FromText("Travel")),
            new(new CellAddress(1, 1), CellValue.FromNumber(450.75)),
            new(new CellAddress(1, 2), CellValue.FromBoolean(true)),
            new(new CellAddress(2, 0), CellValue.FromText("Total")),
            new(new CellAddress(2, 1), CellValue.FromNumber(450.75), formula: "=SUM(B2:B2)")
        };

        var row0 = new SpreadsheetRow(0, cells.GetRange(0, 3));
        var row1 = new SpreadsheetRow(1, cells.GetRange(3, 3));
        var row2 = new SpreadsheetRow(2, cells.GetRange(6, 2));

        var sheet = new SpreadsheetWorksheet("Expenses", [row0, row1, row2]);
        var workbook = new SpreadsheetWorkbook([sheet]);

        RichTextDocument originalDoc = workbook.ToRichTextDocument();

        // Write
        using var stream = new MemoryStream();
        DocumentWriteResult writeResult = _codec.Write(originalDoc, stream);
        Assert.Equal(DocumentResultStatus.Success, writeResult.Status);
        Assert.True(stream.Length > 0);

        // Verify mimetype is first entry
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            Assert.Equal("mimetype", archive.Entries[0].FullName);
        }

        // Read back
        stream.Position = 0;
        DocumentReadResult readResult = _codec.Read(stream);

        Assert.Equal(DocumentResultStatus.Success, readResult.Status);
        Assert.IsType<OdsReadResult>(readResult);

        var odsResult = (OdsReadResult)readResult;
        SpreadsheetWorkbook readWb = odsResult.Workbook;

        Assert.Single(readWb.Sheets);
        SpreadsheetWorksheet readSheet = readWb.Sheets[0];
        Assert.Equal("Expenses", readSheet.Name);
        Assert.Equal(3, readSheet.RowCount);

        Assert.Equal("Travel", readSheet[1, 0]?.CachedText);
        Assert.Equal(450.75, readSheet[1, 1]?.Value.Number);
        Assert.True(readSheet[1, 2]?.Value.Boolean);
        Assert.Equal("=SUM(B2:B2)", readSheet[2, 1]?.Formula);

        // Verify document table mapping
        Assert.Single(readResult.Document.Tables);
        DocumentTable table = readResult.Document.Tables[0];
        Assert.Equal("Expenses", table.Name);
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal("=SUM(B2:B2)", table.Rows[2].Cells[1].Formula);
    }

    [Fact]
    public void Roundtrip_Multiple_Sheets_With_Merged_Cells()
    {
        var cells1 = new List<SpreadsheetCell>
        {
            new(new CellAddress(0, 0), CellValue.FromText("Overview"), columnSpan: 2, rowSpan: 1),
            new(new CellAddress(1, 0), CellValue.FromText("Alpha")),
            new(new CellAddress(1, 1), CellValue.FromNumber(42.0))
        };
        var sheet1 = new SpreadsheetWorksheet("OverviewSheet", [
            new SpreadsheetRow(0, [cells1[0]]),
            new SpreadsheetRow(1, cells1.GetRange(1, 2))
        ]);

        var cells2 = new List<SpreadsheetCell>
        {
            new(new CellAddress(0, 0), CellValue.FromText("Details")),
            new(new CellAddress(1, 0), CellValue.FromText("Line Item 1"))
        };
        var sheet2 = new SpreadsheetWorksheet("DetailsSheet", [
            new SpreadsheetRow(0, [cells2[0]]),
            new SpreadsheetRow(1, [cells2[1]])
        ]);

        var workbook = new SpreadsheetWorkbook([sheet1, sheet2]);
        RichTextDocument doc = workbook.ToRichTextDocument();

        using var stream = new MemoryStream();
        _codec.Write(doc, stream);

        stream.Position = 0;
        var readResult = (OdsReadResult)_codec.Read(stream);

        Assert.Equal(2, readResult.Workbook.Sheets.Count);
        Assert.Equal("OverviewSheet", readResult.Workbook.Sheets[0].Name);
        Assert.Equal("DetailsSheet", readResult.Workbook.Sheets[1].Name);

        SpreadsheetCell? mergedCell = readResult.Workbook.Sheets[0][0, 0];
        Assert.NotNull(mergedCell);
        Assert.Equal("Overview", mergedCell.CachedText);
        Assert.Equal(2, mergedCell.ColumnSpan);

        Assert.Equal(2, readResult.Document.Tables.Count);
        Assert.Equal("OverviewSheet", readResult.Document.Tables[0].Name);
        Assert.Equal("DetailsSheet", readResult.Document.Tables[1].Name);
    }

    [Fact]
    public void Read_Corrupt_Zip_Returns_Rejected_Status_With_Diagnostic()
    {
        byte[] junk = [0x50, 0x4B, 0x03, 0x04, 0xAA, 0xBB, 0xCC, 0xDD];
        using var stream = new MemoryStream(junk);

        DocumentReadResult result = _codec.Read(stream);

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, d => d.Code == "ods.corrupt");
    }
}
