using System.Text;
using Broiler.Graphics;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfWriterTests
{
    [Fact(Timeout = 600000)]
    public void Writes_An_Embedded_Png_As_A_Pict_Destination()
    {
        var image = new InlineImage(new byte[] { 0xDE, 0xAD }, "image/png", 40, 20);
        (image, DocumentWriteOptions writeOptions) = Writable(image);
        RichTextDocument document = RichTextDocument.FromParagraphs(new[]
        {
            RichTextParagraph.Create(InlineImage.PlaceholderText, InlineStyle.Default with { Image = image }),
        });

        string rtf = Write(document, writeOptions);

        Assert.Contains("{\\pict\\pngblip", rtf, StringComparison.Ordinal);
        Assert.Contains("\\picwgoal800", rtf, StringComparison.Ordinal);
        Assert.Contains("\\pichgoal400", rtf, StringComparison.Ordinal);
        Assert.Contains("dead}", rtf, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void Drops_An_Image_Format_Rtf_Cannot_Name()
    {
        var image = new InlineImage(new byte[] { 1, 2 }, "image/webp", 40, 20);
        (image, DocumentWriteOptions writeOptions) = Writable(image);
        RichTextDocument document = RichTextDocument.FromParagraphs(new[]
        {
            RichTextParagraph.Create(InlineImage.PlaceholderText, InlineStyle.Default with { Image = image }),
        });

        using var stream = new MemoryStream();
        DocumentWriteResult result = RtfWriter.Write(document, stream, writeOptions);

        Assert.DoesNotContain("\\pict", Encoding.ASCII.GetString(stream.ToArray()), StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "rtf.image.format");
    }

    private static string Write(RichTextDocument document, DocumentWriteOptions? options = null) =>
        Encoding.ASCII.GetString(RtfWriter.WriteToArray(document, options));

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

    /// <summary>
    /// Admits <paramref name="image"/> under a policy that permits writing it,
    /// and returns the image bound to that decision together with the options a
    /// writer needs.
    /// </summary>
    /// <remarks>
    /// A writer refuses a picture nobody decided on, so a write test has to say
    /// which decision it is testing under. Reading a document is not that
    /// decision: it grants extraction into the model and nothing that puts the
    /// bytes into an output.
    /// </remarks>
    private static (InlineImage Image, DocumentWriteOptions Options) Writable(InlineImage image)
    {
        var builder = new DocumentConversionContextBuilder(DocumentResourcePolicy.AllowOwnDocuments);
        InlineImage admitted = builder.AdmitImage(
            image,
            DocumentResourceProvenance.CallerSupplied,
            DocumentResourceDisposition.Embedded);

        return (admitted, new DocumentWriteOptions(resources: builder.Build()));
    }

    /// <summary>Read options that also permit writing what was read back out.</summary>
    private static DocumentReadOptions RoundTripReadOptions { get; } =
        new(resourcePolicy: DocumentResourcePolicy.AllowOwnDocuments);

    // The allow-list was enforced on the way in and not on the way out, so a
    // target no reader here would accept could still be written into a document,
    // with no diagnostic. The round trip cannot see that: every reader refuses
    // the same schemes, so a document that wrote the link and one that dropped
    // it both read back without it. These assert the bytes.

    [Theory(Timeout = 600000)]
    [InlineData("javascript:alert(1)")]
    [InlineData("vbscript:msgbox")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/relative/path")]
    public void A_Refused_Link_Target_Never_Reaches_The_Output(string href)
    {
        (string written, DocumentWriteResult result) = WriteLink(href);

        Assert.DoesNotContain(href, written, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "rtf.link");
    }

    [Theory(Timeout = 600000)]
    [InlineData("https://example.test/page")]
    [InlineData("http://example.test/page")]
    [InlineData("mailto:someone@example.test")]
    public void A_Permitted_Link_Target_Is_Still_Written(string href)
    {
        (string written, DocumentWriteResult result) = WriteLink(href);

        Assert.Contains(href, written, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "rtf.link");
    }

    private static (string Written, DocumentWriteResult Result) WriteLink(string href)
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("link", new InlineStyle { LinkHref = href })]);

        using var stream = new MemoryStream();
        DocumentWriteResult result = new RtfDocumentCodec().Write(document, stream);
        return (Encoding.ASCII.GetString(stream.ToArray()), result);
    }

    [Fact(Timeout = 600000)]
    public void A_Fragment_Is_Written_As_The_Local_Switch()
    {
        // Not as "#chapter" inside the quotes: a word processor reads that as
        // an address. This codec used to emit one, which is why its own reader
        // could not recognise Word's.
        string written = Write(OneRun("click", new InlineStyle { LinkHref = "#chapter" }));

        Assert.Contains("HYPERLINK \\\\l \"chapter\"", written, StringComparison.Ordinal);
        Assert.DoesNotContain("\"#chapter\"", written, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void A_Fragment_Round_Trips_Through_The_Field()
    {
        string written = Write(OneRun("click", new InlineStyle { LinkHref = "#chapter" }));

        Assert.Equal(
            "#chapter",
            RtfReader.Read(Encoding.ASCII.GetBytes(written)).Document.Paragraphs[0].StyleAt(0).LinkHref);
    }
}
