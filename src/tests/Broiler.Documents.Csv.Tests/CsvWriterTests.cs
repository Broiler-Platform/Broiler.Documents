using System.Text;
using Broiler.Graphics.Color;
using static Broiler.Documents.Csv.Tests.CsvFixtures;

namespace Broiler.Documents.Csv.Tests;

public sealed class CsvWriterTests
{
    [Fact]
    public void Writes_A_Table_As_Rfc_4180_Records()
    {
        DocumentWriteResult result = WriteResult(Table(["a", "b"], ["c", "d"]));

        Assert.Equal("a,b\r\nc,d\r\n", Write(Table(["a", "b"], ["c", "d"])));
        Assert.Equal(DocumentResultStatus.Success, result.Status);
        Assert.Equal(DocumentDestinationState.Committed, result.DestinationState);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Quotes_Only_The_Fields_That_Need_It()
    {
        string csv = Write(Table(["plain", "a,b", "say \"hi\"", "two\nlines", " spaced "]));

        Assert.Equal("plain,\"a,b\",\"say \"\"hi\"\"\",\"two\nlines\", spaced \r\n", csv);
    }

    [Fact]
    public void A_Stated_Delimiter_Changes_What_Needs_Quotes()
    {
        string csv = Write(Table(["1,50", "a;b"]), new CsvWriteOptions(delimiter: ';'));

        Assert.Equal("1,50;\"a;b\"\r\n", csv);
    }

    [Fact]
    public void A_Byte_Order_Mark_Is_Written_Only_On_Request()
    {
        Assert.Equal("a\r\n"u8.ToArray(), WriteBytes(Table(["a"])).Bytes);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a', 0x0D, 0x0A },
            WriteBytes(Table(["a"]), new CsvWriteOptions(byteOrderMark: true)).Bytes);
    }

    [Fact]
    public void Non_Ascii_Text_Is_Written_As_Utf8()
    {
        Assert.Equal(Encoding.UTF8.GetBytes("Grüße,€\r\n"), WriteBytes(Table(["Grüße", "€"])).Bytes);
    }

    [Fact]
    public void An_Empty_Document_Writes_Nothing()
    {
        (byte[] bytes, DocumentWriteResult result) = WriteBytes(RichTextDocument.Empty);

        Assert.Empty(bytes);
        Assert.Equal(DocumentResultStatus.Success, result.Status);
    }

    [Fact]
    public void Paragraphs_Outside_A_Table_Become_One_Field_Records()
    {
        var paragraphs = new List<RichTextParagraph> { RichTextParagraph.Plain("Prices, 2026") };
        DocumentTable table = TableOver(paragraphs, [["a", "b"]]);
        paragraphs.Add(RichTextParagraph.Empty);
        paragraphs.Add(RichTextParagraph.Plain("Source: survey"));
        RichTextDocument document = RichTextDocument.FromParagraphs(paragraphs).WithTables([table]);

        (byte[] bytes, DocumentWriteResult result) = WriteBytes(document);

        // The empty paragraph is spacing, not a row.
        Assert.Equal("\"Prices, 2026\"\r\na,b\r\nSource: survey\r\n", Encoding.UTF8.GetString(bytes));
        Assert.Equal([CsvDiagnosticCodes.TextOutsideTable], Codes(result.Diagnostics));
        Assert.Equal(DocumentResultStatus.Partial, result.Status);
    }

    [Fact]
    public void A_Document_That_Is_Not_A_Table_Keeps_Its_Text()
    {
        Assert.Equal("first\r\nsecond\r\n", Write(RichTextDocument.FromPlainText("first\n\nsecond")));
    }

    [Fact]
    public void A_Short_Row_Is_Padded_To_The_Table_Width()
    {
        Assert.Equal("a,b,c\r\nd,,\r\n", Write(Table(["a", "b", "c"], ["d"])));
    }

