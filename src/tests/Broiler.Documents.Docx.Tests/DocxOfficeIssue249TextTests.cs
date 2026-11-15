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
}
