using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Documents.Model.Spreadsheets;
using Xunit;

namespace Broiler.Documents.Ods.Tests;

/// <summary>
/// Office conformance issue 249 (mailmerge/data/*.ods, calc-data-source.ods):
/// a sheet is printed on the page its page style states, with that style's
/// header and footer, in the cell defaults Calc's styles state, numbers to
/// the right and text at the foot of its cell.
/// </summary>
public sealed class OdsSheetPrintingTests
{
    private const string Namespaces =
        "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
        "xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" " +
        "xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" " +
        "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" " +
        "xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" " +
        "xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\"";

    // As Calc 24.2 writes a new document: the Default page style with no
    // margins stated, a 7.5 mm header and footer, the sheet's name above and
    // the page number below; the pool's cell font is Liberation Sans.
    private const string Styles =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document-styles " + Namespaces + " office:version=\"1.3\">" +
        "<office:font-face-decls><style:font-face style:name=\"Liberation Sans\" svg:font-family=\"&apos;Liberation Sans&apos;\"/>" +
        "<style:font-face style:name=\"Calibri\" svg:font-family=\"Calibri\"/></office:font-face-decls>" +
        "<office:styles><style:default-style style:family=\"table-cell\"><style:text-properties style:font-name=\"Liberation Sans\"/></style:default-style>" +
        "<style:style style:name=\"Default\" style:family=\"table-cell\"/>" +
        "<style:style style:name=\"Heading\" style:family=\"table-cell\" style:parent-style-name=\"Default\">" +
        "<style:text-properties style:font-name=\"Calibri\" fo:font-size=\"16pt\"/></style:style>" +
        "<style:style style:name=\"Centred\" style:family=\"table-cell\" style:parent-style-name=\"Default\">" +
        "<style:table-cell-properties style:text-align-source=\"fix\" style:vertical-align=\"top\"/><style:paragraph-properties fo:text-align=\"center\"/></style:style>" +
        "</office:styles>" +
        "<office:automatic-styles><style:page-layout style:name=\"Mpm1\"><style:page-layout-properties style:writing-mode=\"lr-tb\"/>" +
        "<style:header-style><style:header-footer-properties fo:min-height=\"7.5mm\" fo:margin-bottom=\"2.5mm\"/></style:header-style>" +
        "<style:footer-style><style:header-footer-properties fo:min-height=\"7.5mm\" fo:margin-top=\"2.5mm\"/></style:footer-style>" +
        "</style:page-layout></office:automatic-styles>" +
        "<office:master-styles><style:master-page style:name=\"Default\" style:page-layout-name=\"Mpm1\">" +
        "<style:header><text:p><text:sheet-name>???</text:sheet-name></text:p></style:header>" +
        "<style:header-left style:display=\"false\"/>" +
        "<style:footer><text:p>Page <text:page-number>1</text:page-number></text:p></style:footer>" +
        "</style:master-page></office:master-styles></office:document-styles>";

    private const string Content =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document-content " + Namespaces + " office:version=\"1.3\">" +
        "<office:automatic-styles><style:style style:name=\"ta1\" style:family=\"table\" style:master-page-name=\"Default\"/>" +
        "<style:style style:name=\"ce1\" style:family=\"table-cell\" style:parent-style-name=\"Heading\"/></office:automatic-styles>" +
        "<office:body><office:spreadsheet><table:table table:name=\"addresses\" table:style-name=\"ta1\">" +
        "<table:table-column table:number-columns-repeated=\"3\" table:default-cell-style-name=\"Default\"/>" +
        "<table:table-row><table:table-cell table:style-name=\"ce1\" office:value-type=\"string\"><text:p>Name</text:p></table:table-cell>" +
        "<table:table-cell office:value-type=\"float\" office:value=\"81669\"><text:p>81669</text:p></table:table-cell>" +
        "<table:table-cell table:style-name=\"Centred\" office:value-type=\"float\" office:value=\"2\"><text:p>2</text:p></table:table-cell></table:table-row>" +
        "</table:table></office:spreadsheet></office:body></office:document-content>";

