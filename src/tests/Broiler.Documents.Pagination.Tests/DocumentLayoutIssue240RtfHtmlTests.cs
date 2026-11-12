namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Office conformance issue #240 (rtf-html): a header paragraph whose mark is
/// hidden is drawn as one with the paragraph after it, as Writer draws it
/// (layout/data/min-wtf.rtf).
/// </summary>
public sealed class DocumentLayoutIssue240RtfHtmlTests
{
    private static readonly InlineStyle Hidden = InlineStyle.Default with { Hidden = true };

    private static LayoutLine[] HeaderLines(params RichTextParagraph[] header)
    {
        PageGeometry page = new(300, 300, 20, 20, 50, 20, HeaderDistance: 10);
        RichTextDocument document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("body")])
            .WithPageGeometry(page)
            .WithRunningContent(RunningContent.Empty.WithHeader(PageSelection.Default, header));
        using var images = new ImageStore();
        LayoutPage laid = Assert.Single(new DocumentLayout(new LayoutSettings(), images)
            .Layout(document, new DocumentPageBox(300, 300, 50, 20, 20, 20)).Pages);
        return [.. laid.Lines.Where(line => line.ParagraphIndex < 0)];
    }

    private static string Text(LayoutLine line) => string.Concat(line.Pieces.Select(piece => piece.Text));

    [Fact]
    public void A_Hidden_Mark_Joins_The_Paragraph_To_The_Next_With_The_First_Ones_Formatting()
    {
        RichTextParagraph first = RichTextParagraph.Create("AAA", InlineStyle.Default,
            ParagraphStyle.Default with { Alignment = TextAlignment.Center }).WithMarkStyle(Hidden);
        RichTextParagraph second = RichTextParagraph.Create("BBB", InlineStyle.Default,
            ParagraphStyle.Default with { Alignment = TextAlignment.Right, SpacingAfter = 40 });

        LayoutLine line = Assert.Single(HeaderLines(first, second));

        Assert.Equal("AAABBB", Text(line));
        // Centred, as the first paragraph says, not set against the right.
        double left = line.Pieces[0].X;
        double right = line.Pieces[^1].X + line.Pieces[^1].Width;
        Assert.Equal(280 - right, left - 20, 1);
    }

    [Fact]
    public void A_Wholly_Hidden_Paragraph_With_Text_Still_Lends_Its_Formatting()
    {
        RichTextParagraph first = RichTextParagraph.Create("gone", Hidden,
            ParagraphStyle.Default with { Alignment = TextAlignment.Center }).WithMarkStyle(Hidden);
        RichTextParagraph second = RichTextParagraph.Create("BBB", InlineStyle.Default,
            ParagraphStyle.Default with { Alignment = TextAlignment.Right });

        LayoutLine line = Assert.Single(HeaderLines(first, second));

        Assert.Equal("BBB", Text(line).Trim());
        Assert.True(line.Pieces.First(piece => piece.Text.Length > 0).X < 200);
    }

    [Fact]
    public void An_Empty_Paragraph_With_A_Hidden_Mark_Joins_Nothing()
    {
        RichTextParagraph first = RichTextParagraph.Create(string.Empty, InlineStyle.Default,
            ParagraphStyle.Default with { Alignment = TextAlignment.Center }).WithMarkStyle(Hidden);
        RichTextParagraph second = RichTextParagraph.Create("BBB", InlineStyle.Default,
            ParagraphStyle.Default with { Alignment = TextAlignment.Right });

        LayoutLine line = Assert.Single(HeaderLines(first, second));

        LayoutPiece last = line.Pieces[^1];
        Assert.Equal(280, last.X + last.Width, 1);
    }
}
