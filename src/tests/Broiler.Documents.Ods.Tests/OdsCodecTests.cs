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
    public void Cell_Font_Borders_And_Alignment_Survive_On_A_Blank_Cell()
    {
        var color = Broiler.Graphics.Color.BColor.FromArgb(51, 102, 153);
        var cell = new SpreadsheetCell(new CellAddress(0, 0), CellValue.Empty)
        {
            Font = new InlineStyle { FontFamily = "Arial", FontSize = 15, Bold = true, Foreground = color },
            Borders = CellBorders.All(new TableBorder(color, 1.5, TableBorderStyle.Dashed)),
            Alignment = TextAlignment.Right,
            VerticalAlignment = CellVerticalAlignment.Center,
        };
        var workbook = new SpreadsheetWorkbook(
            [new SpreadsheetWorksheet("Styles", [new SpreadsheetRow(0, [cell])])]);
        using var stream = new MemoryStream();
        Assert.Equal(DocumentResultStatus.Success, _codec.Write(workbook.ToRichTextDocument(), stream).Status);
        stream.Position = 0;

        var result = Assert.IsType<OdsReadResult>(_codec.Read(stream));
        var actual = Assert.Single(result.Workbook.Sheets)[0, 0]!;
        Assert.True(actual.Value.IsEmpty);
        Assert.Equal(cell.Font, actual.Font);
        Assert.Equal(cell.Borders, actual.Borders);
        Assert.Equal(cell.Alignment, actual.Alignment);
        Assert.Equal(cell.VerticalAlignment, actual.VerticalAlignment);
    }

    [Fact]
    public void Plain_Top_Aligned_Text_And_A_Left_Aligned_Number_Survive_The_Calc_Defaults()
    {
        // A cell with no style reads as Calc shows it: text at the foot of the
        // cell, a number to the right. The model's plain cell is top-aligned,
        // and a number may be left-aligned, so the writer has to say so; it
        // gave neither a style, and both came back as Calc's defaults.
        var text = new SpreadsheetCell(new CellAddress(0, 0), CellValue.FromText("plain"));
        var number = new SpreadsheetCell(new CellAddress(0, 1), CellValue.FromNumber(42)) { Alignment = TextAlignment.Left };
        var right = new SpreadsheetCell(new CellAddress(0, 2), CellValue.FromNumber(7)) { Alignment = TextAlignment.Right };
        var workbook = new SpreadsheetWorkbook(
            [new SpreadsheetWorksheet("Defaults", [new SpreadsheetRow(0, [text, number, right])])]);
        using var stream = new MemoryStream();
        Assert.Equal(DocumentResultStatus.Success, _codec.Write(workbook.ToRichTextDocument(), stream).Status);
        stream.Position = 0;

        SpreadsheetWorksheet sheet = Assert.Single(Assert.IsType<OdsReadResult>(_codec.Read(stream)).Workbook.Sheets);
        Assert.Equal(CellVerticalAlignment.Top, sheet[0, 0]!.VerticalAlignment);
        Assert.Equal(TextAlignment.Left, sheet[0, 0]!.Alignment);
        Assert.Equal(TextAlignment.Left, sheet[0, 1]!.Alignment);
        Assert.Equal(TextAlignment.Right, sheet[0, 2]!.Alignment);
    }

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
    public void Roundtrip_Preserves_Styles_And_Dimensions()
    {
        var fill = Broiler.Graphics.Color.BColor.FromArgb(34, 102, 153);
        var row = new SpreadsheetRow(0,
        [
            new(new CellAddress(0, 0), CellValue.FromNumber(450.75), formula: "=450+0.75",
                numberFormat: "$#,##0.00", columnSpan: 2, shading: fill),
            new(new CellAddress(0, 2), CellValue.FromNumber(0.25), numberFormat: "0.0%"),
            new(new CellAddress(0, 3), CellValue.Empty, shading: fill)
        ], height: 24);
        var workbook = new SpreadsheetWorkbook([new SpreadsheetWorksheet("Styled", [row], [80, 90, 100, 110])]);
        using var stream = new MemoryStream();
        Assert.Equal(DocumentResultStatus.Success, _codec.Write(workbook.ToRichTextDocument(), stream).Status);

        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        using (var content = archive.GetEntry("content.xml")!.Open())
        {
            var xml = System.Xml.Linq.XElement.Load(content);
            Assert.Equal("urn:oasis:names:tc:opendocument:xmlns:of:1.2",
                xml.GetNamespaceOfPrefix("of")?.NamespaceName);
        }

        stream.Position = 0;
        var result = Assert.IsType<OdsReadResult>(_codec.Read(stream));
        Assert.Equal(DocumentResultStatus.Success, result.Status);
        var sheet = Assert.Single(result.Workbook.Sheets);
        Assert.Equal(new double[] { 80, 90, 100, 110 }, sheet.ColumnWidths);
        Assert.Equal(24d, Assert.Single(sheet.Rows).Height);
        Assert.Equal("$#,##0.00", sheet[0, 0]?.NumberFormat);
        Assert.Equal(450.75, sheet[0, 0]?.Value.Number);
        Assert.Equal("=450+0.75", sheet[0, 0]?.Formula);
        Assert.Equal(2, sheet[0, 0]?.ColumnSpan);
        Assert.Equal(fill, sheet[0, 0]?.Shading);
        Assert.Equal("0.0%", sheet[0, 2]?.NumberFormat);
        Assert.Equal(fill, sheet[0, 3]?.Shading);
        Assert.True(sheet[0, 3]?.Value.IsEmpty);
    }

    [Theory]
    [InlineData(45000d, "yyyy-mm-dd")]
    [InlineData(45000d, "dd.mm.yyyy")]
    [InlineData(45000d, "mm/dd/yyyy")]
    [InlineData(59.5d, "yyyy-mm-dd")]
    public void Date_Format_Roundtrips_With_Its_Numeric_Value(double serial, string format)
    {
        var row = new SpreadsheetRow(0,
            [new(new CellAddress(0, 0), CellValue.FromNumber(serial), numberFormat: format)]);
        var workbook = new SpreadsheetWorkbook([new SpreadsheetWorksheet("Dates", [row])]);
        using var stream = new MemoryStream();
        Assert.Equal(DocumentResultStatus.Success, _codec.Write(workbook.ToRichTextDocument(), stream).Status);

        stream.Position = 0;
        var result = Assert.IsType<OdsReadResult>(_codec.Read(stream));
        Assert.Equal(DocumentResultStatus.Success, result.Status);
        var cell = result.Workbook.Sheets[0][0, 0];
        Assert.Equal(serial, cell?.Value.Number);
        Assert.Equal(format, cell?.NumberFormat);
    }

    [Fact]
    public void Iso_Date_Uses_The_Explicit_Null_Date_Without_Changing_Ordinary_Numbers()
    {
        const string content = """
            <office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
                xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0"
                xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0"
                xmlns:number="urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0">
              <office:automatic-styles>
                <number:date-style style:name="iso"><number:year number:style="long"/><number:text>-</number:text><number:month number:style="long"/><number:text>-</number:text><number:day number:style="long"/></number:date-style>
                <style:style style:name="date" style:family="table-cell" style:data-style-name="iso"/>
              </office:automatic-styles>
              <office:body><office:spreadsheet>
                <table:calculation-settings><table:null-date table:date-value="1904-01-01"/></table:calculation-settings>
                <table:table table:name="Dates"><table:table-column table:default-cell-style-name="date"/><table:table-column/><table:table-row>
                  <table:table-cell table:number-columns-repeated="2" office:value-type="float" office:value="0"/>
                </table:table-row></table:table>
              </office:spreadsheet></office:body>
            </office:document-content>
            """;
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry("content.xml").Open()))
            writer.Write(content);

        stream.Position = 0;
        var result = Assert.IsType<OdsReadResult>(_codec.Read(stream));
        var sheet = Assert.Single(result.Workbook.Sheets);
        Assert.Equal(1462d, sheet[0, 0]?.Value.Number);
        Assert.Equal("yyyy-mm-dd", sheet[0, 0]?.NumberFormat);
        Assert.Equal(0d, sheet[0, 1]?.Value.Number);
    }

    [Fact]
    public void Unsupported_Number_Format_Reports_Loss_And_Preserves_Value()
    {
        var row = new SpreadsheetRow(0,
            [new(new CellAddress(0, 0), CellValue.FromNumber(45000), numberFormat: "dddd, mmmm d")]);
        var workbook = new SpreadsheetWorkbook([new SpreadsheetWorksheet("Dates", [row])]);
        using var stream = new MemoryStream();
        var written = _codec.Write(workbook.ToRichTextDocument(), stream);
        Assert.Contains(written.Diagnostics, d => d.Code == OdsDiagnosticCodes.NumberFormatUnsupported);

        stream.Position = 0;
        var read = Assert.IsType<OdsReadResult>(_codec.Read(stream));
        Assert.Equal(45000d, read.Workbook.Sheets[0][0, 0]?.Value.Number);
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

    [Fact]
    public void Roundtrip_Preserves_Interior_Empty_Rows_And_Trims_Trailing_Empty_Rows()
    {
        RichTextDocument doc = RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain("First."),
            RichTextParagraph.Empty,
            RichTextParagraph.Plain("Third."),
            RichTextParagraph.Empty,
            RichTextParagraph.Plain("Last.")
        ]);

        using var stream = new MemoryStream();
        _codec.Write(doc, stream);

        stream.Position = 0;
        var readResult = (OdsReadResult)_codec.Read(stream);

        Assert.Equal(5, readResult.Document.ParagraphCount);
        Assert.Equal("First.", readResult.Document.Paragraphs[0].Text);
        Assert.Equal("", readResult.Document.Paragraphs[1].Text);
        Assert.Equal("Third.", readResult.Document.Paragraphs[2].Text);
        Assert.Equal("", readResult.Document.Paragraphs[3].Text);
        Assert.Equal("Last.", readResult.Document.Paragraphs[4].Text);
    }

    [Fact]
    public void Cell_Notes_Roundtrip_In_Ods()
    {
        var cell = new SpreadsheetCell(new CellAddress(0, 0), CellValue.FromText("Reviewed"))
        {
            Note = new CellNote("Checked and verified by auditor", "Auditor")
        };
        var workbook = new SpreadsheetWorkbook([new SpreadsheetWorksheet("Sheet1", [new SpreadsheetRow(0, [cell])])]);
        using var stream = new MemoryStream();
        Assert.Equal(DocumentResultStatus.Success, _codec.Write(workbook.ToRichTextDocument(), stream).Status);

        stream.Position = 0;
        var result = Assert.IsType<OdsReadResult>(_codec.Read(stream));
        var actual = Assert.Single(result.Workbook.Sheets)[0, 0]!;
        Assert.NotNull(actual.Note);
        Assert.Equal("Checked and verified by auditor", actual.Note.Text);
        Assert.Equal("Auditor", actual.Note.Author);
    }

    [Fact]
    public void ConditionalFormatting_Roundtrip_In_Ods()
    {
        var redFill = Broiler.Graphics.Color.BColor.FromArgb(255, 255, 199, 206);
        var redText = Broiler.Graphics.Color.BColor.FromArgb(255, 156, 0, 6);
        var greenFill = Broiler.Graphics.Color.BColor.FromArgb(255, 198, 239, 206);
        var greenText = Broiler.Graphics.Color.BColor.FromArgb(255, 0, 97, 0);

        var rule1 = new ConditionalFormatRule(
            ConditionalFormatRuleType.CellIs,
            ConditionalFormatOperator.GreaterThan,
            Formula1: "100",
            Style: new ConditionalFormatStyle(Shading: greenFill, Foreground: greenText, Bold: true));

        var rule2 = new ConditionalFormatRule(
            ConditionalFormatRuleType.ContainsText,
            Formula1: "Overdue",
            Style: new ConditionalFormatStyle(Shading: redFill, Foreground: redText));

        var rule3 = new ConditionalFormatRule(
            ConditionalFormatRuleType.ColorScale,
            ColorScale: new ColorScaleConfiguration(
                Broiler.Graphics.Color.BColor.FromArgb(255, 255, 0, 0),
                Broiler.Graphics.Color.BColor.FromArgb(255, 0, 255, 0)));

        var cf1 = new ConditionalFormat(
            [new CellRangeAddress(new CellAddress(0, 0), new CellAddress(9, 2))],
            [rule1, rule2]);

        var cf2 = new ConditionalFormat(
            [new CellRangeAddress(new CellAddress(0, 4), new CellAddress(9, 4))],
            [rule3]);

        var cell = new SpreadsheetCell(new CellAddress(0, 0), CellValue.FromNumber(150));
        var sheet = new SpreadsheetWorksheet("Report", [new SpreadsheetRow(0, [cell])])
        {
            ConditionalFormats = [cf1, cf2]
        };

        var workbook = new SpreadsheetWorkbook([sheet]);
        using var stream = new MemoryStream();
        Assert.Equal(DocumentResultStatus.Success, _codec.Write(workbook.ToRichTextDocument(), stream).Status);

        stream.Position = 0;
        var result = Assert.IsType<OdsReadResult>(_codec.Read(stream));
        var readSheet = Assert.Single(result.Workbook.Sheets);
        Assert.NotNull(readSheet.ConditionalFormats);
        Assert.Equal(2, readSheet.ConditionalFormats.Count);

        var readCf1 = readSheet.ConditionalFormats[0];
        Assert.Single(readCf1.Ranges);
        Assert.Equal(0, readCf1.Ranges[0].StartRow);
        Assert.Equal(9, readCf1.Ranges[0].EndRow);
        Assert.Equal(0, readCf1.Ranges[0].StartCol);
        Assert.Equal(2, readCf1.Ranges[0].EndCol);
        Assert.Equal(2, readCf1.Rules.Count);

        var readRule1 = readCf1.Rules[0];
        Assert.Equal(ConditionalFormatRuleType.CellIs, readRule1.Type);
        Assert.Equal(ConditionalFormatOperator.GreaterThan, readRule1.Operator);
        Assert.Equal("100", readRule1.Formula1);
        Assert.NotNull(readRule1.Style);
        Assert.Equal(greenFill, readRule1.Style.Shading);
        Assert.Equal(greenText, readRule1.Style.Foreground);
        Assert.True(readRule1.Style.Bold);

        var readRule2 = readCf1.Rules[1];
        Assert.Equal(ConditionalFormatRuleType.ContainsText, readRule2.Type);
        Assert.Equal("Overdue", readRule2.Formula1);
        Assert.NotNull(readRule2.Style);
        Assert.Equal(redFill, readRule2.Style.Shading);
        Assert.Equal(redText, readRule2.Style.Foreground);

        var readCf2 = readSheet.ConditionalFormats[1];
        Assert.Single(readCf2.Rules);
        Assert.Equal(ConditionalFormatRuleType.ColorScale, readCf2.Rules[0].Type);
        Assert.NotNull(readCf2.Rules[0].ColorScale);
        Assert.Equal(Broiler.Graphics.Color.BColor.FromArgb(255, 255, 0, 0), readCf2.Rules[0].ColorScale!.MinColor);
        Assert.Equal(Broiler.Graphics.Color.BColor.FromArgb(255, 0, 255, 0), readCf2.Rules[0].ColorScale!.MaxColor);
    }

    [Fact]
    public void DataValidation_Roundtrip_In_Ods()
    {
        var dvList = new DataValidation(
            [new CellRangeAddress(new CellAddress(0, 0), new CellAddress(4, 0))],
            DataValidationType.List,
            Formula1: "\"Red,Green,Blue\"",
            AllowBlank: true,
            ShowDropdown: true,
            ShowInputMessage: true,
            InputTitle: "Color",
            InputMessage: "Choose a color",
            ShowErrorMessage: true,
            ErrorTitle: "Invalid",
            ErrorMessage: "Not a valid color",
            ErrorStyle: DataValidationErrorStyle.Stop);

        var cell = new SpreadsheetCell(new CellAddress(0, 0), CellValue.FromText("Red"));
        var worksheet = new SpreadsheetWorksheet("Sheet1", [new SpreadsheetRow(0, [cell])])
        {
            DataValidations = [dvList]
        };

        var workbook = new SpreadsheetWorkbook([worksheet]);
        using var stream = new MemoryStream();
        Assert.Equal(DocumentResultStatus.Success, _codec.Write(workbook.ToRichTextDocument(), stream).Status);

        stream.Position = 0;
        var result = Assert.IsType<OdsReadResult>(_codec.Read(stream));
        var readSheet = Assert.Single(result.Workbook.Sheets);
        Assert.NotNull(readSheet.DataValidations);
        Assert.Single(readSheet.DataValidations);

        var readList = readSheet.DataValidations[0];
        Assert.Equal(DataValidationType.List, readList.Type);
        Assert.Equal("\"Red,Green,Blue\"", readList.Formula1);
        Assert.True(readList.AllowBlank);
        Assert.True(readList.ShowDropdown);
        Assert.True(readList.ShowInputMessage);
        Assert.Equal("Color", readList.InputTitle);
        Assert.Equal("Choose a color", readList.InputMessage);
        Assert.True(readList.ShowErrorMessage);
        Assert.Equal("Invalid", readList.ErrorTitle);
        Assert.Equal("Not a valid color", readList.ErrorMessage);
        Assert.Equal(DataValidationErrorStyle.Stop, readList.ErrorStyle);
    }

    [Fact]
    public void AutoFilter_And_HiddenRows_Roundtrip()
    {
        var row0 = new SpreadsheetRow(0, [
            new SpreadsheetCell(new CellAddress(0, 0), CellValue.FromText("Dept")),
            new SpreadsheetCell(new CellAddress(0, 1), CellValue.FromText("Headcount"))
        ]);
        var row1 = new SpreadsheetRow(1, [
            new SpreadsheetCell(new CellAddress(1, 0), CellValue.FromText("Engineering")),
            new SpreadsheetCell(new CellAddress(1, 1), CellValue.FromNumber(42))
        ], hidden: false);
        var row2 = new SpreadsheetRow(2, [
            new SpreadsheetCell(new CellAddress(2, 0), CellValue.FromText("Marketing")),
            new SpreadsheetCell(new CellAddress(2, 1), CellValue.FromNumber(15))
        ], hidden: true);

        var af = new SpreadsheetAutoFilter(
            new CellRangeAddress(new CellAddress(0, 0), new CellAddress(2, 1)),
            [new SpreadsheetFilterColumn(0, ["Engineering"])]);

        var worksheet = new SpreadsheetWorksheet("Team", [row0, row1, row2])
        {
            AutoFilter = af
        };

        var workbook = new SpreadsheetWorkbook([worksheet]);
        using var stream = new MemoryStream();
        Assert.Equal(DocumentResultStatus.Success, _codec.Write(workbook.ToRichTextDocument(), stream).Status);

        stream.Position = 0;
        var result = Assert.IsType<OdsReadResult>(_codec.Read(stream));
        var readSheet = Assert.Single(result.Workbook.Sheets);

        Assert.Equal(3, readSheet.Rows.Count);
        Assert.False(readSheet.Rows[0].Hidden);
        Assert.False(readSheet.Rows[1].Hidden);
        Assert.True(readSheet.Rows[2].Hidden);

        Assert.NotNull(readSheet.AutoFilter);
        Assert.Equal("A1:B3", readSheet.AutoFilter!.Range.Format());
        var col0 = Assert.Single(readSheet.AutoFilter.Columns);
        Assert.Equal(0, col0.ColumnIndex);
        Assert.Equal(["Engineering"], col0.Values);
    }

    [Fact]
    public void Cell_Hyperlinks_Roundtrip()
    {
        var extCell = new SpreadsheetCell(new CellAddress(0, 0), CellValue.FromText("OpenDocument Spec"))
        {
            Hyperlink = new CellHyperlink("https://docs.oasis-open.org/office/v1.2", "ODF Specification")
        };
        var intCell = new SpreadsheetCell(new CellAddress(1, 0), CellValue.FromText("Sheet Link"))
        {
            Hyperlink = new CellHyperlink("#Sheet2.A1", "Internal Link")
        };

        var worksheet = new SpreadsheetWorksheet("Sheet1", [
            new SpreadsheetRow(0, [extCell]),
            new SpreadsheetRow(1, [intCell])
        ]);

        var workbook = new SpreadsheetWorkbook([worksheet]);
        using var stream = new MemoryStream();
        Assert.Equal(DocumentResultStatus.Success, _codec.Write(workbook.ToRichTextDocument(), stream).Status);

        stream.Position = 0;
        var result = Assert.IsType<OdsReadResult>(_codec.Read(stream));
        var readSheet = Assert.Single(result.Workbook.Sheets);

        var readExt = readSheet[0, 0];
        Assert.NotNull(readExt);
        Assert.NotNull(readExt!.Hyperlink);
        Assert.Equal("https://docs.oasis-open.org/office/v1.2", readExt.Hyperlink!.Target);
        Assert.Equal("ODF Specification", readExt.Hyperlink.Tooltip);

        var readInt = readSheet[1, 0];
        Assert.NotNull(readInt);
        Assert.NotNull(readInt!.Hyperlink);
        Assert.Equal("#Sheet2.A1", readInt.Hyperlink!.Target);
        Assert.Equal("Internal Link", readInt.Hyperlink.Tooltip);
    }

    [Fact]
    public void Outline_And_Grouping_Properties_Roundtrip()
    {
        var row0 = new SpreadsheetRow(0, [new SpreadsheetCell(new CellAddress(0, 0), CellValue.FromText("Group 1 Header"))], outlineLevel: 0, collapsed: false);
        var row1 = new SpreadsheetRow(1, [new SpreadsheetCell(new CellAddress(1, 0), CellValue.FromText("Detail 1"))], outlineLevel: 1, collapsed: true, hidden: true);
        var row2 = new SpreadsheetRow(2, [new SpreadsheetCell(new CellAddress(2, 0), CellValue.FromText("Sub-Detail 1"))], outlineLevel: 2, collapsed: true, hidden: true);
        var row3 = new SpreadsheetRow(3, [new SpreadsheetCell(new CellAddress(3, 0), CellValue.FromText("Summary"))], outlineLevel: 0, collapsed: false);

        var col0 = new SpreadsheetColumn(0, width: 80.0, hidden: false, outlineLevel: 0, collapsed: false);
        var col1 = new SpreadsheetColumn(1, width: 60.0, hidden: true, outlineLevel: 1, collapsed: true);

        var worksheet = new SpreadsheetWorksheet("Outlines", [row0, row1, row2, row3], [80.0, 60.0])
        {
            Columns = [col0, col1],
        };

        var workbook = new SpreadsheetWorkbook([worksheet]);
        using var stream = new MemoryStream();
        Assert.Equal(DocumentResultStatus.Success, _codec.Write(workbook.ToRichTextDocument(), stream).Status);

        stream.Position = 0;
        var result = Assert.IsType<OdsReadResult>(_codec.Read(stream));
        var readSheet = Assert.Single(result.Workbook.Sheets);

        Assert.Equal(4, readSheet.Rows.Count);
        Assert.Equal(0, readSheet.Rows[0].OutlineLevel);
        Assert.False(readSheet.Rows[0].Collapsed);
        Assert.False(readSheet.Rows[0].Hidden);

        Assert.Equal(1, readSheet.Rows[1].OutlineLevel);
        Assert.True(readSheet.Rows[1].Collapsed);
        Assert.True(readSheet.Rows[1].Hidden);

        Assert.Equal(2, readSheet.Rows[2].OutlineLevel);
        Assert.True(readSheet.Rows[2].Collapsed);
        Assert.True(readSheet.Rows[2].Hidden);

        Assert.NotNull(readSheet.Columns);
        var rCol1 = readSheet.Columns!.FirstOrDefault(c => c.ColumnIndex == 1);
        Assert.NotNull(rCol1);
        Assert.Equal(1, rCol1!.OutlineLevel);
        Assert.True(rCol1.Collapsed);
        Assert.True(rCol1.Hidden);
    }
}
