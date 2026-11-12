namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Issue #242, DOCX layout cluster: a section's page number format, and
/// <c>w:contextualSpacing</c> inherited the way writerfilter imports it
/// (tdf170602_exportContext.docx).
/// </summary>
public sealed class DocxIssue242DocxLayoutTests
{
    private static string Section(string numberType) =>
        "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +
        "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/>" +
        numberType + "</w:sectPr>";

    [Fact]
    public void A_Sections_Page_Number_Format_Is_Read()
    {
        // The front matter numbers its pages I, II, III; the body in decimal.
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:pPr>" + Section("<w:pgNumType w:fmt=\"upperRoman\"/>") + "</w:pPr><w:r><w:t>front</w:t></w:r></w:p>" +
            DocxTestPackage.Paragraph("body") +
            Section("<w:pgNumType w:start=\"1\"/>"));

        Assert.Equal(2, result.Document.Sections.Count);
        Assert.Equal(ListNumberFormat.UpperRoman, result.Document.Sections[0].PageNumberFormat);
        Assert.Null(result.Document.Sections[1].PageNumberFormat);
    }

    [Fact]
    public void A_Page_Field_With_A_Format_Switch_Does_Not_Follow_The_Section()
    {
        RichTextParagraph paragraph = Assert.Single(DocxTestPackage.ReadBody(
            "<w:p><w:fldSimple w:instr=\" PAGE \\* arabic \"><w:r><w:t>3</w:t></w:r></w:fldSimple></w:p>").Document.Paragraphs);

        Assert.False(paragraph.StyleAt(0).PageField!.FollowsPageFormat);
    }

    private const string ListParagraph =
        "<w:style w:type=\"paragraph\" w:styleId=\"LP\"><w:name w:val=\"List Paragraph\"/>" +
        "<w:pPr><w:ind w:left=\"720\"/><w:contextualSpacing/></w:pPr></w:style>";

    private const string Heading =
        "<w:style w:type=\"paragraph\" w:styleId=\"H3\"><w:name w:val=\"heading 3\"/><w:basedOn w:val=\"LP\"/>" +
        "<w:pPr><w:spacing w:before=\"360\" w:after=\"120\"/></w:pPr></w:style>";

    private static IReadOnlyList<RichTextParagraph> Headings(string styles) =>
        DocxTestPackage.ReadStyled(
            DocxTestPackage.StyledParagraph("H3", "one") + DocxTestPackage.StyledParagraph("H3", "two"),
            styles).Document.Paragraphs;

    [Fact]
    public void A_Style_With_Its_Own_Spacing_Inherits_Contextual_Spacing_From_A_Parent_Listed_Before_It()
    {
        IReadOnlyList<RichTextParagraph> paragraphs = Headings(ListParagraph + Heading);

        Assert.Equal(0, paragraphs[0].Style.SpacingAfter, 3);
        Assert.Equal(0, paragraphs[1].Style.SpacingBefore, 3);
    }

    [Fact]
    public void A_Style_With_Its_Own_Spacing_Does_Not_Inherit_Contextual_Spacing_From_A_Parent_Listed_After_It()
    {
        // tdf170602_exportContext.docx lists its headings before "List
        // Paragraph"; LibreOffice 24.2 keeps their 18 and 6 points.
        IReadOnlyList<RichTextParagraph> paragraphs = Headings(Heading + ListParagraph);

        Assert.Equal(6, paragraphs[0].Style.SpacingAfter, 3);
        Assert.Equal(18, paragraphs[1].Style.SpacingBefore, 3);
    }

    [Fact]
    public void A_Style_Stating_No_Spacing_Inherits_Contextual_Spacing_From_A_Parent_Listed_After_It()
    {
        const string plainHeading =
            "<w:style w:type=\"paragraph\" w:styleId=\"H3\"><w:name w:val=\"heading 3\"/><w:basedOn w:val=\"LP\"/>" +
            "<w:pPr><w:jc w:val=\"left\"/></w:pPr></w:style>";
        const string spacedList =
            "<w:style w:type=\"paragraph\" w:styleId=\"LP\"><w:name w:val=\"List Paragraph\"/>" +
            "<w:pPr><w:spacing w:before=\"240\"/><w:contextualSpacing/></w:pPr></w:style>";

        IReadOnlyList<RichTextParagraph> paragraphs = Headings(plainHeading + spacedList);

        Assert.Equal(0, paragraphs[1].Style.SpacingBefore, 3);
    }
}
