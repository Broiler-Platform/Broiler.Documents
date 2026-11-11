using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using Broiler.Graphics.Color;
using Broiler.Graphics.Imaging;

namespace Broiler.Documents.Resources;

/// <summary>
/// Rasterizes the vector subset of a StarView Metafile (<c>VCLMTF</c>, LibreOffice's
/// own GDI metafile, which it stores as the replacement picture of a chart or other
/// embedded object under <c>ObjectReplacements/</c>) to a PNG: filled and stroked
/// rectangles, polygons and poly-polygons, and polylines, in their line and fill
/// colours, under the header's and each MAPMODE action's map mode, absolute or
/// relative. Text and bitmaps are not drawn and clip regions are ignored. Returns null
/// when the metafile draws nothing this subset understands.
/// </summary>
/// <remarks>
/// Every action is a <c>u16</c> type followed by a version-compat header (<c>u16</c>
/// version, <c>u32</c> length) and that many bytes of payload, so an action this
/// subset does not read is skipped whole by its stated length.
/// </remarks>
internal static class SvmRasterizer
{
    private const int MaxActions = 200_000;
    private const int MaxPoints = 1_000_000;

    private const ushort ActionRect = 103;
    private const ushort ActionPolyLine = 109;
    private const ushort ActionPolygon = 110;
    private const ushort ActionPolyPolygon = 111;
    private const ushort ActionLineColor = 132;
    private const ushort ActionFillColor = 133;
    private const ushort ActionMapMode = 137;
    private const ushort ActionPush = 139;
    private const ushort ActionPop = 140;

    private const ushort PushLineColor = 0x1;
    private const ushort PushFillColor = 0x2;
    private const ushort PushMapMode = 0x10;

    /// <summary>
    /// A map mode as the affine map it makes, logical units to 0.01 mm on each
    /// axis: <c>x * ScaleX + OffsetX</c>. VCL lands a logical point p at
    /// <c>(p + origin) * scale * unit</c>.
    /// </summary>
    private readonly record struct MapMode(double ScaleX, double OffsetX, double ScaleY, double OffsetY)
    {
        public double X(double x) => x * ScaleX + OffsetX;

        public double Y(double y) => y * ScaleY + OffsetY;

        public static MapMode Absolute(double unit, double originX, double originY, double scaleX, double scaleY) =>
            new(scaleX * unit, originX * scaleX * unit, scaleY * unit, originY * scaleY * unit);

        /// <summary>
        /// A MapRelative map mode: <c>(p + origin) * scale</c> is a point in the
        /// current map mode's logical units, as VCL's relative map resolution composes it.
        /// </summary>
        public MapMode Relative(double originX, double originY, double scaleX, double scaleY) =>
            new(ScaleX * scaleX, ScaleX * originX * scaleX + OffsetX, ScaleY * scaleY, ScaleY * originY * scaleY + OffsetY);
    }

    private sealed record State(BColor Line, BColor Fill, MapMode Map, ushort Flags);

    private sealed record Op(PointF[][] Contours, BColor Color, float StrokeWidth);

    public static bool IsSvm(ReadOnlySpan<byte> data) =>
        data.Length >= 12 && data[..6].SequenceEqual("VCLMTF"u8);

