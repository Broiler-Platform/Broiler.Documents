using Broiler.Documents.Cli.Commands;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// LibreOffice's text export of a body that is one empty paragraph: it writes
/// the first frame the import made, and only that one
/// (<c>SwASCWriter::WriteStream</c>, office issue 249).
/// </summary>
public sealed class DumpFirstFrameTextTests
{
    private static RichTextDocument EmptyBody() => RichTextDocument.FromParagraphs([RichTextParagraph.Plain("")]);

    private static DocumentShape TextBox(string text, int paragraph = 0) =>
        new(paragraph, 0, 0, 100, 80, paragraphs: [RichTextParagraph.Plain(text)]);

    // A shape with no text box is a drawing to LibreOffice, and the DOCX
    // reader marks it as one.
    private static DocumentShape Drawing() => new(0, 0, 0, 100, 80) { IsCustomShape = true };

    [Fact]
    public void A_Drawing_Without_Text_Ahead_Of_A_Text_Box_Leaves_The_Export_Empty()
    {
        // fdo66688.docx: a rectangle, then a text box with "Abraham".
        RichTextDocument document = EmptyBody().WithShapes([Drawing(), TextBox("Abraham")]);

        Assert.Equal("", DocumentCommands.LabelledText(document));
    }

    [Fact]
    public void A_Drawing_Groups_First_Text_Box_Is_Its_First_Frame()
    {
        // dml-groupshape-runfonts.docx: the group's boxes are one object, and
        // its text boxes are made before it.
        RichTextDocument document = EmptyBody().WithShapes(
            [Drawing().InDrawingGroup(1), TextBox("inside").InDrawingGroup(1), TextBox("later")]);

        Assert.Equal("inside", DocumentCommands.LabelledText(document));
    }

    [Fact]
    public void A_Floating_Text_Box_In_The_Default_Header_Is_Exported_After_The_Even_Header()
    {
        // tdf120760_ZOrderInHeader.docx: the even header is read first but
        // holds nothing, the default header's text box is the first frame.
        RichTextDocument document = EmptyBody().WithRunningContent(RunningContent.Empty
            .WithFooter(PageSelection.Default, [RichTextParagraph.Plain("")], [TextBox("footer")])
            .WithHeader(PageSelection.Default, [RichTextParagraph.Plain("")], [TextBox("Blah blah blah")]));

        Assert.Equal("Blah blah blah", DocumentCommands.LabelledText(document));
    }

    [Fact]
    public void A_Text_Watermark_In_The_Header_Is_A_Drawing()
    {
        DocumentShape watermark = new(0, 0, 0, 100, 80, paragraphs: [RichTextParagraph.Plain("DRAFT")])
        {
            StretchesTextToBox = true,
        };
        RichTextDocument document = EmptyBody().WithRunningContent(RunningContent.Empty
            .WithHeader(PageSelection.Default, [RichTextParagraph.Plain("")], [watermark, TextBox("box")]));

        Assert.Equal("", DocumentCommands.LabelledText(document));
    }

    [Theory]
    [InlineData(false, "1")]
    [InlineData(true, "0")]
    public void A_Page_Number_In_A_Footer_Frame_Is_The_Page_That_Draws_It(bool differentFirstPage, string expected)
    {
        // tdf112446_frameStyle.docx writes "1"; tdf112443.docx, whose first
        // page has a footer of its own, "0" whatever number Word saved.
        var field = InlineStyle.Default with { PageField = new PageField(PageFieldKind.PageNumber) };
        DocumentShape frame = new(0, 0, 0, 20, 20, paragraphs: [RichTextParagraph.Create("2", field, ParagraphStyle.Default)]);
        RichTextDocument document = EmptyBody().WithRunningContent(RunningContent.Empty
            .WithDifferentFirstPage(differentFirstPage)
            .WithFooter(PageSelection.Default, [RichTextParagraph.Plain("")], [frame]));

        Assert.Equal(expected, DocumentCommands.LabelledText(document));
    }

    [Fact]
    public void A_Group_Is_Looked_For_In_Its_Own_Part_Only()
    {
        // The body's group of two drawings, with no text box, is the first
        // drawing made, so nothing is written. Each part numbers its groups
        // from one, and the default header's group 1 is another group.
        RichTextDocument document = EmptyBody()
            .WithShapes([Drawing().InDrawingGroup(1), Drawing().InDrawingGroup(1)])
            .WithRunningContent(RunningContent.Empty.WithHeader(PageSelection.Default, [RichTextParagraph.Plain("")],
                [Drawing().InDrawingGroup(1), TextBox("Header").InDrawingGroup(1)]));

        Assert.Equal("", DocumentCommands.LabelledText(document));
    }

    [Fact]
    public void A_Docx_Bodys_Shapes_Come_Before_Its_Headers()
    {
        // A DOCX's import makes the body's shapes before the headers' (whose
        // references the closing w:sectPr names), so a drawing in the default
        // header does not hide the body's text box. ODF makes them the other
        // way round, which applies where the source names its text frames.
        RichTextDocument document = EmptyBody()
            .WithShapes([TextBox("Body")])
            .WithRunningContent(RunningContent.Empty.WithHeader(PageSelection.Default, [RichTextParagraph.Plain("")],
                [Drawing()]));

        Assert.Equal("Body", DocumentCommands.LabelledText(document));
    }
}
