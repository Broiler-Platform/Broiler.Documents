using System.IO.Compression;
using System.Text;

namespace Broiler.Documents.Docx.Tests;

public sealed class DocxDocumentCodecProbeTests
{
    private readonly DocxDocumentCodec _codec = new();

    [Fact]
    public void Probe_Matches_Docx_Zip_With_File_Hint()
    {
        byte[] bytes = DocxDocumentCodec.WriteToArray(RichTextDocument.FromPlainText("hello"));
        var hints = new DocumentSourceHints(fileName: "hello.docx");

        DocumentProbeResult result = _codec.Probe(new DocumentProbeRequest(bytes, hints));

        Assert.True(result.IsMatch);
        Assert.Equal(DocumentProbeConfidence.High, result.Confidence);
        Assert.Equal("DOCX", result.FormatName);
    }

    [Fact]
    public void Probe_Matches_WordprocessingML_Part_Without_File_Hint()
    {
        byte[] bytes = DocxDocumentCodec.WriteToArray(RichTextDocument.FromPlainText("hello"));

        DocumentProbeResult result = _codec.Probe(new DocumentProbeRequest(bytes));

        Assert.True(result.IsMatch);
        Assert.Equal(DocumentProbeConfidence.High, result.Confidence);
    }

    [Fact]
    public void Probe_Does_Not_Claim_Unhinted_Generic_Zip()
    {
        byte[] zip = GenericZip();

        DocumentProbeResult result = _codec.Probe(new DocumentProbeRequest(zip));

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Probe_Does_Not_Claim_Unhinted_Opc_Package_Without_Word_Part()
    {
        byte[] zip = GenericZip("[Content_Types].xml", "<Types />");

        DocumentProbeResult result = _codec.Probe(new DocumentProbeRequest(zip));

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Filename_Hint_Without_Zip_Signature_Is_Low_Confidence()
    {
        DocumentProbeResult result = _codec.Probe(new DocumentProbeRequest(
            Encoding.UTF8.GetBytes("plain text"),
            new DocumentSourceHints(fileName: "notes.docx")));

        Assert.True(result.IsMatch);
        Assert.Equal(DocumentProbeConfidence.Low, result.Confidence);
    }

    [Fact]
    public void Codec_Can_Read_And_Write()
    {
        Assert.True(_codec.CanRead);
        Assert.True(_codec.CanWrite);
    }

    private static byte[] GenericZip(string entryName = "plain.txt", string content = "hello")
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName);
            using Stream writer = entry.Open();
            writer.Write(Encoding.UTF8.GetBytes(content));
        }

        return stream.ToArray();
    }
}
