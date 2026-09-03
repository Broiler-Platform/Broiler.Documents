using static Broiler.Documents.Pdf.Images.Tests.Jbig2Streams;

namespace Broiler.Documents.Pdf.Images.Tests;

/// <summary>
/// Covers the JBIG2 subset this build decodes — generic regions coded with MMR —
/// and the inventory it reports for everything else.
/// </summary>
/// <remarks>
/// Every stream is assembled segment by segment in the test, and the one region
/// type that decodes carries a T.6 bitmap produced by
/// <see cref="CcittFaxEncoder"/>. No JBIG2 file is committed: IP-020 would want
/// one registered with its provenance first, and building the segments by hand is
/// the only way to state exactly which structure each test is about.
/// </remarks>
public sealed class Jbig2StreamFilterTests
{
    private static readonly PdfFilterContext Generous = new(16L * 1024 * 1024, 65536);

    private static PdfFilterResult Decode(byte[] data, byte[]? globals = null)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (globals is not null)
            values["JBIG2Globals"] = globals;

        return new Jbig2StreamFilter().Decode(data, PdfFilterParameters.From(values), Generous);
    }

    // ---- the subset that decodes ----------------------------------------------

    [Fact]
    public void A_Generic_Region_Coded_With_Mmr_Decodes()
    {
        bool[][] bitmap = Pattern(64, 32);
        byte[] stream = Page(64, 32, GenericRegion(bitmap, 0, 0));

        PdfFilterResult result = Decode(stream);

        Assert.True(result.Succeeded, result.Message);
        AssertSame(bitmap, Unpack(result.Data!, 64, 32));
    }

    [Fact]
    public void Two_Regions_Composite_Onto_One_Page()
    {
        bool[][] top = Pattern(32, 16);
        bool[][] bottom = Pattern(32, 16);

        byte[] stream = Page(32, 32, GenericRegion(top, 0, 0), GenericRegion(bottom, 0, 16));

        PdfFilterResult result = Decode(stream);

        Assert.True(result.Succeeded, result.Message);
        bool[][] page = Unpack(result.Data!, 32, 32);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                Assert.Equal(top[y][x], page[y][x]);
                Assert.Equal(bottom[y][x], page[y + 16][x]);
            }
        }
    }

    [Fact]
    public void A_Region_Is_Placed_At_Its_Declared_Offset()
    {
        bool[][] block = Uniform(8, 8, black: true);
        byte[] stream = Page(32, 32, GenericRegion(block, 8, 8));

        bool[][] page = Unpack(Decode(stream).Data!, 32, 32);

        Assert.True(page[8][8]);
        Assert.True(page[15][15]);
        Assert.False(page[7][7]);
        Assert.False(page[16][16]);
    }

    // ---- what it refuses, and how precisely -----------------------------------

    [Fact]
    public void An_Arithmetic_Generic_Region_Decodes_Through_The_Filter()
    {
        // This asserted a refusal until the arithmetic decoder was written. It now
        // asserts the whole path instead: a region encoded by the test encoder,
        // through the segment reader, the MQ decoder, and the page compositor.
        bool[][] bitmap = Pattern(64, 32);
        byte[] stream = Page(64, 32, ArithmeticGenericRegion(bitmap));

        PdfFilterResult result = Decode(stream);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(bitmap, Unpack(result.Data!, 64, 32));
    }

    [Fact]
    public void A_Symbol_Dictionary_Outside_The_Subset_Is_Named_Rather_Than_Counted()
    {
        // This asserted that a symbol dictionary was reported as inventory and
        // nothing more, which was the whole of the behaviour until the dictionary
        // decoder was written. It then asserted a refusal for refinement, which
        // decodes now too. What is left outside the subset is a dictionary that
        // imports another's coding contexts, and that is what these flags declare.
        byte[] stream = Page(
            64, 32,
            Segment(number: 1, type: 0, [0x01, 0x00, 0, 0, 0, 0, 0, 0, 0, 0]),
            Segment(number: 2, type: 6, TextRegionHeader(32, 16), referred: [1]));

        PdfFilterResult result = Decode(stream);

        Assert.Equal(PdfDiagnosticCodes.FilterJbig2Unsupported, result.DiagnosticCode);
        Assert.Contains("imports another's coding contexts", result.Message, StringComparison.Ordinal);
        Assert.Contains("1 symbol dictionary", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_Intermediate_Text_Region_Is_Refused_By_Name()
    {
        // Type 4 is the intermediate form: it is kept in an auxiliary buffer for
        // another segment to refer to rather than drawn, and the buffers are not
        // built. The immediate forms decode; this one says why it does not.
        PdfFilterResult result = Decode(Page(64, 32, Segment(number: 1, type: 4, [1, 2, 3])));

        Assert.Equal(PdfDiagnosticCodes.FilterJbig2Unsupported, result.DiagnosticCode);
        Assert.Contains("text region", result.Message, StringComparison.Ordinal);
        Assert.Contains("whose decoder is not written", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Globals_Are_Read_And_Reported()
    {
        // The parameter is a stream, which the filter extension point could not
        // reach at all until it learned to hand one over decoded. A dictionary in
        // there is now used rather than reported — see the symbol tests — so what
        // this covers is a globals segment that is still outside the subset.
        byte[] globals = Segments(Segment(number: 0, type: 22, [1, 2, 3, 4]));
        byte[] stream = Page(64, 32, GenericRegion(Pattern(64, 32), 0, 0));

        PdfFilterResult result = Decode(stream, globals);

        Assert.Equal(PdfDiagnosticCodes.FilterJbig2Unsupported, result.DiagnosticCode);
        Assert.Contains("JBIG2Globals hold a halftone region", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Halftone_Region_Is_Named()
    {
        PdfFilterResult result = Decode(Page(64, 32, Segment(number: 1, type: 22, [1, 2, 3])));

        Assert.Contains("halftone region", result.Message, StringComparison.Ordinal);
    }

    // ---- malformed input ------------------------------------------------------

    [Fact]
    public void A_Stream_That_Is_Not_Jbig2_Is_Malformed()
    {
        PdfFilterResult result = Decode("not a segment header at all"u8.ToArray());

        Assert.Equal(PdfDiagnosticCodes.FilterMalformed, result.DiagnosticCode);
    }

    [Fact]
    public void A_Segment_Declaring_More_Data_Than_The_Stream_Holds_Is_Malformed()
    {
        var stream = new List<byte>();
        AddUInt32(stream, 1);
        stream.Add(38);                 // immediate generic region
        stream.Add(0);                  // no referred-to segments
        stream.Add(1);                  // page association
        AddUInt32(stream, 9999);        // data length past the end

        Assert.Equal(PdfDiagnosticCodes.FilterMalformed, Decode(stream.ToArray()).DiagnosticCode);
    }

    [Fact]
    public void Every_Truncation_Reaches_A_Decision()
    {
        byte[] stream = Page(64, 32, GenericRegion(Pattern(64, 32), 0, 0));

        for (int length = 0; length <= stream.Length; length += 3)
        {
            PdfFilterResult result = Decode(stream.AsSpan(0, length).ToArray());
            Assert.True(result.Succeeded || result.DiagnosticCode is not null);
        }
    }

    [Fact]
    public void The_Filter_Names_Itself_As_An_Image_Filter()
    {
        var filter = new Jbig2StreamFilter();

        Assert.Equal("JBIG2Decode", filter.Name);
        Assert.False(filter.ProducesByteStream);
    }

    // ---- fixtures -------------------------------------------------------------

    /// <summary>
    /// A text region body that stops after its flags: enough for a page to hold a
    /// region, where the test is about another segment.
    /// </summary>
    private static byte[] TextRegionHeader(int width, int height)
    {
        var body = new List<byte>();
        AddUInt32(body, width);
        AddUInt32(body, height);
        AddUInt32(body, 0);
        AddUInt32(body, 0);
        body.Add(0);
        AddUInt16(body, 0);
        return [.. body];
    }

    /// <summary>An immediate generic region carrying <paramref name="bitmap"/> as MMR.</summary>
    private static byte[] GenericRegion(bool[][] bitmap, int x, int y)
    {
        var body = new List<byte>();
        AddUInt32(body, bitmap[0].Length);
        AddUInt32(body, bitmap.Length);
        AddUInt32(body, x);
        AddUInt32(body, y);
        body.Add(0);                    // combination operator: OR
        body.Add(1);                    // generic flags: MMR
        body.AddRange(CcittFaxEncoder.Encode(bitmap, k: -1));

        return Segment(number: 1, type: 38, [.. body]);
    }

    /// <summary>An immediate generic region declaring arithmetic coding.</summary>
    /// <summary>A generic region actually coded with the arithmetic coder.</summary>
    private static byte[] ArithmeticGenericRegion(bool[][] bitmap)
    {
        int height = bitmap.Length;
        int width = bitmap[0].Length;

        var pixels = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                pixels[(y * width) + x] = bitmap[y][x] ? (byte)1 : (byte)0;
        }

        var body = new List<byte>();
        AddUInt32(body, width);
        AddUInt32(body, height);
        AddUInt32(body, 0);
        AddUInt32(body, 0);
        body.Add(0);                    // combination operator
        body.Add(0);                    // generic flags: arithmetic, template 0

        // Nominal adaptive pixels, as signed bytes.
        body.AddRange(new byte[] { 3, 0xFF, 0xFD, 0xFF, 2, 0xFE, 0xFE, 0xFE });
        body.AddRange(Jbig2GenericEncoder.Encode(pixels, width, height, template: 0));

        return Segment(number: 1, type: 38, [.. body]);
    }

    private static void AssertSame(bool[][] expected, bool[][] actual)
    {
        for (int y = 0; y < expected.Length; y++)
        {
            for (int x = 0; x < expected[y].Length; x++)
                Assert.True(expected[y][x] == actual[y][x], $"pixel ({x}, {y})");
        }
    }

    private static bool[][] Pattern(int columns, int rows)
    {
        var image = new bool[rows][];
        for (int y = 0; y < rows; y++)
        {
            image[y] = new bool[columns];
            for (int x = 0; x < columns; x++)
                image[y][x] = ((x / (3 + (y % 5))) + y) % 3 == 0;
        }

        return image;
    }

    private static bool[][] Uniform(int columns, int rows, bool black)
    {
        var image = new bool[rows][];
        for (int y = 0; y < rows; y++)
        {
            image[y] = new bool[columns];
            for (int x = 0; x < columns; x++)
                image[y][x] = black;
        }

        return image;
    }
}
