namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers Type 3 fonts: the two things that are true of them and of no other
/// simple font, and that reading one as though it were an ordinary font gets
/// wrong.
/// </summary>
/// <remarks>
/// A Type 3 font's glyphs are content streams the document drew and named. That
/// means it has no built-in encoding to fall back on — the names are the only
/// statement of meaning — and it measures those glyphs in a space of its own
/// choosing, declared in <c>/FontMatrix</c>, rather than in the thousandths of
/// text space every other simple font uses. Its glyph procedures are still never
/// executed; nothing here changes that.
/// </remarks>
public sealed class PdfType3FontTests
{
    // ---- /FontMatrix ----------------------------------------------------------

    [Fact]
    public void A_Glyph_Is_Measured_Through_The_Font_Matrix()
    {
        // The font matrix is a hundredth, so the glyph's 1000 units are 10 points
        // of text space per point of size: 1000 x 0.01 x 12 = 120pt of advance
        // from x=72, ending at 192 and well past the run that starts at 100.
        // Measured as thousandths instead, it would end at 84 and leave a
        // sixteen-point hole for the line assembler to read as a word gap.
        string text = Text(Read(Document("[0.01 0 0 0.01 0 0]", width: 1000)));

        Assert.Contains("Atail", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Default_Font_Matrix_Is_Still_A_Thousandth()
    {
        // The default is the same scale every other simple font has, which is why
        // reading Type 3 widths as thousandths was right until a font said
        // otherwise. Stating no matrix must keep that.
        string text = Text(Read(Document(matrix: null, width: 1000)));

        Assert.Contains("A tail", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Font_Matrix_That_States_A_Unit_Scale_Is_Honoured()
    {
        // [1 0 0 1] is a real and legal Type 3 matrix. One glyph unit is one text
        // unit, so a width of 10 is 120pt at size 12 — the same advance the
        // hundredth-scale fixture produces from a width of 1000.
        string text = Text(Read(Document("[1 0 0 1 0 0]", width: 10)));

        Assert.Contains("Atail", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Font_Matrix_This_Build_Will_Not_Honour_Falls_Back_To_The_Default()
    {
        // A scale of zero would collapse every advance to nothing, and a document
        // that states one has said something about its glyphs that cannot be
        // true. The default stands rather than the declaration.
        string text = Text(Read(Document("[0 0 0 0 0 0]", width: 1000)));

        Assert.Contains("A tail", text, StringComparison.Ordinal);
    }

    // ---- encoding --------------------------------------------------------------

    [Fact]
    public void Glyph_Names_In_Differences_Say_What_The_Codes_Mean()
    {
        // A name the format defines, rather than a letter, so this cannot pass on
        // a fallback encoding that happens to agree. Deliberately not a bullet:
        // the projector reads one at the head of a line as a list marker and
        // strips it, which would hide the mapping this is about.
        string text = Text(Read(Document(
            matrix: null,
            width: 1000,
            differences: "[65 /sterling]")));

        Assert.Contains("£", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Type3_Font_With_No_Names_Maps_Nothing_Rather_Than_Inventing_Latin()
    {
        // The point of the change. A Type 3 font has no built-in encoding: its
        // glyphs are drawings the document named, and where it named none there
        // is nothing to read. Falling back to StandardEncoding would have
        // answered "A" for whatever shape the procedure drew — confident nonsense,
        // which is exactly what the ZapfDingbats guard beside it exists to stop.
        PdfReadResult result = Read(Document(matrix: null, width: 1000, differences: null, encoding: false));

        Assert.DoesNotContain("A", Text(result), StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextMappingMissing);
    }

    [Fact]
    public void A_Named_Base_Encoding_Is_Still_Honoured()
    {
        // Refusing the fallback is not refusing an encoding the font actually
        // states. A Type 3 that names WinAnsi has said what its codes mean.
        string text = Text(Read(Document(
            matrix: null,
            width: 1000,
            differences: null,
            baseEncoding: "/WinAnsiEncoding")));

        Assert.Contains("A", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ToUnicode_Outranks_The_Absence_Of_Names()
    {
        string text = Text(Read(Document(
            matrix: null,
            width: 1000,
            differences: null,
            encoding: false,
            toUnicode: true)));

        Assert.Contains("Z", text, StringComparison.Ordinal);
    }

    // ---- what stays true --------------------------------------------------------

    [Fact]
    public void The_Font_Is_Still_Reported_And_Its_Procedures_Still_Never_Run()
    {
        DocumentDiagnostic note = Only(Read(Document(matrix: null, width: 1000)), PdfDiagnosticCodes.FontType3Unsupported);

        Assert.Contains("never executed", note.Message, StringComparison.Ordinal);
        Assert.Contains("/FontMatrix", note.Message, StringComparison.Ordinal);
    }

    // ---- fixtures -------------------------------------------------------------

    private static PdfReadResult Read(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, null);
    }

    private static DocumentDiagnostic Only(PdfReadResult result, string code) =>
        Assert.Single(result.Diagnostics.Where(d => d.Code == code));

    private static string Text(PdfReadResult result) =>
        string.Join("\n", result.Document.Paragraphs.Select(p => p.Text));

    /// <summary>
    /// One page drawing a single Type 3 glyph for code 65 at x=72, and an
    /// ordinary Helvetica run at x=100 on the same baseline. Whether the two
    /// arrive as one word or two is decided by how far the Type 3 glyph is
    /// measured to advance, which is the whole question.
    /// </summary>
    private static byte[] Document(
        string? matrix,
        int width,
        string? differences = "[65 /A]",
        bool encoding = true,
        string? baseEncoding = null,
        bool toUnicode = false)
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();

        // The glyph procedure. It is never executed; it exists so the font is
        // structurally a real one rather than a dictionary shaped like it.
        int proc = builder.AddStream(string.Empty, $"{width} 0 d0\n0 0 {width} 700 re f\n");
        int procs = builder.AddObject($"<< /A {proc} 0 R >>");

        string encodingEntry = string.Empty;
        if (encoding)
        {
            var parts = new List<string>();
            if (baseEncoding is not null)
                parts.Add($"/BaseEncoding {baseEncoding}");
            if (differences is not null)
                parts.Add($"/Differences {differences}");
            encodingEntry = $" /Encoding << /Type /Encoding {string.Join(" ", parts)} >>";
        }

        string unicode = string.Empty;
        if (toUnicode)
        {
            int map = builder.AddStream(string.Empty,
                "/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n" +
                "1 begincodespacerange <00> <FF> endcodespacerange\n" +
                "1 beginbfchar <41> <005A> endbfchar\n" +
                "endcmap end end\n");
            unicode = $" /ToUnicode {map} 0 R";
        }

        string fontMatrix = matrix is null ? string.Empty : $" /FontMatrix {matrix}";
        int type3 = builder.AddObject(
            "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000]" + fontMatrix +
            $" /CharProcs {procs} 0 R /FirstChar 65 /LastChar 65 /Widths [{width}]" +
            encodingEntry + unicode + " >>");

        int helvetica = builder.AddObject(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        int content = builder.AddStream(
            string.Empty,
            "BT /F3 12 Tf 1 0 0 1 72 700 Tm (A) Tj ET\n" +
            "BT /F1 12 Tf 1 0 0 1 100 700 Tm (tail) Tj ET\n");

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F3 {type3} 0 R /F1 {helvetica} 0 R >> >> " +
            $"/Contents {content} 0 R >>");

        return builder.Build(catalog);
    }
}
