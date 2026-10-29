using System.Text;
using Broiler.Documents.Odt;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// A form template read from ODF and laid out end to end: the shape of
/// <c>fdo114306_2.odt</c>, issue #168's first difference, which LibreOffice draws
/// on four pages and this drew on eleven.
/// </summary>
/// <remarks>
/// The document is a dozen text sections under two page styles. Hidden
/// sections hold editing hints, a heading frame and a crest are kept off the
/// printed page, the footer's file name sits in a hidden section, and the
/// approval sheet that starts the second page style restarts the page count
/// its header shows.
/// </remarks>
public sealed class OdtFormTemplateLayoutTests
{
    private const string Namespaces =
        "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
        "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" " +
        "xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" " +
        "xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" " +
        "xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" " +
        "xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" " +
        "xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\"";

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("слово", count));

    private static string Template()
    {
        var body = new StringBuilder();
        body.Append("<text:section text:name=\"_main\" text:display=\"none\"><text:p/></text:section>");
        body.Append("<text:section text:name=\"main\">");
        body.Append("<text:section text:name=\"heading\"><text:p text:style-name=\"first\">");
        body.Append("<draw:frame draw:style-name=\"hint\" text:anchor-type=\"as-char\" svg:width=\"400pt\" svg:height=\"100pt\">");
        body.Append("<draw:text-box><text:p>ПРАВИТЕЛЬСТВО МОСКВЫ</text:p></draw:text-box></draw:frame></text:p></text:section>");
        body.Append("<text:section text:style-name=\"half\" text:name=\"title\"><text:p>О продлении сроков</text:p></text:section>");
        body.Append("</text:section>");
        for (int i = 0; i < 4; i++)
        {
            body.Append("<text:section text:name=\"_part").Append(i).Append("\" text:display=\"none\"><text:p/></text:section>");
            body.Append("<text:section text:name=\"part").Append(i).Append("\"><text:p>").Append(Words(60)).Append("</text:p></text:section>");
        }

        body.Append("<text:section text:name=\"_sheet\"><text:p text:style-name=\"sheet\"/></text:section>");
        body.Append("<text:section text:name=\"sheet\">");
        for (int i = 0; i < 40; i++)
            body.Append("<text:p>").Append(Words(40)).Append("</text:p>");
        body.Append("</text:section>");

        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<office:document " + Namespaces + " office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.text\">" +
            "<office:automatic-styles>" +
            "<style:page-layout style:name=\"pm\"><style:page-layout-properties fo:page-width=\"595pt\" fo:page-height=\"842pt\" " +
            "fo:margin-left=\"85pt\" fo:margin-right=\"28pt\" fo:margin-top=\"40pt\" fo:margin-bottom=\"40pt\"/>" +
            "<style:header-style><style:header-footer-properties fo:min-height=\"20pt\" fo:margin-bottom=\"10pt\"/></style:header-style>" +
            "<style:footer-style><style:header-footer-properties fo:min-height=\"20pt\" fo:margin-top=\"10pt\"/></style:footer-style>" +
            "</style:page-layout>" +
            "<style:style style:name=\"first\" style:family=\"paragraph\" style:master-page-name=\"Main\"/>" +
            "<style:style style:name=\"sheet\" style:family=\"paragraph\" style:master-page-name=\"Sheet\">" +
            "<style:paragraph-properties style:page-number=\"1\"/></style:style>" +
            "<style:style style:name=\"hint\" style:family=\"graphic\"><style:graphic-properties style:print-content=\"false\" style:wrap=\"none\"/></style:style>" +
            "<style:style style:name=\"half\" style:family=\"section\"><style:section-properties fo:margin-right=\"240pt\"/></style:style>" +
            "</office:automatic-styles>" +
            "<office:master-styles>" +
            "<style:master-page style:name=\"Main\" style:page-layout-name=\"pm\">" +
            "<style:header><text:p>- <text:page-number text:select-page=\"current\">3</text:page-number> -</text:p></style:header>" +
            "<style:footer><text:section text:name=\"file\" text:display=\"none\"><text:p>C:\\Temp\\51661523.ODT</text:p></text:section></style:footer>" +
            "</style:master-page>" +
            "<style:master-page style:name=\"Sheet\" style:page-layout-name=\"pm\">" +
            "<style:header><text:p>sheet - <text:page-number text:select-page=\"current\">3</text:page-number> -</text:p></style:header>" +
            "</style:master-page>" +
            "</office:master-styles>" +
            "<office:body><office:text>" + body + "</office:text></office:body></office:document>";
    }

    private static (RichTextDocument Document, LayoutResult Layout) Read()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Template()), writable: false);
        RichTextDocument document = new OdtDocumentCodec().Read(stream).Document;
        using var paginated = new DocumentPaginator().Paginate(document, DocumentPaginator.PreferredSetup(document));
        return (document, paginated.Layout);
    }

    private static string TextOf(LayoutLine line) => string.Concat(line.Pieces.Select(piece => piece.Text));

    [Fact]
    public void The_Sections_Of_A_Page_Style_Share_Its_Pages()
    {
        (RichTextDocument document, LayoutResult layout) = Read();

        // The title and every part follow on the first page style's page; only
        // the paragraph that starts the approval sheet's page style opens one.
        int sheet = Enumerable.Range(0, document.ParagraphCount).Last(index => document.Paragraphs[index].Style.PageBreakBefore);
        int[] mainPages = [.. layout.Pages.Where(page => page.Lines.Any(line => line.ParagraphIndex >= 0 && line.ParagraphIndex < sheet))
            .Select(page => page.Number)];
        int firstSheetPage = layout.Pages.First(page => page.Lines.Any(line => line.ParagraphIndex >= sheet)).Number;

        Assert.Equal([1], mainPages);
        Assert.Equal(2, firstSheetPage);
        Assert.True(layout.Pages.Count >= 3);
    }

    [Fact]
    public void The_Headers_Number_Each_Page_And_The_Sheet_Restarts_The_Count()
    {
        (_, LayoutResult layout) = Read();

        string[] headers = [.. layout.Pages.Select(page => string.Concat(page.Lines
            .Where(line => line.ParagraphIndex < 0 && line.Top < page.HeightPoints / 2).Select(TextOf)))];

        Assert.Equal(
            ["- 1 -", .. Enumerable.Range(1, layout.Pages.Count - 1).Select(number => "sheet - " + number + " -")],
            headers);
    }

    [Fact]
    public void What_The_Template_Keeps_Off_The_Page_Is_Not_Drawn()
    {
        (RichTextDocument document, LayoutResult layout) = Read();

        DocumentShape heading = Assert.Single(document.Shapes);
        Assert.False(heading.Printable);
        Assert.Empty(layout.Pages[0].Shapes);
        Assert.DoesNotContain(layout.Pages.SelectMany(page => page.Lines), line => TextOf(line).Contains("51661523", StringComparison.Ordinal));

        // The title stands below the heading frame it is not drawn with, and in
        // the left half of the page its section's right margin leaves it.
        LayoutLine title = layout.Pages[0].Lines.First(line => TextOf(line).Contains("О продлении", StringComparison.Ordinal));
        Assert.True(title.Top >= 40 + 20 + 10 + 100 - 0.5);
        Assert.True(title.Pieces[^1].X + title.Pieces[^1].Width <= 595 - 28 - 240 + 0.5);
    }
}
