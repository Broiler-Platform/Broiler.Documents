using Broiler.Graphics.Color;

namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Regressions from office conformance issue #232: a table deleted in place
/// (<c>odfexport/data/deleted_table.fodt</c>), the shape colours of an
/// OpenOffice.org document (<c>odfimport/data/fdo75872_ooo33.odt</c>) and a
/// footer page number in its page layout's format
/// (<c>ooxmlexport/data/hyperlink-field.odt</c>).
/// </summary>
public sealed class OdtIssue232RevisionShapeTests
{
    private static RichTextDocument Read(string body, string automaticStyles = "", string? styles = null,
        IReadOnlyDictionary<string, string>? extraParts = null)
    {
        using var stream = new MemoryStream(
            OdtTestPackage.FromBody(body, automaticStyles, styles, extraParts: extraParts), writable: false);
        return new OdtDocumentCodec().Read(stream).Document;
    }

    private static Dictionary<string, string> Meta(string generator) => new(StringComparer.Ordinal)
    {
        ["meta.xml"] = "<office:document-meta " + OdtTestPackage.NamespaceDeclarations +
            " xmlns:meta=\"urn:oasis:names:tc:opendocument:xmlns:meta:1.0\" office:version=\"1.3\"><office:meta>" +
            "<meta:generator>" + generator + "</meta:generator></office:meta></office:document-meta>",
    };

    [Fact]
    public void A_Table_Deleted_In_Place_Is_Marked_Deleted_And_Hidden_With_The_Changes()
    {
        string body =
            "<text:tracked-changes xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><text:changed-region text:id=\"ct1\"><text:deletion>" +
            "<office:change-info><dc:creator>John Doe</dc:creator><dc:date>2001-01-01T01:00:00</dc:date></office:change-info>" +
            "</text:deletion></text:changed-region></text:tracked-changes>" +
            "<text:p><text:change-start text:change-id=\"ct1\"/></text:p>" +
            "<table:table table:name=\"T\"><table:table-column/><table:table-row><table:table-cell>" +
            "<text:p>Deleted table</text:p></table:table-cell></table:table-row></table:table>" +
            "<text:p><text:change-end text:change-id=\"ct1\"/></text:p>";

        RichTextDocument document = Read(body);

        DocumentTable table = Assert.Single(document.Tables);
        Assert.True(document.Paragraphs[table.ParagraphIndex].StyleAt(0).IsDeleted);
        Assert.Empty(RevisionDisplay.WithoutDeletions(document).Tables);
    }

    private const string Rect =
        "<text:p><draw:rect text:anchor-type=\"paragraph\" draw:style-name=\"gr1\" svg:width=\"3cm\" svg:height=\"2cm\" svg:x=\"1cm\" svg:y=\"1cm\"/></text:p>";

    private const string RectStyle =
        "<style:style style:name=\"gr1\" style:family=\"graphic\"><style:graphic-properties style:run-through=\"foreground\" style:wrap=\"run-through\"/></style:style>";

    [Theory]
    [InlineData("OpenOffice.org/3.3$Unix OpenOffice.org_project/330m20$Build-9567", 0x99CCFF, 0x000000)]
    [InlineData("OpenOffice/4.0.0$Unix OpenOffice.org_project/400m3$Build-9702", 0xCFE7F5, 0x808080)]
    [InlineData("LibreOffice/4.0.2.2$Linux_X86_64 LibreOffice_project/4c82dcdd6efcd48b1d8bba66bfe1989deee49c3", 0x99CCFF, 0x000000)]
    public void A_Shape_From_An_Older_Producer_Takes_Its_Default_Colours(string generator, int fill, int line)
    {
        DocumentShape shape = Assert.Single(Read(Rect, RectStyle, extraParts: Meta(generator)).Shapes);

        Assert.Equal(BColor.FromArgb((byte)(fill >> 16), (byte)(fill >> 8), (byte)fill), shape.Fill?.Start);
        Assert.Equal(BColor.FromArgb((byte)(line >> 16), (byte)(line >> 8), (byte)line), shape.Outline);
    }

    [Fact]
    public void A_Shape_From_A_Current_Producer_Keeps_Todays_Reading()
    {
        // Unchanged: a shape stating no paint is not drawn at all.
        Assert.Empty(Read(Rect, RectStyle,
            extraParts: Meta("LibreOffice/24.2.7.2$Linux_X86_64 LibreOffice_project/ee3885777aa7032db5a9b65deec9457448a91162")).Shapes);
    }

    [Fact]
    public void A_Footer_Page_Number_Takes_Its_Page_Layouts_Format()
    {
        string styles =
            "<office:automatic-styles>" +
            "<style:page-layout style:name=\"a\"><style:page-layout-properties fo:page-width=\"200pt\" fo:page-height=\"300pt\" style:num-format=\"i\"/></style:page-layout>" +
            "</office:automatic-styles><office:master-styles>" +
            "<style:master-page style:name=\"Standard\" style:page-layout-name=\"a\"><style:footer><text:p>" +
            "<text:page-number text:select-page=\"current\">0</text:page-number></text:p></style:footer></style:master-page>" +
            "</office:master-styles>";

        RichTextDocument document = OdtTestPackage.ReadStyled("<text:p>body</text:p>", styles).Document;

        RichTextParagraph footer = Assert.Single(document.RunningContent.Footer(PageSelection.Default));
        Assert.Equal(new PageField(PageFieldKind.PageNumber, ListNumberFormat.LowerRoman),
            Assert.Single(footer.Runs, run => run.Style.PageField is not null).Style.PageField);
    }
}
