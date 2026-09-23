using System.Globalization;
using System.Text;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers the running heads and footers a document marks as page furniture:
/// carried once as the document's running content where they repeat, and kept
/// in the body where they do not.
/// </summary>
/// <remarks>
/// PDF 32000-1 §14.8.2.2 lets a document say which text is not part of its
/// content - an <c>/Artifact</c>. Kept in the body, a footer on every page came
/// back once per page between the paragraphs, and it stood between the halves
/// of any table the page boundary cut, so no such table was ever joined.
/// </remarks>
public sealed class PdfPageFurnitureTests
{
    [Fact]
    public void A_Footer_Repeated_On_Every_Page_Becomes_The_Documents_Footer()
    {
        RichTextDocument document = Read(Pages(3, page => "Confidential")).Document;

        RichTextParagraph footer = Assert.Single(document.RunningContent.Footer(PageSelection.Default));
        Assert.Equal("Confidential", footer.Text);
        Assert.DoesNotContain("Confidential", document.PlainText, StringComparison.Ordinal);
        Assert.Equal(["Body 1", "Body 2", "Body 3"], document.Paragraphs.Select(p => p.Text));
    }

    [Fact]
    public void A_Head_Repeated_On_Every_Page_Becomes_The_Documents_Header()
    {
        RichTextDocument document = Read(Pages(2, footer: null, header: page => "Running head")).Document;

        Assert.Equal("Running head", Assert.Single(document.RunningContent.Header(PageSelection.Default)).Text);
        Assert.DoesNotContain("Running head", document.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Footer_That_Differs_Page_By_Page_Stays_In_The_Body()
    {
        // A folio counts the pages, and the model has nowhere to hold text that
        // varies from one page to the next. It stays where it was drawn.
        RichTextDocument document = Read(Pages(3, page => $"Page {page}")).Document;

        Assert.True(document.RunningContent.IsEmpty);
        Assert.Equal(
            ["Body 1", "Page 1", "Body 2", "Page 2", "Body 3", "Page 3"],
            document.Paragraphs.Select(p => p.Text));
    }

    [Fact]
    public void A_First_Page_With_A_Footer_Of_Its_Own_Is_Stated_As_Such()
    {
        // A letterhead: page one says one thing, every page after it another.
        RichTextDocument document = Read(Pages(3, page => page == 1 ? "Letterhead" : "Continued")).Document;
        RunningContent running = document.RunningContent;

        Assert.True(running.DifferentFirstPage);
        Assert.Equal("Letterhead", Assert.Single(running.Footer(PageSelection.First)).Text);
        Assert.Equal("Continued", Assert.Single(running.Footer(PageSelection.Default)).Text);
    }

    [Fact]
    public void A_Single_Page_Keeps_Its_Furniture_In_The_Body()
    {
        // Repetition is the evidence, and one page repeats nothing.
        RichTextDocument document = Read(Pages(1, page => "Confidential")).Document;

        Assert.True(document.RunningContent.IsEmpty);
        Assert.Contains("Confidential", document.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Table_Continues_Across_A_Running_Footer()
    {
        // The footer is below the first half of the table, and it is furniture:
        // nothing the body drew stands between the halves.
        PdfReadResult result = Read(ContinuedTable(footer: "Confidential"));

        DocumentTable table = Assert.Single(result.Document.Tables);
        Assert.Equal(4, table.Rows.Count);
        Assert.Equal("Confidential", Assert.Single(result.Document.RunningContent.Footer(PageSelection.Default)).Text);
    }

    [Fact]
    public void A_Folio_Kept_In_The_Body_Is_Not_Set_Inside_A_Joined_Table()
    {
        // Text that varies goes back where it was drawn - but where it was
        // drawn is now the middle of one table, and the model holds a table as
        // one unbroken run of paragraphs.
        PdfReadResult result = Read(ContinuedTable(footer: null, folios: true));

        DocumentTable table = Assert.Single(result.Document.Tables);
        List<string> paragraphs = result.Document.Paragraphs.Select(p => p.Text).ToList();

        Assert.True(paragraphs.IndexOf("Page 1") >= table.ParagraphEnd, "The first folio follows the joined table.");
        Assert.True(paragraphs.IndexOf("Page 2") > paragraphs.IndexOf("Page 1"), "The folios keep their order.");
        for (int i = table.ParagraphIndex; i < table.ParagraphEnd; i++)
            Assert.DoesNotContain("Page", paragraphs[i], StringComparison.Ordinal);
    }

    [Fact]
    public void The_Diagnostic_Says_The_Footer_Was_Carried_Once()
    {
        DocumentDiagnostic note = Assert.Single(
            Read(Pages(3, page => "Confidential")).Diagnostics,
            d => d.Code == PdfDiagnosticCodes.ReadingOrderHeuristic);

        Assert.Contains("a running footer the same on all 3 pages", note.Message, StringComparison.Ordinal);
        Assert.Contains("carried once, as the document's footer", note.Message, StringComparison.Ordinal);
    }

    // ---- fixtures -------------------------------------------------------------

    private static PdfReadResult Read(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, null);
    }

    /// <summary>
    /// Pages each carrying "Body n", with an artifact footer below it and an
    /// artifact header above it where the functions give one.
    /// </summary>
    private static byte[] Pages(int count, Func<int, string>? footer, Func<int, string>? header = null)
    {
        var contents = new string[count];
        for (int page = 1; page <= count; page++)
        {
            var content = new StringBuilder();
            if (header is not null)
                content.Append("/Artifact BMC\n").Append(PdfFileBuilder.ShowText(header(page), y: 760)).Append("EMC\n");

            content.Append(PdfFileBuilder.ShowText($"Body {page}", y: 700));

            if (footer is not null)
                content.Append("/Artifact BMC\n").Append(PdfFileBuilder.ShowText(footer(page), y: 40)).Append("EMC\n");

            contents[page - 1] = content.ToString();
        }

        return Build(contents);
    }

    /// <summary>
    /// Two pages, each carrying half of one 2-column table drawn to the same
    /// grid, with a furniture footer - the same one, or a folio - under each.
    /// </summary>
    private static byte[] ContinuedTable(string? footer, bool folios = false)
    {
        var contents = new string[2];
        for (int page = 1; page <= 2; page++)
        {
            var content = new StringBuilder();
            foreach (int x in new[] { 72, 222, 372 })
                content.Append(CultureInfo.InvariantCulture, $"{x} 600 0.75 100 re f\n");
            foreach (int y in new[] { 600, 650, 700 })
                content.Append(CultureInfo.InvariantCulture, $"72 {y} 300 0.75 re f\n");

            int first = (page * 2) - 1;
            content.Append(PdfFileBuilder.ShowText($"A{first}", x: 80, y: 670));
            content.Append(PdfFileBuilder.ShowText($"B{first}", x: 230, y: 670));
            content.Append(PdfFileBuilder.ShowText($"A{first + 1}", x: 80, y: 620));
            content.Append(PdfFileBuilder.ShowText($"B{first + 1}", x: 230, y: 620));

            string? band = folios ? $"Page {page}" : footer;
            if (band is not null)
                content.Append("/Artifact BMC\n").Append(PdfFileBuilder.ShowText(band, y: 40)).Append("EMC\n");

            contents[page - 1] = content.ToString();
        }

        return Build(contents);
    }

    private static byte[] Build(string[] contents)
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int font = builder.AddObject(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var kids = new List<string>(contents.Length);
        foreach (string content in contents)
        {
            int stream = builder.AddStream(string.Empty, content);
            int page = builder.AddObject(
                $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
                $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {stream} 0 R >>");
            kids.Add($"{page} 0 R");
        }

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{string.Join(' ', kids)}] /Count {contents.Length} >>");
        return builder.Build(catalog);
    }
}
