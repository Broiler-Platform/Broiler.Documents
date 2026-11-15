using System.Text;
using Broiler.Documents.Resources;

namespace Broiler.Documents.Tests;

/// <summary>
/// Office conformance issue 249, ODT pictures: the text a StarView metafile
/// draws. A formula's replacement picture is nothing but STRETCHTEXT actions
/// (tdf96113.odt, tdf144319.odt), and a chart's labels are TEXTARRAY actions
/// (tdf40260.odt, large-chart-labels.odt). Both were skipped, so a formula drew
/// as the marked missing-picture box and a chart had no labels.
/// </summary>
public sealed class SvmRasterizerIssue249OdtImagesTests
{
    private const ushort Text = 112, TextArray = 113, StretchText = 114, TextColor = 134, TextAlign = 136,
        Font = 138, Push = 139, Pop = 140;

    [Fact]
    public void A_Metafile_Of_Text_Alone_Draws_Its_Text()
    {
        // tdf96113.odt's formula: FONT, TEXTALIGN baseline, TEXTCOLOR, then one
        // STRETCHTEXT per symbol, in 1/100 mm.
        var svm = new SvmBuilder(2000, 1000);
        svm.Add(Font, FontPayload("Liberation Serif", 700, charSet: 0));
        svm.Add(TextAlign, BitConverter.GetBytes((ushort)1));
        svm.Add(TextColor, BitConverter.GetBytes(0x001A1A1Au));
        svm.Add(StretchText, TextPayload(100, 700, "ab", unicode: false, stretchWidth: 600), version: 2);

        byte[] png = Assert.IsType<byte[]>(SvmRasterizer.RenderPng(svm.ToArray()));
        var bitmap = new Issue242Png(png);

        // 2000 x 1000 hundredths of a millimetre at two pixels a point.
        Assert.Equal(114, bitmap.Width);
        Assert.Equal(57, bitmap.Height);
        // Ink above the baseline (7 mm down, 40 px) where the text starts, none below it or to its right.
        Assert.True(Ink(bitmap, 4, 20, 40, 40) > 20);
        Assert.Equal(0, Ink(bitmap, 0, 46, 114, 57));
        Assert.Equal(0, Ink(bitmap, 60, 0, 114, 57));
    }

    [Fact]
    public void A_Unicode_Fonts_Strings_Are_Read_As_Utf16_And_Text_Is_Placed_By_Its_Dx_Array()
    {
        // StarSymbol and the chart fonts stream their strings as UTF-16 with a
        // u32 count (RTL_TEXTENCODING_UNICODE). A TEXTARRAY's DX array places
        // each character: here the second one far to the right.
        var svm = new SvmBuilder(4000, 1000);
        svm.Add(Font, FontPayload("Liberation Sans", 500, charSet: 0xFFFF));
        svm.Add(TextAlign, BitConverter.GetBytes((ushort)1));
        svm.Add(TextColor, BitConverter.GetBytes(0u));
        svm.Add(TextArray, TextPayload(100, 700, "HH", unicode: true, dx: [300, 600]), version: 2);

        var bitmap = new Issue242Png(Assert.IsType<byte[]>(SvmRasterizer.RenderPng(svm.ToArray())));
        Assert.True(Ink(bitmap, 0, 0, 30, 57) > 10);
        // 3 mm after the start, about 20 px in: the second H.
        Assert.True(Ink(bitmap, 16, 0, 30, 57) > 5);
        Assert.Equal(0, Ink(bitmap, 60, 0, 228, 57));
    }

    [Fact]
    public void Pop_Restores_The_Font_And_Text_Colour_A_Push_Saved()
    {
        // PUSH (font and text colour), a transparent text colour, POP: the
        // text after the POP is drawn in the saved black.
        var svm = new SvmBuilder(2000, 1000);
        svm.Add(Font, FontPayload("Liberation Serif", 700, charSet: 0));
        svm.Add(TextAlign, BitConverter.GetBytes((ushort)1));
        svm.Add(TextColor, BitConverter.GetBytes(0u));
        svm.Add(Push, BitConverter.GetBytes((ushort)0x000C));
        svm.Add(TextColor, BitConverter.GetBytes(0xFF000000u));
        svm.Add(Text, TextPayload(1000, 700, "x", unicode: false), version: 2);
        svm.Add(Pop, []);
        svm.Add(Text, TextPayload(100, 700, "x", unicode: false), version: 2);

        var bitmap = new Issue242Png(Assert.IsType<byte[]>(SvmRasterizer.RenderPng(svm.ToArray())));
        Assert.True(Ink(bitmap, 0, 0, 40, 57) > 5);
        Assert.Equal(0, Ink(bitmap, 50, 0, 114, 57));
    }

