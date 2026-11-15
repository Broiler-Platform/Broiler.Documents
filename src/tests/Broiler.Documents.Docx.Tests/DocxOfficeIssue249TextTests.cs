namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// DOCX samples whose text LibreOffice 24.2's text export writes differently
/// (office issue #249, docx-text): paragraph marks in a reference field's
/// result, and the like.
/// </summary>
public sealed class DocxOfficeIssue249TextTests
{
    [Fact]
    public void A_Paragraph_Mark_In_A_Reference_Fields_Result_Is_Not_A_Paragraph()
    {
        // line-break-in-ref.docx: the REF's saved result ends with a paragraph
        // mark, and LibreOffice 24.2 writes "Text1Text2" on one line.
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:fldChar w:fldCharType='begin'/></w:r>" +
            "<w:r><w:instrText xml:space='preserve'> REF BM1 \\*Mergeformat</w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType='separate'/></w:r><w:r><w:t>Text1</w:t></w:r></w:p>" +
            "<w:p><w:r><w:fldChar w:fldCharType='end'/></w:r><w:r><w:t>Text2</w:t></w:r></w:p>" +
            "<w:bookmarkStart w:id='1' w:name='BM1'/><w:p><w:r><w:t>Text1</w:t></w:r></w:p><w:bookmarkEnd w:id='1'/>");

        Assert.Equal(["Text1Text2", "Text1"], result.Document.Paragraphs.Select(paragraph => paragraph.Text));
    }

    [Fact]
    public void A_Reference_Fields_Result_In_A_Cell_Is_One_Paragraph_Of_The_Cell()
    {
        // tdf171383_refFieldInCell.docx: "Issue 1Input text here." in one line.
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:tbl><w:tr><w:tc>" +
            "<w:p><w:r><w:fldChar w:fldCharType='begin'/></w:r><w:r><w:instrText> REF  Issue1 </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType='separate'/></w:r><w:r><w:t>Issue 1</w:t></w:r></w:p>" +
            "<w:p/><w:p><w:r><w:t>Input text here.</w:t></w:r></w:p>" +
            "<w:p><w:r><w:fldChar w:fldCharType='end'/></w:r></w:p>" +
            "</w:tc><w:tc><w:p><w:r><w:t>next</w:t></w:r></w:p></w:tc></w:tr></w:tbl><w:p/>");

        Assert.Equal(["Issue 1Input text here.", "next", ""], result.Document.Paragraphs.Select(paragraph => paragraph.Text));
        TableCell cell = result.Document.Tables[0].Rows[0].Cells[0];
        Assert.Equal(1, cell.ParagraphCount);
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

    private const string FirstStore = "{4E8A9591-F074-446B-902F-511FF79C122F}";
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
