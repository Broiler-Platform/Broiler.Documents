using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// The borders, shading and cell margins a table style states, and its
/// conditional formats: what <c>Table Grid</c> and the built-in list and grid
/// styles draw.
/// </summary>
/// <remarks>
/// Read without them, a table whose lines come from its style - 210 of the
/// DOCX files in LibreOffice's <c>sw/qa/extras</c>, among them
/// <c>tdf95033.docx</c>, <c>tdf91095.docx</c> and <c>tdf123705.docx</c> -
/// drew with no lines at all. The rules are LibreOffice's
/// (<c>DomainMapperTableHandler</c>), which is what the rendering is compared with.
/// </remarks>
public sealed class DocxTableStyleBorderTests
{
    private const string Single = "w:val=\"single\" w:sz=\"8\"";

    /// <summary><c>Table Grid</c> as Word writes it: every edge a half-point line.</summary>
    private static readonly string TableGrid = DocxTestPackage.Style(
        "TableGrid",
        "<w:tblPr><w:tblBorders>" +
        "<w:top w:val=\"single\" w:sz=\"4\" w:color=\"000000\"/>" +
        "<w:left w:val=\"single\" w:sz=\"4\" w:color=\"000000\"/>" +
        "<w:bottom w:val=\"single\" w:sz=\"4\" w:color=\"000000\"/>" +
        "<w:right w:val=\"single\" w:sz=\"4\" w:color=\"000000\"/>" +
        "<w:insideH w:val=\"single\" w:sz=\"4\" w:color=\"000000\"/>" +
        "<w:insideV w:val=\"single\" w:sz=\"4\" w:color=\"000000\"/>" +
        "</w:tblBorders></w:tblPr>",
        type: "table");

    private static string Grid(int columns) =>
        "<w:tblGrid>" + string.Concat(Enumerable.Repeat("<w:gridCol w:w=\"1800\"/>", columns)) + "</w:tblGrid>";

    private static string Cell(string text, string properties = "") =>
        "<w:tc><w:tcPr>" + properties + "</w:tcPr>" + DocxTestPackage.Paragraph(text) + "</w:tc>";

    private static string Row(params string[] cells) => "<w:tr>" + string.Concat(cells) + "</w:tr>";

    private static string Table(string properties, int columns, params string[] rows) =>
        "<w:tbl><w:tblPr>" + properties + "</w:tblPr>" + Grid(columns) + string.Concat(rows) + "</w:tbl>";

    private static string TwoByTwo(string properties) =>
        Table(properties, 2, Row(Cell("a1"), Cell("b1")), Row(Cell("a2"), Cell("b2")));

    private static DocumentTable ReadTable(string body, string styles) =>
        Assert.Single(DocxTestPackage.ReadStyled(body, styles).Document.Tables);

    [Fact]
    public void A_Table_Style_Draws_Its_Borders()
    {
        DocumentTable table = ReadTable(TwoByTwo("<w:tblStyle w:val=\"TableGrid\"/>"), TableGrid);

        foreach (TableRow row in table.Rows)
        {
            foreach (TableCell cell in row.Cells)
            {
                Assert.True(cell.Borders.Left.IsVisible);
                Assert.True(cell.Borders.Top.IsVisible);
                Assert.True(cell.Borders.Right.IsVisible);
                Assert.True(cell.Borders.Bottom.IsVisible);
                Assert.Equal(0.5, cell.Borders.Top.Width, 3);
            }
        }
    }

