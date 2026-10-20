namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// The text columns a page layout divides its page into: <c>style:columns</c>
/// on <c>style:page-layout-properties</c>.
/// </summary>
/// <remarks>
/// LibreOffice's <c>many-paragraphs-per-page-in-6-columns.odt</c> is a landscape
/// page of six columns. Read as one wide column, its twelve pages came out as
/// forty.
/// </remarks>
public sealed class OdtColumnTests
{
    private static string Styles(string columnsXml) =>
        "<office:automatic-styles>" +
        "<style:page-layout style:name=\"pm1\">" +
        "<style:page-layout-properties fo:page-width=\"8.5in\" fo:page-height=\"11in\" " +
        "fo:margin-left=\"1in\" fo:margin-right=\"1in\" fo:margin-top=\"1in\" fo:margin-bottom=\"1in\">" +
        columnsXml +
        "</style:page-layout-properties>" +
        "</style:page-layout>" +
        "</office:automatic-styles>" +
        "<office:master-styles>" +
        "<style:master-page style:name=\"Standard\" style:page-layout-name=\"pm1\"/>" +
        "</office:master-styles>";

    private static DocumentReadResult Read(string columnsXml) =>
        OdtTestPackage.ReadStyled(OdtTestPackage.Paragraph("body"), Styles(columnsXml));

    private static PageGeometry PageOf(DocumentReadResult result) =>
        Assert.IsType<PageGeometry>(result.Document.PageGeometry);

    private static bool Reports(DocumentReadResult result, string code) =>
        result.Diagnostics.Any(diagnostic => diagnostic.Code == code);

    private static RichTextDocument RoundTrip(RichTextDocument document)
    {
        using var stream = new MemoryStream(OdtDocumentCodec.WriteToArray(document), writable: false);
        return new OdtDocumentCodec().Read(stream).Document;
    }

    [Fact]
    public void Reads_The_Column_Count_And_Gap()
    {
        PageGeometry page = PageOf(Read("<style:columns fo:column-count=\"3\" fo:column-gap=\"0.25in\"/>"));

        Assert.Equal(3, page.ColumnCount);
        Assert.Equal(18, page.ColumnGap, 3);
        Assert.Equal(144, page.ColumnWidth, 3);
    }

    [Fact]
    public void Reads_The_Gap_From_Listed_Columns_When_The_Attribute_Is_Absent()
    {
        // LibreOffice lists every column, stating each gap as the end indent of
        // one column and the start indent of the next.
        PageGeometry page = PageOf(Read(
            "<style:columns fo:column-count=\"2\">" +
            "<style:column style:rel-width=\"4999*\" fo:start-indent=\"0in\" fo:end-indent=\"0.125in\"/>" +
            "<style:column style:rel-width=\"5001*\" fo:start-indent=\"0.125in\" fo:end-indent=\"0in\"/>" +
            "</style:columns>"));

        Assert.Equal(2, page.ColumnCount);
        Assert.Equal(18, page.ColumnGap, 3);
    }

    [Fact]
    public void Reads_The_Six_Column_Page_LibreOffice_Writes()
    {
        DocumentReadResult result = Read(
            "<style:columns fo:column-count=\"6\" fo:column-gap=\"0.078in\">" +
            "<style:column-sep style:width=\"0.0035in\" style:color=\"#000000\" style:height=\"100%\" style:style=\"solid\"/>" +
            "<style:column style:rel-width=\"10922*\" fo:start-indent=\"0in\" fo:end-indent=\"0.039in\"/>" +
            "<style:column style:rel-width=\"10922*\" fo:start-indent=\"0.039in\" fo:end-indent=\"0.039in\"/>" +
            "<style:column style:rel-width=\"10922*\" fo:start-indent=\"0.039in\" fo:end-indent=\"0.039in\"/>" +
            "<style:column style:rel-width=\"10922*\" fo:start-indent=\"0.039in\" fo:end-indent=\"0.039in\"/>" +
            "<style:column style:rel-width=\"10922*\" fo:start-indent=\"0.039in\" fo:end-indent=\"0.039in\"/>" +
            "<style:column style:rel-width=\"10925*\" fo:start-indent=\"0.039in\" fo:end-indent=\"0in\"/>" +
            "</style:columns>");

        Assert.Equal(6, PageOf(result).ColumnCount);
        Assert.Equal(5.616, PageOf(result).ColumnGap, 3);

        // 10922 against 10925 is how a producer shares a width out in whole
        // units, not a request for unequal columns.
        Assert.False(Reports(result, "odt.columns.unequal"));
    }

    [Fact]
    public void Columns_Of_Unequal_Width_Are_Read_As_Equal_And_Reported()
    {
        DocumentReadResult result = Read(
            "<style:columns fo:column-count=\"2\" fo:column-gap=\"0.25in\">" +
            "<style:column style:rel-width=\"3000*\"/><style:column style:rel-width=\"7000*\"/>" +
            "</style:columns>");

        Assert.Equal(2, PageOf(result).ColumnCount);
        Assert.True(Reports(result, "odt.columns.unequal"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<style:columns fo:column-count=\"1\"/>")]
    [InlineData("<style:columns fo:column-count=\"0\"/>")]
    [InlineData("<style:columns fo:column-count=\"two\"/>")]
    [InlineData("<style:columns/>")]
    public void A_Page_That_Is_Not_Divided_Has_One_Column(string columnsXml)
    {
        Assert.Equal(1, PageOf(Read(columnsXml)).ColumnCount);
    }

    [Fact]
    public void Columns_That_Leave_No_Room_Keep_The_Page_And_Drop_The_Columns()
    {
        DocumentReadResult result = Read("<style:columns fo:column-count=\"2\" fo:column-gap=\"7in\"/>");

        PageGeometry page = PageOf(result);
        Assert.Equal(612, page.Width, 3);
        Assert.Equal(1, page.ColumnCount);
        Assert.True(Reports(result, "odt.columns.geometry"));
    }

    [Fact]
    public void Columns_Survive_A_Round_Trip()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body")
            .WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72, ColumnCount: 3, ColumnGap: 18));

        PageGeometry page = Assert.IsType<PageGeometry>(RoundTrip(source).PageGeometry);

        Assert.Equal(3, page.ColumnCount);
        Assert.Equal(18, page.ColumnGap, 2);
    }

    [Fact]
    public void A_Single_Column_Writes_No_Columns()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body")
            .WithPageGeometry(new PageGeometry(612, 792, 72, 72, 72, 72));

        PageGeometry page = Assert.IsType<PageGeometry>(RoundTrip(source).PageGeometry);

        Assert.Equal(1, page.ColumnCount);
    }
}
