using static Broiler.Documents.Pdf.Images.Tests.Jbig2Streams;

namespace Broiler.Documents.Pdf.Images.Tests;

/// <summary>
/// Covers refinement: the correction a JBIG2 stream applies to a bitmap it
/// already has, in each of the three places the format allows one.
/// </summary>
/// <remarks>
/// <para>
/// Refinement is what makes symbol substitution safe to use on text. Coding one
/// dictionary shape for every occurrence of a character is a lie in the small,
/// and refinement is where the encoder takes it back — for a few bits per changed
/// pixel, this instance can differ from its dictionary entry, or the page itself
/// can be corrected after the fact.
/// </para>
/// <para>
/// <strong>What these prove.</strong> The same as everything else here: that the
/// encoder in this suite and the decoder in the product implement one reading of
/// T.88, not that the reading is right. No conforming JBIG2 file may be committed
/// (IP-020) and the standard's test sequences are official test material, so a
/// shared misreading of the refinement templates passes every test below. The
/// templates are written out coordinate by coordinate in the decoder for a
/// reviewer to check by eye, which is the only other check available.
/// </para>
/// <para>
/// What the round trip does exercise, and what a decoder could plausibly get
/// wrong on its own: the anchoring of the reference under the bitmap being
/// decoded, the typical-prediction rule that skips settled pixels, the adaptive
/// pixels, and — the one with a visible consequence — the difference between
/// compositing a correction with OR and with REPLACE.
/// </para>
/// </remarks>
public sealed class Jbig2RefinementTests
{
    private static readonly PdfFilterContext Generous = new(16L * 1024 * 1024, 65536);

    private static Jbig2Bitmap Glyph => Symbol(
        "..####..",
        ".#....#.",
        "#......#",
        "#......#",
        "#......#",
        ".#....#.",
        "..####..");

    /// <summary>The same shape with a broken edge, which is what a scan produces.</summary>
    private static Jbig2Bitmap Broken => Symbol(
        "..####..",
        ".#....#.",
        "#......#",
        "#.......",
        "#......#",
        ".#....#.",
        "..##.#..");

    // ---- refinement regions -----------------------------------------------------

