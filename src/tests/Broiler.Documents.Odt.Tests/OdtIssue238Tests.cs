using Broiler.Graphics.Color;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Office conformance issue 238: text frames sized by svg:height and
/// fo:min-height (tdf104440.odt) or by fo:min-width alone (tdf135035.odt), a
/// diamond custom shape that keeps its box (tdf137185.odt), and label frames
/// in a linked section (linked-labels.odt).
/// </summary>
public sealed class OdtIssue238Tests
{
    private static DocumentShape OnlyShape(string body, string automaticStyles = "", string? styles = null) =>
        Assert.Single((styles is null
            ? OdtTestPackage.ReadBody(body, automaticStyles)
            : OdtTestPackage.ReadStyled(body, styles, automaticStyles)).Document.Shapes);

    private static string TextFrame(string frameAttributes, string textBoxAttributes) =>
        "<text:p><draw:frame draw:name=\"Frame1\" text:anchor-type=\"paragraph\" " + frameAttributes + ">" +
        "<draw:text-box " + textBoxAttributes + "><text:p>Frame1</text:p></draw:text-box></draw:frame></text:p>";

    [Fact]
    public void A_Frame_Height_Overrides_Its_Text_Box_Minimum_Height()
    {
        // LibreOffice reads the text box's attributes, then the frame's, into one
        // height: the frame's svg:height is the least the box is, and it grows.
        DocumentShape shape = OnlyShape(TextFrame(
            "svg:width=\"17cm\" svg:height=\"9.979cm\"", "fo:min-height=\"14.896cm\""));

        Assert.Equal(9.979 / 2.54 * 72, shape.Height, 2);
        Assert.True(shape.GrowsToText);
    }

    [Fact]
    public void A_Text_Box_Minimum_Height_Alone_Still_Sizes_The_Frame()
    {
        DocumentShape shape = OnlyShape(TextFrame("svg:width=\"17cm\"", "fo:min-height=\"2cm\""));

        Assert.Equal(2 / 2.54 * 72, shape.Height, 2);
        Assert.True(shape.GrowsToText);
    }

    [Fact]
    public void A_Frame_With_Only_A_Minimum_Width_Widens_To_Its_Text()
    {
        // Writer's auto-width frame: no svg:width, fo:min-width on the text box,
        // and no fo:wrap-option. It was kept at its 0.041 cm minimum, and its
        // text was set a letter to a line.
        DocumentShape shape = OnlyShape(TextFrame("svg:x=\"1.97cm\"", "fo:min-height=\"0.041cm\" fo:min-width=\"0.041cm\""));

        Assert.True(shape.GrowsWidthToText);
        Assert.Equal(0.041 / 2.54 * 72, shape.Width, 2);
    }

    [Fact]
    public void A_Frame_With_A_Stated_Width_Does_Not_Widen()
    {
        DocumentShape shape = OnlyShape(TextFrame("svg:width=\"5cm\"", "fo:min-height=\"1cm\" fo:min-width=\"1cm\""));

        Assert.False(shape.GrowsWidthToText);
    }

    private const string Diamond =
        "<text:p><draw:custom-shape text:anchor-type=\"page\" text:anchor-page-number=\"1\" draw:style-name=\"gr1\" " +
        "svg:width=\"10cm\" svg:height=\"8cm\" svg:x=\"3cm\" svg:y=\"6cm\"><text:p>Align me!</text:p>" +
        "<draw:enhanced-geometry svg:viewBox=\"0 0 21600 21600\" draw:type=\"diamond\"/></draw:custom-shape></text:p>";

    private static string ShapeStyle(string extra) =>
        "<style:style style:name=\"gr1\" style:family=\"graphic\"><style:graphic-properties draw:fill=\"solid\" " +
        "draw:fill-color=\"#729fcf\" draw:textarea-vertical-align=\"middle\" " + extra + "/></style:style>";

    [Fact]
    public void A_Diamond_Shape_That_Does_Not_Grow_Keeps_Its_Box()
    {
        // draw:auto-grow-height="false": LibreOffice draws the 10 x 8 cm diamond
        // with its text in the middle. It was shrunk around "Align me!" as a
        // rectangle.
        DocumentShape shape = OnlyShape(Diamond, ShapeStyle("draw:auto-grow-height=\"false\""));

        Assert.Equal(ShapeGeometry.Diamond, shape.Geometry);
        Assert.False(shape.FitsText);
        Assert.Equal(10 / 2.54 * 72, shape.Width, 2);
        Assert.Equal(CellVerticalAlignment.Center, shape.TextVerticalAlignment);
    }

    [Fact]
    public void A_Custom_Shape_That_States_No_Growth_Still_Fits_Its_Text()
    {
        // ODF takes draw:auto-grow-height as true when unstated
        // (tiledrendering/data/shape-fill-link.fodt).
        DocumentShape shape = OnlyShape(Diamond, ShapeStyle(string.Empty));

        Assert.True(shape.FitsText);
    }

