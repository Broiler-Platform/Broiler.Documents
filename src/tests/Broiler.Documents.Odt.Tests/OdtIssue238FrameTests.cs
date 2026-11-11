using Broiler.Graphics.Color;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Office conformance issue 238, ODT pictures and frames: hidden paragraphs
/// (hidden-para-as-char-fly.fodt), a text frame nested in a text box
/// (tdf46441-2.odt, nested-text-frames.odt), a Writer text frame's edge and a
/// drawn text box that keeps its size (tdf129655-vtextbox.odt).
/// </summary>
public sealed class OdtIssue238FrameTests
{
    private const string FrameStyle =
        "<office:styles><style:style style:name='Frame' style:family='graphic'>" +
        "<style:graphic-properties fo:padding='0cm'/></style:style></office:styles>";

    private static string Graphic(string name, string properties, string? parent = "Frame") =>
        OdtTestPackage.Style(name, "<style:graphic-properties " + properties + "/>", "graphic", parent);

    [Fact]
    public void A_Paragraph_Whose_Hidden_Paragraph_Field_Is_True_Is_Left_Out()
    {
        RichTextDocument document = OdtTestPackage.ReadBody(
            "<text:p>Foo</text:p>" +
            "<text:p>gone<text:hidden-paragraph text:condition='ooow:TRUE' text:is-hidden='true'/></text:p>" +
            "<text:p>kept<text:hidden-paragraph text:condition='ooow:FALSE' text:is-hidden='false'/></text:p>" +
            "<text:p>stored<text:hidden-paragraph text:condition='ooow:Unknown==1' text:is-hidden='true'/></text:p>" +
            "<text:p>Bar</text:p>").Document;

        Assert.Equal("Foo\nkept\nBar", document.PlainText);
    }

    [Fact]
    public void A_Paragraph_Is_Hidden_Only_When_Every_Field_In_It_Hides_It()
    {
        RichTextDocument document = OdtTestPackage.ReadBody(
            "<text:p>both<text:hidden-paragraph text:is-hidden='true'/>" +
            "<text:hidden-paragraph text:condition='ooow:FALSE' text:is-hidden='false'/></text:p>").Document;

        Assert.Equal("both", document.PlainText);
    }

    [Fact]
    public void A_Hidden_Paragraph_Field_In_A_Frame_Hides_The_Frame_Paragraph_Not_Its_Anchor()
    {
        RichTextDocument document = OdtTestPackage.ReadStyled(
            "<text:p>anchor<draw:frame draw:style-name='fr1' text:anchor-type='paragraph' svg:width='100pt' svg:height='50pt'>" +
            "<draw:text-box><text:p>shown</text:p>" +
            "<text:p>hidden<text:hidden-paragraph text:is-hidden='true'/></text:p></draw:text-box></draw:frame></text:p>",
            FrameStyle,
            Graphic("fr1", "style:wrap='run-through'")).Document;

        Assert.Equal("anchor", document.Paragraphs[0].Text);
        DocumentShape shape = Assert.Single(document.Shapes);
        Assert.Equal("shown", Assert.Single(shape.Paragraphs).Text);
    }

