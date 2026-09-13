namespace Broiler.Documents.Docx.Tests;

public sealed class DocxPackageValidationTests
{
    [Theory]
    [InlineData("<!DOCTYPE root [<!ENTITY text 'expanded'>]><root>&text;</root>")]
    [InlineData("<root>")]
    public void Invalid_Xml_And_Dtds_Reject_The_Main_Part(string xml)
    {
        byte[] bytes = DocxTestPackage.FromBody("", extraParts: new Dictionary<string, string>
        {
            ["word/document.xml"] = xml,
        });
        using var stream = new MemoryStream(bytes);
        DocumentReadResult result = new DocxDocumentCodec().Read(stream);
        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.document.xml");
        Assert.Equal(string.Empty, result.Document.PlainText);
    }

    [Fact]
    public void A_Compressed_Main_Part_Over_The_Byte_Budget_Is_Rejected()
    {
        string body = "<w:p><w:r><w:t>" + new string('x', 100_000) + "</w:t></w:r></w:p>";
        using var stream = new MemoryStream(DocxTestPackage.FromBody(body));
        DocumentReadResult result = new DocxDocumentCodec().Read(stream,
            new DocumentReadOptions(new DocumentLimits(maxBinBytes: 4096)));
        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.document.xml.limit");
    }
}
