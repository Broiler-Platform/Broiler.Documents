namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// What a template keeps off the printed page, and what it prints that is not
/// text: a frame whose style says <c>style:print-content="false"</c>, a form
/// label, and a hidden section in a footer.
/// </summary>
/// <remarks>
/// <c>fdo114306_2.odt</c> has all three: a crest and a heading frame the printed
/// company paper already carries, editing hints in non-printing form labels, a
/// "Разослать:" label that does print, and a file-name footer in a hidden section.
/// </remarks>
public sealed class OdtNonPrintingTests
{
    private const string FormNamespace = "urn:oasis:names:tc:opendocument:xmlns:form:1.0";

    private static RichTextDocument RoundTrip(RichTextDocument document)
    {
        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(document), writable: false);
        return new OdtDocumentCodec().Read(stream).Document;
    }

    private static string Frame(string style) =>
        "<text:p><draw:frame draw:style-name=\"" + style + "\" text:anchor-type=\"as-char\" svg:width=\"100pt\" svg:height=\"40pt\">" +
        "<draw:text-box><text:p>heading</text:p></draw:text-box></draw:frame></text:p>";

    [Fact]
    public void A_Frame_Whose_Style_Keeps_It_Off_The_Page_Is_Not_Printable()
    {
        string styles =
            "<style:style style:name=\"hint\" style:family=\"graphic\"><style:graphic-properties style:print-content=\"false\"/></style:style>" +
            "<style:style style:name=\"plain\" style:family=\"graphic\"><style:graphic-properties/></style:style>";

        RichTextDocument document = OdtTestPackage.ReadBody(Frame("hint") + Frame("plain"), styles).Document;

        Assert.Equal([false, true], document.Shapes.Select(shape => shape.Printable));
        Assert.Equal("heading", Assert.Single(document.Shapes[0].Paragraphs).Text);
    }

    [Fact]
    public void A_Floating_Picture_Whose_Style_Keeps_It_Off_The_Page_Is_Not_Printable()
    {
        string styles = "<style:style style:name=\"crest\" style:family=\"graphic\"><style:graphic-properties style:print-content=\"false\"/></style:style>";
        string body = "<text:p><draw:frame draw:style-name=\"crest\" text:anchor-type=\"char\" svg:width=\"20pt\" svg:height=\"20pt\">" +
            "<draw:image xlink:href=\"Pictures/a.png\"/></draw:frame>text</text:p>";

        RichTextDocument document = OdtTestPackage.ReadWithPictures(body, new Dictionary<string, byte[]>
        {
            ["Pictures/a.png"] = OdtTestPackage.OnePixelPng,
        }, options: new DocumentReadOptions(resourcePolicy: DocumentResourcePolicy.AllowOwnDocuments),
            automaticStylesXml: styles).Document;

        DocumentShape crest = Assert.Single(document.Shapes);
        Assert.True(crest.HasImage);
        Assert.False(crest.Printable);
    }

    [Fact]
    public void A_Non_Printing_Shape_Round_Trips()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("body").WithShapes(
        [
            new DocumentShape(0, 0, 0, 100, 40, paragraphs: [RichTextParagraph.Plain("hint")], printable: false),
            new DocumentShape(0, 0, 50, 100, 40, paragraphs: [RichTextParagraph.Plain("kept")]),
        ]);

        Assert.Equal([false, true], RoundTrip(document).Shapes.Select(shape => shape.Printable));
    }

    private static string Forms(string controls) =>
        "<office:forms xmlns:form=\"" + FormNamespace + "\"><form:form form:name=\"Standard\">" + controls + "</form:form></office:forms>";

    private static string Control(string id, string style = "") =>
        "<draw:control text:anchor-type=\"char\" draw:text-style-name=\"label\"" + style +
        " svg:width=\"68pt\" svg:height=\"14pt\" svg:x=\"5pt\" svg:y=\"2pt\" draw:control=\"" + id + "\"/>";

    [Fact]
    public void A_Form_Label_Is_A_Box_Holding_Its_Words_In_Its_Text_Style()
    {
        string body = Forms(
                "<form:fixed-text form:name=\"Distribution\" form:id=\"c1\" form:label=\"Разослать:\" form:multi-line=\"true\"/>" +
                "<form:fixed-text form:name=\"Hint\" form:id=\"c2\" form:label=\"Заголовок\" form:printable=\"false\"/>") +
            "<text:p>" + Control("c1") + Control("c2") + "list</text:p>";
        string styles = "<style:style style:name=\"label\" style:family=\"paragraph\"><style:text-properties fo:font-size=\"15pt\"/></style:style>";

        RichTextDocument document = OdtTestPackage.ReadBody(body, styles).Document;

        Assert.Equal(2, document.Shapes.Count);
        DocumentShape distribution = document.Shapes[0];
        Assert.Equal("Разослать:", Assert.Single(distribution.Paragraphs).Text);
        Assert.Equal(15f, distribution.Paragraphs[0].StyleAt(0).FontSize);
        Assert.Equal((5, 2, 68, 14), (distribution.OffsetX, distribution.OffsetY, distribution.Width, distribution.Height));
        Assert.True(distribution.Printable);
        Assert.Null(distribution.Fill);
        Assert.False(distribution.GrowsWidthToText);

        DocumentShape hint = document.Shapes[1];
        Assert.False(hint.Printable);
        Assert.True(hint.GrowsWidthToText);
        Assert.Equal("list", document.Paragraphs[0].Text);
    }

    [Fact]
    public void Controls_Other_Than_Labels_Are_Left_Out()
    {
        string body = Forms("<form:text form:name=\"Field\" form:id=\"c1\" form:current-value=\"typed\"/>") +
            "<text:p>" + Control("c1") + "body</text:p>";

        RichTextDocument document = OdtTestPackage.ReadBody(body).Document;

        Assert.Empty(document.Shapes);
        Assert.Equal("body", document.Paragraphs[0].Text);
    }

    [Fact]
    public void A_Hidden_Section_In_A_Footer_Is_Not_Part_Of_It()
    {
        string styles =
            "<office:automatic-styles><style:page-layout style:name=\"a\"><style:page-layout-properties fo:page-width=\"200pt\" fo:page-height=\"300pt\"/></style:page-layout></office:automatic-styles>" +
            "<office:master-styles><style:master-page style:name=\"Standard\" style:page-layout-name=\"a\"><style:footer>" +
            "<text:section text:name=\"FileName\" text:display=\"none\"><text:p>C:\\Temp\\51661523.ODT</text:p></text:section>" +
            "<text:section text:name=\"Shown\"><text:p>shown</text:p></text:section>" +
            "</style:footer></style:master-page></office:master-styles>";

        RichTextDocument document = OdtTestPackage.ReadStyled("<text:p>body</text:p>", styles).Document;

        Assert.Equal("shown", Assert.Single(document.RunningContent.Footer(PageSelection.Default)).Text);
    }

    [Fact]
    public void A_Footer_Holding_Only_A_Hidden_Section_Is_Empty()
    {
        string styles =
            "<office:automatic-styles><style:page-layout style:name=\"a\"><style:page-layout-properties fo:page-width=\"200pt\" fo:page-height=\"300pt\"/></style:page-layout></office:automatic-styles>" +
            "<office:master-styles><style:master-page style:name=\"Standard\" style:page-layout-name=\"a\"><style:footer>" +
            "<text:section text:name=\"FileName\" text:display=\"none\"><text:p>C:\\Temp\\51661523.ODT</text:p></text:section>" +
            "</style:footer></style:master-page></office:master-styles>";

        RichTextDocument document = OdtTestPackage.ReadStyled("<text:p>body</text:p>", styles).Document;

        Assert.Empty(document.RunningContent.Footer(PageSelection.Default));
    }

    private static RichTextDocument WithGenerator(string generator, string body, string styles)
    {
        string meta = "<office:document-meta " + OdtTestPackage.NamespaceDeclarations +
            " xmlns:meta=\"urn:oasis:names:tc:opendocument:xmlns:meta:1.0\" office:version=\"1.3\"><office:meta>" +
            "<meta:generator>" + generator + "</meta:generator></office:meta></office:document-meta>";
        using var stream = new MemoryStream(OdtTestPackage.FromBody(body, styles,
            extraParts: new Dictionary<string, string> { ["meta.xml"] = meta }));
        return new OdtDocumentCodec().Read(stream).Document;
    }

    [Theory]
    [InlineData("OpenOffice.org/2.2$Win32 OpenOffice.org_project/680m14$Build-9134", true)]
    [InlineData("OpenOffice.org/3.2$Win32 OpenOffice.org_project/320m12$Build-9483", false)]
    [InlineData("LibreOffice/24.2.7.2$Linux_X86_64 LibreOffice_project/ee3885777aa7032db5a9b65deec9457448a91162", false)]
    public void OpenOffice_2_States_Text_Display_The_Wrong_Way_Round(string generator, bool inverted)
    {
        // LibreOffice reads text:display inverted in a document OpenOffice.org
        // 2.x wrote; fdo114306_2.odt's "Скрытый текст" (hidden text) style says
        // "true" and is not printed.
        string styles =
            "<style:style style:name=\"shown\" style:family=\"text\"><style:text-properties text:display=\"true\"/></style:style>" +
            "<style:style style:name=\"none\" style:family=\"text\"><style:text-properties text:display=\"none\"/></style:style>";
        RichTextDocument document = WithGenerator(generator,
            "<text:p><text:span text:style-name=\"shown\">a</text:span><text:span text:style-name=\"none\">b</text:span></text:p>", styles);

        RichTextParagraph paragraph = document.Paragraphs[0];
        Assert.Equal(inverted, paragraph.StyleAt(0).Hidden);
        Assert.Equal(!inverted, paragraph.StyleAt(1).Hidden);
    }
}
