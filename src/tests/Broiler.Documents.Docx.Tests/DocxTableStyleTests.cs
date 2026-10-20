namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// A table style's own paragraph and run properties: what a <c>w:tblStyle</c>
/// states for every cell, between the document defaults and the paragraph style.
/// </summary>
/// <remarks>
/// Word's default template puts ten points after every paragraph and a 1.15 line
/// in <c>w:docDefaults</c>, and <c>Table Grid</c> takes both back out inside a
/// table. Read without the table style, every cell of such a document carried
/// the body's spacing, and LibreOffice's <c>fdo68607.docx</c> - a glossary laid
/// out as one long table - came out at fifteen pages where Word and LibreOffice
/// draw eight.
/// </remarks>
public sealed class DocxTableStyleTests
{
    /// <summary>Word's defaults since 2007: ten points after, a 1.15 line, 11 point text.</summary>
    private static readonly string WordDefaults = DocxTestPackage.DocumentDefaults(
        "<w:rPr><w:sz w:val=\"22\"/></w:rPr>",
        "<w:pPr><w:spacing w:after=\"200\" w:line=\"276\" w:lineRule=\"auto\"/></w:pPr>");

    /// <summary><c>Table Grid</c> as Word writes it: no space after, single lines.</summary>
    private static readonly string TableGrid = DocxTestPackage.Style(
        "TableGrid",
        "<w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr>",
        type: "table");

    private static string Table(string styleId, params string[] cellContents)
    {
        string style = styleId.Length == 0 ? string.Empty : "<w:tblStyle w:val=\"" + styleId + "\"/>";
        var cells = new System.Text.StringBuilder();
        foreach (string content in cellContents)
            cells.Append("<w:tc><w:tcPr/>").Append(content).Append("</w:tc>");

        return "<w:tbl><w:tblPr>" + style + "</w:tblPr>" +
            "<w:tblGrid><w:gridCol w:w=\"2400\"/></w:tblGrid>" +
            "<w:tr>" + cells + "</w:tr></w:tbl>";
    }

    private static RichTextParagraph ParagraphWithText(DocumentReadResult result, string text) =>
        Assert.Single(result.Document.Paragraphs, paragraph => paragraph.Text == text);

    [Fact]
    public void A_Table_Style_Takes_The_Default_Spacing_Back_Out_Of_Its_Cells()
    {
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            Table("TableGrid", DocxTestPackage.Paragraph("cell")) + DocxTestPackage.Paragraph("body"),
            WordDefaults + TableGrid);

        ParagraphStyle cell = ParagraphWithText(result, "cell").Style;
        Assert.Equal(0f, cell.SpacingAfter);
        Assert.Equal(1f, cell.LineSpacing);

