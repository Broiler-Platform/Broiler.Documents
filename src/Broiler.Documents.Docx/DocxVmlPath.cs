using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using Broiler.Graphics.Color;
using Broiler.Graphics.Imaging;

namespace Broiler.Documents.Docx;

/// <summary>Materializes VML polygonal freeforms into the model's picture format.</summary>
internal static class DocxVmlPath
{
    public static byte[]? RenderNotchedArrow(double width, double height, BColor fill, bool reverse)
    {
        const int size = 10000;
        int head = (int)Math.Round(size * Math.Min(0.5, height / (2 * width)));
        int shaft = size - head;
        int X(int x) => reverse ? size - x : x;
        string path = FormattableString.Invariant(
            $"m{X(0)},2500 l{X(shaft)},2500,{X(shaft)},0,{X(size)},5000,{X(shaft)},10000,{X(shaft)},7500,{X(0)},7500,{X(head / 2)},5000 xe");
        return Render(new XElement("shape", new XAttribute("coordsize", "10000,10000")),
            path, width, height, fill, BColor.Empty, 0);
    }

    public static byte[]? Render(XElement shape, string path, double width, double height, BColor fill, BColor stroke, double strokeWidth)
    {
        if (path.Length > 65536 || !Pair((string?)shape.Attribute("coordsize"), out double spaceX, out double spaceY) ||
            spaceX <= 0 || spaceY <= 0)
            return null;

        if (!Pair((string?)shape.Attribute("coordorigin") ?? "0,0", out double originX, out double originY))
            return null;
        double scale = Math.Min(2, 1536 / Math.Max(width, height));
        int pixelsWide = Math.Max(1, (int)Math.Ceiling(width * scale));
        int pixelsHigh = Math.Max(1, (int)Math.Ceiling(height * scale));

        var contours = new List<PointF[]>();
        var points = new List<PointF>();
        double x = 0, y = 0;
        int count = 0;
        for (int index = 0; index < path.Length;)
        {
            char command = path[index++];
            if (char.IsWhiteSpace(command))
                continue;
            if (command == 'e')
                break;
            if (command == 'x')
            {
                if (points.Count > 0)
                    points.Add(points[0]);
                FinishContour();
                continue;
            }
            if (command is not ('m' or 'l' or 'r'))
                return null;

            int start = index;
            while (index < path.Length && !char.IsAsciiLetter(path[index]))
                index++;
            string coordinates = path[start..index].Trim();
            string[] values = coordinates.Contains(',')
                ? coordinates.Split(',', StringSplitOptions.TrimEntries)
                : coordinates.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length == 0 || values.Length % 2 != 0 || (command == 'm' && values.Length != 2))
                return null;

            if (command == 'm')
                FinishContour();
            else if (points.Count == 0)
                return null;

            for (int i = 0; i < values.Length; i += 2)
            {
                if (++count > 4096 || !Coordinate(values[i], out double a) || !Coordinate(values[i + 1], out double b))
                    return null;
                x = command == 'r' ? x + a : a;
                y = command == 'r' ? y + b : b;
                double px = (x - originX) * pixelsWide / spaceX;
                double py = (y - originY) * pixelsHigh / spaceY;
                if (!double.IsFinite(px) || !double.IsFinite(py) || Math.Abs(px) > 1e6 || Math.Abs(py) > 1e6)
                    return null;
                points.Add(new PointF((float)px, (float)py));
            }
        }

        FinishContour();
        if (contours.Count == 0)
            return null;

        using var bitmap = new BBitmap(pixelsWide, pixelsHigh);
        using (var canvas = bitmap.OpenCanvas())
        {
            if (!fill.IsEmpty)
                canvas.FillGlyphContours(contours, fill);
            if (!stroke.IsEmpty)
                foreach (PointF[] contour in contours)
                    canvas.DrawPathStroke(contour, stroke, (float)Math.Clamp(strokeWidth * scale, 0.5, 1536));
        }

        return EncodePng(bitmap);

        void FinishContour()
        {
            if (points.Count >= 2)
                contours.Add([.. points]);
            points.Clear();
        }
    }

    // The codec can run before the application's image catalog is composed.
    // Encode the generated RGBA pixels as a single, unfiltered PNG image.
    internal static byte[] EncodePng(BBitmap bitmap)
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
            uint crc = UpdateCrc(UpdateCrc(uint.MaxValue, type), bytes);
            BinaryPrimitives.WriteUInt32BigEndian(field, ~crc);
            png.Write(field);
        }
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320u;
        }

        return crc;
    }

    private static bool Pair(string? value, out double x, out double y)
    {
        x = y = 0;
        string[]? pair = value?.Split(',', StringSplitOptions.TrimEntries);
        return pair is { Length: 2 } && Coordinate(pair[0], out x) && Coordinate(pair[1], out y);
    }

    // An empty field between commas is an omitted zero in compact VML paths.
    private static bool Coordinate(string value, out double result)
    {
        result = 0;
        return value.Length == 0 || (double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out result) && double.IsFinite(result));
    }
}
