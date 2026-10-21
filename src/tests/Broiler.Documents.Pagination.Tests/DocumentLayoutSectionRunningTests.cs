namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Headers and footers per section: a page carries those of the section that
/// started it, the first-page part on that section's first page.
/// </summary>
/// <remarks>
/// <c>tdf167527_title_letters_cut_from_below.docx</c> sets its title page in its
/// first section's first-page header; the last section names an empty one of its
/// own and no <c>w:titlePg</c>. Read as the last section's alone, page one drew
/// nothing where LibreOffice draws the title.
/// </remarks>
public sealed class DocumentLayoutSectionRunningTests
{
    /// <summary>US Letter with one-inch margins.</summary>
    private static readonly DocumentPageBox Letter = new(612, 792, 72, 72, 72, 72);

    private static RunningContent Header(string text) =>
        RunningContent.Empty.WithHeader(PageSelection.Default, [RichTextParagraph.Plain(text)]);

    private static RichTextParagraph Body(string text, bool pageBreakBefore = false) =>
        RichTextParagraph.Create(
            text, InlineStyle.Default, ParagraphStyle.Default with { PageBreakBefore = pageBreakBefore });

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, Letter);
    }

    private static bool Holds(LayoutPage page, string text) =>
        page.Lines.Any(line => string.Concat(line.Pieces.Select(piece => piece.Text)).Contains(text, StringComparison.Ordinal));

    [Fact]
    public void Each_Page_Carries_The_Header_Of_The_Section_That_Started_It()
    {
        RichTextDocument document = RichTextDocument
            .FromParagraphs([Body("one"), Body("two", pageBreakBefore: true)])
            .WithRunningContent(Header("the last section's header"))
            .WithSections(
            [
                new DocumentSection(0, 1, 1, 0, runningContent: Header("the first section's header")),
                new DocumentSection(1, 1, 1, 0),
            ]);

        LayoutResult result = Layout(document);

        Assert.Equal(2, result.Pages.Count);
        Assert.True(Holds(result.Pages[0], "the first section's header"));
        Assert.False(Holds(result.Pages[0], "the last section's header"));
        Assert.True(Holds(result.Pages[1], "the last section's header"));
    }

    [Fact]
    public void A_Sections_First_Page_Part_Is_For_Its_First_Page_Not_The_Documents()
    {
        RunningContent second = Header("running head")
            .WithDifferentFirstPage(true)
            .WithHeader(PageSelection.First, [RichTextParagraph.Plain("chapter opening")]);
        var paragraphs = new List<RichTextParagraph> { Body("cover") };
        paragraphs.Add(Body("chapter", pageBreakBefore: true));
        paragraphs.AddRange(Enumerable.Range(0, 120).Select(i => Body("line " + i)));

        RichTextDocument document = RichTextDocument.FromParagraphs(paragraphs)
            .WithRunningContent(second)
            .WithSections(
            [
                new DocumentSection(0, 1, 1, 0, runningContent: RunningContent.Empty),
                new DocumentSection(1, paragraphs.Count - 1, 1, 0),
            ]);

        LayoutResult result = Layout(document);

        Assert.True(result.Pages.Count >= 3, "the second section did not run onto a page of its own");
        Assert.False(Holds(result.Pages[0], "chapter opening"));
        Assert.False(Holds(result.Pages[0], "running head"));
        Assert.True(Holds(result.Pages[1], "chapter opening"));
        Assert.False(Holds(result.Pages[1], "running head"));
        Assert.True(Holds(result.Pages[2], "running head"));
    }

    [Fact]
    public void A_Section_That_Follows_On_The_Same_Page_Does_Not_Change_The_Pages_It_Runs_Onto()
    {
        // A continuous section starts no page, and LibreOffice keeps the page
        // style of the one that did on the pages its text runs onto.
        var paragraphs = new List<RichTextParagraph> { Body("opening") };
        paragraphs.AddRange(Enumerable.Range(0, 120).Select(i => Body("line " + i)));

        RichTextDocument document = RichTextDocument.FromParagraphs(paragraphs)
            .WithRunningContent(Header("the last section's header"))
            .WithSections(
            [
                new DocumentSection(0, 1, 1, 0, runningContent: Header("the first section's header")),
                new DocumentSection(1, paragraphs.Count - 1, 1, 0),
            ]);

        LayoutResult result = Layout(document);

        Assert.True(result.Pages.Count >= 2, "the text did not run onto a second page");
        Assert.All(result.Pages, page => Assert.True(Holds(page, "the first section's header")));
        Assert.All(result.Pages, page => Assert.False(Holds(page, "the last section's header")));
    }
}