    [Fact]
    public void A_Merged_Cell_Is_Written_In_Its_First_Column_And_Reported()
    {
        // | wide (spans 2) | x |
        // | tall (2 rows)  | y | z |
        // | (covered)      | p | q |
        string[] texts = ["wide", "x", "tall", "y", "z", "p", "q"];
        RichTextDocument document = RichTextDocument.FromParagraphs(texts.Select(RichTextParagraph.Plain))
            .WithTables([new DocumentTable(0, texts.Length,
            [
                new TableRow([new TableCell(0, 1, 0, columnSpan: 2), new TableCell(1, 1, 2)]),
                new TableRow([new TableCell(2, 1, 0, rowSpan: 2), new TableCell(3, 1, 1), new TableCell(4, 1, 2)]),
                new TableRow([new TableCell(5, 0, 0, isRowSpanContinuation: true), new TableCell(5, 1, 1), new TableCell(6, 1, 2)]),
            ])]);

        (byte[] bytes, DocumentWriteResult result) = WriteBytes(document);

        Assert.Equal("wide,,x\r\ntall,y,z\r\n,p,q\r\n", Encoding.UTF8.GetString(bytes));
        Assert.Equal([CsvDiagnosticCodes.TableSpan], Codes(result.Diagnostics));
    }

    [Fact]
    public void A_Span_Past_Any_Real_Grid_Is_Clipped_Rather_Than_Allocated()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("a"), RichTextParagraph.Plain("b")])
            .WithTables([new DocumentTable(0, 2, [new TableRow([
                new TableCell(0, 1, 0, columnSpan: int.MaxValue),
                new TableCell(1, 1, int.MaxValue),
            ])])]);

        string csv = Write(document);

        Assert.Equal(CsvWriter.MaxColumns, csv.Split(',').Length);
        Assert.StartsWith("a,", csv, StringComparison.Ordinal);
        Assert.EndsWith(",b\r\n", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Nested_Table_Is_Written_As_Its_Cells_Text()
    {
        // The outer cell holds "outer" and a one-row table of "n1" and "n2".
        RichTextDocument document = RichTextDocument.FromParagraphs(
                new[] { "outer", "n1", "n2", "right" }.Select(RichTextParagraph.Plain))
            .WithTables([new DocumentTable(0, 4,
            [
                new TableRow([
                    new TableCell(0, 3, 0, tables: [new DocumentTable(1, 2, [new TableRow([new TableCell(1, 1, 0), new TableCell(2, 1, 1)])])]),
                    new TableCell(3, 1, 1),
                ]),
            ])]);

        (byte[] bytes, DocumentWriteResult result) = WriteBytes(document);

        Assert.Equal("\"outer\nn1\nn2\",right\r\n", Encoding.UTF8.GetString(bytes));
        Assert.Equal([CsvDiagnosticCodes.TableNested], Codes(result.Diagnostics));
    }

    [Fact]
    public void Table_Layout_Is_Reported_But_A_Header_Row_At_The_Top_Is_Not()
    {
        var plain = new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0)], isHeader: true), new TableRow([new TableCell(1, 1, 0)])]);
        var sized = new DocumentTable(0, 2, plain.Rows, columnWidths: [72]);
        RichTextDocument body = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("h"), RichTextParagraph.Plain("v")]);

        Assert.Empty(WriteResult(body.WithTables([plain])).Diagnostics);
        Assert.Equal([CsvDiagnosticCodes.TableLayout], Codes(WriteResult(body.WithTables([sized])).Diagnostics));
    }

    [Fact]
    public void Cell_Borders_And_Shading_Are_Reported_As_Layout()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("x")])
            .WithTables([new DocumentTable(0, 1, [new TableRow([
                new TableCell(0, 1, 0, borders: CellBorders.All(TableBorder.Solid(BColor.Black))),
            ])])]);

        Assert.Equal([CsvDiagnosticCodes.TableLayout], Codes(WriteResult(document).Diagnostics));
    }

    [Fact]
    public void Formatting_Is_Reported_Once_And_Its_Text_Is_Kept()
    {
        RichTextParagraph bold = RichTextParagraph.Create("bold", InlineStyle.Default with { Bold = true });
        RichTextParagraph link = RichTextParagraph.Create("site", InlineStyle.Default with { LinkHref = "https://example.org" });
        RichTextParagraph centered = RichTextParagraph.Create(
            "mid", InlineStyle.Default, ParagraphStyle.Default with { Alignment = TextAlignment.Center });
        var paragraphs = new List<RichTextParagraph> { bold, link, centered };
        RichTextDocument document = RichTextDocument.FromParagraphs(paragraphs)
            .WithTables([new DocumentTable(0, 3, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1), new TableCell(2, 1, 2)])])]);

        (byte[] bytes, DocumentWriteResult result) = WriteBytes(document);

        Assert.Equal("bold,site,mid\r\n", Encoding.UTF8.GetString(bytes));
        Assert.Equal([CsvDiagnosticCodes.InlineStyle, CsvDiagnosticCodes.ParagraphStyle], Codes(result.Diagnostics));
    }

    [Fact]
    public void A_Picture_Is_Left_Out_And_Reported()
    {
        var image = new InlineImage(new byte[] { 1, 2, 3 }, "image/png", 40, 20, "a logo");
        RichTextParagraph paragraph = RichTextParagraph.Plain("logo: ")
            .InsertText(6, InlineImage.PlaceholderText, InlineStyle.Default with { Image = image });

        (byte[] bytes, DocumentWriteResult result) = WriteBytes(RichTextDocument.FromParagraphs([paragraph])
            .WithTables([new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)])])]));

        Assert.Equal("logo: \r\n", Encoding.UTF8.GetString(bytes));
        Assert.Equal([CsvDiagnosticCodes.ImageOmitted], Codes(result.Diagnostics));
    }

    [Fact]
    public void A_Soft_Break_Is_Written_As_A_Line_Break_In_The_Field()
    {
        string text = "one" + (char)0x2028 + "two";

        Assert.Equal("\"one\ntwo\"\r\n", Write(Table([text])));
    }

    [Fact]
    public void Headers_Footers_And_Shapes_Are_Reported()
    {
        RichTextDocument document = Table(["a"])
            .WithRunningContent(RunningContent.Empty.WithHeader(PageSelection.Default, [RichTextParagraph.Plain("Page header")]))
            .WithShapes([new DocumentShape(0, 0, 0, 10, 10, ShapeFill.Solid(BColor.Black))]);

        (byte[] bytes, DocumentWriteResult result) = WriteBytes(document);

        Assert.Equal("a\r\n", Encoding.UTF8.GetString(bytes));
        Assert.Equal([CsvDiagnosticCodes.RunningContent, CsvDiagnosticCodes.ShapeOmitted], Codes(result.Diagnostics));
    }

    [Fact]
    public void Metadata_Has_Nowhere_To_Go_And_Is_Reported_As_Dropped()
    {
        var metadata = DocumentMetadata.Empty.With(title: "Quarterly figures");

        DocumentWriteResult result = WriteResult(Table(["a"]), new CsvWriteOptions(metadata: metadata));

        DocumentDiagnostic dropped = Assert.Single(result.Diagnostics);
        Assert.Equal(DocumentDiagnosticCodes.MetadataDropped, dropped.Code);
        Assert.DoesNotContain("Quarterly", dropped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Formulas_Are_Written_As_Text_Unless_Escaping_Is_Asked_For()
    {
        RichTextDocument document = Table(["=1+2", "+1", "-5", "@SUM(A1)", "plain", "a=b"]);

        Assert.Equal("=1+2,+1,-5,@SUM(A1),plain,a=b\r\n", Write(document));

        (byte[] bytes, DocumentWriteResult result) = WriteBytes(document, new CsvWriteOptions(escapeFormulas: true));

        Assert.Equal("'=1+2,'+1,'-5,'@SUM(A1),plain,a=b\r\n", Encoding.UTF8.GetString(bytes));
        DocumentDiagnostic escaped = Assert.Single(result.Diagnostics);
        Assert.Equal(CsvDiagnosticCodes.FormulaEscaped, escaped.Code);
        Assert.Contains("4 field(s)", escaped.Message, StringComparison.Ordinal);
        Assert.Equal(DocumentResultStatus.Success, result.Status);
    }

    [Fact]
    public void Another_Codecs_Options_Are_Refused_Before_Writing()
    {
        (byte[] bytes, DocumentWriteResult result) = WriteBytes(Table(["a"]), new OtherWriteOptions());

        Assert.Empty(bytes);
        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Equal(DocumentDiagnosticCodes.OptionsInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void A_Cancelled_Write_Request_Writes_Nothing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var stream = new MemoryStream();

        DocumentWriteResult result = new CsvDocumentCodec().Write(
            new DocumentWriteRequest(Table(["a"]), stream, cancellationToken: cancellation.Token));

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void A_Write_Request_Takes_Its_Typed_Options()
    {
        using var stream = new MemoryStream();

        new CsvDocumentCodec().Write(new DocumentWriteRequest(Table(["a", "b"]), stream, new CsvWriteOptions(delimiter: '\t')));

        Assert.Equal("a\tb\r\n", Encoding.UTF8.GetString(stream.ToArray()));
    }

    private sealed class OtherWriteOptions : DocumentWriteOptions;
}
