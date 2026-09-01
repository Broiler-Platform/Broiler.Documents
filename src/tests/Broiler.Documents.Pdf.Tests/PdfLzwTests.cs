using System.Text;
using Broiler.Documents.Pdf.Filters;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers LZWDecode, built in since IP-010 cleared.
/// </summary>
/// <remarks>
/// Every stream here is encoded by <see cref="LzwEncoder"/>, written in this
/// suite for the purpose. Nothing is transcribed from a specification's worked
/// example: a normative table is exactly the sort of material the approved-source
/// rule keeps out of the repository, and an encoder proves the round trip over
/// arbitrary data rather than over one blessed sample.
/// </remarks>
public sealed class PdfLzwTests
{
    private static readonly PdfFilterContext Generous = new(16L * 1024 * 1024, 4096);

    private static PdfFilterResult Decode(byte[] encoded, PdfFilterContext? context = null) =>
        new LzwDecodeFilter().Decode(encoded, PdfFilterParameters.Empty, context ?? Generous);

    // ---- round trips ----------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("AB")]
    [InlineData("AAAAAAAAAAAAAAAAAAAA")]
    [InlineData("the quick brown fox jumps over the lazy dog")]
    [InlineData("abababababababababababababababababababababab")]
    public void Text_Survives_A_Round_Trip(string text)
    {
        byte[] original = Encoding.ASCII.GetBytes(text);

        PdfFilterResult result = Decode(LzwEncoder.Encode(original));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(original, result.Data);
    }

    [Fact]
    public void A_Stream_Long_Enough_To_Grow_The_Code_Width_Survives()
    {
        // Past 511 entries the codes widen to ten bits, then eleven, then twelve.
        // Getting the width transition wrong does not fail, it silently decodes to
        // different bytes, so the assertion is the whole content.
        byte[] original = Varied(40_000);

        PdfFilterResult result = Decode(LzwEncoder.Encode(original));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(original, result.Data);
    }

    [Fact]
    public void A_Stream_Long_Enough_To_Fill_And_Reset_The_Table_Survives()
    {
        // Past 4096 entries the encoder emits a clear code and starts again.
        byte[] original = Varied(400_000);

        PdfFilterResult result = Decode(LzwEncoder.Encode(original));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(original, result.Data);
    }

    [Fact]
    public void The_Case_Where_A_Code_Is_Used_As_It_Is_Defined_Survives()
    {
        // The KwKwK case: an encoder reaches a string it has only just added, so
        // the decoder must build it from the previous string rather than look it
        // up. A run of one repeated byte produces it immediately.
        byte[] original = Encoding.ASCII.GetBytes(new string('x', 64));

        PdfFilterResult result = Decode(LzwEncoder.Encode(original));

        Assert.Equal(original, result.Data);
    }

    // ---- EarlyChange ----------------------------------------------------------

    [Fact]
    public void An_EarlyChange_Of_Zero_Round_Trips_When_It_Is_Declared()
    {
        byte[] original = Varied(40_000);
        byte[] encoded = LzwEncoder.Encode(original, earlyChange: 0);

        PdfFilterResult result = new LzwDecodeFilter().Decode(encoded, Parameters(0), Generous);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(original, result.Data);
    }

    [Fact]
    public void An_EarlyChange_Of_Zero_Read_As_The_Default_Does_Not_Decode_To_The_Same_Bytes()
    {
        // The parameter is not decorative. Read under the wrong one, the same
        // stream yields different bytes rather than an error, which is why the
        // filter honours it rather than assuming the common case.
        byte[] original = Varied(40_000);
        byte[] encoded = LzwEncoder.Encode(original, earlyChange: 0);

        PdfFilterResult result = Decode(encoded);

        Assert.NotEqual(original, result.Data ?? []);
    }

    // ---- bounds and malformed input -------------------------------------------

