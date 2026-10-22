using System;
using System.Globalization;

namespace Broiler.Documents.Office;

internal static class RenderingThreshold
{
    // Use the weakest metric so white page area cannot hide missing or moved text.
    public static double Similarity(RenderObserved observed) => 100 * Math.Clamp(
        Math.Min(observed.InkProfileSimilarity,
            Math.Min(1 - observed.BlurDiffRatio, 1 - observed.CoverageDelta)), 0, 1);

    public static string Describe(double similarity, double threshold) =>
        "Rendering similarity " + similarity.ToString("0.##", CultureInfo.InvariantCulture) +
        "%; required " + threshold.ToString("0.##", CultureInfo.InvariantCulture) + "%.";
}
