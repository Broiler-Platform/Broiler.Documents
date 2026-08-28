using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Broiler.Graphics;

namespace Broiler.Documents.Odt.Tests;

public sealed class OdtWriterTests
{
    [Fact]
    public void The_Mimetype_Entry_Comes_First_And_Is_Stored_Uncompressed()
    {
        byte[] package = OdtDocumentCodec.WriteToArray(RichTextDocument.FromPlainText("hello"));

        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        ZipArchiveEntry first = archive.Entries[0];

        Assert.Equal("mimetype", first.FullName);
        Assert.Equal(first.Length, first.CompressedLength);
        Assert.Equal(OdtTestPackage.TextMediaType, ReadText(archive, "mimetype"));
    }

    [Fact]
    public void The_Manifest_Lists_Every_Part_That_Was_Written()
    {
        byte[] package = OdtDocumentCodec.WriteToArray(RichTextDocument.FromPlainText("hello"));

        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        XDocument manifest = XDocument.Parse(ReadText(archive, "META-INF/manifest.xml"));
        XNamespace ns = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

        string[] paths = manifest.Root!
            .Elements(ns + "file-entry")
            .Select(entry => (string)entry.Attribute(ns + "full-path")!)
            .ToArray();

        Assert.Equal(["/", "content.xml", "styles.xml", "meta.xml"], paths);
        foreach (string path in paths.Skip(1))
            Assert.NotNull(archive.GetEntry(path));
    }

    [Fact]
    public void Two_Writes_Of_One_Document_Produce_The_Same_Bytes()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("one\ntwo");

