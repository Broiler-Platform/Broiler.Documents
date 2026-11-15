namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Office conformance issue 249, ODT pixel similarity: page line spacing
/// (<c>style:register-true</c> and <c>style:register-truth-ref-style-name</c>,
/// the pagelinespacing samples), the space above a paragraph that opens a page
/// with its own break, an empty paragraph as tall as the empty span in it
/// (tdf139638.odt), the margins of a page layout that states none
/// (tdf134649_pageBreak.fodt) and <c>draw:polygon</c> (tdf138374.odt).
/// </summary>
public sealed class OdtIssue249OdtPixelTests
{
    private const string TextBody =
        "<style:style style:name=\"Text_20_body\" style:display-name=\"Text body\" style:family=\"paragraph\">" +
        "<style:paragraph-properties fo:line-height=\"115%\" style:register-true=\"true\"/>" +
        "<style:text-properties style:font-name=\"Liberation Serif\" fo:font-size=\"12pt\" fo:font-weight=\"bold\"/>" +
        "</style:style>";

    private static string Styles(string pageLayoutProperties, string styles = TextBody) =>
        "<office:styles>" + styles + "</office:styles>" +
        "<office:automatic-styles><style:page-layout style:name=\"pm1\">" +
        "<style:page-layout-properties fo:page-width=\"8.5in\" fo:page-height=\"11in\" " + pageLayoutProperties + "/>" +
        "</style:page-layout></office:automatic-styles>" +
        "<office:master-styles><style:master-page style:name=\"Standard\" style:page-layout-name=\"pm1\"/></office:master-styles>";

    private const string Margins = "fo:margin-top=\"1in\" fo:margin-bottom=\"1in\" fo:margin-left=\"1in\" fo:margin-right=\"1in\"";

