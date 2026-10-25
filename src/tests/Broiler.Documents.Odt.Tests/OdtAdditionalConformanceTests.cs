using System.IO.Compression;
using System.Xml.Linq;

namespace Broiler.Documents.Odt.Tests;

public sealed class OdtAdditionalConformanceTests
{
    [Theory]
    [InlineData(20, 20, 40, -20)]
    [InlineData(60, 15, 75, -15)]
    [InlineData(0, 0, 0, 0)]
    public void Legacy_List_Spacing_Maps_To_Explicit_Text_And_Label_Indents(int before, int width, int left, int first)
    {
        string list = "<text:list-style style:name='N'><text:list-level-style-number text:level='1' style:num-format='1'>" +
            "<style:list-level-properties text:space-before='" + before + "pt' text:min-label-width='" + width +
            "pt'/></text:list-level-style-number></text:list-style>";
        var result = OdtTestPackage.ReadStyled("<text:list text:style-name='N'><text:list-item><text:p>item</text:p>" +
            "<text:p>continuation</text:p></text:list-item></text:list>", "<office:styles>" + list + "</office:styles>");
        Assert.Equal(left, result.Document.Paragraphs[0].Style.LeftIndent);
        Assert.Equal(first, result.Document.Paragraphs[0].Style.FirstLineIndent);
        Assert.Equal(left, result.Document.Paragraphs[1].Style.LeftIndent);
        Assert.Equal(0, result.Document.Paragraphs[1].Style.FirstLineIndent);
    }

    [Theory]
    [InlineData("none", null)]
    [InlineData("dotted", ".")]
    public void Explicit_No_Leader_Overrides_Retained_Leader_Text(string style, string? expected)
    {
        var result = OdtTestPackage.ReadBody("<text:p text:style-name='P'>one<text:tab/>two</text:p>",
            "<style:style style:name='P' style:family='paragraph'><style:paragraph-properties><style:tab-stops>" +
            "<style:tab-stop style:position='120pt' style:leader-style='" + style + "' style:leader-text='.'/>" +
            "</style:tab-stops></style:paragraph-properties></style:style>");
        Assert.Equal(expected, Assert.Single(result.Document.Paragraphs[0].Style.TabStops!).Leader);
        if (expected is null)
        {
            using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(result.Document));
            Assert.Null(Assert.Single(new OdtDocumentCodec().Read(stream).Document.Paragraphs[0].Style.TabStops!).Leader);
        }
    }

    private static string Frame(string path) => "<draw:frame text:anchor-type='as-char' svg:width='1in' svg:height='1in'>" +
        "<draw:image xlink:href='" + path + "'/></draw:frame>";

    [Theory]
    [InlineData("https://example.test/picture", true)]
    [InlineData("javascript:alert(1)", false)]
    public void Clickable_Inline_Pictures_Keep_Allowed_Targets_And_Still_Enforce_Link_Policy(string href, bool allowed)
    {
        var result = OdtTestPackage.ReadWithPictures("<text:p><draw:a xlink:href='" + href + "'>" + Frame("Pictures/logo.png") +
            "</draw:a></text:p>", new Dictionary<string, byte[]> { ["Pictures/logo.png"] = OdtTestPackage.OnePixelPng },
            options: new DocumentReadOptions(resourcePolicy: DocumentResourcePolicy.AllowOwnDocuments));
        var style = result.Document.Paragraphs[0].StyleAt(0);
        Assert.NotNull(style.Image);
        Assert.Equal(allowed ? href : null, style.LinkHref);
        Assert.Equal(!allowed, result.Diagnostics.Any(d => d.Code == "odt.link"));
        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(result.Document,
            new DocumentWriteOptions(resources: result.Resources)));
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            using var content = zip.GetEntry("content.xml")!.Open();
            var xml = XDocument.Load(content);
            XNamespace draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
            if (allowed)
                Assert.Single(Assert.Single(xml.Descendants(draw + "a")).Elements(draw + "frame"));
            else
                Assert.Empty(xml.Descendants(draw + "a"));
        }
        stream.Position = 0;
        Assert.Equal(style.LinkHref, new OdtDocumentCodec().Read(stream).Document.Paragraphs[0].StyleAt(0).LinkHref);
    }

}