    public static byte[]? RenderPng(ReadOnlySpan<byte> data)
    {
        if (!IsSvm(data))
            return null;

        // Header: "VCLMTF", a version-compat block holding the compression mode,
        // the preferred map mode and size, and the action count.
        var header = new Reader(data[6..]);
        header.U16();
        int headerLength = (int)Math.Min(header.U32(), int.MaxValue);
        header.U32(); // compression mode
        MapMode? headerMap = ReadMapMode(ref header, current: null);
        int prefWidth = header.I32(), prefHeight = header.I32();
        uint actionCount = header.U32();
        if (!header.Ok || headerMap is not MapMode map || headerLength > data.Length - 12)
            return null;

        double frameWidth = Math.Abs(prefWidth * map.ScaleX);
        double frameHeight = Math.Abs(prefHeight * map.ScaleY);
        if (!(frameWidth > 0) || !(frameHeight > 0) || !double.IsFinite(frameWidth) || !double.IsFinite(frameHeight))
            return null;
        double widthPt = frameWidth * 72 / 2540, heightPt = frameHeight * 72 / 2540;
        double scale = Math.Min(2, 1536 / Math.Max(widthPt, heightPt));
        int pixelsWide = Math.Max(1, (int)Math.Ceiling(widthPt * scale));
        int pixelsHigh = Math.Max(1, (int)Math.Ceiling(heightPt * scale));
        double sx = pixelsWide / frameWidth, sy = pixelsHigh / frameHeight;
        // A hairline is one device pixel wherever the metafile is played; drawn at
        // a point here, it still shows once the picture is scaled down onto a page.
        double hairline = scale;

        var line = new BColor(0, 0, 0);
        var fill = new BColor(255, 255, 255);
        var saved = new Stack<State>();
        var ops = new List<Op>();
        int totalPoints = 0;

        PointF Map(int x, int y) => new((float)(map.X(x) * sx), (float)(map.Y(y) * sy));

        void Emit(PointF[][] contours, bool closed, float strokeWidth)
        {
            if (contours.Length == 0)
                return;
            if (closed && !fill.IsEmpty)
                ops.Add(new Op(contours, fill, 0));
            if (!line.IsEmpty)
            {
                PointF[][] outline = closed
                    ? Array.ConvertAll(contours, contour => contour.Length > 0 && contour[0] != contour[^1] ? [.. contour, contour[0]] : contour)
                    : contours;
                ops.Add(new Op(outline, line, strokeWidth));
            }
        }

        // Reads the point list tools::Polygon streams: a u16 count, then i32 pairs.
        PointF[]? Polygon(ref Reader r)
        {
            int count = r.U16();
            if (!r.Ok || count > r.Remaining / 8 || (totalPoints += count) > MaxPoints)
                return null;
            var points = new PointF[count];
            for (int i = 0; i < count; i++)
                points[i] = Map(r.I32(), r.I32());
            return points;
        }

        // A version-compat polygon with optional per-point flags; control points
        // become a flattened cubic Bezier.
        PointF[]? FlaggedPolygon(ref Reader r)
        {
            r.U16();
            r.U32();
            PointF[]? points = Polygon(ref r);
            if (points is null || r.U8() == 0 || r.Remaining < points.Length)
                return points;
            ReadOnlySpan<byte> flags = r.Bytes(points.Length);
            var flat = new List<PointF>(points.Length);
            for (int i = 0; i < points.Length; i++)
            {
                if (flags[i] == 2 && i + 2 < points.Length && flat.Count > 0)
                {
                    PointF p0 = flat[^1], c1 = points[i], c2 = points[i + 1], p3 = points[i + 2];
                    const int Steps = 12;
                    for (int step = 1; step <= Steps; step++)
                    {
                        float t = step / (float)Steps, u = 1 - t;
                        float a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
                        flat.Add(new PointF(
                            a * p0.X + b * c1.X + c * c2.X + d * p3.X,
                            a * p0.Y + b * c1.Y + c * c2.Y + d * p3.Y));
                    }
                    i += 2;
                }
                else
                {
                    flat.Add(points[i]);
                }
            }
            return [.. flat];
        }

        float LineWidth(int logicalWidth)
        {
            double width = Math.Max(Math.Abs(logicalWidth * map.ScaleX * sx), hairline);
            return (float)Math.Clamp(width, 1, 64);
        }

        int offset = 6 + 6 + headerLength;
        for (uint action = 0; action < actionCount && action < MaxActions && offset + 8 <= data.Length; action++)
        {
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 2)..]);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
            if (length > (uint)(data.Length - offset - 8))
                break;
            var r = new Reader(data.Slice(offset + 8, (int)length));
            offset += 8 + (int)length;

            switch (type)
            {
                case ActionLineColor or ActionFillColor:
                {
                    uint value = r.U32();
                    bool set = r.U8() != 0;
                    if (!r.Ok)
                        break;
                    BColor color = set ? ColorOf(value) : BColor.Empty;
                    if (type == ActionLineColor)
                        line = color;
                    else
                        fill = color;
                    break;
                }
                case ActionMapMode:
                    if (ReadMapMode(ref r, map) is MapMode mode)
                        map = mode;
                    break;
                case ActionPush:
                    saved.Push(new State(line, fill, map, r.U16()));
                    break;
                case ActionPop:
                    if (saved.Count > 0)
                    {
                        State state = saved.Pop();
                        if ((state.Flags & PushLineColor) != 0)
                            line = state.Line;
                        if ((state.Flags & PushFillColor) != 0)
                            fill = state.Fill;
                        if ((state.Flags & PushMapMode) != 0)
                            map = state.Map;
                    }
                    break;
                case ActionRect:
                {
                    int left = r.I32(), top = r.I32(), right = r.I32(), bottom = r.I32();
                    if (!r.Ok)
                        break;
                    Emit([[Map(left, top), Map(right, top), Map(right, bottom), Map(left, bottom)]], closed: true, LineWidth(0));
                    break;
                }
                case ActionPolyLine:
                {
                    PointF[]? points = Polygon(ref r);
                    int width = 0;
                    bool visible = true;
                    if (version >= 2)
                    {
                        // LineInfo: its own version-compat block, then style and width.
                        r.U16();
                        uint infoLength = r.U32();
                        int infoStart = r.Position;
                        visible = r.U16() != 0;
                        width = r.I32();
                        r.Seek(infoStart + (int)Math.Min(infoLength, int.MaxValue));
                    }
                    if (version >= 3 && r.U8() != 0 && r.Ok && FlaggedPolygon(ref r) is PointF[] flagged)
                        points = flagged;
                    if (points is { Length: >= 2 } && visible && r.Ok)
                        Emit([points], closed: false, LineWidth(width));
                    break;
                }
                case ActionPolygon:
                {
                    PointF[]? points = Polygon(ref r);
                    if (version >= 2 && r.U8() != 0 && r.Ok && FlaggedPolygon(ref r) is PointF[] flagged)
                        points = flagged;
                    if (points is { Length: >= 2 })
                        Emit([points], closed: true, LineWidth(0));
                    break;
                }
                case ActionPolyPolygon:
                {
                    int polygons = r.U16();
                    if (!r.Ok)
                        break;
                    var contours = new List<PointF[]>(Math.Min(polygons, 1024));
                    for (int p = 0; p < polygons && r.Ok; p++)
                    {
                        if (Polygon(ref r) is not PointF[] points)
                            break;
                        contours.Add(points);
                    }
                    if (version >= 2)
                    {
                        int complex = r.U16();
                        for (int c = 0; c < complex && r.Ok; c++)
                        {
                            int index = r.U16();
                            if (FlaggedPolygon(ref r) is PointF[] flagged && index < contours.Count)
                                contours[index] = flagged;
                        }
                    }
                    contours.RemoveAll(contour => contour.Length < 2);
                    Emit([.. contours], closed: true, LineWidth(0));
                    break;
                }
            }
        }

        if (ops.Count == 0)
            return null;

        using var bitmap = new BBitmap(pixelsWide, pixelsHigh);
        using (var canvas = bitmap.OpenCanvas())
        {
            foreach (Op op in ops)
            {
                if (op.StrokeWidth > 0)
                {
                    foreach (PointF[] contour in op.Contours)
                        if (contour.Length >= 2)
                            canvas.DrawPathStroke(contour, op.Color, op.StrokeWidth);
                }
                else
                {
                    canvas.FillGlyphContours(op.Contours, op.Color);
                }
            }
        }

        return EmfRasterizer.EncodePng(bitmap);
    }

    /// <summary>
    /// A version-compat map mode: unit, origin, x and y scale fractions, and the
    /// "simple" flag. A MapRelative map mode composes onto <paramref name="current"/>.
    /// Null when the unit is relative to a device or font this has none of.
    /// </summary>
    private static MapMode? ReadMapMode(ref Reader r, MapMode? current)
    {
        r.U16();
        uint length = r.U32();
        int start = r.Position;
        ushort unit = r.U16();
        int originX = r.I32(), originY = r.I32();
        int scaleXNum = r.I32(), scaleXDen = r.I32(), scaleYNum = r.I32(), scaleYDen = r.I32();
        r.Seek(start + (int)Math.Min(length, int.MaxValue));
        if (!r.Ok || scaleXDen == 0 || scaleYDen == 0 || scaleXNum == 0 || scaleYNum == 0)
            return null;
        double scaleX = scaleXNum / (double)scaleXDen, scaleY = scaleYNum / (double)scaleYDen;
        if (unit == MapRelative)
            return current?.Relative(originX, originY, scaleX, scaleY);

        // VCL's MapUnit, in the order it streams.
        double factor = unit switch
        {
            0 => 1,               // 1/100 mm
            1 => 10,              // 1/10 mm
            2 => 100,             // mm
            3 => 1000,            // cm
            4 => 2.54,            // 1/1000 in
            5 => 25.4,            // 1/100 in
            6 => 254,             // 1/10 in
            7 => 2540,            // in
            8 => 2540.0 / 72,     // pt
            9 => 2540.0 / 1440,   // twip
            10 => 2540.0 / 96,    // pixel, at 96 dpi
            _ => 0,               // system or application font units
        };
        return factor == 0 ? null : MapMode.Absolute(factor, originX, originY, scaleX, scaleY);
    }

    private const ushort MapRelative = 13;

    /// <summary>A tools::Color: transparency in the high byte, then red, green, blue.</summary>
    private static BColor ColorOf(uint value) =>
        (value >> 24) == 0xFF
            ? BColor.Empty
            : new BColor((byte)(value >> 16), (byte)(value >> 8), (byte)value);

    /// <summary>A bounds-checked little-endian cursor; once a read overruns, every read returns zero.</summary>
    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;

        public int Position { get; private set; }

        public bool Ok { get; private set; } = true;

        public readonly int Remaining => _data.Length - Position;

        public byte U8() => Take(1) ? _data[Position - 1] : (byte)0;

        public ushort U16() => Take(2) ? BinaryPrimitives.ReadUInt16LittleEndian(_data[(Position - 2)..]) : (ushort)0;

        public uint U32() => Take(4) ? BinaryPrimitives.ReadUInt32LittleEndian(_data[(Position - 4)..]) : 0;

        public int I32() => Take(4) ? BinaryPrimitives.ReadInt32LittleEndian(_data[(Position - 4)..]) : 0;

        public ReadOnlySpan<byte> Bytes(int count) => Take(count) ? _data.Slice(Position - count, count) : [];

        public void Seek(int position)
        {
            if (position < 0 || position > _data.Length)
                Ok = false;
            else
                Position = position;
        }

        private bool Take(int count)
        {
            if (!Ok || count < 0 || count > _data.Length - Position)
            {
                Ok = false;
                return false;
            }
            Position += count;
            return true;
        }
    }
}
