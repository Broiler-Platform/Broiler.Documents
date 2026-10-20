namespace Broiler.Documents.Model.Tests;

/// <summary>
/// What editing does to a paragraph's mark.
/// </summary>
/// <remarks>
/// <see cref="RichTextParagraph.MarkStyle"/> belongs to the paragraph and not to
/// any run, so every edit that keeps the paragraph keeps it - including the ones
/// that take every run away, which is exactly when it is the only thing saying how
/// tall the line is.
/// </remarks>
public sealed class ParagraphMarkStyleTests
{
    private static readonly InlineStyle Mark = new() { FontFamily = "Arial Narrow", FontSize = 10f };

    private static RichTextParagraph Marked(string text) =>
        RichTextParagraph.Create(text, InlineStyle.Default).WithMarkStyle(Mark);

    [Fact]
    public void A_Paragraph_Has_No_Mark_Formatting_Unless_Given_One()
    {
        Assert.Equal(InlineStyle.Default, RichTextParagraph.Plain("text").MarkStyle);
        Assert.Equal(InlineStyle.Default, RichTextParagraph.Empty.MarkStyle);
    }

    [Fact]
    public void Text_Edits_Keep_The_Mark()
    {
        RichTextParagraph paragraph = Marked("text");

        Assert.Equal(Mark, paragraph.InsertText(4, " more", InlineStyle.Default).MarkStyle);
        Assert.Equal(Mark, paragraph.ApplyInlineStyle(0, 4, InlineStyleDelta.ToggleBold(true)).MarkStyle);
        Assert.Equal(Mark, paragraph.WithParagraphStyle(ParagraphStyle.Default with { SpacingAfter = 6f }).MarkStyle);
    }

    [Fact]
    public void Deleting_Every_Character_Keeps_The_Mark()
    {
        // The case the mark exists for: an empty paragraph has no run left to
        // take a size from.
        RichTextParagraph emptied = Marked("text").RemoveRange(0, 4);

        Assert.Equal(0, emptied.Length);
        Assert.Equal(Mark, emptied.MarkStyle);
    }

    [Fact]
    public void Splitting_Gives_Both_Halves_The_Mark()
    {
        (RichTextParagraph head, RichTextParagraph tail) = Marked("before after").SplitAt(6);

        Assert.Equal(Mark, head.MarkStyle);
        Assert.Equal(Mark, tail.MarkStyle);
    }

    [Fact]
    public void Merging_Keeps_The_First_Paragraph_Mark()
    {
        RichTextParagraph merged = Marked("first").Append(RichTextParagraph.Plain(" second"));

        Assert.Equal(Mark, merged.MarkStyle);
    }

    [Fact]
    public void Pressing_Enter_In_The_Document_Keeps_The_Mark_On_Both_Paragraphs()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs([Marked("before after")]);

        RichTextDocument split = document.InsertText(new RichTextPosition(0, 6), "\n").Document;

        Assert.Equal(2, split.ParagraphCount);
        Assert.All(split.Paragraphs, paragraph => Assert.Equal(Mark, paragraph.MarkStyle));
    }

    [Fact]
    public void Pasted_Lines_Take_The_Mark_Of_The_Paragraph_They_Land_In()
    {
        // The lines between the first and the last are new paragraphs, made from
        // the one the text went into, and are as much its halves as the two ends.
        RichTextDocument document = RichTextDocument.FromParagraphs([Marked("before after")]);

        RichTextDocument pasted = document.InsertText(new RichTextPosition(0, 6), "one\n\ntwo\n").Document;

        Assert.Equal(4, pasted.ParagraphCount);
        Assert.All(pasted.Paragraphs, paragraph => Assert.Equal(Mark, paragraph.MarkStyle));
    }
}