    private static OdsReadResult Read()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (Stream mime = archive.CreateEntry("mimetype", CompressionLevel.NoCompression).Open())
                mime.Write(Encoding.ASCII.GetBytes("application/vnd.oasis.opendocument.spreadsheet"));
            using (Stream styles = archive.CreateEntry("styles.xml").Open())
                styles.Write(Encoding.UTF8.GetBytes(Styles));
            using (Stream content = archive.CreateEntry("content.xml").Open())
                content.Write(Encoding.UTF8.GetBytes(Content));
        }

        stream.Position = 0;
        return (OdsReadResult)new OdsDocumentCodec().Read(stream);
    }

    [Fact]
    public void A_Sheet_Is_Printed_On_Its_Page_Style_With_Its_Header_And_Footer()
    {
        RichTextDocument document = Read().Document;

        // 2 cm margins where the layout states none, A4 paper, and the 7.5 mm
        // bands inside the margins: the text starts 2.75 cm down.
        PageGeometry page = Assert.IsType<PageGeometry>(document.PageGeometry);
        double twoCm = 2 * 72 / 2.54, band = 7.5 * 72 / 25.4;
        Assert.Equal(PageGeometry.A4.Width, page.Width, 3);
        Assert.Equal(PageGeometry.A4.Height, page.Height, 3);
        Assert.Equal(twoCm, page.MarginLeft, 3);
        Assert.Equal(twoCm + band, page.MarginTop, 3);
        Assert.Equal(twoCm + band, page.MarginBottom, 3);
        Assert.Equal(twoCm, page.HeaderDistance, 3);
        Assert.Equal(twoCm, page.FooterDistance, 3);

        RichTextParagraph header = Assert.Single(document.RunningContent.Header(PageSelection.Default));
        Assert.Equal("addresses", header.Text);
        Assert.Equal(TextAlignment.Center, header.Style.Alignment);
        RichTextParagraph footer = Assert.Single(document.RunningContent.Footer(PageSelection.Default));
        Assert.Equal("Page 1", footer.Text);
        Assert.Equal(PageFieldKind.PageNumber, footer.StyleAt(5).PageField?.Kind);

        Assert.True(document.StyleDefaults.PrintsSheets);
        Assert.Equal("Liberation Sans", document.StyleDefaults.FontFamily);
        Assert.Equal(10f, document.StyleDefaults.FontSizePoints);
    }

    [Fact]
    public void A_Cell_Takes_Its_Parent_Styles_And_Calcs_Standard_Alignment()
    {
        SpreadsheetWorksheet sheet = Assert.Single(Read().Workbook.Sheets);

        // ce1 states nothing; its parent Heading names the Calibri face.
        Assert.Equal("Calibri", sheet[0, 0]?.Font.FontFamily);
        Assert.Equal(16f, sheet[0, 0]?.Font.FontSize);
        Assert.Equal(TextAlignment.Left, sheet[0, 0]?.Alignment);
        Assert.Equal(CellVerticalAlignment.Bottom, sheet[0, 0]?.VerticalAlignment);

        // A number with no alignment of its own stands to the right; one whose
        // style fixes the alignment keeps it.
        Assert.Equal(TextAlignment.Right, sheet[0, 1]?.Alignment);
        Assert.Equal(TextAlignment.Center, sheet[0, 2]?.Alignment);
        Assert.Equal(CellVerticalAlignment.Top, sheet[0, 2]?.VerticalAlignment);
    }

    [Fact]
    public void A_Top_Aligned_Cell_Keeps_Its_Alignment_Through_A_Round_Trip()
    {
        RichTextDocument document = Read().Document;
        var codec = new OdsDocumentCodec();
        using var written = new MemoryStream();
        codec.Write(document, written);
        written.Position = 0;

        SpreadsheetWorksheet sheet = Assert.Single(((OdsReadResult)codec.Read(written)).Workbook.Sheets);
        Assert.Equal(CellVerticalAlignment.Bottom, sheet[0, 0]?.VerticalAlignment);
        Assert.Equal(CellVerticalAlignment.Top, sheet[0, 2]?.VerticalAlignment);
    }
}
