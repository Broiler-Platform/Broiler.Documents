using Broiler.Graphics.Color;
using Broiler.Documents.Resources;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Covers <c>wps:wsp</c> shapes: the coloured box and the text box a letterhead
/// template is built from. Both used to reach the picture reader, hold no
/// <c>a:blip</c>, and be dropped with a note saying a drawing had no embedded
/// picture — which took a text box's words with it.
/// </summary>
public sealed class DocxShapeTests
{
    private const string GradientShape =
        "<w:r><w:drawing>" +
        "<wp:anchor xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\">" +
        "<wp:positionH relativeFrom=\"column\"><wp:posOffset>-1270000</wp:posOffset></wp:positionH>" +
        "<wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>127000</wp:posOffset></wp:positionV>" +
        "<wp:extent cx=\"1270000\" cy=\"2540000\"/>" +
        "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
        "<a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
        "<wps:wsp xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
        "<wps:spPr>" +
        "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>" +
        "<a:gradFill><a:gsLst>" +
        "<a:gs pos=\"0\"><a:srgbClr val=\"AECF00\"/></a:gs>" +
        "<a:gs pos=\"100000\"><a:srgbClr val=\"FFFFFF\"/></a:gs>" +
        "</a:gsLst><a:lin ang=\"3600000\"/></a:gradFill>" +
        "<a:ln><a:noFill/></a:ln>" +
        "</wps:spPr></wps:wsp>" +
        "</a:graphicData></a:graphic></wp:anchor>" +
        "</w:drawing></w:r>";

    private const string TextBoxShape =
        "<w:r><w:drawing>" +
        "<wp:anchor xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\">" +
        "<wp:positionH relativeFrom=\"column\"><wp:posOffset>-635000</wp:posOffset></wp:positionH>" +
        "<wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>0</wp:posOffset></wp:positionV>" +
        "<wp:extent cx=\"914400\" cy=\"914400\"/>" +
        "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
        "<a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
        "<wps:wsp xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
        "<wps:spPr>" +
        "<a:solidFill><a:srgbClr val=\"FFFFFF\"/></a:solidFill>" +
        "<a:ln><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill></a:ln>" +
        "</wps:spPr>" +
        "<wps:txbx><w:txbxContent>" +
        "<w:p><w:r><w:t>Put your LOGO here</w:t></w:r></w:p>" +
        "</w:txbxContent></wps:txbx>" +
        "</wps:wsp>" +
        "</a:graphicData></a:graphic></wp:anchor>" +
        "</w:drawing></w:r>";

    private static RichTextDocument Read(string runXml) =>
        DocxTestPackage.ReadBody("<w:p>" + runXml + "<w:r><w:t>body</w:t></w:r></w:p>").Document;

    [Theory]
    [InlineData("spAutoFit", true)]
    [InlineData("noAutofit", false)]
    [InlineData("normAutofit", false)]
    public void Explicit_Shape_Autofit_Is_Read_And_Round_Trips(string kind, bool grows)
    {
        var document = Read(TextBoxShape.Replace("</wps:wsp>",
            "<wps:bodyPr><a:" + kind + "/></wps:bodyPr></wps:wsp>"));
        Assert.Equal(grows, Assert.Single(document.Shapes).GrowsToText);
        using var stream = new MemoryStream(DocxWriter.WriteToArray(document));
        Assert.Equal(grows, Assert.Single(new DocxDocumentCodec().Read(stream).Document.Shapes).GrowsToText);
    }

