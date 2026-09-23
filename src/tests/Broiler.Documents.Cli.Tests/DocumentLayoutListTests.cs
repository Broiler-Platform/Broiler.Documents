using Broiler.Documents.Cli.Rendering;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// List markers in the render layout. Every reader puts a top-level list item
/// at indent level 1, so the marker's style is chosen by how deep the item is
/// nested below that - a top-level list is numbered 1, 2, 3.
/// </summary>
public sealed class DocumentLayoutListTests
{
    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, PageSetup.Default);
    }

    private static IEnumerable<string> Texts(LayoutResult result) =>
        result.Pages.SelectMany(page => page.Lines).SelectMany(line => line.Pieces).Select(piece => piece.Text);

    private static RichTextParagraph Item(string text, ListKind kind, int level) =>
        RichTextParagraph.Create(text, InlineStyle.Default, ParagraphStyle.Default with { ListKind = kind, IndentLevel = level });

    [Fact]
    public void A_Top_Level_Numbered_List_Counts_In_Numbers()
    {
        // Lettering the top level renumbered every list a document held: a
        // list written 1, 2 came back a, b.
        LayoutResult result = Layout(RichTextDocument.FromParagraphs(
        [
            Item("First", ListKind.Numbered, 1),
            Item("Second", ListKind.Numbered, 1),
        ]));

        Assert.Contains("1.", Texts(result));
        Assert.Contains("2.", Texts(result));
        Assert.DoesNotContain("a.", Texts(result));
    }

    [Fact]
    public void A_Nested_Item_Is_Lettered()
    {
        LayoutResult result = Layout(RichTextDocument.FromParagraphs(
        [
            Item("Outer", ListKind.Numbered, 1),
            Item("Inner", ListKind.Numbered, 2),
        ]));

        Assert.Contains("1.", Texts(result));
        Assert.Contains("a.", Texts(result));
    }

    [Fact]
    public void A_Top_Level_Bullet_Is_A_Solid_Bullet()
    {
        LayoutResult result = Layout(RichTextDocument.FromParagraphs([Item("Point", ListKind.Bullet, 1)]));

        Assert.Contains("•", Texts(result));
    }
}
