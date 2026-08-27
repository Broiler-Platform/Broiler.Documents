using System.IO.Compression;
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
}
