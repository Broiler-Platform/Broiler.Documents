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

    [Fact(Timeout = 600000)]
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

    [Fact(Timeout = 600000)]
    public void Places_A_Shape_In_Points_From_The_Text_Column()
    {
        DocumentShape shape = Assert.Single(Read(GradientShape).Shapes);

        // 12700 EMU to the point, and a negative offset puts it in the margin.
        Assert.Equal(-100, shape.OffsetX, 3);
        Assert.Equal(10, shape.OffsetY, 3);
        Assert.Equal(100, shape.Width, 3);
        Assert.Equal(200, shape.Height, 3);
    }

    [Fact(Timeout = 600000)]
    public void Reads_The_Text_Inside_A_Text_Box()
    {
        DocumentShape shape = Assert.Single(Read(TextBoxShape).Shapes);

        Assert.Equal("Put your LOGO here", Assert.Single(shape.Paragraphs).Text);
        Assert.True(shape.HasText);
    }

    [Fact(Timeout = 600000)]
    public void Keeps_A_Shape_Out_Of_The_Body_Flow()
    {
        // The words in a logo box belong to the box, not to the letter.
        Assert.Equal("body", Read(TextBoxShape).PlainText);
    }

    [Fact(Timeout = 600000)]
    public void Reads_A_Solid_Fill_And_Its_Outline()
    {
        DocumentShape shape = Assert.Single(Read(TextBoxShape).Shapes);

        Assert.NotNull(shape.Fill);
        Assert.False(shape.Fill!.IsGradient);
        Assert.False(shape.Outline.IsEmpty);
        Assert.Equal(0x00, shape.Outline.R);
    }

    [Fact(Timeout = 600000)]
    public void An_Outline_Turned_Off_Is_No_Outline()
    {
        Assert.True(Assert.Single(Read(GradientShape).Shapes).Outline.IsEmpty);
    }

    [Fact(Timeout = 600000)]
    public void Anchors_A_Shape_To_The_Paragraph_It_Sits_In()
    {
        RichTextDocument document = DocxTestPackage.ReadBody(
            DocxTestPackage.Paragraph("first") +
            "<w:p>" + GradientShape + "<w:r><w:t>second</w:t></w:r></w:p>").Document;

        Assert.Equal(1, Assert.Single(document.Shapes).ParagraphIndex);
    }

    [Fact(Timeout = 600000)]
    public void A_Shape_Reports_No_Missing_Picture()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody("<w:p>" + GradientShape + "</w:p>");

        // It was never a picture. Reporting one as skipped was the reader saying
        // it had lost something it had in fact never looked for.
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "docx.image.shape");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "docx.image.anchored");
    }

    [Fact(Timeout = 600000)]
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

    [Fact(Timeout = 600000)]
    public void Editing_The_Body_Keeps_The_Shapes()
    {
        RichTextDocument document = Read(TextBoxShape);

        RichTextDocument edited = document.ApplyParagraphStyle(
            new RichTextRange(document.Start, document.End),
            ParagraphStyleDelta.WithAlignment(TextAlignment.Center));

        Assert.Single(edited.Shapes);
    }
}
