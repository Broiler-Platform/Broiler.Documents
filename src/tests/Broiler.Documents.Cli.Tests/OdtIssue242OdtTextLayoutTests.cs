using System.Text;
using Broiler.Documents.Odt;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// Office conformance issue 242, read from ODF and laid out end to end: a
/// header taller than its least height keeps its spacing above the body, as
/// pagecounttest.ott's does, and a custom shape is drawn as the outline its
/// enhanced path states.
/// </summary>
public sealed class OdtIssue242OdtTextLayoutTests
{
    private const string Namespaces =
        "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
        "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" " +
        "xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" " +
        "xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" " +
        "xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" " +
        "xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\"";

    private static (RichTextDocument Document, LayoutResult Layout) Read(
        string headerProperties, string body = "<text:p>Body</text:p>", string headerParagraphStyle = "")
    {
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<office:document " + Namespaces + " office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.text\">" +
            "<office:automatic-styles>" +
            "<style:page-layout style:name=\"pm\"><style:page-layout-properties fo:page-width=\"595pt\" fo:page-height=\"842pt\" " +
            "fo:margin-left=\"56pt\" fo:margin-right=\"56pt\" fo:margin-top=\"56pt\" fo:margin-bottom=\"56pt\"/>" +
            "<style:header-style><style:header-footer-properties " + headerProperties + "/></style:header-style>" +
            "</style:page-layout>" +
            "<style:style style:name=\"blue\" style:family=\"graphic\"><style:graphic-properties draw:fill=\"solid\" draw:fill-color=\"#0000ff\"/></style:style>" +
            "<style:style style:name=\"MP\" style:family=\"paragraph\"><style:paragraph-properties " + headerParagraphStyle + "/></style:style>" +
            "</office:automatic-styles>" +
            "<office:master-styles><style:master-page style:name=\"Standard\" style:page-layout-name=\"pm\">" +
            "<style:header><text:p text:style-name=\"MP\">Page header</text:p></style:header></style:master-page></office:master-styles>" +
            "<office:body><office:text>" + body + "</office:text></office:body></office:document>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml), writable: false);
        RichTextDocument document = new OdtDocumentCodec().Read(stream).Document;
        using var paginated = new DocumentPaginator().Paginate(document, DocumentPaginator.PreferredSetup(document));
        return (document, paginated.Layout);
    }

    private static (LayoutLine Header, LayoutLine Body) Lines(LayoutResult layout)
    {
        LayoutPage page = layout.Pages[0];
        return (page.Lines.First(line => line.ParagraphIndex < 0), page.Lines.First(line => line.ParagraphIndex >= 0));
    }

    [Fact]
    public void A_Header_Taller_Than_Its_Least_Height_Keeps_Its_Spacing()
    {
        // No least height and 14 pt of spacing: the one-line header grows, and
        // the body starts 14 pt below it, not right under it.
        (RichTextDocument document, LayoutResult layout) = Read("fo:min-height=\"0pt\" fo:margin-bottom=\"14pt\"");
        (LayoutLine header, LayoutLine body) = Lines(layout);

        Assert.Equal(14, document.PageGeometry!.HeaderSpacing, 3);
        Assert.Equal(header.Top + header.Height + 14, body.Top, 1);
    }

    [Fact]
    public void A_Header_Within_Its_Least_Height_Leaves_The_Body_At_The_Margin()
    {
        (_, LayoutResult layout) = Read("fo:min-height=\"40pt\" fo:margin-bottom=\"14pt\"");
        (_, LayoutLine body) = Lines(layout);

        Assert.Equal(56 + 40 + 14, body.Top, 1);
    }

    [Fact]
    public void A_Header_Ends_At_Its_Last_Line()
    {
        // tdf134472.odt: the header's paragraphs have 1 cm below them, and the
        // body still starts the header's 14 pt below its last line.
        string xml = "fo:min-height=\"0pt\" fo:margin-bottom=\"14pt\"";
        (_, LayoutResult plain) = Read(xml);
        (RichTextDocument document, LayoutResult spaced) = Read(xml, headerParagraphStyle: "fo:margin-bottom=\"28pt\"");
        (LayoutLine header, LayoutLine body) = Lines(spaced);

        Assert.True(document.StyleDefaults.RunningBandsEndAtLastLine);
        Assert.Equal(header.Top + header.Height + 14, body.Top, 1);
        Assert.Equal(Lines(plain).Body.Top, body.Top, 1);
    }

