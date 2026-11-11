using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using Broiler.Graphics.Color;
using Broiler.Graphics.Imaging;

namespace Broiler.Documents.Rtf;

/// <summary>
/// Rasterizes the vector subset of an Enhanced Metafile (<c>\emfblip</c>) to a PNG:
/// filled polygons and polylines under the window/viewport mapping and world
/// transform. Text, bitmaps and paths are not drawn. Returns null when the
/// metafile draws nothing this subset understands.
/// </summary>
internal static class RtfEmf
{
    private const int MaxRecords = 200_000;
    private const int MaxPoints = 1_000_000;

    private sealed class Dc
    {
        public double M11 = 1, M12, M21, M22 = 1, Dx, Dy;
        public int MapMode = 1;
        public double WinOrgX, WinOrgY, WinExtX = 1, WinExtY = 1, VpOrgX, VpOrgY, VpExtX = 1, VpExtY = 1;
        public BColor Brush = new(255, 255, 255);
        public BColor Pen = new(0, 0, 0);
        public double PenWidth = 1;

        public Dc Clone() => (Dc)MemberwiseClone();
    }

    private readonly record struct GdiObject(bool IsBrush, BColor Color, double Width);

    public static bool IsEmf(ReadOnlySpan<byte> data) =>
        data.Length >= 88 && BinaryPrimitives.ReadUInt32LittleEndian(data) == 1 &&
        BinaryPrimitives.ReadUInt32LittleEndian(data[40..]) == 0x464D4520;

