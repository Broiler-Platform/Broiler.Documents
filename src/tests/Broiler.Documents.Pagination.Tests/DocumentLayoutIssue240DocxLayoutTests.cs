using Broiler.Graphics.Color;
using Broiler.Graphics.RenderList;

namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Issue #240, DOCX layout cluster: body paragraph borders take room above and
/// below the text, join between neighbours that share them, and are drawn.
/// The numbers are LibreOffice 24.2's, measured on cut-down DOCX copies (d1 to
/// d10 in docs/notes/office-issue-240-docx-layout.md).
/// </summary>
public sealed class DocumentLayoutIssue240DocxLayoutTests
{
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    /// <summary>A 3 pt line 1 pt from the text above and below, 4 pt beside it.</summary>
    private static readonly ParagraphBorder Box = new(
        new CellBorders(new TableBorder(BColor.Black, 3), new TableBorder(BColor.Black, 3),
            new TableBorder(BColor.Black, 3), new TableBorder(BColor.Black, 3)),
        new TableCellPadding(4, 1, 4, 1));

    private static RichTextParagraph Line(string text, ParagraphBorder? border = null, double before = 0, double after = 0) =>
        RichTextParagraph.Create(text, InlineStyle.Default, ParagraphStyle.Default with
        {
            Border = border,
            SpacingBefore = (float)before,
            SpacingAfter = (float)after,
            LineHeightRule = LineHeightRule.Exact,
            LineHeight = 12,
        });

    private static RichTextDocument Document(bool laysOut, params RichTextParagraph[] paragraphs) =>
        RichTextDocument.FromParagraphs(paragraphs)
            .WithStyleDefaults(DocumentStyleDefaults.Default with { LaysOutBodyParagraphBorders = laysOut, CollapsesParagraphSpacing = true });

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, Letter);
    }

    private static IReadOnlyList<BRenderCommand> Paint(LayoutPage page)
    {
        using var images = new ImageStore();
        var list = new BRenderList();
        new DocumentPagePainter(new LayoutSettings(), images).Paint(page, Letter, list, _ => null);
        return list.Commands;
    }

    [Fact]
    public void A_Border_Takes_Its_Line_And_Distance_Above_And_Below()
    {
        // d1: a bordered paragraph between two plain ones stands 4 pt lower and
        // pushes the next 4 pt further down.
        IReadOnlyList<LayoutLine> lines = Assert.Single(Layout(Document(true,
            Line("before"), Line("boxed", Box), Line("after"))).Pages).Lines;

        Assert.Equal(12 + 4, lines[1].Top - lines[0].Top, 3);
        Assert.Equal(12 + 4, lines[2].Top - lines[1].Top, 3);
        Assert.Equal(Box, lines[1].Border);
        Assert.Null(lines[0].Border);
    }

    [Fact]
    public void Neighbours_With_The_Same_Border_Share_One_Box()
    {
        // d1 and d4: no room between two bordered paragraphs, and the space
        // between them collapses as usual.
        IReadOnlyList<LayoutLine> lines = Assert.Single(Layout(Document(true,
            Line("one", Box, 10, 10), Line("two", Box, 10, 10), Line("after"))).Pages).Lines;

        Assert.Equal(12 + 10, lines[1].Top - lines[0].Top, 3);
        Assert.Equal(12 + 4 + 10, lines[2].Top - lines[1].Top, 3);
        Assert.True(lines[1].BorderJoinsPrevious);
    }

    [Fact]
    public void Borders_Take_No_Room_Where_The_Document_Does_Not_Lay_Them_Out()
    {
        // An ODT paragraph's border stands inside its margins, which the body
        // layout does not model; it keeps its earlier layout.
        IReadOnlyList<LayoutLine> lines = Assert.Single(Layout(Document(false,
            Line("before"), Line("boxed", Box))).Pages).Lines;

        Assert.Equal(12, lines[1].Top - lines[0].Top, 3);
        Assert.Null(lines[1].Border);
    }

    [Fact]
    public void A_Paragraph_Opening_The_Page_Starts_Below_Its_Border()
    {
        // d5: the first paragraph's text starts 4 pt below the top margin.
        LayoutLine line = Assert.Single(Assert.Single(Layout(Document(true, Line("boxed", Box))).Pages).Lines);

        Assert.Equal(72 + 4, line.Top, 3);
    }

    [Fact]
    public void Atop_Columns_The_Border_Room_Counts_Twice()
    {
        // d5 with two columns: Writer gives the section the upper space of its
        // first paragraph, border included, and the paragraph its own as well.
        RichTextDocument document = Document(true, Line("boxed", Box))
            .WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72, ColumnCount: 2, ColumnGap: 36));

        LayoutLine line = Assert.Single(Assert.Single(Layout(document).Pages).Lines);

        Assert.Equal(72 + 8, line.Top, 3);
    }

    [Fact]
    public void The_Box_Is_Drawn_Round_Joined_Paragraphs()
    {
        LayoutPage page = Assert.Single(Layout(Document(true, Line("one", Box), Line("two", Box))).Pages);

        // One box: its top edge 4 pt above the first line, its bottom edge
        // ending 4 pt below the second, both 3 pt thick.
        var fills = Paint(page).OfType<BRenderCommand.FillRect>().Select(command => command.Rect).ToList();
        Assert.Contains(fills, rect => Math.Abs(rect.Top - (page.Lines[0].Top - 4)) < 0.01 && Math.Abs(rect.Height - 3) < 0.01);
        Assert.Contains(fills, rect => Math.Abs(rect.Bottom - (page.Lines[1].Top + 12 + 4)) < 0.01 && Math.Abs(rect.Height - 3) < 0.01);
        Assert.Equal(2, fills.Count(rect => Math.Abs(rect.Height - 3) < 0.01));
    }
}
