using Broiler.Documents.Cli.Rendering;
using Broiler.Graphics;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// Floating shapes in the render layout. A shape is placed against the paragraph
/// it is anchored to and measured from the text column, which is what lets a
/// letterhead stripe sit in the margin without any page geometry.
/// </summary>
public sealed class DocumentLayoutShapeTests
{
    private static RichTextDocument WithShape(DocumentShape shape, int paragraphs = 1) =>
        RichTextDocument.FromParagraphs(
            Enumerable.Range(0, paragraphs).Select(i => RichTextParagraph.Plain("body " + i)))
            .WithShapes([shape]);

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, PageSetup.Default);
    }

    private static LayoutShape OnlyShape(LayoutResult result) =>
        Assert.Single(result.Pages.SelectMany(page => page.Shapes));

    [Fact]
    public void A_Negative_Offset_Puts_A_Shape_In_The_Margin()
    {
        LayoutResult result = Layout(WithShape(
            new DocumentShape(0, -40, 0, 30, 200, ShapeFill.Solid(BColor.Black))));

        LayoutShape shape = OnlyShape(result);

        Assert.Equal(result.Setup.ContentLeftPoints - 40, shape.Bounds.Left, 3);
        Assert.True(
            shape.Bounds.Left < result.Setup.ContentLeftPoints,
            "the shape should sit left of the text column");
    }

    [Fact]
    public void A_Shape_Hangs_From_The_Paragraph_It_Is_Anchored_To()
    {
        LayoutResult result = Layout(WithShape(
            new DocumentShape(1, 0, 5, 30, 20, ShapeFill.Solid(BColor.Black)),
            paragraphs: 3));

        // Wrapping splits a line across pieces, so match the line's whole text.
        LayoutLine second = result.Pages
            .SelectMany(page => page.Lines)
            .First(line => string.Concat(line.Pieces.Select(piece => piece.Text))
                .Contains("body 1", StringComparison.Ordinal));

        Assert.Equal(second.Top + 5, OnlyShape(result).Bounds.Top, 3);
    }

    [Fact]
    public void A_Shape_Keeps_Its_Fill_And_Outline()
    {
        var fill = new ShapeFill(BColor.Black, BColor.White, 90);
        LayoutShape shape = OnlyShape(Layout(WithShape(
            new DocumentShape(0, 0, 0, 30, 20, fill, BColor.Red))));

        Assert.Equal(fill, shape.Fill);
        Assert.Equal(BColor.Red, shape.Outline);
    }

    [Fact]
    public void A_Shapes_Text_Is_Laid_Out_Inside_The_Shape()
    {
        LayoutShape shape = OnlyShape(Layout(WithShape(
            new DocumentShape(
                0, -60, 0, 50, 40,
                ShapeFill.Solid(BColor.White),
                BColor.Black,
                [RichTextParagraph.Plain("logo")]))));

        LayoutLine line = Assert.Single(shape.Lines);
        LayoutPiece piece = line.Pieces[0];

        // Not against the page's text column: a shape's text belongs in its box.
        Assert.True(
            piece.X >= shape.Bounds.Left - 0.001,
            $"text at {piece.X} started left of the shape at {shape.Bounds.Left}");
        Assert.True(line.Top >= shape.Bounds.Top - 0.001);
    }

    [Fact]
    public void Text_Taller_Than_Its_Shape_Is_Clipped_Rather_Than_Spilling()
    {
        LayoutShape shape = OnlyShape(Layout(WithShape(
            new DocumentShape(
                0, 0, 0, 60, 8,
                ShapeFill.Solid(BColor.White),
                BColor.Empty,
                Enumerable.Range(0, 20).Select(i => RichTextParagraph.Plain("line " + i)).ToList()))));

        Assert.True(shape.Lines.Count < 20, "the overflowing lines should not all be drawn");
        foreach (LayoutLine line in shape.Lines)
            Assert.True(line.Top + line.Height <= shape.Bounds.Bottom + 0.001);
    }

    [Fact]
    public void A_Document_Without_Shapes_Places_None()
    {
        Assert.Empty(Layout(RichTextDocument.FromPlainText("body")).Pages.SelectMany(p => p.Shapes));
    }

    [Fact]
    public void A_Shape_Is_Drawn_Only_On_The_Page_Its_Anchor_Is_On()
    {
        // The anchor top is a y within the page it was measured on, and the map
        // holding it was never emptied at a page break - so a letterhead's logo
        // box, anchored to the first paragraph, was drawn again on every page
        // after it at the y it had on the first.
        var document = RichTextDocument
            .FromParagraphs([
                RichTextParagraph.Plain("first page"),
                RichTextParagraph.Plain("second page")
                    .WithParagraphStyle(ParagraphStyle.Default with { PageBreakBefore = true }),
            ])
            .WithShapes([new DocumentShape(0, -40, 0, 30, 20, ShapeFill.Solid(BColor.Black))]);

        LayoutResult result = Layout(document);

        Assert.Equal(2, result.Pages.Count);
        Assert.Single(result.Pages[0].Shapes);
        Assert.Empty(result.Pages[1].Shapes);
    }

    [Fact]
    public void A_Higher_Z_Order_Draws_Later()
    {
        LayoutResult result = Layout(RichTextDocument
            .FromPlainText("body")
            .WithShapes([
                new DocumentShape(0, 0, 0, 30, 20, ShapeFill.Solid(BColor.White), zOrder: 5),
                new DocumentShape(0, 0, 0, 30, 20, ShapeFill.Solid(BColor.Black), zOrder: 1),
            ]));

        // Read in the wrong order on purpose: the document's own numbering is
        // what decides, not the order the reader happened to walk them in.
        Assert.Equal([1, 5], result.Pages[0].Shapes.Select(shape => shape.ZOrder));
    }

    [Fact]
    public void A_Header_Shape_Draws_Under_A_Body_Shape_That_Outranks_It()
    {
        // The letterhead: a stripe anchored in the header and a logo box anchored
        // in the body, overlapping, with the document saying the box is on top.
        // Running shapes were appended after the body's and painted last, so the
        // stripe covered the box and nothing in the render said so.
        var document = RichTextDocument
            .FromPlainText("body")
            .WithShapes([
                new DocumentShape(
                    0, -40, 0, 30, 20, ShapeFill.Solid(BColor.White), behindText: false, zOrder: 1),
            ])
            .WithRunningContent(RunningContent.Empty.WithHeader(
                PageSelection.Default,
                [],
                [new DocumentShape(
                    0, -40, 0, 30, 200, ShapeFill.Solid(BColor.Black), behindText: false, zOrder: 0)]));

        List<LayoutShape> shapes = Layout(document).Pages[0].Shapes.ToList();

        Assert.Equal(2, shapes.Count);
        Assert.Equal(0, shapes[0].ZOrder);
        Assert.Equal(1, shapes[1].ZOrder);
    }

    [Fact]
    public void Shapes_That_State_No_Order_Keep_The_Order_They_Were_Read_In()
    {
        // Zero means "the format said nothing", and the answer then is the one
        // this gave before z-order was modelled. A sort that was not stable would
        // change that quietly for every document in the corpus.
        LayoutResult result = Layout(RichTextDocument
            .FromPlainText("body")
            .WithShapes([
                new DocumentShape(0, 0, 0, 30, 20, ShapeFill.Solid(BColor.Black)),
                new DocumentShape(0, 0, 0, 30, 20, ShapeFill.Solid(BColor.White)),
                new DocumentShape(0, 0, 0, 30, 20, ShapeFill.Solid(BColor.Red)),
            ]));

        Assert.Equal(
            [BColor.Black, BColor.White, BColor.Red],
            result.Pages[0].Shapes.Select(shape => shape.Fill!.Start));
    }

    [Fact]
    public void The_Body_Is_Not_Moved_By_A_Shape()
    {
        LayoutResult plain = Layout(RichTextDocument.FromPlainText("body 0"));
        LayoutResult decorated = Layout(WithShape(
            new DocumentShape(0, -40, 0, 30, 200, ShapeFill.Solid(BColor.Black))));

        // A shape floats beside the text; it takes no space from the column.
        Assert.Equal(
            plain.Pages[0].Lines[0].Top,
            decorated.Pages[0].Lines.First(l => l.Pieces.Count > 0).Top,
            3);
    }
}
