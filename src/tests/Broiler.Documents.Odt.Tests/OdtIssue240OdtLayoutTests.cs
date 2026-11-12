using Broiler.Graphics.Color;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Office conformance issue 240, ODT layout: a page layout's line text grid
/// (tdf167583.fodt, tdf88752.fodt, tdf144450.fodt), paragraphs that keep off
/// it, contextual spacing between paragraphs of one style and the settings
/// that collapse paragraph spacing (tdf133507_contextualSpacingSection.odt).
/// </summary>
public sealed class OdtIssue240OdtLayoutTests
{
    private static string PageLayout(string gridAttributes) =>
        "<office:automatic-styles><style:page-layout style:name='pm1'>" +
        "<style:page-layout-properties fo:page-width='8.2681in' fo:page-height='11.6929in' fo:margin-top='0.7874in' " +
        "fo:margin-bottom='0.7874in' fo:margin-left='0.7874in' fo:margin-right='0.7874in' " + gridAttributes + "/>" +
        "</style:page-layout></office:automatic-styles>" +
        "<office:master-styles><style:master-page style:name='Standard' style:page-layout-name='pm1'/></office:master-styles>";

    private const string LineGrid =
        "style:layout-grid-mode='line' style:layout-grid-lines='8' style:layout-grid-base-height='1.111in' " +
        "style:layout-grid-ruby-height='0.139in' style:layout-grid-ruby-below='false' " +
        "style:layout-grid-print='false' style:layout-grid-display='false' style:layout-grid-color='#c0c0c0'";

    private static string Settings(params (string Name, string Value)[] items) =>
        "<office:document-settings xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
        "xmlns:config=\"urn:oasis:names:tc:opendocument:xmlns:config:1.0\"><office:settings>" +
        "<config:config-item-set config:name=\"ooo:configuration-settings\">" +
        string.Concat(items.Select(item => "<config:config-item config:name=\"" + item.Name + "\" config:type=\"boolean\">" +
            item.Value + "</config:config-item>")) +
        "</config:config-item-set></office:settings></office:document-settings>";

