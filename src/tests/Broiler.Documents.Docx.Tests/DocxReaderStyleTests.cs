namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Covers <c>word/styles.xml</c> resolution — document defaults, the
/// <c>w:basedOn</c> chain, <c>w:pStyle</c>, <c>w:rStyle</c>, and theme fonts.
/// Template documents carry almost no direct formatting, so a paragraph's
/// appearance lives entirely in the style it names.
/// </summary>
public sealed class DocxReaderStyleTests
{
    [Fact(Timeout = 600000)]
    public void Applies_Run_Properties_From_A_Named_Paragraph_Style()
    {
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.StyledParagraph("Title", "Elene Bartky"),
            DocxTestPackage.Style("Title", "<w:rPr><w:b/><w:sz w:val=\"96\"/></w:rPr>"));

        InlineStyle style = Assert.Single(result.Document.Paragraphs).StyleAt(0);
        Assert.True(style.Bold);
        Assert.Equal(48f, style.FontSize);
    }

    [Fact(Timeout = 600000)]
    public void Applies_Paragraph_Properties_From_A_Named_Paragraph_Style()
    {
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.StyledParagraph("Centered", "text"),
            DocxTestPackage.Style(
                "Centered",
                "<w:pPr><w:jc w:val=\"center\"/><w:spacing w:after=\"240\"/></w:pPr>"));

        ParagraphStyle style = Assert.Single(result.Document.Paragraphs).Style;
        Assert.Equal(TextAlignment.Center, style.Alignment);
        Assert.Equal(12f, style.SpacingAfter);
    }

    [Fact(Timeout = 600000)]
    public void Walks_The_BasedOn_Chain_Root_First()
    {
        string styles =
            DocxTestPackage.Style("Base", "<w:rPr><w:sz w:val=\"20\"/><w:i/></w:rPr>") +
            DocxTestPackage.Style("Derived", "<w:rPr><w:sz w:val=\"48\"/></w:rPr>", basedOn: "Base");

        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.StyledParagraph("Derived", "text"),
            styles);

        InlineStyle style = Assert.Single(result.Document.Paragraphs).StyleAt(0);
        Assert.Equal(24f, style.FontSize);  // the derived style wins
        Assert.True(style.Italic);          // and inherits what it does not set
    }

    [Fact(Timeout = 600000)]
    public void Applies_Document_Defaults_To_Unstyled_Paragraphs()
    {
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.Paragraph("text"),
            DocxTestPackage.DocumentDefaults("<w:rPr><w:sz w:val=\"24\"/></w:rPr>"));

        Assert.Equal(12f, Assert.Single(result.Document.Paragraphs).StyleAt(0).FontSize);
    }

    [Fact(Timeout = 600000)]
    public void Applies_The_Default_Paragraph_Style_When_No_Style_Is_Named()
    {
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.Paragraph("text"),
            DocxTestPackage.Style("Normal", "<w:rPr><w:sz w:val=\"20\"/></w:rPr>", isDefault: true));

        Assert.Equal(10f, Assert.Single(result.Document.Paragraphs).StyleAt(0).FontSize);
    }

    /// <summary>
    /// ECMA-376 §17.7.2: the default style applies to content that names no
    /// style. A paragraph naming its own style picks up the default only when
    /// its <c>w:basedOn</c> chain leads there.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Does_Not_Apply_The_Default_Paragraph_Style_To_A_Paragraph_That_Names_One()
    {
        string styles =
            DocxTestPackage.Style("Normal", "<w:rPr><w:sz w:val=\"20\"/></w:rPr>", isDefault: true) +
            DocxTestPackage.Style("Loud", "<w:rPr><w:b/></w:rPr>");

        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.StyledParagraph("Loud", "text"),
            styles);

        InlineStyle style = Assert.Single(result.Document.Paragraphs).StyleAt(0);
        Assert.True(style.Bold);
        Assert.Null(style.FontSize);
    }

    [Fact(Timeout = 600000)]
    public void Direct_Run_Properties_Override_The_Paragraph_Style()
    {
        string paragraph =
            "<w:p><w:pPr><w:pStyle w:val=\"Title\"/></w:pPr>" +
            "<w:r><w:rPr><w:sz w:val=\"20\"/></w:rPr><w:t>text</w:t></w:r></w:p>";

        DocumentReadResult result = DocxTestPackage.ReadStyled(
            paragraph,
            DocxTestPackage.Style("Title", "<w:rPr><w:b/><w:sz w:val=\"96\"/></w:rPr>"));

        InlineStyle style = Assert.Single(result.Document.Paragraphs).StyleAt(0);
        Assert.Equal(10f, style.FontSize);
        Assert.True(style.Bold);
    }

    [Fact(Timeout = 600000)]
    public void Direct_Paragraph_Properties_Override_The_Paragraph_Style()
    {
        string paragraph =
            "<w:p><w:pPr><w:pStyle w:val=\"Centered\"/><w:jc w:val=\"right\"/></w:pPr>" +
            "<w:r><w:t>text</w:t></w:r></w:p>";

        DocumentReadResult result = DocxTestPackage.ReadStyled(
            paragraph,
            DocxTestPackage.Style("Centered", "<w:pPr><w:jc w:val=\"center\"/></w:pPr>"));

        Assert.Equal(TextAlignment.Right, Assert.Single(result.Document.Paragraphs).Style.Alignment);
    }

    [Fact(Timeout = 600000)]
    public void Applies_A_Character_Style_Named_By_RStyle()
    {
        string paragraph =
            "<w:p><w:r><w:rPr><w:rStyle w:val=\"Emphasis\"/></w:rPr><w:t>text</w:t></w:r></w:p>";

        DocumentReadResult result = DocxTestPackage.ReadStyled(
            paragraph,
            DocxTestPackage.Style("Emphasis", "<w:rPr><w:i/></w:rPr>", type: "character"));

        Assert.True(Assert.Single(result.Document.Paragraphs).StyleAt(0).Italic);
    }

    [Fact(Timeout = 600000)]
    public void A_Character_Style_Overrides_The_Paragraph_Style_And_Yields_To_Direct_Formatting()
    {
        string styles =
            DocxTestPackage.Style("Body", "<w:rPr><w:sz w:val=\"20\"/><w:b/></w:rPr>") +
            DocxTestPackage.Style("Big", "<w:rPr><w:sz w:val=\"48\"/></w:rPr>", type: "character");

        string paragraph =
            "<w:p><w:pPr><w:pStyle w:val=\"Body\"/></w:pPr>" +
            "<w:r><w:rPr><w:rStyle w:val=\"Big\"/></w:rPr><w:t>styled</w:t></w:r>" +
            "<w:r><w:rPr><w:rStyle w:val=\"Big\"/><w:sz w:val=\"28\"/></w:rPr><w:t>direct</w:t></w:r></w:p>";

        DocumentReadResult result = DocxTestPackage.ReadStyled(paragraph, styles);

        RichTextParagraph read = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("styleddirect", read.Text);
        Assert.Equal(24f, read.StyleAt(0).FontSize);
        Assert.True(read.StyleAt(0).Bold);           // still inherited from the paragraph style
        Assert.Equal(14f, read.StyleAt(read.Text.IndexOf("direct", StringComparison.Ordinal)).FontSize);
    }

    [Fact(Timeout = 600000)]
    public void Resolves_Theme_Fonts_Referenced_By_A_Style()
    {
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.StyledParagraph("Heading", "heading"),
            DocxTestPackage.Style("Heading", "<w:rPr><w:rFonts w:asciiTheme=\"majorHAnsi\"/></w:rPr>"),
            DocxTestPackage.ThemeXml("Century Gothic", "Calibri"));

        Assert.Equal("Century Gothic", Assert.Single(result.Document.Paragraphs).StyleAt(0).FontFamily);
    }

    [Fact(Timeout = 600000)]
    public void Resolves_The_Minor_Theme_Font_For_Body_Text()
    {
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.Paragraph("body"),
            DocxTestPackage.DocumentDefaults("<w:rPr><w:rFonts w:asciiTheme=\"minorHAnsi\"/></w:rPr>"),
            DocxTestPackage.ThemeXml("Century Gothic", "Calibri"));

        Assert.Equal("Calibri", Assert.Single(result.Document.Paragraphs).StyleAt(0).FontFamily);
    }

    [Fact(Timeout = 600000)]
    public void An_Explicit_Font_Name_Wins_Over_A_Theme_Reference()
    {
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.StyledParagraph("Heading", "heading"),
            DocxTestPackage.Style(
                "Heading",
                "<w:rPr><w:rFonts w:ascii=\"Georgia\" w:asciiTheme=\"majorHAnsi\"/></w:rPr>"),
            DocxTestPackage.ThemeXml("Century Gothic", "Calibri"));

        Assert.Equal("Georgia", Assert.Single(result.Document.Paragraphs).StyleAt(0).FontFamily);
    }

    [Fact(Timeout = 600000)]
    public void Applies_List_Numbering_Declared_By_A_Paragraph_Style()
    {
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.StyledParagraph("ListBullet", "item"),
            DocxTestPackage.Style(
                "ListBullet",
                "<w:pPr><w:numPr><w:numId w:val=\"1\"/></w:numPr><w:ind w:left=\"720\"/></w:pPr>"));

        ParagraphStyle style = Assert.Single(result.Document.Paragraphs).Style;
        Assert.Equal(ListKind.Bullet, style.ListKind);
        Assert.Equal(2, style.IndentLevel);
    }

    [Fact(Timeout = 600000)]
    public void A_Zero_NumId_Opts_A_Paragraph_Out_Of_Its_Styles_List()
    {
        string paragraph =
            "<w:p><w:pPr><w:pStyle w:val=\"ListBullet\"/>" +
            "<w:numPr><w:numId w:val=\"0\"/></w:numPr></w:pPr>" +
            "<w:r><w:t>not a bullet</w:t></w:r></w:p>";

        DocumentReadResult result = DocxTestPackage.ReadStyled(
            paragraph,
            DocxTestPackage.Style("ListBullet", "<w:pPr><w:numPr><w:numId w:val=\"1\"/></w:numPr></w:pPr>"));

        Assert.Equal(ListKind.None, Assert.Single(result.Document.Paragraphs).Style.ListKind);
    }

    [Fact(Timeout = 600000)]
    public void Resolves_Styles_For_Paragraphs_Inside_Table_Cells()
    {
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.Table([[DocxTestPackage.StyledParagraph("Title", "Elene Bartky")]]),
            DocxTestPackage.Style("Title", "<w:rPr><w:sz w:val=\"96\"/></w:rPr>"));

        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("Elene Bartky", paragraph.Text);
        Assert.Equal(48f, paragraph.StyleAt(0).FontSize);
    }

    [Fact(Timeout = 600000)]
    public void Reports_A_Style_Reference_That_Names_An_Undefined_Style()
    {
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.StyledParagraph("Missing", "text") +
            DocxTestPackage.StyledParagraph("Missing", "more"),
            DocxTestPackage.Style("Present", "<w:rPr><w:b/></w:rPr>"));

        DocumentDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Code == "docx.styles.unknown");

        Assert.Contains("Missing", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal("text", result.Document.Paragraphs[0].Text);
    }

    [Fact(Timeout = 600000)]
    public void Cuts_A_Cyclic_BasedOn_Chain_Short()
    {
        string styles =
            DocxTestPackage.Style("A", "<w:rPr><w:b/></w:rPr>", basedOn: "B") +
            DocxTestPackage.Style("B", "<w:rPr><w:i/></w:rPr>", basedOn: "A");

        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.StyledParagraph("A", "text"),
            styles);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.styles.cycle");

        InlineStyle style = Assert.Single(result.Document.Paragraphs).StyleAt(0);
        Assert.True(style.Bold);
        Assert.True(style.Italic);
    }

    /// <summary>A missing style table is one fact about the package, not one note per style reference.</summary>
    [Fact(Timeout = 600000)]
    public void Reports_A_Missing_Styles_Part_Once()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            DocxTestPackage.StyledParagraph("Title", "text") +
            DocxTestPackage.StyledParagraph("Subtitle", "more"));

        Assert.Equal("text", result.Document.Paragraphs[0].Text);
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "docx.styles.missing");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "docx.styles.unknown");
    }

    [Fact(Timeout = 600000)]
    public void Reads_An_Unstyled_Document_With_No_Styles_Part_Without_Notes()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(DocxTestPackage.Paragraph("text"));

        Assert.Equal("text", Assert.Single(result.Document.Paragraphs).Text);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Code.StartsWith("docx.styles", StringComparison.Ordinal));
    }

    [Fact(Timeout = 600000)]
    public void Reports_The_Resolved_Style_Count_In_The_Read_Summary()
    {
        DocumentReadResult result = DocxTestPackage.ReadStyled(
            DocxTestPackage.Paragraph("text"),
            DocxTestPackage.Style("A", "<w:rPr><w:b/></w:rPr>") +
            DocxTestPackage.Style("B", "<w:rPr><w:i/></w:rPr>"));

        DocumentDiagnostic summary = Assert.Single(
            result.Diagnostics,
            diagnostic => diagnostic.Code == "docx.read.summary");

        Assert.Contains("2 style(s)", summary.Message, StringComparison.Ordinal);
    }
}
