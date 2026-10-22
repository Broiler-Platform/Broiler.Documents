namespace Broiler.Documents.Odt.Tests;

public sealed class OdtTableFlowTests
{
    private static DocumentReadResult Read(string cellProperties, string tableProperties = "", string extraStyles = "") =>
        OdtTestPackage.ReadStyled(
            "<table:table table:style-name=\"table\"><table:table-row>" +
            "<table:table-cell table:style-name=\"cell\"><text:p>body</text:p></table:table-cell>" +
            "</table:table-row></table:table><text:p>after</text:p>",
            "<office:styles>" + extraStyles +
            "<style:style style:name=\"table\" style:family=\"table\"><style:table-properties " + tableProperties + "/></style:style>" +
            "<style:style style:name=\"cell\" style:family=\"table-cell\"><style:table-cell-properties " + cellProperties + "/></style:style>" +
            "</office:styles>");

    [Fact]
    public void Reads_each_cell_edge_with_specific_values_over_uniform_padding()
    {
        DocumentTable table = Assert.Single(Read("fo:padding=\"3pt\" fo:padding-left=\"0pt\" fo:padding-bottom=\"7pt\"").Document.Tables);
        Assert.Equal(new TableCellPadding(0, 3, 3, 7), Assert.Single(Assert.Single(table.Rows).Cells).Padding);
    }

    [Fact]
    public void Keeps_cell_padding_and_table_keep_next_through_an_odt_round_trip()
    {
        RichTextDocument source = Read("fo:padding=\"4pt\"", "fo:keep-with-next=\"always\"").Document;
        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(source));
        DocumentTable table = Assert.Single(new OdtDocumentCodec().Read(stream).Document.Tables);
        Assert.True(table.KeepWithNext);
        Assert.Equal(new TableCellPadding(4, 4, 4, 4), Assert.Single(Assert.Single(table.Rows).Cells).Padding);
        Assert.True(source.Tables[0].Shifted(0, 0, 1)!.KeepWithNext);
        Assert.Equal(new TableCellPadding(4, 4, 4, 4), source.Tables[0].Shifted(0, 0, 1)!.Rows[0].Cells[0].Padding);
    }

    [Fact]
    public void Unspecified_padding_keeps_the_existing_fallback()
    {
        DocumentTable table = Assert.Single(Read("").Document.Tables);
        TableCell cell = Assert.Single(Assert.Single(table.Rows).Cells);
        Assert.Null(cell.Padding);
        Assert.Equal(new TableCellPadding(5.4, 0, 5.4, 0), cell.PaddingFor(table));
        Assert.False(table.KeepWithNext);
    }

    [Fact]
    public void Table_and_cell_styles_inherit_and_can_override_individual_properties()
    {
        string styles = "<office:styles>" +
            "<style:style style:name=\"parent\" style:family=\"table\"><style:table-properties fo:keep-with-next=\"always\"/></style:style>" +
            "<style:style style:name=\"child\" style:family=\"table\" style:parent-style-name=\"parent\"/>" +
            "<style:style style:name=\"baseCell\" style:family=\"table-cell\"><style:table-cell-properties fo:padding=\"5pt\"/></style:style>" +
            "<style:style style:name=\"childCell\" style:family=\"table-cell\" style:parent-style-name=\"baseCell\"><style:table-cell-properties fo:padding-right=\"0pt\"/></style:style>" +
            "</office:styles>";
        RichTextDocument document = OdtTestPackage.ReadStyled(
            "<table:table table:style-name=\"child\"><table:table-row><table:table-cell table:style-name=\"childCell\"><text:p>x</text:p></table:table-cell></table:table-row></table:table>", styles).Document;
        Assert.True(document.Tables[0].KeepWithNext);
        Assert.Equal(new TableCellPadding(5, 5, 0, 5), document.Tables[0].Rows[0].Cells[0].Padding);
    }
}
