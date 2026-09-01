namespace Broiler.Documents.Pdf.Fonts.Tests;

/// <summary>
/// Covers the composed half of the font boundary: what a read recovers when a
/// caller puts <see cref="GraphicsFontProgramReader"/> into the service graph,
/// and what it still reports when they do not.
/// </summary>
/// <remarks>
/// The document these tests build is the case the codec previously could not
/// read at all — a subsetted composite font on an identity encoding, with no
/// <c>ToUnicode</c> map. Every glyph it draws is a number, and the only place
/// those numbers mean anything is inside the embedded program.
/// </remarks>
public sealed class PdfComposedFontTests
{
    private static PdfReadResult Read(byte[] pdf, bool composed)
    {
        PdfCodecServices services = composed
            ? PdfCodecServices.Base.WithFontProgramReader(new GraphicsFontProgramReader())
            : PdfCodecServices.Base;

        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec(services).ReadPdf(stream, null);
    }

    // ---- the recovery ---------------------------------------------------------

    [Fact]
    public void Without_A_Reader_A_ToUnicodeless_Composite_Font_Yields_No_Text()
    {
        PdfReadResult result = Read(Document("ABC", Glyphs(1, 2, 3)), composed: false);

        Assert.DoesNotContain("ABC", result.Document.PlainText, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextMappingMissing);
    }

    [Fact]
    public void With_A_Reader_The_Same_Document_Reads_Its_Text()
    {
        PdfReadResult result = Read(Document("ABC", Glyphs(1, 2, 3)), composed: true);

        // The whole point of IP-012: the glyph indices the page draws mean
        // something, and the meaning was inside the embedded program all along.
        Assert.Contains("ABC", result.Document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextMappingMissing);
    }

    [Fact]
    public void The_Recovery_Is_Reported_Rather_Than_Silent()
    {
        PdfReadResult result = Read(Document("ABC", Glyphs(1, 2, 3)), composed: true);

        DocumentDiagnostic note = Assert.Single(
            result.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.FontProgramNotComposed));

