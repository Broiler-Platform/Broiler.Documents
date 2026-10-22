namespace Broiler.Documents.Docx.Tests;

public sealed class DocxTableGeometryTests
{
    private static RichTextDocument Read(string tableProperties, string cellProperties = "") => DocxTestPackage.ReadBody(
        "<w:tbl><w:tblPr>" + tableProperties + "</w:tblPr><w:tr><w:tc><w:tcPr>" + cellProperties +
        "</w:tcPr><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>").Document;

    [Theory]
    [InlineData("2380", 47.6)]
    [InlineData("47.6%", 47.6)]
    public void Reads_percentage_table_width_in_both_ooxml_spellings(string value, double expected)
    {
        DocumentTable table = Assert.Single(Read("<w:tblW w:type=\"pct\" w:w=\"" + value + "\"/>").Tables);
        Assert.Equal(expected, table.WidthPercent!.Value, 3);
        Assert.Null(table.PreferredWidth);
    }

    [Fact]
    public void Reads_point_width_alignment_indent_and_vertical_alignment()
    {
        DocumentTable table = Assert.Single(Read("<w:tblW w:type=\"dxa\" w:w=\"2400\"/>" +
            "<w:jc w:val=\"center\"/><w:tblInd w:type=\"dxa\" w:w=\"360\"/>", "<w:vAlign w:val=\"bottom\"/>").Tables);
        Assert.Equal(120, table.PreferredWidth);
        Assert.Equal(TableAlignment.Center, table.Alignment);
        Assert.Equal(18, table.LeftIndent);
        Assert.Equal(CellVerticalAlignment.Bottom, table.Rows[0].Cells[0].VerticalAlignment);
    }

    [Fact]
    public void Table_and_cell_geometry_round_trips_and_survives_paragraph_edits()
    {
        RichTextDocument document = Read("<w:tblW w:type=\"pct\" w:w=\"2500\"/>" +
            "<w:jc w:val=\"right\"/><w:tblInd w:type=\"dxa\" w:w=\"100\"/>", "<w:vAlign w:val=\"center\"/>");
        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(document));
        DocumentTable table = Assert.Single(new DocxDocumentCodec().Read(stream).Document.Tables).Shifted(0, 0, 1)!;
        Assert.Equal(50, table.WidthPercent);
        Assert.Equal(TableAlignment.End, table.Alignment);
        Assert.Equal(5, table.LeftIndent);
        Assert.Equal(CellVerticalAlignment.Center, table.Rows[0].Cells[0].VerticalAlignment);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("nil")]
    public void Automatic_or_nil_width_is_not_treated_as_a_fixed_grid_width(string type)
    {
        DocumentTable table = Assert.Single(Read("<w:tblW w:type=\"" + type + "\" w:w=\"2400\"/>").Tables);
        Assert.Null(table.PreferredWidth);
        Assert.Null(table.WidthPercent);
    }

    [Fact]
    public void Direct_geometry_can_cancel_inherited_table_and_cell_geometry()
    {
        string style = DocxTestPackage.Style("Sized", "<w:tblPr><w:tblW w:type=\"pct\" w:w=\"2500\"/>" +
            "<w:jc w:val=\"center\"/></w:tblPr><w:tcPr><w:vAlign w:val=\"bottom\"/></w:tcPr>", type: "table");
        string Body(string tableOverride, string cellOverride) => "<w:tbl><w:tblPr><w:tblStyle w:val=\"Sized\"/>" + tableOverride +
            "</w:tblPr><w:tr><w:tc><w:tcPr>" + cellOverride + "</w:tcPr><w:p/></w:tc></w:tr></w:tbl>";
        DocumentTable inherited = DocxTestPackage.ReadStyled(Body("", ""), style).Document.Tables[0];
        Assert.Equal(50, inherited.WidthPercent);
        Assert.Equal(TableAlignment.Center, inherited.Alignment);
        Assert.Equal(CellVerticalAlignment.Bottom, inherited.Rows[0].Cells[0].VerticalAlignment);
        DocumentTable direct = DocxTestPackage.ReadStyled(Body("<w:tblW w:type=\"auto\" w:w=\"0\"/><w:jc w:val=\"left\"/>",
            "<w:vAlign w:val=\"top\"/>"), style).Document.Tables[0];
        Assert.Null(direct.WidthPercent);
        Assert.Equal(TableAlignment.Start, direct.Alignment);
        Assert.Equal(CellVerticalAlignment.Top, direct.Rows[0].Cells[0].VerticalAlignment);
    }
}
