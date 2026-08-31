using Broiler.Graphics;

namespace Broiler.Documents.Odt.Tests;

public sealed class OdtReaderStyleTests
{
    [Fact]
    public void Applies_An_Automatic_Text_Style_To_The_Span_That_Names_It()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p>plain <text:span text:style-name=\"T1\">bold</text:span></text:p>",
            OdtTestPackage.Style(
                "T1",
                "<style:text-properties fo:font-weight=\"bold\"/>",
                family: "text"));

        RichTextParagraph paragraph = result.Document.Paragraphs[0];
        Assert.False(paragraph.StyleAt(0).Bold);
        Assert.True(paragraph.StyleAt(6).Bold);
    }

    [Fact]
    public void Reads_The_Character_Attributes_The_Model_Carries()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p><text:span text:style-name=\"T1\">x</text:span></text:p>",
            OdtTestPackage.Style(
                "T1",
                "<style:text-properties fo:font-weight=\"bold\" fo:font-style=\"italic\" " +
                "style:text-underline-style=\"solid\" style:text-line-through-style=\"solid\" " +
                "fo:font-family=\"Arial\" fo:font-size=\"18pt\" " +
                "fo:color=\"#112233\" fo:background-color=\"#ffff00\"/>",
                family: "text"));

        InlineStyle style = result.Document.Paragraphs[0].StyleAt(0);
        Assert.True(style.Bold);
        Assert.True(style.Italic);
        Assert.True(style.Underline);
        Assert.True(style.Strikethrough);
        Assert.Equal("Arial", style.FontFamily);
        Assert.Equal(18f, style.FontSize);
        Assert.Equal(BColor.FromArgb(0x11, 0x22, 0x33), style.Foreground);
        Assert.Equal(BColor.FromArgb(0xFF, 0xFF, 0x00), style.Background);
    }

    [Theory]
    [InlineData("fo:font-weight=\"700\"", true)]
    [InlineData("fo:font-weight=\"400\"", false)]
    [InlineData("fo:font-weight=\"normal\"", false)]
    public void Reads_The_Numeric_Font_Weight_The_Way_Css_Defines_It(string attribute, bool expected)
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p><text:span text:style-name=\"T1\">x</text:span></text:p>",
            OdtTestPackage.Style("T1", "<style:text-properties " + attribute + "/>", family: "text"));

        Assert.Equal(expected, result.Document.Paragraphs[0].StyleAt(0).Bold);
    }

    [Fact]
    public void An_Underline_Style_Of_None_Turns_The_Underline_Off_Again()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p text:style-name=\"P1\">" +
            "<text:span text:style-name=\"T1\">x</text:span></text:p>",
            OdtTestPackage.Style(
                "P1",
                "<style:text-properties style:text-underline-style=\"solid\"/>") +
            OdtTestPackage.Style(
                "T1",
                "<style:text-properties style:text-underline-style=\"none\"/>",
                family: "text"));

        Assert.False(result.Document.Paragraphs[0].StyleAt(0).Underline);
    }

    [Fact]
    public void Reads_A_Quoted_Font_Family_List_As_Its_First_Family()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p><text:span text:style-name=\"T1\">x</text:span></text:p>",
            OdtTestPackage.Style(
                "T1",
                "<style:text-properties fo:font-family=\"&apos;Times New Roman&apos;, serif\"/>",
                family: "text"));

        Assert.Equal("Times New Roman", result.Document.Paragraphs[0].StyleAt(0).FontFamily);
    }

    [Fact]
    public void Resolves_A_Font_Name_Through_The_Font_Face_Declarations()
    {
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            "<text:p><text:span text:style-name=\"T1\">x</text:span></text:p>",
            "<office:font-face-decls>" +
            "<style:font-face style:name=\"F1\" svg:font-family=\"Liberation Serif\"/>" +
            "</office:font-face-decls>",
            OdtTestPackage.Style(
                "T1",
                "<style:text-properties style:font-name=\"F1\"/>",
                family: "text"));

        Assert.Equal("Liberation Serif", result.Document.Paragraphs[0].StyleAt(0).FontFamily);
    }

    [Fact]
    public void A_Percentage_Font_Size_Scales_The_Size_It_Inherited()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p text:style-name=\"P1\">" +
            "<text:span text:style-name=\"T1\">x</text:span></text:p>",
            OdtTestPackage.Style("P1", "<style:text-properties fo:font-size=\"10pt\"/>") +
            OdtTestPackage.Style("T1", "<style:text-properties fo:font-size=\"150%\"/>", family: "text"));

        Assert.Equal(15f, result.Document.Paragraphs[0].StyleAt(0).FontSize);
    }

    [Fact]
    public void Reads_Capitalization_From_The_Transform_And_The_Variant()
    {
        DocumentReadResult upper = OdtTestPackage.ReadBody(
            "<text:p><text:span text:style-name=\"T1\">x</text:span></text:p>",
            OdtTestPackage.Style(
                "T1",
                "<style:text-properties fo:text-transform=\"uppercase\"/>",
                family: "text"));
        DocumentReadResult small = OdtTestPackage.ReadBody(
            "<text:p><text:span text:style-name=\"T1\">x</text:span></text:p>",
            OdtTestPackage.Style(
                "T1",
                "<style:text-properties fo:font-variant=\"small-caps\"/>",
                family: "text"));

        Assert.Equal(TextCapitalization.AllCaps, upper.Document.Paragraphs[0].StyleAt(0).Capitalization);
        Assert.Equal(TextCapitalization.SmallCaps, small.Document.Paragraphs[0].StyleAt(0).Capitalization);
    }

    [Fact]
    public void A_Lowercase_Transform_Is_Reported_Rather_Than_Applied()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p><text:span text:style-name=\"T1\">Mixed Case</text:span></text:p>",
            OdtTestPackage.Style(
                "T1",
                "<style:text-properties fo:text-transform=\"lowercase\"/>",
                family: "text"));

        Assert.Equal("Mixed Case", result.Document.Paragraphs[0].Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.text.transform");
    }

    [Fact]
    public void Reads_The_Paragraph_Attributes_The_Model_Carries()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            OdtTestPackage.StyledParagraph("P1", "x"),
            OdtTestPackage.Style(
                "P1",
                "<style:paragraph-properties fo:text-align=\"center\" fo:line-height=\"150%\" " +
                "fo:margin-top=\"6pt\" fo:margin-bottom=\"12pt\" fo:margin-left=\"0.5in\"/>"));

        ParagraphStyle style = result.Document.Paragraphs[0].Style;
        Assert.Equal(TextAlignment.Center, style.Alignment);
        Assert.Equal(1.5f, style.LineSpacing);
        Assert.Equal(6f, style.SpacingBefore);
        Assert.Equal(12f, style.SpacingAfter);
        Assert.Equal(2, style.IndentLevel);
    }

    [Theory]
    [InlineData("start", TextAlignment.Left)]
    [InlineData("left", TextAlignment.Left)]
    [InlineData("center", TextAlignment.Center)]
    [InlineData("end", TextAlignment.Right)]
    [InlineData("right", TextAlignment.Right)]
    public void Maps_Every_Alignment_The_Model_Has(string value, TextAlignment expected)
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            OdtTestPackage.StyledParagraph("P1", "x"),
            OdtTestPackage.Style("P1", "<style:paragraph-properties fo:text-align=\"" + value + "\"/>"));

        Assert.Equal(expected, result.Document.Paragraphs[0].Style.Alignment);
    }

    [Fact]
    public void Reads_A_Justified_Paragraph_As_Justified()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            OdtTestPackage.StyledParagraph("P1", "x"),
            OdtTestPackage.Style("P1", "<style:paragraph-properties fo:text-align=\"justify\"/>"));

        Assert.Equal(TextAlignment.Justify, result.Document.Paragraphs[0].Style.Alignment);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "odt.align.justify");
    }

    [Fact]
    public void Reports_A_Fixed_Line_Height_Rather_Than_Guessing_A_Multiplier()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            OdtTestPackage.StyledParagraph("P1", "x"),
            OdtTestPackage.Style("P1", "<style:paragraph-properties fo:line-height=\"18pt\"/>"));

        Assert.Equal(1f, result.Document.Paragraphs[0].Style.LineSpacing);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.linespacing.fixed");
    }

    [Fact]
    public void Resolves_A_Parent_Style_Chain_Root_First()
    {
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            OdtTestPackage.StyledParagraph("P1", "x"),
            "<office:styles>" +
            OdtTestPackage.Style(
                "Base",
                "<style:text-properties fo:font-size=\"20pt\" fo:font-weight=\"bold\"/>") +
            "</office:styles>",
            OdtTestPackage.Style(
                "P1",
                "<style:text-properties fo:font-size=\"9pt\"/>",
                parent: "Base"));

        InlineStyle style = result.Document.Paragraphs[0].StyleAt(0);
        Assert.Equal(9f, style.FontSize);
        Assert.True(style.Bold);
    }

    [Fact]
    public void The_Family_Default_Style_Applies_To_Content_That_Names_No_Style()
    {
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            OdtTestPackage.Paragraph("x"),
            "<office:styles>" +
            "<style:default-style style:family=\"paragraph\">" +
            "<style:text-properties fo:font-size=\"14pt\"/>" +
            "</style:default-style>" +
            "</office:styles>");

        Assert.Equal(14f, result.Document.Paragraphs[0].StyleAt(0).FontSize);
    }

    [Fact]
    public void Reports_A_Style_Reference_The_Package_Never_Defined()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(OdtTestPackage.StyledParagraph("Ghost", "x"));

        Assert.Contains(result.Diagnostics, d => d.Code == "odt.styles.unknown");
    }

    [Fact]
    public void Cuts_A_Cyclic_Parent_Chain_Short()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            OdtTestPackage.StyledParagraph("A", "x"),
            OdtTestPackage.Style("A", "<style:text-properties fo:font-weight=\"bold\"/>", parent: "B") +
            OdtTestPackage.Style("B", "<style:text-properties/>", parent: "A"));

        Assert.True(result.IsUsable);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.styles.cycle");
    }

    [Fact]
    public void A_Content_Automatic_Style_Wins_A_Name_Collision_With_A_Named_Style()
    {
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            OdtTestPackage.StyledParagraph("P1", "x"),
            "<office:styles>" +
            OdtTestPackage.Style("P1", "<style:text-properties fo:font-size=\"40pt\"/>") +
            "</office:styles>",
            OdtTestPackage.Style("P1", "<style:text-properties fo:font-size=\"11pt\"/>"));

        Assert.Equal(11f, result.Document.Paragraphs[0].StyleAt(0).FontSize);
    }

    [Fact]
    public void A_List_Level_Falls_Back_To_The_Deepest_Level_The_Style_Defines()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:list text:style-name=\"L2\"><text:list-item>" +
            "<text:list><text:list-item><text:p>deep</text:p></text:list-item></text:list>" +
            "</text:list-item></text:list>",
            OdtTestPackage.NumberListStyle("L2", levels: 1));

        Assert.Equal(ListKind.Numbered, result.Document.Paragraphs[0].Style.ListKind);
        Assert.Equal(2, result.Document.Paragraphs[0].Style.IndentLevel);
    }
}