        Assert.Equal(
            OdtDocumentCodec.WriteToArray(document),
            OdtDocumentCodec.WriteToArray(document));
    }

    [Fact]
    public void What_The_Writer_Produces_Probes_As_Odt()
    {
        byte[] package = OdtDocumentCodec.WriteToArray(RichTextDocument.FromPlainText("hello"));

        DocumentProbeResult probe = new OdtDocumentCodec().Probe(new DocumentProbeRequest(package));

        Assert.True(probe.IsMatch);
        Assert.Equal("ODT", probe.FormatName);
        Assert.Equal(DocumentProbeConfidence.Certain, probe.Confidence);
    }

    [Fact]
    public void Plain_Paragraphs_Round_Trip()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("first\nsecond\n\nfourth");

        DocumentAssert.Equivalent(document, RoundTrip(document));
    }

    [Fact]
    public void Inline_Formatting_Round_Trips()
    {
        var style = new InlineStyle
        {
            Bold = true,
            Italic = true,
            Underline = true,
            Strikethrough = true,
            FontFamily = "Times New Roman",
            FontSize = 13.5f,
            Foreground = BColor.FromArgb(0x10, 0x20, 0x30),
            Background = BColor.FromArgb(0xAA, 0xBB, 0xCC),
            Capitalization = TextCapitalization.SmallCaps,
        };
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("styled", style)]);

        DocumentAssert.Equivalent(document, RoundTrip(document));
    }

    [Fact]
    public void Paragraph_Formatting_Round_Trips()
    {
        var style = new ParagraphStyle
        {
            Alignment = TextAlignment.Center,
            LineSpacing = 1.5f,
            SpacingBefore = 6f,
            SpacingAfter = 12f,
            IndentLevel = 2,
        };
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("body", InlineStyle.Default, style)]);

        DocumentAssert.Equivalent(document, RoundTrip(document));
    }

    [Fact]
    public void Lists_Round_Trip_With_Their_Kind_And_Level()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            ListParagraph("one", ListKind.Bullet, 1),
            ListParagraph("nested", ListKind.Bullet, 2),
            ListParagraph("two", ListKind.Bullet, 1),
            RichTextParagraph.Plain("between"),
            ListParagraph("step", ListKind.Numbered, 1),
        ]);

        DocumentAssert.Equivalent(document, RoundTrip(document));
    }

    [Fact]
    public void A_Run_Of_List_Items_Shares_One_Text_List()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            ListParagraph("one", ListKind.Bullet, 1),
            ListParagraph("two", ListKind.Bullet, 1),
        ]);

        XElement text = ReadBodyElement(OdtDocumentCodec.WriteToArray(document));
        XNamespace ns = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

        XElement list = Assert.Single(text.Elements(ns + "list"));
        Assert.Equal(2, list.Elements(ns + "list-item").Count());
    }

    [Theory]
    // Every space these paragraphs hold has to survive, which means the writer
    // has to know which of them a reader would otherwise collapse away.
    [InlineData("  leading")]
    [InlineData("trailing  ")]
    [InlineData("two  spaces  inside")]
    [InlineData("a b")]
    [InlineData("   ")]
    [InlineData("tab\there")]
    public void Significant_Spaces_Round_Trip(string text)
    {
        RichTextDocument document = RichTextDocument.FromPlainText(text);

        Assert.Equal(text, RoundTrip(document).Paragraphs[0].Text);
    }

    [Fact]
    public void A_Soft_Line_Break_Round_Trips()
    {
        // U+2028 is how the model stores the break Shift+Enter makes, and
        // text:line-break is where ODF keeps it.
        RichTextDocument document = RichTextDocument.FromPlainText("before\u2028after");

        Assert.Equal("before\u2028after", RoundTrip(document).Paragraphs[0].Text);
    }

    [Fact]
    public void Hyperlinks_Round_Trip()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("the site", new InlineStyle { LinkHref = "https://example.com/" }),
            RichTextParagraph.Create("jump", new InlineStyle { LinkHref = "#chapter" }),
        ]);

        DocumentAssert.Equivalent(document, RoundTrip(document));
    }

    [Fact]
    public void A_Disallowed_Link_Is_Written_As_Plain_Text()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("click", new InlineStyle { LinkHref = "javascript:alert(1)" })]);

        using var stream = new MemoryStream();
        DocumentWriteResult result = new OdtDocumentCodec().Write(document, stream);

        Assert.Contains(result.Diagnostics, d => d.Code == "odt.link");
        Assert.Equal("click", RoundTrip(document).Paragraphs[0].Text);
        Assert.Null(RoundTrip(document).Paragraphs[0].StyleAt(0).LinkHref);
    }

    [Fact]
    public void A_Control_Character_Xml_Cannot_Hold_Is_Dropped_With_A_Diagnostic()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("a\u0001b");

        using var stream = new MemoryStream();
        DocumentWriteResult result = new OdtDocumentCodec().Write(document, stream);

        Assert.Contains(result.Diagnostics, d => d.Code == "odt.text.control");
        Assert.Equal("ab", RoundTrip(document).Paragraphs[0].Text);
    }

    [Fact]
    public void Identical_Formatting_Shares_One_Automatic_Style()
    {
        var bold = new InlineStyle { Bold = true };
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("one", bold),
            RichTextParagraph.Create("two", bold),
        ]);

        XElement content = ReadContentRoot(OdtDocumentCodec.WriteToArray(document));
        XNamespace office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
        XNamespace style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";

        Assert.Single(content.Element(office + "automatic-styles")!.Elements(style + "style"));
    }

    [Fact]
    public void An_Unformatted_Document_Declares_No_Automatic_Styles()
    {
        XElement content = ReadContentRoot(
            OdtDocumentCodec.WriteToArray(RichTextDocument.FromPlainText("plain")));
        XNamespace office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";

        Assert.Empty(content.Element(office + "automatic-styles")!.Elements());
    }

    [Fact]
    public void The_Write_Result_Reports_The_Byte_Count_It_Wrote()
    {
        using var stream = new MemoryStream();
        DocumentWriteResult result = new OdtDocumentCodec().Write(RichTextDocument.FromPlainText("x"), stream);

        Assert.Equal(stream.Length, result.BytesWritten);
        Assert.Equal(DocumentResultStatus.Success, result.Status);
    }

    internal static RichTextParagraph ListParagraph(string text, ListKind kind, int level) =>
        RichTextParagraph.Create(
            text,
            InlineStyle.Default,
            new ParagraphStyle { LineSpacing = 1f, ListKind = kind, IndentLevel = level });

    internal static RichTextDocument RoundTrip(RichTextDocument document)
    {
        byte[] package = OdtDocumentCodec.WriteToArray(document);
        using var stream = new MemoryStream(package, writable: false);
        DocumentReadResult result = new OdtDocumentCodec().Read(stream);
        Assert.True(result.IsUsable);
        return result.Document;
    }

    internal static XElement ReadContentRoot(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return XDocument.Parse(ReadText(archive, "content.xml"), LoadOptions.PreserveWhitespace).Root!;
    }

    internal static XElement ReadBodyElement(byte[] package)
    {
        XNamespace office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
        return ReadContentRoot(package).Element(office + "body")!.Element(office + "text")!;
    }

    internal static string ReadText(ZipArchive archive, string path)
    {
        using Stream stream = archive.GetEntry(path)!.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
