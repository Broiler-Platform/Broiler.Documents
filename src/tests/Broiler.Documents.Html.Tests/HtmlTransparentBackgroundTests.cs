using System.Text;
using Broiler.Graphics;

namespace Broiler.Documents.Html.Tests;

/// <summary>
/// A transparent background is no background.
/// </summary>
/// <remarks>
/// <para>
/// The model spells "no background" as <see cref="BColor.Empty"/>, which is a
/// sentinel distinct from a real colour whose alpha happens to be zero. Reading
/// <c>background: transparent</c> as the colour rather than as the absence made
/// every such run a highlighted run - it counts in <c>info</c>'s
/// <c>highlightedRuns</c>, and it survives into every writer that emits a
/// highlight, as a highlight nobody applied.
/// </para>
/// <para>
/// It cost almost nothing while only an inline <c>style</c> attribute could say
/// it. Then type-selector rules arrived, and LibreOffice writes
/// <c>background: transparent</c> in the <c>p</c> rule of every HTML document it
/// produces - so a latent wrong answer became one on every paragraph of every
/// such file. That is how it was found: a table document read back with all
/// eight of its runs highlighted.
/// </para>
/// </remarks>
public sealed class HtmlTransparentBackgroundTests
{
    [Fact(Timeout = 600000)]
    public void Transparent_In_A_Type_Rule_Is_Not_A_Highlight()
    {
        // The LibreOffice shape, and the one that made this worth fixing.
        RichTextDocument document = Read(
            "<html><head><style>p { color: #000000; background: transparent }</style></head>" +
            "<body><p>text</p></body></html>");

        Assert.True(OnlyStyle(document).Background.IsEmpty);
    }

    [Fact(Timeout = 600000)]
    public void Transparent_In_An_Inline_Style_Is_Not_A_Highlight()
    {
        // The older path. It was wrong here first and nothing noticed, because a
        // hand-written page rarely says it.
        RichTextDocument document = Read("<p style=\"background: transparent\">text</p>");

        Assert.True(OnlyStyle(document).Background.IsEmpty);
    }

    [Fact(Timeout = 600000)]
    public void A_Real_Background_Is_Still_A_Highlight()
    {
        // The other half of the assertion. A fix that made every background
        // vanish would pass the two tests above and be a worse bug than the one
        // it replaced.
        RichTextDocument document = Read("<p style=\"background: yellow\">text</p>");

        InlineStyle style = OnlyStyle(document);

        Assert.False(style.Background.IsEmpty);
        Assert.Equal(255, style.Background.A);
    }

    [Fact(Timeout = 600000)]
    public void A_Foreground_Is_Untouched_By_This()
    {
        // Only the background is reinterpreted. `color: transparent` is invisible
        // text, which is a thing a document can genuinely mean, and the model has
        // somewhere to put it; a background that is not there is not a colour at
        // all. The two are not the same question and are not answered together.
        RichTextDocument document = Read("<p style=\"color: #123456\">text</p>");

        Assert.Equal(0x12, OnlyStyle(document).Foreground.R);
    }

    [Fact(Timeout = 600000)]
    public void A_Transparent_Background_Round_Trips_As_No_Background()
    {
        // The writer already emits nothing for an empty background, so the fix on
        // the read side is what closes the loop: in and out, and the highlight
        // that was never applied does not appear.
        RichTextDocument document = Read("<p style=\"background: transparent\">text</p>");
        string html = Encoding.UTF8.GetString(HtmlWriter.WriteToArray(document));

        Assert.DoesNotContain("background", html, StringComparison.OrdinalIgnoreCase);
    }

    private static InlineStyle OnlyStyle(RichTextDocument document) =>
        Assert.Single(document.Paragraphs).StyleAt(0);

    private static RichTextDocument Read(string html)
    {
        var codec = new HtmlDocumentCodec();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        return codec.Read(stream).Document;
    }
}
