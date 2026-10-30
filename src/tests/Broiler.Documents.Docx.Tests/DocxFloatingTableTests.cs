namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// A floating table, <c>w:tblpPr</c> (ECMA-376 §17.4.57): where it stands is
/// read into <see cref="DocumentTable.Position"/> and written back.
/// </summary>
/// <remarks>
/// LibreOffice's <c>floattable-center-shift-down.docx</c> centres a table on the
/// margins 3,012 twips down the page; nothing read the position, so the table
/// was laid out in the flow and the inline tables after it moved a page down.
/// </remarks>
public sealed class DocxFloatingTableTests
{
    private const string Grid = "<w:tblGrid><w:gridCol w:w=\"2000\"/></w:tblGrid>";

    private static string Table(string properties, string text = "cell") =>
        "<w:tbl><w:tblPr>" + properties + "</w:tblPr>" + Grid +
        "<w:tr><w:tc><w:tcPr/>" + DocxTestPackage.Paragraph(text) + "</w:tc></w:tr></w:tbl>";

    private static RichTextDocument Read(string bodyXml) => DocxTestPackage.ReadBody(bodyXml).Document;

    private const string LeftBorder = "<w:tblBorders><w:left w:val=\"single\" w:sz=\"8\" w:space=\"0\" w:color=\"000000\"/></w:tblBorders>";

    /// <summary>Reads a body whose settings state <paramref name="mode"/>, or none.</summary>
    private static RichTextDocument ReadInMode(string? mode, string bodyXml)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (mode is not null)
        {
            parts["word/settings.xml"] =
                "<w:settings " + DocxTestPackage.BodyNamespaceDeclarations + "><w:compat>" +
                "<w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"" + mode + "\"/>" +
                "</w:compat></w:settings>";
        }

