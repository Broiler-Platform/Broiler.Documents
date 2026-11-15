using System.Text;
using Broiler.Documents.Resources;

namespace Broiler.Documents.Tests;

/// <summary>
/// Office conformance issue 249, ODT pictures: an SVG with no size and no view
/// box is as big as what it draws, strokes included, as LibreOffice takes the
/// range of such a picture from its content (fdo68839.odt's circle).
/// </summary>
public sealed class SvgRasterizerIssue249OdtImagesTests
{
    private static byte[] Svg(string inner, string attributes = "") =>
        Encoding.UTF8.GetBytes("<?xml version='1.0'?><svg xmlns='http://www.w3.org/2000/svg' version='1.1' " + attributes + ">" + inner + "</svg>");

    [Fact]
    public void A_Picture_With_No_Size_Or_View_Box_Is_Its_Contents_Bounds()
    {
        // fdo68839.odt: a circle at (50, 50), radius 49, stroke 2, so its
        // outer edge spans 0 to 100 in both directions.
        byte[] png = Assert.IsType<byte[]>(SvgRasterizer.RenderPng(
            Svg("<circle cx='50' cy='50' r='49' stroke='black' stroke-width='2' fill='red'/>")));
        var bitmap = new Issue242Png(png);
        Assert.Equal(1024, bitmap.Width);
        Assert.Equal(1024, bitmap.Height);

        var centre = bitmap.GetPixel(512, 512);
        Assert.Equal((255, 0, 0, 255), (centre.R, centre.G, centre.B, centre.A));
        // The stroke reaches each edge of the picture.
        Assert.Equal(255, bitmap.GetPixel(2, 512).A);
        Assert.Equal(255, bitmap.GetPixel(512, 1021).A);
        Assert.Equal(0, bitmap.GetPixel(20, 20).A);
    }

    [Fact]
    public void Content_Away_From_The_Origin_Is_Moved_To_The_Pictures_Corner()
    {
        // A 100 x 50 rectangle at (200, 100) with a 20-unit stroke: the picture
        // is the stroke's outer edge, 120 x 70, and the fill is inside it.
        byte[] png = Assert.IsType<byte[]>(SvgRasterizer.RenderPng(
            Svg("<rect x='200' y='100' width='100' height='50' fill='red' stroke='black' stroke-width='20'/>", "width='100%' height='100%'")));
        var bitmap = new Issue242Png(png);
        Assert.Equal(1024, bitmap.Width);
        Assert.Equal((int)Math.Ceiling(70 * 1024 / 120.0), bitmap.Height);

        var corner = bitmap.GetPixel(4, 4);
        Assert.Equal((0, 0, 0, 255), (corner.R, corner.G, corner.B, corner.A));
        var middle = bitmap.GetPixel(512, bitmap.Height / 2);
        Assert.Equal((255, 0, 0, 255), (middle.R, middle.G, middle.B, middle.A));
    }

    [Fact]
    public void A_Stated_Size_Or_View_Box_Still_Sizes_The_Picture()
    {
        byte[] png = Assert.IsType<byte[]>(SvgRasterizer.RenderPng(
            Svg("<rect x='200' y='100' width='100' height='50' fill='red'/>", "viewBox='0 0 400 200'")));
        var bitmap = new Issue242Png(png);
        Assert.Equal(1024, bitmap.Width);
        Assert.Equal(512, bitmap.Height);
        Assert.Equal(0, bitmap.GetPixel(100, 100).A);
    }

    [Fact]
    public void A_Picture_That_Draws_Nothing_Still_Draws_Nothing()
    {
        Assert.Null(SvgRasterizer.RenderPng(Svg("<g/>")));
        Assert.True(SvgRasterizer.IsBlank(Svg("<g/>")));
    }
}
