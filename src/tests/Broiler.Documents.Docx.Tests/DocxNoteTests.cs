using System.IO.Compression;
using System.Text;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Footnotes and endnotes: a <c>w:footnoteReference</c> in the body naming a
/// <c>w:footnote</c> in the part the document's relationships point to. They
/// used to be read as nothing; LibreOffice's <c>sw/qa/extras</c> tree holds 61
/// DOCX documents that cite one.
/// </summary>
public sealed class DocxNoteTests
{
    private const string Relationships =
        "<Relationship Id=\"rIdF\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes\" Target=\"footnotes.xml\"/>" +
        "<Relationship Id=\"rIdE\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/endnotes\" Target=\"endnotes.xml\"/>";

    private static string NotesPart(string element, string notesXml) =>
        "<w:" + element + "s " + DocxTestPackage.BodyNamespaceDeclarations + ">" +
        "<w:" + element + " w:type=\"separator\" w:id=\"-1\"><w:p><w:r><w:separator/></w:r></w:p></w:" + element + ">" +
        "<w:" + element + " w:type=\"continuationSeparator\" w:id=\"0\"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:" + element + ">" +
        notesXml +
        "</w:" + element + "s>";

    /// <summary>A note as Word writes one: its label repeated, a space, then the text.</summary>
    private static string Note(string element, int id, string text, string labelProperties = "<w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr>") =>
        "<w:" + element + " w:id=\"" + id + "\"><w:p>" +
        "<w:r>" + labelProperties + "<w:" + element + "Ref/></w:r>" +
        "<w:r><w:t xml:space=\"preserve\"> " + text + "</w:t></w:r>" +
        "</w:p></w:" + element + ">";

