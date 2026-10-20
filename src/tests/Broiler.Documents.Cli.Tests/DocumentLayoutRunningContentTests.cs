using Broiler.Documents.Cli.Commands;
using Broiler.Graphics.Color;
using Broiler.Documents.Cli.Rendering;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// Headers and footers in the render layout. They repeat in the page margins
/// rather than flowing with the body, so they are laid out per page once the
/// body has decided how many pages there are.
/// </summary>
public sealed class DocumentLayoutRunningContentTests
{
    private static RichTextDocument WithRunning(
        string body,
        string? header,
        string? footer,
        DocumentShape? headerShape = null)
    {
        RichTextDocument document = RichTextDocument.FromPlainText(body);
        RunningContent running = RunningContent.Empty;
        if (header is not null || headerShape is not null)
        {
            running = running.WithHeader(
                PageSelection.Default,
                header is null ? null : [RichTextParagraph.Plain(header)],
                headerShape is null ? null : [headerShape]);
        }

        if (footer is not null)
            running = running.WithFooter(PageSelection.Default, [RichTextParagraph.Plain(footer)]);
        return document.WithRunningContent(running);
    }

    /// <summary>A stripe 20 points down the page, in the top margin.</summary>
    private static DocumentShape Stripe() =>
        new(0, 0, 20, 120, 8, ShapeFill.Solid(BColor.FromArgb(0xFF, 0xAE, 0xCF, 0x00)));