        // The paragraph after the table is not in it, and keeps the defaults.
        ParagraphStyle body = ParagraphWithText(result, "body").Style;
        Assert.Equal(10f, body.SpacingAfter);
        Assert.Equal(1.15f, body.LineSpacing, 3);
    }

    [Fact]
    public void A_Paragraph_Style_Wins_Over_The_Table_Style()
    {
        string spaced = DocxTestPackage.Style("Spaced", "<w:pPr><w:spacing w:after=\"120\"/></w:pPr>");

        DocumentReadResult result = DocxTestPackage.ReadStyled(
            Table("TableGrid", DocxTestPackage.StyledParagraph("Spaced", "cell")),
            WordDefaults + TableGrid + spaced);

        ParagraphStyle cell = ParagraphWithText(result, "cell").Style;
        Assert.Equal(6f, cell.SpacingAfter);   // the paragraph style's
        Assert.Equal(1f, cell.LineSpacing);    // the table style's, which Spaced does not set
    }

    [Fact]
    public void Direct_Formatting_Wins_Over_The_Table_Style()
    {
        string paragraph =
            "<w:p><w:pPr><w:spacing w:after=\"240\"/></w:pPr>" +
            "<w:r><w:t>cell</w:t></w:r></w:p>";

        DocumentReadResult result = DocxTestPackage.ReadStyled(
            Table("TableGrid", paragraph),
            WordDefaults + TableGrid);

        Assert.Equal(12f, ParagraphWithText(result, "cell").Style.SpacingAfter);
    }

    [Fact]
    public void A_Table_Style_States_The_Character_Formatting_Of_Its_Cells()
    {
        string small = DocxTestPackage.Style(
            "Small",
            "<w:rPr><w:sz w:val=\"16\"/><w:i/></w:rPr>",
            type: "table");
        string ownSize =
            "<w:p><w:r><w:rPr><w:sz w:val=\"28\"/></w:rPr><w:t>own</w:t></w:r></w:p>";

        DocumentReadResult result = DocxTestPackage.ReadStyled(
            Table("Small", DocxTestPackage.Paragraph("styled"), ownSize) + DocxTestPackage.Paragraph("body"),
            WordDefaults + small);

        InlineStyle styled = ParagraphWithText(result, "styled").StyleAt(0);
        Assert.Equal(8f, styled.FontSize);
        Assert.True(styled.Italic);

        // The run's own size beats the table's; what it does not state, it inherits.
        InlineStyle own = ParagraphWithText(result, "own").StyleAt(0);
        Assert.Equal(14f, own.FontSize);
        Assert.True(own.Italic);

        Assert.Equal(11f, ParagraphWithText(result, "body").StyleAt(0).FontSize);
    }

    [Fact]
    public void A_Table_Style_Inherits_Through_Its_BasedOn_Chain()
    {
        string derived = DocxTestPackage.Style("Derived", "<w:rPr><w:b/></w:rPr>", type: "table", basedOn: "TableGrid");

        DocumentReadResult result = DocxTestPackage.ReadStyled(
            Table("Derived", DocxTestPackage.Paragraph("cell")),
            WordDefaults + TableGrid + derived);

        RichTextParagraph cell = ParagraphWithText(result, "cell");
        Assert.Equal(0f, cell.Style.SpacingAfter);
        Assert.True(cell.StyleAt(0).Bold);
    }

    [Fact]
    public void A_Table_That_Names_No_Style_Takes_The_Default_Table_Style()
    {
        string normalTable = DocxTestPackage.Style(
            "NormalTable",
            "<w:pPr><w:spacing w:after=\"0\"/></w:pPr>",
            type: "table",
            isDefault: true);

        DocumentReadResult result = DocxTestPackage.ReadStyled(
            Table(string.Empty, DocxTestPackage.Paragraph("cell")),
            WordDefaults + normalTable);

        Assert.Equal(0f, ParagraphWithText(result, "cell").Style.SpacingAfter);
    }

    [Fact]
    public void A_Nested_Table_Takes_Its_Own_Style_And_Not_Its_Parents()
    {
        string loose = DocxTestPackage.Style(
            "Loose",
            "<w:pPr><w:spacing w:after=\"480\"/></w:pPr>",
            type: "table");
        string inner = Table("Loose", DocxTestPackage.Paragraph("inner"));

        DocumentReadResult result = DocxTestPackage.ReadStyled(
            Table("TableGrid", DocxTestPackage.Paragraph("outer") + inner + DocxTestPackage.Paragraph("after")),
            WordDefaults + TableGrid + loose);

        Assert.Equal(0f, ParagraphWithText(result, "outer").Style.SpacingAfter);
        Assert.Equal(24f, ParagraphWithText(result, "inner").Style.SpacingAfter);

        // Back in the outer cell once the nested table has closed.
        Assert.Equal(0f, ParagraphWithText(result, "after").Style.SpacingAfter);
    }

    [Fact]
    public void A_Table_Style_Reference_To_A_Paragraph_Style_Applies_Nothing()
    {
        // A w:tblStyle naming a paragraph style is a damaged reference. Reading
        // that style into every cell would apply what the document gave to
        // something else.
        string heading = DocxTestPackage.Style("Heading", "<w:pPr><w:spacing w:after=\"0\"/></w:pPr>");

        DocumentReadResult result = DocxTestPackage.ReadStyled(
            Table("Heading", DocxTestPackage.Paragraph("cell")),
            WordDefaults + heading);

        Assert.Equal(10f, ParagraphWithText(result, "cell").Style.SpacingAfter);
    }

    [Fact]
    public void A_Text_Box_In_A_Cell_Is_Not_In_The_Table()
    {
        // A text box is a story of its own. Anchoring it in a cell does not put
        // its paragraphs in the table, so the table style does not reach them.
        string textBox =
            "<w:p><w:r><w:drawing>" +
            "<wp:anchor xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\">" +
            "<wp:positionH relativeFrom=\"column\"><wp:posOffset>0</wp:posOffset></wp:positionH>" +
            "<wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>0</wp:posOffset></wp:positionV>" +
            "<wp:extent cx=\"914400\" cy=\"914400\"/>" +
            "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
            "<a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
            "<wps:wsp xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
            "<wps:spPr/><wps:txbx><w:txbxContent>" + DocxTestPackage.Paragraph("boxed") + "</w:txbxContent></wps:txbx>" +
            "</wps:wsp></a:graphicData></a:graphic></wp:anchor>" +
            "</w:drawing></w:r></w:p>";

        DocumentReadResult result = DocxTestPackage.ReadStyled(
            Table("TableGrid", textBox),
            WordDefaults + TableGrid);

        RichTextParagraph boxed = Assert.Single(Assert.Single(result.Document.Shapes).Paragraphs);
        Assert.Equal("boxed", boxed.Text);
        Assert.Equal(10f, boxed.Style.SpacingAfter);
    }
}
