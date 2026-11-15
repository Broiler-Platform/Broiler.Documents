namespace Broiler.Documents.Pagination.Tests;

/// <summary>
/// Office conformance issue #249, ODT pixel similarity: lines of register-true
/// paragraphs set on the page's line register (the pagelinespacing samples),
/// and the space above an opening paragraph that starts the page with its own
/// break (multipleParagraphs.fodt's title).
/// </summary>
public sealed class DocumentLayoutIssue249OdtPixelTests
{
    private static readonly DocumentPageBox Page = new(400, 600, 50, 20, 20, 20);

    private static LayoutResult Layout(RichTextDocument document)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, Page);
    }

    private static RichTextDocument Document(LineRegister? register, params RichTextParagraph[] paragraphs) =>
        RichTextDocument.FromParagraphs(paragraphs)
            .WithPageGeometry(new PageGeometry(400, 600, 20, 20, 50, 20) { LineRegister = register });

    private static RichTextParagraph Line(string text, float size, bool registerTrue, ParagraphStyle? style = null) =>
        RichTextParagraph.Create(text, InlineStyle.Default with { FontSize = size },
            (style ?? ParagraphStyle.Default) with { RegisterTrue = registerTrue });

    /// <summary>A line's baseline as Writer counts it: its line gap above the text.</summary>
    private static double WriterBaseline(LayoutLine line) =>
        line.Top + line.Baseline + line.Pieces.Max(piece => piece.LineGap);

    private static readonly LineRegister Register20 =
        new(InlineStyle.Default with { FontSize = 20 }, LineHeightRule.Proportional, 1f, 0f);

    [Fact]
    public void Register_True_Lines_Are_Set_A_Register_Pitch_Apart()
    {
        // A 10 pt line on a 20 pt register takes a whole register line, as
        // Writer grows it in SwTextFormatter::CalcRealHeight.
        LayoutResult free = Layout(Document(Register20, Line("one", 10, false), Line("two", 10, false)));
        LayoutResult held = Layout(Document(Register20, Line("one", 10, true), Line("two", 10, true), Line("three", 10, true)));
        LayoutResult big = Layout(Document(null, Line("x", 20, false), Line("y", 20, false)));

        IReadOnlyList<LayoutLine> lines = held.Pages[0].Lines;
        double pitch = big.Pages[0].Lines[1].Top - big.Pages[0].Lines[0].Top;
        Assert.Equal(pitch, WriterBaseline(lines[1]) - WriterBaseline(lines[0]), 3);
        Assert.Equal(pitch, WriterBaseline(lines[2]) - WriterBaseline(lines[1]), 3);
        Assert.True(free.Pages[0].Lines[1].Top - free.Pages[0].Lines[0].Top < pitch - 1);
    }

    [Fact]
    public void The_First_Register_Line_Is_The_Reference_Fonts_Ascent_Below_The_Body_Top()
    {
        LayoutLine reference = Layout(Document(null, Line("x", 20, false))).Pages[0].Lines[0];
        LayoutLine small = Layout(Document(Register20, Line("x", 10, true))).Pages[0].Lines[0];

        // Both baselines are the 20 pt font's ascent and line gap below the
        // top of the body (SwTextFrame::FillRegister's RegStart).
        Assert.Equal(WriterBaseline(reference), WriterBaseline(small), 3);
        Assert.True(small.Top > 50);
    }

    [Fact]
    public void A_Paragraph_That_Is_Not_Register_True_Or_A_Page_Without_A_Register_Keeps_Its_Lines()
    {
        LayoutLine plain = Layout(Document(null, Line("x", 10, false))).Pages[0].Lines[0];
        LayoutLine off = Layout(Document(Register20, Line("x", 10, false))).Pages[0].Lines[0];
        LayoutLine noRegister = Layout(Document(null, Line("x", 10, true))).Pages[0].Lines[0];

        Assert.Equal(50, plain.Top, 3);
        Assert.Equal(50, off.Top, 3);
        Assert.Equal(50, noRegister.Top, 3);
    }

    [Fact]
    public void An_Exact_Reference_Height_Is_The_Pitch()
    {
        var exact = new LineRegister(InlineStyle.Default with { FontSize = 10 }, LineHeightRule.Exact, 1f, 30f);
        IReadOnlyList<LayoutLine> lines = Layout(Document(exact, Line("one", 10, true), Line("two", 10, true))).Pages[0].Lines;

        Assert.Equal(30, WriterBaseline(lines[1]) - WriterBaseline(lines[0]), 3);
        // The first baseline is four fifths of the fixed height down.
        Assert.Equal(50 + 24, WriterBaseline(lines[0]), 3);
    }

    [Fact]
    public void The_Opening_Paragraph_Keeps_Its_Space_Above_When_It_Asks_For_The_Page_Itself()
    {
        ParagraphStyle spaced = ParagraphStyle.Default with { SpacingBefore = 30, PageBreakBefore = true };
        RichTextDocument Opening(bool keeps) =>
            RichTextDocument.FromParagraphs([RichTextParagraph.Create("title", InlineStyle.Default,
                spaced with { PageBreakKeepsSpaceAbove = keeps })])
            .WithStyleDefaults(DocumentStyleDefaults.Default with { PreserveOpeningParagraphSpacing = true });

        Assert.Equal(80, Layout(Opening(true)).Pages[0].Lines[0].Top, 3);
        Assert.Equal(50, Layout(Opening(false)).Pages[0].Lines[0].Top, 3);
    }

    [Fact]
    public void Where_Every_Page_Break_Keeps_The_Space_Above_A_Later_Break_Keeps_It_Too()
    {
        // ODT: Writer keeps the space above a paragraph whose own break opens
        // the page, the first and any later one (SwFlowFrame::HasParaSpaceAtPages),
        // and drops it where the text merely flows onto a new page.
        RichTextDocument Document(bool keeps) =>
            RichTextDocument.FromParagraphs([
                RichTextParagraph.Create("first", InlineStyle.Default, ParagraphStyle.Default with { SpacingBefore = 30, PageBreakBefore = true }),
                RichTextParagraph.Create("second", InlineStyle.Default, ParagraphStyle.Default with { SpacingBefore = 30, PageBreakBefore = true })])
            .WithStyleDefaults(DocumentStyleDefaults.Default with { PreserveOpeningParagraphSpacing = true, PageBreaksKeepSpaceAbove = keeps });

        LayoutResult kept = Layout(Document(true));
        LayoutResult dropped = Layout(Document(false));

        Assert.Equal(80, kept.Pages[0].Lines[0].Top, 3);
        Assert.Equal(80, kept.Pages[1].Lines[0].Top, 3);
        Assert.Equal(50, dropped.Pages[0].Lines[0].Top, 3);
        Assert.Equal(50, dropped.Pages[1].Lines[0].Top, 3);
    }

    [Fact]
    public void A_Turned_Shape_Set_As_A_Character_Stands_In_Its_Line_By_The_Box_Round_It()
    {
        // tdf70838.odt: a 100 x 50 rectangle turned by a quarter stands 50 wide
        // and 100 tall in its line, its turned box's corner at the line's.
        var shape = new DocumentShape(0, 0, 0, 100, 50) { IsAsCharacter = true, RotationRadians = -Math.PI / 2 };
        RichTextDocument document = RichTextDocument.FromParagraphs([RichTextParagraph.Create(string.Empty, InlineStyle.Default)])
            .WithShapes([shape]);

        LayoutPage page = Assert.Single(Layout(document).Pages);
        LayoutShape placed = Assert.Single(page.Shapes);
        Assert.True(page.Lines[0].Height >= 100 - 0.01);
        // Turned a quarter backwards about its top left corner, the box runs
        // up from that corner: the corner sits a box's width below the top.
        Assert.Equal(20, placed.Bounds.X, 3);
        Assert.Equal(50 + 100, placed.Bounds.Y, 3);
    }
}