    [Fact]
    public void A_Text_Frame_In_A_Text_Box_Is_Lifted_Out_Inside_Its_Frame()
    {
        // The outer frame is 20 pt from its paragraph with 5 pt of padding; the
        // inner one is anchored to the box's first paragraph, 30 pt and 10 pt in.
        RichTextDocument document = OdtTestPackage.ReadStyled(
            "<text:p><draw:frame draw:style-name='outer' text:anchor-type='paragraph' svg:x='20pt' svg:y='20pt' " +
            "svg:width='300pt' svg:height='200pt' draw:z-index='3'><draw:text-box>" +
            "<text:p><draw:frame draw:style-name='inner' text:anchor-type='paragraph' svg:x='30pt' svg:y='10pt' " +
            "svg:width='100pt' draw:z-index='1'><draw:text-box fo:min-height='40pt'><text:p>inner text</text:p>" +
            "</draw:text-box></draw:frame>outer text</text:p></draw:text-box></draw:frame>P</text:p>",
            FrameStyle,
            Graphic("outer", "fo:padding='5pt' fo:border='none' style:wrap='run-through' " +
                "style:vertical-pos='from-top' style:vertical-rel='paragraph' style:horizontal-pos='from-left' style:horizontal-rel='paragraph'") +
            Graphic("inner", "style:wrap='parallel' style:vertical-pos='from-top' style:vertical-rel='paragraph' " +
                "style:horizontal-pos='from-left' style:horizontal-rel='paragraph'")).Document;

        Assert.Equal(2, document.Shapes.Count);
        DocumentShape outer = document.Shapes[0];
        DocumentShape inner = document.Shapes[1];
        Assert.Equal("outer text", Assert.Single(outer.Paragraphs).Text);
        Assert.Equal("inner text", Assert.Single(inner.Paragraphs).Text);
        Assert.Equal(outer.ParagraphIndex, inner.ParagraphIndex);
        Assert.Equal(20 + 5 + 30, inner.OffsetX, 2);
        Assert.Equal(20 + 5 + 10, inner.OffsetY, 2);
        // It is drawn above the frame it is in, and the body text runs past it.
        Assert.True(inner.ZOrder > outer.ZOrder);
        Assert.Equal(ShapeWrap.None, inner.Wrap);
    }

    [Fact]
    public void A_Lifted_Frame_Below_Other_Paragraphs_Starts_Below_Them()
    {
        RichTextDocument document = OdtTestPackage.ReadStyled(
            "<text:p><draw:frame draw:style-name='outer' text:anchor-type='paragraph' svg:width='300pt' svg:height='200pt'>" +
            "<draw:text-box><text:p text:style-name='big'/>" +
            "<text:p><draw:frame draw:style-name='inner' text:anchor-type='paragraph' svg:width='100pt' svg:height='40pt'>" +
            "<draw:text-box><text:p>inner</text:p></draw:text-box></draw:frame></text:p></draw:text-box></draw:frame></text:p>",
            FrameStyle,
            Graphic("outer", "fo:padding='0pt' style:wrap='run-through' style:vertical-pos='from-top' style:vertical-rel='paragraph' " +
                "style:horizontal-pos='from-left' style:horizontal-rel='paragraph'") +
            Graphic("inner", "style:vertical-pos='from-top' style:vertical-rel='paragraph' " +
                "style:horizontal-pos='from-left' style:horizontal-rel='paragraph'") +
            OdtTestPackage.Style("big", "<style:paragraph-properties fo:margin-bottom='6pt'/><style:text-properties fo:font-size='20pt'/>")).Document;

        DocumentShape inner = document.Shapes[1];
        // One empty 20 pt line (1.15 em) and its 6 pt below.
        Assert.Equal(20 * 1.15 + 6, inner.OffsetY, 1);
    }

    [Fact]
    public void A_Lifted_Frame_Placed_By_The_Page_Keeps_Its_Page_Position()
    {
        string pageStyles = FrameStyle +
            "<office:automatic-styles><style:page-layout style:name='pm1'><style:page-layout-properties fo:page-width='600pt' " +
            "fo:page-height='800pt' fo:margin-left='50pt' fo:margin-right='50pt' fo:margin-top='50pt' fo:margin-bottom='50pt'/>" +
            "</style:page-layout></office:automatic-styles><office:master-styles>" +
            "<style:master-page style:name='Standard' style:page-layout-name='pm1'/></office:master-styles>";
        RichTextDocument document = OdtTestPackage.ReadStyled(
            "<text:p><draw:frame draw:style-name='outer' text:anchor-type='paragraph' svg:x='10pt' svg:y='30pt' " +
            "svg:width='400pt' svg:height='300pt'><draw:text-box><text:p>" +
            "<draw:frame draw:style-name='inner' text:anchor-type='paragraph' svg:x='72pt' svg:y='120pt' svg:width='200pt' svg:height='100pt'>" +
            "<draw:text-box><text:p>inner</text:p></draw:text-box></draw:frame>outer</text:p></draw:text-box></draw:frame>Anchor</text:p>",
            pageStyles,
            Graphic("outer", "style:vertical-pos='from-top' style:vertical-rel='paragraph' style:horizontal-pos='from-left' style:horizontal-rel='paragraph'") +
            Graphic("inner", "style:vertical-pos='from-top' style:vertical-rel='page' style:horizontal-pos='from-left' style:horizontal-rel='page'")).Document;

        DocumentShape inner = document.Shapes[1];
        Assert.True(inner.OffsetFromPage);
        Assert.Equal(120, inner.OffsetY, 2);
        // x is from the text column: 72 pt from the page edge is 22 pt into it.
        Assert.Equal(72 - 50, inner.OffsetX, 2);
    }

