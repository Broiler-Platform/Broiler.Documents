using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Text;

namespace Broiler.Documents.Resources;

/// <summary>
/// Rasterizes the vector subset of a StarView Metafile (<c>VCLMTF</c>, LibreOffice's
/// own GDI metafile, which it stores as the replacement picture of a chart or other
/// embedded object under <c>ObjectReplacements/</c>) to a PNG: filled and stroked
/// rectangles, polygons and poly-polygons, and polylines, in their line and fill
/// colours, under the header's and each MAPMODE action's map mode, absolute or
/// relative, and text (TEXT, TEXTARRAY, STRETCHTEXT) in the FONT, TEXTCOLOR and
/// TEXTALIGN in force, and bitmaps placed at a stated size (BMPSCALE, and
/// BMPEXSCALE without transparency), plain or zlib-compressed. Clip regions are
/// ignored. Returns null when the metafile draws nothing this subset understands.
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
    private const ushort ActionBmpScale = 117;
    private const ushort ActionBmpExScale = 120;
    private const ushort ActionText = 112;
    private const ushort ActionTextArray = 113;
    private const ushort ActionStretchText = 114;
    private const ushort ActionLineColor = 132;
    private const ushort ActionFillColor = 133;
    private const ushort ActionTextColor = 134;
    private const ushort ActionTextAlign = 136;
    private const ushort ActionMapMode = 137;
    private const ushort ActionFont = 138;
    private const ushort ActionPush = 139;
    private const ushort ActionPop = 140;

    private const ushort PushLineColor = 0x1;
    private const ushort PushFillColor = 0x2;
    private const ushort PushFont = 0x4;
    private const ushort PushTextColor = 0x8;
    private const ushort PushMapMode = 0x10;
    private const ushort PushTextAlign = 0x100;

    // VCL's TextAlign; a new Font is ALIGN_TOP.
    private const ushort AlignTop = 0;
    private const ushort AlignBottom = 2;

    // RTL_TEXTENCODING_UNICODE: a string streamed as UTF-16 with a u32 count.
    private const ushort CharSetUnicode = 0xFFFF;

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

    private sealed record State(BColor Line, BColor Fill, MapMode Map, ushort Flags, SvmFont? Font, BColor TextColor, ushort Align);

    /// <summary>A FONT action's face: its family, em height in logical units, and orientation in tenths of a degree.</summary>
    private sealed record SvmFont(string Family, int Height, bool Bold, bool Italic, int Orientation, ushort CharSet);

    private sealed record Op(PointF[][] Contours, BColor Color, float StrokeWidth, EmfRasterizer.TextDraw? Text = null,
        EmfRasterizer.Blit? Blit = null);

    public static bool IsSvm(ReadOnlySpan<byte> data) =>
        data.Length >= 12 && data[..6].SequenceEqual("VCLMTF"u8);

    /// <summary>
    /// True for a well-formed metafile whose actions only set state - colours,
    /// fonts, map modes, clip regions, push and pop - and so draw nothing at
    /// all, as the replacement LibreOffice stores for an empty embedded
    /// spreadsheet does (<c>fdo68839.odt</c>). False for one with any action
    /// that can draw, including text, bitmaps and comments (which may carry
    /// EMF+), even where <see cref="RenderPng"/> does not draw it.
    /// </summary>
    public static bool IsBlank(ReadOnlySpan<byte> data)
    {
        if (!IsSvm(data))
            return false;

        var header = new Reader(data[6..]);
        header.U16();
        uint headerLength = header.U32();
        if (!header.Ok || headerLength > (uint)(data.Length - 12))
            return false;
        header.U32(); // compression mode
        if (ReadMapMode(ref header, current: null) is null)
            return false;
        header.I32();
        header.I32();
        uint actionCount = header.U32();
        if (!header.Ok)
            return false;

        int offset = 12 + (int)headerLength;
        for (uint action = 0; action < actionCount; action++)
        {
            if (action >= MaxActions || offset + 8 > data.Length)
                return false;
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
            if (length > (uint)(data.Length - offset - 8) || !SetsStateOnly(type))
                return false;
            offset += 8 + (int)length;
        }

        return true;
    }

    // NULL; CLIPREGION to RASTEROP (128-141); REFPOINT, TEXTLINECOLOR (144, 145);
    // LAYOUTMODE, TEXTLANGUAGE, OVERLINECOLOR (149-151).
    private static bool SetsStateOnly(ushort type) =>
        type is 0 or (>= 128 and <= 141) or 144 or 145 or (>= 149 and <= 151);

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
        // A hairline is one device pixel wherever the metafile is played. Half a
        // point is one pixel at the 144 dpi the conformance runner compares at;
        // a full point drew tdf161979_metafile.fodt's hairlines twice as heavy
        // as LibreOffice does.
        double hairline = 0.5 * scale;

        var line = new BColor(0, 0, 0);
        var fill = new BColor(255, 255, 255);
        SvmFont? font = null;
        var textColor = new BColor(0, 0, 0);
        ushort align = AlignTop;
        // The character set strings are streamed in: the last FONT's, which a
        // POP does not restore (SvmReader's meActualCharSet).
        ushort charSet = 0;
        var saved = new Stack<State>();
        var ops = new List<Op>();
        int totalPoints = 0;

        PointF Map(double x, double y) => new((float)(map.X(x) * sx), (float)(map.Y(y) * sy));

        // A text action's string, then its index and length into it. Version 2
        // repeats the string as UTF-16, which is the one to draw.
        void AddText(ref Reader r, ushort type, ushort version)
        {
            int x = r.I32(), y = r.I32();
            string? text = ReadString(ref r, charSet == CharSetUnicode);
            uint stretchWidth = type == ActionStretchText ? r.U32() : 0;
            int index = r.U16(), length = r.U16();
            int[]? dx = null;
            if (type == ActionTextArray)
            {
                uint entries = r.U32();
                if (entries > 0)
                {
                    if (entries > r.Remaining / 4)
                        return;
                    dx = new int[entries];
                    for (int i = 0; i < dx.Length; i++)
                        dx[i] = r.I32();
                }
            }
            if (version >= 2 && r.Remaining >= 2 && ReadUtf16(ref r) is string wide)
                text = wide;
            if (!r.Ok || text is null || index >= text.Length || font is not SvmFont face || textColor.IsEmpty)
                return;
            text = text.Substring(index, Math.Min(length, text.Length - index));
            if (string.IsNullOrWhiteSpace(text))
                return;

            // The baseline runs along the font's orientation, counterclockwise.
            double angle = face.Orientation / 10.0 * Math.PI / 180;
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            PointF origin = Map(x, y);
            PointF along = Map(x + cos, y - sin), across = Map(x + sin, y + cos);
            double ax = along.X - origin.X, ay = along.Y - origin.Y, alongScale = Math.Sqrt(ax * ax + ay * ay);
            double cx = across.X - origin.X, cy = across.Y - origin.Y, acrossScale = Math.Sqrt(cx * cx + cy * cy);
            double sizePx = Math.Abs(face.Height) * acrossScale;
            if (!(alongScale > 0) || !double.IsFinite(alongScale) || !(sizePx >= 0.5) || sizePx > 4096)
                return;
            (double ux, double uy) = (ax / alongScale, ay / alongScale);

            var style = new BFontStyle(face.Family, sizePx, face.Bold ? BFontWeight.Bold : BFontWeight.Normal,
                face.Italic ? BFontSlant.Italic : BFontSlant.Normal);
            double[]? offsets = null;
            if (dx is { Length: > 0 })
            {
                // A DX array is each character's end, from the start of the text.
                offsets = new double[text.Length];
                for (int i = 1; i < offsets.Length; i++)
                    offsets[i] = dx[Math.Min(i - 1, dx.Length - 1)] * alongScale;
            }
            else if (stretchWidth > 0 && text.Length > 1)
            {
                // Stretched text spreads its characters over the stated width.
                double natural = BTextMeasurer.MeasureAdvance(text, style);
                if (natural > 0)
                {
                    double k = stretchWidth * alongScale / natural;
                    offsets = new double[text.Length];
                    for (int i = 1; i < offsets.Length; i++)
                        offsets[i] = BTextMeasurer.MeasureAdvance(text[..i], style) * k;
                }
            }

            double drop = align switch
            {
                AlignTop => sizePx * 0.8,
                AlignBottom => -Math.Max(0, BTextMeasurer.GetLineHeight(style) - sizePx * 0.8),
                _ => 0, // ALIGN_BASELINE
            };
            var baseline = new BPoint(origin.X - uy * drop, origin.Y + ux * drop);
            ops.Add(new Op([], BColor.Empty, 0, new EmfRasterizer.TextDraw(text, offsets, baseline, ux, uy, style, textColor)));
        }

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
            ReadOnlySpan<byte> payload = data.Slice(offset + 8, (int)length);
            var r = new Reader(payload);
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
                case ActionFont:
                    if (ReadFont(ref r) is SvmFont read)
                    {
                        font = read;
                        charSet = read.CharSet;
                        align = AlignTop;
                    }
                    break;
                case ActionTextColor:
                {
                    uint value = r.U32();
                    if (r.Ok)
                        textColor = ColorOf(value);
                    break;
                }
                case ActionTextAlign:
                {
                    ushort value = r.U16();
                    if (r.Ok)
                        align = value;
                    break;
                }
                case ActionText or ActionTextArray or ActionStretchText:
                    AddText(ref r, type, version);
                    break;
                case ActionBmpScale or ActionBmpExScale when payload.Length >= 16:
                {
                    // The bitmap, then the point and size it is drawn at.
                    ReadOnlySpan<byte> place = payload[^16..];
                    int x = BinaryPrimitives.ReadInt32LittleEndian(place), y = BinaryPrimitives.ReadInt32LittleEndian(place[4..]);
                    int w = BinaryPrimitives.ReadInt32LittleEndian(place[8..]), h = BinaryPrimitives.ReadInt32LittleEndian(place[12..]);
                    if (w != 0 && h != 0 && ReadDib(payload[..^16], type == ActionBmpExScale) is EmfRasterizer.Dib dib)
                    {
                        ops.Add(new Op([], BColor.Empty, 0, Blit: new EmfRasterizer.Blit(dib, Map(x, y), Map(x + w, y), Map(x, y + h),
                            0, 0, dib.Width, dib.Height, SourceAlpha: false, ConstantAlpha: 255)));
                    }
                    break;
                }
                case ActionPush:
                    saved.Push(new State(line, fill, map, r.U16(), font, textColor, align));
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
                        if ((state.Flags & PushFont) != 0)
                            font = state.Font;
                        if ((state.Flags & PushTextColor) != 0)
                            textColor = state.TextColor;
                        if ((state.Flags & (PushFont | PushTextAlign)) != 0)
                            align = state.Align;
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

        byte[] rgba = new byte[checked(pixelsWide * pixelsHigh * 4)];
        using var bitmap = new BBitmap(pixelsWide, pixelsHigh, rgba, takeOwnership: true);
        int run = 0;
        while (run < ops.Count)
        {
            if (ops[run].Blit is EmfRasterizer.Blit blit)
            {
                EmfRasterizer.Composite(rgba, pixelsWide, pixelsHigh, blit);
                run++;
                continue;
            }
            if (ops[run].Text is not null)
            {
                // Text goes through the renderer, which owns font resolution, onto
                // a transparent layer composited over what is drawn so far.
                var list = new BRenderList();
                for (; run < ops.Count && ops[run].Text is EmfRasterizer.TextDraw text; run++)
                    EmfRasterizer.DrawText(list, text);
                EmfRasterizer.CompositeLayer(rgba, pixelsWide, pixelsHigh, list);
                continue;
            }
            using var canvas = bitmap.OpenCanvas();
            for (; run < ops.Count && ops[run].Text is null && ops[run].Blit is null; run++)
            {
                Op op = ops[run];
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

    // dibtools' ZCOMPRESS: the pixel data is zlib-compressed behind its coded
    // size, uncoded size and real compression.
    private const uint ZCompress = 0x01004453;
    private const int MaxDibBytes = 64 * 1024 * 1024;

    /// <summary>
    /// A bitmap as vcl's WriteDIB streams it in a metafile: a BITMAPFILEHEADER,
    /// the info header and palette, then the pixels, plain or ZCOMPRESS. A
    /// BitmapEx (<paramref name="extended"/>) follows it with two magic numbers
    /// and its transparency type, and is read only when it has none: a mask or
    /// alpha this does not apply would paint over what lies under it.
    /// </summary>
    private static EmfRasterizer.Dib? ReadDib(ReadOnlySpan<byte> data, bool extended)
    {
        if (data.Length < 54 || data[0] != (byte)'B' || data[1] != (byte)'M')
            return null;
        int bitsAt = BinaryPrimitives.ReadInt32LittleEndian(data[10..]);
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(data[14..]);
        if (headerSize < 40 || bitsAt < 14 + headerSize || bitsAt > data.Length)
            return null;
        byte[] info = data[14..bitsAt].ToArray();
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(16));
        int width = BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(4));
        int height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(8)));
        int bitCount = BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(14));
        long stride = ((long)width * bitCount + 31) / 32 * 4;
        if (width <= 0 || height <= 0 || stride * height > MaxDibBytes)
            return null;

        ReadOnlySpan<byte> bits;
        int end;
        if (compression == ZCompress)
        {
            if (bitsAt + 12 > data.Length)
                return null;
            uint coded = BinaryPrimitives.ReadUInt32LittleEndian(data[bitsAt..]);
            uint uncoded = BinaryPrimitives.ReadUInt32LittleEndian(data[(bitsAt + 4)..]);
            uint real = BinaryPrimitives.ReadUInt32LittleEndian(data[(bitsAt + 8)..]);
            if (coded > (uint)(data.Length - bitsAt - 12) || uncoded > MaxDibBytes)
                return null;
            byte[]? inflated = Inflate(data.Slice(bitsAt + 12, (int)coded), (int)uncoded);
            if (inflated is null)
                return null;
            BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(16), real);
            bits = inflated;
            end = bitsAt + 12 + (int)coded;
        }
        else
        {
            if (stride * height > data.Length - bitsAt)
                return null;
            bits = data[bitsAt..];
            end = bitsAt + (int)(stride * height);
        }

        // BitmapEx: 0x25091962, 0xACB20201, then TransparentType NONE (0).
        if (extended && (end + 9 > data.Length ||
                BinaryPrimitives.ReadUInt32LittleEndian(data[end..]) != 0x25091962 ||
                BinaryPrimitives.ReadUInt32LittleEndian(data[(end + 4)..]) != 0xACB20201 ||
                data[end + 8] != 0))
            return null;

        return EmfRasterizer.DecodeDib(info, bits);
    }

    private static byte[]? Inflate(ReadOnlySpan<byte> coded, int uncoded)
    {
        try
        {
            using var input = new MemoryStream(coded.ToArray(), writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            byte[] output = new byte[uncoded];
            zlib.ReadAtLeast(output, uncoded, throwOnEndOfStream: false);
            return output;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// A version-compat Font (vcl's ReadImplFont): family and style names, the
    /// size, then character set, family, pitch, weight, underline, strikeout,
    /// italic, language and width type as u16s, and the orientation.
    /// </summary>
    private static SvmFont? ReadFont(ref Reader r)
    {
        r.U16();
        uint length = r.U32();
        int start = r.Position;
        string? family = ReadString(ref r, unicode: false);
        ReadString(ref r, unicode: false);
        r.I32();
        int height = r.I32();
        ushort charSet = r.U16();
        r.U16();
        r.U16();
        ushort weight = r.U16();
        r.U16();
        r.U16();
        ushort italic = r.U16();
        r.U16();
        r.U16();
        short orientation = (short)r.U16();
        r.Seek(start + (int)Math.Min(length, int.MaxValue));
        if (!r.Ok || family is null)
            return null;
        family = family.Trim();
        // WEIGHT_SEMIBOLD and heavier; ITALIC_OBLIQUE or ITALIC_NORMAL.
        return new SvmFont(family.Length == 0 ? "Liberation Serif" : family, height, weight is >= 7 and <= 10,
            italic is 1 or 2, orientation, charSet);
    }

    /// <summary>
    /// A string as SvStream::ReadUniOrByteString reads it: UTF-16 with a u32
    /// count in the Unicode character set, else a byte string with a u16 count.
    /// </summary>
    private static string? ReadString(ref Reader r, bool unicode)
    {
        if (unicode)
        {
            uint count = r.U32();
            if (!r.Ok || count > (uint)r.Remaining / 2)
                return null;
            return Encoding.Unicode.GetString(r.Bytes((int)count * 2));
        }

        int bytes = r.U16();
        if (!r.Ok || bytes > r.Remaining)
            return null;
        return Encoding.Latin1.GetString(r.Bytes(bytes));
    }

    /// <summary>A UTF-16 string with a u16 count, the Unicode copy a version 2 text action carries.</summary>
    private static string? ReadUtf16(ref Reader r)
    {
        int count = r.U16();
        if (!r.Ok || count > r.Remaining / 2)
            return null;
        return Encoding.Unicode.GetString(r.Bytes(count * 2));
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
