using System.Linq;
using Broiler.Documents.Model;
using Xunit;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Covers DOCX paragraph frames specified via <c>w:framePr</c> (ECMA-376 17.3.1.11).
/// Word and LibreOffice import framed paragraphs as floating fly frames rather than
/// body flow paragraphs. Adjacent paragraphs sharing identical frame properties are
/// grouped into a single frame.
/// </summary>
public sealed class DocxParagraphFrameTests
{
    private const string A4Section =
        "<w:sectPr>" +
        "<w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +
        "<w:pgMar w:left=\"1417\" w:right=\"1417\" w:top=\"1417\" w:bottom=\"1417\"/>" +
        "</w:sectPr>";

    private const double MarginLeftPoints = 1417 / 20d;

    [Fact]
    public void Zero_Sized_Exact_Frame_In_Document_Defaults_Keeps_Paragraphs_In_Flow()
    {
        string defaults = DocxTestPackage.DocumentDefaults("", "<w:framePr w:w='0' w:h='0' w:hRule='exact'/>");
        RichTextDocument document = DocxTestPackage.ReadStyled(DocxTestPackage.Paragraph("Body") + A4Section, defaults).Document;

        Assert.Equal("Body", document.PlainText);
        Assert.Empty(document.Shapes);
    }

    [Theory]
    [InlineData("", 657.65)]
    [InlineData(" w:y='0'", 70.85)]
    public void Trailing_Frame_Uses_The_Last_Body_Paragraph_And_Resolves_Vertical_Placement(string offset, double y)
    {
        string body = DocxTestPackage.Paragraph("Body") +
            "<w:p><w:pPr><w:framePr w:h='2268' w:hRule='exact' w:yAlign='bottom'" + offset +
            "/></w:pPr><w:r><w:t>Frame</w:t></w:r></w:p>" + A4Section;

        DocumentShape frame = Assert.Single(DocxTestPackage.ReadBody(body).Document.Shapes);
        Assert.Equal(0, frame.ParagraphIndex);
        Assert.Equal(y, frame.OffsetY, 2);
    }

    [Fact]
    public void Frame_Before_A_Removed_Section_Mark_Stays_In_The_Closing_Section()
    {
        string body =
            "<w:p><w:r><w:t>First page</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:framePr w:vAnchor='page' w:y='1000'/></w:pPr><w:r><w:t>Frame</w:t></w:r></w:p>" +
            "<w:p><w:pPr>" + A4Section + "</w:pPr></w:p>" +
            "<w:p><w:r><w:t>Second page</w:t></w:r></w:p>" + A4Section;

        var document = DocxTestPackage.ReadBody(body).Document;
        Assert.Equal(["First page", "Second page"], document.Paragraphs.Select(p => p.Text));
        Assert.Equal(0, Assert.Single(document.Shapes).ParagraphIndex);
        Assert.True(document.Paragraphs[1].Style.PageBreakBefore);
    }

    [Fact]
    public void Direct_FramePr_Creates_DocumentShape_And_Removes_From_Body_Paragraphs()
    {
        // Matches tdf148671: paragraph with framePr positioned relative to page
        string bodyXml =
            "<w:p>" +
            "<w:pPr><w:framePr w:w=\"2833\" w:h=\"285\" w:hRule=\"exact\" w:wrap=\"notBeside\" w:vAnchor=\"page\" w:hAnchor=\"page\" w:x=\"8686\" w:y=\"3661\"/></w:pPr>" +
            "<w:r><w:t>Frame Text</w:t></w:r>" +
            "</w:p>" +
            "<w:p><w:r><w:t>Body Text</w:t></w:r></w:p>" +
            A4Section;

        DocumentReadResult result = DocxTestPackage.ReadBody(bodyXml);

        // Body paragraph should contain only "Body Text"
        Assert.Single(result.Document.Paragraphs);
        Assert.Equal("Body Text", result.Document.Paragraphs[0].Text);

        // Frame should be created as a floating DocumentShape
        Assert.Single(result.Document.Shapes);
        DocumentShape shape = result.Document.Shapes[0];
        Assert.NotNull(shape.Paragraphs);
        Assert.Single(shape.Paragraphs);
        Assert.Equal("Frame Text", shape.Paragraphs[0].Text);

        Assert.Equal(2833 / 20d, shape.Width, precision: 2);
        Assert.Equal(285 / 20d, shape.Height, precision: 2);
        Assert.False(shape.GrowsToText);
        Assert.Equal(ShapeWrap.TopAndBottom, shape.Wrap);
        Assert.True(shape.OffsetFromPage);
        Assert.Equal(3661 / 20d, shape.OffsetY, precision: 2);
        Assert.Equal((8686 / 20d) - MarginLeftPoints, shape.OffsetX, precision: 2);
    }

