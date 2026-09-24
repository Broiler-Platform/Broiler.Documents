using System.IO.Compression;
using Broiler.Graphics.Imaging;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers the raw-sample subset PDF roadmap §9.3 approved: which images become
/// pixels in the document, which are refused, and what a refusal says.
/// </summary>
/// <remarks>
/// <para>
/// Every fixture here decodes through the base build alone. That is the point of
/// the subset: an image whose samples are its own needs no composed codec, so
/// the ordinary Flate and unfiltered pictures that make up most documents are
/// reachable in a build that composes nothing.
/// </para>
/// <para>
/// The assertions read pixels rather than counting images. A projection that
/// produced the right number of wrong colours would pass a test that only asked
/// whether something arrived, and the whole risk in unpacking bit depths,
/// applying <c>/Decode</c>, and looking up a palette is landing on a plausible
/// wrong picture.
/// </para>
/// </remarks>
public sealed class PdfImageProjectionTests
{
    // ---- the device spaces ----------------------------------------------------

    [Fact]
    public void DeviceGray_At_Eight_Bits_Becomes_Grey_Pixels()
    {
        BPixelBuffer pixels = Pixels(Read(Document(
            "/Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8",
            [0x00, 0xFF])));

        Assert.Equal((0, 0, 0, 255), At(pixels, 0, 0));
        Assert.Equal((255, 255, 255, 255), At(pixels, 1, 0));
    }

    [Fact]
    public void DeviceGray_At_One_Bit_Restarts_Each_Row_On_A_Byte_Boundary()
    {
        // Five pixels a row at one bit occupy five bits, and the next row starts
        // in the next byte rather than three bits into this one. Reading the rows
        // as one continuous bit stream is the classic way to produce a sheared
        // picture, so the padding is what this asserts.
        BPixelBuffer pixels = Pixels(Read(Document(
            "/Width 5 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 1",
            [0b10101000, 0b01010000])));

        Assert.Equal((255, 255, 255, 255), At(pixels, 0, 0));
        Assert.Equal((0, 0, 0, 255), At(pixels, 1, 0));
        Assert.Equal((255, 255, 255, 255), At(pixels, 4, 0));

        Assert.Equal((0, 0, 0, 255), At(pixels, 0, 1));
        Assert.Equal((255, 255, 255, 255), At(pixels, 1, 1));
    }

    [Fact]
    public void DeviceGray_At_Four_Bits_Scales_To_The_Full_Range()
    {
        // A four-bit sample runs 0..15, and the top of that range has to land on
        // 255 rather than on 15: a picture scaled by the wrong maximum is uniformly
        // too dark and looks like a decode that "worked".
        BPixelBuffer pixels = Pixels(Read(Document(
            "/Width 3 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 4",
            [0x0F, 0x80])));

        Assert.Equal((0, 0, 0, 255), At(pixels, 0, 0));
        Assert.Equal((255, 255, 255, 255), At(pixels, 1, 0));
        Assert.Equal((136, 136, 136, 255), At(pixels, 2, 0));
    }

    [Fact]
    public void DeviceRGB_Keeps_Its_Channel_Order()
    {
        BPixelBuffer pixels = Pixels(Read(Document(
            "/Width 2 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8",
            [10, 20, 30, 40, 50, 60])));

        Assert.Equal((10, 20, 30, 255), At(pixels, 0, 0));
        Assert.Equal((40, 50, 60, 255), At(pixels, 1, 0));
    }

    [Fact]
    public void A_Flate_Compressed_Image_Reaches_The_Document_Too()
    {
        // The filter chain is the ordinary case rather than the exception: almost
        // every raw-sample image in a real document is Flate-compressed, and the
        // pipeline that decodes a content stream decodes this one.
        BPixelBuffer pixels = Pixels(Read(Document(
            "/Width 2 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8",
            Deflate([1, 2, 3, 4, 5, 6]),
            filter: "FlateDecode")));

        Assert.Equal((1, 2, 3, 255), At(pixels, 0, 0));
        Assert.Equal((4, 5, 6, 255), At(pixels, 1, 0));
    }

    // ---- /Decode --------------------------------------------------------------

