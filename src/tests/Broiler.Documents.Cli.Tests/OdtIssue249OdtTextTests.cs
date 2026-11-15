using System.Text;
using Broiler.Documents.Cli.Commands;
using Broiler.Documents.Odt;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// Office conformance issue 249, ODT text: flat ODF documents read and written
/// as text the way LibreOffice 24.2's text export writes them - page numbers
/// it has not laid out yet, form fields, list labels in its numbering types
/// and list formats, and headings whose style leaves the outline.
/// </summary>
public sealed class OdtIssue249OdtTextTests
{
    private const string Namespaces =
        "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
        "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" " +
        "xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" " +
        "xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" " +
        "xmlns:form=\"urn:oasis:names:tc:opendocument:xmlns:form:1.0\" " +
        "xmlns:field=\"urn:openoffice:names:experimental:ooo-ms-interop:xmlns:field:1.0\" " +
        "xmlns:loext=\"urn:org:documentfoundation:names:experimental:office:xmlns:loext:1.0\" " +
        "xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" " +
        "xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\"";

    private static RichTextDocument Read(string body, string styles = "", string forms = "")
    {
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<office:document " + Namespaces + " office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.text\">" +
            "<office:styles>" + styles + "</office:styles>" +
            "<office:body><office:text>" + forms + body + "</office:text></office:body></office:document>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml), writable: false);
        return new OdtDocumentCodec().Read(stream).Document;
    }

    private static string Text(string body, string styles = "", string forms = "") =>
        DocumentCommands.LabelledText(Read(body, styles, forms), joinsTableRows: false);

    private const string PageNumber = "<text:page-number text:select-page=\"current\">7</text:page-number>";

    [Fact]
    public void Page_Numbers_Past_The_First_Paragraph_Read_Zero()
    {
        // LibreOffice 24.2 writes "1", "0", "0" for tdf160976_headerFooter.odt's
        // three paragraphs of a page number each: only the first paragraph has
        // been laid out when the text is written.
        Assert.Equal("a 7\nb 0\nc 0",
            Text("<text:p>a " + PageNumber + "</text:p><text:p>b " + PageNumber + "</text:p><text:p>c " + PageNumber + "</text:p>"));
    }

    [Fact]
    public void A_Paragraph_Kept_With_The_First_Is_Laid_Out_With_It()
    {
        // A heading kept with the next paragraph has that paragraph formatted
        // too, so its page number is the page's (soffice: "Head|x 1 y 1|z 0").
        string styles = "<style:style style:name=\"Keep\" style:family=\"paragraph\">" +
            "<style:paragraph-properties fo:keep-with-next=\"always\"/></style:style>";

        Assert.Equal("Head\nx 7\nz 0",
            Text("<text:p text:style-name=\"Keep\">Head</text:p><text:p>x " + PageNumber + "</text:p><text:p>z " + PageNumber + "</text:p>", styles));
    }

    [Fact]
    public void A_Frame_In_The_First_Paragraph_Keeps_The_Saved_Page_Numbers()
    {
        // tdf134951_duplicates.odt: a picture anchored to the first paragraph
        // has LibreOffice lay out the first page to place it, so its fifth
        // paragraph's page number reads "1" (soffice: "a |p1 1|p2 2|p2b 0").
        Assert.Equal("a\nb 7",
            Text("<text:p>a <draw:frame text:anchor-type=\"char\" svg:width=\"1cm\" svg:height=\"1cm\"><draw:text-box><text:p>F</text:p>" +
                "</draw:text-box></draw:frame></text:p><text:p>b " + PageNumber + "</text:p>"));
    }

    [Fact]
    public void A_Frame_Set_As_A_Character_Makes_Its_Paragraph_Not_Empty()
    {
        // tdf164932.fodt: the body's one paragraph holds a frame set as a
        // character, so LibreOffice writes that paragraph, not the frame's text.
        Assert.Equal("",
            Text("<text:p><draw:frame text:anchor-type=\"as-char\" svg:width=\"1cm\" svg:height=\"1cm\"><draw:text-box><text:p>Inside</text:p>" +
                "</draw:text-box></draw:frame></text:p>"));
    }

