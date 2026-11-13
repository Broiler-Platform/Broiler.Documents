using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Ods;

internal readonly record struct OdsCellAppearance(BColor Fill, CellBorders Borders,
    CellVerticalAlignment Vertical, TextAlignment Alignment, InlineStyle Font)
{
    private static readonly XNamespace Style = OdsNamespaces.Style;
    private static readonly XNamespace Fo = OdsNamespaces.Fo;

    public static OdsCellAppearance Read(XElement? style)
    {
        XElement? cell = style?.Element(Style + "table-cell-properties");
        XElement? paragraph = style?.Element(Style + "paragraph-properties");
        XElement? text = style?.Element(Style + "text-properties");
        string? border = (string?)cell?.Attribute(Fo + "border");
        TableBorder Edge(string side) => ReadBorder((string?)cell?.Attribute(Fo + ("border-" + side)) ?? border);

        return new OdsCellAppearance(
            ReadColor((string?)cell?.Attribute(Fo + "background-color")),
            new CellBorders(Edge("left"), Edge("top"), Edge("right"), Edge("bottom")),
            (string?)cell?.Attribute(Style + "vertical-align") switch
            {
                "middle" => CellVerticalAlignment.Center,
                "bottom" => CellVerticalAlignment.Bottom,
                _ => CellVerticalAlignment.Top,
            },
            (string?)paragraph?.Attribute(Fo + "text-align") switch
            {
                "center" => TextAlignment.Center,
                "right" or "end" => TextAlignment.Right,
                "justify" => TextAlignment.Justify,
                _ => TextAlignment.Left,
            },
            new InlineStyle
            {
                FontFamily = ((string?)text?.Attribute(Fo + "font-family") ?? (string?)text?.Attribute(Style + "font-name"))?.Trim('\'', '"'),
                FontSize = OdsReader.TryParseLength((string?)text?.Attribute(Fo + "font-size"), out double size)
                    && size is > 0 and <= float.MaxValue ? (float)size : null,
                Foreground = ReadColor((string?)text?.Attribute(Fo + "color")),
                Bold = (string?)text?.Attribute(Fo + "font-weight") == "bold",
                Italic = (string?)text?.Attribute(Fo + "font-style") is "italic" or "oblique",
                Underline = (string?)text?.Attribute(Style + "text-underline-style") is not (null or "none"),
                Strikethrough = (string?)text?.Attribute(Style + "text-line-through-style") is not (null or "none"),
            });
    }

    public XElement? WriteCell()
    {
        var cell = new XElement(Style + "table-cell-properties",
            Alignment == TextAlignment.Left ? null : new XAttribute(Style + "text-align-source", "fix"),
            Fill.A > 0 ? new XAttribute(Fo + "background-color", Color(Fill)) : null,
            Vertical == CellVerticalAlignment.Top ? null :
                new XAttribute(Style + "vertical-align", Vertical == CellVerticalAlignment.Center ? "middle" : "bottom"),
            WriteBorder("left", Borders.Left), WriteBorder("top", Borders.Top),
            WriteBorder("right", Borders.Right), WriteBorder("bottom", Borders.Bottom));
        return cell.HasAttributes ? cell : null;
    }

    public XElement? WriteParagraph() => Alignment == TextAlignment.Left ? null :
        new XElement(Style + "paragraph-properties",
            new XAttribute(Fo + "text-align", Alignment.ToString().ToLowerInvariant()));

    public XElement? WriteText()
    {
        var text = new XElement(Style + "text-properties",
            Font.FontFamily is { Length: > 0 } family ? new XAttribute(Fo + "font-family", family) : null,
            Font.FontSize is float size && float.IsFinite(size) && size > 0
                ? new XAttribute(Fo + "font-size", Points(size)) : null,
            Font.Foreground.A > 0 ? new XAttribute(Fo + "color", Color(Font.Foreground)) : null,
            Font.Bold ? new XAttribute(Fo + "font-weight", "bold") : null,
            Font.Italic ? new XAttribute(Fo + "font-style", "italic") : null,
            Font.Underline ? new XAttribute(Style + "text-underline-style", "solid") : null,
            Font.Strikethrough ? new XAttribute(Style + "text-line-through-style", "solid") : null);
        return text.HasAttributes ? text : null;
    }

    private static TableBorder ReadBorder(string? value)
    {
        string[] parts = value?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (parts.Length != 3 || !OdsReader.TryParseLength(parts[0], out double width) ||
            !double.IsFinite(width) || width <= 0 ||
            !Enum.TryParse(parts[1], ignoreCase: true, out TableBorderStyle style) || !Enum.IsDefined(style))
            return default;

        BColor color = ReadColor(parts[2]);
        return color.A == 0 ? default : new TableBorder(color, width, style);
    }

    private static XAttribute? WriteBorder(string side, TableBorder border) =>
        border.IsVisible && double.IsFinite(border.Width)
            ? new XAttribute(Fo + ("border-" + side),
                $"{Points(border.Width)} {border.Style.ToString().ToLowerInvariant()} {Color(border.Color)}")
            : null;

    private static string Points(double value) => string.Create(CultureInfo.InvariantCulture, $"{value:0.########}pt");
    private static string Color(BColor color) => string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");

    private static BColor ReadColor(string? color) =>
        color is { Length: 7 } && color[0] == '#' &&
        uint.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb)
            ? BColor.FromArgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb) : default;
}
