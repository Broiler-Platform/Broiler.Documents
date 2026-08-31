using Broiler.Documents.Model;
using System;
using System.Collections.Generic;
using Broiler.Documents.Cli.Composition;
using Broiler.Graphics;

namespace Broiler.Documents.Cli.Rendering;

/// <summary>
/// Draws laid-out pages into bitmaps through the Broiler.Graphics software
/// renderer.
/// </summary>
/// <remarks>
/// <para>
/// Everything is expressed as a <see cref="BRenderList"/> in layout points and
/// replayed once per page at a surface scale of DPI/72, so the same layout
/// rasterizes at any resolution without a second measuring pass and without the
/// rounding that a per-command scale would introduce.
/// </para>
/// <para>
/// Underlines, strikethroughs, and highlights are drawn here rather than asked
/// for, because <see cref="BRenderCommand.DrawText"/> draws glyphs and nothing
/// else - <see cref="BFontStyle"/> carries a family, a size, a weight, and a
/// slant, and has no notion of a decoration. Drawing them as rectangles under
/// this tool's control also means their thickness and offset are stated here,
/// where a comparison can hold them constant.
/// </para>
/// </remarks>
public sealed class DocumentRasterizer : IDisposable
{
    private readonly ImageStore _images;
    private readonly LayoutSettings _settings;
    private readonly BImageRenderer _renderer = new();
    private bool _disposed;

