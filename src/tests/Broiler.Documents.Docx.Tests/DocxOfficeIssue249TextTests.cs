namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// DOCX samples whose text LibreOffice 24.2's text export writes differently
/// (office issue #249, docx-text): paragraph marks in a reference field's
/// result, and the like.
/// </summary>
public sealed class DocxOfficeIssue249TextTests
{
    [Fact]
    public void A_Reference_Fields_Result_Moves_To_Where_The_Field_Ends()
    {
        // line-break-in-ref.docx: the REF's saved result ends with a paragraph
        // mark. LibreOffice 24.2 keeps the mark and shows the result where the
        // field ends: an empty paragraph, then "Text1Text2" (its flat ODF export).
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:fldChar w:fldCharType='begin'/></w:r>" +
            "<w:r><w:instrText xml:space='preserve'> REF BM1 \\*Mergeformat</w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType='separate'/></w:r><w:r><w:t>Text1</w:t></w:r></w:p>" +
            "<w:p><w:r><w:fldChar w:fldCharType='end'/></w:r><w:r><w:t>Text2</w:t></w:r></w:p>" +
            "<w:bookmarkStart w:id='1' w:name='BM1'/><w:p><w:r><w:t>Text1</w:t></w:r></w:p><w:bookmarkEnd w:id='1'/>");

