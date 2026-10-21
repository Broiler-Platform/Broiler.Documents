using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx;

/// <summary>
/// The parts of VML (ECMA-376 Part 4, §19) a DOCX still carries where
/// DrawingML has no say: the page background, and the shapes of documents
/// written before Word 2010 or by producers that only know <c>w:pict</c>.
/// </summary>
/// <remarks>
/// <para>
/// VML states a fill as two colours and a type on <c>v:fill</c>, and leaves
/// what the numbers mean to its own conventions: its angle runs
/// counter-clockwise from the bottom where DrawingML's runs clockwise from the
/// left, its first colour is at the end of the gradient rather than the start,
/// and a focus of 50% mirrors the gradient. The conversion here is
/// LibreOffice's (<c>oox/source/vml/vmlformatting.cxx</c>,
/// <c>FillModel::pushToPropMap</c>), which is what the comparison draws, so the
/// two agree on which colour lands in which corner.
/// </para>
/// <para>
/// A fill the model cannot hold - VML's rectangular "radial" gradient, a
/// picture, a pattern of two colours - is held as the one colour that covers
/// most of the box, and says so.
/// </para>
/// </remarks>
internal static class DocxVml
{
    /// <summary>
    /// The page background a document states in <c>w:background</c>
    /// (ECMA-376 §17.2.1), or null when it states none or the document does not
    /// turn background display on.
    /// </summary>
    /// <remarks>
    /// <c>w:displayBackgroundShape</c> (§17.15.1.25) is what says the background
    /// is shown at all; Word leaves a page colour it does not display in the file,
    /// and LibreOffice's import reads neither the colour nor the shape without
    /// it. The VML shape inside is the background Word draws; <c>w:color</c> is the
    /// colour it falls back to, and the whole background where there is no shape.
    /// </remarks>
    public static ShapeFill? ReadBackground(XElement? background, bool display, Action<string, string> report)
    {
        if (background is null || !display)
            return null;

        DocxValueReader.TryParseHexColor(
            (string?)background.Attribute(DocxNamespaces.Wordprocessing + "color"),
            out BColor fallback);

        XElement? shape = background.Element(DocxNamespaces.Vml + "background");
        if (shape is not null)
            return ReadFill(shape, fallback, report, "docx.background.fill", "page background");

        return fallback.IsEmpty ? null : ShapeFill.Solid(fallback);
    }

    /// <summary>
    /// The fill a VML shape states with its <c>fillcolor</c> and <c>filled</c>
    /// attributes and its <c>v:fill</c> child, or null for a shape that is not
    /// filled. <paramref name="fallback"/> is the colour to use where the fill is a
    /// picture this reader cannot hold, and may be empty.
    /// </summary>
    public static ShapeFill? ReadFill(
        XElement shape,
        BColor fallback,
        Action<string, string> report,
        string code,
        string what)
    {
        XElement? fill = shape.Element(DocxNamespaces.Vml + "fill");
        if (IsOff((string?)shape.Attribute("filled")) || IsOff((string?)fill?.Attribute("on")))
            return null;

        // VML's default fill colour is white (§19.1.2.9, fillcolor), and its
        // second colour defaults to the first.
        BColor first = TryColor((string?)shape.Attribute("fillcolor"), out BColor stated)
            ? stated
            : TryColor((string?)fill?.Attribute("color"), out BColor own) ? own : BColor.FromArgb(255, 255, 255);
        BColor second = TryColor((string?)fill?.Attribute("color2"), out BColor other) ? other : first;
        string type = ((string?)fill?.Attribute("type"))?.Trim() ?? "solid";

        switch (type)
        {
            case "gradient":
            {
                string? angleText = (string?)fill!.Attribute("angle");
                double angle = TryNumber(angleText, out double stated2) ? stated2 : 0;
                double focus = TryFraction((string?)fill.Attribute("focus"), out double f) ? f : 0;
                bool negativeAngle = angle < 0;
                double vmlAngle = ((angle % 360) + 360) % 360;
                double modelAngle = (((90 - vmlAngle) % 360) + 360) % 360;

                if ((focus >= -0.75 && focus <= -0.25) || (focus >= 0.25 && focus <= 0.75))
                {
                    // A focus near 50% mirrors the gradient: the first colour at
                    // both edges for a positive focus, the second for a negative
                    // one, and the other way round again for an angle stated as
                    // negative.
                    bool outerIsFirst = focus > 0;
                    if (negativeAngle)
                        outerIsFirst = !outerIsFirst;

                    return new ShapeFill(
                        outerIsFirst ? first : second,
                        outerIsFirst ? second : first,
                        modelAngle,
                        Axial: true);
                }

                // VML starts a linear gradient from its second colour; a focus
                // of 100% reverses it, and so does a negative angle.
                bool swap = true;
                if (focus < -0.5 || focus > 0.5)
                    swap = !swap;
                if (negativeAngle)
                    swap = !swap;

                return new ShapeFill(swap ? second : first, swap ? first : second, modelAngle);
            }

            case "gradientRadial":
            {
                // A rectangular gradient from a focus point, which two stops along
                // a line cannot draw. From a focus inside the box it reaches its
                // outer colour within half the box, so the outer colour is most of
                // what is seen. Which colour is outer follows the focus as
                // LibreOffice reads it: from 0% to 50% either way the first colour
                // is outside, and beyond that the second.
                double focus = TryFraction((string?)fill!.Attribute("focus"), out double f) ? f : 0;
                bool firstIsOuter = focus >= -0.5 && focus <= 0.5;
                report(code, "A VML rectangular gradient on a " + what + " was drawn in its outer colour.");
                return ShapeFill.Solid(firstIsOuter ? first : second);
            }

            case "pattern":
                // An eight-by-eight pattern of the two colours. One that paints
                // both the same is that colour, exactly.
                if (first == second)
                    return ShapeFill.Solid(first);

                report(code, "A VML pattern fill on a " + what + " was drawn in one colour.");
                return ShapeFill.Solid(fallback.IsEmpty ? second : fallback);

            case "tile":
            case "frame":
                report(code, "A VML picture fill on a " + what + " was drawn in one colour.");
                return ShapeFill.Solid(fallback.IsEmpty ? first : fallback);

            default:
                return ShapeFill.Solid(first);
        }
    }

