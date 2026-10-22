using System;
using System.Collections.Generic;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Paging;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using Broiler.Graphics.Text;

namespace Broiler.Documents.Pagination;

/// <summary>
/// Draws laid-out pages as Broiler.Graphics commands in points: into a <see cref="BPage"/>
/// that a preview, a printer or a PDF replays, or straight into a render list.
/// </summary>
/// <remarks>
/// <para>
/// Underlines, strikethroughs, and highlights are drawn here rather than asked
/// for, because <see cref="BRenderCommand.DrawText"/> draws glyphs and nothing
/// else - <see cref="BFontStyle"/> carries a family, a size, a weight, and a
/// slant, and has no notion of a decoration. Drawing them as rectangles under
/// this painter's control also means their thickness and offset are stated here,
/// where a comparison can hold them constant.
/// </para>
/// <para>
/// One painter serves every output, so what the CLI rasterizes, what a print
/// preview shows and what a printer receives are the same commands.
/// </para>
/// </remarks>
public sealed class DocumentPagePainter
{
    /// <summary>How wide a changed line's bar is drawn: a hairline, as LibreOffice draws it.</summary>
    private const double ChangeBarWidth = 0.5;

    /// <summary>The colour of a changed line's bar, LibreOffice's default black.</summary>
    private static readonly BColor ChangeBarColor = new(0, 0, 0);

    private readonly LayoutSettings _settings;
    private readonly ImageStore _images;

