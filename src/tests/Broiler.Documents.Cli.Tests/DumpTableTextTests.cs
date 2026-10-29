using Broiler.Documents.Cli.Commands;

namespace Broiler.Documents.Cli.Tests;

public sealed class DumpTableTextTests
{
    [Fact]
    public void Text_Export_Keeps_Hidden_Source_Text_As_LibreOffice_Does()
    {
        var paragraph = RichTextParagraph.Plain("visible").InsertText(0, "secret", InlineStyle.Default with { Hidden = true });
        var document = RichTextDocument.FromParagraphs([paragraph]);
        Assert.Equal("secretvisible", document.PlainText);
        Assert.Equal("secretvisible", DocumentCommands.LabelledText(document));
    }

    [Fact]
    public void Anchored_Text_Box_Text_Is_Not_Part_Of_The_Body_Text()
    {
        // LibreOffice's text export walks the body and nothing anchored to it.
        var definition = new ListDefinition([new ListLevel { Template = "%1." }]);
        var itemStyle = ParagraphStyle.Default with { List = new ListReference(definition, 0), ListKind = ListKind.Numbered };
        var document = RichTextDocument.FromParagraphs([
            RichTextParagraph.Create("before", InlineStyle.Default, itemStyle),
            RichTextParagraph.Create("after", InlineStyle.Default, itemStyle)])
            .WithShapes([new DocumentShape(1, 0, 0, 100, 80,
                paragraphs: [RichTextParagraph.Create("A", InlineStyle.Default, itemStyle), RichTextParagraph.Plain("B")],
                tables: [new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])])])]);
        Assert.Equal("1. before\n2. after", DocumentCommands.LabelledText(document));
    }

    [Fact]
    public void A_Body_Of_One_Empty_Paragraph_Exports_Its_First_Text_Box_In_Its_Own_Flow()
    {
        // The one case LibreOffice exports a frame: nothing in the body but an
        // empty paragraph, and then only the first frame, tables and lists
        // numbered within it.
        var definition = new ListDefinition([new ListLevel { Template = "%1." }]);
        var itemStyle = ParagraphStyle.Default with { List = new ListReference(definition, 0), ListKind = ListKind.Numbered };
        var document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("")])
            .WithShapes(
            [
                new DocumentShape(0, 0, 0, 100, 80,
                    paragraphs: [RichTextParagraph.Create("A", InlineStyle.Default, itemStyle), RichTextParagraph.Plain("B")],
                    tables: [new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])])]),
                new DocumentShape(0, 0, 100, 100, 80, paragraphs: [RichTextParagraph.Plain("second")]),
            ]);
        Assert.Equal("1. A\tB", DocumentCommands.LabelledText(document));
    }

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
    public void Without_joined_rows_each_cell_is_a_line_as_before_LibreOffice_24_8()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("before\nA\nB\nC\nD\nafter")
            .WithTables([new DocumentTable(1, 4,
                [new TableRow([new TableCell(1, 1, 0), new TableCell(2, 1, 1)]),
                 new TableRow([new TableCell(3, 1, 0), new TableCell(4, 1, 1)])])]);

        Assert.Equal("before\nA\nB\nC\nD\nafter", DocumentCommands.LabelledText(document, joinsTableRows: false));
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