        Assert.Equal(["", "Text1Text2", "Text1"], result.Document.Paragraphs.Select(paragraph => paragraph.Text));
    }

    [Fact]
    public void A_Reference_Fields_Result_In_A_Cell_Moves_To_The_Cells_Paragraph_Where_It_Ends()
    {
        // tdf171383_refFieldInCell.docx: three empty paragraphs, then
        // "Issue 1Input text here.", in LibreOffice 24.2.
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:tbl><w:tr><w:tc>" +
            "<w:p><w:r><w:fldChar w:fldCharType='begin'/></w:r><w:r><w:instrText> REF  Issue1 </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType='separate'/></w:r><w:r><w:t>Issue 1</w:t></w:r></w:p>" +
            "<w:p/><w:p><w:r><w:t>Input text here.</w:t></w:r></w:p>" +
            "<w:p><w:r><w:fldChar w:fldCharType='end'/></w:r></w:p>" +
            "</w:tc><w:tc><w:p><w:r><w:t>next</w:t></w:r></w:p></w:tc></w:tr></w:tbl><w:p/>");

        Assert.Equal(["", "", "", "Issue 1Input text here.", "next", ""], result.Document.Paragraphs.Select(paragraph => paragraph.Text));
        TableCell cell = result.Document.Tables[0].Rows[0].Cells[0];
        Assert.Equal(4, cell.ParagraphCount);
    }

    [Fact]
    public void A_Reference_Fields_Result_That_Runs_Into_A_Table_Keeps_Its_Paragraphs()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:fldChar w:fldCharType='begin'/></w:r><w:r><w:instrText> REF ID0 \\h </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType='separate'/></w:r></w:p>" +
            "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>ID</w:t></w:r></w:p></w:tc></w:tr></w:tbl>" +
            "<w:p><w:r><w:fldChar w:fldCharType='end'/></w:r></w:p>");

        Assert.Single(result.Document.Tables);
        Assert.Equal(["", "ID", ""], result.Document.Paragraphs.Select(paragraph => paragraph.Text));
    }

    [Fact]
    public void An_Ask_Field_Shows_None_Of_Its_Saved_Result()
    {
        // n751017.docx: "AbarBbarC" saved, "ABbarC" in LibreOffice 24.2.
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:t>A</w:t></w:r><w:r><w:fldChar w:fldCharType='begin'/></w:r>" +
            "<w:r><w:instrText xml:space='preserve'> ASK foo \"What is foo?\" \\* CHARFORMAT </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType='separate'/></w:r><w:r><w:t>bar</w:t></w:r><w:r><w:fldChar w:fldCharType='end'/></w:r>" +
            "<w:r><w:t>B</w:t></w:r></w:p>");

        Assert.Equal("AB", Assert.Single(result.Document.Paragraphs).Text);
    }

    private const string Wps ="http://schemas.microsoft.com/office/word/2010/wordprocessingShape";

    private static string ShapeXml(bool textBox) =>
        "<wps:wsp xmlns:wps=\"" + Wps + "\"><wps:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"914400\" cy=\"457200\"/></a:xfrm><a:solidFill><a:srgbClr val=\"AECF00\"/></a:solidFill></wps:spPr>" +
        (textBox ? "<wps:txbx><w:txbxContent><w:p><w:r><w:t>text</w:t></w:r></w:p></w:txbxContent></wps:txbx>" : "") +
        "<wps:bodyPr/></wps:wsp>";

    private static string Drawing(string wrapper, string graphicData, string uri = Wps) =>
        "<w:r><w:drawing><wp:" + wrapper + " xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\">" +
        (wrapper == "anchor" ? "<wp:positionH relativeFrom=\"column\"><wp:posOffset>0</wp:posOffset></wp:positionH>" +
            "<wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>0</wp:posOffset></wp:positionV>" : "") +
        "<wp:extent cx=\"914400\" cy=\"457200\"/>" +
        "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:graphicData uri=\"" + uri + "\">" +
        graphicData + "</a:graphicData></a:graphic></wp:" + wrapper + "></w:drawing></w:r>";

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_Shape_Without_A_Text_Box_Is_A_Drawing(bool textBox, bool drawing)
    {
        // tdf117028.docx: LibreOffice imports a wps shape without wps:txbx as a
        // drawing, and one with a text box with a text frame attached.
        DocumentShape shape = Assert.Single(DocxTestPackage.ReadBody(
            "<w:p xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" + Drawing("anchor", ShapeXml(textBox)) + "</w:p>").Document.Shapes);

        Assert.Equal(drawing, shape.IsCustomShape);
        Assert.False(shape.InlineInSource);
        Assert.Equal(0, shape.GroupNumber);
    }

    [Fact]
    public void An_Inline_Shape_Is_A_Character_Of_Its_Paragraph_In_The_Source()
    {
        // inline-textbox-position.docx: a wp:inline text box.
        DocumentShape shape = Assert.Single(DocxTestPackage.ReadBody(
            "<w:p xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" + Drawing("inline", ShapeXml(true)) + "</w:p>").Document.Shapes);

        Assert.True(shape.InlineInSource);
        Assert.False(shape.IsAsCharacter);
    }

    [Fact]
    public void The_Members_Of_A_Group_Share_Its_Number()
    {
        string group = "<wpg:wgp xmlns:wpg=\"http://schemas.microsoft.com/office/word/2010/wordprocessingGroup\"><wpg:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"914400\" cy=\"457200\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"914400\" cy=\"457200\"/></a:xfrm></wpg:grpSpPr>" +
            ShapeXml(false) + ShapeXml(true) + "</wpg:wgp>";
        RichTextDocument document = DocxTestPackage.ReadBody(
            "<w:p xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
            Drawing("anchor", group, "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup") +
            Drawing("anchor", ShapeXml(true)) + "</w:p>").Document;

        Assert.Equal([1, 1, 0], document.Shapes.Select(shape => shape.GroupNumber));
        Assert.Equal([true, false, false], document.Shapes.Select(shape => shape.IsCustomShape));
    }

    private const string FirstStore ="{4E8A9591-F074-446B-902F-511FF79C122F}";
    private const string SecondStore = "{8E0C1334-910E-4E16-9074-50205539AF1C}";

    private static string BoundControl(string? storeItemId, string xpath, string prefixes = "xmlns:f='urn:first'") =>
        "<w:p><w:r><w:t xml:space='preserve'>Placeholder -&gt; </w:t></w:r><w:sdt><w:sdtPr>" +
        "<w:dataBinding w:prefixMappings=\"" + prefixes + "\" w:xpath=\"" + xpath + "\"" +
        (storeItemId is null ? "" : " w:storeItemID=\"" + storeItemId + "\"") + "/><w:text/></w:sdtPr>" +
        "<w:sdtContent><w:r><w:t>PLACEHOLDER IN DOC</w:t></w:r><w:proofErr w:type='spellEnd'/></w:sdtContent></w:sdt></w:p>";

    private static Dictionary<string, string> Stores() => new(StringComparer.Ordinal)
    {
        ["customXml/item1.xml"] = "<f:fields xmlns:f='urn:first'><f:record><f:field ref='other' text='no'/>" +
            "<f:field ref='objsubject' text='ABC'/><f:field ref='objsubject' text='Betreff'/></f:record></f:fields>",
        ["customXml/itemProps1.xml"] = "<ds:datastoreItem xmlns:ds='http://schemas.openxmlformats.org/officeDocument/2006/customXml' ds:itemID='" + FirstStore + "'/>",
        ["customXml/item2.xml"] = "<f:fields xmlns:f='urn:second'><f:record><f:field ref='objsubject' text='HERUNTERLADEN'/></f:record></f:fields>",
        ["customXml/itemProps2.xml"] = "<ds:datastoreItem xmlns:ds='http://schemas.openxmlformats.org/officeDocument/2006/customXml' ds:itemID='" + SecondStore + "'/>",
        ["docProps/app.xml"] = "<Properties xmlns='http://schemas.openxmlformats.org/officeDocument/2006/extended-properties'><Company>Extended Value</Company></Properties>",
    };

    private const string FieldPath = "/*[name()='f:fields']/*[name()='f:record']/*[name()='f:field' and @ref='objsubject']/@text";

    [Fact]
    public void A_Control_Bound_To_A_Custom_Xml_Store_Shows_The_Stores_Value()
    {
        // customxml-two-data-stores.docx: one path, two stores. name() is the
        // name the store spells, so f:fields matches the second store's root
        // although the binding maps f to the first store's namespace.
        using var stream = new MemoryStream(DocxTestPackage.FromBody(
            BoundControl(FirstStore, FieldPath) + BoundControl(SecondStore, FieldPath), Stores()), writable: false);
        RichTextDocument document = new DocxDocumentCodec().Read(stream).Document;

        Assert.Equal(["Placeholder -> ABC", "Placeholder -> HERUNTERLADEN"], document.Paragraphs.Select(paragraph => paragraph.Text));
    }

    [Fact]
    public void A_Control_Naming_No_Store_Takes_The_First_Store_The_Path_Matches()
    {
        // tdf147724.docx's second control states no w:storeItemID.
        using var stream = new MemoryStream(DocxTestPackage.FromBody(BoundControl(null, FieldPath), Stores()), writable: false);
        RichTextDocument document = new DocxDocumentCodec().Read(stream).Document;

        Assert.Equal("Placeholder -> ABC", Assert.Single(document.Paragraphs).Text);
    }

    [Theory]
    [InlineData("/ns0:Properties[1]/ns0:Company[1]", "Placeholder -> Extended Value")]
    [InlineData("/ns0:Properties[1]/ns0:Manager[1]", "Placeholder -> PLACEHOLDER IN DOC")]
    [InlineData("//ns0:Company", "Placeholder -> PLACEHOLDER IN DOC")]
    public void A_Control_Bound_To_The_Extended_Properties_Shows_Their_Value(string xpath, string expected)
    {
        // tdf104823.docx: "True Extended Property Value" over a saved "INVALID
        // VALUE". A path that selects nothing, or one that is not a plain
        // child path, keeps the saved text.
        using var stream = new MemoryStream(DocxTestPackage.FromBody(
            BoundControl("{6668398D-A668-4E3E-A5EB-62B293D839F1}", xpath,
                "xmlns:ns0='http://schemas.openxmlformats.org/officeDocument/2006/extended-properties'"), Stores()), writable: false);
        RichTextDocument document = new DocxDocumentCodec().Read(stream).Document;

        Assert.Equal(expected, Assert.Single(document.Paragraphs).Text);
    }
}
