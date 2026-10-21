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

    [Fact]
    public void A_Hard_Page_Break_In_A_Following_Section_With_Its_Own_Headers_Starts_Its_Pages()
    {
        // LibreOffice puts such a section's page style on the paragraph that
        // breaks the page inside it, with the first page sharing the others'
        // parts, since the section starts no page of its own.
        RunningContent annex = Header("the annex header")
            .WithDifferentFirstPage(true)
            .WithHeader(PageSelection.First, [RichTextParagraph.Plain("an annex first page")]);
        RichTextDocument document = RichTextDocument
            .FromParagraphs([Body("report"), Body("annex"), Body("annex, page two", pageBreakBefore: true)])
            .WithRunningContent(Header("the report header"))
            .WithSections(
            [
                new DocumentSection(0, 1, 1, 0),
                new DocumentSection(1, 2, 1, 0, runningContent: annex),
            ]);

        LayoutResult result = Layout(document);

        Assert.Equal(2, result.Pages.Count);
        Assert.True(Holds(result.Pages[0], "the report header"));
        Assert.True(Holds(result.Pages[1], "the annex header"));
        Assert.False(Holds(result.Pages[1], "an annex first page"));
    }

    [Fact]
    public void A_Narrow_Section_That_Follows_On_A_Page_Keeps_The_Pages_Margins()
    {
        // tdf90697_complexBreaksHeaders.docx: narrow two-column sections follow
        // each other on continuous breaks, each with right margins of its own.
        // Page four started in one, and its header was set in that section's
        // width and wrapped; LibreOffice keeps the page's margins and spreads
        // the section's columns across them.
        const string header = "alpha beta gamma delta epsilon zeta eta theta";
        var paragraphs = new List<RichTextParagraph> { Body("opening") };
        paragraphs.AddRange(Enumerable.Range(0, 120).Select(i => Body("line " + i)));
        var narrow = new PageGeometry(612, 792, 72, 400, 72, 72);

        RichTextDocument document = RichTextDocument.FromParagraphs(paragraphs)
            .WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72))
            .WithRunningContent(Header(header))
            .WithSections(
            [
                new DocumentSection(0, 1, 1, 0),
                new DocumentSection(1, paragraphs.Count - 1, 2, 12, narrow),
            ]);

        LayoutResult result = Layout(document);

        Assert.True(result.Pages.Count >= 2, "the narrow section did not run onto a second page");
        foreach (LayoutPage page in result.Pages)
        {
            // One header line, at the page's left margin.
            LayoutLine top = Assert.Single(page.Lines, line => line.Top < 72);
            Assert.Equal(header, string.Concat(top.Pieces.Select(piece => piece.Text)).Trim());
        }

        // The section's two columns share the page's 468 points: the second
        // starts past the middle of the page, where in its own 140 it would not.
        Assert.Contains(result.Pages[0].Lines, line => line.Top >= 72 && line.Pieces[0].X > 300);
    }
}
