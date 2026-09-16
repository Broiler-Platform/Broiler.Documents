using Broiler.Documents.Model;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers what a missing interior rule means, and what two tables on one page
/// mean.
/// </summary>
/// <remarks>
/// A rule the document did not draw between two cells is not a gap in the
/// evidence: it is the statement that those cells are one cell. These fixtures
/// leave rules out on purpose and assert the merge that follows.
/// </remarks>
public sealed class PdfTableSpanTests
{
    [Fact]
    public void A_Header_With_No_Rule_Under_Its_Middle_Spans_The_Columns()
    {
        DocumentTable table = Assert.Single(Read(Spanned()).Document.Tables);

        // One cell across the top, two beneath it.
        TableCell header = Assert.Single(table.Rows[0].Cells);
        Assert.Equal(2, header.ColumnSpan);
        Assert.Equal(2, table.Rows[1].Cells.Count);
    }

    [Fact]
    public void A_Spanning_Header_Keeps_Its_Text_And_Its_Outer_Borders()
    {
        PdfReadResult result = Read(Spanned());
        DocumentTable table = Assert.Single(result.Document.Tables);
        TableCell header = table.Rows[0].Cells[0];

        Assert.Equal("Header", Text(result, header));

        // Bounded by the outside of the block it covers, not by the lattice line
        // its top-left corner happens to sit on.
        Assert.NotEqual(TableBorder.None, header.Borders.Left);
        Assert.NotEqual(TableBorder.None, header.Borders.Right);
    }

    [Fact]
    public void The_Row_Below_A_Span_Is_Unaffected()
    {
        PdfReadResult result = Read(Spanned());
        DocumentTable table = Assert.Single(result.Document.Tables);

        Assert.Equal("A2", Text(result, table.Rows[1].Cells[0]));
        Assert.Equal("B2", Text(result, table.Rows[1].Cells[1]));
    }

    [Fact]
    public void A_Cell_With_No_Rule_Across_It_Spans_The_Rows()
    {
        DocumentTable table = Assert.Single(Read(RowSpanned()).Document.Tables);

        Assert.Equal(2, table.Rows[0].Cells[0].RowSpan);
        Assert.Equal(1, table.Rows[0].Cells[1].RowSpan);
    }

    [Fact]
    public void The_Covered_Row_Still_Has_Its_Columns()
    {
        // The model wants a cell in the lower half of a vertical merge so the row
        // counts right. It draws nothing and holds nothing.
        DocumentTable table = Assert.Single(Read(RowSpanned()).Document.Tables);

        Assert.Equal(2, table.Rows[1].Cells.Count);
        Assert.True(table.Rows[1].Cells[0].IsRowSpanContinuation);
        Assert.Equal(0, table.Rows[1].Cells[0].ParagraphCount);
        Assert.False(table.Rows[1].Cells[1].IsRowSpanContinuation);
    }

    [Fact]
    public void A_Row_Spanning_Cell_Holds_The_Text_Of_Everything_It_Covers()
    {
        PdfReadResult result = Read(RowSpanned());
        DocumentTable table = Assert.Single(result.Document.Tables);

        // Two lines drawn in the region the merge covers, both of them its text.
        string merged = Text(result, table.Rows[0].Cells[0]);
        Assert.Contains("Over", merged, StringComparison.Ordinal);
        Assert.Contains("Under", merged, StringComparison.Ordinal);
    }

    // ---- more than one table on a page ----------------------------------------

    [Fact]
    public void Two_Tables_On_A_Page_Are_Two_Tables()
    {
        // Both used to be lost: every rule on the page went into one candidate
        // lattice, and a lattice spanning two tables is never closed.
        Assert.Equal(2, Read(TwoTables()).Document.Tables.Count);
    }

    [Fact]
    public void The_Upper_Table_Comes_First()
    {
        PdfReadResult result = Read(TwoTables());
        IReadOnlyList<DocumentTable> tables = result.Document.Tables;

        Assert.True(
            tables[0].ParagraphIndex < tables[1].ParagraphIndex,
            "Tables are read down the page.");
        Assert.Equal("A1", Text(result, tables[0].Rows[0].Cells[0]));
        Assert.Equal("C1", Text(result, tables[1].Rows[0].Cells[0]));
    }

    [Fact]
    public void Text_Between_Two_Tables_Stays_Between_Them()
    {
        PdfReadResult result = Read(TwoTables(caption: true));
        IReadOnlyList<DocumentTable> tables = result.Document.Tables;

        int caption = -1;
        for (int i = 0; i < result.Document.Paragraphs.Count; i++)
        {
            if (result.Document.Paragraphs[i].Text == "Caption")
                caption = i;
        }

        Assert.True(caption > tables[0].ParagraphEnd - 1, "The caption follows the first table.");
        Assert.True(caption < tables[1].ParagraphIndex, "The caption precedes the second.");
    }

