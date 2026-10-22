using System;
using System.Collections.Generic;

namespace Broiler.Documents.Model;

/// <summary>Line-count constraints shared by paginated renderers.</summary>
public static class ParagraphPagination
{
    /// <summary>
    /// How many lines can stay before a break. Zero requests a fresh column;
    /// callers must relax this on an empty column if the constraints cannot fit.
    /// </summary>
    public static int LinesThatFit(IReadOnlyList<double> heights, int start, double room,
        int orphans, int widows)
    {
        int fit = 0;
        double used = 0;
        while (start + fit < heights.Count && used + heights[start + fit] <= room + 0.001)
            used += heights[start + fit++];
        int remaining = heights.Count - start;
        if (fit == remaining)
            return fit;
        if (remaining - fit < Math.Max(0, widows))
            fit = Math.Max(0, remaining - Math.Max(0, widows));
        if (start == 0 && fit < Math.Max(0, orphans))
            return 0;
        return fit;
    }
}
