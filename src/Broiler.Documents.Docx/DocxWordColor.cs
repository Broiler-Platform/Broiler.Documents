using System;
using System.Globalization;
using System.Xml.Linq;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx;

/// <summary>WordprocessingML theme references and byte-valued luminance modifiers.
/// The live theme takes precedence over its cached RGB fallback.</summary>
internal static class DocxWordColor
{
    public static bool TryRead(XElement? element, DocxTheme theme, bool fill, out BColor color)
    {
        string themeAttribute = fill ? "themeFill" : "themeColor";
        string? name = (string?)element?.Attribute(DocxNamespaces.Wordprocessing + themeAttribute);
        string? slot = name switch
        {
            "dark1" => "dk1", "light1" => "lt1", "dark2" => "dk2", "light2" => "lt2",
            "text1" => "tx1", "background1" => "bg1", "text2" => "tx2", "background2" => "bg2",
            "hyperlink" => "hlink", "followedHyperlink" => "folHlink", "none" => null, _ => name,
        };
        if (theme.TryResolveColor(slot, out color))
        {
            string tonePrefix = fill ? "themeFill" : "theme";
            byte? tint = ReadByte(element, tonePrefix + "Tint");
            byte? shade = ReadByte(element, tonePrefix + "Shade");
            color = DocxDrawingColor.ApplyWordLuminance(color, tint, shade);
            return true;
        }
        string rgbAttribute = fill ? "fill" : element?.Name.LocalName == "color" ? "val" : "color";
        return DocxValueReader.TryParseHexColor((string?)element?.Attribute(DocxNamespaces.Wordprocessing + rgbAttribute), out color);
    }

    public static BColor Shading(XElement shading, DocxTheme theme)
    {
        string? pattern = DocxValueReader.WordValue(shading);
        if (pattern == "nil") return BColor.Empty;
        return TryRead(shading, theme, fill: pattern != "solid", out BColor color) ? color : BColor.Empty;
    }

    private static byte? ReadByte(XElement? element, string attribute)
    {
        string? text = ((string?)element?.Attribute(DocxNamespaces.Wordprocessing + attribute))?.Trim();
        return text?.Length == 2 && byte.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value) ? value : null;
    }
}