    [Fact]
    public void An_Unlaid_Page_Number_In_Other_Numerals_Or_On_Another_Page_Reads_Nothing()
    {
        // Page 0 is written only in Arabic numerals, and the next page of a
        // document with no pages yet does not exist (spellout-numberingtypes.odt's
        // French line reads "French: , ,").
        Assert.Equal("first\nr \nn \no ",
            Text("<text:p>first</text:p>" +
                "<text:p>r <text:page-number style:num-format=\"i\">i</text:page-number></text:p>" +
                "<text:p>n <text:page-number text:select-page=\"next\">2</text:page-number></text:p>" +
                "<text:p>o <text:page-number style:num-format=\"1st\">1st</text:page-number></text:p>"));
    }

    [Fact]
    public void Footnotes_Numbered_By_Page_Read_Zero_Past_The_First_Paragraph()
    {
        // footnote-in-2-column-section.fodt: "foo1", "bar0".
        string styles = "<text:notes-configuration text:note-class=\"footnote\" text:start-numbering-at=\"page\"/>";
        string Note(string id, string text) =>
            "<text:note text:id=\"" + id + "\" text:note-class=\"footnote\"><text:note-citation>9</text:note-citation>" +
            "<text:note-body><text:p>" + text + "</text:p></text:note-body></text:note>";

        Assert.Equal("foo1\nbar0", Text("<text:p>foo" + Note("n1", "First") + "</text:p><text:p>bar" + Note("n2", "Second") + "</text:p>", styles));
    }

    [Fact]
    public void Legacy_Form_Fields_Are_Drawn_But_Are_Not_Text()
    {
        RichTextDocument document = Read(
            "<text:p>x<field:fieldmark field:type=\"vnd.oasis.opendocument.field.FORMDROPDOWN\">" +
            "<field:param field:name=\"Dropdown_ListEntry\" field:value=\"2020/2021\"/></field:fieldmark>x</text:p>" +
            "<text:p><field:fieldmark field:type=\"vnd.oasis.opendocument.field.FORMCHECKBOX\"/>box</text:p>");

        Assert.Equal("x2020/2021x", document.Paragraphs[0].Text);
        Assert.Equal("☐box", document.Paragraphs[1].Text);
        Assert.Equal("xx\nbox", DocumentCommands.LabelledText(document));
    }

    [Fact]
    public void A_Form_Control_Is_Not_The_Frame_An_Empty_Body_Exports()
    {
        // checkbox_control.odt: a body of one empty paragraph exports its first
        // text frame, and a control is a drawing object, not a frame.
        string forms = "<office:forms><form:form form:name=\"f\"><form:checkbox form:name=\"c\" form:id=\"control1\" " +
            "form:label=\"Floating Checkbox\"/></form:form></office:forms>";
        RichTextDocument document = Read(
            "<text:p><draw:control text:anchor-type=\"paragraph\" svg:width=\"4cm\" svg:height=\"1cm\" svg:x=\"1cm\" svg:y=\"1cm\" draw:control=\"control1\"/></text:p>",
            forms: forms);

        Assert.Single(document.Shapes);
        Assert.Equal("", DocumentCommands.LabelledText(document));
    }

    [Fact]
    public void A_Page_Variable_Setting_Shows_Nothing()
    {
        // ref_page_get_field.fodt: SwRefPageSetField expands to nothing; only
        // the text:page-variable-get shows its number.
        Assert.Equal("Page Ref: 1",
            Read("<text:p><text:page-variable-set text:page-adjust=\"1\">1</text:page-variable-set>Page Ref: " +
                "<text:page-variable-get text:page-adjust=\"1\">1</text:page-variable-get></text:p>").Paragraphs[0].Text);
    }

