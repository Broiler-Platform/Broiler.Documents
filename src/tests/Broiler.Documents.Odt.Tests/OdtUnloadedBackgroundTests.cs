using Broiler.Graphics.Color;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Backgrounds whose picture is not read, painted as LibreOffice paints a
/// picture it cannot load, and the page bounds LibreOffice gives a page
/// layout's fill by the document's producer.
/// </summary>
/// <remarks>
/// The colours and bounds are LibreOffice 24.2's: its reference of
/// <c>tiledrendering/data/page-background-link.fodt</c> (issue #178) fills the
/// page with <c>#729FCF</c>, and LibreOffice 26.2 draws the cases authored here
/// the same way. See <c>docs/notes/office-issue-178.md</c>.
/// </remarks>
public sealed class OdtUnloadedBackgroundTests
{
    private static readonly BColor Unloaded = BColor.FromArgb(0x72, 0x9F, 0xCF);
    private const string Link = "http://192.0.2.1:12345/background.png";

    private static DocumentReadResult ReadPage(string properties, string children = "", string? generator = null,
        IReadOnlyDictionary<string, byte[]>? pictures = null)
    {
        string styles = "<office:automatic-styles><style:page-layout style:name=\"pm\"><style:page-layout-properties " +
            "fo:page-width=\"200pt\" fo:page-height=\"200pt\" fo:margin-left=\"20pt\" fo:margin-right=\"20pt\" " +
            "fo:margin-top=\"20pt\" fo:margin-bottom=\"20pt\" " + properties + ">" + children +
            "</style:page-layout-properties></style:page-layout></office:automatic-styles>" +
            "<office:master-styles><style:master-page style:name=\"Standard\" style:page-layout-name=\"pm\"/></office:master-styles>";
        var parts = new Dictionary<string, string>();
        if (generator is not null)
        {
            parts["meta.xml"] = "<office:document-meta " + OdtTestPackage.NamespaceDeclarations +
                " xmlns:meta=\"urn:oasis:names:tc:opendocument:xmlns:meta:1.0\" office:version=\"1.3\"><office:meta>" +
                "<meta:generator>" + generator + "</meta:generator></office:meta></office:document-meta>";
        }
        using var stream = new MemoryStream(OdtTestPackage.FromBody(OdtTestPackage.Paragraph("body"), stylesPartInnerXml: styles,
            extraParts: parts, binaryParts: pictures));
        return new OdtDocumentCodec().Read(stream);
    }

    private static string LegacyImage(string href, string repeat = "no-repeat") =>
        "<style:background-image xlink:href=\"" + href + "\" xlink:type=\"simple\" xlink:actuate=\"onLoad\" " +
        "style:position=\"center\" style:repeat=\"" + repeat + "\"/>";

