using System.IO.Compression;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Reading and writing embedded pictures: the DrawingML and VML shapes a run can
/// hold, the media parts they reference, and the package a write produces.
/// </summary>
public sealed class DocxImageTests
{
    /// <summary>914400 EMUs per inch; one inch square is the size these tests use.</summary>
    private const long OneInchEmus = 914400;

    private static Dictionary<string, byte[]> Media(string path, byte[]? bytes = null) =>
        new(StringComparer.Ordinal) { [path] = bytes ?? DocxTestPackage.OnePixelPng };

    [Fact(Timeout = 600000)]
    public void Reads_An_Inline_Drawing_As_One_Image_Character()
    {
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p>" + DocxTestPackage.DrawingRun("rId7", OneInchEmus, OneInchEmus / 2) + "</w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.png"),
            Media("word/media/image1.png"));

        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal(InlineImage.PlaceholderText, paragraph.Text);

        StyleRun run = Assert.Single(paragraph.Runs);
        InlineImage image = Assert.IsType<InlineImage>(run.Style.Image);
        Assert.Equal(1, run.Length);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(DocxTestPackage.OnePixelPng, image.Data.ToArray());

        // 914400 EMUs is one inch, which is 72 points.
        Assert.Equal(72, image.Width, 3);
        Assert.Equal(36, image.Height, 3);
    }

    [Fact(Timeout = 600000)]
    public void Reads_Alternative_Text_And_Keeps_Surrounding_Text_In_Order()
    {
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p><w:r><w:t>before </w:t></w:r>" +
            DocxTestPackage.DrawingRun("rId7", OneInchEmus, OneInchEmus, altText: "a logo") +
            "<w:r><w:t> after</w:t></w:r></w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.png"),
            Media("word/media/image1.png"));

        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("before " + InlineImage.PlaceholderText + " after", paragraph.Text);
        Assert.Equal(3, paragraph.Runs.Count);
        Assert.Equal("a logo", paragraph.Runs[1].Style.Image?.AltText);
        Assert.Null(paragraph.Runs[0].Style.Image);
        Assert.Null(paragraph.Runs[2].Style.Image);
    }

    [Fact(Timeout = 600000)]
    public void Keeps_The_Runs_Own_Formatting_On_An_Image()
    {
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p><w:hyperlink r:id=\"rId8\">" +
            DocxTestPackage.DrawingRun("rId7", OneInchEmus, OneInchEmus) +
            "</w:hyperlink></w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.png") +
            "<Relationship Id=\"rId8\" " +
            "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" " +
            "Target=\"https://example.test/\" TargetMode=\"External\"/>",
            Media("word/media/image1.png"));

        StyleRun run = Assert.Single(Assert.Single(result.Document.Paragraphs).Runs);
        Assert.NotNull(run.Style.Image);
        Assert.Equal("https://example.test/", run.Style.LinkHref);
    }

    [Fact(Timeout = 600000)]
    public void Reads_A_Legacy_Vml_Picture_And_Its_Css_Size()
    {
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p>" + DocxTestPackage.VmlPictureRun("rId7", "width:90pt;height:45pt") + "</w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.png"),
            Media("word/media/image1.png"));

        StyleRun run = Assert.Single(Assert.Single(result.Document.Paragraphs).Runs);
        InlineImage image = Assert.IsType<InlineImage>(run.Style.Image);
        Assert.Equal(90, image.Width, 3);
        Assert.Equal(45, image.Height, 3);
    }

    [Fact(Timeout = 600000)]
    public void Reads_One_Media_Part_Once_When_Several_Runs_Share_It()
    {
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p>" +
            DocxTestPackage.DrawingRun("rId7", OneInchEmus, OneInchEmus) +
            DocxTestPackage.DrawingRun("rId7", OneInchEmus, OneInchEmus) +
            "</w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.png"),
            Media("word/media/image1.png"));

        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);

        // Two pictures are two characters and two runs: each carries its own
        // image object, so neither collapses into the other.
        Assert.Equal(2, paragraph.Text.Length);
        Assert.Equal(2, paragraph.Runs.Count);
        Assert.NotSame(paragraph.Runs[0].Style.Image, paragraph.Runs[1].Style.Image);
        Assert.Equal(
            paragraph.Runs[0].Style.Image!.Data.ToArray(),
            paragraph.Runs[1].Style.Image!.Data.ToArray());
    }

    [Fact(Timeout = 600000)]
    public void Counts_Images_In_The_Read_Summary()
    {
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p>" + DocxTestPackage.DrawingRun("rId7", OneInchEmus, OneInchEmus) + "</w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.png"),
            Media("word/media/image1.png"));

        DocumentDiagnostic summary = Assert.Single(
            result.Diagnostics.Where(d => d.Code == "docx.read.summary"));
        Assert.Contains("embedded 1 image(s)", summary.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void Reports_A_Picture_Whose_Media_Part_Is_Missing()
    {
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p>" + DocxTestPackage.DrawingRun("rId7", OneInchEmus, OneInchEmus) + "</w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.png"),
            new Dictionary<string, byte[]>(StringComparer.Ordinal));

        Assert.Empty(Assert.Single(result.Document.Paragraphs).Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.image.missing");
    }

    [Fact(Timeout = 600000)]
    public void Reports_A_Picture_Whose_Relationship_Is_Undefined()
    {
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p>" + DocxTestPackage.DrawingRun("rId99", OneInchEmus, OneInchEmus) + "</w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.png"),
            Media("word/media/image1.png"));

        Assert.Empty(Assert.Single(result.Document.Paragraphs).Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.image.relationship");
    }

    [Fact(Timeout = 600000)]
    public void Reports_An_Image_Format_It_Does_Not_Carry()
    {
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p>" + DocxTestPackage.DrawingRun("rId7", OneInchEmus, OneInchEmus) + "</w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.emf"),
            Media("word/media/image1.emf", [0x01, 0x00, 0x00, 0x00, 0x6C]));

        Assert.Empty(Assert.Single(result.Document.Paragraphs).Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.image.format");
    }

    [Fact(Timeout = 600000)]
    public void Skips_An_Image_Part_Over_The_Binary_Limit()
    {
        // The limit has to clear the package's XML parts, which it also governs,
        // so the picture rather than the document is what exceeds it.
        byte[] oversized = new byte[8192];
        DocxTestPackage.OnePixelPng.CopyTo(oversized, 0);

        var options = new DocumentReadOptions(new DocumentLimits(maxBinBytes: 4096));
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p>" + DocxTestPackage.DrawingRun("rId7", OneInchEmus, OneInchEmus) + "</w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.png"),
            Media("word/media/image1.png", oversized),
            options);

        Assert.Empty(Assert.Single(result.Document.Paragraphs).Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.image.limit");
    }

    [Fact(Timeout = 600000)]
    public void Does_Not_Fetch_An_Externally_Linked_Picture()
    {
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p>" + DocxTestPackage.DrawingRun("rId7", OneInchEmus, OneInchEmus) + "</w:p>",
            "<Relationship Id=\"rId7\" " +
            "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" " +
            "Target=\"https://example.test/logo.png\" TargetMode=\"External\"/>",
            new Dictionary<string, byte[]>(StringComparer.Ordinal));

        Assert.Empty(Assert.Single(result.Document.Paragraphs).Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.image.external");
    }

    [Fact(Timeout = 600000)]
    public void Writes_An_Image_As_A_Media_Part_With_Its_Relationship_And_Content_Type()
    {
        var image = new InlineImage(DocxTestPackage.OnePixelPng, "image/png", 72, 36, "a logo");
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create(InlineImage.PlaceholderText, InlineStyle.Default with { Image = image }),
        ]);

        byte[] bytes = DocxDocumentCodec.WriteToArray(document);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        ZipArchiveEntry media = Assert.IsType<ZipArchiveEntry>(archive.GetEntry("word/media/image1.png"));
        using (Stream stream = media.Open())
        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            Assert.Equal(DocxTestPackage.OnePixelPng, buffer.ToArray());
        }

        string relationships = ReadEntry(archive, "word/_rels/document.xml.rels");
        Assert.Contains("relationships/image", relationships, StringComparison.Ordinal);
        Assert.Contains("media/image1.png", relationships, StringComparison.Ordinal);

        string contentTypes = ReadEntry(archive, "[Content_Types].xml");
        Assert.Contains("Extension=\"png\"", contentTypes, StringComparison.Ordinal);

        string documentXml = ReadEntry(archive, "word/document.xml");
        Assert.Contains("<w:drawing>", documentXml, StringComparison.Ordinal);
        Assert.Contains("descr=\"a logo\"", documentXml, StringComparison.Ordinal);

        // 72 points and 36 points, in EMUs.
        Assert.Contains("cx=\"914400\"", documentXml, StringComparison.Ordinal);
        Assert.Contains("cy=\"457200\"", documentXml, StringComparison.Ordinal);
        Assert.DoesNotContain("￼", documentXml, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void Writes_One_Media_Part_For_An_Image_Used_Twice()
    {
        var image = new InlineImage(DocxTestPackage.OnePixelPng, "image/png", 72, 72);
        var style = InlineStyle.Default with { Image = image };
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create(InlineImage.PlaceholderText, style),
            RichTextParagraph.Create(InlineImage.PlaceholderText, style),
        ]);

        byte[] bytes = DocxDocumentCodec.WriteToArray(document);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry("word/media/image1.png"));
        Assert.Null(archive.GetEntry("word/media/image2.png"));
    }

    [Fact(Timeout = 600000)]
    public void Round_Trips_An_Image_Through_Write_And_Read()
    {
        var image = new InlineImage(DocxTestPackage.OnePixelPng, "image/png", 120, 60, "a logo");
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("before", InlineStyle.Default)
                .InsertText(6, InlineImage.PlaceholderText, InlineStyle.Default with { Image = image })
                .InsertText(7, "after", InlineStyle.Default),
        ]);

        byte[] bytes = DocxDocumentCodec.WriteToArray(document);
        using var stream = new MemoryStream(bytes, writable: false);
        DocumentReadResult result = new DocxDocumentCodec().Read(stream);

        RichTextParagraph paragraph = Assert.Single(result.Document.Paragraphs);
        Assert.Equal("before" + InlineImage.PlaceholderText + "after", paragraph.Text);

        InlineImage restored = Assert.IsType<InlineImage>(paragraph.Runs[1].Style.Image);
        Assert.Equal(DocxTestPackage.OnePixelPng, restored.Data.ToArray());
        Assert.Equal("image/png", restored.ContentType);
        Assert.Equal(120, restored.Width, 3);
        Assert.Equal(60, restored.Height, 3);
        Assert.Equal("a logo", restored.AltText);
    }

    [Fact(Timeout = 600000)]
    public void Drops_A_Placeholder_Character_That_Carries_No_Image()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("a" + InlineImage.PlaceholderText + "b", InlineStyle.Default),
        ]);

        using var buffer = new MemoryStream();
        DocumentWriteResult write = new DocxDocumentCodec().Write(document, buffer);

        using var archive = new ZipArchive(new MemoryStream(buffer.ToArray()), ZipArchiveMode.Read);
        string documentXml = ReadEntry(archive, "word/document.xml");

        Assert.DoesNotContain("￼", documentXml, StringComparison.Ordinal);
        Assert.Contains(write.Diagnostics, d => d.Code == "docx.image.placeholder");
    }

    private static string ReadEntry(ZipArchive archive, string path)
    {
        ZipArchiveEntry entry = Assert.IsType<ZipArchiveEntry>(archive.GetEntry(path));
        using Stream stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
