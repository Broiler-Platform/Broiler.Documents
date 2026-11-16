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

        Assert.Equal(fromParagraph, shape.OffsetFromBandParagraph is not null);
        if (fromParagraph)
            Assert.Equal(10, shape.OffsetFromBandParagraph!.Value, 3);
    }

    [Fact]
    public void A_Body_Shape_Is_Not_Placed_From_A_Header_Paragraph()
    {
        DocumentShape shape = Assert.Single(DocxTestPackage.ReadBody("<w:p>" + ShapeRun("paragraph", 0) + "</w:p>").Document.Shapes);

        Assert.Null(shape.OffsetFromBandParagraph);
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

        Assert.Equal(fromParagraph, shape.OffsetFromBandParagraph is not null);
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

    [Theory]
    [InlineData(@"file:///F:\ISUW\ole9.gif", false)]
    [InlineData(@"C:\pictures\logo.png", false)]
    [InlineData("linked-picture.png", true)]
    [InlineData("http://example.com/flag.jpg", true)]
    public void A_Linked_Picture_On_The_Authors_Disk_Leaves_No_Mark(string target, bool marked)
    {
        // FileWithInvalidImageLink.docx: LibreOffice prints nothing for a link
        // to file:///F:\...; a relative link it looks up beside the document,
        // and draws what it finds (linked-picture.docx).
        string run = DocxTestPackage.AnchoredDrawingRun("rId7", 914400, 914400)
            .Replace("r:embed=\"rId7\"", "r:link=\"rId9\"", StringComparison.Ordinal);
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p>" + run + "</w:p>",
            "<Relationship Id=\"rId9\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" " +
            "Target=\"" + target + "\" TargetMode=\"External\"/>",
            new Dictionary<string, byte[]>(StringComparer.Ordinal));

        InlineImage image = Assert.Single(result.Document.Shapes).Image!;
        Assert.True(image.IsMissingPayload);
        Assert.Equal(marked, image.MarksMissingPayload);
    }

    [Fact]
    public void An_Inline_WordArt_String_Is_Marked_As_Shape_Text()
    {
        // fdo77727.docx: the inline WordArt "Currents" is drawn in its line,
        // but LibreOffice's text export does not write it.
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:pict><v:shape xmlns:v=\"urn:schemas-microsoft-com:vml\" style=\"width:285.3pt;height:37.35pt\" fillcolor=\"#4e6128\">" +
            "<v:textpath on=\"t\" style=\"font-family:&quot;Times New Roman&quot;\" string=\"Currents\"/></v:shape></w:pict></w:r>" +
            "<w:r><w:t>Combat</w:t></w:r></w:p>");

        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("CurrentsCombat", paragraph.Text);
        Assert.True(paragraph.Runs[0].Style.IsShapeText);
        Assert.False(paragraph.Runs[^1].Style.IsShapeText);
    }

    [Theory]
    [InlineData("<wp:wrapNone/>", false)]
    [InlineData("<wp:wrapSquare wrapText=\"bothSides\"/>", true)]
    [InlineData("<wp:wrapTight wrapText=\"bothSides\"><wp:wrapPolygon edited=\"0\"><wp:start x=\"0\" y=\"0\"/><wp:lineTo x=\"21600\" y=\"0\"/><wp:lineTo x=\"21600\" y=\"21600\"/><wp:lineTo x=\"0\" y=\"0\"/></wp:wrapPolygon></wp:wrapTight>", true)]
    [InlineData("<wp:wrapTopAndBottom/>", true)]
    public void A_Wrapped_Object_Behind_The_Document_Is_Drawn_In_Front(string wrap, bool inFront)
    {
        // tdf60351.docx: LibreOffice draws a tightly wrapped behindDoc picture
        // over its paragraph's shading; with wp:wrapNone the shading covers it.
        string run = DocxTestPackage.AnchoredDrawingRun("rId7", 914400, 914400, behindDoc: "1")
            .Replace("<wp:wrapSquare wrapText=\"bothSides\"/>", wrap, StringComparison.Ordinal);
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p>" + run + "</w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.png"),
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["word/media/image1.png"] = DocxTestPackage.OnePixelPng });

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Equal(inFront, shape.DrawsInFront);
        // What the document says is kept, so it is written back as read.
        Assert.True(shape.BehindText);
    }

    [Theory]
    // tdf105143.docx's freeform: "hd" before its curve, which LibreOffice passes over.
    [InlineData("m,57hdc80,137,78,189,131,282v41,72,98,127,131,205c270,505,271,526,281,543l2039,132xe", true)]
    [InlineData("m,57ha12,34l2039,132xe", true)]
    [InlineData("m,57hz l2039,132xe", false)] // only ha to hi are Office's tokens
    public void A_Vml_Freeform_Passes_Over_Offices_H_Tokens(string path, bool drawn)
    {
        var shape = new System.Xml.Linq.XElement("shape", new System.Xml.Linq.XAttribute("coordsize", "2039,1347"));

        byte[]? png = DocxVmlPath.Render(shape, path, 102, 67, new Broiler.Graphics.Color.BColor(0, 0, 0),
            new Broiler.Graphics.Color.BColor(0, 0, 0), 0.75);

        Assert.Equal(drawn, png is not null);
    }

    [Theory]
    [InlineData("tile", ImageFillRepeat.Tile)]
    [InlineData("frame", ImageFillRepeat.Stretch)]
    public void A_Vml_Picture_Fill_Paints_Its_Picture(string type, ImageFillRepeat repeat)
    {
        // tdf78749.docx: a text box tiled with water drops, drawn in one colour.
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p><w:r><w:pict><v:rect xmlns:v=\"urn:schemas-microsoft-com:vml\" " +
            "style=\"position:absolute;margin-left:66pt;margin-top:74.2pt;width:228pt;height:99pt\">" +
            "<v:fill r:id=\"rId7\" type=\"" + type + "\"/></v:rect></w:pict></w:r></w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.png"),
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["word/media/image1.png"] = DocxTestPackage.OnePixelPng });

        ImageFill picture = Assert.Single(result.Document.Shapes).Fill!.Image!;
        Assert.Equal(repeat, picture.Repeat);
        Assert.False(picture.Picture.IsMissingPayload);
    }

    [Fact]
    public void A_Band_Shape_Is_Placed_On_The_Page_Of_The_Section_That_Shows_It()
    {
        // watermark-shapetype.docx: a portrait section and a landscape one,
        // each with a watermark centred in the margin. The portrait one was
        // centred on the landscape page.
        const string Header =
            "<w:p><w:r><w:pict><v:rect xmlns:v=\"urn:schemas-microsoft-com:vml\" fillcolor=\"silver\" " +
            "style=\"position:absolute;margin-left:0;margin-top:0;width:100pt;height:100pt;" +
            "mso-position-vertical:center;mso-position-vertical-relative:margin\"/></w:pict></w:r></w:p>";
        static string Section(int width, int height) =>
            "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rH\"/><w:pgSz w:w=\"" + width + "\" w:h=\"" + height + "\"/>" +
            "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>";
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/_rels/document.xml.rels"] =
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rH\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\"/>" +
                "</Relationships>",
            ["word/header1.xml"] = "<w:hdr " + DocxTestPackage.BodyNamespaceDeclarations + ">" + Header + "</w:hdr>",
        };
        using var stream = new MemoryStream(DocxTestPackage.FromBody(
            "<w:p><w:pPr>" + Section(11906, 16838) + "</w:pPr><w:r><w:t>portrait</w:t></w:r></w:p>" +
            "<w:p><w:r><w:t>landscape</w:t></w:r></w:p>" + Section(16838, 11906), parts), writable: false);
        RichTextDocument document = new DocxDocumentCodec().Read(stream).Document;

        double Top(RunningContent running) => Assert.Single(running.HeaderShapes(PageSelection.Default)).OffsetY;
        Assert.Equal(72 + (16838 / 20d - 144 - 100) / 2, Top(document.Sections[0].RunningContent!), 3);
        Assert.Equal(72 + (11906 / 20d - 144 - 100) / 2, Top(document.RunningContent), 3);
    }

    [Fact]
    public void A_Turned_Floating_Picture_Takes_Its_Turned_Box_About_The_Same_Centre()
    {
        // position-and-rotation.docx: a picture turned 110 degrees was drawn
        // unturned. A quarter turn of a 100 x 50 pt picture at (10, 20) is a
        // 50 x 100 pt box about the same centre, (35, -5).
        string run = DocxTestPackage.AnchoredDrawingRun("rId7", 1270000, 635000, offsetXEmus: 127000, offsetYEmus: 254000)
            .Replace("<pic:spPr>", "<pic:spPr><a:xfrm rot=\"5400000\"><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1270000\" cy=\"635000\"/></a:xfrm>",
                StringComparison.Ordinal)
            .Replace("<wp:wrapSquare wrapText=\"bothSides\"/>",
                "<wp:wrapTight wrapText=\"bothSides\"><wp:wrapPolygon edited=\"0\"><wp:start x=\"0\" y=\"0\"/>" +
                "<wp:lineTo x=\"21600\" y=\"0\"/><wp:lineTo x=\"21600\" y=\"21600\"/><wp:lineTo x=\"0\" y=\"21600\"/></wp:wrapPolygon></wp:wrapTight>",
                StringComparison.Ordinal);
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p>" + run + "</w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.png"),
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["word/media/image1.png"] = DocxTestPackage.OnePixelPng });

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Equal(50, shape.Width, 3);
        Assert.Equal(100, shape.Height, 3);
        Assert.Equal(35, shape.OffsetX, 3);
        Assert.Equal(-5, shape.OffsetY, 3);
        Assert.Equal(90, shape.Image!.Presentation.Rotation, 3);
        Assert.Equal(100, shape.Image.Presentation.UnrotatedWidth!.Value, 3);
        // The wrap polygon turns with it: the unturned box's top left corner
        // is the turned box's top right.
        (double X, double Y) first = shape.WrapPolygon![0];
        Assert.Equal(1, first.X, 3);
        Assert.Equal(0, first.Y, 3);
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
