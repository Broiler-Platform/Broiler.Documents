namespace Broiler.Documents.Odt.Tests;

public sealed class OdtTableGeometryTests
{
    private static RichTextDocument Read(string tableProperties, string cellProperties = "") => OdtTestPackage.ReadStyled(
        "<table:table table:style-name=\"table\"><table:table-row><table:table-cell table:style-name=\"cell\">" +
        "<text:p>cell</text:p></table:table-cell></table:table-row></table:table>",
        "<office:styles><style:style style:name=\"table\" style:family=\"table\"><style:table-properties " + tableProperties + "/></style:style>" +
        "<style:style style:name=\"cell\" style:family=\"table-cell\"><style:table-cell-properties " + cellProperties + "/></style:style></office:styles>").Document;

    [Fact]
    public void Reads_table_width_alignment_and_cell_vertical_alignment()
    {
        DocumentTable table = Assert.Single(Read("style:width=\"120pt\" style:rel-width=\"50%\" table:align=\"center\" fo:margin-left=\"5pt\"",
            "style:vertical-align=\"middle\"").Tables);
        Assert.Equal(120, table.PreferredWidth);
        Assert.Equal(50, table.WidthPercent);
        Assert.Equal(TableAlignment.Center, table.Alignment);
        Assert.Equal(5, table.LeftIndent);
        Assert.Equal(CellVerticalAlignment.Center, table.Rows[0].Cells[0].VerticalAlignment);
    }

    [Fact]
    public void Writes_and_reads_both_widths_and_alignment()
    {
        RichTextDocument source = Read("style:width=\"100pt\" style:rel-width=\"40%\" table:align=\"right\" fo:margin-left=\"7pt\"",
            "style:vertical-align=\"bottom\"");
        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(source));
        DocumentTable table = Assert.Single(new OdtDocumentCodec().Read(stream).Document.Tables);
        Assert.Equal(100, table.PreferredWidth);
        Assert.Equal(40, table.WidthPercent);
        Assert.Equal(TableAlignment.End, table.Alignment);
        Assert.Equal(7, table.LeftIndent);
        Assert.Equal(CellVerticalAlignment.Bottom, table.Rows[0].Cells[0].VerticalAlignment);
    }

    [Fact]
    public void Unknown_alignment_and_bad_width_leave_the_automatic_layout_available()
    {
        DocumentTable table = Assert.Single(Read("style:width=\"-4pt\" style:rel-width=\"0%\" table:align=\"margins\"").Tables);
        Assert.Null(table.PreferredWidth);
        Assert.Null(table.WidthPercent);
        Assert.Equal(TableAlignment.Start, table.Alignment);
    }
}
