using System.Buffers.Binary;

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
    public void An_Arithmetic_Generic_Region_Is_Refused_By_Name()
    {
        byte[] stream = Page(64, 32, ArithmeticGenericRegion(64, 32));

        PdfFilterResult result = Decode(stream);

        Assert.Equal(PdfDiagnosticCodes.FilterJbig2Unsupported, result.DiagnosticCode);
        Assert.Contains("64x32 generic region is arithmetic-coded with template 0", result.Message, StringComparison.Ordinal);

        // The distinction the whole exercise turns on.
        Assert.Contains("outstanding work rather than a pending approval", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Symbol_Dictionary_And_Text_Region_Are_Reported_As_The_Inventory()
    {
        // The shape almost every real JBIG2 in a PDF has, and the one this build
        // does not decode. What it says back is the inventory a decision about
        // writing the arithmetic decoder would be made from.
        byte[] stream = Page(64, 32, Segment(number: 1, type: 0, [1, 2, 3]), Segment(number: 2, type: 6, [4, 5, 6]));

        PdfFilterResult result = Decode(stream);

        Assert.Equal(PdfDiagnosticCodes.FilterJbig2Unsupported, result.DiagnosticCode);
        Assert.Contains("symbol dictionary", result.Message, StringComparison.Ordinal);
        Assert.Contains("text region", result.Message, StringComparison.Ordinal);
        Assert.Contains("needs the arithmetic decoder", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Globals_Are_Read_And_Reported()
    {
        // The parameter is a stream, which the filter extension point could not
        // reach at all until it learned to hand one over decoded.
        byte[] globals = Segments(Segment(number: 0, type: 0, [1, 2, 3, 4]));
        byte[] stream = Page(64, 32, GenericRegion(Pattern(64, 32), 0, 0));

        PdfFilterResult result = Decode(stream, globals);

        Assert.Equal(PdfDiagnosticCodes.FilterJbig2Unsupported, result.DiagnosticCode);
        Assert.Contains("JBIG2Globals hold 1 symbol dictionary", result.Message, StringComparison.Ordinal);
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

    /// <summary>A page information segment, the given segments, and an end of page.</summary>
    private static byte[] Page(int width, int height, params byte[][] segments)
    {
        var info = new List<byte>();
        AddUInt32(info, width);
        AddUInt32(info, height);
        AddUInt32(info, 0);             // x resolution
        AddUInt32(info, 0);             // y resolution
        info.Add(0);                    // flags
        AddUInt16(info, 0);             // striping

        var all = new List<byte[]> { Segment(number: 0, type: 48, [.. info]) };
        all.AddRange(segments);
        all.Add(Segment(number: 9999, type: 49, []));
        return Segments([.. all]);
    }

    private static byte[] Segments(params byte[][] segments)
    {
        var bytes = new List<byte>();
        foreach (byte[] segment in segments)
            bytes.AddRange(segment);
        return bytes.ToArray();
    }

    /// <summary>One segment header in the sequential organisation, plus its data.</summary>
    private static byte[] Segment(uint number, int type, byte[] data)
    {
        var bytes = new List<byte>();
        AddUInt32(bytes, number);
        bytes.Add((byte)type);          // flags: type, one-byte page association
        bytes.Add(0);                   // no referred-to segments, no retain bits
        bytes.Add(1);                   // page association
        AddUInt32(bytes, data.Length);
        bytes.AddRange(data);
        return bytes.ToArray();
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
    private static byte[] ArithmeticGenericRegion(int width, int height)
    {
        var body = new List<byte>();
        AddUInt32(body, width);
        AddUInt32(body, height);
        AddUInt32(body, 0);
        AddUInt32(body, 0);
        body.Add(0);                    // combination operator
        body.Add(0);                    // generic flags: arithmetic, template 0
        body.AddRange(new byte[8]);     // adaptive template pixels
        body.AddRange(new byte[16]);    // stand-in for the arithmetic data

        return Segment(number: 1, type: 38, [.. body]);
    }

    private static bool[][] Unpack(byte[] packed, int columns, int rows)
    {
        int stride = (columns + 7) / 8;
        var image = new bool[rows][];

        for (int y = 0; y < rows; y++)
        {
            image[y] = new bool[columns];
            for (int x = 0; x < columns; x++)
            {
                // The filter emits PDF's convention, where zero is black.
                int bit = (packed[(y * stride) + (x >> 3)] >> (7 - (x & 7))) & 1;
                image[y][x] = bit == 0;
            }
        }

        return image;
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
