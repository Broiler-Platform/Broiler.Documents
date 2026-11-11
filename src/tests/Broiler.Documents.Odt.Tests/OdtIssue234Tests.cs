using Broiler.Documents.Resources;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Office conformance issue 234: hyperlink-field.odt, whose last page holds a
/// paragraph-anchored frame linking a picture outside the package.
/// </summary>
public sealed class OdtIssue234Tests
{
    private static readonly DocumentReadOptions Options = new(resourcePolicy: DocumentResourcePolicy.AllowOwnDocuments);

    private static string Frame(string href) =>
        "<text:p><draw:frame draw:name=\"Graphic5\" text:anchor-type=\"paragraph\" " +
        "svg:y=\"0.5cm\" svg:width=\"3cm\" svg:height=\"0.8cm\">" +
        "<draw:image xlink:href=\"" + href + "\" xlink:type=\"simple\" xlink:show=\"embed\"/>" +
        "</draw:frame>body</text:p>";

    [Theory]
    [InlineData("../../../backup/horde/sxw/lozenge.wmf")]
    [InlineData("Pictures/absent.png")]
    public void A_Picture_The_Package_Does_Not_Hold_Keeps_Its_Box_Without_A_Mark(string href)
    {
        // LibreOffice prints the frame of a picture it cannot find as an empty
        // box: no replacement graphic reaches the printed or exported page.
        DocumentReadResult result = OdtTestPackage.ReadBody(Frame(href), options: Options);

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        InlineImage image = Assert.IsType<InlineImage>(shape.Image);
        Assert.True(image.IsMissingPayload);
        Assert.False(image.MarksMissingPayload);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.image.missing");
    }

    [Fact]
    public void A_Picture_At_A_Url_Keeps_Its_Missing_Mark()
    {
        // LibreOffice may fetch and draw it; this codec does not, so the page
        // still shows where the picture is.
        DocumentReadResult result = OdtTestPackage.ReadBody(Frame("https://example.invalid/a.png"), options: Options);

        InlineImage image = Assert.IsType<InlineImage>(Assert.Single(result.Document.Shapes).Image);
        Assert.True(image.IsMissingPayload);
        Assert.True(image.MarksMissingPayload);
    }

    [Fact]
    public void A_Picture_Frame_Takes_No_Outline_From_The_Default_Graphic_Stroke()
    {
        // Most files' default graphic style states svg:stroke-color="#000000",
        // which is for drawing shapes; a Writer picture frame's edge is fo:border.
        string styles = "<office:styles><style:default-style style:family=\"graphic\">" +
            "<style:graphic-properties svg:stroke-color=\"#000000\" draw:fill-color=\"#99ccff\"/>" +
            "</style:default-style></office:styles>";
        using var stream = new MemoryStream(OdtTestPackage.FromBody(
            Frame("Pictures/logo.png"), stylesPartInnerXml: styles,
            binaryParts: new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["Pictures/logo.png"] = OdtTestPackage.OnePixelPng }));

        DocumentShape shape = Assert.Single(new OdtDocumentCodec().Read(stream, Options).Document.Shapes);
        Assert.True(shape.HasImage);
        Assert.True(shape.Outline.IsEmpty);
    }

    [Fact]
    public void A_Picture_Frame_Border_Still_Draws_Its_Outline()
    {
        string styles = OdtTestPackage.Style("fr1",
            "<style:graphic-properties fo:border=\"1pt solid #ff0000\"/>", family: "graphic");
        string frame = Frame("Pictures/logo.png").Replace("<draw:frame ", "<draw:frame draw:style-name=\"fr1\" ", StringComparison.Ordinal);
        DocumentReadResult result = OdtTestPackage.ReadWithPictures(frame,
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["Pictures/logo.png"] = OdtTestPackage.OnePixelPng },
            options: Options, automaticStylesXml: styles);

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Equal(255, shape.Outline.R);
        Assert.Equal(0, shape.Outline.G);
        Assert.Equal(1, shape.OutlineWidth, 2);
    }
}
