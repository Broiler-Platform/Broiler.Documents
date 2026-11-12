using System.Text;
using Broiler.Documents.Resources;

namespace Broiler.Documents.Tests;

/// <summary>
/// Office conformance issue 242, ODT pictures: the plain-geometry SVG subset
/// <see cref="SvgRasterizer"/> draws (image_anchored_to_paragraph_no_wrap.fodt's
/// diagonal line), the empty SVG that draws nothing (tdf128820.fodt), and the
/// pictures it declines so a raster fallback is drawn instead.
/// </summary>
public sealed class SvgRasterizerIssue242OdtImagesTests
{
    private static byte[] Svg(string inner, string attributes = "viewBox='0 0 20 20'") =>
        Encoding.UTF8.GetBytes("<?xml version='1.0'?><svg xmlns='http://www.w3.org/2000/svg' " + attributes + ">" + inner + "</svg>");

    private static (byte R, byte G, byte B, byte A) Pixel(byte[] png, int x, int y) => new Issue242Png(png).GetPixel(x, y);

    [Fact]
    public void A_Stroked_Path_Draws_At_The_View_Box_Size_With_Butt_Ends()
    {
        // The SVG of image_anchored_to_paragraph_no_wrap.fodt: one stroke from
        // (1, 1) to (19, 19) in a 20-unit view box, stroke width 1 by default.
        byte[] png = Assert.IsType<byte[]>(SvgRasterizer.RenderPng(Svg("<path d='M1,1L19,19'/>", "viewBox='0 0 20 20' stroke='black'")));
        var bitmap = new Issue242Png(png);
        Assert.Equal(1024, bitmap.Width);
        Assert.Equal(1024, bitmap.Height);

        var middle = bitmap.GetPixel(512, 512);
        Assert.Equal((0, 0, 0, 255), (middle.R, middle.G, middle.B, middle.A));
        // Off the line, and before its butt start, nothing is drawn.
        Assert.Equal(0, bitmap.GetPixel(800, 200).A);
        Assert.Equal(0, bitmap.GetPixel(40, 40).A);
        // The fill under the open path is the line itself: no area to fill.
        Assert.Equal(0, bitmap.GetPixel(700, 300).A);
    }

    [Fact]
    public void Shapes_Take_Their_Fill_Transform_And_Style_Declarations()
    {
        byte[] svg = Svg(
            "<g transform='translate(10 0)'><rect x='0' y='0' width='10' height='10' style='fill:#ff0000;stroke:none'/></g>" +
            "<circle cx='5' cy='15' r='4' fill='rgb(0,0,255)'/>" +
            "<path d='M 2 2 A 1 1 0 0 1 4 2 Z' fill='none' stroke='lime'/>");
        byte[] png = Assert.IsType<byte[]>(SvgRasterizer.RenderPng(svg));

        var red = Pixel(png, 768, 256);
        Assert.Equal((255, 0, 0), (red.R, red.G, red.B));
        Assert.Equal(0, Pixel(png, 256, 256).A);
        var blue = Pixel(png, 256, 768);
        Assert.Equal((0, 0, 255), (blue.R, blue.G, blue.B));
        Assert.Equal(0, Pixel(png, 768, 768).A);
    }

    [Fact]
    public void Width_And_Height_Size_The_Picture_And_The_View_Box_Is_Fitted_Into_It()
    {
        // A 40 x 20 picture whose square view box is centred in it (xMidYMid meet).
        byte[] png = Assert.IsType<byte[]>(SvgRasterizer.RenderPng(
            Svg("<rect width='10' height='10' fill='black'/>", "width='40px' height='20px' viewBox='0 0 10 10'")));
        var bitmap = new Issue242Png(png);
        Assert.Equal(1024, bitmap.Width);
        Assert.Equal(512, bitmap.Height);
        Assert.Equal(0, bitmap.GetPixel(100, 256).A);
        Assert.Equal(255, bitmap.GetPixel(512, 256).A);
        Assert.Equal(0, bitmap.GetPixel(900, 256).A);
    }

