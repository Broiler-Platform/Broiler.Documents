using System.IO.Compression;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Covers <c>w:sectPr</c> page geometry. It was on the reader's silently-ignored
/// list, so a renderer had to invent a page — and a letter whose left margin is
/// 4.5 cm to make room for a letterhead stripe was laid out on whatever margin
/// the caller happened to name.
/// </summary>
public sealed class DocxPageGeometryTests
{
    private const string A4Section =
        "<w:sectPr>" +
        "<w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +
        "<w:pgMar w:left=\"2551\" w:right=\"1134\" w:top=\"1134\" w:bottom=\"1134\" " +
        "w:header=\"723\" w:footer=\"1134\" w:gutter=\"0\"/>" +
        "</w:sectPr>";

    private static RichTextDocument Read(string sectPr) =>
        DocxTestPackage.ReadBody(DocxTestPackage.Paragraph("body") + sectPr).Document;

    [Fact(Timeout = 600000)]
    public void Reads_The_Page_Size_In_Points()
    {
        PageGeometry geometry = Assert.IsType<PageGeometry>(Read(A4Section).PageGeometry);

        // 20 twips to the point, so 11906 x 16838 twips is A4.
        Assert.Equal(595.3, geometry.Width, 1);
        Assert.Equal(841.9, geometry.Height, 1);
        Assert.False(geometry.IsLandscape);
    }

    [Fact(Timeout = 600000)]
    public void Reads_The_Margins_A_Letterhead_Depends_On()
    {
        PageGeometry geometry = Assert.IsType<PageGeometry>(Read(A4Section).PageGeometry);

        // 4.5 cm of left margin: the room the stripe stands in.
        Assert.Equal(127.55, geometry.MarginLeft, 2);
        Assert.Equal(56.7, geometry.MarginRight, 2);
        Assert.Equal(56.7, geometry.MarginTop, 2);
        Assert.Equal(56.7, geometry.MarginBottom, 2);
    }

    [Fact(Timeout = 600000)]
    public void Reads_The_Header_And_Footer_Distances()
    {
        PageGeometry geometry = Assert.IsType<PageGeometry>(Read(A4Section).PageGeometry);

        Assert.Equal(36.15, geometry.HeaderDistance, 2);
        Assert.Equal(56.7, geometry.FooterDistance, 2);
    }

    [Fact(Timeout = 600000)]
    public void A_Document_Stating_No_Page_Size_Has_No_Geometry()
    {
        Assert.Null(Read("<w:sectPr/>").PageGeometry);
    }

    [Fact(Timeout = 600000)]
    public void Margins_That_Leave_No_Column_Are_Refused()
    {
        RichTextDocument document = Read(
            "<w:sectPr><w:pgSz w:w=\"2000\" w:h=\"2000\"/>" +
            "<w:pgMar w:left=\"1500\" w:right=\"1500\" w:top=\"0\" w:bottom=\"0\"/></w:sectPr>");

        // Honouring it would produce a page with nothing on it and no explanation.
        Assert.Null(document.PageGeometry);
    }

    [Fact(Timeout = 600000)]
    public void Reports_A_Page_It_Refused()
    {
        DocumentReadResult result = DocxTestPackage.ReadBody(
            DocxTestPackage.Paragraph("body") +
            "<w:sectPr><w:pgSz w:w=\"2000\" w:h=\"2000\"/>" +
            "<w:pgMar w:left=\"1500\" w:right=\"1500\" w:top=\"0\" w:bottom=\"0\"/></w:sectPr>");

        Assert.Contains(result.Diagnostics, d => d.Code == "docx.section.geometry");
    }

    [Fact(Timeout = 600000)]
    public void Landscape_Is_A_Page_Wider_Than_It_Is_Tall()
    {
        RichTextDocument document = Read(
            "<w:sectPr><w:pgSz w:w=\"16838\" w:h=\"11906\"/>" +
            "<w:pgMar w:left=\"720\" w:right=\"720\" w:top=\"720\" w:bottom=\"720\"/></w:sectPr>");

        Assert.True(Assert.IsType<PageGeometry>(document.PageGeometry).IsLandscape);
    }

    [Fact(Timeout = 600000)]
    public void The_Page_Survives_A_Round_Trip()
    {
        RichTextDocument source = Read(A4Section);

        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source), writable: false);
        PageGeometry geometry = Assert.IsType<PageGeometry>(
            new DocxDocumentCodec().Read(stream).Document.PageGeometry);

        Assert.Equal(source.PageGeometry!.Width, geometry.Width, 2);
        Assert.Equal(source.PageGeometry.MarginLeft, geometry.MarginLeft, 2);
        Assert.Equal(source.PageGeometry.HeaderDistance, geometry.HeaderDistance, 2);
    }

    [Fact(Timeout = 600000)]
    public void Writes_The_Section_A_Reader_Needs()
    {
        byte[] bytes = DocxDocumentCodec.WriteToArray(Read(A4Section));
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        string xml = reader.ReadToEnd();

        Assert.Contains("w:w=\"11906\"", xml, StringComparison.Ordinal);
        Assert.Contains("w:left=\"2551\"", xml, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void A_Document_With_No_Geometry_Writes_No_Page()
    {
        byte[] bytes = DocxDocumentCodec.WriteToArray(RichTextDocument.FromPlainText("body"));
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());

        // Inventing a page size would put words in the author's mouth.
        Assert.DoesNotContain("pgSz", reader.ReadToEnd(), StringComparison.Ordinal);
    }
}
