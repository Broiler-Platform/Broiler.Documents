using System.Text;

namespace Broiler.Documents.Html.Tests;

public sealed class HtmlTableTests
{
    [Fact]
    public void Keeps_Cells_Borders_And_Empty_Cells_Separate_From_Body()
    {
        RichTextDocument document = Read("<p>before</p><table border='1' cellpadding='2' width='80%'>" +
            "<tr><th>A</th><th>B</th></tr><tr><td></td><td>value</td></tr></table><p>after</p>");
        DocumentTable table = Assert.Single(document.Tables);
        Assert.Equal(1, table.ParagraphIndex);
        Assert.Equal(4, table.ParagraphCount);
        Assert.Equal(80, table.WidthPercent);
        Assert.Equal("before\nA\nB\n\nvalue\nafter", document.PlainText);
        Assert.True(document.Paragraphs[1].StyleAt(0).Bold);
        TableCell cell = table.Rows[1].Cells[0];
        Assert.Equal(.75, cell.Borders.Left.Width);
        Assert.Equal(1.5, cell.Padding!.Value.Top);
        Assert.Equal(1, cell.ParagraphCount);
    }

    [Fact]
    public void Places_Row_And_Column_Spans_And_Retains_Nested_Tables()
    {
        RichTextDocument document = Read("<table><tr><td rowspan='2'>A</td><td colspan='2'>B</td></tr>" +
            "<tr><td><table><tr><td>inner</td></tr></table></td><td>C</td></tr></table>");
        DocumentTable table = Assert.Single(document.Tables);
        Assert.Equal(2, table.Rows[0].Cells[0].RowSpan);
        Assert.Equal(2, table.Rows[0].Cells[1].ColumnSpan);
        Assert.Equal(1, table.Rows[1].Cells[0].ColumnIndex);
        Assert.Equal(2, table.Rows[1].Cells[1].ColumnIndex);
        DocumentTable inner = Assert.Single(table.Rows[1].Cells[0].Tables);
        Assert.Equal("inner", document.Paragraphs[inner.ParagraphIndex].Text);
        Assert.Equal("A\nB\ninner\nC", document.PlainText);
    }

    [Fact]
    public void Keeps_Caption_And_Multiple_Paragraphs_In_A_Cell()
    {
        RichTextDocument document = Read("<table><caption>caption</caption><tr><td><p>one</p><p>two</p></td></tr></table>");
        DocumentTable table = Assert.Single(document.Tables);
        Assert.Equal("caption\none\ntwo", document.PlainText);
        Assert.Equal(1, table.ParagraphIndex);
        Assert.Equal(2, table.ParagraphCount);
        Assert.True(table.AutoFit);
    }

    private static RichTextDocument Read(string html) =>
        new HtmlDocumentCodec().Read(new MemoryStream(Encoding.UTF8.GetBytes(html))).Document;
}
