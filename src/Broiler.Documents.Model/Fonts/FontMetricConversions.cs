using System;

namespace Broiler.Documents.Model.Fonts;

/// <summary>
/// Centralized unit conversions and metrics calculations for font measurements across
/// layout (CSS pixels), document flow / pagination (typographic points), and fixed-page output (PDF 1/1000 em).
/// </summary>
/// <remarks>
/// <para>
/// <b>Unit Definitions:</b>
/// <list type="bullet">
///   <item><term>Point (pt)</term><description>Typographic point: 1/72 inch.</description></item>
///   <item><term>CSS Pixel (px)</term><description>Standard CSS reference pixel: 1/96 inch (96 DPI). 1 pt = 4/3 px ≈ 1.333333 px; 1 px = 3/4 pt = 0.75 pt.</description></item>
///   <item><term>PDF 1/1000 em</term><description>PDF font space: 1000 units per em, as specified in ISO 32000-1 §9.2.4.</description></item>
///   <item><term>Design Unit (FUnit)</term><description>Integer grid units defined by the font's <c>head.unitsPerEm</c> (typically 1000 or 2048).</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Separation of Intrinsic Metrics vs. Line-Height Policy:</b>
/// Intrinsic metrics (ascent, descent, line gap) belong to the font face itself. Line-height policy
/// (proportional leading, CSS line-height multipliers, fixed point leading) is an layout policy
/// applied above intrinsic metrics, never baked into the face.
/// </para>
/// </remarks>
public static class FontMetricConversions
{
    /// <summary>Points per inch in standard typographic systems (PostScript / DTP).</summary>
    public const double PointsPerInch = 72.0;

    /// <summary>CSS reference pixels per inch (CSS 2.1 / CSS Values 3).</summary>
    public const double PixelsPerInch = 96.0;

    /// <summary>Ratio to convert typographic points to CSS pixels: 96 / 72 = 4 / 3.</summary>
    public const double PointsToPixelsFactor = PixelsPerInch / PointsPerInch; // 1.3333333333333333

    /// <summary>Ratio to convert CSS pixels to typographic points: 72 / 96 = 3 / 4 = 0.75.</summary>
    public const double PixelsToPointsFactor = PointsPerInch / PixelsPerInch; // 0.75

    /// <summary>Units per em in PDF font metrics coordinate space.</summary>
    public const double PdfUnitsPerEm = 1000.0;

    /// <summary>Converts typographic points to CSS pixels.</summary>
    public static double PointsToPixels(double points) => points * PointsToPixelsFactor;

    /// <summary>Converts CSS pixels to typographic points.</summary>
    public static double PixelsToPoints(double pixels) => pixels * PixelsToPointsFactor;

    /// <summary>Converts font design units (FUnits) to typographic points at a given font size.</summary>
    public static double DesignUnitsToPoints(double designUnits, double fontSizePoints, int unitsPerEm)
    {
        if (unitsPerEm <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitsPerEm), "UnitsPerEm must be positive.");

        return (designUnits * fontSizePoints) / unitsPerEm;
    }

    /// <summary>Converts font design units (FUnits) to CSS pixels at a given font size in points.</summary>
    public static double DesignUnitsToPixels(double designUnits, double fontSizePoints, int unitsPerEm)
    {
        double points = DesignUnitsToPoints(designUnits, fontSizePoints, unitsPerEm);
        return PointsToPixels(points);
    }

    /// <summary>Converts font design units (FUnits) to PDF 1/1000 em units.</summary>
    public static double DesignUnitsToPdfUnits(double designUnits, int unitsPerEm)
    {
        if (unitsPerEm <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitsPerEm), "UnitsPerEm must be positive.");

        return (designUnits * PdfUnitsPerEm) / unitsPerEm;
    }

    /// <summary>Converts PDF 1/1000 em units to typographic points at a given font size.</summary>
    public static double PdfUnitsToPoints(double pdfUnits, double fontSizePoints) =>
        (pdfUnits * fontSizePoints) / PdfUnitsPerEm;

    /// <summary>Converts typographic points to PDF 1/1000 em units at a given font size.</summary>
    public static double PointsToPdfUnits(double points, double fontSizePoints)
    {
        if (fontSizePoints <= 0)
            return 0;

        return (points * PdfUnitsPerEm) / fontSizePoints;
    }

    /// <summary>Converts PDF 1/1000 em units to CSS pixels at a given font size in points.</summary>
    public static double PdfUnitsToPixels(double pdfUnits, double fontSizePoints) =>
        PointsToPixels(PdfUnitsToPoints(pdfUnits, fontSizePoints));

    /// <summary>Rounds a CSS pixel value to the nearest physical device pixel at the given DPI.</summary>
    public static double SnapToDevicePixels(double cssPixels, double dpi = PixelsPerInch)
    {
        if (dpi <= 0)
            return cssPixels;

        double scale = dpi / PixelsPerInch;
        return Math.Round(cssPixels * scale, MidpointRounding.AwayFromZero) / scale;
    }

    /// <summary>
    /// Computes the intrinsic total line height (ascent + descent + line gap) in points.
    /// </summary>
    public static double IntrinsicLineHeightPoints(double ascentPoints, double descentPoints, double lineGapPoints) =>
        Math.Max(0, ascentPoints + descentPoints + Math.Max(0, lineGapPoints));

    /// <summary>
    /// Computes the intrinsic total line height in CSS pixels.
    /// </summary>
    public static double IntrinsicLineHeightPixels(double ascentPx, double descentPx, double lineGapPx) =>
        Math.Max(0, ascentPx + descentPx + Math.Max(0, lineGapPx));

    /// <summary>
    /// Calculates line height according to CSS line-height or document proportional line spacing policy.
    /// </summary>
    /// <param name="intrinsicHeightPoints">The intrinsic face line height (ascent + descent + gap).</param>
    /// <param name="fontSizePoints">The nominal font size in points.</param>
    /// <param name="lineSpacingMultiplier">The line height multiplier (e.g., 1.0 for single, 1.15, 1.5, 2.0).</param>
    /// <param name="useNominalFontSize">
    /// When true (CSS standard behavior), proportional line height is multiplier * fontSizePoints.
    /// When false (word processor legacy), proportional line height scales the intrinsic height.
    /// </param>
    public static double CalculateLineHeightPoints(
        double intrinsicHeightPoints,
        double fontSizePoints,
        double lineSpacingMultiplier,
        bool useNominalFontSize = true)
    {
        if (lineSpacingMultiplier <= 0)
            return intrinsicHeightPoints;

        double baseHeight = useNominalFontSize ? fontSizePoints : intrinsicHeightPoints;
        return Math.Max(0, baseHeight * lineSpacingMultiplier);
    }
}
