using System.Text;
using static Broiler.Documents.Csv.Tests.CsvFixtures;

namespace Broiler.Documents.Csv.Tests;

public sealed class CsvReaderTests
{
    [Fact]
    public void Reads_Records_Into_One_Table_Over_Every_Paragraph()
    {
        DocumentReadResult result = Read("a,b\r\nc,d\r\n");

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        Assert.Empty(result.Diagnostics);
        Assert.Equal([["a", "b"], ["c", "d"]], Grid(result.Document));

        DocumentTable table = result.Document.Tables[0];
        Assert.Equal(0, table.ParagraphIndex);
        Assert.Equal(result.Document.ParagraphCount, table.ParagraphCount);
        Assert.Equal([0, 1], table.Rows[1].Cells.Select(cell => cell.ColumnIndex));
        Assert.Empty(table.ColumnWidths);
    }

    [Theory]
    [InlineData("a,b\nc,d")]
    [InlineData("a,b\nc,d\n")]
    [InlineData("a,b\rc,d\r")]
    [InlineData("a,b\r\nc,d")]
    public void The_Last_Line_Break_Is_Optional_And_Any_Line_Break_Ends_A_Record(string csv)
    {
        Assert.Equal([["a", "b"], ["c", "d"]], Grid(Read(csv).Document));
    }

    [Fact]
    public void Quoted_Fields_Hold_Delimiters_Quotes_And_Line_Breaks()
    {
        DocumentReadResult result = Read("\"x,y\",\"say \"\"hi\"\"\",\"line 1\r\nline 2\"\r\n");

        Assert.Equal([["x,y", "say \"hi\"", "line 1\nline 2"]], Grid(result.Document));
        Assert.Equal(2, result.Document.Tables[0].Rows[0].Cells[2].ParagraphCount);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Spaces_Belong_To_The_Field()
    {
        Assert.Equal([[" a ", " b"]], Grid(Read(" a , b").Document));
    }

    [Fact]
    public void Empty_Fields_Lines_And_Trailing_Delimiters_Are_Empty_Cells()
    {
        Assert.Equal([["a", "", "c"], ["", "", ""], ["d", "e", ""]], Grid(Read("a,,c\n,,\nd,e,\n").Document));
    }

    [Fact]
    public void A_Blank_Line_Is_An_Empty_Row()
    {
        Assert.Equal([["a"], [""], ["b"]], Grid(Read("a\n\nb\n").Document));
    }

    [Fact]
    public void Empty_Input_Is_An_Empty_Document()
    {
        DocumentReadResult result = Read(string.Empty);

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        Assert.Empty(result.Document.Tables);
        Assert.Equal(1, result.Document.ParagraphCount);
    }

    [Fact]
    public void Short_Records_Are_Padded_And_Noted()
    {
        DocumentReadResult result = Read("a,b,c\nd\ne,f\n");

        Assert.Equal([["a", "b", "c"], ["d", "", ""], ["e", "f", ""]], Grid(result.Document));
        DocumentDiagnostic ragged = Assert.Single(result.Diagnostics);
        Assert.Equal(CsvDiagnosticCodes.RowRagged, ragged.Code);
        Assert.Equal(DocumentDiagnosticSeverity.Info, ragged.Severity);
        Assert.Contains("2 record(s)", ragged.Message, StringComparison.Ordinal);
        Assert.Equal(DocumentResultStatus.Success, result.Status);
    }

    [Fact]
    public void Values_Are_Text_As_Written()
    {
        // Nothing is parsed or evaluated: a spreadsheet's types are its own
        // reading of the text.
        Assert.Equal([["=SUM(A1:A2)", "007", "1e5", "2026-10-12"]], Grid(Read("=SUM(A1:A2),007,1e5,2026-10-12").Document));
    }

    [Fact]
    public void Detects_A_Semicolon_Delimiter_With_Decimal_Commas_In_The_Values()
    {
        DocumentReadResult result = Read("Name;Preis\nApfel;1,50\nBirne;2,00\n");

        Assert.Equal([["Name", "Preis"], ["Apfel", "1,50"], ["Birne", "2,00"]], Grid(result.Document));
    }

    [Fact]
    public void A_Title_Line_Above_The_Data_Does_Not_Hide_The_Delimiter()
    {
        DocumentReadResult result = Read("Umsatzbericht 2026\nName;Preis\nApfel;1,50\n");

        Assert.Equal([["Umsatzbericht 2026", ""], ["Name", "Preis"], ["Apfel", "1,50"]], Grid(result.Document));
        Assert.Equal([CsvDiagnosticCodes.RowRagged], Codes(result.Diagnostics));
    }

    [Fact]
    public void Detects_A_Tab_Delimiter()
    {
        Assert.Equal([["a b", "c"], ["d", "e,f"]], Grid(Read("a b\tc\nd\te,f\n").Document));
    }

    [Fact]
    public void Detection_Respects_Quotes()
    {
        Assert.Equal([["a;b", "c"], ["d;e", "f"]], Grid(Read("\"a;b\",c\n\"d;e\",f\n").Document));
    }

    [Fact]
    public void A_Tie_Goes_To_The_Rarer_Delimiter()
    {
        // Both split every record in two. Commas turn up inside values far more
        // often than semicolons do.
        Assert.Equal([["a", "b,c"], ["d", "e,f"]], Grid(Read("a;b,c\nd;e,f\n").Document));
    }

    [Fact]
    public void Text_With_No_Candidate_Delimiter_Is_One_Column()
    {
        Assert.Equal([["first line"], ["second line"]], Grid(Read("first line\nsecond line\n").Document));
    }

    [Fact]
    public void A_Stated_Delimiter_Is_Used_As_Stated()
    {
        DocumentReadResult result = Read("a,b;c\n", new CsvReadOptions(delimiter: ';'));

        Assert.Equal([["a,b", "c"]], Grid(result.Document));
    }

    [Fact]
    public void The_Header_Row_Is_Marked_Only_When_The_Caller_Says_So()
    {
        Assert.All(Read("h1,h2\nv1,v2\n").Document.Tables[0].Rows, row => Assert.False(row.IsHeader));

        TableRow[] rows = [.. Read("h1,h2\nv1,v2\n", new CsvReadOptions(hasHeaderRow: true)).Document.Tables[0].Rows];
        Assert.True(rows[0].IsHeader);
        Assert.False(rows[1].IsHeader);
    }

    [Fact]
    public void A_Utf8_Byte_Order_Mark_Is_Not_Text()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Grüße,€\n")];

        DocumentReadResult result = Read(bytes);

        Assert.Equal([["Grüße", "€"]], Grid(result.Document));
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Utf16_Is_Read_From_Its_Byte_Order_Mark(bool bigEndian)
    {
        var encoding = new UnicodeEncoding(bigEndian, byteOrderMark: true);
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes("Grüße;€\n")];

        Assert.Equal([["Grüße", "€"]], Grid(Read(bytes).Document));
    }

