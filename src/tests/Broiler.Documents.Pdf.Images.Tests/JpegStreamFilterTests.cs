using System.Text;
using Broiler.Media.Image;
using Broiler.Media.Image.Managed;

namespace Broiler.Documents.Pdf.Images.Tests;

/// <summary>
/// Covers the DCTDecode filter cleared under IP-005: the tuples it decodes, the
/// tuples it refuses by name, and the budget it honours before a decoder ever
/// sees the data.
/// </summary>
/// <remarks>
/// Every JPEG here is produced in the test, either by the managed encoder or by
/// assembling marker segments byte by byte. Nothing is committed, so no fixture
/// carries anyone else's image, and each refusal test states the exact frame it
/// is about instead of hiding it in a file.
/// </remarks>
public sealed class JpegStreamFilterTests
{
    private static readonly PdfFilterContext Generous = new(64L * 1024 * 1024, 4096);

    // ---- the cleared tuples ---------------------------------------------------

    [Fact]
    public void A_Baseline_Huffman_Jpeg_Decodes_To_Its_Declared_Pixel_Count()
    {
        byte[] jpeg = Jpeg(32, 32);

        PdfFilterResult result = new JpegStreamFilter().Decode(jpeg, PdfFilterParameters.Empty, Generous);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(32 * 32 * 4, result.Data!.Length);
    }

    [Fact]
    public void A_Progressive_Jpeg_Decodes_To_Its_Declared_Pixel_Count()
    {
        // The case widening IP-005 unlocked on 2026-09-02. Nothing about decoding
        // changed: Broiler.Media has decoded SOF2 all along, and this filter was
        // refusing it because the approval on record named baseline. So the proof
        // that matters is that real progressive entropy data now reaches the
        // decoder and comes back as samples.
        PdfFilterResult result = Decode(ProgressiveJpeg());

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(8 * 8 * 4, result.Data!.Length);

        // The file codes one DC-only scan with a non-zero difference, so every
        // pixel is the same grey and it is not the 128 a block of zero
        // coefficients would produce. That distinguishes a decode from a decoder
        // that read the headers and handed back an empty plane.
        byte grey = result.Data[0];
        Assert.True(grey > 128, $"Expected the coded DC value to lift the plane above mid-grey; got {grey}.");
        for (int pixel = 0; pixel < 8 * 8; pixel++)
        {
            Assert.Equal(grey, result.Data[pixel * 4]);
            Assert.Equal(grey, result.Data[(pixel * 4) + 1]);
            Assert.Equal(grey, result.Data[(pixel * 4) + 2]);
        }
    }

    [Fact]
    public void The_Filter_Names_Itself_As_An_Image_Filter()
    {
        var filter = new JpegStreamFilter();

        Assert.Equal("DCTDecode", filter.Name);
        Assert.Equal("DCT", filter.Abbreviation);

        // The property that keeps pixels out of the object layer.
        Assert.False(filter.ProducesByteStream);
    }

    // ---- the tuples IP-005 does not cover -------------------------------------