    [Fact]
    public void A_Page_Layout_Naming_A_Register_Reference_Style_Carries_It_Resolved()
    {
        RichTextDocument document = OdtTestPackage.ReadStyled(OdtTestPackage.StyledParagraph("Text_20_body", "text"),
            Styles(Margins + " style:register-truth-ref-style-name=\"Text_20_body\"")).Document;

        LineRegister register = document.PageGeometry!.LineRegister!;
        Assert.Equal("Liberation Serif", register.Text.FontFamily);
        Assert.Equal(12, register.Text.FontSize!.Value, 3);
        Assert.True(register.Text.Bold);
        Assert.Equal(LineHeightRule.Proportional, register.Rule);
        Assert.Equal(1.15f, register.LineSpacing, 3);
        Assert.True(document.Paragraphs[0].Style.RegisterTrue);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" style:register-truth-ref-style-name=\"Undefined\"")]
    public void A_Page_Layout_With_No_Defined_Reference_Style_Has_No_Register(string attribute)
    {
        // pageLineSpacingDisabled.fodt names none: Writer sets no register.
        RichTextDocument document = OdtTestPackage.ReadStyled(OdtTestPackage.StyledParagraph("Text_20_body", "text"),
            Styles(Margins + attribute)).Document;

        Assert.Null(document.PageGeometry!.LineRegister);
    }

    [Fact]
    public void Register_True_False_Turns_A_Paragraph_Off_The_Register()
    {
        RichTextDocument document = OdtTestPackage.ReadStyled(OdtTestPackage.StyledParagraph("P1", "text"),
            Styles(Margins + " style:register-truth-ref-style-name=\"Text_20_body\""),
            OdtTestPackage.Style("P1", "<style:paragraph-properties style:register-true=\"false\"/>", parent: "Text_20_body")).Document;

        Assert.False(document.Paragraphs[0].Style.RegisterTrue);
    }

    [Fact]
    public void A_Page_Layout_That_States_No_Margins_Has_Writers_Two_Centimetres()
    {
        // tdf134649_pageBreak.fodt: LibreOffice draws its text 2 cm in from
        // every edge; broilerdoc drew it at the paper's edge.
        PageGeometry page = OdtTestPackage.ReadStyled(OdtTestPackage.Paragraph("text"), Styles(string.Empty)).Document.PageGeometry!;

        double twoCentimetres = 2 * 72 / 2.54;
        Assert.Equal(twoCentimetres, page.MarginLeft, 1);
        Assert.Equal(twoCentimetres, page.MarginRight, 1);
        Assert.Equal(twoCentimetres, page.MarginTop, 1);
        Assert.Equal(twoCentimetres, page.MarginBottom, 1);
    }

    [Fact]
    public void A_Stated_Margin_Wins_Over_The_Default_And_A_Uniform_Margin_Fills_The_Rest()
    {
        PageGeometry page = OdtTestPackage.ReadStyled(OdtTestPackage.Paragraph("text"),
            Styles("fo:margin=\"0in\" fo:margin-top=\"1in\"")).Document.PageGeometry!;

        Assert.Equal(72, page.MarginTop, 1);
        Assert.Equal(0, page.MarginLeft, 1);
        Assert.Equal(0, page.MarginBottom, 1);
    }

    [Theory]
    [InlineData("<style:paragraph-properties fo:break-before=\"page\"/>", "")]
    [InlineData("", " style:master-page-name=\"Standard\"")]
    public void A_Paragraph_That_Opens_A_Page_With_Its_Own_Break_Keeps_Its_Space_Above(string properties, string masterPage)
    {
        // Writer counts a break before, a break after the paragraph above and a
        // page style as the page's own break (SwFlowFrame::IsPageBreak).
        RichTextDocument document = OdtTestPackage.ReadBody(
            OdtTestPackage.Paragraph("first") + OdtTestPackage.StyledParagraph("P1", "second"),
            "<style:style style:name=\"P1\" style:family=\"paragraph\"" + masterPage + ">" + properties + "</style:style>").Document;

        Assert.True(document.Paragraphs[1].Style.PageBreakBefore);
        Assert.True(document.StyleDefaults.PageBreaksKeepSpaceAbove);
    }

    [Fact]
    public void A_Break_After_The_Paragraph_Above_Keeps_The_Space_Above_Too()
    {
        RichTextDocument document = OdtTestPackage.ReadBody(
            OdtTestPackage.StyledParagraph("P1", "first") + OdtTestPackage.Paragraph("second"),
            OdtTestPackage.Style("P1", "<style:paragraph-properties fo:break-after=\"page\"/>")).Document;

        Assert.True(document.Paragraphs[1].Style.PageBreakBefore);
        Assert.True(document.StyleDefaults.PageBreaksKeepSpaceAbove);
    }

    [Fact]
    public void An_Empty_Paragraph_Holding_An_Empty_Span_Is_As_Tall_As_The_Span()
    {
        // tdf139638.odt: a 12 pt paragraph holding an empty 10.5 pt span is a
        // 10.5 pt line in LibreOffice; without the span it is a 12 pt one.
        RichTextDocument document = OdtTestPackage.ReadBody(
            "<text:p text:style-name=\"P1\"><text:span text:style-name=\"T1\"/></text:p>" +
            "<text:p text:style-name=\"P1\"><text:span text:style-name=\"T1\">text</text:span></text:p>" +
            "<text:p text:style-name=\"P1\"/>",
            OdtTestPackage.Style("P1", "<style:text-properties fo:font-size=\"12pt\"/>") +
            OdtTestPackage.Style("T1", "<style:text-properties fo:font-size=\"10.5pt\" fo:font-name=\"Times New Roman\"/>", "text")).Document;

        Assert.Equal(10.5, document.Paragraphs[0].MarkStyle.FontSize!.Value, 3);
        Assert.Equal(12, document.Paragraphs[1].MarkStyle.FontSize!.Value, 3);
        Assert.Equal(12, document.Paragraphs[2].MarkStyle.FontSize!.Value, 3);
    }

    [Fact]
    public void A_Polygon_Is_Drawn_As_Its_Points_In_Its_View_Box()
    {
        // tdf138374.odt's bent arrow was dropped: draw:polygon was not read.
        RichTextDocument document = OdtTestPackage.ReadBody(
            "<text:p><draw:polygon text:anchor-type=\"paragraph\" draw:style-name=\"gr1\" svg:width=\"1in\" svg:height=\"2in\" " +
            "svg:x=\"0in\" svg:y=\"0in\" svg:viewBox=\"100 0 1000 2000\" draw:points=\"100,0 1100,0 600,2000\"/></text:p>",
            "<style:style style:name=\"gr1\" style:family=\"graphic\"><style:graphic-properties draw:fill=\"solid\" " +
            "draw:fill-color=\"#ffd320\"/></style:style>").Document;

        DocumentShape shape = Assert.Single(document.Shapes);
        Assert.Equal(72, shape.Width, 1);
        Assert.Equal(144, shape.Height, 1);
        IReadOnlyList<(double X, double Y)> corners = Assert.Single(shape.OutlinePaths!);
        Assert.Equal([(0, 0), (1, 0), (0.5, 1)], corners);
    }

    [Theory]
    [InlineData("skewX (3.72304590677509E-017) rotate (1.30899693899575) translate (0in 1in)", true)]
    [InlineData("rotate (1.30899693899575) translate (0in 1in)", true)]
    [InlineData("skewX (0.5) rotate (1.30899693899575) translate (0in 1in)", false)]
    public void A_Rotation_After_A_Shear_Of_Nothing_Is_Read(string transform, bool turned)
    {
        // tdf70838.odt: Writer wrote a shear of 3.7E-17 before the rotation,
        // and the rectangle was drawn upright.
        RichTextDocument document = OdtTestPackage.ReadBody(
            "<text:p><draw:rect text:anchor-type=\"paragraph\" draw:style-name=\"gr1\" svg:width=\"2in\" svg:height=\"1in\" " +
            "draw:transform=\"" + transform + "\"><text:p/></draw:rect></text:p>",
            "<style:style style:name=\"gr1\" style:family=\"graphic\"><style:graphic-properties draw:fill=\"solid\" " +
            "draw:fill-color=\"#729fcf\"/></style:style>").Document;

        DocumentShape shape = Assert.Single(document.Shapes);
        Assert.Equal(turned ? -1.30899693899575 : 0, shape.RotationRadians, 6);
    }

    [Fact]
    public void A_Turned_Shape_Set_As_A_Character_Keeps_No_Saved_Position()
    {
        RichTextDocument document = OdtTestPackage.ReadBody(
            "<text:p><draw:rect text:anchor-type=\"as-char\" draw:style-name=\"gr1\" svg:width=\"2in\" svg:height=\"1in\" " +
            "draw:transform=\"rotate (1.30899693899575) translate (0in 1.6465in)\"><text:p/></draw:rect></text:p>",
            "<style:style style:name=\"gr1\" style:family=\"graphic\"><style:graphic-properties draw:fill=\"solid\" " +
            "draw:fill-color=\"#729fcf\"/></style:style>").Document;

        DocumentShape shape = Assert.Single(document.Shapes);
        Assert.True(shape.IsAsCharacter);
        Assert.Equal(0, shape.OffsetY, 3);
    }
}
