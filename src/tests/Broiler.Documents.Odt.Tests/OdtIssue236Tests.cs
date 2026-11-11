namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Office conformance issue 236: i84870.fodt, whose header holds a text frame
/// aligned to the right of the page's content area, 1 mm below its paragraph.
/// </summary>
public sealed class OdtIssue236Tests
{
    // A 400 x 600 pt page with 40 pt margins: a 320 pt content area, and a
    // header that starts 40 pt down the page.
    private static string Styles(string frameProperties) =>
        "<office:automatic-styles><style:page-layout style:name='page'>" +
        "<style:page-layout-properties fo:page-width='400pt' fo:page-height='600pt' fo:margin-top='40pt' " +
        "fo:margin-bottom='40pt' fo:margin-left='40pt' fo:margin-right='40pt'/>" +
        "<style:header-style><style:header-footer-properties fo:margin-bottom='3pt'/></style:header-style>" +
        "</style:page-layout><style:style style:name='fr1' style:family='graphic'>" +
        "<style:graphic-properties " + frameProperties + "/></style:style></office:automatic-styles>" +
        "<office:master-styles><style:master-page style:name='Standard' style:page-layout-name='page'>" +
        "<style:header><text:p><draw:frame draw:style-name='fr1' text:anchor-type='paragraph' svg:y='3pt'>" +
        "<draw:text-box fo:min-height='14pt' fo:min-width='100pt'><text:p>Frame in header</text:p></draw:text-box>" +
        "</draw:frame></text:p></style:header></style:master-page></office:master-styles>";

    private static DocumentShape HeaderFrame(string frameProperties) =>
        Assert.Single(OdtTestPackage.ReadStyled("<text:p>body</text:p>", Styles(frameProperties))
            .Document.RunningContent.HeaderShapes(PageSelection.Default));

    [Fact]
    public void A_Header_Frame_At_The_Right_Of_The_Content_Area_Ends_At_Its_Right_Edge()
    {
        // The header was read without a page, so the content area had no width
        // and the frame went its own width left of the margin, off the page.
        DocumentShape shape = HeaderFrame(
            "style:horizontal-pos='right' style:horizontal-rel='page-content' style:vertical-rel='paragraph'");

        Assert.Equal(100, shape.Width, 2);
        Assert.Equal(320 - 100, shape.OffsetX, 2);
    }

    [Fact]
    public void A_Header_Frame_Is_Offset_Down_From_Its_Paragraph_In_The_Header()
    {
        // svg:y is measured from the paragraph, which is at the top of the header
        // band: the page's top margin. It was read from the top of the page.
        DocumentShape shape = HeaderFrame(
            "style:vertical-pos='from-top' style:vertical-rel='paragraph' style:horizontal-rel='paragraph'");

        Assert.Equal(40 + 3, shape.OffsetY, 2);
        Assert.True(shape.OffsetFromPage);
    }

    [Fact]
    public void A_Header_Frame_Measured_From_The_Page_Keeps_Its_Offset()
    {
        DocumentShape shape = HeaderFrame(
            "style:vertical-pos='from-top' style:vertical-rel='page' style:horizontal-rel='paragraph'");

        Assert.Equal(3, shape.OffsetY, 2);
    }
}