    [Fact]
    public void Cells_Take_The_Outer_Edges_At_The_Boundary_And_The_Inside_Edges_Between()
    {
        string style = DocxTestPackage.Style(
            "Framed",
            "<w:tblPr><w:tblBorders>" +
            "<w:top " + Single + " w:color=\"FF0000\"/><w:left " + Single + " w:color=\"FF0000\"/>" +
            "<w:bottom " + Single + " w:color=\"FF0000\"/><w:right " + Single + " w:color=\"FF0000\"/>" +
            "<w:insideH " + Single + " w:color=\"0000FF\"/><w:insideV w:val=\"nil\"/>" +
            "</w:tblBorders></w:tblPr>",
            type: "table");

        DocumentTable table = ReadTable(TwoByTwo("<w:tblStyle w:val=\"Framed\"/>"), style);
        TableCell topLeft = table.Rows[0].Cells[0];
        TableCell bottomRight = table.Rows[1].Cells[1];

        Assert.Equal(0xFF, topLeft.Borders.Top.Color.R);
        Assert.Equal(0xFF, topLeft.Borders.Left.Color.R);
        Assert.Equal(0xFF, topLeft.Borders.Bottom.Color.B);   // insideH, below it
        Assert.False(topLeft.Borders.Right.IsVisible);        // insideV is nil

        Assert.Equal(0xFF, bottomRight.Borders.Top.Color.B);
        Assert.Equal(0xFF, bottomRight.Borders.Bottom.Color.R);
        Assert.Equal(0xFF, bottomRight.Borders.Right.Color.R);
        Assert.False(bottomRight.Borders.Left.IsVisible);
    }

    [Fact]
    public void The_Tables_Own_Borders_Win_Over_Its_Styles()
    {
        DocumentTable table = ReadTable(
            TwoByTwo(
                "<w:tblStyle w:val=\"TableGrid\"/>" +
                "<w:tblBorders><w:insideH w:val=\"none\"/><w:top " + Single + " w:color=\"00FF00\"/></w:tblBorders>"),
            TableGrid);

        TableCell topLeft = table.Rows[0].Cells[0];
        Assert.Equal(0xFF, topLeft.Borders.Top.Color.G);
        Assert.False(topLeft.Borders.Bottom.IsVisible);
        Assert.True(topLeft.Borders.Left.IsVisible);   // still the style's
    }

    [Fact]
    public void A_Rows_Table_Property_Exceptions_State_Its_Borders()
    {
        // tdf95033.docx: the table turns its outer edges off, and some rows turn
        // them back on through w:tblPrEx.
        string exception =
            "<w:tr><w:tblPrEx><w:tblBorders><w:left " + Single + "/><w:right " + Single + "/></w:tblBorders></w:tblPrEx>" +
            Cell("a2") + Cell("b2") + "</w:tr>";
        DocumentTable table = ReadTable(
            Table(
                "<w:tblStyle w:val=\"TableGrid\"/><w:tblBorders><w:left w:val=\"none\"/><w:right w:val=\"none\"/></w:tblBorders>",
                2,
                Row(Cell("a1"), Cell("b1")),
                exception),
            TableGrid);

        Assert.False(table.Rows[0].Cells[0].Borders.Left.IsVisible);
        Assert.False(table.Rows[0].Cells[1].Borders.Right.IsVisible);
        Assert.True(table.Rows[1].Cells[0].Borders.Left.IsVisible);
        Assert.True(table.Rows[1].Cells[1].Borders.Right.IsVisible);
    }

    [Fact]
    public void A_Cells_Own_Borders_Win_Over_The_Style()
    {
        DocumentTable table = ReadTable(
            Table(
                "<w:tblStyle w:val=\"TableGrid\"/>",
                2,
                Row(Cell("a", "<w:tcBorders><w:top " + Single + " w:color=\"00FF00\"/></w:tcBorders>"), Cell("b"))),
            TableGrid);

        Assert.Equal(0xFF, table.Rows[0].Cells[0].Borders.Top.Color.G);
        Assert.Equal(0x00, table.Rows[0].Cells[1].Borders.Top.Color.G);
    }

