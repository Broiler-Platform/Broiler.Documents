using static Broiler.Documents.Pdf.Images.Tests.Jbig2Streams;

namespace Broiler.Documents.Pdf.Images.Tests;

/// <summary>
/// Covers the symbol dictionaries and text regions a scanned page is made of.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What these prove, and what they cannot.</strong> Every stream here is
/// produced by the encoders in this project, so a passing round trip establishes
/// that the two halves implement the same reading of ITU-T T.88 — not that the
/// reading is right. That limit is the corpus rule rather than an omission: the
/// standard's test sequences are official test material, and no real JBIG2 file
/// may be committed as a fixture (IP-020). Until a real scanned page has been
/// decoded, nothing here is evidence that this build can read one.
/// </para>
/// <para>
/// <strong>Where the halves are deliberately not shared.</strong> The reference
/// corner is the one piece of geometry written twice — once in the decoder from
/// T.88 6.4.5, once in <see cref="Place"/> here — because it is the part where a
/// slip produces a page that looks entirely reasonable and has the symbols in the
/// wrong places. Two independent expressions of it will not catch a misreading
/// they share, but they do catch a transcription slip in either.
/// </para>
/// <para>
/// The export flags get the same treatment from the other direction: a dictionary
/// that exports fewer symbols than it defines is built deliberately, because a
/// decoder that ignored the flags would still produce a page — one drawing the
/// wrong glyph under every identifier.
/// </para>
/// </remarks>
public sealed class Jbig2SymbolRegionTests
{
    private static readonly PdfFilterContext Generous = new(16L * 1024 * 1024, 65536);

    /// <summary>Three shapes distinct enough that a swap between them is visible.</summary>
    private static Jbig2Bitmap Box => Symbol(
        "#####",
        "#...#",
        "#...#",
        "#####");

    private static Jbig2Bitmap Bar => Symbol(
        "###",
        "###",
        "...",
        "###");

    private static Jbig2Bitmap Dot => Symbol(
        ".##.",
        "####",
        "####",
        ".##.");

    // ---- the whole path ---------------------------------------------------------

    [Fact]
    public void A_Dictionary_And_A_Text_Region_Draw_A_Page()
    {
        Jbig2Bitmap[] source = [Box, Bar, Dot];
        byte[] dictionary = Jbig2SymbolDictionaryEncoder.Encode(source, out Jbig2Bitmap[] order);

        Jbig2Instance[] instances =
        [
            new(Id: 0, S: 2, T: 3),
            new(Id: 1, S: 12, T: 3),
            new(Id: 2, S: 24, T: 3),
            new(Id: 0, S: 6, T: 14),
        ];

        byte[] stream = Page(
            48, 24,
            Segment(number: 1, type: 0, dictionary),
            Segment(number: 2, type: 6, Jbig2TextRegionEncoder.Encode(48, 24, order, instances), referred: [1]));

        PdfFilterResult result = Decode(stream);

        Assert.True(result.Succeeded, result.Message);
        AssertSame(Expected(48, 24, order, instances, corner: 1, transposed: false), Unpack(result.Data!, 48, 24));
    }

    [Fact]
    public void The_Dictionary_May_Arrive_Through_Globals()
    {
        // Which is where a PDF nearly always keeps it: one dictionary shared by
        // every JBIG2 image in the file, with the page streams holding only the
        // regions that draw from it. Segment numbers are one space across the two.
        Jbig2Bitmap[] source = [Box, Dot];
        byte[] dictionary = Jbig2SymbolDictionaryEncoder.Encode(source, out Jbig2Bitmap[] order);
        Jbig2Instance[] instances = [new(Id: 0, S: 1, T: 2), new(Id: 1, S: 20, T: 6)];

        byte[] globals = Segments(Segment(number: 1, type: 0, dictionary));
        byte[] stream = Page(
            32, 16,
            Segment(number: 2, type: 6, Jbig2TextRegionEncoder.Encode(32, 16, order, instances), referred: [1]));

        PdfFilterResult result = Decode(stream, globals);

        Assert.True(result.Succeeded, result.Message);
        AssertSame(Expected(32, 16, order, instances, corner: 1, transposed: false), Unpack(result.Data!, 32, 16));
    }

