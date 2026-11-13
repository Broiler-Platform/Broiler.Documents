using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Xlsx;

internal static class XlsxCellAppearance
{
    private static readonly XNamespace Ns = XlsxNamespaces.Spreadsheet;

    public static BColor ReadColor(XElement? element, XElement? theme = null, XElement? indexed = null)
    {
        BColor direct =
        (string?)element?.Attribute("rgb") is { Length: 8 } rgb &&
        uint.TryParse(rgb, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value)
            ? BColor.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : default;

        if (direct.A != 0 || element is null)
            return direct;

        if (int.TryParse((string?)element.Attribute("indexed"), out int index) && index is >= 0 and < 64)
        {
            var palette = indexed?.Elements(Ns + "rgbColor").ElementAtOrDefault(index);
            return palette is not null ? ReadColor(palette) : ReadColor(new XElement("color",
                new XAttribute("rgb", "FF" + DefaultPalette.Substring(index * 6, 6))));
        }

        if (!int.TryParse((string?)element.Attribute("theme"), out int slot) ||
            (uint)slot >= (uint)ThemeNames.Length || theme is null)
            return default;

        var color = theme.Element(theme.Name.Namespace + ThemeNames[slot])?.Elements().FirstOrDefault();
        string? hex = (string?)color?.Attribute("lastClr") ?? (string?)color?.Attribute("val");
        if (hex?.Length != 6)
            return default;
        BColor resolved = ReadColor(new XElement("color", new XAttribute("rgb", "FF" + hex)));
        if (!double.TryParse((string?)element.Attribute("tint"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out double tint) || !double.IsFinite(tint) || tint == 0)
            return resolved;

        tint = Math.Clamp(tint, -1, 1);
        double max = Math.Max(resolved.R, Math.Max(resolved.G, resolved.B)) / 255d;
        double min = Math.Min(resolved.R, Math.Min(resolved.G, resolved.B)) / 255d;
        double lightness = (max + min) / 2;
        double target = tint < 0 ? lightness * (1 + tint) : lightness + (1 - lightness) * tint;
        double chroma = 1 - Math.Abs(2 * lightness - 1);
        double scale = chroma == 0 ? 0 : (1 - Math.Abs(2 * target - 1)) / chroma;
        byte Channel(byte value) => (byte)Math.Clamp(Math.Round((target + (value / 255d - lightness) * scale) * 255), 0, 255);
        return BColor.FromArgb(Channel(resolved.R), Channel(resolved.G), Channel(resolved.B));
    }

    private static readonly string[] ThemeNames =
        ["lt1", "dk1", "lt2", "dk2", "accent1", "accent2", "accent3", "accent4", "accent5", "accent6", "hlink", "folHlink"];

    private const string DefaultPalette =
        "000000FFFFFFFF000000FF000000FFFFFF00FF00FF00FFFF000000FFFFFFFF000000FF000000FFFFFF00FF00FF00FFFF800000008000000080808000800080008080C0C0C08080809999FF993366FFFFCCCCFFFF660066FF80800066CCCCCCCC000080FF00FFFFFF0000FFFF8000808000000080800000FF00CCFFCCFFFFCCFFCCFFFF9999CCFFFF99CCCC99FFFFCC993366FF33CCCC99CC00FFCC00FF9900FF6600666699969696003366339966003300333300993300993366333399333333";

    private static XElement Color(string name, BColor color) => new(Ns + name,
        new XAttribute("rgb", string.Create(CultureInfo.InvariantCulture, $"{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}")));

    public static InlineStyle ReadFont(XElement font, XElement? theme, XElement? indexed) => new()
    {
        FontFamily = (string?)font.Element(Ns + "name")?.Attribute("val"),
        FontSize = float.TryParse((string?)font.Element(Ns + "sz")?.Attribute("val"),
            NumberStyles.Float, CultureInfo.InvariantCulture, out float size) && float.IsFinite(size) && size > 0 ? size : null,
        Bold = Enabled(font, "b"),
        Italic = Enabled(font, "i"),
        Underline = Enabled(font, "u"),
        Strikethrough = Enabled(font, "strike"),
        Foreground = ReadColor(font.Element(Ns + "color"), theme, indexed),
    };

    private static bool Enabled(XElement font, string name) =>
        font.Element(Ns + name) is { } flag && (string?)flag.Attribute("val") is not ("0" or "false" or "none");

    public static XElement WriteFont(InlineStyle font) => new(Ns + "font",
        new XElement(Ns + "name", new XAttribute("val", font.FontFamily ?? "Calibri")),
        new XElement(Ns + "sz", new XAttribute("val", font.FontSize ?? 11)),
        font.Bold ? new XElement(Ns + "b") : null,
        font.Italic ? new XElement(Ns + "i") : null,
        font.Underline ? new XElement(Ns + "u") : null,
        font.Strikethrough ? new XElement(Ns + "strike") : null,
        font.Foreground.A > 0 ? Color("color", font.Foreground) : null);

    public static CellBorders ReadBorders(XElement border, XElement? theme, XElement? indexed) => new(
        ReadEdge(border, "left", theme, indexed), ReadEdge(border, "top", theme, indexed), ReadEdge(border, "right", theme, indexed), ReadEdge(border, "bottom", theme, indexed));

    private static TableBorder ReadEdge(XElement border, string name, XElement? theme, XElement? indexed)
    {
        XElement? edge = border.Element(Ns + name);
        string? kind = (string?)edge?.Attribute("style");
        if (kind is null or "none")
            return default;

        var color = ReadColor(edge?.Element(Ns + "color"), theme, indexed);
        return new TableBorder(color.A == 0 ? BColor.Black : color, kind switch
        {
            "hair" => 0.25,
            "medium" or "mediumDashed" => 1.5,
            "thick" => 2.25,
            _ => 0.75,
        }, kind switch
        {
            "dotted" => TableBorderStyle.Dotted,
            "dashed" or "mediumDashed" or "dashDot" or "dashDotDot" or "mediumDashDot" or "mediumDashDotDot" or "slantDashDot" => TableBorderStyle.Dashed,
            "double" => TableBorderStyle.Double,
            _ => TableBorderStyle.Solid,
        });
    }

    public static XElement WriteBorders(CellBorders borders) => new(Ns + "border",
        WriteEdge("left", borders.Left), WriteEdge("right", borders.Right),
        WriteEdge("top", borders.Top), WriteEdge("bottom", borders.Bottom));

    private static XElement WriteEdge(string name, TableBorder edge) => new(Ns + name,
        edge.IsVisible ? new XAttribute("style", edge.Style switch
        {
            TableBorderStyle.Double => "double",
            TableBorderStyle.Dotted => "dotted",
            TableBorderStyle.Dashed => edge.Width >= 1.5 ? "mediumDashed" : "dashed",
            _ => edge.Width >= 2.25 ? "thick" : edge.Width >= 1.5 ? "medium" : edge.Width <= 0.25 ? "hair" : "thin",
        }) : null,
        edge.IsVisible ? Color("color", edge.Color) : null);
}
