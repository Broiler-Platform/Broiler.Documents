namespace Broiler.Documents.Pdf.Tests;

public sealed class PdfTextExtractionTests
{
    private static PdfReadResult Read(byte[] pdf, PdfReadOptions? options = null)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, options);
    }

    private static string TextOf(string content) =>
        Read(PdfFileBuilder.SinglePage(content)).Document.PlainText;

    [Fact]
    public void Joins_Runs_On_One_Baseline_Into_One_Line()
    {
        string content =
            "BT /F1 12 Tf 1 0 0 1 72 720 Tm (Hello) Tj 1 0 0 1 110 720 Tm (world) Tj ET\n";

        Assert.Equal("Hello world", TextOf(content).Trim());
    }

    [Fact]
    public void Separates_Words_Across_A_Wide_TJ_Adjustment()
    {
        // A large negative adjustment moves the pen right by more than a space.
        string content = "BT /F1 12 Tf 1 0 0 1 72 720 Tm [(Hello)-2000(world)] TJ ET\n";

        Assert.Equal("Hello world", TextOf(content).Trim());
    }

    [Fact]
    public void Keeps_A_Small_TJ_Kern_Inside_One_Word()
    {
        // Kerning between letters must not become a space.
        string content = "BT /F1 12 Tf 1 0 0 1 72 720 Tm [(Wa)-40(ter)] TJ ET\n";

        Assert.Equal("Water", TextOf(content).Trim());
    }

    [Fact]
    public void Splits_Baselines_Into_Separate_Lines()
    {
        string content =
            "BT /F1 12 Tf 1 0 0 1 72 720 Tm (First line) Tj 1 0 0 1 72 700 Tm (Second line) Tj ET\n";

        string text = TextOf(content);
        Assert.Contains("First line", text);
        Assert.Contains("Second line", text);
    }

    [Fact]
    public void Applies_Text_Leading_Through_The_Quote_Operators()
    {
        string content = "BT /F1 12 Tf 14 TL 1 0 0 1 72 720 Tm (One) Tj (Two) ' (Three) ' ET\n";

        string text = TextOf(content);
        Assert.Contains("One", text);
        Assert.Contains("Two", text);
        Assert.Contains("Three", text);
    }

    [Fact]
    public void Decodes_WinAnsi_High_Bytes()
    {
        // 0x93/0x94 are the typographic double quotes in WinAnsi, not control codes,
        // and 0xE9 is e-acute.
        string content = "BT /F1 12 Tf 1 0 0 1 72 720 Tm <93 63 61 66 E9 94> Tj ET\n";

        Assert.Equal("“café”", TextOf(content).Trim());
    }

    [Fact]
    public void Honours_An_Encoding_Differences_Array()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int encoding = builder.AddObject("<< /Type /Encoding /BaseEncoding /WinAnsiEncoding /Differences [65 /eacute /Euro] >>");
        int font = builder.AddObject($"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding {encoding} 0 R >>");
        int content = builder.AddStream(string.Empty, "BT /F1 12 Tf 1 0 0 1 72 720 Tm (AB) Tj ET\n");

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");

        Assert.Equal("é€", Read(builder.Build(catalog)).Document.PlainText.Trim());
    }

    [Fact]
    public void Prefers_A_ToUnicode_Map_Over_The_Encoding()
    {
        const string CMap = """
            /CIDInit /ProcSet findresource begin
            12 dict begin begincmap
            1 begincodespacerange <00> <FF> endcodespacerange
            2 beginbfchar <41> <03B1> <42> <03B2> endbfchar
            endcmap end end
            """;

        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int toUnicode = builder.AddStream(string.Empty, CMap);
        int font = builder.AddObject(
            $"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding /ToUnicode {toUnicode} 0 R >>");
        int content = builder.AddStream(string.Empty, "BT /F1 12 Tf 1 0 0 1 72 720 Tm (AB) Tj ET\n");

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");

        Assert.Equal("αβ", Read(builder.Build(catalog)).Document.PlainText.Trim());
    }

    [Fact]
    public void Reads_A_Composite_Font_Through_Its_ToUnicode_Map()
    {
        const string CMap = """
            1 begincodespacerange <0000> <FFFF> endcodespacerange
            1 beginbfrange <0003> <0005> <0041> endbfrange
            endcmap
            """;

        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int toUnicode = builder.AddStream(string.Empty, CMap);
        int descendant = builder.AddObject(
            "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /ABCDEF+Sample /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /DW 500 >>");
        int font = builder.AddObject(
            $"<< /Type /Font /Subtype /Type0 /BaseFont /ABCDEF+Sample /Encoding /Identity-H /DescendantFonts [{descendant} 0 R] /ToUnicode {toUnicode} 0 R >>");
        int content = builder.AddStream(string.Empty, "BT /F1 12 Tf 1 0 0 1 72 720 Tm <000300040005> Tj ET\n");

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");

        Assert.Equal("ABC", Read(builder.Build(catalog)).Document.PlainText.Trim());
    }

    [Fact]
    public void Strips_A_Subset_Prefix_From_The_Family_Name()
    {
        Assert.Equal("Minion", Text.PdfFont.StripSubsetPrefix("ABCDEF+Minion"));

        // Only the exact six-capital form is a subset tag.
        Assert.Equal("abcdef+Minion", Text.PdfFont.StripSubsetPrefix("abcdef+Minion"));
        Assert.Equal("Minion", Text.PdfFont.StripSubsetPrefix("Minion"));
    }

    [Fact]
    public void ActualText_Replaces_The_Glyphs_It_Encloses()
    {
        string content =
            "BT /F1 12 Tf 1 0 0 1 72 720 Tm /Span << /ActualText (fi) >> BDC (\\256) Tj EMC ET\n";

        Assert.Equal("fi", TextOf(content).Trim());
    }

    [Fact]
    public void Reports_Invisible_Text_Rather_Than_Judging_Visibility()
    {
        string content = "BT /F1 12 Tf 3 Tr 1 0 0 1 72 720 Tm (Hidden) Tj ET\n";
        PdfReadResult result = Read(PdfFileBuilder.SinglePage(content));

        // The default extracts it and says so; visibility is never asserted.
        Assert.Contains("Hidden", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextVisibilityUncertain);

        PdfReadResult omitted = Read(
            PdfFileBuilder.SinglePage(content),
            new PdfReadOptions(includeInvisibleText: false));
        Assert.DoesNotContain("Hidden", omitted.Document.PlainText);
    }

    [Fact]
    public void Runs_A_Form_XObject_And_Guards_Against_Its_Recursion()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int form = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int content = builder.AddStream(string.Empty, "/Fm0 Do\n");

        // The form invokes itself, which must terminate rather than recurse.
        builder.SetObject(form, "<< /Type /XObject /Subtype /Form /BBox [0 0 612 792] " +
                                $"/Resources << /Font << /F1 {font} 0 R >> /XObject << /Fm0 {form} 0 R >> >> /Length 62 >>\n" +
                                "stream\nBT /F1 12 Tf 1 0 0 1 72 720 Tm (In a form) Tj ET /Fm0 Do\nendstream");

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
                                $"/Resources << /XObject << /Fm0 {form} 0 R >> /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");

        PdfReadResult result = Read(builder.Build(catalog));

        Assert.Contains("In a form", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ObjectCycle);
    }

    [Fact]
    public void Detects_An_Image_Without_Decoding_It()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int image = builder.AddStream(
            "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8",
            "abc",
            filter: "DCTDecode");
        int content = builder.AddStream(string.Empty, "q 100 0 0 100 72 600 cm /Im0 Do Q\n");

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] /Resources << /XObject << /Im0 {image} 0 R >> >> /Contents {content} 0 R >>");

        PdfReadResult result = Read(builder.Build(catalog));

        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.FilterDctUnsupported);
        Assert.Equal(DocumentResultStatus.Partial, result.Status);
    }

    [Fact]
    public void Consumes_An_Inline_Image_Without_Losing_The_Text_After_It()
    {
        string content =
            "BT /F1 12 Tf 1 0 0 1 72 720 Tm (Before) Tj ET\n" +
            "q BI /W 2 /H 2 /CS /G /BPC 8 ID  EI Q\n" +
            "BT /F1 12 Tf 1 0 0 1 72 700 Tm (After) Tj ET\n";

        string text = TextOf(content);

        Assert.Contains("Before", text);
        Assert.Contains("After", text);
    }

    [Fact]
    public void Projects_An_Admitted_Link_And_Rejects_A_Javascript_Target()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int content = builder.AddStream(string.Empty,
            "BT /F1 12 Tf 1 0 0 1 72 720 Tm (Broiler) Tj ET\n" +
            "BT /F1 12 Tf 1 0 0 1 72 700 Tm (Danger) Tj ET\n");
        int good = builder.AddObject("<< /Type /Annot /Subtype /Link /Rect [70 715 140 735] /A << /S /URI /URI (https://example.org/docs) >> >>");
        int bad = builder.AddObject("<< /Type /Annot /Subtype /Link /Rect [70 695 140 715] /A << /S /URI /URI (javascript:alert\\(1\\)) >> >>");

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
                                $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R /Annots [{good} 0 R {bad} 0 R] >>");

        PdfReadResult result = Read(builder.Build(catalog));

        var links = new List<string>();
        foreach (RichTextParagraph paragraph in result.Document.Paragraphs)
        {
            foreach (StyleRun run in paragraph.Runs)
            {
                if (run.Style.LinkHref is { } href)
                    links.Add(href);
            }
        }

        Assert.Contains("https://example.org/docs", links);
        Assert.DoesNotContain(links, href => href.StartsWith("javascript", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.UriRejected);
    }

    [Fact]
    public void Warns_Loudly_About_An_Unapplied_Redaction()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int content = builder.AddStream(string.Empty, PdfFileBuilder.ShowText("Still here"));
        int redact = builder.AddObject("<< /Type /Annot /Subtype /Redact /Rect [70 715 200 735] >>");

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
                                $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R /Annots [{redact} 0 R] >>");

        PdfReadResult result = Read(builder.Build(catalog));

        DocumentDiagnostic warning = Assert.Single(
            result.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.RedactionNotApplied));
        Assert.Equal(DocumentDiagnosticSeverity.Error, warning.Severity);

        // The point of the warning: the covered text is still in the document.
        Assert.Contains("Still here", result.Document.PlainText);
    }

    [Fact]
    public void Reports_A_Page_With_No_Text_As_Needing_Ocr()
    {
        PdfReadResult result = Read(PdfFileBuilder.SinglePage("q 1 0 0 1 0 0 cm Q\n"));

        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextOcrRequired);
        Assert.Equal(DocumentResultStatus.Partial, result.Status);
    }

    [Fact]
    public void Says_That_Reading_Order_Was_Inferred()
    {
        PdfReadResult result = Read(PdfFileBuilder.SinglePage(PdfFileBuilder.ShowText("Anything")));

        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ReadingOrderHeuristic);
    }

    [Fact]
    public void Groups_Wrapped_Lines_Into_One_Paragraph_And_Splits_On_A_Wide_Gap()
    {
        string content =
            "BT /F1 12 Tf 1 0 0 1 72 720 Tm (This sentence wraps onto a) Tj ET\n" +
            "BT /F1 12 Tf 1 0 0 1 72 706 Tm (second line of the same paragraph.) Tj ET\n" +
            "BT /F1 12 Tf 1 0 0 1 72 640 Tm (A separate paragraph after a wide gap.) Tj ET\n";

        RichTextDocument document = Read(PdfFileBuilder.SinglePage(content)).Document;

        Assert.Equal(2, document.ParagraphCount);
        Assert.Contains("wraps onto a second line", document.Paragraphs[0].Text);
        Assert.StartsWith("A separate paragraph", document.Paragraphs[1].Text);
    }

    [Fact]
    public void Recognizes_A_Bullet_Marker_As_A_List_Paragraph()
    {
        string content =
            "BT /F1 12 Tf 1 0 0 1 72 720 Tm (\\267 First item) Tj ET\n" +
            "BT /F1 12 Tf 1 0 0 1 72 700 Tm (\\267 Second item) Tj ET\n";

        RichTextDocument document = Read(PdfFileBuilder.SinglePage(content)).Document;

        Assert.All(document.Paragraphs, paragraph => Assert.Equal(ListKind.Bullet, paragraph.Style.ListKind));
        Assert.Equal("First item", document.Paragraphs[0].Text);
    }

    [Theory]
    [InlineData("• Item", ListKind.Bullet, "Item")]
    [InlineData("- Item", ListKind.Bullet, "Item")]
    [InlineData("1. Item", ListKind.Numbered, "Item")]
    [InlineData("a) Item", ListKind.Numbered, "Item")]
    public void Detects_The_List_Marker_Forms(string text, ListKind expected, string remainder)
    {
        Assert.True(Text.PdfModelProjector.DetectListMarker(text, out ListKind kind, out int markerLength));
        Assert.Equal(expected, kind);
        Assert.Equal(remainder, text[markerLength..]);
    }

    [Theory]
    [InlineData("well-known hyphenation")]
    [InlineData("2026 was a year")]
    [InlineData("")]
    public void Does_Not_Invent_A_List_From_Ordinary_Text(string text)
    {
        Assert.False(Text.PdfModelProjector.DetectListMarker(text, out _, out _));
    }
}
