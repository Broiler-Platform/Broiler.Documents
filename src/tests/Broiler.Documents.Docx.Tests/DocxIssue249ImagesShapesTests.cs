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

    private static DocumentShape PresetShape(string preset, string transform = "", string adjust = "") =>
        Assert.Single(DocxTestPackage.ReadBody("<w:p>" + ShapeRun("paragraph", 0)
            .Replace("<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>",
                "<a:xfrm" + transform + "><a:off x=\"0\" y=\"0\"/><a:ext cx=\"914400\" cy=\"228600\"/></a:xfrm>" +
                "<a:prstGeom prst=\"" + preset + "\"><a:avLst>" + adjust + "</a:avLst></a:prstGeom>", StringComparison.Ordinal) +
            "</w:p>").Document.Shapes);

    [Fact]
    public void A_Five_Point_Star_Is_Drawn_As_Its_Outline()
    {
        // tdf159158_zOrder_1and0equalB.docx: LibreOffice draws two stars where
        // broilerdoc drew two boxes. The default star's top point is at the
        // box's top centre, its lower points at the foot.
        DocumentShape star = PresetShape("star5");

        IReadOnlyList<(double X, double Y)> outline = Assert.Single(star.OutlinePaths!);
        Assert.Equal(10, outline.Count);
        Assert.Contains(outline, point => Math.Abs(point.X - 0.5) < 1e-9 && Math.Abs(point.Y) < 1e-9);
        Assert.Equal(1, outline.Max(point => point.Y), 2);
        Assert.Equal(0, outline.Min(point => point.X), 2);
        Assert.Equal(1, outline.Max(point => point.X), 2);
    }

    [Fact]
    public void A_Triangle_Takes_Its_Apex_From_Its_Adjust_Value_And_Flips()
    {
        IReadOnlyList<(double X, double Y)> apexLeft = Assert.Single(PresetShape("triangle",
            adjust: "<a:gd name=\"adj\" fmla=\"val 0\"/>").OutlinePaths!);
        Assert.Equal([(0d, 1d), (0d, 0d), (1d, 1d)], apexLeft);

        IReadOnlyList<(double X, double Y)> flipped = Assert.Single(PresetShape("triangle", " flipV=\"1\"").OutlinePaths!);
        Assert.Equal([(0d, 0d), (0.5, 1d), (1d, 0d)], flipped);

        // A half turn is both flips (rot180-flipv.docx).
        IReadOnlyList<(double X, double Y)> turned = Assert.Single(PresetShape("rtTriangle", " rot=\"10800000\" flipV=\"1\"").OutlinePaths!);
        Assert.Equal([(1d, 1d), (1d, 0d), (0d, 1d)], turned);
    }

    [Fact]
    public void A_Quarter_Turned_Preset_And_A_Rectangle_Keep_Their_Box()
    {
        // The reader does not turn a drawn shape's box, so a quarter-turned
        // arrow (tdf87924.docx) stays a box rather than an arrow lying wrong.
        Assert.Null(PresetShape("rightArrow", " rot=\"5400000\"").OutlinePaths);
        Assert.Null(PresetShape("rect").OutlinePaths);
        Assert.NotNull(PresetShape("rightArrow").OutlinePaths);
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