    [Fact]
    public void A_Text_Frame_Takes_No_Outline_From_The_Default_Graphic_Stroke()
    {
        // mailmerge/data/linked-labels.odt: the default graphic style's
        // svg:stroke-color="#3465a4" drew a blue edge round both labels.
        string styles = "<office:styles><style:default-style style:family=\"graphic\">" +
            "<style:graphic-properties svg:stroke-color=\"#3465a4\" draw:fill-color=\"#729fcf\"/>" +
            "</style:default-style></office:styles>";
        DocumentShape shape = OnlyShape(TextFrame("svg:width=\"5cm\" svg:height=\"3cm\"", string.Empty), styles: styles);

        Assert.True(shape.Outline.IsEmpty);
        Assert.Null(shape.Fill);
    }

    [Fact]
    public void A_Text_Frame_Keeps_Its_Border()
    {
        string styles = "<office:styles><style:default-style style:family=\"graphic\">" +
            "<style:graphic-properties svg:stroke-color=\"#3465a4\"/></style:default-style></office:styles>";
        string automatic = "<style:style style:name=\"fr1\" style:family=\"graphic\">" +
            "<style:graphic-properties fo:border=\"0.5pt solid #ff0000\"/></style:style>";
        DocumentShape shape = OnlyShape(
            TextFrame("draw:style-name=\"fr1\" svg:width=\"5cm\" svg:height=\"3cm\"", string.Empty), automatic, styles);

        Assert.Equal(BColor.FromRgba(0xff, 0, 0, 0xff), shape.Outline);
    }

    private const string DefaultStroke = "<style:default-style style:family=\"graphic\">" +
        "<style:graphic-properties svg:stroke-color=\"#3465a4\"/></style:default-style>";

    [Fact]
    public void A_Frame_Under_An_Undefined_Frame_Style_Takes_Writers_Hairline_Edge()
    {
        // uiwriter/data/redlineFrame_at_char_start_inside.fodt names Writer's
        // built-in "Frame" style as the parent without defining it; LibreOffice
        // draws that style's black hairline round the frame.
        string automatic = "<style:style style:name=\"fr1\" style:family=\"graphic\" style:parent-style-name=\"Frame\">" +
            "<style:graphic-properties style:vertical-pos=\"top\"/></style:style>";
        DocumentShape shape = OnlyShape(TextFrame("draw:style-name=\"fr1\" svg:width=\"2cm\" svg:height=\"1cm\"", string.Empty),
            automatic, "<office:styles>" + DefaultStroke + "</office:styles>");

        Assert.Equal(BColor.Black, shape.Outline);
    }

    [Fact]
    public void A_Frame_Whose_Own_Style_Turns_A_Stroke_On_Keeps_It()
    {
        // ww8export/data/tdf136620.odt: a shape's text box saved as a frame.
        string automatic = "<style:style style:name=\"gr1\" style:family=\"graphic\">" +
            "<style:graphic-properties draw:stroke=\"solid\" svg:stroke-color=\"#000000\" draw:fill=\"none\"/></style:style>";
        DocumentShape shape = OnlyShape(TextFrame("draw:style-name=\"gr1\" svg:width=\"2cm\" svg:height=\"1cm\"", string.Empty),
            automatic, "<office:styles>" + DefaultStroke + "</office:styles>");

        Assert.Equal(BColor.Black, shape.Outline);
    }

    [Fact]
    public void A_Frame_With_One_Bordered_Edge_Is_Outlined_With_It()
    {
        // layout/data/stable-at-page-anchored-fly-position.odt: a 0.041 cm frame
        // drawn as a rule by its fo:border-top alone.
        string automatic = "<style:style style:name=\"fr2\" style:family=\"graphic\"><style:graphic-properties " +
            "fo:border-left=\"none\" fo:border-right=\"none\" fo:border-top=\"0.06pt solid #000000\" fo:border-bottom=\"none\"/></style:style>";
        DocumentShape shape = OnlyShape(TextFrame("draw:style-name=\"fr2\" svg:width=\"0.3cm\" svg:height=\"0.041cm\"", string.Empty),
            automatic, "<office:styles>" + DefaultStroke + "</office:styles>");

        Assert.Equal(BColor.Black, shape.Outline);
    }

    [Fact]
    public void A_Linked_Section_Reads_Its_Saved_Copy_Without_A_Warning()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:section text:name=\"Section1\"><text:section-source text:section-name=\"MasterLabel\"/>" +
            "<text:p>copy</text:p></text:section>");

        Assert.Equal("copy", Assert.Single(result.Document.Paragraphs).Text);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "odt.block.unsupported");
    }
}
