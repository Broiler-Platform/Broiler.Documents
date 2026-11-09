using System.Xml.Linq;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Issue #218: the DOCX readings behind the highest-severity conformance
/// failures - page borders, aligned and percentage anchor positions, and a
/// title-page section that names no first-page header.
/// </summary>
public sealed class DocxIssue218Tests
{
    private const string HeaderRels =
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rH\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\"/>" +
        "</Relationships>";

    private static DocumentReadResult Read(string body, Dictionary<string, string>? parts = null)
    {
        using var stream = new MemoryStream(DocxTestPackage.FromBody(body, parts ?? []), writable: false);
        return new DocxDocumentCodec().Read(stream);
    }

    private const string Borders =
        "<w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +
        "<w:pgMar w:top=\"1729\" w:right=\"1729\" w:bottom=\"1729\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/>" +
        "<w:pgBorders w:display=\"{0}\" w:offsetFrom=\"page\">" +
        "<w:top w:val=\"single\" w:sz=\"24\" w:space=\"30\" w:color=\"auto\"/>" +
        "<w:left w:val=\"single\" w:sz=\"24\" w:space=\"30\" w:color=\"auto\"/>" +
        "<w:bottom w:val=\"single\" w:sz=\"24\" w:space=\"30\" w:color=\"auto\"/>" +
        "<w:right w:val=\"single\" w:sz=\"24\" w:space=\"30\" w:color=\"auto\"/>" +
        "</w:pgBorders>";

    [Fact]
    public void First_Page_Border_Is_Drawn_On_The_First_Page_Only()
    {
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("") + "<w:sectPr>" + string.Format(Borders, "firstPage") + "</w:sectPr>");

        RunningContent running = result.Document.RunningContent;
        DocumentShape border = Assert.Single(running.EffectiveHeaderShapes(PageSelection.First));
        Assert.Empty(running.EffectiveHeaderShapes(PageSelection.Default));
        Assert.Empty(running.EffectiveHeaderShapes(PageSelection.Even));

        Assert.Null(border.Fill);
        Assert.Equal(30, border.OffsetY, 3);
        Assert.Equal(30 - 72, border.OffsetX, 3);
        Assert.Equal(595.3 - 60, border.Width, 1);
        Assert.Equal(841.9 - 60, border.Height, 1);
        Assert.Equal(3, border.OutlineWidth, 3);
    }

    [Fact]
    public void All_Pages_Border_Is_Drawn_On_Every_Page()
    {
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("") + "<w:sectPr>" + string.Format(Borders, "allPages") + "</w:sectPr>");

        RunningContent running = result.Document.RunningContent;
        Assert.Single(running.EffectiveHeaderShapes(PageSelection.First));
        Assert.Single(running.EffectiveHeaderShapes(PageSelection.Default));
        Assert.Single(running.EffectiveHeaderShapes(PageSelection.Even));
    }

    [Fact]
    public void Later_Title_Page_Section_Without_Any_Header_Of_Its_Own_Keeps_The_Default_Header()
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/_rels/document.xml.rels"] = HeaderRels,
            ["word/header1.xml"] = "<w:hdr " + DocxTestPackage.BodyNamespaceDeclarations + "><w:p><w:r><w:t>head</w:t></w:r></w:p></w:hdr>",
        };

        DocumentReadResult result = Read(
            "<w:p><w:pPr><w:sectPr><w:headerReference w:type=\"default\" r:id=\"rH\"/></w:sectPr></w:pPr>" +
            "<w:r><w:t>first section</w:t></w:r></w:p>" +
            DocxTestPackage.Paragraph("body") +
            "<w:sectPr><w:titlePg/></w:sectPr>",
            parts);

        RunningContent running = result.Document.RunningContent;
        Assert.False(running.DifferentFirstPage);
        Assert.Equal("head", Assert.Single(running.EffectiveHeader(PageSelection.First)).Text);
    }

    private static XElement Anchor(string positionH, string positionV) => XElement.Parse(
        "<wp:anchor xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
        "xmlns:wp14=\"http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing\">" +
        positionH + positionV +
        "<wp:extent cx=\"3118485\" cy=\"10058400\"/></wp:anchor>");

    [Fact]
    public void Right_Aligned_Anchor_Sits_At_The_Right_Page_Edge()
    {
        XElement anchor = Anchor(
            "<wp:positionH relativeFrom=\"page\"><wp:align>right</wp:align></wp:positionH>",
            "<wp:positionV relativeFrom=\"page\"><wp:align>top</wp:align></wp:positionV>");
        var page = new PageGeometry(612, 792, 72, 72, 72, 72);

        Assert.Equal(612 - 245.55, DocxReader.AlignedOrRelativeH(anchor, "page", page)!.Value, 2);
        Assert.Equal(0, DocxReader.AlignedOrRelativeV(anchor, "page", page)!.Value, 3);
    }

    [Fact]
    public void Percentage_Offsets_Are_A_Share_Of_The_Frame()
    {
        XElement anchor = Anchor(
            "<wp:positionH relativeFrom=\"margin\"><wp14:pctPosHOffset>10000</wp14:pctPosHOffset></wp:positionH>",
            "<wp:positionV relativeFrom=\"page\"><wp14:pctPosVOffset>25000</wp14:pctPosVOffset></wp:positionV>");
        var page = new PageGeometry(612, 792, 72, 72, 72, 72);

        Assert.Equal(72 + 46.8, DocxReader.AlignedOrRelativeH(anchor, "margin", page)!.Value, 3);
        Assert.Equal(198, DocxReader.AlignedOrRelativeV(anchor, "page", page)!.Value, 3);
    }

    [Fact]
    public void Plain_Offsets_Are_Left_To_The_Existing_Reading()
    {
        XElement anchor = Anchor(
            "<wp:positionH relativeFrom=\"page\"><wp:posOffset>12700</wp:posOffset></wp:positionH>",
            "<wp:positionV relativeFrom=\"paragraph\"><wp:align>top</wp:align></wp:positionV>");
        var page = new PageGeometry(612, 792, 72, 72, 72, 72);

        Assert.Null(DocxReader.AlignedOrRelativeH(anchor, "page", page));
        Assert.Null(DocxReader.AlignedOrRelativeV(anchor, "paragraph", page));
    }
}
