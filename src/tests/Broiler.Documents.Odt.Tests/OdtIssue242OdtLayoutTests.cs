namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Office conformance issue 242, ODT layout: raised and lowered text
/// (<c>style:text-position</c>, tdf127316_autoEscapement2.odt), Writer's
/// defaults for a document that states no page or font
/// (hiddenSectionsAroundPageBreak.fodt), its built-in Landscape and Heading
/// styles, and an index that was never generated.
/// </summary>
public sealed class OdtIssue242OdtLayoutTests
{
    private static RichTextDocument Read(string body, string automaticStyles = "") =>
        OdtTestPackage.ReadBody(body, automaticStyles).Document;

    private static InlineStyle SpanStyle(string position)
    {
        RichTextDocument document = Read(
            "<text:p>Base<text:span text:style-name=\"T1\">raised</text:span></text:p>",
            OdtTestPackage.Style("T1", "<style:text-properties style:text-position=\"" + position + "\"/>", "text"));
        return StyleOf(document.Paragraphs[0], "raised");
    }

    /// <summary>The style of the run that holds <paramref name="text"/>.</summary>
    private static InlineStyle StyleOf(RichTextParagraph paragraph, string text)
    {
        int at = paragraph.Text.IndexOf(text, StringComparison.Ordinal);
        int start = 0;
        foreach (StyleRun run in paragraph.Runs)
        {
            if (at < start + run.Length)
                return run.Style;
            start += run.Length;
        }
        throw new InvalidOperationException(text);
    }

    [Fact]
    public void Super_With_A_Size_Is_An_Automatic_Superscript_At_That_Size()
    {
        BaselineShift shift = SpanStyle("super 80%").Shift!.Value;

        Assert.Equal(BaselinePosition.AutoSuperscript, shift.Position);
        Assert.Equal(0.8, shift.Scale, 6);
    }

    [Fact]
    public void Sub_Without_A_Size_Takes_Writer_Default_58_Percent()
    {
        BaselineShift shift = SpanStyle("sub").Shift!.Value;

        Assert.Equal(BaselinePosition.AutoSubscript, shift.Position);
        Assert.Equal(BaselineShift.DefaultScale, shift.Scale, 6);
    }

    [Theory]
    [InlineData("33% 58%", 0.33, 0.58)]
    [InlineData("-33% 100%", -0.33, 1.0)]
    [InlineData("14%", 0.14, 0.58)]
    public void A_Percentage_Moves_The_Baseline_By_That_Part_Of_The_Font_Height(string position, double offset, double scale)
    {
        BaselineShift shift = SpanStyle(position).Shift!.Value;

        Assert.Equal(BaselinePosition.Offset, shift.Position);
        Assert.Equal(offset, shift.Offset, 6);
        Assert.Equal(scale, shift.Scale, 6);
    }

    [Fact]
    public void Text_On_The_Baseline_At_Full_Size_Has_No_Shift()
    {
        Assert.Null(SpanStyle("0% 100%").Shift);
    }

    [Fact]
    public void A_Span_Back_On_The_Baseline_Undoes_Its_Paragraph_Style_Shift()
    {
        RichTextDocument document = Read(
            "<text:p text:style-name=\"P1\">up<text:span text:style-name=\"T1\">down</text:span></text:p>",
            OdtTestPackage.Style("P1", "<style:text-properties style:text-position=\"super 58%\"/>") +
            OdtTestPackage.Style("T1", "<style:text-properties style:text-position=\"0% 100%\"/>", "text"));

        Assert.NotNull(StyleOf(document.Paragraphs[0], "up").Shift);
        Assert.Null(StyleOf(document.Paragraphs[0], "down").Shift);
    }

    [Fact]
    public void A_Document_With_No_Master_Page_Is_On_Writers_Default_Page()
    {
        PageGeometry page = Read(OdtTestPackage.Paragraph("text")).PageGeometry!;

        Assert.Equal(595.3, page.Width, 1);
        Assert.Equal(841.89, page.Height, 1);
        Assert.Equal(2 * 72 / 2.54, page.MarginLeft, 1);
        Assert.Equal(2 * 72 / 2.54, page.MarginTop, 1);
    }

    [Fact]
    public void A_Document_That_Names_No_Font_Is_Set_In_Liberation_Serif()
    {
        Assert.Equal("Liberation Serif", Read(OdtTestPackage.Paragraph("text")).StyleDefaults.FontFamily);
    }

    [Fact]
    public void A_Default_Style_Font_Is_Kept()
    {
        RichTextDocument document = OdtTestPackage.ReadStyled(OdtTestPackage.Paragraph("text"),
            "<office:styles><style:default-style style:family=\"paragraph\">" +
            "<style:text-properties fo:font-family=\"DejaVu Sans\"/></style:default-style></office:styles>").Document;

        Assert.Equal("DejaVu Sans", document.StyleDefaults.FontFamily);
    }

    [Fact]
    public void The_Opening_Paragraph_On_The_Built_In_Landscape_Style_Turns_The_First_Page()
    {
        // hiddenSectionsAroundPageBreak.fodt names "Landscape" without defining
        // it, and writes no page layout at all.
        RichTextDocument document = Read(
            "<text:section text:name=\"S1\" text:display=\"none\"/>" +
            OdtTestPackage.StyledParagraph("P1", "first") + OdtTestPackage.Paragraph("second"),
            "<style:style style:name=\"P1\" style:family=\"paragraph\" style:master-page-name=\"Landscape\">" +
            "<style:paragraph-properties fo:break-before=\"page\"/></style:style>");

        PageGeometry page = document.PageGeometry!;
        Assert.Equal(841.89, page.Width, 1);
        Assert.Equal(595.3, page.Height, 1);
        Assert.False(document.Paragraphs[1].Style.PageBreakBefore);
    }

    [Fact]
    public void An_Unstyled_Heading_Without_A_Heading_Style_Uses_Writers_Built_In_One()
    {
        // IndexElementsInHiddenSections.fodt: Writer's Heading is Liberation
        // Sans at 14 pt, and Heading 1 is 130% of it, bold.
        RichTextDocument document = Read("<text:h text:outline-level=\"1\">Title</text:h>" + OdtTestPackage.Paragraph("body"));

        InlineStyle heading = document.Paragraphs[0].Runs[0].Style;
        Assert.Equal("Liberation Sans", heading.FontFamily);
        Assert.Equal(14 * 1.3, heading.FontSize!.Value, 2);
        Assert.True(heading.Bold);
        Assert.True(document.Paragraphs[0].Style.KeepWithNext);
    }

    [Fact]
    public void An_Index_That_Was_Never_Generated_Is_One_Empty_Paragraph()
    {
        RichTextDocument document = Read(
            "<text:table-of-content text:name=\"TOC\"><text:table-of-content-source text:outline-level=\"1\"/></text:table-of-content>" +
            OdtTestPackage.Paragraph("after"));

        Assert.Equal(2, document.ParagraphCount);
        Assert.Equal(string.Empty, document.Paragraphs[0].Text);
        Assert.Equal("after", document.Paragraphs[1].Text);
    }

    [Fact]
    public void A_Generated_Index_Adds_No_Empty_Paragraph()
    {
        RichTextDocument document = Read(
            "<text:table-of-content text:name=\"TOC\"><text:table-of-content-source text:outline-level=\"1\"/>" +
            "<text:index-body><text:p>Entry</text:p></text:index-body></text:table-of-content>" +
            OdtTestPackage.Paragraph("after"));

        Assert.Equal(2, document.ParagraphCount);
        Assert.Equal("Entry", document.Paragraphs[0].Text);
    }
}
