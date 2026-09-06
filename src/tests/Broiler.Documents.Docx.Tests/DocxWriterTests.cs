using System.IO.Compression;
using System.Text;
using Broiler.Graphics;

namespace Broiler.Documents.Docx.Tests;

public sealed class DocxWriterTests
{
    [Fact(Timeout = 600000)]
    public void Writes_A_Minimal_Docx_Package()
    {
        byte[] bytes = DocxDocumentCodec.WriteToArray(RichTextDocument.FromPlainText("hello"));

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.NotNull(archive.GetEntry("_rels/.rels"));
        Assert.NotNull(archive.GetEntry("word/document.xml"));
        string documentXml = ReadEntry(archive, "word/document.xml");
        Assert.Contains("hello", documentXml);
        Assert.Contains("wordprocessingml", documentXml);
    }

    [Fact(Timeout = 600000)]
    public void Writes_Hyperlink_Relationships_And_Numbering()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(new[]
        {
            MakeParagraph(
                ParagraphStyle.Default with { ListKind = ListKind.Bullet, IndentLevel = 1 },
                ("item ", InlineStyle.Default),
                ("link", InlineStyle.Default with { LinkHref = "https://example.test" })),
        });

        byte[] bytes = DocxDocumentCodec.WriteToArray(document);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry("word/numbering.xml"));
        string relationships = ReadEntry(archive, "word/_rels/document.xml.rels");
        Assert.Contains("numbering", relationships);
        Assert.Contains("hyperlink", relationships);
        Assert.Contains("https://example.test", relationships);
    }

    [Fact(Timeout = 600000)]
    public void Model_To_Docx_To_Model_RoundTrips_Supported_Subset()
    {
        RichTextDocument expected = RichTextDocument.FromParagraphs(new[]
        {
            MakeParagraph(
                new ParagraphStyle
                {
                    Alignment = TextAlignment.Right,
                    LineSpacing = 1.25f,
                    IndentLevel = 2,
                    SpacingBefore = 3f,
                    SpacingAfter = 4f,
                },
                ("Hello\t", InlineStyle.Default),
                ("world", InlineStyle.Default with
                {
                    Bold = true,
                    Italic = true,
                    Underline = true,
                    Strikethrough = true,
                    FontFamily = "Serif",
                    FontSize = 13f,
                    Foreground = BColor.Blue,
                    Background = BColor.FromArgb(240, 240, 240),
                }),
                (((char)0x2028).ToString(), InlineStyle.Default),
                ("link", InlineStyle.Default with { LinkHref = "mailto:test@example.test" })),
            MakeParagraph(
                ParagraphStyle.Default with { ListKind = ListKind.Numbered, IndentLevel = 1 },
                ("Second", InlineStyle.Default)),
        });

        byte[] bytes = DocxDocumentCodec.WriteToArray(expected);
        using var stream = new MemoryStream(bytes);
        RichTextDocument actual = new DocxDocumentCodec().Read(stream).Document;

        DocumentAssert.Equivalent(expected, actual);
    }

    [Fact(Timeout = 600000)]
    public void Alpha_Color_Writes_Rgb_And_Reports_Diagnostic()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(new[]
        {
            MakeParagraph(
                ParagraphStyle.Default,
                ("transparent", InlineStyle.Default with { Foreground = BColor.FromRgba(1, 2, 3, 128) })),
        });

        using var stream = new MemoryStream();
        DocumentWriteResult result = new DocxDocumentCodec().Write(document, stream);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.color.alpha");
    }

    private static string ReadEntry(ZipArchive archive, string path)
    {
        using Stream stream = archive.GetEntry(path)!.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static RichTextParagraph MakeParagraph(
        ParagraphStyle paragraphStyle,
        params (string Text, InlineStyle Style)[] segments)
    {
        RichTextParagraph paragraph = RichTextParagraph.Empty.WithParagraphStyle(paragraphStyle);
        int offset = 0;
        foreach ((string text, InlineStyle style) in segments)
        {
            paragraph = paragraph.InsertText(offset, text, style);
            offset += text.Length;
        }

        return paragraph;
    }

    // XML has no representation for most control characters, not even an escape.
    // Handing one to the serializer throws, and the tool turns that into exit 70
    // - which its own help defines as always a defect in it. The character
    // arrives from ordinary input: the HTML reader decodes `&#7;` into the model
    // and the RTF reader passes `\u7` through, so a document that read cleanly
    // could not be written.

    [Theory(Timeout = 600000)]
    [InlineData("a\u0001b", "ab")]
    [InlineData("a\u0007b", "ab")]
    [InlineData("a\u000Bb", "ab")]
    [InlineData("a\u001Fb", "ab")]
    public void A_Control_Character_Xml_Cannot_Hold_Is_Dropped_With_A_Diagnostic(
        string text, string expected)
    {
        RichTextDocument document = RichTextDocument.FromPlainText(text);

        using var stream = new MemoryStream();
        DocumentWriteResult result = new DocxDocumentCodec().Write(document, stream);

        Assert.Contains(result.Diagnostics, d => d.Code == "docx.text.control");
        Assert.Equal(expected, RoundTrip(document).Paragraphs[0].Text);
    }

    [Fact(Timeout = 600000)]
    public void A_Control_Character_In_A_Picture_Description_Is_Dropped_Too()
    {
        // An attribute is as much XML as an element, and the description comes
        // from the same document the run text did. This path threw as well.
        var image = new InlineImage(DocxTestPackage.OnePixelPng, "image/png", 8, 8, altText: "x\u0007y");
        (InlineImage admitted, DocumentWriteOptions options) = Writable(image);

        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create(
                InlineImage.PlaceholderText, InlineStyle.Default with { Image = admitted })]);

        using var stream = new MemoryStream();
        DocumentWriteResult result = new DocxDocumentCodec().Write(document, stream, options);

        Assert.Contains(result.Diagnostics, d => d.Code == "docx.text.control");

        // The part, not the package: a .docx is a ZIP, and the deflated picture
        // bytes contain every byte value including this one.
        using var archive = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var part = new StreamReader(archive.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
        string documentXml = part.ReadToEnd();

        Assert.DoesNotContain("\u0007", documentXml, StringComparison.Ordinal);
        Assert.Contains("xy", documentXml, StringComparison.Ordinal);
    }

    [Theory(Timeout = 600000)]
    // What must survive: the characters XML does allow, including the ones that
    // arrive as surrogate pairs and would fail a naive per-char test.
    [InlineData("emoji \U0001F600 and \U0001D400")]
    [InlineData("tab\tand break\u2028here")]
    [InlineData("ordinary text")]
    public void Text_Xml_Can_Hold_Is_Written_Untouched(string text)
    {
        RichTextDocument document = RichTextDocument.FromPlainText(text);

        using var stream = new MemoryStream();
        DocumentWriteResult result = new DocxDocumentCodec().Write(document, stream);

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "docx.text.control");
        Assert.Equal(text, RoundTrip(document).Paragraphs[0].Text);
    }

    private static RichTextDocument RoundTrip(RichTextDocument document)
    {
        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(document), writable: false);
        return new DocxDocumentCodec().Read(stream).Document;
    }

    /// <summary>Admits an image under a policy that permits writing it.</summary>
    private static (InlineImage Image, DocumentWriteOptions Options) Writable(InlineImage image)
    {
        var builder = new DocumentConversionContextBuilder(DocumentResourcePolicy.AllowOwnDocuments);
        InlineImage admitted = builder.AdmitImage(
            image,
            DocumentResourceProvenance.CallerSupplied,
            DocumentResourceDisposition.Embedded);

        return (admitted, new DocumentWriteOptions(resources: builder.Build()));
    }

    [Fact(Timeout = 600000)]
    public void A_Fragment_Is_Written_As_An_Internal_Anchor()
    {
        // WordprocessingML spells a same-document reference as w:anchor, not as
        // a relationship to an external target. The distinction is invisible to
        // a round trip - both read back as "#chapter" - so only the bytes can
        // hold it.
        byte[] bytes = DocxDocumentCodec.WriteToArray(RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("jump", new InlineStyle { LinkHref = "#chapter" })]));

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        string documentXml = ReadEntry(archive, "word/document.xml");

        Assert.Contains("w:anchor=\"chapter\"", documentXml, StringComparison.Ordinal);
        Assert.DoesNotContain("\"#chapter\"", documentXml, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void A_Fragment_Round_Trips_Through_The_Package()
    {
        byte[] bytes = DocxDocumentCodec.WriteToArray(RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("jump", new InlineStyle { LinkHref = "#chapter" })]));

        using var stream = new MemoryStream(bytes, writable: false);
        DocumentReadResult result = new DocxDocumentCodec().Read(stream);

        Assert.Equal("#chapter", result.Document.Paragraphs[0].StyleAt(0).LinkHref);
    }
}
