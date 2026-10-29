namespace Broiler.Documents.Odt.Tests;

public sealed class OdtPageStyleTransitionTests
{
    private const string Layouts = "<office:automatic-styles>" +
        "<style:page-layout style:name=\"a\"><style:page-layout-properties fo:page-width=\"200pt\" fo:page-height=\"300pt\" fo:margin-left=\"10pt\" fo:margin-right=\"10pt\" fo:margin-top=\"10pt\" fo:margin-bottom=\"10pt\"/></style:page-layout>" +
        "<style:page-layout style:name=\"b\"><style:page-layout-properties fo:page-width=\"300pt\" fo:page-height=\"200pt\" fo:margin-left=\"20pt\" fo:margin-right=\"20pt\" fo:margin-top=\"20pt\" fo:margin-bottom=\"20pt\"/></style:page-layout>" +
        "</office:automatic-styles>";

    private static string Masters(bool cycle) => Layouts + "<office:master-styles>" +
        "<style:master-page style:name=\"Standard\" style:page-layout-name=\"a\"" + (cycle ? " style:next-style-name=\"Other\"" : "") +
        "><style:header><text:p>head A</text:p></style:header></style:master-page>" +
        "<style:master-page style:name=\"Other\" style:page-layout-name=\"b\"" + (cycle ? " style:next-style-name=\"Standard\"" : "") +
        "><style:footer><text:p>foot B</text:p></style:footer></style:master-page></office:master-styles>";

    [Fact]
    public void Missing_Master_Definition_Still_Requires_A_Break_Outside_Hidden_Sections()
    {
        var result = OdtTestPackage.ReadBody(
            "<text:p>first</text:p><text:section text:display='none'><text:p text:style-name='missing'>hidden</text:p></text:section>" +
            "<text:p text:style-name='missing'>next page</text:p><text:p text:style-name='cancel'>same page</text:p>",
            "<style:style style:name='missing' style:family='paragraph' style:master-page-name='Absent'/>" +
            "<style:style style:name='cancel' style:family='paragraph' style:parent-style-name='missing' style:master-page-name=''/>");
        Assert.False(result.Document.Paragraphs[1].Style.PageBreakBefore);
        Assert.True(result.Document.Paragraphs[2].Style.PageBreakBefore);
        Assert.False(result.Document.Paragraphs[3].Style.PageBreakBefore);
    }

    [Fact]
    public void A_Later_Paragraph_Changes_Paper_And_Running_Content()
    {
        var result = OdtTestPackage.ReadStyled("<text:p>first</text:p><text:p text:style-name=\"switch\">second</text:p>",
            Masters(false), "<style:style style:name=\"switch\" style:family=\"paragraph\" style:master-page-name=\"Other\"/>");
        var sections = result.Document.Sections;
        Assert.Equal(2, sections.Count);
        Assert.Equal(200, sections[0].Page!.Width);
        Assert.Equal(300, sections[1].Page!.Width);
        Assert.True(result.Document.Paragraphs[1].Style.PageBreakBefore);
        Assert.Equal("head A", Assert.Single(sections[0].RunningContent!.Header(PageSelection.Default)).Text);
        Assert.Empty(sections[1].RunningContent!.Header(PageSelection.Default));
        Assert.Equal("foot B", Assert.Single(sections[1].RunningContent!.Footer(PageSelection.Default)).Text);
    }

    [Fact]
    public void Table_Styles_Can_Start_A_New_Page_Style()
    {
        var result = OdtTestPackage.ReadStyled("<text:p>first</text:p><table:table table:style-name=\"switch\"><table:table-row><table:table-cell><text:p>cell</text:p></table:table-cell></table:table-row></table:table>",
            Masters(false), "<style:style style:name=\"switch\" style:family=\"table\" style:master-page-name=\"Other\"/>");
        Assert.Equal(300, result.Document.Sections[1].Page!.Width);
        Assert.True(Assert.Single(result.Document.Tables).PageBreakBefore);
    }

    [Fact]
    public void Cell_Paragraph_Page_Styles_Do_Not_Change_The_Body_Page()
    {
        var result = OdtTestPackage.ReadStyled("<text:p>first</text:p><table:table><table:table-row><table:table-cell><text:p text:style-name=\"switch\">cell</text:p></table:table-cell></table:table-row></table:table>",
            Masters(false), "<style:style style:name=\"switch\" style:family=\"paragraph\" style:master-page-name=\"Other\"/>");
        Assert.Empty(result.Document.Sections);
    }

