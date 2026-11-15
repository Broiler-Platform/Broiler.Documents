namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// DOCX shapes of office issue #249 (docx-images-shapes): a header's shape
/// placed from its paragraph, and a drawn shape the document hides.
/// </summary>
public sealed class DocxIssue249ImagesShapesTests
{
    /// <summary>A DrawingML box, its vertical frame <paramref name="verticalFrom"/>.</summary>
    private static string ShapeRun(string verticalFrom, long offsetY, string docPr = "<wp:docPr id=\"1\" name=\"Box\"/>") =>
        "<w:r><w:drawing>" +
        "<wp:anchor xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" behindDoc=\"0\" simplePos=\"0\" relativeHeight=\"2\">" +
        "<wp:positionH relativeFrom=\"column\"><wp:posOffset>0</wp:posOffset></wp:positionH>" +
        "<wp:positionV relativeFrom=\"" + verticalFrom + "\"><wp:posOffset>" + offsetY + "</wp:posOffset></wp:positionV>" +
        "<wp:extent cx=\"914400\" cy=\"228600\"/><wp:wrapNone/>" + docPr +
        "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
        "<a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
        "<wps:wsp xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
        "<wps:spPr><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val=\"FF0000\"/></a:solidFill></wps:spPr>" +
        "<wps:bodyPr/></wps:wsp></a:graphicData></a:graphic></wp:anchor>" +
        "</w:drawing></w:r>";

    private static DocumentShape HeaderShape(string runXml)
    {
        RichTextDocument document = DocxRunningContentTests.ReadWithHeader("<w:p>" + runXml + "</w:p>").Document;
        return Assert.Single(document.RunningContent.HeaderShapes(PageSelection.Default));
    }

    [Theory]
    [InlineData("paragraph", true)]
    [InlineData("line", true)]
    [InlineData("page", false)]
    [InlineData("margin", false)]
    public void A_Header_Shape_Placed_From_Its_Paragraph_Says_So(string verticalFrom, bool fromParagraph)
    {
        // Cool15691_headerTextWrapsAroundImage.docx: LibreOffice draws the
        // picture at nought from its header paragraph, not from the page top.
        DocumentShape shape = HeaderShape(ShapeRun(verticalFrom, 127000));

        Assert.Equal(fromParagraph, shape.OffsetFromBandParagraph);
        if (fromParagraph)
            Assert.Equal(10, shape.OffsetY, 3);
    }

    [Fact]
    public void A_Body_Shape_Is_Not_Placed_From_A_Header_Paragraph()
    {
        DocumentShape shape = Assert.Single(DocxTestPackage.ReadBody("<w:p>" + ShapeRun("paragraph", 0) + "</w:p>").Document.Shapes);

        Assert.False(shape.OffsetFromBandParagraph);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("mso-position-vertical-relative:text;", true)]
    [InlineData("mso-position-vertical-relative:page;", false)]
    public void A_Header_Vml_Shape_Without_A_Page_Frame_Hangs_From_Its_Paragraph(string frame, bool fromParagraph)
    {
        // n779834.docx: a full-page picture in the header with no vertical
        // frame (w10:wrap's anchory="margin" is not read by LibreOffice); it is
        // drawn from the header paragraph, 28.35 points down.
        DocumentShape shape = HeaderShape(
            "<w:r><w:pict><v:rect xmlns:v=\"urn:schemas-microsoft-com:vml\" fillcolor=\"red\" " +
            "style=\"position:absolute;margin-left:0;margin-top:0;width:100pt;height:20pt;" + frame + "\"/></w:pict></w:r>");

        Assert.Equal(fromParagraph, shape.OffsetFromBandParagraph);
    }

    [Fact]
    public void A_Hidden_Drawn_Shape_Is_Not_Printed()
    {
        // tdf156484.docx / tdf169802_hidden_shape.docx: LibreOffice 24.2 neither
        // shows nor prints a shape whose wp:docPr says hidden="1", and wraps
        // text around it all the same.
        DocumentShape hidden = Assert.Single(DocxTestPackage.ReadBody(
            "<w:p>" + ShapeRun("paragraph", 0, "<wp:docPr id=\"1\" name=\"Box\" hidden=\"1\"/>") + "</w:p>").Document.Shapes);
        DocumentShape shown = Assert.Single(DocxTestPackage.ReadBody("<w:p>" + ShapeRun("paragraph", 0) + "</w:p>").Document.Shapes);

        Assert.False(hidden.Printable);
        Assert.True(shown.Printable);
    }

    [Fact]
    public void A_Hidden_Picture_Is_Still_Printed()
    {
        // tdf162527_hidden_image.docx: LibreOffice 24.2 draws the hidden picture.
        string run = DocxTestPackage.AnchoredDrawingRun("rId7", 914400, 914400)
            .Replace("name=\"Picture 1\"/>", "name=\"Picture 1\" hidden=\"1\"/>", StringComparison.Ordinal);
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p>" + run + "</w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.png"),
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["word/media/image1.png"] = DocxTestPackage.OnePixelPng });

        Assert.True(Assert.Single(result.Document.Shapes).Printable);
    }
}
