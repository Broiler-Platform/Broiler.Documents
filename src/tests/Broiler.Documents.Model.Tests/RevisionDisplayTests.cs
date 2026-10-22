using Broiler.Graphics.Color;

namespace Broiler.Documents.Model.Tests;

/// <summary>
/// Tracked changes as LibreOffice draws them: inserted text underlined and
/// deleted text struck through in the author's colour, and a document that
/// hides its changes drawn as it reads with its deletions gone.
/// </summary>
public sealed class RevisionDisplayTests
{
    private static readonly TextRevision InsertedByAnna = new(RevisionKind.Insertion, "Anna");
    private static readonly TextRevision DeletedByAnna = new(RevisionKind.Deletion, "Anna");
    private static readonly TextRevision DeletedByBen = new(RevisionKind.Deletion, "Ben");

    private static InlineStyle Marked(TextRevision revision) => InlineStyle.Default with { Revision = revision };

    /// <summary>A paragraph of plain text followed by text <paramref name="revision"/> marks.</summary>
    private static RichTextParagraph Paragraph(string plain, string marked, TextRevision revision) =>
        RichTextParagraph.Plain(plain).InsertText(plain.Length, marked, Marked(revision));

    private static RichTextParagraph WithDeletedMark(RichTextParagraph paragraph) =>
        paragraph.WithMarkStyle(paragraph.MarkStyle with { Revision = DeletedByAnna });

