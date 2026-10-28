using System;
using System.IO;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Documents.Resources;

namespace Broiler.Documents.Markdown.Tests;

/// <summary>
/// Unit tests for Markdown embedded image reading and round-tripping (<c>![alt](data:...)</c>).
/// </summary>
public sealed class MarkdownImageTests
{
    private static readonly byte[] SamplePng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private static string SamplePngDataUri =>
        "data:image/png;base64," + Convert.ToBase64String(SamplePng);

    private static DocumentReadResult ReadResult(string markdown, DocumentReadOptions? options = null)
    {
        var codec = new MarkdownDocumentCodec();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(markdown));
        return codec.Read(stream, options);
    }

    [Fact]
    public void Embedded_Image_With_Data_Uri_Is_Read_Into_Model()
    {
        string md = $"Before ![sample logo]({SamplePngDataUri}) After";

        DocumentReadResult result = ReadResult(md);
        RichTextParagraph p = Assert.Single(result.Document.Paragraphs);

        Assert.Equal("Before " + InlineImage.PlaceholderText + " After", p.Text);
        InlineImage? image = p.StyleAt(7).Image;
        Assert.NotNull(image);
        Assert.Equal("sample logo", image.AltText);
        Assert.Equal("sample logo", image.Name);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(SamplePng, image.Data.ToArray());
        Assert.NotEqual(DocumentResourceId.None, image.ResourceId);
        Assert.True(result.Resources.TryGetEntry(image.ResourceId, out _));
    }

    [Fact]
    public void Embedded_Image_With_Empty_Alt_Is_Read_Into_Model()
    {
        string md = $"![]({SamplePngDataUri})";

        DocumentReadResult result = ReadResult(md);
        RichTextParagraph p = Assert.Single(result.Document.Paragraphs);

        Assert.Equal(InlineImage.PlaceholderText, p.Text);
        InlineImage? image = p.StyleAt(0).Image;
        Assert.NotNull(image);
        Assert.Equal(string.Empty, image.AltText);
    }

    [Fact]
    public void Resource_Policy_Denial_Falls_Back_To_Description_And_Reports_Diagnostic()
    {
        string md = $"Before ![my image]({SamplePngDataUri}) After";
        var options = new DocumentReadOptions(resourcePolicy: DocumentResourcePolicy.DenyAll);

        DocumentReadResult result = ReadResult(md, options);

        Assert.Equal("Before my image After", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, d => d.Code == "markdown.image.denied");
    }

    [Fact]
    public void Oversized_Embedded_Image_Exceeding_MaxBinBytes_Falls_Back_To_Description()
    {
        string md = $"![huge image]({SamplePngDataUri})";
        var limits = new DocumentLimits(maxBinBytes: 10);
        var options = new DocumentReadOptions(limits: limits);

        DocumentReadResult result = ReadResult(md, options);

        Assert.Equal("huge image", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, d => d.Code == "markdown.image.limit");
    }

    [Fact]
    public void Malformed_Data_Uri_Reports_Diagnostic_And_Falls_Back_To_Description()
    {
        string md = "Before ![broken](data:image/png;base64,not-base64!) After";

        DocumentReadResult result = ReadResult(md);

        Assert.Equal("Before broken After", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, d => d.Code == "markdown.image.malformed");
    }

    [Fact]
    public void External_Images_Remain_Replaced_By_Description_Without_Network_Access()
    {
        string md = "Before ![photo](https://example.com/pic.jpg) After";

        DocumentReadResult result = ReadResult(md);

        Assert.Equal("Before photo After", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, d => d.Code == "markdown.image.dropped");
    }

    [Fact]
    public void Embedded_Image_Survives_Markdown_Round_Trip()
    {
        var originalImage = new InlineImage(SamplePng, "image/png", 50, 25, "roundtrip logo", "logo");
        var builder = new DocumentConversionContextBuilder(DocumentResourcePolicy.AllowOwnDocuments);
        InlineImage admitted = builder.AdmitImage(
            originalImage,
            DocumentResourceProvenance.CallerSupplied,
            DocumentResourceDisposition.Embedded);

        RichTextDocument document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("Logo: ", InlineStyle.Default)
                .InsertText(6, InlineImage.PlaceholderText, InlineStyle.Default with { Image = admitted })
        ]);

        var writeOptions = new DocumentWriteOptions(resources: builder.Build());
        using var stream = new MemoryStream();
        new MarkdownDocumentCodec().Write(document, stream, writeOptions);

        DocumentReadResult readBack = ReadResult(Encoding.UTF8.GetString(stream.ToArray()));
        RichTextParagraph p = readBack.Document.Paragraphs[0];

        Assert.Equal("Logo: " + InlineImage.PlaceholderText, p.Text);
        InlineImage? readImage = p.StyleAt(6).Image;
        Assert.NotNull(readImage);
        Assert.Equal("roundtrip logo", readImage.AltText);
        Assert.Equal(SamplePng, readImage.Data.ToArray());
    }
}
