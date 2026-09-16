using Broiler.Documents.Model;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers a table the document only partly ruled: rules across it, nothing
/// between its columns.
/// </summary>
/// <remarks>
/// This is the path that reads divisions off the text, so the tests that matter
/// most here are the ones asserting it does <em>not</em> fire. A stack of rules
/// is the anchor; without one, alignment is just alignment.
/// </remarks>
public sealed class PdfPartialTableTests
{
    [Fact]
    public void Rules_Across_A_Table_Are_Enough_To_Find_Its_Columns()
    {
        DocumentTable table = Assert.Single(Read(HeaderRuled()).Document.Tables);

        Assert.Equal(3, table.Rows.Count);
        Assert.Equal(2, table.Rows[0].Cells.Count);
    }

    [Fact]
    public void Each_Cell_Holds_Its_Own_Text()
    {
        PdfReadResult result = Read(HeaderRuled());
        DocumentTable table = Assert.Single(result.Document.Tables);

        Assert.Equal("Item", CellText(result, table, 0, 0));
        Assert.Equal("Cost", CellText(result, table, 0, 1));
        Assert.Equal("Pen", CellText(result, table, 1, 0));
        Assert.Equal("1.20", CellText(result, table, 1, 1));
        Assert.Equal("Ink", CellText(result, table, 2, 0));
        Assert.Equal("9.99", CellText(result, table, 2, 1));
    }

    [Fact]
    public void A_Band_Of_Several_Lines_Is_Several_Rows()
    {
        // Only three rules are drawn, and two of the three rows are inside one
        // band. A pass that took bands for rows would return two.
        DocumentTable table = Assert.Single(Read(HeaderRuled()).Document.Tables);

        Assert.Equal(3, table.Rows.Count);
    }

    [Fact]
    public void An_Inferred_Division_Carries_No_Border()
    {
        // The rule under the header is real and is carried. Nothing was drawn
        // between the columns, so nothing is claimed there: inferring where a
        // column starts is not the same as inventing a line down it.
        DocumentTable table = Assert.Single(Read(HeaderRuled()).Document.Tables);

        Assert.NotEqual(TableBorder.None, table.Rows[0].Cells[0].Bottom());
        Assert.Equal(TableBorder.None, table.Rows[0].Cells[0].Borders.Right);
        Assert.Equal(TableBorder.None, table.Rows[1].Cells[1].Borders.Left);
    }

    [Fact]
    public void The_Diagnostic_Separates_Inferred_From_Ruled()
    {
        DocumentDiagnostic note = Assert.Single(
            Read(HeaderRuled()).Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.TableReconstructed));

        Assert.Contains("only partly ruled", note.Message, StringComparison.Ordinal);
        Assert.Contains("read off the alignment", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Fully_Ruled_Grid_Is_Still_Reported_As_Ruled()
    {
        // The strict path keeps its meaning: a reader must be able to tell the
        // document's own lattice from this build's reading of the text.
        DocumentDiagnostic note = Assert.Single(
            Read(FullyRuled()).Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.TableReconstructed));

        Assert.Contains("Every one was fully ruled", note.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("read off the alignment", note.Message, StringComparison.Ordinal);
    }

    // ---- what the anchor refuses ----------------------------------------------

    [Fact]
    public void Two_Rules_Are_Not_A_Stack()
    {
        // A rule above and below a block of text is a frame, not a table. Three
        // is the first count that puts a rule strictly inside the region.
        PdfReadResult result = Read(HeaderRuled(rules: 2));

        Assert.Empty(result.Document.Tables);
    }

    [Fact]
    public void Rules_That_Do_Not_Overlap_Are_Not_One_Tables()
    {
        // Three rules down a page that share no run between them: a heading rule
        // here, a footer rule there. Stacking is about overlap, not count.
        PdfReadResult result = Read(HeaderRuled(staggered: true));

        Assert.Empty(result.Document.Tables);
    }

    [Fact]
    public void Prose_Under_A_Stack_Of_Rules_Has_No_Columns()
    {
        // The anchor is present and the text is not a table: every line runs the
        // width of the region, so no lane crosses it and nothing is claimed.
        PdfReadResult result = Read(HeaderRuled(prose: true));

        Assert.Empty(result.Document.Tables);
    }

    // ---- fixtures -------------------------------------------------------------

    private static PdfReadResult Read(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, null);
    }

    private static string CellText(PdfReadResult result, DocumentTable table, int row, int column)
    {
        TableCell cell = table.Rows[row].Cells[column];
        return string.Join(
            "\n",
            Enumerable.Range(cell.ParagraphIndex, cell.ParagraphCount)
                .Select(i => result.Document.Paragraphs[i].Text));
    }

    /// <summary>
    /// Three rules across x 72..372 at y 700, 670 and 600, with two columns of
    /// text and nothing drawn between them.
    /// </summary>
    private static byte[] HeaderRuled(int rules = 3, bool staggered = false, bool prose = false)
    {
        var content = new System.Text.StringBuilder();

        int[] ys = rules == 2 ? [700, 600] : [700, 670, 600];
        foreach (int y in ys)
        {
            // Staggered rules share no run, so they stack into nothing.
            int x = staggered && y == 670 ? 380 : 72;
            content.Append(System.Globalization.CultureInfo.InvariantCulture, $"{x} {y} 200 0.75 re f\n");
        }

        if (prose)
        {
            // Lines that run the full width leave no lane to read a column from.
            content.Append(PdfFileBuilder.ShowText("The quick brown fox jumped", x: 80, y: 680));
            content.Append(PdfFileBuilder.ShowText("over the lazy dog and away", x: 80, y: 650));
            content.Append(PdfFileBuilder.ShowText("into the woods before dawn", x: 80, y: 630));
        }
        else
        {
            content.Append(PdfFileBuilder.ShowText("Item", x: 80, y: 680));
            content.Append(PdfFileBuilder.ShowText("Cost", x: 200, y: 680));
            content.Append(PdfFileBuilder.ShowText("Pen", x: 80, y: 650));
            content.Append(PdfFileBuilder.ShowText("1.20", x: 200, y: 650));
            content.Append(PdfFileBuilder.ShowText("Ink", x: 80, y: 630));
            content.Append(PdfFileBuilder.ShowText("9.99", x: 200, y: 630));
        }

        return Page(content.ToString());
    }

    /// <summary>A 2x2 lattice with every edge painted, for the contrast.</summary>
    private static byte[] FullyRuled()
    {
        var content = new System.Text.StringBuilder();
        foreach (int x in new[] { 72, 222, 372 })
            content.Append(System.Globalization.CultureInfo.InvariantCulture, $"{x} 600 0.75 100 re f\n");
        foreach (int y in new[] { 600, 650, 700 })
            content.Append(System.Globalization.CultureInfo.InvariantCulture, $"72 {y} 300 0.75 re f\n");

        content.Append(PdfFileBuilder.ShowText("A1", x: 80, y: 670));
        content.Append(PdfFileBuilder.ShowText("B1", x: 230, y: 670));
        content.Append(PdfFileBuilder.ShowText("A2", x: 80, y: 620));
        content.Append(PdfFileBuilder.ShowText("B2", x: 230, y: 620));

        return Page(content.ToString());
    }

    private static byte[] Page(string content)
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int stream = builder.AddStream(string.Empty, content);

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {stream} 0 R >>");

        return builder.Build(catalog);
    }
}

internal static class TableCellAssertions
{
    /// <summary>The bottom border, named so a test reads as the page looks.</summary>
    public static TableBorder Bottom(this TableCell cell) => cell.Borders.Bottom;
}