    private static RichTextDocument Read(string body, string? styles = null, string automaticStyles = "", string? settings = null)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (settings is not null)
            parts["settings.xml"] = settings;
        using var stream = new MemoryStream(
            OdtTestPackage.FromBody(body, automaticStyles, styles, extraParts: parts), writable: false);
        return new OdtDocumentCodec().Read(stream).Document;
    }

    [Fact]
    public void A_Line_Grid_Is_Read_Into_The_Page()
    {
        TextGrid grid = Assert.IsType<TextGrid>(Read(OdtTestPackage.Paragraph("x"), PageLayout(LineGrid)).PageGeometry?.TextGrid);

        Assert.Equal(1.111 * 72, grid.BaseHeight, 3);
        Assert.Equal(0.139 * 72, grid.RubyHeight, 3);
        Assert.Equal(1.25 * 72, grid.Pitch, 3);
        Assert.Equal(8, grid.Lines);
        Assert.False(grid.RubyBelow);
        Assert.True(grid.CentredInBody);
        Assert.False(grid.Printed);
        Assert.Equal(0, grid.CharWidth);
    }

    [Fact]
    public void A_Page_Without_A_Grid_Mode_Has_No_Grid()
    {
        Assert.Null(Read(OdtTestPackage.Paragraph("x"),
            PageLayout(LineGrid.Replace("layout-grid-mode='line'", "layout-grid-mode='none'", StringComparison.Ordinal)))
            .PageGeometry?.TextGrid);
    }

    [Fact]
    public void A_Printed_Grid_Of_Characters_Has_Square_Cells_Unless_Its_Width_Is_Stated()
    {
        string both = LineGrid.Replace("layout-grid-mode='line'", "layout-grid-mode='both'", StringComparison.Ordinal)
            .Replace("layout-grid-print='false' style:layout-grid-display='false'",
                "layout-grid-print='true' style:layout-grid-display='true'", StringComparison.Ordinal)
            .Replace("#c0c0c0", "#b4c7dc", StringComparison.Ordinal);

        TextGrid square = Read(OdtTestPackage.Paragraph("x"), PageLayout(both)).PageGeometry!.TextGrid!;
        Assert.True(square.Printed);
        Assert.Equal(square.BaseHeight, square.CharWidth, 3);
        Assert.Equal(new BColor(0xb4, 0xc7, 0xdc), square.Color);

        TextGrid standard = Read(OdtTestPackage.Paragraph("x"), PageLayout(both +
            " style:layout-grid-standard-mode='true' style:layout-grid-base-width='0.2in'")).PageGeometry!.TextGrid!;
        Assert.Equal(0.2 * 72, standard.CharWidth, 3);
    }

    [Fact]
    public void Word_Grid_Metrics_Start_The_Grid_At_The_Top_Of_The_Body()
    {
        // tdf#165089: from LibreOffice 25.8 the grid is not centred under
        // MsWordCompGridMetrics.
        TextGrid grid = Read(OdtTestPackage.Paragraph("x"), PageLayout(LineGrid),
            settings: Settings(("MsWordCompGridMetrics", "true"))).PageGeometry!.TextGrid!;

        Assert.False(grid.CentredInBody);
    }

    [Fact]
    public void A_Paragraph_Can_Keep_Off_The_Grid()
    {
        RichTextDocument document = Read(
            OdtTestPackage.StyledParagraph("off", "off") + OdtTestPackage.Paragraph("on"),
            automaticStyles: OdtTestPackage.Style("off", "<style:paragraph-properties style:snap-to-layout-grid='false'/>"));

        Assert.True(document.Paragraphs[0].Style.IgnoresTextGrid);
        Assert.False(document.Paragraphs[1].Style.IgnoresTextGrid);
    }

    private const string ContextualStyles =
        "<office:styles><style:style style:name='Body' style:family='paragraph'>" +
        "<style:paragraph-properties fo:margin-top='10pt' fo:margin-bottom='20pt'/></style:style>" +
        "<style:style style:name='Other' style:family='paragraph'>" +
        "<style:paragraph-properties fo:margin-top='10pt' fo:margin-bottom='20pt' style:contextual-spacing='true'/></style:style>" +
        "</office:styles>";

    private const string ContextualAutomatic =
        "<style:style style:name='C' style:family='paragraph' style:parent-style-name='Body'>" +
        "<style:paragraph-properties style:contextual-spacing='true'/></style:style>" +
        "<style:style style:name='N' style:family='paragraph' style:parent-style-name='Body'>" +
        "<style:paragraph-properties style:contextual-spacing='false'/></style:style>";

    private static RichTextDocument ReadContextual(string body) =>
        Read(body, ContextualStyles, ContextualAutomatic);

    [Fact]
    public void Paragraphs_Of_One_Style_With_Contextual_Spacing_Lose_The_Space_Between_Them()
    {
        // Two automatic styles over one named style are one style to Writer.
        RichTextDocument document = ReadContextual(
            OdtTestPackage.StyledParagraph("C", "a") + OdtTestPackage.StyledParagraph("C", "b"));

        Assert.Equal(0, document.Paragraphs[0].Style.SpacingAfter);
        Assert.Equal(0, document.Paragraphs[1].Style.SpacingBefore);
        Assert.Equal(10, document.Paragraphs[0].Style.SpacingBefore);
        Assert.Equal(20, document.Paragraphs[1].Style.SpacingAfter);
    }

    [Fact]
    public void Contextual_Spacing_Takes_Each_Paragraphs_Own_Setting()
    {
        // LibreOffice 24.2: a paragraph with the setting loses its space below
        // before one of its style, and its space above only after one that has
        // the setting too.
        RichTextDocument document = ReadContextual(
            OdtTestPackage.StyledParagraph("N", "a") + OdtTestPackage.StyledParagraph("C", "b") +
            OdtTestPackage.StyledParagraph("N", "c"));

        Assert.Equal(20, document.Paragraphs[0].Style.SpacingAfter);
        Assert.Equal(10, document.Paragraphs[1].Style.SpacingBefore);
        Assert.Equal(0, document.Paragraphs[1].Style.SpacingAfter);
        Assert.Equal(10, document.Paragraphs[2].Style.SpacingBefore);
    }

    [Fact]
    public void Contextual_Spacing_Needs_The_Same_Named_Style()
    {
        RichTextDocument document = ReadContextual(
            OdtTestPackage.StyledParagraph("Other", "a") + OdtTestPackage.StyledParagraph("C", "b"));

        Assert.Equal(20, document.Paragraphs[0].Style.SpacingAfter);
        Assert.Equal(10, document.Paragraphs[1].Style.SpacingBefore);
    }

    [Fact]
    public void Contextual_Spacing_Stops_At_A_Section_Edge()
    {
        RichTextDocument document = ReadContextual(
            "<text:section text:name='S1'>" + OdtTestPackage.StyledParagraph("C", "a") +
            OdtTestPackage.StyledParagraph("C", "b") + "</text:section>" + OdtTestPackage.StyledParagraph("C", "c"));

        Assert.Equal(0, document.Paragraphs[0].Style.SpacingAfter);
        Assert.Equal(20, document.Paragraphs[1].Style.SpacingAfter);
        Assert.Equal(10, document.Paragraphs[2].Style.SpacingBefore);
    }

    [Theory]
    [InlineData("false", true, false)]
    [InlineData("true", false, true)]
    public void The_Paragraph_Spacing_Settings_Are_Read(string value, bool collapses, bool opening)
    {
        DocumentStyleDefaults defaults = Read(OdtTestPackage.Paragraph("x"), settings: Settings(
            ("AddParaTableSpacing", value), ("AddParaTableSpacingAtStart", value))).StyleDefaults;

        Assert.Equal(collapses, defaults.CollapsesParagraphSpacing);
        Assert.Equal(opening, defaults.PreserveOpeningParagraphSpacing);
    }

    [Fact]
    public void A_Document_That_States_No_Spacing_Setting_Adds_Both_Spaces()
    {
        DocumentStyleDefaults defaults = OdtTestPackage.ReadBody(OdtTestPackage.Paragraph("x")).Document.StyleDefaults;

        Assert.False(defaults.CollapsesParagraphSpacing);
        Assert.True(defaults.PreserveOpeningParagraphSpacing);
    }
}
