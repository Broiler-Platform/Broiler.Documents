namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// The page an ODT states. ODF keeps it in a style:page-layout that a master
/// page names, which the reader never looked at — and the writer emitted a
/// hardcoded 8.5x11 inch page with one inch margins whatever the document said.
/// </summary>
public sealed class OdtPageGeometryTests
{
    private static readonly PageGeometry A4Letterhead =
        new(595.276, 841.89, 127.55, 56.7, 56.7, 56.7);

    private static RichTextDocument RoundTrip(RichTextDocument document)
    {
        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(document), writable: false);
        return new OdtDocumentCodec().Read(stream).Document;
    }

    [Fact]
    public void The_Page_Survives_A_Round_Trip()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body").WithPageGeometry(A4Letterhead);

        PageGeometry geometry = Assert.IsType<PageGeometry>(RoundTrip(source).PageGeometry);

        Assert.Equal(A4Letterhead.Width, geometry.Width, 1);
        Assert.Equal(A4Letterhead.Height, geometry.Height, 1);
        Assert.Equal(A4Letterhead.MarginLeft, geometry.MarginLeft, 1);
        Assert.Equal(A4Letterhead.MarginRight, geometry.MarginRight, 1);
    }

    [Fact]
    public void Landscape_Is_Written_As_The_Orientation_It_Is()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body")
            .WithPageGeometry(new PageGeometry(841.89, 595.276, 72, 72, 72, 72));

        string styles = StylesOf(OdtDocumentCodec.WriteToArray(source));

        Assert.Contains("landscape", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Document_Stating_No_Page_Still_Gets_A_Usable_One()
    {
        // The writer has always emitted a letter-sized page for documents that
        // state none. That guess stays exactly where it was rather than moving.
        PageGeometry geometry = Assert.IsType<PageGeometry>(
            RoundTrip(RichTextDocument.FromPlainText("body")).PageGeometry);

        Assert.Equal(612, geometry.Width, 1);
        Assert.Equal(792, geometry.Height, 1);
    }

    [Fact]
    public void A_Header_Names_Only_Styles_Its_Own_Part_Defines()
    {
        // A style reference resolves within the part that carries it. The footer
        // lives in styles.xml, so the styles it names have to be there too - they
        // were in content.xml alone, and every such document read back with a
        // warning and lost the footer's formatting.
        RichTextDocument source = RichTextDocument.FromPlainText("body").WithRunningContent(
            RunningContent.Empty.WithFooter(
                PageSelection.Default,
                [RichTextParagraph.Create(
                    "page one",
                    InlineStyle.Default,
                    ParagraphStyle.Default with { Alignment = TextAlignment.Right })]));

        DocumentReadResult result;
        using (var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(source), writable: false))
            result = new OdtDocumentCodec().Read(stream);

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "odt.styles.unknown");
        Assert.Equal(
            TextAlignment.Right,
            Assert.Single(result.Document.RunningContent.Footer(PageSelection.Default)).Style.Alignment);
    }

    private static string StylesOf(byte[] odt)
    {
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(odt));
        using var reader = new StreamReader(archive.GetEntry("styles.xml")!.Open());
        return reader.ReadToEnd();
    }
}
