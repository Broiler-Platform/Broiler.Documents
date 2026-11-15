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

    private static DocumentNote LongFootnote(int lines) =>
        new(NoteKind.Footnote, [.. Enumerable.Range(1, lines).Select(i => RichTextParagraph.Plain("Line " + i))]);

    private static List<LayoutLine> NoteLines(LayoutPage page) =>
        [.. page.Lines.Where(line => line.ParagraphIndex < 0)];

    [Fact]
    public void A_Footnote_Taller_Than_Its_Room_Continues_At_The_Foot_Of_The_Next_Page()
    {
        // quo_vadis_footnote.fodt and ergo_sum_footnote.fodt: the lines that
        // fit stay under the citation, the rest are set at the foot of page 2.
        RichTextDocument document = RichTextDocument.FromParagraphs([Citing("Body", LongFootnote(60))]);

        LayoutResult result = Layout(document);

        Assert.Equal(2, result.Pages.Count);
        List<LayoutLine> first = NoteLines(result.Pages[0]);
        List<LayoutLine> second = NoteLines(result.Pages[1]);
        Assert.Equal(60, first.Count + second.Count);
        Assert.NotEmpty(first);
        Assert.NotEmpty(second);
        Assert.Equal("Line " + (first.Count + 1), TextOf(second[0]));
        Assert.Equal(720, second[^1].Top + second[^1].Height, 3);
        Assert.Single(result.Pages[1].Cells);
    }

    [Fact]
    public void Footnotes_Leave_A_Fifth_Of_The_Page_To_The_Body()
    {
        // SwFootnoteBossFrame::GetVarSpace: ergo_sum_footnote.fodt keeps seven
        // of its eight lines on page 1 though all eight would fit under the citation.
        RichTextDocument document = RichTextDocument.FromParagraphs([Citing("Body", LongFootnote(60))]);

        LayoutPage page = Layout(document).Pages[0];

        LayoutCell separator = Assert.Single(page.Cells);
        Assert.True(720 - separator.Bounds.Y <= 648 * 0.8);
        Assert.True(720 - separator.Bounds.Y > 648 * 0.8 - 20);
    }

    [Fact]
    public void A_Footnote_With_No_Room_Under_A_Citation_That_Cannot_Move_Goes_On_To_The_Next_Page()
    {
        // Cool16072_rowWithOwnFootnoteKeepsThePage.fodt: the citing text fills
        // the page, so its note is set whole on the next page rather than over it.
        RichTextParagraph tall = Citing("Tall", LongFootnote(3))
            .WithParagraphStyle(ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 640 });
        RichTextDocument document = RichTextDocument.FromParagraphs([tall]);

        LayoutResult result = Layout(document);

        Assert.Equal(2, result.Pages.Count);
        Assert.Empty(NoteLines(result.Pages[0]));
        Assert.Empty(result.Pages[0].Cells);
        Assert.Equal(3, NoteLines(result.Pages[1]).Count);
    }
}
