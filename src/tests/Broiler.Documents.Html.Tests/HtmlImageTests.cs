using System;
using System.IO;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Documents.Resources;

namespace Broiler.Documents.Html.Tests;

/// <summary>
/// Unit tests for HTML embedded image reading and round-tripping (<c>&lt;img src="data:..."&gt;</c>).
/// </summary>
public sealed class HtmlImageTests
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

    private static DocumentReadResult ReadResult(string html, DocumentReadOptions? options = null)
    {
        var codec = new HtmlDocumentCodec();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
        return codec.Read(stream, options);
    }

    [Fact]
    public void Embedded_Image_With_Data_Uri_Is_Read_Into_Model()
    {
        string html = $"<p>Before <img src=\"{SamplePngDataUri}\" alt=\"sample logo\" title=\"logo\"> After</p>";

        DocumentReadResult result = ReadResult(html);
        RichTextParagraph p = Assert.Single(result.Document.Paragraphs);

        Assert.Equal("Before " + InlineImage.PlaceholderText + " After", p.Text);
        InlineImage? image = p.StyleAt(7).Image;
        Assert.NotNull(image);
        Assert.Equal("sample logo", image.AltText);
        Assert.Equal("logo", image.Name);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(SamplePng, image.Data.ToArray());
        Assert.NotEqual(DocumentResourceId.None, image.ResourceId);
        Assert.True(result.Resources.TryGetEntry(image.ResourceId, out _));
    }

    [Fact]
    public void Image_Dimensions_Are_Read_From_Css_Style()
    {
        string html = $"<p><img src=\"{SamplePngDataUri}\" style=\"width: 60pt; height: 30pt\"></p>";

        DocumentReadResult result = ReadResult(html);
        InlineImage? image = result.Document.Paragraphs[0].StyleAt(0).Image;

        Assert.NotNull(image);
        Assert.Equal(60.0, image.Width);
        Assert.Equal(30.0, image.Height);
    }

    [Fact]
    public void Image_Dimensions_Are_Read_From_Html_Attributes()
    {
        // Attribute numbers mean px at 96 DPI: 96px = 72pt, 48px = 36pt.
        string html = $"<p><img src=\"{SamplePngDataUri}\" width=\"96\" height=\"48\"></p>";

        DocumentReadResult result = ReadResult(html);
        InlineImage? image = result.Document.Paragraphs[0].StyleAt(0).Image;

        Assert.NotNull(image);
        Assert.Equal(72.0, image.Width);
        Assert.Equal(36.0, image.Height);
    }

    [Fact]
    public void Image_Inside_Hyperlink_Carries_Link_Href()
    {
        string html = $"<p><a href=\"https://example.com/target\"><img src=\"{SamplePngDataUri}\"></a></p>";

        DocumentReadResult result = ReadResult(html);
        InlineStyle style = result.Document.Paragraphs[0].StyleAt(0);

        Assert.NotNull(style.Image);
        Assert.Equal("https://example.com/target", style.LinkHref);
    }

    [Fact]
    public void Resource_Policy_Denial_Omits_Image_And_Reports_Diagnostic()
    {
        string html = $"<p>Text <img src=\"{SamplePngDataUri}\"> end</p>";
        var options = new DocumentReadOptions(resourcePolicy: DocumentResourcePolicy.DenyAll);

        DocumentReadResult result = ReadResult(html, options);

        Assert.Equal("Text end", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, d => d.Code == "html.image.denied");
    }

    [Fact]
    public void Oversized_Embedded_Image_Exceeding_MaxBinBytes_Is_Skipped()
    {
        string html = $"<p><img src=\"{SamplePngDataUri}\"></p>";
        var limits = new DocumentLimits(maxBinBytes: 10);
        var options = new DocumentReadOptions(limits: limits);

        DocumentReadResult result = ReadResult(html, options);

        Assert.Contains(result.Diagnostics, d => d.Code == "html.image.limit");
    }

    [Fact]
    public void Malformed_Data_Uri_Reports_Diagnostic_And_Degrades_Gracefully()
    {
        string html = "<p>Before <img src=\"data:image/png;base64,not-valid-base64!!!\"> After</p>";

        DocumentReadResult result = ReadResult(html);

        Assert.Equal("Before After", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, d => d.Code == "html.image.malformed");
    }

    [Fact]
    public void External_Images_Remain_Skipped_Without_Network_Access()
    {
        string html = "<p>Before <img src=\"https://example.com/photo.jpg\"> After</p>";

        DocumentReadResult result = ReadResult(html);

        Assert.Equal("Before After", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, d => d.Code == "html.skip.external");
    }

    [Fact]
    public void Embedded_Image_Survives_Html_Round_Trip()
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
        new HtmlDocumentCodec().Write(document, stream, writeOptions);

        DocumentReadResult readBack = ReadResult(Encoding.UTF8.GetString(stream.ToArray()));
        RichTextParagraph p = readBack.Document.Paragraphs[0];

        Assert.Equal("Logo: " + InlineImage.PlaceholderText, p.Text);
        InlineImage? readImage = p.StyleAt(6).Image;
        Assert.NotNull(readImage);
        Assert.Equal(50.0, readImage.Width);
        Assert.Equal(25.0, readImage.Height);
        Assert.Equal("roundtrip logo", readImage.AltText);
        Assert.Equal(SamplePng, readImage.Data.ToArray());
    }
}