    [Fact]
    public void An_Empty_Stream_Decodes_To_Nothing()
    {
        PdfFilterResult result = Decode([]);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public void A_Stream_That_Expands_Past_Its_Ceiling_Is_Refused()
    {
        byte[] encoded = LzwEncoder.Encode(Encoding.ASCII.GetBytes(new string('z', 200_000)));

        PdfFilterResult result = new LzwDecodeFilter().Decode(
            encoded, PdfFilterParameters.Empty, new PdfFilterContext(4096, 512));

        Assert.Equal(PdfDiagnosticCodes.FilterLimit, result.DiagnosticCode);
    }

    [Fact]
    public void A_Code_That_Is_Not_In_The_Table_Is_Malformed()
    {
        // Nine-bit 256 (clear), then nine-bit 300 — a code the table cannot hold
        // when nothing has been added to it yet.
        PdfFilterResult result = Decode([0x80, 0x4B, 0x00]);

        Assert.Equal(PdfDiagnosticCodes.FilterMalformed, result.DiagnosticCode);
    }

    [Fact]
    public void A_Truncated_Stream_Returns_What_It_Decoded()
    {
        byte[] encoded = LzwEncoder.Encode(Encoding.ASCII.GetBytes("the quick brown fox"));

        // A producer that omits the end-of-data code is common enough that
        // refusing the stream would lose readable documents.
        PdfFilterResult result = Decode(encoded.AsSpan(0, encoded.Length - 2).ToArray());

        Assert.True(result.Succeeded, result.Message);
        Assert.StartsWith("the quick", Encoding.ASCII.GetString(result.Data!), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_Truncation_Reaches_A_Decision()
    {
        byte[] encoded = LzwEncoder.Encode(Varied(4000));

        for (int length = 0; length < encoded.Length; length += 7)
        {
            PdfFilterResult result = Decode(encoded.AsSpan(0, length).ToArray());
            Assert.True(result.Succeeded || result.DiagnosticCode is not null);
        }
    }

    // ---- through the codec ----------------------------------------------------

    [Fact]
    public void A_Content_Stream_Compressed_With_Lzw_Is_Read()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int content = builder.AddStream(
            string.Empty,
            LzwEncoder.Encode(PdfFileBuilder.Latin1(PdfFileBuilder.ShowText("Compressed with LZW"))),
            filter: "LZWDecode");

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");

        using var stream = new MemoryStream(builder.Build(catalog));
        PdfReadResult result = new PdfDocumentCodec().ReadPdf(stream, null);

        Assert.Contains("Compressed with LZW", result.Document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.FilterLzwUnsupported);
    }

    // ---- fixtures -------------------------------------------------------------

    private static PdfFilterParameters Parameters(int earlyChange) =>
        new(new Dictionary<string, object?>(StringComparer.Ordinal) { ["EarlyChange"] = (long)earlyChange });

    /// <summary>
    /// Data with enough structure to compress and enough variety to keep filling
    /// the table, so a long run of it crosses every code-width boundary.
    /// </summary>
    private static byte[] Varied(int length)
    {
        var bytes = new byte[length];
        for (int i = 0; i < length; i++)
            bytes[i] = (byte)((i * 7) ^ (i >> 5));
        return bytes;
    }
}

/// <summary>
/// The LZW encoder these tests compress with. Test-only: the codec reads PDFs and
/// has no reason to write an LZW stream.
/// </summary>
/// <remarks>
/// It mirrors the decoder's code-width rule from the other side. The encoder's
/// table always holds exactly one more entry than the decoder's at the same point
/// in the stream, which is the whole reason <c>EarlyChange</c> exists, so the
/// encoder widens one entry later than the decoder does.
/// </remarks>
internal sealed class LzwEncoder
{
    private const int ClearCode = 256;
    private const int EndOfDataCode = 257;
    private const int FirstAssignedCode = 258;
    private const int MaxCodes = 4096;

    private readonly List<byte> _output = [];
    private readonly Dictionary<(int Prefix, byte Suffix), int> _table = [];
    private readonly int _earlyChange;

    private int _bitBuffer;
    private int _bitCount;
    private int _codeWidth = 9;
    private int _next = FirstAssignedCode;

    private LzwEncoder(int earlyChange) => _earlyChange = earlyChange;

    public static byte[] Encode(ReadOnlySpan<byte> data, int earlyChange = 1)
    {
        var encoder = new LzwEncoder(earlyChange);
        encoder.Run(data);
        return encoder._output.ToArray();
    }

    private void Run(ReadOnlySpan<byte> data)
    {
        Write(ClearCode);

        if (data.Length == 0)
        {
            Write(EndOfDataCode);
            Flush();
            return;
        }

        int current = data[0];
        for (int i = 1; i < data.Length; i++)
        {
            byte b = data[i];
            if (_table.TryGetValue((current, b), out int combined))
            {
                current = combined;
                continue;
            }

            Write(current);

            if (_next < MaxCodes)
            {
                _table[(current, b)] = _next++;
                if (_codeWidth < 12 && _next - 1 + _earlyChange >= 1 << _codeWidth)
                    _codeWidth++;
            }
            else
            {
                Write(ClearCode);
                _table.Clear();
                _next = FirstAssignedCode;
                _codeWidth = 9;
            }

            current = b;
        }

        Write(current);
        Write(EndOfDataCode);
        Flush();
    }

    private void Write(int code)
    {
        _bitBuffer = (_bitBuffer << _codeWidth) | code;
        _bitCount += _codeWidth;

        while (_bitCount >= 8)
        {
            _bitCount -= 8;
            _output.Add((byte)(_bitBuffer >> _bitCount));
        }

        _bitBuffer &= (1 << _bitCount) - 1;
    }

    private void Flush()
    {
        if (_bitCount > 0)
            _output.Add((byte)(_bitBuffer << (8 - _bitCount)));
    }
}
