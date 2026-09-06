using Broiler.Documents.Cli.Rendering;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// The forced line break inside a paragraph, which the model spells U+2028 and
/// every codec reads and writes - <c>text:line-break</c>, <c>w:br</c>,
/// <c>\line</c>, <c>&lt;br&gt;</c>.
/// </summary>
/// <remarks>
/// It used to reach layout and do nothing. The tokenizer classified characters
/// with <c>char.IsWhiteSpace</c>, which is true for U+2028, so a break became an
/// ordinary break <em>opportunity</em>: a five-line address reflowed onto two
/// wrapped lines and everything under it moved up the page. Nothing caught it,
/// because a text projection maps U+2028 to a newline on both sides before
/// comparing - the office suite's <c>line-break-within-paragraph</c> seed exists
/// to put the assertion where it can be made, on the render.
/// </remarks>
public sealed class DocumentLayoutLineBreakTests
{
    private const char Break = '\u2028';

    private static LayoutResult Layout(string text)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images)
            .Layout(RichTextDocument.FromPlainText(text), PageSetup.Default);
    }

    private static List<string> Lines(LayoutResult result) =>
        result.Pages
            .SelectMany(page => page.Lines)
            .Select(line => string.Concat(line.Pieces.Select(piece => piece.Text)).Trim())
            .ToList();

    [Fact(Timeout = 600000)]
    public void A_Break_Ends_The_Line_With_Room_To_Spare()
    {
        // Five short lines that would otherwise fit two or three to a row, which
        // is what makes a lost break visible rather than merely wrong.
        List<string> lines = Lines(Layout(
            "Sender Name" + Break + "Broiler Platform" + Break + "12 Example Street" +
            Break + "00000 Example City" + Break + "Example State"));

        Assert.Equal(
            ["Sender Name", "Broiler Platform", "12 Example Street", "00000 Example City", "Example State"],
            lines);
    }

    [Fact(Timeout = 600000)]
    public void A_Break_Is_Not_A_Space()
    {
        // The regression itself, stated as the difference it makes: the same two
        // words with a break between them take two lines, with a space one.
        Assert.Equal(2, Lines(Layout("alpha" + Break + "bravo")).Count);
        Assert.Single(Lines(Layout("alpha bravo")));
    }

    [Fact(Timeout = 600000)]
    public void Two_Breaks_In_A_Row_Leave_The_Blank_Line_Between_Them()
    {
        List<string> lines = Lines(Layout("alpha" + Break + Break + "bravo"));

        Assert.Equal(["alpha", string.Empty, "bravo"], lines);
    }

    [Fact(Timeout = 600000)]
    public void A_Break_Draws_No_Glyph_Of_Its_Own()
    {
        // U+2028 has no glyph in any pinned face, so one reaching a piece would
        // draw as a box or as nothing and measure as neither.
        bool present = Layout("alpha" + Break + "bravo").Pages
            .SelectMany(page => page.Lines)
            .SelectMany(line => line.Pieces)
            .Any(piece => piece.Text.Contains(Break));

        Assert.False(present, "the break character should not survive into a drawn piece");
    }

    [Fact(Timeout = 600000)]
    public void A_Break_Still_Wraps_What_Does_Not_Fit()
    {
        // A forced break ends a line early; it does not turn wrapping off for the
        // rest of the paragraph.
        string wide = string.Join(' ', Enumerable.Repeat("wide", 200));
        List<string> lines = Lines(Layout("short" + Break + wide));

        Assert.Equal("short", lines[0]);
        Assert.True(lines.Count > 2, "the long half should still have wrapped");
    }
}
