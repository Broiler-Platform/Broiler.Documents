namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Footnotes at the foot of the page that cites them, under a short line, and
/// endnotes after the body, as LibreOffice sets them.
/// </summary>
public sealed class DocumentLayoutNoteTests
{
    /// <summary>US Letter with one-inch margins: a text area 468 points wide and 648 tall.</summary>
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    private static LayoutResult Layout(RichTextDocument document, DocumentPageBox? box = null)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, box ?? Letter);
    }

    private static RichTextParagraph Citing(string text, DocumentNote note) =>
        RichTextParagraph.Plain(text).InsertText(text.Length, DocumentNote.PlaceholderText, InlineStyle.Default with { Note = note });

    private static DocumentNote Footnote(string text) => new(NoteKind.Footnote, [RichTextParagraph.Plain(text)]);

    private static string TextOf(LayoutLine line) => string.Concat(line.Pieces.Select(piece => piece.Text));

    [Fact]
    public void A_Footnote_Is_Set_At_The_Foot_Of_The_Page_Under_A_Line()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([Citing("Body", Footnote("The note."))]);

        LayoutPage page = Assert.Single(Layout(document).Pages);

        LayoutLine body = page.Lines[0];
        Assert.Equal("Body1", TextOf(body));
        LayoutLine note = Assert.Single(page.Lines, line => TextOf(line).Contains("The note.", StringComparison.Ordinal));

        // The note's last line ends at the foot of the text, 720 points down.
        Assert.Equal(720, note.Top + note.Height, 3);
        Assert.Equal("1", note.Pieces[0].Text);

        // The line above the notes is a quarter of the text's width.
        LayoutCell separator = Assert.Single(page.Cells);
        Assert.Equal(72, separator.Bounds.X, 3);
        Assert.Equal(468 * 0.25, separator.Bounds.Width, 3);
        Assert.True(separator.Bounds.Y < note.Top);
    }

    [Fact]
    public void The_Citation_Is_Raised_And_Smaller_Than_Its_Text()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([Citing("Body", Footnote("n"))]);

        LayoutLine body = Layout(document).Pages[0].Lines[0];
        LayoutPiece text = body.Pieces[0];
        LayoutPiece citation = Assert.Single(body.Pieces, piece => piece.Note is not null);

        Assert.True(citation.Rise > 0);
        Assert.True(citation.Font.Size < text.Font.Size);
        Assert.Equal("1", citation.Text);
    }

    [Fact]
    public void Footnotes_Are_Numbered_In_The_Order_They_Are_Cited()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            Citing("First", Footnote("one")),
            Citing("Second", Footnote("two")),
        ]);

        LayoutPage page = Assert.Single(Layout(document).Pages);
        Assert.Equal(["First1", "Second2"], page.Lines.Take(2).Select(TextOf));
        Assert.Contains(page.Lines, line => TextOf(line) == "2two");
    }

    [Fact]
    public void A_Line_Whose_Note_Would_Not_Fit_Goes_To_The_Next_Page_With_Its_Note()
    {
        int perPage = Layout(RichTextDocument.FromParagraphs(
            Enumerable.Range(1, 400).Select(i => RichTextParagraph.Plain("Line " + i)))).Pages[0].Lines.Count;

        // The last line that fits on the first page cites a note: the note
        // needs room the page no longer has, so both move on.
        var paragraphs = Enumerable.Range(1, perPage - 1).Select(i => RichTextParagraph.Plain("Line " + i)).ToList();
        paragraphs.Add(Citing("Cites", Footnote("The note.")));
        LayoutResult result = Layout(RichTextDocument.FromParagraphs(paragraphs));

        Assert.Equal(2, result.Pages.Count);
        Assert.DoesNotContain(result.Pages[0].Lines, line => TextOf(line).Contains("The note.", StringComparison.Ordinal));
        Assert.Equal("Cites1", TextOf(result.Pages[1].Lines[0]));
        Assert.Contains(result.Pages[1].Lines, line => TextOf(line).Contains("The note.", StringComparison.Ordinal));
    }

    [Fact]
    public void Endnotes_Follow_The_Body()
    {
        var endnote = new DocumentNote(NoteKind.Endnote, [RichTextParagraph.Plain("At the end.")]);
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [Citing("Body", endnote), RichTextParagraph.Plain("More body")]);

        LayoutPage page = Assert.Single(Layout(document).Pages);

        Assert.Equal(["Bodyi", "More body", "iAt the end."], page.Lines.Select(TextOf));
        Assert.True(page.Lines[2].Top > page.Lines[1].Top + page.Lines[1].Height);
    }

    [Fact]
    public void A_Document_That_Raises_Its_Labels_Opens_The_Note_With_A_Raised_One()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([Citing("Body", Footnote("The note."))])
            .WithNoteNumbering(NoteNumbering.Default with { RaisedLabels = true });

        LayoutLine note = Assert.Single(Layout(document).Pages[0].Lines, line => TextOf(line).Contains("The note.", StringComparison.Ordinal));

        Assert.Equal("1 The note.", TextOf(note));
        Assert.True(note.Pieces[0].Rise > 0);
    }

    [Fact]
    public void A_Continuous_Render_Sets_Footnotes_After_The_Body()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [Citing("Body", Footnote("The note.")), RichTextParagraph.Plain("More")]);

        LayoutPage page = Assert.Single(Layout(document, new DocumentPageBox(612, 792, 72, 72, 72, 72, continuous: true)).Pages);

        Assert.Equal(["Body1", "More", "1The note."], page.Lines.Select(TextOf));
    }
}
