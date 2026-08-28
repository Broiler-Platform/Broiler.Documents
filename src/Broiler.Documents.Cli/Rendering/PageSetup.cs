using System;
using System.Collections.Generic;
using System.Globalization;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Graphics;

namespace Broiler.Documents.Cli.Rendering;

/// <summary>
/// The page box a render lays text into: paper size, margins, and the scale that
/// turns the layout's points into image pixels.
/// </summary>
/// <remarks>
/// <para>
/// Everything in the layout is measured in <b>points</b>, because that is what
/// the document model is measured in: <c>InlineStyle.FontSize</c>,
/// <c>ParagraphStyle.SpacingBefore</c>, <c>SpacingAfter</c>, and
/// <c>InlineImage.Width</c> are all points already. Converting once, at the
/// device boundary, keeps a rounding step out of every measurement.
/// </para>
/// <para>
/// The document model carries no page geometry at all - it is an ordered list of
/// paragraphs, and neither the DOCX reader nor any other reader brings section
/// properties across. So the page is entirely the caller's choice here, and two
/// renders are only comparable when they were given the same one. That is why
/// the geometry is echoed into the render manifest rather than assumed.
/// </para>
/// </remarks>
public sealed class PageSetup
{
    /// <summary>Points per inch. The definition, not an approximation.</summary>
    public const double PointsPerInch = 72.0;

    /// <summary>CSS reference pixels per inch, for <c>px</c> values and the default DPI.</summary>
    public const double PixelsPerInch = 96.0;