    [Fact]
    public void A_Repeating_Master_Chain_Keeps_Both_Papers_And_Bands()
    {
        var document = OdtTestPackage.ReadStyled("<text:p>body</text:p>", Masters(true)).Document;
        var chain = Assert.Single(document.Sections).PageStyles!;
        Assert.Equal(0, chain.RepeatFrom);
        Assert.Equal(200, chain.At(0).Page!.Width);
        Assert.Equal(300, chain.At(1).Page!.Width);
        Assert.Equal(200, chain.At(2).Page!.Width);
        Assert.Equal("foot B", Assert.Single(chain.At(3).RunningContent.Footer(PageSelection.Default)).Text);
    }

    [Fact]
    public void Text_Sections_Between_Page_Styles_Follow_On_The_Page_They_Are_On()
    {
        // fdo114306_2.odt is a form of a dozen text sections under two page
        // styles. Every stretch between two section edges used to start a page
        // of its own, which set its four pages out on eleven.
        var result = OdtTestPackage.ReadStyled(
            "<text:section text:name=\"a\"><text:p>title</text:p></text:section>" +
            "<text:section text:name=\"hidden\" text:display=\"none\"><text:p>hint</text:p></text:section>" +
            "<text:section text:name=\"b\"><text:p>body</text:p></text:section>" +
            "<text:p text:style-name=\"switch\">sheet</text:p>" +
            "<text:section text:name=\"c\"><text:p>more</text:p></text:section>",
            Masters(false), "<style:style style:name=\"switch\" style:family=\"paragraph\" style:master-page-name=\"Other\"/>");

        var sections = result.Document.Sections;
        Assert.Equal(SectionBreakType.NextPage, DocumentSection.Containing(sections, 0)!.BreakType);
        Assert.Equal(SectionBreakType.Continuous, DocumentSection.Containing(sections, 2)!.BreakType);
        Assert.Equal(SectionBreakType.NextPage, DocumentSection.Containing(sections, 3)!.BreakType);
        Assert.Equal(3, DocumentSection.Containing(sections, 4)!.ParagraphIndex);
        Assert.True(DocumentSection.Containing(sections, 1)!.Hidden);
        Assert.Equal([0, 3], sections.Where(section => section.BreakType == SectionBreakType.NextPage).Select(section => section.ParagraphIndex));
    }

    [Fact]
    public void A_Sections_Margins_Indent_It_Under_A_Page_Style_And_Nested_Ones_Add_Up()
    {
        var result = OdtTestPackage.ReadStyled(
            "<text:p>first</text:p>" +
            "<text:section text:style-name=\"half\" text:name=\"title\"><text:p>title</text:p>" +
            "<text:section text:style-name=\"inner\" text:name=\"inner\"><text:p>inner</text:p></text:section></text:section>" +
            "<text:p text:style-name=\"switch\">sheet</text:p>",
            Masters(false),
            "<style:style style:name=\"switch\" style:family=\"paragraph\" style:master-page-name=\"Other\"/>" +
            "<style:style style:name=\"half\" style:family=\"section\"><style:section-properties fo:margin-right=\"8.5cm\"/></style:style>" +
            "<style:style style:name=\"inner\" style:family=\"section\"><style:section-properties fo:margin-left=\"10pt\"/></style:style>");

        DocumentSection title = DocumentSection.Containing(result.Document.Sections, 1)!;
        DocumentSection inner = DocumentSection.Containing(result.Document.Sections, 2)!;
        Assert.Equal((0, 240.94), (title.LeftIndent, Math.Round(title.RightIndent, 2)));
        Assert.Equal((10, 240.94), (inner.LeftIndent, Math.Round(inner.RightIndent, 2)));
        Assert.Equal(200, title.Page!.Width);
        Assert.Equal(SectionBreakType.Continuous, title.BreakType);
        Assert.Equal(0, DocumentSection.Containing(result.Document.Sections, 3)!.LeftIndent);
    }

    [Fact]
    public void A_Deleted_Paragraph_Ahead_Of_The_Body_Does_Not_Choose_Its_Page()
    {
        // text:tracked-changes keeps a deletion's paragraphs ahead of the body.
        // tdf130680.odt's body starts on a first-page style with no footer, and
        // reading the deletion first put it on the default page, footer and all.
        var result = OdtTestPackage.ReadStyled(
            "<text:tracked-changes><text:changed-region text:id=\"c1\"><text:deletion>" +
            "<text:p text:style-name=\"plain\">gone</text:p></text:deletion></text:changed-region></text:tracked-changes>" +
            "<text:p text:style-name=\"switch\">first</text:p>",
            Masters(false),
            "<style:style style:name=\"plain\" style:family=\"paragraph\"/>" +
            "<style:style style:name=\"switch\" style:family=\"paragraph\" style:master-page-name=\"Other\"/>");

        Assert.Equal(300, result.Document.PageGeometry!.Width);
        Assert.Equal("foot B", Assert.Single(result.Document.RunningContent.Footer(PageSelection.Default)).Text);
    }
}
