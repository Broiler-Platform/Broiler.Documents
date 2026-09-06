using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
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

    /// <summary>
    /// A rule body with its nested blocks taken out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The nesting has to go before the declarations are split, not after.
    /// <see cref="ParseDeclarations"/> divides on semicolons and then on the
    /// first colon, and a nested block carries both - so
    /// <c>@top-center { content: "x" } margin: 18pt</c> parses as one declaration
    /// whose name is everything up to <c>content</c>, and the margin after it
    /// disappears. The rule was read, the size came back, and the margin silently
    /// did not: exactly the failure this guard exists to stop.
    /// </para>
    /// <para>
    /// A block takes the text before it back to the previous semicolon, because
    /// what precedes it is the nested rule's own selector rather than a
    /// declaration.
    /// </para>
    /// <para>
    /// It lives beside the splitter rather than beside either caller because both
    /// callers meet the same trap and neither owns it: an <c>@page</c> rule nests
    /// margin boxes, and a type rule nests whatever CSS nesting puts in one. The
    /// guard was written once for the first of those and had to be found again by
    /// the second, which is the argument for it being here.
    /// </para>
    /// </remarks>
    public static string WithoutNestedBlocks(string body)
    {
        if (body.IndexOf('{') < 0)
            return body;

        var kept = new StringBuilder(body.Length);
        for (int index = 0; index < body.Length; index++)
        {
            if (body[index] != '{')
            {
                kept.Append(body[index]);
                continue;
            }

            int selector = kept.Length;
            while (selector > 0 && kept[selector - 1] != ';')
                selector--;

            kept.Length = selector;

            int depth = 0;
            for (; index < body.Length; index++)
            {
                if (body[index] == '{')
                    depth++;
                else if (body[index] == '}' && --depth == 0)
                    break;
            }
        }

        return kept.ToString();
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

    /// <summary>
    /// A CSS length in points.
    /// </summary>
    /// <remarks>
    /// The absolute units - <c>in</c>, <c>cm</c>, <c>mm</c>, <c>pc</c>, <c>q</c> -
    /// are here because a word processor writing HTML reaches for them first. Every
    /// length LibreOffice emits is in centimetres, and without them this returned
    /// false for all of them: not a wrong number, but no number, so a margin
    /// stated in a document simply did not arrive. A parser that claims to read
    /// CSS lengths and knows only the four a hand-written page tends to use is
    /// claiming too much.
    /// </remarks>
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
        else if (trimmed.EndsWith("in", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^2].Trim();
            multiplier = 72f;
        }
        else if (trimmed.EndsWith("cm", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^2].Trim();
            multiplier = 72f / 2.54f;
        }
        else if (trimmed.EndsWith("mm", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^2].Trim();
            multiplier = 72f / 25.4f;
        }
        else if (trimmed.EndsWith("pc", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^2].Trim();
            multiplier = 12f;
        }
        else if (trimmed.EndsWith("q", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^1].Trim();
            multiplier = 72f / 101.6f;
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
        value.EndsWith("rem", StringComparison.Ordinal) ||
        value.EndsWith("in", StringComparison.Ordinal) ||
        value.EndsWith("cm", StringComparison.Ordinal) ||
        value.EndsWith("mm", StringComparison.Ordinal) ||
        value.EndsWith("pc", StringComparison.Ordinal) ||
        value.EndsWith("q", StringComparison.Ordinal);

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