    private static LayoutResult Layout(RichTextDocument document, PageSetup? setup = null)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images)
            .Layout(document, setup ?? PageSetup.Default);
    }

    /// <summary>The first line whose pieces together hold <paramref name="text"/>.</summary>
    private static LayoutLine? Line(LayoutResult result, string text) =>
        result.Pages
            .SelectMany(page => page.Lines)
            .FirstOrDefault(line =>
                string.Concat(line.Pieces.Select(piece => piece.Text))
                    .Contains(text, StringComparison.Ordinal));

    [Fact]
    public void A_Header_Shape_Is_Drawn_On_The_Page_It_Belongs_To()
    {
        // It used to be anchored to a body paragraph and drawn wherever that
        // paragraph happened to land, which on page two was nowhere.
        LayoutResult result = Layout(WithRunning("body", "letterhead", null, Stripe()));

        LayoutShape stripe = Assert.Single(Assert.Single(result.Pages).Shapes);
        Assert.Equal(20, stripe.Bounds.Top, 1);
        Assert.Equal(120, stripe.Bounds.Width, 1);
    }

    [Fact]
    public void A_Header_Shape_Repeats_On_Every_Page()
    {
        // (char)10 rather than an escape: a newline is what separates paragraphs here.
        string body = string.Join((char)10, Enumerable.Range(0, 400).Select(i => "line " + i));
        LayoutResult result = Layout(WithRunning(body, null, null, Stripe()));

        Assert.True(result.Pages.Count > 1, "the body did not run to a second page");
        Assert.All(result.Pages, page => Assert.Single(page.Shapes));
        // Measured from the top of each page, so every copy lands in the same place.
        Assert.All(result.Pages, page => Assert.Equal(20, page.Shapes[0].Bounds.Top, 1));
    }

    [Fact]
    public void A_Header_Sits_Above_The_Body_In_The_Top_Margin()
    {
        LayoutResult result = Layout(WithRunning("body", "letterhead", null));

        LayoutLine header = Assert.IsType<LayoutLine>(Line(result, "letterhead"));
        LayoutLine body = Assert.IsType<LayoutLine>(Line(result, "body"));

        Assert.True(header.Top >= 0, "the header was placed off the top of the page");
        Assert.True(
            header.Top + header.Height <= result.Setup.ContentTopPoints,
            $"the header at {header.Top} ran into the body column at {result.Setup.ContentTopPoints}");
        Assert.True(header.Top < body.Top);
    }

    [Fact]
    public void A_Footer_Sits_Below_The_Body_In_The_Bottom_Margin()
    {
        LayoutResult result = Layout(WithRunning("body", null, "page one"));

        LayoutLine footer = Assert.IsType<LayoutLine>(Line(result, "page one"));
        double contentBottom = result.Setup.ContentTopPoints + result.Setup.ContentHeightPoints;

        Assert.True(
            footer.Top >= contentBottom,
            $"the footer at {footer.Top} overlapped the body column ending at {contentBottom}");
        Assert.True(footer.Top + footer.Height <= result.Setup.HeightPoints);
    }

    [Fact]
    public void The_Body_Is_Unchanged_By_Running_Content()
    {
        LayoutResult plain = Layout(RichTextDocument.FromPlainText("body"));
        LayoutResult decorated = Layout(WithRunning("body", "letterhead", "page one"));

        LayoutLine before = Assert.IsType<LayoutLine>(Line(plain, "body"));
        LayoutLine after = Assert.IsType<LayoutLine>(Line(decorated, "body"));

        // A header takes its space from the margin, not from the text column.
        Assert.Equal(before.Top, after.Top, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(36)]
    public void A_Docx_Header_Starts_At_Its_Declared_Distance_From_The_Page_Edge(double distance)
    {
        RichTextDocument document = WithRunning("body", "letterhead", null)
            .WithPageGeometry(PageGeometry.A4 with { HeaderDistance = distance });
        using var stream = new MemoryStream();
        var codec = new Broiler.Documents.Docx.DocxDocumentCodec();
        codec.Write(document, stream);
        stream.Position = 0;
        LayoutResult result = Layout(codec.Read(stream).Document);
        LayoutLine header = Assert.IsType<LayoutLine>(Line(result, "letterhead"));

        Assert.Equal(distance, header.Top, 3);
    }

    [Fact]
    public void Multiple_Header_Lines_Are_Anchored_As_One_Block()
    {
        RichTextDocument document = WithRunning("body", "first\u2028last", null)
            .WithPageGeometry(PageGeometry.A4 with { HeaderDistance = 12 });
        LayoutResult result = Layout(document);
        LayoutLine first = Assert.IsType<LayoutLine>(Line(result, "first"));
        LayoutLine last = Assert.IsType<LayoutLine>(Line(result, "last"));
        Assert.Equal(12, first.Top, 3);
        Assert.Equal(first.Top + first.Height, last.Top, 3);
    }

    [Theory]
    [InlineData(72)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_Header_That_Cannot_Fit_At_Its_Declared_Distance_Is_Reported(double distance)
    {
        LayoutResult result = Layout(WithRunning("body", "letterhead", null)
            .WithPageGeometry(PageGeometry.A4 with { HeaderDistance = distance }));
        Assert.Null(Line(result, "letterhead"));
        Assert.Contains(result.Notes, note => note.Contains("declared distance", StringComparison.Ordinal));
        Assert.NotNull(Line(result, "body"));
    }

    [Fact]
    public void A_Reused_Layout_Does_Not_Keep_The_Previous_Documents_Header_Distance()
    {
        using var images = new ImageStore();
        var layout = new DocumentLayout(new LayoutSettings(), images);
        RichTextDocument plain = WithRunning("body", "letterhead", null);
        layout.Layout(plain.WithPageGeometry(PageGeometry.A4 with { HeaderDistance = 12 }), PageSetup.Default);
        LayoutResult result = layout.Layout(plain, PageSetup.Default);
        LayoutLine header = Assert.IsType<LayoutLine>(Line(result, "letterhead"));
        Assert.Equal(result.Setup.MarginTopPoints / 2, header.Top + header.Height / 2, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(36)]
    public void A_Docx_Footer_Ends_At_Its_Declared_Distance_From_The_Page_Edge(double distance)
    {
        var geometry = new PageGeometry(600, 800, 72, 72, 72, 72, 24, distance);
        RichTextDocument document = WithRunning("body", null, "page number")
            .WithPageGeometry(geometry);
        using var stream = new MemoryStream();
        var codec = new Broiler.Documents.Docx.DocxDocumentCodec();
        codec.Write(document, stream);
        stream.Position = 0;
        RichTextDocument imported = codec.Read(stream).Document;
        LayoutResult result = Layout(imported);
        LayoutLine footer = Assert.IsType<LayoutLine>(Line(result, "page number"));

        Assert.Equal(result.Setup.HeightPoints - distance, footer.Top + footer.Height, 3);
    }

    [Fact]
    public void Multiple_Footer_Lines_Are_Anchored_As_One_Block()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("body")
            .WithPageGeometry(PageGeometry.A4 with { FooterDistance = 12 })
            .WithRunningContent(RunningContent.Empty.WithFooter(PageSelection.Default,
                [RichTextParagraph.Plain("footer first"), RichTextParagraph.Plain("footer last")]));
        LayoutResult result = Layout(document);
        LayoutLine first = Assert.IsType<LayoutLine>(Line(result, "footer first"));
        LayoutLine last = Assert.IsType<LayoutLine>(Line(result, "footer last"));
        Assert.Equal(first.Top + first.Height, last.Top, 3);
        Assert.Equal(result.Setup.HeightPoints - 12, last.Top + last.Height, 3);
    }

    [Fact]
    public void A_Footer_That_Cannot_Fit_At_Its_Distance_Is_Reported()
    {
        RichTextDocument document = WithRunning("body", null, "footer")
            .WithPageGeometry(PageGeometry.A4 with { FooterDistance = 72 });
        LayoutResult result = Layout(document);
        Assert.Null(Line(result, "footer"));
        Assert.Contains(result.Notes, note => note.Contains("footer", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void An_Invalid_Footer_Distance_Does_Not_Produce_Invalid_Coordinates(double distance)
    {
        LayoutResult result = Layout(WithRunning("body", null, "footer")
            .WithPageGeometry(PageGeometry.A4 with { FooterDistance = distance }));
        Assert.Null(Line(result, "footer"));
        Assert.Contains(result.Notes, note => note.Contains("declared distance", StringComparison.Ordinal));
    }

    [Fact]
    public void A_Reused_Layout_Does_Not_Keep_The_Previous_Documents_Footer_Distance()
    {
        using var images = new ImageStore();
        var layout = new DocumentLayout(new LayoutSettings(), images);
        RichTextDocument plain = WithRunning("body", null, "footer");
        layout.Layout(plain.WithPageGeometry(PageGeometry.A4 with { FooterDistance = 12 }), PageSetup.Default);
        LayoutResult result = layout.Layout(plain, PageSetup.Default);
        LayoutLine footer = Assert.IsType<LayoutLine>(Line(result, "footer"));
        Assert.Equal(result.Setup.HeightPoints - result.Setup.MarginBottomPoints / 2,
            footer.Top + footer.Height / 2, 3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Running_Paragraph_Spacing_Is_Included_In_Placement_And_Anchoring(bool header)
    {
        RichTextParagraph[] paragraphs =
        [
            RichTextParagraph.Plain("first").WithParagraphStyle(ParagraphStyle.Default with
                { SpacingBefore = 100, SpacingAfter = 7 }),
            RichTextParagraph.Plain("last").WithParagraphStyle(ParagraphStyle.Default with
                { SpacingBefore = 5, SpacingAfter = 11 }),
        ];
        RunningContent running = header
            ? RunningContent.Empty.WithHeader(PageSelection.Default, paragraphs)
            : RunningContent.Empty.WithFooter(PageSelection.Default, paragraphs);
        LayoutResult result = Layout(RichTextDocument.FromPlainText("body")
            .WithPageGeometry(PageGeometry.A4 with { HeaderDistance = 12, FooterDistance = 12 })
            .WithRunningContent(running));
        LayoutLine first = Assert.IsType<LayoutLine>(Line(result, "first"));
        LayoutLine last = Assert.IsType<LayoutLine>(Line(result, "last"));

        Assert.Equal(first.Top + first.Height + 12, last.Top, 3);
        if (header)
            Assert.Equal(12, first.Top, 3);
        else
            Assert.Equal(result.Setup.HeightPoints - 12 - 11, last.Top + last.Height, 3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Running_Paragraph_Spacing_Is_Counted_When_Checking_The_Margin_Fit(bool header)
    {
        RichTextParagraph[] paragraphs =
        [
            RichTextParagraph.Plain("first").WithParagraphStyle(ParagraphStyle.Default with { SpacingAfter = 72 }),
            RichTextParagraph.Plain("last"),
        ];
        RunningContent running = header
            ? RunningContent.Empty.WithHeader(PageSelection.Default, paragraphs)
            : RunningContent.Empty.WithFooter(PageSelection.Default, paragraphs);
        LayoutResult result = Layout(RichTextDocument.FromPlainText("body").WithRunningContent(running));

        Assert.Null(Line(result, "first"));
        Assert.Null(Line(result, "last"));
        Assert.NotNull(Line(result, "body"));
        Assert.Contains(result.Notes, note => note.Contains("taller than its page margin", StringComparison.Ordinal));
    }

    [Fact]
    public void A_Continuous_Render_Draws_No_Running_Content()
    {
        // Continuous collapses the document to one tall page. There is no page for
        // a header to repeat on, and no bottom margin for a footer to sit in.
        PageSetup continuous = PageSetup.FromCommandLine(
            CommandLine.Parse(RenderCommand.Create().Spec, ["in.docx", "--continuous"]));

        LayoutResult result = Layout(WithRunning("body", "letterhead", "page one"), continuous);

        Assert.True(continuous.Continuous, "the setup under test must be continuous");
        Assert.Null(Line(result, "letterhead"));
        Assert.Null(Line(result, "page one"));
    }

    [Fact]
    public void A_Header_Taller_Than_Its_Margin_Is_Reported_Rather_Than_Drawn()
    {
        var tall = new string('x', 40);
        RichTextDocument document = RichTextDocument.FromPlainText("body").WithRunningContent(
            RunningContent.Empty.WithHeader(
                PageSelection.Default,
                [.. Enumerable.Range(0, 40).Select(_ => RichTextParagraph.Plain(tall))]));

        LayoutResult result = Layout(document);

        Assert.Null(Line(result, tall));
        Assert.Contains(result.Notes, note => note.Contains("taller than its page margin", StringComparison.Ordinal));
    }

    [Fact]
    public void A_Different_First_Page_Draws_No_Footer_Where_The_Document_States_None()
    {
        // The letterhead: a header of its own on page one, a page number from
        // page two on, and nothing under the letterhead. Both pages used to get
        // the footer, because an empty first-page slot fell back to the default.
        var document = RichTextDocument
            .FromParagraphs([
                RichTextParagraph.Plain("page one"),
                RichTextParagraph.Plain("page two")
                    .WithParagraphStyle(ParagraphStyle.Default with { PageBreakBefore = true }),
            ])
            .WithRunningContent(RunningContent.Empty
                .WithDifferentFirstPage(true)
                .WithHeader(PageSelection.First, [RichTextParagraph.Plain("letterhead")])
                .WithFooter(PageSelection.Default, [RichTextParagraph.Plain("page number")]));

        LayoutResult result = Layout(document);
        string TextOn(int page) => string.Concat(result.Pages[page].Lines
            .SelectMany(line => line.Pieces)
            .Select(piece => piece.Text));

        Assert.Equal(2, result.Pages.Count);
        Assert.Contains("letterhead", TextOn(0), StringComparison.Ordinal);
        Assert.DoesNotContain("page number", TextOn(0), StringComparison.Ordinal);
        Assert.Contains("page number", TextOn(1), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_The_Flag_The_First_Page_Still_Takes_The_Default_Footer()
    {
        // The fallback is the rule and the flag is the exception, not the other
        // way round: a document that names one footer wants it on every page.
        var document = RichTextDocument
            .FromPlainText("body")
            .WithRunningContent(RunningContent.Empty
                .WithFooter(PageSelection.Default, [RichTextParagraph.Plain("page number")]));

        string text = string.Concat(Layout(document).Pages[0].Lines
            .SelectMany(line => line.Pieces)
            .Select(piece => piece.Text));

        Assert.Contains("page number", text, StringComparison.Ordinal);
    }
}