        using var stream = new MemoryStream(DocxTestPackage.FromBody(bodyXml, parts), writable: false);
        return new DocxDocumentCodec().Read(stream).Document;
    }

    private static RichTextDocument RoundTrip(RichTextDocument document)
    {
        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(document), writable: false);
        return new DocxDocumentCodec().Read(stream).Document;
    }

    [Fact]
    public void Reads_The_Position_Of_A_Floating_Table()
    {
        RichTextDocument document = Read(Table(
            "<w:tblpPr w:leftFromText=\"180\" w:rightFromText=\"200\" w:topFromText=\"20\" w:bottomFromText=\"40\" " +
            "w:vertAnchor=\"page\" w:horzAnchor=\"margin\" w:tblpXSpec=\"center\" w:tblpY=\"3012\"/>"));

        TablePosition position = Assert.IsType<TablePosition>(Assert.Single(document.Tables).Position);
        Assert.Equal(TableAnchor.Page, position.VerticalAnchor);
        Assert.Equal(TableAnchor.Margin, position.HorizontalAnchor);
        Assert.Equal(TableAlignment.Center, position.HorizontalAlignment);
        Assert.Equal(TableAlignment.None, position.VerticalAlignment);
        Assert.Equal(150.6, position.Y, 3);
        Assert.Equal(9, position.LeftFromText, 3);
        Assert.Equal(10, position.RightFromText, 3);
        Assert.Equal(1, position.TopFromText, 3);
        Assert.Equal(2, position.BottomFromText, 3);
        Assert.True(position.AllowOverlap);
    }

    [Fact]
    public void An_Anchor_Left_Out_Is_Read_As_LibreOffice_Reads_It()
    {
        // writerfilter's TablePositionHandler: the margin down, the text across.
        TablePosition position = Assert.IsType<TablePosition>(
            Assert.Single(Read(Table("<w:tblpPr w:tblpX=\"400\" w:tblpY=\"600\"/>")).Tables).Position);

        Assert.Equal(TableAnchor.Text, position.HorizontalAnchor);
        Assert.Equal(TableAnchor.Margin, position.VerticalAnchor);
        // A package stating no compatibility mode stands the table its left
        // cell margin, 5.4 points by default, left of the offset.
        Assert.Equal(20 - 5.4, position.X, 3);
        Assert.Equal(30, position.Y, 3);
    }

    [Theory]
    [InlineData("14", 20 - 5.4 - 0.5)]
    [InlineData("15", 20 - 0.5)]
    [InlineData(null, 20 - 5.4 - 0.5)]
    public void A_Table_Placed_By_Offset_Stands_Where_Word_Draws_It(string? mode, double x)
    {
        // LibreOffice 24.2's DomainMapperTableHandler: below Word 2013's mode
        // the left cell margin, and in every mode half the first cell's left
        // border. fdo68607.docx, mode 14, so lines its text up with the margin.
        TablePosition position = Assert.IsType<TablePosition>(Assert.Single(ReadInMode(mode, Table(
            "<w:tblpPr w:horzAnchor=\"margin\" w:tblpX=\"400\" w:tblpY=\"600\"/>" + LeftBorder)).Tables).Position);

        Assert.Equal(x, position.X, 3);
    }

    [Fact]
    public void A_Table_Aligned_Across_Is_Not_Moved()
    {
        TablePosition position = Assert.IsType<TablePosition>(Assert.Single(ReadInMode("14", Table(
            "<w:tblpPr w:horzAnchor=\"margin\" w:tblpXSpec=\"center\" w:tblpY=\"600\"/>" + LeftBorder)).Tables).Position);

        Assert.Equal(TableAlignment.Center, position.HorizontalAlignment);
        Assert.Equal(0, position.X, 3);
    }

    [Theory]
    [InlineData("14", 0, -5.4)]
    [InlineData("14", 360, 18 - 5.4)]
    [InlineData("15", 360, 18)]
    [InlineData(null, 360, 18)]
    public void An_Inline_Table_Of_Word_2010_Lines_Its_Text_Up_With_Its_Indent(string? mode, int indent, double edge)
    {
        // Word 2010 and earlier put the first column's text at w:tblInd, so
        // the edge stands the cell margin left of it; Word 2013 and a document
        // stating no mode put the edge there.
        DocumentTable table = Assert.Single(ReadInMode(mode, Table(
            "<w:tblInd w:w=\"" + indent + "\" w:type=\"dxa\"/>" + LeftBorder)).Tables);

        Assert.Equal(edge, table.LeftIndent, 3);
    }

    [Fact]
    public void A_Nested_Table_Keeps_Its_Indent()
    {
        string inner = Table("<w:tblInd w:w=\"360\" w:type=\"dxa\"/>", "inner");
        string outer = "<w:tbl><w:tblPr/>" + Grid + "<w:tr><w:tc><w:tcPr/>" + inner +
            DocxTestPackage.Paragraph("after") + "</w:tc></w:tr></w:tbl>";

        DocumentTable nested = Assert.Single(Assert.Single(ReadInMode("14", outer).Tables).Rows[0].Cells[0].Tables);
        Assert.Equal(18, nested.LeftIndent, 3);
    }

    [Theory]
    [InlineData("14")]
    [InlineData(null)]
    public void A_Floating_Table_Read_From_Word_Comes_Back_Where_It_Was(string? mode)
    {
        // The writer states no compatibility mode, so it writes the offset a
        // reader takes the margin and border back off.
        RichTextDocument source = ReadInMode(mode, Table(
            "<w:tblpPr w:horzAnchor=\"margin\" w:tblpX=\"400\" w:tblpY=\"600\"/>" + LeftBorder));
        TablePosition read = Assert.IsType<TablePosition>(Assert.Single(source.Tables).Position);

        TablePosition actual = Assert.IsType<TablePosition>(Assert.Single(RoundTrip(source).Tables).Position);
        Assert.Equal(read.X, actual.X, 3);
    }

    [Fact]
    public void A_Table_With_No_Position_Is_In_The_Flow()
    {
        Assert.Null(Assert.Single(Read(Table(string.Empty)).Tables).Position);
    }

    [Fact]
    public void Reads_A_Table_That_May_Not_Overlap()
    {
        TablePosition position = Assert.IsType<TablePosition>(Assert.Single(Read(Table(
            "<w:tblpPr w:vertAnchor=\"page\" w:tblpY=\"600\"/><w:tblOverlap w:val=\"never\"/>")).Tables).Position);

        Assert.False(position.AllowOverlap);
    }

    [Fact]
    public void Reads_ZOrder_And_BehindText_Of_A_Floating_Table()
    {
        TablePosition position = Assert.IsType<TablePosition>(Assert.Single(Read(Table(
            "<w:tblpPr w:vertAnchor=\"page\" w:tblpY=\"600\" w:zOrder=\"7\" w:behindText=\"1\"/>")).Tables).Position);

        Assert.Equal(7, position.ZOrder);
        Assert.True(position.BehindText);
    }

    [Fact]
    public void Roundtrips_ZOrder_And_BehindText()
    {
        var position = new TablePosition(TableAnchor.Margin, 20, TableAnchor.Page, 50, ZOrder: 5, BehindText: true);
        var source = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("cell")])
            .WithTables([new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)])], [100.0], position: position)]);

        TablePosition actual = Assert.IsType<TablePosition>(Assert.Single(RoundTrip(source).Tables).Position);
        Assert.Equal(5, actual.ZOrder);
        Assert.True(actual.BehindText);
    }

    [Fact]
    public void A_Nested_Floating_Table_Stays_In_Its_Cell_And_Is_Reported()
    {
        string inner = Table("<w:tblpPr w:vertAnchor=\"page\" w:tblpY=\"600\"/>", "inner");
        string outer = "<w:tbl><w:tblPr/>" + Grid + "<w:tr><w:tc><w:tcPr/>" + inner +
            DocxTestPackage.Paragraph("after") + "</w:tc></w:tr></w:tbl>";

        DocumentReadResult result = DocxTestPackage.ReadBody(outer);

        DocumentTable table = Assert.Single(result.Document.Tables);
        Assert.Null(table.Position);
        Assert.Null(Assert.Single(table.Rows[0].Cells[0].Tables).Position);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "docx.table.floating");
    }

    [Theory]
    [InlineData(TableAlignment.None, TableAlignment.None)]
    [InlineData(TableAlignment.Center, TableAlignment.End)]
    [InlineData(TableAlignment.Outside, TableAlignment.Start)]
    public void Writes_The_Position_Back(TableAlignment across, TableAlignment down)
    {
        var position = new TablePosition(
            TableAnchor.Margin, 36, TableAnchor.Page, 150.6, across, down, 9, 10, 1, 2, AllowOverlap: false);
        RichTextDocument source = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("cell")])
            .WithTables([new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)])], [100.0], position: position)]);

        TablePosition actual = Assert.IsType<TablePosition>(Assert.Single(RoundTrip(source).Tables).Position);

        Assert.Equal(position.HorizontalAnchor, actual.HorizontalAnchor);
        Assert.Equal(position.VerticalAnchor, actual.VerticalAnchor);
        Assert.Equal(across, actual.HorizontalAlignment);
        Assert.Equal(down, actual.VerticalAlignment);
        if (across == TableAlignment.None)
            Assert.Equal(36, actual.X, 3);
        if (down == TableAlignment.None)
            Assert.Equal(150.6, actual.Y, 3);
        Assert.Equal(9, actual.LeftFromText, 3);
        Assert.Equal(10, actual.RightFromText, 3);
        Assert.Equal(1, actual.TopFromText, 3);
        Assert.Equal(2, actual.BottomFromText, 3);
        Assert.False(actual.AllowOverlap);
    }

    [Fact]
    public void The_Position_Comes_Before_The_Width_In_The_Table_Properties()
    {
        // CT_TblPr is a sequence: tblStyle, tblpPr, tblOverlap, ..., tblW.
        var position = new TablePosition(TableAnchor.Margin, 0, TableAnchor.Page, 100, AllowOverlap: false);
        RichTextDocument source = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("cell")])
            .WithTables([new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)])], [100.0], position: position)]);

        string xml = ReadDocumentPart(DocxDocumentCodec.WriteToArray(source));

        int positionAt = xml.IndexOf("<w:tblpPr", StringComparison.Ordinal);
        int overlapAt = xml.IndexOf("<w:tblOverlap", StringComparison.Ordinal);
        int widthAt = xml.IndexOf("<w:tblW", StringComparison.Ordinal);
        Assert.True(positionAt >= 0 && positionAt < overlapAt && overlapAt < widthAt);
    }

    private static string ReadDocumentPart(byte[] bytes)
    {
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(bytes), System.IO.Compression.ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        return reader.ReadToEnd();
    }
}