    [Fact]
    public void A_Style_Reaches_Through_Its_BasedOn_Chain_And_The_Default_Table_Style_Applies_Unnamed()
    {
        string derived = DocxTestPackage.Style("Derived", "<w:rPr><w:b/></w:rPr>", type: "table", basedOn: "TableGrid");
        DocumentTable viaChain = ReadTable(TwoByTwo("<w:tblStyle w:val=\"Derived\"/>"), TableGrid + derived);
        Assert.True(viaChain.Rows[0].Cells[0].Borders.Top.IsVisible);

        string normal = DocxTestPackage.Style(
            "Normal",
            "<w:tblPr><w:tblBorders><w:top " + Single + "/></w:tblBorders></w:tblPr>",
            type: "table",
            isDefault: true);
        DocumentTable unnamed = ReadTable(TwoByTwo(string.Empty), normal);
        Assert.True(unnamed.Rows[0].Cells[0].Borders.Top.IsVisible);
        Assert.False(unnamed.Rows[1].Cells[0].Borders.Top.IsVisible);
    }

    [Fact]
    public void A_First_Row_Format_Shades_And_Formats_The_First_Row()
    {
        string style = DocxTestPackage.Style(
            "Header",
            "<w:tblStylePr w:type=\"firstRow\">" +
            "<w:rPr><w:b/><w:color w:val=\"FFFFFF\"/></w:rPr>" +
            "<w:tcPr><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"4472C4\"/>" +
            "<w:tcBorders><w:bottom " + Single + " w:color=\"FF0000\"/></w:tcBorders></w:tcPr>" +
            "</w:tblStylePr>",
            type: "table");

        DocumentReadResult result = DocxTestPackage.ReadStyled(
            TwoByTwo("<w:tblStyle w:val=\"Header\"/>"), style);
        DocumentTable table = Assert.Single(result.Document.Tables);

        Assert.Equal(0x44, table.Rows[0].Cells[1].Shading.R);
        Assert.Equal(0xFF, table.Rows[0].Cells[1].Borders.Bottom.Color.R);
        Assert.True(table.Rows[1].Cells[0].Shading.IsEmpty);

        RichTextParagraph header = Assert.Single(result.Document.Paragraphs, p => p.Text == "b1");
        Assert.True(header.StyleAt(0).Bold);
        Assert.Equal(0xFF, header.StyleAt(0).Foreground.G);
        Assert.False(Assert.Single(result.Document.Paragraphs, p => p.Text == "b2").StyleAt(0).Bold);
    }

    [Fact]
    public void The_Look_Turns_A_Format_Off_And_Banding_Counts_From_The_Header()
    {
        string style = DocxTestPackage.Style(
            "Banded",
            "<w:tblStylePr w:type=\"firstRow\"><w:rPr><w:b/></w:rPr></w:tblStylePr>" +
            "<w:tblStylePr w:type=\"band1Horz\"><w:tcPr><w:shd w:val=\"clear\" w:fill=\"DDDDDD\"/></w:tcPr></w:tblStylePr>",
            type: "table");
        string rows = Row(Cell("h")) + Row(Cell("r1")) + Row(Cell("r2")) + Row(Cell("r3"));

        // The default look: a first row, then bands - the first band below it.
        DocumentTable banded = ReadTable(
            Table("<w:tblStyle w:val=\"Banded\"/>", 1, rows), style);
        Assert.True(banded.Rows[0].Cells[0].Shading.IsEmpty);
        Assert.Equal(0xDD, banded.Rows[1].Cells[0].Shading.R);
        Assert.True(banded.Rows[2].Cells[0].Shading.IsEmpty);
        Assert.Equal(0xDD, banded.Rows[3].Cells[0].Shading.R);

        // No first row and no banding: nothing is shaded.
        DocumentTable plain = ReadTable(
            Table("<w:tblStyle w:val=\"Banded\"/><w:tblLook w:firstRow=\"0\" w:noHBand=\"1\"/>", 1, rows), style);
        Assert.All(plain.Rows, row => Assert.True(row.Cells[0].Shading.IsEmpty));

        // No first row but banding: the bands start at the top.
        DocumentTable fromTop = ReadTable(
            Table("<w:tblStyle w:val=\"Banded\"/><w:tblLook w:val=\"0400\"/>", 1, rows), style);
        Assert.Equal(0xDD, fromTop.Rows[0].Cells[0].Shading.R);
        Assert.True(fromTop.Rows[1].Cells[0].Shading.IsEmpty);
    }

