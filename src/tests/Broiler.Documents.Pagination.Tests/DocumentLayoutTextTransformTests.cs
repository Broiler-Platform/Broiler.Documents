namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentLayoutTextTransformTests
{
    [Theory]
    [InlineData(TextCapitalization.Capitalize, "hello world", "Hello World", null)]
    [InlineData(TextCapitalization.Capitalize, "istanbul izmir", "İstanbul İzmir", "tr-TR")]
    [InlineData(TextCapitalization.Lowercase, "ISTANBUL İZMİR", "ıstanbul izmir", "tr-TR")]
    [InlineData(TextCapitalization.Lowercase, "HELLO WORLD", "hello world", null)]
    public void Transforms_Display_Text_Across_Style_Runs(TextCapitalization capitalization, string source, string expected, string? language)
    {

        InlineStyle style = InlineStyle.Default with { Capitalization = capitalization, Language = language };
        RichTextParagraph paragraph = RichTextParagraph.Create(source, style)
            .ApplyInlineStyle(2, 3, InlineStyleDelta.ToggleBold(true));
        using var images = new ImageStore();
        var document = RichTextDocument.FromParagraphs([paragraph]);
        var page = new DocumentPageBox(612, 792, 72, 72, 72, 72);
        var line = Assert.Single(new DocumentLayout(new LayoutSettings(), images).Layout(document, page).Pages[0].Lines);

        Assert.Equal(expected, string.Concat(line.Pieces.Select(piece => piece.Text)));
        Assert.Equal(source, document.PlainText);
    }
}