    [Fact]
    public void A_Symbol_The_Dictionary_Declines_To_Export_Is_Not_Drawn()
    {
        // The dictionary defines three symbols and exports the last two. A
        // decoder that ignored the export flags would answer identifier 0 with
        // the first symbol instead of the second, and draw a plausible, wrong
        // page rather than failing.
        Jbig2Bitmap[] source = [Box, Bar, Dot];
        byte[] dictionary = Jbig2SymbolDictionaryEncoder.Encode(source, out Jbig2Bitmap[] order, unexportedPrefix: 1);
        Jbig2Bitmap[] exported = [.. order.Skip(1)];

        Jbig2Instance[] instances = [new(Id: 0, S: 2, T: 2)];

        byte[] stream = Page(
            24, 12,
            Segment(number: 1, type: 0, dictionary),
            Segment(number: 2, type: 6, Jbig2TextRegionEncoder.Encode(24, 12, exported, instances), referred: [1]));

        PdfFilterResult result = Decode(stream);

        Assert.True(result.Succeeded, result.Message);
        bool[][] page = Unpack(result.Data!, 24, 12);

        AssertSame(Expected(24, 12, exported, instances, corner: 1, transposed: false), page);

        // And the symbol it declined to export draws differently, which is what
        // makes the assertion above mean something.
        Assert.NotEqual(
            Expected(24, 12, order, instances, corner: 1, transposed: false),
            page);
    }

    [Theory]
    [InlineData(0)]     // BOTTOMLEFT
    [InlineData(1)]     // TOPLEFT
    [InlineData(2)]     // BOTTOMRIGHT
    [InlineData(3)]     // TOPRIGHT
    public void Each_Reference_Corner_Places_The_Symbol_Where_It_Says(int corner)
    {
        Jbig2Bitmap[] source = [Box, Dot];
        byte[] dictionary = Jbig2SymbolDictionaryEncoder.Encode(source, out Jbig2Bitmap[] order);
        Jbig2Instance[] instances = [new(Id: 0, S: 6, T: 8), new(Id: 1, S: 20, T: 8)];

        byte[] stream = Page(
            40, 20,
            Segment(number: 1, type: 0, dictionary),
            Segment(
                number: 2,
                type: 6,
                Jbig2TextRegionEncoder.Encode(40, 20, order, instances, corner: corner),
                referred: [1]));

        PdfFilterResult result = Decode(stream);

        Assert.True(result.Succeeded, result.Message);
        AssertSame(Expected(40, 20, order, instances, corner, transposed: false), Unpack(result.Data!, 40, 20));
    }

    [Fact]
    public void A_Transposed_Region_Runs_Its_Symbols_Down_The_Page()
    {
        // Transposed swaps the axes: the running coordinate goes down the region
        // and the strips run across it. The format has it for vertical writing,
        // and it is the case where reading S as a column would still produce a
        // page.
        Jbig2Bitmap[] source = [Box, Dot];
        byte[] dictionary = Jbig2SymbolDictionaryEncoder.Encode(source, out Jbig2Bitmap[] order);
        Jbig2Instance[] instances = [new(Id: 0, S: 2, T: 4), new(Id: 1, S: 12, T: 4)];

        byte[] stream = Page(
            24, 32,
            Segment(number: 1, type: 0, dictionary),
            Segment(
                number: 2,
                type: 6,
                Jbig2TextRegionEncoder.Encode(24, 32, order, instances, transposed: true),
                referred: [1]));

        PdfFilterResult result = Decode(stream);

        Assert.True(result.Succeeded, result.Message);
        AssertSame(Expected(24, 32, order, instances, corner: 1, transposed: true), Unpack(result.Data!, 24, 32));
    }

