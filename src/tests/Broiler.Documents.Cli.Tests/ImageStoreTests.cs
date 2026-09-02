using System;
using Broiler.Documents.Cli.Rendering;
using Broiler.Documents.Model;
using Broiler.Graphics;
using Xunit;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// Covers the two payloads <see cref="ImageStore"/> has to render from.
/// </summary>
/// <remarks>
/// A picture pasted from a file arrives as encoded bytes and is decoded here. A
/// picture recovered from inside a container — a PDF image is the case that
/// prompted this — arrives as samples a decoder already produced, and has
/// nothing left to decode. The store used to know only the first, so the second
/// counted as a failed decode and drew a placeholder.
/// </remarks>
public sealed class ImageStoreTests
{
    private static BPixelBuffer Checkerboard(int width, int height)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte level = ((x / 4) + (y / 4)) % 2 == 0 ? (byte)0 : (byte)255;
                int i = ((y * width) + x) * 4;
                rgba[i] = level;
                rgba[i + 1] = level;
                rgba[i + 2] = level;
                rgba[i + 3] = 255;
            }
        }

        return new BPixelBuffer(width, height, rgba);
    }

    private static InlineImage Decoded(int width, int height) =>
        new(BImageResource.FromPixels(Checkerboard(width, height)));

    [Fact(Timeout = 600000)]
    public void A_Decoded_Payload_Needs_No_Decoding()
    {
        using var store = new ImageStore();
        InlineImage image = Decoded(32, 16);

        BBitmap? bitmap = store.Bitmap(image);

        Assert.NotNull(bitmap);
        Assert.Equal(32, bitmap!.Width);
        Assert.Equal(16, bitmap.Height);
        Assert.Equal(1, store.DecodedCount);
        Assert.Equal(0, store.FailedCount);
        Assert.Empty(store.Notes);
    }

    [Fact(Timeout = 600000)]
    public void A_Decoded_Payload_Measures_From_Its_Own_Pixels()
    {
        using var store = new ImageStore();

        // 96 pixels per inch into 72 points per inch: a 96-pixel picture is one
        // inch wide, which is 72 points.
        (double width, double height) = store.MeasurePoints(Decoded(96, 48));

        Assert.Equal(72, width, 3);
        Assert.Equal(36, height, 3);
    }

    [Fact(Timeout = 600000)]
    public void The_Store_Does_Not_Alias_A_Shared_Resource()
    {
        // The resource is immutable and shared across the whole document, and
        // BBitmap is mutable by API. If the store wrapped the resource's buffer
        // instead of copying it, drawing on one page's bitmap would change the
        // same picture everywhere else it appears.
        using var store = new ImageStore();
        InlineImage image = Decoded(8, 8);

        BBitmap bitmap = Assert.IsType<BBitmap>(store.Bitmap(image));
        bitmap.SetPixel(0, 0, new BColor(255, 0, 0, 255));

        Assert.True(image.Resource.TryGetPixels(out BPixelBuffer? pixels));
        Assert.Equal(0, pixels!.Rgba[0]);
        Assert.Equal(0, pixels.Rgba[1]);
    }

    [Fact(Timeout = 600000)]
    public void Bytes_That_Are_Not_An_Image_Are_Reported_Rather_Than_Thrown()
    {
        using var store = new ImageStore();
        var image = new InlineImage(new byte[] { 1, 2, 3, 4 }, "image/png", 0, 0, name: "broken");

        Assert.Null(store.Bitmap(image));
        Assert.Equal(1, store.FailedCount);
        Assert.Contains("broken", Assert.Single(store.Notes), StringComparison.Ordinal);
    }
}