    [Theory]
    [InlineData((byte)0xC9, "arithmetic")]
    [InlineData((byte)0xCA, "arithmetic")]
    [InlineData((byte)0xCB, "arithmetic")]
    public void An_Arithmetic_Coded_Frame_Is_Refused_By_Name(byte marker, string expected)
    {
        PdfFilterResult result = Decode(Frame(marker, precision: 8, width: 16, height: 16, components: 3));

        Assert.False(result.Succeeded);
        Assert.Equal(PdfDiagnosticCodes.FilterDctUnsupported, result.DiagnosticCode);
        Assert.Contains(expected, result.Message, StringComparison.Ordinal);
        Assert.Contains("IP-005", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData((byte)0xC1)]
    [InlineData((byte)0xC3)]
    [InlineData((byte)0xC5)]
    [InlineData((byte)0xC6)]
    [InlineData((byte)0xC7)]
    public void A_Frame_Process_Outside_The_Cleared_Pair_Is_Refused(byte marker)
    {
        // 0xC6 is differential *progressive*, and it stays out. Widening the row
        // to progressive widened one axis — the spectral order of a Huffman
        // process — and not the hierarchical and differential families beside it.
        PdfFilterResult result = Decode(Frame(marker, precision: 8, width: 16, height: 16, components: 3));

        Assert.Equal(PdfDiagnosticCodes.FilterDctUnsupported, result.DiagnosticCode);
    }

    [Fact]
    public void A_Progressive_Frame_Still_Faces_Every_Other_Gate()
    {
        // Proof that the widening moved exactly one axis. Precision and component
        // count are separate parts of the cleared tuple, and a progressive frame
        // meets them on the same terms a baseline one does.
        Assert.Equal(
            PdfDiagnosticCodes.FilterDctUnsupported,
            Decode(Frame(0xC2, precision: 12, width: 16, height: 16, components: 3)).DiagnosticCode);

        Assert.Equal(
            PdfDiagnosticCodes.FilterDctUnsupported,
            Decode(Frame(0xC2, precision: 8, width: 16, height: 16, components: 4)).DiagnosticCode);

        Assert.Equal(
            PdfDiagnosticCodes.FilterDctColorTransformUncertain,
            Decode(Frame(0xC2, precision: 8, width: 16, height: 16, components: 3, adobeTransform: 7)).DiagnosticCode);
    }

    [Fact]
    public void Twelve_Bit_Precision_Is_Refused()
    {
        PdfFilterResult result = Decode(Frame(0xC0, precision: 12, width: 16, height: 16, components: 3));

        Assert.Equal(PdfDiagnosticCodes.FilterDctUnsupported, result.DiagnosticCode);
        Assert.Contains("12-bit", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Four_Component_Frame_Is_Refused_By_Scope_Rather_Than_By_Clearance()
    {
        PdfFilterResult result = Decode(Frame(0xC0, precision: 8, width: 16, height: 16, components: 4));

        Assert.Equal(PdfDiagnosticCodes.FilterDctUnsupported, result.DiagnosticCode);
        Assert.Contains("CMYK", result.Message, StringComparison.Ordinal);
        Assert.Contains("outside this release's scope", result.Message, StringComparison.Ordinal);
    }

    // ---- the colour declarations IP-006 cleared -------------------------------

    [Fact]
    public void An_Adobe_Marker_Declaring_YCbCr_Is_Decoded()
    {
        // The case IP-006 unlocked. Before it cleared, this exact image was
        // refused for carrying a marker the filter was not allowed to read.
        byte[] jpeg = WithAdobeMarker(Jpeg(32, 32), transform: 1);

        PdfFilterResult result = Decode(jpeg);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(32 * 32 * 4, result.Data!.Length);
    }

    [Fact]
    public void An_Adobe_Marker_Declaring_No_Transform_Is_Honoured()
    {
        // Transform 0 says the samples are already RGB. This used to be refused,
        // not because IP-006 withheld it but because the composed decoder always
        // applied the YCbCr conversion. It can now be told not to, so the
        // declaration is honoured rather than reported.
        byte[] jpeg = WithAdobeMarker(Jpeg(32, 32), transform: 0);

        PdfFilterResult result = Decode(jpeg);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(32 * 32 * 4, result.Data!.Length);
    }

    [Fact]
    public void The_Two_Readings_Of_One_Image_Differ()
    {
        // The declaration has to change the pixels, or honouring it is a claim
        // rather than a behaviour. The same encoded bytes are read both ways.
        byte[] samples = Jpeg(32, 32);

        PdfFilterResult asYCbCr = Decode(WithAdobeMarker(samples, transform: 1));
        PdfFilterResult asRgb = Decode(WithAdobeMarker(samples, transform: 0));

        Assert.True(asYCbCr.Succeeded, asYCbCr.Message);
        Assert.True(asRgb.Succeeded, asRgb.Message);
        Assert.NotEqual(asYCbCr.Data!, asRgb.Data!);
    }

    [Fact]
    public void A_Ycck_Transform_On_Three_Components_Is_Refused()
    {
        PdfFilterResult result = Decode(
            Frame(0xC0, precision: 8, width: 16, height: 16, components: 3, adobeTransform: 2));

        Assert.Equal(PdfDiagnosticCodes.FilterDctUnsupported, result.DiagnosticCode);
        Assert.Contains("YCCK", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Transform_Value_The_Format_Does_Not_Define_Is_Uncertain()
    {
        PdfFilterResult result = Decode(
            Frame(0xC0, precision: 8, width: 16, height: 16, components: 3, adobeTransform: 7));

        Assert.Equal(PdfDiagnosticCodes.FilterDctColorTransformUncertain, result.DiagnosticCode);
        Assert.Contains("not one of the values the format defines", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Greyscale_Passes_The_Colour_Gate_Whatever_It_Declares()
    {
        // One plane, nothing to transform. The frame here carries no scan data, so
        // reaching the decoder is the proof: a malformed stream means the colour
        // gate let it through, where a tuple refusal would mean it did not.
        PdfFilterResult result = Decode(
            Frame(0xC0, precision: 8, width: 16, height: 16, components: 1, adobeTransform: 1));

        Assert.Equal(PdfDiagnosticCodes.FilterMalformed, result.DiagnosticCode);
    }

    // ---- budgets and malformed input ------------------------------------------

    [Fact]
    public void An_Oversized_Frame_Is_Refused_From_Its_Header_Rather_Than_Decoded()
    {
        // 20000x20000 RGBA is 1.6 GB. The header alone has to stop it: this blob
        // carries no scan data at all, so reaching the decoder would report a
        // malformed stream instead — which is how the test tells the two apart.
        PdfFilterResult result = new JpegStreamFilter().Decode(
            Frame(0xC0, precision: 8, width: 20000, height: 20000, components: 3),
            PdfFilterParameters.Empty,
            new PdfFilterContext(1_000_000, 512));

        Assert.Equal(PdfDiagnosticCodes.FilterLimit, result.DiagnosticCode);
        Assert.Contains("past this stage's ceiling", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Stream_That_Is_Not_A_Jpeg_Is_Malformed_Rather_Than_Unsupported()
    {
        PdfFilterResult result = Decode("this is not a JPEG"u8.ToArray());

        Assert.Equal(PdfDiagnosticCodes.FilterMalformed, result.DiagnosticCode);
        Assert.Contains("SOI", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Truncated_Jpeg_Fails_Without_Throwing()
    {
        byte[] jpeg = Jpeg(32, 32);

        // Every prefix of a valid file must reach a decision, never an escape.
        for (int length = 2; length < jpeg.Length; length += 37)
        {
            PdfFilterResult result = Decode(jpeg.AsSpan(0, length).ToArray());
            Assert.False(result.Succeeded);
            Assert.NotNull(result.DiagnosticCode);
        }
    }

    [Fact]
    public void A_Frame_Declaring_No_Pixels_Is_Malformed()
    {
        PdfFilterResult result = Decode(Frame(0xC0, precision: 8, width: 0, height: 16, components: 3));

        Assert.Equal(PdfDiagnosticCodes.FilterMalformed, result.DiagnosticCode);
    }

    // ---- fixtures -------------------------------------------------------------

    private static PdfFilterResult Decode(byte[] data) =>
        new JpegStreamFilter().Decode(data, PdfFilterParameters.Empty, Generous);

    /// <summary>A real baseline JPEG, encoded here from a generated gradient.</summary>
    internal static byte[] Jpeg(int width, int height)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = ((y * width) + x) * 4;
                rgba[offset] = (byte)(x * 255 / Math.Max(1, width - 1));
                rgba[offset + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                rgba[offset + 2] = 128;
                rgba[offset + 3] = 255;
            }
        }

        return new JpegImageCodec().Encode(new ImageBuffer(width, height, rgba), quality: 90);
    }

    /// <summary>
    /// A real progressive JPEG: 8x8 greyscale carrying one DC-only scan
    /// (<c>Ss</c>=<c>Se</c>=0, <c>Ah</c>=<c>Al</c>=0) that codes a non-zero
    /// difference.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The managed encoder writes baseline only and this repository commits no
    /// image fixtures, so a progressive decode has to be tested against a file
    /// written here. This is the smallest one that is genuinely progressive
    /// rather than merely labelled so: a single block, and a scan whose entropy
    /// data the decoder must actually read to produce the right samples.
    /// </para>
    /// <para>
    /// The Huffman table carries two codes of length two, which makes symbol 4 —
    /// DC magnitude category 4 — the bit pair <c>01</c>. Four magnitude bits of
    /// <c>1111</c> extend to +15, and the scan pads to a byte boundary with ones,
    /// so the whole entropy segment is the single byte <c>0x7F</c>. Against the
    /// flat quantization table below that lands the plane at 158, well clear of
    /// the 128 an all-zero block would give.
    /// </para>
    /// </remarks>
    private static byte[] ProgressiveJpeg()
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };

        // DQT: one flat 8-bit table.
        bytes.AddRange([0xFF, 0xDB]);
        bytes.AddRange(BigEndian(2 + 1 + 64));
        bytes.Add(0x00);
        bytes.AddRange(Enumerable.Repeat((byte)16, 64));

        // SOF2: 8x8, one component, no subsampling, quantization table 0.
        bytes.AddRange([0xFF, 0xC2]);
        bytes.AddRange(BigEndian(2 + 6 + 3));
        bytes.Add(8);
        bytes.AddRange(BigEndian(8));
        bytes.AddRange(BigEndian(8));
        bytes.Add(1);
        bytes.AddRange([0x01, 0x11, 0x00]);

        // DHT: DC table 0 — no codes of length one, two of length two, for
        // symbols 0 and 4.
        bytes.AddRange([0xFF, 0xC4]);
        bytes.AddRange(BigEndian(2 + 1 + 16 + 2));
        bytes.Add(0x00);
        bytes.AddRange([0x00, 0x02]);
        bytes.AddRange(Enumerable.Repeat((byte)0, 14));
        bytes.AddRange([0x00, 0x04]);

        // SOS: the DC band alone, first pass, no successive approximation.
        bytes.AddRange([0xFF, 0xDA]);
        bytes.AddRange(BigEndian(2 + 1 + 2 + 3));
        bytes.Add(1);
        bytes.AddRange([0x01, 0x00]);
        bytes.AddRange([0x00, 0x00, 0x00]);

        bytes.Add(0x7F);

        bytes.AddRange([0xFF, 0xD9]);
        return bytes.ToArray();
    }

    /// <summary>
    /// Marker segments only: SOI, an optional Adobe APP14, one SOFn, EOI. Enough
    /// for every decision the filter makes before decoding, and nothing more.
    /// </summary>
    private static byte[] Frame(
        byte marker,
        int precision,
        int width,
        int height,
        int components,
        int adobeTransform = -1)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };

        if (adobeTransform >= 0)
            bytes.AddRange(AdobeMarker(adobeTransform));

        int payload = 6 + (components * 3);
        bytes.AddRange([0xFF, marker]);
        bytes.AddRange(BigEndian(payload + 2));
        bytes.Add((byte)precision);
        bytes.AddRange(BigEndian(height));
        bytes.AddRange(BigEndian(width));
        bytes.Add((byte)components);
        for (int i = 0; i < components; i++)
            bytes.AddRange([(byte)(i + 1), 0x11, 0x00]);

        bytes.AddRange([0xFF, 0xD9]);
        return bytes.ToArray();
    }

    /// <summary>The same JPEG with an Adobe APP14 marker inserted after the SOI.</summary>
    internal static byte[] WithAdobeMarker(byte[] jpeg, int transform)
    {
        var bytes = new List<byte>(jpeg.Length + 16);
        bytes.AddRange(jpeg.AsSpan(0, 2).ToArray());
        bytes.AddRange(AdobeMarker(transform));
        bytes.AddRange(jpeg.AsSpan(2).ToArray());
        return bytes.ToArray();
    }

    private static byte[] AdobeMarker(int transform)
    {
        var bytes = new List<byte> { 0xFF, 0xEE, 0x00, 0x0E };
        bytes.AddRange(Encoding.ASCII.GetBytes("Adobe"));
        bytes.AddRange([0x00, 0x64, 0x00, 0x00, 0x00, 0x00, (byte)transform]);
        return bytes.ToArray();
    }

    private static byte[] BigEndian(int value) => [(byte)(value >> 8), (byte)(value & 0xFF)];
}
