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
    public void Resolves_Indexed_And_Tinted_Theme_Colors()
    {
        var theme = System.Xml.Linq.XElement.Parse(
            "<clrScheme><accent1><srgbClr val='000000'/></accent1></clrScheme>");
        var indexed = XlsxCellAppearance.ReadColor(System.Xml.Linq.XElement.Parse("<color indexed='10'/>"));
        var tinted = XlsxCellAppearance.ReadColor(System.Xml.Linq.XElement.Parse("<color theme='4' tint='0.5'/>"), theme);
        Assert.Equal(Broiler.Graphics.Color.BColor.FromArgb(255, 0, 0), indexed);
        Assert.Equal(Broiler.Graphics.Color.BColor.FromArgb(128, 128, 128), tinted);
    }

    [Fact]
    public void Cell_Font_Borders_And_Horizontal_Alignment_Roundtrip()
    {
        var color = Broiler.Graphics.Color.BColor.FromArgb(51, 102, 153);
        var border = new TableBorder(color, 1.5, TableBorderStyle.Dashed);
        var cell = new SpreadsheetCell(new CellAddress(0, 0), CellValue.FromText("Styled"))
        {
            Alignment = TextAlignment.Right,
            Font = new InlineStyle { FontFamily = "Arial", FontSize = 15, Bold = true, Italic = true, Foreground = color },
            Borders = CellBorders.All(border),
        };
        var workbook = new SpreadsheetWorkbook(
            [new SpreadsheetWorksheet("Styles", [new SpreadsheetRow(0, [cell])])]);
        using var stream = new MemoryStream();
        Assert.Equal(DocumentResultStatus.Success, _codec.Write(workbook.ToRichTextDocument(), stream).Status);

        stream.Position = 0;
        var result = Assert.IsType<XlsxReadResult>(_codec.Read(stream));
        var actual = Assert.Single(result.Workbook.Sheets)[0, 0]!;
        Assert.Equal(cell.Font, actual.Font);
        Assert.Equal(cell.Borders, actual.Borders);
        Assert.Equal(cell.Alignment, actual.Alignment);
        Assert.Equal(cell.Font, result.Document.Paragraphs[0].StyleAt(0));
    }

    [Fact]
    public void Column_Widths_Use_Calibri_Digit_Metrics_And_Pixel_Rounding()
    {
        Assert.Equal(45.75, XlsxColumnWidth.ToPoints(8.7109375));
        Assert.Equal(8.7109375, XlsxColumnWidth.FromPoints(45.75));
        Assert.Equal(99.75, XlsxColumnWidth.ToPoints(XlsxColumnWidth.FromPoints(100)));
    }

    [Fact]
    public void Workbook_1904_Dates_Normalize_Without_Shifting_Times_Or_Numbers()
    {
        var workbook = new SpreadsheetWorkbook([new SpreadsheetWorksheet("Dates",
            [new SpreadsheetRow(0,
            [
                new(new CellAddress(0, 0), CellValue.FromNumber(0), numberFormat: "yyyy-mm-dd"),
                new(new CellAddress(0, 1), CellValue.FromNumber(0.5), numberFormat: "hh:mm"),
                new(new CellAddress(0, 2), CellValue.FromNumber(0)),
            ])])]);
        using var stream = new MemoryStream();
        _codec.Write(workbook.ToRichTextDocument(), stream);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("xl/workbook.xml")!;
            System.Xml.Linq.XElement root;
            using (var part = entry.Open())
                root = System.Xml.Linq.XElement.Load(part);
            root.AddFirst(new System.Xml.Linq.XElement(root.Name.Namespace + "workbookPr",
                new System.Xml.Linq.XAttribute("date1904", "1")));
            entry.Delete();
            using var replacement = zip.CreateEntry("xl/workbook.xml").Open();
            root.Save(replacement);
        }

        stream.Position = 0;
        var result = Assert.IsType<XlsxReadResult>(_codec.Read(stream));
        var sheet = Assert.Single(result.Workbook.Sheets);
        Assert.Equal(1462d, sheet[0, 0]?.Value.Number);
        Assert.Equal("1904-01-01", sheet[0, 0]?.CachedText);
        Assert.Equal(0.5d, sheet[0, 1]?.Value.Number);
        Assert.Equal(0d, sheet[0, 2]?.Value.Number);
    }

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
    public void Roundtrip_Preserves_Number_Formats_And_Solid_Fills()
    {
        var fill = Broiler.Graphics.Color.BColor.FromArgb(34, 102, 153);
        var row = new SpreadsheetRow(0,
        [
            new(new CellAddress(0, 0), CellValue.FromNumber(45000), formula: "=44999+1",
                numberFormat: "yyyy-mm-dd", cachedText: "2023-03-15", columnSpan: 2, shading: fill),
            new(new CellAddress(0, 2), CellValue.Empty, shading: fill),
            new(new CellAddress(0, 3), CellValue.FromNumber(12.5), numberFormat: "$0.00", shading: fill) { VerticalAlignment = CellVerticalAlignment.Center },
            new(new CellAddress(0, 4), CellValue.FromNumber(15), numberFormat: "$0.00", shading: fill) { VerticalAlignment = CellVerticalAlignment.Center }
        ]);
        var workbook = new SpreadsheetWorkbook([new SpreadsheetWorksheet("Styled", [row])]);

        using var stream = new MemoryStream();
        Assert.Equal(DocumentResultStatus.Success, _codec.Write(workbook.ToRichTextDocument(), stream).Status);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            using var stylesPart = archive.GetEntry("xl/styles.xml")!.Open();
            var styles = System.Xml.Linq.XElement.Load(stylesPart);
            var ns = styles.Name.Namespace;
            Assert.Equal("2", (string?)styles.Element(ns + "numFmts")?.Attribute("count"));
            Assert.Equal("3", (string?)styles.Element(ns + "fills")?.Attribute("count"));
            Assert.Equal("4", (string?)styles.Element(ns + "cellXfs")?.Attribute("count"));
        }

        stream.Position = 0;
        var result = Assert.IsType<XlsxReadResult>(_codec.Read(stream));
        Assert.Equal(DocumentResultStatus.Success, result.Status);
        var sheet = Assert.Single(result.Workbook.Sheets);
        Assert.Equal("yyyy-mm-dd", sheet[0, 0]?.NumberFormat);
        Assert.Equal(45000d, sheet[0, 0]?.Value.Number);
        Assert.Equal("=44999+1", sheet[0, 0]?.Formula);
        Assert.Equal(2, sheet[0, 0]?.ColumnSpan);
        Assert.Equal(fill, sheet[0, 0]?.Shading);
        Assert.Equal(fill, sheet[0, 2]?.Shading);
        Assert.Equal("$0.00", sheet[0, 3]?.NumberFormat);
        Assert.Equal(CellVerticalAlignment.Center, sheet[0, 3]?.VerticalAlignment);
        Assert.Equal(fill, sheet[0, 4]?.Shading);
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
