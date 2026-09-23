using System.Text;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers a closed box drawn around text: a bordered note, read as the one-cell
/// table every format this model writes holds one as.
/// </summary>
/// <remarks>
/// A box is a far weaker statement than a lattice, so most of what is asserted
/// here is what stops one being read: nothing inside it, text crossing its edge,
/// or a box so large it is the page's own border.
/// </remarks>
public sealed class PdfFrameTests
{
    [Fact]
    public void A_Closed_Frame_Around_Text_Is_A_One_Cell_Table()
    {
        PdfReadResult result = Read(Framed());

        DocumentTable table = Assert.Single(result.Document.Tables);
        TableRow row = Assert.Single(table.Rows);
        TableCell cell = Assert.Single(row.Cells);

        Assert.True(cell.Borders.IsVisible);
        Assert.Contains("Boxed note", Text(result, cell), StringComparison.Ordinal);
        Assert.Contains("Second line of the note", Text(result, cell), StringComparison.Ordinal);
    }

    [Fact]
    public void A_Frame_Keeps_Its_Place_Among_The_Paragraphs_Around_It()
    {
        PdfReadResult result = Read(Framed());
        DocumentTable table = Assert.Single(result.Document.Tables);
        List<string> paragraphs = result.Document.Paragraphs.Select(p => p.Text).ToList();

        Assert.True(paragraphs.IndexOf("Before the box") < table.ParagraphIndex, "The paragraph above the box reads first.");
        Assert.True(paragraphs.IndexOf("After the box") >= table.ParagraphEnd, "The paragraph below the box reads after it.");
    }

    [Fact]
    public void The_Diagnostic_Says_It_Was_A_Frame_Rather_Than_A_Grid()
    {
        DocumentDiagnostic note = Assert.Single(
            Read(Framed()).Diagnostics,
            d => d.Code == PdfDiagnosticCodes.TableReconstructed);

        Assert.Contains("1 closed frame around text was read as a one-cell table", note.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("fully ruled grid", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Frame_Around_Nothing_Is_Not_A_Table()
    {
        Assert.Empty(Read(Framed(withText: false)).Document.Tables);
    }

    [Fact]
    public void A_Box_A_Line_Runs_Across_Is_Not_A_Frame()
    {
        // A highlight drawn around part of a line rather than a container for
        // it: the text starts inside the box and carries on past its edge.
        Assert.Empty(Read(Framed(crossing: true)).Document.Tables);
    }

    [Fact]
    public void A_Box_Around_Most_Of_The_Page_Is_Its_Border()
    {
        // Read as a frame, a page border would put the whole page in one cell.
        Assert.Empty(Read(Framed(pageBorder: true)).Document.Tables);
    }

    // ---- fixtures -------------------------------------------------------------

    private static PdfReadResult Read(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, null);
    }

    private static string Text(PdfReadResult result, TableCell cell) =>
        string.Join(
            "\n",
            Enumerable.Range(cell.ParagraphIndex, cell.ParagraphCount)
                .Select(i => result.Document.Paragraphs[i].Text));

    /// <summary>
    /// A paragraph, a stroked box with two lines of text in it, and a paragraph
    /// below. The box runs from x 100 to 400 and y 500 to 600.
    /// </summary>
    private static byte[] Framed(bool withText = true, bool crossing = false, bool pageBorder = false)
    {
        var content = new StringBuilder();
        content.Append(PdfFileBuilder.ShowText("Before the box", y: 700));

        content.Append(pageBorder
            ? "0.5 w 20 20 572 752 re S\n"
            : "0.5 w 100 500 300 100 re S\n");

        if (withText)
        {
            content.Append(PdfFileBuilder.ShowText("Boxed note", x: 110, y: 570));
            content.Append(PdfFileBuilder.ShowText("Second line of the note", x: 110, y: 555));
        }

        if (crossing)
            content.Append(PdfFileBuilder.ShowText("This line starts in the box and runs out of it", x: 300, y: 530));

        content.Append(PdfFileBuilder.ShowText("After the box", y: 400));

        return PdfFileBuilder.SinglePage(content.ToString());
    }
}
