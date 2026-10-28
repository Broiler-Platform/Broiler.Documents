using System.Text;
using Broiler.Documents.Model;

namespace Broiler.Documents.Csv.Tests;

public sealed class CsvMediaTypeParametersTests
{
    [Fact]
    public void MimeType_Hint_With_Header_Present_Marks_First_Row_As_Header()
    {
        string csv = "ColA,ColB\r\nVal1,Val2\r\n";
        byte[] bytes = Encoding.UTF8.GetBytes(csv);
        var hints = new DocumentSourceHints("test.csv", "text/csv; header=present");
        using var input = DocumentInput.FromBytes(bytes);
        var request = new DocumentReadRequest(input, hints: hints);

        DocumentReadResult result = new CsvDocumentCodec().Read(request);

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        DocumentTable table = Assert.Single(result.Document.Tables);
        Assert.True(table.Rows[0].IsHeader);
        Assert.False(table.Rows[1].IsHeader);
    }

    [Fact]
    public void MimeType_Hint_With_Header_Absent_Leaves_First_Row_NonHeader()
    {
        string csv = "ColA,ColB\r\nVal1,Val2\r\n";
        byte[] bytes = Encoding.UTF8.GetBytes(csv);
        var hints = new DocumentSourceHints("test.csv", "text/csv; header=absent");
        using var input = DocumentInput.FromBytes(bytes);
        var request = new DocumentReadRequest(input, hints: hints);

        DocumentReadResult result = new CsvDocumentCodec().Read(request);

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        DocumentTable table = Assert.Single(result.Document.Tables);
        Assert.False(table.Rows[0].IsHeader);
    }

    [Fact]
    public void MimeType_Hint_With_Explicit_Charset_Windows1252_Decodes_Without_Fallback_Warning()
    {
        // 0x80 in Windows-1252 is Euro sign '€'
        byte[] bytes = [(byte)'p', (byte)'r', (byte)'i', (byte)'c', (byte)'e', (byte)',', 0x80, (byte)'5', (byte)'0', (byte)'\r', (byte)'\n'];
        var hints = new DocumentSourceHints("prices.csv", "text/csv; charset=windows-1252");
        using var input = DocumentInput.FromBytes(bytes);
        var request = new DocumentReadRequest(input, hints: hints);

        DocumentReadResult result = new CsvDocumentCodec().Read(request);

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        Assert.Empty(result.Diagnostics); // No fallback warning because charset was explicit!
        CsvReadResult csv = Assert.IsAssignableFrom<CsvReadResult>(result);
        Assert.NotNull(csv.Encoding);
        Assert.Equal("windows-1252", csv.Encoding.WebName);

        DocumentTable table = Assert.Single(result.Document.Tables);
        Assert.Equal("€50", result.Document.Paragraphs[table.Rows[0].Cells[1].ParagraphIndex].Text);
    }

    [Fact]
    public void MimeType_Hint_With_Header_And_Charset_Combined()
    {
        string csv = "item,cost\r\nwidget,10\r\n";
        byte[] bytes = Encoding.UTF8.GetBytes(csv);
        var hints = new DocumentSourceHints("data.csv", "text/csv; header=present; charset=utf-8");
        using var input = DocumentInput.FromBytes(bytes);
        var request = new DocumentReadRequest(input, hints: hints);

        DocumentReadResult result = new CsvDocumentCodec().Read(request);

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        CsvReadResult csvResult = Assert.IsAssignableFrom<CsvReadResult>(result);
        Assert.Equal(Encoding.UTF8, csvResult.Encoding);
        DocumentTable table = Assert.Single(result.Document.Tables);
        Assert.True(table.Rows[0].IsHeader);
    }

    [Fact]
    public void Quoted_MimeType_Parameters_Are_Parsed_Correctly()
    {
        string csv = "h1,h2\r\nv1,v2\r\n";
        byte[] bytes = Encoding.UTF8.GetBytes(csv);
        var hints = new DocumentSourceHints("data.csv", "text/csv; header=\"present\"; charset=\"utf-8\"");
        using var input = DocumentInput.FromBytes(bytes);
        var request = new DocumentReadRequest(input, hints: hints);

        DocumentReadResult result = new CsvDocumentCodec().Read(request);

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        DocumentTable table = Assert.Single(result.Document.Tables);
        Assert.True(table.Rows[0].IsHeader);
    }
}