        Assert.Contains("a composed reader inspected", note.Message, StringComparison.Ordinal);
        Assert.Contains("read for a glyph-to-text map", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Glyph_Indices_With_No_Mapping_Stay_Unmapped()
    {
        // Glyph 9 is past the end of the generated font's map. A reader that
        // invented a character for it would be worse than one that does not.
        PdfReadResult result = Read(Document("ABC", Glyphs(1, 9, 3)), composed: true);

        // The unmapped glyph still advances the pen, so the gap it leaves reads as
        // a word break. What matters is that no character was invented for it.
        Assert.Contains("A", result.Document.PlainText, StringComparison.Ordinal);
        Assert.Contains("C", result.Document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("B", result.Document.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void An_Existing_ToUnicode_Map_Still_Wins()
    {
        // The producer's own statement outranks anything recovered from the
        // program, and the program is not even read when one is present.
        PdfReadResult result = Read(
            Document("ABC", Glyphs(1, 2, 3), toUnicode: ToUnicodeMappingToXyz),
            composed: true);

        Assert.Contains("XYZ", result.Document.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("ABC", result.Document.PlainText, StringComparison.Ordinal);
    }

    // ---- the formats the composed reader does not inspect ---------------------

    [Fact]
    public void A_Type1_Program_Is_Still_Reported_As_Uninspected()
    {
        // FontFile is Type 1. The composed parser exposes no glyph names, so the
        // reader declines it rather than guessing, and the note says so.
        PdfReadResult result = Read(
            Document("ABC", Glyphs(1, 2, 3), programKey: "FontFile"),
            composed: true);

        DocumentDiagnostic note = Assert.Single(
            result.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.FontProgramNotComposed));

        Assert.Contains("does not inspect", note.Message, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextMappingMissing);
    }

    [Fact]
    public void A_Program_That_Is_Not_A_Font_Costs_The_Font_And_Not_The_Document()
    {
        PdfReadResult result = Read(
            Document("ABC", Glyphs(1, 2, 3), program: "this is not a font program"u8.ToArray()),
            composed: true);

        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextMappingMissing);
        Assert.Equal(DocumentResultStatus.Partial, result.Status);
    }

    [Fact]
    public void A_Program_Past_The_Ceiling_Is_Not_Inspected()
    {
        PdfReadResult result = Read(
            Document("ABC", Glyphs(1, 2, 3)),
            composed: true,
            new PdfReadOptions(pdfLimits: new PdfLimits(maxFontProgramBytes: 16)));

        Assert.Contains(
            result.Diagnostics,
            d => d.Code == PdfDiagnosticCodes.FontProgramNotComposed &&
                d.Message.Contains("past the font-program ceiling", StringComparison.Ordinal));
    }

    private static PdfReadResult Read(byte[] pdf, bool composed, PdfReadOptions options)
    {
        PdfCodecServices services = composed
            ? PdfCodecServices.Base.WithFontProgramReader(new GraphicsFontProgramReader())
            : PdfCodecServices.Base;

        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec(services).ReadPdf(stream, options);
    }

    // ---- the reader on its own ------------------------------------------------

    [Fact]
    public void The_Reader_Declines_Every_Format_It_Does_Not_Inspect()
    {
        var reader = new GraphicsFontProgramReader();
        var context = new PdfFontProgramContext(1024 * 1024);
        byte[] font = GeneratedFont.SpellingOut("ABC");

        Assert.Null(reader.Read(font, "FontFile", null, context));
        Assert.Null(reader.Read(font, "FontFile3", "Type1C", context));
        Assert.Null(reader.Read(font, "FontFile3", "CIDFontType0C", context));

        Assert.NotNull(reader.Read(font, "FontFile2", null, context));
        Assert.NotNull(reader.Read(font, "FontFile3", "OpenType", context));
    }

    [Fact]
    public void The_Reader_Honours_Its_Byte_Ceiling()
    {
        byte[] font = GeneratedFont.SpellingOut("ABC");

        Assert.Null(new GraphicsFontProgramReader().Read(
            font, "FontFile2", null, new PdfFontProgramContext(font.Length - 1)));
    }

    [Fact]
    public void The_Reader_Maps_Glyphs_To_The_Text_The_Font_Draws()
    {
        PdfFontProgramMap? map = new GraphicsFontProgramReader().Read(
            GeneratedFont.SpellingOut("ABC"), "FontFile2", null, new PdfFontProgramContext(1024 * 1024));

        Assert.NotNull(map);
        Assert.Equal("TrueType", map!.Format);
        Assert.Equal("A", map.GlyphText[1]);
        Assert.Equal("B", map.GlyphText[2]);
        Assert.Equal("C", map.GlyphText[3]);
        Assert.False(map.GlyphText.ContainsKey(0));
    }

    [Fact]
    public void Rubbish_Returns_Null_Rather_Than_Throwing()
    {
        var reader = new GraphicsFontProgramReader();
        var context = new PdfFontProgramContext(1024 * 1024);

        Assert.Null(reader.Read([], "FontFile2", null, context));
        Assert.Null(reader.Read("not a font"u8.ToArray(), "FontFile2", null, context));

        byte[] font = GeneratedFont.SpellingOut("ABC");
        for (int length = 1; length < font.Length; length += 13)
            reader.Read(font.AsSpan(0, length).ToArray(), "FontFile2", null, context);
    }

    // ---- fixtures -------------------------------------------------------------

    /// <summary>A ToUnicode CMap that maps codes 1, 2, 3 to X, Y, Z.</summary>
    private const string ToUnicodeMappingToXyz =
        "/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n" +
        "1 begincodespacerange <0000> <ffff> endcodespacerange\n" +
        "3 beginbfchar\n<0001> <0058>\n<0002> <0059>\n<0003> <005a>\nendbfchar\n" +
        "endcmap end end\n";

    /// <summary>
    /// One page that shows <paramref name="codes"/> through a subsetted composite
    /// font on an identity encoding, embedding a font that spells
    /// <paramref name="alphabet"/>.
    /// </summary>
    private static byte[] Document(
        string alphabet,
        string codes,
        string? toUnicode = null,
        string programKey = "FontFile2",
        byte[]? program = null)
    {
        var objects = new List<byte[]>();

        int programObject = Add(objects, Stream("/Length1 512", program ?? GeneratedFont.SpellingOut(alphabet)));
        int descriptor = Add(objects, Latin1(
            "<< /Type /FontDescriptor /FontName /ABCDEF+Generated /Flags 4 /ItalicAngle 0 " +
            $"/Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /{programKey} {programObject} 0 R >>"));

        int descendant = Add(objects, Latin1(
            "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /ABCDEF+Generated " +
            "/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
            $"/FontDescriptor {descriptor} 0 R /DW 500 /CIDToGIDMap /Identity >>"));

        string map = toUnicode is null
            ? string.Empty
            : $" /ToUnicode {Add(objects, Stream(string.Empty, Latin1(toUnicode)))} 0 R";

        int font = Add(objects, Latin1(
            "<< /Type /Font /Subtype /Type0 /BaseFont /ABCDEF+Generated /Encoding /Identity-H " +
            $"/DescendantFonts [{descendant} 0 R]{map} >>"));

        int content = Add(objects, Stream(string.Empty, Latin1(
            $"BT /F1 12 Tf 1 0 0 1 72 720 Tm <{Hex(codes)}> Tj ET\n")));

        int pages = Add(objects, []);
        int page = Add(objects, Latin1(
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>"));
        int catalog = Add(objects, []);

        objects[pages - 1] = Latin1($"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        objects[catalog - 1] = Latin1($"<< /Type /Catalog /Pages {pages} 0 R >>");

        return Build(objects, catalog);
    }

    /// <summary>Glyph indices as the two-byte codes an identity encoding draws.</summary>
    private static string Glyphs(params int[] indices)
    {
        var text = new System.Text.StringBuilder(indices.Length);
        foreach (int index in indices)
            text.Append((char)index);
        return text.ToString();
    }

    private static string Hex(string codes)
    {
        var text = new System.Text.StringBuilder(codes.Length * 4);
        foreach (char code in codes)
            text.Append(((int)code).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
        return text.ToString();
    }

    private static int Add(List<byte[]> objects, byte[] body)
    {
        objects.Add(body);
        return objects.Count;
    }

    private static byte[] Stream(string dictionary, byte[] data)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Latin1($"<< {dictionary} /Length {data.Length} >>\nstream\n"));
        bytes.AddRange(data);
        bytes.AddRange(Latin1("\nendstream"));
        return bytes.ToArray();
    }

    private static byte[] Build(List<byte[]> objects, int rootObject)
    {
        var output = new MemoryStream();
        Append(output, "%PDF-1.7\n");

        var offsets = new long[objects.Count + 1];
        for (int i = 1; i <= objects.Count; i++)
        {
            offsets[i] = output.Length;
            Append(output, $"{i} 0 obj\n");
            output.Write(objects[i - 1], 0, objects[i - 1].Length);
            Append(output, "\nendobj\n");
        }

        long xref = output.Length;
        Append(output, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        for (int i = 1; i <= objects.Count; i++)
            Append(output, $"{offsets[i]:D10} 00000 n \n");

        Append(output, $"trailer\n<< /Size {objects.Count + 1} /Root {rootObject} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    private static void Append(MemoryStream stream, string text)
    {
        byte[] bytes = Latin1(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static byte[] Latin1(string text)
    {
        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++)
            bytes[i] = (byte)text[i];
        return bytes;
    }
}
