using System;
using System.Collections.Generic;
using System.Linq;
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
            DrawFill(list, page.BackgroundBounds, background, resolveImage);

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

        if (page.FloatingTables.Count == 0)
        {
            DrawCells(list, page.Cells);
            DrawParagraphShading(list, page.Lines);
            foreach (LayoutLine line in page.Lines)
                DrawLine(list, line, resolveImage);
        }
        else
        {
            // Behind-text floating tables
            foreach (LayoutFloatingTable table in page.FloatingTables.Where(t => t.BehindText).OrderBy(t => t.ZOrder))
            {
                DrawCells(list, table.Cells);
                DrawParagraphShading(list, table.Lines);
                foreach (LayoutLine line in table.Lines)
                    DrawLine(list, line, resolveImage);
            }

            // Body text & inline table cells
            DrawCells(list, page.InlineCells);
            DrawParagraphShading(list, page.InlineLines);
            foreach (LayoutLine line in page.InlineLines)
                DrawLine(list, line, resolveImage);

            // In-front floating tables
            foreach (LayoutFloatingTable table in page.FloatingTables.Where(t => !t.BehindText).OrderBy(t => t.ZOrder))
            {
                DrawCells(list, table.Cells);
                DrawParagraphShading(list, table.Lines);
                foreach (LayoutLine line in table.Lines)
                    DrawLine(list, line, resolveImage);
            }
        }

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
        if (shape.Geometry is ShapeGeometry.Line or ShapeGeometry.LineAscending)
        {
            BRect box = shape.Bounds;
            DrawSegment(list, new BPoint(box.Left, shape.Geometry == ShapeGeometry.Line ? box.Top : box.Bottom),
                new BPoint(box.Right, shape.Geometry == ShapeGeometry.Line ? box.Bottom : box.Top), shape.Outline, shape.OutlineWidth);
            return;
        }
        if (shape.Bounds.Width <= 0 || shape.Bounds.Height <= 0)
        {
            DrawRule(list, shape);
            return;
        }

        if (shape.Fill is ShapeFill fill)
        {
            if (shape.Geometry == ShapeGeometry.Ellipse)
            {
                if (!fill.IsGradient && fill.Image is null && fill.Hatch is null)
                    list.Append(new BRenderCommand.FillRoundedRect(shape.Bounds, fill.Start, shape.Bounds.Width / 2, shape.Bounds.Height / 2));
                else
                {
                    // BRenderList clips rectangles. Bounded horizontal strips keep
                    // every fill type inside the ellipse without a rectangular backdrop.
                    const int strips = 128;
                    for (int i = 0; i < strips; i++)
                    {
                        double unit = 2 * (i + 0.5) / strips - 1;
                        double half = shape.Bounds.Width / 2 * Math.Sqrt(Math.Max(0, 1 - unit * unit));
                        list.PushClip(new BRect(shape.Bounds.Left + shape.Bounds.Width / 2 - half,
                            shape.Bounds.Top + shape.Bounds.Height * i / strips, 2 * half, shape.Bounds.Height / strips));
                        DrawFill(list, shape.Bounds, fill, resolveImage);
                        list.PopClip();
                    }
                }
            }
            else DrawFill(list, shape.Bounds, fill, resolveImage);
        }

        // Over the fill and under the outline: a framed picture keeps its frame.
        if (shape.Image is InlineImage image)
            DrawShapeImage(list, image, shape.Bounds, resolveImage);

        if (!shape.Outline.IsEmpty && shape.Outline.A > 0)
        {
            double thickness = shape.OutlineWidth == 0 ? 0.25 : shape.OutlineWidth;
            if (shape.Geometry == ShapeGeometry.Ellipse)
                list.Append(new BRenderCommand.StrokeRoundedRect(shape.Bounds, shape.Outline, shape.Bounds.Width / 2, shape.Bounds.Height / 2, thickness));
            else list.StrokeRect(shape.Bounds, shape.Outline, thickness);
        }

        DrawCells(list, shape.Cells);
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
        double width = shape.OutlineWidth == 0 ? 0.25 : shape.OutlineWidth;
        if (shape.Outline.IsEmpty || shape.Outline.A == 0)
            return;

        BRect bounds = shape.Bounds;
        if (bounds.Width <= 0 && bounds.Height > 0)
            list.FillRect(new BRect(bounds.Left - (width / 2), bounds.Top, width, bounds.Height), shape.Outline);
        else if (bounds.Height <= 0 && bounds.Width > 0)
            list.FillRect(new BRect(bounds.Left, bounds.Top - (width / 2), bounds.Width, width), shape.Outline);
    }

    private static void DrawSegment(BRenderList list, BPoint start, BPoint end, BColor color, double width)
    {
        if (color.IsEmpty || color.A == 0) return;
        double dx = end.X - start.X, dy = end.Y - start.Y, length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0) return;
        double half = (width == 0 ? 0.25 : width) / 2;
        list.PushTransform(new BMatrix3x2(dx / length, dy / length, -dy / length, dx / length, start.X, start.Y));
        list.FillRect(new BRect(0, -half, length, 2 * half), color);
        list.PopTransform();
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

            double bottom = line.Top + Math.Min(line.Height, line.ClipHeight ?? line.Height);
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
                // The rows a wrapping shape left no room in above the line are
                // still the paragraph's, and its shading fills them too.
                Flush();
                open = new BRect(line.BackgroundLeft, line.Top - line.LeadingSkip, line.BackgroundWidth,
                    line.Height + line.LeadingSkip);
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

    private enum BorderEdge { Top, Bottom, Left, Right, SharedH, SharedV }

    /// <summary>
    /// Paints table cells: their backgrounds, diagonal dividing lines, and borders with
    /// border-collapse and half-width arbitration for shared edges between adjacent cells.
    /// </summary>
    private static void DrawCells(BRenderList list, IReadOnlyList<LayoutCell> cells)
    {
        if (cells.Count == 0)
            return;

        // 1. Shading fills under borders so overlapping or shared edges are never occluded.
        foreach (LayoutCell cell in cells)
        {
            BRect bounds = cell.Bounds;
            if (bounds.Width > 0 && bounds.Height > 0 && !cell.Shading.IsEmpty && cell.Shading.A > 0)
                list.FillRect(bounds, cell.Shading);
        }

        // 2. Diagonal lines clipped to each cell's box.
        foreach (LayoutCell cell in cells)
        {
            BRect bounds = cell.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                continue;

            CellBorders borders = cell.Borders;
            if (borders.DiagonalDown.IsVisible)
            {
                list.PushClip(bounds);
                DrawStyledSegment(list, new BPoint(bounds.Left, bounds.Top), new BPoint(bounds.Right, bounds.Bottom), borders.DiagonalDown);
                list.PopClip();
            }

            if (borders.DiagonalUp.IsVisible)
            {
                list.PushClip(bounds);
                DrawStyledSegment(list, new BPoint(bounds.Left, bounds.Bottom), new BPoint(bounds.Right, bounds.Top), borders.DiagonalUp);
                list.PopClip();
            }
        }

        // 3. Four-sided borders with half-width collapse for shared boundaries.
        DrawCellBorders(list, cells);
    }

    private static void DrawCell(BRenderList list, LayoutCell cell)
    {
        DrawCells(list, [cell]);
    }

    private static void DrawCellBorders(BRenderList list, IReadOnlyList<LayoutCell> cells)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            LayoutCell cell = cells[i];
            BRect bounds = cell.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                continue;

            double left = bounds.Left;
            double top = bounds.Top;
            double right = bounds.Right;
            double bottom = bounds.Bottom;
            CellBorders borders = cell.Borders;

            // 1. Right edge and right-hand shared seams
            var rightNeighborIntervals = new List<(double Start, double End)>();
            for (int j = 0; j < cells.Count; j++)
            {
                if (i == j) continue;
                LayoutCell other = cells[j];
                if (Math.Abs(other.Bounds.Left - right) < 0.05)
                {
                    double y0 = Math.Max(top, other.Bounds.Top);
                    double y1 = Math.Min(bottom, other.Bounds.Bottom);
                    if (y1 > y0 + 0.05)
                    {
                        rightNeighborIntervals.Add((y0, y1));
                        TableBorder winner = ArbitrateBorders(borders.Right, other.Borders.Left);
                        if (winner.IsVisible)
                        {
                            double w = winner.Width;
                            BRect seamRect = new(right - (w / 2.0), y0, w, y1 - y0);
                            DrawBorderEdge(list, seamRect, winner, isHorizontal: false, BorderEdge.SharedV);
                        }
                    }
                }
            }

            if (borders.Right.IsVisible)
            {
                foreach (var (y0, y1) in SubtractIntervals(top, bottom, rightNeighborIntervals))
                {
                    BRect unsharedRect = new(right - borders.Right.Width, y0, borders.Right.Width, y1 - y0);
                    DrawBorderEdge(list, unsharedRect, borders.Right, isHorizontal: false, BorderEdge.Right);
                }
            }

            // 2. Left edge unshared portions (shared portions drawn by left neighbor as its Right edge)
            if (borders.Left.IsVisible)
            {
                var leftNeighborIntervals = new List<(double Start, double End)>();
                for (int j = 0; j < cells.Count; j++)
                {
                    if (i == j) continue;
                    LayoutCell other = cells[j];
                    if (Math.Abs(other.Bounds.Right - left) < 0.05)
                    {
                        double y0 = Math.Max(top, other.Bounds.Top);
                        double y1 = Math.Min(bottom, other.Bounds.Bottom);
                        if (y1 > y0 + 0.05)
                            leftNeighborIntervals.Add((y0, y1));
                    }
                }

                foreach (var (y0, y1) in SubtractIntervals(top, bottom, leftNeighborIntervals))
                {
                    BRect unsharedRect = new(left, y0, borders.Left.Width, y1 - y0);
                    DrawBorderEdge(list, unsharedRect, borders.Left, isHorizontal: false, BorderEdge.Left);
                }
            }

            // 3. Bottom edge and bottom shared seams
            var bottomNeighborIntervals = new List<(double Start, double End)>();
            for (int j = 0; j < cells.Count; j++)
            {
                if (i == j) continue;
                LayoutCell other = cells[j];
                if (Math.Abs(other.Bounds.Top - bottom) < 0.05)
                {
                    double x0 = Math.Max(left, other.Bounds.Left);
                    double x1 = Math.Min(right, other.Bounds.Right);
                    if (x1 > x0 + 0.05)
                    {
                        bottomNeighborIntervals.Add((x0, x1));
                        TableBorder winner = ArbitrateBorders(borders.Bottom, other.Borders.Top);
                        if (winner.IsVisible)
                        {
                            double w = winner.Width;
                            BRect seamRect = new(x0, bottom - (w / 2.0), x1 - x0, w);
                            DrawBorderEdge(list, seamRect, winner, isHorizontal: true, BorderEdge.SharedH);
                        }
                    }
                }
            }

            if (borders.Bottom.IsVisible)
            {
                foreach (var (x0, x1) in SubtractIntervals(left, right, bottomNeighborIntervals))
                {
                    BRect unsharedRect = new(x0, bottom - borders.Bottom.Width, x1 - x0, borders.Bottom.Width);
                    DrawBorderEdge(list, unsharedRect, borders.Bottom, isHorizontal: true, BorderEdge.Bottom);
                }
            }

            // 4. Top edge unshared portions (shared portions drawn by top neighbor as its Bottom edge)
            if (borders.Top.IsVisible)
            {
                var topNeighborIntervals = new List<(double Start, double End)>();
                for (int j = 0; j < cells.Count; j++)
                {
                    if (i == j) continue;
                    LayoutCell other = cells[j];
                    if (Math.Abs(other.Bounds.Bottom - top) < 0.05)
                    {
                        double x0 = Math.Max(left, other.Bounds.Left);
                        double x1 = Math.Min(right, other.Bounds.Right);
                        if (x1 > x0 + 0.05)
                            topNeighborIntervals.Add((x0, x1));
                    }
                }

                foreach (var (x0, x1) in SubtractIntervals(left, right, topNeighborIntervals))
                {
                    BRect unsharedRect = new(x0, top, x1 - x0, borders.Top.Width);
                    DrawBorderEdge(list, unsharedRect, borders.Top, isHorizontal: true, BorderEdge.Top);
                }
            }
        }
    }

    private static List<(double Start, double End)> SubtractIntervals(double start, double end, List<(double Start, double End)> toSubtract)
    {
        var result = new List<(double Start, double End)>();
        if (end <= start)
            return result;

        var clamped = toSubtract
            .Select(i => (Start: Math.Max(start, i.Start), End: Math.Min(end, i.End)))
            .Where(i => i.End > i.Start + 0.05)
            .OrderBy(i => i.Start)
            .ToList();

        double current = start;
        foreach (var (subStart, subEnd) in clamped)
        {
            if (subStart > current + 0.05)
                result.Add((current, subStart));
            current = Math.Max(current, subEnd);
        }

        if (current < end - 0.05)
            result.Add((current, end));

        return result;
    }

    private static TableBorder ArbitrateBorders(TableBorder a, TableBorder b) => TableBorder.Arbitrate(a, b);

    private static void DrawBorderEdge(BRenderList list, BRect rect, TableBorder border, bool isHorizontal, BorderEdge edge)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || !border.IsVisible)
            return;

        switch (border.Style)
        {
            case TableBorderStyle.Dashed:
                if (isHorizontal)
                {
                    double dash = Math.Max(3.0, border.Width * 3.0);
                    double gap = Math.Max(2.0, border.Width * 2.0);
                    double x = rect.Left;
                    while (x < rect.Right)
                    {
                        double w = Math.Min(dash, rect.Right - x);
                        list.FillRect(new BRect(x, rect.Top, w, rect.Height), border.Color);
                        x += dash + gap;
                    }
                }
                else
                {
                    double dash = Math.Max(3.0, border.Width * 3.0);
                    double gap = Math.Max(2.0, border.Width * 2.0);
                    double y = rect.Top;
                    while (y < rect.Bottom)
                    {
                        double h = Math.Min(dash, rect.Bottom - y);
                        list.FillRect(new BRect(rect.Left, y, rect.Width, h), border.Color);
                        y += dash + gap;
                    }
                }
                break;

            case TableBorderStyle.Dotted:
                if (isHorizontal)
                {
                    double dot = Math.Max(1.0, border.Width);
                    double gap = dot;
                    double x = rect.Left;
                    while (x < rect.Right)
                    {
                        double w = Math.Min(dot, rect.Right - x);
                        list.FillRect(new BRect(x, rect.Top, w, rect.Height), border.Color);
                        x += dot + gap;
                    }
                }
                else
                {
                    double dot = Math.Max(1.0, border.Width);
                    double gap = dot;
                    double y = rect.Top;
                    while (y < rect.Bottom)
                    {
                        double h = Math.Min(dot, rect.Bottom - y);
                        list.FillRect(new BRect(rect.Left, y, rect.Width, h), border.Color);
                        y += dot + gap;
                    }
                }
                break;

            case TableBorderStyle.Double:
                if (isHorizontal)
                {
                    double stripe = rect.Height / 3.0;
                    list.FillRect(new BRect(rect.Left, rect.Top, rect.Width, stripe), border.Color);
                    list.FillRect(new BRect(rect.Left, rect.Bottom - stripe, rect.Width, stripe), border.Color);
                }
                else
                {
                    double stripe = rect.Width / 3.0;
                    list.FillRect(new BRect(rect.Left, rect.Top, stripe, rect.Height), border.Color);
                    list.FillRect(new BRect(rect.Right - stripe, rect.Top, stripe, rect.Height), border.Color);
                }
                break;

            case TableBorderStyle.Groove:
                if (isHorizontal)
                {
                    double half = rect.Height / 2.0;
                    bool topDark = edge is BorderEdge.Top or BorderEdge.SharedH;
                    BColor c1 = topDark ? Darken(border.Color) : Lighten(border.Color);
                    BColor c2 = topDark ? Lighten(border.Color) : Darken(border.Color);
                    list.FillRect(new BRect(rect.Left, rect.Top, rect.Width, half), c1);
                    list.FillRect(new BRect(rect.Left, rect.Top + half, rect.Width, half), c2);
                }
                else
                {
                    double half = rect.Width / 2.0;
                    bool leftDark = edge is BorderEdge.Left or BorderEdge.SharedV;
                    BColor c1 = leftDark ? Darken(border.Color) : Lighten(border.Color);
                    BColor c2 = leftDark ? Lighten(border.Color) : Darken(border.Color);
                    list.FillRect(new BRect(rect.Left, rect.Top, half, rect.Height), c1);
                    list.FillRect(new BRect(rect.Left + half, rect.Top, half, rect.Height), c2);
                }
                break;

            case TableBorderStyle.Ridge:
                if (isHorizontal)
                {
                    double half = rect.Height / 2.0;
                    bool topLight = edge is BorderEdge.Top or BorderEdge.SharedH;
                    BColor c1 = topLight ? Lighten(border.Color) : Darken(border.Color);
                    BColor c2 = topLight ? Darken(border.Color) : Lighten(border.Color);
                    list.FillRect(new BRect(rect.Left, rect.Top, rect.Width, half), c1);
                    list.FillRect(new BRect(rect.Left, rect.Top + half, rect.Width, half), c2);
                }
                else
                {
                    double half = rect.Width / 2.0;
                    bool leftLight = edge is BorderEdge.Left or BorderEdge.SharedV;
                    BColor c1 = leftLight ? Lighten(border.Color) : Darken(border.Color);
                    BColor c2 = leftLight ? Darken(border.Color) : Lighten(border.Color);
                    list.FillRect(new BRect(rect.Left, rect.Top, half, rect.Height), c1);
                    list.FillRect(new BRect(rect.Left + half, rect.Top, half, rect.Height), c2);
                }
                break;

            case TableBorderStyle.Inset:
                BColor cinset = (edge is BorderEdge.Top or BorderEdge.Left or BorderEdge.SharedH or BorderEdge.SharedV)
                    ? Darken(border.Color) : Lighten(border.Color);
                list.FillRect(rect, cinset);
                break;

            case TableBorderStyle.Outset:
                BColor coutset = (edge is BorderEdge.Top or BorderEdge.Left or BorderEdge.SharedH or BorderEdge.SharedV)
                    ? Lighten(border.Color) : Darken(border.Color);
                list.FillRect(rect, coutset);
                break;

            case TableBorderStyle.Solid:
            default:
                list.FillRect(rect, border.Color);
                break;
        }
    }

    private static void DrawStyledSegment(BRenderList list, BPoint start, BPoint end, TableBorder border)
    {
        if (!border.IsVisible) return;
        double dx = end.X - start.X, dy = end.Y - start.Y, length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0) return;
        double width = border.Width <= 0 ? 0.5 : border.Width;
        double half = width / 2.0;

        list.PushTransform(new BMatrix3x2(dx / length, dy / length, -dy / length, dx / length, start.X, start.Y));

        switch (border.Style)
        {
            case TableBorderStyle.Dashed:
                double dash = Math.Max(3.0, width * 3.0);
                double gap = Math.Max(2.0, width * 2.0);
                double x = 0;
                while (x < length)
                {
                    double w = Math.Min(dash, length - x);
                    list.FillRect(new BRect(x, -half, w, width), border.Color);
                    x += dash + gap;
                }
                break;

            case TableBorderStyle.Dotted:
                double dot = Math.Max(1.0, width);
                double dotGap = dot;
                double xd = 0;
                while (xd < length)
                {
                    double w = Math.Min(dot, length - xd);
                    list.FillRect(new BRect(xd, -half, w, width), border.Color);
                    xd += dot + dotGap;
                }
                break;

            case TableBorderStyle.Double:
                double stripe = width / 3.0;
                list.FillRect(new BRect(0, -half, length, stripe), border.Color);
                list.FillRect(new BRect(0, half - stripe, length, stripe), border.Color);
                break;

            case TableBorderStyle.Groove:
                double gh = width / 2.0;
                list.FillRect(new BRect(0, -half, length, gh), Darken(border.Color));
                list.FillRect(new BRect(0, 0, length, gh), Lighten(border.Color));
                break;

            case TableBorderStyle.Ridge:
                double rh = width / 2.0;
                list.FillRect(new BRect(0, -half, length, rh), Lighten(border.Color));
                list.FillRect(new BRect(0, 0, length, rh), Darken(border.Color));
                break;

            case TableBorderStyle.Inset:
                list.FillRect(new BRect(0, -half, length, width), Darken(border.Color));
                break;

            case TableBorderStyle.Outset:
                list.FillRect(new BRect(0, -half, length, width), Lighten(border.Color));
                break;

            case TableBorderStyle.Solid:
            default:
                list.FillRect(new BRect(0, -half, length, width), border.Color);
                break;
        }

        list.PopTransform();
    }

    private static BColor Darken(BColor color, double factor = 0.6) =>
        new((byte)Math.Clamp((int)Math.Round(color.R * factor), 0, 255),
            (byte)Math.Clamp((int)Math.Round(color.G * factor), 0, 255),
            (byte)Math.Clamp((int)Math.Round(color.B * factor), 0, 255),
            color.A);

    private static BColor Lighten(BColor color, double factor = 0.6) =>
        new((byte)Math.Clamp((int)Math.Round(color.R + (255 - color.R) * factor), 0, 255),
            (byte)Math.Clamp((int)Math.Round(color.G + (255 - color.G) * factor), 0, 255),
            (byte)Math.Clamp((int)Math.Round(color.B + (255 - color.B) * factor), 0, 255),
            color.A);

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
    private void DrawFill(BRenderList list, BRect bounds, ShapeFill fill, Func<InlineImage, BImageHandle?> resolveImage)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (fill.Image is ImageFill image)
        {
            var size = _images.MeasurePoints(image.Picture);
            list.Append(new BRenderCommand.PushClip(bounds));
            foreach (var tile in FillGeometry.Tiles(image, bounds.Width, bounds.Height, size.Width, size.Height))
                DrawShapeImage(list, image.Picture, new BRect(bounds.Left + tile.X, bounds.Top + tile.Y, tile.Width, tile.Height), resolveImage);
            list.Append(new BRenderCommand.PopClip());
            return;
        }
        if (fill.Hatch is HatchFill hatch)
        {
            list.Append(new BRenderCommand.PushClip(bounds));
            if (!fill.Start.IsEmpty && fill.Start.A > 0) list.FillRect(bounds, fill.Start);
            foreach (var stroke in FillGeometry.Hatching(hatch, bounds.Width, bounds.Height))
            {
                double sx = stroke.X2 - stroke.X1, sy = stroke.Y2 - stroke.Y1;
                double length = Math.Sqrt(sx * sx + sy * sy);
                if (length < 0.001) continue;
                double nx = -sy / length * 0.25, ny = sx / length * 0.25;
                BPoint a = new(bounds.Left + stroke.X1 + nx, bounds.Top + stroke.Y1 + ny);
                BPoint b = new(bounds.Left + stroke.X2 + nx, bounds.Top + stroke.Y2 + ny);
                BPoint c = new(bounds.Left + stroke.X2 - nx, bounds.Top + stroke.Y2 - ny);
                BPoint d = new(bounds.Left + stroke.X1 - nx, bounds.Top + stroke.Y1 - ny);
                list.FillTriangle(a, b, c, hatch.Color); list.FillTriangle(a, c, d, hatch.Color);
            }
            list.Append(new BRenderCommand.PopClip());
            return;
        }
        if (fill.Geometry != GradientGeometry.Linear && fill.IsGradient)
        {
            const int count = 128;
            double w = bounds.Width / count, h = bounds.Height / count;
            for (int y = 0; y < count; y++)
                for (int x = 0; x < count; x++)
                    list.FillRect(new BRect(bounds.Left + x * w, bounds.Top + y * h, w, h),
                        fill.ColorAt((x + 0.5) * w, (y + 0.5) * h, bounds.Width, bounds.Height));
            return;
        }

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
        if (line.ClipHeight is null && line.ClipRight is null)
        {
            DrawLineContents(list, line, resolveImage);
            return;
        }
        // Restrict the vertical extent where the line states one, and the right
        // where it states that; otherwise include glyph overhangs and any change
        // bar, so a clip on one side does not cut the line on another.
        double left = Math.Min(line.BackgroundLeft, line.Pieces.Select(piece => piece.X - piece.Font.Size)
            .DefaultIfEmpty(line.BackgroundLeft).Min());
        double right = Math.Max(line.BackgroundLeft + line.BackgroundWidth,
            line.Pieces.Select(piece => piece.X + piece.Width + piece.Font.Size)
                .DefaultIfEmpty(line.BackgroundLeft).Max());
        if (line.ChangeBarX is double bar)
            left = Math.Min(left, bar - ChangeBarWidth);
        if (line.ClipRight is double clipRight)
            right = clipRight;
        (double top, double height) = line.ClipHeight is double clipHeight
            ? (line.Top, clipHeight)
            : (line.Top - line.Height, line.Height * 3);
        list.PushClip(new BRect(left, top, Math.Max(1, right - left), height));
        DrawLineContents(list, line, resolveImage);
        list.PopClip();
    }

    private void DrawLineContents(BRenderList list, LayoutLine line, Func<InlineImage, BImageHandle?> resolveImage)
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

            if (piece.IsTab && piece.TabLeader is string leader)
            {
                double advance = _settings.TextMeasurer is not null ? _settings.TextMeasurer.MeasureAdvance(leader, piece.Font) : BTextMeasurer.MeasureAdvance(leader, piece.Font);
                if (advance > 0 && double.IsFinite(advance))
                {
                    int count = (int)Math.Min(2000, Math.Floor(piece.Width / advance));
                    if (count > 0)
                        list.DrawText(new BTextRun(string.Concat(Enumerable.Repeat(leader, count)), piece.Font, piece.Color),
                            new BPoint(piece.X, baseline - piece.Rise - piece.Font.Size * 0.8));
                }
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
