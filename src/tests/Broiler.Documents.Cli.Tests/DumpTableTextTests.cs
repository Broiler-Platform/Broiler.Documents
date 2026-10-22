using Broiler.Documents.Cli.Commands;

namespace Broiler.Documents.Cli.Tests;

public sealed class DumpTableTextTests
{
    [Fact]
    public void Image_object_markers_are_omitted_but_literal_replacement_characters_are_kept()
    {
        var image = new InlineImage(new byte[] { 1, 2, 3 }, "image/png", 12, 12);
        RichTextParagraph paragraph = RichTextParagraph.Plain("before after")
            .InsertText(7, InlineImage.PlaceholderText, InlineStyle.Default with { Image = image });
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [paragraph, RichTextParagraph.Plain("literal " + InlineImage.PlaceholderText)]);

        Assert.Equal("before after\nliteral " + InlineImage.PlaceholderText, DocumentCommands.LabelledText(document));
    }

    [Fact]
    public void Cells_are_tab_separated_and_rows_and_body_paragraphs_keep_newlines()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("before\nA\nB\nC\nD\nafter")
            .WithTables([new DocumentTable(1, 4,
                [new TableRow([new TableCell(1, 1, 0), new TableCell(2, 1, 1)]),
                 new TableRow([new TableCell(3, 1, 0), new TableCell(4, 1, 1)])])]);

        Assert.Equal("before\nA\tB\nC\tD\nafter", DocumentCommands.LabelledText(document));
    }

    [Fact]
    public void Multiple_paragraphs_in_a_cell_keep_their_boundaries()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("A1\nA2\nB")
            .WithTables([new DocumentTable(0, 3,
                [new TableRow([new TableCell(0, 2, 0), new TableCell(2, 1, 1)])])]);

        Assert.Equal("A1\nA2\tB", DocumentCommands.LabelledText(document));
    }

    [Fact]
    public void Nested_tables_keep_both_inner_and_outer_cell_boundaries()
    {
        var nested = new DocumentTable(0, 4,
            [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)]),
             new TableRow([new TableCell(2, 1, 0), new TableCell(3, 1, 1)])]);
        RichTextDocument document = RichTextDocument.FromPlainText("A\nB\nC\nD\nE")
            .WithTables([new DocumentTable(0, 5,
                [new TableRow([new TableCell(0, 4, 0, tables: [nested]), new TableCell(4, 1, 1)])])]);

        Assert.Equal("A\tB\nC\tD\tE", DocumentCommands.LabelledText(document));
    }
}
