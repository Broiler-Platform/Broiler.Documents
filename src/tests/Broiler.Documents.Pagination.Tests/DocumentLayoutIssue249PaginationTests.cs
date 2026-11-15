namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Issue #249, DOCX page counts: the space proportional line spacing adds
/// below the last line on a page may run past the page's bottom, as
/// LibreOffice 24.2 lays a DOCX out (calendar3.docx, tdf90810short.docx).
/// </summary>
public sealed class DocumentLayoutIssue249PaginationTests
{
    private static readonly ParagraphStyle Double = ParagraphStyle.Default with
    {
        LineHeightRule = LineHeightRule.Proportional,
        LineSpacing = 2f,
    };

    private static RichTextParagraph Para(string text) =>
        RichTextParagraph.Create(text, InlineStyle.Default, Double);

    private static LayoutResult Layout(RichTextDocument document, double height)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(300, height, 10, 10, 10, 10));
    }

    /// <summary>The height of one line set double, and of its text alone.</summary>
    private static (double Line, double Text) Measure()
    {
        LayoutLine line = Layout(RichTextDocument.FromParagraphs([Para("x")]), 400).Pages[0].Lines[0];
        return (line.Height, line.Height / 2);
    }

    [Theory]
    [InlineData(true, 3)]
    [InlineData(false, 2)]
    public void The_Last_Line_On_A_Page_Needs_Room_Only_For_Its_Text(bool runsPast, int linesOnFirstPage)
    {
        (double line, double text) = Measure();
        // Room for two lines and the third's text, not for its spacing.
        double height = 20 + (2 * line) + text + 1;
        RichTextDocument document = RichTextDocument.FromParagraphs([Para("a"), Para("b"), Para("c"), Para("d")])
            .WithStyleDefaults(new DocumentStyleDefaults { ProportionalSpacingRunsPastBottom = runsPast });

        LayoutResult result = Layout(document, height);

        Assert.Equal(linesOnFirstPage, result.Pages[0].Lines.Count);
        Assert.Equal(4, result.Pages.Sum(page => page.Lines.Count));
    }

    [Fact]
    public void A_Line_Inside_A_Paragraph_May_Also_End_The_Page_On_Its_Text()
    {
        (double line, double text) = Measure();
        double height = 20 + (2 * line) + text + 1;
        RichTextDocument document = RichTextDocument.FromParagraphs([Para("a\u2028b\u2028c\u2028d\u2028e")])
            .WithStyleDefaults(new DocumentStyleDefaults { ProportionalSpacingRunsPastBottom = true });

        LayoutResult result = Layout(document, height);

        Assert.Equal(new[] { 3, 2 }, result.Pages.Select(page => page.Lines.Count));
    }

    [Fact]
    public void Widow_Control_Still_Counts_The_Line_That_Ends_The_Page()
    {
        // Four lines with two kept together at the end: three would fit, but
        // the fourth would be left alone, so two go on.
        (double line, double text) = Measure();
        double height = 20 + (2 * line) + text + 1;
        RichTextParagraph paragraph = RichTextParagraph.Create("a\u2028b\u2028c\u2028d", InlineStyle.Default,
            Double with { WidowLines = 2, OrphanLines = 2 });
        RichTextDocument document = RichTextDocument.FromParagraphs([paragraph])
            .WithStyleDefaults(new DocumentStyleDefaults { ProportionalSpacingRunsPastBottom = true });

        LayoutResult result = Layout(document, height);

        Assert.Equal(new[] { 2, 2 }, result.Pages.Select(page => page.Lines.Count));
    }
}