    [Fact]
    public void A_Writer_Text_Frame_Draws_No_Line_From_The_Default_Shape_Stroke()
    {
        string styles = "<office:styles><style:default-style style:family='graphic'>" +
            "<style:graphic-properties svg:stroke-color='#000000'/></style:default-style>" +
            "<style:style style:name='Frame' style:family='graphic'><style:graphic-properties fo:padding='0cm'/></style:style>" +
            "</office:styles>";
        string body =
            "<text:p><draw:frame draw:style-name='{0}' text:anchor-type='paragraph' svg:width='100pt' svg:height='50pt'>" +
            "<draw:text-box><text:p>text</text:p></draw:text-box></draw:frame></text:p>";

        DocumentShape frame = Assert.Single(OdtTestPackage.ReadStyled(string.Format(body, "fr1"), styles,
            Graphic("fr1", "style:wrap='run-through'")).Document.Shapes);
        Assert.True(frame.Outline.IsEmpty);

        // A text box drawn as a shape has a style of its own and keeps its stroke.
        DocumentShape drawn = Assert.Single(OdtTestPackage.ReadStyled(string.Format(body, "gr1"), styles,
            Graphic("gr1", "draw:stroke='solid' svg:stroke-color='#000000' style:wrap='run-through'", parent: null)).Document.Shapes);
        Assert.False(drawn.Outline.IsEmpty);
    }

    [Fact]
    public void A_Text_Frame_Whose_Frame_Style_Is_Not_Defined_Gets_Writers_Hairline_Border()
    {
        DocumentShape frame = Assert.Single(OdtTestPackage.ReadBody(
            "<text:p><draw:frame draw:style-name='fr1' text:anchor-type='paragraph' svg:width='100pt' svg:height='50pt'>" +
            "<draw:text-box><text:p>text</text:p></draw:text-box></draw:frame></text:p>",
            Graphic("fr1", "style:wrap='run-through'")).Document.Shapes);

        Assert.Equal(BColor.Black, frame.Outline);
        Assert.True(frame.OutlineWidth < 0.1);
    }

    [Theory]
    [InlineData("draw:auto-grow-height='false'", false)]
    [InlineData("", true)]
    public void A_Drawn_Shape_That_Does_Not_Grow_To_Its_Text_Keeps_Its_Size(string grow, bool fits)
    {
        DocumentShape shape = Assert.Single(OdtTestPackage.ReadBody(
            "<text:p><draw:custom-shape draw:style-name='gr1' text:anchor-type='paragraph' svg:width='272pt' svg:height='256pt'>" +
            "<text:p>text</text:p><draw:enhanced-geometry svg:viewBox='0 0 21600 21600' draw:type='rectangle'/>" +
            "</draw:custom-shape></text:p>",
            Graphic("gr1", grow + " style:wrap='run-through'", parent: null)).Document.Shapes);

        Assert.Equal(fits, shape.FitsText);
        Assert.Equal(272, shape.Width, 2);
        Assert.Equal(256, shape.Height, 2);
    }
}