    [Fact]
    public void A_Grid_Inside_A_Cell_Does_Not_Become_A_Second_Table()
    {
        // Separating rules by region made a nested grid findable, and findable is
        // not carried: kept as a sibling it would be a table claiming paragraphs
        // the outer one already holds. It is dropped, and its text stays in the
        // cell it was drawn in.
        PdfReadResult result = Read(Nested());
        DocumentTable table = Assert.Single(result.Document.Tables);

        Assert.Equal(2, table.Rows.Count);
        Assert.Contains("Inner", Text(result, table.Rows[0].Cells[0]), StringComparison.Ordinal);
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
    /// A 2x2 grid whose middle vertical rule stops at the second row, so the top
    /// row is one cell across.
    /// </summary>
    private static byte[] Spanned()
    {
        var content = new System.Text.StringBuilder();
        Rule(content, 72, 600, 0.75, 100);
        Rule(content, 372, 600, 0.75, 100);
        Rule(content, 222, 600, 0.75, 50);          // lower half only
        Rule(content, 72, 700, 300, 0.75);
        Rule(content, 72, 650, 300, 0.75);
        Rule(content, 72, 600, 300, 0.75);

        content.Append(PdfFileBuilder.ShowText("Header", x: 80, y: 670));
        content.Append(PdfFileBuilder.ShowText("A2", x: 80, y: 620));
        content.Append(PdfFileBuilder.ShowText("B2", x: 230, y: 620));
        return Page(content.ToString());
    }

    /// <summary>
    /// A 2x2 grid whose middle horizontal rule covers only the right column, so
    /// the left column is one cell down.
    /// </summary>
    private static byte[] RowSpanned()
    {
        var content = new System.Text.StringBuilder();
        Rule(content, 72, 600, 0.75, 100);
        Rule(content, 222, 600, 0.75, 100);
        Rule(content, 372, 600, 0.75, 100);
        Rule(content, 72, 700, 300, 0.75);
        Rule(content, 222, 650, 150, 0.75);         // right column only
        Rule(content, 72, 600, 300, 0.75);

        content.Append(PdfFileBuilder.ShowText("Over", x: 80, y: 670));
        content.Append(PdfFileBuilder.ShowText("Under", x: 80, y: 620));
        content.Append(PdfFileBuilder.ShowText("B1", x: 230, y: 670));
        content.Append(PdfFileBuilder.ShowText("B2", x: 230, y: 620));
        return Page(content.ToString());
    }

    /// <summary>Two closed 2x2 grids that share no rule, one above the other.</summary>
    private static byte[] TwoTables(bool caption = false)
    {
        var content = new System.Text.StringBuilder();

        foreach ((int bottom, string prefix) in new[] { (600, "A"), (400, "C") })
        {
            foreach (int x in new[] { 72, 222, 372 })
                Rule(content, x, bottom, 0.75, 100);
            foreach (int y in new[] { bottom, bottom + 50, bottom + 100 })
                Rule(content, 72, y, 300, 0.75);

            content.Append(PdfFileBuilder.ShowText(prefix + "1", x: 80, y: bottom + 70));
            content.Append(PdfFileBuilder.ShowText(prefix + "2", x: 80, y: bottom + 20));
        }

        if (caption)
            content.Append(PdfFileBuilder.ShowText("Caption", x: 72, y: 560));

        return Page(content.ToString());
    }

    /// <summary>A closed 2x2 grid with a second, smaller closed grid inside one cell.</summary>
    private static byte[] Nested()
    {
        var content = new System.Text.StringBuilder();
        foreach (int x in new[] { 72, 222, 372 })
            Rule(content, x, 600, 0.75, 100);
        foreach (int y in new[] { 600, 650, 700 })
            Rule(content, 72, y, 300, 0.75);

        // Wholly inside the top-left cell, touching none of the outer rules.
        foreach (int x in new[] { 90, 130, 170 })
            Rule(content, x, 660, 0.75, 30);
        foreach (int y in new[] { 660, 675, 690 })
            Rule(content, 90, y, 80, 0.75);

        content.Append(PdfFileBuilder.ShowText("Inner", x: 95, y: 665));
        content.Append(PdfFileBuilder.ShowText("B1", x: 230, y: 670));
        content.Append(PdfFileBuilder.ShowText("A2", x: 80, y: 620));
        content.Append(PdfFileBuilder.ShowText("B2", x: 230, y: 620));
        return Page(content.ToString());
    }

    private static void Rule(System.Text.StringBuilder content, double x, double y, double width, double height) =>
        content.Append(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{x} {y} {width} {height} re f\n");

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
