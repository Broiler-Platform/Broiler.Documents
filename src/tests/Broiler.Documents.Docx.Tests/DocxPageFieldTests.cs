namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// <c>PAGE</c> and <c>NUMPAGES</c> fields, whose saved result a header or
/// footer replaces on every page, and the runs inside a <c>TOC</c> field.
/// </summary>
/// <remarks>
/// <c>wDateValueFormat.docx</c>'s footer reads "Seite PAGE von NUMPAGES" with
/// the results Word saved, 14 and 19, and was drawn so on every page; its
/// table of contents, blue and underlined in Word's <c>Hyperlink</c> style,
/// is plain in LibreOffice.
/// </remarks>
public sealed class DocxPageFieldTests
{
    private static string ComplexField(string instruction, string saved, string separatorRunProperties = "") =>
        "<w:p>" +
        "<w:r><w:t xml:space=\"preserve\">Seite </w:t></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
        "<w:r><w:instrText xml:space=\"preserve\">" + instruction + "</w:instrText></w:r>" +
        "<w:r><w:rPr>" + separatorRunProperties + "</w:rPr><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
        (saved.Length == 0 ? "" : "<w:r><w:t>" + saved + "</w:t></w:r>") +
        "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>" +
        "<w:r><w:t xml:space=\"preserve\"> end</w:t></w:r>" +
        "</w:p>";

    private static RichTextParagraph Read(string bodyXml) =>
        Assert.Single(DocxTestPackage.ReadBody(bodyXml).Document.Paragraphs);

    private static InlineStyle StyleOf(RichTextParagraph paragraph, string text) =>
        paragraph.StyleAt(paragraph.Text.IndexOf(text, StringComparison.Ordinal));

    [Theory]
    [InlineData("PAGE", PageFieldKind.PageNumber)]
    [InlineData(" PAGE \\* MERGEFORMAT ", PageFieldKind.PageNumber)]
    [InlineData(" NUMPAGES ", PageFieldKind.PageCount)]
    [InlineData(" numpages \\* Arabic ", PageFieldKind.PageCount)]
    public void A_Page_Field_Marks_Its_Saved_Result(string instruction, PageFieldKind kind)
    {
        RichTextParagraph paragraph = Read(ComplexField(instruction, "14"));

        Assert.Equal("Seite 14 end", paragraph.Text);
        Assert.Equal(new PageField(kind), StyleOf(paragraph, "14").PageField);
        Assert.Null(StyleOf(paragraph, "Seite").PageField);
        Assert.Null(StyleOf(paragraph, "end").PageField);
    }

    [Theory]
    [InlineData(" PAGE \\* roman ", ListNumberFormat.LowerRoman)]
    [InlineData(" PAGE \\* ROMAN ", ListNumberFormat.UpperRoman)]
    [InlineData(" PAGE \\* alphabetic ", ListNumberFormat.LowerLetter)]
    [InlineData(" PAGE \\* ALPHABETIC ", ListNumberFormat.UpperLetter)]
    public void A_Format_Switch_Writes_The_Number_In_Numerals_Or_Letters(string instruction, ListNumberFormat format)
    {
        RichTextParagraph paragraph = Read(ComplexField(instruction, "x"));

        Assert.Equal(new PageField(PageFieldKind.PageNumber, format), StyleOf(paragraph, "x").PageField);
    }

    [Theory]
    [InlineData(" PAGE \\# \"00\" ")]
    [InlineData(" PAGE \\* ArabicDash ")]
    [InlineData(" SECTIONPAGES ")]
    public void A_Field_This_Does_Not_Work_Out_Keeps_Its_Saved_Result(string instruction)
    {
        RichTextParagraph paragraph = Read(ComplexField(instruction, "14"));

        Assert.Equal("Seite 14 end", paragraph.Text);
        Assert.Null(StyleOf(paragraph, "14").PageField);
    }

    [Fact]
    public void The_Result_Is_Formatted_As_The_Separator_Run_Is()
    {
        // LibreOffice inserts the field at the separator and never reads the
        // saved result's runs: their missing size does not shrink the number.
        RichTextParagraph paragraph = Read(ComplexField("PAGE", "14", "<w:sz w:val=\"24\"/>"));

        InlineStyle style = StyleOf(paragraph, "14");
        Assert.Equal(12f, style.FontSize);
        Assert.NotNull(style.PageField);
    }

    [Fact]
    public void A_Page_Field_Saved_Without_A_Result_Still_Shows_A_Number()
    {
        RichTextParagraph paragraph = Read(ComplexField(" PAGE \\* ROMAN ", ""));

        Assert.Equal("Seite I end", paragraph.Text);
        Assert.Equal(new PageField(PageFieldKind.PageNumber, ListNumberFormat.UpperRoman), StyleOf(paragraph, "I").PageField);
    }

    [Fact]
    public void A_Simple_Page_Field_Marks_Its_Result()
    {
        RichTextParagraph paragraph = Read(
            "<w:p><w:fldSimple w:instr=\" NUMPAGES \"><w:r><w:t>19</w:t></w:r></w:fldSimple></w:p>");

        Assert.Equal("19", paragraph.Text);
        Assert.Equal(new PageField(PageFieldKind.PageCount), paragraph.StyleAt(0).PageField);
    }

    [Fact]
    public void Runs_In_A_Table_Of_Contents_Take_No_Character_Style()
    {
        const string styles =
            "<w:style w:type=\"character\" w:styleId=\"Hyperlink\"><w:name w:val=\"Hyperlink\"/>" +
            "<w:rPr><w:color w:val=\"0000FF\"/><w:u w:val=\"single\"/></w:rPr></w:style>";
        const string entry =
            "<w:hyperlink w:anchor=\"_Toc1\"><w:r><w:rPr><w:rStyle w:val=\"Hyperlink\"/><w:b/></w:rPr><w:t>Status</w:t></w:r></w:hyperlink>";
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
            "<w:r><w:instrText xml:space=\"preserve\"> TOC \\o \"1-3\" \\h \\z \\u </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" + entry + "</w:p>" +
            "<w:p>" + entry + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>" +
            "<w:p>" + entry + "</w:p>",
            styles);

        IReadOnlyList<RichTextParagraph> paragraphs = result.Document.Paragraphs;
        foreach (RichTextParagraph inToc in paragraphs.Take(2))
        {
            InlineStyle style = StyleOf(inToc, "Status");
            Assert.True(style.Foreground.IsEmpty);
            Assert.False(style.Underline);
            Assert.True(style.Bold);
            Assert.Equal("#_Toc1", style.LinkHref);
        }

        InlineStyle after = StyleOf(paragraphs[2], "Status");
        Assert.False(after.Foreground.IsEmpty);
        Assert.True(after.Underline);
    }

    [Fact]
    public void A_Docx_Draws_Its_Links_As_Their_Formatting_Says()
    {
        Assert.True(DocxTestPackage.ReadBody("<w:p><w:r><w:t>x</w:t></w:r></w:p>").Document.StyleDefaults.UndecoratedLinks);
    }
}
