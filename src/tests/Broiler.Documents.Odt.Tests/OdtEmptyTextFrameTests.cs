namespace Broiler.Documents.Odt.Tests;

public sealed class OdtEmptyTextFrameTests
{
    [Fact]
    public void A_Final_Empty_Frame_Keeps_Its_Paragraph_Anchor()
    {
        var document = OdtTestPackage.ReadBody("<text:p>body</text:p>" +
            "<draw:frame text:anchor-type='paragraph' svg:width='1cm' svg:height='1cm'>" +
            "<draw:text-box><text:p/></draw:text-box></draw:frame>").Document;
        Assert.Equal(2, document.ParagraphCount);
        Assert.Equal(1, Assert.Single(document.Shapes).ParagraphIndex);
        Assert.Equal("", document.Paragraphs[1].Text);
    }

    [Fact]
    public void An_Unrepresented_Drawing_Is_Not_Treated_As_An_Empty_Text_Frame()
    {
        var result = OdtTestPackage.ReadBody(
            "<draw:frame text:anchor-type='page' svg:width='2cm' svg:height='2cm'><draw:text-box>" +
            "<text:p><draw:frame text:anchor-type='as-char' svg:width='1cm' svg:height='1cm'>" +
            "<draw:object/></draw:frame></text:p></draw:text-box></draw:frame><text:p>body</text:p>");
        Assert.Empty(result.Document.Shapes);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "odt.frame.textbox");
        Assert.Equal("body", result.Document.Paragraphs[^1].Text);
    }

    [Theory]
    [InlineData("page")]
    [InlineData("paragraph")]
    public void An_Empty_Positioned_Text_Frame_Does_Not_Add_A_Body_Paragraph(string anchor)
    {
        var result = OdtTestPackage.ReadBody(
            $"<draw:frame draw:style-name='empty' text:anchor-type='{anchor}' text:anchor-page-number='1' " +
            "svg:width='1cm' svg:height='1cm'><draw:text-box><text:p/></draw:text-box></draw:frame>" +
            "<text:p>body</text:p>",
            "<style:style style:name='empty' style:family='graphic'><style:graphic-properties " +
            "draw:fill='none' draw:stroke='none' style:wrap='parallel'/></style:style>");
        Assert.Equal("body", Assert.Single(result.Document.Paragraphs).Text);
        DocumentShape frame = Assert.Single(result.Document.Shapes);
        Assert.False(frame.HasText);
        Assert.True(frame.Wraps);
        Assert.Equal(anchor == "page", frame.IsPageAnchored);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "odt.frame.textbox");
    }
}
