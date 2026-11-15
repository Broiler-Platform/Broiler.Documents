namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Office conformance issue 249, ODT pagination: causes that put a different
/// number of lines on a page than LibreOffice 24.2 does.
/// </summary>
public sealed class OdtIssue249OdtPaginationTests
{
    [Fact]
    public void A_Heading_Style_Whose_Parent_Heading_Is_Undefined_Inherits_Writers_Built_In_Heading()
    {
        // tdf160518_*.fodt and tdf132599_*.fodt: Heading 1 names the parent
        // "Heading", which the document never defines. Writer creates its pool
        // Heading for it: Liberation Sans, 12 pt above, 6 pt below, kept with
        // the next paragraph.
        RichTextDocument document = OdtTestPackage.ReadStyled(
            "<text:h text:style-name=\"Heading_20_1\" text:outline-level=\"1\">Title</text:h>" +
            OdtTestPackage.Paragraph("body"),
            "<office:styles>" +
            "<style:style style:name=\"Standard\" style:family=\"paragraph\"/>" +
            "<style:style style:name=\"Heading_20_1\" style:family=\"paragraph\" style:parent-style-name=\"Heading\">" +
            "<style:text-properties fo:font-size=\"18pt\" fo:font-weight=\"bold\"/></style:style>" +
            "</office:styles>").Document;

        RichTextParagraph heading = document.Paragraphs[0];
        Assert.Equal("Liberation Sans", heading.Runs[0].Style.FontFamily);
        Assert.Equal(18, heading.Runs[0].Style.FontSize!.Value, 2);
        Assert.Equal(12, heading.Style.SpacingBefore, 2);
        Assert.Equal(6, heading.Style.SpacingAfter, 2);
        Assert.True(heading.Style.KeepWithNext);
    }

    [Fact]
    public void A_Style_Whose_Undefined_Parent_Is_Not_A_Writer_Pool_Name_Stays_Without_One()
    {
        RichTextDocument document = OdtTestPackage.ReadStyled(
            OdtTestPackage.StyledParagraph("Mine", "text"),
            "<office:styles>" +
            "<style:style style:name=\"Mine\" style:family=\"paragraph\" style:parent-style-name=\"Nowhere\">" +
            "<style:text-properties fo:font-size=\"18pt\"/></style:style>" +
            "</office:styles>").Document;

        RichTextParagraph paragraph = document.Paragraphs[0];
        Assert.Equal(0, paragraph.Style.SpacingBefore, 2);
        Assert.False(paragraph.Style.KeepWithNext);
    }
}
