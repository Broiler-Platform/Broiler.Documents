using System.Text;
using Broiler.Documents.Cli.Commands;
using Broiler.Documents.Odt;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// Office conformance issue 240, ODT layout, read and laid out end to end: body
/// lines set on a page's line text grid (tdf167583.fodt, tdf88752.fodt,
/// tdf144450.fodt), the printed grid (tdf106736-grid.odt), and a frame set as
/// a character in a header's table (table-in-frame-in-table-in-header-base.odt).
/// </summary>
public sealed class OdtIssue240OdtLayoutLayoutTests
{
    private const string Namespaces =
        "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
        "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" " +
        "xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" " +
        "xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" " +
        "xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" " +
        "xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" " +
        "xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\"";

    // A 72 pt margin on a 612 x 792 pt page leaves a body 648 pt tall. Rows of
    // 60 + 12 pt fill it with 9 whole rows; a grid of 8 leaves 72 pt, 36 above.
    private const double Pitch = 72;
    private const double GridTop = 72 + 36;

    private static string Document(string body, string automaticStyles = "", string grid = "line", string print = "false",
        string masterContent = "")
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<office:document " + Namespaces + " office:version=\"1.3\" office:mimetype=\"application/vnd.oasis.opendocument.text\">" +
            "<office:automatic-styles>" +
            "<style:page-layout style:name=\"pm\"><style:page-layout-properties fo:page-width=\"612pt\" fo:page-height=\"792pt\" " +
            "fo:margin-left=\"72pt\" fo:margin-right=\"72pt\" fo:margin-top=\"72pt\" fo:margin-bottom=\"72pt\" " +
            "style:layout-grid-mode=\"" + grid + "\" style:layout-grid-lines=\"8\" style:layout-grid-base-height=\"60pt\" " +
            "style:layout-grid-ruby-height=\"12pt\" style:layout-grid-ruby-below=\"false\" style:layout-grid-print=\"" + print + "\" " +
            "style:layout-grid-display=\"" + print + "\" style:layout-grid-color=\"#c0c0c0\"/>" +
            "<style:header-style><style:header-footer-properties fo:min-height=\"0pt\" fo:margin-bottom=\"0pt\"/></style:header-style>" +
            "</style:page-layout>" + automaticStyles +
            "</office:automatic-styles>" +
            "<office:master-styles><style:master-page style:name=\"Standard\" style:page-layout-name=\"pm\">" + masterContent +
            "</style:master-page></office:master-styles>" +
            "<office:body><office:text>" + body + "</office:text></office:body></office:document>";
    }

    private static (RichTextDocument Document, LayoutResult Layout) Lay(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml), writable: false);
        RichTextDocument document = new OdtDocumentCodec().Read(stream).Document;
        using var paginated = new DocumentPaginator().Paginate(document, DocumentPaginator.PreferredSetup(document));
        return (document, paginated.Layout);
    }

    private static string TextOf(LayoutLine line) => string.Concat(line.Pieces.Select(piece => piece.Text));

    private static LayoutLine Line(LayoutResult layout, string text) =>
        layout.Pages.SelectMany(page => page.Lines).First(line => TextOf(line).Contains(text, StringComparison.Ordinal));

    [Fact]
    public void Body_Lines_Are_Set_On_The_Rows_Of_A_Grid_Centred_In_The_Body()
    {
        (_, LayoutResult layout) = Lay(Document("<text:p>one</text:p><text:p>two</text:p>"));

        LayoutLine one = Line(layout, "one");
        LayoutLine two = Line(layout, "two");
        Assert.Equal(GridTop, one.Top, 3);
        Assert.Equal(Pitch, one.Height, 3);
        Assert.Equal(GridTop + Pitch, two.Top, 3);

        // The text sits in the middle of the row below its ruby
        // (SwTextFormatter::CalcRealHeight).
        double natural = one.Pieces[0].Ascent + one.Pieces[0].Descent + one.Pieces[0].LineGap;
        Assert.Equal(one.Pieces[0].Ascent + ((Pitch - natural + 12) / 2), one.Baseline, 3);
    }

    [Fact]
    public void Space_Above_A_Paragraph_Moves_It_To_The_Next_Row()
    {
        (_, LayoutResult layout) = Lay(Document(
            "<text:p>one</text:p><text:p text:style-name=\"P1\">two</text:p>",
            "<style:style style:name=\"P1\" style:family=\"paragraph\"><style:paragraph-properties fo:margin-top=\"10pt\"/></style:style>"));

        Assert.Equal(GridTop + (2 * Pitch), Line(layout, "two").Top, 3);
    }

    [Fact]
    public void A_Line_Taller_Than_A_Row_Takes_Two_And_The_Grid_Ends_The_Page()
    {
        string body = "<text:p text:style-name=\"Big\">big</text:p>" +
            string.Concat(Enumerable.Range(1, 7).Select(i => "<text:p>line" + i + "</text:p>"));
        (_, LayoutResult layout) = Lay(Document(body,
            "<style:style style:name=\"Big\" style:family=\"paragraph\"><style:text-properties fo:font-size=\"80pt\"/></style:style>"));

        Assert.Equal(2 * Pitch, Line(layout, "big").Height, 3);

        // Two rows for the big line and six for the next six: the seventh is
        // on the next page.
        Assert.Equal(2, layout.Pages.Count);
        Assert.Equal(GridTop, Line(layout, "line7").Top, 3);
    }

    [Fact]
    public void A_Paragraph_That_Keeps_Off_The_Grid_Keeps_Its_Own_Height()
    {
        (_, LayoutResult layout) = Lay(Document(
            "<text:p text:style-name=\"Off\">off</text:p><text:p>on</text:p>",
            "<style:style style:name=\"Off\" style:family=\"paragraph\"><style:paragraph-properties style:snap-to-layout-grid=\"false\"/></style:style>"));

        LayoutLine off = Line(layout, "off");
        Assert.True(off.Height < 20);
        Assert.Equal(GridTop + Pitch, Line(layout, "on").Top, 3);
    }

    [Fact]
    public void A_Header_Is_Not_Set_On_The_Grid()
    {
        (_, LayoutResult layout) = Lay(Document("<text:p>body</text:p>",
            masterContent: "<style:header><text:p>head</text:p></style:header>"));

        Assert.True(Line(layout, "head").Height < 20);
    }

    [Fact]
    public void A_Printed_Grid_Is_Drawn_Under_The_Text_And_An_Unprinted_One_Is_Not()
    {
        (_, LayoutResult hidden) = Lay(Document("<text:p>x</text:p>"));
        Assert.Empty(hidden.Pages[0].Cells);

        (_, LayoutResult printed) = Lay(Document("<text:p>x</text:p>", grid: "both", print: "true"));
        IReadOnlyList<LayoutCell> rules = printed.Pages[0].Cells;

        // A rule above each row and between its ruby and its text, one below,
        // two sides, and the 6 inner edges of the 7 cells of 60 pt that fit
        // across the 468 pt body, centred in it.
        Assert.Equal((8 * 2) + 1 + 2 + (8 * 6), rules.Count);
        Assert.Contains(rules, rule => Math.Abs(rule.Bounds.Top - GridTop) < 0.01 && Math.Abs(rule.Bounds.Width - 420) < 0.01);
        Assert.Contains(rules, rule => Math.Abs(rule.Bounds.Top - (GridTop + 12)) < 0.01 && Math.Abs(rule.Bounds.Left - 96) < 0.01);
        Assert.All(rules, rule => Assert.Equal(192, rule.Shading.R));
    }

    private const string HeaderTable =
        "<style:header><table:table table:name=\"T1\"><table:table-column table:number-columns-repeated=\"2\"/>" +
        "<table:table-row><table:table-cell><text:p>Outer table</text:p></table:table-cell><table:table-cell><text:p/></table:table-cell></table:table-row>" +
        "<table:table-row><table:table-cell><text:p><draw:frame draw:name=\"Frame1\" text:anchor-type=\"as-char\" svg:width=\"200pt\">" +
        "<draw:text-box fo:min-height=\"40pt\"><text:p>As char frame.</text:p></draw:text-box></draw:frame></text:p></table:table-cell>" +
        "<table:table-cell><text:p/></table:table-cell></table:table-row></table:table><text:p/></style:header>";

    [Fact]
    public void A_Frame_Set_As_A_Character_In_A_Header_Table_Stands_In_Its_Row()
    {
        (_, LayoutResult layout) = Lay(Document("<text:p/>", grid: "none", masterContent: HeaderTable));

        LayoutPage page = layout.Pages[0];
        LayoutLine outer = Line(layout, "Outer table");
        LayoutShape frame = Assert.Single(page.Shapes);

        // The frame is below the first row, in the left cell, and the header
        // grew to hold it: the body starts below it.
        Assert.True(frame.Bounds.Top >= outer.Top + outer.Height - 0.01);
        Assert.True(frame.Bounds.Left >= 72 - 0.01 && frame.Bounds.Left < 100);
        Assert.True(frame.Bounds.Height >= 40 - 0.01);
    }

    [Fact]
    public void The_Text_Of_A_Document_With_An_Empty_Body_Is_Its_Header_Frame()
    {
        // LibreOffice's text export writes the first frame's text where the
        // body has nothing, a frame in a header included.
        (RichTextDocument document, _) = Lay(Document("<text:p/>", grid: "none", masterContent: HeaderTable));

        Assert.Equal("As char frame.", DocumentCommands.LabelledText(document, joinsTableRows: false));
    }
}
