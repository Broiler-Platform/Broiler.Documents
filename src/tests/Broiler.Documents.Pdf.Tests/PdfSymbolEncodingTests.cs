namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers the Symbol font's built-in encoding, which IP-013 unblocked.
/// </summary>
/// <remarks>
/// This is authored data, so the tests are the review: each assertion states what
/// character a slot denotes, and a wrong entry is a silently wrong character in
/// extracted text rather than an error. The slots deliberately left empty are
/// asserted too — Symbol reserves a run of codes for the pieces large brackets
/// and integral signs are drawn from, and a piece of a glyph is not text.
/// </remarks>
public sealed class PdfSymbolEncodingTests
{
    private static string TextOf(string content, string baseFont = "Symbol")
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject($"<< /Type /Font /Subtype /Type1 /BaseFont /{baseFont} >>");
        int stream = builder.AddStream(string.Empty, content);

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {stream} 0 R >>");

        using var stream2 = new MemoryStream(builder.Build(catalog));
        return new PdfDocumentCodec().ReadPdf(stream2, null).Document.PlainText;
    }

    /// <summary>Shows the given byte codes through /F1.</summary>
    private static string Show(params int[] codes)
    {
        var hex = new System.Text.StringBuilder();
        foreach (int code in codes)
            hex.Append(code.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));

        return $"BT /F1 12 Tf 1 0 0 1 72 720 Tm <{hex}> Tj ET\n";
    }

    // ---- the mapping ----------------------------------------------------------

    [Fact]
    public void Lower_Case_Slots_Are_The_Lower_Case_Greek_Alphabet()
    {
        Assert.Equal("αβγδε", TextOf(Show(0x61, 0x62, 0x67, 0x64, 0x65)));
    }

    [Fact]
    public void Upper_Case_Slots_Are_The_Upper_Case_Greek_Alphabet()
    {
        Assert.Equal("ΑΒΓΔΕ", TextOf(Show(0x41, 0x42, 0x47, 0x44, 0x45)));
    }

    [Fact]
    public void The_Mathematical_Operators_Map()
    {
        // Summation, product, radical, integral, partial differential, infinity.
        Assert.Equal("∑∏√∫∂∞", TextOf(Show(0xE5, 0xD5, 0xD6, 0xF2, 0xB6, 0xA5)));
    }

    [Fact]
    public void The_Set_And_Logic_Operators_Map()
    {
        Assert.Equal("∈∉∩∪⊂⊃∅¬∧∨", TextOf(Show(0xCE, 0xCF, 0xC7, 0xC8, 0xCC, 0xC9, 0xC6, 0xD8, 0xD9, 0xDA)));
    }

    [Fact]
    public void The_Arrows_Map()
    {
        Assert.Equal("←↑→↓↔⇒⇔", TextOf(Show(0xAC, 0xAD, 0xAE, 0xAF, 0xAB, 0xDE, 0xDB)));
    }

    [Fact]
    public void Ascii_Slots_Symbol_Keeps_Are_Unchanged()
    {
        Assert.Equal("0123456789()[]", TextOf(Show(
            0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x28, 0x29, 0x5B, 0x5D)));
    }

    [Fact]
    public void Ascii_Slots_Symbol_Replaces_Are_Replaced()
    {
        // The four that look like punctuation and are not: for all, there exists,
        // such that, and the asterisk operator.
        Assert.Equal("∀∃∋∗", TextOf(Show(0x22, 0x24, 0x27, 0x2A)));
    }

    [Fact]
    public void The_Bullet_Maps()
    {
        // A Symbol bullet is how a great many PDFs draw a list marker, and it was
        // dropped entirely before this encoding existed.
        Assert.Equal("•", TextOf(Show(0xB7)));
    }

    [Fact]
    public void The_Glyph_Assembly_Pieces_Stay_Unmapped()
    {
        // Codes 0xE6-0xF0 and 0xF4 draw the top, middle, and bottom pieces of
        // large brackets and integrals. They are parts of a glyph, not characters,
        // so extracting anything for them would be inventing text.
        Assert.Equal(string.Empty, TextOf(Show(0xE6, 0xE7, 0xE8, 0xF0, 0xF4)).Trim());
    }

    // ---- the boundary the encoding stays inside -------------------------------

    [Fact]
    public void A_Font_That_Is_Merely_Symbolic_Gets_No_Symbol_Table()
    {
        // The encoding belongs to the font named Symbol, not to the symbolic flag.
        // Applying it to any other font would invent Greek letters for arbitrary
        // glyphs, which is the failure this build exists to avoid.
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int descriptor = builder.AddObject("<< /Type /FontDescriptor /FontName /Whatever /Flags 4 >>");
        int font = builder.AddObject(
            $"<< /Type /Font /Subtype /TrueType /BaseFont /Whatever /FontDescriptor {descriptor} 0 R >>");
        int stream = builder.AddStream(string.Empty, Show(0x61, 0x62, 0x63));

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {stream} 0 R >>");

        using var pdf = new MemoryStream(builder.Build(catalog));
        PdfReadResult result = new PdfDocumentCodec().ReadPdf(pdf, null);

        Assert.DoesNotContain("α", result.Document.PlainText, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextMappingMissing);
    }

    [Fact]
    public void A_Subset_Prefixed_Symbol_Still_Resolves()
    {
        Assert.Equal("αβ", TextOf(Show(0x61, 0x62), baseFont: "ABCDEF+Symbol"));
    }

    [Fact]
    public void ZapfDingbats_Extracts_Nothing_Rather_Than_Latin_Letters()
    {
        // Deliberate: its assignments run through private-use slots that only the
        // font's own data resolves, and its glyphs are ornaments rather than text.
        // Before it was recognized it fell through to the Latin fallback and
        // extracted "ab" for two ornaments, which is the more damaging answer.
        Assert.Equal(string.Empty, TextOf(Show(0x61, 0x62), baseFont: "ZapfDingbats").Trim());
    }

    [Fact]
    public void A_Differences_Array_Still_Overrides_The_Built_In_Encoding()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Symbol " +
            "/Encoding << /Differences [97 /A] >> >>");
        int stream = builder.AddStream(string.Empty, Show(0x61, 0x62));

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {stream} 0 R >>");

        using var pdf = new MemoryStream(builder.Build(catalog));

        // The difference wins for 97, and 98 falls through to the font's own
        // built-in encoding rather than to Latin — an /Encoding dictionary that
        // names no BaseEncoding does not replace what the font already knows.
        Assert.StartsWith("Aβ", new PdfDocumentCodec().ReadPdf(pdf, null).Document.PlainText, StringComparison.Ordinal);
    }
}
