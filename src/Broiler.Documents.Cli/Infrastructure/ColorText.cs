using System;
using System.Globalization;
using Broiler.Graphics;

namespace Broiler.Documents.Cli.Infrastructure;

/// <summary>
/// Reads and writes colours in the one spelling this tool uses everywhere:
/// <c>#RRGGBBAA</c>, the same straight-alpha form the Formatting Codes grammar
/// freezes. Shorter hex forms and CSS colour names are accepted on input so the
/// command line stays typeable.
/// </summary>
/// <remarks>
/// <see cref="BColor.Empty"/> is a distinct third state from transparent black
/// and is not a colour: the model uses it to mean "this run said nothing about
/// its colour", which is what lets a renderer supply a default rather than
/// painting invisible text. It round-trips through <see cref="Format"/> as
/// <c>default</c> and is never confused with <c>#00000000</c>.
/// </remarks>
public static class ColorText
{
    /// <summary>The spelling <see cref="BColor.Empty"/> takes in output and accepts on input.</summary>
    public const string DefaultToken = "default";

    /// <summary>Parses a colour, or throws a usage error naming the option that carried it.</summary>
    public static BColor Parse(string value, string optionName)
    {
        if (!TryParse(value, out BColor color))
        {
            throw new UsageException(
                optionName + " expects #RGB, #RRGGBB, #RRGGBBAA, a CSS colour name, or \"" +
                DefaultToken + "\", not \"" + value + "\".");
        }

        return color;
    }

    /// <summary>Parses a colour without throwing.</summary>
    public static bool TryParse(string? value, out BColor color)
    {
        color = BColor.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string token = value.Trim();
        if (string.Equals(token, DefaultToken, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(token, "none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!token.StartsWith('#'))
            return BColor.TryGetNamedColor(token, out color);

        string hex = token[1..];
        foreach (char character in hex)
        {
            if (!Uri.IsHexDigit(character))
                return false;
        }

        switch (hex.Length)
        {
            case 3:
                color = new BColor(Repeat(hex[0]), Repeat(hex[1]), Repeat(hex[2]));
                return true;
            case 4:
                color = new BColor(Repeat(hex[0]), Repeat(hex[1]), Repeat(hex[2]), Repeat(hex[3]));
                return true;
            case 6:
                color = new BColor(Byte(hex, 0), Byte(hex, 2), Byte(hex, 4));
                return true;
            case 8:
                color = new BColor(Byte(hex, 0), Byte(hex, 2), Byte(hex, 4), Byte(hex, 6));
                return true;
            default:
                return false;
        }
    }

    /// <summary>The canonical <c>#RRGGBBAA</c> spelling, or <c>default</c> for the unset colour.</summary>
    public static string Format(BColor color)
    {
        if (color.IsEmpty)
            return DefaultToken;

        return string.Format(
            CultureInfo.InvariantCulture,
            "#{0:X2}{1:X2}{2:X2}{3:X2}",
            color.R,
            color.G,
            color.B,
            color.A);
    }

    /// <summary><paramref name="color"/> when it is set, otherwise <paramref name="fallback"/>.</summary>
    public static BColor Or(BColor color, BColor fallback) => color.IsEmpty ? fallback : color;

    private static byte Repeat(char digit)
    {
        int value = Convert.ToInt32(digit.ToString(), 16);
        return (byte)((value << 4) | value);
    }

    private static byte Byte(string hex, int index) =>
        (byte)Convert.ToInt32(hex.Substring(index, 2), 16);
}
