namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentLayoutAutoTableTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(80.0)]
    public void Auto_Table_Allocates_More_Space_To_Long_Content(double? percent)
    {
        var document = RichTextDocument.FromParagraphs([
            RichTextParagraph.Create("a longer cell value", InlineStyle.Default),
            RichTextParagraph.Create("3", InlineStyle.Default)])
            .WithTables([new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])],
                autoFit: true, widthPercent: percent)]);
        using var images = new ImageStore();
        var page = Assert.Single(new DocumentLayout(new LayoutSettings(), images)
            .Layout(document, new DocumentPageBox(600, 800, 20, 20, 20, 20)).Pages);
        Assert.Equal(2, page.Cells.Count);
        Assert.True(page.Cells[0].Bounds.Width > page.Cells[1].Bounds.Width + 20);
        double total = page.Cells.Sum(cell => cell.Bounds.Width);
        if (percent is double stated)
            Assert.Equal(560 * stated / 100, total, 5);
        else
            Assert.True(total < 560);
    }
}
