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

    /// <summary>
    /// How wide an outline of width zero is drawn. DrawingML <c>a:ln w="0"</c> and
    /// ODF <c>svg:stroke-width="0"</c> are hairlines, which LibreOffice draws one
    /// device pixel wide: three quarters of a point is that pixel at 96 dpi. A
    /// narrower band fell between pixel centres and drew nothing (tdf115153.rtf's <c>\dplinew0</c>
    /// rule, tdf122717.docx).
    /// </summary>
    internal const double HairlineWidth = 0.75;

    /// <summary>The colour of a changed line's bar, LibreOffice's default black.</summary>
    private static readonly BColor ChangeBarColor = new(0, 0, 0);

    private readonly LayoutSettings _settings;
    private readonly ImageStore _images;
    private bool _verticalWriting;

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
        _verticalWriting = page.VerticalWriting;

        // The page's own colour goes under everything, the content box included.
        if (page.Background is ShapeFill background)
            DrawFill(list, page.BackgroundBounds, background, resolveImage);

        if (page.PageBorders != default)
        {
            TableCellPadding inset = page.PageBorderInsets;
            double width = page.WidthPoints - inset.Left - inset.Right;
            double height = page.HeightPoints - inset.Top - inset.Bottom;
            if (width > 0 && height > 0)
                DrawCells(list, [new LayoutCell(new BRect(inset.Left, inset.Top, width, height), BColor.Empty, page.PageBorders)]);
        }

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

        foreach (BRect separator in page.ColumnSeparators)
            list.FillRect(separator, BColor.FromName("black"));

        if (page.FloatingTables.Count == 0)
        {
            DrawCells(list, page.Cells);
            DrawParagraphShading(list, page.Lines);
            DrawParagraphBorders(list, page.Lines);
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
            DrawParagraphBorders(list, page.InlineLines);
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
        double angle = shape.RotationRadians;
        if (angle == 0 || !double.IsFinite(angle))
        {
            DrawShapeContent(list, shape, resolveImage);
            return;
        }

        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        double x = shape.Bounds.Left, y = shape.Bounds.Top;
        list.PushTransform(new BMatrix3x2(cos, sin, -sin, cos,
            x - x * cos + y * sin, y - x * sin - y * cos));
        try
        {
            DrawShapeContent(list, shape, resolveImage);
        }
        finally
        {
            list.PopTransform();
        }
    }

    private void DrawShapeContent(BRenderList list, LayoutShape shape, Func<InlineImage, BImageHandle?> resolveImage)
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

        BRect bounds = shape.Bounds;
        double radius = Math.Min(bounds.Width, bounds.Height) / 6;
        BPoint apex = new(bounds.Left + bounds.Width / 2, bounds.Top);
        BPoint lowerLeft = new(bounds.Left, bounds.Bottom), lowerRight = new(bounds.Right, bounds.Bottom);
        IReadOnlyList<IReadOnlyList<(double X, double Y)>>? paths = shape.OutlinePaths is { Count: > 0 } outline ? outline : null;
        if (shape.Fill is ShapeFill fill)
        {
            if (paths is not null && fill.Image is null && fill.Hatch is null)
            {
                // The painter cannot clip a gradient to the outline, so its
                // middle colour fills it, which keeps the shape's form.
                BColor color = fill.IsGradient ? fill.ColorAt(0.5) : fill.Start;
                foreach (IReadOnlyList<(double X, double Y)> path in paths)
                    FillPolygon(list, PolygonCorners(bounds, path), color);
            }
            else if (shape.Geometry == ShapeGeometry.Ellipse)
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
            else if (shape.Geometry == ShapeGeometry.RoundedRectangle && !fill.IsGradient && !fill.HasExtendedPaint)
                list.FillRoundedRect(bounds, fill.Start, radius, radius);
            else if (shape.Geometry == ShapeGeometry.Triangle && !fill.IsGradient && !fill.HasExtendedPaint)
                list.FillTriangle(apex, lowerLeft, lowerRight, fill.Start);
            else if (shape.Geometry == ShapeGeometry.Diamond && !fill.IsGradient && !fill.HasExtendedPaint)
            {
                (BPoint top, BPoint right, BPoint bottom, BPoint left) = DiamondCorners(bounds);
                list.FillTriangle(top, right, bottom, fill.Start);
                list.FillTriangle(top, bottom, left, fill.Start);
            }
            else DrawFill(list, shape.Bounds, fill, resolveImage);
        }

        // Over the fill and under the outline: a framed picture keeps its frame.
        if (shape.Image is InlineImage image)
            DrawShapeImage(list, image, shape.Bounds, resolveImage);

        if (!shape.Outline.IsEmpty && shape.Outline.A > 0)
        {
            double thickness = shape.OutlineWidth == 0 ? HairlineWidth : shape.OutlineWidth;
            if (paths is not null)
            {
                foreach (IReadOnlyList<(double X, double Y)> path in paths)
                {
                    List<BPoint> corners = PolygonCorners(bounds, path);
                    for (int i = 0; i < corners.Count; i++)
                        DrawSegment(list, corners[i], corners[(i + 1) % corners.Count], shape.Outline, thickness);
                }
            }
            else if (shape.Geometry == ShapeGeometry.Ellipse)
                list.Append(new BRenderCommand.StrokeRoundedRect(shape.Bounds, shape.Outline, shape.Bounds.Width / 2, shape.Bounds.Height / 2, thickness));
            else if (shape.Geometry == ShapeGeometry.RoundedRectangle)
                list.StrokeRoundedRect(bounds, shape.Outline, radius, radius, thickness);
            else if (shape.Geometry == ShapeGeometry.Triangle)
            {
                DrawSegment(list, apex, lowerLeft, shape.Outline, thickness);
                DrawSegment(list, lowerLeft, lowerRight, shape.Outline, thickness);
                DrawSegment(list, lowerRight, apex, shape.Outline, thickness);
            }
            else if (shape.Geometry == ShapeGeometry.Diamond)
            {
                (BPoint top, BPoint right, BPoint bottom, BPoint left) = DiamondCorners(bounds);
                DrawSegment(list, top, right, shape.Outline, thickness);
                DrawSegment(list, right, bottom, shape.Outline, thickness);
                DrawSegment(list, bottom, left, shape.Outline, thickness);
                DrawSegment(list, left, top, shape.Outline, thickness);
            }
            else if (shape.RotationRadians != 0)
            {
                // A stroked rectangle is not turned by the transform it is drawn
                // under, so a rotated box's outline is drawn as its four edges
                // (tdf76446.docx).
                BPoint topLeft = new(bounds.Left, bounds.Top), topRight = new(bounds.Right, bounds.Top);
                DrawSegment(list, topLeft, topRight, shape.Outline, thickness);
                DrawSegment(list, topRight, lowerRight, shape.Outline, thickness);
                DrawSegment(list, lowerRight, lowerLeft, shape.Outline, thickness);
                DrawSegment(list, lowerLeft, topLeft, shape.Outline, thickness);
            }
            else list.StrokeRect(shape.Bounds, shape.Outline, thickness);
        }

        // Turned text was laid out in the box turned with it; a quarter turn
        // about the box's centre puts it back: clockwise for text that reads
        // downwards, anticlockwise for text that reads upwards.
        bool turned = shape.TextFlow != ShapeTextFlow.Horizontal;
        if (turned)
        {
            double sin = shape.TextFlow == ShapeTextFlow.TopToBottom ? 1 : -1;
            double cx = bounds.Left + bounds.Width / 2, cy = bounds.Top + bounds.Height / 2;
            list.PushTransform(new BMatrix3x2(0, sin, -sin, 0, cx + cy * sin, cy - cx * sin));
        }
        try
        {
            DrawCells(list, shape.Cells);
            DrawParagraphShading(list, shape.Lines);
            if (!DrawStretchedText(list, shape, resolveImage))
            {
                foreach (LayoutLine line in shape.Lines)
                    DrawLine(list, line, resolveImage);
            }
        }
        finally
        {
            if (turned)
                list.PopTransform();
        }
    }

    /// <summary>
    /// Paints WordArt text (<see cref="LayoutShape.StretchesTextToBox"/>) stretched
    /// to its box, as Word and LibreOffice fit a VML text path's glyphs to the
    /// shape: each line's ink from the box's left edge to its right, and the ink
    /// of all the lines from its top to its bottom. The glyph outlines are then
    /// stroked in <see cref="LayoutShape.TextOutline"/>. False, with nothing
    /// drawn, when the glyphs' outlines are not known, so the lines are drawn as
    /// they were laid out.
    /// </summary>
    private bool DrawStretchedText(BRenderList list, LayoutShape shape, Func<InlineImage, BImageHandle?> resolveImage)
    {
        if (!shape.StretchesTextToBox || shape.Lines.Count == 0 || shape.TextFlow != ShapeTextFlow.Horizontal ||
            _settings.TextMeasurer is not Fonts.PaginationFontAdapter adapter)
            return false;

        var lines = new List<(LayoutLine Line, List<BPoint[]> Contours, double Left, double Right)>();
        double top = double.PositiveInfinity, bottom = double.NegativeInfinity;
        foreach (LayoutLine line in shape.Lines)
        {
            List<BPoint[]> contours = [];
            double left = double.PositiveInfinity, right = double.NegativeInfinity;
            foreach (LayoutPiece piece in line.Pieces)
            {
                if (piece.IsImage || piece.Text.Length == 0)
                    continue;
                if (adapter.FontService.ResolveFace(piece.Font.FamilyName, piece.Bold, piece.Italic) is not { TrueType: { } font } face ||
                    face.UnitsPerEm <= 0)
                    return false;

                double scale = piece.Font.Size / face.UnitsPerEm;
                double x = piece.X, baseline = line.Top + line.Baseline - piece.Rise;
                for (int i = 0; i < piece.Text.Length;)
                {
                    int codepoint = char.ConvertToUtf32(piece.Text, i);
                    string character = char.ConvertFromUtf32(codepoint);
                    i += character.Length;
                    foreach (var contour in font.GetGlyphContours(font.GetGlyphIndex(codepoint)))
                    {
                        BPoint[] points = [.. contour.Select(point => new BPoint(x + point.X * scale, baseline - point.Y * scale))];
                        foreach (BPoint point in points)
                        {
                            left = Math.Min(left, point.X);
                            right = Math.Max(right, point.X);
                            top = Math.Min(top, point.Y);
                            bottom = Math.Max(bottom, point.Y);
                        }
                        if (points.Length > 1)
                            contours.Add(points);
                    }
                    x += face.GetAdvancePoints(character, piece.Font.Size);
                }
            }
            if (contours.Count > 0)
                lines.Add((line, contours, left, right));
        }
        if (lines.Count == 0 || bottom - top <= 0)
            return false;

        BRect box = shape.Bounds;
        double scaleY = box.Height / (bottom - top);
        foreach ((LayoutLine line, List<BPoint[]> contours, double left, double right) in lines)
        {
            double scaleX = right - left > 0 ? box.Width / (right - left) : 1;
            var matrix = new BMatrix3x2(scaleX, 0, 0, scaleY, box.Left - left * scaleX, box.Top - top * scaleY);
            list.PushTransform(matrix);
            try
            {
                DrawLine(list, line, resolveImage);
            }
            finally
            {
                list.PopTransform();
            }

            if (shape.TextOutline.IsEmpty || shape.TextOutline.A == 0)
                continue;
            double width = shape.TextOutlineWidth > 0 ? shape.TextOutlineWidth : HairlineWidth;
            foreach (BPoint[] contour in contours)
            {
                for (int i = 0; i < contour.Length; i++)
                {
                    BPoint start = contour[i], end = contour[(i + 1) % contour.Length];
                    DrawSegment(list,
                        new BPoint(box.Left + (start.X - left) * scaleX, box.Top + (start.Y - top) * scaleY),
                        new BPoint(box.Left + (end.X - left) * scaleX, box.Top + (end.Y - top) * scaleY),
                        shape.TextOutline, width);
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Paints a box with no breadth as the rule its outline makes: a filled band
    /// of Word's default line width, centred on the box's one edge.
    /// </summary>
    private static void DrawRule(BRenderList list, LayoutShape shape)
    {
        double width = shape.OutlineWidth == 0 ? HairlineWidth : shape.OutlineWidth;
        if (shape.Outline.IsEmpty || shape.Outline.A == 0)
            return;

        BRect bounds = shape.Bounds;
        if (bounds.Width <= 0 && bounds.Height > 0)
            list.FillRect(new BRect(bounds.Left - (width / 2), bounds.Top, width, bounds.Height), shape.Outline);
        else if (bounds.Height <= 0 && bounds.Width > 0)
            list.FillRect(new BRect(bounds.Left, bounds.Top - (width / 2), bounds.Width, width), shape.Outline);
    }

    /// <summary>The corners of one of a shape's outline polygons on the page.</summary>
    private static List<BPoint> PolygonCorners(BRect bounds, IReadOnlyList<(double X, double Y)> path)
    {
        var corners = new List<BPoint>(path.Count);
        foreach ((double x, double y) in path)
        {
            var corner = new BPoint(bounds.Left + x * bounds.Width, bounds.Top + y * bounds.Height);
            if (corners.Count == 0 || corners[^1] != corner)
                corners.Add(corner);
        }
        if (corners.Count > 1 && corners[0] == corners[^1])
            corners.RemoveAt(corners.Count - 1);
        return corners;
    }

    /// <summary>
    /// Fills a simple polygon, convex or not - a star's points - as the
    /// triangles ear clipping cuts it into.
    /// </summary>
    private static void FillPolygon(BRenderList list, List<BPoint> corners, BColor color)
    {
        if (corners.Count < 3)
            return;
        double area = 0;
        for (int i = 0; i < corners.Count; i++)
        {
            BPoint a = corners[i], b = corners[(i + 1) % corners.Count];
            area += a.X * b.Y - b.X * a.Y;
        }
        double orientation = Math.Sign(area);
        if (orientation == 0)
            return;

        // Neighbouring triangles each shade their shared edge half way, which
        // leaves a pale seam across an opaque fill; grown a little about their
        // centres they overlap there instead.
        double grow = color.A == 255 ? 0.3 : 0;

        var remaining = new List<BPoint>(corners);
        int guard = remaining.Count * remaining.Count;
        int index = 0;
        while (remaining.Count > 3 && guard-- > 0)
        {
            int count = remaining.Count;
            BPoint previous = remaining[(index + count - 1) % count], current = remaining[index % count], next = remaining[(index + 1) % count];
            if (Cross(previous, current, next) * orientation > 0 && !ContainsCorner(remaining, previous, current, next, orientation))
            {
                Fill(previous, current, next);
                remaining.RemoveAt(index % count);
                index = Math.Max(0, index - 1);
                continue;
            }
            index = (index + 1) % count;
        }

        if (remaining.Count == 3)
            Fill(remaining[0], remaining[1], remaining[2]);
        else
        {
            // Not simple after all: fan it rather than leave a hole.
            for (int i = 1; i + 1 < remaining.Count; i++)
                Fill(remaining[0], remaining[i], remaining[i + 1]);
        }

        void Fill(BPoint a, BPoint b, BPoint c)
        {
            double cx = (a.X + b.X + c.X) / 3, cy = (a.Y + b.Y + c.Y) / 3;
            list.FillTriangle(Grown(a), Grown(b), Grown(c), color);

            BPoint Grown(BPoint p)
            {
                double dx = p.X - cx, dy = p.Y - cy, length = Math.Sqrt(dx * dx + dy * dy);
                return length <= 0 || grow == 0 ? p : new BPoint(p.X + dx / length * grow, p.Y + dy / length * grow);
            }
        }

        static double Cross(BPoint a, BPoint b, BPoint c) => (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);

        static bool ContainsCorner(List<BPoint> points, BPoint a, BPoint b, BPoint c, double orientation)
        {
            foreach (BPoint p in points)
            {
                if (p == a || p == b || p == c)
                    continue;
                if (Cross(a, b, p) * orientation >= 0 && Cross(b, c, p) * orientation >= 0 && Cross(c, a, p) * orientation >= 0)
                    return true;
            }
            return false;
        }
    }

    /// <summary>The midpoints of a box's four edges, the corners of the diamond it holds.</summary>
    private static (BPoint Top, BPoint Right, BPoint Bottom, BPoint Left) DiamondCorners(BRect bounds)
    {
        double middleX = bounds.Left + bounds.Width / 2, middleY = bounds.Top + bounds.Height / 2;
        return (new BPoint(middleX, bounds.Top), new BPoint(bounds.Right, middleY),
            new BPoint(middleX, bounds.Bottom), new BPoint(bounds.Left, middleY));
    }

    /// <summary>
    /// Draws a piece's text, with each private-use character its face has no
    /// glyph for drawn as the face's missing glyph (<c>.notdef</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The renderer draws a character its face lacks as a built-in block
    /// question mark. LibreOffice first looks for the character in the other
    /// installed faces, and only where none has it draws the face's own
    /// missing glyph: a box in DejaVu Sans, nothing in Liberation Serif, whose
    /// <c>.notdef</c> has no contours. A private-use character, which is what a
    /// symbol font's text is (Wingdings' arrow at U+F0E7), is in no face of the
    /// reference set, so the missing glyph is what LibreOffice draws.
    /// </para>
    /// <para>
    /// Other characters are left to the renderer: LibreOffice draws Arabic
    /// from another face that has it, and East Asian text in the run's East
    /// Asian font, neither of which this layout models. Drawing the Latin
    /// face's empty <c>.notdef</c> there left tdf130314.docx's Chinese
    /// paragraphs blank where LibreOffice draws boxes.
    /// Where the face's outlines are not known, the text is drawn as it is.
    /// </para>
    /// </remarks>
    private void DrawRun(BRenderList list, LayoutPiece piece, BTextRun run, double originY, double baseline)
    {
        if (_settings.TextMeasurer is not Fonts.PaginationFontAdapter adapter ||
            adapter.FontService.ResolveFace(piece.Font.FamilyName, piece.Bold, piece.Italic) is not { TrueType: { } font } face ||
            face.UnitsPerEm <= 0 || !HasMissingGlyph(piece.Text, font))
        {
            list.DrawText(run, new BPoint(piece.X, originY));
            return;
        }

        string text = piece.Text;
        double size = piece.Font.Size, scale = size / face.UnitsPerEm;
        int start = 0;
        for (int i = 0; i <= text.Length;)
        {
            int codepoint = i < text.Length ? char.ConvertToUtf32(text, i) : -1;
            int length = codepoint > 0xFFFF ? 2 : 1;
            if (codepoint >= 0 && !IsMissingGlyph(codepoint, font))
            {
                i += length;
                continue;
            }

            if (i > start)
            {
                list.DrawText(new BTextRun(text[start..i], piece.Font, piece.Color),
                    new BPoint(piece.X + face.GetAdvancePoints(text[..start], size), originY));
            }
            if (codepoint < 0)
                break;

            double x = piece.X + face.GetAdvancePoints(text[..i], size);
            FillContours(list, [.. font.GetGlyphContours(0).Select(contour =>
                contour.Select(point => new BPoint(x + point.X * scale, baseline - point.Y * scale)).ToArray())], piece.Color);
            i += length;
            start = i;
        }
    }

    private static bool HasMissingGlyph(string text, TrueTypeFont font)
    {
        for (int i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
        {
            if (IsMissingGlyph(char.ConvertToUtf32(text, i), font))
                return true;
        }
        return false;
    }

    /// <summary>True for a private-use character the face has no glyph for (<see cref="DrawRun"/>).</summary>
    private static bool IsMissingGlyph(int codepoint, TrueTypeFont font) =>
        System.Globalization.CharUnicodeInfo.GetUnicodeCategory(codepoint) == System.Globalization.UnicodeCategory.PrivateUse &&
        font.GetGlyphIndex(codepoint) == 0;

    /// <summary>
    /// Fills closed polygons by the non-zero winding rule, as a glyph's
    /// contours are filled: each band between two corner heights is split
    /// into the trapezoids its edges bound, drawn as two triangles each.
    /// </summary>
    internal static void FillContours(BRenderList list, IReadOnlyList<BPoint[]> contours, BColor color)
    {
        var edges = new List<(BPoint Top, BPoint Bottom, int Winding)>();
        foreach (BPoint[] contour in contours)
        {
            for (int i = 0; i < contour.Length && contour.Length > 2; i++)
            {
                BPoint a = contour[i], b = contour[(i + 1) % contour.Length];
                if (a.Y == b.Y)
                    continue;
                edges.Add(a.Y < b.Y ? (a, b, 1) : (b, a, -1));
            }
        }
        if (edges.Count == 0 || color.IsEmpty || color.A == 0)
            return;

        double[] heights = [.. edges.SelectMany(edge => new[] { edge.Top.Y, edge.Bottom.Y }).Distinct().Order()];
        for (int band = 0; band + 1 < heights.Length; band++)
        {
            double top = heights[band], bottom = heights[band + 1], middle = (top + bottom) / 2;
            var crossings = edges
                .Where(edge => edge.Top.Y <= top && edge.Bottom.Y >= bottom)
                .Select(edge => (Middle: At(edge, middle), Top: At(edge, top), Bottom: At(edge, bottom), edge.Winding))
                .OrderBy(crossing => crossing.Middle)
                .ToList();
            int winding = 0;
            for (int i = 0; i + 1 < crossings.Count; i++)
            {
                winding += crossings[i].Winding;
                if (winding == 0)
                    continue;
                var (left, right) = (crossings[i], crossings[i + 1]);
                list.FillTriangle(new BPoint(left.Top, top), new BPoint(right.Top, top), new BPoint(right.Bottom, bottom), color);
                list.FillTriangle(new BPoint(left.Top, top), new BPoint(right.Bottom, bottom), new BPoint(left.Bottom, bottom), color);
            }
        }

        static double At((BPoint Top, BPoint Bottom, int Winding) edge, double y) =>
            edge.Top.X + (edge.Bottom.X - edge.Top.X) * (y - edge.Top.Y) / (edge.Bottom.Y - edge.Top.Y);
    }

    private static void DrawSegment(BRenderList list, BPoint start, BPoint end, BColor color, double width)
    {
        if (color.IsEmpty || color.A == 0) return;
        double dx = end.X - start.X, dy = end.Y - start.Y, length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0) return;
        double half = (width == 0 ? HairlineWidth : width) / 2;
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

    /// <summary>
    /// Paints the borders of body paragraphs: one box round each run of lines
    /// of a paragraph, and of the neighbours that join it, in one column. The
    /// box stands outside the text by each edge's distance and line, from the
    /// indent (the hanging first line's, where it hangs) to the right indent.
    /// </summary>
    private static void DrawParagraphBorders(BRenderList list, IReadOnlyList<LayoutLine> lines)
    {
        List<LayoutCell>? boxes = null;
        LayoutLine? first = null, last = null;
        double left = 0, right = 0;

        foreach (LayoutLine line in lines)
        {
            if (line.Border is not { } border)
                continue;

            double lineLeft = line.IndentLeft;
            double lineRight = line.IndentLeft + line.IndentWidth;
            bool continues = first is not null && last is not null && Equals(first.Border, border) &&
                (line.ParagraphIndex == last.ParagraphIndex || (line.ParagraphIndex == last.ParagraphIndex + 1 && line.BorderJoinsPrevious)) &&
                Math.Abs(lineRight - right) < 0.01 && line.Top >= last.Top + last.Height - 0.01;
            if (!continues)
            {
                Flush();
                first = line;
                left = lineLeft;
                right = lineRight;
            }

            left = Math.Min(left, Math.Min(lineLeft, line.Pieces.Count > 0 ? line.Pieces[0].X : lineLeft));
            last = line;
        }

        Flush();
        if (boxes is not null)
            DrawCells(list, boxes);

        void Flush()
        {
            if (first?.Border is { } border && last is not null)
            {
                CellBorders edges = border.Edges;
                double top = first.Top - first.LeadingSkip - border.Spacing.Top - (edges.Top.IsVisible ? edges.Top.Width : 0);
                double bottom = last.Top + last.Height + border.Spacing.Bottom + (edges.Bottom.IsVisible ? edges.Bottom.Width : 0);
                double boxLeft = left - border.Spacing.Left - (edges.Left.IsVisible ? edges.Left.Width : 0);
                double boxRight = right + border.Spacing.Right + (edges.Right.IsVisible ? edges.Right.Width : 0);
                if (bottom > top && boxRight > boxLeft)
                    (boxes ??= []).Add(new LayoutCell(new BRect(boxLeft, top, boxRight - boxLeft, bottom - top), BColor.Empty, edges));
            }

            first = last = null;
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
        if (image.Presentation.Shadow is ImageShadow shadow && !shadow.Color.IsEmpty && shadow.Color.A > 0)
            list.FillRect(shadow.Bounds(destination), shadow.Color);

        if (resolveImage(image) is not BImageHandle decoded)
        {
            if (image.MarksMissingPayload || !image.IsMissingPayload)
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
                    // Each leader character is set at the advance the layout
                    // measured, not run on as one string: the face the renderer
                    // draws a run with can advance wider than the measured one
                    // (a bold leader did), and a run would then spill past the
                    // tab over the text after it and off the page.
                    int count = (int)Math.Min(2000, Math.Floor(piece.Width / advance));
                    var leaderRun = new BTextRun(leader, piece.Font, piece.Color);
                    double leaderY = baseline - piece.Rise - piece.Font.Size * 0.8;
                    for (int i = 0; i < count; i++)
                        list.DrawText(leaderRun, new BPoint(piece.X + i * advance, leaderY));
                }
            }

            if (piece.Text.Length == 0 ||
                (_verticalWriting && _settings.TextMeasurer is Fonts.PaginationFontAdapter adapter &&
                 adapter.HasUnmappedVerticalCjk(piece.Text, piece.Font)))
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
                DrawRun(list, piece, run, originY, pieceBaseline);
                list.PopTransform();
            }
            else
            {
                DrawRun(list, piece, run, originY, pieceBaseline);
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
        ImagePresentation presentation = piece.Image!.Presentation;
        if (!presentation.IsDefault)
        {
            // The line box holds the picture's effect extent around its turned box.
            TableCellPadding extent = presentation.EffectExtent;
            double left = Math.Max(0, extent.Left), top = Math.Max(0, extent.Top);
            destination = new BRect(destination.X + left, destination.Y + top,
                Math.Max(1, destination.Width - left - Math.Max(0, extent.Right)),
                Math.Max(1, destination.Height - top - Math.Max(0, extent.Bottom)));
            if (presentation.Shadow is ImageShadow shadow && !shadow.Color.IsEmpty && shadow.Color.A > 0)
                list.FillRect(shadow.Bounds(destination), shadow.Color);
            // A turned picture's bitmap is already turned (ImagePresentation.Apply)
            // and covers its turned bounding box, centred on the line's box.
            if (presentation.TurnedBounds is (double width, double height))
            {
                destination = new BRect(destination.X + (destination.Width - width) / 2,
                    destination.Y + (destination.Height - height) / 2, width, height);
            }
        }

        DrawImageInto(list, piece.Image, destination, resolveImage);
    }

    private static void DrawImageInto(BRenderList list, InlineImage picture, BRect destination, Func<InlineImage, BImageHandle?> resolveImage)
    {
        if (resolveImage(picture) is not BImageHandle image)
        {
            // The image did not decode. Draw the box it would have occupied with
            // a cross through it: the page then shows where content is missing
            // instead of quietly closing up around the hole - unless the source
            // prints nothing there (InlineImage.MarksMissingPayload).
            if (picture.MarksMissingPayload || !picture.IsMissingPayload)
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
