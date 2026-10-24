namespace Broiler.Documents.Docx.Tests;

public sealed class DocxTableBlockBreakTests
{
    private const string Table = "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";

    [Fact]
    public void A_Section_Break_Before_A_Table_Belongs_To_The_Table()
    {
        var document = DocxTestPackage.ReadBody(
            "<w:p><w:pPr><w:sectPr/></w:pPr><w:r><w:t>before</w:t></w:r></w:p>" +
            Table + "<w:sectPr/>").Document;
        Assert.True(Assert.Single(document.Tables).PageBreakBefore);
        Assert.False(document.Paragraphs[1].Style.PageBreakBefore);
        Assert.Equal("before\ncell", document.PlainText);
    }

    [Theory]
    [InlineData("page", true, false)]
    [InlineData("column", false, true)]
    public void A_Run_Break_Before_A_Table_Is_Consumed_Once(string type, bool page, bool column)
    {
        var document = DocxTestPackage.ReadBody(
            $"<w:p><w:r><w:t>before</w:t><w:br w:type='{type}'/></w:r></w:p>" + Table +
            "<w:p><w:r><w:t>after</w:t></w:r></w:p>" +
            "<w:sectPr><w:cols w:num='2'/></w:sectPr>").Document;
        DocumentTable table = Assert.Single(document.Tables);
        Assert.Equal(page, table.PageBreakBefore);
        Assert.Equal(column, table.ColumnBreakBefore);
        Assert.All(document.Paragraphs, paragraph =>
        {
            Assert.False(paragraph.Style.PageBreakBefore);
            Assert.False(paragraph.Style.ColumnBreakBefore);
        });
    }

    [Fact]
    public void An_Empty_Table_Does_Not_Consume_A_Pending_Break()
    {
        var document = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:t>before</w:t><w:br w:type='page'/></w:r></w:p><w:tbl><w:tr/></w:tbl>" +
            "<w:p><w:r><w:t>after</w:t></w:r></w:p>").Document;
        Assert.True(document.Paragraphs[1].Style.PageBreakBefore);
        Assert.Empty(document.Tables);
    }

    [Fact]
    public void A_Break_Inside_A_Cell_Does_Not_Break_The_Table()
    {
        var result = DocxTestPackage.ReadBody(Table.Replace("cell", "cell</w:t><w:br w:type='page'/><w:t>"));
        Assert.False(Assert.Single(result.Document.Tables).PageBreakBefore);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.pagebreak.table");
    }
}
