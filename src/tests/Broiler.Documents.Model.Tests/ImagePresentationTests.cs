using Broiler.Graphics;

namespace Broiler.Documents.Model.Tests;

/// <summary>
/// How a picture is cropped and shaped: the source rectangle a crop selects, and
/// the alpha an ellipse mask leaves behind.
/// </summary>
public sealed class ImagePresentationTests
{
    private static BBitmap Opaque(int width, int height)
    {
        var bitmap = new BBitmap(width, height);
        bitmap.Clear(new BColor(0x20, 0x40, 0x60, 0xFF));
        return bitmap;
    }

    [Fact(Timeout = 600000)]
    public void The_Default_Presentation_Hands_Back_The_Same_Bitmap()
    {
        using BBitmap source = Opaque(8, 8);

        // Reference equality is the contract: it is how a caller knows it does not
        // own a second bitmap to dispose.
        Assert.Same(source, ImagePresentation.Default.Apply(source));
    }

    [Fact(Timeout = 600000)]
    public void A_Crop_Selects_Its_Fraction_Of_The_Source()
    {
        var presentation = new ImagePresentation { CropTop = 0.25, CropLeft = 0.5 };
        BRect source = presentation.SourceRect(100, 80);

        Assert.Equal(50, source.Left, 3);
        Assert.Equal(20, source.Top, 3);
        Assert.Equal(50, source.Width, 3);
        Assert.Equal(60, source.Height, 3);
    }

    [Fact(Timeout = 600000)]
    public void A_Crop_Produces_A_Smaller_Bitmap()
    {
        using BBitmap source = Opaque(100, 80);
        using BBitmap cropped = new ImagePresentation { CropTop = 0.25, CropLeft = 0.5 }.Apply(source);

        Assert.Equal(50, cropped.Width);
        Assert.Equal(60, cropped.Height);
    }

    [Theory(Timeout = 600000)]
    [InlineData(0.7, 0.6)]
    [InlineData(1.0, 0.0)]
    [InlineData(0.5, 0.5)]
    public void Crops_That_Meet_Or_Cross_Keep_The_Whole_Source(double left, double right)
    {
        // Honouring them leaves no rectangle, and a picture drawn as nothing is a
        // picture lost. The whole source is used instead.
        BRect source = new ImagePresentation { CropLeft = left, CropRight = right }.SourceRect(100, 40);

        Assert.Equal(0, source.Left);
        Assert.Equal(100, source.Width, 3);
    }

    [Fact(Timeout = 600000)]
    public void A_Negative_Crop_Asks_For_Nothing()
    {
        // Some formats allow a negative inset to pad a picture out. It is not
        // represented, and it must not be read as a crop of the other sign.
        BRect source = new ImagePresentation { CropLeft = -0.5 }.SourceRect(100, 40);

        Assert.Equal(0, source.Left);
        Assert.Equal(100, source.Width, 3);
    }

    [Fact(Timeout = 600000)]
    public void An_Ellipse_Clears_The_Corners_And_Keeps_The_Middle()
    {
        using BBitmap source = Opaque(64, 64);
        using BBitmap masked = new ImagePresentation { Mask = ImageMask.Ellipse }.Apply(source);

        Assert.Equal(0, masked.GetPixel(0, 0).A);
        Assert.Equal(0, masked.GetPixel(63, 0).A);
        Assert.Equal(0, masked.GetPixel(0, 63).A);
        Assert.Equal(0, masked.GetPixel(63, 63).A);
        Assert.Equal(255, masked.GetPixel(32, 32).A);

        // The colour channels are untouched: alpha is straight here, so shaping a
        // picture scales that channel alone and the backends premultiply on upload.
        BColor middle = masked.GetPixel(32, 32);
        Assert.Equal(0x20, middle.R);
        Assert.Equal(0x40, middle.G);
        Assert.Equal(0x60, middle.B);
    }

    [Fact(Timeout = 600000)]
    public void The_Ellipse_Edge_Is_Sampled_Rather_Than_Stepped()
    {
        using BBitmap source = Opaque(64, 64);
        using BBitmap masked = new ImagePresentation { Mask = ImageMask.Ellipse }.Apply(source);

        // A hard in-or-out test gives a portrait a visibly stepped rim. Somewhere
        // along the boundary there must be a pixel that is neither in nor out.
        bool partial = false;
        for (int x = 0; x < 64 && !partial; x++)
        {
            for (int y = 0; y < 64; y++)
            {
                byte alpha = masked.GetPixel(x, y).A;
                if (alpha is > 0 and < 255)
                {
                    partial = true;
                    break;
                }
            }
        }

        Assert.True(partial, "the mask has a hard edge; no pixel is partially covered");
    }

    [Fact(Timeout = 600000)]
    public void The_Ellipse_Is_Inscribed_In_The_Cropped_Box_Not_The_Source()
    {
        // Order matters. Masking first and cropping after would leave a slice of
        // an ellipse; cropping first puts the ellipse in the box actually drawn,
        // so the centre of the *result* is opaque and its corners are not.
        using BBitmap source = Opaque(80, 80);
        using BBitmap presented = new ImagePresentation
        {
            CropTop = 0.5,
            Mask = ImageMask.Ellipse,
        }.Apply(source);

        Assert.Equal(80, presented.Width);
        Assert.Equal(40, presented.Height);
        Assert.Equal(255, presented.GetPixel(40, 20).A);
        Assert.Equal(0, presented.GetPixel(0, 0).A);
        Assert.Equal(0, presented.GetPixel(79, 39).A);
    }

    [Fact(Timeout = 600000)]
    public void An_Images_Presentation_Survives_Resizing_And_Re_Identifying_It()
    {
        // Every With method has to carry it, or an edit silently un-crops a
        // picture - and the resource gate re-identifies one on every admission.
        var presentation = new ImagePresentation { CropTop = 0.1, Mask = ImageMask.Ellipse };
        var image = new InlineImage(
            new byte[] { 1, 2, 3 }, "image/png", 10, 10, presentation: presentation);

        Assert.Equal(ImageMask.Ellipse, image.WithSize(20, 20).Presentation.Mask);
        Assert.Equal(ImageMask.Ellipse, image.WithAltText("x").Presentation.Mask);
        Assert.Equal(0.1, image.WithSize(20, 20).Presentation.CropTop, 5);
    }
}
