namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Issue #236: the DOCX readings behind its layout differences - a table in a
/// header, a vertical percentage position, line breaks written between blocks,
/// and a body that states no section properties.
/// </summary>
public sealed class DocxIssue236Tests
{
    private const string HeaderRels =
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rH\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\"/>" +
        "</Relationships>";

    private const string HeaderTable =
        "<w:tbl><w:tblPr><w:tblW w:w=\"9000\" w:type=\"dxa\"/></w:tblPr>" +
        "<w:tblGrid><w:gridCol w:w=\"4500\"/><w:gridCol w:w=\"4500\"/></w:tblGrid>" +
        "<w:tr><w:tc><w:p><w:r><w:t>left</w:t></w:r></w:p></w:tc>" +
        "<w:tc><w:p><w:r><w:t>right</w:t></w:r></w:p></w:tc></w:tr></w:tbl>" +
        "<w:p/>";

    private static DocumentReadResult Read(string body, Dictionary<string, string>? parts = null)
    {
        using var stream = new MemoryStream(DocxTestPackage.FromBody(body, parts ?? []), writable: false);
        return new DocxDocumentCodec().Read(stream);
    }

    private static Dictionary<string, string> Header(string innerXml) => new(StringComparer.Ordinal)
    {
        ["word/_rels/document.xml.rels"] = HeaderRels,
        ["word/header1.xml"] = "<w:hdr " + DocxTestPackage.BodyNamespaceDeclarations + ">" + innerXml + "</w:hdr>",
    };

    [Fact]
    public void A_Table_In_A_Header_Is_Read_As_The_Headers_Table()
    {
        // FDO76312.docx: the header is a three-column table of a title, a
        // subtitle and a logo. Its cells were read as paragraphs stacked down
        // the page, the table itself dropped.
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("body") +
            "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rH\"/></w:sectPr>",
            Header(HeaderTable));

