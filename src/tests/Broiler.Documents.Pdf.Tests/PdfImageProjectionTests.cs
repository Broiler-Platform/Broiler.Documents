using System.IO.Compression;
using Broiler.Graphics;

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

    // ---- what stays refused ---------------------------------------------------

    [Fact]
    public void An_Image_Carrying_A_Soft_Mask_Is_Refused_Rather_Than_Carried_Opaque()
    {
        // The transparency is the picture's shape. Projecting the colour plane on
        // its own puts a solid rectangle where a logo's transparent ground
        // belongs, which is exactly the plausible wrong picture this subset
        // refuses to produce.
        var builder = new PdfFileBuilder();
        int mask = builder.AddStream(
            "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8",
            new byte[] { 0x80 });

        Assert.Contains(
            "transparency this build does not composite",
            Refusal(Read(Document(
                $"/Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask {mask} 0 R",
                [1, 2, 3],
                extra: builder))),
            StringComparison.Ordinal);
    }

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
    private static byte[] Document(
        string dictionaryBody,
        byte[] data,
        string? filter = null,
        string? colorSpaces = null,
        PdfFileBuilder? extra = null)
    {
        PdfFileBuilder builder = extra ?? new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int image = builder.AddStream($"/Type /XObject /Subtype /Image {dictionaryBody}", data, filter);
        int content = builder.AddStream(string.Empty, "q /Im0 Do Q\n" + PdfFileBuilder.ShowText("Body"));

        string spaces = colorSpaces is null ? string.Empty : $" /ColorSpace << {colorSpaces} >>";

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F1 {font} 0 R >> /XObject << /Im0 {image} 0 R >>{spaces} >> " +
            $"/Contents {content} 0 R >>");

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
