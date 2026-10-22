namespace Broiler.Documents.Docx.Tests;

public sealed class DocxCellPaddingTests
{
    [Fact]
    public void Cell_margins_override_only_the_named_table_edges()
    {
        RichTextDocument document = DocxTestPackage.ReadBody(
            "<w:tbl><w:tblPr><w:tblCellMar><w:left w:w=\"100\"/><w:right w:w=\"200\"/>" +
            "<w:top w:w=\"60\"/><w:bottom w:w=\"80\"/></w:tblCellMar></w:tblPr>" +
            "<w:tr><w:tc><w:tcPr><w:tcMar><w:start w:w=\"0\" w:type=\"dxa\"/>" +
            "<w:bottom w:type=\"nil\"/></w:tcMar></w:tcPr><w:p><w:r><w:t>x</w:t></w:r></w:p>" +
            "</w:tc></w:tr></w:tbl>").Document;
        Assert.Equal(new TableCellPadding(0, 3, 10, 0), document.Tables[0].Rows[0].Cells[0].Padding);
    }

    [Fact]
    public void All_four_cell_edges_round_trip()
    {
        var padding = new TableCellPadding(1, 2, 3, 4);
        RichTextDocument document = RichTextDocument.FromPlainText("cell").WithTables([
            new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0, padding: padding)])], [100])]);
        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(document));
        Assert.Equal(padding, new DocxDocumentCodec().Read(stream).Document.Tables[0].Rows[0].Cells[0].Padding);
    }

    [Fact]
    public void Writing_a_table_keep_rule_reports_the_unrepresentable_property()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("cell").WithTables([
            new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)])], keepWithNext: true)]);
        using var stream = new MemoryStream();
        DocumentWriteResult result = new DocxDocumentCodec().Write(document, stream);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.table.keepnext");
    }

    [Fact]
    public void Writing_a_table_break_reports_the_unrepresentable_property()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("cell").WithTables([
            new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)])], pageBreakBefore: true)]);
        using var stream = new MemoryStream();
        DocumentWriteResult result = new DocxDocumentCodec().Write(document, stream);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.table.break");
    }
}
