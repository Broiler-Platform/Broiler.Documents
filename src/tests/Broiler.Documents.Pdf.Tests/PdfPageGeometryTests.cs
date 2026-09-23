using System.Text;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers the page a PDF is stated on: the size its pages are displayed at, and
/// margins where its content sits.
/// </summary>
/// <remarks>
/// A landscape course booklet came back with no page at all, and everything
/// downstream fell back to a page of its own: rendered on A4 portrait, its
/// tables were squeezed into a column not two-thirds their width, and words the
/// page had never broken broke mid-word. The fixtures' font declares no
/// <c>/Widths</c>, so every glyph is half an em wide and the tests can say
/// exactly where each line stops.
/// </remarks>
public sealed class PdfPageGeometryTests
{
    // ---- the size -------------------------------------------------------------

    [Fact]
    public void A_Landscape_Page_Is_Stated_As_Landscape()
    {
        PageGeometry page = PageOf(Build(new Page("0 0 842 595", Show("Landscape", 72, 500))));

        Assert.Equal(842, page.Width, 2);
        Assert.Equal(595, page.Height, 2);
        Assert.True(page.IsLandscape);
    }

    [Fact]
    public void A_Portrait_Page_Is_Stated_As_Portrait()
    {
        PageGeometry page = PageOf(PdfFileBuilder.SinglePage(PdfFileBuilder.ShowText("Portrait")));

        Assert.Equal(612, page.Width, 2);
        Assert.Equal(792, page.Height, 2);
        Assert.False(page.IsLandscape);
    }

    [Fact]
    public void A_Page_Turned_By_Its_Rotate_Entry_Is_Stated_As_Displayed()
    {
        // The other way to make a landscape page: a tall box a viewer turns.
        PageGeometry page = PageOf(Build(new Page("0 0 595 842", Show("Turned", 72, 500), " /Rotate 90")));

        Assert.Equal(842, page.Width, 2);
        Assert.Equal(595, page.Height, 2);
    }

    [Fact]
    public void The_Visible_Box_Is_The_Page()
    {
        PageGeometry page = PageOf(Build(new Page("0 0 1000 1000", Show("Cropped", 150, 800), " /CropBox [100 100 712 892]")));

        Assert.Equal(612, page.Width, 2);
        Assert.Equal(792, page.Height, 2);
    }

    [Fact]
    public void A_Crop_Box_Reaching_Past_The_Media_Box_Is_Clipped_To_It()
    {
        // What lies outside the media box is not on the page at all (clause 14.11.2).
        PageGeometry page = PageOf(Build(new Page("0 0 612 792", Show("Clipped", 72, 700), " /CropBox [-50 -50 700 900]")));

        Assert.Equal(612, page.Width, 2);
        Assert.Equal(792, page.Height, 2);
    }

    [Fact]
    public void A_Page_Measured_In_Larger_Units_Is_Stated_In_Points()
    {
        // /UserUnit 2 makes every unit of the page two points, text included.
        PdfReadResult result = Read(Build(new Page("0 0 612 792", Show("Large", 72, 700, size: 12), " /UserUnit 2")));
        PageGeometry page = Assert.IsType<PageGeometry>(result.Document.PageGeometry);

        Assert.Equal(1224, page.Width, 2);
        Assert.Equal(1584, page.Height, 2);
        Assert.Equal(24f, Assert.Single(result.Document.Paragraphs[0].Runs).Style.FontSize);
    }

    [Fact]
    public void Pages_Of_Different_Sizes_State_The_Size_Most_Of_Them_Share()
    {
        PdfReadResult result = Read(Build(
            new Page("0 0 612 792", Show("One", 72, 700)),
            new Page("0 0 792 612", Show("Two", 72, 500)),
            new Page("0 0 612 792", Show("Three", 72, 700))));

        PageGeometry page = Assert.IsType<PageGeometry>(result.Document.PageGeometry);
        Assert.Equal(612, page.Width, 2);
        Assert.Equal(792, page.Height, 2);

        DocumentDiagnostic note = Assert.Single(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.PageSizeMixed);
        Assert.Equal(DocumentDiagnosticSeverity.Info, note.Severity);
        Assert.Contains("2 of 3 are 612 x 792 pt (portrait) and 1 is 792 x 612 pt (landscape), on page 2", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Pages_Of_One_Size_Raise_No_Note()
    {
        PdfReadResult result = Read(Build(
            new Page("0 0 842 595", Show("One", 72, 500)),
            new Page("0 0 841.89 595.28", Show("Two", 72, 500))));

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.PageSizeMixed);
    }

    [Fact]
    public void A_Stated_Page_Survives_A_Round_Trip()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("A landscape letter.")
            .WithPageGeometry(new PageGeometry(842, 595, 54, 54, 54, 54));

        var codec = new PdfDocumentCodec();
        using var buffer = new MemoryStream();
        codec.WritePdf(source, buffer);
        buffer.Position = 0;
        PageGeometry page = Assert.IsType<PageGeometry>(codec.ReadPdf(buffer).Document.PageGeometry);

        Assert.Equal(842, page.Width, 2);
        Assert.Equal(595, page.Height, 2);
        Assert.Equal(54, page.MarginLeft, 0);
    }

    // ---- the margins ----------------------------------------------------------

