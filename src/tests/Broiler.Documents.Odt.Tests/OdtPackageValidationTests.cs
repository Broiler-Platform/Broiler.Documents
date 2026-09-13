namespace Broiler.Documents.Odt.Tests;

public sealed class OdtPackageValidationTests
{
    [Theory]
    [InlineData("<!DOCTYPE root [<!ENTITY text 'expanded'>]><root>&text;</root>")]
    [InlineData("<root>")]
    public void Invalid_Xml_And_Dtds_Reject_The_Main_Part(string xml)
    {
        byte[] bytes = OdtTestPackage.FromBody("", extraParts: new Dictionary<string, string>
        {
            ["content.xml"] = xml,
        });
        using var stream = new MemoryStream(bytes);
        DocumentReadResult result = new OdtDocumentCodec().Read(stream);
        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "odt.content.xml");
        Assert.Equal(string.Empty, result.Document.PlainText);
    }

    [Fact]
    public void A_Compressed_Main_Part_Over_The_Byte_Budget_Is_Rejected()
    {
        string body = "<text:p>" + new string('x', 100_000) + "</text:p>";
        using var stream = new MemoryStream(OdtTestPackage.FromBody(body));
        DocumentReadResult result = new OdtDocumentCodec().Read(stream,
            new DocumentReadOptions(new DocumentLimits(maxBinBytes: 4096)));
        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "odt.content.xml.limit");
    }
}
