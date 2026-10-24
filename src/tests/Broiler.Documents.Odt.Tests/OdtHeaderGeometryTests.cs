namespace Broiler.Documents.Odt.Tests;

public sealed class OdtHeaderGeometryTests
{
    [Fact]
    public void A_Positioned_Frames_Text_Does_Not_Select_The_Bodys_First_Page()
    {
        string styles = "<office:automatic-styles>" +
            "<style:page-layout style:name='frame'><style:page-layout-properties fo:page-width='400pt' fo:page-height='600pt'/></style:page-layout>" +
            "<style:page-layout style:name='body'><style:page-layout-properties fo:page-width='300pt' fo:page-height='600pt'/></style:page-layout>" +
            "</office:automatic-styles><office:master-styles>" +
            "<style:master-page style:name='Frame' style:page-layout-name='frame'/>" +
            "<style:master-page style:name='Body' style:page-layout-name='body'/></office:master-styles>";
        using var stream = new MemoryStream(OdtTestPackage.FromBody(
            "<draw:frame text:anchor-type='page' text:anchor-page-number='1' svg:width='10pt' svg:height='10pt'>" +
            "<draw:text-box><text:p text:style-name='frame'>box</text:p></draw:text-box></draw:frame>" +
            "<text:p text:style-name='body'>body</text:p>",
            "<style:style style:name='frame' style:family='paragraph' style:master-page-name='Frame'/>" +
            "<style:style style:name='body' style:family='paragraph' style:master-page-name='Body'/>",
            stylesPartInnerXml: styles));
        Assert.Equal(300, new OdtDocumentCodec().Read(stream).Document.PageGeometry!.Width);
    }

    [Theory]
    [InlineData("height")]
    [InlineData("min-height")]
    public void The_Outer_Page_Margin_And_Header_Band_Are_Separate(string height)
    {
        var document = Read("<style:header><text:p>header</text:p></style:header>", height).Document;
        Assert.Equal(20, document.PageGeometry!.HeaderDistance);
        Assert.Equal(40, document.PageGeometry.MarginTop);
        Assert.Equal(30, document.PageGeometry.MarginBottom);
    }

    [Fact]
    public void An_Unused_Header_Style_Does_Not_Enlarge_The_Body_Margin()
    {
        var document = Read("", "min-height").Document;
        Assert.Equal(20, document.PageGeometry!.MarginTop);
        Assert.Equal(0, document.PageGeometry.HeaderDistance);
    }

    [Fact]
    public void An_Explicit_Header_Distance_And_Body_Margin_Round_Trip()
    {
        var source = RichTextDocument.FromPlainText("body")
            .WithPageGeometry(new PageGeometry(400, 600, 30, 30, 72, 30, HeaderDistance: 18))
            .WithRunningContent(RunningContent.Empty.WithHeader(PageSelection.Default, [RichTextParagraph.Plain("header")]));
        var restored = OdtWriterTests.RoundTrip(source);
        Assert.Equal(72, restored.PageGeometry!.MarginTop, 3);
        Assert.Equal(18, restored.PageGeometry.HeaderDistance, 3);
        Assert.Equal("header", Assert.Single(restored.RunningContent.Header(PageSelection.Default)).Text);
    }

    [Fact]
    public void Page_Content_Anchored_Objects_Start_Below_The_Header_Band()
    {
        var document = Read("<style:header><text:p>header</text:p></style:header>", "min-height",
            "<draw:rect draw:style-name='positioned' text:anchor-type='page' text:anchor-page-number='1' " +
            "svg:x='0pt' svg:y='0pt' svg:width='10pt' svg:height='10pt'/><text:p>body</text:p>").Document;
        Assert.Equal(40, Assert.Single(document.Shapes).OffsetY);
    }

    private static DocumentReadResult Read(string header, string height, string body = "<text:p>body</text:p>")
    {
        string styles = "<office:automatic-styles><style:style style:name='positioned' style:family='graphic'>" +
            "<style:graphic-properties draw:fill='solid' draw:fill-color='#ff0000' " +
            "style:vertical-rel='page-content' style:horizontal-rel='page-content'/></style:style>" +
            "<style:page-layout style:name='page'>" +
            "<style:page-layout-properties fo:page-width='400pt' fo:page-height='600pt' fo:margin-left='30pt' " +
            "fo:margin-right='30pt' fo:margin-top='20pt' fo:margin-bottom='30pt'/>" +
            $"<style:header-style><style:header-footer-properties fo:{height}='20pt'/></style:header-style>" +
            "</style:page-layout></office:automatic-styles><office:master-styles>" +
            "<style:master-page style:name='Standard' style:page-layout-name='page'>" + header +
            "</style:master-page></office:master-styles>";
        using var stream = new MemoryStream(OdtTestPackage.FromBody(body, stylesPartInnerXml: styles));
        return new OdtDocumentCodec().Read(stream);
    }
}
