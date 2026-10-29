namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Page fields in headers and footers: each page draws its own number, not the
/// one the document saved, and a section that restarts the count starts it again.
/// </summary>
/// <remarks>
/// <c>fdo114306_2.odt</c> saved "3" in the page number of a header that heads its
/// second and its fourth page; the approval sheet that starts on the third page
/// restarts the count, so LibreOffice draws "- 2 -" on both.
/// </remarks>
public sealed class DocumentLayoutPageFieldTests
{
    /// <summary>A small page: a text area 160 points square below a 40-point header band.</summary>
    private static readonly DocumentPageBox Small = new(200, 240, 20, 20, 40, 40);

    private static RichTextParagraph Line(string text, ParagraphStyle? style = null) =>
        RichTextParagraph.Create(text, InlineStyle.Default, style ?? ParagraphStyle.Default);

    /// <summary>"- 9 -" with the 9 a page field saved as 9.</summary>
    private static RichTextParagraph Field(PageField field, string saved = "9") =>
        RichTextParagraph.Plain("- ")
            .InsertText(2, saved, InlineStyle.Default with { PageField = field })
            .InsertText(2 + saved.Length, " -", InlineStyle.Default);

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, Small);
    }

    /// <summary>The text of each page's header, page by page.</summary>
    private static string[] Headers(LayoutResult result) =>
        [.. result.Pages.Select(page => string.Concat(page.Lines
            .Where(line => line.ParagraphIndex < 0 && line.Top < Small.MarginTopPoints)
            .SelectMany(line => line.Pieces).Select(piece => piece.Text)))];

    private static RichTextDocument Body(int lines, params RichTextParagraph[] header) =>
        RichTextDocument.FromParagraphs([.. Enumerable.Range(1, lines).Select(i => Line("Line " + i))])
            .WithRunningContent(RunningContent.Empty.WithHeader(PageSelection.Default, header));

    [Fact]
    public void Each_Page_Draws_Its_Own_Number_In_Place_Of_The_Saved_One()
    {
        LayoutResult result = Layout(Body(30, Field(new PageField(PageFieldKind.PageNumber))));

        Assert.True(result.Pages.Count >= 3);
        Assert.Equal(
            Enumerable.Range(1, result.Pages.Count).Select(number => "- " + number + " -"),
            Headers(result));
    }

    [Fact]
    public void A_Section_That_Restarts_The_Count_Numbers_Its_First_Page_From_There()
    {
        RichTextDocument document = Body(40, Field(new PageField(PageFieldKind.PageNumber)));
        RichTextParagraph[] paragraphs = [.. document.Paragraphs];
        paragraphs[10] = paragraphs[10].WithParagraphStyle(ParagraphStyle.Default with { PageBreakBefore = true });
        document = RichTextDocument.FromParagraphs(paragraphs)
            .WithRunningContent(document.RunningContent)
            .WithSections(
            [
                new DocumentSection(0, 10, 1, 0),
                new DocumentSection(10, 30, 1, 0, breakType: SectionBreakType.NextPage, pageNumberStart: 1),
            ]);

        LayoutResult result = Layout(document);
        string[] headers = Headers(result);
        int restart = result.Pages.First(page => page.Lines.Any(line => line.ParagraphIndex == 10)).Number;

        Assert.True(restart > 1);
        Assert.Equal("- " + (restart - 1) + " -", headers[restart - 2]);
        Assert.Equal("- 1 -", headers[restart - 1]);
        Assert.Equal("- 2 -", headers[restart]);
    }

    [Fact]
    public void A_Page_Count_Field_Shows_How_Many_Pages_The_Document_Has()
    {
        RichTextParagraph header = RichTextParagraph.Plain("of ")
            .InsertText(3, "1", InlineStyle.Default with { PageField = new PageField(PageFieldKind.PageCount) });

        LayoutResult result = Layout(Body(30, header));

        Assert.All(Headers(result), text => Assert.Equal("of " + result.Pages.Count, text));
    }

    [Fact]
    public void A_Field_Writes_The_Number_In_Its_Format()
    {
        LayoutResult result = Layout(Body(30, Field(new PageField(PageFieldKind.PageNumber, ListNumberFormat.UpperRoman))));

        Assert.Equal(["- I -", "- II -"], Headers(result).Take(2));
    }

    [Fact]
    public void A_Field_Adds_Its_Adjustment_To_The_Number()
    {
        LayoutResult result = Layout(Body(30, Field(new PageField(PageFieldKind.PageNumber, Adjust: 1))));

        Assert.Equal(["- 2 -", "- 3 -"], Headers(result).Take(2));
    }

    [Fact]
    public void A_Number_Adjusted_To_A_Page_That_Is_Not_There_Shows_Nothing()
    {
        // fdo-style "continued on page N" footers: LibreOffice leaves the
        // previous page's number off the first page and the next page's off
        // the last.
        LayoutResult previous = Layout(Body(30, Field(new PageField(PageFieldKind.PageNumber, Adjust: -1), saved: "x")));
        LayoutResult next = Layout(Body(30, Field(new PageField(PageFieldKind.PageNumber, Adjust: 1), saved: "x")));

        Assert.Equal("-  -", Headers(previous)[0]);
        Assert.Equal("- 1 -", Headers(previous)[1]);
        Assert.Equal("- 2 -", Headers(next)[0]);
        Assert.Equal("-  -", Headers(next)[^1]);
    }
}
