using System;
using Broiler.Graphics.Geometry;

namespace Broiler.Documents.Pagination.Drawing;

/// <summary>
/// Handles exact unit conversion and scale transformations between CSS pixels (96 DPI)
/// and typographical page points (72 DPI).
/// </summary>
public static class CssPointsBridge
{
    /// <summary>Standard CSS pixel density (96 pixels per inch).</summary>
    public const double CssDpi = 96.0;

    /// <summary>Standard typographical points per inch (72 points per inch).</summary>
    public const double PointsPerInch = 72.0;

    /// <summary>Scale factor to convert CSS pixels to typographical points (72 / 96 = 0.75).</summary>
    public const double PixelsToPointsFactor = 72.0 / 96.0;

    /// <summary>Scale factor to convert typographical points to CSS pixels (96 / 72 = 1.3333333333333333).</summary>
    public const double PointsToPixelsFactor = 96.0 / 72.0;

    /// <summary>Converts CSS pixels to page typographical points.</summary>
    public static double PixelsToPoints(double pixels) =>
        pixels * PixelsToPointsFactor;

    /// <summary>Converts page typographical points to CSS pixels.</summary>
    public static double PointsToPixels(double points) =>
        points * PointsToPixelsFactor;

    /// <summary>Converts a 2D coordinate in CSS pixels to page typographical points.</summary>
    public static BPoint PointToPoints(double xPixels, double yPixels) =>
        new(PixelsToPoints(xPixels), PixelsToPoints(yPixels));

    /// <summary>Converts dimensions in CSS pixels to typographical points.</summary>
    public static BSize SizeToPoints(double widthPixels, double heightPixels) =>
        new(PixelsToPoints(widthPixels), PixelsToPoints(heightPixels));

    /// <summary>Converts a rectangle in CSS pixels to typographical points.</summary>
    public static BRect RectToPoints(double x, double y, double width, double height) =>
        new(PixelsToPoints(x), PixelsToPoints(y), PixelsToPoints(width), PixelsToPoints(height));
}
