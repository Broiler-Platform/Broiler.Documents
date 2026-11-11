using Broiler.Graphics.Color;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Office conformance issue 232: tdf163285.fodt, nestedTableInFooter.odt and
/// tdf135056.odt, each read as LibreOffice reads it.
/// </summary>
public sealed class OdtIssue232Tests
{
    private const string StandardWithSpacing =
        "<office:styles><style:style style:name=\"Standard\" style:family=\"paragraph\">" +
        "<style:paragraph-properties fo:margin-bottom=\"8pt\"/></style:style></office:styles>";

    [Fact]
    public void A_Paragraph_Naming_No_Style_Takes_Standard()
    {
        // tdf163285.fodt: bare list items and empty spacer paragraphs keep
        // Standard's 8pt below, as Writer gives every unnamed text:p Standard.
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            "<text:p>bare</text:p><text:list><text:list-item><text:p>item</text:p></text:list-item></text:list>",
            StandardWithSpacing);

        Assert.All(result.Document.Paragraphs, paragraph => Assert.Equal(8, paragraph.Style.SpacingAfter, 3));
    }

    [Fact]
    public void A_Footer_Keeps_Its_Table_And_Drops_The_Empty_Paragraph_After_A_Nested_One()
    {
        // nestedTableInFooter.odt: a footer holding a two-column table whose
        // first cell holds a nested table and the empty paragraph Writer
        // saves after it.
        string cellStyle = "<style:style style:name=\"C\" style:family=\"table-cell\">" +
            "<style:table-cell-properties fo:border=\"0.5pt solid #000000\"/></style:style>";
        string footer =
            "<style:footer><table:table table:name=\"T1\"><table:table-column table:number-columns-repeated=\"2\"/>" +
            "<table:table-row><table:table-cell table:style-name=\"C\">" +
            "<table:table table:name=\"T2\"><table:table-column/><table:table-row><table:table-cell table:style-name=\"C\"><text:p/></table:table-cell></table:table-row></table:table>" +
            "<text:p/></table:table-cell><table:table-cell table:style-name=\"C\"><text:p/></table:table-cell></table:table-row>" +
            "</table:table><text:p/></style:footer>";
        string styles = "<office:automatic-styles>" + cellStyle +
            "<style:page-layout style:name=\"pm\"><style:page-layout-properties fo:page-width=\"400pt\" fo:page-height=\"600pt\" " +
            "fo:margin-left=\"30pt\" fo:margin-right=\"30pt\" fo:margin-top=\"30pt\" fo:margin-bottom=\"30pt\"/>" +
            "<style:footer-style><style:header-footer-properties fo:min-height=\"0pt\" fo:margin-top=\"10pt\"/></style:footer-style>" +
            "</style:page-layout></office:automatic-styles><office:master-styles>" +
            "<style:master-page style:name=\"Standard\" style:page-layout-name=\"pm\">" + footer + "</style:master-page></office:master-styles>";

        using var stream = new MemoryStream(OdtTestPackage.FromBody("<text:p>body</text:p>", stylesPartInnerXml: styles));
        RichTextDocument document = new OdtDocumentCodec().Read(stream).Document;

        DocumentTable outer = Assert.Single(document.RunningContent.EffectiveFooterTables(PageSelection.Default));
        TableCell first = outer.Rows[0].Cells[0];
        DocumentTable nested = Assert.Single(first.Tables);

        // The first cell ends where its nested table does.
        Assert.Equal(nested.ParagraphEnd, first.ParagraphIndex + first.ParagraphCount);
        Assert.Equal(3, document.RunningContent.EffectiveFooter(PageSelection.Default).Count);
    }

    [Fact]
    public void A_Link_Naming_A_Style_Takes_Only_That_Style()
    {
        // tdf135056.odt's table of contents: entries name the empty "Index
        // Link" style and are drawn neither blue nor underlined; a link naming
        // no style beside them takes Writer's navy, underlined Internet Link.
        string styles = "<office:styles><style:style style:name=\"Index_20_Link\" style:family=\"text\"/></office:styles>";
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            "<text:p><text:a xlink:type=\"simple\" xlink:href=\"#h\" text:style-name=\"Index_20_Link\">entry</text:a></text:p>" +
            "<text:p><text:a xlink:type=\"simple\" xlink:href=\"https://example.com/\">plain</text:a></text:p>",
            styles);

        Assert.True(result.Document.StyleDefaults.UndecoratedLinks);
        InlineStyle entry = result.Document.Paragraphs[0].StyleAt(0);
        Assert.False(entry.Underline);
        Assert.True(entry.Foreground.IsEmpty);
        InlineStyle plain = result.Document.Paragraphs[1].StyleAt(0);
        Assert.True(plain.Underline);
        Assert.Equal(BColor.FromArgb(0x00, 0x00, 0x80), plain.Foreground);
    }

    [Fact]
    public void Links_Naming_No_Style_Keep_The_Layouts_Link_Decoration()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p><text:a xlink:type=\"simple\" xlink:href=\"https://example.com/\">plain</text:a></text:p>");

        Assert.False(result.Document.StyleDefaults.UndecoratedLinks);
        Assert.False(result.Document.Paragraphs[0].StyleAt(0).Underline);
    }

    [Fact]
    public void An_Empty_Footer_Paragraph_With_A_Border_Is_Still_A_Footer()
    {
        // tdf135056.odt: the rule above the page number is the top border of
        // an otherwise empty footer paragraph.
        string styles = "<office:automatic-styles>" +
            "<style:style style:name=\"MP6\" style:family=\"paragraph\"><style:paragraph-properties fo:border-top=\"0.06pt solid #000000\"/></style:style>" +
            "<style:page-layout style:name=\"pm\"><style:page-layout-properties fo:page-width=\"400pt\" fo:page-height=\"600pt\"/></style:page-layout>" +
            "</office:automatic-styles><office:master-styles><style:master-page style:name=\"Standard\" style:page-layout-name=\"pm\">" +
            "<style:footer><text:p text:style-name=\"MP6\"/></style:footer></style:master-page></office:master-styles>";

        using var stream = new MemoryStream(OdtTestPackage.FromBody("<text:p>body</text:p>", stylesPartInnerXml: styles));
        RichTextDocument document = new OdtDocumentCodec().Read(stream).Document;

        RichTextParagraph footer = Assert.Single(document.RunningContent.EffectiveFooter(PageSelection.Default));
        Assert.NotNull(footer.Style.Border);
    }
}