    [Fact]
    public void Utf8_Without_A_Byte_Order_Mark_Needs_No_Guess()
    {
        DocumentReadResult result = Read(Encoding.UTF8.GetBytes("Grüße,€\n"));

        Assert.Equal([["Grüße", "€"]], Grid(result.Document));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Bytes_That_Are_Not_Utf8_Are_Read_As_Windows_1252_And_Reported()
    {
        // "Preis;€ 5" and "Größe" as a spreadsheet on Windows saves them: 0x80 is
        // the euro sign, 0xF6 and 0xDF are ö and ß. 0x80 would be a C1 control
        // character in Latin-1.
        byte[] bytes = [.. "Preis;"u8, 0x80, .. " 5\nGr"u8, 0xF6, 0xDF, .. "e;x\n"u8];

        DocumentReadResult result = Read(bytes);

        Assert.Equal([["Preis", "€ 5"], ["Größe", "x"]], Grid(result.Document));
        Assert.Equal([CsvDiagnosticCodes.EncodingFallback], Codes(result.Diagnostics));
        Assert.Equal(DocumentResultStatus.Partial, result.Status);
    }

    [Fact]
    public void Invalid_Bytes_After_A_Byte_Order_Mark_Are_Replaced_And_Reported()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. "a,"u8, 0xFF, .. "b\n"u8];

        DocumentReadResult result = Read(bytes);

