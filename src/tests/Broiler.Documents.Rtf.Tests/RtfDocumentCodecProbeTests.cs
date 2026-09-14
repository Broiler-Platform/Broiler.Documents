using System.Text;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfDocumentCodecProbeTests
{
    private static byte[] Bytes(string s) => Encoding.Latin1.GetBytes(s);

    private readonly RtfDocumentCodec _codec = new();

    private DocumentProbeResult Probe(byte[] prefix, DocumentSourceHints? hints = null) =>
        _codec.Probe(new DocumentProbeRequest(prefix, hints));

    private DocumentProbeResult Probe(string prefix, DocumentSourceHints? hints = null) =>
        Probe(Bytes(prefix), hints);

    [Fact]
    public void Rtf1_Signature_Is_Certain()
    {
        Assert.Equal(DocumentProbeConfidence.Certain, Probe("{\\rtf1\\ansi\\deff0}").Confidence);
    }

    [Fact]
    public void Rtf_Without_Version_Digit_Is_High()
    {
        Assert.Equal(DocumentProbeConfidence.High, Probe("{\\rtf\\ansi}").Confidence);
    }

    [Fact]
    public void Leading_Whitespace_Before_The_Signature_Still_Matches()
    {
        Assert.Equal(DocumentProbeConfidence.Certain, Probe("   \r\n{\\rtf1}").Confidence);
    }

    [Fact]
    public void Utf8_Bom_Before_The_Signature_Still_Matches()
    {
        byte[] prefix = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Bytes("{\\rtf1}")).ToArray();

        Assert.Equal(DocumentProbeConfidence.Certain, Probe(prefix).Confidence);
    }

    [Fact]
    public void Filename_Hint_Without_A_Signature_Is_Low_Confidence()
    {
        DocumentProbeResult result = Probe("<html></html>", new DocumentSourceHints(fileName: "notes.rtf"));

        Assert.True(result.IsMatch);
        Assert.Equal(DocumentProbeConfidence.Low, result.Confidence);
    }

    [Fact]
    public void MimeType_Hint_Without_A_Signature_Is_Low_Confidence()
    {
        DocumentProbeResult result = Probe("plain text", new DocumentSourceHints(mimeType: "application/rtf"));

        Assert.Equal(DocumentProbeConfidence.Low, result.Confidence);
    }

    [Fact]
    public void No_Signature_And_No_Hint_Does_Not_Match()
    {
        Assert.False(Probe("<html></html>").IsMatch);
    }

    [Fact]
    public void Codec_Can_Read_And_Write()
    {
        Assert.True(_codec.CanRead);
        Assert.True(_codec.CanWrite);
    }

    [Fact]
    public void Read_And_Write_Both_Work()
    {
        using var input = new MemoryStream(Bytes("{\\rtf1 hi}"));
        using var output = new MemoryStream();

        DocumentReadResult read = _codec.Read(input);
        Assert.Equal("hi", read.Document.PlainText);

        DocumentWriteResult written = _codec.Write(read.Document, output);
        Assert.True(written.BytesWritten > 0);
        Assert.Equal(written.BytesWritten, output.Length);
    }
}
