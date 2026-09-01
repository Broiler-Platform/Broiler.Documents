using System;
using System.Collections.Generic;

namespace Broiler.Documents.Model;

/// <summary>The horizontal span a line of text has left, in column coordinates.</summary>
/// <param name="Left">Points from the text column's left edge.</param>
/// <param name="Width">How much room the line has from there.</param>
public readonly record struct TextBand(double Left, double Width);

/// <summary>
/// The boxes floating shapes keep body text out of, and the span a line has left
/// once they are taken into account.
/// </summary>
/// <remarks>
/// <para>
/// One implementation for all three layout engines - the editing surface, the
/// CLI rasterizer and the PDF writer - because three answers to "where does this
/// line start and how wide is it" would be three different documents.
/// </para>
/// <para>
/// A line keeps one span. A shape narrows it from the left or the right,
/// whichever leaves more room, rather than splitting the line into runs on both
/// sides of the shape. That is <c>wrapText="largest"</c> rather than
/// <c>bothSides</c>, and it is what keeps a line one measurable thing all the
/// way through wrapping, alignment, the caret and hit-testing.
/// </para>
/// <para>
/// Everything here is in column coordinates: x from the text column's left edge,
/// y down from the top of the body. A shape's y is resolved by the caller, which
/// is the one that knows where the anchoring paragraph landed.
/// </para>
/// </remarks>
public sealed class TextWrapExclusions
{
    private readonly List<Exclusion> _items = [];

    /// <summary>True when nothing wraps, which is the common case and the fast path.</summary>
    public bool IsEmpty => _items.Count == 0;

    /// <summary>
    /// Records a shape's box. <paramref name="top"/> is where the shape starts
    /// once its anchoring paragraph is placed; the margin the shape keeps around
    /// itself is already added by the caller.
    /// </summary>
    /// <remarks>
    /// <paramref name="scale"/> is the zoom a surface draws at. A shape states
    /// points and a zoomed surface lays out in its own units, so the box is
    /// scaled here rather than in each caller - <paramref name="top"/> is already
    /// in those units, being where the paragraph landed.
    /// </remarks>
    public void Add(DocumentShape shape, double top, double scale = 1)
    {
        if (shape.Wrap == ShapeWrap.None || shape.Width <= 0 || shape.Height <= 0)
            return;

        double margin = Math.Max(0, shape.WrapDistance) * scale;
        _items.Add(new Exclusion(
            top - margin,
            top + (shape.Height * scale) + margin,
            (shape.OffsetX * scale) - margin,
            ((shape.OffsetX + shape.Width) * scale) + margin,
            shape.Wrap,
            shape.WrapSide));
    }

    /// <summary>
    /// The span a line starting at <paramref name="top"/> has, after moving it
    /// down past anything that leaves it no room to be written in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one entry point a layout engine needs, so the order of the two
    /// questions - what has to be cleared, and what is left beside what does not
    /// - is decided here rather than three times over. A top-and-bottom shape
    /// takes the whole band and never appears in <see cref="Band"/>, so asking
    /// only that would leave a line sitting inside one.
    /// </para>
    /// <para>
    /// The search is bounded rather than repeated until it settles: clearing one
    /// shape can move a line into the next, and shapes covering the column would
    /// push a line down for as long as the document is tall. After the last try
    /// the line takes the full column and draws through, which is visible and
    /// finite where a hang is neither.
    /// </para>
    /// </remarks>
    public TextBand Resolve(ref double top, double height, double columnWidth, out double skip)
    {
        skip = 0;
        if (IsEmpty)
            return new TextBand(0, columnWidth);

        for (int attempt = 0; attempt < 8; attempt++)
        {
            // What has to be cleared outright first, then what is left beside the
            // rest. A cleared line can land beside something else, which is what
            // the next turn of the loop is for.
            double cleared = PushBelow(top, top + height, squareToo: false);
            if (cleared > top)
            {
                skip += cleared - top;
                top = cleared;
                continue;
            }

            TextBand band = Band(top, top + height, columnWidth);
            if (band.Width > 0)
                return band;

            double moved = PushBelow(top, top + height, squareToo: true);
            if (moved <= top)
                break;

            skip += moved - top;
            top = moved;
        }

        return new TextBand(0, columnWidth);
    }

    /// <summary>
    /// The span a line occupying <paramref name="top"/> to <paramref name="bottom"/>
    /// has left in a column <paramref name="columnWidth"/> wide.
    /// </summary>
    /// <remarks>
    /// A shape that leaves no usable room - one spanning the column, or one whose
    /// side is too narrow to write in - returns a zero width, which tells the
    /// caller to move the line down rather than to draw nothing.
    /// </remarks>
    public TextBand Band(double top, double bottom, double columnWidth)
    {
        double left = 0;
        double right = columnWidth;

        foreach (Exclusion item in _items)
        {
            if (item.Wrap != ShapeWrap.Square || !item.Intersects(top, bottom))
                continue;

            // Clamped to the column: a stripe in the margin takes nothing from the
            // text, and only the part of a shape that overlaps the column can.
            double blockedLeft = Math.Max(left, item.Left);
            double blockedRight = Math.Min(right, item.Right);
            if (blockedRight <= blockedLeft)
                continue;

            double roomLeft = blockedLeft - left;
            double roomRight = right - blockedRight;
            bool keepLeft = item.Side switch
            {
                WrapSide.Left => true,
                WrapSide.Right => false,
                _ => roomLeft >= roomRight,
            };

            if (keepLeft)
                right = blockedLeft;
            else
                left = blockedRight;

            if (right <= left)
                return new TextBand(left, 0);
        }

        return new TextBand(left, Math.Max(0, right - left));
    }

    /// <summary>
    /// Where a line has to start when the band it wanted is blocked outright: past
    /// the bottom of whatever is in the way, or where it already was.
    /// </summary>
    /// <remarks>
    /// This is both what <see cref="ShapeWrap.TopAndBottom"/> does to every line
    /// and what a <see cref="ShapeWrap.Square"/> shape does to one it leaves no
    /// room beside. Applied repeatedly by the caller, because clearing one shape
    /// can move a line into another.
    /// </remarks>
    public double PushBelow(double top, double bottom, bool squareToo)
    {
        double moved = top;
        foreach (Exclusion item in _items)
        {
            if (item.Wrap == ShapeWrap.Square && !squareToo)
                continue;

            if (item.Intersects(moved, bottom - top + moved) && item.Bottom > moved)
                moved = item.Bottom;
        }

        return moved;
    }

    private readonly record struct Exclusion(
        double Top,
        double Bottom,
        double Left,
        double Right,
        ShapeWrap Wrap,
        WrapSide Side)
    {
        /// <summary>True when the shape's band overlaps a line's, touching edges excluded.</summary>
        public bool Intersects(double top, double bottom) => Top < bottom && Bottom > top;
    }
}
