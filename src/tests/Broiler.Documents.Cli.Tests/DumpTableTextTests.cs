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
    public void A_Floating_Tables_Cells_Are_Not_Part_Of_The_Body_Text()
    {
        // LibreOffice imports a positioned table as a text frame holding it,
        // and its text export leaves frames out (floattable-center-shift-down.docx).
        RichTextDocument document = RichTextDocument.FromPlainText("A\nB\nbefore\nC\nD\nafter")
            .WithTables(
            [
                new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])], position: Floating),
                new DocumentTable(3, 2, [new TableRow([new TableCell(3, 1, 0), new TableCell(4, 1, 1)])]),
            ]);

        Assert.Equal("before\nC\tD\nafter", DocumentCommands.LabelledText(document));
        Assert.Equal("before\nC\nD\nafter", DocumentCommands.LabelledText(document, joinsTableRows: false));
    }

    [Fact]
    public void A_Body_Of_One_Empty_Paragraph_Exports_Its_Floating_Table()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("A\nB\nC\nD\n")
            .WithTables([new DocumentTable(0, 4,
                [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)]),
                 new TableRow([new TableCell(2, 1, 0), new TableCell(3, 1, 1)])], position: Floating)]);

        Assert.Equal("A\tB\nC\tD", DocumentCommands.LabelledText(document));
    }

    [Fact]
    public void A_Floating_Table_Is_Exported_Before_A_Text_Box()
    {
        // LibreOffice's first frame is the table's, even with a text box in a
        // cell (tdf170516_drawingBeforePlainText.docx, tdf78727.docx).
        RichTextDocument document = RichTextDocument.FromPlainText("A\nB\n")
            .WithTables([new DocumentTable(0, 2,
                [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])], position: Floating)])
            .WithShapes([new DocumentShape(0, 0, 0, 100, 80, paragraphs: [RichTextParagraph.Plain("box")])]);

        Assert.Equal("A\tB", DocumentCommands.LabelledText(document));
    }

    [Fact]
    public void A_Drawing_With_No_Text_Before_The_Text_Box_Leaves_The_Export_Empty()
    {
        // LibreOffice exports only the first of the document's frames and
        // drawings; a rectangle or a line before the text frame has no text to
        // give (n825305.rtf, tdf88811.rtf, tdf104936.rtf).
        RichTextDocument document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("")])
            .WithShapes(
            [
                new DocumentShape(0, 0, 0, 100, 80),
                new DocumentShape(0, 0, 100, 100, 80, paragraphs: [RichTextParagraph.Plain("Frame1")]),
            ]);
        Assert.Equal(string.Empty, DocumentCommands.LabelledText(document));
    }

    [Fact]
    public void A_Groups_Text_Box_Is_Exported_With_The_Group()
    {
        // Shapes that share a stated stacking order, as a group's do, are one
        // drawing to LibreOffice, whose text boxes it makes, and exports, before
        // the group's own drawing (dml-groupshape-runfonts.docx).
        RichTextDocument document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("")])
            .WithShapes(
            [
                new DocumentShape(0, 0, 0, 100, 80, zOrder: 251660288) { IsCustomShape = true },
                new DocumentShape(0, 10, 10, 80, 60, paragraphs: [RichTextParagraph.Plain("grouped")], zOrder: 251660288),
            ]);
        Assert.Equal("grouped", DocumentCommands.LabelledText(document));
    }

    private static readonly TablePosition Floating = new(TableAnchor.Page, 0, TableAnchor.Page, 100, TableAlignment.Center);

    private static DocumentShape Box(string? text, bool drawing = false, bool inline = false, int group = 0) =>
        new(0, 0, 0, 100, 80, paragraphs: text is null ? null : [RichTextParagraph.Plain(text)])
        {
            IsCustomShape = drawing,
            InlineInSource = inline,
            GroupNumber = group,
        };

    [Fact]
    public void A_Form_Controls_Characters_Are_Not_Text()
    {
        // tdf126626.docx: an ActiveX check box drawn as its box and caption.
        RichTextParagraph paragraph = RichTextParagraph.Plain("before after")
            .InsertText(7, "☐ Check Box ", InlineStyle.Default with { IsFormControl = true });

        Assert.Equal("before after", DocumentCommands.LabelledText(RichTextDocument.FromParagraphs([paragraph])));
    }

    [Fact]
    public void A_Paragraph_Holding_A_Shape_As_A_Character_Is_Not_Empty()
    {
        // inline-textbox-position.docx, tdf117188.docx: LibreOffice anchors the
        // text box as a character, so the body is not one empty paragraph and
        // its text export writes nothing.
        RichTextDocument document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("")])
            .WithShapes([Box("SHORT BOX TEXT", inline: true)]);

        Assert.Equal("", DocumentCommands.LabelledText(document));
    }

    [Fact]
    public void A_Drawing_Before_A_Text_Box_Leaves_The_Export_Empty()
    {
        // tdf117028.docx: a rounded rectangle, then a text box. The first frame
        // LibreOffice's export finds is the rectangle's drawing, which has no
        // text of its own.
        RichTextDocument document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("")])
            .WithShapes([Box(null, drawing: true), Box("Hello")]);

        Assert.Equal("", DocumentCommands.LabelledText(document));
    }

    [Theory]
    [InlineData("member", "member")]
    [InlineData(null, "")]
    public void A_Groups_First_Text_Box_Comes_Before_Its_Drawing(string? firstBox, string expected)
    {
        // dml-groupshape-capitalization.docx: a plain member, then a text box;
        // the box is the first frame. dml-groupshape-sdt.docx: the first text
        // box is empty, so nothing is written.
        RichTextDocument document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("")])
            .WithShapes([Box(null, drawing: true, group: 1), Box(firstBox, group: 1), Box("second", group: 1)]);

        Assert.Equal(expected, DocumentCommands.LabelledText(document));
    }

    [Theory]
    [InlineData(false, "1")]
    [InlineData(true, "0")]
    public void A_Page_Number_In_A_Footer_Frame_Is_The_First_Pages_Or_Nought(bool differentFirstPage, string expected)
    {
        // tdf112446_frameStyle.docx writes 1 for its footer frame's PAGE field,
        // tdf112443.docx, whose first page has a footer of its own, 0; the
        // saved results are 1 and 2.
        RichTextParagraph number = RichTextParagraph.Plain("").InsertText(0, "2",
            InlineStyle.Default with { PageField = new PageField(PageFieldKind.PageNumber) });
        RichTextDocument document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("")])
            .WithRunningContent(RunningContent.Empty
                .WithFooter(PageSelection.Default, null, [new DocumentShape(0, 0, 0, 20, 20, paragraphs: [number])])
                .WithDifferentFirstPage(differentFirstPage));

        Assert.Equal(expected, DocumentCommands.LabelledText(document));
    }

    [Fact]
    public void A_Header_Text_Box_Is_Exported_When_The_Body_Has_No_Frame()
    {
        // n773061.docx: the only frame is a text box in the header; a text
        // watermark there is a drawing and is passed over.
        RichTextDocument document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("")])
            .WithRunningContent(RunningContent.Empty.WithHeader(PageSelection.Default, null,
                [new DocumentShape(0, 0, 0, 100, 80, paragraphs: [RichTextParagraph.Plain("DRAFT")]) { StretchesTextToBox = true },
                 new DocumentShape(0, 0, 0, 100, 80, paragraphs: [RichTextParagraph.Plain("Line1"), RichTextParagraph.Plain("Line2")])]));

        Assert.Equal("Line1\nLine2", DocumentCommands.LabelledText(document));
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
