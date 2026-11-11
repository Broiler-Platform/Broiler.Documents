using System.Buffers.Binary;
using System.IO.Compression;
using Broiler.Documents.Resources;

namespace Broiler.Documents.Tests;

/// <summary>
/// The StarView metafile (VCLMTF) LibreOffice stores as a chart's or OLE
/// object's replacement picture, rasterized to the vector subset it draws.
/// </summary>
public sealed class SvmRasterizerTests
{
    [Fact]
    public void A_Filled_Polygon_And_A_Hairline_Are_Drawn_In_Their_Colours()
    {
        // 1/100 mm, 1000 x 1000: a red square over the left half and a blue
        // hairline across the top quarter.
        var svm = new SvmBuilder(unit: 0, width: 1000, height: 1000);
        svm.LineColor(0, set: false);
        svm.FillColor(0xFF0000, set: true);
        svm.PolyPolygon([(0, 0), (500, 0), (500, 1000), (0, 1000)]);
        svm.LineColor(0x0000FF, set: true);
        svm.PolyLine((0, 250), (1000, 250));

        byte[] png = Assert.IsType<byte[]>(SvmRasterizer.RenderPng(svm.ToArray()));

        (int width, int height, byte[] rgba) = DecodePng(png);
        Assert.Equal((255, 0, 0), Pixel(rgba, width, width / 4, height * 3 / 4));
        Assert.Equal(0, rgba[((height * 3 / 4) * width + width * 3 / 4) * 4 + 3]); // right half untouched
        int lineRow = height / 4;
        Assert.True(Enumerable.Range(lineRow - 2, 5).Any(row => Pixel(rgba, width, width * 3 / 4, row) == (0, 0, 255)),
            "the hairline is drawn");
    }

    [Fact]
    public void A_Relative_Map_Mode_Composes_Onto_The_Current_One()
    {
        // A twip page with a 1/100 mm chart placed at its centre at half size,
        // as LibreOffice stores a chart inside an embedded text object.
        var svm = new SvmBuilder(unit: 9, width: 2000, height: 2000);
        svm.LineColor(0, set: false);
        svm.FillColor(0x00FF00, set: true);
        svm.MapMode(unit: 13, originX: 2000, originY: 2000, scaleNum: 1, scaleDen: 2);
        svm.PolyPolygon([(0, 0), (1000, 0), (1000, 1000), (0, 1000)]);

        (int width, int height, byte[] rgba) = DecodePng(Assert.IsType<byte[]>(SvmRasterizer.RenderPng(svm.ToArray())));

        // (p + 2000) / 2 puts the square at 1000..1500 of the 2000-twip page.
        Assert.Equal((0, 255, 0), Pixel(rgba, width, width * 5 / 8, height * 5 / 8));
        Assert.Equal(0, rgba[((height / 4) * width + width / 4) * 4 + 3]);
        Assert.Equal(0, rgba[((height * 7 / 8) * width + width * 7 / 8) * 4 + 3]);
    }

    [Fact]
    public void An_Inch_Map_Mode_Scaled_To_Hundredths_Of_A_Millimetre_Places_Its_Lines()
    {
        // tdf161979_metafile.fodt's first picture: MapInch with a 1/2540 scale,
        // so logical units are 1/100 mm; two diagonal hairlines on a 1076 x 1789
        // canvas. Only the lines are drawn; the rest stays transparent.
        var svm = new SvmBuilder(unit: 7, width: 1076, height: 1789, scaleNum: 1, scaleDen: 2540);
        svm.LineColor(0, set: true);
        svm.PolyLine((788, 288), (49, 1789));
        svm.PolyLine((1076, 49), (337, 1550));

        (int width, int height, byte[] rgba) = DecodePng(Assert.IsType<byte[]>(SvmRasterizer.RenderPng(svm.ToArray())));

        // 1076 x 1789 hundredths of a millimetre at 2 px per point.
        Assert.InRange(width, 61, 62);
        Assert.InRange(height, 101, 102);
        // The first line's midpoint, (418.5, 1038.5), is inked; the top-left corner is not.
        int x = (int)(418.5 / 1076 * width), y = (int)(1038.5 / 1789 * height);
        Assert.True(Enumerable.Range(-1, 3).Any(d => rgba[((y + d) * width + x) * 4 + 3] > 0), "the line is drawn");
        Assert.Equal(0, rgba[3]);
    }

    [Fact]
    public void A_Metafile_Of_Emf_Plus_Comments_Alone_Yields_Null()
    {
        // tdf161979_metafile.fodt's second picture carries its drawing only as
        // EMF+ records in COMMENT actions, which this subset does not play, so
        // the picture keeps the missing-format mark rather than drawing blank.
        var svm = new SvmBuilder(unit: 0, width: 6219, height: 1893);
        svm.Comment("EMF_PLUS_HEADER_INFO");
        svm.Comment("EMF_PLUS");
        Assert.Null(SvmRasterizer.RenderPng(svm.ToArray()));
    }

    [Fact]
    public void A_Metafile_With_Nothing_Drawable_Or_Not_A_Metafile_Yields_Null()
    {
        var svm = new SvmBuilder(unit: 0, width: 100, height: 100);
        svm.FillColor(0xFF0000, set: true);
        Assert.Null(SvmRasterizer.RenderPng(svm.ToArray()));
        Assert.Null(SvmRasterizer.RenderPng("VCLMTF"u8.ToArray()));
        Assert.False(SvmRasterizer.IsSvm("not a metafile"u8.ToArray()));
    }