    [Fact]
    public void The_Margins_Are_Where_The_Content_Sits()
    {
        // Text starts 90 points in, and 708 points up where the first line's
        // glyphs reach; a date set to the right ends at 550; the fullest page
        // runs down to a descender two points under its last baseline, at 100.
        PageGeometry page = PageOf(Build(
            new Page("0 0 612 792", Show("Body text on the first page", 90, 700, size: 10) +
                                   Show("Page one", 510, 400, size: 10) +
                                   Show("Last line of the first page", 90, 100, size: 10)),
            new Page("0 0 612 792", Show("Body text on the second page", 90, 700, size: 10))));

        Assert.Equal(90, page.MarginLeft, 1);
        Assert.Equal(62, page.MarginRight, 1);
        Assert.Equal(84, page.MarginTop, 1);
        Assert.Equal(98, page.MarginBottom, 1);
    }

    [Fact]
    public void A_Table_Wider_Than_Its_Text_Widens_The_Column()
    {
        // The column is what the page used, so the table fits it at the width it
        // was drawn - which is what kept words whole on the booklet's pages.
        var content = new StringBuilder(Show("A paragraph above the table", 100, 720));
        foreach (int x in new[] { 50, 306, 561 })
            content.Append($"{x} 600 1 100 re f\n");
        foreach (int y in new[] { 600, 650, 699 })
            content.Append($"50 {y} 512 1 re f\n");
        content.Append(Show("A1", 60, 670)).Append(Show("B1", 316, 670));
        content.Append(Show("A2", 60, 620)).Append(Show("B2", 316, 620));

        RichTextDocument document = Read(Build(new Page("0 0 612 792", content.ToString()))).Document;
        PageGeometry page = Assert.IsType<PageGeometry>(document.PageGeometry);
        DocumentTable table = Assert.Single(document.Tables);

        Assert.Equal(50, page.MarginLeft, 0);
        Assert.True(page.ContentWidth >= table.TotalWidth, $"A {table.TotalWidth}pt table in a {page.ContentWidth}pt column.");
    }

    [Fact]
    public void A_Page_Of_A_Few_Short_Lines_Keeps_A_Whole_Column()
    {
        // Three lines ending far short of the edge, on the only page there is:
        // neither gap is a margin, and the margins opposite stand in for them.
        PageGeometry page = PageOf(Build(new Page(
            "0 0 612 792",
            Show("Dear reader,", 72, 700) + Show("thank you.", 72, 686) + Show("Yours", 72, 672))));

        Assert.Equal(page.MarginLeft, page.MarginRight, 2);
        Assert.Equal(page.MarginTop, page.MarginBottom, 2);
        Assert.True(page.ContentHeight > 500, $"A column {page.ContentHeight}pt tall.");
    }

    [Fact]
    public void A_Running_Footer_Sits_In_The_Bottom_Margin()
    {
        // The footer repeats, so it is running content and not the body: the
        // column ends above it, and the footer keeps its distance from the edge.
        PdfReadResult result = Read(Build(
            FooterPage("Body 1", "Confidential"),
            FooterPage("Body 2", "Confidential"),
            FooterPage("Body 3", "Confidential")));
        PageGeometry page = Assert.IsType<PageGeometry>(result.Document.PageGeometry);

        Assert.False(result.Document.RunningContent.IsEmpty);
        Assert.Equal(147.6, page.MarginBottom, 1);
        Assert.Equal(37.6, page.FooterDistance, 1);
    }

    [Fact]
    public void A_Footer_Kept_In_The_Body_Is_Inside_The_Column()
    {
        // A folio differs page by page and stays in the body, so the column
        // reaches down to it.
        PageGeometry page = PageOf(Build(
            FooterPage("Body 1", "Page 1"),
            FooterPage("Body 2", "Page 2"),
            FooterPage("Body 3", "Page 3")));

        Assert.Equal(37.6, page.MarginBottom, 1);
    }

    [Fact]
    public void A_Page_With_Nothing_On_It_Still_States_Its_Size()
    {
        PageGeometry page = PageOf(Build(new Page("0 0 842 595", string.Empty)));

        Assert.Equal(842, page.Width, 2);
        Assert.Equal(72, page.MarginLeft, 2);
        Assert.True(page.IsUsable);
    }

    // ---- fixtures -------------------------------------------------------------

    private static PdfReadResult Read(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, null);
    }

    private static PageGeometry PageOf(byte[] pdf) =>
        Assert.IsType<PageGeometry>(Read(pdf).Document.PageGeometry);

    /// <summary>A run at whole-point coordinates, which every culture writes the same way.</summary>
    private static string Show(string text, int x, int y, int size = 12) =>
        PdfFileBuilder.ShowText(text, x, y, size);

    /// <summary>A body line at the top, one near the foot, and an artifact band below both.</summary>
    private static Page FooterPage(string body, string footer) => new(
        "0 0 612 792",
        Show(body, 72, 700) + Show("The last line of the body", 72, 150) +
        "/Artifact BMC\n" + Show(footer, 72, 40) + "EMC\n");

    /// <summary>One page: its media box, its content, and any further page entries.</summary>
    private sealed record Page(string MediaBox, string Content, string Extra = "");

    private static byte[] Build(params Page[] pages)
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int tree = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var kids = new List<string>(pages.Length);
        foreach (Page page in pages)
        {
            int stream = builder.AddStream(string.Empty, page.Content);
            int number = builder.AddObject(
                $"<< /Type /Page /Parent {tree} 0 R /MediaBox [{page.MediaBox}] " +
                $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {stream} 0 R{page.Extra} >>");
            kids.Add($"{number} 0 R");
        }

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        builder.SetObject(tree, $"<< /Type /Pages /Kids [{string.Join(' ', kids)}] /Count {pages.Length} >>");
        return builder.Build(catalog);
    }
}
