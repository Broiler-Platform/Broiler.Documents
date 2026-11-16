using Broiler.Documents.Resources;

namespace Broiler.Documents.Tests;

/// <summary>
/// Office conformance issue 249, ODT pictures: htmlexport's tdf162282.odt is an
/// EMF of SETARCDIRECTION, ARC and PIE. The rasterizer knew none of them, so it
/// drew nothing and the picture became the marked missing-picture box.
/// </summary>
public sealed class EmfRasterizerIssue249OdtImagesTests
{
    // A 50 mm square EMF whose logical units are 500 device pixels across.
    private const int Arc = 45, Chord = 46, Pie = 47, SetArcDirection = 57;

    [Fact]
    public void A_Pie_Runs_Counterclockwise_By_Default()
    {
        // From the radial through the right edge to the one through the top:
        // counterclockwise on the page is the top-right quarter.
        byte[] png = Assert.IsType<byte[]>(EmfRasterizer.RenderPng(Emf(Record(Pie, 0, 0, 400, 400, 400, 200, 200, 0))));
        var bitmap = new Issue242Png(png);

        Assert.Equal(284, bitmap.Width);
        Assert.Equal(255, bitmap.GetPixel(170, 57).A);
        Assert.Equal(0, bitmap.GetPixel(57, 170).A);
        Assert.Equal(0, bitmap.GetPixel(170, 170).A);
    }

    [Fact]
    public void SetArcDirection_Clockwise_Takes_The_Other_Way_Round()
    {
        byte[] png = Assert.IsType<byte[]>(EmfRasterizer.RenderPng(Emf(
            Record(SetArcDirection, 2),
            Record(Pie, 0, 0, 400, 400, 400, 200, 200, 0))));
        var bitmap = new Issue242Png(png);

        Assert.Equal(0, bitmap.GetPixel(170, 57).A);
        Assert.Equal(255, bitmap.GetPixel(57, 170).A);
        Assert.Equal(255, bitmap.GetPixel(170, 170).A);
    }

    [Fact]
    public void An_Arc_With_Equal_Radials_Is_The_Whole_Outline_And_Is_Not_Filled()
    {
        // tdf162282.odt's ARC: start and end both (20, 20).
        byte[] png = Assert.IsType<byte[]>(EmfRasterizer.RenderPng(Emf(Record(Arc, 0, 0, 400, 400, 20, 20, 20, 20))));
        var bitmap = new Issue242Png(png);

        // The outline crosses each side of the box; the middle stays empty.
        Assert.True(Ink(bitmap, 0, 4, 100, 130) > 0);
        Assert.True(Ink(bitmap, 223, 231, 100, 130) > 0);
        Assert.Equal(0, bitmap.GetPixel(113, 113).A);
    }

    [Fact]
    public void A_Chord_Is_Closed_By_The_Straight_Line_Between_Its_Ends()
    {
        // The top half, right to left counterclockwise, closed along the
        // diameter: filled above it, empty below it.
        byte[] png = Assert.IsType<byte[]>(EmfRasterizer.RenderPng(Emf(Record(Chord, 0, 0, 400, 400, 400, 200, 0, 200))));
        var bitmap = new Issue242Png(png);

        Assert.Equal(255, bitmap.GetPixel(113, 57).A);
        Assert.Equal(0, bitmap.GetPixel(113, 170).A);
    }

    [Fact]
    public void A_Mask_Anded_Then_A_Picture_Xored_Over_It_Draws_The_Picture_Where_The_Mask_Is_Black()
    {
        // tdf131537.odt's OLE icon: a monochrome mask with SRCAND, then the
        // colour picture with SRCINVERT, as Windows draws an icon. LibreOffice
        // merges the pair into one picture shown where the mask is black.
        byte[] mask = Dib(bitCount: 1, palette: [0, 0, 0, 0, 255, 255, 255, 0], bits: [0x40, 0, 0, 0]);
        byte[] red = Dib(bitCount: 24, palette: [], bits: [0, 0, 255, 0, 0, 255, 0, 0]);
        byte[] png = Assert.IsType<byte[]>(EmfRasterizer.RenderPng(Emf(
            StretchDiBits(mask, 0x008800C6),
            StretchDiBits(red, 0x00660046))));
        var bitmap = new Issue242Png(png);

        var left = bitmap.GetPixel(57, 113);
        Assert.Equal((255, 0, 0, 255), (left.R, left.G, left.B, left.A));
        Assert.Equal(0, bitmap.GetPixel(170, 113).A);
    }