    [Fact]
    public void A_Paragraph_A_Field_Hides_Takes_No_Room_But_Keeps_Its_Text()
    {
        // tdf114973.fodt: LibreOffice's text export writes hidden paragraphs.
        RichTextDocument document = Read(
            "<text:p>hidden<text:hidden-paragraph text:condition=\"ooow:TRUE\" text:is-hidden=\"true\"/></text:p><text:p>shown</text:p>");

        Assert.Equal("hidden\nshown", DocumentCommands.LabelledText(document));
        Assert.True(document.Paragraphs[0].MarkStyle.Hidden);
        Assert.All(document.Paragraphs[0].Runs, run => Assert.True(run.Style.Hidden));
        Assert.False(document.Paragraphs[0].Style.PageBreakBefore);

        using var paginated = new DocumentPaginator().Paginate(document, DocumentPaginator.PreferredSetup(document));
        Assert.DoesNotContain(paginated.Layout.Pages[0].Lines, line => line.ParagraphIndex == 0);
    }

    [Fact]
    public void Every_Text_Body_Is_Read()
    {
        // background-image-link.fodt holds two office:text elements; LibreOffice
        // reads the second after the first.
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<office:document " + Namespaces + " office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.text\">" +
            "<office:body><office:text><text:p>one</text:p></office:text><office:text><text:p>two</text:p></office:text></office:body></office:document>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml), writable: false);

        Assert.Equal("one\ntwo", DocumentCommands.LabelledText(new OdtDocumentCodec().Read(stream).Document));
    }

