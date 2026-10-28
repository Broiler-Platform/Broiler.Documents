using System.IO;
using System.Text;
using Broiler.Documents.Model;
using static Broiler.Documents.Csv.Tests.CsvFixtures;

namespace Broiler.Documents.Csv.Tests;

public sealed class TsvCodecTests
{
    private readonly TsvDocumentCodec _codec = new();

    [Fact]
    public void Tsv_Descriptor_Has_Correct_Name_MimeType_And_Extensions()
    {
        Assert.Equal("TSV", _codec.Descriptor.Name);
        Assert.Contains("text/tab-separated-values", _codec.Descriptor.MimeTypes);
        Assert.Contains(".tsv", _codec.Descriptor.FileExtensions);
        Assert.Contains(".tab", _codec.Descriptor.FileExtensions);
    }

    [Fact]
    public void Probe_Returns_High_Confidence_For_Tab_Separated_Grid()
    {
        string tsv = "col1\tcol2\tcol3\r\na\tb\tc\r\n";
        byte[] bytes = Encoding.UTF8.GetBytes(tsv);
        var hints = new DocumentSourceHints("data.tsv");
        DocumentProbeResult probe = _codec.Probe(new DocumentProbeRequest(bytes, hints));

        Assert.True(probe.IsMatch);
        Assert.Equal(DocumentProbeConfidence.High, probe.Confidence);
    }

    [Fact]
    public void Probe_Returns_Medium_Confidence_For_Non_Grid_Text()
    {
        string text = "plain non-grid text";
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        var hints = new DocumentSourceHints("notes.tsv");
        DocumentProbeResult probe = _codec.Probe(new DocumentProbeRequest(bytes, hints));

        Assert.True(probe.IsMatch);
        Assert.Equal(DocumentProbeConfidence.Medium, probe.Confidence);
    }

    [Fact]
    public void Probe_Returns_NoMatch_Without_Tsv_Hint()
    {
        string tsv = "col1\tcol2\r\na\tb\r\n";
        byte[] bytes = Encoding.UTF8.GetBytes(tsv);
        DocumentProbeResult probe = _codec.Probe(new DocumentProbeRequest(bytes));

        Assert.False(probe.IsMatch);
    }

    [Fact]
    public void Read_Splits_Fields_By_Tab_And_Records_By_Line_Break()
    {
        string tsv = "a\tb\tc\r\n1\t2\t3\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(tsv));
        DocumentReadResult result = _codec.Read(stream);

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        TsvReadResult tsvResult = Assert.IsAssignableFrom<TsvReadResult>(result);
        Assert.Equal('\t', tsvResult.Delimiter);
        Assert.Equal([["a", "b", "c"], ["1", "2", "3"]], Grid(result.Document));
    }

    [Fact]
    public void Read_Treats_Double_Quotes_As_Plain_Literal_Characters()
    {
        // In pure TSV per RFC 1996 / IANA registration:
        // Quotes do NOT escape tabs, do NOT wrap fields, and do NOT escape themselves.
        string tsv = "\"quoted\"\t\"with \"\"inner\"\" quotes\"\tplain\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(tsv));
        DocumentReadResult result = _codec.Read(stream);

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        Assert.Empty(result.Diagnostics);
        Assert.Equal([["\"quoted\"", "\"with \"\"inner\"\" quotes\"", "plain"]], Grid(result.Document));
    }

    [Fact]
    public void Read_Header_Present_Hint_Marks_First_Row_Header()
    {
        string tsv = "name\tscore\r\nAlice\t100\r\n";
        byte[] bytes = Encoding.UTF8.GetBytes(tsv);
        var hints = new DocumentSourceHints("scores.tsv", "text/tab-separated-values; header=present");
        using var input = DocumentInput.FromBytes(bytes);
        var request = new DocumentReadRequest(input, hints: hints);

        DocumentReadResult result = _codec.Read(request);

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        DocumentTable table = Assert.Single(result.Document.Tables);
        Assert.True(table.Rows[0].IsHeader);
        Assert.False(table.Rows[1].IsHeader);
    }

    [Fact]
    public void Write_Emits_Tabs_And_Never_Encloses_Or_Doubles_Quotes()
    {
        RichTextDocument document = Table(
            ["name", "quote", "status"],
            ["Alice", "says \"hi\"", "active"]);

        using var stream = new MemoryStream();
        DocumentWriteResult result = _codec.Write(document, stream);

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        string written = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Equal("name\tquote\tstatus\r\nAlice\tsays \"hi\"\tactive\r\n", written);
    }

    [Fact]
    public void Write_Replaces_Embedded_Tabs_And_Newlines_With_Spaces()
    {
        RichTextDocument document = Table(
            ["field with\ttab", "field with\nnewline"]);

        using var stream = new MemoryStream();
        DocumentWriteResult result = _codec.Write(document, stream);

        Assert.Equal(DocumentResultStatus.Success, result.Status);
        string written = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Equal("field with tab\tfield with newline\r\n", written);
    }

    [Fact]
    public void RoundTrip_Through_Tsv_Preserves_Grid()
    {
        RichTextDocument original = Table(
            ["id", "first", "last"],
            ["1", "Jane", "Doe"],
            ["2", "John", "Smith"]);

        using var stream = new MemoryStream();
        _codec.Write(original, stream);

        stream.Position = 0;
        DocumentReadResult reread = _codec.Read(stream);

        Assert.Equal(Grid(original), Grid(reread.Document));
    }
}
