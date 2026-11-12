namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Office issue #242, DOCX pages one side drew blank: symbol characters, the
/// <c>IF</c> fields LibreOffice works out again on the page, and a drawing
/// group of one shape.
/// </summary>
public sealed class DocxIssue242BlankTests
{
    [Fact]
    public void A_Symbol_Character_Keeps_Its_Font_And_Is_Marked_As_One()
    {
        // fdo78384.docx: a 72 point Wingdings arrow inserted as w:sym.
        RichTextDocument document = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:t>A</w:t></w:r><w:r><w:sym w:font=\"Wingdings\" w:char=\"F0E7\"/></w:r></w:p>").Document;

        RichTextParagraph paragraph = document.Paragraphs[0];
        Assert.Equal("A\uF0E7", paragraph.Text);
        Assert.Equal(2, paragraph.Runs.Count);
        Assert.False(paragraph.Runs[0].Style.IsSymbolCharacter);
        Assert.Equal("Wingdings", paragraph.Runs[1].Style.FontFamily);
        Assert.True(paragraph.Runs[1].Style.IsSymbolCharacter);
    }

    private static string Field(string instruction, string result) =>
        "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
        "<w:r><w:instrText xml:space=\"preserve\"> " + instruction + " </w:instrText></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
        "<w:r><w:t xml:space=\"preserve\">" + result + "</w:t></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>";

    /// <summary>The paragraph's text without its hidden runs.</summary>
    private static string Shown(RichTextParagraph paragraph)
    {
        var shown = new System.Text.StringBuilder();
        int start = 0;
        foreach (StyleRun run in paragraph.Runs)
        {
            if (!run.Style.Hidden)
                shown.Append(paragraph.Text, start, run.Length);
            start += run.Length;
        }
        return shown.ToString();
    }

    [Fact]
    public void An_If_Field_Libreoffice_Reads_As_True_Shows_Its_True_Result()
    {
        // tdf125038b.docx: LibreOffice imports the condition "1234" = "" as
        // "1234 FORMULA", which its calculator takes as true, so the page shows
        // the empty true result. Its text export lists the saved result, which
        // stays in the document, hidden.
        RichTextDocument document = DocxTestPackage.ReadBody(
            Field("IF \"1234\" = \"\" \"\" \"phone: 1234\"", "phone: 1234")).Document;

        Assert.Equal("", Shown(document.Paragraphs[0]));
        Assert.Equal("phone: 1234", document.Paragraphs[0].Text);
    }

    [Theory]
    [InlineData("IF 5 > 3 \"T\" \"F\"", "T")]
    [InlineData("IF 3 > 5 \"T\" \"F\"", "F")]
    [InlineData("IF 0 \"T\" \"F\"", "F")]
    [InlineData("IF 1 = 2 \"T\" \"F\" \\* MERGEFORMAT", "T")]
    public void An_If_Field_Libreoffice_Works_Out_Shows_That_Result(string instruction, string shown)
    {
        RichTextDocument document = DocxTestPackage.ReadBody(Field(instruction, "CACHED")).Document;

        Assert.Equal(shown, Shown(document.Paragraphs[0]));
        Assert.Equal(shown + "CACHED", document.Paragraphs[0].Text);
    }

    [Theory]
    [InlineData("IF \"a\" = \"a\" \"T\" \"F\"")]
    [InlineData("IF \"x.x@x.x\" = \"\" \"\" \"email\"")]
    [InlineData("IF 1 + 1 \"T\" \"F\"")]
    [InlineData("IF \"T\" \"F\"")]
    public void An_If_Field_Libreoffice_Does_Not_Work_Out_Keeps_Its_Saved_Result(string instruction)
    {
        RichTextDocument document = DocxTestPackage.ReadBody(Field(instruction, "CACHED")).Document;

        Assert.Equal("CACHED", Shown(document.Paragraphs[0]));
    }

    [Fact]
    public void An_If_Field_With_A_Nested_Field_Keeps_Its_Saved_Result()
    {
        RichTextDocument document = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
            "<w:r><w:instrText xml:space=\"preserve\"> IF </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
            "<w:r><w:instrText xml:space=\"preserve\"> MERGEFIELD n </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>1</w:t></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>" +
            "<w:r><w:instrText xml:space=\"preserve\"> = 2 \"T\" \"F\" </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>CACHED</w:t></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>").Document;

        Assert.EndsWith("CACHED", Shown(document.Paragraphs[0]));
        Assert.DoesNotContain("T", Shown(document.Paragraphs[0]));
        Assert.DoesNotContain("F", Shown(document.Paragraphs[0]));
    }

    private static string Group(string children) =>
        "<w:p><w:r><w:drawing>" +
        "<wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\">" +
        "<wp:extent cx=\"2540000\" cy=\"1270000\"/>" +
        "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
        "<a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingGroup\">" +
        "<wpg:wgp xmlns:wpg=\"http://schemas.microsoft.com/office/word/2010/wordprocessingGroup\" " +
        "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
        "<wpg:cNvGrpSpPr/><wpg:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"2540000\" cy=\"1270000\"/>" +
        "<a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"20000\" cy=\"10000\"/></a:xfrm></wpg:grpSpPr>" +
        children +
        "</wpg:wgp></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>";

    private static string Box(long x, long y, long cx, long cy, string text) =>
        "<wps:wsp><wps:cNvSpPr txBox=\"1\"/><wps:spPr><a:xfrm><a:off x=\"" + x + "\" y=\"" + y + "\"/>" +
        "<a:ext cx=\"" + cx + "\" cy=\"" + cy + "\"/></a:xfrm><a:prstGeom prst=\"rect\"/>" +
        "<a:solidFill><a:srgbClr val=\"FFFFFF\"/></a:solidFill></wps:spPr>" +
        "<wps:txbx><w:txbxContent><w:p><w:r><w:t>" + text + "</w:t></w:r></w:p></w:txbxContent></wps:txbx>" +
        "<wps:bodyPr/></wps:wsp>";

    [Fact]
    public void A_Group_Of_One_Shape_Draws_It_Over_The_Whole_Group()
    {
        // shape-in-floattable.docx: the one text box sits at the group's right
        // in its child space; LibreOffice imports it over the whole group.
        DocumentShape shape = Assert.Single(DocxTestPackage.ReadBody(Group(Box(12000, 6000, 8000, 3000, "Hello"))).Document.Shapes);

        Assert.Equal(0, shape.OffsetX, 3);
        Assert.Equal(200, shape.Width, 3);
        Assert.Equal(100, shape.Height, 3);
    }

    [Fact]
    public void A_Group_Of_Two_Shapes_Still_Places_Each_In_The_Child_Space()
    {
        RichTextDocument document = DocxTestPackage.ReadBody(
            Group(Box(12000, 6000, 8000, 3000, "Hello") + Box(0, 0, 2000, 1000, "x"))).Document;

        Assert.Equal(2, document.Shapes.Count);
        Assert.Equal(120, document.Shapes[0].OffsetX, 3);
        Assert.Equal(80, document.Shapes[0].Width, 3);
    }
}
