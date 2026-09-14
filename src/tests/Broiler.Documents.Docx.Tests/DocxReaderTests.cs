using System.Text;

namespace Broiler.Documents.Docx.Tests;

public sealed class DocxReaderTests
{
    [Fact]
    public void Read_Invalid_Zip_Returns_Empty_Document_With_Error()
    {
        var codec = new DocxDocumentCodec();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a docx"));

        DocumentReadResult result = codec.Read(stream);

        Assert.True(result.HasErrors);
        Assert.Equal(string.Empty, result.Document.PlainText);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.package.zip");
    }

    [Fact]
    public void Read_Over_MaxDocumentBytes_Returns_Limit_Error()
    {
        byte[] bytes = DocxDocumentCodec.WriteToArray(RichTextDocument.FromPlainText("hello"));
        var codec = new DocxDocumentCodec();
        using var stream = new MemoryStream(bytes);
        var limits = new DocumentLimits(maxDocumentBytes: 16);

        DocumentReadResult result = codec.Read(stream, new DocumentReadOptions(limits));

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.limit.bytes");
    }
}
