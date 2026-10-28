using System.IO;
using System.Text;
using Broiler.Documents.Model;
using static Broiler.Documents.Csv.Tests.CsvFixtures;

namespace Broiler.Documents.Csv.Tests;

public sealed class CsvDialectCarryingTests
{
    [Fact]
    public void Read_Returns_CsvReadResult_With_Detected_Comma_Delimiter()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a,b\r\n1,2\r\n"));
        DocumentReadResult result = new CsvDocumentCodec().Read(stream);

        CsvReadResult csv = Assert.IsAssignableFrom<CsvReadResult>(result);
        Assert.Equal(',', csv.Delimiter);
        Assert.Equal(Encoding.UTF8, csv.Encoding);
    }

    [Fact]
    public void Read_Returns_CsvReadResult_With_Detected_Semicolon_Delimiter()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("name;age;city\r\nAlice;30;Berlin\r\nBob;25;Munich\r\n"));
        DocumentReadResult result = new CsvDocumentCodec().Read(stream);

        CsvReadResult csv = Assert.IsAssignableFrom<CsvReadResult>(result);
        Assert.Equal(';', csv.Delimiter);
        Assert.Equal([["name", "age", "city"], ["Alice", "30", "Berlin"], ["Bob", "25", "Munich"]], Grid(result.Document));
    }

    [Fact]
    public void Read_Returns_CsvReadResult_With_Detected_Tab_Delimiter()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a\tb\tc\r\n1\t2\t3\r\n"));
        DocumentReadResult result = new CsvDocumentCodec().Read(stream);

        CsvReadResult csv = Assert.IsAssignableFrom<CsvReadResult>(result);
        Assert.Equal('\t', csv.Delimiter);
    }

    [Fact]
    public void Explicit_Delimiter_In_Options_Is_Preserved_In_CsvReadResult()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a;b\r\n1;2\r\n"));
        var options = new CsvReadOptions(delimiter: ';');
        DocumentReadResult result = new CsvDocumentCodec().Read(stream, options);

        CsvReadResult csv = Assert.IsAssignableFrom<CsvReadResult>(result);
        Assert.Equal(';', csv.Delimiter);
    }

    [Fact]
    public void ReadCsv_Helper_Returns_Typed_CsvReadResult()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("x;y\r\n10;20\r\n"));
        CsvReadResult csv = new CsvDocumentCodec().ReadCsv(stream);

        Assert.Equal(';', csv.Delimiter);
        Assert.Equal(DocumentResultStatus.Success, csv.Status);
    }

    [Fact]
    public void Host_Saving_Back_Preserves_Carried_Delimiter()
    {
        // Read a file that used semicolons
        byte[] input = Encoding.UTF8.GetBytes("first;last;score\r\nJohn;Doe;95\r\n");
        using var readStream = new MemoryStream(input);
        DocumentReadResult readResult = new CsvDocumentCodec().Read(readStream);

        CsvReadResult csvRead = Assert.IsAssignableFrom<CsvReadResult>(readResult);
        Assert.Equal(';', csvRead.Delimiter);

        // Host saving back what it opened passes csvRead.Delimiter to write options
        using var writeStream = new MemoryStream();
        var writeOptions = new CsvWriteOptions(delimiter: csvRead.Delimiter);
        DocumentWriteResult writeResult = new CsvDocumentCodec().Write(readResult.Document, writeStream, writeOptions);

        Assert.Equal(DocumentResultStatus.Success, writeResult.Status);
        string written = Encoding.UTF8.GetString(writeStream.ToArray());
        Assert.Contains("first;last;score\r\nJohn;Doe;95\r\n", written, StringComparison.Ordinal);
    }
}