    [Fact]
    public void A_Refinement_Region_Corrects_The_Page_Under_It()
    {
        // The page holds a generic region; the refinement segment that follows it
        // replaces that rectangle with a corrected version. Both the added and the
        // removed pixels matter, which is the point of the fixture pair.
        byte[] stream = Page(
            16, 8,
            GenericRegion(Glyph, number: 1),
            Segment(number: 2, type: 42, Jbig2RefinementEncoder.RegionSegment(Broken, Glyph)));

        PdfFilterResult result = Decode(stream);

        Assert.True(result.Succeeded, result.Message);
        AssertSame(Broken, Unpack(result.Data!, 16, 8), 0, 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Both_Refinement_Templates_Survive_A_Round_Trip(int template)
    {
        byte[] stream = Page(
            16, 8,
            GenericRegion(Glyph, number: 1),
            Segment(
                number: 2,
                type: 42,
                Jbig2RefinementEncoder.RegionSegment(Broken, Glyph, template: template)));

        PdfFilterResult result = Decode(stream);

        Assert.True(result.Succeeded, result.Message);
        AssertSame(Broken, Unpack(result.Data!, 16, 8), 0, 0);
    }

    [Fact]
    public void Typical_Prediction_Skips_The_Rows_The_Reference_Settles()
    {
        // Most of a refined bitmap is unchanged, and TPGRON is how the format
        // says so: a row whose settled pixels already agree with the reference
        // costs one bit. A decoder that read the flag and then decoded the row
        // anyway would take the following bits as pixels and produce noise.
        byte[] stream = Page(
            16, 8,
            GenericRegion(Glyph, number: 1),
            Segment(
                number: 2,
                type: 42,
                Jbig2RefinementEncoder.RegionSegment(Broken, Glyph, typicalPrediction: true)));

        PdfFilterResult result = Decode(stream);

        Assert.True(result.Succeeded, result.Message);
        AssertSame(Broken, Unpack(result.Data!, 16, 8), 0, 0);
    }

    [Fact]
    public void A_Moved_Adaptive_Pixel_Is_Looked_Up_Where_The_Header_Says()
    {
        (int X, int Y)[] adaptive = [(-2, -1), (1, -1)];

        byte[] stream = Page(
            16, 8,
            GenericRegion(Glyph, number: 1),
            Segment(
                number: 2,
                type: 42,
                Jbig2RefinementEncoder.RegionSegment(Broken, Glyph, adaptive: adaptive)));

        PdfFilterResult result = Decode(stream);

        Assert.True(result.Succeeded, result.Message);
        AssertSame(Broken, Unpack(result.Data!, 16, 8), 0, 0);
    }

    [Fact]
    public void Or_Cannot_Take_A_Pixel_Back_And_Replace_Can()
    {
        // The same correction under the two operators. It is the clearest
        // statement of why the compositing rule is not a formality: OR only ever
        // adds black, so a refinement that clears a pixel needs REPLACE to say so,
        // and a decoder treating them alike silently keeps the uncorrected page.
        byte[] replacing = Page(
            16, 8,
            GenericRegion(Glyph, number: 1),
            Segment(number: 2, type: 42, Jbig2RefinementEncoder.RegionSegment(Broken, Glyph, combination: 4)));

        byte[] merging = Page(
            16, 8,
            GenericRegion(Glyph, number: 1),
            Segment(number: 2, type: 42, Jbig2RefinementEncoder.RegionSegment(Broken, Glyph, combination: 0)));

        bool[][] replaced = Unpack(Decode(replacing).Data!, 16, 8);
        bool[][] merged = Unpack(Decode(merging).Data!, 16, 8);

        // Row 3 loses its right-hand stem in the correction.
        Assert.False(replaced[3][7]);
        Assert.True(merged[3][7]);
    }

    [Fact]
    public void An_Intermediate_Refinement_Region_Is_Refused_By_Name()
    {
        // Type 40 is held in an auxiliary buffer for another segment to refer to
        // rather than drawn, and the buffers are not built.
        byte[] stream = Page(
            16, 8,
            GenericRegion(Glyph, number: 1),
            Segment(number: 2, type: 40, Jbig2RefinementEncoder.RegionSegment(Broken, Glyph)));

        PdfFilterResult result = Decode(stream);

        Assert.Equal(PdfDiagnosticCodes.FilterJbig2Unsupported, result.DiagnosticCode);
        Assert.Contains("refinement region", result.Message, StringComparison.Ordinal);
        Assert.Contains("whose decoder is not written", result.Message, StringComparison.Ordinal);
    }

    // ---- refinement inside a text region ----------------------------------------

    [Fact]
    public void A_Text_Region_Draws_A_Corrected_Instance()
    {
        // Two instances of one dictionary symbol, the second corrected. This is
        // the shape a lossless scanned page actually has, and the reason two
        // occurrences of the same character need not be identical.
        byte[] dictionary = Jbig2SymbolDictionaryEncoder.Encode([Glyph], out Jbig2Bitmap[] order);

        Jbig2Instance[] instances =
        [
            new(Id: 0, S: 1, T: 1),
            new(Id: 0, S: 12, T: 1, Refine: new Jbig2InstanceRefinement(Broken)),
        ];

        byte[] stream = Page(
            24, 10,
            Segment(number: 1, type: 0, dictionary),
            Segment(number: 2, type: 6, Jbig2TextRegionEncoder.Encode(24, 10, order, instances), referred: [1]));

        PdfFilterResult result = Decode(stream);

        Assert.True(result.Succeeded, result.Message);
        bool[][] page = Unpack(result.Data!, 24, 10);

        AssertSame(Glyph, page, 1, 1);
        AssertSame(Broken, page, 12, 1);
    }

    [Fact]
    public void A_Refined_Instance_May_Change_Size()
    {
        // The correction states its size as a difference, and the reference is
        // anchored half of that difference away, so a symbol growing by two grows
        // by one on each side. Both halves compute that anchor the same way, so
        // this does not check the formula — what it does exercise is the decoder
        // reading a reference that does not start at the origin, where every
        // neighbourhood lookup has to be offset and clipped.
        Jbig2Bitmap larger = Symbol(
            "..######..",
            ".#......#.",
            "#........#",
            "#........#",
            "#........#",
            "#........#",
            "#........#",
            ".#......#.",
            "..######..");

        byte[] dictionary = Jbig2SymbolDictionaryEncoder.Encode([Glyph], out Jbig2Bitmap[] order);
        Jbig2Instance[] instances = [new(Id: 0, S: 2, T: 1, Refine: new Jbig2InstanceRefinement(larger))];

        byte[] stream = Page(
            16, 12,
            Segment(number: 1, type: 0, dictionary),
            Segment(number: 2, type: 6, Jbig2TextRegionEncoder.Encode(16, 12, order, instances), referred: [1]));

        PdfFilterResult result = Decode(stream);

        Assert.True(result.Succeeded, result.Message);
        AssertSame(larger, Unpack(result.Data!, 16, 12), 2, 1);
    }

    [Fact]
    public void A_Region_That_Declares_Refinement_May_Refine_Nothing()
    {
        // The flag is per region and the decision is per instance, so a region can
        // declare refinement and then decline it for every symbol. The bit still
        // has to be read, or everything after it is misaligned.
        byte[] dictionary = Jbig2SymbolDictionaryEncoder.Encode([Glyph], out Jbig2Bitmap[] order);
        Jbig2Instance[] instances = [new(Id: 0, S: 1, T: 1), new(Id: 0, S: 12, T: 1)];

        byte[] stream = Page(
            24, 10,
            Segment(number: 1, type: 0, dictionary),
            Segment(
                number: 2,
                type: 6,
                Jbig2TextRegionEncoder.Encode(24, 10, order, instances, refine: true),
                referred: [1]));

        PdfFilterResult result = Decode(stream);

        Assert.True(result.Succeeded, result.Message);
        bool[][] page = Unpack(result.Data!, 24, 10);

        AssertSame(Glyph, page, 1, 1);
        AssertSame(Glyph, page, 12, 1);
    }

    // ---- refinement inside a symbol dictionary ----------------------------------

    [Fact]
    public void A_Dictionary_Defines_A_Symbol_As_A_Correction_Of_Another()
    {
        // The second dictionary refines the first's symbol rather than coding a
        // new shape, which is what an encoder does for a character that recurs
        // slightly changed. Its export flags have to skip the symbol it was given
        // and export only what it defined.
        byte[] first = Jbig2SymbolDictionaryEncoder.Encode([Glyph], out Jbig2Bitmap[] given);

        byte[] second = Jbig2SymbolDictionaryEncoder.EncodeRefining(
            given, [new Jbig2RefinedSymbol(Broken, ReferenceId: 0)], out Jbig2Bitmap[] defined);

        Jbig2Instance[] instances = [new(Id: 0, S: 2, T: 1)];

        byte[] stream = Page(
            16, 10,
            Segment(number: 1, type: 0, first),
            Segment(number: 2, type: 0, second, referred: [1]),
            Segment(
                number: 3,
                type: 6,
                Jbig2TextRegionEncoder.Encode(16, 10, defined, instances),
                referred: [2]));

        PdfFilterResult result = Decode(stream);

        Assert.True(result.Succeeded, result.Message);
        AssertSame(Broken, Unpack(result.Data!, 16, 10), 2, 1);
    }

    [Fact]
    public void An_Aggregated_Symbol_Is_Refused_By_Name()
    {
        // A symbol built from several instances is a text region nested inside a
        // dictionary — a second decoder rather than a variation on this one.
        var encoder = new MqEncoder();
        var height = new Jbig2IntegerEncoder();
        var width = new Jbig2IntegerEncoder();
        var instances = new Jbig2IntegerEncoder();

        height.Encode(encoder, 4);
        width.Encode(encoder, 4);
        instances.Encode(encoder, 2);

        var body = new List<byte>();
        AddUInt16(body, 0x02);                                          // SDREFAGG
        body.AddRange(new byte[] { 3, 0xFF, 0xFD, 0xFF, 2, 0xFE, 0xFE, 0xFE });
        body.AddRange(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });           // refinement pixels
        AddUInt32(body, 1);
        AddUInt32(body, 1);
        body.AddRange(encoder.Flush());

        byte[] stream = Page(
            16, 8,
            Segment(number: 1, type: 0, [.. body]),
            Segment(number: 2, type: 6, BareTextRegion(), referred: [1]));

        PdfFilterResult result = Decode(stream);

        Assert.Equal(PdfDiagnosticCodes.FilterJbig2Unsupported, result.DiagnosticCode);
        Assert.Contains("aggregates several instances", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Dictionary_Refining_A_Symbol_It_Has_Not_Defined_Is_Malformed()
    {
        // The identifier names a symbol out of the list the dictionary will hold,
        // most of which does not exist yet when the first symbol is decoded.
        var encoder = new MqEncoder();
        var height = new Jbig2IntegerEncoder();
        var width = new Jbig2IntegerEncoder();
        var instances = new Jbig2IntegerEncoder();
        var identifiers = new Jbig2SymbolIdEncoder(Jbig2TextRegion.CodeLength(2));

        height.Encode(encoder, 4);
        width.Encode(encoder, 4);
        instances.Encode(encoder, 1);
        identifiers.Encode(encoder, 1);

        var body = new List<byte>();
        AddUInt16(body, 0x02);
        body.AddRange(new byte[] { 3, 0xFF, 0xFD, 0xFF, 2, 0xFE, 0xFE, 0xFE });
        body.AddRange(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
        AddUInt32(body, 2);
        AddUInt32(body, 2);
        body.AddRange(encoder.Flush());

        byte[] stream = Page(
            16, 8,
            Segment(number: 1, type: 0, [.. body]),
            Segment(number: 2, type: 6, BareTextRegion(), referred: [1]));

        PdfFilterResult result = Decode(stream);

        Assert.Equal(PdfDiagnosticCodes.FilterMalformed, result.DiagnosticCode);
        Assert.Contains("has not defined yet", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_Truncation_Reaches_A_Decision()
    {
        byte[] stream = Page(
            16, 8,
            GenericRegion(Glyph, number: 1),
            Segment(number: 2, type: 42, Jbig2RefinementEncoder.RegionSegment(Broken, Glyph)));

        for (int length = 0; length <= stream.Length; length++)
        {
            PdfFilterResult result = Decode(stream.AsSpan(0, length).ToArray());
            Assert.True(result.Succeeded || result.DiagnosticCode is not null);
        }
    }

    // ---- fixtures ---------------------------------------------------------------

    private static PdfFilterResult Decode(byte[] data)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        return new Jbig2StreamFilter().Decode(data, PdfFilterParameters.From(values), Generous);
    }

    /// <summary>An arithmetic generic region carrying one bitmap, as the page's own content.</summary>
    private static byte[] GenericRegion(Jbig2Bitmap bitmap, uint number, int x = 0, int y = 0)
    {
        var body = new List<byte>();
        AddUInt32(body, bitmap.Width);
        AddUInt32(body, bitmap.Height);
        AddUInt32(body, x);
        AddUInt32(body, y);
        body.Add(0);                    // combination operator: OR
        body.Add(0);                    // arithmetic, template 0
        body.AddRange(new byte[] { 3, 0xFF, 0xFD, 0xFF, 2, 0xFE, 0xFE, 0xFE });
        body.AddRange(Jbig2GenericEncoder.Encode(bitmap.Pixels, bitmap.Width, bitmap.Height, template: 0));

        return Segment(number, type: 38, [.. body]);
    }

    /// <summary>A text region body that stops after its flags, where another segment is the subject.</summary>
    private static byte[] BareTextRegion()
    {
        var body = new List<byte>();
        AddUInt32(body, 8);
        AddUInt32(body, 4);
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

    private static void AssertSame(Jbig2Bitmap expected, bool[][] page, int originX, int originY)
    {
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                bool black = expected.Pixels[(y * expected.Width) + x] != 0;
                Assert.True(black == page[originY + y][originX + x], $"pixel ({x}, {y})");
            }
        }
    }
}
