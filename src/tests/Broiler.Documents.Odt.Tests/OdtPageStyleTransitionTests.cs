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
}
