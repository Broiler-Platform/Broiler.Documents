using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Broiler.Graphics.Color;
using Broiler.Graphics.Imaging;

namespace Broiler.Documents.Resources;

/// <summary>
/// Rasterizes the plain-geometry subset of an SVG picture to a PNG: <c>path</c>,
/// <c>rect</c>, <c>circle</c>, <c>ellipse</c>, <c>line</c>, <c>polyline</c> and
/// <c>polygon</c>, in nested <c>g</c> and <c>svg</c> groups, with their
/// transforms, solid fills (nonzero) and solid strokes (butt, square or round
/// caps; miter, round or bevel joins), from presentation attributes or a
/// <c>style</c> attribute. The picture is the size its <c>width</c> and
/// <c>height</c> state, else its <c>viewBox</c>'s, and the view box is fitted
/// into it as <c>preserveAspectRatio</c> says.
/// </summary>
/// <remarks>
/// The rasterizer is strict: anything it cannot draw faithfully - text, embedded
/// images, <c>use</c>, a gradient or pattern paint, a filter, mask or clip path,
/// a dashed stroke, an even-odd fill of more than one contour, a stylesheet -
/// makes it return null, so a raster fallback beside the SVG (an ODF frame
/// usually carries one) is drawn instead of a picture with parts missing.
/// Metadata, descriptions, definitions nothing refers to, and Inkscape's
/// <c>flowRoot</c> text (which LibreOffice does not draw either) are skipped.
/// </remarks>
internal static class SvgRasterizer
{
    private const int MaxElements = 20_000;
    private const int MaxPoints = 1_000_000;
    private const int MaxPixels = 1024;
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    private sealed class UnsupportedException : Exception;

    private readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
    {
        public static readonly Matrix Identity = new(1, 0, 0, 1, 0, 0);

        public Matrix Then(Matrix outer) => new(
            A * outer.A + B * outer.C, A * outer.B + B * outer.D,
            C * outer.A + D * outer.C, C * outer.B + D * outer.D,
            E * outer.A + F * outer.C + outer.E, E * outer.B + F * outer.D + outer.F);

        public PointF Apply(double x, double y) => new((float)(A * x + C * y + E), (float)(B * x + D * y + F));
    }

    private sealed record Paint(BColor? Fill, BColor? Stroke, double StrokeWidth, double FillOpacity, double StrokeOpacity,
        bool EvenOdd, string LineCap, string LineJoin, double MiterLimit, BColor Color, bool Visible, double Opacity = 1);

    private readonly record struct Contour(List<(double X, double Y)> Points, bool Closed);

    private sealed record Op(PointF[][] Contours, BColor Color);

    /// <summary>True when the data looks like an SVG document: an XML text whose root is <c>svg</c>.</summary>
    public static bool IsSvg(ReadOnlySpan<byte> data)
    {
        string head = System.Text.Encoding.UTF8.GetString(data[..Math.Min(data.Length, 4096)]).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return head.StartsWith('<') && head.Contains("<svg", StringComparison.Ordinal);
    }

    /// <summary>
    /// True for a well-formed SVG that draws nothing at all: no shape, text or
    /// image anywhere in it, as tdf128820.fodt's <c>&lt;svg xmlns=.../&gt;</c>.
    /// LibreOffice draws such a picture as nothing.
    /// </summary>
    public static bool IsBlank(ReadOnlySpan<byte> data)
    {
        if (!IsSvg(data) || Load(data) is not XElement root || root.Name != Svg + "svg")
            return false;
        foreach (XElement element in root.Descendants())
        {
            if (element.Name.Namespace == Svg &&
                element.Name.LocalName is not ("g" or "defs" or "title" or "desc" or "metadata" or "style"))
                return false;
        }
        return true;
    }

    public static byte[]? RenderPng(ReadOnlySpan<byte> data)
    {
        if (!IsSvg(data) || Load(data) is not XElement root || root.Name != Svg + "svg")
            return null;
        // A stylesheet can paint by class or selector, which this subset does not follow.
        if (root.Descendants(Svg + "style").Any())
            return null;
        try
        {
            return Render(root);
        }
        catch (UnsupportedException)
        {
            return null;
        }
    }

