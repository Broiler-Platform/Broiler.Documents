namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// The space Word 2013 and later leave above a paragraph that four or more
/// objects hang from, which LibreOffice's DOCX import imitates
/// (<c>HandleIncreasedAnchoredObjectSpacing</c>): <c>tdf115719.docx</c> puts
/// its two text boxes on page two, <c>tdf123243.docx</c>, with two objects,
/// keeps them on page one.
/// </summary>
public sealed class DocxAnchoredObjectSpacingTests
{
    /// <summary>US Letter with half-inch margins: a text area 540 points wide.</summary>
    private const string Section =
        "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/>" +
        "<w:pgMar w:top=\"720\" w:right=\"720\" w:bottom=\"720\" w:left=\"720\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>";

    private static string Shape(double width, double height, string wrap, bool behind) =>
        "<w:r><w:drawing>" +
        "<wp:anchor xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
        "distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\" behindDoc=\"" + (behind ? "1" : "0") + "\">" +
        "<wp:positionH relativeFrom=\"column\"><wp:posOffset>0</wp:posOffset></wp:positionH>" +
        "<wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>0</wp:posOffset></wp:positionV>" +
        "<wp:extent cx=\"" + (long)(width * 12700) + "\" cy=\"" + (long)(height * 12700) + "\"/>" +
        "<wp:" + wrap + "/>" +
        "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
        "<a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
        "<wps:wsp xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
        "<wps:spPr><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>" +
        "<a:solidFill><a:srgbClr val=\"5B9BD5\"/></a:solidFill></wps:spPr></wps:wsp>" +
        "</a:graphicData></a:graphic></wp:anchor>" +
        "</w:drawing></w:r>";

    private static RichTextDocument Read(string mode, params string[] shapes)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/settings.xml"] =
                "<w:settings " + DocxTestPackage.BodyNamespaceDeclarations + "><w:compat>" +
                "<w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"" + mode + "\"/>" +
                "</w:compat></w:settings>",
        };
        string body = "<w:p><w:r><w:t>First</w:t></w:r></w:p><w:p>" + string.Concat(shapes) + "</w:p>" + Section;
        using var stream = new MemoryStream(DocxTestPackage.FromBody(body, parts), writable: false);
        return new DocxDocumentCodec().Read(stream).Document;
    }

    private static readonly string[] AsInTdf115719 =
    [
        Shape(60, 50, "wrapNone", behind: true),
        Shape(60, 70, "wrapNone", behind: false),
        Shape(270, 100, "wrapSquare wrapText=\"bothSides\"", behind: false),
        Shape(270, 200, "wrapSquare wrapText=\"bothSides\"", behind: false),
    ];

    [Fact]
    public void Four_Objects_Wider_Together_Than_The_Text_Area_Push_Their_Paragraph_Down()
    {
        RichTextDocument document = Read("15", AsInTdf115719);

        Assert.Equal(420, document.Paragraphs[1].Style.SpacingBefore, 3);
        Assert.Equal(0, document.Paragraphs[0].Style.SpacingBefore, 3);
    }

    [Fact]
    public void Word_2010_Documents_Are_Not_Affected()
    {
        Assert.Equal(0, Read("14", AsInTdf115719).Paragraphs[1].Style.SpacingBefore, 3);
    }

    [Fact]
    public void Fewer_Than_Four_Objects_Leave_The_Spacing_Alone()
    {
        // tdf123243.docx: two text boxes.
        Assert.Equal(0, Read("15", AsInTdf115719[2], AsInTdf115719[3]).Paragraphs[1].Style.SpacingBefore, 3);
    }

    [Fact]
    public void Objects_None_Of_Them_Behind_The_Text_Leave_The_Spacing_Alone()
    {
        string[] shapes = [Shape(60, 50, "wrapNone", behind: false), .. AsInTdf115719[1..]];

        Assert.Equal(0, Read("15", shapes).Paragraphs[1].Style.SpacingBefore, 3);
    }

    [Fact]
    public void Wrapping_Objects_That_Leave_Room_Beside_Them_Leave_The_Spacing_Alone()
    {
        string[] shapes = [.. AsInTdf115719[..3], Shape(260, 200, "wrapSquare wrapText=\"bothSides\"", behind: false)];

        Assert.Equal(0, Read("15", shapes).Paragraphs[1].Style.SpacingBefore, 3);
    }
}
