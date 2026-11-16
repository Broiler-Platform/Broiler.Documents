using Broiler.Graphics.Imaging;
using Broiler.Graphics.Resources;

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
    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 1)]
    public void A_Tab_To_A_Default_Stop_Past_The_Edge_Starts_A_New_Line(bool tabsOverMargin, int lines)
    {
        // tdf142404_tabSpacing.docx: a leader stop near the edge, then a
        // default stop that still fits, then one past the edge, which
        // LibreOffice 24.2 moves to the next line (two tabs a line).
        var paragraph = RichTextParagraph.Create("\t\t\t\t\t\tx", InlineStyle.Default,
            ParagraphStyle.Default with { TabStops = new ParagraphTabStops([new(350, TabAlignment.Left, "_")]) });
        using var images = new ImageStore();
        LayoutResult result = new DocumentLayout(new LayoutSettings(), images).Layout(
            RichTextDocument.FromParagraphs([paragraph])
                .WithStyleDefaults(DocumentStyleDefaults.Default with { TabsOverMargin = tabsOverMargin }),
            new DocumentPageBox(400, 400, 10, 10, 10, 10));

        LayoutLine[] placed = [.. result.Pages.SelectMany(page => page.Lines)];
        Assert.Equal(lines, placed.Length);
        Assert.All(placed, line => Assert.Contains(line.Pieces, piece => piece.IsTab && piece.TabLeader == "_"));
        if (!tabsOverMargin)
            Assert.Contains(placed[^1].Pieces, piece => piece.Text == "x");
    }

    [Fact]
    public void A_Word_That_Does_Not_Fit_After_A_Row_Of_Tabs_Starts_The_Next_Row()
    {
        // tdf142404_tabOverMarginC15.docx: a leader tab to a stop at the very
        // edge, then text, which goes on below the leader.
        var paragraph = RichTextParagraph.Create("\tWas", InlineStyle.Default,
            ParagraphStyle.Default with { TabStops = new ParagraphTabStops([new(380, TabAlignment.Left, "_")]) });
        using var images = new ImageStore();
        LayoutResult result = new DocumentLayout(new LayoutSettings(), images).Layout(
            RichTextDocument.FromParagraphs([paragraph]), new DocumentPageBox(400, 400, 10, 10, 10, 10));

        LayoutLine[] placed = [.. result.Pages.SelectMany(page => page.Lines)];
        Assert.Equal(2, placed.Length);
        Assert.Contains(placed[0].Pieces, piece => piece.IsTab && piece.TabLeader == "_");
        Assert.Equal(10, placed[1].Pieces.First(piece => piece.Text == "Was").X, 3);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Spaces_Beside_Words_Leave_The_Line_Height_Where_The_Document_Says_So(bool ignores, bool tall)
    {
        // tdf117988.docx: 72 point spaces between 12 point words.
        var paragraph = RichTextParagraph.Create("The height", InlineStyle.Default with { FontSize = 12 }, ParagraphStyle.Default)
            .ApplyInlineStyle(3, 1, new InlineStyleDelta { SetFontSize = true, FontSize = 72 });
        LayoutLine line = LayoutOne(paragraph, DocumentStyleDefaults.Default with { IgnoresBlanksInLineHeight = ignores });

        Assert.Equal(tall, line.Height > 40);
    }

    [Fact]
    public void Spaces_Beside_Nothing_But_A_Picture_Still_Size_The_Line()
    {
        // tdf165359_SdtWithInline.docx: a run of large spaces before a small logo.
        var image = new InlineImage(BImageResource.FromPixels(new BPixelBuffer(1, 1, [0, 0, 0, 255])), width: 10, height: 10);
        var paragraph = RichTextParagraph.Create("  ", InlineStyle.Default with { FontSize = 72 }, ParagraphStyle.Default)
            .InsertText(2, InlineImage.PlaceholderText, new InlineStyle { Image = image });
        LayoutLine line = LayoutOne(paragraph, DocumentStyleDefaults.Default with { IgnoresBlanksInLineHeight = true });

        Assert.True(line.Height > 40);
    }

    [Fact]
    public void A_Line_Of_Only_A_Picture_Takes_The_Spacing_Past_Single_Of_The_Paragraph_Type()
    {
        // tdf109137.docx: LibreOffice 24.2 sets a 526 point picture 6.9 points
        // taller at 1.5 than at single in 12 point type.
        var image = new InlineImage(BImageResource.FromPixels(new BPixelBuffer(1, 1, [0, 0, 0, 255])), width: 100, height: 100);
        RichTextParagraph Picture(float spacing) => RichTextParagraph.Create(InlineImage.PlaceholderText, new InlineStyle { Image = image },
            ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Proportional, LineSpacing = spacing });
        RichTextParagraph text = RichTextParagraph.Create("x", InlineStyle.Default, ParagraphStyle.Default);

        double single = LayoutOne(Picture(1f), DocumentStyleDefaults.Default).Height;
        double spaced = LayoutOne(Picture(1.5f), DocumentStyleDefaults.Default).Height;
        double type = LayoutOne(text, DocumentStyleDefaults.Default).Height;

        Assert.Equal(100, single, 3);
        Assert.Equal(100 + (type / 2), spaced, 3);
    }

    [Fact]
    public void A_Paragraph_That_Continues_A_Kept_Run_Begun_Above_Does_Not_Move_By_Itself()
    {
        // defaultStyle.docx: the heading opening the page keeps with two empty
        // headings below it and those with a "Title" that does not fit; the run
        // cannot move from the page top, so only the "Title" goes on.
        ParagraphStyle kept = ParagraphStyle.Default with { KeepWithNext = true, LineHeightRule = LineHeightRule.Exact, LineHeight = 40 };
        ParagraphStyle plain = kept with { KeepWithNext = false };
        RichTextDocument document = RichTextDocument.FromParagraphs([
            RichTextParagraph.Create("a\u2028b", InlineStyle.Default, kept),
            RichTextParagraph.Create(string.Empty, InlineStyle.Default, kept),
            RichTextParagraph.Create(string.Empty, InlineStyle.Default, kept),
            RichTextParagraph.Create("Title", InlineStyle.Default, plain),
        ]);
        using var images = new ImageStore();
        LayoutResult result = new DocumentLayout(new LayoutSettings(), images).Layout(document,
            new DocumentPageBox(300, 200, 10, 10, 10, 10));

        Assert.Equal(new[] { 0, 0, 1, 2 }, result.Pages[0].Lines.Select(line => line.ParagraphIndex));
        Assert.Equal(3, Assert.Single(result.Pages[1].Lines).ParagraphIndex);
    }

    [Fact]
    public void A_Picture_Line_In_A_Text_Box_Keeps_Its_Height()
    {
        // test_msword_hang.docx: a frame its picture already overfills, set
        // 1.15, keeps the picture's line as it was and still draws it.
        var image = new InlineImage(BImageResource.FromPixels(new BPixelBuffer(1, 1, [0, 0, 0, 255])), width: 100, height: 100);
        RichTextParagraph picture = RichTextParagraph.Create(InlineImage.PlaceholderText, new InlineStyle { Image = image },
            ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Proportional, LineSpacing = 1.5f });
        using var images = new ImageStore();
        LayoutPage page = Assert.Single(new DocumentLayout(new LayoutSettings(), images).Layout(
            RichTextDocument.FromPlainText("body").WithShapes([new DocumentShape(0, 0, 0, 120, 90, paragraphs: [picture])]),
            new DocumentPageBox(600, 600, 10, 10, 10, 10)).Pages);

        LayoutLine line = Assert.Single(Assert.Single(page.Shapes).Lines);
        Assert.Equal(100, line.Height, 3);
    }

    private static LayoutLine LayoutOne(RichTextParagraph paragraph, DocumentStyleDefaults defaults)
    {
        using var images = new ImageStore();
        return Assert.Single(new DocumentLayout(new LayoutSettings(), images).Layout(
            RichTextDocument.FromParagraphs([paragraph]).WithStyleDefaults(defaults),
            new DocumentPageBox(600, 600, 10, 10, 10, 10)).Pages).Lines[0];
    }
}
