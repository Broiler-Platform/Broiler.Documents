using Broiler.Graphics;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Floating shapes in ODT. A draw:custom-shape was on the reader's ignorable
/// list and a text-box frame was flattened into the body with a note that its
/// position could not be represented — which stopped being true once the model
/// grew somewhere to put it.
/// </summary>
public sealed class OdtShapeTests
{
    private static readonly ShapeFill Green =
        new(BColor.FromArgb(0xFF, 0xAE, 0xCF, 0x00), BColor.White, 60);

    private static RichTextDocument WithShapes(params DocumentShape[] shapes) =>
        RichTextDocument.FromPlainText("body").WithShapes(shapes);

    private static RichTextDocument RoundTrip(RichTextDocument document)
    {
        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(document), writable: false);
        return new OdtDocumentCodec().Read(stream).Document;
    }

    private static string ContentOf(byte[] odt)
    {
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(odt));
        using var reader = new StreamReader(archive.GetEntry("content.xml")!.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void A_Shape_Round_Trips_With_Its_Box()
    {
        RichTextDocument source = WithShapes(
            new DocumentShape(0, -111.8, -20.05, 100.3, 779.5, Green));

        DocumentShape shape = Assert.Single(RoundTrip(source).Shapes);

        Assert.Equal(-111.8, shape.OffsetX, 1);
        Assert.Equal(-20.05, shape.OffsetY, 1);
        Assert.Equal(100.3, shape.Width, 1);
        Assert.Equal(779.5, shape.Height, 1);
    }

    [Fact]
    public void A_Gradient_Round_Trips_Through_A_Named_Gradient()
    {
        DocumentShape shape = Assert.Single(
            RoundTrip(WithShapes(new DocumentShape(0, -40, 0, 30, 200, Green))).Shapes);

        Assert.NotNull(shape.Fill);
        Assert.True(shape.Fill!.IsGradient);
        Assert.Equal(0xAE, shape.Fill.Start.R);
        Assert.Equal(0xCF, shape.Fill.Start.G);
        Assert.Equal(60, shape.Fill.AngleDegrees, 1);
    }

    [Fact]
    public void A_Solid_Fill_And_Outline_Round_Trip()
    {
        DocumentShape shape = Assert.Single(RoundTrip(WithShapes(
            new DocumentShape(0, -40, 0, 70, 70, ShapeFill.Solid(BColor.White), BColor.Black))).Shapes);

        Assert.NotNull(shape.Fill);
        Assert.False(shape.Fill!.IsGradient);
        Assert.False(shape.Outline.IsEmpty);
    }

    [Fact]
    public void A_Shapes_Text_Round_Trips_And_Stays_Out_Of_The_Body()
    {
        RichTextDocument actual = RoundTrip(WithShapes(new DocumentShape(
            0, -80, 0, 70, 60,
            ShapeFill.Solid(BColor.White),
            BColor.Black,
            [RichTextParagraph.Plain("Put your LOGO here")])));

        Assert.Equal("Put your LOGO here", Assert.Single(Assert.Single(actual.Shapes).Paragraphs).Text);
        Assert.Equal("body", actual.PlainText);
    }

    [Fact]
    public void A_Custom_Shape_Carries_Its_Text_Directly()
    {
        // draw:text-box belongs to draw:frame. Inside a custom shape it is markup
        // a reader cannot parse, and LibreOffice refuses the whole document
        // rather than just the shape.
        string content = ContentOf(OdtDocumentCodec.WriteToArray(WithShapes(new DocumentShape(
            0, -80, 0, 70, 60, ShapeFill.Solid(BColor.White), BColor.Empty,
            [RichTextParagraph.Plain("LOGO")]))));

        int shape = content.IndexOf("<draw:custom-shape", StringComparison.Ordinal);
        int end = content.IndexOf("</draw:custom-shape>", StringComparison.Ordinal);
        string inner = content[shape..end];

        Assert.DoesNotContain("draw:text-box", inner, StringComparison.Ordinal);
        Assert.Contains("LOGO", inner, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Shape_Says_What_Form_It_Has()
    {
        // A custom shape with no enhanced geometry has been given no form, and a
        // reader that meets one keeps the box and drops the text it holds.
        string content = ContentOf(OdtDocumentCodec.WriteToArray(
            WithShapes(new DocumentShape(0, -40, 0, 30, 30, ShapeFill.Solid(BColor.Black)))));

        Assert.Contains("draw:enhanced-geometry", content, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Geometry_Is_Not_Read_As_Content()
    {
        // It sits beside the shape's text; walking it as a block would report an
        // unsupported element for something deliberately unused.
        using var stream = new MemoryStream(
            OdtDocumentCodec.WriteToArray(WithShapes(new DocumentShape(
                0, -40, 0, 30, 30, ShapeFill.Solid(BColor.Black), BColor.Empty,
                [RichTextParagraph.Plain("in the box")]))),
            writable: false);

        DocumentReadResult result = new OdtDocumentCodec().Read(stream);

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "odt.block.unsupported");
    }

    [Theory]
    [InlineData(true, "background")]
    [InlineData(false, "foreground")]
    public void A_Shape_States_Which_Side_Of_The_Text_It_Sits_On(bool behindText, string expected)
    {
        string content = ContentOf(OdtDocumentCodec.WriteToArray(
            WithShapes(new DocumentShape(0, -40, 0, 30, 200, Green, behindText: behindText))));

        Assert.Contains(
            "style:run-through=\"" + expected + "\"",
            content,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_Shape_Round_Trips_The_Side_Of_The_Text_It_Sits_On(bool behindText)
    {
        RichTextDocument source = WithShapes(
            new DocumentShape(0, -40, 0, 30, 200, Green, behindText: behindText));

        Assert.Equal(behindText, Assert.Single(RoundTrip(source).Shapes).BehindText);
    }

    [Theory]
    // ODF's names read backwards: run-through is text through the shape, and
    // none is no text beside it at all.
    [InlineData(ShapeWrap.None, "run-through")]
    [InlineData(ShapeWrap.Square, "parallel")]
    [InlineData(ShapeWrap.TopAndBottom, "none")]
    public void A_Wrap_Round_Trips_Through_The_Graphic_Style(ShapeWrap wrap, string expected)
    {
        RichTextDocument source = WithShapes(
            new DocumentShape(0, -40, 0, 30, 200, Green, wrap: wrap));

        Assert.Contains(
            "style:wrap=\"" + expected + "\"",
            ContentOf(OdtDocumentCodec.WriteToArray(source)),
            StringComparison.Ordinal);
        Assert.Equal(wrap, Assert.Single(RoundTrip(source).Shapes).Wrap);
    }

    [Fact]
    public void A_Document_Without_Shapes_Writes_None()
    {
        Assert.DoesNotContain(
            "draw:custom-shape",
            ContentOf(OdtDocumentCodec.WriteToArray(RichTextDocument.FromPlainText("body"))),
            StringComparison.Ordinal);
    }
}
