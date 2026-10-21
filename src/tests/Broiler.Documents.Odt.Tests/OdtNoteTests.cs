using System.IO.Compression;
using System.Text;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Footnotes and endnotes (<c>text:note</c>). They used to be dropped with
/// <c>odt.note</c>; LibreOffice keeps 45 ODT and 21 flat ODT documents in its
/// <c>sw/qa/extras</c> tree that cite one, and draws each note at the foot of
/// the page.
/// </summary>
public sealed class OdtNoteTests
{
    private static string Note(string text, string noteClass = "footnote", string citation = "1", string? label = null) =>
        "<text:note text:id=\"ftn1\" text:note-class=\"" + noteClass + "\">" +
        "<text:note-citation" + (label is null ? string.Empty : " text:label=\"" + label + "\"") + ">" + citation + "</text:note-citation>" +
        "<text:note-body><text:p>" + text + "</text:p></text:note-body></text:note>";

    private static DocumentNote SingleNote(RichTextDocument document) =>
        Assert.Single(document.Notes);

    [Fact]
    public void A_Footnote_Is_One_Citation_Holding_Its_Paragraphs()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p>kept" + Note("the note") + " after</text:p>");

        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("kept" + DocumentNote.PlaceholderText + " after", paragraph.Text);
        DocumentNote note = SingleNote(result.Document);
        Assert.Same(note, paragraph.StyleAt(4).Note);
        Assert.Equal(NoteKind.Footnote, note.Kind);
        Assert.Equal("the note", note.PlainText);
        Assert.Null(note.CustomMark);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "odt.note");
    }

    [Fact]
    public void A_Note_Keeps_Every_Paragraph_Of_Its_Body()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p>x<text:note text:note-class=\"footnote\"><text:note-citation>1</text:note-citation>" +
            "<text:note-body><text:p>one</text:p><text:p>two</text:p></text:note-body></text:note></text:p>");

        Assert.Equal(["one", "two"], SingleNote(result.Document).Paragraphs.Select(p => p.Text));
    }

    [Fact]
    public void An_Endnote_Is_Read_As_One()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody("<text:p>x" + Note("end", "endnote", "i") + "</text:p>");

        Assert.Equal(NoteKind.Endnote, SingleNote(result.Document).Kind);
    }

    [Fact]
    public void A_Label_The_Author_Typed_Is_Kept_And_The_Drawn_Number_Is_Not()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody("<text:p>x" + Note("starred", citation: "*", label: "*") + "</text:p>");

        Assert.Equal("*", SingleNote(result.Document).CustomMark);
    }

    [Fact]
    public void The_Citation_Takes_The_Formatting_Around_It()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p><text:span text:style-name=\"B\">bold" + Note("n") + "</text:span></text:p>",
            OdtTestPackage.Style("B", "<style:text-properties fo:font-weight=\"bold\"/>", "text"));

        InlineStyle citation = result.Document.Paragraphs[0].StyleAt(4);
        Assert.True(citation.Bold);
        Assert.NotNull(citation.Note);
    }

    [Fact]
    public void The_Notes_Configuration_Says_How_Notes_Are_Numbered()
    {
        DocumentReadResult result = OdtTestPackage.ReadStyled(
            "<text:p>x" + Note("n") + "</text:p>",
            "<office:styles>" +
            "<style:style style:name=\"Raised\" style:family=\"text\"><style:text-properties style:text-position=\"super 58%\"/></style:style>" +
            "<text:notes-configuration text:note-class=\"footnote\" text:citation-style-name=\"Raised\" style:num-format=\"a\" text:start-value=\"2\"/>" +
            "<text:notes-configuration text:note-class=\"endnote\" style:num-format=\"I\" text:start-value=\"0\"/>" +
            "</office:styles>");

        NoteNumbering numbering = result.Document.NoteNumbering;
        Assert.Equal(ListNumberFormat.LowerLetter, numbering.FootnoteFormat);
        Assert.Equal(3, numbering.FootnoteStart);
        Assert.Equal(ListNumberFormat.UpperRoman, numbering.EndnoteFormat);
        Assert.Equal(1, numbering.EndnoteStart);
        Assert.True(numbering.RaisedLabels);
    }

    [Fact]
    public void A_Document_That_States_No_Configuration_Numbers_As_LibreOffice_Does()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody("<text:p>x" + Note("n") + "</text:p>");

        Assert.Equal(NoteNumbering.Default, result.Document.NoteNumbering);
    }

    [Fact]
    public void A_Note_Inside_A_Note_Is_Reported_And_Not_Nested()
    {
        DocumentReadResult result = OdtTestPackage.ReadBody(
            "<text:p>x<text:note text:note-class=\"footnote\"><text:note-citation>1</text:note-citation>" +
            "<text:note-body><text:p>outer" + Note("inner") + "</text:p></text:note-body></text:note></text:p>");

        DocumentNote note = SingleNote(result.Document);
        Assert.Equal("outer", note.PlainText);
        Assert.Contains(result.Diagnostics, d => d.Code == "odt.note.nested");
    }

    [Fact]
    public void A_Note_Written_And_Read_Back_Is_The_Same_Note()
    {
        var note = new DocumentNote(NoteKind.Footnote, [RichTextParagraph.Plain("first"), RichTextParagraph.Plain("second")]);
        var endnote = new DocumentNote(NoteKind.Endnote, [RichTextParagraph.Plain("at the end")], "*");
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Plain("body ")
                .InsertText(5, DocumentNote.PlaceholderText, InlineStyle.Default with { Note = note })
                .InsertText(6, " and", InlineStyle.Default)
                .InsertText(10, DocumentNote.PlaceholderText, InlineStyle.Default with { Note = endnote }),
        ]).WithNoteNumbering(NoteNumbering.Default with { FootnoteFormat = ListNumberFormat.LowerLetter, RaisedLabels = true });

        byte[] bytes = OdtDocumentCodec.WriteToArray(document);
        using var stream = new MemoryStream(bytes, writable: false);
        RichTextDocument read = new OdtDocumentCodec().Read(stream).Document;

        Assert.Equal(document.Paragraphs[0].Text, read.Paragraphs[0].Text);
        Assert.Collection(
            read.Notes,
            first =>
            {
                Assert.Equal(NoteKind.Footnote, first.Kind);
                Assert.Equal(["first", "second"], first.Paragraphs.Select(p => p.Text));
                Assert.Null(first.CustomMark);
            },
            second =>
            {
                Assert.Equal(NoteKind.Endnote, second.Kind);
                Assert.Equal("*", second.CustomMark);
            });
        Assert.Equal(ListNumberFormat.LowerLetter, read.NoteNumbering.FootnoteFormat);
        Assert.True(read.NoteNumbering.RaisedLabels);

        string content = ReadPart(bytes, "content.xml");
        Assert.Contains("<text:note-citation>a</text:note-citation>", content, StringComparison.Ordinal);
    }

    private static string ReadPart(byte[] package, string name)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
