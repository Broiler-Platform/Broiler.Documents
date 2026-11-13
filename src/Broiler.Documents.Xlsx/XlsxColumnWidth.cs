using System;

namespace Broiler.Documents.Xlsx;

internal static class XlsxColumnWidth
{
    // Calibri 11, the writer's default font, has a seven-pixel maximum digit width at 96 dpi.
    private const double MaximumDigitWidth = 7;
    private const double PointsPerPixel = 0.75;
    private const double UnitsPerCharacter = 256;

    public static double ToPoints(double width, double digitWidth = MaximumDigitWidth) =>
        Math.Truncate(Math.Clamp(width, 0, 255) * digitWidth +
            Math.Truncate(128 / digitWidth) * digitWidth / UnitsPerCharacter) * PointsPerPixel;

    public static double FromPoints(double points) =>
        Math.Truncate(Math.Clamp(Math.Round(points / PointsPerPixel) / MaximumDigitWidth, 0, 255)
            * UnitsPerCharacter) / UnitsPerCharacter;
}