    public DocumentPagePainter(LayoutSettings settings, ImageStore images)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _images = images ?? throw new ArgumentNullException(nameof(images));
    }

    /// <summary>
    /// Paints one page into a frozen <see cref="BPage"/> that carries the pixels of every
    /// image it draws.
    /// </summary>
    public BPage Paint(LayoutPage page, DocumentPageBox box)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(box);

        var builder = new BPageBuilder(new BSize(page.WidthPoints, page.HeightPoints));
        var added = new Dictionary<InlineImage, BImageHandle>(ReferenceEqualityComparer.Instance);
        Paint(page, box, builder.Content, image =>
        {
            if (added.TryGetValue(image, out BImageHandle existing))
                return existing;

            BBitmap? bitmap = _images.Bitmap(image);
            if (bitmap is null)
                return null;

            BImageHandle handle = builder.AddImage(bitmap.ToPixelBuffer(copy: false));
            added[image] = handle;
            return handle;
        });

        return builder.Build();
    }

    /// <summary>
    /// Paints one page into <paramref name="list"/>, drawing each image through the handle
    /// <paramref name="resolveImage"/> answers for it. A null answer means the image could not
    /// be decoded, and the box it would have filled is marked instead.
    /// </summary>
    public void Paint(LayoutPage page, DocumentPageBox box, BRenderList list, Func<InlineImage, BImageHandle?> resolveImage)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(box);
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(resolveImage);

        // The page's own colour goes under everything, the content box included.
        if (page.Background is ShapeFill background)
            DrawFill(list, new BRect(0, 0, page.WidthPoints, page.HeightPoints), background);

        if (_settings.ShowContentBox)
        {
            list.StrokeRect(new BRect(box.ContentLeftPoints, box.ContentTopPoints, box.ContentWidthPoints, box.ContentHeightPoints),
                new BColor(0xC0, 0xC0, 0xC0), 0.5);
        }

        // A letterhead's stripe goes down first, under everything it is a
        // background for. Table cells follow, so a shaded cell covers the stripe
        // rather than the other way round, then the text.
        foreach (LayoutShape shape in page.Shapes)
        {
            if (shape.BehindText)
                DrawShape(list, shape, resolveImage);
        }

        foreach (LayoutCell cell in page.Cells)
            DrawCell(list, cell);

        // Over a cell's shading, so a shaded paragraph in a shaded cell shows.
        DrawParagraphShading(list, page.Lines);

        foreach (LayoutLine line in page.Lines)
            DrawLine(list, line, resolveImage);

        // Last: the shapes the document stacks in front of its text. A stamp over
        // a letter is the case, and drawing it under the text would leave the
        // letter legible through a mark whose whole purpose is to cover it.
        foreach (LayoutShape shape in page.Shapes)
        {
            if (!shape.BehindText)
                DrawShape(list, shape, resolveImage);
        }
    }

    /// <summary>
    /// Paints one shape: its fill, its picture, its outline, then its own text.
    /// </summary>
    private void DrawShape(BRenderList list, LayoutShape shape, Func<InlineImage, BImageHandle?> resolveImage)
    {
        if (shape.Bounds.Width <= 0 || shape.Bounds.Height <= 0)
        {
            DrawRule(list, shape);
            return;
        }

        if (shape.Fill is ShapeFill fill)
            DrawFill(list, shape.Bounds, fill);

        // Over the fill and under the outline: a framed picture keeps its frame.
        if (shape.Image is InlineImage image)
            DrawShapeImage(list, image, shape.Bounds, resolveImage);

        if (!shape.Outline.IsEmpty && shape.Outline.A > 0)
            list.StrokeRect(shape.Bounds, shape.Outline, 1);

        foreach (LayoutCell cell in shape.Cells)
            DrawCell(list, cell);
        DrawParagraphShading(list, shape.Lines);
        foreach (LayoutLine line in shape.Lines)
            DrawLine(list, line, resolveImage);
    }

    /// <summary>
    /// Paints a box with no breadth as the rule its outline makes: a filled band
    /// of Word's default line width, centred on the box's one edge.
    /// </summary>
    private static void DrawRule(BRenderList list, LayoutShape shape)
    {
        const double width = 0.75;
        if (shape.Outline.IsEmpty || shape.Outline.A == 0)
            return;

        BRect bounds = shape.Bounds;
        if (bounds.Width <= 0 && bounds.Height > 0)
            list.FillRect(new BRect(bounds.Left - (width / 2), bounds.Top, width, bounds.Height), shape.Outline);
        else if (bounds.Height <= 0 && bounds.Width > 0)
            list.FillRect(new BRect(bounds.Left, bounds.Top - (width / 2), bounds.Width, width), shape.Outline);
    }

    /// <summary>
    /// Paints the shading of the shaded paragraphs among <paramref name="lines"/>,
    /// one box for each run of lines it covers.
    /// </summary>
    /// <remarks>
    /// The box is LibreOffice's, measured: from the top of a paragraph's first
    /// line to the bottom of its last, between its indents, leaving its space
    /// before and after white. A paragraph that follows straight on with the same
    /// shading and the same indents joins the box above it, the space between
    /// included. Lines of one paragraph that go on in another column start a box
    /// of their own there.
    /// </remarks>
    private static void DrawParagraphShading(BRenderList list, IReadOnlyList<LayoutLine> lines)
    {
        BRect? open = null;
        BColor color = BColor.Empty;
        int paragraph = 0;

        foreach (LayoutLine line in lines)
        {
            if (line.Background.IsEmpty || line.Background.A == 0)
            {
                Flush();
                continue;
            }

            double bottom = line.Top + line.Height;
            if (open is BRect box &&
                line.Background == color &&
                (line.ParagraphIndex == paragraph || line.ParagraphIndex == paragraph + 1) &&
                Math.Abs(line.BackgroundLeft - box.Left) < 0.01 &&
                Math.Abs(line.BackgroundWidth - box.Width) < 0.01 &&
                line.Top >= box.Bottom - 0.01)
            {
                open = new BRect(box.Left, box.Top, box.Width, bottom - box.Top);
            }
            else
            {
                Flush();
                open = new BRect(line.BackgroundLeft, line.Top, line.BackgroundWidth, line.Height);
                color = line.Background;
            }

            paragraph = line.ParagraphIndex;
        }

        Flush();

        void Flush()
        {
            if (open is BRect box && box.Width > 0 && box.Height > 0)
                list.FillRect(box, color);

            open = null;
        }
    }

    /// <summary>
    /// Paints one table cell: its background, then the edges it states. Each edge
    /// is a filled rectangle rather than a stroked box, because a cell states its
    /// four sides separately and three of them may be turned off.
    /// </summary>
    private static void DrawCell(BRenderList list, LayoutCell cell)
    {
        BRect bounds = cell.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (!cell.Shading.IsEmpty && cell.Shading.A > 0)
            list.FillRect(bounds, cell.Shading);

        CellBorders borders = cell.Borders;
        if (borders.Top.IsVisible)
            list.FillRect(new BRect(bounds.Left, bounds.Top, bounds.Width, borders.Top.Width), borders.Top.Color);

        if (borders.Bottom.IsVisible)
            list.FillRect(new BRect(bounds.Left, bounds.Bottom - borders.Bottom.Width, bounds.Width, borders.Bottom.Width), borders.Bottom.Color);

        if (borders.Left.IsVisible)
            list.FillRect(new BRect(bounds.Left, bounds.Top, borders.Left.Width, bounds.Height), borders.Left.Color);

        if (borders.Right.IsVisible)
            list.FillRect(new BRect(bounds.Right - borders.Right.Width, bounds.Top, borders.Right.Width, bounds.Height), borders.Right.Color);
    }

    /// <summary>
    /// Draws a floating picture into its box, or marks the box when the image did
    /// not decode - the same crossed rectangle an inline picture leaves, for the
    /// same reason: the page should show where content is missing.
    /// </summary>
    private static void DrawShapeImage(BRenderList list, InlineImage image, BRect destination, Func<InlineImage, BImageHandle?> resolveImage)
    {
        if (resolveImage(image) is not BImageHandle decoded)
        {
            DrawMissingImage(list, destination);
            return;
        }

        list.DrawImage(decoded, new BRect(0, 0, decoded.PixelSize.Width, decoded.PixelSize.Height), destination);
    }

    /// <summary>
    /// Paints <paramref name="bounds"/> with <paramref name="fill"/>: a page's
    /// background or a shape's box.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A gradient is drawn as bands of solid colour because the render list has
    /// no gradient primitive. The band count is the extent in points, capped, so
    /// a band is about a point wide and the seams fall below what the eye picks
    /// out at any sensible DPI.
    /// </para>
    /// <para>
    /// The bands run across the angle the fill states, which DrawingML measures
    /// clockwise from the x axis, and the gradient spans the box's length along
    /// that direction, so its first and last colours meet the corners - how
    /// DrawingML and CSS both draw a linear gradient. A gradient along an axis is
    /// banded in rectangles; a diagonal one in strips clipped to the box. Banding
    /// a diagonal along the nearer axis, which this did, turned a page whose
    /// colours meet in its corners into one whose colours meet at its sides.
    /// </para>
    /// </remarks>
    private static void DrawFill(BRenderList list, BRect bounds, ShapeFill fill)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (!fill.IsGradient)
        {
            list.FillRect(bounds, fill.Start);
            return;
        }

        double radians = fill.AngleDegrees * Math.PI / 180.0;
        double dx = Math.Cos(radians);
        double dy = Math.Sin(radians);
        if (Math.Abs(dx) < 1e-9)
            dx = 0;
        if (Math.Abs(dy) < 1e-9)
            dy = 0;

        double extent = Math.Abs(bounds.Width * dx) + Math.Abs(bounds.Height * dy);
        int bands = (int)Math.Clamp(Math.Round(extent), 2, 512);
        double step = extent / bands;
        double centerX = bounds.Left + (bounds.Width / 2);
        double centerY = bounds.Top + (bounds.Height / 2);
        bool diagonal = dx != 0 && dy != 0;

        // Half the length a strip has to run across the gradient to cover the
        // box at any angle; the clip trims it.
        double reach = bounds.Width + bounds.Height;
        if (diagonal)
            list.Append(new BRenderCommand.PushClip(bounds));

        // Opaque bands overlap by half a point, so no seam shows between them.
        // Translucent ones must not: where two overlap the colour is laid down
        // twice, and a fade from transparent draws as stripes.
        double overlap = fill.Start.A == 255 && fill.End.A == 255 ? 0.5 : 0;
        for (int i = 0; i < bands; i++)
        {
            BColor color = fill.ColorAt((double)i / (bands - 1));
            double from = (-extent / 2) + (step * i);
            double to = from + step + overlap;

            if (!diagonal)
            {
                // Along an axis, the strip is a rectangle: measured from the
                // side the gradient starts at, which is the far one when the
                // angle points back along the axis.
                double near = dx != 0 ? bounds.Width / 2 : bounds.Height / 2;
                double sign = dx + dy;
                double a = sign > 0 ? near + from : near - to;
                double b = sign > 0 ? near + to : near - from;
                a = Math.Max(0, a);
                b = Math.Min(2 * near, b);
                if (b <= a)
                    continue;

                list.FillRect(
                    dx != 0
                        ? new BRect(bounds.Left + a, bounds.Top, b - a, bounds.Height)
                        : new BRect(bounds.Left, bounds.Top + a, bounds.Width, b - a),
                    color);
                continue;
            }

            BPoint p1 = new(centerX + (dx * from) + (dy * reach), centerY + (dy * from) - (dx * reach));
            BPoint p2 = new(centerX + (dx * to) + (dy * reach), centerY + (dy * to) - (dx * reach));
            BPoint p3 = new(centerX + (dx * to) - (dy * reach), centerY + (dy * to) + (dx * reach));
            BPoint p4 = new(centerX + (dx * from) - (dy * reach), centerY + (dy * from) + (dx * reach));
            list.FillTriangle(p1, p2, p3, color);
            list.FillTriangle(p1, p3, p4, color);
        }

        if (diagonal)
            list.Append(new BRenderCommand.PopClip());
    }

    private void DrawLine(BRenderList list, LayoutLine line, Func<InlineImage, BImageHandle?> resolveImage)
    {
        double baseline = line.Top + line.Baseline;

        // LibreOffice marks a line holding a tracked change with a hairline in
        // the margin, the height of the line, in black.
        if (line.ChangeBarX is double bar)
            list.FillRect(new BRect(bar - (ChangeBarWidth / 2), line.Top, ChangeBarWidth, line.Height), ChangeBarColor);

        foreach (LayoutPiece piece in line.Pieces)
        {
            if (piece.Width <= 0)
                continue;

            if (!piece.Highlight.IsEmpty && piece.Highlight.A > 0)
            {
                list.FillRect(
                    new BRect(piece.X, baseline - piece.Ascent, piece.Width, piece.Ascent + piece.Descent),
                    piece.Highlight);
            }

            if (piece.IsImage)
            {
                DrawImage(list, piece, baseline, resolveImage);
                continue;
            }

            if (piece.Text.Length == 0)
                continue;

            // The renderer puts the baseline at origin.Y + size * 0.8, and the
            // line's baseline may come from host-supplied font metrics. Subtract
            // the renderer's origin offset, not the measured ascent, to keep
            // differently sized runs on that same resolved baseline. A raised
            // piece - a note's citation - sits that much above it.
            double pieceBaseline = baseline - piece.Rise;
            double originY = pieceBaseline - (piece.Font.Size * 0.8);
            var run = new BTextRun(piece.Text, piece.Font, piece.Color);

            if (piece.Oblique)
            {
                // A horizontal shear about the baseline: a point at height h above
                // it moves right by slant * h, and a point on it does not move.
                // Written directly because BMatrix3x2 offers translation and scale
                // and nothing else.
                double slant = _settings.ObliqueSlant;
                list.PushTransform(new BMatrix3x2(1, 0, -slant, 1, slant * pieceBaseline, 0));
                list.DrawText(run, new BPoint(piece.X, originY));
                list.PopTransform();
            }
            else
            {
                list.DrawText(run, new BPoint(piece.X, originY));
            }

            double thickness = Math.Max(0.5, piece.Font.Size * 0.055);

            // Moved text's line is doubled: a second one below the underline,
            // and the strikethrough split into two either side of where it was.
            double gap = thickness * 2;
            if (piece.Underline)
            {
                list.FillRect(
                    new BRect(piece.X, pieceBaseline + (piece.Font.Size * 0.10), piece.Width, thickness),
                    piece.Color);
                if (piece.Doubled)
                {
                    list.FillRect(
                        new BRect(piece.X, pieceBaseline + (piece.Font.Size * 0.10) + gap, piece.Width, thickness),
                        piece.Color);
                }
            }

            if (piece.Strikethrough)
            {
                double strike = pieceBaseline - (piece.Font.Size * 0.26);
                if (piece.Doubled)
                {
                    list.FillRect(new BRect(piece.X, strike - (gap / 2), piece.Width, thickness), piece.Color);
                    list.FillRect(new BRect(piece.X, strike + (gap / 2), piece.Width, thickness), piece.Color);
                }
                else
                {
                    list.FillRect(new BRect(piece.X, strike, piece.Width, thickness), piece.Color);
                }
            }
        }
    }

    private static void DrawImage(BRenderList list, LayoutPiece piece, double baseline, Func<InlineImage, BImageHandle?> resolveImage)
    {
        var destination = new BRect(piece.X, baseline - piece.Ascent, piece.Width, piece.Ascent);

        if (resolveImage(piece.Image!) is not BImageHandle image)
        {
            // The image did not decode. Draw the box it would have occupied with
            // a cross through it: the page then shows where content is missing
            // instead of quietly closing up around the hole.
            DrawMissingImage(list, destination);
            return;
        }

        list.DrawImage(image, new BRect(0, 0, image.PixelSize.Width, image.PixelSize.Height), destination);
    }

    private static void DrawMissingImage(BRenderList list, BRect destination)
    {
        var missing = new BColor(0xB0, 0xB0, 0xB0);
        list.StrokeRect(destination, missing, 1.0);
        list.FillRect(new BRect(destination.X, destination.Y + (destination.Height / 2), destination.Width, 1), missing);
    }
}
