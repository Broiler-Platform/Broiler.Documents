using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// RTF's <c>\footnote</c> group, read where the body cites it. Word writes
/// the citation as <c>{\super\chftn}</c> and the note's text opens with the
/// label again; <c>\ftnalt</c> makes it an endnote.
/// </summary>
public sealed class RtfNoteTests
{
    private static RichTextDocument Read(string text) =>
        RtfReader.Read(Encoding.Latin1.GetBytes(text)).Document;

    [Fact]
    public void A_Footnote_Group_Is_A_Citation_Where_The_Group_Stands()
    {
        RichTextDocument document = Read(
            @"{\rtf1 Text{\super\chftn}{\footnote\pard\plain{\super\chftn} The note.} after\par}");

        Assert.Equal("Text" + DocumentNote.PlaceholderText + " after", document.Paragraphs[0].Text);
        DocumentNote note = Assert.Single(document.Notes);
        Assert.Equal(NoteKind.Footnote, note.Kind);
        Assert.Equal("The note.", note.PlainText);
    }

    [Fact]
    public void Ftnalt_Makes_It_An_Endnote()
    {
        RichTextDocument document = Read(@"{\rtf1 x{\footnote\ftnalt\pard\plain end}\par}");

        Assert.Equal(NoteKind.Endnote, Assert.Single(document.Notes).Kind);
    }

    [Fact]
    public void A_Note_Keeps_Its_Paragraphs()
    {
        RichTextDocument document = Read(@"{\rtf1 x{\footnote\pard\plain one\par\pard\plain two}\par}");

        Assert.Equal(["one", "two"], Assert.Single(document.Notes).Paragraphs.Select(p => p.Text));
    }

    [Fact]
    public void A_Note_In_A_Header_Is_Still_Skipped()
    {
        RichTextDocument document = Read(@"{\rtf1{\header head{\footnote hidden}\par}body\par}");

        Assert.Empty(document.Notes);
        Assert.Equal("head", document.RunningContent.Header(PageSelection.Default)[0].Text);
    }

    [Fact]
    public void Notes_Written_And_Read_Back_Are_The_Same_Notes()
    {
        var footnote = new DocumentNote(NoteKind.Footnote, [RichTextParagraph.Plain("one"), RichTextParagraph.Plain("two")]);
        var endnote = new DocumentNote(NoteKind.Endnote, [RichTextParagraph.Plain("last")]);
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Plain("a")
                .InsertText(1, DocumentNote.PlaceholderText, InlineStyle.Default with { Note = footnote })
                .InsertText(2, "b", InlineStyle.Default)
                .InsertText(3, DocumentNote.PlaceholderText, InlineStyle.Default with { Note = endnote }),
        ]);

        RichTextDocument read = RtfReader.Read(RtfWriter.WriteToArray(document)).Document;

        Assert.Equal(document.Paragraphs[0].Text, read.Paragraphs[0].Text);
        Assert.Collection(
            read.Notes,
            first =>
            {
                Assert.Equal(NoteKind.Footnote, first.Kind);
                Assert.Equal(["one", "two"], first.Paragraphs.Select(p => p.Text));
            },
            second =>
            {
                Assert.Equal(NoteKind.Endnote, second.Kind);
                Assert.Equal("last", second.PlainText);
            });
    }
}