    [Fact]
    public void Style_Inherited_FramePr_Creates_DocumentShape()
    {
        // Matches tdf75573: frame properties defined on paragraph style
        string stylesXml =
            DocxTestPackage.Style(
                "Proprietary",
                "<w:pPr><w:framePr w:wrap=\"auto\" w:vAnchor=\"page\" w:hAnchor=\"page\" w:x=\"1675\" w:y=\"13145\"/></w:pPr>");

        string bodyXml =
            "<w:p>" +
            "<w:pPr><w:pStyle w:val=\"Proprietary\"/><w:framePr w:wrap=\"auto\"/></w:pPr>" +
            "<w:r><w:t>lorem ipsum</w:t></w:r>" +
            "</w:p>" +
            "<w:p><w:r><w:t>SOME TEXT</w:t></w:r></w:p>" +
            A4Section;

        DocumentReadResult result = DocxTestPackage.ReadStyled(bodyXml, stylesXml);

        Assert.Single(result.Document.Paragraphs);
        Assert.Equal("SOME TEXT", result.Document.Paragraphs[0].Text);

        Assert.Single(result.Document.Shapes);
        DocumentShape shape = result.Document.Shapes[0];
        Assert.NotNull(shape.Paragraphs);
        Assert.Single(shape.Paragraphs);
        Assert.Equal("lorem ipsum", shape.Paragraphs[0].Text);
        Assert.True(shape.OffsetFromPage);
        Assert.True(shape.GrowsToText);
        Assert.Equal(13145 / 20d, shape.OffsetY, precision: 2);
    }

    [Fact]
    public void Adjacent_Paragraphs_With_Identical_Frame_Are_Combined_Into_Single_Shape()
    {
        string bodyXml =
            "<w:p>" +
            "<w:pPr><w:framePr w:wrap=\"auto\" w:vAnchor=\"page\" w:hAnchor=\"page\" w:x=\"2000\" w:y=\"4000\"/></w:pPr>" +
            "<w:r><w:t>Para 1</w:t></w:r>" +
            "</w:p>" +
            "<w:p>" +
            "<w:pPr><w:framePr w:wrap=\"auto\" w:vAnchor=\"page\" w:hAnchor=\"page\" w:x=\"2000\" w:y=\"4000\"/></w:pPr>" +
            "<w:r><w:t>Para 2</w:t></w:r>" +
            "</w:p>" +
            "<w:p><w:r><w:t>Body</w:t></w:r></w:p>" +
            A4Section;

        DocumentReadResult result = DocxTestPackage.ReadBody(bodyXml);

        Assert.Single(result.Document.Paragraphs);
        Assert.Equal("Body", result.Document.Paragraphs[0].Text);

        Assert.Single(result.Document.Shapes);
        DocumentShape shape = result.Document.Shapes[0];
        Assert.NotNull(shape.Paragraphs);
        Assert.Equal(2, shape.Paragraphs.Count);
        Assert.Equal("Para 1", shape.Paragraphs[0].Text);
        Assert.Equal("Para 2", shape.Paragraphs[1].Text);
    }

