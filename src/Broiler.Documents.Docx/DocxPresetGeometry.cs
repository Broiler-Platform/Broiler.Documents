using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Broiler.Documents.Docx;

/// <summary>
/// The outlines of DrawingML preset shapes (<c>a:prstGeom</c>) that are
/// polygons, worked out from the guide formulas of ECMA-376 Part 1's
/// <c>presetShapeDefinitions.xml</c> for the shape's box and adjust values
/// (<c>a:avLst</c>). LibreOffice draws these presets from the same
/// definitions (oox's generated custom shape presets).
/// </summary>
/// <remarks>
/// A shape the reader did not know was drawn as its box: the five-point stars
/// of <c>tdf159158_zOrder_*.docx</c> were two overlapping rectangles.
/// </remarks>
internal static class DocxPresetGeometry
{
    /// <summary>
    /// The preset's outline as one closed polygon, corners as fractions of the
    /// box's width and height, mirrored as the shape's <c>a:xfrm</c> flips it
    /// and turned by its half turn; or null for a preset this does not draw as
    /// a polygon, or a shape turned any other way, which the reader leaves as
    /// its unturned box.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<(double X, double Y)>>? Outline(
        string? preset, XElement? presetGeometry, double width, double height, XElement? transform)
    {
        if (preset is null || !(width > 0) || !(height > 0))
            return null;

        bool flipH = (string?)transform?.Attribute("flipH") is "1" or "true";
        bool flipV = (string?)transform?.Attribute("flipV") is "1" or "true";
        long rotation = long.TryParse((string?)transform?.Attribute("rot"), NumberStyles.Integer, CultureInfo.InvariantCulture,
            out long turn) ? ((turn % 21600000) + 21600000) % 21600000 : 0;
        if (rotation == 10800000)
            (flipH, flipV) = (!flipH, !flipV);
        else if (rotation != 0)
            return null;

        Dictionary<string, double> adjust = Adjustments(presetGeometry);
        double Adj(string name, double fallback) => adjust.TryGetValue(name, out double value) ? value : fallback;
        double w = width, h = height, ss = Math.Min(w, h), hc = w / 2, vc = h / 2;
        static double Pin(double low, double value, double high) => Math.Max(low, Math.Min(high, value));

        (double X, double Y)[]? points;
        switch (preset)
        {
            case "triangle":
            {
                double x2 = w * Pin(0, Adj("adj", 50000), 100000) / 100000;
                points = [(0, h), (x2, 0), (w, h)];
                break;
            }
            case "rtTriangle":
                points = [(0, h), (0, 0), (w, h)];
                break;
            case "diamond":
                points = [(hc, 0), (w, vc), (hc, h), (0, vc)];
                break;
            case "star5":
            {
                double a = Pin(0, Adj("adj", 19098), 50000);
                double swd2 = hc * Adj("hf", 105146) / 100000;
                double shd2 = vc * Adj("vf", 110557) / 100000;
                double svc = vc * Adj("vf", 110557) / 100000;
                double dx1 = swd2 * Cos(18), dx2 = swd2 * Cos(306);
                double dy1 = shd2 * Sin(18), dy2 = shd2 * Sin(306);
                double iwd2 = swd2 * a / 50000, ihd2 = shd2 * a / 50000;
                double sdx1 = iwd2 * Cos(342), sdx2 = iwd2 * Cos(54);
                double sdy1 = ihd2 * Sin(54), sdy2 = ihd2 * Sin(342);
                double x1 = hc - dx1, x2 = hc - dx2, x3 = hc + dx2, x4 = hc + dx1;
                double y1 = svc - dy1, y2 = svc - dy2;
                double sx1 = hc - sdx1, sx2 = hc - sdx2, sx3 = hc + sdx2, sx4 = hc + sdx1;
                double sy1 = svc - sdy1, sy2 = svc - sdy2, sy3 = svc + ihd2;
                points = [(x1, y1), (sx2, sy1), (hc, 0), (sx3, sy1), (x4, y1), (sx4, sy2), (x3, y2), (hc, sy3), (x2, y2), (sx1, sy2)];
                break;
            }
            case "rightArrow":
            case "leftArrow":
            {
                double a1 = Pin(0, Adj("adj1", 50000), 100000);
                double a2 = Pin(0, Adj("adj2", 50000), 100000 * w / ss);
                double head = ss * a2 / 100000;
                double y1 = vc - h * a1 / 200000, y2 = vc + h * a1 / 200000;
                double x1 = w - head;
                points = [(0, y1), (x1, y1), (x1, 0), (w, vc), (x1, h), (x1, y2), (0, y2)];
                if (preset == "leftArrow")
                    flipH = !flipH;
                break;
            }
            case "downArrow":
            case "upArrow":
            {
                double a1 = Pin(0, Adj("adj1", 50000), 100000);
                double a2 = Pin(0, Adj("adj2", 50000), 100000 * h / ss);
                double head = ss * a2 / 100000;
                double x1 = hc - w * a1 / 200000, x2 = hc + w * a1 / 200000;
                double y1 = h - head;
                points = [(x1, 0), (x2, 0), (x2, y1), (w, y1), (hc, h), (0, y1), (x1, y1)];
                if (preset == "upArrow")
                    flipV = !flipV;
                break;
            }
            default:
                points = null;
                break;
        }

        if (points is null)
            return null;

        return [[.. points.Select(point => (
            flipH ? 1 - point.X / w : point.X / w,
            flipV ? 1 - point.Y / h : point.Y / h))]];
    }

    // DrawingML angles are in 60,000ths of a degree; these take degrees.
    private static double Cos(double degrees) => Math.Cos(degrees * Math.PI / 180);

    private static double Sin(double degrees) => Math.Sin(degrees * Math.PI / 180);

    /// <summary>The <c>a:avLst</c> guides a preset states, by name: <c>val n</c> only.</summary>
    private static Dictionary<string, double> Adjustments(XElement? presetGeometry)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        if (presetGeometry?.Element(DocxNamespaces.Drawing + "avLst") is not XElement list)
            return values;

        foreach (XElement guide in list.Elements(DocxNamespaces.Drawing + "gd"))
        {
            string? name = (string?)guide.Attribute("name");
            string? formula = ((string?)guide.Attribute("fmla"))?.Trim();
            if (name is null || formula is null || !formula.StartsWith("val ", StringComparison.Ordinal))
                continue;
            if (double.TryParse(formula.AsSpan(4), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
                double.IsFinite(value))
                values[name] = value;
        }

        return values;
    }
}
