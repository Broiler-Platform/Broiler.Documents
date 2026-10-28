using System.IO;
using System.Text;
using static Broiler.Documents.Csv.Tests.CsvFixtures;

namespace Broiler.Documents.Csv.Tests;

public sealed class CsvSepDirectiveTests
{
    [Fact]
    public void Sep_Line_With_Semicolon_Sets_Delimiter_And_Is_Not_In_Document_Table()
    {
        string content = "sep=;\r\nalpha;beta;gamma\r\n1;2;3\r\n4;5;6\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        DocumentReadResult result = new CsvDocumentCodec().Read(stream);

        CsvReadResult csv = Assert.IsAssignableFrom<CsvReadResult>(result);
        Assert.Equal(';', csv.Delimiter);
        Assert.Equal(
            [["alpha", "beta", "gamma"], ["1", "2", "3"], ["4", "5", "6"]],
            Grid(result.Document));
    }

    [Fact]
    public void Sep_Line_With_Tab_Sets_Delimiter()
    {
        string content = "sep=\t\r\ncol1\tcol2\r\nval1\tval2\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        DocumentReadResult result = new CsvDocumentCodec().Read(stream);

        CsvReadResult csv = Assert.IsAssignableFrom<CsvReadResult>(result);
        Assert.Equal('\t', csv.Delimiter);
        Assert.Equal([["col1", "col2"], ["val1", "val2"]], Grid(result.Document));
    }

    [Fact]
    public void Sep_Line_With_Comma_And_Lf_Only()
    {
        string content = "sep=,\nheader1,header2\ndata1,data2\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        DocumentReadResult result = new CsvDocumentCodec().Read(stream);

        CsvReadResult csv = Assert.IsAssignableFrom<CsvReadResult>(result);
        Assert.Equal(',', csv.Delimiter);
        Assert.Equal([["header1", "header2"], ["data1", "data2"]], Grid(result.Document));
    }

    [Fact]
    public void Sep_Line_With_Pipe_Delimiter()
    {
        string content = "sep=|\r\na|b|c\r\n1|2|3\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        DocumentReadResult result = new CsvDocumentCodec().Read(stream);

        CsvReadResult csv = Assert.IsAssignableFrom<CsvReadResult>(result);
        Assert.Equal('|', csv.Delimiter);
        Assert.Equal([["a", "b", "c"], ["1", "2", "3"]], Grid(result.Document));
    }

    [Fact]
    public void Sep_Line_Alone_Yields_Empty_Document_And_Carries_Delimiter()
    {
        string content = "sep=;\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        DocumentReadResult result = new CsvDocumentCodec().Read(stream);

        CsvReadResult csv = Assert.IsAssignableFrom<CsvReadResult>(result);
        Assert.Equal(';', csv.Delimiter);
        Assert.Empty(result.Document.Tables);
        Assert.Equal(1, result.Document.ParagraphCount);
    }

    [Fact]
    public void Non_Sep_Line_Starting_With_Sep_Is_Read_As_Data()
    {
        // "separator;date" is not "sep=<single-char>"
        string content = "separator;date\r\ncomma;2026-10-28\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        DocumentReadResult result = new CsvDocumentCodec().Read(stream);

        CsvReadResult csv = Assert.IsAssignableFrom<CsvReadResult>(result);
        Assert.Equal(';', csv.Delimiter);
        Assert.Equal([["separator", "date"], ["comma", "2026-10-28"]], Grid(result.Document));
    }

    [Fact]
    public void Probe_Returns_High_Confidence_For_Sep_Prefixed_Csv()
    {
        string content = "sep=;\r\na;b;c\r\n1;2;3\r\n";
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        var hints = new DocumentSourceHints("data.csv");
        DocumentProbeResult probe = new CsvDocumentCodec().Probe(new DocumentProbeRequest(bytes, hints));

        Assert.True(probe.IsMatch);
        Assert.Equal(DocumentProbeConfidence.High, probe.Confidence);
    }
}