    [Fact]
    public void A_Cell_Turning_Off_A_Style_Border_Gives_The_Edge_Back_To_The_Table()
    {
        string style = DocxTestPackage.Style(
            "Cells",
            "<w:tblPr><w:tblBorders><w:top " + Single + " w:color=\"0000FF\"/></w:tblBorders></w:tblPr>" +
            "<w:tcPr><w:tcBorders><w:top " + Single + " w:color=\"FF0000\"/></w:tcBorders></w:tcPr>",
            type: "table");

        DocumentTable table = ReadTable(
            Table(
                "<w:tblStyle w:val=\"Cells\"/>",
                2,
                Row(Cell("a", "<w:tcBorders><w:top w:val=\"nil\"/></w:tcBorders>"), Cell("b"))),
            style);

        Assert.Equal(0xFF, table.Rows[0].Cells[0].Borders.Top.Color.B);   // the table's, not off
        Assert.Equal(0xFF, table.Rows[0].Cells[1].Borders.Top.Color.R);   // the style's cell border
    }

    [Fact]
    public void A_Style_States_The_Cell_Margin_And_The_Table_Shading()
    {
        string style = DocxTestPackage.Style(
            "Padded",
            "<w:tblPr><w:tblCellMar><w:left w:w=\"216\" w:type=\"dxa\"/></w:tblCellMar>" +
            "<w:shd w:val=\"clear\" w:fill=\"00FF00\"/></w:tblPr>",
            type: "table");

        DocumentTable table = ReadTable(
            Table("<w:tblStyle w:val=\"Padded\"/>", 2, Row(Cell("a"), Cell("b", "<w:shd w:val=\"clear\" w:fill=\"auto\"/>"))),
            style);

        Assert.Equal(10.8, table.CellPadding, 3);
        Assert.Equal(0xFF, table.Rows[0].Cells[0].Shading.G);
        Assert.True(table.Rows[0].Cells[1].Shading.IsEmpty);   // stated, and stated as none
    }

    [Fact]
    public void A_Style_First_Row_Marked_As_Header_Repeats()
    {
        string style = DocxTestPackage.Style(
            "Repeating",
            "<w:tblStylePr w:type=\"firstRow\"><w:trPr><w:tblHeader/></w:trPr></w:tblStylePr>",
            type: "table");

        DocumentTable table = ReadTable(
            Table("<w:tblStyle w:val=\"Repeating\"/>", 1, Row(Cell("h")), Row(Cell("r"))), style);

        Assert.True(table.Rows[0].IsHeader);
        Assert.False(table.Rows[1].IsHeader);
    }

    [Fact]
    public void Only_What_A_Style_States_And_Is_Not_Applied_Is_Reported()
    {
        DocumentReadResult applied = DocxTestPackage.ReadStyled(TwoByTwo("<w:tblStyle w:val=\"TableGrid\"/>"), TableGrid);
        Assert.DoesNotContain(applied.Diagnostics, d => d.Code == "docx.table.style");

        string centred = DocxTestPackage.Style(
            "Centred",
            "<w:tcPr><w:vAlign w:val=\"center\"/></w:tcPr>",
            type: "table");
        DocumentReadResult unapplied = DocxTestPackage.ReadStyled(TwoByTwo("<w:tblStyle w:val=\"Centred\"/>"), centred);
        Assert.Contains(unapplied.Diagnostics, d => d.Code == "docx.table.style" && d.Message.Contains("vAlign"));
    }
}
