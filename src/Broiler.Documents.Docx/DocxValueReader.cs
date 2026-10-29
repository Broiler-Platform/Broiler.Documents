using System;
using System.Globalization;
using System.Xml.Linq;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx;

internal static class DocxValueReader
{
    internal static string? WordValue(XElement? element) => (string?)element?.Attribute(DocxNamespaces.Wordprocessing + "val");

    internal static bool TryReadInt(XAttribute? attribute, out int value) => int.TryParse((string?)attribute, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// The text of a <c>w:t</c> or <c>w:delText</c> as Word and LibreOffice show it.
    /// </summary>
    /// <remarks>
    /// A line feed in run text is a space, not a line break; a break is
    /// <c>w:br</c>. Unless <c>xml:space="preserve"</c> is in scope - on the
    /// element or the nearest ancestor that states it - white space at either
    /// end is dropped and a tab is a space (XML 1.0 §2.10; LibreOffice's
    /// <c>OOXMLFastContextHandler::text</c>, tdf#108806 and tdf#108995).
    /// The parser has already turned a written CR LF into one line feed.
    /// </remarks>
    internal static string RunText(XElement text)
    {
        string value = text.Value.Replace('\n', ' ');
        if (PreservesSpace(text))
            return value;

        return value.Trim('\t', '\n', '\r', ' ').Replace('\t', ' ');
    }

    private static bool PreservesSpace(XElement element)
    {
        for (XElement? scope = element; scope is not null; scope = scope.Parent)
        {
            if (scope.Attribute(DocxNamespaces.Xml + "space") is XAttribute space)
                return (string)space == "preserve";
        }

        return false;
    }

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
        TryReadTwips(element?.Attribute(DocxNamespaces.Wordprocessing + name), out int twips) ? twips / 20d : 0;

    /// <summary>A length in twips, written either way <see cref="TryReadMeasure"/> accepts.</summary>
    internal static bool TryReadTwips(XAttribute? attribute, out int twips) =>
        TryReadMeasure(attribute, unitsPerPoint: 20, out twips);

    /// <summary>A font size in half-points, written either way <see cref="TryReadMeasure"/> accepts.</summary>
    internal static bool TryReadHalfPoints(XAttribute? attribute, out int halfPoints) =>
        TryReadMeasure(attribute, unitsPerPoint: 2, out halfPoints);

    /// <summary>
    /// Reads a length as a whole number of the attribute's own unit: either that
    /// number written bare, or a universal measure - a decimal with a unit,
    /// <c>18pt</c>, <c>-14.15pt</c>, <c>2.5cm</c> - converted to it and rounded.
    /// </summary>
    /// <remarks>
    /// ISO/IEC 29500 lets a twips or half-point measure be written as a
    /// universal measure (<c>mm</c>, <c>cm</c>, <c>in</c>, <c>pt</c>,
    /// <c>pc</c> or <c>pi</c>). Strict files are written that way throughout,
    /// and the Transitional schema allows the same spelling, so this is read
    /// wherever such a length is rather than only in Strict files.
    /// </remarks>
    internal static bool TryReadMeasure(XAttribute? attribute, double unitsPerPoint, out int value)
    {
        if (TryReadInt(attribute, out value))
            return true;

        string? text = ((string?)attribute)?.Trim();
        if (text is null || text.Length < 3)
        {
            value = 0;
            return false;
        }

        double pointsPerUnit = text[^2..] switch
        {
            "pt" => 1,
            "pc" or "pi" => 12,
            "in" => 72,
            "cm" => 72 / 2.54,
            "mm" => 72 / 25.4,
            _ => 0,
        };

        if (pointsPerUnit == 0 ||
            !double.TryParse(text[..^2], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out double number))
        {
            value = 0;
            return false;
        }

        double units = Math.Round(number * pointsPerUnit * unitsPerPoint, MidpointRounding.AwayFromZero);
        if (double.IsNaN(units) || units < int.MinValue || units > int.MaxValue)
        {
            value = 0;
            return false;
        }

        value = (int)units;
        return true;
    }
}
