namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Office conformance issue 249, ODT pagination: notes a section collects at
/// its end.
/// </summary>
public sealed class DocumentLayoutIssue249OdtPaginationTests
{
    /// <summary>US Letter with one-inch margins: a text area 468 points wide and 648 tall.</summary>
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, Letter);
    }

    private static RichTextParagraph Citing(string text, DocumentNote note) =>
        RichTextParagraph.Plain(text).InsertText(text.Length, DocumentNote.PlaceholderText, InlineStyle.Default with { Note = note });

    private static string TextOf(LayoutLine line) => string.Concat(line.Pieces.Select(piece => piece.Text));

    [Fact]
    public void An_Endnote_Its_Section_Collects_Is_Set_At_The_Sections_End_Under_A_Line()
    {
        // tdf146704_EndnoteInSection.odt: the section collects its endnote,
        // so it is set under the section's text on page 1, not on an endnote
        // page of its own, under the page's note separator.
        var collection = new SectionFootnotes();
        var document = RichTextDocument.FromParagraphs(
        [
            Citing("In", new DocumentNote(NoteKind.Endnote, [RichTextParagraph.Plain("collected")]) { Section = collection }),
            RichTextParagraph.Plain("After"),
        ]).WithSections(
        [
            new DocumentSection(0, 1, 1, 0) { FootnotesEnding = [collection] },
        ]).WithNoteNumbering(NoteNumbering.Default with { EndnotesStartOnNewPage = true });

        LayoutPage page = Assert.Single(Layout(document).Pages);
        Assert.Equal(["Ini", "icollected", "After"], page.Lines.Select(TextOf));
        LayoutCell separator = Assert.Single(page.Cells);
        Assert.Equal(468 * 0.25, separator.Bounds.Width, 3);
        Assert.True(separator.Bounds.Y > page.Lines[0].Top + page.Lines[0].Height);
        Assert.True(separator.Bounds.Y < page.Lines[1].Top);
    }

    [Fact]
    public void A_Collected_Endnote_With_Its_Own_Start_Is_Numbered_From_It()
    {
        // ooo32780-1.odt: text:start-value="6" labels the section's endnote vi.
        var collection = new SectionFootnotes(6);
        var note = new DocumentNote(NoteKind.Endnote, [RichTextParagraph.Plain("end")]) { Section = collection };

        Assert.Equal("vi", new NoteNumberer().Label(note));
    }
}
