namespace Broiler.Documents.Model.Tests;

public sealed class RichTextDocumentFactoryTests
{
    [Fact]
    public void FromParagraphs_Assembles_A_Document_Preserving_Styles()
    {
        RichTextParagraph one = RichTextParagraph.Create("one", InlineStyle.Default);
        RichTextParagraph two = RichTextParagraph.Create("two", new InlineStyle { Bold = true });

        RichTextDocument document = RichTextDocument.FromParagraphs(new[] { one, two });

        Assert.Equal(2, document.ParagraphCount);
        Assert.Equal("one\ntwo", document.PlainText);
        Assert.True(document.Paragraphs[1].StyleAt(0).Bold);
    }

    [Fact]
    public void FromParagraphs_Empty_Yields_A_Single_Empty_Paragraph()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(Array.Empty<RichTextParagraph>());

        Assert.Equal(1, document.ParagraphCount);
        Assert.Equal(string.Empty, document.PlainText);
    }

    [Fact]
    public void FromParagraphs_Rejects_Null()
    {
        Assert.Throws<ArgumentNullException>(() => RichTextDocument.FromParagraphs(null!));
    }
}
