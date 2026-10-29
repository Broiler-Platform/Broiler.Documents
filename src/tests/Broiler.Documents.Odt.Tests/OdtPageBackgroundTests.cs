using Broiler.Graphics.Color;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// The page background an ODT states on its master page, which the reader did
/// not look at and the writer did not write.
/// </summary>
public sealed class OdtPageBackgroundTests
{
    private const string Page = "fo:page-width=\"21cm\" fo:page-height=\"29.7cm\"";

    private static DocumentReadResult Read(
        string layoutProperties,
        string masterAttributes = "",
        string namedStyles = "",
        string automaticStyles = "")
    {
        string styles =
            "<office:styles>" + namedStyles + "</office:styles>" +
            "<office:automatic-styles>" +
            "<style:page-layout style:name=\"pm1\"><style:page-layout-properties " + Page + " " +
            layoutProperties + "/></style:page-layout>" + automaticStyles +
            "</office:automatic-styles>" +
            "<office:master-styles><style:master-page style:name=\"Standard\" style:page-layout-name=\"pm1\" " +
            masterAttributes + "/></office:master-styles>";
        return OdtTestPackage.ReadStyled(OdtTestPackage.Paragraph("body"), styles);
    }

    private static RichTextDocument RoundTrip(RichTextDocument document)
    {
        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(document), writable: false);
        return new OdtDocumentCodec().Read(stream).Document;
    }

    [Fact]
    public void A_Page_Layouts_Background_Colour_Is_Read()
    {
        Assert.Equal(
            ShapeFill.Solid(BColor.FromArgb(0xFF, 0, 0)),
            Read("fo:background-color=\"#ff0000\"").Document.PageBackground);
    }

    [Fact]
    public void A_Transparent_Page_Has_No_Background()
    {
        Assert.Null(Read("fo:background-color=\"transparent\"").Document.PageBackground);
    }

    [Fact]
    public void A_Drawing_Page_Fill_Wins_Over_The_Flattened_Colour_Beside_It()
    {
        // LibreOffice states a gradient page three times, with the background
        // colour flattened to the gradient's average. The drawing-page style
        // is the page as it is drawn.
        DocumentReadResult result = Read(
            "fo:background-color=\"#808080\" draw:fill=\"gradient\" draw:fill-gradient-name=\"g1\"",
            "draw:style-name=\"dp1\"",
            "<draw:gradient draw:name=\"g1\" draw:style=\"axial\" draw:start-color=\"#00ff00\" " +
            "draw:end-color=\"#ff00ff\" draw:angle=\"225deg\"/>",
            "<style:style style:name=\"dp1\" style:family=\"drawing-page\">" +
            "<style:drawing-page-properties draw:fill=\"gradient\" draw:fill-gradient-name=\"g1\"/></style:style>");

        // Lime along the middle and fuchsia at the edges, as LibreOffice draws
        // an axial gradient whose start is lime.
        Assert.Equal(
            new ShapeFill(BColor.FromArgb(0xFF, 0, 0xFF), BColor.FromArgb(0, 0xFF, 0), 225, Axial: true),
            result.Document.PageBackground);
    }

    [Fact]
    public void A_Fill_Of_None_Is_No_Background_Whatever_The_Colour_Says()
    {
        Assert.Null(Read("fo:background-color=\"#ff0000\" draw:fill=\"none\"").Document.PageBackground);
    }

    [Fact]
    public void A_Rectangular_Gradient_Retains_Its_Geometry_And_Centre()
    {
        DocumentReadResult result = Read(
            "draw:fill=\"gradient\" draw:fill-gradient-name=\"g1\"",
            namedStyles:
                "<draw:gradient draw:name=\"g1\" draw:style=\"rectangular\" draw:cx=\"0%\" draw:cy=\"0%\" " +
                "draw:start-color=\"#ff8200\" draw:end-color=\"#000082\" draw:angle=\"90deg\"/>");

        var fill = Assert.IsType<ShapeFill>(result.Document.PageBackground);
        Assert.Equal(GradientGeometry.Rectangular, fill.Geometry);
        Assert.Equal(0, fill.CenterX);
        Assert.Equal(0, fill.CenterY);
        Assert.Equal(BColor.FromArgb(0, 0, 0x82), fill.ColorAt(0, 0, 100, 100));
    }

    [Fact]
    public void A_Bitmap_Page_Fill_Without_A_Bitmap_Is_Reported_And_Drawn_In_Its_Fill_Colour()
    {
        DocumentReadResult result = Read("fo:background-color=\"transparent\" draw:fill=\"bitmap\"");

        Assert.Equal(ShapeFill.Solid(BColor.FromArgb(0x72, 0x9F, 0xCF)), result.Document.PageBackground);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "odt.fill");
    }

    [Theory]
    [MemberData(nameof(Backgrounds))]
    public void A_Background_Survives_A_Round_Trip(ShapeFill background)
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body").WithPageBackground(background);

        ShapeFill read = Assert.IsType<ShapeFill>(RoundTrip(source).PageBackground);

        Assert.Equal(background.Start, read.Start);
        Assert.Equal(background.End, read.End);
        Assert.Equal(background.Axial, read.Axial);
        Assert.Equal(background.AngleDegrees, read.AngleDegrees, 3);
    }

    public static TheoryData<ShapeFill> Backgrounds() => new()
    {
        ShapeFill.Solid(BColor.FromArgb(0x12, 0x34, 0x56)),
        new ShapeFill(BColor.FromArgb(255, 0, 0), BColor.FromArgb(0, 0, 255), 30),
        new ShapeFill(BColor.FromArgb(255, 0, 255), BColor.FromArgb(0, 255, 0), 225, Axial: true),
    };

    [Fact]
    public void An_Axial_Shape_Fill_Survives_A_Round_Trip()
    {
        var fill = new ShapeFill(BColor.FromArgb(255, 0, 0), BColor.FromArgb(0, 0, 255), 90, Axial: true);
        RichTextDocument source = RichTextDocument.FromPlainText("body")
            .WithShapes([new DocumentShape(0, 0, 0, 100, 50, fill)]);

        ShapeFill read = Assert.IsType<ShapeFill>(Assert.Single(RoundTrip(source).Shapes).Fill);

        Assert.Equal(fill, read with { AngleDegrees = Math.Round(read.AngleDegrees, 3) });
    }
}
