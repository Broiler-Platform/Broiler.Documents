namespace Broiler.Documents.Model.Tests;

/// <summary>
/// What editing does to a paragraph that starts a page.
/// </summary>
/// <remarks>
/// <see cref="ParagraphStyle.PageBreakBefore"/> is the one paragraph property
/// that is not a description of how the paragraph looks. Every other one applies
/// to both halves of a split because both halves look that way; a break says
/// where the paragraph starts, and after a split there are two paragraphs and
/// still only one start.
/// </remarks>
public sealed class ParagraphPageBreakTests
{
    private static RichTextParagraph Breaking(string text) =>
        RichTextParagraph.Create(
            text, InlineStyle.Default, ParagraphStyle.Default with { PageBreakBefore = true });

    [Fact]
    public void Splitting_Keeps_The_Break_On_The_Head_Only()
    {
        // Pressing Enter inside a paragraph that begins a page must not add a
        // second page break to the document. An edit that invented a break nobody
        // typed would be the worst kind of edit, and the copy that produced it is
        // the obvious way to write the split.
        (RichTextParagraph head, RichTextParagraph tail) = Breaking("before after").SplitAt(6);

        Assert.True(head.Style.PageBreakBefore);
        Assert.False(tail.Style.PageBreakBefore);
    }

    [Fact]
    public void Splitting_Carries_Every_Other_Paragraph_Property_To_Both_Halves()
    {
        // The rule above is an exception, so it is worth pinning that it is the
        // only one - a split that quietly dropped the alignment too would pass
        // the test above and be a different bug.
        var style = ParagraphStyle.Default with
        {
            Alignment = TextAlignment.Center,
            IndentLevel = 2,
            SpacingAfter = 6f,
            PageBreakBefore = true,
        };

        (RichTextParagraph head, RichTextParagraph tail) =
            RichTextParagraph.Create("before after", InlineStyle.Default, style).SplitAt(6);

        foreach (RichTextParagraph half in new[] { head, tail })
        {
            Assert.Equal(TextAlignment.Center, half.Style.Alignment);
            Assert.Equal(2, half.Style.IndentLevel);
            Assert.Equal(6f, half.Style.SpacingAfter);
        }
    }

    [Fact]
    public void Merging_Keeps_The_First_Paragraph_Break_And_Discards_The_Second()
    {
        // Two paragraphs becoming one leaves one start, and it is the first
        // paragraph's. Append already keeps the first paragraph's style, so this
        // pins the behaviour rather than changing it.
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [Breaking("first"), Breaking("second")]);

        RichTextDocument merged = document.MergeParagraphs(0).Document;

        Assert.Single(merged.Paragraphs);
        Assert.True(merged.Paragraphs[0].Style.PageBreakBefore);
    }

    [Fact]
    public void Merging_A_Breaking_Paragraph_Into_One_Without_A_Break_Drops_It()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Plain("first"), Breaking("second")]);

        RichTextDocument merged = document.MergeParagraphs(0).Document;

        Assert.False(merged.Paragraphs[0].Style.PageBreakBefore);
    }

    [Fact]
    public void The_Delta_Carries_The_Break_Both_Ways()
    {
        Assert.True(new ParagraphStyleDelta { PageBreakBefore = true }
            .Apply(ParagraphStyle.Default).PageBreakBefore);

        Assert.False(new ParagraphStyleDelta { PageBreakBefore = false }
            .Apply(ParagraphStyle.Default with { PageBreakBefore = true }).PageBreakBefore);
    }

    [Fact]
    public void A_Delta_That_Says_Nothing_Leaves_The_Break_Alone()
    {
        // The whole contract of a delta: null is unchanged, not false.
        Assert.True(default(ParagraphStyleDelta)
            .Apply(ParagraphStyle.Default with { PageBreakBefore = true }).PageBreakBefore);
    }

    [Fact]
    public void The_Default_Paragraph_Starts_No_Page()
    {
        Assert.False(ParagraphStyle.Default.PageBreakBefore);
    }
}
