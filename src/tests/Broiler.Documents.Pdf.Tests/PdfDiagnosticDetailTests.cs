namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers what a diagnostic <em>says</em>, not merely which code it carries.
/// </summary>
/// <remarks>
/// The codes are the stable API and are asserted throughout the rest of the
/// suite. These tests exist because a code alone does not answer the question a
/// reader actually has when a construct is skipped — how much was skipped, where,
/// and which variant of it. A note that loses those to de-duplication is
/// technically correct and useless, so the aggregation is contract too.
/// </remarks>
public sealed class PdfDiagnosticDetailTests
{
    private static PdfReadResult Read(byte[] pdf, PdfReadOptions? options = null)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, options);
    }

    private static DocumentDiagnostic Only(PdfReadResult result, string code) =>
        Assert.Single(result.Diagnostics.Where(d => d.Code == code));

    // ---- occurrence counts and locations --------------------------------------

    [Fact]
    public void One_Code_Repeated_Across_Pages_Keeps_Its_Count_And_Names_The_Pages()
    {
        PdfReadResult result = Read(PagesDrawingOneImage(3));

        DocumentDiagnostic image = Only(result, PdfDiagnosticCodes.FilterDctUnsupported);

        // The point of the test: three images collapse to one diagnostic, and the
        // three does not vanish with them.
        Assert.Contains("3 images", image.Message, StringComparison.Ordinal);
        Assert.Contains("On pages 1, 2, 3.", image.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Page_Level_Diagnostic_Carries_The_Page_It_Came_From()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int readable = builder.AddStream(string.Empty, PdfFileBuilder.ShowText("Readable"));
        int unreadable = builder.AddStream(string.Empty, "not really fax data", filter: "CCITTFaxDecode");

        int first = builder.AddObject(Page(pages, font, readable));
        int second = builder.AddObject(Page(pages, font, unreadable));

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{first} 0 R {second} 0 R] /Count 2 >>");

        PdfReadResult result = Read(builder.Build(catalog));

        DocumentDiagnostic ccitt = Only(result, PdfDiagnosticCodes.FilterCcittUnsupported);
        Assert.Equal(2, ccitt.Location?.PageNumber);
    }

    [Fact]
    public void A_Document_Level_Diagnostic_Claims_No_Page()
    {
        PdfReadResult result = Read(PdfFileBuilder.SinglePage(PdfFileBuilder.ShowText("Anything")));

        Assert.Null(Only(result, PdfDiagnosticCodes.ReadingOrderHeuristic).Location);
    }

    [Fact]
    public void A_Condition_Met_On_Every_Page_Is_Counted_Rather_Than_Reported_Once()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var kids = new List<string>(2);
        for (int i = 0; i < 2; i++)
        {
            int content = builder.AddStream(
                string.Empty,
                "BT /F1 12 Tf 3 Tr 1 0 0 1 72 720 Tm (Watermark) Tj ET\n");
            kids.Add($"{builder.AddObject(Page(pages, font, content))} 0 R");
        }

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{string.Join(' ', kids)}] /Count 2 >>");

        DocumentDiagnostic invisible = Only(
            Read(builder.Build(catalog)),
            PdfDiagnosticCodes.TextVisibilityUncertain);

        // The old code guarded this report behind "have I already said this?",
        // which kept the entry and threw away the scale of it.
        Assert.Contains("Seen 2 times", invisible.Message, StringComparison.Ordinal);
        Assert.Contains("on pages 1, 2", invisible.Message, StringComparison.Ordinal);
        Assert.Equal(1, invisible.Location?.PageNumber);
    }

    // ---- images ---------------------------------------------------------------

    [Fact]
    public void An_Undecoded_Image_Reports_The_Tuple_Its_Dictionary_Declares()
    {
        PdfReadResult result = Read(PagesDrawingOneImage(1));

        DocumentDiagnostic image = Only(result, PdfDiagnosticCodes.FilterDctUnsupported);

        // Everything a DCT decoder would be asked to handle, short of the entropy
        // mode that only the sample data knows. This is the part IP-005 can act on.
        Assert.Contains("1000x750 8bpc DeviceRGB DCTDecode", image.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Distinct_Image_Tuples_Are_Listed_Rather_Than_Collapsed()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int colour = builder.AddStream(
            "/Type /XObject /Subtype /Image /Width 1000 /Height 750 /ColorSpace /DeviceRGB /BitsPerComponent 8",
            "not jpeg data",
            filter: "DCTDecode");
        int grey = builder.AddStream(
            "/Type /XObject /Subtype /Image /Width 64 /Height 64 /ColorSpace /DeviceGray /BitsPerComponent 8",
            "not jpeg data either",
            filter: "DCTDecode");
        int content = builder.AddStream(string.Empty, "q /Im0 Do /Im1 Do Q\n");

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /XObject << /Im0 {colour} 0 R /Im1 {grey} 0 R >> >> /Contents {content} 0 R >>");

        DocumentDiagnostic image = Only(Read(builder.Build(catalog)), PdfDiagnosticCodes.FilterDctUnsupported);

        Assert.Contains("1000x750 8bpc DeviceRGB DCTDecode", image.Message, StringComparison.Ordinal);
        Assert.Contains("64x64 8bpc DeviceGray DCTDecode", image.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_Inline_Image_Is_Described_From_Its_Abbreviated_Parameters()
    {
        string content =
            "q BI /W 8 /H 8 /CS /G /BPC 8 ID  EI Q\n" +
            PdfFileBuilder.ShowText("After");

        DocumentDiagnostic image = Only(
            Read(PdfFileBuilder.SinglePage(content)),
            PdfDiagnosticCodes.ImageNotComposed);

        Assert.Contains("all inline", image.Message, StringComparison.Ordinal);
        Assert.Contains("8x8 8bpc", image.Message, StringComparison.Ordinal);
    }

    // ---- vector artwork -------------------------------------------------------

    [Fact]
    public void Dropped_Paths_Are_Counted_By_The_Shape_They_Had()
    {
        string content =
            "72 700 400 1 re f\n" +                     // a rule
            "72 500 200 100 re f\n" +                   // an area
            "72 300 m 200 400 l S\n" +                  // a diagonal
            "72 200 m 100 250 150 250 200 200 c S\n" +  // a curve
            PdfFileBuilder.ShowText("Body");

        DocumentDiagnostic artwork = Only(
            Read(PdfFileBuilder.SinglePage(content)),
            PdfDiagnosticCodes.VectorArtworkDropped);

        Assert.Contains("4 path-painting operations were dropped", artwork.Message, StringComparison.Ordinal);
        Assert.Contains("1 thin axis-aligned bar", artwork.Message, StringComparison.Ordinal);
        Assert.Contains("1 axis-aligned area", artwork.Message, StringComparison.Ordinal);
        Assert.Contains("2 general paths", artwork.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Rotated_Rectangle_Is_Not_Called_A_Rule()
    {
        // Same thin rectangle, turned 45 degrees by the CTM. On the page it is a
        // diagonal bar, and calling it a rule would claim structure a reader can
        // see is not there.
        string content =
            "q 0.7071 0.7071 -0.7071 0.7071 300 300 cm 0 0 400 1 re f Q\n" +
            PdfFileBuilder.ShowText("Body");

        DocumentDiagnostic artwork = Only(
            Read(PdfFileBuilder.SinglePage(content)),
            PdfDiagnosticCodes.VectorArtworkDropped);

        Assert.Contains("1 general path", artwork.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("thin axis-aligned bar", artwork.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Path_Used_Only_For_Clipping_Drops_Nothing()
    {
        string content =
            "q 72 600 400 100 re W n\n" +
            PdfFileBuilder.ShowText("Clipped") +
            "Q\n";

        PdfReadResult result = Read(PdfFileBuilder.SinglePage(content));

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.VectorArtworkDropped);
        Assert.Contains("Clipped", result.Document.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Shading_Is_Reported_As_A_Shading()
    {
        DocumentDiagnostic artwork = Only(
            Read(PdfFileBuilder.SinglePage("q /Sh0 sh Q\n" + PdfFileBuilder.ShowText("Body"))),
            PdfDiagnosticCodes.VectorArtworkDropped);

        Assert.Contains("1 smooth shading", artwork.Message, StringComparison.Ordinal);
    }

    // ---- font programs --------------------------------------------------------

    [Fact]
    public void An_Embedded_Font_Program_Is_Reported_By_Format()
    {
        DocumentDiagnostic font = Only(
            Read(PageUsingEmbeddedFont("/FontFile2", flags: 4, toUnicode: null)),
            PdfDiagnosticCodes.FontProgramNotComposed);

        Assert.Contains("1 FontFile2 (TrueType)", font.Message, StringComparison.Ordinal);

        // Symbolic, no ToUnicode, no program to read: the one combination where
        // the extracted text is a guess, and the note has to say so.
        Assert.Contains("1 is without a ToUnicode map", font.Message, StringComparison.Ordinal);
        Assert.Contains("marked symbolic", font.Message, StringComparison.Ordinal);
        Assert.Contains("may be wrong", font.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Font_Program_With_A_ToUnicode_Map_Is_Not_Reported_As_Uncertain()
    {
        DocumentDiagnostic font = Only(
            Read(PageUsingEmbeddedFont("/FontFile2", flags: 4, toUnicode: SimpleToUnicode)),
            PdfDiagnosticCodes.FontProgramNotComposed);

        Assert.Contains("every one supplies a ToUnicode map", font.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("may be wrong", font.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Font_Program_Note_Never_Names_The_Font()
    {
        PdfReadResult result = Read(PageUsingEmbeddedFont("/FontFile2", flags: 4, toUnicode: null));

        // The base font is ABCDEF+ConfidentialProject in the fixture. A format is
        // a construct and may be reported; a font name is a value and may not.
        Assert.All(
            result.Diagnostics,
            d => Assert.DoesNotContain("ConfidentialProject", d.Message, StringComparison.Ordinal));
    }

    // ---- metadata and structure ----------------------------------------------

    [Fact]
    public void A_Dropped_Xmp_Packet_Is_Sized_And_Says_What_Survived_It()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int content = builder.AddStream(string.Empty, PdfFileBuilder.ShowText("Body"));
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int metadata = builder.AddStream("/Type /Metadata /Subtype /XML", "<?xpacket begin=''?><x:xmpmeta/><?xpacket end='w'?>");

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R /Metadata {metadata} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, Page(pages, font, content));

        DocumentDiagnostic dropped = Only(Read(builder.Build(catalog)), PdfDiagnosticCodes.MetadataRawDropped);

        Assert.Contains("raw bytes", dropped.Message, StringComparison.Ordinal);

        // Without an Info dictionary the dropped packet was the whole of the
        // document's metadata, which is the fact a caller needs and the old note
        // did not give.
        Assert.Contains("no Info dictionary", dropped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Structure_Tree_Is_Described_Rather_Than_Only_Noted()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int content = builder.AddStream(string.Empty, PdfFileBuilder.ShowText("Body"));
        int parentTree = builder.AddObject("<< /Nums [0 []] >>");
        int structure = builder.AddObject(
            $"<< /Type /StructTreeRoot /K [<< /S /P >> << /S /P >> << /S /H1 >>] /ParentTree {parentTree} 0 R >>");

        builder.SetObject(
            catalog,
            $"<< /Type /Catalog /Pages {pages} 0 R /StructTreeRoot {structure} 0 R /MarkInfo << /Marked true >> >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, Page(pages, font, content));

        PdfReadResult result = Read(builder.Build(catalog));

        // One code, one note: the structure tree is why the heuristic was still
        // needed, so it belongs in that sentence rather than in a second entry
        // the sink would silently reduce to a count.
        DocumentDiagnostic order = Only(result, PdfDiagnosticCodes.ReadingOrderHeuristic);

        Assert.Contains("Reading order was inferred from page geometry", order.Message, StringComparison.Ordinal);
        Assert.Contains("3 top-level elements", order.Message, StringComparison.Ordinal);
        Assert.Contains("marks the document as tagged", order.Message, StringComparison.Ordinal);
        Assert.Contains("ParentTree maps marked content back to it", order.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Structure_Tree_Without_A_ParentTree_Says_So()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int content = builder.AddStream(string.Empty, PdfFileBuilder.ShowText("Body"));
        int structure = builder.AddObject("<< /Type /StructTreeRoot /K [] >>");

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R /StructTreeRoot {structure} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, Page(pages, font, content));

        DocumentDiagnostic order = Only(Read(builder.Build(catalog)), PdfDiagnosticCodes.ReadingOrderHeuristic);

        Assert.Contains("0 top-level elements", order.Message, StringComparison.Ordinal);
        Assert.Contains("does not mark the document as tagged", order.Message, StringComparison.Ordinal);
        Assert.Contains("no ParentTree", order.Message, StringComparison.Ordinal);
    }

    // ---- fixtures -------------------------------------------------------------

    private const string SimpleToUnicode =
        "/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n" +
        "1 begincodespacerange <00> <ff> endcodespacerange\n" +
        "1 beginbfrange <41> <5a> <0041> endbfrange\n" +
        "endcmap end end\n";

    private static string Page(int pages, int font, int content) =>
        $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
        $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>";

    /// <summary>A document of <paramref name="pageCount"/> pages, each drawing the same JPEG.</summary>
    private static byte[] PagesDrawingOneImage(int pageCount)
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int image = builder.AddStream(
            "/Type /XObject /Subtype /Image /Width 1000 /Height 750 /ColorSpace /DeviceRGB /BitsPerComponent 8",
            "not jpeg data",
            filter: "DCTDecode");

        var kids = new List<string>(pageCount);
        for (int i = 0; i < pageCount; i++)
        {
            int content = builder.AddStream(string.Empty, "q 100 0 0 100 72 600 cm /Im0 Do Q\n");
            int page = builder.AddObject(
                $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
                $"/Resources << /XObject << /Im0 {image} 0 R >> >> /Contents {content} 0 R >>");
            kids.Add($"{page} 0 R");
        }

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{string.Join(' ', kids)}] /Count {pageCount} >>");
        return builder.Build(catalog);
    }

    /// <summary>One page whose text is set in a font that embeds an unreadable program.</summary>
    private static byte[] PageUsingEmbeddedFont(string programKey, int flags, string? toUnicode)
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int program = builder.AddStream("/Length1 64", "this is not a font program");
        int descriptor = builder.AddObject(
            $"<< /Type /FontDescriptor /FontName /ABCDEF+ConfidentialProject /Flags {flags} {programKey} {program} 0 R >>");

        string map = toUnicode is null
            ? string.Empty
            : $" /ToUnicode {builder.AddStream(string.Empty, toUnicode)} 0 R";

        int font = builder.AddObject(
            "<< /Type /Font /Subtype /TrueType /BaseFont /ABCDEF+ConfidentialProject " +
            $"/Encoding /WinAnsiEncoding /FontDescriptor {descriptor} 0 R{map} >>");
        int content = builder.AddStream(string.Empty, PdfFileBuilder.ShowText("Body"));

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, Page(pages, font, content));

        return builder.Build(catalog);
    }
}
