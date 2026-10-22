using Broiler.Graphics.Color;

namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Tracked changes on the page, as LibreOffice draws them: inserted text
/// underlined and deleted text struck through in the author's colour, a bar in
/// the margin beside each line that holds a change, and nothing of a deletion
/// where the document hides its changes.
/// </summary>
public sealed class DocumentLayoutRevisionTests
{
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, Letter);
    }

    private static RichTextDocument Changed(bool shown) =>
        RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Plain("kept ")
                .InsertText(5, "added", InlineStyle.Default with { Revision = new TextRevision(RevisionKind.Insertion, "Anna") })
                .InsertText(10, "gone", InlineStyle.Default with { Revision = new TextRevision(RevisionKind.Deletion, "Anna") }),
            RichTextParagraph.Plain("plain"),
        ]).WithShowsRevisions(shown);

    [Fact]
    public void Shown_Changes_Are_Drawn_In_Their_Authors_Colour_With_A_Bar_Beside_The_Line()
    {
        LayoutPage page = Assert.Single(Layout(Changed(shown: true)).Pages);

        LayoutPiece added = Assert.Single(page.Lines[0].Pieces, piece => piece.Text.Contains("added", StringComparison.Ordinal));
        LayoutPiece gone = Assert.Single(page.Lines[0].Pieces, piece => piece.Text.Contains("gone", StringComparison.Ordinal));
        Assert.True(added.Underline);
        Assert.True(gone.Strikethrough);
        Assert.Equal(new BColor(0xC6, 0x92, 0x00), added.Color);
        Assert.Equal(added.Color, gone.Color);

        // A quarter of a centimetre left of the text frame, LibreOffice's distance.
        Assert.Equal(72 - (567.0 / 4 / 20), page.Lines[0].ChangeBarX!.Value, 3);
        Assert.Null(page.Lines[1].ChangeBarX);
    }

    [Fact]
    public void Hidden_Changes_Leave_Deletions_Out_And_Draw_Insertions_Plain()
    {
        LayoutPage page = Assert.Single(Layout(Changed(shown: false)).Pages);

        string text = string.Concat(page.Lines[0].Pieces.Select(piece => piece.Text));
        Assert.Equal("kept added", text);
        Assert.All(page.Lines[0].Pieces, piece => Assert.False(piece.Underline));
        Assert.Null(page.Lines[0].ChangeBarX);
    }
}