    /// <summary>
    /// A VML colour: <c>#rrggbb</c>, <c>#rgb</c>, or one of the sixteen named
    /// colours VML takes from HTML 4. Word writes a palette index after the value
    /// (<c>#4f81bd [3204]</c>), which is dropped. A system colour or a colour
    /// computed from another (<c>fill darken(118)</c>) is not read.
    /// </summary>
    public static bool TryColor(string? value, out BColor color)
    {
        color = BColor.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string text = value.Trim();
        int space = text.IndexOf(' ', StringComparison.Ordinal);
        if (space > 0)
            text = text[..space];

        if (text.StartsWith('#'))
        {
            string hex = text[1..];
            if (hex.Length == 3)
                hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
            return DocxValueReader.TryParseHexColor(hex, out color);
        }

        if (NamedColors.TryGetValue(text, out int rgb))
        {
            color = BColor.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return true;
        }

        return false;
    }

    /// <summary>
    /// A VML shape's CSS <c>style</c> attribute as property and value.
    /// </summary>
    public static Dictionary<string, string> ReadStyle(string? style)
    {
        var declarations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(style))
            return declarations;

        foreach (string declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = declaration.IndexOf(':');
            if (colon > 0)
                declarations[declaration[..colon].Trim()] = declaration[(colon + 1)..].Trim();
        }

        return declarations;
    }

    /// <summary>
    /// A CSS length in a VML style, in points, sign kept: a shape's margins move
    /// it up and left as often as down and right. A bare number is in points,
    /// VML's own unit.
    /// </summary>
    public static bool TryLength(string? value, out double points)
    {
        points = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string text = value.Trim();
        int end = 0;
        while (end < text.Length && (char.IsAsciiDigit(text[end]) || text[end] is '.' or '-' or '+'))
            end++;

        if (end == 0 || !TryNumber(text[..end], out double number))
            return false;

        double? converted = text[end..].Trim().ToLowerInvariant() switch
        {
            "pt" or "" => number,
            "in" => number * 72,
            "cm" => number * 72 / 2.54,
            "mm" => number * 72 / 25.4,
            "pc" => number * 12,
            "px" => number * 72 / 96,
            _ => null,
        };
        if (converted is not double result)
            return false;

        points = result;
        return true;
    }

    private static readonly Dictionary<string, int> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = 0x000000,
        ["silver"] = 0xC0C0C0,
        ["gray"] = 0x808080,
        ["white"] = 0xFFFFFF,
        ["maroon"] = 0x800000,
        ["red"] = 0xFF0000,
        ["purple"] = 0x800080,
        ["fuchsia"] = 0xFF00FF,
        ["green"] = 0x008000,
        ["lime"] = 0x00FF00,
        ["olive"] = 0x808000,
        ["yellow"] = 0xFFFF00,
        ["navy"] = 0x000080,
        ["blue"] = 0x0000FF,
        ["teal"] = 0x008080,
        ["aqua"] = 0x00FFFF,
    };

    private static bool IsOff(string? value) =>
        value is not null && value.Trim() is "f" or "false" or "False" or "0";

    private static bool TryNumber(string? value, out double number) =>
        double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) &&
        double.IsFinite(number);

    /// <summary>
    /// A VML fraction: <c>50%</c>, or a 16.16 fixed-point number written with an
    /// <c>f</c> (<c>32768f</c>), or a plain number.
    /// </summary>
    private static bool TryFraction(string? value, out double fraction)
    {
        fraction = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string text = value.Trim();
        if (text.EndsWith('%') && TryNumber(text[..^1], out double percent))
        {
            fraction = percent / 100;
            return true;
        }

        if (text.EndsWith('f') && TryNumber(text[..^1], out double fixedPoint))
        {
            fraction = fixedPoint / 65536;
            return true;
        }

        return TryNumber(text, out fraction);
    }
}
