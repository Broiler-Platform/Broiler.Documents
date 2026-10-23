using System;
using System.Collections.Generic;

namespace Broiler.Documents.Model;

/// <summary>Bounded geometry shared by screen/raster and PDF fill painters.</summary>
public static class FillGeometry
{
    public readonly record struct Tile(double X, double Y, double Width, double Height);
    public readonly record struct Stroke(double X1, double Y1, double X2, double Y2);

    public static IEnumerable<Tile> Tiles(ImageFill fill, double width, double height, double intrinsicWidth, double intrinsicHeight)
    {
        if (width <= 0 || height <= 0) yield break;
        if (fill.Repeat == ImageFillRepeat.Stretch) { yield return new(0, 0, width, height); yield break; }
        double w = double.IsFinite(fill.Width) && fill.Width > 0 ? fill.Width : intrinsicWidth;
        double h = double.IsFinite(fill.Height) && fill.Height > 0 ? fill.Height : intrinsicHeight;
        if (!double.IsFinite(w) || !double.IsFinite(h) || w <= 0 || h <= 0) yield break;
        w = Math.Max(0.1, w); h = Math.Max(0.1, h);
        double ax = double.IsFinite(fill.AlignX) ? Math.Clamp(fill.AlignX, 0, 1) : 0.5;
        double ay = double.IsFinite(fill.AlignY) ? Math.Clamp(fill.AlignY, 0, 1) : 0.5;
        double x = (width - w) * ax, y = (height - h) * ay;
        if (fill.Repeat == ImageFillRepeat.Single) { yield return new(x, y, w, h); yield break; }
        // Bound tiny hostile tiles without creating gaps: enlarge the pattern's period.
        double scale = Math.Max(1, Math.Sqrt(Math.Ceiling(width / w + 2) * Math.Ceiling(height / h + 2) / 16384));
        w *= scale; h *= scale;
        x %= w; y %= h;
        if (x > 0) x -= w;
        if (y > 0) y -= h;
        int count = 0;
        for (double top = y; top < height && count < 16384; top += h)
            for (double left = x; left < width && count++ < 16384; left += w)
                yield return new(left, top, w, h);
    }

    public static IEnumerable<Stroke> Hatching(HatchFill hatch, double width, double height)
    {
        if (width <= 0 || height <= 0) yield break;
        double spacing = double.IsFinite(hatch.Distance) && hatch.Distance > 0 ? hatch.Distance : 6;
        spacing = Math.Max(spacing, (width + height) / 4096);
        for (int direction = 0; direction < Math.Clamp(hatch.Directions, 1, 3); direction++)
        {
            double angle = (double.IsFinite(hatch.AngleDegrees) ? hatch.AngleDegrees : 0) + (direction == 1 ? 90 : direction == 2 ? 45 : 0);
            double dx = Math.Cos(angle * Math.PI / 180), dy = Math.Sin(angle * Math.PI / 180);
            double nx = -dy, ny = dx;
            double min = Math.Min(0, nx * width) + Math.Min(0, ny * height);
            double max = Math.Max(0, nx * width) + Math.Max(0, ny * height);
            for (double offset = Math.Ceiling(min / spacing) * spacing; offset <= max; offset += spacing)
            {
                double px = nx * offset, py = ny * offset;
                double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
                bool Clip(double p, double d, double length)
                {
                    if (Math.Abs(d) < 1e-9) return p >= -0.001 && p <= length + 0.001;
                    double a = -p / d, b = (length - p) / d;
                    lo = Math.Max(lo, Math.Min(a, b)); hi = Math.Min(hi, Math.Max(a, b));
                    return lo <= hi;
                }
                if (Clip(px, dx, width) && Clip(py, dy, height))
                    yield return new(px + dx * lo, py + dy * lo, px + dx * hi, py + dy * hi);
            }
        }
    }
}