    public DocumentRasterizer(LayoutSettings settings, ImageStore images)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _images = images ?? throw new ArgumentNullException(nameof(images));
        CodecComposition.RegisterImageCodecs();
    }

    /// <summary>Renders one page. The caller owns and disposes the bitmap.</summary>
    public BBitmap Render(LayoutPage page, PageSetup setup)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(setup);

        var list = new BRenderList();

        if (_settings.ShowContentBox)
        {
            list.StrokeRect(
                new BRect(
                    setup.ContentLeftPoints,
                    setup.ContentTopPoints,
                    setup.ContentWidthPoints,
                    setup.ContentHeightPoints),
                new BColor(0xC0, 0xC0, 0xC0),
                0.5);
        }

        // Shapes first: a letterhead's stripe sits under its text, and the model
        // has no z-order to say otherwise.
        foreach (LayoutShape shape in page.Shapes)
            DrawShape(list, shape);

        foreach (LayoutLine line in page.Lines)
            DrawLine(list, line);

        var descriptor = new BSurfaceDescriptor(
            new BSize(page.WidthPoints, page.HeightPoints),
            setup.DpiScale);

        return _renderer.RenderToImage(
            list,
            descriptor,
            new BFrameContext(setup.Background, page.Number, BRenderOptions.Default));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _renderer.Dispose();
    }

    /// <summary>
    /// Paints one shape: its fill, its outline, then its own text.
    /// </summary>
    /// <remarks>
    /// A gradient is drawn as bands of solid colour because the render list has
    /// no gradient primitive. The band count is the extent in points, capped, so
    /// a band is about a point tall and the seams fall below what the eye picks
    /// out at any sensible DPI.
    /// </remarks>
    private void DrawShape(BRenderList list, LayoutShape shape)
    {
        if (shape.Bounds.Width <= 0 || shape.Bounds.Height <= 0)
            return;

        if (shape.Fill is ShapeFill fill)
        {
            if (!fill.IsGradient)
            {
                list.FillRect(shape.Bounds, fill.Start);
            }
            else
            {
                // DrawingML measures the angle clockwise from the x axis. The
                // render list fills rectangles, so the gradient is banded along
                // whichever axis it runs closer to; a diagonal is approximated by
                // the axis it is nearer.
                double radians = fill.AngleDegrees * Math.PI / 180.0;
                bool vertical = Math.Abs(Math.Sin(radians)) >= Math.Abs(Math.Cos(radians));
                double extent = vertical ? shape.Bounds.Height : shape.Bounds.Width;
                int bands = (int)Math.Clamp(Math.Round(extent), 2, 512);

                for (int i = 0; i < bands; i++)
                {
                    double t = bands == 1 ? 0 : (double)i / (bands - 1);
                    BColor color = Mix(fill.Start, fill.End, t);
                    double start = extent * i / bands;
                    double size = (extent / bands) + 0.5; // overlap, so no seam shows

                    list.FillRect(
                        vertical
                            ? new BRect(shape.Bounds.Left, shape.Bounds.Top + start, shape.Bounds.Width, size)
                            : new BRect(shape.Bounds.Left + start, shape.Bounds.Top, size, shape.Bounds.Height),
                        color);
                }
            }
        }

        if (!shape.Outline.IsEmpty && shape.Outline.A > 0)
            list.StrokeRect(shape.Bounds, shape.Outline, 1);

        foreach (LayoutLine line in shape.Lines)
            DrawLine(list, line);
    }

    private static BColor Mix(BColor from, BColor to, double t) =>
        new(
            (byte)Math.Round(from.R + ((to.R - from.R) * t)),
            (byte)Math.Round(from.G + ((to.G - from.G) * t)),
            (byte)Math.Round(from.B + ((to.B - from.B) * t)),
            (byte)Math.Round(from.A + ((to.A - from.A) * t)));

    private void DrawLine(BRenderList list, LayoutLine line)
    {
        double baseline = line.Top + line.Baseline;

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
                DrawImage(list, piece, baseline);
                continue;
            }

            if (piece.Text.Length == 0)
                continue;

            // The renderer puts the baseline at origin.Y + size * 0.8, and the
            // layout used the same 0.8 for the piece's ascent. Subtracting it
            // back out is what keeps a 24pt run and an 8pt run on one baseline.
            double originY = baseline - (piece.Font.SizeInPixels * 0.8);
            var run = new BTextRun(piece.Text, piece.Font, piece.Color);

            if (piece.Oblique)
            {
                // A horizontal shear about the baseline: a point at height h above
                // it moves right by slant * h, and a point on it does not move.
                // Written directly because BMatrix3x2 offers translation and scale
                // and nothing else.
                double slant = _settings.ObliqueSlant;
                list.PushTransform(new BMatrix3x2(1, 0, -slant, 1, slant * baseline, 0));
                list.DrawText(run, new BPoint(piece.X, originY));
                list.PopTransform();
            }
            else
            {
                list.DrawText(run, new BPoint(piece.X, originY));
            }

            double thickness = Math.Max(0.5, piece.Font.SizeInPixels * 0.055);

            if (piece.Underline)
            {
                list.FillRect(
                    new BRect(piece.X, baseline + (piece.Font.SizeInPixels * 0.10), piece.Width, thickness),
                    piece.Color);
            }

            if (piece.Strikethrough)
            {
                list.FillRect(
                    new BRect(piece.X, baseline - (piece.Font.SizeInPixels * 0.26), piece.Width, thickness),
                    piece.Color);
            }
        }
    }

    private void DrawImage(BRenderList list, LayoutPiece piece, double baseline)
    {
        var destination = new BRect(piece.X, baseline - piece.Ascent, piece.Width, piece.Ascent);
        BImageHandle? handle = _images.Handle(_renderer, piece.Image!);

        if (handle is not BImageHandle image)
        {
            // The image did not decode. Draw the box it would have occupied with
            // a cross through it: the page then shows where content is missing
            // instead of quietly closing up around the hole, which is exactly the
            // kind of gap this tool exists to surface.
            var missing = new BColor(0xB0, 0xB0, 0xB0);
            list.StrokeRect(destination, missing, 1.0);
            list.FillRect(new BRect(destination.X, destination.Y + (destination.Height / 2), destination.Width, 1), missing);
            return;
        }

        list.DrawImage(
            image,
            new BRect(0, 0, image.PixelSize.Width, image.PixelSize.Height),
            destination);
    }

    /// <summary>Encodes a bitmap as PNG bytes.</summary>
    public static byte[] EncodePng(BBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        return bitmap.Encode(Broiler.Media.Image.ImageEncodeFormat.Png);
    }

    /// <summary>The names the <c>--format</c> option accepts, and the encoder each selects.</summary>
    public static IReadOnlyDictionary<string, Broiler.Media.Image.ImageEncodeFormat> ImageFormats { get; } =
        new Dictionary<string, Broiler.Media.Image.ImageEncodeFormat>(StringComparer.OrdinalIgnoreCase)
        {
            ["png"] = Broiler.Media.Image.ImageEncodeFormat.Png,
            ["jpeg"] = Broiler.Media.Image.ImageEncodeFormat.Jpeg,
            ["jpg"] = Broiler.Media.Image.ImageEncodeFormat.Jpeg,
            ["bmp"] = Broiler.Media.Image.ImageEncodeFormat.Bmp,
        };
}
