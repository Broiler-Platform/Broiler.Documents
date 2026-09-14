using System.Text;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfLimitTests
{
    private static byte[] Bytes(string s) => Encoding.Latin1.GetBytes(s);

    private static DocumentReadResult Read(string rtf, DocumentLimits limits) =>
        RtfReader.Read(Bytes(rtf), new DocumentReadOptions(limits));

    [Fact]
    public void Group_Depth_Limit_Is_Enforced_Without_Overflow()
    {
        DocumentReadResult result = Read(
            "{\\rtf1" + new string('{', 10_000) + "x",
            new DocumentLimits(maxGroupDepth: 8));

        Assert.NotNull(result.Document);
        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.depth");
    }

    [Fact]
    public void Document_Size_Limit_Truncates_And_Reports()
    {
        DocumentReadResult result = Read(
            "{\\rtf1 " + new string('a', 1000) + "}",
            new DocumentLimits(maxDocumentBytes: 50));

        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.size");
    }

    [Fact]
    public void Paragraph_Count_Limit_Caps_Paragraphs_And_Reports()
    {
        DocumentReadResult result = Read(
            "{\\rtf1 A\\par B\\par C\\par D\\par E}",
            new DocumentLimits(maxParagraphCount: 3));

        Assert.Equal(3, result.Document.ParagraphCount);
        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.paragraphs");
    }

    [Fact]
    public void Paragraph_Limit_Bounds_Work_After_The_Cap()
    {
        string body = string.Concat(Enumerable.Range(0, 500).Select(_ => "X\\par "));
        DocumentReadResult result = Read("{\\rtf1 " + body + "}", new DocumentLimits(maxParagraphCount: 2));

        Assert.Equal(2, result.Document.ParagraphCount);
    }

    [Fact]
    public void Run_Length_Limit_Still_Preserves_The_Full_Text()
    {
        DocumentReadResult result = Read(
            "{\\rtf1 " + new string('a', 20) + "}",
            new DocumentLimits(maxRunLength: 4));

        Assert.Equal(new string('a', 20), result.Document.PlainText);
    }

    [Fact]
    public void Bin_Binary_Is_Skipped_So_It_Cannot_Corrupt_Structure()
    {
        // Five raw bytes "}}}}X" would prematurely close groups if not skipped.
        DocumentReadResult result = Read(
            "{\\rtf1{\\pict\\bin5}}}}X}After}",
            new DocumentLimits(maxBinBytes: 2));

        Assert.Equal("After", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.bin");
    }

    [Fact]
    public void Codec_Read_Honours_Options_Limits()
    {
        var codec = new RtfDocumentCodec();
        using var stream = new MemoryStream(Bytes("{\\rtf1 " + new string('a', 1000) + "}"));

        DocumentReadResult result = codec.Read(stream, new DocumentReadOptions(new DocumentLimits(maxDocumentBytes: 50)));

        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.size");
    }
}