    [Theory]
    [InlineData(ShapeGeometry.Rectangle, 40d)]
    [InlineData(ShapeGeometry.Ellipse, 40d)]
    [InlineData(ShapeGeometry.Line, 0d)]
    [InlineData(ShapeGeometry.Line, 40d)]
    [InlineData(ShapeGeometry.LineAscending, 40d)]
    public void Empty_Unfilled_Geometry_And_Stroke_Width_Survive_Export(ShapeGeometry geometry, double height)
    {
        var source = RichTextDocument.FromPlainText("body").WithShapes([new DocumentShape(0, 10, 20, 80, height,
            outline: BColor.Black, geometry: geometry, outlineWidth: 2.5)]);
        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source));
        var restored = Assert.Single(new DocxDocumentCodec().Read(stream).Document.Shapes);
        Assert.Null(restored.Fill);
        Assert.Equal(geometry, restored.Geometry);
        Assert.Equal(2.5, restored.OutlineWidth, 3);
        Assert.Equal(height, restored.Height, 3);
    }

    [Fact]
    public void A_Straight_Connector_Uses_A_Line_Path()
    {
        var shape = Assert.Single(Read(GradientShape.Replace("prst=\"rect\"", "prst=\"straightConnector1\"")).Shapes);
        Assert.Equal(ShapeGeometry.Line, shape.Geometry);
    }

    [Fact]
    public void Reads_A_Gradient_Shape_With_Its_Colours_And_Angle()
    {
        DocumentShape shape = Assert.Single(Read(GradientShape).Shapes);

        Assert.NotNull(shape.Fill);
        Assert.True(shape.Fill!.IsGradient);
        Assert.Equal(0xAE, shape.Fill.Start.R);
        Assert.Equal(0xCF, shape.Fill.Start.G);
        Assert.Equal(0x00, shape.Fill.Start.B);
        Assert.Equal(60, shape.Fill.AngleDegrees, 3);
    }

    [Fact]
    public void Places_A_Shape_In_Points_From_The_Text_Column()
    {
        DocumentShape shape = Assert.Single(Read(GradientShape).Shapes);

        // 12700 EMU to the point, and a negative offset puts it in the margin.
        Assert.Equal(-100, shape.OffsetX, 3);
        Assert.Equal(10, shape.OffsetY, 3);
        Assert.Equal(100, shape.Width, 3);
        Assert.Equal(200, shape.Height, 3);
    }

    [Fact]
    public void Reads_The_Text_Inside_A_Text_Box()
    {
        DocumentShape shape = Assert.Single(Read(TextBoxShape).Shapes);

        Assert.Equal("Put your LOGO here", Assert.Single(shape.Paragraphs).Text);
        Assert.True(shape.HasText);
    }

    [Fact]
    public void Keeps_A_Shape_Out_Of_The_Body_Flow()
    {
        // The words in a logo box belong to the box, not to the letter.
        Assert.Equal("body", Read(TextBoxShape).PlainText);
    }

    [Fact]
    public void Reads_A_Solid_Fill_And_Its_Outline()
    {
        DocumentShape shape = Assert.Single(Read(TextBoxShape).Shapes);

        Assert.NotNull(shape.Fill);
        Assert.False(shape.Fill!.IsGradient);
        Assert.False(shape.Outline.IsEmpty);
        Assert.Equal(0x00, shape.Outline.R);
    }

    [Fact]
    public void An_Outline_Turned_Off_Is_No_Outline()
    {
        Assert.True(Assert.Single(Read(GradientShape).Shapes).Outline.IsEmpty);
    }

    [Fact]
    public void Anchors_A_Shape_To_The_Paragraph_It_Sits_In()
    {
        RichTextDocument document = DocxTestPackage.ReadBody(
            DocxTestPackage.Paragraph("first") +
            "<w:p>" + GradientShape + "<w:r><w:t>second</w:t></w:r></w:p>").Document;

        Assert.Equal(1, Assert.Single(document.Shapes).ParagraphIndex);
    }

    [Fact]
    public void A_Shape_Reports_No_Missing_Picture()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody("<w:p>" + GradientShape + "</w:p>");

        // It was never a picture. Reporting one as skipped was the reader saying
        // it had lost something it had in fact never looked for.
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "docx.image.shape");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "docx.image.anchored");
    }

    [Fact]
    public void A_Pattern_Fill_Is_Read_As_Hatch_Over_Background_Colour()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody("<w:p>" + DocxThemeColorTests.ShapeRun(
            "<a:pattFill prst=\"wdUpDiag\">" +
            "<a:fgClr><a:srgbClr val=\"5EAD35\"/></a:fgClr>" +
            "<a:bgClr><a:srgbClr val=\"007929\"/></a:bgClr>" +
            "</a:pattFill>") + "</w:p>");

        ShapeFill fill = Assert.Single(result.Document.Shapes).Fill!;
        Assert.False(fill.IsGradient);
        Assert.Equal(BColor.FromArgb(0x00, 0x79, 0x29), fill.Start);
        Assert.NotNull(fill.Hatch);
        Assert.Equal(BColor.FromArgb(0x5E, 0xAD, 0x35), fill.Hatch.Color);
        Assert.Equal(45, fill.Hatch.AngleDegrees);
        Assert.Equal(1, fill.Hatch.Directions);
    }

    [Fact]
    public void A_Pattern_With_No_Background_Is_Its_Foreground()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody("<w:p>" + DocxThemeColorTests.ShapeRun(
            "<a:pattFill prst=\"pct50\"><a:fgClr><a:srgbClr val=\"5EAD35\"/></a:fgClr></a:pattFill>") + "</w:p>");

        Assert.Equal(BColor.FromArgb(0x5E, 0xAD, 0x35), Assert.Single(result.Document.Shapes).Fill!.Start);
    }

    [Fact]
    public void Shapes_Survive_A_Round_Trip()
    {
        RichTextDocument source = Read(GradientShape + TextBoxShape);
        Assert.Equal(2, source.Shapes.Count);

        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source), writable: false);
        RichTextDocument actual = new DocxDocumentCodec().Read(stream).Document;

        Assert.Equal(2, actual.Shapes.Count);
        Assert.Contains(actual.Shapes, s => s.Fill?.IsGradient == true);
        Assert.Contains(actual.Shapes, s => s.Paragraphs.Any(p => p.Text == "Put your LOGO here"));
    }

    [Fact]
    public void A_Shape_That_States_No_Stacking_Is_Behind_The_Text()
    {
        // Neither fixture carries behindDoc, which is the letterhead case: the
        // stripe and the logo box are what the letter is written on top of.
        Assert.All(Read(GradientShape + TextBoxShape).Shapes, shape => Assert.True(shape.BehindText));
    }

    [Fact]
    public void A_Shape_Stacked_In_Front_Of_The_Text_Keeps_That_Through_A_Round_Trip()
    {
        RichTextDocument source = Read(
            GradientShape.Replace(
                "<wp:anchor xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\">",
                "<wp:anchor xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
                "behindDoc=\"0\">",
                StringComparison.Ordinal));

        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source), writable: false);
        RichTextDocument actual = new DocxDocumentCodec().Read(stream).Document;

        Assert.False(Assert.Single(source.Shapes).BehindText);
        Assert.False(Assert.Single(actual.Shapes).BehindText);
    }

    [Theory]
    [InlineData("<wp:wrapSquare wrapText=\"bothSides\"/>", ShapeWrap.Square, WrapSide.Largest)]
    [InlineData("<wp:wrapSquare wrapText=\"left\"/>", ShapeWrap.Square, WrapSide.Left)]
    [InlineData("<wp:wrapSquare wrapText=\"right\"/>", ShapeWrap.Square, WrapSide.Right)]
    // Both follow the picture's outline; the model follows its box.
    [InlineData("<wp:wrapTight/>", ShapeWrap.Square, WrapSide.Largest)]
    [InlineData("<wp:wrapThrough/>", ShapeWrap.Square, WrapSide.Largest)]
    [InlineData("<wp:wrapTopAndBottom/>", ShapeWrap.TopAndBottom, WrapSide.Largest)]
    [InlineData("<wp:wrapNone/>", ShapeWrap.None, WrapSide.Largest)]
    [InlineData("", ShapeWrap.None, WrapSide.Largest)]
    public void Reads_The_Wrap_An_Anchor_States(string wrapXml, ShapeWrap expected, WrapSide side)
    {
        DocumentShape shape = Assert.Single(Read(
            GradientShape.Replace("<wp:extent", wrapXml + "<wp:extent", StringComparison.Ordinal)).Shapes);

        Assert.Equal(expected, shape.Wrap);
        Assert.Equal(side, shape.WrapSide);
    }

    [Theory]
    [InlineData("<wp:wrapSquare wrapText=\"left\"/>", "wrapSquare")]
    [InlineData("<wp:wrapTopAndBottom/>", "wrapTopAndBottom")]
    public void A_Wrap_Survives_A_Round_Trip(string wrapXml, string expectedElement)
    {
        // The writer stated wrapNone whatever it was given, so a picture read
        // with text flowing around it was saved as one the text runs under - the
        // arrangement lost on every save.
        RichTextDocument source = Read(
            GradientShape.Replace("<wp:extent", wrapXml + "<wp:extent", StringComparison.Ordinal));

        byte[] bytes = DocxDocumentCodec.WriteToArray(source);
        using var package = new System.IO.Compression.ZipArchive(
            new MemoryStream(bytes), System.IO.Compression.ZipArchiveMode.Read);
        using var reader = new StreamReader(package.GetEntry("word/document.xml")!.Open());
        Assert.Contains(expectedElement, reader.ReadToEnd(), StringComparison.Ordinal);

        using var stream = new MemoryStream(bytes, writable: false);
        RichTextDocument actual = new DocxDocumentCodec().Read(stream).Document;
        Assert.Equal(
            Assert.Single(source.Shapes).Wrap,
            Assert.Single(actual.Shapes).Wrap);
    }

    [Fact]
    public void An_Anchors_Relative_Height_Is_The_Shapes_Z_Order()
    {
        // A letterhead states its stacking here and nowhere else: the stripe in
        // the header and the box over it in the body carry relativeHeight 2 and 3,
        // and reading neither left the render painting them in whatever order the
        // parts happened to be walked in.
        string shape = GradientShape.Replace(
            "<wp:anchor xmlns:wp=",
            "<wp:anchor relativeHeight=\"7\" xmlns:wp=",
            StringComparison.Ordinal);

        Assert.Equal(7, Assert.Single(Read(shape).Shapes).ZOrder);
    }

    [Fact]
    public void An_Anchor_That_States_No_Relative_Height_Reads_As_Zero()
    {
        // Zero is "the format said nothing", which leaves the order the shapes
        // were read in - the answer every anchor got before this was read at all.
        Assert.Equal(0, Assert.Single(Read(GradientShape).Shapes).ZOrder);
    }

    [Fact]
    public void A_Z_Order_Round_Trips_Through_The_Writer()
    {
        RichTextDocument source = RichTextDocument
            .FromPlainText("body")
            .WithShapes([new DocumentShape(0, -40, 0, 30, 200, ShapeFill.Solid(BColor.Black), zOrder: 4)]);

        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source), writable: false);

        Assert.Equal(4, Assert.Single(new DocxDocumentCodec().Read(stream).Document.Shapes).ZOrder);
    }

    [Fact]
    public void Editing_The_Body_Keeps_The_Shapes()
    {
        RichTextDocument document = Read(TextBoxShape);

        RichTextDocument edited = document.ApplyParagraphStyle(
            new RichTextRange(RichTextDocument.Start, document.End),
            ParagraphStyleDelta.WithAlignment(TextAlignment.Center));

        Assert.Single(edited.Shapes);
    }

    [Fact]
    public void A_Page_Anchored_Shape_Is_Measured_From_The_Page_And_Says_It_Lost_The_Number()
    {
        var shape = new DocumentShape(0, 10, 300, 100, 50, ShapeFill.Solid(BColor.Black), anchorPage: 3);
        RichTextDocument document = RichTextDocument.FromPlainText("body").WithShapes([shape]);

        using var output = new MemoryStream();
        DocumentWriteResult result = new DocxDocumentCodec().Write(document, output);
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(output.ToArray()));
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        string xml = reader.ReadToEnd();

        Assert.Matches("positionV relativeFrom=\"page\"", xml);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.shape.page");
    }
    [Fact]
    public void DrawingML_Pattern_Fill_Round_Trips()
    {
        var hatch = new HatchFill(BColor.FromArgb(0x5E, 0xAD, 0x35), 6, 45, 1);
        var fill = ShapeFill.Solid(BColor.FromArgb(0x00, 0x79, 0x29)) with { Hatch = hatch };
        var shape = new DocumentShape(0, 10, 20, 100, 50, fill);
        var document = RichTextDocument.FromPlainText("body").WithShapes([shape]);

        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(document), writable: false);
        DocumentReadResult read = new DocxDocumentCodec().Read(stream);

        DocumentShape restored = Assert.Single(read.Document.Shapes);
        Assert.NotNull(restored.Fill?.Hatch);
        Assert.Equal(BColor.FromArgb(0x00, 0x79, 0x29), restored.Fill.Start);
        Assert.Equal(BColor.FromArgb(0x5E, 0xAD, 0x35), restored.Fill.Hatch.Color);
        Assert.Equal(45, restored.Fill.Hatch.AngleDegrees);
        Assert.DoesNotContain(read.Diagnostics, d => d.Code == "docx.fill.extended");
    }

    [Theory]
    [InlineData(GradientGeometry.Radial, 0.5, 0.5)]
    [InlineData(GradientGeometry.Rectangular, 0.3, 0.7)]
    public void DrawingML_Non_Linear_Gradients_Round_Trip(GradientGeometry geometry, double cx, double cy)
    {
        var fill = new ShapeFill(BColor.FromArgb(255, 0, 0), BColor.FromArgb(0, 0, 255), 0)
        {
            Geometry = geometry,
            CenterX = cx,
            CenterY = cy,
        };
        var shape = new DocumentShape(0, 10, 20, 100, 50, fill);
        var document = RichTextDocument.FromPlainText("body").WithShapes([shape]);

        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(document), writable: false);
        DocumentReadResult read = new DocxDocumentCodec().Read(stream);

        DocumentShape restored = Assert.Single(read.Document.Shapes);
        Assert.NotNull(restored.Fill);
        Assert.Equal(geometry, restored.Fill.Geometry);
        Assert.Equal(cx, restored.Fill.CenterX, 2);
        Assert.Equal(cy, restored.Fill.CenterY, 2);
        Assert.DoesNotContain(read.Diagnostics, d => d.Code == "docx.fill.extended");
    }

    private static (InlineImage Image, DocumentWriteOptions Options) Writable(InlineImage image)
    {
        var builder = new DocumentConversionContextBuilder(DocumentResourcePolicy.AllowOwnDocuments);
        InlineImage admitted = builder.AdmitImage(
            image,
            DocumentResourceProvenance.CallerSupplied,
            DocumentResourceDisposition.Embedded);

        return (admitted, new DocumentWriteOptions(resources: builder.Build()));
    }

    [Fact]
    public void DrawingML_Shape_Blip_Fill_Round_Trips()
    {
        var picture = new InlineImage(DocxTestPackage.OnePixelPng, "image/png", 100, 50);
        (InlineImage admitted, DocumentWriteOptions writeOptions) = Writable(picture);
        var fill = ShapeFill.Solid(BColor.Empty) with
        {
            Image = new ImageFill(admitted, ImageFillRepeat.Stretch),
        };
        var shape = new DocumentShape(0, 10, 20, 100, 50, fill);
        var document = RichTextDocument.FromPlainText("body").WithShapes([shape]);

        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(document, writeOptions), writable: false);
        DocumentReadResult read = new DocxDocumentCodec().Read(stream, new DocumentReadOptions(resourcePolicy: DocumentResourcePolicy.AllowOwnDocuments));

        DocumentShape restored = Assert.Single(read.Document.Shapes);
        Assert.NotNull(restored.Fill?.Image);
        Assert.Equal(ImageFillRepeat.Stretch, restored.Fill.Image.Repeat);
        Assert.DoesNotContain(read.Diagnostics, d => d.Code == "docx.fill.extended");
    }

    [Fact]
    public void Extended_Fill_Diagnostic_Emitted_Only_When_Approximated()
    {
        var fill = ShapeFill.Solid(BColor.Black) with { Border = 0.5 };
        var shape = new DocumentShape(0, 10, 20, 100, 50, fill);
        var document = RichTextDocument.FromPlainText("body").WithShapes([shape]);

        using var output = new MemoryStream();
        DocumentWriteResult result = new DocxDocumentCodec().Write(document, output);

        Assert.Contains(result.Diagnostics, d => d.Code == "docx.fill.extended");
    }
}
