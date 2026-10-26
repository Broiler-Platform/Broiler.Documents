using Broiler.Documents.Resources;

namespace Broiler.Documents.Docx.Tests;

public sealed class DocxBackgroundImageTests
{
    private static DocumentReadResult Read(string type = "frame", string? settings = "<w:displayBackgroundShape/>",
        string attributes = "", bool external = false, bool missing = false, bool oversized = false, DocumentReadOptions? options = null, string? color = "FFFFFF")
    {
        var parts = new Dictionary<string, string>
        {
            ["word/document.xml"] = "<w:document " + DocxTestPackage.BodyNamespaceDeclarations +
                " xmlns:v='urn:schemas-microsoft-com:vml'><w:background" + (color is null ? "" : " w:color='" + color + "'") + "><v:background>" +
                "<v:fill type='" + type + "' r:id='bg' " + attributes + "/></v:background></w:background>" +
                "<w:body><w:p/></w:body></w:document>",
            ["word/settings.xml"] = "<w:settings " + DocxTestPackage.BodyNamespaceDeclarations + ">" + settings + "</w:settings>",
            ["word/_rels/document.xml.rels"] = "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>" +
                "<Relationship Id='bg' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/image' Target='" +
                (external ? "https://example.invalid/background.png' TargetMode='External" : "media/background.png") + "'/></Relationships>",
        };
        var media = new Dictionary<string, byte[]>();
        if (!missing) media["word/media/background.png"] = oversized ? new byte[5000] : DocxTestPackage.OnePixelPng;
        using var stream = new MemoryStream(DocxTestPackage.FromBody("", parts, media));
        return new DocxDocumentCodec().Read(stream, options);
    }

    [Theory]
    [InlineData("frame")]
    [InlineData("tile")]
    [InlineData("solid")]
    [InlineData("gradient")]
    public void Vml_Without_An_Activating_Colour_Does_Not_Paint_Or_Load_Resources(string type)
    {
        var result = Read(type, color: null, external: true);
        Assert.Null(result.Document.PageBackground);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.background.color");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "docx.image.external");
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("FFFFFF")]
    public void Explicit_Automatic_Or_Numeric_Colour_Activates_The_Embedded_Background(string color)
    {
        Assert.NotNull(Read(color: color).Document.PageBackground?.Image);
    }

    [Theory]
    [InlineData("frame", ImageFillRepeat.Stretch)]
    [InlineData("tile", ImageFillRepeat.Tile)]
    public void Embedded_background_images_keep_their_bytes_and_repeat_mode(string type, ImageFillRepeat repeat)
    {
        var result = Read(type);
        ImageFill image = Assert.IsType<ImageFill>(result.Document.PageBackground?.Image);
        Assert.Equal(repeat, image.Repeat);
        Assert.False(image.Picture.ResourceId.IsNone);
        Assert.True(image.Picture.TryGetEncoded(out ReadOnlyMemory<byte> bytes, out _));
        Assert.Equal(DocxTestPackage.OnePixelPng, bytes.ToArray());
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "docx.background.fill");
    }

    [Theory]
    [InlineData(true, false, "docx.image.external")]
    [InlineData(false, true, "docx.image.missing")]
    public void Unavailable_backgrounds_fall_back_with_diagnostics(bool external, bool missing, string code)
    {
        var result = Read(external: external, missing: missing);
        Assert.Null(result.Document.PageBackground?.Image);
        Assert.Contains(result.Diagnostics, d => d.Code == code);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.background.fill");
    }

    [Fact]
    public void Custom_vml_image_layout_keeps_the_diagnosed_colour_fallback()
    {
        var result = Read(attributes: "size='0.5,0.5'", external: true);
        Assert.Null(result.Document.PageBackground?.Image);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.background.fill");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "docx.image.external");
    }

    [Fact]
    public void Resource_denial_does_not_leave_image_bytes_in_the_model()
    {
        var result = Read(options: new DocumentReadOptions(resourcePolicy: DocumentResourcePolicy.DenyAll));
        Assert.Null(result.Document.PageBackground?.Image);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.image.denied");
    }

    [Fact]
    public void Image_size_limit_applies_to_backgrounds()
    {
        var result = Read(oversized: true, options: new DocumentReadOptions(new DocumentLimits(maxBinBytes: 4096)));
        Assert.Null(result.Document.PageBackground?.Image);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.image.limit");
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("<w:displayBackgroundShape/>", "on='false'")]
    public void Disabled_backgrounds_do_not_load_the_image(string? settings, string attributes)
    {
        var result = Read(settings: settings, attributes: attributes, external: true);
        Assert.Null(result.Document.PageBackground?.Image);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "docx.image.external");
    }
}
