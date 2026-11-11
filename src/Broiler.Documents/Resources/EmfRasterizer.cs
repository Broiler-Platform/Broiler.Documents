using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using Broiler.Graphics.Color;
using Broiler.Graphics.Imaging;

namespace Broiler.Documents.Resources;

/// <summary>
/// Rasterizes the vector subset of an Enhanced Metafile (an RTF <c>\emfblip</c> or a DOCX <c>.emf</c> media part) to a PNG:
/// filled and stroked polygons, polylines, Bezier paths, rectangles, ellipses
/// and DIB blits (STRETCHDIBITS, BITBLT, STRETCHBLT, ALPHABLEND) under the
/// window/viewport mapping and world transform. Text is not drawn and clip
/// paths are ignored. Returns null when the metafile draws nothing this subset
/// understands.
/// </summary>
internal static class EmfRasterizer
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
        var ops = new List<Op>();
        var figures = new List<List<PointF>>();
        bool inPath = false;
        PointF current = default;
        int totalPoints = 0;

        PointF Map(double x, double y)
        {
            (double dx, double dy) = ToDevice(dc, x, y);
            return new PointF((float)((dx - left) * sx), (float)((dy - top) * sy));
        }

        float PenWidth() =>
            (float)Math.Clamp(Math.Max(1, dc.PenWidth) * Math.Sqrt(Math.Abs((dc.M11 * dc.M22 - dc.M12 * dc.M21) * DeviceScaleX(dc) * DeviceScaleY(dc))) * Math.Min(sx, sy), 0.5, 64);

        void Emit(PointF[][] contours, bool fill, bool stroke)
        {
            if (fill && !dc.Brush.IsEmpty)
                ops.Add(new Op(contours, dc.Brush, 0, null));
            if (stroke && !dc.Pen.IsEmpty)
                ops.Add(new Op(contours, dc.Pen, PenWidth(), null));
        }

        void MoveTo(PointF point)
        {
            current = point;
            if (inPath)
                figures.Add([point]);
        }

        void LineTo(PointF point)
        {
            if (inPath)
            {
                if (figures.Count == 0)
                    figures.Add([current]);
                figures[^1].Add(point);
            }
            else
            {
                Emit([[current, point]], fill: false, stroke: true);
            }
            current = point;
        }

        void BezierTo(PointF c1, PointF c2, PointF end)
        {
            PointF start = current;
            const int Steps = 12;
            for (int step = 1; step <= Steps; step++)
            {
                float t = step / (float)Steps, u = 1 - t;
                float a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
                LineTo(new PointF(
                    a * start.X + b * c1.X + c * c2.X + d * end.X,
                    a * start.Y + b * c1.Y + c * c2.Y + d * end.Y));
            }
        }

        // Reads `count` points at `at`, 16- or 32-bit, into page pixels; null when
        // the record is too short or the point budget is spent.
        PointF[]? Points(ReadOnlySpan<byte> r, int at, int count, bool shortPoints)
        {
            int stride = shortPoints ? 4 : 8;
            if (count < 0 || count > (r.Length - at) / stride || (totalPoints += count) > MaxPoints)
                return null;
            var points = new PointF[count];
            for (int i = 0; i < count; i++)
            {
                double px = shortPoints ? BinaryPrimitives.ReadInt16LittleEndian(r[(at + i * 4)..]) : I32(r, at + i * 8);
                double py = shortPoints ? BinaryPrimitives.ReadInt16LittleEndian(r[(at + 2 + i * 4)..]) : I32(r, at + 4 + i * 8);
                points[i] = Map(px, py);
            }
            return points;
        }

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
                case 27 when size >= 16: // MOVETOEX
                    MoveTo(Map(I32(r, 8), I32(r, 12)));
                    break;
                case 54 when size >= 16: // LINETO
                    LineTo(Map(I32(r, 8), I32(r, 12)));
                    break;
                case 3 or 4 or 86 or 87 when size >= 28: // POLYGON, POLYLINE, POLYGON16, POLYLINE16
                {
                    int count = I32(r, 24);
                    if (count < 2 || Points(r, 28, count, type >= 86) is not PointF[] points)
                        break;
                    bool closed = type is 3 or 86;
                    if (inPath)
                    {
                        figures.Add([.. points]);
                        if (closed)
                            figures[^1].Add(points[0]);
                        current = points[^1];
                        break;
                    }
                    if (closed)
                        points = [.. points, points[0]];
                    Emit([points], fill: closed, stroke: true);
                    break;
                }
                case 6 or 89 or 5 or 88 when size >= 28: // POLYLINETO, POLYLINETO16, POLYBEZIERTO, POLYBEZIERTO16
                {
                    if (Points(r, 28, I32(r, 24), type >= 86) is not PointF[] points)
                        break;
                    if (type is 6 or 89)
                    {
                        foreach (PointF point in points)
                            LineTo(point);
                    }
                    else
                    {
                        for (int i = 0; i + 2 < points.Length; i += 3)
                            BezierTo(points[i], points[i + 1], points[i + 2]);
                    }
                    break;
                }
                case 2 or 85 when size >= 28: // POLYBEZIER, POLYBEZIER16
                {
                    if (Points(r, 28, I32(r, 24), type == 85) is not PointF[] points || points.Length < 4)
                        break;
                    bool wasInPath = inPath;
                    if (!wasInPath)
                    {
                        // Collect the curve as a path of its own and stroke it.
                        inPath = true;
                        figures.Clear();
                    }
                    MoveTo(points[0]);
                    for (int i = 1; i + 2 < points.Length; i += 3)
                        BezierTo(points[i], points[i + 1], points[i + 2]);
                    if (!wasInPath)
                    {
                        inPath = false;
                        Emit(Contours(figures), fill: false, stroke: true);
                        figures.Clear();
                    }
                    break;
                }
                case 7 or 8 or 90 or 91 when size >= 32: // POLYPOLYLINE, POLYPOLYGON, and their 16-bit forms
                {
                    int polygons = I32(r, 24), total = I32(r, 28);
                    if (polygons <= 0 || polygons > (size - 32) / 4)
                        break;
                    bool shortPoints = type >= 90;
                    if (Points(r, 32 + polygons * 4, total, shortPoints) is not PointF[] points)
                        break;
                    bool closed = type is 8 or 91;
                    var contours = new List<PointF[]>();
                    int at = 0;
                    for (int p = 0; p < polygons; p++)
                    {
                        int count = I32(r, 32 + p * 4);
                        if (count < 0 || count > points.Length - at)
                            break;
                        if (count >= 2)
                        {
                            PointF[] contour = points[at..(at + count)];
                            contours.Add(closed ? [.. contour, contour[0]] : contour);
                        }
                        at += count;
                    }
                    if (inPath)
                        figures.AddRange(contours.ConvertAll(contour => new List<PointF>(contour)));
                    else
                        Emit([.. contours], fill: closed, stroke: true);
                    break;
                }
                case 42 or 43 when size >= 24: // ELLIPSE, RECTANGLE
                {
                    double l = I32(r, 8), t = I32(r, 12), rr = I32(r, 16), b = I32(r, 20);
                    PointF[] shape;
                    if (type == 43)
                    {
                        shape = [Map(l, t), Map(rr, t), Map(rr, b), Map(l, b), Map(l, t)];
                    }
                    else
                    {
                        shape = new PointF[49];
                        for (int i = 0; i < shape.Length; i++)
                        {
                            double angle = i * Math.PI * 2 / (shape.Length - 1);
                            shape[i] = Map((l + rr) / 2 + Math.Cos(angle) * (rr - l) / 2, (t + b) / 2 + Math.Sin(angle) * (b - t) / 2);
                        }
                    }
                    Emit([shape], fill: true, stroke: true);
                    break;
                }
                case 59: // BEGINPATH
                    inPath = true;
                    figures.Clear();
                    break;
                case 60: // ENDPATH
                    inPath = false;
                    break;
                case 61: // CLOSEFIGURE
                    if (figures.Count > 0 && figures[^1].Count > 0)
                    {
                        figures[^1].Add(figures[^1][0]);
                        current = figures[^1][0];
                    }
                    break;
                case 62 or 63 or 64: // FILLPATH, STROKEANDFILLPATH, STROKEPATH
                    Emit(Contours(figures), fill: type != 64, stroke: type != 62);
                    figures.Clear();
                    break;
                case 67 or 68: // SELECTCLIPPATH (clipping is not modelled), ABORTPATH
                    figures.Clear();
                    inPath = false;
                    break;
                case 81 when size >= 80: // STRETCHDIBITS
                    if (U32(r, 68) == SrcCopy)
                        AddBlit(r, I32(r, 24), I32(r, 28), I32(r, 72), I32(r, 76), I32(r, 32), I32(r, 36), I32(r, 40), I32(r, 44),
                            I32(r, 48), I32(r, 52), I32(r, 56), I32(r, 60), sourceAlpha: false, constantAlpha: 255);
                    break;
                case 76 or 77 when size >= 100: // BITBLT, STRETCHBLT
                    if (U32(r, 40) == SrcCopy && I32(r, 88) > 0)
                    {
                        bool stretch = type == 77 && size >= 108;
                        AddBlit(r, I32(r, 24), I32(r, 28), I32(r, 32), I32(r, 36), I32(r, 44), I32(r, 48),
                            stretch ? I32(r, 100) : I32(r, 32), stretch ? I32(r, 104) : I32(r, 36),
                            I32(r, 84), I32(r, 88), I32(r, 92), I32(r, 96), sourceAlpha: false, constantAlpha: 255);
                    }
                    break;
                case 114 when size >= 108: // ALPHABLEND
                    AddBlit(r, I32(r, 24), I32(r, 28), I32(r, 32), I32(r, 36), I32(r, 44), I32(r, 48), I32(r, 100), I32(r, 104),
                        I32(r, 84), I32(r, 88), I32(r, 92), I32(r, 96), sourceAlpha: r[43] == 1, constantAlpha: r[42]);
                    break;
            }
        }

        if (ops.Count == 0)
            return null;

        byte[] rgba = new byte[checked(pixelsWide * pixelsHigh * 4)];
        using var bitmap = new BBitmap(pixelsWide, pixelsHigh, rgba, takeOwnership: true);
        int run = 0;
        while (run < ops.Count)
        {
            if (ops[run].Blit is Blit blit)
            {
                Composite(rgba, pixelsWide, pixelsHigh, blit);
                run++;
                continue;
            }
            using (var canvas = bitmap.OpenCanvas())
            {
                for (; run < ops.Count && ops[run].Blit is null; run++)
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
        }

        return EncodePng(bitmap);

        void AddBlit(ReadOnlySpan<byte> r, int xDest, int yDest, int cxDest, int cyDest, int xSrc, int ySrc, int cxSrc, int cySrc,
            int offBmi, int cbBmi, int offBits, int cbBits, bool sourceAlpha, byte constantAlpha)
        {
            if (cxDest == 0 || cyDest == 0 || cxSrc <= 0 || cySrc <= 0 || offBmi < 0 || cbBmi < 40 || offBits < 0 || cbBits <= 0 ||
                offBmi > r.Length - cbBmi || offBits > r.Length - cbBits)
                return;
            if (DecodeDib(r.Slice(offBmi, cbBmi), r.Slice(offBits, cbBits)) is not Dib dib)
                return;
            ops.Add(new Op([], BColor.Empty, 0, new Blit(dib, Map(xDest, yDest), Map(xDest + cxDest, yDest), Map(xDest, yDest + cyDest),
                xSrc, ySrc, cxSrc, cySrc, sourceAlpha, constantAlpha)));
        }
    }

    private const uint SrcCopy = 0x00CC0020;

    private sealed record Blit(Dib Source, PointF Origin, PointF XEdge, PointF YEdge, int XSrc, int YSrc, int CxSrc, int CySrc,
        bool SourceAlpha, byte ConstantAlpha);

    private sealed record Op(PointF[][] Contours, BColor Color, float StrokeWidth, Blit? Blit);

    /// <summary>A decoded device-independent bitmap, top row first, as BGRA.</summary>
    private sealed record Dib(int Width, int Height, byte[] Bgra);

    private static PointF[][] Contours(List<List<PointF>> figures) =>
        figures.FindAll(figure => figure.Count >= 2).ConvertAll(figure => figure.ToArray()).ToArray();

    private const int MaxDibPixels = 16_000_000;

    private static Dib? DecodeDib(ReadOnlySpan<byte> info, ReadOnlySpan<byte> bits)
    {
        int headerSize = I32(info, 0);
        if (headerSize < 40 || headerSize > info.Length)
            return null;
        int width = I32(info, 4), rawHeight = I32(info, 8);
        int bitCount = BinaryPrimitives.ReadUInt16LittleEndian(info[14..]);
        uint compression = U32(info, 16);
        int height = Math.Abs(rawHeight);
        if (width <= 0 || height <= 0 || (long)width * height > MaxDibPixels || (compression != 0 && compression != 3))
            return null;
        if (bitCount is not (1 or 4 or 8 or 16 or 24 or 32))
            return null;

        int paletteOffset = headerSize + (compression == 3 && headerSize == 40 ? 12 : 0);
        int colorsUsed = I32(info, 32);
        int paletteSize = bitCount <= 8 ? (colorsUsed > 0 ? Math.Min(colorsUsed, 1 << bitCount) : 1 << bitCount) : 0;
        if (paletteOffset + paletteSize * 4 > info.Length)
            paletteSize = Math.Max(0, (info.Length - paletteOffset) / 4);

        long stride = ((long)width * bitCount + 31) / 32 * 4;
        if (stride * height > bits.Length)
            return null;

        byte[] bgra = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
        {
            int sourceRow = rawHeight > 0 ? height - 1 - row : row;
            ReadOnlySpan<byte> line = bits.Slice((int)(sourceRow * stride), (int)stride);
            for (int col = 0; col < width; col++)
            {
                int o = (row * width + col) * 4;
                byte b, g, rd, a = 255;
                switch (bitCount)
                {
                    case 32:
                        (b, g, rd, a) = (line[col * 4], line[col * 4 + 1], line[col * 4 + 2], line[col * 4 + 3]);
                        break;
                    case 24:
                        (b, g, rd) = (line[col * 3], line[col * 3 + 1], line[col * 3 + 2]);
                        break;
                    case 16:
                        int v = BinaryPrimitives.ReadUInt16LittleEndian(line[(col * 2)..]);
                        (b, g, rd) = ((byte)((v & 0x1F) * 255 / 31), (byte)(((v >> 5) & 0x1F) * 255 / 31), (byte)(((v >> 10) & 0x1F) * 255 / 31));
                        break;
                    default:
                        int perByte = 8 / bitCount;
                        int index = (line[col / perByte] >> ((perByte - 1 - col % perByte) * bitCount)) & ((1 << bitCount) - 1);
                        if (index >= paletteSize)
                            (b, g, rd) = (0, 0, 0);
                        else
                        {
                            int p = paletteOffset + index * 4;
                            (b, g, rd) = (info[p], info[p + 1], info[p + 2]);
                        }
                        break;
                }
                (bgra[o], bgra[o + 1], bgra[o + 2], bgra[o + 3]) = (b, g, rd, a);
            }
        }
        return new Dib(width, height, bgra);
    }

    /// <summary>
    /// Draws a bitmap blit into the straight-alpha RGBA page by mapping each page
    /// pixel back through the destination parallelogram to its nearest source pixel.
    /// </summary>
    private static void Composite(byte[] rgba, int pageWidth, int pageHeight, Blit blit)
    {
        float ax = blit.XEdge.X - blit.Origin.X, ay = blit.XEdge.Y - blit.Origin.Y;
        float bx = blit.YEdge.X - blit.Origin.X, by = blit.YEdge.Y - blit.Origin.Y;
        float det = ax * by - ay * bx;
        if (Math.Abs(det) < 1e-6f)
            return;
        float corner2X = blit.XEdge.X + bx, corner2Y = blit.XEdge.Y + by;
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(Math.Min(blit.Origin.X, blit.XEdge.X), Math.Min(blit.YEdge.X, corner2X))));
        int maxX = Math.Min(pageWidth, (int)Math.Ceiling(Math.Max(Math.Max(blit.Origin.X, blit.XEdge.X), Math.Max(blit.YEdge.X, corner2X))));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(Math.Min(blit.Origin.Y, blit.XEdge.Y), Math.Min(blit.YEdge.Y, corner2Y))));
        int maxY = Math.Min(pageHeight, (int)Math.Ceiling(Math.Max(Math.Max(blit.Origin.Y, blit.XEdge.Y), Math.Max(blit.YEdge.Y, corner2Y))));
        Dib dib = blit.Source;

        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                float qx = x + 0.5f - blit.Origin.X, qy = y + 0.5f - blit.Origin.Y;
                float u = (qx * by - qy * bx) / det, v = (ax * qy - ay * qx) / det;
                if (u < 0 || u >= 1 || v < 0 || v >= 1)
                    continue;
                int sxp = blit.XSrc + (int)(u * blit.CxSrc), syp = blit.YSrc + (int)(v * blit.CySrc);
                if (sxp < 0 || syp < 0 || sxp >= dib.Width || syp >= dib.Height)
                    continue;
                int s = (syp * dib.Width + sxp) * 4;
                double alpha = blit.SourceAlpha ? dib.Bgra[s + 3] / 255.0 : 1;
                alpha *= blit.ConstantAlpha / 255.0;
                if (alpha <= 0)
                    continue;
                // AC_SRC_ALPHA sources are premultiplied; opaque ones are not.
                double unpremultiply = blit.SourceAlpha && dib.Bgra[s + 3] > 0 ? 255.0 / dib.Bgra[s + 3] : 1;
                int d = (y * pageWidth + x) * 4;
                double destAlpha = rgba[d + 3] / 255.0;
                double outAlpha = alpha + destAlpha * (1 - alpha);
                for (int c = 0; c < 3; c++)
                {
                    double source = Math.Min(255, dib.Bgra[s + 2 - c] * unpremultiply);
                    rgba[d + c] = (byte)Math.Round((source * alpha + rgba[d + c] * destAlpha * (1 - alpha)) / outAlpha);
                }
                rgba[d + 3] = (byte)Math.Round(outAlpha * 255);
            }
        }
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
            uint crc = Crc(Crc(uint.MaxValue, type), bytes);
            BinaryPrimitives.WriteUInt32BigEndian(field, ~crc);
            png.Write(field);
        }
    }
}
