using Broiler.Graphics;

namespace Broiler.Documents.Model.Tests;

/// <summary>
/// A table names the paragraphs of its cells rather than holding them, so every
/// edit that adds or removes a paragraph has to move the ranges with it. This is
/// what keeps the grid around the text while it is typed in.
/// </summary>
public sealed class DocumentTableTests
{
    /// <summary>Four paragraphs in a two-by-two grid, one paragraph per cell.</summary>
    private static RichTextDocument Grid() =>
        RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain("a1"),
            RichTextParagraph.Plain("b1"),
            RichTextParagraph.Plain("a2"),
            RichTextParagraph.Plain("b2"),
        ]).WithTables([
            new DocumentTable(
                0,
                4,
                [
                    new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)]),
                    new TableRow([new TableCell(2, 1, 0), new TableCell(3, 1, 1)]),
                ],
                [100, 100]),
        ]);

    private static DocumentTable TableOf(RichTextDocument document) => Assert.Single(document.Tables);

    [Fact(Timeout = 600000)]
    public void Splitting_A_Cells_Paragraph_Grows_That_Cell()
    {
        RichTextDocument document = Grid().SplitParagraph(new RichTextPosition(0, 1)).Document;

        DocumentTable table = TableOf(document);
        Assert.Equal(5, table.ParagraphCount);
        Assert.Equal(2, table.Rows[0].Cells[0].ParagraphCount);

        // Everything after it moved by one, so no cell names a paragraph that is
        // now in a different cell.
        Assert.Equal(2, table.Rows[0].Cells[1].ParagraphIndex);
        Assert.Equal(3, table.Rows[1].Cells[0].ParagraphIndex);
        Assert.Equal(4, table.Rows[1].Cells[1].ParagraphIndex);
    }

    [Fact(Timeout = 600000)]
    public void Merging_Two_Paragraphs_In_A_Cell_Shrinks_It_Again()
    {
        RichTextDocument split = Grid().SplitParagraph(new RichTextPosition(0, 1)).Document;
        RichTextDocument document = split.MergeParagraphs(0).Document;

        DocumentTable table = TableOf(document);
        Assert.Equal(4, table.ParagraphCount);
        Assert.Equal(1, table.Rows[0].Cells[0].ParagraphCount);
        Assert.Equal(1, table.Rows[0].Cells[1].ParagraphIndex);
    }

    [Fact(Timeout = 600000)]
    public void Typing_Inside_A_Cell_Leaves_Every_Range_Alone()
    {
        RichTextDocument document = Grid().InsertText(new RichTextPosition(1, 2), "!").Document;

        DocumentTable table = TableOf(document);
        Assert.Equal(4, table.ParagraphCount);
        Assert.Equal(1, table.Rows[0].Cells[1].ParagraphIndex);
        Assert.Equal("b1!", document.Paragraphs[1].Text);
    }

    [Fact(Timeout = 600000)]
    public void A_Paragraph_Added_Before_The_Table_Moves_The_Whole_Grid()
    {
        RichTextDocument document = RichTextDocument
            .FromParagraphs([RichTextParagraph.Plain("intro"), .. Grid().Paragraphs])
            .WithTables([
                new DocumentTable(
                    1,
                    4,
                    [
                        new TableRow([new TableCell(1, 1, 0), new TableCell(2, 1, 1)]),
                        new TableRow([new TableCell(3, 1, 0), new TableCell(4, 1, 1)]),
                    ],
                    [100, 100]),
            ])
            .SplitParagraph(new RichTextPosition(0, 2))
            .Document;

        DocumentTable table = TableOf(document);
        Assert.Equal(2, table.ParagraphIndex);
        Assert.Equal(4, table.ParagraphCount);
        Assert.Equal(2, table.Rows[0].Cells[0].ParagraphIndex);
    }

    [Fact(Timeout = 600000)]
    public void A_Paragraph_Added_After_The_Table_Moves_Nothing()
    {
        RichTextDocument document = RichTextDocument
            .FromParagraphs([.. Grid().Paragraphs, RichTextParagraph.Plain("after")])
            .WithTables(Grid().Tables)
            .SplitParagraph(new RichTextPosition(4, 2))
            .Document;

        DocumentTable table = TableOf(document);
        Assert.Equal(0, table.ParagraphIndex);
        Assert.Equal(4, table.ParagraphCount);
    }

    [Fact(Timeout = 600000)]
    public void Deleting_A_Tables_Whole_Text_Leaves_The_Grid_Standing()
    {
        RichTextDocument document = Grid()
            .DeleteRange(new RichTextRange(new RichTextPosition(0, 0), new RichTextPosition(3, 2)))
            .Document;

        // Selecting a table's text and deleting it empties the cells and keeps
        // the table, which is what every word processor does with it.
        DocumentTable table = TableOf(document);
        Assert.Equal(string.Empty, document.PlainText);
        Assert.Equal(1, document.ParagraphCount);

        // And the paragraph that is left is still in a cell. One inside the
        // table's range but in no cell would be laid out nowhere at all.
        int held = 0;
        foreach (TableRow row in table.Rows)
        {
            foreach (TableCell cell in row.Cells)
                held += cell.ParagraphCount;
        }

        Assert.Equal(document.ParagraphCount, held);
    }

    [Fact(Timeout = 600000)]
    public void A_Nested_Table_Moves_With_The_Cell_It_Is_In()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain("intro"),
            RichTextParagraph.Plain("deep"),
            RichTextParagraph.Plain("right"),
        ]).WithTables([
            new DocumentTable(
                1,
                2,
                [
                    new TableRow([
                        new TableCell(
                            1,
                            1,
                            0,
                            tables: [new DocumentTable(1, 1, [new TableRow([new TableCell(1, 1, 0)])], [50])]),
                        new TableCell(2, 1, 1),
                    ]),
                ],
                [100, 100]),
        ]).SplitParagraph(new RichTextPosition(0, 2)).Document;

        DocumentTable outer = TableOf(document);
        Assert.Equal(2, outer.ParagraphIndex);
        Assert.Equal(2, Assert.Single(outer.Rows[0].Cells[0].Tables).ParagraphIndex);
    }

    [Fact(Timeout = 600000)]
    public void Applying_A_Style_Keeps_The_Tables()
    {
        RichTextDocument document = Grid().ApplyInlineStyle(
            new RichTextRange(new RichTextPosition(0, 0), new RichTextPosition(1, 2)),
            InlineStyleDelta.ToggleBold(true));

        Assert.Equal(4, TableOf(document).ParagraphCount);
    }

    [Fact(Timeout = 600000)]
    public void The_Table_Starting_At_A_Paragraph_Is_The_One_The_Body_Walks()
    {
        RichTextDocument document = Grid();

        Assert.NotNull(document.TableStartingAt(0));
        Assert.Null(document.TableStartingAt(1));
        Assert.Null(document.TableStartingAt(4));
    }

    [Fact(Timeout = 600000)]
    public void A_Cell_With_No_Grid_Still_States_Where_It_Is()
    {
        var cell = new TableCell(3, 2, 1, columnSpan: 2, rowSpan: 3);

        Assert.Equal(5, cell.ParagraphEnd);
        Assert.Equal(2, cell.ColumnSpan);
        Assert.Equal(3, cell.RowSpan);
        Assert.Empty(cell.Tables);
    }

    [Fact(Timeout = 600000)]
    public void A_Border_Draws_Only_When_It_Has_A_Width_And_A_Colour()
    {
        Assert.False(TableBorder.None.IsVisible);
        Assert.False(new TableBorder(BColor.Black, 0).IsVisible);
        Assert.False(new TableBorder(BColor.Empty, 1).IsVisible);
        Assert.True(TableBorder.Solid(BColor.Black).IsVisible);
        Assert.True(CellBorders.All(TableBorder.Solid(BColor.Black)).IsVisible);
        Assert.False(default(CellBorders).IsVisible);
    }
}
