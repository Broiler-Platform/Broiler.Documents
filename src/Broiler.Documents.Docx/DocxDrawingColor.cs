using System;
using System.Globalization;
using System.Xml.Linq;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx;

/// <summary>
/// Reads a DrawingML colour (ECMA-376 §20.1.2.3): the colour element a fill, a
/// gradient stop or an outline holds, with the transforms nested in it.
/// </summary>
/// <remarks>
/// <para>
/// A shape Word draws in its template colours names them rather than stating
/// them: <c>a:schemeClr val="accent1"</c>, resolved through the theme's colour
/// scheme (§20.1.6.2), often under a <c>a:lumMod</c> or a <c>a:shade</c>. Read as
/// <c>a:srgbClr</c> only, such a fill was no fill, and the shape went with it.
/// </para>
/// <para>
/// The transforms are applied in document order, as §20.1.2.3 lists them, and
/// the arithmetic is LibreOffice's (<c>oox/source/drawingml/color.cxx</c>),
/// measured against its rendering rather than taken on trust: <c>a:shade</c> and
/// <c>a:tint</c> scale toward black and white in linear RGB, decoded with a gamma
/// of 2.3, so accent blue 4F81BD at a shade of 50% is 3A5F8B and at a tint of 50%
/// is C2CCE1; <c>a:lumMod</c> and <c>a:lumOff</c> scale and shift the HSL
/// luminance, so the same blue at 75% is 376092. <c>a:alpha</c> is the colour's
/// opacity. Transforms this reader does not know are passed over.
/// </para>
/// </remarks>
internal static class DocxDrawingColor
{
    private const double Gamma = 2.3;

    /// <summary>
    /// The colour the first colour element inside <paramref name="parent"/>
    /// states, or false when it holds none this reader can resolve.
    /// </summary>
    public static bool TryRead(XElement? parent, DocxTheme theme, out BColor color)
    {
        color = BColor.Empty;
        if (parent is null)
            return false;

        foreach (XElement element in parent.Elements())
        {
            if (element.Name.Namespace != DocxNamespaces.Drawing)
                continue;

            if (TryBase(element, theme, out BColor baseColor))
            {
                color = Transform(baseColor, element);
                return true;
            }
        }

        return false;
    }

    private static bool TryBase(XElement element, DocxTheme theme, out BColor color)
    {
        color = BColor.Empty;
        switch (element.Name.LocalName)
        {
            case "srgbClr":
                return DocxValueReader.TryParseHexColor((string?)element.Attribute("val"), out color);
            case "sysClr":
                // The system colour as it was when the file was saved.
                return DocxValueReader.TryParseHexColor((string?)element.Attribute("lastClr"), out color);
            case "schemeClr":
                return theme.TryResolveColor((string?)element.Attribute("val"), out color);
            case "prstClr":
                return TryPreset((string?)element.Attribute("val"), out color);
            default:
                return false;
        }
    }

    private static BColor Transform(BColor color, XElement element)
    {
        double r = color.R / 255d;
        double g = color.G / 255d;
        double b = color.B / 255d;
        double alpha = color.A / 255d;

        foreach (XElement transform in element.Elements())
        {
            if (transform.Name.Namespace != DocxNamespaces.Drawing ||
                !long.TryParse((string?)transform.Attribute("val"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long raw))
            {
                continue;
            }

            double value = raw / 100000d;
            switch (transform.Name.LocalName)
            {
                case "alpha":
                    alpha = Math.Clamp(value, 0, 1);
                    break;
                case "shade":
                    value = Math.Clamp(value, 0, 1);
                    (r, g, b) = (FromLinear(ToLinear(r) * value), FromLinear(ToLinear(g) * value), FromLinear(ToLinear(b) * value));
                    break;
                case "tint":
                    value = Math.Clamp(value, 0, 1);
                    (r, g, b) = (Tint(r, value), Tint(g, value), Tint(b, value));
                    break;
                case "lumMod":
                case "lumOff":
                case "satMod":
                {
                    (double h, double s, double l) = ToHsl(r, g, b);
                    switch (transform.Name.LocalName)
                    {
                        case "lumMod": l *= value; break;
                        case "lumOff": l += value; break;
                        default: s *= value; break;
                    }

                    (r, g, b) = FromHsl(h, Math.Clamp(s, 0, 1), Math.Clamp(l, 0, 1));
                    break;
                }

                default:
                    break;
            }
        }

        return new BColor(Byte(r), Byte(g), Byte(b), Byte(alpha));
    }

    private static double Tint(double channel, double value) =>
        FromLinear(1 - ((1 - ToLinear(channel)) * value));

    private static double ToLinear(double channel) => Math.Pow(Math.Clamp(channel, 0, 1), Gamma);

    private static double FromLinear(double channel) => Math.Pow(Math.Clamp(channel, 0, 1), 1 / Gamma);

    private static byte Byte(double channel) => (byte)Math.Round(Math.Clamp(channel, 0, 1) * 255);

    private static (double H, double S, double L) ToHsl(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2;
        double delta = max - min;
        if (delta <= 0)
            return (0, 0, l);

        double s = delta / (1 - Math.Abs((2 * l) - 1));
        double h = max == r ? ((g - b) / delta) % 6
            : max == g ? ((b - r) / delta) + 2
            : ((r - g) / delta) + 4;
        h *= 60;
        if (h < 0)
            h += 360;

        return (h, s, l);
    }

    private static (double R, double G, double B) FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs((2 * l) - 1)) * s;
        double x = c * (1 - Math.Abs(((h / 60) % 2) - 1));
        double m = l - (c / 2);
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return (r + m, g + m, b + m);
    }

    /// <summary>The handful of preset colours (§20.1.10.47) a template is likely to name.</summary>
    private static bool TryPreset(string? name, out BColor color)
    {
        color = name switch
        {
            "black" => BColor.FromArgb(0, 0, 0),
            "white" => BColor.FromArgb(255, 255, 255),
            "red" => BColor.FromArgb(255, 0, 0),
            "green" => BColor.FromArgb(0, 128, 0),
            "blue" => BColor.FromArgb(0, 0, 255),
            "yellow" => BColor.FromArgb(255, 255, 0),
            "gray" => BColor.FromArgb(128, 128, 128),
            _ => BColor.Empty,
        };
        return !color.IsEmpty;
    }
}
