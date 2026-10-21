namespace Broiler.Documents.Model.Tests;

/// <summary>
/// A footnote or endnote is held by the one character that cites it, and its
/// label is worked out from the order notes are cited in.
/// </summary>
public sealed class DocumentNoteTests
{
    private static RichTextParagraph Citing(string text, DocumentNote note) =>
        RichTextParagraph.Plain(text).InsertText(text.Length, DocumentNote.PlaceholderText, InlineStyle.Default with { Note = note });

    [Fact]
    public void Notes_Are_Labelled_In_Citation_Order_Each_Kind_On_Its_Own()
    {
        var first = new DocumentNote(NoteKind.Footnote, [RichTextParagraph.Plain("a")]);
        var end = new DocumentNote(NoteKind.Endnote, [RichTextParagraph.Plain("b")]);
        var second = new DocumentNote(NoteKind.Footnote, [RichTextParagraph.Plain("c")]);
        var marked = new DocumentNote(NoteKind.Footnote, [RichTextParagraph.Plain("d")], "*");

        NoteNumberer numberer = NoteNumberer.For([Citing("x", first), Citing("y", end), Citing("z", marked), Citing("w", second)]);

        Assert.Equal("1", numberer.Label(first));
        Assert.Equal("i", numberer.Label(end));
        Assert.Equal("*", numberer.Label(marked));
        Assert.Equal("2", numberer.Label(second));
    }

    [Fact]
    public void A_Document_Numbers_Its_Notes_As_It_States()
    {
        var note = new DocumentNote(NoteKind.Footnote, [RichTextParagraph.Plain("a")]);
        var numbering = NoteNumbering.Default with { FootnoteFormat = ListNumberFormat.UpperLetter, FootnoteStart = 3 };

        Assert.Equal("C", new NoteNumberer(numbering).Label(note));
    }

    [Fact]
    public void A_Note_With_No_Paragraphs_Has_One_Empty_One()
    {
        var note = new DocumentNote(NoteKind.Footnote, []);

        Assert.Equal(string.Empty, Assert.Single(note.Paragraphs).Text);
    }

    [Fact]
    public void The_Document_Lists_The_Notes_Its_Body_Cites()
    {
        var note = new DocumentNote(NoteKind.Footnote, [RichTextParagraph.Plain("n")]);
        RichTextDocument document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("none"), Citing("cites", note)]);

        Assert.Same(note, Assert.Single(document.Notes));
    }

    [Fact]
    public void Text_Typed_After_A_Citation_Is_Not_Part_Of_It()
    {
        var note = new DocumentNote(NoteKind.Footnote, [RichTextParagraph.Plain("n")]);
        RichTextDocument document = RichTextDocument.FromParagraphs([Citing("cites", note)]);

        RichTextDocument typed = document.InsertText(new RichTextPosition(0, 6), "!").Document;

        Assert.Same(note, Assert.Single(typed.Notes));
        Assert.Null(typed.Paragraphs[0].StyleAt(6).Note);
    }

    [Fact]
    public void Deleting_The_Citation_Deletes_The_Note()
    {
        var note = new DocumentNote(NoteKind.Footnote, [RichTextParagraph.Plain("n")]);
        RichTextDocument document = RichTextDocument.FromParagraphs([Citing("cites", note)]);

        RichTextDocument deleted = document.DeleteRange(new RichTextRange(new RichTextPosition(0, 5), new RichTextPosition(0, 6))).Document;

        Assert.Empty(deleted.Notes);
    }

    [Fact]
    public void Every_Edit_Keeps_The_Note_Numbering()
    {
        var numbering = NoteNumbering.Default with { EndnoteFormat = ListNumberFormat.Decimal };
        RichTextDocument document = RichTextDocument.FromPlainText("x").WithNoteNumbering(numbering);

        RichTextDocument edited = document.InsertText(new RichTextPosition(0, 1), "\nmore").Document;

        Assert.Same(numbering, edited.NoteNumbering);
    }
}
