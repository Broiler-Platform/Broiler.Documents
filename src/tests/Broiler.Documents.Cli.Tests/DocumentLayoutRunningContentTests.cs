using Broiler.Documents.Cli.Commands;
using Broiler.Graphics;
using Broiler.Documents.Cli.Infrastructure;
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

    [Fact]
    public void A_Continuous_Render_Draws_No_Running_Content()
    {
        // Continuous collapses the document to one tall page. There is no page for
        // a header to repeat on, and no bottom margin for a footer to sit in.
        PageSetup continuous = PageSetup.FromCommandLine(
            CommandLine.Parse(RenderCommand.Create().Spec, new[] { "in.docx", "--continuous" }));

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
                Enumerable.Range(0, 40).Select(_ => RichTextParagraph.Plain(tall)).ToList()));

        LayoutResult result = Layout(document);

        Assert.Null(Line(result, tall));
        Assert.Contains(result.Notes, note => note.Contains("taller than its page margin", StringComparison.Ordinal));
    }
}
