using Broiler.Documents.Cli.Rendering;
using Broiler.Graphics;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// Text wrapping around a floating shape. Every line used to be as wide as the
/// column whatever stood in it, so a picture was drawn over the words rather
/// than beside them.
/// </summary>
public sealed class DocumentLayoutWrapTests
{
    /// <summary>Long enough that the text runs well past the shape's own height.</summary>
    private const string Body =
        "The quick brown fox jumps over the lazy dog and keeps on running for " +
        "long enough to need several lines of text on the page. It carries on " +
        "past the bottom of the shape so that some of its lines are beside the " +
        "box and some of them are below it, which is the whole of what wrapping " +
        "has to get right. A few more words make the difference plain.";

    private static RichTextDocument With(DocumentShape shape) =>
        RichTextDocument.FromParagraphs([RichTextParagraph.Plain(Body)]).WithShapes([shape]);

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, PageSetup.Default);
    }

    private static List<LayoutLine> BodyLines(LayoutResult result) =>
        result.Pages.SelectMany(page => page.Lines).ToList();

    /// <summary>The rightmost edge any piece of the line reaches.</summary>
    private static double Right(LayoutLine line) =>
        line.Pieces.Max(piece => piece.X + piece.Width);

    private static double Left(LayoutLine line) =>
        line.Pieces.Min(piece => piece.X);

    /// <summary>A 150pt-wide box down the left of the column, 60pt tall.</summary>
    private static DocumentShape LeftBox(ShapeWrap wrap, WrapSide side = WrapSide.Largest) =>
        new(0, 0, 0, 150, 60, ShapeFill.Solid(BColor.Black), wrap: wrap, wrapSide: side);

    [Fact]
    public void Without_A_Wrap_Every_Line_Has_The_Whole_Column()
    {
        List<LayoutLine> lines = BodyLines(Layout(With(LeftBox(ShapeWrap.None))));

        Assert.All(lines, line => Assert.Equal(
            Layout(With(LeftBox(ShapeWrap.None))).Setup.ContentLeftPoints, Left(line), 1));
    }

    [Fact]
    public void A_Square_Wrap_Moves_The_First_Lines_Past_The_Shape()
    {
        LayoutResult result = Layout(With(LeftBox(ShapeWrap.Square)));
        List<LayoutLine> lines = BodyLines(result);
        double column = result.Setup.ContentLeftPoints;

        // The shape covers the left 150 points of the column for its first 60,
        // so the lines beside it start to its right and the ones below do not.
        Assert.True(lines.Count > 2, "the body should need several lines");
        Assert.True(
            Left(lines[0]) >= column + 150 - 0.01,
            $"the first line started at {Left(lines[0])}, inside the shape at {column + 150}");
        Assert.Equal(column, Left(lines[^1]), 1);
    }

    [Fact]
    public void A_Square_Wrap_Keeps_The_Text_Inside_The_Column()
    {
        LayoutResult result = Layout(With(LeftBox(ShapeWrap.Square)));
        double right = result.Setup.ContentLeftPoints + result.Setup.ContentWidthPoints;

        Assert.All(BodyLines(result), line => Assert.True(
            Right(line) <= right + 0.01,
            $"a wrapped line reached {Right(line)}, past the column edge at {right}"));
    }

    [Fact]
    public void A_Wrap_Distance_Holds_The_Text_Further_Off()
    {
        double plain = Left(BodyLines(Layout(With(LeftBox(ShapeWrap.Square))))[0]);
        double spaced = Left(BodyLines(Layout(With(new DocumentShape(
            0, 0, 0, 150, 60, ShapeFill.Solid(BColor.Black),
            wrap: ShapeWrap.Square, wrapDistance: 12))))[0]);

        Assert.Equal(plain + 12, spaced, 1);
    }

    [Fact]
    public void A_Right_Side_Wrap_Keeps_The_Text_Left_Of_The_Shape()
    {
        // The shape sits in the right half, so the text keeps to the left of it
        // and no line reaches into it.
        LayoutResult result = Layout(With(new DocumentShape(
            0, 200, 0, 150, 60, ShapeFill.Solid(BColor.Black), wrap: ShapeWrap.Square)));

        double shapeLeft = result.Setup.ContentLeftPoints + 200;
        Assert.True(Right(BodyLines(result)[0]) <= shapeLeft + 0.01);
    }

    [Fact]
    public void A_Top_And_Bottom_Wrap_Pushes_The_Text_Below_The_Shape()
    {
        LayoutResult result = Layout(With(LeftBox(ShapeWrap.TopAndBottom)));
        List<LayoutLine> lines = BodyLines(result);

        // Nothing beside it at all: the first line starts below the box rather
        // than to its right, and keeps the full column.
        Assert.Equal(result.Setup.ContentLeftPoints, Left(lines[0]), 1);
        Assert.True(
            lines[0].Top >= result.Setup.ContentTopPoints + 60 - 0.01,
            $"the first line at {lines[0].Top} did not clear the shape's 60 points");
    }

    [Fact]
    public void A_Shape_In_The_Margin_Takes_Nothing_From_The_Text()
    {
        // Entirely left of the column, which is where a letterhead stripe lives.
        LayoutResult result = Layout(With(new DocumentShape(
            0, -60, 0, 40, 60, ShapeFill.Solid(BColor.Black), wrap: ShapeWrap.Square)));

        Assert.Equal(result.Setup.ContentLeftPoints, Left(BodyLines(result)[0]), 1);
    }
}
