using System.Text;
using Broiler.Graphics.Imaging;

namespace Broiler.Documents.Pdf.Images.Tests;

/// <summary>
/// Covers ICC-based colour through the codec: what a read carries when a
/// caller composes <see cref="IccColorProfileReader"/>, and what it refuses by
/// name when nobody does.
/// </summary>
/// <remarks>
/// Both halves, as PDF extension points §5.6 asks. The not-composed refusal is
/// part of the contract too: a build that composes no colour-profile reader
/// must keep saying which construct it met rather than start drawing ICC
/// colour in values its profile never stated.
/// </remarks>
public sealed class PdfIccImageTests
{
    [Fact]
    public void An_ICC_Based_Picture_Is_Converted_Through_Its_Profile()
    {
        BPixelBuffer pixels = Pixels(Read(
            Document("/Width 2 /Height 1 /ColorSpace [/ICCBased {0} 0 R] /BitsPerComponent 8", [10, 200, 77, 250, 128, 3], IccColorProfileReaderTests.SrgbProfile(), 3),
            composed: true));

        Near((10, 200, 77), At(pixels, 0));
        Near((250, 128, 3), At(pixels, 1));
        Assert.Equal(255, pixels.Rgba[3]);
    }

    [Fact]
    public void Without_A_Reader_The_Picture_Is_Refused_By_Name()
    {
        PdfReadResult result = Read(
            Document("/Width 2 /Height 1 /ColorSpace [/ICCBased {0} 0 R] /BitsPerComponent 8", [10, 200, 77, 250, 128, 3], IccColorProfileReaderTests.SrgbProfile(), 3),
            composed: false);

        Assert.Empty(ImagesIn(result));
        Assert.Contains(
            "the colour space ICCBased, whose profile no composed reader converts",
            Assert.Single(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ImageDecodedNotProjected).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_Indexed_Palette_Over_An_ICC_Space_Is_Converted_Entry_By_Entry()
    {
        BPixelBuffer pixels = Pixels(Read(
            Document(
                "/Width 2 /Height 1 /ColorSpace [/Indexed [/ICCBased {0} 0 R] 1 <0AC84DFA8003>] /BitsPerComponent 8",
                [1, 0],
                IccColorProfileReaderTests.SrgbProfile(),
                3),
            composed: true));

        Near((250, 128, 3), At(pixels, 0));
        Near((10, 200, 77), At(pixels, 1));
    }

    [Fact]
    public void A_CMYK_Picture_Is_Converted_Through_Its_Press_Profile()
    {
        // DeviceCMYK alone stays outside the subset - its colours need a
        // profile to mean anything - and with one it converts.
        BPixelBuffer pixels = Pixels(Read(
            Document("/Width 2 /Height 1 /ColorSpace [/ICCBased {0} 0 R] /BitsPerComponent 8", [0, 0, 0, 0, 0, 0, 0, 255], IccColorProfileReaderTests.PressProfile(), 4),
            composed: true));

        Near((255, 255, 255), At(pixels, 0));
        Near((0, 0, 0), At(pixels, 1));
    }

    [Fact]
    public void A_Gray_Picture_Is_Converted_At_Its_Own_Depth()
    {
        // One component may be packed below a byte, as DeviceGray may: the bits
        // 1 and 0 are full light and none.
        byte[] profile = new IccProfileBuilder("GRAY", "XYZ ", "mntr")
            .With("kTRC", IccProfileBuilder.Gamma(1.0))
            .Build();

        BPixelBuffer pixels = Pixels(Read(
            Document("/Width 2 /Height 1 /ColorSpace [/ICCBased {0} 0 R] /BitsPerComponent 1", [0x80], profile, 1),
            composed: true));

        Near((255, 255, 255), At(pixels, 0));
        Near((0, 0, 0), At(pixels, 1));
    }

    [Fact]
    public void A_Profile_The_Reader_Declines_Refuses_Its_Pictures_And_Says_Why()
    {
        PdfReadResult result = Read(
            Document("/Width 1 /Height 1 /ColorSpace [/ICCBased {0} 0 R] /BitsPerComponent 8", [1, 2, 3], new IccProfileBuilder(deviceClass: "link").Build(), 3),
            composed: true);

        Assert.Empty(ImagesIn(result));
        Assert.Contains(
            "an ICC profile the composed reader declined (a device-link, abstract, or named-colour profile)",
            Assert.Single(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ImageDecodedNotProjected).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_Range_Other_Than_The_Default_Is_Refused_Rather_Than_Ignored()
    {
        PdfReadResult result = Read(
            Document(
                "/Width 1 /Height 1 /ColorSpace [/ICCBased {0} 0 R] /BitsPerComponent 8",
                [1, 2, 3],
                IccColorProfileReaderTests.SrgbProfile(),
                3,
                profileEntries: "/Range [0 2 0 1 0 1]"),
            composed: true);

        Assert.Contains(
            "an ICC-based colour space with a Range other than the default",
            Assert.Single(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ImageDecodedNotProjected).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_Images_Own_Intent_Chooses_The_Profiles_Table()
    {
        BPixelBuffer perceptual = Pixels(Read(
            Document("/Width 1 /Height 1 /ColorSpace [/ICCBased {0} 0 R] /BitsPerComponent 8 /Intent /Perceptual", [128], IntentProfile(), 1),
            composed: true));
        BPixelBuffer relative = Pixels(Read(
            Document("/Width 1 /Height 1 /ColorSpace [/ICCBased {0} 0 R] /BitsPerComponent 8", [128], IntentProfile(), 1),
            composed: true));

        Near((255, 255, 255), At(perceptual, 0));
        Near((0, 0, 0), At(relative, 0));
    }

    [Fact]
    public void The_ri_Operator_Sets_The_Intent_For_What_Is_Drawn_After_It()
    {
        BPixelBuffer pixels = Pixels(Read(
            Document("/Width 1 /Height 1 /ColorSpace [/ICCBased {0} 0 R] /BitsPerComponent 8", [128], IntentProfile(), 1, prefix: "/Perceptual ri "),
            composed: true));

        Near((255, 255, 255), At(pixels, 0));
    }

    // ---- fixtures --------------------------------------------------------------

    /// <summary>A gray profile whose perceptual table paints white and relative colorimetric one black.</summary>
    private static byte[] IntentProfile() =>
        new IccProfileBuilder("GRAY", "Lab ", "scnr")
            .With("A2B0", IccProfileBuilder.Lut8(1, 2, _ => [1, 128 / 255d, 128 / 255d]))
            .With("A2B1", IccProfileBuilder.Lut8(1, 2, _ => [0, 128 / 255d, 128 / 255d]))
            .Build();

    private static PdfReadResult Read(byte[] pdf, bool composed)
    {
        PdfCodecServices services = composed
            ? PdfCodecServices.Base.WithColorProfileReader(new IccColorProfileReader())
            : PdfCodecServices.Base;

        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec(services).ReadPdf(stream, null);
    }

    private static BPixelBuffer Pixels(PdfReadResult result)
    {
        InlineImage image = Assert.Single(ImagesIn(result));
        Assert.True(image.Resource.TryGetPixels(out BPixelBuffer? pixels));
        return pixels!;
    }

    private static List<InlineImage> ImagesIn(PdfReadResult result) =>
        [.. result.Document.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Style.Image)
            .OfType<InlineImage>()];

    private static (int R, int G, int B) At(BPixelBuffer pixels, int x)
    {
        int at = x * BPixelBuffer.BytesPerPixel;
        return (pixels.Rgba[at], pixels.Rgba[at + 1], pixels.Rgba[at + 2]);
    }

    private static void Near((int R, int G, int B) expected, (int R, int G, int B) actual) =>
        Assert.True(
            Math.Abs(expected.R - actual.R) <= 1 && Math.Abs(expected.G - actual.G) <= 1 && Math.Abs(expected.B - actual.B) <= 1,
            $"Expected {expected} within one step, got {actual}.");

    /// <summary>
    /// One page of text and one image whose colour space names an ICC profile.
    /// <c>{0}</c> in the image dictionary is replaced by the profile's object
    /// number.
    /// </summary>
    private static byte[] Document(
        string imageDictionary,
        byte[] samples,
        byte[] profile,
        int components,
        string profileEntries = "",
        string prefix = "")
    {
        var objects = new List<byte[]>();
        int profileObject = Add(objects, Stream($"/N {components} {profileEntries}", profile));
        int image = Add(objects, Stream("/Type /XObject /Subtype /Image " + imageDictionary.Replace("{0}", profileObject.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal), samples));
        int font = Add(objects, Latin1("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        int content = Add(objects, Stream(
            string.Empty,
            Latin1("BT /F1 12 Tf 1 0 0 1 72 720 Tm (Body text) Tj ET\nq " + prefix + "100 0 0 100 72 500 cm /Im0 Do Q\n")));

        int pages = Add(objects, []);
        int page = Add(objects, Latin1(
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F1 {font} 0 R >> /XObject << /Im0 {image} 0 R >> >> /Contents {content} 0 R >>"));
        int catalog = Add(objects, []);

        objects[pages - 1] = Latin1($"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        objects[catalog - 1] = Latin1($"<< /Type /Catalog /Pages {pages} 0 R >>");

        var output = new List<byte>(Latin1("%PDF-1.7\n"));
        var offsets = new List<int>();
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Count);
            output.AddRange(Latin1($"{i + 1} 0 obj\n"));
            output.AddRange(objects[i]);
            output.AddRange(Latin1("\nendobj\n"));
        }

        int xref = output.Count;
        var table = new StringBuilder($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
            table.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");

        table.Append($"trailer\n<< /Size {objects.Count + 1} /Root {catalog} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        output.AddRange(Latin1(table.ToString()));
        return [.. output];
    }

    private static int Add(List<byte[]> objects, byte[] body)
    {
        objects.Add(body);
        return objects.Count;
    }

    private static byte[] Stream(string dictionary, byte[] data)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Latin1($"<< {dictionary} /Length {data.Length} >>\nstream\n"));
        bytes.AddRange(data);
        bytes.AddRange(Latin1("\nendstream"));
        return [.. bytes];
    }

    private static byte[] Latin1(string text) => [.. text.Select(c => (byte)c)];
}