    [Fact]
    public void Strips_Carry_Their_Own_Vertical_Offsets()
    {
        // With more than one row per strip, each instance codes its own offset
        // within the strip instead of taking the strip's own T. Nothing else in
        // the suite reads that value, so a decoder that skipped it would pass
        // every other test here and stack the symbols on one line.
        Jbig2Bitmap[] source = [Box, Dot];
        byte[] dictionary = Jbig2SymbolDictionaryEncoder.Encode(source, out Jbig2Bitmap[] order);
        Jbig2Instance[] instances = [new(Id: 0, S: 2, T: 4), new(Id: 1, S: 12, T: 6)];

        byte[] stream = Page(
            32, 20,
            Segment(number: 1, type: 0, dictionary),
            Segment(
                number: 2,
                type: 6,
                Jbig2TextRegionEncoder.Encode(32, 20, order, instances, logStrips: 2),
                referred: [1]));

        PdfFilterResult result = Decode(stream);

        Assert.True(result.Succeeded, result.Message);
        AssertSame(Expected(32, 20, order, instances, corner: 1, transposed: false), Unpack(result.Data!, 32, 20));
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(0)]
    [InlineData(5)]
    public void The_Spacing_Offset_Shifts_Every_Gap(int offset)
    {
        // A five-bit signed field added to every gap between symbols, so that an
        // encoder can make the common gap cost nothing. Read with the wrong sign
        // it would still draw a page, with the spacing steadily wrong.
        Jbig2Bitmap[] source = [Box, Dot];
        byte[] dictionary = Jbig2SymbolDictionaryEncoder.Encode(source, out Jbig2Bitmap[] order);
        Jbig2Instance[] instances = [new(Id: 0, S: 2, T: 3), new(Id: 1, S: 14, T: 3), new(Id: 0, S: 22, T: 3)];

        byte[] stream = Page(
            40, 16,
            Segment(number: 1, type: 0, dictionary),
            Segment(
                number: 2,
                type: 6,
                Jbig2TextRegionEncoder.Encode(40, 16, order, instances, spacingOffset: offset),
                referred: [1]));

        PdfFilterResult result = Decode(stream);

        Assert.True(result.Succeeded, result.Message);
        AssertSame(Expected(40, 16, order, instances, corner: 1, transposed: false), Unpack(result.Data!, 40, 16));
    }

    [Fact]
    public void A_Dictionary_Built_On_Another_Dictionarys_Symbols_Exports_Both()
    {
        // A dictionary's export flags count through the symbols it was given
        // before the ones it defined, so a second dictionary can re-export the
        // first's. The identifiers a text region then uses are indices into that
        // combined list, and getting the order wrong swaps glyphs silently.
        Jbig2Bitmap[] first = [Box];
        byte[] dictionary = Jbig2SymbolDictionaryEncoder.Encode(first, out Jbig2Bitmap[] firstOrder);

        Jbig2Bitmap[] second = [Dot];
        byte[] chained = Jbig2SymbolDictionaryEncoder.Encode(second, out Jbig2Bitmap[] secondOrder);

        Jbig2Bitmap[] available = [.. firstOrder, .. secondOrder];
        Jbig2Instance[] instances = [new(Id: 0, S: 2, T: 2), new(Id: 1, S: 14, T: 2)];

        byte[] stream = Page(
            32, 12,
            Segment(number: 1, type: 0, dictionary),
            Segment(number: 2, type: 0, chained),
            Segment(
                number: 3,
                type: 6,
                Jbig2TextRegionEncoder.Encode(32, 12, available, instances),
                referred: [1, 2]));

        PdfFilterResult result = Decode(stream);

        Assert.True(result.Succeeded, result.Message);
        AssertSame(Expected(32, 12, available, instances, corner: 1, transposed: false), Unpack(result.Data!, 32, 12));
    }

