namespace Broiler.Documents.Pdf.Images.Tests;

/// <summary>
/// Covers CCITTFaxDecode: the three coding schemes ITU-T T.4 and T.6 define, and
/// the PDF parameters that are the only place a fax stream's shape is written
/// down.
/// </summary>
/// <remarks>
/// Every stream here is produced by <see cref="CcittFaxEncoder"/>, written in this
/// suite. Nothing is transcribed from the standard's test material, and no fax
/// image is committed: a round trip over generated bitmaps proves more than one
/// blessed sample would, and it exercises the two-dimensional modes that a fixed
/// fixture would only reach by accident.
/// </remarks>
public sealed class CcittFaxTests
{
    private static readonly PdfFilterContext Generous = new(16L * 1024 * 1024, 65536);

    // ---- the three coding schemes ---------------------------------------------

    [Theory]
    [InlineData(0)]     // Modified Huffman: every line one-dimensional
    [InlineData(-1)]    // Modified Modified READ: every line two-dimensional
    [InlineData(4)]     // Modified READ: lines tagged one- or two-dimensional
    public void A_Bitmap_Survives_A_Round_Trip(int k)
    {
        bool[][] original = Pattern(64, 24);

        bool[][] decoded = RoundTrip(original, k);

        AssertSame(original, decoded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_Wide_Bitmap_Crosses_The_Makeup_Codes(int k)
    {
        // Runs past 63 need a makeup code before the terminating one, and runs
        // past 1728 need the extended makeup shared by both colours. A full
        // fax-width blank line reaches both.
        bool[][] original = Pattern(2000, 4);

        AssertSame(original, RoundTrip(original, k));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_All_White_Bitmap_Survives(int k)
    {
        bool[][] original = Uniform(80, 6, black: false);

        AssertSame(original, RoundTrip(original, k));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_All_Black_Bitmap_Survives(int k)
    {
        bool[][] original = Uniform(80, 6, black: true);

        AssertSame(original, RoundTrip(original, k));
    }

    [Fact]
    public void Two_Dimensional_Coding_Uses_Every_Mode_And_Still_Round_Trips()
    {
        // Rows that repeat exactly take vertical modes, rows that shift by one or
        // two take the near verticals, a row whose run vanishes takes a pass, and
        // an unrelated row falls back to horizontal. This bitmap has all four.
        bool[][] original =
        [
            Row(64, (10, 20), (40, 50)),
            Row(64, (10, 20), (40, 50)),
            Row(64, (11, 21), (41, 51)),
            Row(64, (13, 23)),
            Row(64, (2, 60)),
            Row(64, (2, 60)),
        ];

        AssertSame(original, RoundTrip(original, k: -1));
    }

    // ---- the parameters -------------------------------------------------------

    [Fact]
    public void BlackIs1_Chooses_Which_Bit_Value_Means_Black()
    {
        bool[][] original = Pattern(32, 4);

        byte[] whenFalse = Decode(CcittFaxEncoder.Encode(original, k: 0), 32, k: 0, blackIs1: false).Data!;
        byte[] whenTrue = Decode(CcittFaxEncoder.Encode(original, k: 0), 32, k: 0, blackIs1: true).Data!;

        // The same image, and every bit the other way round.
        Assert.Equal(whenFalse.Length, whenTrue.Length);
        for (int i = 0; i < whenFalse.Length; i++)
            Assert.Equal((byte)~whenFalse[i], whenTrue[i]);
    }

    [Fact]
    public void A_Declared_Row_Count_Stops_The_Decode()
    {
        bool[][] original = Pattern(32, 10);
        byte[] encoded = CcittFaxEncoder.Encode(original, k: 0);

        PdfFilterResult result = Decode(encoded, 32, k: 0, rows: 3);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(3 * 4, result.Data!.Length);
    }

    [Fact]
    public void Byte_Aligned_Lines_Round_Trip_When_They_Are_Declared()
    {
        bool[][] original = Pattern(48, 8);
        byte[] encoded = CcittFaxEncoder.Encode(original, k: 0, byteAlign: true);

        PdfFilterResult result = Decode(encoded, 48, k: 0, byteAlign: true);

        Assert.True(result.Succeeded, result.Message);
        AssertSame(original, Unpack(result.Data!, 48, original.Length, blackIs1: false));
    }

    // ---- bounds and malformed input -------------------------------------------

    [Fact]
    public void An_Empty_Stream_Is_Malformed()
    {
        Assert.Equal(PdfDiagnosticCodes.FilterMalformed, Decode([], 32, k: 0).DiagnosticCode);
    }

    [Fact]
    public void A_Stream_Of_Rubbish_Reaches_A_Decision()
    {
        var noise = new byte[256];
        for (int i = 0; i < noise.Length; i++)
            noise[i] = (byte)(i * 31);

        PdfFilterResult result = Decode(noise, 1728, k: 0);

        Assert.True(result.Succeeded || result.DiagnosticCode is not null);
    }

    [Fact]
    public void An_Image_Past_Its_Ceiling_Is_Refused()
    {
        byte[] encoded = CcittFaxEncoder.Encode(Uniform(1728, 400, black: false), k: 0);

        PdfFilterResult result = new CcittFaxStreamFilter().Decode(
            encoded,
            Parameters(1728, k: 0),
            new PdfFilterContext(1024, 4));

        Assert.Equal(PdfDiagnosticCodes.FilterLimit, result.DiagnosticCode);
    }

    [Fact]
    public void A_Column_Count_Outside_The_Supported_Range_Is_Malformed()
    {
        Assert.Equal(
            PdfDiagnosticCodes.FilterMalformed,
            Decode(CcittFaxEncoder.Encode(Pattern(32, 2), k: 0), columns: 0, k: 0).DiagnosticCode);
    }

    [Fact]
    public void Every_Truncation_Reaches_A_Decision()
    {
        byte[] encoded = CcittFaxEncoder.Encode(Pattern(64, 12), k: 0);

        for (int length = 0; length <= encoded.Length; length += 5)
        {
            PdfFilterResult result = Decode(encoded.AsSpan(0, length).ToArray(), 64, k: 0);
            Assert.True(result.Succeeded || result.DiagnosticCode is not null);
        }
    }

    [Fact]
    public void The_Filter_Names_Itself_As_An_Image_Filter()
    {
        var filter = new CcittFaxStreamFilter();

        Assert.Equal("CCITTFaxDecode", filter.Name);
        Assert.Equal("CCF", filter.Abbreviation);
        Assert.False(filter.ProducesByteStream);
    }

    // ---- through the codec ----------------------------------------------------

    [Fact]
    public void Without_The_Filter_A_Fax_Image_Reports_Its_Own_Row()
    {
        PdfReadResult result = ReadDocument(composed: false);

        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.FilterCcittUnsupported);
    }

    [Fact]
    public void With_The_Filter_The_Same_Image_Is_Decoded()
    {
        PdfReadResult result = ReadDocument(composed: true);

        // What the decode is for: the samples reach the document rather than
        // stopping at the filter pipeline.
        Assert.Single(ImagesIn(result));
        Assert.DoesNotContain(
            result.Diagnostics,
            d => d.Code == PdfDiagnosticCodes.ImageDecodedNotProjected);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.FilterCcittUnsupported);
    }

    private static PdfReadResult ReadDocument(bool composed)
    {
        byte[] fax = CcittFaxEncoder.Encode(Pattern(64, 16), k: -1);
        byte[] pdf = PdfComposedImageTests.DocumentWithImage(
            "/Type /XObject /Subtype /Image /Width 64 /Height 16 /ColorSpace /DeviceGray /BitsPerComponent 1 " +
            "/Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 64 /Rows 16 >>",
            fax);

        PdfCodecServices services = composed
            ? PdfCodecServices.Base.WithStreamFilters(new CcittFaxStreamFilter())
            : PdfCodecServices.Base;

        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec(services).ReadPdf(stream, null);
    }

    // ---- fixtures -------------------------------------------------------------

    private static PdfFilterResult Decode(
        byte[] encoded,
        int columns,
        int k,
        int rows = 0,
        bool blackIs1 = false,
        bool byteAlign = false) =>
        new CcittFaxStreamFilter().Decode(encoded, Parameters(columns, k, rows, blackIs1, byteAlign), Generous);

    private static PdfFilterParameters Parameters(
        int columns,
        int k,
        int rows = 0,
        bool blackIs1 = false,
        bool byteAlign = false) =>
        PdfFilterParameters.From(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["K"] = (long)k,
            ["Columns"] = (long)columns,
            ["Rows"] = (long)rows,
            ["BlackIs1"] = blackIs1,
            ["EncodedByteAlign"] = byteAlign,
        });

    private static bool[][] RoundTrip(bool[][] original, int k)
    {
        int columns = original[0].Length;
        PdfFilterResult result = Decode(CcittFaxEncoder.Encode(original, k), columns, k);

        Assert.True(result.Succeeded, result.Message);
        return Unpack(result.Data!, columns, original.Length, blackIs1: false);
    }

    private static void AssertSame(bool[][] expected, bool[][] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int y = 0; y < expected.Length; y++)
        {
            for (int x = 0; x < expected[y].Length; x++)
                Assert.True(expected[y][x] == actual[y][x], $"pixel ({x}, {y})");
        }
    }

    private static bool[][] Unpack(byte[] packed, int columns, int rows, bool blackIs1)
    {
        int stride = (columns + 7) / 8;
        var image = new bool[rows][];

        for (int y = 0; y < rows; y++)
        {
            image[y] = new bool[columns];
            for (int x = 0; x < columns; x++)
            {
                int bit = (packed[(y * stride) + (x >> 3)] >> (7 - (x & 7))) & 1;
                image[y][x] = blackIs1 ? bit == 1 : bit == 0;
            }
        }

        return image;
    }

    /// <summary>A bitmap with runs of varying length in both colours.</summary>
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

    /// <summary>A row that is white except for the half-open black spans given.</summary>
    private static bool[] Row(int columns, params (int Start, int End)[] black)
    {
        var row = new bool[columns];
        foreach ((int start, int end) in black)
        {
            for (int x = start; x < end && x < columns; x++)
                row[x] = true;
        }

        return row;
    }

    /// <summary>
    /// The images a read carried into the document, in reading order.
    /// </summary>
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
}