    private static DocumentReadResult Read(
        string bodyXml,
        string footnotesXml = "",
        string endnotesXml = "",
        string? settingsXml = null)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/_rels/document.xml.rels"] =
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                Relationships +
                (settingsXml is null
                    ? string.Empty
                    : "<Relationship Id=\"rIdS\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>") +
                "</Relationships>",
            ["word/footnotes.xml"] = NotesPart("footnote", footnotesXml),
            ["word/endnotes.xml"] = NotesPart("endnote", endnotesXml),
        };
        if (settingsXml is not null)
            parts["word/settings.xml"] = "<w:settings " + DocxTestPackage.BodyNamespaceDeclarations + ">" + settingsXml + "</w:settings>";

        using var stream = new MemoryStream(DocxTestPackage.FromBody(bodyXml, parts), writable: false);
        return new DocxDocumentCodec().Read(stream);
    }

    private static string Reference(string element, int id, string extra = "") =>
        "<w:r><w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr><w:" + element + "Reference w:id=\"" + id + "\"" + extra + "/></w:r>";

    [Fact]
    public void A_Footnote_Reference_Is_A_Citation_Holding_The_Note()
    {
        DocumentReadResult result = Read(
            "<w:p><w:r><w:t>text</w:t></w:r>" + Reference("footnote", 1) + "<w:r><w:t xml:space=\"preserve\"> after</w:t></w:r></w:p>",
            Note("footnote", 1, "The note."));

        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("text" + DocumentNote.PlaceholderText + " after", paragraph.Text);
        DocumentNote note = Assert.Single(result.Document.Notes);
        Assert.Same(note, paragraph.StyleAt(4).Note);
        Assert.Equal(NoteKind.Footnote, note.Kind);

        // The repeated label and the space after it are drawn from the citation.
        Assert.Equal("The note.", note.PlainText);
    }

    [Fact]
    public void An_Endnote_Reference_Names_A_Note_In_The_Endnotes_Part()
    {
        DocumentReadResult result = Read(
            "<w:p><w:r><w:t>x</w:t></w:r>" + Reference("endnote", 2) + "</w:p>",
            endnotesXml: Note("endnote", 2, "At the end."));

        DocumentNote note = Assert.Single(result.Document.Notes);
        Assert.Equal(NoteKind.Endnote, note.Kind);
        Assert.Equal("At the end.", note.PlainText);
    }

    [Fact]
    public void A_Custom_Mark_Is_The_Text_After_The_Reference_And_Not_Body_Text()
    {
        DocumentReadResult result = Read(
            "<w:p><w:r><w:t>x</w:t></w:r><w:r><w:footnoteReference w:customMarkFollows=\"1\" w:id=\"1\"/><w:t>*</w:t></w:r></w:p>",
            Note("footnote", 1, "Starred."));

        Assert.Equal("x" + DocumentNote.PlaceholderText, result.Document.Paragraphs[0].Text);
        Assert.Equal("*", Assert.Single(result.Document.Notes).CustomMark);
    }

    [Fact]
    public void A_Reference_To_A_Note_The_Part_Lacks_Is_Reported_And_Dropped()
    {
        DocumentReadResult result = Read("<w:p><w:r><w:t>x</w:t></w:r>" + Reference("footnote", 9) + "</w:p>");

        Assert.Equal("x", result.Document.Paragraphs[0].Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.note.missing");
    }

    [Fact]
    public void A_Superscript_Label_At_The_Head_Of_A_Note_Is_Read_As_Word_Sets_It()
    {
        DocumentReadResult raised = Read(
            "<w:p>" + Reference("footnote", 1) + "</w:p>", Note("footnote", 1, "n"));
        DocumentReadResult level = Read(
            "<w:p>" + Reference("footnote", 1) + "</w:p>", Note("footnote", 1, "n", labelProperties: string.Empty));

        Assert.True(raised.Document.NoteNumbering.RaisedLabels);
        Assert.False(level.Document.NoteNumbering.RaisedLabels);
    }

    [Fact]
    public void The_Settings_Part_Says_How_Notes_Are_Numbered()
    {
        DocumentReadResult result = Read(
            "<w:p>" + Reference("footnote", 1) + "</w:p>",
            Note("footnote", 1, "n"),
            settingsXml:
                "<w:footnotePr><w:numFmt w:val=\"upperLetter\"/><w:numStart w:val=\"3\"/></w:footnotePr>" +
                "<w:endnotePr><w:numFmt w:val=\"decimal\"/></w:endnotePr>");

        Assert.Equal(ListNumberFormat.UpperLetter, result.Document.NoteNumbering.FootnoteFormat);
        Assert.Equal(3, result.Document.NoteNumbering.FootnoteStart);
        Assert.Equal(ListNumberFormat.Decimal, result.Document.NoteNumbering.EndnoteFormat);
    }

    [Fact]
    public void A_Note_Cited_In_A_Table_Cell_Is_Read()
    {
        DocumentReadResult result = Read(
            "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>cell</w:t></w:r>" + Reference("footnote", 1) + "</w:p></w:tc></w:tr></w:tbl>",
            Note("footnote", 1, "In a cell."));

        Assert.Equal("In a cell.", Assert.Single(result.Document.Notes).PlainText);
    }

    [Fact]
    public void Notes_Written_And_Read_Back_Are_The_Same_Notes()
    {
        var footnote = new DocumentNote(NoteKind.Footnote, [RichTextParagraph.Plain("one"), RichTextParagraph.Plain("two")]);
        var endnote = new DocumentNote(NoteKind.Endnote, [RichTextParagraph.Plain("last")], "†");
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Plain("a")
                .InsertText(1, DocumentNote.PlaceholderText, InlineStyle.Default with { Note = footnote })
                .InsertText(2, "b", InlineStyle.Default)
                .InsertText(3, DocumentNote.PlaceholderText, InlineStyle.Default with { Note = endnote }),
        ]).WithNoteNumbering(NoteNumbering.Default with { EndnoteFormat = ListNumberFormat.Decimal, RaisedLabels = true });

        byte[] bytes = DocxDocumentCodec.WriteToArray(document);
        using var stream = new MemoryStream(bytes, writable: false);
        DocumentReadResult read = new DocxDocumentCodec().Read(stream);

        Assert.Equal(document.Paragraphs[0].Text, read.Document.Paragraphs[0].Text);
        Assert.Collection(
            read.Document.Notes,
            first => Assert.Equal(["one", "two"], first.Paragraphs.Select(p => p.Text)),
            second =>
            {
                Assert.Equal(NoteKind.Endnote, second.Kind);
                Assert.Equal("†", second.CustomMark);
                Assert.Equal("last", second.PlainText);
            });
        Assert.Equal(ListNumberFormat.Decimal, read.Document.NoteNumbering.EndnoteFormat);
        Assert.True(read.Document.NoteNumbering.RaisedLabels);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("word/footnotes.xml"));
        Assert.NotNull(archive.GetEntry("word/endnotes.xml"));
        using var reader = new StreamReader(archive.GetEntry("[Content_Types].xml")!.Open(), Encoding.UTF8);
        string types = reader.ReadToEnd();
        Assert.Contains("/word/footnotes.xml", types, StringComparison.Ordinal);
        Assert.Contains("/word/endnotes.xml", types, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Document_Without_Notes_Writes_No_Notes_Parts()
    {
        byte[] bytes = DocxDocumentCodec.WriteToArray(RichTextDocument.FromPlainText("plain"));

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.Null(archive.GetEntry("word/footnotes.xml"));
        Assert.Null(archive.GetEntry("word/endnotes.xml"));
    }
}
