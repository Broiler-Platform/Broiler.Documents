namespace Broiler.Documents.Odt.Tests;

public sealed class OdtEmptyCellAndBreakTests
{
    private const string EmptyTable = "<table:table><table:table-row><table:table-cell/></table:table-row></table:table>";
    private const string StyledTable = "<table:table table:style-name=\"T\"><table:table-row><table:table-cell><text:p>cell</text:p></table:table-cell></table:table-row></table:table>";

    [Fact]
    public void Empty_cells_keep_a_paragraph_and_a_place_in_the_table()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(EmptyTable + "<text:p>after</text:p>");
        DocumentTable table = Assert.Single(result.Document.Tables);
        Assert.Equal(1, table.ParagraphCount);
        Assert.Equal(1, table.Rows[0].Cells[0].ParagraphCount);
        Assert.Equal("", result.Document.Paragraphs[0].Text);
        Assert.Equal("after", result.Document.Paragraphs[1].Text);
        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(result.Document));
        Assert.Equal(1, new OdtDocumentCodec().Read(stream).Document.Tables[0].Rows[0].Cells[0].ParagraphCount);
    }

    [Fact]
    public void Empty_cell_paragraph_marks_inherit_the_document_font()
    {
        RichTextDocument document = OdtTestPackage.ReadStyled(EmptyTable,
            "<office:styles><style:default-style style:family=\"paragraph\"><style:text-properties fo:font-size=\"17pt\"/>" +
            "</style:default-style></office:styles>").Document;
        Assert.Equal(17, document.Paragraphs[0].MarkStyle.FontSize);
    }

    [Fact]
    public void Covered_cells_do_not_get_extra_empty_paragraphs()
    {
        RichTextDocument document = OdtTestPackage.ReadBody("<table:table><table:table-row>" +
            "<table:table-cell table:number-columns-spanned=\"2\"/><table:covered-table-cell/>" +
            "</table:table-row></table:table>").Document;
        Assert.Equal(1, document.ParagraphCount);
        Assert.Single(document.Tables[0].Rows[0].Cells);
    }

    [Fact]
    public void Synthesized_paragraphs_obey_the_read_limit()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody("<table:table><table:table-row>" +
            "<table:table-cell table:number-columns-repeated=\"4\"/></table:table-row></table:table>",
            options: new DocumentReadOptions(new DocumentLimits(maxParagraphCount: 2)));
        Assert.Equal(2, result.Document.ParagraphCount);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "odt.limit.paragraphs");
    }

    [Theory]
    [InlineData("page", true, false)]
    [InlineData("column", false, true)]
    public void A_tables_own_break_round_trips_on_the_table(string kind, bool page, bool column)
    {
        RichTextDocument document = OdtTestPackage.ReadBody(StyledTable,
            OdtTestPackage.Style("T", "<style:table-properties fo:break-before=\"" + kind + "\"/>", family: "table")).Document;
        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(document));
        DocumentTable table = Assert.Single(new OdtDocumentCodec().Read(stream).Document.Tables);
        Assert.Equal(page, table.PageBreakBefore);
        Assert.Equal(column, table.ColumnBreakBefore);
        DocumentTable shifted = table.Shifted(0, 0, 1)!;
        Assert.Equal(page, shifted.PageBreakBefore);
        Assert.Equal(column, shifted.ColumnBreakBefore);
    }

    [Fact]
    public void A_break_after_a_table_reaches_the_following_table()
    {
        RichTextDocument document = OdtTestPackage.ReadBody(StyledTable + EmptyTable,
            OdtTestPackage.Style("T", "<style:table-properties fo:break-after=\"page\"/>", family: "table")).Document;
        Assert.False(document.Tables[0].PageBreakBefore);
        Assert.True(document.Tables[1].PageBreakBefore);
        Assert.All(document.Paragraphs, paragraph => Assert.False(paragraph.Style.PageBreakBefore));
    }
}
