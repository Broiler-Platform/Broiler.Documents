using System.Text;
using Broiler.Graphics;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// Floating shapes in RTF. A drawing arrives as a <c>\shp</c> group whose
/// geometry is control words and whose paint is <c>{\sp{\sn name}{\sv value}}</c>
/// pairs; none of it was read, so a letterhead converted to RTF lost its stripe
/// and its logo box.
/// </summary>
public sealed class RtfShapeTests
{
    private static readonly ShapeFill Green =
        new(BColor.FromArgb(0xFF, 0xAE, 0xCF, 0x00), BColor.White, 60);

    private static RichTextDocument WithShapes(params DocumentShape[] shapes) =>
        RichTextDocument.FromPlainText("body").WithShapes(shapes);

    private static RichTextDocument RoundTrip(RichTextDocument document) =>
        RtfReader.Read(RtfWriter.WriteToArray(document)).Document;

    private static string Ascii(RichTextDocument document) =>
        Encoding.ASCII.GetString(RtfWriter.WriteToArray(document));

    [Fact(Timeout = 600000)]
    public void A_Shape_Round_Trips_With_Its_Box()
    {
        DocumentShape shape = Assert.Single(
            RoundTrip(WithShapes(new DocumentShape(0, -111.8, -20.05, 100.3, 779.5, Green))).Shapes);

        Assert.Equal(-111.8, shape.OffsetX, 1);
        Assert.Equal(-20.05, shape.OffsetY, 1);
        Assert.Equal(100.3, shape.Width, 1);
        Assert.Equal(779.5, shape.Height, 1);
    }

    [Fact(Timeout = 600000)]
    public void A_Colour_Survives_Its_Reversed_Packing()
    {
        // RTF packs a shape colour as blue, green and red in that order, which is
        // the reverse of how the rest of the format writes one.
        DocumentShape shape = Assert.Single(
            RoundTrip(WithShapes(new DocumentShape(
                0, -40, 0, 30, 30,
                ShapeFill.Solid(BColor.FromArgb(0xFF, 0xAE, 0xCF, 0x00))))).Shapes);

        Assert.Equal(0xAE, shape.Fill!.Start.R);
        Assert.Equal(0xCF, shape.Fill.Start.G);
        Assert.Equal(0x00, shape.Fill.Start.B);
    }

    [Fact(Timeout = 600000)]
    public void A_Gradient_Round_Trips_With_Its_Angle()
    {
        DocumentShape shape = Assert.Single(
            RoundTrip(WithShapes(new DocumentShape(0, -40, 0, 30, 200, Green))).Shapes);

        Assert.True(shape.Fill!.IsGradient);
        Assert.Equal(60, shape.Fill.AngleDegrees, 1);
    }

    [Fact(Timeout = 600000)]
    public void An_Outline_Round_Trips_And_Its_Absence_Does_Too()
    {
        DocumentShape outlined = Assert.Single(RoundTrip(WithShapes(
            new DocumentShape(0, -40, 0, 30, 30, ShapeFill.Solid(BColor.White), BColor.Black))).Shapes);
        DocumentShape bare = Assert.Single(RoundTrip(WithShapes(
            new DocumentShape(0, -40, 0, 30, 30, ShapeFill.Solid(BColor.White)))).Shapes);

        Assert.False(outlined.Outline.IsEmpty);
        Assert.True(bare.Outline.IsEmpty);
    }

    [Fact(Timeout = 600000)]
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

    [Fact(Timeout = 600000)]
    public void A_Shape_Is_Read_Once_Rather_Than_Once_Per_Group()
    {
        // \shp and \shpinst are two nested groups around one drawing. If both
        // close as the shape it is finished twice and the document gains a shape
        // that was never in it.
        Assert.Single(RoundTrip(WithShapes(
            new DocumentShape(0, -40, 0, 30, 30, ShapeFill.Solid(BColor.Black)))).Shapes);
    }

    [Fact(Timeout = 600000)]
    public void An_Understood_Ignorable_Destination_Is_Not_Skipped()
    {
        // A shape arrives as {\*\shpinst ...}. The star says to ignore what the
        // reader does not understand, and this one it does.
        string rtf = Ascii(WithShapes(new DocumentShape(0, -40, 0, 30, 30, ShapeFill.Solid(BColor.Black))));

        Assert.Contains("\\*\\shpinst", rtf, StringComparison.Ordinal);
        Assert.NotEmpty(RtfReader.Read(Encoding.ASCII.GetBytes(rtf)).Document.Shapes);
    }

    [Fact(Timeout = 600000)]
    public void A_Document_Without_Shapes_Writes_None()
    {
        Assert.DoesNotContain("shpinst", Ascii(RichTextDocument.FromPlainText("body")), StringComparison.Ordinal);
    }
}