    [Fact]
    public void Text_Before_Any_Font_Is_Still_Not_Drawn()
    {
        var svm = new SvmBuilder(2000, 1000);
        svm.Add(Text, TextPayload(100, 700, "x", unicode: false), version: 2);

        Assert.Null(SvmRasterizer.RenderPng(svm.ToArray()));
    }

    [Fact]
    public void A_Scaled_Bitmap_Is_Drawn_At_Its_Stated_Place_And_Size()
    {
        // tdf109228.odt's OLE replacement is one BMPSCALE: a DIB, then the point
        // and size, here the whole 2 x 1 cm picture. Top row red, bottom blue.
        foreach (bool compressed in new[] { false, true })
        {
            var svm = new SvmBuilder(2000, 1000);
            svm.Add(BmpScale, [.. Dib(compressed), .. Place(0, 0, 2000, 1000)]);

            var bitmap = new Issue242Png(Assert.IsType<byte[]>(SvmRasterizer.RenderPng(svm.ToArray())));
            var top = bitmap.GetPixel(57, 10);
            var bottom = bitmap.GetPixel(57, 45);
            Assert.Equal((255, 0, 0, 255), (top.R, top.G, top.B, top.A));
            Assert.Equal((0, 0, 255, 255), (bottom.R, bottom.G, bottom.B, bottom.A));
        }
    }

    [Fact]
    public void A_BitmapEx_Is_Drawn_Only_Without_Transparency()
    {
        byte[] Ex(byte transparency) =>
            [.. Dib(compressed: true), .. BitConverter.GetBytes(0x25091962u), .. BitConverter.GetBytes(0xACB20201u), transparency];

        var opaque = new SvmBuilder(2000, 1000);
        opaque.Add(BmpExScale, [.. Ex(0), .. Place(0, 0, 2000, 1000)]);
        var bitmap = new Issue242Png(Assert.IsType<byte[]>(SvmRasterizer.RenderPng(opaque.ToArray())));
        Assert.Equal(255, bitmap.GetPixel(57, 10).A);

        // A mask or alpha this does not apply: nothing is drawn rather than an opaque block.
        var masked = new SvmBuilder(2000, 1000);
        masked.Add(BmpExScale, [.. Ex(2), .. Place(0, 0, 2000, 1000)]);
        Assert.Null(SvmRasterizer.RenderPng(masked.ToArray()));
    }

    private const ushort BmpScale = 117, BmpExScale = 120;

    private static byte[] Place(int x, int y, int width, int height) =>
        [.. BitConverter.GetBytes(x), .. BitConverter.GetBytes(y), .. BitConverter.GetBytes(width), .. BitConverter.GetBytes(height)];

