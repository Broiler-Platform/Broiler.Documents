namespace Broiler.Documents.Odt.Tests;

public sealed class OdtHiddenSectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_Hidden_Section_Inside_A_Cell_Does_Not_Duplicate_The_Table_On_Write(bool nested)
    {
        string table = "<table:table><table:table-row><table:table-cell><text:section text:display='none'>" +
            "<text:p>secret</text:p></text:section></table:table-cell><table:table-cell><text:p>visible</text:p>" +
            "</table:table-cell></table:table-row></table:table>";
        if (nested)
            table = "<table:table><table:table-row><table:table-cell>" + table +
                "</table:table-cell><table:table-cell><text:p>outside</text:p></table:table-cell></table:table-row></table:table>";
        var document = OdtTestPackage.ReadBody(table).Document;
        var restored = OdtWriterTests.RoundTrip(document);
        Assert.Equal(document.PlainText, restored.PlainText);
        Assert.Single(restored.Tables);
        Assert.True(Assert.Single(restored.Sections).Hidden);
        Assert.Equal(1, restored.Sections[0].ParagraphCount);
    }

    [Fact]
    public void A_Hidden_First_Block_Does_Not_Select_The_Initial_Page_Style()
    {
        string styles = "<office:automatic-styles>" +
            "<style:page-layout style:name='wide'><style:page-layout-properties fo:page-width='300pt' fo:page-height='400pt'/></style:page-layout>" +
            "<style:page-layout style:name='narrow'><style:page-layout-properties fo:page-width='200pt' fo:page-height='400pt'/></style:page-layout>" +
            "</office:automatic-styles><office:master-styles>" +
            "<style:master-page style:name='Visible' style:page-layout-name='wide'/>" +
            "<style:master-page style:name='Hidden' style:page-layout-name='narrow'/></office:master-styles>";
        using var stream = new MemoryStream(OdtTestPackage.FromBody(
            "<text:section text:display='none'><text:p text:style-name='hidden'>secret</text:p></text:section>" +
            "<text:p text:style-name='visible'>body</text:p>",
            "<style:style style:name='hidden' style:family='paragraph' style:master-page-name='Hidden'/>" +
            "<style:style style:name='visible' style:family='paragraph' style:master-page-name='Visible'/>",
            stylesPartInnerXml: styles));
        var document = new OdtDocumentCodec().Read(stream).Document;
        Assert.Equal(300, document.PageGeometry!.Width);
        Assert.True(DocumentSection.Containing(document.Sections, 0)!.Hidden);
    }

    [Fact]
    public void Hidden_Section_Content_And_Visibility_Survive_Odt_Round_Trip()
    {
        var document = OdtTestPackage.ReadBody("<text:p>before</text:p><text:section text:name='hidden' text:display='none'>" +
            "<text:p>retained</text:p></text:section><text:p>after</text:p>").Document;
        Assert.Equal("before\nretained\nafter", document.PlainText);
        var hidden = Assert.Single(document.Sections);
        Assert.True(hidden.Hidden);
        Assert.Equal(1, hidden.ParagraphIndex);
        Assert.True(hidden.Shifted(0, 0, 1)!.Hidden);
        var restored = OdtWriterTests.RoundTrip(document);
        Assert.Equal(document.PlainText, restored.PlainText);
        Assert.True(Assert.Single(restored.Sections).Hidden);
    }

    [Fact]
    public void An_Inner_Visible_Section_Does_Not_Reveal_A_Hidden_Outer_Section()
    {
        var document = OdtTestPackage.ReadBody("<text:section text:display='none'><text:p>A</text:p>" +
            "<text:section><text:p>B</text:p></text:section><text:p>C</text:p></text:section><text:p>visible</text:p>").Document;
        var hidden = Assert.Single(document.Sections);
        Assert.True(hidden.Hidden);
        Assert.Equal(3, hidden.ParagraphCount);
    }

    [Fact]
    public void A_Hidden_Paragraphs_Trailing_Break_Does_Not_Reach_Visible_Text()
    {
        var document = OdtTestPackage.ReadBody("<text:p>before</text:p><text:section text:display='none'>" +
            "<text:p text:style-name='break'>hidden</text:p></text:section><text:p>after</text:p>",
            "<style:style style:name='break' style:family='paragraph'><style:paragraph-properties fo:break-after='page'/></style:style>").Document;
        Assert.False(document.Paragraphs[^1].Style.PageBreakBefore);
    }

    [Fact]
    public void A_Visible_Paragraphs_Trailing_Break_Passes_Across_A_Hidden_Section()
    {
        var document = OdtTestPackage.ReadBody("<text:p text:style-name='break'>before</text:p>" +
            "<text:section text:display='none'><text:p>hidden</text:p></text:section><text:p>after</text:p>",
            "<style:style style:name='break' style:family='paragraph'><style:paragraph-properties fo:break-after='page'/></style:style>").Document;
        Assert.True(document.Paragraphs[^1].Style.PageBreakBefore);
    }
}