    private static readonly Dictionary<string, (double WidthMillimetres, double HeightMillimetres)> NamedSizes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["a3"] = (297, 420),
            ["a4"] = (210, 297),
            ["a5"] = (148, 210),
            ["b5"] = (176, 250),
            ["letter"] = (215.9, 279.4),
            ["legal"] = (215.9, 355.6),
            ["tabloid"] = (279.4, 431.8),
            ["executive"] = (184.15, 266.7),
        };

    private PageSetup(
        double widthPoints,
        double heightPoints,
        double marginTop,
        double marginRight,
        double marginBottom,
        double marginLeft,
        double dpi,
        BColor background,
        bool continuous)
    {
        WidthPoints = widthPoints;
        HeightPoints = heightPoints;
        MarginTopPoints = marginTop;
        MarginRightPoints = marginRight;
        MarginBottomPoints = marginBottom;
        MarginLeftPoints = marginLeft;
        Dpi = dpi;
        Background = background;
        Continuous = continuous;
    }

    public double WidthPoints { get; }

    public double HeightPoints { get; }

    public double MarginTopPoints { get; }

    public double MarginRightPoints { get; }

    public double MarginBottomPoints { get; }

    public double MarginLeftPoints { get; }

    /// <summary>Output resolution. 96 makes one point 1.333 pixels, the screen convention.</summary>
    public double Dpi { get; }

    public BColor Background { get; }

    /// <summary>
    /// True when the page grows to fit the content instead of paginating.
    /// </summary>
    /// <remarks>
    /// Worth reaching for in a comparison harness: with pagination on, one extra
    /// line near a page break shifts every later page and turns a one-line
    /// difference into a whole-document difference. One tall image localizes the
    /// difference to where it actually is.
    /// </remarks>
    public bool Continuous { get; }

    /// <summary>The scale from layout points to device pixels.</summary>
    public double DpiScale => Dpi / PointsPerInch;

    public double ContentLeftPoints => MarginLeftPoints;

    public double ContentTopPoints => MarginTopPoints;

    public double ContentWidthPoints => Math.Max(1.0, WidthPoints - MarginLeftPoints - MarginRightPoints);

    public double ContentHeightPoints => Math.Max(1.0, HeightPoints - MarginTopPoints - MarginBottomPoints);

    /// <summary>A4 at 96 DPI with 1 inch margins on white.</summary>
    public static PageSetup Default { get; } = new(
        595.276, 841.89, 72, 72, 72, 72, PixelsPerInch, BColor.White, continuous: false);

    /// <summary>Every named paper size, for help text.</summary>
    public static IEnumerable<string> NamedSizeNames => NamedSizes.Keys;

    /// <summary>Builds the page box from the render options on a command line.</summary>
    public static PageSetup FromCommandLine(CommandLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        (double width, double height) = ParsePageSize(line.Get("page-size", "a4")!);
        if (line.Has("landscape"))
            (width, height) = (height, width);

        (double top, double right, double bottom, double left) = ParseMargins(line.Get("margin", "1in")!);

        double dpi = line.GetDouble("dpi", PixelsPerInch);
        if (dpi < 1 || dpi > 2400)
            throw new UsageException("--dpi must be between 1 and 2400.");

        BColor background = ColorText.Parse(line.Get("background", "#FFFFFF")!, "--background");

        // Margins that overlap leave no content column at all. Catching it here
        // names the option that is wrong; letting it through produces a page of
        // background with no explanation.
        if (left + right >= width)
            throw new UsageException("--margin leaves no horizontal room on the page.");
        if (!line.Has("continuous") && top + bottom >= height)
            throw new UsageException("--margin leaves no vertical room on the page.");

        return new PageSetup(width, height, top, right, bottom, left, dpi, background, line.Has("continuous"));
    }

    /// <summary>The same box with a different height, for the continuous single-page form.</summary>
    public PageSetup WithHeight(double heightPoints) => new(
        WidthPoints,
        Math.Max(heightPoints, MarginTopPoints + MarginBottomPoints + 1),
        MarginTopPoints,
        MarginRightPoints,
        MarginBottomPoints,
        MarginLeftPoints,
        Dpi,
        Background,
        Continuous);

    /// <summary>Pixel width of the rendered image at <see cref="Dpi"/>.</summary>
    public int PixelWidth => (int)Math.Ceiling(WidthPoints * DpiScale);

    /// <summary>Pixel height of the rendered image at <see cref="Dpi"/>.</summary>
    public int PixelHeight => (int)Math.Ceiling(HeightPoints * DpiScale);

    /// <summary>
    /// Parses a page size: a named paper size, or explicit dimensions such as
    /// <c>210x297mm</c>, <c>8.5x11in</c>, or <c>612x792pt</c>.
    /// </summary>
    public static (double WidthPoints, double HeightPoints) ParsePageSize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string token = value.Trim();

        if (NamedSizes.TryGetValue(token, out (double Width, double Height) millimetres))
            return (MillimetresToPoints(millimetres.Width), MillimetresToPoints(millimetres.Height));

        int separator = token.IndexOf('x', StringComparison.OrdinalIgnoreCase);
        if (separator <= 0)
        {
            throw new UsageException(
                "--page-size expects a paper name (" + string.Join(", ", NamedSizes.Keys) +
                ") or dimensions such as 210x297mm, not \"" + value + "\".");
        }

        string widthToken = token[..separator];
        string heightToken = token[(separator + 1)..];

        // The unit is written once, on the height: "210x297mm". Allowing it on
        // the width too would let "210mm x 297in" through.
        string unit = ExtractUnit(heightToken, out string heightNumber);
        double width = ToPoints(ParseNumber(widthToken, "--page-size"), unit, "--page-size");
        double height = ToPoints(ParseNumber(heightNumber, "--page-size"), unit, "--page-size");

        if (width <= 0 || height <= 0)
            throw new UsageException("--page-size dimensions must be positive.");

        return (width, height);
    }

    /// <summary>
    /// Parses a margin: one value for all four sides, two for vertical and
    /// horizontal, or four in CSS order (top, right, bottom, left).
    /// </summary>
    public static (double Top, double Right, double Bottom, double Left) ParseMargins(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        double[] points = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            points[i] = ParseLength(parts[i], "--margin");

        foreach (double point in points)
        {
            if (point < 0)
                throw new UsageException("--margin cannot be negative.");
        }

        return points.Length switch
        {
            1 => (points[0], points[0], points[0], points[0]),
            2 => (points[0], points[1], points[0], points[1]),
            4 => (points[0], points[1], points[2], points[3]),
            _ => throw new UsageException(
                "--margin expects 1, 2, or 4 comma-separated lengths, not " + parts.Length + "."),
        };
    }

    /// <summary>Parses one length with an optional unit suffix; a bare number is points.</summary>
    public static double ParseLength(string value, string optionName)
    {
        ArgumentNullException.ThrowIfNull(value);
        string unit = ExtractUnit(value.Trim(), out string number);
        return ToPoints(ParseNumber(number, optionName), unit, optionName);
    }

    private static double MillimetresToPoints(double millimetres) => millimetres / 25.4 * PointsPerInch;

    private static string ExtractUnit(string token, out string number)
    {
        foreach (string unit in new[] { "mm", "cm", "in", "pt", "px" })
        {
            if (token.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
            {
                number = token[..^unit.Length].Trim();
                return unit.ToLowerInvariant();
            }
        }

        number = token;
        return "pt";
    }

    private static double ParseNumber(string token, string optionName)
    {
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
            double.IsNaN(parsed) || double.IsInfinity(parsed))
        {
            throw new UsageException(optionName + " expects a finite number, not \"" + token + "\".");
        }

        return parsed;
    }

    private static double ToPoints(double value, string unit, string optionName) => unit switch
    {
        "pt" => value,
        "in" => value * PointsPerInch,
        "mm" => MillimetresToPoints(value),
        "cm" => MillimetresToPoints(value * 10),
        "px" => value * PointsPerInch / PixelsPerInch,
        _ => throw new UsageException(optionName + " does not understand the unit \"" + unit + "\"."),
    };
}
