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
/// opacity. The reader also accepts scRGB/HSL bases, DrawingML preset names,
/// opacity arithmetic, hue/saturation/luminance changes and parameterless color
/// operations. Hue arithmetic clamps at the native angle boundaries. Unsupported
/// channel-specific transforms are passed over.
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
            case "scrgbClr":
                if (!Component(element, "r", out double red) || !Component(element, "g", out double green) || !Component(element, "b", out double blue))
                    return false;
                color = new BColor(Byte(FromLinear(red)), Byte(FromLinear(green)), Byte(FromLinear(blue)));
                return true;
            case "hslClr":
                if (!Angle(element, "hue", out double hue) || !Component(element, "sat", out double saturation) || !Component(element, "lum", out double luminance))
                    return false;
                (double r, double g, double b) = FromHsl(hue, saturation, luminance);
                color = new BColor(Byte(r), Byte(g), Byte(b));
                return true;
            default:
                return false;
        }
    }

    internal static BColor ApplyWordLuminance(BColor color, byte? tint, byte? shade)
    {
        if (tint is null && shade is null) return color;
        (double h, double saturation, double luminance) = ToHsl(color.R / 255d, color.G / 255d, color.B / 255d);
        if (tint is byte t) luminance = luminance * (t / 255d) + (1 - t / 255d);
        else if (shade is byte s) luminance *= s / 255d;
        (double r, double g, double b) = FromHsl(h, saturation, Math.Clamp(luminance, 0, 1));
        // WordprocessingML converts the resolved luminance back to byte
        // channels by truncation. Keep an exact identity from drifting at a
        // floating-point boundary; DrawingML uses its separate conversion.
        static byte WordByte(double channel) => (byte)Math.Clamp(Math.Floor(channel * 255 + 1e-9), 0, 255);
        return new BColor(WordByte(r), WordByte(g), WordByte(b), color.A);
    }

    private static bool Component(XElement element, string name, out double value)
    {
        bool read = long.TryParse((string?)element.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out long raw);
        value = Math.Clamp(raw / 100000d, 0, 1);
        return read;
    }

    private static bool Angle(XElement element, string name, out double value)
    {
        bool read = long.TryParse((string?)element.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out long raw);
        value = Math.Clamp(raw / 60000d, 0, 360);
        return read;
    }

    private static double Hue(double value) => ((value % 360) + 360) % 360;

    private static BColor Transform(BColor color, XElement element)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        double alpha = color.A / 255d;
        double h = 0, saturation = 0, luminance = 0;
        bool inHsl = false;
        // Keep source precision until the final model color, including the hue
        // of an initially achromatic HSL color that later gains saturation.
        if (element.Name.LocalName == "hslClr")
        {
            Angle(element, "hue", out h);
            Component(element, "sat", out saturation);
            Component(element, "lum", out luminance);
            inHsl = true;
        }
        else if (element.Name.LocalName == "scrgbClr")
        {
            Component(element, "r", out double red); Component(element, "g", out double green); Component(element, "b", out double blue);
            (r, g, b) = (FromLinear(red), FromLinear(green), FromLinear(blue));
        }
        void Rgb()
        {
            if (inHsl) (r, g, b) = FromHsl(h, saturation, luminance);
            inHsl = false;
        }
        void Hsl()
        {
            if (!inHsl) (h, saturation, luminance) = ToHsl(r, g, b);
            inHsl = true;
        }

        foreach (XElement transform in element.Elements())
        {
            if (transform.Name.Namespace != DocxNamespaces.Drawing) continue;
            string kind = transform.Name.LocalName;
            bool flag = kind is "comp" or "inv" or "gray" or "gamma" or "invGamma";
            if (!long.TryParse((string?)transform.Attribute("val"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long raw) && !flag)
                continue;
            double value = raw / 100000d;
            switch (kind)
            {
                case "alpha": alpha = Math.Clamp(value, 0, 1); break;
                case "alphaMod": alpha = Math.Clamp(alpha * value, 0, 1); break;
                case "alphaOff": alpha = Math.Clamp(alpha + value, 0, 1); break;
                case "shade":
                    Rgb(); value = Math.Clamp(value, 0, 1);
                    (r, g, b) = (FromLinear(ToLinear(r) * value), FromLinear(ToLinear(g) * value), FromLinear(ToLinear(b) * value));
                    break;
                case "tint":
                    Rgb(); value = Math.Clamp(value, 0, 1);
                    (r, g, b) = (Tint(r, value), Tint(g, value), Tint(b, value));
                    break;
                case "hue": case "hueOff": case "hueMod":
                case "sat": case "satMod": case "satOff":
                case "lum": case "lumMod": case "lumOff":
                case "comp":
                    Hsl();
                    switch (kind)
                    {
                        case "hue": h = Math.Clamp(raw / 60000d, 0, 360); break;
                        case "hueOff": h = Math.Clamp(h + raw / 60000d, 0, 360); break;
                        case "hueMod": h = Math.Clamp(h * Math.Max(0, value), 0, 360); break;
                        case "sat": saturation = value; break;
                        case "satMod": saturation *= value; break;
                        case "satOff": saturation += value; break;
                        case "lum": luminance = value; break;
                        case "lumMod": luminance *= value; break;
                        case "lumOff": luminance += value; break;
                        case "comp": h = Hue(h + 180); break;
                    }
                    saturation = Math.Clamp(saturation, 0, 1);
                    luminance = Math.Clamp(luminance, 0, 1);
                    if (kind.StartsWith("lum", StringComparison.Ordinal) && (luminance == 0 || luminance == 1)) saturation = 0;
                    break;
                case "inv":
                    Rgb();
                    (r, g, b) = (FromLinear(1 - ToLinear(r)), FromLinear(1 - ToLinear(g)), FromLinear(1 - ToLinear(b)));
                    break;
                case "gray":
                    Rgb();
                    // DrawingML's native weighted RGB grayscale conversion.
                    r = g = b = Math.Floor((r * 0.22 + g * 0.72 + b * 0.06) * 255 + 1e-9) / 255;
                    break;
                case "gamma":
                    Rgb(); (r, g, b) = (FromLinear(r), FromLinear(g), FromLinear(b));
                    break;
                case "invGamma":
                    Rgb(); (r, g, b) = (ToLinear(r), ToLinear(g), ToLinear(b));
                    break;
            }
        }
        Rgb();
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

    /// <summary>DrawingML ST_PresetColorVal names, including its dk/lt/med abbreviations.</summary>
    private static bool TryPreset(string? name, out BColor color)
    {
        color = name switch
        {
            "aliceBlue" => BColor.FromArgb(240, 248, 255),
            "antiqueWhite" => BColor.FromArgb(250, 235, 215),
            "aqua" => BColor.FromArgb(0, 255, 255),
            "aquamarine" => BColor.FromArgb(127, 255, 212),
            "azure" => BColor.FromArgb(240, 255, 255),
            "beige" => BColor.FromArgb(245, 245, 220),
            "bisque" => BColor.FromArgb(255, 228, 196),
            "black" => BColor.FromArgb(0, 0, 0),
            "blanchedAlmond" => BColor.FromArgb(255, 235, 205),
            "blue" => BColor.FromArgb(0, 0, 255),
            "blueViolet" => BColor.FromArgb(138, 43, 226),
            "brown" => BColor.FromArgb(165, 42, 42),
            "burlyWood" => BColor.FromArgb(222, 184, 135),
            "cadetBlue" => BColor.FromArgb(95, 158, 160),
            "chartreuse" => BColor.FromArgb(127, 255, 0),
            "chocolate" => BColor.FromArgb(210, 105, 30),
            "coral" => BColor.FromArgb(255, 127, 80),
            "cornflowerBlue" => BColor.FromArgb(100, 149, 237),
            "cornsilk" => BColor.FromArgb(255, 248, 220),
            "crimson" => BColor.FromArgb(220, 20, 60),
            "cyan" => BColor.FromArgb(0, 255, 255),
            "deepPink" => BColor.FromArgb(255, 20, 147),
            "deepSkyBlue" => BColor.FromArgb(0, 191, 255),
            "dimGray" => BColor.FromArgb(105, 105, 105),
            "dkBlue" => BColor.FromArgb(0, 0, 139),
            "dkCyan" => BColor.FromArgb(0, 139, 139),
            "dkGoldenrod" => BColor.FromArgb(184, 134, 11),
            "dkGray" => BColor.FromArgb(169, 169, 169),
            "dkGreen" => BColor.FromArgb(0, 100, 0),
            "dkKhaki" => BColor.FromArgb(189, 183, 107),
            "dkMagenta" => BColor.FromArgb(139, 0, 139),
            "dkOliveGreen" => BColor.FromArgb(85, 107, 47),
            "dkOrange" => BColor.FromArgb(255, 140, 0),
            "dkOrchid" => BColor.FromArgb(153, 50, 204),
            "dkRed" => BColor.FromArgb(139, 0, 0),
            "dkSalmon" => BColor.FromArgb(233, 150, 122),
            "dkSeaGreen" => BColor.FromArgb(143, 188, 143),
            "dkSlateBlue" => BColor.FromArgb(72, 61, 139),
            "dkSlateGray" => BColor.FromArgb(47, 79, 79),
            "dkTurquoise" => BColor.FromArgb(0, 206, 209),
            "dkViolet" => BColor.FromArgb(148, 0, 211),
            "dodgerBlue" => BColor.FromArgb(30, 144, 255),
            "firebrick" => BColor.FromArgb(178, 34, 34),
            "floralWhite" => BColor.FromArgb(255, 250, 240),
            "forestGreen" => BColor.FromArgb(34, 139, 34),
            "fuchsia" => BColor.FromArgb(255, 0, 255),
            "gainsboro" => BColor.FromArgb(220, 220, 220),
            "ghostWhite" => BColor.FromArgb(248, 248, 255),
            "gold" => BColor.FromArgb(255, 215, 0),
            "goldenrod" => BColor.FromArgb(218, 165, 32),
            "gray" => BColor.FromArgb(128, 128, 128),
            "green" => BColor.FromArgb(0, 128, 0),
            "greenYellow" => BColor.FromArgb(173, 255, 47),
            "honeydew" => BColor.FromArgb(240, 255, 240),
            "hotPink" => BColor.FromArgb(255, 105, 180),
            "indianRed" => BColor.FromArgb(205, 92, 92),
            "indigo" => BColor.FromArgb(75, 0, 130),
            "ivory" => BColor.FromArgb(255, 255, 240),
            "khaki" => BColor.FromArgb(240, 230, 140),
            "lavender" => BColor.FromArgb(230, 230, 250),
            "lavenderBlush" => BColor.FromArgb(255, 240, 245),
            "lawnGreen" => BColor.FromArgb(124, 252, 0),
            "lemonChiffon" => BColor.FromArgb(255, 250, 205),
            "lime" => BColor.FromArgb(0, 255, 0),
            "limeGreen" => BColor.FromArgb(50, 205, 50),
            "linen" => BColor.FromArgb(250, 240, 230),
            "ltBlue" => BColor.FromArgb(173, 216, 230),
            "ltCoral" => BColor.FromArgb(240, 128, 128),
            "ltCyan" => BColor.FromArgb(224, 255, 255),
            "ltGoldenrodYellow" => BColor.FromArgb(250, 250, 210),
            "ltGray" => BColor.FromArgb(211, 211, 211),
            "ltGreen" => BColor.FromArgb(144, 238, 144),
            "ltPink" => BColor.FromArgb(255, 182, 193),
            "ltSalmon" => BColor.FromArgb(255, 160, 122),
            "ltSeaGreen" => BColor.FromArgb(32, 178, 170),
            "ltSkyBlue" => BColor.FromArgb(135, 206, 250),
            "ltSlateGray" => BColor.FromArgb(119, 136, 153),
            "ltSteelBlue" => BColor.FromArgb(176, 196, 222),
            "ltYellow" => BColor.FromArgb(255, 255, 224),
            "magenta" => BColor.FromArgb(255, 0, 255),
            "maroon" => BColor.FromArgb(128, 0, 0),
            "medAquamarine" => BColor.FromArgb(102, 205, 170),
            "medBlue" => BColor.FromArgb(0, 0, 205),
            "medOrchid" => BColor.FromArgb(186, 85, 211),
            "medPurple" => BColor.FromArgb(147, 112, 219),
            "medSeaGreen" => BColor.FromArgb(60, 179, 113),
            "medSlateBlue" => BColor.FromArgb(123, 104, 238),
            "medSpringGreen" => BColor.FromArgb(0, 250, 154),
            "medTurquoise" => BColor.FromArgb(72, 209, 204),
            "medVioletRed" => BColor.FromArgb(199, 21, 133),
            "midnightBlue" => BColor.FromArgb(25, 25, 112),
            "mintCream" => BColor.FromArgb(245, 255, 250),
            "mistyRose" => BColor.FromArgb(255, 228, 225),
            "moccasin" => BColor.FromArgb(255, 228, 181),
            "navajoWhite" => BColor.FromArgb(255, 222, 173),
            "navy" => BColor.FromArgb(0, 0, 128),
            "oldLace" => BColor.FromArgb(253, 245, 230),
            "olive" => BColor.FromArgb(128, 128, 0),
            "oliveDrab" => BColor.FromArgb(107, 142, 35),
            "orange" => BColor.FromArgb(255, 165, 0),
            "orangeRed" => BColor.FromArgb(255, 69, 0),
            "orchid" => BColor.FromArgb(218, 112, 214),
            "paleGoldenrod" => BColor.FromArgb(238, 232, 170),
            "paleGreen" => BColor.FromArgb(152, 251, 152),
            "paleTurquoise" => BColor.FromArgb(175, 238, 238),
            "paleVioletRed" => BColor.FromArgb(219, 112, 147),
            "papayaWhip" => BColor.FromArgb(255, 239, 213),
            "peachPuff" => BColor.FromArgb(255, 218, 185),
            "peru" => BColor.FromArgb(205, 133, 63),
            "pink" => BColor.FromArgb(255, 192, 203),
            "plum" => BColor.FromArgb(221, 160, 221),
            "powderBlue" => BColor.FromArgb(176, 224, 230),
            "purple" => BColor.FromArgb(128, 0, 128),
            "red" => BColor.FromArgb(255, 0, 0),
            "rosyBrown" => BColor.FromArgb(188, 143, 143),
            "royalBlue" => BColor.FromArgb(65, 105, 225),
            "saddleBrown" => BColor.FromArgb(139, 69, 19),
            "salmon" => BColor.FromArgb(250, 128, 114),
            "sandyBrown" => BColor.FromArgb(244, 164, 96),
            "seaGreen" => BColor.FromArgb(46, 139, 87),
            "seaShell" => BColor.FromArgb(255, 245, 238),
            "sienna" => BColor.FromArgb(160, 82, 45),
            "silver" => BColor.FromArgb(192, 192, 192),
            "skyBlue" => BColor.FromArgb(135, 206, 235),
            "slateBlue" => BColor.FromArgb(106, 90, 205),
            "slateGray" => BColor.FromArgb(112, 128, 144),
            "snow" => BColor.FromArgb(255, 250, 250),
            "springGreen" => BColor.FromArgb(0, 255, 127),
            "steelBlue" => BColor.FromArgb(70, 130, 180),
            "tan" => BColor.FromArgb(210, 180, 140),
            "teal" => BColor.FromArgb(0, 128, 128),
            "thistle" => BColor.FromArgb(216, 191, 216),
            "tomato" => BColor.FromArgb(255, 99, 71),
            "turquoise" => BColor.FromArgb(64, 224, 208),
            "violet" => BColor.FromArgb(238, 130, 238),
            "wheat" => BColor.FromArgb(245, 222, 179),
            "white" => BColor.FromArgb(255, 255, 255),
            "whiteSmoke" => BColor.FromArgb(245, 245, 245),
            "yellow" => BColor.FromArgb(255, 255, 0),
            "yellowGreen" => BColor.FromArgb(154, 205, 50),
            _ => BColor.Empty,
        };
        return !color.IsEmpty;
    }
}
