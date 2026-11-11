namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Text boxes whose text runs down or up their side (office issue #238): a
/// DrawingML <c>a:bodyPr vert</c> and a VML <c>layout-flow</c>, which were read
/// as ordinary left-to-right text.
/// </summary>
public sealed class DocxOfficeIssue238Tests
{
    private const string Vml =
        "xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\"";

    private static DocumentShape ReadTextBox(string bodyPrAttributes) =>
        Assert.Single(DocxTestPackage.ReadBody(
            "<w:p><w:r><w:drawing>" +
            "<wp:anchor xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\">" +
            "<wp:positionH relativeFrom=\"column\"><wp:posOffset>0</wp:posOffset></wp:positionH>" +
            "<wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>0</wp:posOffset></wp:positionV>" +
            "<wp:extent cx=\"724992\" cy=\"4894580\"/>" +
            "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
            "<a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
            "<wps:wsp xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
            "<wps:spPr><a:prstGeom prst=\"rect\"/></wps:spPr>" +
            "<wps:txbx><w:txbxContent><w:p><w:r><w:t>Vertical Text</w:t></w:r></w:p></w:txbxContent></wps:txbx>" +
            "<wps:bodyPr " + bodyPrAttributes + "/>" +
            "</wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r></w:p>").Document.Shapes);

    private static DocumentShape ReadVmlTextBox(string textBoxStyle) =>
        Assert.Single(DocxTestPackage.ReadBody(
            "<w:p><w:r><w:pict " + Vml + ">" +
            "<v:rect style=\"position:absolute;width:57.1pt;height:385.4pt\" stroked=\"f\">" +
            "<v:textbox style=\"" + textBoxStyle + "\"><w:txbxContent><w:p><w:r><w:t>Vertical Text</w:t></w:r></w:p>" +
            "</w:txbxContent></v:textbox></v:rect></w:pict></w:r></w:p>").Document.Shapes);

    [Theory]
    [InlineData("vert=\"vert\"", ShapeTextFlow.TopToBottom)]
    [InlineData("vert=\"eaVert\"", ShapeTextFlow.TopToBottom)]
    [InlineData("vert=\"vert270\"", ShapeTextFlow.BottomToTop)] // fdo69636.docx
    [InlineData("vert=\"horz\"", ShapeTextFlow.Horizontal)]
    [InlineData("", ShapeTextFlow.Horizontal)]
    public void A_DrawingML_Text_Box_Reads_Which_Way_Its_Text_Runs(string attributes, ShapeTextFlow expected)
    {
        DocumentShape shape = ReadTextBox(attributes);

        Assert.Equal(expected, shape.TextFlow);
        Assert.True(shape.HasText);
    }

    [Theory]
    [InlineData("layout-flow:vertical", ShapeTextFlow.TopToBottom)] // tbrl-frame-vml.docx
    [InlineData("layout-flow:vertical;mso-layout-flow-alt:bottom-to-top", ShapeTextFlow.BottomToTop)] // btlr-frame-vml.docx
    [InlineData("layout-flow:horizontal", ShapeTextFlow.Horizontal)]
    [InlineData("mso-fit-shape-to-text:t", ShapeTextFlow.Horizontal)]
    public void A_Vml_Text_Box_Reads_Which_Way_Its_Text_Runs(string style, ShapeTextFlow expected)
    {
        DocumentShape shape = ReadVmlTextBox(style);

        Assert.Equal(expected, shape.TextFlow);
        Assert.Equal(57.1, shape.Width, 3);
        Assert.Equal(385.4, shape.Height, 3);
    }

    private static string FramedParagraph(string framePr, string text) =>
        "<w:p><w:pPr><w:framePr " + framePr + "/></w:pPr><w:r><w:t>" + text + "</w:t></w:r></w:p>";

    [Fact]
    public void A_Header_Frame_Placed_On_The_Page_Is_A_Shape_Of_The_Header()
    {
        // tdf112446_frameStyle.docx: a footer's page number framed 237 points
        // down the page; it was a line of the band, 80 points higher.
        RichTextDocument document = DocxRunningContentTests.ReadWithHeader(
            FramedParagraph("w:w=\"335\" w:wrap=\"auto\" w:vAnchor=\"page\" w:hAnchor=\"page\" w:x=\"3742\" w:y=\"4745\"", "1") +
            DocxTestPackage.Paragraph("after")).Document;

        DocumentShape frame = Assert.Single(document.RunningContent.HeaderShapes(PageSelection.Default));
        Assert.Equal("1", Assert.Single(frame.Paragraphs).Text);
        Assert.Equal(4745 / 20d, frame.OffsetY, 3);
        Assert.True(frame.OffsetFromPage);
        Assert.Equal(335 / 20d, frame.Width, 3);
        Assert.Equal("after", Assert.Single(document.RunningContent.Header(PageSelection.Default)).Text);
    }

    [Fact]
    public void A_Header_Frame_That_Hangs_From_Its_Paragraph_Stays_A_Paragraph()
    {
        // The usual page number: vAnchor="text" at the band's right.
        RichTextDocument document = DocxRunningContentTests.ReadWithHeader(
            FramedParagraph("w:wrap=\"around\" w:vAnchor=\"text\" w:hAnchor=\"margin\" w:xAlign=\"right\" w:y=\"1\"", "7")).Document;

        Assert.Empty(document.RunningContent.HeaderShapes(PageSelection.Default));
        Assert.Equal("7", Assert.Single(document.RunningContent.Header(PageSelection.Default)).Text);
    }

    [Fact]
    public void A_Frames_Own_Position_Replaces_The_Alignment_Its_Style_Gives()
    {
        // tdf112446_frameStyle.docx: the style says xAlign="right", the
        // paragraph says x="3742"; LibreOffice places the frame at x.
        string styles = DocxTestPackage.Style("PageNumber",
            "<w:pPr><w:framePr w:wrap=\"auto\" w:vAnchor=\"text\" w:hAnchor=\"margin\" w:xAlign=\"right\" w:y=\"1\"/></w:pPr>");
        DocumentShape frame = Assert.Single(DocxTestPackage.ReadStyled(
            "<w:p><w:pPr><w:pStyle w:val=\"PageNumber\"/><w:framePr w:w=\"335\" w:vAnchor=\"page\" w:hAnchor=\"page\" " +
            "w:x=\"3742\" w:y=\"4745\"/></w:pPr><w:r><w:t>1</w:t></w:r></w:p>" +
            "<w:sectPr><w:pgSz w:w=\"8392\" w:h=\"5954\"/><w:pgMar w:left=\"2109\" w:right=\"1701\" w:top=\"1701\" w:bottom=\"1701\"/></w:sectPr>",
            styles).Document.Shapes);

        Assert.Equal((3742 - 2109) / 20d, frame.OffsetX, 3);
        Assert.Equal(4745 / 20d, frame.OffsetY, 3);
    }
}