    [Fact]
    public void A_Group_Opacity_Fades_Shapes_That_State_Their_Own_Fill_Opacity()
    {
        // tdf104254_noHeaderWrapping.odt's Inkscape layers: style="opacity:0.25"
        // on the layer, fill-opacity:1 on its path.
        byte[] png = Assert.IsType<byte[]>(SvgRasterizer.RenderPng(Svg(
            "<g style='display:none'><rect width='20' height='20' fill='red'/></g>" +
            "<g style='display:inline;opacity:0.25'><rect width='10' height='10' style='fill:#000000;fill-opacity:1'/></g>")));
        Assert.InRange(Pixel(png, 256, 256).A, 60, 68);
        Assert.Equal(0, Pixel(png, 768, 768).A);
    }

    [Fact]
    public void An_Svg_With_Nothing_In_It_Is_Blank()
    {
        // tdf128820.fodt's picture.
        byte[] svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        Assert.True(SvgRasterizer.IsSvg(svg));
        Assert.True(SvgRasterizer.IsBlank(svg));
        Assert.Null(SvgRasterizer.RenderPng(svg));
    }

    [Theory]
    [InlineData("<text x='1' y='10'>words</text>")]
    [InlineData("<defs><linearGradient id='g'/></defs><rect width='5' height='5' fill='url(#g)'/>")]
    [InlineData("<image width='5' height='5' href='a.png'/>")]
    [InlineData("<rect width='5' height='5' filter='url(#f)'/>")]
    [InlineData("<path d='M0,0 L10,10' stroke='black' stroke-dasharray='2 2'/>")]
    [InlineData("<path d='M0,0 L10,0 L10,10 Z M2,2 L8,2 L8,8 Z' fill-rule='evenodd'/>")]
    [InlineData("<style>rect { fill: red }</style><rect width='5' height='5'/>")]
    public void A_Picture_With_Parts_This_Subset_Cannot_Draw_Is_Declined(string inner)
    {
        byte[] svg = Svg(inner);
        Assert.Null(SvgRasterizer.RenderPng(svg));
        Assert.False(SvgRasterizer.IsBlank(svg));
    }

    [Fact]
    public void Metadata_And_Flowed_Text_Are_Skipped()
    {
        byte[] svg = Svg(
            "<metadata><x xmlns='urn:x'/></metadata><title>t</title>" +
            "<flowRoot><flowRegion><rect width='5' height='5'/></flowRegion><flowPara>text</flowPara></flowRoot>" +
            "<rect x='10' y='10' width='10' height='10' fill='#00ff00'/>");
        byte[] png = Assert.IsType<byte[]>(SvgRasterizer.RenderPng(svg));
        Assert.Equal(0, Pixel(png, 100, 100).A);
        Assert.Equal(255, Pixel(png, 800, 800).G);
    }

    [Fact]
    public void Other_Data_Is_Not_Svg()
    {
        Assert.False(SvgRasterizer.IsSvg("VCLMTF\0\0\0\0\0\0"u8));
        Assert.False(SvgRasterizer.IsSvg("<html><body/></html>"u8));
        Assert.Null(SvgRasterizer.RenderPng("<svg xmlns='http://www.w3.org/2000/svg'><rect"u8.ToArray()));
    }
}

/// <summary>Reads the unfiltered RGBA PNG the rasterizers write, for pixel checks.</summary>
internal sealed class Issue242Png
{
    private readonly byte[] _rgba;

    public Issue242Png(byte[] png)
    {
        Width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));
        Height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
        using var idat = new MemoryStream();
        for (int at = 8; at < png.Length;)
        {
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            if (png.AsSpan(at + 4, 4).SequenceEqual("IDAT"u8))
                idat.Write(png, at + 8, length);
            at += 12 + length;
        }
        idat.Position = 0;
        using var inflater = new System.IO.Compression.ZLibStream(idat, System.IO.Compression.CompressionMode.Decompress);
        using var raw = new MemoryStream();
        inflater.CopyTo(raw);
        byte[] rows = raw.ToArray();
        _rgba = new byte[Width * Height * 4];
        for (int row = 0; row < Height; row++)
            Array.Copy(rows, row * (Width * 4 + 1) + 1, _rgba, row * Width * 4, Width * 4);
    }

    public int Width { get; }

    public int Height { get; }

    public (byte R, byte G, byte B, byte A) GetPixel(int x, int y)
    {
        int at = (y * Width + x) * 4;
        return (_rgba[at], _rgba[at + 1], _rgba[at + 2], _rgba[at + 3]);
    }
}
