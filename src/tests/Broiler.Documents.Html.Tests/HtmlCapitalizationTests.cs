namespace Broiler.Documents.Html.Tests;

/// <summary>
/// <c>text-transform: uppercase</c> and <c>font-variant: small-caps</c> — the CSS
/// spellings of the same display property, and the reason HTML export of a
/// capitalized run keeps the author's casing in the markup.
/// </summary>
public sealed class HtmlCapitalizationTests
{
    private static RichTextDocument Read(string html)
    {
        var codec = new HtmlDocumentCodec();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(html));
        return codec.Read(stream).Document;
    }

    private static string Write(RichTextDocument document) =>
        System.Text.Encoding.UTF8.GetString(HtmlWriter.WriteToArray(document));

    [Fact(Timeout = 600000)]
    public void Reads_Text_Transform_Uppercase()
    {
        RichTextDocument document = Read(
            "<p><span style=\"text-transform: uppercase\">Elene Bartky</span></p>");

        RichTextParagraph paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal("Elene Bartky", paragraph.Text);
        Assert.Equal(TextCapitalization.AllCaps, paragraph.StyleAt(0).Capitalization);
    }

    [Fact(Timeout = 600000)]
    public void Reads_Font_Variant_Small_Caps()
    {
        RichTextDocument document = Read(
            "<p><span style=\"font-variant: small-caps\">Kontakt</span></p>");

        Assert.Equal(
            TextCapitalization.SmallCaps,
            Assert.Single(document.Paragraphs).StyleAt(0).Capitalization);
    }

    [Fact(Timeout = 600000)]
    public void Writes_Text_Transform_For_All_Caps_And_Leaves_The_Text_Alone()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create(
                "Elene Bartky",
                InlineStyle.Default with { Capitalization = TextCapitalization.AllCaps }),
        ]);

        string html = Write(document);

        Assert.Contains("text-transform: uppercase", html, StringComparison.Ordinal);
        Assert.Contains("Elene Bartky", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ELENE BARTKY", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TextCapitalization.None)]
    [InlineData(TextCapitalization.AllCaps)]
    [InlineData(TextCapitalization.SmallCaps)]
    public void Round_Trips_Capitalization(TextCapitalization capitalization)
    {
        RichTextDocument original = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create(
                "Elene Bartky",
                InlineStyle.Default with { Capitalization = capitalization }),
        ]);

        RichTextDocument round = Read(Write(original));

        RichTextParagraph paragraph = Assert.Single(round.Paragraphs);
        Assert.Equal("Elene Bartky", paragraph.Text);
        Assert.Equal(capitalization, paragraph.StyleAt(0).Capitalization);
    }
}