    [Fact]
    public void Different_Frame_Coordinates_Create_Distinct_Shapes()
    {
        string bodyXml =
            "<w:p>" +
            "<w:pPr><w:framePr w:wrap=\"notBeside\" w:vAnchor=\"page\" w:hAnchor=\"page\" w:x=\"1000\" w:y=\"1000\"/></w:pPr>" +
            "<w:r><w:t>Frame 1</w:t></w:r>" +
            "</w:p>" +
            "<w:p>" +
            "<w:pPr><w:framePr w:wrap=\"notBeside\" w:vAnchor=\"page\" w:hAnchor=\"page\" w:x=\"2000\" w:y=\"2000\"/></w:pPr>" +
            "<w:r><w:t>Frame 2</w:t></w:r>" +
            "</w:p>" +
            A4Section;

        DocumentReadResult result = DocxTestPackage.ReadBody(bodyXml);

        Assert.Single(result.Document.Paragraphs);
        Assert.Equal("", result.Document.Paragraphs[0].Text);
        Assert.Equal(2, result.Document.Shapes.Count);
        Assert.Equal("Frame 1", result.Document.Shapes[0].Paragraphs![0].Text);
        Assert.Equal("Frame 2", result.Document.Shapes[1].Paragraphs![0].Text);
    }

    [Fact]
    public void DropCap_Is_Prepended_To_Host_Paragraph()
    {
        // Drop cap paragraph in Word OOXML is a preceding framed paragraph with w:dropCap="drop".
        // Its runs prepend to the subsequent body paragraph, matching Word and LibreOffice.
        string bodyXml =
            "<w:p>" +
            "<w:pPr><w:framePr w:dropCap=\"drop\" w:lines=\"3\"/></w:pPr>" +
            "<w:r><w:t>W</w:t></w:r>" +
            "</w:p>" +
            "<w:p><w:r><w:t>ord</w:t></w:r></w:p>" +
            A4Section;

        DocumentReadResult result = DocxTestPackage.ReadBody(bodyXml);

        Assert.Empty(result.Document.Shapes);
        Assert.Single(result.Document.Paragraphs);
        Assert.Equal("Word", result.Document.Paragraphs[0].Text);
    }

    [Fact]
    public void DropCap_Without_Following_Paragraph_Flushes_As_Standalone_Paragraph()
    {
        string bodyXml =
            "<w:p>" +
            "<w:pPr><w:framePr w:dropCap=\"drop\" w:lines=\"3\"/></w:pPr>" +
            "<w:r><w:t>W</w:t></w:r>" +
            "</w:p>" +
            A4Section;

        DocumentReadResult result = DocxTestPackage.ReadBody(bodyXml);

        Assert.Empty(result.Document.Shapes);
        Assert.Single(result.Document.Paragraphs);
        Assert.Equal("W", result.Document.Paragraphs[0].Text);
    }

    [Fact]
    public void Framed_Section_Following_Page_Break_Preserves_Break_And_Anchors_Shape()
    {
        // fdo55381: when all paragraphs in a section are framed shapes, the pending
        // page break from the preceding section break must not be dropped.
        string bodyXml =
            "<w:p><w:r><w:t>Page 1 text</w:t></w:r><w:pPr><w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/></w:sectPr></w:pPr></w:p>" +
            "<w:p>" +
            "<w:pPr><w:framePr w:wrap=\"notBeside\" w:vAnchor=\"page\" w:hAnchor=\"page\" w:x=\"1000\" w:y=\"1000\"/></w:pPr>" +
            "<w:r><w:t>Frame on page 2</w:t></w:r>" +
            "</w:p>" +
            A4Section;

        DocumentReadResult result = DocxTestPackage.ReadBody(bodyXml);

        Assert.Equal(2, result.Document.Paragraphs.Count);
        Assert.Equal("Page 1 text", result.Document.Paragraphs[0].Text);
        Assert.Equal("", result.Document.Paragraphs[1].Text);
        Assert.True(result.Document.Paragraphs[1].Style.PageBreakBefore);
        Assert.Single(result.Document.Shapes);
        Assert.Equal(1, result.Document.Shapes[0].ParagraphIndex);
        Assert.Equal("Frame on page 2", result.Document.Shapes[0].Paragraphs![0].Text);
    }
}
