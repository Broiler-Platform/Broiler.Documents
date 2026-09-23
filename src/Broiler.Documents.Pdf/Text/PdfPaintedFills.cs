using System;
using System.Collections.Generic;

namespace Broiler.Documents.Pdf.Text;

/// <summary>
/// Reads what a page's filled areas were: a background painted beneath some
/// text, or paint in the paper's own colour on bare paper.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A fill beneath a run is that run's background.</strong> PDF paints
/// a heading's coloured band, a highlighter's stroke and a panel behind a note
/// the same way: a filled rectangle first, and the words over it. The model
/// carries a background colour per run, and that is what a band behind words
/// is to the words. Dropping it is not neutral either. White text on a dark
/// band is common in exactly the places a document most wants read - a title,
/// a banner - and without the band it is white on white.
/// </para>
/// <para>
/// Only the topmost fill under a run counts, because it is the one a reader
/// sees there: a white box drawn over a blue band gives the words on it no blue
/// background. A cell's shade is already the cell's shading, so it is not read
/// again. A fill covering most of the page is the page's colour, and painting
/// it behind every run as a highlight would say something the page never did.
/// A fill painted after the run lies over it rather than under it, and is not
/// a background at all.
/// </para>
/// <para>
/// <strong>A fill in the paper's colour on bare paper paints nothing.</strong>
/// Producers paint a white background behind paragraphs as a matter of course,
/// and counting those as lost artwork reported shapes no reader could ever see.
/// Only a fill with nothing under it qualifies, because the same white box over
/// a picture or a line of text hides it, and that is something the page did.
/// </para>
/// </remarks>
internal static class PdfPaintedFills
{
    /// <summary>
    /// The largest share of the page a fill may cover and still be a background
    /// behind some words rather than the colour of the page itself.
    /// </summary>
    private const double MaximumBackgroundShare = 0.9;

    /// <summary>
    /// How far, in ems, a run may reach past a fill and still be on it. A band
    /// is drawn to a line's height, and a glyph's box is only an estimate.
    /// </summary>
    private const double Slack = 0.1;

    /// <summary>
    /// How high above its baseline, in ems, a run has to lie within a fill:
    /// enough to say the fill is behind the letters rather than under their feet.
    /// </summary>
    private const double BodyHeight = 0.5;

    /// <summary>
    /// Gives each run the colour of the fill painted beneath it, where one was,
    /// and reports which fills were read that way.
    /// </summary>
    /// <returns>A flag per entry of <paramref name="paths"/>, or null where none was read.</returns>
    public static bool[]? ReadBackgrounds(
        IReadOnlyList<PdfTextFragment> fragments,
        IReadOnlyList<PdfPaintedPath> paths,
        IReadOnlyList<PdfTableGrid> grids,
        double pageArea)
    {
        if (fragments.Count == 0 || paths.Count == 0)
            return null;

        bool[]? read = null;

        foreach (PdfTextFragment fragment in fragments)
        {
            if (fragment.IsInvisible || fragment.Text.Length == 0 || fragment.FontSize <= 0)
                continue;

            // The topmost fill under the run, whatever it is: a white box over
            // a band hides the band from the words on the box.
            int topmost = -1;
            for (int i = 0; i < paths.Count; i++)
            {
                PdfPaintedPath path = paths[i];
                if (!path.Filled || path.Kind != PdfArtworkKind.Block || path.Order >= fragment.Order)
                    continue;

                if (!Under(path, fragment))
                    continue;

                if (topmost < 0 || path.Order > paths[topmost].Order)
                    topmost = i;
            }

            if (topmost < 0 || !IsBackground(paths[topmost], grids, pageArea))
                continue;

            fragment.Background = paths[topmost].Color;
            (read ??= new bool[paths.Count])[topmost] = true;
        }

        return read;
    }

    /// <summary>
    /// Finds the fills in the paper's colour that were painted on bare paper,
    /// and so painted nothing a reader can see.
    /// </summary>
    /// <param name="marksTruncated">
    /// True when the page painted more than was kept. Nothing can then be said
    /// to lie under nothing, and no fill is reported as bare.
    /// </param>
    /// <returns>A flag per entry of <paramref name="paths"/>, or null where none was found.</returns>
    public static bool[]? FindBarePaper(
        IReadOnlyList<PdfTextFragment> fragments,
        IReadOnlyList<PdfPaintedPath> paths,
        IReadOnlyList<PdfPaintedMark> marks,
        bool marksTruncated)
    {
        if (paths.Count == 0 || marksTruncated)
            return null;

        bool[]? bare = null;

        for (int i = 0; i < paths.Count; i++)
        {
            PdfPaintedPath path = paths[i];
            if (!path.Filled || path.Stroked || path.Color != PdfPaintedMark.Paper)
                continue;

            if (Covers(path, fragments) || Covers(path, marks))
                continue;

            (bare ??= new bool[paths.Count])[i] = true;
        }

        return bare;
    }

    /// <summary>Whether a fill lies beneath enough of a run to be behind its letters.</summary>
    private static bool Under(in PdfPaintedPath fill, PdfTextFragment fragment)
    {
        double slack = fragment.FontSize * Slack;

        return fragment.X >= fill.MinX - slack &&
            fragment.EndX <= fill.MaxX + slack &&
            fragment.Y >= fill.MinY - slack &&
            fragment.Y + (fragment.FontSize * BodyHeight) <= fill.MaxY + slack;
    }

    /// <summary>
    /// Whether the topmost fill under a run is a background the run should
    /// carry: coloured, not a cell's shade, and not the page's own colour.
    /// </summary>
    private static bool IsBackground(in PdfPaintedPath fill, IReadOnlyList<PdfTableGrid> grids, double pageArea)
    {
        if (fill.Color == PdfPaintedMark.Paper || fill.Color.IsEmpty)
            return false;

        if (pageArea > 0 && fill.Width * fill.Height >= pageArea * MaximumBackgroundShare)
            return false;

        foreach (PdfTableGrid grid in grids)
        {
            if (grid.ShadesCell(fill))
                return false;
        }

        return true;
    }

    /// <summary>Whether any run painted before the fill lies under part of it.</summary>
    private static bool Covers(in PdfPaintedPath fill, IReadOnlyList<PdfTextFragment> fragments)
    {
        foreach (PdfTextFragment fragment in fragments)
        {
            if (fragment.Order >= fill.Order || fragment.IsInvisible)
                continue;

            // A run's box from its baseline down to its descenders and up to
            // its ascenders, which is where its ink can be.
            double descent = fragment.FontSize * 0.25;
            double ascent = fragment.FontSize;
            if (fragment.X < fill.MaxX && fragment.EndX > fill.MinX &&
                fragment.Y - descent < fill.MaxY && fragment.Y + ascent > fill.MinY)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether anything but paper-coloured paint was put down before the fill,
    /// where it lies.
    /// </summary>
    private static bool Covers(in PdfPaintedPath fill, IReadOnlyList<PdfPaintedMark> marks)
    {
        foreach (PdfPaintedMark mark in marks)
        {
            if (mark.Order >= fill.Order || mark.IsPaper)
                continue;

            if (mark.Overlaps(fill.MinX, fill.MinY, fill.MaxX, fill.MaxY))
                return true;
        }

        return false;
    }
}
