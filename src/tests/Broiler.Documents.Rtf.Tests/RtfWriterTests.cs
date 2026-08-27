using System.Text;
using Broiler.Graphics;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfWriterTests
{
    [Fact(Timeout = 600000)]
    public void Writes_An_Embedded_Png_As_A_Pict_Destination()
    {
        var image = new InlineImage(new byte[] { 0xDE, 0xAD }, "image/png", 40, 20);
        RichTextDocument document = RichTextDocument.FromParagraphs(new[]
        {
            RichTextParagraph.Create(InlineImage.PlaceholderText, InlineStyle.Default with { Image = image }),
        });

        string rtf = Write(document);

        Assert.Contains("{\\pict\\pngblip", rtf, StringComparison.Ordinal);
        Assert.Contains("\\picwgoal800", rtf, StringComparison.Ordinal);
        Assert.Contains("\\pichgoal400", rtf, StringComparison.Ordinal);
        Assert.Contains("dead}", rtf, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void Drops_An_Image_Format_Rtf_Cannot_Name()
    {
        var image = new InlineImage(new byte[] { 1, 2 }, "image/webp", 40, 20);
        RichTextDocument document = RichTextDocument.FromParagraphs(new[]
        {
            RichTextParagraph.Create(InlineImage.PlaceholderText, InlineStyle.Default with { Image = image }),
        });

        using var stream = new MemoryStream();
        DocumentWriteResult result = RtfWriter.Write(document, stream);

        Assert.DoesNotContain("\\pict", Encoding.ASCII.GetString(stream.ToArray()), StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "rtf.image.format");
    }

    private static string Write(RichTextDocument document) =>
        Encoding.ASCII.GetString(RtfWriter.WriteToArray(document));

    private static RichTextDocument OneRun(string text, InlineStyle style) =>
        RichTextDocument.FromParagraphs(new[]
        {
            RichTextParagraph.Create(string.Empty, InlineStyle.Default).InsertText(0, text, style),
        });

    [Fact(Timeout = 600000)]
    public void Output_Is_A_Wrapped_Rtf_Group()
    {
        string rtf = Write(RichTextDocument.FromPlainText("hello"));

        Assert.StartsWith("{\\rtf1\\ansi", rtf);
        Assert.EndsWith("}", rtf);
        Assert.Contains("\\par", rtf);
    }

    [Fact(Timeout = 600000)]
    public void Special_Characters_Are_Escaped()
    {
        string rtf = Write(RichTextDocument.FromPlainText("a{b}\\c"));

        Assert.Contains("a\\{b\\}\\\\c", rtf);
    }

    [Fact(Timeout = 600000)]
    public void Bold_And_Foreground_Emit_Control_Words_And_A_Color_Table()
    {
        var style = new InlineStyle { Bold = true, Foreground = new BColor(255, 0, 0) };
        string rtf = Write(OneRun("hi", style));

        Assert.Contains("{\\colortbl;\\red255\\green0\\blue0;}", rtf);
        Assert.Contains("\\b", rtf);
        Assert.Contains("\\cf1", rtf);
    }

    [Fact(Timeout = 600000)]
    public void Font_Family_Emits_A_Font_Table_Entry()
    {
        string rtf = Write(OneRun("hi", new InlineStyle { FontFamily = "Arial" }));

        Assert.Contains("{\\f1\\fnil Arial;}", rtf);
        Assert.Contains("\\f1", rtf);
    }

    [Fact(Timeout = 600000)]
    public void Non_Ascii_Is_Escaped_As_Unicode()
    {
        // é == U+00E9 == 233
        string rtf = Write(RichTextDocument.FromPlainText(((char)0x00E9).ToString()));

        Assert.Contains("\\u233?", rtf);
    }

    [Fact(Timeout = 600000)]
    public void Hyperlink_Run_Emits_A_Field()
    {
        string rtf = Write(OneRun("click", new InlineStyle { LinkHref = "https://x.com" }));

        Assert.Contains("HYPERLINK \"https://x.com\"", rtf);
        Assert.Contains("\\fldrslt", rtf);
    }

    [Fact(Timeout = 600000)]
    public void Output_Is_Pure_Ascii()
    {
        byte[] bytes = RtfWriter.WriteToArray(RtfReader.Read(
            Encoding.Latin1.GetBytes("{\\rtf1 caf\\'e9 \\u9731?}")).Document);

        Assert.All(bytes, b => Assert.True(b < 0x80));
    }
}
