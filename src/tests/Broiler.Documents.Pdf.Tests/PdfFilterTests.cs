using System.IO.Compression;
using Broiler.Documents.Pdf.Filters;

namespace Broiler.Documents.Pdf.Tests;

public sealed class PdfFilterTests
{
    private static PdfFilterContext Context(long maxBytes = 1 << 20, int ratio = 512) =>
        new(maxBytes, ratio);

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var compressor = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            compressor.Write(data, 0, data.Length);
        return output.ToArray();
    }

    [Fact]
    public void Flate_Round_Trips_Compressed_Data()
    {
        byte[] original = PdfFileBuilder.Latin1(new string('x', 5000) + "tail");
        PdfFilterResult result = new FlateDecodeFilter().Decode(Deflate(original), PdfFilterParameters.Empty, Context());

        Assert.True(result.Succeeded);
        Assert.Equal(original, result.Data);
    }

    [Fact]
    public void Flate_Stops_A_Decompression_Bomb_At_Its_Ceiling()
    {
        byte[] bomb = Deflate(new byte[4 * 1024 * 1024]);
        PdfFilterResult result = new FlateDecodeFilter().Decode(bomb, PdfFilterParameters.Empty, Context(maxBytes: 64 * 1024));

        Assert.False(result.Succeeded);
        Assert.Equal(PdfDiagnosticCodes.FilterLimit, result.DiagnosticCode);
    }

    [Fact]
    public void Flate_Reports_Malformed_Input_Rather_Than_Throwing()
    {
        PdfFilterResult result = new FlateDecodeFilter().Decode(
            [1, 2, 3, 4, 5, 6, 7, 8],
            PdfFilterParameters.Empty,
            Context());

        Assert.False(result.Succeeded);
        Assert.Equal(PdfDiagnosticCodes.FilterMalformed, result.DiagnosticCode);
    }

    [Fact]
    public void AsciiHex_Decodes_And_Stops_At_The_Terminator()
    {
        PdfFilterResult result = new AsciiHexDecodeFilter().Decode(
            PdfFileBuilder.Latin1("48 65 6C 6C 6F>ignored"),
            PdfFilterParameters.Empty,
            Context());

        Assert.True(result.Succeeded);
        Assert.Equal("Hello", System.Text.Encoding.ASCII.GetString(result.Data!));
    }

    [Fact]
    public void AsciiHex_Rejects_A_Non_Hexadecimal_Byte()
    {
        PdfFilterResult result = new AsciiHexDecodeFilter().Decode(
            PdfFileBuilder.Latin1("48zz"),
            PdfFilterParameters.Empty,
            Context());

        Assert.False(result.Succeeded);
        Assert.Equal(PdfDiagnosticCodes.FilterMalformed, result.DiagnosticCode);
    }

    [Fact]
    public void Ascii85_Decodes_Groups_And_The_Zero_Shorthand()
    {
        // "z" stands for four zero bytes; the trailing group is short by one.
        PdfFilterResult result = new Ascii85DecodeFilter().Decode(
            PdfFileBuilder.Latin1("z87cURD]~>"),
            PdfFilterParameters.Empty,
            Context());

        Assert.True(result.Succeeded);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, result.Data!.Take(4).ToArray());
        Assert.Equal("Hello", System.Text.Encoding.ASCII.GetString(result.Data!, 4, result.Data!.Length - 4));
    }

    [Fact]
    public void Ascii85_Rejects_A_Single_Character_Final_Group()
    {
        // "87cUR" is one full group; "D]" is a valid two-character tail, while a
        // lone "D" cannot encode any whole number of bytes.
        PdfFilterResult valid = new Ascii85DecodeFilter().Decode(
            PdfFileBuilder.Latin1("87cURD]~>"),
            PdfFilterParameters.Empty,
            Context());

        PdfFilterResult shortGroup = new Ascii85DecodeFilter().Decode(
            PdfFileBuilder.Latin1("87cURD~>"),
            PdfFilterParameters.Empty,
            Context());

        Assert.True(valid.Succeeded);
        Assert.False(shortGroup.Succeeded);
        Assert.Equal(PdfDiagnosticCodes.FilterMalformed, shortGroup.DiagnosticCode);
    }

    [Fact]
    public void RunLength_Decodes_Literal_And_Repeat_Runs()
    {
        // 2 -> three literal bytes; 254 -> repeat the next byte three times; 128 ends.
        byte[] encoded = [2, (byte)'a', (byte)'b', (byte)'c', 254, (byte)'z', 128];
        PdfFilterResult result = new RunLengthDecodeFilter().Decode(encoded, PdfFilterParameters.Empty, Context());

        Assert.True(result.Succeeded);
        Assert.Equal("abczzz", System.Text.Encoding.ASCII.GetString(result.Data!));
    }

    [Fact]
    public void RunLength_Rejects_A_Literal_Run_Past_The_End()
    {
        PdfFilterResult result = new RunLengthDecodeFilter().Decode([10, (byte)'a'], PdfFilterParameters.Empty, Context());
        Assert.False(result.Succeeded);
        Assert.Equal(PdfDiagnosticCodes.FilterMalformed, result.DiagnosticCode);
    }

    [Theory]
    [InlineData(0, new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 })]        // None
    [InlineData(1, new byte[] { 1, 1, 1 }, new byte[] { 1, 2, 3 })]        // Sub
    [InlineData(2, new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 })]        // Up over a zero first row
    public void Png_Predictor_Reverses_Each_Row_Filter(int tag, byte[] row, byte[] expected)
    {
        byte[] data = [(byte)tag, .. row];
        Assert.True(PdfPredictor.TryReverse(data, 12, colors: 1, bitsPerComponent: 8, columns: 3, out byte[] result, out string? error));
        Assert.Null(error);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Predictor_Rejects_Out_Of_Range_Parameters_Before_Indexing()
    {
        Assert.False(PdfPredictor.TryReverse([0, 1, 2], 12, colors: 0, bitsPerComponent: 8, columns: 3, out _, out string? error));
        Assert.NotNull(error);

        Assert.False(PdfPredictor.TryReverse([0, 1, 2], 12, colors: 1, bitsPerComponent: 7, columns: 3, out _, out error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Tiff_Predictor_Accumulates_Along_A_Row()
    {
        byte[] data = [1, 1, 1, 5, 1, 1];
        Assert.True(PdfPredictor.TryReverse(data, PdfPredictor.Tiff, colors: 1, bitsPerComponent: 8, columns: 3, out byte[] result, out _));
        Assert.Equal(new byte[] { 1, 2, 3, 5, 6, 7 }, result);
    }
}

public sealed class PdfFilterCompositionTests
{
    [Fact]
    public void The_Base_Composition_Carries_Exactly_The_Filters_This_Repository_Implements()
    {
        PdfCodecServices services = PdfCodecServices.Base;

        Assert.True(services.SupportsFilter(PdfFilterNames.Flate));
        Assert.True(services.SupportsFilter(PdfFilterNames.AsciiHex));
        Assert.True(services.SupportsFilter(PdfFilterNames.Ascii85));
        Assert.True(services.SupportsFilter(PdfFilterNames.RunLength));

        // Everything with an open IP-register row stays out of the base build.
        Assert.False(services.SupportsFilter(PdfFilterNames.Lzw));
        Assert.False(services.SupportsFilter(PdfFilterNames.Dct));
        Assert.False(services.SupportsFilter(PdfFilterNames.CcittFax));
        Assert.False(services.SupportsFilter(PdfFilterNames.Jpx));
        Assert.False(services.SupportsFilter(PdfFilterNames.Jbig2));
    }

    [Fact]
    public void Each_Uncleared_Filter_Has_Its_Own_Stable_Diagnostic()
    {
        Assert.Equal(PdfDiagnosticCodes.FilterLzwUnsupported, PdfFilterNames.UnsupportedDiagnosticFor(PdfFilterNames.Lzw));
        Assert.Equal(PdfDiagnosticCodes.FilterCcittUnsupported, PdfFilterNames.UnsupportedDiagnosticFor(PdfFilterNames.CcittFax));
        Assert.Equal(PdfDiagnosticCodes.FilterJpxUnsupported, PdfFilterNames.UnsupportedDiagnosticFor(PdfFilterNames.Jpx));
        Assert.Equal(PdfDiagnosticCodes.FilterJbig2Unsupported, PdfFilterNames.UnsupportedDiagnosticFor(PdfFilterNames.Jbig2));

        // An abbreviation names the same filter as its long form.
        Assert.Equal(PdfFilterNames.Flate, PdfFilterNames.Canonicalize("Fl"));
        Assert.Equal(PdfFilterNames.Dct, PdfFilterNames.Canonicalize("DCT"));
    }

    [Fact]
    public void A_Composed_Filter_Replaces_The_Built_In_Of_The_Same_Name()
    {
        var services = new PdfCodecServices([new StubFilter(PdfFilterNames.Flate)]);

        int flateCount = services.StreamFilters.Count(filter =>
            PdfFilterNames.Canonicalize(filter.Name) == PdfFilterNames.Flate);

        Assert.Equal(1, flateCount);
        Assert.IsType<StubFilter>(services.StreamFilters.First(filter =>
            PdfFilterNames.Canonicalize(filter.Name) == PdfFilterNames.Flate));
    }

    [Fact]
    public void Adding_A_Reviewed_Filter_Keeps_The_Built_Ins()
    {
        PdfCodecServices services = PdfCodecServices.Base.WithStreamFilters(new StubFilter(PdfFilterNames.Lzw));

        Assert.True(services.SupportsFilter(PdfFilterNames.Lzw));
        Assert.True(services.SupportsFilter(PdfFilterNames.Flate));
    }

    private sealed class StubFilter : IPdfStreamFilter
    {
        public StubFilter(string name) => Name = name;

        public string Name { get; }

        public string? Abbreviation => null;

        public bool ProducesByteStream => true;

        public PdfFilterResult Decode(ReadOnlySpan<byte> input, PdfFilterParameters parameters, PdfFilterContext context) =>
            PdfFilterResult.Success(input.ToArray());
    }
}