    public static byte[]? RenderPng(ReadOnlySpan<byte> data)
    {
        if (!IsEmf(data))
            return null;

        // Frame is in 0.01 mm; szlDevice / szlMillimeters convert it to device units.
        int frameLeft = I32(data, 24), frameTop = I32(data, 28), frameRight = I32(data, 32), frameBottom = I32(data, 36);
        int deviceX = I32(data, 72), deviceY = I32(data, 76), mmX = I32(data, 80), mmY = I32(data, 84);
        if (deviceX <= 0 || deviceY <= 0 || mmX <= 0 || mmY <= 0 || frameRight <= frameLeft || frameBottom <= frameTop)
            return null;
        double unitX = deviceX / (mmX * 100.0), unitY = deviceY / (mmY * 100.0);
        double left = frameLeft * unitX, top = frameTop * unitY;
        double width = (frameRight - frameLeft) * unitX, height = (frameBottom - frameTop) * unitY;
        double widthPt = (frameRight - frameLeft) * 72 / 2540.0, heightPt = (frameBottom - frameTop) * 72 / 2540.0;
        double scale = Math.Min(2, 1536 / Math.Max(widthPt, heightPt));
        int pixelsWide = Math.Max(1, (int)Math.Ceiling(widthPt * scale));
        int pixelsHigh = Math.Max(1, (int)Math.Ceiling(heightPt * scale));
        double sx = pixelsWide / width, sy = pixelsHigh / height;

        var objects = new Dictionary<uint, GdiObject>();
        var saved = new Stack<Dc>();
        var dc = new Dc();
        var shapes = new List<(PointF[] Points, BColor Color, float StrokeWidth)>();
        int totalPoints = 0;

        int offset = 0;
        for (int record = 0; record < MaxRecords && offset + 8 <= data.Length; record++)
        {
            uint type = U32(data, offset);
            int size = I32(data, offset + 4);
            if (size < 8 || size % 4 != 0 || size > data.Length - offset)
                break;
            ReadOnlySpan<byte> r = data.Slice(offset, size);
            offset += size;
            switch (type)
            {
                case 14: // EOF
                    offset = data.Length;
                    break;
                case 9 when size >= 16: dc.WinExtX = I32(r, 8); dc.WinExtY = I32(r, 12); break;
                case 10 when size >= 16: dc.WinOrgX = I32(r, 8); dc.WinOrgY = I32(r, 12); break;
                case 11 when size >= 16: dc.VpExtX = I32(r, 8); dc.VpExtY = I32(r, 12); break;
                case 12 when size >= 16: dc.VpOrgX = I32(r, 8); dc.VpOrgY = I32(r, 12); break;
                case 17 when size >= 12: dc.MapMode = I32(r, 8); break;
                case 33: saved.Push(dc.Clone()); break;
                case 34 when size >= 12:
                    for (int pop = I32(r, 8); pop < 0 && saved.Count > 0; pop++)
                        dc = saved.Pop();
                    break;
                case 35 when size >= 32: // SETWORLDTRANSFORM
                    SetTransform(dc, ReadXform(r));
                    break;
                case 36 when size >= 36: // MODIFYWORLDTRANSFORM
                    double[] x = ReadXform(r);
                    switch (I32(r, 32))
                    {
                        case 1: SetTransform(dc, [1, 0, 0, 1, 0, 0]); break;
                        case 2: SetTransform(dc, Multiply(x, Current(dc))); break;
                        case 3: SetTransform(dc, Multiply(Current(dc), x)); break;
                        case 4: SetTransform(dc, x); break;
                    }
                    break;
                case 39 when size >= 24: // CREATEBRUSHINDIRECT
                    objects[U32(r, 8)] = new GdiObject(true, U32(r, 12) == 1 ? BColor.Empty : ColorRef(U32(r, 16)), 0);
                    break;
                case 38 when size >= 28: // CREATEPEN
                    objects[U32(r, 8)] = new GdiObject(false, (U32(r, 12) & 0xF) == 5 ? BColor.Empty : ColorRef(U32(r, 24)), I32(r, 16));
                    break;
                case 95 when size >= 44: // EXTCREATEPEN
                    objects[U32(r, 8)] = new GdiObject(false, (U32(r, 28) & 0xF) == 5 || U32(r, 36) == 1 ? BColor.Empty : ColorRef(U32(r, 40)), U32(r, 32));
                    break;
                case 40 when size >= 12: objects.Remove(U32(r, 8)); break;
                case 37 when size >= 12: Select(dc, U32(r, 8), objects); break;
                case 3 or 4 or 86 or 87 when size >= 28: // POLYGON, POLYLINE, POLYGON16, POLYLINE16
                    bool shortPoints = type >= 86;
                    int count = I32(r, 24);
                    int stride = shortPoints ? 4 : 8;
                    if (count < 2 || count > (size - 28) / stride || (totalPoints += count) > MaxPoints)
                        break;
                    var points = new PointF[count + (type is 3 or 86 ? 1 : 0)];
                    for (int i = 0; i < count; i++)
                    {
                        double px = shortPoints ? BinaryPrimitives.ReadInt16LittleEndian(r[(28 + i * 4)..]) : I32(r, 28 + i * 8);
                        double py = shortPoints ? BinaryPrimitives.ReadInt16LittleEndian(r[(30 + i * 4)..]) : I32(r, 32 + i * 8);
                        (double dx, double dy) = ToDevice(dc, px, py);
                        points[i] = new PointF((float)((dx - left) * sx), (float)((dy - top) * sy));
                    }
                    if (type is 3 or 86)
                    {
                        points[count] = points[0];
                        if (!dc.Brush.IsEmpty)
                            shapes.Add((points, dc.Brush, 0));
                    }
                    if (!dc.Pen.IsEmpty)
                    {
                        double penWidth = Math.Max(1, dc.PenWidth) * Math.Sqrt(Math.Abs((dc.M11 * dc.M22 - dc.M12 * dc.M21) * DeviceScaleX(dc) * DeviceScaleY(dc)));
                        shapes.Add((points, dc.Pen, (float)Math.Clamp(penWidth * Math.Min(sx, sy), 0.5, 64)));
                    }
                    break;
            }
        }

        if (shapes.Count == 0)
            return null;

        using var bitmap = new BBitmap(pixelsWide, pixelsHigh);
        using (var canvas = bitmap.OpenCanvas())
        {
            foreach (var (points, color, strokeWidth) in shapes)
            {
                if (strokeWidth > 0)
                    canvas.DrawPathStroke(points, color, strokeWidth);
                else
                    canvas.FillGlyphContours([points], color);
            }
        }

        return EncodePng(bitmap);
    }

