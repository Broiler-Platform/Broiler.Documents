namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Covers the frame an anchor states its offsets against. Only the offset was
/// read and <c>relativeFrom</c> was ignored, so a stripe positioned from the
/// page's edge was placed that far from the text column instead — and then
/// written back as column-relative, which made the wrong position the document's
/// own.
/// </summary>
public sealed class DocxAnchorFrameTests
{
    /// <summary>A4 with a 4.5 cm left margin: the room a letterhead stripe stands in.</summary>
    private const string A4Section =
        "<w:sectPr>" +
        "<w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +
        "<w:pgMar w:left=\"2551\" w:right=\"1134\" w:top=\"1134\" w:bottom=\"1134\"/>" +
        "</w:sectPr>";

    private const double MarginLeft = 127.55;
    private const double PageWidth = 595.3;
    private const double MarginRight = 56.7;

    /// <summary>12700 EMU to the point.</summary>
    private static long Emu(double points) => (long)(points * 12700);

    private static string ShapeRun(
        string horizontalFrom,
        string verticalFrom,
        double offsetXPoints = 0,
        double offsetYPoints = 0) =>
        "<w:r><w:drawing>" +
        "<wp:anchor xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\">" +
        "<wp:positionH relativeFrom=\"" + horizontalFrom + "\">" +
        "<wp:posOffset>" + Emu(offsetXPoints) + "</wp:posOffset></wp:positionH>" +
        "<wp:positionV relativeFrom=\"" + verticalFrom + "\">" +
        "<wp:posOffset>" + Emu(offsetYPoints) + "</wp:posOffset></wp:positionV>" +
        "<wp:extent cx=\"914400\" cy=\"228600\"/>" +
        "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
        "<a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
        "<wps:wsp xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
        "<wps:spPr><a:solidFill><a:srgbClr val=\"AECF00\"/></a:solidFill></wps:spPr>" +
        "</wps:wsp></a:graphicData></a:graphic></wp:anchor>" +
        "</w:drawing></w:r>";

    private static DocumentReadResult Read(string runXml, string sectPr = A4Section) =>
        DocxTestPackage.ReadBody("<w:p>" + runXml + "<w:r><w:t>body</w:t></w:r></w:p>" + sectPr);

    [Theory(Timeout = 600000)]
    // The page's left edge and the left margin's both sit a left margin to the
    // left of the column, so an offset of nothing is the column's negative.
    [InlineData("page", -MarginLeft)]
    [InlineData("leftMargin", -MarginLeft)]
    // The text area and the column start at the same place.
    [InlineData("column", 0)]
    [InlineData("margin", 0)]
    // The right margin starts where the column ends.
    [InlineData("rightMargin", PageWidth - MarginRight - MarginLeft)]
    public void Converts_A_Horizontal_Frame_To_The_Text_Column(string from, double expected)
    {
        DocumentShape shape = Assert.Single(Read(ShapeRun(from, "paragraph")).Document.Shapes);

        Assert.Equal(expected, shape.OffsetX, 1);
    }

    [Fact(Timeout = 600000)]
    public void Converts_A_Horizontal_Offset_Along_With_Its_Frame()
    {
        // An inch from the page's left edge, on a page whose margin is wider than
        // that, is still inside the margin - a negative column offset.
        DocumentShape shape = Assert.Single(
            Read(ShapeRun("page", "paragraph", offsetXPoints: 72)).Document.Shapes);

        Assert.Equal(72 - MarginLeft, shape.OffsetX, 1);
    }

    [Theory(Timeout = 600000)]
    [InlineData("insideMargin", -MarginLeft)]
    [InlineData("outsideMargin", PageWidth - MarginRight - MarginLeft)]
    public void Reads_A_Mirrored_Margin_As_An_Odd_Page_And_Says_So(string from, double expected)
    {
        DocumentReadResult result = Read(ShapeRun(from, "paragraph"));

        Assert.Equal(expected, Assert.Single(result.Document.Shapes).OffsetX, 1);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.anchor.relativefrom");
    }

    [Fact(Timeout = 600000)]
    public void Takes_An_Offset_As_Column_Relative_When_The_Document_States_No_Page()
    {
        // Nothing to convert with, so the offset is read the way every anchor
        // used to be read.
        DocumentShape shape = Assert.Single(
            Read(ShapeRun("page", "paragraph", offsetXPoints: 40), sectPr: string.Empty).Document.Shapes);

        Assert.Equal(40, shape.OffsetX, 1);
    }

    [Theory(Timeout = 600000)]
    [InlineData("paragraph")]
    [InlineData("line")]
    public void Keeps_A_Paragraph_Relative_Vertical_Offset_Without_A_Note(string from)
    {
        DocumentReadResult result = Read(ShapeRun("column", from, offsetYPoints: 20));

        Assert.Equal(20, Assert.Single(result.Document.Shapes).OffsetY, 1);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "docx.anchor.relativefrom");
    }

    [Theory(Timeout = 600000)]
    [InlineData("page")]
    [InlineData("margin")]
    [InlineData("topMargin")]
    [InlineData("bottomMargin")]
    public void Says_That_A_Page_Relative_Vertical_Offset_Was_Not_Converted(string from)
    {
        // Where a paragraph sits on the page is a layout result this reader does
        // not have, so the offset is kept and reported rather than guessed at.
        DocumentReadResult result = Read(ShapeRun("column", from, offsetYPoints: 20));

        Assert.Equal(20, Assert.Single(result.Document.Shapes).OffsetY, 1);
        DocumentDiagnostic note = Assert.Single(
            result.Diagnostics.Where(d => d.Code == "docx.anchor.relativefrom"));
        Assert.Contains("vertical", note.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void A_Page_Relative_Position_Survives_A_Round_Trip_Where_It_Was_Read()
    {
        // The bug this covers: the offset was kept as stated and then written
        // back as column-relative, so every save moved the stripe a left margin
        // further right.
        RichTextDocument source = Read(ShapeRun("page", "paragraph")).Document;

        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source), writable: false);
        RichTextDocument actual = new DocxDocumentCodec().Read(stream).Document;

        Assert.Equal(-MarginLeft, Assert.Single(source.Shapes).OffsetX, 1);
        Assert.Equal(-MarginLeft, Assert.Single(actual.Shapes).OffsetX, 1);
    }

    [Fact(Timeout = 600000)]
    public void Reports_The_Frame_Once_However_Many_Objects_State_One()
    {
        DocumentReadResult result = Read(
            ShapeRun("column", "page") + ShapeRun("column", "page"));

        Assert.Equal(2, result.Document.Shapes.Count);
        Assert.Single(result.Diagnostics.Where(d => d.Code == "docx.anchor.relativefrom"));
    }
}
