namespace Broiler.Documents.Model.Tests;

/// <summary>
/// Confirms the model promotion (ADR 0002): the types live in the
/// <c>Broiler.Documents.Model</c> namespace/assembly and still behave. Exhaustive
/// behavioural coverage remains in the RichEdit test suites.
/// </summary>
public sealed class ModelPromotionTests
{
    [Fact]
    public void RichTextDocument_Lives_In_The_Promoted_Namespace_And_Assembly()
    {
        Assert.Equal("Broiler.Documents.Model", typeof(RichTextDocument).Namespace);
        Assert.Equal("Broiler.Documents.Model", typeof(RichTextDocument).Assembly.GetName().Name);
    }

    [Fact]
    public void FromPlainText_Splits_Paragraphs_And_Round_Trips()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("alpha\nbeta\ngamma");

        Assert.Equal(3, document.ParagraphCount);
        Assert.Equal("alpha\nbeta\ngamma", document.PlainText);
    }

    [Fact]
    public void InsertText_Then_ApplyBold_Produces_A_Styled_Run()
    {
        RichTextDocument document = RichTextDocument.Empty;
        RichTextEditResult inserted = document.InsertText(document.Start, "Hello");

        RichTextRange all = new(inserted.Document.Start, inserted.Document.End);
        RichTextDocument bold = inserted.Document.ApplyInlineStyle(all, new InlineStyleDelta { Bold = true });

        Assert.Equal("Hello", bold.PlainText);
        Assert.True(bold.InlineStyleAt(bold.End).Bold);
    }
}
