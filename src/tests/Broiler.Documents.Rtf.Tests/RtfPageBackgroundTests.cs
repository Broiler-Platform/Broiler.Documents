using System.Text;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// The page background RTF states as a shape in <c>{\*\background}</c>, shown
/// by <c>\viewbksp1</c>. The destination was skipped as an unknown one, so the
/// page came out white.
/// </summary>
public sealed class RtfPageBackgroundTests
{
    /// <summary>How LibreOffice writes a page colour, #804141 here.</summary>
    private const string Background =
        "{\\*\\background{\\shp{\\*\\shpinst{\\sp{\\sn shapeType}{\\sv 1}}{\\sp{\\sn fillColor}{\\sv 4276608}}}}}";

    private static RichTextDocument Read(string header) =>
        RtfReader.Read(Encoding.ASCII.GetBytes("{\\rtf1\\ansi " + header + "body\\par}")).Document;

    [Fact]
    public void A_Background_Shown_By_Viewbksp_Is_The_Page_Background()
    {
        RichTextDocument document = Read("\\viewbksp1" + Background);

        Assert.Equal(ShapeFill.Solid(BColor.FromArgb(0x80, 0x41, 0x41)), document.PageBackground);
        Assert.Empty(document.Shapes);
        Assert.Equal("body", document.PlainText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\\viewbksp0")]
    public void A_Background_Not_Shown_Is_Not_Read(string view)
    {
        // LibreOffice takes \viewbksp as Word's w:displayBackgroundShape, and
        // draws no background without it.
        RichTextDocument document = Read(view + Background);

        Assert.Null(document.PageBackground);
        Assert.Empty(document.Shapes);
    }

    [Theory]
    [MemberData(nameof(Backgrounds))]
    public void A_Background_Survives_A_Round_Trip(ShapeFill background)
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body").WithPageBackground(background);

        RichTextDocument actual = RtfReader.Read(RtfWriter.WriteToArray(source)).Document;

        Assert.Equal(background, actual.PageBackground);
        Assert.Equal("body", actual.PlainText);
    }

    public static TheoryData<ShapeFill> Backgrounds() => new()
    {
        ShapeFill.Solid(BColor.FromArgb(0x12, 0x34, 0x56)),
        new ShapeFill(BColor.FromArgb(255, 0, 0), BColor.FromArgb(0, 0, 255), 30),
        new ShapeFill(BColor.FromArgb(255, 0, 255), BColor.FromArgb(0, 255, 0), 225, Axial: true),
    };
}
