namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Office conformance issue #242, ODT layout: raised and lowered runs
/// (tdf127316_autoEscapement2.odt) and rows tied by a merged cell taller than
/// a page (fdo114306.odt).
/// </summary>
public sealed class DocumentLayoutIssue242OdtLayoutTests
{
    private static LayoutResult Layout(RichTextDocument document, DocumentPageBox box)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, box);
    }

    private static readonly DocumentPageBox Wide = new(600, 400, 20, 20, 20, 20);

    private static LayoutLine OneLine(BaselineShift? shift)
    {
        var full = InlineStyle.Default with { FontSize = 40 };
        RichTextParagraph paragraph = RichTextParagraph.Create("Base", full).InsertText(4, "Moved", full with { Shift = shift });
        return Assert.Single(Assert.Single(Layout(RichTextDocument.FromParagraphs([paragraph]), Wide).Pages).Lines);
    }

    private static LayoutPiece Moved(LayoutLine line) => line.Pieces.Single(piece => piece.Text.Contains("Moved", StringComparison.Ordinal));

    [Fact]
    public void A_Superscript_Is_Smaller_And_Raised_And_The_Line_Keeps_Its_Height()
    {
        LayoutLine plain = OneLine(null);
        LayoutLine raised = OneLine(BaselineShift.Superscript(0.5));
        LayoutPiece piece = Moved(raised);

        Assert.Equal(20, piece.Font.Size, 3);
        Assert.True(piece.Rise > 0);
        Assert.True(Moved(raised).Width < Moved(plain).Width * 0.6);
        Assert.Equal(plain.Height, raised.Height, 3);
    }

    [Fact]
    public void An_Automatic_Superscript_Meets_The_Top_Of_The_Full_Size_Text()
    {
        LayoutLine plain = OneLine(null);
        LayoutPiece piece = Moved(OneLine(BaselineShift.Superscript(0.5)));

        // Writer's automatic superscript: its ascent, raised, is the full-size
        // text's (SwFont::CalcEsc, DFLT_ESC_AUTO_SUPER).
        Assert.Equal(Moved(plain).Ascent, piece.Ascent, 3);
    }

    [Fact]
    public void A_Subscript_Is_Lowered()
    {
        LayoutLine plain = OneLine(null);
        LayoutLine lowered = OneLine(BaselineShift.Subscript(0.5));

        Assert.True(Moved(lowered).Rise < 0);
        Assert.Equal(plain.Height, lowered.Height, 3);
    }

    [Fact]
    public void A_Stated_Offset_Is_A_Part_Of_The_Full_Size_Text_Height()
    {
        LayoutPiece plain = Moved(OneLine(null));
        LayoutPiece piece = Moved(OneLine(new BaselineShift(BaselinePosition.Offset, 0.33, 0.58)));

        Assert.Equal((plain.Ascent + plain.Descent) * 0.33, piece.Rise, 3);
    }

    private static RichTextParagraph Para(string text) => RichTextParagraph.Create(text, InlineStyle.Default,
        ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10 });

    [Fact]
    public void Rows_Tied_By_A_Merged_Cell_Taller_Than_A_Page_Break_Across_It()
    {
        // fdo114306.odt: the rows a merged cell covers are cut on the merged
        // cell's line boundary and it carries on at the top of the next page.
        // They were kept whole and the cell ran off the page's foot.
        var paragraphs = Enumerable.Range(0, 12).Select(i => Para("A" + i)).Append(Para("B1")).Append(Para("B2")).ToList();
        var table = new DocumentTable(0, 14, [
            new TableRow([new TableCell(0, 12, 0, rowSpan: 2), new TableCell(12, 1, 1)]),
            new TableRow([new TableCell(13, 1, 1)]),
        ], [80, 80], cellPadding: 0);
        LayoutResult result = Layout(RichTextDocument.FromParagraphs(paragraphs).WithTables([table]), new DocumentPageBox(200, 100, 10, 10, 10, 10));

        Assert.Equal(2, result.Pages.Count);
        Assert.All(result.Pages.SelectMany(page => page.Lines), line => Assert.True(line.Top + line.Height <= 90.001));
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 12, 13], result.Pages[0].Lines.Select(line => line.ParagraphIndex).Order());
        Assert.Equal([8, 9, 10, 11], result.Pages[1].Lines.Select(line => line.ParagraphIndex).Order());
        Assert.Equal(10, result.Pages[1].Lines.Min(line => line.Top), 3);
        // The merged cell's box continues on the second page from its top.
        Assert.Contains(result.Pages[1].Cells, cell => cell.Bounds.Left == 10 && cell.Bounds.Top == 10 && cell.Bounds.Height >= 40 - 0.001);
    }

    [Fact]
    public void Rows_Tied_By_A_Merged_Cell_That_Fit_A_Page_Still_Move_Whole()
    {
        var paragraphs = new List<RichTextParagraph> { Para("lead"), Para("lead"), Para("lead"), Para("lead"), Para("lead") };
        paragraphs.AddRange(Enumerable.Range(0, 5).Select(i => Para("A" + i)));
        paragraphs.Add(Para("B1"));
        paragraphs.Add(Para("B2"));
        var table = new DocumentTable(5, 7, [
            new TableRow([new TableCell(5, 5, 0, rowSpan: 2), new TableCell(10, 1, 1)]),
            new TableRow([new TableCell(11, 1, 1)]),
        ], [80, 80], cellPadding: 0);
        LayoutResult result = Layout(RichTextDocument.FromParagraphs(paragraphs).WithTables([table]), new DocumentPageBox(200, 100, 10, 10, 10, 10));

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal([5, 6, 7, 8, 9, 10, 11], result.Pages[1].Lines.Select(line => line.ParagraphIndex).Order());
    }
}
