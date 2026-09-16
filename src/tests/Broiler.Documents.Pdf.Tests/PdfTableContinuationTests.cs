using Broiler.Documents.Model;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers a table the page boundary cut in half.
/// </summary>
/// <remarks>
/// Joining is the one part of the table work that can merge things the document
/// kept apart, so most of what is asserted here is what stops it: a column that
/// does not line up, or anything at all drawn between the two halves.
/// </remarks>
public sealed class PdfTableContinuationTests
{
    [Fact]
    public void A_Table_Cut_By_A_Page_Boundary_Is_One_Table()
    {
        DocumentTable table = Assert.Single(Read(Continued()).Document.Tables);

        Assert.Equal(4, table.Rows.Count);
    }

    [Fact]
    public void The_Second_Half_Follows_The_First()
    {
        PdfReadResult result = Read(Continued());
        DocumentTable table = Assert.Single(result.Document.Tables);

        Assert.Equal("A1", Text(result, table.Rows[0].Cells[0]));
        Assert.Equal("A2", Text(result, table.Rows[1].Cells[0]));
        Assert.Equal("A3", Text(result, table.Rows[2].Cells[0]));
        Assert.Equal("A4", Text(result, table.Rows[3].Cells[0]));
    }

    [Fact]
    public void The_Joined_Range_Covers_Every_Cell_It_Holds()
    {
        // The model holds a table as one contiguous run of paragraphs. A join
        // that left a gap would put a cell outside the table that owns it.
        PdfReadResult result = Read(Continued());
        DocumentTable table = Assert.Single(result.Document.Tables);

        foreach (TableRow row in table.Rows)
        {
            foreach (TableCell cell in row.Cells)
            {
                Assert.True(cell.ParagraphIndex >= table.ParagraphIndex, "A cell starts inside its table.");
                Assert.True(
                    cell.ParagraphIndex + cell.ParagraphCount <= table.ParagraphEnd,
                    "A cell ends inside its table.");
            }
        }
    }

    [Fact]
    public void No_Page_Break_Is_Written_Inside_A_Joined_Table()
    {
        // The break is what made this impossible: an empty paragraph between the
        // halves leaves the range discontiguous. Asked for, it is still written
        // everywhere else - just not through the middle of a table.
        PdfReadResult result = Read(Continued(), new PdfReadOptions(mapPageBreaks: true));
        DocumentTable table = Assert.Single(result.Document.Tables);

        for (int i = table.ParagraphIndex; i < table.ParagraphEnd; i++)
            Assert.NotEqual(string.Empty, result.Document.Paragraphs[i].Text);
    }

    [Fact]
    public void A_Page_Break_Between_Pages_That_Do_Not_Join_Is_Still_Written()
    {
        PdfReadResult result = Read(
            Continued(columnsDiffer: true),
            new PdfReadOptions(mapPageBreaks: true));

        Assert.Equal(2, result.Document.Tables.Count);
        Assert.Contains(result.Document.Paragraphs, p => p.Text.Length == 0);
    }

    [Fact]
    public void The_Diagnostic_Says_A_Table_Was_Joined()
    {
        DocumentDiagnostic note = Assert.Single(
            Read(Continued()).Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.TableReconstructed));

        Assert.Contains("continued a table from the page before", note.Message, StringComparison.Ordinal);
    }

    // ---- what stops a join ----------------------------------------------------

    [Fact]
    public void Columns_That_Do_Not_Line_Up_Are_Two_Tables()
    {
        Assert.Equal(2, Read(Continued(columnsDiffer: true)).Document.Tables.Count);
    }

    [Fact]
    public void Text_Below_The_First_Half_Stops_The_Join()
    {
        // A table the page finished with, rather than one the page ran out of
        // room for. The document put something after it, so it ended.
        Assert.Equal(2, Read(Continued(textAfterFirst: true)).Document.Tables.Count);
    }

    [Fact]
    public void Text_Above_The_Second_Half_Stops_The_Join()
    {
        // A heading over the second table is the document saying it is a second
        // table. This is also what keeps two identical tables on consecutive
        // pages apart, which nothing about their columns could.
        Assert.Equal(2, Read(Continued(headingBeforeSecond: true)).Document.Tables.Count);
    }

    // ---- fixtures -------------------------------------------------------------

    private static PdfReadResult Read(byte[] pdf, PdfReadOptions? options = null)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, options);
    }

    private static string Text(PdfReadResult result, TableCell cell) =>
        string.Join(
            "\n",
            Enumerable.Range(cell.ParagraphIndex, cell.ParagraphCount)
                .Select(i => result.Document.Paragraphs[i].Text));

    /// <summary>
    /// Two pages, each carrying half of one 2-column table drawn to the same
    /// grid, with nothing else on either page.
    /// </summary>
    private static byte[] Continued(
        bool columnsDiffer = false,
        bool textAfterFirst = false,
        bool headingBeforeSecond = false)
    {
        var first = new System.Text.StringBuilder();
        Grid(first, 72, 222, 372);
        first.Append(PdfFileBuilder.ShowText("A1", x: 80, y: 670));
        first.Append(PdfFileBuilder.ShowText("B1", x: 230, y: 670));
        first.Append(PdfFileBuilder.ShowText("A2", x: 80, y: 620));
        first.Append(PdfFileBuilder.ShowText("B2", x: 230, y: 620));
        if (textAfterFirst)
            first.Append(PdfFileBuilder.ShowText("Continued overleaf", x: 72, y: 560));

        var second = new System.Text.StringBuilder();
        if (headingBeforeSecond)
            second.Append(PdfFileBuilder.ShowText("Second table", x: 72, y: 740));

        // A different middle column makes the grids disagree about their columns.
        Grid(second, 72, columnsDiffer ? 300 : 222, 372);
        second.Append(PdfFileBuilder.ShowText("A3", x: 80, y: 670));
        second.Append(PdfFileBuilder.ShowText("B3", x: 230, y: 670));
        second.Append(PdfFileBuilder.ShowText("A4", x: 80, y: 620));
        second.Append(PdfFileBuilder.ShowText("B4", x: 230, y: 620));

        return Pages(first.ToString(), second.ToString());
    }

    /// <summary>A closed 2x2 lattice from 600 to 700 at the given column edges.</summary>
    private static void Grid(System.Text.StringBuilder content, params int[] columns)
    {
        foreach (int x in columns)
            content.Append(System.Globalization.CultureInfo.InvariantCulture, $"{x} 600 0.75 100 re f\n");

        int width = columns[^1] - columns[0];
        foreach (int y in new[] { 600, 650, 700 })
            content.Append(System.Globalization.CultureInfo.InvariantCulture, $"{columns[0]} {y} {width} 0.75 re f\n");
    }

    private static byte[] Pages(params string[] contents)
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
