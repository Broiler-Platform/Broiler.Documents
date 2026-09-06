using Broiler.Documents.Cli.Commands;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.Cli.Rendering;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// An explicit page break in the render layout.
/// </summary>
/// <remarks>
/// <para>
/// Found by the office conformance suite, which reported that a document with a
/// page break rendered as one page here against two in LibreOffice, through all
/// four interchange formats. Four formats agreeing ruled out a codec: nothing in
/// the model could carry a break, so every codec dropped one silently and there
/// was nothing for this layout to honour.
/// </para>
/// <para>
/// These tests are about the half of the fix that lives here - given a document
/// that states a break, the pages come out where the document said. Whether each
/// format's break reaches the model is that format's own tests.
/// </para>
/// </remarks>
public sealed class DocumentLayoutPageBreakTests
{
    private static RichTextDocument Document(params bool[] breakBefore) =>
        RichTextDocument.FromParagraphs(
            breakBefore.Select((wants, index) => RichTextParagraph.Create(
                "paragraph " + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                InlineStyle.Default,
                ParagraphStyle.Default with { PageBreakBefore = wants })));

    /// <summary>A continuous setup, built the way the command line builds one.</summary>
    private static PageSetup Continuous() => PageSetup.FromCommandLine(
        CommandLine.Parse(RenderCommand.Create().Spec, new[] { "in.docx", "--continuous" }));

    private static LayoutResult Layout(RichTextDocument document, PageSetup? setup = null)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, setup ?? PageSetup.Default);
    }

    [Fact]
    public void A_Break_Starts_A_New_Page()
    {
        // Two short paragraphs that would otherwise share a page.
        LayoutResult result = Layout(Document(false, true));

        Assert.Equal(2, result.Pages.Count);
        Assert.Single(result.Pages[0].Lines);
        Assert.Single(result.Pages[1].Lines);
    }

    [Fact]
    public void Without_A_Break_They_Share_A_Page()
    {
        // The other half of the assertion, and the one that stops the test above
        // passing for a reason that has nothing to do with the break.
        LayoutResult result = Layout(Document(false, false));

        Assert.Single(result.Pages);
        Assert.Equal(2, result.Pages[0].Lines.Count);
    }

    [Fact]
    public void A_Break_On_The_First_Paragraph_Does_Not_Open_An_Empty_Page()
    {
        // A document that opens with a break is asking to start on a fresh page,
        // and it is already on one. Emitting a blank page in front of it would be
        // honouring the letter of the break against its evident meaning.
        LayoutResult result = Layout(Document(true, false));

        Assert.Single(result.Pages);
    }

    [Fact]
    public void Every_Break_Is_Taken()
    {
        LayoutResult result = Layout(Document(false, true, true, true));

        Assert.Equal(4, result.Pages.Count);
        Assert.All(result.Pages, page => Assert.Single(page.Lines));
    }

    [Fact]
    public void Consecutive_Breaks_Each_Open_A_Page()
    {
        // Three paragraphs, the last two both asking to start a page. The middle
        // one occupies its own page and the last one starts another, which is what
        // both breaks said even though it leaves a page holding one line.
        LayoutResult result = Layout(Document(false, true, true));

        Assert.Equal(3, result.Pages.Count);
    }

    [Fact]
    public void A_Continuous_Render_Ignores_Breaks_And_Says_So()
    {
        // --continuous collapses the document to one tall page, so there is
        // nothing to break between. Doing nothing quietly would be the wrong kind
        // of quiet: the note is what tells a caller comparing two exports that the
        // pagination they asked to ignore was in fact ignored.
        LayoutResult result = Layout(
            Document(false, true, true),
            Continuous());

        Assert.Single(result.Pages);
        Assert.Contains(
            result.Notes,
            note => note.Contains("ignored 2 page break", StringComparison.Ordinal));
    }

    [Fact]
    public void A_Document_Without_Breaks_Gets_No_Note()
    {
        // A note nobody needs is noise, and this one appears in the render
        // manifest where a harness reads it.
        LayoutResult result = Layout(
            Document(false, false),
            Continuous());

        Assert.DoesNotContain(result.Notes, note => note.Contains("page break", StringComparison.Ordinal));
    }
}
