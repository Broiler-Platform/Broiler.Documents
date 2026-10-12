using Broiler.Documents.Model;
using System;
using System.Collections.Generic;
using Broiler.Documents.Cli.Composition;
using Broiler.Documents.Pagination;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using Broiler.Graphics.Text;
using Broiler.Media.Image;

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
/// What is drawn comes from <see cref="DocumentPagePainter"/>, the same painter a
/// print preview and a printer replay, so a page this tool renders is the page an
/// application prints. Only the resolution and the background are this tool's.
/// </para>
/// </remarks>
public sealed class DocumentRasterizer : IDisposable
{
    private readonly ImageStore _images;
    private readonly DocumentPagePainter _painter;
    private readonly BImageRenderer _renderer = new();
    private bool _disposed;

    public DocumentRasterizer(LayoutSettings settings, ImageStore images)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _images = images ?? throw new ArgumentNullException(nameof(images));
        _painter = new DocumentPagePainter(settings, images);
        CodecComposition.RegisterImageCodecs();
    }

    /// <summary>
    /// Renders one page laid out in <paramref name="box"/> at the resolution and on the
    /// background <paramref name="setup"/> states. The caller owns and disposes the bitmap.
    /// </summary>
    public BBitmap Render(LayoutPage page, DocumentPageBox box, PageSetup setup)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(box);
        ArgumentNullException.ThrowIfNull(setup);

        var list = new BRenderList();
        _painter.Paint(page, box, list, image => _images.Handle(_renderer, image));

        var descriptor = new BSurfaceDescriptor(new BSize(page.WidthPoints, page.HeightPoints), setup.DpiScale);

        return _renderer.RenderToImage(list, descriptor, new BFrameContext(setup.Background, page.Number, BRenderOptions.Default));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _renderer.Dispose();
    }

    /// <summary>Encodes a bitmap as PNG bytes.</summary>
    public static byte[] EncodePng(BBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        return bitmap.Encode(ImageEncodeFormat.Png);
    }

    /// <summary>The names the <c>--format</c> option accepts, and the encoder each selects.</summary>
    public static IReadOnlyDictionary<string, ImageEncodeFormat> ImageFormats { get; } =
        new Dictionary<string, ImageEncodeFormat>(StringComparer.OrdinalIgnoreCase)
        {
            ["png"] = ImageEncodeFormat.Png,
            ["jpeg"] = ImageEncodeFormat.Jpeg,
            ["jpg"] = ImageEncodeFormat.Jpeg,
            ["bmp"] = ImageEncodeFormat.Bmp,
        };
}