    [Fact]
    public void A_Frame_Whose_Automatic_Style_Has_No_Parent_Is_A_Drawing()
    {
        // tdf167541.fodt: "Draw objects have an automatic style without a parent
        // style" (XMLTextFrameContext), and LibreOffice's export takes only a
        // text frame's text for an empty body; with the frame style's parent
        // "Frame" it is a text frame.
        string Body(string style) =>
            "<office:automatic-styles><style:style style:name=\"gr1\" style:family=\"graphic\"/>" +
            "<style:style style:name=\"fr1\" style:family=\"graphic\" style:parent-style-name=\"Frame\"/></office:automatic-styles>" +
            "<office:body><office:text><text:p><draw:frame draw:style-name=\"" + style + "\" text:anchor-type=\"paragraph\" " +
            "svg:width=\"2cm\" svg:height=\"1cm\"><draw:text-box><text:p>T</text:p></draw:text-box></draw:frame></text:p></office:text></office:body>";
        string Dump(string style)
        {
            string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document " + Namespaces +
                " office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.text\">" + Body(style) + "</office:document>";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml), writable: false);
            return DocumentCommands.LabelledText(new OdtDocumentCodec().Read(stream).Document);
        }

        Assert.Equal("", Dump("gr1"));
        Assert.Equal("T", Dump("fr1"));
    }

    [Fact]
    public void An_Empty_Body_Whose_First_Shape_Is_A_Drawing_Writes_Nothing()
    {
        // IndexingExport_Shapes.odt: the first shape is a circle, so LibreOffice
        // writes nothing, not the text frame after it.
        Assert.Equal("",
            Text("<text:p><draw:custom-shape text:anchor-type=\"paragraph\" svg:width=\"2cm\" svg:height=\"2cm\" svg:x=\"0cm\" svg:y=\"0cm\">" +
                "<text:p>circle</text:p><draw:enhanced-geometry svg:viewBox=\"0 0 21600 21600\" draw:type=\"ellipse\"/></draw:custom-shape>" +
                "<draw:frame text:anchor-type=\"paragraph\" svg:width=\"2cm\" svg:height=\"1cm\"><draw:text-box><text:p>frame</text:p></draw:text-box></draw:frame></text:p>"));
    }

    [Fact]
    public void A_Fixed_Time_Without_A_Data_Style_Shows_Its_Value()
    {
        // tdf111969_field.fodt: soffice writes "–12:34:56–" for an empty field.
        Assert.Equal("–12:34:56–\na15:04:05b",
            Text("<text:p>–<text:time text:time-value=\"1899-12-30T12:34:56\" text:fixed=\"true\"/>–</text:p>" +
                "<text:p>a<text:time text:time-value=\"PT15H04M05S\" text:fixed=\"true\">cached</text:time>b</text:p>"));
    }

    [Fact]
    public void Sender_Initials_In_LibreOffices_Namespace_Are_A_Field()
    {
        // sender-initials.fodt: loext:sender-initials, written 2013-2017.
        Assert.Equal("I",
            Text("<text:p><loext:sender-initials>I</loext:sender-initials></text:p>"));
    }

    [Fact]
    public void A_Dimension_Lines_Words_Are_Not_Its_Paragraphs()
    {
        // fdo56272.odt: the heading reads "a", not "13,09mma".
        Assert.Equal("a",
            Text("<text:p><draw:measure text:anchor-type=\"paragraph\" svg:x1=\"0cm\" svg:y1=\"0cm\" svg:x2=\"1cm\" svg:y2=\"0cm\">" +
                "<text:p><text:measure text:kind=\"value\">13,09</text:measure><text:measure text:kind=\"unit\">mm</text:measure></text:p></draw:measure>a</text:p>"));
    }

    [Fact]
    public void A_Custom_Shape_Styled_From_Frame_Holds_A_Text_Frame()
    {
        // fdo82860.odt: a custom shape whose automatic style derives from
        // "Frame" has its words written for an empty body; one with a style of
        // no parent does not.
        string Dump(string parent)
        {
            string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document " + Namespaces +
                " office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.text\">" +
                "<office:automatic-styles><style:style style:name=\"gr1\" style:family=\"graphic\"" + parent + "/></office:automatic-styles>" +
                "<office:body><office:text><text:p><draw:custom-shape draw:style-name=\"gr1\" text:anchor-type=\"paragraph\" " +
                "svg:width=\"2cm\" svg:height=\"2cm\" svg:x=\"0cm\" svg:y=\"0cm\"><text:p>hello</text:p>" +
                "<draw:enhanced-geometry svg:viewBox=\"0 0 21600 21600\" draw:type=\"rectangle\"/></draw:custom-shape></text:p></office:text></office:body></office:document>";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml), writable: false);
            return DocumentCommands.LabelledText(new OdtDocumentCodec().Read(stream).Document);
        }

        Assert.Equal("hello", Dump(" style:parent-style-name=\"Frame\""));
        Assert.Equal("", Dump(""));
    }

    [Fact]
    public void A_Space_After_A_Frame_Makes_The_Body_Paragraph_Not_Empty()
    {
        // shape-textbox.odt and z-index-header.odt: "<p> <frame/> </p>" keeps
        // the space after the frame, so LibreOffice writes the paragraph, not
        // the frame.
        string frame = "<draw:frame text:anchor-type=\"paragraph\" svg:width=\"1cm\" svg:height=\"1cm\"><draw:text-box><text:p>F</text:p></draw:text-box></draw:frame>";
        Assert.Equal(" ", Text("<text:p> " + frame + " </text:p>"));
        Assert.Equal("F", Text("<text:p> " + frame + "</text:p>"));
    }

    [Fact]
    public void A_Text_Frame_In_The_Header_Comes_Before_The_Bodys()
    {
        // user-field-decl-fly.odt: the header's frame is made first, as the
        // styles are read before the body, and it is the one written.
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document " + Namespaces +
            " office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.text\">" +
            "<office:automatic-styles><style:page-layout style:name=\"pm1\"><style:page-layout-properties fo:page-width=\"21cm\" fo:page-height=\"29.7cm\"/>" +
            "<style:header-style/></style:page-layout></office:automatic-styles>" +
            "<office:master-styles><style:master-page style:name=\"Standard\" style:page-layout-name=\"pm1\"><style:header><text:p>h" +
            "<draw:frame text:anchor-type=\"char\" svg:width=\"2cm\" svg:height=\"1cm\"><draw:text-box><text:p>header frame</text:p></draw:text-box></draw:frame>" +
            "</text:p></style:header></style:master-page></office:master-styles>" +
            "<office:body><office:text><text:p><draw:frame text:anchor-type=\"paragraph\" svg:width=\"2cm\" svg:height=\"1cm\"><draw:text-box>" +
            "<text:p>body frame</text:p></draw:text-box></draw:frame></text:p></office:text></office:body></office:document>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml), writable: false);

        Assert.Equal("header frame", DocumentCommands.LabelledText(new OdtDocumentCodec().Read(stream).Document));
    }

    private const string Outline =
        "<text:outline-style style:name=\"Outline\">" +
        "<text:outline-level-style text:level=\"1\" style:num-suffix=\".\" style:num-format=\"1\"/></text:outline-style>";

    [Fact]
    public void A_Heading_Whose_Style_Empties_Its_List_Style_Is_Not_Numbered()
    {
        // tdf106541_noinheritChapterNumbering.odt: a style inheriting from
        // Heading 1 with style:list-style-name="" leaves the outline.
        string styles = Outline +
            "<style:style style:name=\"Heading_20_1\" style:family=\"paragraph\" style:default-outline-level=\"1\"/>" +
            "<style:style style:name=\"Child\" style:family=\"paragraph\" style:parent-style-name=\"Heading_20_1\" style:list-style-name=\"\"/>" +
            "<style:style style:name=\"Grandchild\" style:family=\"paragraph\" style:parent-style-name=\"Child\"/>";

        Assert.Equal("1. Heading\nInherits\nInherits further\n2. Heading again",
            Text("<text:h text:style-name=\"Heading_20_1\" text:outline-level=\"1\">Heading</text:h>" +
                "<text:h text:style-name=\"Child\" text:outline-level=\"1\">Inherits</text:h>" +
                "<text:h text:style-name=\"Grandchild\" text:outline-level=\"1\">Inherits further</text:h>" +
                "<text:h text:style-name=\"Heading_20_1\" text:outline-level=\"1\">Heading again</text:h>", styles));
    }

    private static string ListText(string levels, int level = 1)
    {
        string styles = "<text:list-style style:name=\"L\">" + levels + "</text:list-style>";
        string item = "<text:p>A</text:p>";
        for (int i = 1; i < level; i++)
            item = "<text:list><text:list-item>" + item + "</text:list-item></text:list>";
        return Text("<text:list text:style-name=\"L\"><text:list-item>" + item + "</text:list-item></text:list>", styles);
    }

    [Theory]
    [InlineData("01, 02, 03, ...", "01. A")]
    [InlineData("001, 002, 003, ...", "001. A")]
    [InlineData("00001, 00002, 00003, ...", "00001. A")]
    [InlineData("一, 二, 三, ...", "一. A")]
    [InlineData("א, י, ק, ...", "א. A")]
    [InlineData("①, ②, ③, ...", "①. A")]
    [InlineData("Α, Β, ... (gr)", "Α. A")]
    [InlineData("А, Б, .., Аа, Аб, ... (bg)", "А. A")]
    [InlineData("ก, ข, ค, ...", "ก. A")]
    public void A_Level_Numbered_In_A_LibreOffice_Numbering_Type_Writes_It(string format, string expected) =>
        Assert.Equal(expected, ListText("<text:list-level-style-number text:level=\"1\" style:num-suffix=\".\" style:num-format=\"" + format + "\"/>"));

    [Fact]
    public void A_List_Format_States_The_Whole_Label()
    {
        // tdf156105_percentSuffix.odt: "(%)%1%[%]" is drawn as it stands, the
        // "%" that marks no level kept.
        Assert.Equal("(%)1[%] A", ListText(
            "<text:list-level-style-number text:level=\"1\" loext:num-list-format=\"(%)%1%[%]\" style:num-prefix=\"(\" " +
            "style:num-suffix=\"]\" style:num-format=\"1\"/>"));
    }

    [Fact]
    public void A_Legal_Level_Writes_The_Levels_Above_In_Arabic_Numerals()
    {
        // IsLegal.fodt: "Sect %1%.%2%" over "I" and "01" reads "Sect 1.01".
        Assert.Equal("Sect 1.01 A", ListText(
            "<text:list-level-style-number text:level=\"1\" loext:num-list-format=\"CH %1%\" style:num-prefix=\"CH \" style:num-format=\"I\"/>" +
            "<text:list-level-style-number text:level=\"2\" loext:is-legal=\"true\" loext:num-list-format=\"Sect %1%.%2%\" " +
            "style:num-prefix=\"Sect \" style:num-format=\"01, 02, 03, ...\" text:display-levels=\"2\"/>", level: 2));
    }
}
