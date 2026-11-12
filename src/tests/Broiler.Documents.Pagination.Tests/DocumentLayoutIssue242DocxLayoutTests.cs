namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Issue #242, DOCX layout cluster: a table breaks between the rows a merged
/// cell spans, as Writer breaks it (tdf128959.docx), and the merged cell's
/// text continues on the next page beside the rows it still spans.
/// </summary>
public sealed class DocumentLayoutIssue242DocxLayoutTests
{
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    private static RichTextParagraph Line(string text, double after = 0) =>
        RichTextParagraph.Create(text, InlineStyle.Default, ParagraphStyle.Default with
        {
            SpacingAfter = (float)after,
            LineHeightRule = LineHeightRule.Exact,
            LineHeight = 12,
        });

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, Letter);
    }

    /// <summary>
    /// A filler paragraph leaving 40 pt at the foot of the page, then a table of
    /// three exact 17 pt rows whose first cell spans all three and holds five
    /// 12 pt lines, more than the rows hold.
    /// </summary>
    private static RichTextDocument MergedCellAtFoot(bool exact)
    {
        RichTextParagraph[] paragraphs =
        [
            Line("filler", after: 648 - 12 - 40),
            Line("one"), Line("two"), Line("three"), Line("four"), Line("five"),
            Line("after"),
        ];
        TableRow Row(params TableCell[] cells) => new(cells, minHeight: 17, exactHeight: exact);
        return RichTextDocument.FromParagraphs(paragraphs).WithTables([new DocumentTable(1, 5,
        [
            Row(new TableCell(1, 5, 0, rowSpan: 3)),
            Row(new TableCell(6, 0, 0, isRowSpanContinuation: true)),
            Row(new TableCell(6, 0, 0, isRowSpanContinuation: true)),
        ], [200], cellPadding: 0)]);
    }

    [Fact]
    public void A_Table_Breaks_Between_The_Rows_A_Merged_Cell_Spans()
    {
        LayoutResult result = Layout(MergedCellAtFoot(exact: true));

        Assert.Equal(2, result.Pages.Count);
        // Two rows, 34 pt, fit in the 40 left: the two lines that end beside
        // them stay, the third would end at 36 pt and moves on.
        Assert.Equal([0, 1, 2], result.Pages[0].Lines.Select(line => line.ParagraphIndex));
        IReadOnlyList<LayoutLine> second = result.Pages[1].Lines;
        Assert.Equal([3, 4, 5, 6], second.Select(line => line.ParagraphIndex));
        Assert.Equal(72, second[0].Top, 3);
        Assert.Equal(84, second[1].Top, 3);
    }

    [Fact]
    public void The_Merged_Cells_Box_Ends_At_The_Break_And_Continues_Beside_The_Rows_Left()
    {
        LayoutResult result = Layout(MergedCellAtFoot(exact: true));

        LayoutCell before = Assert.Single(result.Pages[0].Cells);
        Assert.Equal(34, before.Bounds.Height, 3);
        LayoutCell after = Assert.Single(result.Pages[1].Cells);
        Assert.Equal(72, after.Bounds.Top, 3);
        Assert.Equal(17, after.Bounds.Height, 3);
    }

    [Fact]
    public void Rows_That_May_Grow_Grow_To_Hold_The_Rest_Of_The_Merged_Cell()
    {
        LayoutResult result = Layout(MergedCellAtFoot(exact: false));

        Assert.Equal(2, result.Pages.Count);
        LayoutCell after = Assert.Single(result.Pages[1].Cells);
        Assert.Equal(36, after.Bounds.Height, 3);
        Assert.Equal(72 + 36, result.Pages[1].Lines.Single(line => line.ParagraphIndex == 6).Top, 3);
    }
}
