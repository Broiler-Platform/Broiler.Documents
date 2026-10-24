namespace Broiler.Documents.Odt.Tests;

public sealed class OdtImageAlternativeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_Unsupported_Preferred_Image_Does_Not_Hide_An_Embedded_Png(bool linked)
    {
        string images = "<draw:image xlink:href='Pictures/vector.svm'/><draw:image xlink:href='Pictures/fallback.png'/>";
        if (linked)
            images = "<draw:a xlink:href='https://example.test/'>" + images + "</draw:a>";
        var result = Read(images, new Dictionary<string, byte[]>
        {
            ["Pictures/vector.svm"] = "unsupported vector"u8.ToArray(),
            ["Pictures/fallback.png"] = OdtTestPackage.OnePixelPng,
        });
        InlineImage image = Assert.IsType<InlineImage>(Assert.Single(result.Document.Paragraphs).StyleAt(0).Image);
        Assert.Equal(OdtTestPackage.OnePixelPng, image.Data.ToArray());
        Assert.Equal(72, image.Width);
        Assert.Equal(36, image.Height);
        Assert.Equal("description", image.AltText);
        Assert.Equal(1, result.Document.PlainText.Length);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "odt.image.format");
    }

    [Fact]
    public void A_Readable_First_Image_Wins_Without_Inspecting_Later_Alternatives()
    {
        var result = Read("<draw:image xlink:href='Pictures/preferred.png'/><draw:image xlink:href='Pictures/missing.png'/>",
            new Dictionary<string, byte[]> { ["Pictures/preferred.png"] = OdtTestPackage.OnePixelPng });
        Assert.NotNull(result.Document.Paragraphs[0].StyleAt(0).Image);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "odt.image.missing");
    }

    [Fact]
    public void A_Flat_Document_Can_Use_Embedded_Data_After_An_External_Alternative()
    {
        string data = Convert.ToBase64String(OdtTestPackage.OnePixelPng);
        var result = OdtTestPackage.ReadBody(Frame("<draw:image xlink:href='https://example.test/not-fetched.png'/>" +
            "<draw:image><office:binary-data>" + data + "</office:binary-data></draw:image>"));
        Assert.NotNull(result.Document.Paragraphs[0].StyleAt(0).Image);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "odt.image.external");
    }

    [Fact]
    public void Resource_Denial_Does_Not_Trigger_Another_Alternative()
    {
        var result = OdtTestPackage.ReadWithPictures(
            Frame("<draw:image xlink:href='Pictures/preferred.png'/><draw:image xlink:href='Pictures/missing.png'/>"),
            new Dictionary<string, byte[]> { ["Pictures/preferred.png"] = OdtTestPackage.OnePixelPng },
            options: new DocumentReadOptions(resourcePolicy: DocumentResourcePolicy.DenyAll));
        Assert.Null(result.Document.Paragraphs[0].StyleAt(0).Image);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "odt.image.denied");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "odt.image.missing");
    }

    private static string Frame(string images) => "<text:p><draw:frame text:anchor-type='as-char' svg:width='1in' " +
        "svg:height='0.5in'>" + images + "<svg:title>description</svg:title></draw:frame></text:p>";

    private static DocumentReadResult Read(string images, IReadOnlyDictionary<string, byte[]> pictures) =>
        OdtTestPackage.ReadWithPictures(Frame(images), pictures);
}
