using System.Text;
using Broiler.Documents.Markdown;

namespace Broiler.Documents.Csv.Tests;

public sealed class CsvProbeTests
{
    private static readonly CsvDocumentCodec Codec = new();

    [Fact]
    public void Content_Alone_Is_Never_A_Match()
    {
        // Prose with commas is CSV of some shape; without a hint the codec
        // would claim every plain-text file.
        DocumentProbeResult result = Probe("a,b,c\n1,2,3\n4,5,6\n");

        Assert.False(result.IsMatch);
    }

    [Theory]
    [InlineData("table.csv", null)]
    [InlineData("TABLE.CSV", null)]
    [InlineData(null, "text/csv")]
    [InlineData(null, "text/csv; charset=utf-8; header=present")]
    public void A_Hinted_Grid_Is_A_High_Confidence_Match(string? fileName, string? mimeType)
    {
        DocumentProbeResult result = Probe("a,b,c\n1,2,3\n4,5,6\n", fileName, mimeType);

        Assert.Equal(DocumentProbeConfidence.High, result.Confidence);
        Assert.Equal("CSV", result.FormatName);
        Assert.Equal("text/csv", result.MimeType);
    }

    [Theory]
    [InlineData("just one column\nof text\n")]
    [InlineData("a,b\nc\n")]
    [InlineData("")]
    public void Hinted_Text_That_Is_Not_A_Grid_Is_A_Medium_Match(string content)
    {
        Assert.Equal(DocumentProbeConfidence.Medium, Probe(content, "notes.csv").Confidence);
    }

    [Fact]
    public void A_Record_Cut_By_The_Prefix_Does_Not_Count_As_A_Short_Row()
    {
        // "4,5" is the start of a record the prefix ended inside.
        Assert.Equal(DocumentProbeConfidence.High, Probe("a,b,c\n1,2,3\n4,5", "t.csv").Confidence);
    }

    [Fact]
    public void Binary_Content_Is_Not_A_Match_Whatever_Its_Name()
    {
        // A spreadsheet package renamed .csv: zip headers carry NUL bytes.
        byte[] zip = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00];

        DocumentProbeResult result = Codec.Probe(new DocumentProbeRequest(zip, new DocumentSourceHints("book.csv")));

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Utf16_With_A_Byte_Order_Mark_Is_Read_As_Text()
    {
        byte[] bytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("a;b\n1;2\n3;4\n")];

        DocumentProbeResult result = Codec.Probe(new DocumentProbeRequest(bytes, new DocumentSourceHints("t.csv")));

        Assert.Equal(DocumentProbeConfidence.High, result.Confidence);
    }

    [Fact]
    public void A_Named_Csv_File_Wins_Over_Markdown_Reading_Its_First_Cell_As_A_List()
    {
        // "1. Quartal" is an ordered-list marker to the Markdown probe, which
        // claims it at medium confidence. The caller said .csv.
        var catalog = new DocumentCodecCatalog([new MarkdownDocumentCodec(), new CsvDocumentCodec()]);
        byte[] bytes = Encoding.UTF8.GetBytes("1. Quartal;100\n2. Quartal;200\n");

        DocumentCodecSelection selection = catalog.SelectAndRead(
            DocumentInput.FromBytes(bytes), hints: new DocumentSourceHints("umsatz.csv"));

        Assert.Equal("CSV", selection.Codec?.Name);
        Assert.Equal([["1. Quartal", "100"], ["2. Quartal", "200"]], CsvFixtures.Grid(selection.Result.Document));
    }

    [Fact]
    public void Without_A_Hint_Markdown_Keeps_What_It_Recognizes()
    {
        var catalog = new DocumentCodecCatalog([new MarkdownDocumentCodec(), new CsvDocumentCodec()]);

        DocumentCodecMatch? match = catalog.Select(Encoding.UTF8.GetBytes("# Title\n\nSome, text.\n"));

        Assert.Equal("Markdown", match?.Codec.Name);
    }

    private static DocumentProbeResult Probe(string content, string? fileName = null, string? mimeType = null) =>
        Codec.Probe(new DocumentProbeRequest(Encoding.UTF8.GetBytes(content), new DocumentSourceHints(fileName, mimeType)));
}
