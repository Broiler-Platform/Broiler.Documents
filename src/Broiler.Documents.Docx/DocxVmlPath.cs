using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
        // The built-in isosceles triangle's first guide is its adjustable apex.
        if (path == "m@0,l,21600r21600,xe" &&
            Coordinate((string?)shape.Attribute("adj") ?? "10800", out double apex))
        {
            path = FormattableString.Invariant($"m{Math.Clamp(apex, 0, 21600)},0 l0,21600,21600,21600 xe");
        }
        if (path.Length > 65536 || !Pair((string?)shape.Attribute("coordsize"), out double spaceX, out double spaceY) ||
            spaceX <= 0 || spaceY <= 0)
            return null;

        if (!Pair((string?)shape.Attribute("coordorigin") ?? "0,0", out double originX, out double originY))
            return null;
        double scale = Math.Min(2, 1536 / Math.Max(width, height));
        int pixelsWide = Math.Max(1, (int)Math.Ceiling(width * scale));
        int pixelsHigh = Math.Max(1, (int)Math.Ceiling(height * scale));

        // VML's flip mirrors the path inside its box (tdf104115's arc is flip:y).
        string flip = DocxVml.ReadStyle((string?)shape.Attribute("style")).GetValueOrDefault("flip") ?? "";
        bool flipX = flip.Contains('x'), flipY = flip.Contains('y');

        // Each contour remembers whether its subpath is filled and stroked:
        // nf and ns switch either off until the subpath's e.
        var contours = new List<(PointF[] Points, bool Fill, bool Stroke)>();
        var points = new List<PointF>();
        double x = 0, y = 0;
        int count = 0;
        bool fillOn = true, strokeOn = true;
        for (int index = 0; index < path.Length;)
        {
            char command = path[index++];
            if (char.IsWhiteSpace(command))
                continue;
            string name = command.ToString();
            if (index < path.Length && (command, path[index]) is ('n', 'f' or 's') or ('w', 'a' or 'r') or ('a', 't' or 'r'))
                name += path[index++];
            if (name == "e")
            {
                FinishContour();
                fillOn = strokeOn = true;
                continue;
            }
            if (name is "nf" or "ns")
            {
                if (name == "nf") fillOn = false; else strokeOn = false;
                continue;
            }
            if (name == "x")
            {
                if (points.Count > 0)
                    points.Add(points[0]);
                FinishContour();
                continue;
            }
            bool arc = name is "wa" or "wr" or "at" or "ar";
            bool curve = name is "c" or "v";
            if (!arc && !curve && name is not ("m" or "l" or "r"))
                return null;

            int start = index;
            while (index < path.Length && !char.IsAsciiLetter(path[index]))
                index++;
            string coordinates = path[start..index].Trim();
            string[] values = coordinates.Contains(',')
                ? coordinates.Split(',', StringSplitOptions.TrimEntries)
                : coordinates.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length == 0 || values.Length % (arc ? 8 : curve ? 6 : 2) != 0 || (name == "m" && values.Length != 2))
                return null;

            if (name == "m")
                FinishContour();
            else if (points.Count == 0 && !arc)
                return null;

            if (arc)
            {
                for (int i = 0; i < values.Length; i += 8)
                {
                    var v = new double[8];
                    for (int k = 0; k < 8; k++)
                        if (!Coordinate(values[i + k], out v[k]))
                            return null;
                    // An arc of the ellipse in the box (v0,v1)-(v2,v3) from the ray
                    // through (v4,v5) to the ray through (v6,v7): wa and wr run
                    // clockwise, at and ar counter-clockwise; ar and wr start a
                    // new subpath where at and wa draw a line to the arc's start.
                    double cx = (v[0] + v[2]) / 2, cy = (v[1] + v[3]) / 2;
                    double rx = Math.Abs(v[2] - v[0]) / 2, ry = Math.Abs(v[3] - v[1]) / 2;
                    if (rx <= 0 || ry <= 0)
                        return null;
                    double from = Math.Atan2((v[5] - cy) / ry, (v[4] - cx) / rx);
                    double to = Math.Atan2((v[7] - cy) / ry, (v[6] - cx) / rx);
                    bool clockwise = name[0] == 'w';
                    double sweep = to - from;
                    if (clockwise && sweep <= 0) sweep += 2 * Math.PI;
                    if (!clockwise && sweep >= 0) sweep -= 2 * Math.PI;
                    if (name is "ar" or "wr")
                        FinishContour();
                    int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 64)));
                    for (int step = 0; step <= steps; step++)
                    {
                        double angle = from + sweep * step / steps;
                        if (!Add(cx + rx * Math.Cos(angle), cy + ry * Math.Sin(angle)))
                            return null;
                    }
                }
                continue;
            }

            if (curve)
            {
                // A cubic Bezier through two control points to an end point:
                // c states them absolutely, v relative to the current point
                // (FDO74774.docx's chart frames are rounded with them).
                for (int i = 0; i < values.Length; i += 6)
                {
                    var v = new double[6];
                    for (int k = 0; k < 6; k++)
                        if (!Coordinate(values[i + k], out v[k]))
                            return null;
                    double x0 = x, y0 = y;
                    if (name == "v")
                        for (int k = 0; k < 6; k += 2)
                            (v[k], v[k + 1]) = (v[k] + x0, v[k + 1] + y0);
                    const int steps = 12;
                    for (int step = 1; step <= steps; step++)
                    {
                        double t = (double)step / steps, u = 1 - t;
                        double bx = u * u * u * x0 + 3 * u * u * t * v[0] + 3 * u * t * t * v[2] + t * t * t * v[4];
                        double by = u * u * u * y0 + 3 * u * u * t * v[1] + 3 * u * t * t * v[3] + t * t * t * v[5];
                        if (!Add(bx, by))
                            return null;
                    }
                }
                continue;
            }

            for (int i = 0; i < values.Length; i += 2)
            {
                if (!Coordinate(values[i], out double a) || !Coordinate(values[i + 1], out double b))
                    return null;
                if (!Add(name == "r" ? x + a : a, name == "r" ? y + b : b))
                    return null;
            }
        }

        FinishContour();
        if (contours.Count == 0)
            return null;

        using var bitmap = new BBitmap(pixelsWide, pixelsHigh);
        using (var canvas = bitmap.OpenCanvas())
        {
            var filled = contours.Where(c => c.Fill).Select(c => c.Points).ToList();
            if (!fill.IsEmpty && filled.Count > 0)
                canvas.FillGlyphContours(filled, fill);
            if (!stroke.IsEmpty)
                foreach (var contour in contours.Where(c => c.Stroke))
                    canvas.DrawPathStroke(contour.Points, stroke, (float)Math.Clamp(strokeWidth * scale, 0.5, 1536));
        }

        return EncodePng(bitmap);

        void FinishContour()
        {
            if (points.Count >= 2)
                contours.Add(([.. points], fillOn, strokeOn));
            points.Clear();
        }

        bool Add(double nextX, double nextY)
        {
            if (++count > 4096)
                return false;
            (x, y) = (nextX, nextY);
            double px = (x - originX) * pixelsWide / spaceX;
            double py = (y - originY) * pixelsHigh / spaceY;
            if (flipX) px = pixelsWide - px;
            if (flipY) py = pixelsHigh - py;
            if (!double.IsFinite(px) || !double.IsFinite(py) || Math.Abs(px) > 1e6 || Math.Abs(py) > 1e6)
                return false;
            points.Add(new PointF((float)px, (float)py));
            return true;
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
