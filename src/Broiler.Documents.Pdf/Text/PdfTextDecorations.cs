using System;
using System.Collections.Generic;

namespace Broiler.Documents.Pdf.Text;

/// <summary>
/// Reads underline and strikethrough back off the rules a page painted under and
/// through its text.
/// </summary>
/// <remarks>
/// <para>
/// PDF has no text-decoration operator. A producer that underlines a word sets
/// the word and then strokes a line beneath it, and nothing but their
/// coordinates relates the two. Every reader that reports underlined text at all
/// infers it from geometry; the alternative is not a safer answer but a document
/// where the formatting is simply absent and the rules are reported as dropped
/// artwork instead.
/// </para>
/// <para>
/// The inference is narrow on purpose, because the shape it looks for — a thin
/// horizontal bar — is also the shape of every table rule on the page. Three
/// things have to hold. The lines a grid was read from are excluded outright,
/// before any geometry is considered. The bar has to sit within a fraction of
/// the font size of a run's baseline, measured in ems so that the same rule
/// applies to a footnote and a heading. And the runs it sits under have to
/// account for most of its length: a bar that runs the width of the page with
/// one short word above it is a rule, and a bar that stops where the word stops
/// is an underline.
/// </para>
/// <para>
/// Where the bar lands decides which decoration it is. Below the baseline, or
/// barely above it, is an underline; a tenth of the em or more above it crosses
/// the glyphs and is a strikethrough. Both are properties the shared model
/// already carries, so neither smuggles a coordinate into a logical document.
/// </para>
/// </remarks>
internal static class PdfTextDecorations
{
    /// <summary>Highest above the baseline, in ems, a bar may sit and still decorate a run.</summary>
    private const double HighestOffset = 0.50;

    /// <summary>Lowest below it, in ems, the same.</summary>
    private const double LowestOffset = -0.35;

    /// <summary>Above this, in ems, the bar crosses the glyphs rather than running under them.</summary>
    private const double StrikeOffset = 0.10;

    /// <summary>The thickest bar, in ems, that is a decoration rather than a panel edge.</summary>
    private const double MaxThickness = 0.12;

    /// <summary>How much of a run a bar must span before it decorates it.</summary>
    private const double MinimumRunShare = 0.5;

    /// <summary>How much of a bar the runs it decorates must account for.</summary>
    private const double MinimumBarShare = 0.5;

    /// <summary>
    /// Marks the fragments this page's rules decorate, and reports which paths
    /// were read that way.
    /// </summary>
    /// <returns>
    /// A flag per entry of <paramref name="paths"/>, or null where nothing was
    /// read back. The caller needs the positions and not just a count: a path
    /// read as a decoration must not also be counted as one a grid took, and the
    /// two sets overlap wherever a table cell holds underlined text.
    /// </returns>
    public static bool[]? Apply(
        IReadOnlyList<PdfTextFragment> fragments,
        IReadOnlyList<PdfPaintedPath> paths,
        IReadOnlyList<PdfTableGrid> grids)
    {
        if (fragments.Count == 0 || paths.Count == 0)
            return null;

        // Runs ordered by baseline, with the largest font on the page. Together
        // they bound which runs a bar can possibly decorate: pairing every bar
        // against every run is quadratic, and a dense page has thousands of
        // each. The window is generous - it is drawn with the largest size
        // anywhere on the page rather than the size of the run being tested -
        // and the per-run test inside it is unchanged, so the bound costs no
        // match it would otherwise have made.
        PdfTextFragment[] byBaseline = [.. fragments];
        Array.Sort(byBaseline, static (left, right) => left.Y.CompareTo(right.Y));

        double largest = 0;
        foreach (PdfTextFragment fragment in fragments)
            largest = Math.Max(largest, fragment.FontSize);

        if (largest <= 0)
            return null;

        bool[]? consumed = null;
        var matched = new List<(PdfTextFragment Fragment, bool Strike)>();

        for (int i = 0; i < paths.Count; i++)
        {
            PdfPaintedPath path = paths[i];

            // A vertical bar decorates nothing, and only a bar is ever a
            // decoration: an area is a panel however thin it looks.
            if (path.Kind != PdfArtworkKind.Rule || path.Width <= path.Height)
                continue;

            if (IsLatticeLine(grids, path))
                continue;

            matched.Clear();
            double covered = 0;

            double at = (path.MinY + path.MaxY) / 2;
            for (int f = LowerBound(byBaseline, at - (largest * HighestOffset)); f < byBaseline.Length; f++)
            {
                PdfTextFragment fragment = byBaseline[f];
                if (fragment.Y > at - (largest * LowestOffset))
                    break;

                if (!Decorates(path, fragment, out double overlap, out bool strike))
                    continue;

                matched.Add((fragment, strike));
                covered += overlap;
            }

            // Runs on one baseline do not overlap each other, so the covered
            // lengths add up rather than double-counting. A bar the runs under
            // it cannot account for is a rule that happens to pass beneath some
            // text, and it stays artwork.
            if (matched.Count == 0 || covered < path.Width * MinimumBarShare)
                continue;

            foreach ((PdfTextFragment fragment, bool strike) in matched)
            {
                if (strike)
                    fragment.Strikethrough = true;
                else
                    fragment.Underline = true;
            }

            consumed ??= new bool[paths.Count];
            consumed[i] = true;
        }

        return consumed;
    }

    /// <summary>The first run whose baseline is at or above <paramref name="y"/>.</summary>
    private static int LowerBound(PdfTextFragment[] byBaseline, double y)
    {
        int low = 0;
        int high = byBaseline.Length;

        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (byBaseline[middle].Y < y)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    /// <summary>
    /// Whether this bar decorates this run, and if so how much of the run it
    /// spans and which decoration it is.
    /// </summary>
    private static bool Decorates(in PdfPaintedPath path, PdfTextFragment fragment, out double overlap, out bool strike)
    {
        overlap = 0;
        strike = false;

        double size = fragment.FontSize;

        // A run with no size gives the thresholds nothing to scale by, and an
        // invisible run is not being decorated by anything a reader can see.
        if (size <= 0 || fragment.Text.Length == 0 || fragment.IsInvisible)
            return false;

        if (path.Height > size * MaxThickness)
            return false;

        double offset = ((path.MinY + path.MaxY) / 2) - fragment.Y;
        if (offset < size * LowestOffset || offset > size * HighestOffset)
            return false;

        overlap = Math.Min(path.MaxX, fragment.EndX) - Math.Max(path.MinX, fragment.X);
        if (overlap <= 0)
            return false;

        double run = fragment.EndX - fragment.X;
        if (run <= 0 || overlap < run * MinimumRunShare)
            return false;

        strike = offset > size * StrikeOffset;
        return true;
    }

    private static bool IsLatticeLine(IReadOnlyList<PdfTableGrid> grids, in PdfPaintedPath path)
    {
        foreach (PdfTableGrid grid in grids)
        {
            if (grid.IsLatticeLine(path))
                return true;
        }

        return false;
    }
}
