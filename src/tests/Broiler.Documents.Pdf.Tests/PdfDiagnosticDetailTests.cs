using System.IO.Compression;

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

    /// <summary>Every image that reached the document.</summary>
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

    [Theory]
    [InlineData(7, "On pages 1, 2, 3, 4, 5, 6, 7.")]
    [InlineData(9, "On pages 1, 2, 3, 4, 5, 6 and 3 more.")]
    public void A_Long_Page_List_Counts_What_It_Does_Not_Name(int pages, string expected)
    {
        // "and others" said the same thing about one more page as about ninety,
        // and one more page is shorter to name than to count.
        DocumentDiagnostic image = Only(Read(PagesDrawingOneImage(pages)), PdfDiagnosticCodes.FilterDctUnsupported);

        Assert.Contains(expected, image.Message, StringComparison.Ordinal);
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

        // An inline image is read from its declaration by design, not for want of
        // a decoder, and the note must not blame one.
        Assert.DoesNotContain("composes no image decoder", image.Message, StringComparison.Ordinal);
        Assert.Contains("The logical model carries no images", image.Message, StringComparison.Ordinal);
    }

    // ---- images this build can decode -----------------------------------------

    [Fact]
    public void A_Flate_Image_Is_Decoded_Rather_Than_Blamed_On_A_Missing_Decoder()
    {
        // The regression this covers: FlateDecode is composed by every build, so
        // a stencil mask's samples were reachable all along. Reporting it as an
        // image no decoder could reach named a gap that did not exist and hid the
        // real one. A stencil is carried now, in the fill colour; painted with a
        // pattern it has no colour to carry, and is the decoded image that stays.
        byte[] samples = Samples(240 / 8 * 240);
        PdfReadResult result = Read(DocumentWithImage(
            "/Width 240 /Height 240 /ImageMask true /BitsPerComponent 1",
            Deflate(samples),
            filter: "FlateDecode",
            prefix: "/Pattern cs /P0 scn "));

        DocumentDiagnostic decoded = Only(result, PdfDiagnosticCodes.ImageDecodedNotProjected);
        Assert.Contains("1 image was decoded", decoded.Message, StringComparison.Ordinal);
        Assert.Contains($"{samples.Length} bytes of samples", decoded.Message, StringComparison.Ordinal);
        Assert.Contains("240x240 1bpc ImageMask FlateDecode", decoded.Message, StringComparison.Ordinal);
        Assert.Contains("a stencil mask painted with a pattern", decoded.Message, StringComparison.Ordinal);

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ImageNotComposed);

        // 240 × 240 at one bit is exactly 7200 bytes, so the dictionary and the
        // samples agree. Sizing raw samples as though a codec had normalized them
        // to RGBA would have called every stencil mask a disagreement.
        Assert.DoesNotContain("disagree", decoded.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Raw_Image_Whose_Samples_Do_Not_Fill_Its_Declaration_Is_Reported_As_Disagreeing()
    {
        // The check still has to earn its keep: a stream carrying half the samples
        // its dictionary calls for is a real disagreement, and saying so is the
        // whole reason the decode is worth doing.
        PdfReadResult result = Read(DocumentWithImage(
            "/Width 8 /Height 8 /ColorSpace /DeviceGray /BitsPerComponent 8",
            Samples(8 * 4),
            filter: null));

        DocumentDiagnostic decoded = Only(result, PdfDiagnosticCodes.ImageDecodedNotProjected);
        Assert.Contains("the dictionary and the image data disagree", decoded.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_Unfiltered_Image_Is_Decoded_From_Its_Raw_Samples()
    {
        // No filter at all is the case where "composes no image decoder" was
        // furthest from true: raw samples need no decoder to be reachable, and
        // DeviceGray at eight bits is inside the approved raw-sample subset, so
        // they reach the document rather than stopping at the pipeline.
        byte[] samples = Samples(8 * 8);
        PdfReadResult result = Read(DocumentWithImage(
            "/Width 8 /Height 8 /ColorSpace /DeviceGray /BitsPerComponent 8",
            samples,
            filter: null));

        Assert.Single(ImagesIn(result));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ImageNotComposed);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.ImageDecodedNotProjected);
    }

    [Fact]
    public void An_Image_Past_The_Description_Ceiling_Is_Reported_From_Its_Dictionary()
    {
        // The decode is diagnostic work, so it is bounded, and hitting the bound
        // costs the sentence rather than the document: the text still arrives and
        // the image is still described from what it declared.
        byte[] samples = Samples(240 / 8 * 240);
        PdfReadResult result = Read(
            DocumentWithImage(
                "/Width 240 /Height 240 /ImageMask true /BitsPerComponent 1",
                Deflate(samples),
                filter: "FlateDecode"),
            new PdfReadOptions(pdfLimits: new PdfLimits(maxDescribedImageBytes: 16)));

        DocumentDiagnostic image = Only(result, PdfDiagnosticCodes.ImageNotComposed);
        Assert.Contains("240x240 1bpc ImageMask FlateDecode", image.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("composes no image decoder", image.Message, StringComparison.Ordinal);

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.Limit);
        Assert.Equal("Body", Assert.Single(result.Document.Paragraphs).Text);
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
    /// <summary>
    /// One page carrying the word "Body" and a single image XObject built from
    /// <paramref name="dictionaryBody"/> and <paramref name="data"/>.
    /// </summary>
    private static byte[] DocumentWithImage(string dictionaryBody, byte[] data, string? filter, string prefix = "")
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int image = builder.AddStream($"/Type /XObject /Subtype /Image {dictionaryBody}", data, filter);
        int content = builder.AddStream(string.Empty, "q " + prefix + "/Im0 Do Q\n" + PdfFileBuilder.ShowText("Body"));

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F1 {font} 0 R >> /XObject << /Im0 {image} 0 R >> >> " +
            $"/Contents {content} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>Sample bytes with enough variety that a round trip proves itself.</summary>
    private static byte[] Samples(int length)
    {
        byte[] samples = new byte[length];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (byte)(i * 7 & 0xFF);
        return samples;
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var compressor = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            compressor.Write(data, 0, data.Length);
        return output.ToArray();
    }

    // ---- annotations ----------------------------------------------------------

    [Fact]
    public void One_Refused_Link_Target_Is_Reported_As_One()
    {
        // "1 link targets" was hardcoded plural, and this file's whole premise is
        // that the sentence is contract too.
        DocumentDiagnostic note = Only(
            Read(PagesWithOneLinkEach("mailto:someone@example.org")),
            PdfDiagnosticCodes.UriRejected);

        Assert.Contains("1 link target did not pass", note.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("1 link targets", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refused_Link_Targets_Are_Counted_Across_Pages_Not_Per_Page()
    {
        // The defect this section exists for. The count was per page and the sink
        // keeps one entry per code, so two pages each refusing one target
        // produced "1 link targets ... Seen 2 times" - which reads as one target
        // met twice, and undercounts by exactly the number of pages involved.
        DocumentDiagnostic note = Only(
            Read(PagesWithOneLinkEach("mailto:a@example.org", "mailto:b@example.org")),
            PdfDiagnosticCodes.UriRejected);

        Assert.Contains("2 link targets did not pass", note.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Seen ", note.Message, StringComparison.Ordinal);
        Assert.Contains("On pages 1, 2.", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Refused_Link_Target_Says_Why_It_Was_Refused()
    {
        // The read side discarded the reason while the write side carried it, so
        // the same policy refusing the same value said one thing on the way in
        // and another on the way out.
        DocumentDiagnostic note = Only(
            Read(PagesWithOneLinkEach("mailto:someone@example.org")),
            PdfDiagnosticCodes.UriRejected);

        Assert.Contains("because the mailto scheme is not admitted", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Distinct_Refusal_Reasons_Are_Kept_Apart_And_Counted()
    {
        // One is answered by configuring a policy and the other by nothing at
        // all, so collapsing them into a single number tells a caller which work
        // to do only by accident.
        DocumentDiagnostic note = Only(
            Read(PagesWithOneLinkEach("mailto:a@example.org", "chapter-two", "mailto:b@example.org")),
            PdfDiagnosticCodes.UriRejected);

        Assert.Contains("3 link targets did not pass", note.Message, StringComparison.Ordinal);
        Assert.Contains("2 because the mailto scheme is not admitted", note.Message, StringComparison.Ordinal);
        Assert.Contains("1 because the value is not an absolute URI", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Refused_Link_Target_Never_Carries_The_Value()
    {
        // Naming the reason must not become a way to leak the target: the reason
        // is the policy's own sentence, and nothing the file wrote reaches it.
        PdfReadResult result = Read(PagesWithOneLinkEach("mailto:someone@example.org"));

        Assert.All(
            result.Diagnostics,
            d => Assert.DoesNotContain("someone@example.org", d.Message, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void One_Active_Annotation_Is_Reported_As_One()
    {
        DocumentDiagnostic note = Only(
            Read(PagesWithOneAnnotationEach(JavaScriptAnnotation)),
            PdfDiagnosticCodes.ActiveContentRemoved);

        Assert.Contains("1 active annotation or action was detected", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Active_Annotations_Are_Counted_Across_Pages()
    {
        DocumentDiagnostic note = Only(
            Read(PagesWithOneAnnotationEach(JavaScriptAnnotation, JavaScriptAnnotation)),
            PdfDiagnosticCodes.ActiveContentRemoved);

        Assert.Contains("2 active annotations or actions were detected", note.Message, StringComparison.Ordinal);
        Assert.Contains("On pages 1, 2.", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void One_Dropped_Destination_Is_Reported_As_One()
    {
        DocumentDiagnostic note = Only(
            Read(PagesWithOneAnnotationEach(DestinationAnnotation)),
            PdfDiagnosticCodes.LinkDestinationDropped);

        Assert.Contains("1 annotation named a place inside the document", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dropped_Destinations_Are_Counted_Across_Pages()
    {
        DocumentDiagnostic note = Only(
            Read(PagesWithOneAnnotationEach(DestinationAnnotation, DestinationAnnotation)),
            PdfDiagnosticCodes.LinkDestinationDropped);

        Assert.Contains("2 annotations named a place inside the document", note.Message, StringComparison.Ordinal);
        Assert.Contains("On pages 1, 2.", note.Message, StringComparison.Ordinal);
    }

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

    private const string JavaScriptAnnotation =
        "<< /Type /Annot /Subtype /Link /Rect [70 715 140 735] /A << /S /JavaScript /JS (x) >> >>";

    private const string DestinationAnnotation =
        "<< /Type /Annot /Subtype /Link /Rect [70 715 140 735] /Dest (chapter-two) >>";

    /// <summary>One page per target, each carrying a single URI link annotation.</summary>
    private static byte[] PagesWithOneLinkEach(params string[] targets)
    {
        var annotations = new List<string>(targets.Length);
        foreach (string target in targets)
        {
            annotations.Add(
                "<< /Type /Annot /Subtype /Link /Rect [70 715 140 735] " +
                $"/A << /S /URI /URI ({target}) >> >>");
        }

        return PagesWithOneAnnotationEach([.. annotations]);
    }

    /// <summary>
    /// One page per annotation, so that a per-page count and a document-wide one
    /// cannot agree by accident.
    /// </summary>
    private static byte[] PagesWithOneAnnotationEach(params string[] annotations)
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int font = builder.AddObject(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var kids = new List<string>(annotations.Length);
        foreach (string annotation in annotations)
        {
            int content = builder.AddStream(string.Empty, PdfFileBuilder.ShowText("Body"));
            int annot = builder.AddObject(annotation);
            int page = builder.AddObject(
                $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
                $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R " +
                $"/Annots [{annot} 0 R] >>");
            kids.Add($"{page} 0 R");
        }

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(
            pages,
            $"<< /Type /Pages /Kids [{string.Join(" ", kids)}] /Count {annotations.Length} >>");
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