        Assert.Equal([["a", (char)0xFFFD + "b"]], Grid(result.Document));
        Assert.Equal([CsvDiagnosticCodes.EncodingInvalid], Codes(result.Diagnostics));
    }

    [Fact]
    public void Input_Past_The_Byte_Limit_Is_Cut_And_Reported_As_An_Error()
    {
        var options = new CsvReadOptions(new DocumentLimits(maxDocumentBytes: 8));

        DocumentReadResult result = Read("a,b\nc,d\ne,f\n", options);

        Assert.Equal([["a", "b"], ["c", "d"]], Grid(result.Document));
        DocumentDiagnostic limit = Assert.Single(result.Diagnostics);
        Assert.Equal(CsvDiagnosticCodes.LimitBytes, limit.Code);
        Assert.Equal(DocumentDiagnosticSeverity.Error, limit.Severity);
    }

    [Fact]
    public void A_Character_Split_By_The_Byte_Limit_Does_Not_Turn_The_File_Into_Windows_1252()
    {
        // "a,ä" is 61 2C C3 A4; a limit of three bytes cuts the ä in half.
        var options = new CsvReadOptions(new DocumentLimits(maxDocumentBytes: 3));

        DocumentReadResult result = Read("a,ä", options);

        Assert.Equal([["a", ""]], Grid(result.Document));
        Assert.Equal([CsvDiagnosticCodes.LimitBytes], Codes(result.Diagnostics));
    }

    [Fact]
    public void An_Unterminated_Quote_Runs_To_The_End_And_Is_Reported()
    {
        DocumentReadResult result = Read("a,\"b\nc,d\n");

        Assert.Equal([["a", "b\nc,d\n"]], Grid(result.Document));
        Assert.Equal([CsvDiagnosticCodes.QuoteUnterminated], Codes(result.Diagnostics));
        Assert.Equal(DocumentResultStatus.Partial, result.Status);
    }

    [Theory]
    [InlineData("5\" disk,x\n", "5\" disk")]
    [InlineData("\"ab\"cd,x\n", "abcd")]
    public void A_Stray_Quote_Is_Kept_As_Text_And_Noted(string csv, string expected)
    {
        DocumentReadResult result = Read(csv);

        Assert.Equal([[expected, "x"]], Grid(result.Document));
        DocumentDiagnostic stray = Assert.Single(result.Diagnostics);
        Assert.Equal(CsvDiagnosticCodes.QuoteStray, stray.Code);
        Assert.Equal(DocumentResultStatus.Success, result.Status);
    }

    [Fact]
    public void Records_Past_The_Paragraph_Limit_Are_Dropped_Whole()
    {
        var options = new CsvReadOptions(new DocumentLimits(maxParagraphCount: 3));

        DocumentReadResult result = Read("a,b\nc,d\ne,f\n", options);

        Assert.Equal([["a", "b"]], Grid(result.Document));
        DocumentDiagnostic limit = Assert.Single(result.Diagnostics);
        Assert.Equal(CsvDiagnosticCodes.LimitParagraphs, limit.Code);
        Assert.Contains("2 of 3", limit.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Padding_Counts_Against_The_Paragraph_Limit()
    {
        // One very wide record must not make every other row a million cells.
        var options = new CsvReadOptions(new DocumentLimits(maxParagraphCount: 10));

        DocumentReadResult result = Read("a,b,c,d,e,f,g,h\ni\n", options);

        Assert.Single(result.Document.Tables[0].Rows);
        Assert.Contains(CsvDiagnosticCodes.LimitParagraphs, Codes(result.Diagnostics));
    }

    [Fact]
    public void A_Line_Past_The_Run_Limit_Is_Truncated()
    {
        var options = new CsvReadOptions(new DocumentLimits(maxRunLength: 3));

        DocumentReadResult result = Read("abcdef,x\n", options);

        Assert.Equal([["abc", "x"]], Grid(result.Document));
        Assert.Equal([CsvDiagnosticCodes.LimitRun], Codes(result.Diagnostics));
    }

    [Fact]
    public void Another_Codecs_Options_Are_Refused_Before_Reading()
    {
        DocumentReadResult result = Read("a,b\n", new OtherReadOptions());

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Equal(DocumentDiagnosticCodes.OptionsInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void The_Shared_Options_Are_Accepted_And_Their_Limits_Apply()
    {
        DocumentReadResult result = Read("abcdef\n", new DocumentReadOptions(new DocumentLimits(maxRunLength: 2)));

        Assert.Equal([["ab"]], Grid(result.Document));
    }

    [Theory]
    [InlineData('"')]
    [InlineData('\r')]
    [InlineData('\n')]
    public void A_Delimiter_That_Cannot_Separate_Fields_Is_Refused(char delimiter)
    {
        Assert.Throws<ArgumentException>(() => new CsvReadOptions(delimiter: delimiter));
        Assert.Throws<ArgumentException>(() => new CsvWriteOptions(delimiter: delimiter));
    }

    [Fact]
    public void A_Cancelled_Request_Is_Rejected()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        DocumentReadResult result = new CsvDocumentCodec().Read(
            DocumentReadRequest.FromBytes(Encoding.UTF8.GetBytes("a,b\n"), cancellationToken: cancellation.Token));

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Equal(DocumentDiagnosticCodes.Cancelled, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void A_Request_Reads_Like_A_Stream()
    {
        DocumentReadResult result = new CsvDocumentCodec().Read(
            DocumentReadRequest.FromBytes(Encoding.UTF8.GetBytes("a;b\nc;d\n"), new CsvReadOptions(hasHeaderRow: true), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal([["a", "b"], ["c", "d"]], Grid(result.Document));
        Assert.True(result.Document.Tables[0].Rows[0].IsHeader);
    }

    private sealed class OtherReadOptions : DocumentReadOptions;
}