    [Fact]
    public void Dynamic_Spacing_Keeps_No_Spacing_Of_Its_Own()
    {
        (RichTextDocument document, _) = Read("fo:min-height=\"0pt\" fo:margin-bottom=\"14pt\" style:dynamic-spacing=\"true\"");

        Assert.Equal(0, document.PageGeometry!.HeaderSpacing);
    }

    [Fact]
    public void A_Custom_Shape_Is_Drawn_As_Its_Enhanced_Path()
    {
        // pagecounttest.ott's four-pointed star: its inner corners are
        // equations of the shape's modifier.
        string star = "<text:p><draw:custom-shape text:anchor-type=\"paragraph\" draw:style-name=\"blue\" svg:width=\"200pt\" svg:height=\"200pt\" svg:x=\"0pt\" svg:y=\"0pt\">" +
            "<draw:enhanced-geometry svg:viewBox=\"0 0 21600 21600\" draw:type=\"star4\" draw:modifiers=\"8100\" " +
            "draw:enhanced-path=\"M 0 10800 L ?f4 ?f4 10800 0 ?f3 ?f4 21600 10800 ?f3 ?f3 10800 21600 ?f4 ?f3 0 10800 Z N\">" +
            "<draw:equation draw:name=\"f0\" draw:formula=\"7600\"/><draw:equation draw:name=\"f1\" draw:formula=\"?f0 *$0 /10800\"/>" +
            "<draw:equation draw:name=\"f2\" draw:formula=\"?f0 -?f1 \"/><draw:equation draw:name=\"f3\" draw:formula=\"10800+?f2 \"/>" +
            "<draw:equation draw:name=\"f4\" draw:formula=\"10800-?f2 \"/></draw:enhanced-geometry></draw:custom-shape></text:p>";
        (RichTextDocument document, LayoutResult layout) = Read("fo:min-height=\"0pt\"", star);

        var path = Assert.Single(Assert.Single(document.Shapes).OutlinePaths!);
        Assert.Equal(8, path.Count - 1);
        double inner = (10800 - (7600 - 7600 * 8100.0 / 10800)) / 21600;
        Assert.Equal((inner, inner), (path[1].X, path[1].Y));
        Assert.Equal((0.5, 0.0), path[2]);
        Assert.Same(document.Shapes[0].OutlinePaths, Assert.Single(layout.Pages[0].Shapes).OutlinePaths);
    }

    [Fact]
    public void A_Mirrored_Turned_Shape_Is_Mirrored_On_The_Page()
    {
        // flip_and_rotate.odt: Writer mirrors the turned shape, so the outline
        // mirrored in its box is turned back by twice the angle, here a half turn.
        string shape = "<text:p><draw:custom-shape text:anchor-type=\"paragraph\" draw:style-name=\"blue\" svg:width=\"100pt\" svg:height=\"100pt\" " +
            "draw:transform=\"rotate (1.5707963267949) translate (100pt 200pt)\">" +
            "<draw:enhanced-geometry svg:viewBox=\"0 0 100 100\" draw:type=\"mso-spt100\" draw:mirror-vertical=\"true\" " +
            "draw:enhanced-path=\"M 0 0 L 100 0 0 100 Z N\"/></draw:custom-shape></text:p>";
        (RichTextDocument document, _) = Read("fo:min-height=\"0pt\"", shape);

        var path = Assert.Single(Assert.Single(document.Shapes).OutlinePaths!);
        (double X, double Y)[] expected = [(1, 0), (0, 0), (1, 1)];
        Assert.Equal(expected.Length, path.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].X, path[i].X, 6);
            Assert.Equal(expected[i].Y, path[i].Y, 6);
        }
    }

    [Theory]
    [InlineData("M 0 0 C 10 10 20 20 30 30 Z N")]
    [InlineData("M 0 0 L $0 0 0 100 Z N")]
    public void A_Path_Of_Curves_Or_Unknown_Values_Keeps_The_Box(string path)
    {
        string shape = "<text:p><draw:custom-shape text:anchor-type=\"paragraph\" draw:style-name=\"blue\" svg:width=\"100pt\" svg:height=\"100pt\">" +
            "<draw:enhanced-geometry svg:viewBox=\"0 0 100 100\" draw:type=\"mso-spt100\" draw:enhanced-path=\"" + path + "\"/></draw:custom-shape></text:p>";
        (RichTextDocument document, _) = Read("fo:min-height=\"0pt\"", shape);

        Assert.Null(Assert.Single(document.Shapes).OutlinePaths);
    }
}