        RunningContent running = result.Document.RunningContent;
        IReadOnlyList<RichTextParagraph> header = running.EffectiveHeader(PageSelection.Default);
        DocumentTable table = Assert.Single(running.EffectiveHeaderTables(PageSelection.Default));
        Assert.Equal(2, Assert.Single(table.Rows).Cells.Count);
        Assert.Equal("left", header[table.Rows[0].Cells[0].ParagraphIndex].Text);
        Assert.Equal("right", header[table.Rows[0].Cells[1].ParagraphIndex].Text);
    }

    [Fact]
    public void A_Page_Border_Keeps_The_Headers_Table()
    {
        // The page border is added to the header band as a shape, which
        // replaced each header slot: the table must come along with it.
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("body") +
            "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rH\"/>" +
            "<w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +
            "<w:pgBorders w:offsetFrom=\"page\"><w:top w:val=\"single\" w:sz=\"8\" w:space=\"24\" w:color=\"auto\"/></w:pgBorders>" +
            "</w:sectPr>",
            Header(HeaderTable));

        RunningContent running = result.Document.RunningContent;
        Assert.Single(running.EffectiveHeaderShapes(PageSelection.Default));
        Assert.Single(running.EffectiveHeaderTables(PageSelection.Default));
    }

    [Fact]
    public void A_Vertical_Percentage_Position_Is_A_Share_Of_The_Page()
    {
        // FDO76312.docx's cover text box: wp14:pctPosVOffset 35000 against the
        // page. The offset was read as 0 from the anchoring paragraph, which
        // put the title at the top of the page instead of 35 % down it.
        string drawing =
            "<w:p><w:r><w:drawing>" +
            "<wp:anchor xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
            "xmlns:wp14=\"http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing\" " +
            "simplePos=\"0\" behindDoc=\"0\" relativeHeight=\"1\">" +
            "<wp:simplePos x=\"0\" y=\"0\"/>" +
            "<wp:positionH relativeFrom=\"page\"><wp14:pctPosHOffset>45500</wp14:pctPosHOffset></wp:positionH>" +
            "<wp:positionV relativeFrom=\"page\"><wp14:pctPosVOffset>35000</wp14:pctPosVOffset></wp:positionV>" +
            "<wp:extent cx=\"2906395\" cy=\"1270000\"/><wp:wrapNone/><wp:docPr id=\"1\" name=\"Text Box 1\"/>" +
            "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
            "<a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
            "<wps:wsp xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
            "<wps:spPr><a:solidFill><a:srgbClr val=\"4F81BD\"/></a:solidFill></wps:spPr>" +
            "</wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r></w:p>";

        DocumentReadResult result = Read(
            drawing +
            "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +
            "<w:pgMar w:top=\"720\" w:right=\"720\" w:bottom=\"284\" w:left=\"720\" w:header=\"0\" w:footer=\"0\"/></w:sectPr>");

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.True(shape.OffsetFromPage);
        Assert.Equal(841.9 * 0.35, shape.OffsetY, 1);
        Assert.Equal((595.3 * 0.455) - 36, shape.OffsetX, 1);
    }

    [Fact]
    public void Line_Breaks_Between_Blocks_Open_The_Next_Paragraph()
    {
        // tdf108714.docx: a w:br under w:body, two more at the head of a cell
        // and one in the cell's paragraph outside any run. Word and LibreOffice
        // start the cell with four empty lines; all four were dropped.
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("Paragraph 4") +
            "<w:br w:type=\"textWrapping\"/>" +
            "<w:tbl><w:tr><w:tc>" +
            "<w:br w:type=\"textWrapping\"/><w:br w:type=\"textWrapping\"/>" +
            "<w:p><w:br w:type=\"textWrapping\"/><w:r><w:t>Paragraph 5 in table</w:t></w:r></w:p>" +
            "</w:tc></w:tr></w:tbl>" +
            DocxTestPackage.Paragraph("Paragraph 6"));

        RichTextDocument document = result.Document;
        Assert.Equal("Paragraph 4", document.Paragraphs[0].Text);
        Assert.Equal(new string((char)0x2028, 4) + "Paragraph 5 in table", document.Paragraphs[1].Text);
        Assert.Equal("Paragraph 6", document.Paragraphs[2].Text);
        Assert.False(document.Paragraphs[2].Style.PageBreakBefore);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "docx.block.unsupported");
    }

    [Fact]
    public void A_Page_Break_Between_Blocks_Still_Starts_A_Page()
    {
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("one") + "<w:br w:type=\"page\"/>" + DocxTestPackage.Paragraph("two"));

        Assert.Equal("two", result.Document.Paragraphs[1].Text);
        Assert.True(result.Document.Paragraphs[1].Style.PageBreakBefore);
    }

    [Fact]
    public void A_Page_Break_Between_Blocks_Leaves_A_Following_Empty_Paragraph_On_Its_Page()
    {
        // tdf108714.docx's second page break precedes an empty paragraph. Word
        // reads the break as the first thing in that paragraph's first run, so
        // the empty paragraph ends page one and "Paragraph 3" opens page two
        // (the upstream test: paragraph 2 has no break, paragraph 3 one before
        // it). Breaking before the empty paragraph put "Paragraph 3" a line
        // down page two.
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("Paragraph 1") + "<w:br w:type=\"page\"/><w:p/>" +
            DocxTestPackage.Paragraph("Paragraph 3"));

        RichTextDocument document = result.Document;
        Assert.Equal(3, document.ParagraphCount);
        Assert.False(document.Paragraphs[1].Style.PageBreakBefore);
        Assert.Equal("Paragraph 3", document.Paragraphs[2].Text);
        Assert.True(document.Paragraphs[2].Style.PageBreakBefore);
    }

    private static string InlineRectangle(string alignment, string text = "") =>
        "<w:p><w:pPr><w:jc w:val=\"" + alignment + "\"/></w:pPr>" +
        (text.Length > 0 ? "<w:r><w:t>" + text + "</w:t></w:r>" : string.Empty) +
        "<w:r><mc:AlternateContent><mc:Choice Requires=\"wps\"><w:drawing>" +
        "<wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">" +
        "<wp:extent cx=\"1270000\" cy=\"635000\"/><wp:docPr id=\"1\" name=\"Rectangle 1\"/>" +
        "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
        "<a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
        "<wps:wsp xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
        "<wps:spPr><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val=\"99FF66\"/></a:solidFill></wps:spPr>" +
        "<wps:bodyPr/></wps:wsp></a:graphicData></a:graphic></wp:inline></w:drawing></mc:Choice>" +
        "<mc:Fallback><w:pict/></mc:Fallback></mc:AlternateContent></w:r></w:p>";

    [Fact]
    public void An_Inline_Shape_Alone_In_Its_Paragraph_Takes_Its_Line_And_Alignment()
    {
        // dml-shape-fillpattern.docx: centred paragraphs each holding one inline
        // rectangle. They were drawn at the column's left edge, one line tall,
        // with the caption below beside them; the document took two pages
        // where Word and LibreOffice take three.
        DocumentReadResult result = Read(
            InlineRectangle("center") +
            "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/>" +
            "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\"/></w:sectPr>");

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        ParagraphStyle style = result.Document.Paragraphs[0].Style;
        Assert.Equal((468 - 100) / 2d, shape.OffsetX, 1);
        Assert.Equal(0, shape.OffsetY, 1);
        Assert.Equal(LineHeightRule.Exact, style.LineHeightRule);
        Assert.Equal(50, style.LineHeight, 1);
    }

    [Fact]
    public void An_Inline_Shape_Beside_Text_Keeps_The_Paragraphs_Own_Line()
    {
        DocumentReadResult result = Read(InlineRectangle("center", "caption"));

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Equal(0, shape.OffsetX, 1);
        Assert.NotEqual(LineHeightRule.Exact, result.Document.Paragraphs[0].Style.LineHeightRule);
    }

    [Theory]
    [InlineData("<w:p/>", true)]
    [InlineData("<w:p><w:r><w:t>text</w:t></w:r></w:p>", false)]
    public void The_Empty_Paragraph_Closing_A_Cell_After_A_Nested_Table_Is_Hidden(string closing, bool hidden)
    {
        // fdo78907.docx's footer: a cell holding a nested table and the empty
        // paragraph Word needs to end the cell. LibreOffice gives that
        // paragraph no line; one with text keeps its line.
        string nested =
            "<w:tbl><w:tblGrid><w:gridCol w:w=\"3000\"/></w:tblGrid><w:tr><w:tc>" +
            DocxTestPackage.Paragraph("inner") + "</w:tc></w:tr></w:tbl>";
        DocumentReadResult result = Read(
            "<w:tbl><w:tblGrid><w:gridCol w:w=\"6000\"/></w:tblGrid><w:tr><w:tc>" +
            nested + closing + "</w:tc></w:tr></w:tbl>" +
            DocxTestPackage.Paragraph("after"));

        RichTextDocument document = result.Document;
        Assert.Equal("inner", document.Paragraphs[0].Text);
        Assert.Equal(hidden, document.Paragraphs[1].MarkStyle.Hidden);
        Assert.False(document.Paragraphs[2].MarkStyle.Hidden);
    }

    [Fact]
    public void A_Body_Without_Section_Properties_Is_Laid_Out_On_A_Letter_Page()
    {
        // tdf108714.docx states no w:sectPr. LibreOffice lays it out on Letter
        // with one-inch margins, the defaults of an empty w:sectPr; leaving the
        // page unstated drew it on the renderer's A4.
        PageGeometry page = Read(DocxTestPackage.Paragraph("body")).Document.PageGeometry!;

        Assert.Equal(612, page.Width, 1);
        Assert.Equal(792, page.Height, 1);
        Assert.Equal(72, page.MarginLeft, 1);
        Assert.Equal(72, page.MarginTop, 1);
    }
}