    private static void Select(Dc dc, uint handle, Dictionary<uint, GdiObject> objects)
    {
        if ((handle & 0x80000000) != 0)
        {
            switch (handle & 0x7FFFFFFF)
            {
                case 0: dc.Brush = new BColor(255, 255, 255); break;
                case 1: dc.Brush = new BColor(192, 192, 192); break;
                case 2: dc.Brush = new BColor(128, 128, 128); break;
                case 3: dc.Brush = new BColor(64, 64, 64); break;
                case 4: dc.Brush = new BColor(0, 0, 0); break;
                case 5: dc.Brush = BColor.Empty; break;
                case 6: dc.Pen = new BColor(255, 255, 255); dc.PenWidth = 1; break;
                case 7: dc.Pen = new BColor(0, 0, 0); dc.PenWidth = 1; break;
                case 8: dc.Pen = BColor.Empty; break;
            }
        }
        else if (objects.TryGetValue(handle, out GdiObject value))
        {
            if (value.IsBrush)
                dc.Brush = value.Color;
            else
            {
                dc.Pen = value.Color;
                dc.PenWidth = value.Width;
            }
        }
    }

    private static (double X, double Y) ToDevice(Dc dc, double x, double y)
    {
        double pageX = x * dc.M11 + y * dc.M21 + dc.Dx;
        double pageY = x * dc.M12 + y * dc.M22 + dc.Dy;
        return ((pageX - dc.WinOrgX) * DeviceScaleX(dc) + dc.VpOrgX, (pageY - dc.WinOrgY) * DeviceScaleY(dc) + dc.VpOrgY);
    }

    private static double DeviceScaleX(Dc dc) =>
        dc.MapMode is 7 or 8 && dc.WinExtX != 0 ? dc.VpExtX / dc.WinExtX : 1;

    private static double DeviceScaleY(Dc dc) =>
        dc.MapMode is 7 or 8 && dc.WinExtY != 0 ? dc.VpExtY / dc.WinExtY : 1;

    private static double[] ReadXform(ReadOnlySpan<byte> r)
    {
        var x = new double[6];
        for (int i = 0; i < 6; i++)
            x[i] = BinaryPrimitives.ReadSingleLittleEndian(r[(8 + i * 4)..]);
        return x;
    }

    private static double[] Current(Dc dc) => [dc.M11, dc.M12, dc.M21, dc.M22, dc.Dx, dc.Dy];

    // Row-vector convention: the result applies a, then b.
    private static double[] Multiply(double[] a, double[] b) =>
    [
        a[0] * b[0] + a[1] * b[2], a[0] * b[1] + a[1] * b[3],
        a[2] * b[0] + a[3] * b[2], a[2] * b[1] + a[3] * b[3],
        a[4] * b[0] + a[5] * b[2] + b[4], a[4] * b[1] + a[5] * b[3] + b[5],
    ];

    private static void SetTransform(Dc dc, double[] x)
    {
        foreach (double value in x)
            if (!double.IsFinite(value))
                return;
        (dc.M11, dc.M12, dc.M21, dc.M22, dc.Dx, dc.Dy) = (x[0], x[1], x[2], x[3], x[4], x[5]);
    }

    private static BColor ColorRef(uint value) =>
        new((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));

    private static int I32(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadInt32LittleEndian(data[at..]);

    private static uint U32(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);

    private static uint Crc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320u;
        }
        return crc;
    }

    private static byte[] EncodePng(BBitmap bitmap)
    {
        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteInt32BigEndian(header, bitmap.Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], bitmap.Height);
        header[8] = 8;
        header[9] = 6;
        Chunk("IHDR"u8, header);

        using var data = new MemoryStream();
        byte[] rgba = bitmap.ToPixelBuffer(copy: false).Rgba;
        int stride = bitmap.Width * 4;
        using (var compressor = new ZLibStream(data, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (int row = 0; row < bitmap.Height; row++)
            {
                compressor.WriteByte(0);
                compressor.Write(rgba.AsSpan(row * stride, stride));
            }
        }
        Chunk("IDAT"u8, data.GetBuffer().AsSpan(0, (int)data.Length));
        Chunk("IEND"u8, []);
        return png.ToArray();

        void Chunk(ReadOnlySpan<byte> type, ReadOnlySpan<byte> bytes)
        {
            Span<byte> field = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(field, bytes.Length);
            png.Write(field);
            png.Write(type);
            png.Write(bytes);
            uint crc = Crc(Crc(uint.MaxValue, type), bytes);
            BinaryPrimitives.WriteUInt32BigEndian(field, ~crc);
            png.Write(field);
        }
    }
}
