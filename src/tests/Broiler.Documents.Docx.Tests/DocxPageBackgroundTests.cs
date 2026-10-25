using System.IO.Compression;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Covers the page background, <c>w:background</c>. It was not read at all, so a
/// document LibreOffice draws on a coloured page came out white - five of the
/// top ten in one ranking of LibreOffice's own test documents.
/// </summary>
public sealed class DocxPageBackgroundTests
{
    private const string Vml =
        "xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\"";

    private static DocumentReadResult Read(string background, string? settings = "<w:displayBackgroundShape/>")
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/document.xml"] =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<w:document " + DocxTestPackage.BodyNamespaceDeclarations + " " + Vml + ">" +
                background +
                "<w:body>" + DocxTestPackage.Paragraph("text") + "</w:body></w:document>",
        };
        if (settings is not null)
        {
            parts["word/settings.xml"] =
                "<w:settings " + DocxTestPackage.BodyNamespaceDeclarations + ">" + settings + "</w:settings>";
        }

        using var stream = new MemoryStream(DocxTestPackage.FromBody(string.Empty, parts), writable: false);
        return new DocxDocumentCodec().Read(stream);
    }

    private static BColor Rgb(int r, int g, int b) => BColor.FromArgb(r, g, b);

    [Fact]
    public void A_Page_Colour_Is_Read()
    {
        ShapeFill? fill = Read("<w:background w:color=\"FF0000\"/>").Document.PageBackground;

        Assert.Equal(ShapeFill.Solid(Rgb(255, 0, 0)), fill);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<w:displayBackgroundShape w:val=\"false\"/>")]
    public void A_Background_The_Settings_Do_Not_Display_Is_Not_Read(string? settings)
    {
        // ECMA-376 §17.15.1.25: Word keeps a page colour it is not told to show,
        // and LibreOffice reads neither colour nor shape without the switch.
        Assert.Null(Read("<w:background w:color=\"FF0000\"/>", settings).Document.PageBackground);
    }

    [Fact]
    public void A_Vml_Linear_Gradient_Starts_From_Its_Second_Colour()
    {
        // VML puts fillcolor at the bottom of a gradient at angle 0, and so starts
        // it from color2 at the top: the model's 90, running down the page from
        // red to blue, which is how LibreOffice reads it.
        ShapeFill? fill = Read(
            "<w:background w:color=\"0000FF\"><v:background fillcolor=\"blue\">" +
            "<v:fill color2=\"red\" type=\"gradient\"/></v:background></w:background>").Document.PageBackground;

        Assert.Equal(new ShapeFill(Rgb(255, 0, 0), Rgb(0, 0, 255), 90), fill);
    }

    [Theory]
    [InlineData("225", 225)]
    [InlineData("135", 315)]
    public void A_Vml_Focus_Of_Fifty_Percent_Is_An_Axial_Gradient(string angle, double modelAngle)
    {
        // tdf126533_axialAngle: fuchsia in two corners and lime along the
        // diagonal between them, which is what LibreOffice draws.
        ShapeFill? fill = Read(
            "<w:background w:color=\"FF00FF\"><v:background fillcolor=\"fuchsia\">" +
            "<v:fill color2=\"lime\" angle=\"" + angle + "\" focus=\"50%\" type=\"gradient\"/>" +
            "</v:background></w:background>").Document.PageBackground;

        Assert.Equal(new ShapeFill(Rgb(255, 0, 255), Rgb(0, 255, 0), modelAngle, Axial: true), fill);
    }

    [Fact]
    public void A_Negative_Vml_Angle_Reverses_The_Colours()
    {
        ShapeFill? fill = Read(
            "<w:background w:color=\"auto\"><v:background fillcolor=\"green\">" +
            "<v:fill color2=\"red\" angle=\"-150\" type=\"gradient\"/></v:background></w:background>")
            .Document.PageBackground;

        Assert.Equal(new ShapeFill(Rgb(0, 128, 0), Rgb(255, 0, 0), 240), fill);
    }

    [Fact]
    public void A_Rectangular_Gradient_Is_Its_Outer_Colour_And_Says_So()
    {
        // fill.docx: a focus of 100% puts the first colour at the focus and the
        // second around it, which is most of the page.
        DocumentReadResult result = Read(
            "<w:background w:color=\"000082\"><v:background fillcolor=\"#000082\">" +
            "<v:fill color2=\"#ff8200\" focus=\"100%\" type=\"gradientRadial\"/></v:background></w:background>");

        Assert.Equal(ShapeFill.Solid(Rgb(0xFF, 0x82, 0x00)), result.Document.PageBackground);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.background.fill");
    }

    [Fact]
    public void A_Pattern_Of_One_Colour_Is_That_Colour()
    {
        // tdf159626_blackPatternFill: black on black.
        DocumentReadResult result = Read(
            "<w:background w:color=\"000000\"><v:background fillcolor=\"black\">" +
            "<v:fill r:id=\"rId2\" color2=\"black\" type=\"pattern\"/></v:background></w:background>");

        Assert.Equal(ShapeFill.Solid(Rgb(0, 0, 0)), result.Document.PageBackground);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "docx.background.fill");
    }

    [Theory]
    [InlineData("#ff8200 [3204]", 0xFF, 0x82, 0x00)]
    [InlineData("#f80", 0xFF, 0x88, 0x00)]
    [InlineData("Navy", 0x00, 0x00, 0x80)]
    public void Vml_Colours_Are_Read_In_Their_Three_Spellings(string value, int r, int g, int b)
    {
        ShapeFill? fill = Read(
            "<w:background w:color=\"auto\"><v:background fillcolor=\"" + value + "\"/></w:background>").Document.PageBackground;

        Assert.Equal(ShapeFill.Solid(Rgb(r, g, b)), fill);
    }

    [Theory]
    [MemberData(nameof(Backgrounds))]
    public void The_Writer_Writes_A_Background_The_Reader_Reads_Back(ShapeFill background)
    {
        RichTextDocument document = RichTextDocument.FromPlainText("text").WithPageBackground(background);

        byte[] bytes = DocxWriter.WriteToArray(document);

        using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            using var settings = new StreamReader(archive.GetEntry("word/settings.xml")!.Open());
            Assert.Contains("displayBackgroundShape", settings.ReadToEnd());
        }

        using var stream = new MemoryStream(bytes, writable: false);
        Assert.Equal(background, new DocxDocumentCodec().Read(stream).Document.PageBackground);
    }

    public static TheoryData<ShapeFill> Backgrounds() => new()
    {
        ShapeFill.Solid(BColor.FromArgb(0x12, 0x34, 0x56)),
        new ShapeFill(BColor.FromArgb(255, 0, 0), BColor.FromArgb(0, 0, 255), 30),
        new ShapeFill(BColor.FromArgb(255, 0, 255), BColor.FromArgb(0, 255, 0), 225, Axial: true),
    };

    [Fact]
    public void A_Document_Without_A_Background_Writes_No_Settings_For_It()
    {
        byte[] bytes = DocxWriter.WriteToArray(RichTextDocument.FromPlainText("text"));

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.Null(archive.GetEntry("word/settings.xml"));
    }

    [Fact]
    public void A_Drawingml_Gradient_Whose_Ends_Agree_Is_Axial_And_Round_Trips()
    {
        var shape = new DocumentShape(
            0, 0, 0, 100, 50,
            fill: new ShapeFill(BColor.FromArgb(255, 0, 0), BColor.FromArgb(0, 0, 255), 90, Axial: true));
        RichTextDocument document = RichTextDocument.FromPlainText("text").WithShapes([shape]);

        using var stream = new MemoryStream(DocxWriter.WriteToArray(document), writable: false);
        DocumentShape read = Assert.Single(new DocxDocumentCodec().Read(stream).Document.Shapes);

        Assert.Equal(shape.Fill, read.Fill);
    }
}