    [Fact]
    public void An_Xor_Alone_Is_Not_Drawn()
    {
        byte[] red = Dib(bitCount: 24, palette: [], bits: [0, 0, 255, 0, 0, 255, 0, 0]);
        Assert.Null(EmfRasterizer.RenderPng(Emf(StretchDiBits(red, 0x00660046))));
    }

    /// <summary>A 2 x 1 BITMAPINFOHEADER, palette and bits.</summary>
    private static byte[] Dib(ushort bitCount, byte[] palette, byte[] bits)
    {
        var header = new byte[40];
        BitConverter.GetBytes(40).CopyTo(header, 0);
        BitConverter.GetBytes(2).CopyTo(header, 4);
        BitConverter.GetBytes(1).CopyTo(header, 8);
        BitConverter.GetBytes((ushort)1).CopyTo(header, 12);
        BitConverter.GetBytes(bitCount).CopyTo(header, 14);
        BitConverter.GetBytes(palette.Length / 4).CopyTo(header, 32);
        return [.. header, .. palette, .. bits];
    }

    /// <summary>A STRETCHDIBITS of the whole 2 x 1 bitmap over the logical box (0, 0)-(400, 400).</summary>
    private static byte[] StretchDiBits(byte[] dib, uint rop)
    {
        int infoLength = dib[14] == 1 ? 48 : 40;
        byte[] record = Record(81, 0, 0, 400, 400, 0, 0, 0, 0, 2, 1, 80, infoLength, 80 + infoLength, dib.Length - infoLength,
            0, unchecked((int)rop), 400, 400);
        return [.. record[..4], .. BitConverter.GetBytes(record.Length + dib.Length), .. record[8..], .. dib];
    }

    private static int Ink(Issue242Png bitmap, int left, int right, int top, int bottom)
    {
        int ink = 0;
        for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
                if (bitmap.GetPixel(x, y).A > 0)
                    ink++;
        return ink;
    }

    private static byte[] Record(int type, params int[] values)
    {
        var record = new byte[8 + values.Length * 4];
        BitConverter.GetBytes(type).CopyTo(record, 0);
        BitConverter.GetBytes(record.Length).CopyTo(record, 4);
        for (int i = 0; i < values.Length; i++)
            BitConverter.GetBytes(values[i]).CopyTo(record, 8 + i * 4);
        return record;
    }

    private static byte[] Emf(params byte[][] records)
    {
        var header = new byte[108];
        BitConverter.GetBytes(1).CopyTo(header, 0);
        BitConverter.GetBytes(108).CopyTo(header, 4);
        BitConverter.GetBytes(500).CopyTo(header, 16);  // bounds right
        BitConverter.GetBytes(500).CopyTo(header, 20);  // bounds bottom
        BitConverter.GetBytes(5000).CopyTo(header, 32); // frame right, 0.01 mm
        BitConverter.GetBytes(5000).CopyTo(header, 36); // frame bottom
        BitConverter.GetBytes(0x464D4520).CopyTo(header, 40);
        BitConverter.GetBytes(0x10000).CopyTo(header, 44);
        BitConverter.GetBytes(500).CopyTo(header, 72);  // szlDevice
        BitConverter.GetBytes(500).CopyTo(header, 76);
        BitConverter.GetBytes(50).CopyTo(header, 80);   // szlMillimeters
        BitConverter.GetBytes(50).CopyTo(header, 84);
        byte[] eof = Record(14, 0, 16, 20);
        return [.. header, .. records.SelectMany(record => record), .. eof];
    }
}
