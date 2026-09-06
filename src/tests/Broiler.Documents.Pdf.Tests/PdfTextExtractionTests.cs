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

    // ---- annotations that name a place instead of a URI ----------------------

    [Fact]
    public void Reports_A_Link_That_Names_A_Destination_Instead_Of_An_Action()
    {
        // It used to leave with no counter touched: the link vanished, no
        // diagnostic was raised, and the read still reported Success.
        PdfReadResult result = ReadWithAnnotations(
            "<< /Type /Annot /Subtype /Link /Rect [70 715 140 735] /Dest (chapter-two) >>");

        Assert.Single(result.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.LinkDestinationDropped));
        Assert.Empty(LinksOf(result));
        Assert.Equal(DocumentResultStatus.Partial, result.Status);

        // The destination is never read, so nothing the file named can reach a
        // caller through a message.
        Assert.All(result.Diagnostics, d => Assert.DoesNotContain("chapter-two", d.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void A_Destination_Is_Never_Projected_As_A_Fragment()
    {
        // The load-bearing one. A synthesised "#name" would pass no policy - the
        // PDF policy refuses it - but DocumentLinkTarget admits it, so it would
        // leave here inert and arrive in DOCX as a live w:hyperlink w:anchor.
        PdfReadResult result = ReadWithAnnotations(
            "<< /Type /Annot /Subtype /Link /Rect [70 715 140 735] /Dest (chapter-two) >>");

        Assert.DoesNotContain(LinksOf(result), href => href.StartsWith("#", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/Dest (chapter-two)")]
    [InlineData("/Dest /ChapterOne")]
    [InlineData("/Dest [$page 0 R /XYZ 72 720 0]")]
    public void Reports_A_Destination_In_Any_Spelling_The_Same_Way(string destination)
    {
        // The fix keys off the key being present and never parses its value, so
        // every spelling is reported identically and nothing is claimed about a
        // form that was not read.
        PdfReadResult result = ReadWithAnnotations(
            "<< /Type /Annot /Subtype /Link /Rect [70 715 140 735] " + destination + " >>");

        Assert.Single(result.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.LinkDestinationDropped));
        Assert.Empty(LinksOf(result));
    }

    [Fact]
    public void A_Same_Document_Jump_Is_Not_Reported_As_Active_Content()
    {
        // A GoTo executes nothing and fetches nothing. Counting it as active
        // content made a table of contents read like a document carrying
        // JavaScript, under one merged number the two could not be told apart in.
        PdfReadResult result = ReadWithAnnotations(
            "<< /Type /Annot /Subtype /Link /Rect [70 715 140 735] /A << /S /GoTo /D (c) >> >>");

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ActiveContentRemoved);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.LinkDestinationDropped);

        // Reclassifying must not make the document quieter: the loss is still a
        // skip, so the read is still Partial.
        Assert.Equal(DocumentResultStatus.Partial, result.Status);
    }

    [Theory]
    [InlineData("/A << /S /GoToR /F (other.pdf) >>")]
    [InlineData("/A << /S /GoToE /T << /R /C >> >>")]
    [InlineData("/A << /S /Named /N /NextPage >>")]
    public void A_Jump_That_Leaves_This_File_Is_Still_Active_Content(string action)
    {
        // The carve-out is exact string equality on purpose: "GoTo" is a prefix of
        // both remote spellings, so a StartsWith regression fails here at once.
        PdfReadResult result = ReadWithAnnotations(
            "<< /Type /Annot /Subtype /Link /Rect [70 715 140 735] " + action + " >>");

        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ActiveContentRemoved);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.LinkDestinationDropped);
    }

    [Theory]
    [InlineData("/Subtype /Widget ")]
    [InlineData("")]
    public void Counts_An_Active_Action_On_An_Annotation_That_Is_Not_A_Link(string subtype)
    {
        // The only guard against hoisting the subtype test above the action test,
        // which would silence annotation-level JavaScript on every subtype but
        // Link - and a file engineered to hide one is exactly the file that omits
        // /Subtype altogether. Classify before you filter.
        PdfReadResult result = ReadWithAnnotations(
            "<< /Type /Annot " + subtype + "/Rect [70 715 140 735] /A << /S /JavaScript /JS (x) >> >>");

        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ActiveContentRemoved);
    }

    [Fact]
    public void Counts_A_Uri_Action_On_An_Annotation_That_Is_Not_A_Link()
    {
        // The same target was reported on a /Link and silent on a /Widget.
        PdfReadResult result = ReadWithAnnotations(
            "<< /Type /Annot /Subtype /Widget /Rect [70 715 140 735] " +
            "/A << /S /URI /URI (javascript:evil) >> >>");

        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ActiveContentRemoved);
        Assert.Empty(LinksOf(result));
        Assert.NotEqual(DocumentResultStatus.Success, result.Status);
    }

    [Theory]
    [InlineData("/A << /URI (https://example.org/y) >>")]
    [InlineData("/A << /S (URI) /URI (https://example.org/y) >>")]
    public void Counts_An_Action_That_Names_No_Type(string action)
    {
        // An action dictionary with no /S, or an /S that is not a name, fell past
        // the old length guard without incrementing anything.
        PdfReadResult result = ReadWithAnnotations(
            "<< /Type /Annot /Subtype /Link /Rect [70 715 140 735] " + action + " >>");

        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ActiveContentRemoved);
        Assert.Empty(LinksOf(result));
    }

    [Theory]
    [InlineData("/A 42")]
    [InlineData("/A 999 0 R")]
    public void Counts_An_Action_That_Is_Not_A_Dictionary(string action)
    {
        // Present and unreadable is an action of a kind that cannot be named, so
        // it is inventoried rather than trusted. It used to drop the annotation in
        // silence.
        PdfReadResult result = ReadWithAnnotations(
            "<< /Type /Annot /Subtype /Link /Rect [70 715 140 735] " + action + " >>");

        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ActiveContentRemoved);
        Assert.Empty(LinksOf(result));
        Assert.NotEqual(DocumentResultStatus.Success, result.Status);
    }

    [Fact]
    public void An_Explicit_Null_Action_Is_Not_Counted_As_Active_Content()
    {
        // A null /A states an absent action rather than an unreadable one. This is
        // what the PdfNull guard buys; without a test it reads as noise.
        PdfReadResult result = ReadWithAnnotations(
            "<< /Type /Annot /Subtype /Link /Rect [70 715 140 735] /A null >>");

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ActiveContentRemoved);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.LinkDestinationDropped);
    }

    [Fact]
    public void An_Admitted_Action_Wins_Over_A_Coexisting_Destination()
    {
        // /A and /Dest are alternatives. A file carrying both cannot re-route a
        // good link into a destination drop, and the annotation is not counted
        // twice.
        PdfReadResult result = ReadWithAnnotations(
            "<< /Type /Annot /Subtype /Link /Rect [70 715 140 735] " +
            "/A << /S /URI /URI (https://example.org/both) >> /Dest [$page 0 R /XYZ 72 720 0] >>");

        Assert.Contains("https://example.org/both", LinksOf(result));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.LinkDestinationDropped);
    }


    // ---- annotations on a page that drew nothing ------------------------------

    [Fact]
    public void An_Unapplied_Redaction_On_A_Page_That_Drew_Nothing_Is_Still_An_Error()
    {
        // The annotation reader used to sit below the emptiness test, so a page
        // that drew nothing was never inspected. This is the shape that matters:
        // a caller could read the conversion as a redaction and be told nothing.
        PdfReadResult result = ReadPageWithAnnotations(
            "q 1 0 0 1 0 0 cm Q\n",
            "<< /Type /Annot /Subtype /Redact /Rect [70 715 200 735] >>");

        DocumentDiagnostic redaction = Assert.Single(
            result.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.RedactionNotApplied));
        Assert.Equal(DocumentDiagnosticSeverity.Error, redaction.Severity);

        // The page really is empty, and the fix must not pretend otherwise.
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextOcrRequired);
    }

    [Fact]
    public void Active_Content_On_A_Page_That_Drew_Nothing_Is_Still_Counted()
    {
        PdfReadResult result = ReadPageWithAnnotations(
            "q 1 0 0 1 0 0 cm Q\n",
            "<< /Type /Annot /Subtype /Widget /Rect [70 715 200 735] /A << /S /JavaScript /JS (x) >> >>");

        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ActiveContentRemoved);
    }

    [Fact]
    public void A_Scanned_Page_Whose_Image_Was_Refused_Still_Reports_Its_Redaction()
    {
        // Why the hole mattered rather than merely existed. A scanned page is
        // image-only, and this project does not compose the JPEG decoder, so the
        // image is refused and the page reaches the emptiness test with nothing -
        // which is exactly the document someone redacts.
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int image = builder.AddStream(
            "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8",
            "abc",
            filter: "DCTDecode");
        int content = builder.AddStream(string.Empty, "q 100 0 0 100 72 600 cm /Im0 Do Q\n");
        int redact = builder.AddObject("<< /Type /Annot /Subtype /Redact /Rect [70 715 200 735] >>");

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
                                $"/Resources << /XObject << /Im0 {image} 0 R >> >> " +
                                $"/Contents {content} 0 R /Annots [{redact} 0 R] >>");

        PdfReadResult result = Read(builder.Build(catalog));

        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.RedactionNotApplied);

        // Said out loud, because it is the reason the page was empty.
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.FilterDctUnsupported);
    }

    [Fact]
    public void An_Empty_Page_With_No_Annotations_Reports_Only_Ocr()
    {
        // The guard against a fix that invents diagnostics on a blank page.
        PdfReadResult result = ReadPageWithAnnotations("q 1 0 0 1 0 0 cm Q\n");

        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextOcrRequired);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.RedactionNotApplied);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ActiveContentRemoved);
    }

    /// <summary>
    /// A single page that draws text, carrying the given annotation dictionaries.
    /// "$page" stands for the page's object number. The text is here so the
    /// projection assertions have a run to attach a link to - annotations are read
    /// whether or not the page drew anything, which
    /// <see cref="ReadPageWithAnnotations"/> is the fixture for.
    /// </summary>
    private static PdfReadResult ReadWithAnnotations(params string[] annotations) =>
        ReadPageWithAnnotations("BT /F1 12 Tf 1 0 0 1 72 720 Tm (Broiler) Tj ET\n", annotations);

    /// <summary>
    /// A single page whose content stream is given verbatim, carrying the given
    /// annotation dictionaries. An empty stream is the point of it: a page that
    /// draws nothing still has its annotations inspected.
    /// </summary>
    private static PdfReadResult ReadPageWithAnnotations(
        string contentStream, params string[] annotations)
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int content = builder.AddStream(string.Empty, contentStream);

        string references = string.Empty;
        foreach (string annotation in annotations)
        {
            int id = builder.AddObject(annotation.Replace("$page", page.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
            references += (references.Length == 0 ? string.Empty : " ") + id + " 0 R";
        }

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
                                $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R " +
                                $"/Annots [{references}] >>");

        return Read(builder.Build(catalog));
    }

    private static List<string> LinksOf(PdfReadResult result)
    {
        var links = new List<string>();
        foreach (RichTextParagraph paragraph in result.Document.Paragraphs)
        {
            foreach (StyleRun run in paragraph.Runs)
            {
                if (run.Style.LinkHref is { } href)
                    links.Add(href);
            }
        }

        return links;
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
