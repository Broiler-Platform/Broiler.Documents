using System.Buffers.Binary;

namespace Broiler.Documents.Pdf.Images.Tests;

/// <summary>
/// Covers what the composed JPX filter can say about a JPEG 2000 image, which is
/// everything except what its pixels are.
/// </summary>
/// <remarks>
/// Every codestream here is assembled marker by marker in the test. No JPEG 2000
/// file is committed — IP-020 would need one registered with its provenance and
/// rights first — and building the headers by hand is also the only way to state
/// the exact tuple each test is about.
/// </remarks>
public sealed class JpxStreamFilterTests
{
    private static readonly PdfFilterContext Generous = new(16L * 1024 * 1024, 4096);

    private static PdfFilterResult Decode(byte[] data) =>
        new JpxStreamFilter().Decode(data, PdfFilterParameters.Empty, Generous);

    // ---- what it reports ------------------------------------------------------

    [Fact]
    public void A_Part_One_Codestream_Is_Reported_With_Its_Whole_Tuple()
    {
        PdfFilterResult result = Decode(Codestream(width: 2480, height: 3508, components: 3, bitDepth: 8));

        Assert.Equal(PdfDiagnosticCodes.FilterJpxUnsupported, result.DiagnosticCode);
        Assert.Contains("2480x3508", result.Message, StringComparison.Ordinal);
        Assert.Contains("3 components", result.Message, StringComparison.Ordinal);
        Assert.Contains("8-bit unsigned", result.Message, StringComparison.Ordinal);
        Assert.Contains("5 decomposition levels", result.Message, StringComparison.Ordinal);
        Assert.Contains("9/7 irreversible wavelet", result.Message, StringComparison.Ordinal);

        // The message has to distinguish "cleared but unwritten" from "pending",
        // because they are fixed by completely different work.
        Assert.Contains("outstanding work rather than a pending approval", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Reversible_Codestream_Is_Reported_As_Reversible()
    {
        PdfFilterResult result = Decode(
            Codestream(width: 64, height: 64, components: 1, bitDepth: 12, reversible: true, levels: 2));

        Assert.Contains("12-bit unsigned", result.Message, StringComparison.Ordinal);
        Assert.Contains("1 component,", result.Message, StringComparison.Ordinal);
        Assert.Contains("5/3 reversible wavelet", result.Message, StringComparison.Ordinal);
        Assert.Contains("2 decomposition levels", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Signed_And_Subsampled_Components_Are_Reported()
    {
        PdfFilterResult result = Decode(
            Codestream(width: 32, height: 32, components: 3, bitDepth: 10, signed: true, subsampled: true));

        Assert.Contains("10-bit signed", result.Message, StringComparison.Ordinal);
        Assert.Contains("subsampled components", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Jp2_Container_Is_Unwrapped_To_Its_Codestream()
    {
        byte[] wrapped = Jp2(Codestream(width: 100, height: 200, components: 3, bitDepth: 8));

        PdfFilterResult result = Decode(wrapped);

        Assert.Contains("a JP2 container holding", result.Message, StringComparison.Ordinal);
        Assert.Contains("100x200", result.Message, StringComparison.Ordinal);
    }

    // ---- the edge of the row --------------------------------------------------

    [Fact]
    public void A_Part_Two_Codestream_Is_Refused_As_Outside_The_Row()
    {
        // Rsiz with a high bit set names Part 2 extensions. IP-007 clears the
        // Part 1 core coding system, and saying so is the point of reading Rsiz.
        PdfFilterResult result = Decode(
            Codestream(width: 64, height: 64, components: 3, bitDepth: 8, capability: 0x8000));

        Assert.Equal(PdfDiagnosticCodes.FilterJpxUnsupported, result.DiagnosticCode);
        Assert.Contains("Part 2 extensions", result.Message, StringComparison.Ordinal);
        Assert.Contains("outside the row", result.Message, StringComparison.Ordinal);
    }

    // ---- malformed input ------------------------------------------------------

    [Fact]
    public void A_Stream_That_Is_Not_A_Codestream_Is_Malformed()
    {
        PdfFilterResult result = Decode("this is not a codestream"u8.ToArray());

        Assert.Equal(PdfDiagnosticCodes.FilterMalformed, result.DiagnosticCode);
        Assert.Contains("JP2 signature", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Codestream_With_No_Siz_Marker_Is_Malformed()
    {
        PdfFilterResult result = Decode([0xFF, 0x4F, 0xFF, 0xD9]);

        Assert.Equal(PdfDiagnosticCodes.FilterMalformed, result.DiagnosticCode);
        Assert.Contains("no SIZ marker", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Codestream_Declaring_An_Empty_Image_Is_Malformed()
    {
        PdfFilterResult result = Decode(Codestream(width: 0, height: 32, components: 1, bitDepth: 8));

        Assert.Equal(PdfDiagnosticCodes.FilterMalformed, result.DiagnosticCode);
    }

    [Fact]
    public void Every_Truncation_Reaches_A_Decision()
    {
        byte[] codestream = Jp2(Codestream(width: 640, height: 480, components: 3, bitDepth: 8));

        for (int length = 0; length <= codestream.Length; length += 3)
        {
            PdfFilterResult result = Decode(codestream.AsSpan(0, length).ToArray());
            Assert.False(result.Succeeded);
            Assert.NotNull(result.DiagnosticCode);
        }
    }

    [Fact]
    public void The_Filter_Names_Itself_As_An_Image_Filter()
    {
        var filter = new JpxStreamFilter();

        Assert.Equal("JPXDecode", filter.Name);
        Assert.Null(filter.Abbreviation);
        Assert.False(filter.ProducesByteStream);
    }

    // ---- fixtures -------------------------------------------------------------

    /// <summary>SOC, a SIZ describing the image, a COD describing the coding, EOC.</summary>
    private static byte[] Codestream(
        int width,
        int height,
        int components,
        int bitDepth,
        bool signed = false,
        bool subsampled = false,
        bool reversible = false,
        int levels = 5,
        int capability = 0)
    {
        var bytes = new List<byte> { 0xFF, 0x4F };

        var siz = new List<byte>();
        AddUInt16(siz, capability);
        AddUInt32(siz, width);          // Xsiz
        AddUInt32(siz, height);         // Ysiz
        AddUInt32(siz, 0);              // XOsiz
        AddUInt32(siz, 0);              // YOsiz
        AddUInt32(siz, 1024);           // XTsiz
        AddUInt32(siz, 1024);           // YTsiz
        AddUInt32(siz, 0);              // XTOsiz
        AddUInt32(siz, 0);              // YTOsiz
        AddUInt16(siz, components);
        for (int c = 0; c < components; c++)
        {
            siz.Add((byte)((bitDepth - 1) | (signed ? 0x80 : 0x00)));
            siz.Add((byte)(subsampled && c > 0 ? 2 : 1));
            siz.Add((byte)(subsampled && c > 0 ? 2 : 1));
        }

        AddSegment(bytes, 0xFF51, siz);

        var cod = new List<byte> { 0, 0 };          // Scod, then SGcod progression order
        AddUInt16(cod, 3);                          // SGcod: quality layers
        cod.Add(1);                                 // SGcod: multiple component transform
        cod.Add((byte)levels);                      // SPcod: decomposition levels
        cod.Add(4);                                 // SPcod: code-block width exponent
        cod.Add(4);                                 // SPcod: code-block height exponent
        cod.Add(0);                                 // SPcod: code-block style
        cod.Add((byte)(reversible ? 1 : 0));        // SPcod: transform

        AddSegment(bytes, 0xFF52, cod);

        bytes.Add(0xFF);
        bytes.Add(0xD9);
        return bytes.ToArray();
    }

    /// <summary>The JP2 signature and file-type boxes, then the codestream box.</summary>
    private static byte[] Jp2(byte[] codestream)
    {
        var bytes = new List<byte>();

        bytes.AddRange([0x00, 0x00, 0x00, 0x0C]);
        bytes.AddRange("jP  "u8.ToArray());
        bytes.AddRange([0x0D, 0x0A, 0x87, 0x0A]);

        var ftyp = new List<byte>();
        ftyp.AddRange("jp2 "u8.ToArray());
        AddUInt32(ftyp, 0);
        ftyp.AddRange("jp2 "u8.ToArray());
        AddBox(bytes, "ftyp", ftyp);

        AddBox(bytes, "jp2c", [.. codestream]);
        return bytes.ToArray();
    }

    private static void AddBox(List<byte> target, string type, List<byte> body)
    {
        AddUInt32(target, body.Count + 8);
        foreach (char c in type)
            target.Add((byte)c);
        target.AddRange(body);
    }

    private static void AddSegment(List<byte> target, int marker, List<byte> body)
    {
        target.Add((byte)(marker >> 8));
        target.Add((byte)(marker & 0xFF));
        AddUInt16(target, body.Count + 2);
        target.AddRange(body);
    }

    private static void AddUInt16(List<byte> target, int value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        target.AddRange(bytes.ToArray());
    }

    private static void AddUInt32(List<byte> target, long value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value);
        target.AddRange(bytes.ToArray());
    }
}
