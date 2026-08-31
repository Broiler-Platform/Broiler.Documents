using System.IO.Compression;
using System.Xml.Linq;

namespace Broiler.Documents.Odt.Tests;

public sealed class OdtImageTests
{
    [Fact]
    public void Reads_A_Frame_As_One_Placeholder_Character_Carrying_The_Picture()
    {
        DocumentReadResult result = OdtTestPackage.ReadWithPictures(
            "<text:p>before" + Frame("Pictures/logo.png", "1in", "0.5in", "the logo") + "after</text:p>",
            new Dictionary<string, byte[]> { ["Pictures/logo.png"] = OdtTestPackage.OnePixelPng });

        RichTextParagraph paragraph = result.Document.Paragraphs[0];
        Assert.Equal("before" + InlineImage.PlaceholderText + "after", paragraph.Text);

        InlineImage image = Assert.IsType<InlineImage>(paragraph.StyleAt(6).Image);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(72, image.Width);
        Assert.Equal(36, image.Height);
        Assert.Equal("the logo", image.AltText);
        Assert.Equal(OdtTestPackage.OnePixelPng, image.Data.ToArray());
    }

    [Fact]
    public void Reads_A_Picture_Held_Inline_As_Base64()
    {
        string base64 = Convert.ToBase64String(OdtTestPackage.OnePixelPng);
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p><draw:frame text:anchor-type=\"as-char\" svg:width=\"1in\" svg:height=\"1in\">" +
            "<draw:image><office:binary-data>" + base64 + "</office:binary-data></draw:image>" +
            "</draw:frame></text:p>");