    [Fact]
    public void A_Decode_Array_That_Runs_Backwards_Inverts_The_Image()
    {
        // The ordinary way a PDF says "inverted". Refusing it outright, as this
        // build did before the subset was implemented, dropped a correct picture
        // for a mapping the format defines in one line.
        BPixelBuffer pixels = Pixels(Read(Document(
            "/Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Decode [1 0]",
            [0x00, 0xFF])));

        Assert.Equal((255, 255, 255, 255), At(pixels, 0, 0));
        Assert.Equal((0, 0, 0, 255), At(pixels, 1, 0));
    }

    [Fact]
    public void The_Default_Decode_Array_Is_Recognized_As_The_Default()
    {
        BPixelBuffer pixels = Pixels(Read(Document(
            "/Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Decode [0 1]",
            [0x00, 0xFF])));

        Assert.Equal((0, 0, 0, 255), At(pixels, 0, 0));
        Assert.Equal((255, 255, 255, 255), At(pixels, 1, 0));
    }

    [Fact]
    public void A_Decode_Array_Of_The_Wrong_Length_Is_Refused()
    {
        Assert.Contains(
            "a Decode array outside the range the format allows",
            Refusal(Read(Document(
                "/Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Decode [0 1]",
                [1, 2, 3]))),
            StringComparison.Ordinal);
    }

    // ---- Indexed --------------------------------------------------------------

    [Fact]
    public void An_Indexed_Image_Is_Looked_Up_In_Its_Palette()
    {
        BPixelBuffer pixels = Pixels(Read(Document(
            "/Width 3 /Height 1 /ColorSpace [/Indexed /DeviceRGB 2 <FF000000FF000000FF>] /BitsPerComponent 8",
            [0, 1, 2])));

        Assert.Equal((255, 0, 0, 255), At(pixels, 0, 0));
        Assert.Equal((0, 255, 0, 255), At(pixels, 1, 0));
        Assert.Equal((0, 0, 255, 255), At(pixels, 2, 0));
    }

    [Fact]
    public void An_Indexed_Palette_Over_Gray_Expands_To_Triples()
    {
        BPixelBuffer pixels = Pixels(Read(Document(
            "/Width 2 /Height 1 /ColorSpace [/Indexed /DeviceGray 1 <0080>] /BitsPerComponent 1",
            [0b01000000])));

        Assert.Equal((0, 0, 0, 255), At(pixels, 0, 0));
        Assert.Equal((128, 128, 128, 255), At(pixels, 1, 0));
    }

    [Fact]
    public void An_Index_Past_The_Palette_Is_Black_Rather_Than_A_Failure()
    {
        // The format leaves an out-of-range index undefined. A document that
        // holds one is malformed, not dangerous, and dropping the whole picture
        // over a single stray byte serves nobody.
        BPixelBuffer pixels = Pixels(Read(Document(
            "/Width 2 /Height 1 /ColorSpace [/Indexed /DeviceRGB 0 <FF0000>] /BitsPerComponent 8",
            [0, 200])));

        Assert.Equal((255, 0, 0, 255), At(pixels, 0, 0));
        Assert.Equal((0, 0, 0, 255), At(pixels, 1, 0));
    }

