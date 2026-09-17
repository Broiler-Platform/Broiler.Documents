using System;
using System.Globalization;
using System.Xml.Linq;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx;

internal static class DocxValueReader
{
    internal static string? WordValue(XElement? element) => (string?)element?.Attribute(DocxNamespaces.Wordprocessing + "val");

    internal static bool TryReadInt(XAttribute? attribute, out int value) => int.TryParse((string?)attribute, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    internal static bool TryParseHexColor(string? value, out BColor color)
    {
        color = BColor.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Equals("auto", StringComparison.OrdinalIgnoreCase) || value.Length != 6)
        {
            return false;
        }

        if (!int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            return false;

        color = BColor.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        return true;
    }

    /// <summary>A twip attribute in points: 20 twips to the point.</summary>
    internal static double Twips(XElement? element, string name) =>
        TryReadInt(element?.Attribute(DocxNamespaces.Wordprocessing + name), out int twips) ? twips / 20d : 0;
}