    /// <summary>
    /// A 2 x 2, 24-bit DIB with its file header as vcl's WriteDIB writes it,
    /// bottom row first; compressed is dibtools' ZCOMPRESS.
    /// </summary>
    private static byte[] Dib(bool compressed)
    {
        byte[] pixels = [255, 0, 0, 255, 0, 0, 0, 0, 0, 0, 255, 0, 0, 255, 0, 0];
        byte[] bits = pixels;
        if (compressed)
        {
            using var coded = new MemoryStream();
            using (var zlib = new System.IO.Compression.ZLibStream(coded, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(pixels);
            bits = [.. BitConverter.GetBytes((uint)coded.Length), .. BitConverter.GetBytes((uint)pixels.Length),
                .. BitConverter.GetBytes(0u), .. coded.ToArray()];
        }

        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("BM"u8);
        w.Write((uint)(54 + bits.Length));
        w.Write(0u);
        w.Write(54u);
        w.Write(40u);
        w.Write(2); w.Write(2);
        w.Write((ushort)1); w.Write((ushort)24);
        w.Write(compressed ? 0x01004453u : 0u);
        w.Write((uint)pixels.Length);
        w.Write(0); w.Write(0); w.Write(0u); w.Write(0u);
        w.Write(bits);
        w.Flush();
        return stream.ToArray();
    }

    private static int Ink(Issue242Png bitmap, int left, int top, int right, int bottom)
    {
        int count = 0;
        for (int y = top; y < Math.Min(bottom, bitmap.Height); y++)
            for (int x = left; x < Math.Min(right, bitmap.Width); x++)
                if (bitmap.GetPixel(x, y).A > 128)
                    count++;
        return count;
    }

    /// <summary>A version-compat Font as vcl's WriteImplFont streams it.</summary>
    private static byte[] FontPayload(string family, int height, ushort charSet)
    {
        using var body = new MemoryStream();
        using (var w = new BinaryWriter(body, Encoding.Latin1, leaveOpen: true))
        {
            w.Write((ushort)family.Length);
            w.Write(Encoding.Latin1.GetBytes(family));
            w.Write((ushort)0);          // style name
            w.Write(0); w.Write(height); // size
            w.Write(charSet);
            w.Write((ushort)0);          // family
            w.Write((ushort)0);          // pitch
            w.Write((ushort)5);          // WEIGHT_NORMAL
            w.Write((ushort)0);          // underline
            w.Write((ushort)0);          // strikeout
            w.Write((ushort)0);          // ITALIC_NONE
            w.Write((ushort)0);          // language
            w.Write((ushort)0);          // width type
            w.Write((short)0);           // orientation
            w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
        }

        using var payload = new MemoryStream();
        using (var w = new BinaryWriter(payload))
        {
            w.Write((ushort)1);
            w.Write((uint)body.Length);
            w.Write(body.ToArray());
        }
        return payload.ToArray();
    }

    /// <summary>A TEXT, TEXTARRAY or STRETCHTEXT payload, with the version 2 UTF-16 copy.</summary>
    private static byte[] TextPayload(int x, int y, string text, bool unicode, uint? stretchWidth = null, int[]? dx = null)
    {
        using var payload = new MemoryStream();
        using (var w = new BinaryWriter(payload))
        {
            w.Write(x); w.Write(y);
            if (unicode)
            {
                w.Write((uint)text.Length);
                w.Write(Encoding.Unicode.GetBytes(text));
            }
            else
            {
                w.Write((ushort)text.Length);
                w.Write(Encoding.Latin1.GetBytes(text));
            }
            if (stretchWidth is uint width)
                w.Write(width);
            w.Write((ushort)0);
            w.Write((ushort)text.Length);
            if (dx is not null)
            {
                w.Write((uint)dx.Length);
                foreach (int value in dx)
                    w.Write(value);
            }
            w.Write((ushort)text.Length);
            w.Write(Encoding.Unicode.GetBytes(text));
        }
        return payload.ToArray();
    }

    /// <summary>A VCLMTF in 1/100 mm of the given preferred size.</summary>
    private sealed class SvmBuilder(int width, int height)
    {
        private readonly MemoryStream _actions = new();
        private int _count;

        public void Add(ushort type, byte[] payload, ushort version = 1)
        {
            using var w = new BinaryWriter(_actions, Encoding.Latin1, leaveOpen: true);
            w.Write(type);
            w.Write(version);
            w.Write((uint)payload.Length);
            w.Write(payload);
            _count++;
        }

        public byte[] ToArray()
        {
            using var stream = new MemoryStream();
            using var w = new BinaryWriter(stream);
            w.Write("VCLMTF"u8);
            w.Write((ushort)1);
            w.Write(49u);
            w.Write(0u);                                // compression
            w.Write((ushort)1); w.Write(27u);           // map mode compat header
            w.Write((ushort)0);                         // 1/100 mm
            w.Write(0); w.Write(0);                     // origin
            w.Write(1); w.Write(1); w.Write(1); w.Write(1);
            w.Write((byte)1);
            w.Write(width); w.Write(height);            // preferred size
            w.Write((uint)_count);
            w.Write(_actions.ToArray());
            w.Flush();
            return stream.ToArray();
        }
    }
}
