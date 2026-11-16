namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// The text dump leaves out the string of a drawing set in the line
/// (<see cref="InlineStyle.IsShapeText"/>), as LibreOffice's text export does
/// for an inline WordArt shape (fdo77727.docx's "Currents").
/// </summary>
public sealed class TextDumpShapeTextTests
{
    [Fact]
    public void An_Inline_WordArt_String_Is_Not_Paragraph_Text()
    {
        RichTextParagraph paragraph = RichTextParagraph.Empty
            .InsertText(0, "Currents", InlineStyle.Default with { IsShapeText = true })
            .InsertText(8, "Combat", InlineStyle.Default);
        RichTextDocument document = RichTextDocument.FromParagraphs([paragraph, RichTextParagraph.Plain("after")]);

        Assert.Equal("Combat\nafter", Broiler.Documents.Cli.Commands.DocumentCommands.LabelledText(document));
    }
}
