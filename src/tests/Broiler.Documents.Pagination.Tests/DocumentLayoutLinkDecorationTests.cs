namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// A link takes the renderer's link look only in a document whose links have
/// one without saying so: an ODT, HTML or Markdown link, not a DOCX or RTF one
/// (<see cref="DocumentStyleDefaults.UndecoratedLinks"/>).
/// </summary>
public sealed class DocumentLayoutLinkDecorationTests
{
    private static LayoutPiece LinkPiece(bool undecorated)
    {
        using var images = new ImageStore();
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("link", InlineStyle.Default with { LinkHref = "https://example.com/" }),
        ]).WithStyleDefaults(DocumentStyleDefaults.Default with { UndecoratedLinks = undecorated });
        LayoutPage page = new DocumentLayout(new LayoutSettings(), images)
            .Layout(document, new DocumentPageBox(612, 792, 72, 72, 72, 72)).Pages[0];
        return Assert.Single(Assert.Single(page.Lines).Pieces);
    }

    [Fact]
    public void A_Link_Is_Decorated_Where_The_Document_Leaves_It_To_The_Renderer()
    {
        LayoutPiece piece = LinkPiece(undecorated: false);

        Assert.True(piece.Underline);
        Assert.Equal(new LayoutSettings().LinkColor, piece.Color);
    }

    [Fact]
    public void A_Link_Keeps_Its_Own_Formatting_Where_The_Document_Says_So()
    {
        LayoutPiece piece = LinkPiece(undecorated: true);

        Assert.False(piece.Underline);
        Assert.Equal("https://example.com/", piece.Link);
        Assert.NotEqual(new LayoutSettings().LinkColor, piece.Color);
    }
}