        InlineImage image = Assert.IsType<InlineImage>(result.Document.Paragraphs[0].StyleAt(0).Image);
        Assert.Equal("image/png", image.ContentType);
    }

    [Fact]
    public void Reads_A_Picture_Through_The_Draw_A_A_Clickable_Image_Wraps_It_In()
    {
        DocumentReadResult result = OdtTestPackage.ReadWithPictures(
            "<text:p><draw:frame text:anchor-type=\"as-char\" svg:width=\"1in\" svg:height=\"1in\">" +
            "<draw:a xlink:href=\"https://example.com/\">" +
            "<draw:image xlink:href=\"Pictures/logo.png\"/>" +
            "</draw:a></draw:frame></text:p>",
            new Dictionary<string, byte[]> { ["Pictures/logo.png"] = OdtTestPackage.OnePixelPng });

        Assert.NotNull(result.Document.Paragraphs[0].StyleAt(0).Image);
    }

    [Fact]
    public void Takes_The_Media_Type_The_Manifest_Declares()
    {
        DocumentReadResult result = OdtTestPackage.ReadWithPictures(
            "<text:p>" + Frame("Pictures/logo.bin", "1in", "1in") + "</text:p>",
            new Dictionary<string, byte[]> { ["Pictures/logo.bin"] = OdtTestPackage.OnePixelPng },
            manifestInnerXml:
                "<manifest:file-entry manifest:full-path=\"/\" manifest:media-type=\"" +
                OdtTestPackage.TextMediaType + "\"/>" +
                "<manifest:file-entry manifest:full-path=\"Pictures/logo.bin\" " +
                "manifest:media-type=\"image/png\"/>");

        InlineImage image = Assert.IsType<InlineImage>(result.Document.Paragraphs[0].StyleAt(0).Image);
        Assert.Equal("image/png", image.ContentType);
    }

    [Fact]
    public void Reports_A_Picture_That_Links_Outside_The_Package()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p>" + Frame("https://example.com/logo.png", "1in", "1in") + "</text:p>");

        Assert.Null(result.Document.Paragraphs[0].StyleAt(0).Image);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.image.external");
    }

    [Fact]
    public void Reports_A_Picture_The_Package_Does_Not_Contain()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p>" + Frame("Pictures/missing.png", "1in", "1in") + "</text:p>");

        Assert.Contains(result.Diagnostics, d => d.Code == "odt.image.missing");
    }

    [Fact]
    public void Reports_A_Format_This_Codec_Does_Not_Carry()
    {
        DocumentReadResult result = OdtTestPackage.ReadWithPictures(
            "<text:p>" + Frame("Pictures/chart.svg", "1in", "1in") + "</text:p>",
            new Dictionary<string, byte[]> { ["Pictures/chart.svg"] = "<svg/>"u8.ToArray() });

        Assert.Contains(result.Diagnostics, d => d.Code == "odt.image.format");
    }

    [Fact]
    public void Reports_A_Picture_Over_The_Binary_Byte_Limit()
    {
        // Large enough to trip the limit, while the XML parts stay under it: the
        // signature is what types the picture, so the padding is harmless.
        byte[] padded = [.. OdtTestPackage.OnePixelPng, .. new byte[8192]];

        DocumentReadResult result = OdtTestPackage.ReadWithPictures(
            "<text:p>" + Frame("Pictures/logo.png", "1in", "1in") + "</text:p>",
            new Dictionary<string, byte[]> { ["Pictures/logo.png"] = padded },
            options: new DocumentReadOptions(new DocumentLimits(maxBinBytes: 4096)));

        Assert.Contains(result.Diagnostics, d => d.Code == "odt.image.limit");
    }

    [Fact]
    public void Loads_A_Floating_Picture_The_Same_Way_As_An_Inline_One()
    {
        DocumentReadResult result = OdtTestPackage.ReadWithPictures(
            "<text:p><draw:frame text:anchor-type=\"paragraph\" svg:width=\"1in\" svg:height=\"1in\">" +
            "<draw:image xlink:href=\"Pictures/logo.png\"/></draw:frame></text:p>",
            new Dictionary<string, byte[]> { ["Pictures/logo.png"] = OdtTestPackage.OnePixelPng });

        // Where it is placed belongs to OdtFloatingPictureTests; that its bytes
        // are loaded at all belongs here.
        InlineImage image = Assert.IsType<InlineImage>(Assert.Single(result.Document.Shapes).Image);
        Assert.Equal(OdtTestPackage.OnePixelPng, image.Data.ToArray());
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.image.anchored");
    }

    [Fact]
    public void An_Image_Round_Trips_Through_A_Written_Package()
    {
        var image = new InlineImage(OdtTestPackage.OnePixelPng, "image/png", 72, 36, "the logo", "logo");
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create(InlineImage.PlaceholderText, new InlineStyle { Image = image })]);

        RichTextDocument read = OdtWriterTests.RoundTrip(document);
        InlineImage actual = Assert.IsType<InlineImage>(read.Paragraphs[0].StyleAt(0).Image);

        Assert.Equal("image/png", actual.ContentType);
        Assert.Equal(72, actual.Width);
        Assert.Equal(36, actual.Height);
        Assert.Equal("the logo", actual.AltText);
        Assert.Equal(OdtTestPackage.OnePixelPng, actual.Data.ToArray());
    }

    [Fact]
    public void A_Written_Picture_Is_A_Package_Entry_The_Manifest_Declares()
    {
        var image = new InlineImage(OdtTestPackage.OnePixelPng, "image/png", 72, 72);
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create(InlineImage.PlaceholderText, new InlineStyle { Image = image })]);

        byte[] package = OdtDocumentCodec.WriteToArray(document);
        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry("Pictures/image1.png"));

        XNamespace ns = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
        XDocument manifest = XDocument.Parse(OdtWriterTests.ReadText(archive, "META-INF/manifest.xml"));
        Assert.Contains(
            manifest.Root!.Elements(ns + "file-entry"),
            entry => (string?)entry.Attribute(ns + "full-path") == "Pictures/image1.png" &&
                (string?)entry.Attribute(ns + "media-type") == "image/png");
    }

    [Fact]
    public void One_Image_Object_Shown_Twice_Stores_Its_Bytes_Once()
    {
        var image = new InlineImage(OdtTestPackage.OnePixelPng, "image/png", 72, 72);
        var style = new InlineStyle { Image = image };
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create(InlineImage.PlaceholderText, style),
            RichTextParagraph.Create(InlineImage.PlaceholderText, style),
        ]);

        byte[] package = OdtDocumentCodec.WriteToArray(document);
        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.Single(archive.Entries, entry => entry.FullName.StartsWith("Pictures/", StringComparison.Ordinal));
    }

    [Fact]
    public void A_Placeholder_With_No_Image_Is_Dropped_With_A_Diagnostic()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Plain("a" + InlineImage.PlaceholderText + "b")]);

        using var stream = new MemoryStream();
        DocumentWriteResult result = new OdtDocumentCodec().Write(document, stream);

        Assert.Contains(result.Diagnostics, d => d.Code == "odt.image.placeholder");
        Assert.Equal("ab", OdtWriterTests.RoundTrip(document).Paragraphs[0].Text);
    }

    [Fact]
    public void An_Image_With_No_Stated_Size_Is_Written_One_Inch_Square()
    {
        var image = new InlineImage(OdtTestPackage.OnePixelPng, "image/png", 0, 0);
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create(InlineImage.PlaceholderText, new InlineStyle { Image = image })]);

        using var stream = new MemoryStream();
        DocumentWriteResult result = new OdtDocumentCodec().Write(document, stream);

        Assert.Contains(result.Diagnostics, d => d.Code == "odt.image.size");
        InlineImage read = Assert.IsType<InlineImage>(
            OdtWriterTests.RoundTrip(document).Paragraphs[0].StyleAt(0).Image);
        Assert.Equal(72, read.Width);
        Assert.Equal(72, read.Height);
    }

    private static string Frame(string href, string width, string height, string? title = null) =>
        "<draw:frame draw:name=\"Image1\" text:anchor-type=\"as-char\" " +
        "svg:width=\"" + width + "\" svg:height=\"" + height + "\">" +
        "<draw:image xlink:href=\"" + href + "\" xlink:type=\"simple\" xlink:show=\"embed\"/>" +
        (title is null ? string.Empty : "<svg:title>" + OdtTestPackage.Escape(title) + "</svg:title>") +
        "</draw:frame>";
}
