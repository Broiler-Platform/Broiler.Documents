namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Office conformance issue 249 (mailmerge/data/10-testing-addresses.ods,
/// 4_v01.ods): Calc prints a sheet wider than the page a run of whole columns
/// per page, leaves out pages with nothing on them, and lets text run on over
/// the empty cells beside it rather than wrap.
/// </summary>
public sealed class DocumentLayoutSheetPrintingTests
{
    /// <summary>US Letter with one-inch margins: a text area 468 points wide.</summary>
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, Letter);
    }

    /// <summary>A sheet of <paramref name="rows"/>, a cell per non-null text, in columns <paramref name="widths"/> wide.</summary>
    private static RichTextDocument Sheet(double[] widths, bool printsSheets, params string?[][] rows)
    {
        var paragraphs = new List<RichTextParagraph>();
        var tableRows = new List<TableRow>();
        foreach (string?[] row in rows)
        {
            var cells = new List<TableCell>();
            for (int c = 0; c < row.Length; c++)
            {
                if (row[c] is not string text)
                    continue;
                cells.Add(new TableCell(paragraphs.Count, 1, c, verticalAlignment: CellVerticalAlignment.Bottom));
                paragraphs.Add(RichTextParagraph.Plain(text));
            }

            tableRows.Add(new TableRow(cells, minHeight: 13, exactHeight: true));
        }

        return RichTextDocument.FromParagraphs(paragraphs)
            .WithTables([new DocumentTable(0, paragraphs.Count, tableRows, widths)])
            .WithStyleDefaults(DocumentStyleDefaults.Default with { FontSizePoints = 10, PrintsSheets = printsSheets });
    }

    private static string[] TextsOn(LayoutPage page, RichTextDocument document) =>
        [.. page.Lines.Where(line => line.ParagraphIndex >= 0)
            .Select(line => document.Paragraphs[line.ParagraphIndex].Text).Distinct()];

    [Fact]
    public void A_Sheet_Wider_Than_The_Page_Continues_A_Run_Of_Columns_Per_Page()
    {
        RichTextDocument document = Sheet([200, 200, 200, 200], printsSheets: true,
            ["A1", "B1", "C1", "D1"],
            ["A2", "B2", "C2", "D2"]);

        LayoutResult result = Layout(document);

        // 200 + 200 fit the 468 points; a third does not.
        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(["A1", "B1", "A2", "B2"], TextsOn(result.Pages[0], document));
        Assert.Equal(["C1", "D1", "C2", "D2"], TextsOn(result.Pages[1], document));
        // Each run starts at the page's left margin.
        Assert.Equal(72 + 1, result.Pages[1].Lines.SelectMany(line => line.Pieces).Min(piece => piece.X), 1);
    }

    [Fact]
    public void A_Run_Of_Empty_Columns_Prints_No_Page()
    {
        RichTextDocument document = Sheet([300, 300, 300], printsSheets: true,
            ["A1", "", "C1"],
            ["A2", null, "C2"]);

        LayoutResult result = Layout(document);

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(["A1", "A2"], TextsOn(result.Pages[0], document));
        Assert.Equal(["C1", "C2"], TextsOn(result.Pages[1], document));
    }

    [Fact]
    public void Text_Runs_On_Over_Empty_Cells_Instead_Of_Wrapping()
    {
        const string Long = "sub/lastname1 firstname1";
        RichTextDocument document = Sheet([40, 100, 100], printsSheets: true,
            [Long, null, "Last"],
            ["x", "y", "z"]);

        LayoutResult result = Layout(document);

        LayoutPage page = Assert.Single(result.Pages);
        LayoutLine line = Assert.Single(page.Lines, line => line.ParagraphIndex == 0);
        Assert.Equal(1, page.Lines.Count(candidate => candidate.ParagraphIndex == 0));
        Assert.True(line.Pieces.Max(piece => piece.X + piece.Width) > 72 + 40, "The text ran on past its 40-point column.");
    }

    [Fact]
    public void Text_In_The_Last_Column_Runs_On_Past_The_Sheet()
    {
        // datasource.ods: the sheet's one column holds a heading wider than it.
        RichTextDocument document = Sheet([40], printsSheets: true,
            ["Name with spaces, \"quotes\" and \\backslashes"],
            ["A"]);

        LayoutPage page = Assert.Single(Layout(document).Pages);
        LayoutLine line = Assert.Single(page.Lines, line => line.ParagraphIndex == 0);
        Assert.True(line.Pieces.Max(piece => piece.X + piece.Width) > 72 + 40);
    }

    [Fact]
    public void A_Table_That_Is_Not_A_Sheet_Is_Not_Cut()
    {
        RichTextDocument document = Sheet([200, 200, 200, 200], printsSheets: false,
            ["A1", "B1", "C1", "D1"]);

        Assert.Single(Layout(document).Pages);
    }
}
