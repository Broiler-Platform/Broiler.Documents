using System.Text;
using Broiler.Graphics;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// A wrapping shape stops at the foot of its own page.
/// </summary>
/// <remarks>
/// <para>
/// The exclusions a shape contributes are page-local: they are held as a
/// distance down from the head of the page, and the writer's <c>y</c> restarts
/// at that head on every page. So an exclusion left in the list after a page
/// ends lands at the same place on the next one, and pushes text aside on a page
/// with nothing beside it.
/// </para>
/// <para>
/// The CLI layout documents this exact defect on
/// <c>DocumentLayout.BreakPage</c>, where it was found while fixing something
/// else and fixed by emptying the list. Neither engine had a test for it - the
/// PDF writer had three places that start a page and none of them emptied
/// anything.
/// </para>
/// </remarks>
public sealed class PdfWrapPageResetTests
{
    /// <summary>Long enough to run past a page on its own.</summary>
    private const string Body =
        "The quick brown fox jumps over the lazy dog and keeps on running for " +
        "long enough to need several lines of text on the page. It carries on " +
        "past the bottom of the shape so that some of its lines are beside the " +
        "box and some of them are below it, which is the whole of what wrapping " +
        "has to get right. A few more words make the difference plain. ";

    /// <summary>
    /// A box down the left of the column, taller than any page it can sit on, so
    /// every line it reaches is pushed clear of it and none is pushed by
    /// accident of where a page happened to break.
    /// </summary>
    private static DocumentShape LeftBox(ShapeWrap wrap) =>
        new(0, 0, 0, 150, 2000, ShapeFill.Solid(BColor.Black), wrap: wrap);

    /// <summary>
    /// Enough separate paragraphs to run past a page. Separate, not one long
    /// one: a paragraph is line-broken in a single pass before any of it is
    /// placed, so its own tail lines keep the bands it started with whatever
    /// happens to pages underneath them. What a page break can put right is the
    /// state the <em>next</em> paragraph is measured against.
    /// </summary>
    private static RichTextDocument Overflowing(ShapeWrap wrap) =>
        RichTextDocument
            .FromParagraphs(Enumerable.Range(0, 40).Select(_ => RichTextParagraph.Plain(Body)))
            .WithShapes([LeftBox(wrap)]);

    /// <summary>One paragraph long enough to run past a page on its own.</summary>
    private static RichTextDocument Straddling(ShapeWrap wrap) =>
        RichTextDocument
            .FromParagraphs([RichTextParagraph.Plain(string.Concat(Enumerable.Repeat(Body, 12)))])
            .WithShapes([LeftBox(wrap)]);

    /// <summary>
    /// The shape's paragraph, then a second one that opens a page of its own, so
    /// the line under test is the first on page two whatever the shape did to
    /// page one.
    /// </summary>
    private static RichTextDocument Broken(ShapeWrap wrap) =>
        RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Plain("first"),
            RichTextParagraph.Create(
                "SECONDPAGE", InlineStyle.Default,
                ParagraphStyle.Default with { PageBreakBefore = true }),
        ]).WithShapes([LeftBox(wrap)]);

    private static string Write(RichTextDocument document)
    {
        using var stream = new MemoryStream();
        new PdfDocumentCodec().WritePdf(document, stream, new PdfWriteOptions(compressStreams: false));
        return Encoding.Latin1.GetString(stream.ToArray());
    }

    [Fact(Timeout = 600000)]
    public void A_Wrap_Does_Not_Follow_The_Text_Onto_The_Next_Page()
    {
        // The line is the first on page two either way, so the two runs are the
        // same text in the same place and the only variable is the shape.
        double wrapped = XOf(Write(Broken(ShapeWrap.Square)), "SECONDPAGE");
        double plain = XOf(Write(Broken(ShapeWrap.None)), "SECONDPAGE");

        Assert.Equal(plain, wrapped, 3);
    }

    [Fact(Timeout = 600000)]
    public void The_Wrap_Still_Applies_On_The_Page_The_Shape_Is_On()
    {
        // The other half, and the one that stops the test above passing because
        // the shape wraps nothing at all.
        double wrapped = XOf(Write(Broken(ShapeWrap.Square)), "first");
        double plain = XOf(Write(Broken(ShapeWrap.None)), "first");

        Assert.True(wrapped > plain + 100, $"the shape moved the line to {wrapped} from {plain}");
    }

    [Fact(Timeout = 600000)]
    public void An_Overflowing_Document_Regains_Its_Column_After_The_Break()
    {
        // The other page-starting site: no explicit break, just text running out
        // of room. The box is taller than a page, so with the exclusion carried
        // forward every line in the document is indented and the column's own
        // left edge never appears again after page one.
        List<double> wrapped = RunXs(Write(Overflowing(ShapeWrap.Square)));
        double column = RunXs(Write(Overflowing(ShapeWrap.None))).Min();

        Assert.Contains(wrapped, x => Math.Abs(x - column) < 0.5);
        Assert.Contains(wrapped, x => x > column + 100);
    }

    [Fact(Timeout = 600000)]
    public void A_Paragraph_That_Straddles_A_Break_Keeps_Its_Bands()
    {
        // The limit of the reset, pinned rather than left to be met by surprise
        // - I wrote the test above against one long paragraph first, and it
        // failed with the fix in place.
        //
        // A paragraph is line-broken in a single pass before any of it is
        // placed, so a page that starts in the middle of one cannot re-measure
        // the lines it already has: every line keeps the exclusions it was
        // broken against, on whatever page it lands. Emptying the list at a page
        // boundary cannot reach that, because the boundary is found afterwards.
        // Breaking a paragraph against the page it will sit on is the shared
        // paginator's job - roadmap 11.1 - not a reset's.
        List<double> straddling = RunXs(Write(Straddling(ShapeWrap.Square)));
        double column = RunXs(Write(Straddling(ShapeWrap.None))).Min();

        Assert.DoesNotContain(straddling, x => Math.Abs(x - column) < 0.5);
    }

    /// <summary>The x of the text matrix that placed <paramref name="text"/>.</summary>
    private static double XOf(string content, string text)
    {
        int at = content.IndexOf("(" + text, StringComparison.Ordinal);
        Assert.True(at > 0, $"{text} was not drawn");

        string before = content[..at];
        int tm = before.LastIndexOf(" Tm", StringComparison.Ordinal);
        Assert.True(tm > 0, $"no text matrix preceded {text}");

        return Operand(before[..tm], 2);
    }

    /// <summary>The x of every text matrix in the file, in order.</summary>
    private static List<double> RunXs(string content)
    {
        var found = new List<double>();
        int at = 0;
        while ((at = content.IndexOf(" Tm", at, StringComparison.Ordinal)) >= 0)
        {
            found.Add(Operand(content[..at], 2));
            at += 3;
        }

        Assert.NotEmpty(found);
        return found;
    }

    /// <summary>The nth operand from the end of a text matrix: 1 is y, 2 is x.</summary>
    private static double Operand(string upToOperator, int fromEnd)
    {
        string[] parts = upToOperator.Split('\n')[^1].Split(' ');
        return double.Parse(parts[^fromEnd], System.Globalization.CultureInfo.InvariantCulture);
    }
}
