using System.Buffers.Binary;
using System.IO.Compression;
using Broiler.Documents.Resources;

namespace Broiler.Documents.Tests;

/// <summary>
/// The Windows Metafile a DOCX stores as <c>word/media/*.wmf</c> (office issue
/// #242, <c>tdf103001.docx</c>), drawn by translating its records to EMF.
/// </summary>
public sealed class WmfRasterizerTests
{
    [Fact]
    public void A_Placeable_Metafile_Draws_Its_Polygons_In_Their_Brushes()
    {
        // A 1000 x 1000 box at 1000 units an inch, the window offset as Word's
        // clip-art writes it: a red left half and a blue bottom-right quarter.
        var wmf = new WmfBuilder(left: 500, top: 500, right: 1500, bottom: 1500, inch: 1000);
        wmf.Record(0x020B, 500, 500); // SETWINDOWORG y, x
        wmf.Record(0x020C, 1000, 1000); // SETWINDOWEXT y, x
        wmf.Record(0x02FA, 5, 0, 0, 0, 0); // CREATEPENINDIRECT PS_NULL -> slot 0
        wmf.Record(0x012D, 0);
        wmf.Brush(0x0000FF); // slot 1, red
        wmf.Record(0x012D, 1);
        wmf.Polygon((500, 500), (1000, 500), (1000, 1500), (500, 1500));
        wmf.Record(0x01F0, 1); // DELETEOBJECT frees slot 1
        wmf.Brush(0xFF0000); // takes slot 1 again, blue
        wmf.Record(0x012D, 1);
        wmf.Polygon((1000, 1000), (1500, 1000), (1500, 1500), (1000, 1500));

        byte[] png = Assert.IsType<byte[]>(WmfRasterizer.RenderPng(wmf.ToArray()));

        (int width, int height, byte[] rgba) = DecodePng(png);
        Assert.Equal((255, 0, 0, 255), Pixel(rgba, width, width / 4, height / 2));
        Assert.Equal((0, 0, 255, 255), Pixel(rgba, width, width * 3 / 4, height * 3 / 4));
        Assert.Equal(0, Pixel(rgba, width, width * 3 / 4, height / 4).A);
    }

    [Fact]
    public void Data_That_Is_Not_A_Metafile_Is_Refused()
    {
        Assert.False(WmfRasterizer.IsWmf(new byte[64]));
        Assert.Null(WmfRasterizer.RenderPng([0x89, 0x50, 0x4E, 0x47]));
    }

    private static (int R, int G, int B, int A) Pixel(byte[] rgba, int width, int x, int y)
    {
        int at = (y * width + x) * 4;
        return (rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]);
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
            Array.Copy(rows, row * (width * 4 + 1) + 1, rgba, row * width * 4, width * 4);
        return (width, height, rgba);
    }

    /// <summary>Writes a placeable WMF: the 22-byte placeable header, the metafile header, records, EOF.</summary>
    private sealed class WmfBuilder(short left, short top, short right, short bottom, ushort inch)
    {
        private readonly MemoryStream _records = new();

        public void Record(ushort function, params short[] parameters)
        {
            Write32(3 + parameters.Length);
            Write16(function);
            foreach (short value in parameters)
                Write16((ushort)value);
        }

        public void Brush(uint color)
        {
            Write32(3 + 4);
            Write16(0x02FC);
            Write16(0); // BS_SOLID
            Write32((int)color);
            Write16(0);
        }

        public void Polygon(params (short X, short Y)[] points)
        {
            Write32(3 + 1 + points.Length * 2);
            Write16(0x0324);
            Write16((ushort)points.Length);
            foreach ((short x, short y) in points)
            {
                Write16((ushort)x);
                Write16((ushort)y);
            }
        }

        public byte[] ToArray()
        {
            Record(0x0000);
            byte[] records = _records.ToArray();
            var output = new MemoryStream();
            Span<byte> placeable = stackalloc byte[22];
            placeable.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(placeable, 0x9AC6CDD7);
            BinaryPrimitives.WriteInt16LittleEndian(placeable[6..], left);
            BinaryPrimitives.WriteInt16LittleEndian(placeable[8..], top);
            BinaryPrimitives.WriteInt16LittleEndian(placeable[10..], right);
            BinaryPrimitives.WriteInt16LittleEndian(placeable[12..], bottom);
            BinaryPrimitives.WriteUInt16LittleEndian(placeable[14..], inch);
            output.Write(placeable);
            Span<byte> header = stackalloc byte[18];
            header.Clear();
            BinaryPrimitives.WriteUInt16LittleEndian(header, 1);
            BinaryPrimitives.WriteUInt16LittleEndian(header[2..], 9);
            BinaryPrimitives.WriteUInt16LittleEndian(header[4..], 0x0300);
            BinaryPrimitives.WriteInt32LittleEndian(header[6..], (18 + records.Length) / 2);
            BinaryPrimitives.WriteUInt16LittleEndian(header[10..], 2);
            output.Write(header);
            output.Write(records);
            return output.ToArray();
        }

        private void Write16(ushort value)
        {
            Span<byte> bytes = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
            _records.Write(bytes);
        }

        private void Write32(int value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            _records.Write(bytes);
        }
    }
}
