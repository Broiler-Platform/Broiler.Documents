namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers pages a viewer turns before showing them, and text drawn turned
/// against the page it is on.
/// </summary>
/// <remarks>
/// A landscape page is often a portrait box with <c>/Rotate 90</c>, its content
/// drawn up the page so that it reads across once the viewer turns it. It used
/// to be read in the unturned space, where every line runs up the page, and it
/// came back as a column of single letters - the first letter of every line,
/// then the second - with nothing reported and the read a success.
/// </remarks>
public sealed class PdfPageRotationTests
{
    private static readonly string[] Lines =
    [
        "The first paragraph of a landscape page runs",
        "across three lines of text so that there is",
        "something to join.",
        "A second paragraph follows after a gap here.",
    ];

    private static readonly string[] Paragraphs =
    [
        "The first paragraph of a landscape page runs across three lines of text so that there is something to join.",
        "A second paragraph follows after a gap here.",
    ];

    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void A_Turned_Page_Reads_The_Way_It_Is_Displayed(int rotate)
    {
        // Drawn the way a print driver draws it: one transform up front, and
        // then the page's content in the coordinates it will be seen in.
        PdfReadResult result = Read(Turned(rotate, Upright(Lines)));

        Assert.Equal(Paragraphs, result.Document.Paragraphs.Select(p => p.Text));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextOrientationUnsupported);
        Assert.Equal(DocumentResultStatus.Success, result.Status);
    }

    [Fact]
    public void Text_Turned_By_Its_Own_Matrix_Reads_Across_A_Turned_Page()
    {
        // The same page with each line's text matrix turned instead: it runs up
        // the unturned box, and across the page as displayed.
        var content = new System.Text.StringBuilder();
        int x = 95;
        for (int i = 0; i < Lines.Length; i++)
        {
            if (i == 3)
                x += 14;
            content.Append($"BT /F1 12 Tf 0 1 -1 0 {x} 72 Tm ({Lines[i]}) Tj ET\n");
            x += 14;
        }

        PdfReadResult result = Read(Page("0 0 595 842", content.ToString(), " /Rotate 90"));

        Assert.Equal(Paragraphs, result.Document.Paragraphs.Select(p => p.Text));
    }

    [Fact]
    public void A_Link_On_A_Turned_Page_Still_Covers_Its_Words()
    {
        // The annotation's /Rect is in the unturned box - x across the short
        // side, y up the long one - and the word it covers is measured on the
        // page as displayed. Unturned, the two never met.
        string annotation = "<< /Type /Annot /Subtype /Link /Rect [83 70 100 116] /A << /S /URI /URI (https://example.org/docs) >> >>";
        PdfReadResult result = Read(Turned(90, Show("Broiler", 72, 500) + Show("is linked.", 72, 470), annotation));

        StyleRun linked = Assert.Single(result.Document.Paragraphs.SelectMany(p => p.Runs), run => run.Style.IsLink);
        Assert.Equal("https://example.org/docs", linked.Style.LinkHref);
    }

    [Fact]
    public void A_Table_On_A_Turned_Page_Is_Still_A_Table()
    {
        var content = new System.Text.StringBuilder();
        foreach (int x in new[] { 72, 222, 372 })
            content.Append($"{x} 400 1 100 re f\n");
        foreach (int y in new[] { 400, 450, 499 })
            content.Append($"72 {y} 301 1 re f\n");
        content.Append(Show("A1", 80, 470)).Append(Show("B1", 230, 470));
        content.Append(Show("A2", 80, 420)).Append(Show("B2", 230, 420));

        RichTextDocument document = Read(Turned(90, content.ToString())).Document;

        DocumentTable table = Assert.Single(document.Tables);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(
            ["A1", "B1", "A2", "B2"],
            table.Rows.SelectMany(row => row.Cells).Select(cell => document.Paragraphs[cell.ParagraphIndex].Text));
    }

    // ---- text turned against its page ---------------------------------------

    [Fact]
    public void A_Sideways_Label_On_An_Upright_Page_Is_Reported()
    {
        // A label running up the margin is set on horizontal lines all the same,
        // one letter to a line, and the read no longer vouches for it.
        PdfReadResult result = Read(PdfFileBuilder.SinglePage(
            PdfFileBuilder.ShowText("The body reads across the page.") +
            "BT /F1 12 Tf 0 1 -1 0 40 300 Tm (Side ways) Tj ET\n"));

        DocumentDiagnostic note = Assert.Single(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextOrientationUnsupported);
        Assert.Equal(DocumentDiagnosticSeverity.Warning, note.Severity);
        Assert.StartsWith("8 characters of text were drawn turned against the page as it is displayed", note.Message, StringComparison.Ordinal);
        Assert.EndsWith("On page 1.", note.Message, StringComparison.Ordinal);
        Assert.Equal(DocumentResultStatus.Partial, result.Status);
    }

    [Theory]
    [InlineData("1 0 0 1", false)]
    [InlineData("0.99996 0.00873 -0.00873 0.99996", false)]
    [InlineData("0 1 -1 0", true)]
    [InlineData("0 -1 1 0", true)]
    [InlineData("-1 0 0 -1", true)]
    [InlineData("-1 0 0 1", true)]
    [InlineData("0.9848 0.1736 -0.1736 0.9848", true)]
    public void Text_Is_Turned_When_It_Runs_Any_Way_But_Across(string matrix, bool turned)
    {
        // Upright, and half a degree off it, read across. Sideways either way,
        // upside down, mirrored, and ten degrees off do not.
        PdfReadResult result = Read(PdfFileBuilder.SinglePage(
            PdfFileBuilder.ShowText("The body reads across the page.") +
            $"BT /F1 12 Tf {matrix} 72 400 Tm (Label) Tj ET\n"));

        Assert.Equal(turned, result.Diagnostics.Any(d => d.Code == PdfDiagnosticCodes.TextOrientationUnsupported));
    }

    [Fact]
    public void A_Page_Turned_Only_For_Viewing_Says_Its_Text_Runs_Sideways()
    {
        // Upright text on a box a viewer turns reads down the page as it is
        // displayed. The page is read the way it is shown, and what that does
        // to its text is said rather than passed off as a success.
        PdfReadResult result = Read(Page("0 0 595 842", Show("Turned for viewing", 72, 700), " /Rotate 90"));

        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextOrientationUnsupported);
    }

    // ---- fixtures -------------------------------------------------------------

    private static PdfReadResult Read(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, null);
    }

    /// <summary>A run at whole-point coordinates, which every culture writes the same way.</summary>
    private static string Show(string text, int x, int y) => PdfFileBuilder.ShowText(text, x, y);

    /// <summary>The lines down the displayed page, with a gap before the last.</summary>
    private static string Upright(string[] lines)
    {
        var content = new System.Text.StringBuilder();
        int y = 500;
        for (int i = 0; i < lines.Length; i++)
        {
            if (i == 3)
                y -= 14;
            content.Append(Show(lines[i], 72, y));
            y -= 14;
        }

        return content.ToString();
    }

    /// <summary>
    /// A 595 by 842 box turned by <paramref name="rotate"/>, whose content is
    /// written in the coordinates of the page as displayed and mapped back into
    /// the box by one transform - the inverse of the viewer's turn.
    /// </summary>
    private static byte[] Turned(int rotate, string displayed, string? annotation = null)
    {
        string transform = rotate switch
        {
            90 => "0 1 -1 0 595 0",
            180 => "-1 0 0 -1 595 842",
            270 => "0 -1 1 0 0 842",
            _ => throw new ArgumentOutOfRangeException(nameof(rotate)),
        };

        return Page("0 0 595 842", $"q {transform} cm\n{displayed}Q\n", $" /Rotate {rotate}", annotation);
    }

    private static byte[] Page(string mediaBox, string content, string extra, string? annotation = null)
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int tree = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int stream = builder.AddStream(string.Empty, content);
        string annots = annotation is null ? string.Empty : $" /Annots [{builder.AddObject(annotation)} 0 R]";

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        builder.SetObject(tree, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {tree} 0 R /MediaBox [{mediaBox}] " +
            $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {stream} 0 R{extra}{annots} >>");

        return builder.Build(catalog);
    }
}