    [Fact]
    public void An_Insertion_Is_Underlined_And_A_Deletion_Struck_Through_In_Their_Authors_Colour()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [Paragraph("a", "inserted", InsertedByAnna), Paragraph("b", "deleted", DeletedByBen)]);
        RevisionDisplay display = RevisionDisplay.For(document);

        InlineStyle inserted = display.Decorate(Marked(InsertedByAnna), out bool doubled);
        InlineStyle deleted = display.Decorate(Marked(DeletedByBen), out _);

        // LibreOffice's AUTHOR1 and AUTHOR2, taken in the order the authors appear.
        Assert.True(inserted.Underline);
        Assert.False(doubled);
        Assert.Equal(new BColor(0xC6, 0x92, 0x00), inserted.Foreground);
        Assert.True(deleted.Strikethrough);
        Assert.Equal(new BColor(0x06, 0x46, 0xA2), deleted.Foreground);
        Assert.Equal(inserted.Foreground, display.Decorate(Marked(DeletedByAnna), out _).Foreground);
    }

    [Fact]
    public void Moved_Text_Is_Green_With_Its_Line_Doubled_And_A_Format_Change_Bold()
    {
        RevisionDisplay display = RevisionDisplay.For(RichTextDocument.Empty);

        InlineStyle moved = display.Decorate(Marked(new TextRevision(RevisionKind.Deletion, "Anna", Moved: true)), out bool doubled);
        InlineStyle formatted = display.Decorate(Marked(new TextRevision(RevisionKind.Format, "Anna")), out _);

        Assert.True(doubled);
        Assert.True(moved.Strikethrough);
        Assert.Equal(RevisionDisplay.MovedColor, moved.Foreground);
        Assert.True(formatted.Bold);
    }

    [Fact]
    public void A_Document_That_Hides_Its_Changes_Draws_None_As_A_Change()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([Paragraph("a", "b", InsertedByAnna)])
            .WithShowsRevisions(false);

        InlineStyle drawn = RevisionDisplay.For(document).Decorate(Marked(InsertedByAnna), out _);

        Assert.False(drawn.Underline);
        Assert.True(drawn.Foreground.IsEmpty);
    }

    [Fact]
    public void Hiding_Changes_Leaves_A_Document_That_Shows_Them_As_It_Is()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([Paragraph("kept ", "gone", DeletedByAnna)]);

        Assert.Same(document, RevisionDisplay.HideDeletions(document));
    }

    [Fact]
    public void Hidden_Deletions_Are_Left_Out_And_A_Deleted_Break_Joins_Its_Paragraphs()
    {
        RichTextParagraph first = WithDeletedMark(Paragraph("Head ", "tail", DeletedByAnna))
            .WithParagraphStyle(ParagraphStyle.Default with { Alignment = TextAlignment.Center });
        RichTextParagraph second = RichTextParagraph.Plain("rest").InsertText(0, "lead ", Marked(DeletedByAnna));
        RichTextDocument document = RichTextDocument.FromParagraphs([first, second, RichTextParagraph.Plain("next")])
            .WithShowsRevisions(false);

        RichTextDocument hidden = RevisionDisplay.HideDeletions(document);

        // The joined paragraph keeps the first one's properties, as LibreOffice
        // does when it hides the change.
        Assert.Equal(["Head rest", "next"], hidden.Paragraphs.Select(paragraph => paragraph.Text));
        Assert.Equal(TextAlignment.Center, hidden.Paragraphs[0].Style.Alignment);
    }

    [Fact]
    public void A_Wholly_Deleted_Paragraph_Goes_And_Hands_On_The_Page_It_Started()
    {
        RichTextParagraph deleted = WithDeletedMark(RichTextParagraph.Create("gone", Marked(DeletedByAnna)))
            .WithParagraphStyle(ParagraphStyle.Default with { PageBreakBefore = true });
        RichTextDocument document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("one"), deleted, RichTextParagraph.Plain("two")])
            .WithShowsRevisions(false);

        RichTextDocument hidden = RevisionDisplay.HideDeletions(document);

        Assert.Equal(["one", "two"], hidden.Paragraphs.Select(paragraph => paragraph.Text));
        Assert.True(hidden.Paragraphs[1].Style.PageBreakBefore);
    }

    [Fact]
    public void A_Wholly_Deleted_Table_Goes_With_Its_Grid()
    {
        RichTextParagraph Cell(string text) => WithDeletedMark(RichTextParagraph.Create(text, Marked(DeletedByAnna)));
        var table = new DocumentTable(1, 2, [new TableRow([new TableCell(1, 1, 0), new TableCell(2, 1, 1)])]);
        RichTextDocument document = RichTextDocument
            .FromParagraphs([RichTextParagraph.Plain("before"), Cell("a"), Cell("b"), RichTextParagraph.Plain("after")])
            .WithTables([table])
            .WithShowsRevisions(false);

        RichTextDocument hidden = RevisionDisplay.HideDeletions(document);

        Assert.Equal(["before", "after"], hidden.Paragraphs.Select(paragraph => paragraph.Text));
        Assert.Empty(hidden.Tables);
    }

    [Fact]
    public void A_Deleted_Break_Does_Not_Join_A_Paragraph_To_The_Next_Table_Cell()
    {
        RichTextParagraph first = WithDeletedMark(Paragraph("a", "x", DeletedByAnna));
        var table = new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])]);
        RichTextDocument document = RichTextDocument.FromParagraphs([first, RichTextParagraph.Plain("b")])
            .WithTables([table])
            .WithShowsRevisions(false);

        RichTextDocument hidden = RevisionDisplay.HideDeletions(document);

        Assert.Equal(["a", "b"], hidden.Paragraphs.Select(paragraph => paragraph.Text));
        Assert.Single(hidden.Tables);
    }

    [Fact]
    public void Joining_Deleted_Breaks_Keeps_Each_As_A_Deleted_Separator()
    {
        RichTextParagraph first = WithDeletedMark(Paragraph("Head ", "tail", DeletedByAnna));
        RichTextParagraph second = RichTextParagraph.Plain("rest").WithMarkStyle(InlineStyle.Default with { Bold = true });

        RichTextDocument joined = RevisionDisplay.JoinDeletedBreaks(RichTextDocument.FromParagraphs([first, second]));

        RichTextParagraph paragraph = Assert.Single(joined.Paragraphs);
        Assert.Equal("Head tail" + RevisionDisplay.DeletedBreak + "rest", paragraph.Text);
        Assert.True(paragraph.StyleAt(9).IsDeleted);
        Assert.True(paragraph.MarkStyle.Bold);
    }

    [Fact]
    public void Text_Typed_Beside_A_Change_Is_Not_Part_Of_It()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([Paragraph("a", "deleted", DeletedByAnna)]);

        Assert.Null(document.InlineStyleAt(document.End).Revision);
    }
}