    [Fact]
    public void A_Truncated_Action_Stops_The_Read_Without_Throwing()
    {
        var svm = new SvmBuilder(unit: 0, width: 100, height: 100);
        svm.FillColor(0xFF0000, set: true);
        svm.PolyPolygon([(0, 0), (100, 0), (100, 100)]);
        byte[] data = svm.ToArray();
        Assert.NotNull(SvmRasterizer.RenderPng(data));
        Assert.Null(SvmRasterizer.RenderPng(data[..^10]));
    }

    private static (int R, int G, int B) Pixel(byte[] rgba, int width, int x, int y)
    {
        int at = (y * width + x) * 4;
        return (rgba[at], rgba[at + 1], rgba[at + 2]);
    }

    /// <summary>Decodes the unfiltered RGBA PNG the rasterizers write.</summary>
    private static (int Width, int Height, byte[] Rgba) DecodePng(byte[] png)
    {
        int width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
        using var idat = new MemoryStream();
        for (int at = 8; at < png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            if (png.AsSpan(at + 4, 4).SequenceEqual("IDAT"u8))
                idat.Write(png, at + 8, length);
            at += 12 + length;
        }
        idat.Position = 0;
        using var inflater = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        inflater.CopyTo(raw);
        byte[] rows = raw.ToArray();
        byte[] rgba = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
        {
            Assert.Equal(0, rows[row * (width * 4 + 1)]);
            Array.Copy(rows, row * (width * 4 + 1) + 1, rgba, row * width * 4, width * 4);
        }
        return (width, height, rgba);
    }

    /// <summary>Writes a VCLMTF stream: the header, then version-compat actions.</summary>
    internal sealed class SvmBuilder
    {
        private readonly MemoryStream _actions = new();
        private readonly int _unit, _width, _height, _scaleNum, _scaleDen;
        private int _count;

        public SvmBuilder(int unit, int width, int height, int scaleNum = 1, int scaleDen = 1) =>
            (_unit, _width, _height, _scaleNum, _scaleDen) = (unit, width, height, scaleNum, scaleDen);

        public void Comment(string text) => Action(512, w =>
        {
            w.Write((ushort)text.Length);
            w.Write(System.Text.Encoding.ASCII.GetBytes(text));
            w.Write(0);   // value
            w.Write(0u);  // data size
        });

        public void LineColor(int rgb, bool set) => Action(132, w => { w.Write((uint)rgb); w.Write((byte)(set ? 1 : 0)); });

        public void FillColor(int rgb, bool set) => Action(133, w => { w.Write((uint)rgb); w.Write((byte)(set ? 1 : 0)); });

        public void MapMode(int unit, int originX, int originY, int scaleNum, int scaleDen) =>
            Action(137, w => WriteMapMode(w, unit, originX, originY, scaleNum, scaleDen));

        public void PolyPolygon(params (int X, int Y)[][] polygons) => Action(111, w =>
        {
            w.Write((ushort)polygons.Length);
            foreach ((int X, int Y)[] polygon in polygons)
                WritePolygon(w, polygon);
            w.Write((ushort)0); // no complex polygons
        }, version: 2);

        public void PolyLine(params (int X, int Y)[] points) => Action(109, w =>
        {
            WritePolygon(w, points);
            // LineInfo: version 1, solid, hairline.
            w.Write((ushort)1);
            w.Write(6u);
            w.Write((ushort)1);
            w.Write(0);
            w.Write((byte)0); // no flagged polygon
        }, version: 3);

        public byte[] ToArray()
        {
            using var stream = new MemoryStream();
            using var w = new BinaryWriter(stream);
            w.Write("VCLMTF"u8);
            using var header = new MemoryStream();
            using (var h = new BinaryWriter(header, System.Text.Encoding.ASCII, leaveOpen: true))
            {
                h.Write(0u); // compression mode
                WriteMapMode(h, _unit, 0, 0, _scaleNum, _scaleDen);
                h.Write(_width);
                h.Write(_height);
                h.Write((uint)_count);
            }
            w.Write((ushort)1);
            w.Write((uint)header.Length);
            w.Write(header.ToArray());
            w.Write(_actions.ToArray());
            w.Flush();
            return stream.ToArray();
        }

        private static void WriteMapMode(BinaryWriter w, int unit, int originX, int originY, int scaleNum, int scaleDen)
        {
            w.Write((ushort)1);
            w.Write(27u);
            w.Write((ushort)unit);
            w.Write(originX);
            w.Write(originY);
            w.Write(scaleNum);
            w.Write(scaleDen);
            w.Write(scaleNum);
            w.Write(scaleDen);
            w.Write((byte)(scaleNum == scaleDen && originX == 0 && originY == 0 ? 1 : 0));
        }

        private static void WritePolygon(BinaryWriter w, (int X, int Y)[] points)
        {
            w.Write((ushort)points.Length);
            foreach ((int x, int y) in points)
            {
                w.Write(x);
                w.Write(y);
            }
        }

        private void Action(ushort type, Action<BinaryWriter> payload, ushort version = 1)
        {
            using var body = new MemoryStream();
            using (var b = new BinaryWriter(body, System.Text.Encoding.ASCII, leaveOpen: true))
                payload(b);
            using var w = new BinaryWriter(_actions, System.Text.Encoding.ASCII, leaveOpen: true);
            w.Write(type);
            w.Write(version);
            w.Write((uint)body.Length);
            w.Write(body.ToArray());
            _count++;
        }
    }
}
