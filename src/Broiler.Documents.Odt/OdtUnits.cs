using System;
using System.Globalization;
using Broiler.Graphics;

namespace Broiler.Documents.Odt;

/// <summary>
/// The value syntaxes ODF borrows from XSL-FO and CSS: lengths with a unit,
/// percentages, and <c>#rrggbb</c> colors. Everything this codec measures is
/// kept in points, which is what <see cref="Model.InlineStyle.FontSize"/> and the
/// paragraph spacing attributes use.
/// </summary>
internal static class OdtUnits
{
    /// <summary>
    /// A ceiling on any length read from a document. A corrupt or hostile extent
    /// would otherwise ask the layout for a line millions of units tall.
    /// </summary>
    public const double MaxLengthPoints = 20000.0;

    /// <summary>The points one indent level is written as: a quarter inch, matching the DOCX codec.</summary>
    public const double PointsPerIndentLevel = 18.0;

    /// <summary>Parses an ODF length (<c>12pt</c>, <c>0.5in</c>, <c>2cm</c>) into points.</summary>
    public static bool TryParseLength(string? value, out double points)
    {
        points = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string text = value.Trim();
        int end = 0;
        while (end < text.Length && (char.IsAsciiDigit(text[end]) || text[end] is '.' or '-' or '+'))
            end++;

        if (end == 0 ||
            !double.TryParse(text[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
            double.IsNaN(number) ||
            double.IsInfinity(number))
        {
            return false;
        }

        double scale = text[end..].Trim().ToLowerInvariant() switch
        {
            "pt" => 1,
            "in" => 72,
            "cm" => 72 / 2.54,
            "mm" => 72 / 25.4,
            "pc" => 12,
            "px" => 72.0 / 96.0,
            // A bare number is not a valid ODF length. Rejecting it keeps a
            // percentage from being read as though it were a measurement.
            _ => 0,
        };

        if (scale == 0)
            return false;

        points = Math.Clamp(number * scale, -MaxLengthPoints, MaxLengthPoints);
        return true;
    }

    /// <summary>Parses an ODF percentage (<c>150%</c>) into its multiplier (<c>1.5</c>).</summary>
    public static bool TryParsePercentage(string? value, out double multiplier)
    {
        multiplier = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string text = value.Trim();
        if (!text.EndsWith('%'))
            return false;

        if (!double.TryParse(
                text[..^1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double percent) ||
            double.IsNaN(percent) ||
            double.IsInfinity(percent))
        {
            return false;
        }

        multiplier = percent / 100.0;
        return true;
    }

    /// <summary>Parses an ODF color (<c>#rrggbb</c>). <c>transparent</c> is not a color.</summary>
    public static bool TryParseColor(string? value, out BColor color)
    {
        color = BColor.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string text = value.Trim();
        if (text.Length != 7 || text[0] != '#')
            return false;

        if (!int.TryParse(text[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            return false;

        color = BColor.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        return true;
    }

    public static string FormatColor(BColor color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:x2}{color.G:x2}{color.B:x2}");

    /// <summary>
    /// Reads an ODF angle - a number with an optional <c>deg</c>, <c>grad</c> or
    /// <c>rad</c> unit - as degrees.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A bare number is tenths of a degree, and that is not a guess about the
    /// producer: ODF 1.2 typed <c>draw:angle</c> as an integer in tenths, ODF 1.3
    /// retyped it as an angle with a unit, and both spellings are in the wild
    /// because LibreOffice changed which it writes. Reading only the older one is
    /// how <c>draw:angle="30deg"</c> came back as no angle at all - a failed parse
    /// and an absent attribute reach the same branch, so the gradient drew the
    /// wrong way round and said nothing.
    /// </para>
    /// <para>
    /// The result is degrees in ODF's own frame, which is not the model's.
    /// Converting between the two belongs to the caller that knows what the angle
    /// is measuring; this only reads the number.
    /// </para>
    /// </remarks>
    public static bool TryParseAngle(string? value, out double degrees)
    {
        degrees = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string text = value.Trim();
        (string Suffix, double PerUnit)[] units =
        [
            ("deg", 1.0),
            ("grad", 0.9),
            ("rad", 180.0 / Math.PI),
        ];

        foreach ((string suffix, double perUnit) in units)
        {
            if (!text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!double.TryParse(
                    text[..^suffix.Length].Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double amount))
            {
                return false;
            }

            degrees = amount * perUnit;
            return double.IsFinite(degrees);
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double tenths))
            return false;

        degrees = tenths / 10.0;
        return double.IsFinite(degrees);
    }

    /// <summary>Writes an angle in degrees, with the unit ODF 1.3 asks for.</summary>
    public static string FormatAngle(double degrees) =>
        Round(degrees).ToString("0.##", CultureInfo.InvariantCulture) + "deg";

    /// <summary>Formats a length in points, with the trailing zeroes an ODF consumer does not need.</summary>
    public static string FormatPoints(double points) =>
        Round(points).ToString("0.###", CultureInfo.InvariantCulture) + "pt";

    /// <summary>Formats a length in inches, which is how indents and frame extents read best.</summary>
    public static string FormatInches(double points) =>
        Round(points / 72.0).ToString("0.####", CultureInfo.InvariantCulture) + "in";

    public static string FormatPercentage(double multiplier) =>
        Round(multiplier * 100.0).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// Rounds away the last bits of a binary fraction, so a value that arrived as
    /// a float writes as <c>1.5</c> rather than <c>1.5000000596</c> and a package
    /// stays byte-for-byte reproducible.
    /// </summary>
    private static double Round(double value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}
