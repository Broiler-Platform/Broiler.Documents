using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using Broiler.Graphics;

namespace Broiler.Documents.Html;

internal static class HtmlCss
{
    public static IReadOnlyDictionary<string, string> ParseDeclarations(string? style)
    {
        var declarations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(style))
            return declarations;

        string decoded = WebUtility.HtmlDecode(style) ?? string.Empty;
        string[] parts = decoded.Split(';');
        foreach (string part in parts)
        {
            int colon = part.IndexOf(':');
            if (colon <= 0)
                continue;

            string name = part[..colon].Trim().ToLowerInvariant();
            string value = part[(colon + 1)..].Trim();
            if (name.Length > 0 && value.Length > 0)
                declarations[name] = value;
        }

        return declarations;
    }

    public static bool TryParseColor(string? value, out BColor color)
    {
        color = BColor.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim().Trim('"', '\'');
        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            color = BColor.Transparent;
            return true;
        }

        if (value.StartsWith('#'))
            return TryParseHexColor(value, out color);

        if (value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) &&
            value.EndsWith(")", StringComparison.Ordinal))
        {
            string inner = value[4..^1];
            string[] components = inner.Split(',');
            if (components.Length >= 3 &&
                TryParseByte(components[0], out byte r) &&
                TryParseByte(components[1], out byte g) &&
                TryParseByte(components[2], out byte b))
            {
                color = BColor.FromArgb(r, g, b);
                return true;
            }
        }

        if (BColor.TryGetNamedColor(value, out color))
            return true;

        color = BColor.Empty;
        return false;
    }

    public static string? ParseFontFamily(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string first = value.Split(',')[0].Trim().Trim('"', '\'');
        return first.Length == 0 ? null : first;
    }

    public static bool TryParseFontSize(string? value, out float size)
    {
        size = 0;
        return TryParsePoints(value, out size);
    }

    public static bool TryParsePoints(string? value, out float points)
    {
        points = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string trimmed = value.Trim().ToLowerInvariant();
        float multiplier = 1f;
        if (trimmed.EndsWith("px", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^2].Trim();
            multiplier = 72f / 96f;
        }
        else if (trimmed.EndsWith("pt", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^2].Trim();
        }
        else if (trimmed.EndsWith("rem", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^3].Trim();
            multiplier = 12f;
        }
        else if (trimmed.EndsWith("em", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^2].Trim();
            multiplier = 12f;
        }

        if (!float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float valueNumber))
            return false;

        points = Math.Max(0, valueNumber * multiplier);
        return true;
    }

    public static bool TryParseLineSpacing(string? value, out float spacing)
    {
        spacing = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string trimmed = value.Trim().ToLowerInvariant();
        if (trimmed.Equals("normal", StringComparison.Ordinal))
        {
            spacing = 1f;
            return true;
        }

        if (trimmed.EndsWith("%", StringComparison.Ordinal) &&
            float.TryParse(trimmed[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out float percent))
        {
            spacing = Math.Max(0, percent / 100f);
            return true;
        }

        if (TryParsePoints(trimmed, out float points) && HasLengthUnit(trimmed))
        {
            spacing = Math.Max(0, points / 12f);
            return true;
        }

        if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float multiplier))
        {
            spacing = Math.Max(0, multiplier);
            return true;
        }

        return false;
    }

    public static string FormatColor(BColor color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");

    public static string FormatPoints(float points) =>
        points.ToString("0.###", CultureInfo.InvariantCulture) + "pt";

    private static bool HasLengthUnit(string value) =>
        value.EndsWith("px", StringComparison.Ordinal) ||
        value.EndsWith("pt", StringComparison.Ordinal) ||
        value.EndsWith("em", StringComparison.Ordinal) ||
        value.EndsWith("rem", StringComparison.Ordinal);

    private static bool TryParseHexColor(string value, out BColor color)
    {
        color = BColor.Empty;
        string hex = value[1..];
        if (hex.Length == 3)
        {
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
        }

        if (hex.Length != 6 ||
            !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            return false;
        }

        color = BColor.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        return true;
    }

    private static bool TryParseByte(string value, out byte result)
    {
        result = 0;
        string trimmed = value.Trim();
        if (trimmed.EndsWith("%", StringComparison.Ordinal))
        {
            if (!float.TryParse(trimmed[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out float percent))
                return false;
            result = (byte)Math.Clamp((int)Math.Round(percent * 255f / 100f), 0, 255);
            return true;
        }

        if (!float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
            return false;

        result = (byte)Math.Clamp((int)Math.Round(number), 0, 255);
        return true;
    }
}
