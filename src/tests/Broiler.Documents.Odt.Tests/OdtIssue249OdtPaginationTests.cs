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

    private const string Endnote =
        "<text:note text:id=\"ftn1\" text:note-class=\"endnote\"><text:note-citation>i</text:note-citation>" +
        "<text:note-body><text:p>end</text:p></text:note-body></text:note>";

    private static RichTextDocument ReadWithSettings(string body, string? settingName = null, string stylesXml = "<office:styles/>")
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (settingName is not null)
        {
            parts["settings.xml"] =
                "<office:document-settings xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
                "xmlns:config=\"urn:oasis:names:tc:opendocument:xmlns:config:1.0\"><office:settings>" +
                "<config:config-item-set config:name=\"ooo:configuration-settings\">" +
                "<config:config-item config:name=\"" + settingName + "\" config:type=\"boolean\">true</config:config-item>" +
                "</config:config-item-set></office:settings></office:document-settings>";
        }

        using var stream = new MemoryStream(
            OdtTestPackage.FromBody(body, stylesPartInnerXml: stylesXml, extraParts: parts), writable: false);
        return new OdtDocumentCodec().Read(stream).Document;
    }

    [Fact]
    public void Endnotes_Start_On_A_Page_Of_Their_Own_When_The_Configuration_Names_No_Page_Style()
    {
        // tdf160253_ordinary_numbering.fodt states no notes configuration;
        // LibreOffice sets its endnote on page 2, in the Endnote page style.
        Assert.True(ReadWithSettings("<text:p>x" + Endnote + "</text:p>").NoteNumbering.EndnotesStartOnNewPage);
        Assert.True(ReadWithSettings(
            "<text:p>x" + Endnote + "</text:p>",
            stylesXml: "<office:styles><text:notes-configuration text:note-class=\"endnote\" style:num-format=\"i\"/></office:styles>")
            .NoteNumbering.EndnotesStartOnNewPage);
    }

    [Fact]
    public void Continuous_Endnotes_Run_On_Under_The_Body_And_Survive_A_Round_Trip()
    {
        RichTextDocument document = ReadWithSettings("<text:p>x" + Endnote + "</text:p>", "ContinuousEndnotes");
        Assert.False(document.NoteNumbering.EndnotesStartOnNewPage);

        using var stream = new MemoryStream(OdtWriter.WriteToArray(document));
        Assert.False(new OdtDocumentCodec().Read(stream).Document.NoteNumbering.EndnotesStartOnNewPage);
    }

    [Fact]
    public void A_Master_Page_That_Names_No_Page_Layout_Is_On_Writers_Default_Page()
    {
        // tdf160253_outline_numbering.fodt: <style:master-page style:name="Standard"/>.
        PageGeometry page = ReadWithSettings(
            OdtTestPackage.Paragraph("text"),
            stylesXml: "<office:master-styles><style:master-page style:name=\"Standard\"/></office:master-styles>").PageGeometry!;

        Assert.Equal(595.3, page.Width, 1);
        Assert.Equal(841.89, page.Height, 1);
        Assert.Equal(2 * 72 / 2.54, page.MarginLeft, 1);
        Assert.Equal(2 * 72 / 2.54, page.MarginTop, 1);
    }

    [Fact]
    public void A_Sections_Endnote_Configuration_Collects_Its_Endnotes_At_Its_End()
    {
        // tdf146704_EndnoteInSection.odt and ooo32780-1.odt.
        string style = OdtTestPackage.Style("Collect",
            "<style:section-properties><text:notes-configuration text:note-class='endnote' text:start-value='6'/>" +
            "</style:section-properties>", "section");
        RichTextDocument document = OdtTestPackage.ReadBody(
            "<text:section text:style-name='Collect'><text:p>in" + Endnote + "</text:p></text:section>" +
            "<text:p>out" + Endnote.Replace("ftn1", "ftn2", StringComparison.Ordinal) + "</text:p>", style).Document;

        DocumentSection section = Assert.Single(document.Sections);
        Assert.Same(document.Notes[0].Section, Assert.Single(section.FootnotesEnding));
        Assert.Null(document.Notes[1].Section);
        var numberer = new NoteNumberer();
        Assert.Equal(["vi", "i"], document.Notes.Select(numberer.Label));
    }

    [Fact]
    public void A_Notes_Unstyled_Paragraph_Is_In_Writers_Built_In_Footnote_Style()
    {
        // ergo_sum_footnote.fodt: 10 pt, the text 6 mm in, the label hanging.
        RichTextDocument document = OdtTestPackage.ReadBody(
            "<text:p>x<text:note text:id=\"ftn1\" text:note-class=\"footnote\"><text:note-citation>1</text:note-citation>" +
            "<text:note-body><text:p>note</text:p></text:note-body></text:note></text:p>").Document;

        RichTextParagraph note = Assert.Single(Assert.Single(document.Notes).Paragraphs);
        Assert.Equal(10, note.Runs[0].Style.FontSize!.Value, 2);
        Assert.Equal(6 * 72 / 25.4, note.Style.LeftIndent!.Value, 1);
        Assert.Equal(-6 * 72 / 25.4, note.Style.FirstLineIndent!.Value, 1);
        Assert.NotEqual(10, document.Paragraphs[0].Runs[0].Style.FontSize);
    }

    [Fact]
    public void A_Note_Style_Whose_Parent_Footnote_Is_Undefined_Inherits_The_Built_In_One()
    {
        // Cool16071_footnoteTailKeptWhenTruncated.fodt's PFtn names Footnote.
        RichTextDocument document = OdtTestPackage.ReadBody(
            "<text:p>x<text:note text:id=\"ftn1\" text:note-class=\"footnote\"><text:note-citation>1</text:note-citation>" +
            "<text:note-body><text:p text:style-name=\"PFtn\">note</text:p></text:note-body></text:note></text:p>",
            OdtTestPackage.Style("PFtn", "<style:text-properties fo:font-size=\"9pt\"/>", parent: "Footnote")).Document;

        RichTextParagraph note = Assert.Single(Assert.Single(document.Notes).Paragraphs);
        Assert.Equal(9, note.Runs[0].Style.FontSize!.Value, 2);
        Assert.Equal(6 * 72 / 25.4, note.Style.LeftIndent!.Value, 1);
    }

    [Fact]
    public void A_Cell_Whose_Paragraphs_Are_All_Hidden_By_Their_Fields_Has_No_Line()
    {
        // table-0-height-rows.fodt: every paragraph of most rows holds a
        // text:hidden-paragraph whose condition is true; Writer gives those
        // rows no height, and an empty cell still gets its one empty line.
        const string hidden = "<text:p>gone<text:hidden-paragraph text:condition=\"ooow:1\" text:is-hidden=\"true\"/></text:p>";
        RichTextDocument document = OdtTestPackage.ReadBody(
            OdtTestPackage.Table([hidden], [hidden + hidden], [string.Empty], ["<text:p>kept</text:p>"])).Document;

        DocumentTable table = Assert.Single(document.Tables);
        Assert.Equal([0, 0, 1, 1], table.Rows.Select(row => row.Cells.Sum(cell => cell.ParagraphCount)));
        Assert.Equal(["", "kept"], document.Paragraphs.Select(paragraph => paragraph.Text));
    }

    [Fact]
    public void An_Odt_Documents_Column_Breaks_Need_Columns()
    {
        Assert.True(OdtTestPackage.ReadBody(OdtTestPackage.Paragraph("x")).Document.StyleDefaults.ColumnBreaksNeedColumns);
    }
}
