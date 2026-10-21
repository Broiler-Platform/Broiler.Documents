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

    private static readonly ListDefinition Clauses = new(
    [
        new ListLevel { Template = "%1.", Start = 4 },
        new ListLevel { Template = "(%2)", Format = ListNumberFormat.LowerRoman },
    ]);

    private static RichTextParagraph Clause(string text, int level)
    {
        var reference = new ListReference(Clauses, level);
        return RichTextParagraph.Create(
            text,
            InlineStyle.Default,
            ParagraphStyle.Default with { List = reference, ListKind = reference.Kind, IndentLevel = level + 1 });
    }

    [Fact]
    public void A_Defined_List_Draws_Its_Labels_And_Counts_On_Across_Other_Paragraphs()
    {
        // tdf168017.docx: clauses 4, 9 and 12 continue a list around
        // unnumbered paragraphs, and start where the definition says.
        LayoutResult result = Layout(RichTextDocument.FromParagraphs(
        [
            Clause("Changes", 0),
            Clause("first", 1),
            Clause("second", 1),
            RichTextParagraph.Plain("An unnumbered paragraph."),
            Clause("Taxes", 0),
        ]));

        List<string> texts = [.. Texts(result)];
        Assert.Contains("4.", texts);
        Assert.Contains("(i)", texts);
        Assert.Contains("(ii)", texts);
        Assert.Contains("5.", texts);
        Assert.DoesNotContain("1.", texts);
    }

    [Fact]
    public void A_Defined_List_Counts_On_Through_A_Table()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            Clause("before", 0),
            Clause("in a cell", 0),
            Clause("after", 0),
        ]).WithTables([new DocumentTable(1, 1, [new TableRow([new TableCell(1, 1, 0)])])]);

        List<string> texts = [.. Texts(Layout(document))];
        Assert.Contains("4.", texts);
        Assert.Contains("5.", texts);
        Assert.Contains("6.", texts);
    }

    [Fact]
    public void The_Text_Dump_Writes_Each_Label_Before_Its_Paragraph()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            Clause("Changes", 0),
            RichTextParagraph.Plain("Plain"),
            Item("Legacy", ListKind.Numbered, 1),
            Clause("Taxes", 0),
        ]);

        // As LibreOffice's text export writes them; a list known only by its
        // kind has no label anybody stated.
        Assert.Equal("4. Changes\nPlain\nLegacy\n5. Taxes", Broiler.Documents.Cli.Commands.DocumentCommands.LabelledText(document));
    }
}