    [Fact]
    public void A_Page_Background_Linked_Outside_The_Package_Is_A_Page_Of_The_Unloaded_Colour()
    {
        DocumentReadResult result = ReadPage("", LegacyImage(Link));

        Assert.Equal(ShapeFill.Solid(Unloaded), result.Document.PageBackground);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "odt.image.external");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "odt.fill");
    }

    [Fact]
    public void The_Unloaded_Colour_Replaces_The_Background_Colour_Beside_The_Link()
    {
        Assert.Equal(ShapeFill.Solid(Unloaded), ReadPage("fo:background-color=\"#ff0000\"", LegacyImage(Link)).Document.PageBackground);
    }

    [Fact]
    public void A_Legacy_Picture_The_Package_Lacks_Leaves_The_Background_Colour()
    {
        DocumentReadResult result = ReadPage("fo:background-color=\"#ff0000\"", LegacyImage("Pictures/missing.png", "stretch"));

        Assert.Equal(ShapeFill.Solid(BColor.FromArgb(0xFF, 0, 0)), result.Document.PageBackground);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "odt.image.missing");
    }

    [Theory]
    [InlineData("draw:fill=\"bitmap\"")]
    [InlineData("draw:fill=\"bitmap\" draw:fill-image-name=\"undefined\"")]
    public void An_Unresolved_Page_Bitmap_Without_A_Colour_Paints_The_Unloaded_Colour(string properties)
    {
        Assert.Equal(ShapeFill.Solid(Unloaded), ReadPage(properties).Document.PageBackground);
    }

    [Fact]
    public void An_Unresolved_Page_Bitmap_Keeps_Its_Stated_Colour()
    {
        Assert.Equal(ShapeFill.Solid(BColor.FromArgb(0, 0xFF, 0)),
            ReadPage("draw:fill=\"bitmap\" draw:fill-color=\"#00ff00\"", LegacyImage(Link)).Document.PageBackground);
    }

    [Theory]
    [InlineData(null, "fo:background-color=\"#ff0000\"", "", false)]
    [InlineData("Broiler.Documents.Odt", "fo:background-color=\"#ff0000\"", "", false)]
    [InlineData("LibreOffice/24.2.7.2$Linux_X86_64 LibreOffice_project/ee3885777aa7032db5a9b65deec9457448a91162", "fo:background-color=\"#ff0000\"", "", false)]
    [InlineData("OpenOffice.org/3.3$Unix OpenOffice.org_project/330m20$Build-9567", "fo:background-color=\"#ff0000\"", "", true)]
    [InlineData("LibreOffice/6.2.8.2$Linux_X86_64 LibreOffice_project/f82ddfca21ebc1e222a662a32b25c0c9d20169ee", "fo:background-color=\"#ff0000\"", "", true)]
    [InlineData("LibreOfficeDev/6.4.8.0.0$Linux_X86_64 LibreOffice_project/29d686fea9f6705b262d369fede658f824154cc0", "fo:background-color=\"#ff0000\"", "", false)]
    [InlineData("LibreOfficeDev/6.4.8.0.0$Linux_X86_64 LibreOffice_project/29d686fea9f6705b262d369fede658f824154cc0", "", "stretch", true)]
    [InlineData("LibreOfficeDev/6.4.8.0.0$Linux_X86_64 LibreOffice_project/29d686fea9f6705b262d369fede658f824154cc0", "", "repeat", false)]
    public void A_Page_Layouts_Fill_Covers_What_Its_Producer_Gave_It(string? generator, string properties, string repeat, bool insideMargins)
    {
        string children = repeat.Length == 0 ? "" :
            "<style:background-image style:repeat=\"" + repeat + "\"><office:binary-data>" +
            Convert.ToBase64String(OdtTestPackage.OnePixelPng) + "</office:binary-data></style:background-image>";

        ShapeFill fill = Assert.IsType<ShapeFill>(ReadPage(properties, children, generator).Document.PageBackground);

        Assert.Equal(insideMargins, fill.ContentOnly);
    }

    [Fact]
    public void A_Drawing_Page_Fill_Keeps_Its_Own_Bounds_Whatever_The_Producer()
    {
        string styles = "<office:automatic-styles><style:page-layout style:name=\"pm\"><style:page-layout-properties fo:background-color=\"#ff0000\"/></style:page-layout>" +
            "<style:style style:name=\"dp\" style:family=\"drawing-page\"><style:drawing-page-properties draw:fill=\"solid\" draw:fill-color=\"#ff0000\" draw:background-size=\"full\"/></style:style>" +
            "</office:automatic-styles><office:master-styles><style:master-page style:name=\"Standard\" style:page-layout-name=\"pm\" draw:style-name=\"dp\"/></office:master-styles>";
        string meta = "<office:document-meta " + OdtTestPackage.NamespaceDeclarations +
            " xmlns:meta=\"urn:oasis:names:tc:opendocument:xmlns:meta:1.0\" office:version=\"1.3\"><office:meta>" +
            "<meta:generator>OpenOffice.org/3.3$Unix OpenOffice.org_project/330m20$Build-9567</meta:generator></office:meta></office:document-meta>";
        using var stream = new MemoryStream(OdtTestPackage.FromBody(OdtTestPackage.Paragraph("body"), stylesPartInnerXml: styles,
            extraParts: new Dictionary<string, string> { ["meta.xml"] = meta }));

        Assert.False(Assert.IsType<ShapeFill>(new OdtDocumentCodec().Read(stream).Document.PageBackground).ContentOnly);
    }

    [Theory]
    [InlineData(null, "WholePage")]
    [InlineData("", "WholePage")]
    [InlineData("MicrosoftOffice/16.0 MicrosoftWord", "WholePage")]
    [InlineData("LibreOffice/26.2.5.2$Linux_X86_64 LibreOffice_project/620$Build-2", "WholePage")]
    [InlineData("LibreOfficeDev/7.0.6.0.0$Linux_X86_64 LibreOffice_project/dfc40e2292c6e19e285c10ed8c8044d9454107d0", "WholePage")]
    [InlineData("LibreOfficeDev/6.3.0.0.alpha0$Linux_X86_64 LibreOffice_project/4ac1ffb2c5e3b9f0d5f9b1f1d2f3a4b5c6d7e8f9", "ByFill")]
    [InlineData("LibreOffice/5.4.1.2$Linux_X86_64 LibreOffice_project/ea7cb86e6eeb2bf3a5af73a8f7777ac570321527", "InsideMargins")]
    [InlineData("LibreOffice/4.3.7.2$Linux_X86_64 LibreOffice_project/430m0$Build-2", "InsideMargins")]
    [InlineData("OpenOffice/4.2.0$Win32 OpenOffice.org_project/420m1$Build-9800", "InsideMargins")]
    [InlineData("OpenOffice.org/2.2$Win32 OpenOffice.org_project/680m14$Build-9134", "InsideMargins")]
    [InlineData("StarOffice 7", "InsideMargins")]
    public void The_Producer_Is_Read_As_LibreOffice_Reads_Its_Build_Id(string? generator, string expected)
    {
        Assert.Equal(Enum.Parse<OdtPageFillBounds>(expected), OdtMetadata.PageFillBounds(generator));
    }

    [Theory]
    [InlineData("", 0x72, 0x9F, 0xCF)]
    [InlineData("<style:default-style style:family=\"graphic\"><style:graphic-properties draw:fill-color=\"#ff00ff\"/></style:default-style>", 0xFF, 0, 0xFF)]
    public void A_Shapes_Unloaded_Bitmap_Paints_The_Default_Styles_Colour_Or_The_Unloaded_One(string defaults, byte r, byte g, byte b)
    {
        string styles = "<office:styles>" + defaults +
            "<draw:fill-image draw:name=\"remote\" xlink:href=\"" + Link + "\" xlink:type=\"simple\" xlink:actuate=\"onLoad\"/>" +
            "<style:style style:name=\"g\" style:family=\"graphic\"><style:graphic-properties draw:fill=\"bitmap\" " +
            "draw:fill-image-name=\"remote\" style:repeat=\"stretch\"/></style:style></office:styles>";

        DocumentShape shape = Assert.Single(OdtTestPackage.ReadStyled(
            "<text:p>anchor<draw:rect draw:style-name=\"g\" svg:width=\"80pt\" svg:height=\"40pt\"/></text:p>", styles).Document.Shapes);

        Assert.Equal(ShapeFill.Solid(BColor.FromArgb(r, g, b)), shape.Fill);
    }

    [Fact]
    public void A_Paragraph_Background_Linked_Outside_The_Package_Is_The_Unloaded_Colour()
    {
        string styles = OdtTestPackage.Style("P1", "<style:paragraph-properties fo:background-color=\"#ff0000\">" + LegacyImage(Link) +
            "</style:paragraph-properties>");

        DocumentReadResult result = OdtTestPackage.ReadBody(OdtTestPackage.StyledParagraph("P1", "linked"), styles);

        Assert.Equal(Unloaded, result.Document.Paragraphs[0].Style.Background);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "odt.fill");
    }

    [Fact]
    public void A_Paragraphs_Embedded_Background_Picture_Leaves_Its_Colour()
    {
        string styles = OdtTestPackage.Style("P1", "<style:paragraph-properties fo:background-color=\"#ff0000\">" +
            "<style:background-image><office:binary-data>" + Convert.ToBase64String(OdtTestPackage.OnePixelPng) +
            "</office:binary-data></style:background-image></style:paragraph-properties>");

        DocumentReadResult result = OdtTestPackage.ReadBody(OdtTestPackage.StyledParagraph("P1", "embedded"), styles);

        Assert.Equal(BColor.FromArgb(0xFF, 0, 0), result.Document.Paragraphs[0].Style.Background);
    }
}
