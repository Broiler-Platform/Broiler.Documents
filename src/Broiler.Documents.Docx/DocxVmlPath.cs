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
        // A preset's path names its guides (@n) and adjust values (#n).
        double[]? guides = null;
        double[] adjust = [];
        if (path.Contains('@') || path.Contains('#'))
        {
            adjust = Adjustments((string?)shape.Attribute("adj"));
            guides = EvaluateFormulas(shape, adjust, originX, originY, spaceX, spaceY);
            if (guides is null)
                return null;
        }
        bool Parameter(string value, out double result) => Resolve(value, guides, adjust, out result);
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
            if (!arc && name is not ("m" or "l" or "r"))
                return null;

            int start = index;
            while (index < path.Length && !char.IsAsciiLetter(path[index]))
                index++;
            string[] values = Values(path[start..index].Trim());
            if (values.Length == 0 || values.Length % (arc ? 8 : 2) != 0 || (name == "m" && values.Length != 2))
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
                        if (!Parameter(values[i + k], out v[k]))
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

            for (int i = 0; i < values.Length; i += 2)
            {
                if (!Parameter(values[i], out double a) || !Parameter(values[i + 1], out double b))
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
            float penWidth = (float)Math.Clamp(strokeWidth * scale, 0.5, 1536);
            // A preset connector runs along its box's edges (an elbow's two
            // arms, fdo73215); kept whole inside the picture, the stroke is
            // not halved at its edge.
            float inset = guides is null ? 0 : Math.Min(penWidth / 2, Math.Min(pixelsWide, pixelsHigh) / 2f);
            if (!stroke.IsEmpty)
                foreach (var contour in contours.Where(c => c.Stroke))
                    canvas.DrawPathStroke(inset == 0 ? contour.Points : [.. contour.Points.Select(point => new PointF(
                        Math.Clamp(point.X, inset, pixelsWide - inset), Math.Clamp(point.Y, inset, pixelsHigh - inset)))],
                        stroke, penWidth);
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
            // A preset whose guides carry it outside its box (an elbow
            // connector's or a callout's leader, fdo73215) would be cut off
            // at the picture's edge; leave such a shape to its box.
            if (guides is not null && (px < -0.05 * pixelsWide || px > 1.05 * pixelsWide ||
                py < -0.05 * pixelsHigh || py > 1.05 * pixelsHigh))
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

    /// <summary>
    /// A command's parameters: separated by commas or white space, and a guide
    /// or adjust reference starts a new one even with no separator
    /// (<c>l@3@2</c> is two values).
    /// </summary>
    internal static string[] Values(string coordinates)
    {
        string[] fields = coordinates.Contains(',')
            ? coordinates.Split(',', StringSplitOptions.TrimEntries)
            : coordinates.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (!fields.Any(field => field.Length > 1 && field.IndexOfAny(['@', '#'], 1) > 0))
            return fields;
        var values = new List<string>();
        foreach (string field in fields)
        {
            if (field.Length == 0)
            {
                values.Add(field);
                continue;
            }
            int begin = 0;
            for (int i = 1; i <= field.Length; i++)
            {
                if (i < field.Length && field[i] is not ('@' or '#'))
                    continue;
                values.Add(field[begin..i].Trim());
                begin = i;
            }
        }
        return [.. values];
    }

    /// <summary>A shape's adjust values (<c>adj</c>); an omitted one is zero.</summary>
    private static double[] Adjustments(string? adj)
    {
        if (string.IsNullOrWhiteSpace(adj))
            return [];
        string[] fields = adj.Split(',', StringSplitOptions.TrimEntries);
        var values = new double[Math.Min(fields.Length, 10)];
        for (int i = 0; i < values.Length; i++)
            if (!Coordinate(fields[i], out values[i]))
                values[i] = 0;
        return values;
    }

    /// <summary>A path parameter: a number, a guide (<c>@n</c>) or an adjust value (<c>#n</c>).</summary>
    private static bool Resolve(string value, double[]? guides, ReadOnlySpan<double> adjust, out double result)
    {
        result = 0;
        if (value.Length > 1 && value[0] is '@' or '#')
        {
            if (!int.TryParse(value.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                return false;
            if (value[0] == '#')
            {
                result = index < adjust.Length ? adjust[index] : 0;
                return index < 10;
            }
            if (guides is null || index >= guides.Length)
                return false;
            result = guides[index];
            return true;
        }
        return Coordinate(value, out result);
    }

    /// <summary>
    /// Evaluates a shape's <c>v:formulas</c> in order, each <c>v:f eqn</c> one
    /// of the VML guide operations (val, sum, prod, mid, abs, min, max, if,
    /// mod, sqrt, sin, cos, tan, atan2, cosatan2, sinatan2, sumangle, ellipse)
    /// on numbers, adjust values, earlier guides and the shape's coordinate
    /// space (width, height, xcenter, ycenter, ...). Angles are in 1/65536
    /// degrees. Returns null when a formula cannot be read, so the caller falls
    /// back on the box. Device measures (pixelWidth, emuWidth, ...) are not
    /// known here: the picture frame (o:spt 75) that names them stays a box,
    /// whose outline is drawn whole rather than clipped at the image edge.
    /// </summary>
    internal static double[]? EvaluateFormulas(XElement shape, double[] adjust, double originX, double originY, double spaceX, double spaceY)
    {
        var formulas = shape.Element(DocxNamespaces.Vml + "formulas")?.Elements(DocxNamespaces.Vml + "f").Take(128).ToList() ?? [];
        var guides = new double[formulas.Count];
        const double Degree = 65536;
        for (int index = 0; index < formulas.Count; index++)
        {
            string[] parts = ((string?)formulas[index].Attribute("eqn") ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return null;
            var args = new double[3];
            double[] earlier = guides[..index];
            for (int i = 1; i < parts.Length && i <= 3; i++)
            {
                double? named = parts[i] switch
                {
                    "width" or "xrange" or "xlimo" => spaceX,
                    "height" or "yrange" or "ylimo" => spaceY,
                    "xcenter" => originX + spaceX / 2,
                    "ycenter" => originY + spaceY / 2,
                    "hasstroke" or "hasfill" or "lineDrawn" => 1,
                    _ => null,
                };
                if (named is double known)
                    args[i - 1] = known;
                else if (!Resolve(parts[i], earlier, adjust, out args[i - 1]))
                    return null;
            }
            double a = args[0], b = args[1], c = args[2];
            static double Radians(double angle) => angle / Degree * Math.PI / 180;
            double value = parts[0] switch
            {
                "val" => a,
                "sum" => a + b - c,
                "prod" or "product" => c == 0 ? 0 : a * b / c,
                "mid" => (a + b) / 2,
                "abs" => Math.Abs(a),
                "min" => Math.Min(a, b),
                "max" => Math.Max(a, b),
                "if" => a > 0 ? b : c,
                "mod" => Math.Sqrt(a * a + b * b + c * c),
                "sqrt" => Math.Sqrt(Math.Max(0, a)),
                "sin" => a * Math.Sin(Radians(b)),
                "cos" => a * Math.Cos(Radians(b)),
                "tan" => a * Math.Tan(Radians(b)),
                "atan2" => Math.Atan2(b, a) * 180 / Math.PI * Degree,
                "cosatan2" => a * Math.Cos(Math.Atan2(c, b)),
                "sinatan2" => a * Math.Sin(Math.Atan2(c, b)),
                "sumangle" => a + b * Degree - c * Degree,
                "ellipse" => b == 0 ? 0 : c * Math.Sqrt(Math.Max(0, 1 - a / b * (a / b))),
                _ => double.NaN,
            };
            if (!double.IsFinite(value) || Math.Abs(value) > 1e9)
                return null;
            guides[index] = value;
        }
        return guides;
    }
}
