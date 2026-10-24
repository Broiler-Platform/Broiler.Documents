using Broiler.Graphics.Color;
using Broiler.Documents.Resources;

namespace Broiler.Documents.Odt.Tests;

public sealed class OdtAdvancedFillTests
{
    [Theory]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#00ccff", 0, 204, 255)]
    public void An_Empty_Legacy_Image_Does_Not_Hide_The_Page_Colour(string color, byte r, byte g, byte b)
    {
        var result = Read("fo:background-color=\"" + color + "\"", children: "<style:background-image/>");
        Assert.Equal(ShapeFill.Solid(new BColor(r, g, b)) with { ContentOnly = true }, result.Document.PageBackground);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code is "odt.fill" or "odt.image.shape");
    }

    private static DocumentReadResult Read(string properties, string definitions = "", string children = "")
    {
        string styles = "<office:styles>" + definitions + "</office:styles><office:automatic-styles>" +
            "<style:page-layout style:name=\"pm\"><style:page-layout-properties fo:page-width=\"200pt\" fo:page-height=\"200pt\" " + properties + ">" + children +
            "</style:page-layout-properties></style:page-layout></office:automatic-styles>" +
            "<office:master-styles><style:master-page style:name=\"Standard\" style:page-layout-name=\"pm\"/></office:master-styles>";
        return OdtTestPackage.ReadStyled(OdtTestPackage.Paragraph("body"), styles);
    }

    [Theory]
    [InlineData("single", 1)]
    [InlineData("double", 2)]
    [InlineData("triple", 3)]
    public void Hatches_Keep_Their_Strokes_Without_An_Unrequested_Solid_Background(string kind, int directions)
    {
        var fill = Assert.IsType<ShapeFill>(Read("draw:fill=\"hatch\" draw:fill-hatch-name=\"h\" draw:fill-color=\"#ff0000\" draw:fill-hatch-solid=\"false\"",
            "<draw:hatch draw:name=\"h\" draw:style=\"" + kind + "\" draw:color=\"#000000\" draw:distance=\"6pt\" draw:rotation=\"450\"/>").Document.PageBackground);
        Assert.Equal(0, fill.Start.A);
        Assert.Equal(new HatchFill(BColor.Black, 6, -45, directions), fill.Hatch);
        var restored = OdtWriterTests.RoundTrip(RichTextDocument.FromPlainText("x").WithPageBackground(fill)).PageBackground;
        Assert.Equal(fill, restored);
    }

    [Theory]
    [InlineData("radial", GradientGeometry.Radial)]
    [InlineData("ellipsoid", GradientGeometry.Ellipsoid)]
    [InlineData("square", GradientGeometry.Square)]
    [InlineData("rectangular", GradientGeometry.Rectangular)]
    public void Centred_Gradients_Keep_Their_Stops_Border_And_Origin(string kind, GradientGeometry geometry)
    {
        var fill = Assert.IsType<ShapeFill>(Read("draw:fill=\"gradient\" draw:fill-gradient-name=\"g\" draw:background-size=\"border\"",
            "<draw:gradient draw:name=\"g\" draw:style=\"" + kind + "\" draw:start-color=\"#ff0000\" draw:end-color=\"#0000ff\" draw:cx=\"25%\" draw:cy=\"75%\" draw:border=\"20%\"/>").Document.PageBackground);
        Assert.Equal(geometry, fill.Geometry);
        Assert.True(fill.ContentOnly);
        Assert.Equal(0.2, fill.Border);
        Assert.Equal(BColor.FromArgb(0, 0, 255), fill.ColorAt(25, 75, 100, 100));
        var restored = OdtWriterTests.RoundTrip(RichTextDocument.FromPlainText("x").WithPageBackground(fill)).PageBackground;
        Assert.Equal(fill, restored);
    }

    [Fact]
    public void Gradient_Intensity_And_Uniform_Opacity_Are_Applied_To_Both_Stops()
    {
        var fill = Assert.IsType<ShapeFill>(Read("draw:fill=\"gradient\" draw:fill-gradient-name=\"g\" draw:opacity=\"50%\"",
            "<draw:gradient draw:name=\"g\" draw:style=\"linear\" draw:start-color=\"#ff0000\" draw:end-color=\"#0000ff\" draw:start-intensity=\"50%\" draw:end-intensity=\"20%\" draw:border=\"25%\"/>").Document.PageBackground);
        Assert.Equal(new BColor(128, 0, 0, 128), fill.Start);
        Assert.Equal(new BColor(0, 0, 51, 128), fill.End);
        Assert.Equal(fill.Start, fill.ColorAt(0.2));
        Assert.Equal(fill.End, fill.ColorAt(1));
        Assert.Equal(fill, OdtWriterTests.RoundTrip(RichTextDocument.FromPlainText("x").WithPageBackground(fill)).PageBackground);
    }

    [Theory]
    [InlineData(ImageFillRepeat.Tile)]
    [InlineData(ImageFillRepeat.Stretch)]
    [InlineData(ImageFillRepeat.Single)]
    public void Embedded_Bitmap_Fills_Round_Trip_Through_The_Image_Resource_Gate(ImageFillRepeat repeat)
    {
        var resources = new DocumentConversionContextBuilder(DocumentResourcePolicy.AllowOwnDocuments);
        var picture = resources.AdmitImage(new InlineImage(OdtTestPackage.OnePixelPng, "image/png", 12, 15),
            DocumentResourceProvenance.CallerSupplied, DocumentResourceDisposition.Embedded);
        var fill = ShapeFill.Solid(BColor.Empty) with { ContentOnly = true, Image = new ImageFill(picture, repeat, 12, 15, 0, 1) };
        var document = OdtWriterTests.RoundTrip(RichTextDocument.FromPlainText("x").WithPageBackground(fill), new DocumentWriteOptions(resources: resources.Build()));
        var restored = Assert.IsType<ImageFill>(document.PageBackground!.Image);
        Assert.Equal(repeat, restored.Repeat);
        Assert.Equal(12, restored.Width);
        Assert.Equal(15, restored.Height);
        Assert.Equal(0, restored.AlignX);
        Assert.Equal(1, restored.AlignY);
        Assert.True(document.PageBackground.ContentOnly);
        Assert.Equal(OdtTestPackage.OnePixelPng, restored.Picture.Data.ToArray());
    }

    [Fact]
    public void A_Bitmap_Fill_Cannot_Bypass_The_Writers_Resource_Gate()
    {
        var fill = ShapeFill.Solid(BColor.Empty) with { Image = new ImageFill(new InlineImage(OdtTestPackage.OnePixelPng, "image/png", 12, 12)) };
        using var stream = new MemoryStream();
        var result = new OdtDocumentCodec().Write(RichTextDocument.FromPlainText("x").WithPageBackground(fill), stream);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "odt.image.omitted");
        stream.Position = 0;
        using var archive = new System.IO.Compression.ZipArchive(stream);
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("Pictures/", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_Gradient_Styles_Still_Report_Their_Approximation()
    {
        var result = Read("draw:fill=\"gradient\" draw:fill-gradient-name=\"g\"",
            "<draw:gradient draw:name=\"g\" draw:style=\"future-style\" draw:start-color=\"#ff0000\" draw:end-color=\"#0000ff\"/>");
        Assert.Equal(ShapeFill.Solid(BColor.FromArgb(255, 0, 0)), result.Document.PageBackground);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "odt.gradient");
    }

    [Fact]
    public void Legacy_Embedded_Backgrounds_Are_Read_Without_External_Fetching()
    {
        string encoded = Convert.ToBase64String(OdtTestPackage.OnePixelPng);
        var fill = Assert.IsType<ShapeFill>(Read("", children: "<style:background-image style:repeat=\"stretch\"><office:binary-data>" + encoded + "</office:binary-data></style:background-image>").Document.PageBackground);
        Assert.Equal(ImageFillRepeat.Stretch, fill.Image!.Repeat);
        Assert.Equal(OdtTestPackage.OnePixelPng, fill.Image.Picture.Data.ToArray());
        var external = Read("", children: "<style:background-image xlink:href=\"https://example.invalid/background.png\"/>");
        Assert.Null(external.Document.PageBackground);
        Assert.Contains(external.Diagnostics, diagnostic => diagnostic.Code.StartsWith("odt.image", StringComparison.Ordinal));
    }
}