    // ---- what it refuses, and how precisely -------------------------------------

    [Fact]
    public void A_Huffman_Coded_Dictionary_Is_Refused_By_Name()
    {
        // With a text region drawing from it, which is what makes the dictionary
        // worth reading at all: a page holding a dictionary and nothing else is
        // refused for having no region long before its flags are looked at.
        byte[] stream = Page(
            32, 16,
            Segment(number: 1, type: 0, [0x00, 0x01, 0, 0, 0, 0, 0, 0, 0, 0]),
            Segment(number: 2, type: 6, BareTextRegion(16, 8), referred: [1]));

        PdfFilterResult result = Decode(stream);

        Assert.Equal(PdfDiagnosticCodes.FilterJbig2Unsupported, result.DiagnosticCode);
        Assert.Contains("Huffman-coded symbol dictionary", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Text_Region_That_Refines_Its_Symbols_Is_Refused_By_Name()
    {
        byte[] dictionary = Jbig2SymbolDictionaryEncoder.Encode([Box], out _);

        // Region information, then flags with the refinement bit set. The decoder
        // refuses on the flags without reading further, which is why this body
        // needs nothing after them.
        var body = new List<byte>();
        AddUInt32(body, 16);
        AddUInt32(body, 8);
        AddUInt32(body, 0);
        AddUInt32(body, 0);
        body.Add(0);
        AddUInt16(body, 0x02);

        byte[] stream = Page(
            32, 16,
            Segment(number: 1, type: 0, dictionary),
            Segment(number: 2, type: 6, [.. body], referred: [1]));

        PdfFilterResult result = Decode(stream);

        Assert.Equal(PdfDiagnosticCodes.FilterJbig2Unsupported, result.DiagnosticCode);
        Assert.Contains("refines the symbols it places", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Text_Region_With_No_Symbols_Is_Refused_Rather_Than_Drawn_Empty()
    {
        // It refers to no dictionary, so every identifier it codes means nothing.
        // Drawing the blank region it describes would report an empty page as
        // though the original had been blank.
        Jbig2Bitmap[] source = [Box];
        _ = Jbig2SymbolDictionaryEncoder.Encode(source, out Jbig2Bitmap[] order);
        Jbig2Instance[] instances = [new(Id: 0, S: 2, T: 2)];

        byte[] stream = Page(
            32, 16,
            Segment(number: 2, type: 6, Jbig2TextRegionEncoder.Encode(32, 16, order, instances)));

        PdfFilterResult result = Decode(stream);

        Assert.Equal(PdfDiagnosticCodes.FilterJbig2Unsupported, result.DiagnosticCode);
        Assert.Contains("export no symbol", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_Instance_Naming_A_Symbol_That_Does_Not_Exist_Is_Malformed()
    {
        // Three symbols are coded in two bits, so a stream can name a fourth. It
        // is a broken file rather than an unsupported one, and the difference
        // matters to a host deciding whether to report the document or the build.
        Jbig2Bitmap[] source = [Box, Bar, Dot];
        byte[] dictionary = Jbig2SymbolDictionaryEncoder.Encode(source, out Jbig2Bitmap[] order);
        Jbig2Instance[] instances = [new(Id: 3, S: 2, T: 2)];

        byte[] region = Jbig2TextRegionEncoder.Encode(32, 16, [.. order, Box], instances);

        byte[] stream = Page(
            32, 16,
            Segment(number: 1, type: 0, dictionary),
            Segment(number: 2, type: 6, region, referred: [1]));

        PdfFilterResult result = Decode(stream);

        Assert.Equal(PdfDiagnosticCodes.FilterMalformed, result.DiagnosticCode);
        Assert.Contains("do not define", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Height_Class_That_Defines_No_Symbol_Is_Malformed()
    {
        // Not a style rule. The class ends on an out-of-band value and the outer
        // loop ends on the symbol count, so a class that defines nothing leaves
        // both exactly where they were — and past the end of its data the
        // arithmetic decoder repeats one answer forever. Without this refusal the
        // stream below does not decode slowly, it does not return.
        var encoder = new MqEncoder();
        var height = new Jbig2IntegerEncoder();
        var width = new Jbig2IntegerEncoder();
        height.Encode(encoder, 4);
        width.EncodeOutOfBand(encoder);

        var body = new List<byte>();
        AddUInt16(body, 0);
        body.AddRange(new byte[] { 3, 0xFF, 0xFD, 0xFF, 2, 0xFE, 0xFE, 0xFE });
        AddUInt32(body, 1);
        AddUInt32(body, 1);
        body.AddRange(encoder.Flush());

        byte[] stream = Page(
            32, 16,
            Segment(number: 1, type: 0, [.. body]),
            Segment(number: 2, type: 6, BareTextRegion(16, 8), referred: [1]));

        PdfFilterResult result = Decode(stream);

        Assert.Equal(PdfDiagnosticCodes.FilterMalformed, result.DiagnosticCode);
        Assert.Contains("holding no symbol", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_Truncation_Reaches_A_Decision()
    {
        Jbig2Bitmap[] source = [Box, Bar, Dot];
        byte[] dictionary = Jbig2SymbolDictionaryEncoder.Encode(source, out Jbig2Bitmap[] order);
        Jbig2Instance[] instances = [new(Id: 0, S: 2, T: 2), new(Id: 2, S: 14, T: 2)];

        byte[] stream = Page(
            32, 16,
            Segment(number: 1, type: 0, dictionary),
            Segment(number: 2, type: 6, Jbig2TextRegionEncoder.Encode(32, 16, order, instances), referred: [1]));

        for (int length = 0; length <= stream.Length; length++)
        {
            PdfFilterResult result = Decode(stream.AsSpan(0, length).ToArray());
            Assert.True(result.Succeeded || result.DiagnosticCode is not null);
        }
    }

    // ---- the integer procedures -------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(83)]
    [InlineData(84)]
    [InlineData(339)]
    [InlineData(340)]
    [InlineData(4435)]
    [InlineData(4436)]
    [InlineData(70000)]
    [InlineData(-1)]
    [InlineData(-4)]
    [InlineData(-20)]
    [InlineData(-340)]
    [InlineData(-4436)]
    public void An_Integer_Survives_A_Round_Trip(int value)
    {
        // The values on both sides of every boundary in Annex A's decision tree,
        // because the prefix decides both how many bits follow and what to add to
        // them: a range read one too wide still decodes most numbers correctly.
        var encoder = new MqEncoder();
        var writer = new Jbig2IntegerEncoder();
        writer.Encode(encoder, value);
        writer.EncodeOutOfBand(encoder);

        var decoder = new MqDecoder(encoder.Flush());
        var reader = new Jbig2IntegerDecoder();

        Assert.Equal(Jbig2IntegerOutcome.Value, reader.Decode(decoder, out int decoded));
        Assert.Equal(value, decoded);
        Assert.Equal(Jbig2IntegerOutcome.OutOfBand, reader.Decode(decoder, out _));
    }

    [Fact]
    public void A_Sequence_Of_Integers_Keeps_Its_Order()
    {
        int[] values = [0, 5, -5, 900, 1, -1, 4436, 83];

        var encoder = new MqEncoder();
        var writer = new Jbig2IntegerEncoder();
        foreach (int value in values)
            writer.Encode(encoder, value);

        var decoder = new MqDecoder(encoder.Flush());
        var reader = new Jbig2IntegerDecoder();

        foreach (int value in values)
        {
            Assert.Equal(Jbig2IntegerOutcome.Value, reader.Decode(decoder, out int decoded));
            Assert.Equal(value, decoded);
        }
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(256, 8)]
    [InlineData(257, 9)]
    public void A_Symbol_Identifier_Is_As_Wide_As_The_Symbol_Count_Needs(int symbols, int expected) =>
        Assert.Equal(expected, Jbig2TextRegion.CodeLength(symbols));

    [Fact]
    public void Symbol_Identifiers_Survive_A_Round_Trip()
    {
        int[] ids = [0, 5, 3, 7, 1, 0, 6];

        var encoder = new MqEncoder();
        var writer = new Jbig2SymbolIdEncoder(3);
        foreach (int id in ids)
            writer.Encode(encoder, id);

        var decoder = new MqDecoder(encoder.Flush());
        var reader = new Jbig2SymbolIdDecoder(3);

        foreach (int id in ids)
            Assert.Equal(id, reader.Decode(decoder));
    }

    // ---- fixtures ---------------------------------------------------------------

    private static PdfFilterResult Decode(byte[] data, byte[]? globals = null)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (globals is not null)
            values["JBIG2Globals"] = globals;

        return new Jbig2StreamFilter().Decode(data, PdfFilterParameters.From(values), Generous);
    }

    /// <summary>
    /// A text region body that stops after its flags, for the tests where another
    /// segment is the subject and this one only has to exist.
    /// </summary>
    private static byte[] BareTextRegion(int width, int height)
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

    private static Jbig2Bitmap Symbol(params string[] rows)
    {
        int height = rows.Length;
        int width = rows[0].Length;
        var pixels = new byte[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                pixels[(y * width) + x] = rows[y][x] == '#' ? (byte)1 : (byte)0;
        }

        return new Jbig2Bitmap(width, height, pixels);
    }

    /// <summary>
    /// The page the placements describe, drawn from the reference-corner rule as
    /// this file reads T.88 6.4.5 — deliberately a second expression of it rather
    /// than a call into the decoder's.
    /// </summary>
    private static bool[][] Expected(
        int width,
        int height,
        IReadOnlyList<Jbig2Bitmap> symbols,
        IReadOnlyList<Jbig2Instance> instances,
        int corner,
        bool transposed)
    {
        var page = new bool[height][];
        for (int y = 0; y < height; y++)
            page[y] = new bool[width];

        foreach (Jbig2Instance instance in instances)
        {
            Jbig2Bitmap symbol = symbols[instance.Id];
            (int left, int top) = Place(corner, transposed, instance.S, instance.T, symbol.Width, symbol.Height);

            for (int row = 0; row < symbol.Height; row++)
            {
                int y = top + row;
                if (y < 0 || y >= height)
                    continue;

                for (int column = 0; column < symbol.Width; column++)
                {
                    int x = left + column;
                    if (x < 0 || x >= width)
                        continue;

                    if (symbol.Pixels[(row * symbol.Width) + column] != 0)
                        page[y][x] = true;
                }
            }
        }

        return page;
    }

    /// <summary>
    /// Where a symbol's top-left pixel lands, given the corner its coordinates
    /// name. The running S coordinate is the leading edge either way, because the
    /// format advances it to a trailing corner before placing and past it after,
    /// so only the T axis shifts here.
    /// </summary>
    private static (int Left, int Top) Place(int corner, bool transposed, int s, int t, int symbolWidth, int symbolHeight)
    {
        bool bottom = (corner & 1) == 0;    // BOTTOMLEFT is 0 and BOTTOMRIGHT is 2
        bool right = corner >= 2;

        if (!transposed)
            return (s, bottom ? t - symbolHeight + 1 : t);

        return (right ? t - symbolWidth + 1 : t, s);
    }

    private static void AssertSame(bool[][] expected, bool[][] actual)
    {
        for (int y = 0; y < expected.Length; y++)
        {
            for (int x = 0; x < expected[y].Length; x++)
                Assert.True(expected[y][x] == actual[y][x], $"pixel ({x}, {y})");
        }
    }
}