    private static XElement? Load(ReadOnlySpan<byte> data)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = 64L * 1024 * 1024,
            };
            using var stream = new MemoryStream(data.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, settings);
            return XDocument.Load(reader).Root;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static byte[]? Render(XElement root)
    {
        var viewBox = ViewBox(root);
        double width = Length(root, "width") ?? viewBox?.Width ?? 0;
        double height = Length(root, "height") ?? viewBox?.Height ?? 0;
        if (!(width > 0) || !(height > 0) || !double.IsFinite(width) || !double.IsFinite(height))
            return null;

        double scale = MaxPixels / Math.Max(width, height);
        int pixelsWide = Math.Max(1, (int)Math.Ceiling(width * scale));
        int pixelsHigh = Math.Max(1, (int)Math.Ceiling(height * scale));
        Matrix device = new(pixelsWide / width, 0, 0, pixelsHigh / height, 0, 0);
        Matrix rootMatrix = ViewBoxMatrix(root, viewBox, width, height).Then(device);

        var paint = new Paint(new BColor(0, 0, 0), null, 1, 1, 1, false, "butt", "miter", 4, new BColor(0, 0, 0), true);
        var ops = new List<Op>();
        int elements = 0, points = 0;
        Walk(root, rootMatrix, paint, ops, ref elements, ref points, isRoot: true);
        if (ops.Count == 0)
            return null;

        using var bitmap = new BBitmap(pixelsWide, pixelsHigh);
        using (var canvas = bitmap.OpenCanvas())
        {
            foreach (Op op in ops)
                canvas.FillGlyphContours(op.Contours, op.Color);
        }
        return EmfRasterizer.EncodePng(bitmap);
    }

    private static void Walk(XElement element, Matrix parent, Paint inherited, List<Op> ops, ref int elements, ref int points, bool isRoot = false)
    {
        if (++elements > MaxElements)
            throw new UnsupportedException();

        if (element.Name.Namespace != Svg)
            return;

        Dictionary<string, string> style = Style(element);
        if (style.TryGetValue("display", out string? display) && display == "none")
            return;
        if (style.ContainsKey("filter") || style.ContainsKey("mask") || style.ContainsKey("clip-path"))
        {
            if (!IsNone(style.GetValueOrDefault("filter")) || !IsNone(style.GetValueOrDefault("mask")) ||
                !IsNone(style.GetValueOrDefault("clip-path")))
                throw new UnsupportedException();
        }

        Paint paint = Inherit(inherited, style);
        Matrix matrix = isRoot ? parent : Transform((string?)element.Attribute("transform")).Then(parent);

        switch (element.Name.LocalName)
        {
            case "svg" when !isRoot:
            {
                double x = Coordinate(element, "x"), y = Coordinate(element, "y");
                var box = ViewBox(element);
                double width = Length(element, "width") ?? box?.Width ?? 0;
                double height = Length(element, "height") ?? box?.Height ?? 0;
                if (!(width > 0) || !(height > 0))
                    return;
                Matrix inner = ViewBoxMatrix(element, box, width, height).Then(new Matrix(1, 0, 0, 1, x, y)).Then(matrix);
                foreach (XElement child in element.Elements())
                    Walk(child, inner, paint, ops, ref elements, ref points);
                return;
            }
            case "svg":
            case "g":
            case "a":
                foreach (XElement child in element.Elements())
                    Walk(child, matrix, paint, ops, ref elements, ref points);
                return;
            case "defs":
            case "title":
            case "desc":
            case "metadata":
            case "symbol":
            case "linearGradient":
            case "radialGradient":
            case "pattern":
            case "clipPath":
            case "mask":
            case "filter":
            case "marker":
            case "flowRoot":
                // Definitions draw only where something refers to them, and a
                // reference makes the picture unsupported. LibreOffice's SVG
                // import has no flowed-text element, so Inkscape's flowRoot
                // draws nothing there either.
                return;
            case "path":
                Draw(PathData((string?)element.Attribute("d")), matrix, paint, ops, ref points);
                return;
            case "rect":
                Draw(Rect(element), matrix, paint, ops, ref points);
                return;
            case "circle":
            {
                double r = Coordinate(element, "r");
                Draw(Ellipse(Coordinate(element, "cx"), Coordinate(element, "cy"), r, r), matrix, paint, ops, ref points);
                return;
            }
            case "ellipse":
                Draw(Ellipse(Coordinate(element, "cx"), Coordinate(element, "cy"), Coordinate(element, "rx"), Coordinate(element, "ry")),
                    matrix, paint, ops, ref points);
                return;
            case "line":
                Draw([new([(Coordinate(element, "x1"), Coordinate(element, "y1")), (Coordinate(element, "x2"), Coordinate(element, "y2"))], false)],
                    matrix, paint with { Fill = null }, ops, ref points);
                return;
            case "polyline":
            case "polygon":
            {
                List<double> numbers = Numbers((string?)element.Attribute("points"));
                var list = new List<(double, double)>();
                for (int i = 0; i + 1 < numbers.Count; i += 2)
                    list.Add((numbers[i], numbers[i + 1]));
                if (list.Count >= 2)
                    Draw([new(list, element.Name.LocalName == "polygon")], matrix, paint, ops, ref points);
                return;
            }
            default:
                // text, image, use, switch, foreignObject, style and anything
                // else this subset does not draw.
                throw new UnsupportedException();
        }
    }

    private static bool IsNone(string? value) => value is null || value.Trim() == "none";

    private static void Draw(List<Contour> contours, Matrix matrix, Paint paint, List<Op> ops, ref int points)
    {
        if (!paint.Visible || contours.Count == 0)
            return;
        foreach (Contour contour in contours)
        {
            points += contour.Points.Count;
            if (points > MaxPoints)
                throw new UnsupportedException();
        }

        if (paint.Fill is BColor fill && paint.FillOpacity * paint.Opacity > 0)
        {
            var filled = new List<PointF[]>();
            foreach (Contour contour in contours)
            {
                if (contour.Points.Count >= 3)
                    filled.Add(contour.Points.ConvertAll(p => matrix.Apply(p.X, p.Y)).ToArray());
            }
            if (paint.EvenOdd && filled.Count > 1)
                throw new UnsupportedException();
            if (filled.Count > 0)
                ops.Add(new Op([.. filled], WithOpacity(fill, paint.FillOpacity * paint.Opacity)));
        }

        if (paint.Stroke is BColor stroke && paint.StrokeOpacity * paint.Opacity > 0 && paint.StrokeWidth > 0)
        {
            var outline = new List<PointF[]>();
            foreach (Contour contour in contours)
                StrokeContour(contour, paint, matrix, outline);
            if (outline.Count > 0)
                ops.Add(new Op([.. outline], WithOpacity(stroke, paint.StrokeOpacity * paint.Opacity)));
        }
    }

    private static BColor WithOpacity(BColor color, double opacity) =>
        opacity >= 1 ? color : BColor.FromArgb((int)Math.Round(color.A * Math.Clamp(opacity, 0, 1)), color.R, color.G, color.B);

    /// <summary>
    /// The outline of a stroked contour, built in user space and only then
    /// mapped to the device, so a stroke scales with its transform as SVG
    /// says: each segment is a quadrilateral, each join a wedge (miter, round
    /// or bevel) and each open end a cap. All pieces wind the same way, so a
    /// nonzero fill paints their union.
    /// </summary>
    private static void StrokeContour(Contour contour, Paint paint, Matrix matrix, List<PointF[]> outline)
    {
        var pts = new List<(double X, double Y)>(contour.Points.Count);
        foreach ((double X, double Y) p in contour.Points)
        {
            if (pts.Count == 0 || Math.Abs(pts[^1].X - p.X) > 1e-9 || Math.Abs(pts[^1].Y - p.Y) > 1e-9)
                pts.Add(p);
        }
        bool closed = contour.Closed;
        if (closed && pts.Count > 2 && Math.Abs(pts[0].X - pts[^1].X) < 1e-9 && Math.Abs(pts[0].Y - pts[^1].Y) < 1e-9)
            pts.RemoveAt(pts.Count - 1);
        double h = paint.StrokeWidth / 2;

        if (pts.Count == 1)
        {
            // A zero-length subpath draws only its round or square cap.
            if (paint.LineCap == "round")
                Add(Ellipse(pts[0].X, pts[0].Y, h, h)[0].Points);
            else if (paint.LineCap == "square")
                Add([(pts[0].X - h, pts[0].Y - h), (pts[0].X + h, pts[0].Y - h), (pts[0].X + h, pts[0].Y + h), (pts[0].X - h, pts[0].Y + h)]);
            return;
        }

        int segments = closed ? pts.Count : pts.Count - 1;
        for (int i = 0; i < segments; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            (double nx, double ny) = Normal(a, b, h);
            double ex = 0, ey = 0, sx = 0, sy = 0;
            if (!closed && paint.LineCap == "square")
            {
                (double dx, double dy) = (ny, -nx);
                if (i == 0)
                    (sx, sy) = (-dx, -dy);
                if (i == segments - 1)
                    (ex, ey) = (dx, dy);
            }
            Add([(a.X + nx + sx, a.Y + ny + sy), (b.X + nx + ex, b.Y + ny + ey), (b.X - nx + ex, b.Y - ny + ey), (a.X - nx + sx, a.Y - ny + sy)]);
        }

        if (!closed && paint.LineCap == "round")
        {
            Add(Ellipse(pts[0].X, pts[0].Y, h, h)[0].Points);
            Add(Ellipse(pts[^1].X, pts[^1].Y, h, h)[0].Points);
        }

        int firstJoin = closed ? 0 : 1;
        int lastJoin = closed ? pts.Count - 1 : pts.Count - 2;
        for (int i = firstJoin; i <= lastJoin; i++)
        {
            var prev = pts[(i - 1 + pts.Count) % pts.Count];
            var at = pts[i];
            var next = pts[(i + 1) % pts.Count];
            (double n1x, double n1y) = Normal(prev, at, h);
            (double n2x, double n2y) = Normal(at, next, h);
            double cross = (at.X - prev.X) * (next.Y - at.Y) - (at.Y - prev.Y) * (next.X - at.X);
            if (Math.Abs(cross) < 1e-12)
                continue;
            // The outer side of the turn is opposite the turn's direction.
            double side = cross > 0 ? -1 : 1;
            var o1 = (X: at.X + side * n1x, Y: at.Y + side * n1y);
            var o2 = (X: at.X + side * n2x, Y: at.Y + side * n2y);
            if (paint.LineJoin == "round")
            {
                Add(Ellipse(at.X, at.Y, h, h)[0].Points);
                continue;
            }
            Add([at, o1, o2]);
            if (paint.LineJoin == "bevel")
                continue;
            // Miter: the tip where the two offset edges meet, within the limit.
            double cosTheta = (n1x * n2x + n1y * n2y) / (h * h);
            double miterRatio = 1 / Math.Sqrt(Math.Max(1e-12, (1 + cosTheta) / 2));
            if (miterRatio > paint.MiterLimit)
                continue;
            double mx = n1x + n2x, my = n1y + n2y;
            double length = Math.Sqrt(mx * mx + my * my);
            if (length < 1e-12)
                continue;
            double reach = h * miterRatio;
            Add([o1, (at.X + side * mx / length * reach, at.Y + side * my / length * reach), o2, at]);
        }

        void Add(List<(double X, double Y)> polygon)
        {
            double area = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                var p = polygon[i];
                var q = polygon[(i + 1) % polygon.Count];
                area += p.X * q.Y - q.X * p.Y;
            }
            if (Math.Abs(area) < 1e-18)
                return;
            // One winding for every piece, measured after the transform, so
            // a reflecting transform cannot cancel pieces against each other.
            PointF[] mapped = polygon.ConvertAll(p => matrix.Apply(p.X, p.Y)).ToArray();
            double mappedArea = 0;
            for (int i = 0; i < mapped.Length; i++)
            {
                PointF p = mapped[i], q = mapped[(i + 1) % mapped.Length];
                mappedArea += (double)p.X * q.Y - (double)q.X * p.Y;
            }
            if (mappedArea < 0)
                Array.Reverse(mapped);
            outline.Add(mapped);
        }
    }

    private static (double X, double Y) Normal((double X, double Y) a, (double X, double Y) b, double h)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        return length < 1e-12 ? (0, 0) : (-dy / length * h, dx / length * h);
    }

    private static Paint Inherit(Paint parent, Dictionary<string, string> style)
    {
        Paint paint = parent;
        if (style.TryGetValue("color", out string? color) && ParseColor(color, parent.Color) is BColor current)
            paint = paint with { Color = current };
        if (style.TryGetValue("fill", out string? fill))
            paint = paint with { Fill = PaintColor(fill, paint.Color) };
        if (style.TryGetValue("stroke", out string? stroke))
            paint = paint with { Stroke = PaintColor(stroke, paint.Color) };
        if (style.TryGetValue("stroke-width", out string? width) && Number(width) is double strokeWidth)
            paint = paint with { StrokeWidth = strokeWidth };
        if (style.TryGetValue("fill-opacity", out string? fillOpacity) && Number(fillOpacity) is double fo)
            paint = paint with { FillOpacity = fo };
        if (style.TryGetValue("stroke-opacity", out string? strokeOpacity) && Number(strokeOpacity) is double so)
            paint = paint with { StrokeOpacity = so };
        if (style.TryGetValue("opacity", out string? opacity) && Number(opacity) is double o && o < 1)
            // Opacity is not inherited but composes down the tree: a group's
            // fades everything in it, whatever their own fill-opacity says.
            // The group is a layer; for the shapes side by side this subset
            // meets, fading each paint is the same.
            paint = paint with { Opacity = paint.Opacity * o };
        if (style.TryGetValue("fill-rule", out string? rule))
            paint = paint with { EvenOdd = rule.Trim() == "evenodd" };
        if (style.TryGetValue("stroke-linecap", out string? cap))
            paint = paint with { LineCap = cap.Trim() };
        if (style.TryGetValue("stroke-linejoin", out string? join))
            paint = paint with { LineJoin = join.Trim() };
        if (style.TryGetValue("stroke-miterlimit", out string? limit) && Number(limit) is double miter && miter >= 1)
            paint = paint with { MiterLimit = miter };
        if (style.TryGetValue("visibility", out string? visibility))
            paint = paint with { Visible = visibility.Trim() is not ("hidden" or "collapse") };
        if (style.TryGetValue("stroke-dasharray", out string? dashes) && !IsNone(dashes))
            throw new UnsupportedException();
        return paint;
    }

    private static BColor? PaintColor(string value, BColor current)
    {
        value = value.Trim();
        if (value == "none")
            return null;
        if (value.StartsWith("url(", StringComparison.Ordinal))
            throw new UnsupportedException();
        return ParseColor(value, current) ?? throw new UnsupportedException();
    }

    private static BColor? ParseColor(string value, BColor current)
    {
        value = value.Trim();
        if (value == "currentColor")
            return current;
        if (value.StartsWith('#'))
        {
            string hex = value[1..];
            if (hex.Length == 3)
                hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
            return hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)
                ? BColor.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF)
                : null;
        }
        if (value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(')'))
        {
            string[] parts = value[4..^1].Split(',');
            if (parts.Length != 3)
                return null;
            int[] channel = new int[3];
            for (int i = 0; i < 3; i++)
            {
                string part = parts[i].Trim();
                bool percent = part.EndsWith('%');
                if (!double.TryParse(percent ? part[..^1] : part, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    return null;
                channel[i] = (int)Math.Round(Math.Clamp(percent ? v * 2.55 : v, 0, 255));
            }
            return BColor.FromArgb(channel[0], channel[1], channel[2]);
        }
        return BColor.TryGetNamedColor(value, out BColor named) ? named : null;
    }

    /// <summary>Presentation attributes, then the <c>style</c> attribute's declarations over them.</summary>
    private static Dictionary<string, string> Style(XElement element)
    {
        var style = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XAttribute attribute in element.Attributes())
        {
            if (attribute.Name.Namespace == XNamespace.None && attribute.Name.LocalName is "fill" or "stroke" or "stroke-width" or
                "fill-opacity" or "stroke-opacity" or "opacity" or "fill-rule" or "stroke-linecap" or "stroke-linejoin" or
                "stroke-miterlimit" or "stroke-dasharray" or "visibility" or "display" or "color" or "filter" or "mask" or "clip-path")
                style[attribute.Name.LocalName] = attribute.Value;
        }
        if ((string?)element.Attribute("style") is string declarations)
        {
            foreach (string declaration in declarations.Split(';'))
            {
                int colon = declaration.IndexOf(':');
                if (colon > 0)
                    style[declaration[..colon].Trim()] = declaration[(colon + 1)..].Trim();
            }
        }
        return style;
    }

    private static (double X, double Y, double Width, double Height)? ViewBox(XElement element)
    {
        List<double> numbers = Numbers((string?)element.Attribute("viewBox"));
        return numbers.Count == 4 && numbers[2] > 0 && numbers[3] > 0 ? (numbers[0], numbers[1], numbers[2], numbers[3]) : null;
    }

    private static Matrix ViewBoxMatrix(XElement element, (double X, double Y, double Width, double Height)? viewBox, double width, double height)
    {
        if (viewBox is not { } box)
            return Matrix.Identity;
        string[] aspect = ((string?)element.Attribute("preserveAspectRatio") ?? "xMidYMid meet").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string align = aspect.Length > 0 ? aspect[0] : "xMidYMid";
        if (align == "defer" && aspect.Length > 1)
            align = aspect[1];
        double sx = width / box.Width, sy = height / box.Height;
        if (align == "none")
            return new Matrix(sx, 0, 0, sy, -box.X * sx, -box.Y * sy);
        bool slice = Array.IndexOf(aspect, "slice") >= 0;
        double s = slice ? Math.Max(sx, sy) : Math.Min(sx, sy);
        double tx = -box.X * s, ty = -box.Y * s;
        double extraX = width - box.Width * s, extraY = height - box.Height * s;
        if (align.Contains("xMid", StringComparison.Ordinal))
            tx += extraX / 2;
        else if (align.Contains("xMax", StringComparison.Ordinal))
            tx += extraX;
        if (align.Contains("YMid", StringComparison.Ordinal))
            ty += extraY / 2;
        else if (align.Contains("YMax", StringComparison.Ordinal))
            ty += extraY;
        return new Matrix(s, 0, 0, s, tx, ty);
    }

    private static Matrix Transform(string? value)
    {
        Matrix result = Matrix.Identity;
        if (string.IsNullOrWhiteSpace(value))
            return result;
        int index = 0;
        while (index < value.Length)
        {
            int open = value.IndexOf('(', index);
            if (open < 0)
                break;
            int close = value.IndexOf(')', open);
            if (close < 0)
                throw new UnsupportedException();
            string name = value[index..open].Trim(' ', ',', '\t', '\n', '\r');
            List<double> a = Numbers(value[(open + 1)..close]);
            Matrix m = name switch
            {
                "matrix" when a.Count == 6 => new Matrix(a[0], a[1], a[2], a[3], a[4], a[5]),
                "translate" when a.Count is 1 or 2 => new Matrix(1, 0, 0, 1, a[0], a.Count == 2 ? a[1] : 0),
                "scale" when a.Count is 1 or 2 => new Matrix(a[0], 0, 0, a.Count == 2 ? a[1] : a[0], 0, 0),
                "rotate" when a.Count is 1 or 3 => Rotate(a),
                "skewX" when a.Count == 1 => new Matrix(1, 0, Math.Tan(a[0] * Math.PI / 180), 1, 0, 0),
                "skewY" when a.Count == 1 => new Matrix(1, Math.Tan(a[0] * Math.PI / 180), 0, 1, 0, 0),
                _ => throw new UnsupportedException(),
            };
            // A list applies right to left: the last transform is innermost.
            result = m.Then(result);
            index = close + 1;
        }
        return result;

        static Matrix Rotate(List<double> a)
        {
            double r = a[0] * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
            var rotation = new Matrix(cos, sin, -sin, cos, 0, 0);
            if (a.Count == 1)
                return rotation;
            return new Matrix(1, 0, 0, 1, -a[1], -a[2]).Then(rotation).Then(new Matrix(1, 0, 0, 1, a[1], a[2]));
        }
    }

    private static double Coordinate(XElement element, string name) => Length(element, name) ?? 0;

    /// <summary>A length in user units (CSS pixels); null when absent, a percentage or unreadable.</summary>
    private static double? Length(XElement element, string name)
    {
        string? value = ((string?)element.Attribute(name))?.Trim();
        if (string.IsNullOrEmpty(value) || value.EndsWith('%'))
            return null;
        double unit = 1;
        foreach ((string suffix, double factor) in new[] { ("px", 1.0), ("pt", 96 / 72.0), ("pc", 16.0), ("mm", 96 / 25.4), ("cm", 96 / 2.54), ("in", 96.0) })
        {
            if (value.EndsWith(suffix, StringComparison.Ordinal))
            {
                value = value[..^suffix.Length];
                unit = factor;
                break;
            }
        }
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)
            ? number * unit
            : null;
    }

    private static double? Number(string value)
    {
        value = value.Trim();
        if (value.EndsWith("px", StringComparison.Ordinal))
            value = value[..^2];
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)
            ? number
            : null;
    }

    private static List<double> Numbers(string? value)
    {
        var numbers = new List<double>();
        if (value is null)
            return numbers;
        var reader = new PathReader(value);
        while (reader.TryNumber(out double number))
            numbers.Add(number);
        return numbers;
    }

    private static List<Contour> Rect(XElement element)
    {
        double x = Coordinate(element, "x"), y = Coordinate(element, "y");
        double w = Coordinate(element, "width"), h = Coordinate(element, "height");
        if (!(w > 0) || !(h > 0))
            return [];
        double? rxValue = Length(element, "rx"), ryValue = Length(element, "ry");
        double rx = Math.Min(rxValue ?? ryValue ?? 0, w / 2), ry = Math.Min(ryValue ?? rxValue ?? 0, h / 2);
        if (rx <= 0 || ry <= 0)
            return [new([(x, y), (x + w, y), (x + w, y + h), (x, y + h)], true)];
        var points = new List<(double, double)>();
        void Corner(double cx, double cy, double start)
        {
            for (int i = 0; i <= 8; i++)
            {
                double angle = (start + 90.0 * i / 8) * Math.PI / 180;
                points.Add((cx + rx * Math.Cos(angle), cy + ry * Math.Sin(angle)));
            }
        }
        Corner(x + w - rx, y + ry, 270);
        Corner(x + w - rx, y + h - ry, 0);
        Corner(x + rx, y + h - ry, 90);
        Corner(x + rx, y + ry, 180);
        return [new(points, true)];
    }

    private static List<Contour> Ellipse(double cx, double cy, double rx, double ry)
    {
        if (!(rx > 0) || !(ry > 0))
            return [];
        const int steps = 72;
        var points = new List<(double, double)>(steps);
        for (int i = 0; i < steps; i++)
        {
            double angle = 2 * Math.PI * i / steps;
            points.Add((cx + rx * Math.Cos(angle), cy + ry * Math.Sin(angle)));
        }
        return [new(points, true)];
    }

    /// <summary>Parses SVG path data into flattened contours, curves cut into short lines.</summary>
    private static List<Contour> PathData(string? data)
    {
        var contours = new List<Contour>();
        if (string.IsNullOrWhiteSpace(data))
            return contours;
        var reader = new PathReader(data);
        List<(double X, double Y)>? current = null;
        double x = 0, y = 0, startX = 0, startY = 0;
        double lastControlX = 0, lastControlY = 0;
        char previous = ' ';
        char command = ' ';
        while (true)
        {
            if (reader.TryCommand(out char next))
                command = next;
            else if (command == ' ' || reader.AtEnd)
                break;
            else if (command is 'M')
                command = 'L';
            else if (command is 'm')
                command = 'l';
            else if (command is 'Z' or 'z')
                throw new UnsupportedException();

            bool relative = char.IsLower(command);
            double ox = relative ? x : 0, oy = relative ? y : 0;
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    (x, y) = (ox + Read(), oy + Read());
                    Close(false);
                    current = [(x, y)];
                    (startX, startY) = (x, y);
                    break;
                case 'L':
                    (x, y) = (ox + Read(), oy + Read());
                    LineTo(x, y);
                    break;
                case 'H':
                    x = ox + Read();
                    LineTo(x, y);
                    break;
                case 'V':
                    y = oy + Read();
                    LineTo(x, y);
                    break;
                case 'C':
                {
                    double x1 = ox + Read(), y1 = oy + Read(), x2 = ox + Read(), y2 = oy + Read();
                    double ex = ox + Read(), ey = oy + Read();
                    Cubic(x1, y1, x2, y2, ex, ey);
                    break;
                }
                case 'S':
                {
                    double x1 = char.ToUpperInvariant(previous) is 'C' or 'S' ? 2 * x - lastControlX : x;
                    double y1 = char.ToUpperInvariant(previous) is 'C' or 'S' ? 2 * y - lastControlY : y;
                    double x2 = ox + Read(), y2 = oy + Read(), ex = ox + Read(), ey = oy + Read();
                    Cubic(x1, y1, x2, y2, ex, ey);
                    break;
                }
                case 'Q':
                {
                    double qx = ox + Read(), qy = oy + Read(), ex = ox + Read(), ey = oy + Read();
                    Quadratic(qx, qy, ex, ey);
                    break;
                }
                case 'T':
                {
                    double qx = char.ToUpperInvariant(previous) is 'Q' or 'T' ? 2 * x - lastControlX : x;
                    double qy = char.ToUpperInvariant(previous) is 'Q' or 'T' ? 2 * y - lastControlY : y;
                    double ex = ox + Read(), ey = oy + Read();
                    Quadratic(qx, qy, ex, ey);
                    break;
                }
                case 'A':
                {
                    double rx = Math.Abs(Read()), ry = Math.Abs(Read()), rotation = Read();
                    bool large = reader.TryFlag(out bool l) ? l : throw new UnsupportedException();
                    bool sweep = reader.TryFlag(out bool s) ? s : throw new UnsupportedException();
                    double ex = ox + Read(), ey = oy + Read();
                    Arc(rx, ry, rotation, large, sweep, ex, ey);
                    break;
                }
                case 'Z':
                    if (current is not null)
                    {
                        Close(true);
                        current = null;
                    }
                    (x, y) = (startX, startY);
                    break;
                default:
                    throw new UnsupportedException();
            }
            previous = command;
        }
        Close(false);
        return contours;

        double Read() => reader.TryNumber(out double value) ? value : throw new UnsupportedException();

        void Close(bool closed)
        {
            if (current is { Count: >= 1 })
                contours.Add(new Contour(current, closed));
            current = null;
        }

        void LineTo(double px, double py)
        {
            current ??= [(startX, startY)];
            current.Add((px, py));
        }

        void Cubic(double x1, double y1, double x2, double y2, double ex, double ey)
        {
            current ??= [(startX, startY)];
            const int steps = 16;
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps, u = 1 - t;
                current.Add((u * u * u * x + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * ex,
                    u * u * u * y + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * ey));
            }
            (lastControlX, lastControlY, x, y) = (x2, y2, ex, ey);
        }

        void Quadratic(double qx, double qy, double ex, double ey)
        {
            current ??= [(startX, startY)];
            const int steps = 12;
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps, u = 1 - t;
                current.Add((u * u * x + 2 * u * t * qx + t * t * ex, u * u * y + 2 * u * t * qy + t * t * ey));
            }
            (lastControlX, lastControlY, x, y) = (qx, qy, ex, ey);
        }

        // SVG 1.1 appendix F.6.5: endpoint to centre parameterisation.
        void Arc(double rx, double ry, double rotationDegrees, bool large, bool sweep, double ex, double ey)
        {
            current ??= [(startX, startY)];
            if ((Math.Abs(ex - x) < 1e-12 && Math.Abs(ey - y) < 1e-12))
                return;
            if (rx < 1e-12 || ry < 1e-12)
            {
                current.Add((ex, ey));
                (x, y) = (ex, ey);
                return;
            }
            double phi = rotationDegrees * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
            double dx = (x - ex) / 2, dy = (y - ey) / 2;
            double x1 = cos * dx + sin * dy, y1 = -sin * dx + cos * dy;
            double lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
            if (lambda > 1)
            {
                double root = Math.Sqrt(lambda);
                rx *= root;
                ry *= root;
            }
            double numerator = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
            double denominator = rx * rx * y1 * y1 + ry * ry * x1 * x1;
            double factor = Math.Sqrt(Math.Max(0, numerator / denominator)) * (large == sweep ? -1 : 1);
            double cx1 = factor * rx * y1 / ry, cy1 = -factor * ry * x1 / rx;
            double cx = cos * cx1 - sin * cy1 + (x + ex) / 2, cy = sin * cx1 + cos * cy1 + (y + ey) / 2;
            double start = Math.Atan2((y1 - cy1) / ry, (x1 - cx1) / rx);
            double end = Math.Atan2((-y1 - cy1) / ry, (-x1 - cx1) / rx);
            double delta = end - start;
            if (sweep && delta < 0)
                delta += 2 * Math.PI;
            else if (!sweep && delta > 0)
                delta -= 2 * Math.PI;
            int steps = Math.Max(4, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 24)));
            for (int i = 1; i <= steps; i++)
            {
                double angle = start + delta * i / steps;
                double px = rx * Math.Cos(angle), py = ry * Math.Sin(angle);
                current.Add((cos * px - sin * py + cx, sin * px + cos * py + cy));
            }
            current[^1] = (ex, ey);
            (x, y) = (ex, ey);
        }
    }

    /// <summary>Reads the numbers, flags and command letters of SVG path data and point lists.</summary>
    private sealed class PathReader(string text)
    {
        private int _index;

        public bool AtEnd
        {
            get
            {
                SkipSeparators();
                return _index >= text.Length;
            }
        }

        public bool TryCommand(out char command)
        {
            SkipSeparators();
            if (_index < text.Length && char.IsAsciiLetter(text[_index]) && text[_index] is not ('e' or 'E'))
            {
                command = text[_index++];
                return true;
            }
            command = ' ';
            return false;
        }

        public bool TryFlag(out bool flag)
        {
            SkipSeparators();
            if (_index < text.Length && text[_index] is '0' or '1')
            {
                flag = text[_index++] == '1';
                return true;
            }
            flag = false;
            return false;
        }

        public bool TryNumber(out double number)
        {
            SkipSeparators();
            int start = _index;
            if (_index < text.Length && text[_index] is '+' or '-')
                _index++;
            bool digits = false, dot = false;
            while (_index < text.Length)
            {
                char c = text[_index];
                if (char.IsAsciiDigit(c))
                    digits = true;
                else if (c == '.' && !dot)
                    dot = true;
                else
                    break;
                _index++;
            }
            if (digits && _index < text.Length && text[_index] is 'e' or 'E')
            {
                int mark = _index++;
                if (_index < text.Length && text[_index] is '+' or '-')
                    _index++;
                if (_index < text.Length && char.IsAsciiDigit(text[_index]))
                {
                    while (_index < text.Length && char.IsAsciiDigit(text[_index]))
                        _index++;
                }
                else
                {
                    _index = mark;
                }
            }
            if (!digits)
            {
                _index = start;
                number = 0;
                return false;
            }
            return double.TryParse(text.AsSpan(start, _index - start), NumberStyles.Float, CultureInfo.InvariantCulture, out number) &&
                double.IsFinite(number);
        }

        private void SkipSeparators()
        {
            while (_index < text.Length && (char.IsWhiteSpace(text[_index]) || text[_index] == ','))
                _index++;
        }
    }
}
