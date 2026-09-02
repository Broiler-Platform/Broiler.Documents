using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Broiler.Documents.Model;
using Broiler.Graphics;
using Broiler.Media;

namespace Broiler.Documents.Cli.Rendering;

/// <summary>
/// Decodes each inline image once and keeps the result for the whole render:
/// layout needs its intrinsic size, and rasterization needs its pixels, and a
/// document that uses the same picture on forty pages should decode it once.
/// </summary>
/// <remarks>
/// Keyed by reference. Two <see cref="InlineImage"/> instances holding identical
/// bytes are treated as two images, which costs a decode and never produces a
/// wrong result; hashing the bytes to merge them would cost a full pass over
/// every image to save that decode.
/// </remarks>
public sealed class ImageStore : IDisposable
{
    private readonly Dictionary<InlineImage, Entry> _entries = new(ByReference.Instance);

    private readonly List<string> _notes = new();
    private bool _disposed;

    /// <summary>Images that could not be decoded, and why.</summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>How many images decoded successfully.</summary>
    public int DecodedCount { get; private set; }

    /// <summary>How many images failed to decode.</summary>
    public int FailedCount { get; private set; }

    /// <summary>
    /// The size to draw an image at, in points. An image that states its own
    /// display size keeps it; one that does not falls back to its pixel size
    /// read as CSS reference pixels, which is the convention the HTML and DOCX
    /// readers already imply when they leave the size unstated.
    /// </summary>
    public (double Width, double Height) MeasurePoints(InlineImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        // The model resolves this without touching the payload whenever the
        // picture states a size or its resource knows its own pixels, which is
        // every case but one: an encoded payload nothing could inspect.
        if (image.TryGetDisplaySize(out double width, out double height))
            return (width, height);

        Entry entry = GetOrDecode(image);
        if (entry.Bitmap is null)
        {
            // Nothing decoded it, so there is no intrinsic size to fall back on.
            // A visible placeholder box beats a zero-sized nothing: the gap shows
            // up in the render instead of silently not being there.
            return (72.0, 72.0);
        }

        double scale = PageSetup.PointsPerInch / PageSetup.PixelsPerInch;
        return (entry.Bitmap.Width * scale, entry.Bitmap.Height * scale);
    }

    /// <summary>The decoded bitmap, or null when the image could not be decoded.</summary>
    public BBitmap? Bitmap(InlineImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return GetOrDecode(image).Bitmap;
    }

    /// <summary>
    /// The renderer handle for an image, created on first use. Null when the
    /// image could not be decoded, in which case the caller draws a placeholder.
    /// </summary>
    public BImageHandle? Handle(BImageRenderer renderer, InlineImage image)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(image);

        Entry entry = GetOrDecode(image);
        if (entry.Bitmap is null)
            return null;

        if (entry.Handle is null)
            entry.Handle = renderer.CreateImage(entry.Bitmap.ToPixelBuffer());

        return entry.Handle;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (Entry entry in _entries.Values)
            entry.Bitmap?.Dispose();

        _entries.Clear();
    }

    private Entry GetOrDecode(InlineImage image)
    {
        if (_entries.TryGetValue(image, out Entry? existing))
            return existing;

        var entry = new Entry();
        try
        {
            entry.Bitmap = BBitmap.Decode(image.Data.Span);
            DecodedCount++;
        }
        catch (Exception exception) when (
            exception is MediaException or InvalidOperationException or ArgumentException
                or NotSupportedException or FormatException)
        {
            // A codec this process did not compose, or bytes that are not the
            // format the document claimed. Both are worth reporting and neither
            // is worth failing the render over: the rest of the page is still
            // information, and the placeholder says where the hole is.
            FailedCount++;
            _notes.Add(
                "could not decode image \"" + image.Name + "\" (" + image.ContentType + ", " +
                image.Data.Length + " bytes): " + exception.Message);
        }

        _entries[image] = entry;
        return entry;
    }

    private sealed class Entry
    {
        public BBitmap? Bitmap { get; set; }

        public BImageHandle? Handle { get; set; }
    }

    /// <summary>Identity, not value: two images with the same bytes stay two images.</summary>
    private sealed class ByReference : IEqualityComparer<InlineImage>
    {
        public static ByReference Instance { get; } = new();

        public bool Equals(InlineImage? x, InlineImage? y) => ReferenceEquals(x, y);

        public int GetHashCode(InlineImage obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
