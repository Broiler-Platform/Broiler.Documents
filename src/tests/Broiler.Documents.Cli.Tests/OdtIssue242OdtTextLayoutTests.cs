using System.Text;
using Broiler.Documents.Odt;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// Office conformance issue 242, read from ODF and laid out end to end: a
/// header taller than its least height keeps its spacing above the body, as
/// pagecounttest.ott's does.
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
}
