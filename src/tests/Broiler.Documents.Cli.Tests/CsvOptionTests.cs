using System.Text;
using System.IO.Compression;
using System.Xml.Linq;
using Broiler.Documents.Cli.Composition;
using Broiler.Documents.Csv;

namespace Broiler.Documents.Cli.Tests;

public sealed class CsvOptionTests : IDisposable
{
    private readonly CliHarness _cli = new();
    public void Dispose() => _cli.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Conversion_Separates_Input_And_Output_Dialects(bool explicitFormat)
    {
        string source = _cli.Path(explicitFormat ? "source.txt" : "source.csv");
        File.WriteAllText(source, "Name|Value\r\nAlice|=1+1\r\n");
        string destination = _cli.Path("out.csv");
        var args = new List<string> { "convert", source, "--out", destination,
            "--csv-read-delimiter", "|", "--csv-write-delimiter", "semicolon",
            "--csv-bom", "--csv-escape-formulas" };
        if (explicitFormat)
            args.AddRange(["--from", "csv"]);
        CliHarness.RunExpecting(ExitCode.Ok, [.. args]);

        byte[] bytes = File.ReadAllBytes(destination);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("Name;Value\r\nAlice;'=1+1\r\n", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public void Defaults_Still_Detect_Input_And_Write_Commas_Without_Formula_Escaping_Or_Bom()
    {
        string source = _cli.Path("source.csv");
        File.WriteAllText(source, "Name;Value\r\nAlice;=1+1\r\n");
        string destination = _cli.Path("out.csv");
        CliHarness.RunExpecting(ExitCode.Ok, "convert", source, "--out", destination);
        Assert.Equal(Encoding.UTF8.GetBytes("Name,Value\r\nAlice,=1+1\r\n"), File.ReadAllBytes(destination));
    }

    [Fact]
    public void Csv_Header_Is_Preserved_When_Converting_To_Docx()
    {
        string source = _cli.Path("source.csv");
        File.WriteAllText(source, "Name,Value\r\nAlice,42\r\n");
        string destination = _cli.Path("out.docx");
        CliHarness.RunExpecting(ExitCode.Ok, "convert", source, "--out", destination, "--csv-header");
        using ZipArchive archive = ZipFile.OpenRead(destination);
        using Stream stream = archive.GetEntry("word/document.xml")!.Open();
        XDocument xml = XDocument.Load(stream);
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XElement[] rows = xml.Descendants(word + "tr").ToArray();
        Assert.Single(rows[0].Descendants(word + "tblHeader"));
        Assert.Empty(rows[1].Descendants(word + "tblHeader"));
    }

    [Fact]
    public void Roundtrip_Reads_The_Output_Delimiter_And_Can_Mix_Formats()
    {
        string source = _cli.Path("source.csv");
        File.WriteAllText(source, "Name|Value\r\nAlice|42\r\n");
        CliHarness.RunExpecting(ExitCode.Ok, "roundtrip", source,
            "--csv-read-delimiter", "|", "--csv-write-delimiter", "tab", "--csv-header",
            "--via", "csv", "--via", "docx");
    }

    [Fact]
    public void Csv_Options_Do_Not_Reach_Other_Input_Codecs()
    {
        string source = _cli.MakeDocument("source.docx", "hello");
        string destination = _cli.Path("out.csv");
        CliHarness.RunExpecting(ExitCode.Ok, "convert", source, "--out", destination,
            "--csv-read-delimiter", "|", "--csv-header", "--csv-write-delimiter", "tab");
        Assert.Equal("hello\r\n", File.ReadAllText(destination));
    }

    [Fact]
    public void Configured_Csv_Preserves_Request_Limits_And_Cancellation()
    {
        DocumentCodec codec = CodecComposition.CreateCatalog(csvReadOptions: new CsvReadOptions(delimiter: '|'))
            .FindByName("CSV")!;
        using DocumentInput input = DocumentInput.FromBytes(Encoding.UTF8.GetBytes("a|b\r\nc|d\r\n"));
        var options = new DocumentReadOptions(new DocumentLimits(maxParagraphCount: 2));
        DocumentReadResult limited = codec.Read(new DocumentReadRequest(input, options));
        Assert.Single(limited.Document.Tables[0].Rows);
        Assert.Equal(DocumentResultStatus.Partial, limited.Status);
        DocumentReadResult cancelled = codec.Read(new DocumentReadRequest(input, options, new CancellationToken(true)));
        Assert.Equal(DocumentResultStatus.Rejected, cancelled.Status);
        using var destination = new MemoryStream();
        DocumentWriteResult write = codec.Write(new DocumentWriteRequest(limited.Document, destination,
            DocumentWriteOptions.Default, new CancellationToken(true)));
        Assert.Equal(DocumentResultStatus.Rejected, write.Status);
        Assert.Equal(0, destination.Length);
    }

    [Theory]
    [InlineData("csv-read-delimiter", "")]
    [InlineData("csv-read-delimiter", "||")]
    [InlineData("csv-read-delimiter", "\"")]
    [InlineData("csv-write-delimiter", "\n")]
    [InlineData("csv-write-delimiter", "\r")]
    public void Invalid_Delimiters_Fail_Before_Replacing_Output(string option, string value)
    {
        string source = _cli.Path("source.csv");
        File.WriteAllText(source, "a,b\r\n");
        string destination = _cli.Path("out.csv");
        File.WriteAllText(destination, "keep");
        CliRun run = CliHarness.RunExpecting(ExitCode.Usage, "convert", source, "--out", destination,
            "--" + option + "=" + value);
        Assert.Contains(option, run.Error, StringComparison.Ordinal);
        Assert.Equal("keep", File.ReadAllText(destination));
    }
}
