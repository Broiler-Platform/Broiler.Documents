namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// <c>\caps</c> and <c>\scaps</c>. Like their DOCX counterparts these are display
/// properties over text stored in the author's own casing.
/// </summary>
public sealed class RtfCapitalizationTests
{
    private static RichTextDocument Read(string rtf)
    {
        var codec = new RtfDocumentCodec();
        using var stream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(rtf));
        return codec.Read(stream).Document;
    }

    [Fact(Timeout = 600000)]
    public void Reads_Caps_As_An_Attribute_Not_As_Uppercased_Text()
    {
        RichTextDocument document = Read(@"{\rtf1\ansi{\caps Elene Bartky}}");

        RichTextParagraph paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal("Elene Bartky", paragraph.Text);
        Assert.Equal(TextCapitalization.AllCaps, paragraph.StyleAt(0).Capitalization);
    }

    [Fact(Timeout = 600000)]
    public void Reads_Small_Caps()
    {
        RichTextDocument document = Read(@"{\rtf1\ansi{\scaps Kontakt}}");

        Assert.Equal(
            TextCapitalization.SmallCaps,
            Assert.Single(document.Paragraphs).StyleAt(0).Capitalization);
    }

    [Fact(Timeout = 600000)]
    public void Caps0_Turns_Capitalization_Off()
    {
        RichTextDocument document = Read(@"{\rtf1\ansi{\caps\caps0 plain}}");

        Assert.Equal(
            TextCapitalization.None,
            Assert.Single(document.Paragraphs).StyleAt(0).Capitalization);
    }

    /// <summary>Each control word clears only the kind it names.</summary>
    [Fact(Timeout = 600000)]
    public void Scaps0_Leaves_An_Active_Caps_State_Alone()
    {
        RichTextDocument document = Read(@"{\rtf1\ansi{\caps\scaps0 loud}}");

        Assert.Equal(
            TextCapitalization.AllCaps,
            Assert.Single(document.Paragraphs).StyleAt(0).Capitalization);
    }

    [Fact(Timeout = 600000)]
    public void Plain_Resets_Capitalization()
    {
        RichTextDocument document = Read(@"{\rtf1\ansi{\caps\plain quiet}}");

        Assert.Equal(
            TextCapitalization.None,
            Assert.Single(document.Paragraphs).StyleAt(0).Capitalization);
    }

    [Theory]
    [InlineData(TextCapitalization.None)]
    [InlineData(TextCapitalization.AllCaps)]
    [InlineData(TextCapitalization.SmallCaps)]
    public void Round_Trips_Capitalization_Without_Rewriting_The_Text(TextCapitalization capitalization)
    {
        RichTextDocument original = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("Elene Bartky", InlineStyle.Default with { Capitalization = capitalization }),
        ]);

        byte[] bytes = RtfWriter.WriteToArray(original);
        var codec = new RtfDocumentCodec();
        using var stream = new MemoryStream(bytes);
        RichTextDocument round = codec.Read(stream).Document;

        RichTextParagraph paragraph = Assert.Single(round.Paragraphs);
        Assert.Equal("Elene Bartky", paragraph.Text);
        Assert.Equal(capitalization, paragraph.StyleAt(0).Capitalization);
    }
}