    [Fact]
    public void An_Indexed_Image_That_Remaps_Its_Own_Indices_Is_Refused()
    {
        // Decode on an Indexed image remaps indices rather than colour values,
        // which is a different operation. Applying half of it would produce a
        // picture in the right shape and the wrong colours.
        Assert.Contains(
            "an Indexed image that remaps its own indices",
            Refusal(Read(Document(
                "/Width 2 /Height 1 /ColorSpace [/Indexed /DeviceRGB 1 <FF000000FF00>] /BitsPerComponent 8 /Decode [1 0]",
                [0, 1]))),
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_Indexed_Palette_Over_An_Unapproved_Base_Is_Refused()
    {
        Assert.Contains(
            "an Indexed palette over a colour space outside the approved subset",
            Refusal(Read(Document(
                "/Width 1 /Height 1 /ColorSpace [/Indexed /DeviceCMYK 0 <00000000>] /BitsPerComponent 8",
                [0]))),
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_Indexed_Palette_Shorter_Than_It_Declares_Is_Refused()
    {
        Assert.Contains(
            "an Indexed palette shorter than it declares",
            Refusal(Read(Document(
                "/Width 1 /Height 1 /ColorSpace [/Indexed /DeviceRGB 3 <FF0000>] /BitsPerComponent 8",
                [0]))),
            StringComparison.Ordinal);
    }

    // ---- transparency, carried as alpha --------------------------------------

    [Fact]
    public void A_Soft_Mask_Becomes_The_Pictures_Alpha()
    {
        // The transparency is the picture's shape, and the model carries it as
        // straight alpha: the colours stay as they were stored, and nothing is
        // blended against a page this model does not keep. The mask is reached
        // through a reference, which is how every real one is written.
        var builder = new PdfFileBuilder();
        int mask = builder.AddStream(
            "/Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8",
            [0x80, 0xFE]);

        BPixelBuffer pixels = Pixels(Read(Document(
            $"/Width 2 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask {mask} 0 R",
            [1, 2, 3, 4, 5, 6],
            extra: builder)));

        Assert.Equal((1, 2, 3, 0x80), At(pixels, 0, 0));
        Assert.Equal((4, 5, 6, 0xFE), At(pixels, 1, 0));
    }

    [Fact]
    public void A_Soft_Mask_Of_Another_Size_Is_Read_Onto_The_Same_Square()
    {
        // A mask is a picture of its own mapped onto the unit square the
        // picture it masks is, so two mask samples across cover four pixels.
        var builder = new PdfFileBuilder();
        int mask = builder.AddStream(
            "/Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8",
            [0x00, 0xFF]);

        BPixelBuffer pixels = Pixels(Read(Document(
            $"/Width 4 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /SMask {mask} 0 R",
            [10, 20, 30, 40],
            extra: builder)));

        Assert.Equal([0, 0, 255, 255], Enumerable.Range(0, 4).Select(x => (int)At(pixels, x, 0).A));
    }

    [Fact]
    public void A_Sixteen_Bit_Soft_Mask_Is_Read_At_Its_Own_Depth()
    {
        var builder = new PdfFileBuilder();
        int mask = builder.AddStream(
            "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 16",
            [0x80, 0x00]);

        BPixelBuffer pixels = Pixels(Read(Document(
            $"/Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask {mask} 0 R",
            [1, 2, 3],
            extra: builder)));

        Assert.Equal(128, At(pixels, 0, 0).A);
    }

    [Fact]
    public void A_Matte_Is_Undone_Rather_Than_Left_In_The_Colours()
    {
        // `/Matte` says the colours were blended with white by the alpha before
        // they were stored: 0.2, 0.4 and 0.6 at half alpha were stored as 153,
        // 178 and 204. Left in, every soft edge would carry a white fringe.
        var builder = new PdfFileBuilder();
        int mask = builder.AddStream(
            "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray " +
            "/BitsPerComponent 8 /Matte [1 1 1]",
            [0x80]);

        BPixelBuffer pixels = Pixels(Read(Document(
            $"/Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask {mask} 0 R",
            [153, 178, 204],
            extra: builder)));

        Assert.Equal((52, 102, 153, 128), At(pixels, 0, 0));
    }

    [Fact]
    public void A_Colour_Key_Makes_Its_Range_Transparent()
    {
        PdfReadResult result = Read(Document(
            "/Width 2 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Mask [0 0 0 0 0 0]",
            [0, 0, 0, 9, 9, 9]));

        BPixelBuffer pixels = Pixels(result);
        Assert.Equal((0, 0, 0, 0), At(pixels, 0, 0));
        Assert.Equal((9, 9, 9, 255), At(pixels, 1, 0));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ImageDecodedNotProjected);
    }

    [Fact]
    public void An_Indexed_Logo_Keyed_On_Its_Ground_Is_Transparent_There()
    {
        // The shape a producer gives a logo with a transparent ground: a palette
        // whose first entry is the ground - dark green here, which carried opaque
        // would put the logo on a green box - and `/Mask [0 0]` keying it out.
        // The key is on the index, not on the colour it looks up.
        BPixelBuffer pixels = Pixels(Read(Document(
            "/Width 3 /Height 1 /ColorSpace [/Indexed /DeviceRGB 1 <47704CFF9900>] /BitsPerComponent 8 /Mask [0 0]",
            [0, 1, 0])));

        Assert.Equal((0x47, 0x70, 0x4C, 0), At(pixels, 0, 0));
        Assert.Equal((0xFF, 0x99, 0x00, 255), At(pixels, 1, 0));
        Assert.Equal((0x47, 0x70, 0x4C, 0), At(pixels, 2, 0));
    }

    [Fact]
    public void A_Colour_Key_Is_Matched_Against_The_Samples_As_Stored()
    {
        // PDF 32000-1 8.9.6.4 keys the samples before `/Decode`. The one-bit
        // sample 1 is black once this Decode array inverts it, and it is the
        // sample, not the black, that the key names.
        BPixelBuffer pixels = Pixels(Read(Document(
            "/Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 1 /Decode [1 0] /Mask [1 1]",
            [0x80])));

        Assert.Equal((0, 0, 0, 0), At(pixels, 0, 0));
        Assert.Equal((255, 255, 255, 255), At(pixels, 1, 0));
    }

    [Fact]
    public void An_Explicit_Mask_Hides_What_Its_Ones_Cover()
    {
        // An explicit mask is a stencil of its own: a zero paints, a one masks.
        var builder = new PdfFileBuilder();
        int mask = builder.AddStream(
            "/Type /XObject /Subtype /Image /Width 8 /Height 1 /ImageMask true",
            [0x0F]);

        BPixelBuffer pixels = Pixels(Read(Document(
            $"/Width 8 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Mask {mask} 0 R",
            [10, 20, 30, 40, 50, 60, 70, 80],
            extra: builder)));

        Assert.Equal(
            [255, 255, 255, 255, 0, 0, 0, 0],
            Enumerable.Range(0, 8).Select(x => (int)At(pixels, x, 0).A));
        Assert.Equal((10, 10, 10, 255), At(pixels, 0, 0));
    }

    [Fact]
    public void An_Explicit_Mask_Decoded_One_To_Zero_Hides_The_Other_Half()
    {
        var builder = new PdfFileBuilder();
        int mask = builder.AddStream(
            "/Type /XObject /Subtype /Image /Width 8 /Height 1 /ImageMask true /Decode [1 0]",
            [0x0F]);

        BPixelBuffer pixels = Pixels(Read(Document(
            $"/Width 8 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Mask {mask} 0 R",
            [10, 20, 30, 40, 50, 60, 70, 80],
            extra: builder)));

        Assert.Equal(
            [0, 0, 0, 0, 255, 255, 255, 255],
            Enumerable.Range(0, 8).Select(x => (int)At(pixels, x, 0).A));
    }

    [Fact]
    public void A_Stencil_Is_Painted_In_The_Fill_Colour()
    {
        // A stencil has no colours of its own. It paints the fill colour in
        // force when it is drawn through its one-bit shape, and projecting it as
        // black and white would have invented colours the page never used.
        BPixelBuffer pixels = Pixels(Read(Document(
            "/Width 8 /Height 1 /ImageMask true",
            [0x0F],
            prefix: "1 0 0 rg ")));

        Assert.Equal((255, 0, 0, 255), At(pixels, 0, 0));
        Assert.Equal((255, 0, 0, 255), At(pixels, 3, 0));
        Assert.Equal(0, At(pixels, 4, 0).A);
        Assert.Equal(0, At(pixels, 7, 0).A);
    }

    [Fact]
    public void A_Soft_Mask_Outranks_A_Colour_Key()
    {
        // PDF 32000-1 11.6.5.3: where both are given, `/Mask` is ignored. The key
        // here would make everything transparent; the soft mask says opaque.
        var builder = new PdfFileBuilder();
        int mask = builder.AddStream(
            "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8",
            [0xFF]);

        BPixelBuffer pixels = Pixels(Read(Document(
            $"/Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask {mask} 0 R /Mask [0 255 0 255 0 255]",
            [1, 2, 3],
            extra: builder)));

        Assert.Equal((1, 2, 3, 255), At(pixels, 0, 0));
    }

    [Fact]
    public void A_Null_Soft_Mask_Is_The_Key_Being_Absent()
    {
        // PDF 32000-1 7.3.9: a key whose value is the null object is equivalent
        // to the key not being there. The test was a presence check on the raw
        // entry, so the null object read as "there is a mask" and refused an
        // image that carries no transparency at all.
        PdfReadResult result = Read(Document(
            "/Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask null",
            [1, 2, 3]));

        Assert.DoesNotContain(
            result.Diagnostics,
            d => d.Code == PdfDiagnosticCodes.ImageDecodedNotProjected);
        Assert.Equal((1, 2, 3, 255), At(Pixels(result), 0, 0));
    }

    [Fact]
    public void A_Null_Colour_Key_Mask_Is_The_Key_Being_Absent()
    {
        PdfReadResult result = Read(Document(
            "/Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Mask null",
            [1, 2, 3]));

        Assert.DoesNotContain(
            result.Diagnostics,
            d => d.Code == PdfDiagnosticCodes.ImageDecodedNotProjected);
        Assert.Equal((1, 2, 3, 255), At(Pixels(result), 0, 0));
    }

    [Fact]
    public void A_Soft_Mask_Reference_To_A_Free_Object_Is_Not_A_Soft_Mask()
    {
        // The other way the raw entry lied: a reference is always non-null as an
        // entry, whatever it resolves to. This one resolves to nothing, so the
        // document declares a mask it does not carry.
        PdfReadResult result = Read(Document(
            "/Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask 9999 0 R",
            [1, 2, 3]));

        Assert.DoesNotContain(
            result.Diagnostics,
            d => d.Code == PdfDiagnosticCodes.ImageDecodedNotProjected);
        Assert.Single(ImagesIn(result));
    }

    // ---- soft masks that mask nothing -----------------------------------------

    [Fact]
    public void A_Soft_Mask_That_Is_Opaque_Everywhere_Leaves_Its_Image_Opaque()
    {
        // Producers attach a soft mask whether the picture needs one or not, and
        // one whose own image type always carries an alpha channel writes it
        // solid opaque edge to edge. Refusing on the key's presence used to
        // discard such a logo for a mask that says nothing.
        var builder = new PdfFileBuilder();
        int mask = builder.AddStream(
            "/Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8",
            [0xFF, 0xFF]);

        PdfReadResult result = Read(Document(
            $"/Width 2 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask {mask} 0 R",
            [1, 2, 3, 4, 5, 6],
            extra: builder));

        Assert.DoesNotContain(
            result.Diagnostics,
            d => d.Code == PdfDiagnosticCodes.ImageDecodedNotProjected);
        Assert.Equal((4, 5, 6, 255), At(Pixels(result), 1, 0));
    }

    [Fact]
    public void An_Inverted_Soft_Mask_Of_Zeroes_Is_Opaque()
    {
        // How the opaque masks in the wild are actually written: every sample
        // zero, and `/Decode [1 0]` turning every one of them into full alpha.
        // Reading the samples without the Decode array would call this
        // completely transparent.
        var builder = new PdfFileBuilder();
        int mask = builder.AddStream(
            "/Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace /DeviceGray " +
            "/BitsPerComponent 8 /Decode [1 0]",
            [0x00, 0x00]);

        PdfReadResult result = Read(Document(
            $"/Width 2 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask {mask} 0 R",
            [1, 2, 3, 4, 5, 6],
            extra: builder));

        Assert.Equal((1, 2, 3, 255), At(Pixels(result), 0, 0));
    }

    [Fact]
    public void A_Flat_Soft_Mask_Is_Not_Refused_As_A_Decompression_Bomb()
    {
        // A uniform mask compresses to almost nothing, and the expansion ratio
        // that guards against a bomb refused it on the ratio alone. An image
        // states its own decoded size, so the guess gives way to the declaration.
        const int Width = 512;
        const int Height = 512;

        var builder = new PdfFileBuilder();
        int mask = builder.AddStream(
            $"/Type /XObject /Subtype /Image /Width {Width} /Height {Height} " +
            "/ColorSpace /DeviceGray /BitsPerComponent 8 /Decode [1 0]",
            Deflate(new byte[Width * Height]),
            filter: "FlateDecode");

        PdfReadResult result = Read(Document(
            $"/Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask {mask} 0 R",
            [1, 2, 3],
            extra: builder));

        Assert.Single(ImagesIn(result));
    }

    [Fact]
    public void An_Opaque_Soft_Mask_Leaves_The_Pixels_Alone()
    {
        // Reading a mask must not turn into compositing: the colour plane
        // arrives exactly as declared.
        var builder = new PdfFileBuilder();
        int mask = builder.AddStream(
            "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8",
            [0xFF]);

        BPixelBuffer pixels = Pixels(Read(Document(
            $"/Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask {mask} 0 R",
            [10, 20, 30],
            extra: builder)));

        Assert.Equal((10, 20, 30, 255), At(pixels, 0, 0));
    }

    // ---- masks that stay refused ----------------------------------------------

    [Fact]
    public void A_Stencil_Painted_With_A_Pattern_Is_Refused()
    {
        // A pattern is not a colour this interpreter reads, so a stencil painted
        // with one has no colour it could honestly be carried in.
        Assert.Contains(
            "a stencil mask painted with a pattern",
            Refusal(Read(Document(
                "/Width 8 /Height 1 /ImageMask true",
                [0x0F],
                prefix: "/Pattern cs /P0 scn "))),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_Soft_Mask_Outside_DeviceGray_Is_Malformed()
    {
        // PDF 32000-1 11.6.5.3 requires DeviceGray. A mask that names anything
        // else is not a mask this can read, and an unread mask refuses.
        var builder = new PdfFileBuilder();
        int mask = builder.AddStream(
            "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8",
            [0xFF, 0xFF, 0xFF]);

        Assert.Contains(
            "a soft mask outside DeviceGray",
            Refusal(Read(Document(
                $"/Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask {mask} 0 R",
                [1, 2, 3],
                extra: builder))),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_Matte_Over_An_Indexed_Picture_Is_Refused()
    {
        // A matte is a colour in the picture's own components, and an index is
        // not one: undoing it on the palette's colours would be a guess.
        var builder = new PdfFileBuilder();
        int mask = builder.AddStream(
            "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Matte [0]",
            [0x80]);

        Assert.Contains(
            "a premultiplied soft mask over colours this build cannot undo it on",
            Refusal(Read(Document(
                $"/Width 1 /Height 1 /ColorSpace [/Indexed /DeviceRGB 0 <102030>] /BitsPerComponent 8 /SMask {mask} 0 R",
                [0],
                extra: builder))),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_Mask_Whose_Filter_Is_Not_Composed_Refuses_Its_Picture()
    {
        // The base build composes no JPEG decoder, so a mask compressed as one is
        // a transparency it cannot read - and carrying the picture opaque would
        // be the solid box masks exist to prevent.
        var builder = new PdfFileBuilder();
        int mask = builder.AddStream(
            "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8",
            [0xFF, 0xD8, 0xFF, 0xD9],
            filter: "DCTDecode");

        Assert.Contains(
            "a mask whose filter is not composed",
            Refusal(Read(Document(
                $"/Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask {mask} 0 R",
                [1, 2, 3],
                extra: builder))),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_Colour_Key_Of_The_Wrong_Length_Refuses_Its_Picture()
    {
        Assert.Contains(
            "a colour-key mask this build cannot read",
            Refusal(Read(Document(
                "/Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Mask [0 0]",
                [1, 2, 3]))),
            StringComparison.Ordinal);
    }

    // ---- what stays refused ---------------------------------------------------

    [Fact]
    public void A_Colour_Space_Outside_The_Subset_Is_Refused_By_Name()
    {
        Assert.Contains(
            "the colour space DeviceCMYK",
            Refusal(Read(Document(
                "/Width 1 /Height 1 /ColorSpace /DeviceCMYK /BitsPerComponent 8",
                [1, 2, 3, 4]))),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_Bit_Depth_Outside_The_Subset_Is_Refused()
    {
        Assert.Contains(
            "a bit depth outside the approved subset",
            Refusal(Read(Document(
                "/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 16",
                [0x12, 0x34]))),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceRGB_At_A_Depth_Other_Than_Eight_Bits_Is_Refused()
    {
        Assert.Contains(
            "DeviceRGB at a depth other than eight bits",
            Refusal(Read(Document(
                "/Width 2 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 4",
                [0x12, 0x34, 0x56]))),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Samples_That_Do_Not_Fill_The_Declaration_Are_Refused()
    {
        Assert.Contains(
            "a sample count its declaration does not account for",
            Refusal(Read(Document(
                "/Width 8 /Height 8 /ColorSpace /DeviceGray /BitsPerComponent 8",
                new byte[32]))),
            StringComparison.Ordinal);
    }

    // ---- a picture under the whole page ---------------------------------------

    [Fact]
    public void A_Page_Sized_Picture_Beneath_The_Text_Is_The_Pages_Background()
    {
        // Stationery, or a sheet of fold marks: the text is drawn over it. In the
        // flow it was a page-sized paragraph ahead of every word, and counted as
        // the body's ink it took the margins to nothing.
        PdfReadResult result = Read(Document(
            "/Width 2 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 8",
            [255, 255, 255, 0],
            prefix: "612 0 0 792 0 0 cm "));

        Assert.Empty(ImagesIn(result));
        Assert.Equal("Body", Assert.Single(result.Document.Paragraphs).Text);
        Assert.Contains(
            "a page-sized picture beneath the text, read as the page's background",
            Assert.Single(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ImageDecodedNotProjected).Message,
            StringComparison.Ordinal);
        Assert.Equal(72, result.Document.PageGeometry!.MarginLeft, 0);
    }

    [Fact]
    public void A_Page_That_Is_Only_A_Picture_Keeps_It()
    {
        // A scan with nothing drawn over it: the picture is the page.
        PdfReadResult result = Read(Document(
            "/Width 2 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 8",
            [255, 255, 255, 0],
            content: "q 612 0 0 792 0 0 cm /Im0 Do Q\n"));

        Assert.Single(ImagesIn(result));
    }

    [Fact]
    public void A_Scan_Under_Invisible_Text_Keeps_Its_Picture()
    {
        // Recognized text is drawn invisibly over the scan it was read from.
        // The scan is still everything a reader sees.
        PdfReadResult result = Read(Document(
            "/Width 2 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 8",
            [255, 255, 255, 0],
            content: "q 612 0 0 792 0 0 cm /Im0 Do Q\nBT /F1 12 Tf 3 Tr 1 0 0 1 72 720 Tm (Recognized) Tj ET\n"));

        Assert.Single(ImagesIn(result));
    }

    [Fact]
    public void A_Picture_Painted_Over_The_Text_Is_Not_Its_Background()
    {
        PdfReadResult result = Read(Document(
            "/Width 2 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 8",
            [255, 255, 255, 0],
            content: PdfFileBuilder.ShowText("Body") + "q 612 0 0 792 0 0 cm /Im0 Do Q\n"));

        Assert.Single(ImagesIn(result));
    }

    [Fact]
    public void A_Picture_Covering_Part_Of_The_Page_Stays_In_The_Flow()
    {
        // Half the page beneath the text is a picture on the page, not the page.
        PdfReadResult result = Read(Document(
            "/Width 2 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 8",
            [255, 255, 255, 0],
            prefix: "612 0 0 396 0 0 cm "));

        Assert.Single(ImagesIn(result));
    }

    // ---- colour spaces named through the resource dictionary ------------------

    [Fact]
    public void A_Resource_Label_Is_Followed_To_The_Space_It_Stands_For()
    {
        // Naming a space in the resource dictionary is ordinary, and refusing
        // every image that does it would have left most real documents' pictures
        // behind for a reason that is purely a lookup.
        BPixelBuffer pixels = Pixels(Read(Document(
            "/Width 1 /Height 1 /ColorSpace /CS0 /BitsPerComponent 8",
            [10, 20, 30],
            colorSpaces: "/CS0 /DeviceRGB")));

        Assert.Equal((10, 20, 30, 255), At(pixels, 0, 0));
    }

    [Fact]
    public void A_Label_Bound_To_An_Unapproved_Space_Is_Refused_By_The_Family_It_Names()
    {
        string refusal = Refusal(Read(Document(
            "/Width 1 /Height 1 /ColorSpace /CS0 /BitsPerComponent 8",
            [10, 20, 30],
            colorSpaces: "/CS0 [/Lab << >>]")));

        Assert.Contains("the colour space Lab", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Label_That_Resolves_To_Nothing_Is_Refused_Generically()
    {
        // The refusal reason says the subset was missed and stops there, because
        // a name the document's author chose is not a construct this build
        // recognized and repeating it in a reason states nothing true about the
        // format. The image inventory in the same message still reports what the
        // dictionary declared — that is its long-standing job, and the reason and
        // the inventory answer different questions.
        string refusal = Refusal(Read(Document(
            "/Width 1 /Height 1 /ColorSpace /PrivateSpaceName /BitsPerComponent 8",
            [10, 20, 30])));

        Assert.Contains("a colour space outside the approved subset", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("the colour space PrivateSpaceName", refusal, StringComparison.Ordinal);
    }

    // ---- fixtures -------------------------------------------------------------

    private static PdfReadResult Read(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, null);
    }

    /// <summary>The single image the document carried, as pixels.</summary>
    private static BPixelBuffer Pixels(PdfReadResult result)
    {
        InlineImage image = Assert.Single(ImagesIn(result));
        Assert.True(image.Resource.TryGetPixels(out BPixelBuffer? pixels));
        return pixels!;
    }

    /// <summary>
    /// The message of the one not-projected diagnostic, having first established
    /// that nothing reached the document: a refusal that still projected
    /// something would pass a test that only read the message.
    /// </summary>
    private static string Refusal(PdfReadResult result)
    {
        Assert.Empty(ImagesIn(result));
        return Assert.Single(
            result.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.ImageDecodedNotProjected)).Message;
    }

    private static List<InlineImage> ImagesIn(PdfReadResult result)
    {
        var images = new List<InlineImage>();
        foreach (RichTextParagraph paragraph in result.Document.Paragraphs)
        {
            foreach (StyleRun run in paragraph.Runs)
            {
                if (run.Style.Image is InlineImage image)
                    images.Add(image);
            }
        }

        return images;
    }

    private static (byte R, byte G, byte B, byte A) At(BPixelBuffer pixels, int x, int y)
    {
        int at = ((y * pixels.Width) + x) * BPixelBuffer.BytesPerPixel;
        return (pixels.Rgba[at], pixels.Rgba[at + 1], pixels.Rgba[at + 2], pixels.Rgba[at + 3]);
    }

    /// <summary>
    /// One page drawing a single image XObject, plus a line of text so the page
    /// is not mistaken for a scan needing OCR.
    /// </summary>
    /// <param name="extra">
    /// A builder already carrying objects the image dictionary refers to, so a
    /// fixture can point at a soft mask by object number.
    /// </param>
    /// <param name="prefix">
    /// Content run just before the image is drawn - a fill colour for a
    /// stencil to be painted in.
    /// </param>
    /// <param name="content">
    /// The whole content stream, for a fixture that needs its own order of
    /// picture and text; otherwise the picture is drawn, then one line of text.
    /// </param>
    private static byte[] Document(
        string dictionaryBody,
        byte[] data,
        string? filter = null,
        string? colorSpaces = null,
        PdfFileBuilder? extra = null,
        string prefix = "",
        string? content = null)
    {
        PdfFileBuilder builder = extra ?? new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int image = builder.AddStream($"/Type /XObject /Subtype /Image {dictionaryBody}", data, filter);
        int stream = builder.AddStream(string.Empty, content ?? "q " + prefix + "/Im0 Do Q\n" + PdfFileBuilder.ShowText("Body"));

        string spaces = colorSpaces is null ? string.Empty : $" /ColorSpace << {colorSpaces} >>";

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F1 {font} 0 R >> /XObject << /Im0 {image} 0 R >>{spaces} >> " +
            $"/Contents {stream} 0 R >>");

        return builder.Build(catalog);
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var compressor = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            compressor.Write(data, 0, data.Length);
        return output.ToArray();
    }
}
