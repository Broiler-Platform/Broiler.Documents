using System.IO.Compression;
using System.Text;

namespace Broiler.Documents.Odt.Tests;

public sealed class OdtDocumentCodecProbeTests
{
    private readonly OdtDocumentCodec _codec = new();

    [Fact]
    public void The_Descriptor_Names_The_Format_Its_Extension_And_Its_Media_Type()
    {
        Assert.Equal("ODT", _codec.Descriptor.Name);
        Assert.True(_codec.Descriptor.MatchesExtension("odt"));
        Assert.True(_codec.Descriptor.MatchesExtension(".ODT"));
        Assert.True(_codec.Descriptor.MatchesMimeType(OdtTestPackage.TextMediaType));
        Assert.True(_codec.CanRead);
        Assert.True(_codec.CanWrite);
    }

    [Fact]
    public void A_Leading_Mimetype_Entry_Is_Certain_With_No_Hint_At_All()
    {
        byte[] package = OdtTestPackage.FromBody(OdtTestPackage.Paragraph("x"));

        DocumentProbeResult result = _codec.Probe(new DocumentProbeRequest(package));

        Assert.Equal(DocumentProbeConfidence.Certain, result.Confidence);
        Assert.Equal("ODT", result.FormatName);
        Assert.Equal(OdtTestPackage.TextMediaType, result.MimeType);
    }

    [Fact]
    public void A_Text_Template_Matches_Because_Its_Body_Is_The_Same()
    {
        byte[] package = OdtTestPackage.FromBody(
            OdtTestPackage.Paragraph("x"),
            mediaType: "application/vnd.oasis.opendocument.text-template");

        DocumentProbeResult result = _codec.Probe(new DocumentProbeRequest(package));

        Assert.Equal(DocumentProbeConfidence.High, result.Confidence);
        Assert.Equal("ODT", result.FormatName);
    }

    [Theory]
    [InlineData("application/vnd.oasis.opendocument.spreadsheet")]
    [InlineData("application/vnd.oasis.opendocument.presentation")]
    [InlineData("application/vnd.oasis.opendocument.graphics")]
    public void Another_Opendocument_Type_Is_Not_Claimed_Even_With_An_Odt_Hint(string mediaType)
    {
        byte[] package = OdtTestPackage.FromBody(OdtTestPackage.Paragraph("x"), mediaType: mediaType);

        DocumentProbeResult result = _codec.Probe(new DocumentProbeRequest(
            package,
            new DocumentSourceHints("book.odt")));

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void A_Package_Layout_With_No_Mimetype_Entry_Is_High_Confidence()
    {
        byte[] package = OdtTestPackage.FromBody(OdtTestPackage.Paragraph("x"), mediaType: null);

        DocumentProbeResult result = _codec.Probe(new DocumentProbeRequest(package));

        Assert.Equal(DocumentProbeConfidence.High, result.Confidence);
        Assert.Equal("ODT", result.FormatName);
    }

    [Fact]
    public void A_Generic_Zip_Is_Not_Claimed_Without_A_Hint()
    {
        DocumentProbeResult result = _codec.Probe(new DocumentProbeRequest(ZipOf("readme.txt", "hello")));

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void A_Generic_Zip_With_An_Odt_Filename_Is_High_Confidence()
    {
        DocumentProbeResult result = _codec.Probe(new DocumentProbeRequest(
            ZipOf("readme.txt", "hello"),
            new DocumentSourceHints("report.odt")));

        Assert.Equal(DocumentProbeConfidence.High, result.Confidence);
    }

    [Fact]
    public void A_Non_Zip_With_An_Odt_Filename_Is_Low_Confidence()
    {
        DocumentProbeResult result = _codec.Probe(new DocumentProbeRequest(
            "this is not a package"u8.ToArray(),
            new DocumentSourceHints("report.odt")));

        Assert.Equal(DocumentProbeConfidence.Low, result.Confidence);
    }

    [Fact]
    public void Unrelated_Bytes_Are_Not_Claimed()
    {
        DocumentProbeResult result = _codec.Probe(new DocumentProbeRequest("{rtf1}"u8.ToArray()));

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void An_Empty_Prefix_Is_Not_Claimed()
    {
        DocumentProbeResult result = _codec.Probe(new DocumentProbeRequest(ReadOnlyMemory<byte>.Empty));

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void A_Catalog_Selects_The_Odt_Codec_For_An_Odt_Package()
    {
        var catalog = new DocumentCodecCatalog([new OdtDocumentCodec()]);
        byte[] package = OdtTestPackage.FromBody(OdtTestPackage.Paragraph("x"));

        using var stream = new MemoryStream(package, writable: false);
        DocumentCodecMatch? match = catalog.Select(stream);

        Assert.NotNull(match);
        Assert.Equal("ODT", match.Codec.Descriptor.Name);
    }

    private static byte[] ZipOf(string entryName, string content)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
            using Stream stream = entry.Open();
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            stream.Write(bytes, 0, bytes.Length);
        }

        return buffer.ToArray();
    }
}
