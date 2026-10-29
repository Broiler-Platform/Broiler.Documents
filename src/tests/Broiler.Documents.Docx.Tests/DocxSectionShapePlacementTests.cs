namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// A floating object positioned against the page or a margin is converted to
/// the text column of the page it is on: the margins of the last section that
/// started a page, which a continuous section keeps. It was converted with the
/// document's last section, so in <c>tdf123163-1.docx</c>, whose last section
/// has a left margin of 275.5 points, a full-page backdrop in the first
/// section (134.9 points) stood 140 points left of the page.
/// </summary>
public sealed class DocxSectionShapePlacementTests
{
    private const string Vml = "xmlns:v=\"urn:schemas-microsoft-com:vml\"";

    private static string Section(int left, string type, bool inParagraph) =>
        (inParagraph ? "<w:p><w:pPr>" : string.Empty) +
        "<w:sectPr><w:type w:val=\"" + type + "\"/><w:pgSz w:w=\"12000\" w:h=\"16000\"/>" +
        "<w:pgMar w:left=\"" + left + "\" w:right=\"400\" w:top=\"400\" w:bottom=\"400\"/></w:sectPr>" +
        (inParagraph ? "</w:pPr></w:p>" : string.Empty);

    private static string PageShape(string marginLeft) =>
        "<w:p><w:r><w:pict " + Vml + "><v:rect fillcolor=\"#EAE9E4\" stroked=\"f\" style=\"position:absolute;" +
        "width:100pt;height:50pt;margin-left:" + marginLeft + ";margin-top:10pt;" +
        "mso-position-horizontal-relative:page;mso-position-vertical-relative:page\"/></w:pict></w:r>" +
        "<w:r><w:t>text</w:t></w:r></w:p>";

    [Fact]
    public void A_Page_Relative_Shape_Converts_With_Its_Own_Sections_Margin()
    {
        // First section 100 pt from the page's left edge, the last 20 pt.
        DocumentReadResult result = DocxTestPackage.ReadBody(
            PageShape("0pt") + Section(2000, "nextPage", inParagraph: true) +
            "<w:p><w:r><w:t>last</w:t></w:r></w:p>" + Section(400, "nextPage", inParagraph: false));

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Equal(-100, shape.OffsetX, 3);
    }

    [Fact]
    public void A_Continuous_Section_Measures_From_The_Page_It_Follows_On()
    {
        // The continuous section states 50 pt, but it is laid out in the page
        // its predecessor started, whose margin is 100 pt.
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:t>first</w:t></w:r></w:p>" + Section(2000, "nextPage", inParagraph: true) +
            PageShape("30pt") + Section(1000, "continuous", inParagraph: true) +
            "<w:p><w:r><w:t>last</w:t></w:r></w:p>" + Section(400, "nextPage", inParagraph: false));

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Equal(30 - 100, shape.OffsetX, 3);
    }

    [Fact]
    public void A_Section_That_Starts_A_Page_Takes_Over()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            "<w:p><w:r><w:t>first</w:t></w:r></w:p>" + Section(2000, "nextPage", inParagraph: true) +
            PageShape("30pt") + Section(1000, "nextPage", inParagraph: true) +
            "<w:p><w:r><w:t>last</w:t></w:r></w:p>" + Section(400, "nextPage", inParagraph: false));

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Equal(30 - 50, shape.OffsetX, 3);
    }

    [Fact]
    public void A_DrawingML_Anchor_On_The_Page_Converts_The_Same_Way()
    {
        string anchor = DocxTestPackage.AnchoredDrawingRun("rId1", 635000, 635000, offsetXEmus: 254000)
            .Replace("relativeFrom=\"column\"", "relativeFrom=\"page\"");
        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p>" + anchor + "<w:r><w:t>text</w:t></w:r></w:p>" + Section(2000, "nextPage", inParagraph: true) +
            "<w:p><w:r><w:t>last</w:t></w:r></w:p>" + Section(400, "nextPage", inParagraph: false),
            DocxTestPackage.ImageRelationship("rId1", "media/image1.png"),
            new Dictionary<string, byte[]> { ["word/media/image1.png"] = DocxTestPackage.OnePixelPng });

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Equal(20 - 100, shape.OffsetX, 3);
    }
}
