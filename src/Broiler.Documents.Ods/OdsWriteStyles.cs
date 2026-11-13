using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Broiler.Documents.Model.Spreadsheets;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Ods;

internal sealed class OdsWriteStyles(Action reportUnsupportedFormat)
{
    private readonly Dictionary<(string Family, double Points), string> _dimensions = [];
    private readonly Dictionary<(string? DataStyle, OdsCellAppearance Appearance), string> _cells = [];
    private readonly Dictionary<string, string?> _formats = new(StringComparer.Ordinal);

    public XElement Definitions { get; } = new(OdsNamespaces.Office + "automatic-styles");

    public XAttribute? Dimension(string family, string property, double? points)
    {
        if (points is not double length || !double.IsFinite(length) || length <= 0)
            return null;

        var key = (family, length);
        if (!_dimensions.TryGetValue(key, out string? name))
        {
            name = "dim" + _dimensions.Count.ToString(CultureInfo.InvariantCulture);
            _dimensions.Add(key, name);
            XNamespace style = OdsNamespaces.Style;
            Definitions.Add(new XElement(style + "style",
                new XAttribute(style + "name", name), new XAttribute(style + "family", family),
                new XElement(style + (family + "-properties"),
                    new XAttribute(style + property, length.ToString("0.################", CultureInfo.InvariantCulture) + "pt"))));
        }
        return Reference(name);
    }

    public XAttribute? Cell(SpreadsheetCell cell, string? fontFamily)
    {
        string? dataStyle = null;
        if (cell.NumberFormat is { Length: > 0 } format && format != "General" &&
            !_formats.TryGetValue(format, out dataStyle))
        {
            XElement? definition = OdsNumberStyles.Write(format);
            if (definition is null)
                reportUnsupportedFormat();
            else
            {
                dataStyle = "num" + _formats.Count.ToString(CultureInfo.InvariantCulture);
                definition.SetAttributeValue(OdsNamespaces.Style + "name", dataStyle);
                Definitions.Add(definition);
            }
            _formats.Add(format, dataStyle);
        }

        BColor fill = cell.Shading.A == 0 ? default : BColor.FromArgb(cell.Shading.R, cell.Shading.G, cell.Shading.B);
        var appearance = new OdsCellAppearance(fill, cell.Borders, cell.VerticalAlignment, cell.Alignment, new Broiler.Documents.Model.InlineStyle
        {
            FontFamily = fontFamily, FontSize = cell.Font.FontSize, Foreground = cell.Font.Foreground,
            Bold = cell.Font.Bold, Italic = cell.Font.Italic, Underline = cell.Font.Underline, Strikethrough = cell.Font.Strikethrough,
        });
        if (dataStyle is null && appearance == default)
            return null;

        var key = (dataStyle, appearance);
        if (!_cells.TryGetValue(key, out string? name))
        {
            name = "cell" + _cells.Count.ToString(CultureInfo.InvariantCulture);
            _cells.Add(key, name);
            XNamespace style = OdsNamespaces.Style;
            Definitions.Add(new XElement(style + "style",
                new XAttribute(style + "name", name), new XAttribute(style + "family", "table-cell"),
                dataStyle is null ? null : new XAttribute(style + "data-style-name", dataStyle),
                appearance.WriteCell(), appearance.WriteParagraph(), appearance.WriteText()));
        }
        return Reference(name);
    }

    private static XAttribute Reference(string name) => new(OdsNamespaces.Table + "style-name", name);
}
