namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Issue #249, DOCX pixel cluster: the readings behind pages whose text sat in
/// the wrong place - automatic spacing under a numbered cell paragraph
/// (tdf132802.docx), the line grid of a <c>w:docGrid</c> (fdo80902.docx), the
/// room collapsing table borders take, and VML shapes aligned in the top or
/// bottom margin (tdf137642_Vertical_Alignment_toppage.docx).
/// </summary>
public sealed class DocxIssue249DocxPixelTests
{
    private static DocumentReadResult Read(string body, Dictionary<string, string>? parts = null)
    {
        using var stream = new MemoryStream(DocxTestPackage.FromBody(body, parts ?? []), writable: false);
        return new DocxDocumentCodec().Read(stream);
    }

    private const string AutoSpacing = "<w:spacing w:after=\"100\" w:afterAutospacing=\"1\"/>";

    private static Dictionary<string, string> Numbering() => new(StringComparer.Ordinal)
    {
        ["word/numbering.xml"] = "<w:numbering " + DocxTestPackage.BodyNamespaceDeclarations + ">" +
            "<w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/>" +
            "<w:lvlText w:val=\"%1.\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>" +
            "</w:numbering>",
    };

    private static string CellParagraph(string text, bool numbered) =>
        "<w:p><w:pPr>" + (numbered ? "<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr>" : string.Empty) +
        AutoSpacing + "</w:pPr><w:r><w:t>" + text + "</w:t></w:r></w:p>";

    private static string OneCellTable(string content) =>
        "<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"dxa\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"5000\"/></w:tblGrid>" +
        "<w:tr><w:tc>" + content + "</w:tc></w:tr></w:tbl>";

    [Fact]
    public void The_Last_Numbered_Paragraph_Of_A_Cell_Keeps_Its_Automatic_Space_Below()
    {
        // writerfilter's ClearPreviousParagraph takes the automatic space off
        // a cell's last paragraph "except in paragraphs with numbering"
        // (tdf#132802): tdf132802.docx's numbered cells keep 280 twips under them.
        DocumentReadResult result = Read(
            OneCellTable(CellParagraph("first", numbered: true) + CellParagraph("last", numbered: true)) +
            OneCellTable(CellParagraph("plain", numbered: false)) +
            DocxTestPackage.Paragraph("after"),
            Numbering());

        ParagraphStyle Style(string text) => result.Document.Paragraphs.Single(p => p.Text.EndsWith(text, StringComparison.Ordinal)).Style;
        Assert.Equal(0, Style("first").SpacingAfter);
        Assert.Equal(14, Style("last").SpacingAfter);
        Assert.Equal(0, Style("plain").SpacingAfter);
    }

    private static string Section(string grid) =>
        "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +
        "<w:pgMar w:top=\"1440\" w:right=\"1800\" w:bottom=\"1440\" w:left=\"1800\" w:header=\"851\" w:footer=\"992\" w:gutter=\"0\"/>" +
        grid + "</w:sectPr>";

    [Theory]
    [InlineData("lines")]
    [InlineData("linesAndChars")]
    [InlineData("snapToChars")]
    public void A_Line_Grid_Sets_Rows_Of_The_Line_Pitch(string type)
    {
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("text") + Section("<w:docGrid w:type=\"" + type + "\" w:linePitch=\"360\"/>"));

        TextGrid grid = Assert.IsType<TextGrid>(result.Document.PageGeometry!.TextGrid);
        Assert.Equal(18, grid.BaseHeight, 3);
        Assert.Equal(0, grid.RubyHeight, 3);
        // (841.9 - 72 - 72) / 18 = 38.8 rows.
        Assert.Equal(38, grid.Lines);
    }

    [Fact]
    public void The_Default_Grid_Type_Sets_No_Grid()
    {
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("text") + Section("<w:docGrid w:type=\"default\" w:linePitch=\"360\"/>"));

        Assert.Null(result.Document.PageGeometry!.TextGrid);
        Assert.Null(Read(DocxTestPackage.Paragraph("text") + Section("<w:docGrid w:linePitch=\"360\"/>"))
            .Document.PageGeometry!.TextGrid);
    }

    [Fact]
    public void A_Paragraph_That_Does_Not_Snap_To_The_Grid_Ignores_It()
    {
        DocumentReadResult result = Read(
            "<w:p><w:pPr><w:snapToGrid w:val=\"0\"/></w:pPr><w:r><w:t>off</w:t></w:r></w:p>" +
            DocxTestPackage.Paragraph("on"));

        Assert.True(result.Document.Paragraphs[0].Style.IgnoresTextGrid);
        Assert.False(result.Document.Paragraphs[1].Style.IgnoresTextGrid);
    }

    [Fact]
    public void Tables_Lay_Out_With_Collapsing_Borders()
    {
        Assert.True(Read(DocxTestPackage.Paragraph("a")).Document.StyleDefaults.CollapsingTableBorders);
    }

    private static string MarginShape(string vertical, string frame) =>
        "<w:p><w:r><w:pict xmlns:v=\"urn:schemas-microsoft-com:vml\"><v:rect fillcolor=\"#FF0000\" stroked=\"f\" " +
        "style=\"position:absolute;margin-left:19pt;margin-top:0;width:26.5pt;height:22pt;" +
        "mso-position-vertical:" + vertical + ";mso-position-vertical-relative:" + frame + "\"/></w:pict></w:r>" +
        "<w:r><w:t>text</w:t></w:r></w:p>";

    [Theory]
    [InlineData("top", "top-margin-area", 0)]
    [InlineData("center", "top-margin-area", 61)]
    [InlineData("bottom", "top-margin-area", 122)]
    [InlineData("top", "bottom-margin-area", 841.9 - 216)]
    [InlineData("bottom", "bottom-margin-area", 841.9 - 22)]
    public void A_Vml_Shape_Is_Aligned_In_The_Margin_Band_It_Names(string vertical, string frame, double top)
    {
        // tdf137642_Vertical_Alignment_toppage.docx: three triangles aligned
        // top, centre and bottom in a 144 pt top margin.
        DocumentReadResult result = Read(MarginShape(vertical, frame) +
            "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +
            "<w:pgMar w:top=\"2880\" w:right=\"1440\" w:bottom=\"4320\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>");

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.Equal(top, shape.OffsetY, 0);
    }

    private static Dictionary<string, string> SplitSettings() => new(StringComparer.Ordinal)
    {
        ["word/settings.xml"] = "<w:settings " + DocxTestPackage.BodyNamespaceDeclarations +
            "><w:compat><w:splitPgBreakAndParaMark/></w:compat></w:settings>",
    };

    private const string PageBreak = "<w:r><w:br w:type=\"page\"/></w:r>";

    [Fact]
    public void With_SplitPgBreakAndParaMark_A_Paragraph_Ending_In_A_Page_Break_Leaves_Its_Mark_On_The_New_Page()
    {
        // tdf123636_newlinePageBreak3.docx: "Last line on page 1" ends in a
        // page break; writerfilter splits its mark off as an empty paragraph
        // that opens page 2, above the paragraph after it.
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("first") +
            "<w:p><w:r><w:t>last on page 1</w:t></w:r>" + PageBreak + "</w:p>" +
            DocxTestPackage.Paragraph("second line on page 2"),
            SplitSettings());

        IReadOnlyList<RichTextParagraph> paragraphs = result.Document.Paragraphs;
        Assert.Equal(["first", "last on page 1", "", "second line on page 2"], paragraphs.Select(p => p.Text));
        Assert.False(paragraphs[1].Style.PageBreakBefore);
        Assert.True(paragraphs[2].Style.PageBreakBefore);
        Assert.False(paragraphs[3].Style.PageBreakBefore);
    }

    [Fact]
    public void With_SplitPgBreakAndParaMark_A_Lone_Page_Break_Moves_Its_Paragraph_Unless_It_Opens_The_Section()
    {
        // tdf123636_newlinePageBreak4.docx: an empty paragraph that holds only
        // the break is not split and goes to the new page itself; the
        // section's first paragraph is split (tdf123636_newlinePageBreak.docx).
        DocumentReadResult result = Read(
            "<w:p>" + PageBreak + "</w:p>" +
            DocxTestPackage.Paragraph("text") +
            "<w:p>" + PageBreak + "</w:p>" +
            DocxTestPackage.Paragraph("after"),
            SplitSettings());

        IReadOnlyList<RichTextParagraph> paragraphs = result.Document.Paragraphs;
        Assert.Equal(["", "", "text", "", "after"], paragraphs.Select(p => p.Text));
        Assert.Equal([false, true, false, true, false], paragraphs.Select(p => p.Style.PageBreakBefore));
    }

    [Fact]
    public void With_SplitPgBreakAndParaMark_A_Section_Mark_Holding_Only_A_Break_Passes_It_On()
    {
        // tdf97648_relativeWidth.docx: the paragraph closing a continuous
        // section holds nothing but a page break. writerfilter makes no
        // paragraph of such a mark, so nothing moves to the new page above
        // "next".
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("text") +
            "<w:p><w:pPr><w:sectPr><w:type w:val=\"continuous\"/></w:sectPr></w:pPr>" + PageBreak + "</w:p>" +
            DocxTestPackage.Paragraph("next") +
            "<w:sectPr><w:type w:val=\"continuous\"/></w:sectPr>",
            SplitSettings());

        RichTextParagraph next = result.Document.Paragraphs.Single(p => p.Text == "next");
        Assert.True(next.Style.PageBreakBefore);
        Assert.DoesNotContain(result.Document.Paragraphs, p => p.Text.Length == 0 && p.Style.PageBreakBefore);
    }

    [Fact]
    public void Without_SplitPgBreakAndParaMark_A_Trailing_Page_Break_Passes_To_The_Next_Paragraph()
    {
        DocumentReadResult result = Read(
            "<w:p><w:r><w:t>text</w:t></w:r>" + PageBreak + "</w:p>" + DocxTestPackage.Paragraph("after"));

        Assert.Equal(["text", "after"], result.Document.Paragraphs.Select(p => p.Text));
        Assert.True(result.Document.Paragraphs[1].Style.PageBreakBefore);
    }

    private static string GutterSection(string extra) =>
        "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/>" +
        "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"720\"/>" +
        extra + "</w:sectPr>";

    [Fact]
    public void The_Gutter_Widens_The_Left_Margin()
    {
        PageGeometry page = Read(DocxTestPackage.Paragraph("a") + GutterSection(string.Empty)).Document.PageGeometry!;

        Assert.Equal((108, 72, 72), (page.MarginLeft, page.MarginRight, page.MarginTop));
    }

    [Fact]
    public void A_Right_To_Left_Gutter_Widens_The_Right_Margin()
    {
        PageGeometry page = Read(DocxTestPackage.Paragraph("a") + GutterSection("<w:rtlGutter/>")).Document.PageGeometry!;

        Assert.Equal((72, 108, 72), (page.MarginLeft, page.MarginRight, page.MarginTop));
    }

    [Fact]
    public void A_Gutter_At_The_Top_Widens_The_Top_Margin()
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/settings.xml"] = "<w:settings " + DocxTestPackage.BodyNamespaceDeclarations + "><w:gutterAtTop/></w:settings>",
        };
        PageGeometry page = Read(DocxTestPackage.Paragraph("a") + GutterSection(string.Empty), settings).Document.PageGeometry!;

        Assert.Equal((72, 72, 108), (page.MarginLeft, page.MarginRight, page.MarginTop));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void No_Column_Balance_Leaves_Every_Sections_Columns_Unbalanced(bool noColumnBalance)
    {
        // unbalanced-columns-compat.docx: two lines in two columns, then a
        // continuous section; LibreOffice leaves both in the left column.
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/settings.xml"] = "<w:settings " + DocxTestPackage.BodyNamespaceDeclarations + "><w:compat>" +
                (noColumnBalance ? "<w:noColumnBalance/>" : string.Empty) + "</w:compat></w:settings>",
        };
        DocumentReadResult result = Read(
            DocxTestPackage.Paragraph("left") +
            "<w:p><w:pPr><w:sectPr><w:type w:val=\"continuous\"/><w:cols w:num=\"2\"/></w:sectPr></w:pPr><w:r><w:t>second</w:t></w:r></w:p>" +
            DocxTestPackage.Paragraph("after") +
            "<w:sectPr><w:type w:val=\"continuous\"/></w:sectPr>",
            settings);

        Assert.Equal(2, result.Document.Sections.Count);
        Assert.All(result.Document.Sections, section => Assert.Equal(noColumnBalance, section.DontBalanceColumns));
    }

    [Theory]
    [InlineData("center", 12, (1.15 * 12 - 72) / 2)]
    [InlineData("center", 20, (1.15 * 20 - 72) / 2)]
    [InlineData("top", 12, 0)]
    public void A_Picture_Centred_On_Its_Line_Stands_Half_Above_It(string align, int points, double top)
    {
        // tdf162551_notLayoutInCell_charLeft_fromTop.docx: positionV
        // relativeFrom="line" with wp:align center; LibreOffice centres the
        // picture on the line it is anchored in.
        string run = DocxTestPackage.AnchoredDrawingRun("rId7", 914400, 914400, 0, 0)
            .Replace("<wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>0</wp:posOffset></wp:positionV>",
                "<wp:positionV relativeFrom=\"line\"><wp:align>" + align + "</wp:align></wp:positionV>", StringComparison.Ordinal)
            .Replace("<w:r>", "<w:r><w:rPr><w:sz w:val=\"" + (points * 2) + "\"/></w:rPr>", StringComparison.Ordinal);
        Assert.Contains("relativeFrom=\"line\"", run, StringComparison.Ordinal);

        DocumentReadResult result = DocxTestPackage.ReadWithMedia(
            "<w:p>" + run + "<w:r><w:t>anchor point</w:t></w:r></w:p>",
            DocxTestPackage.ImageRelationship("rId7", "media/image1.png"),
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["word/media/image1.png"] = DocxTestPackage.OnePixelPng });

        DocumentShape shape = Assert.Single(result.Document.Shapes);
        Assert.False(shape.OffsetFromPage);
        Assert.Equal(top, shape.OffsetY, 2);
    }

    private static string WidthCell(int twips, string text) =>
        "<w:tc><w:tcPr><w:tcW w:w=\"" + twips + "\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>" + text + "</w:t></w:r></w:p></w:tc>";

    [Fact]
    public void A_Grid_Narrower_Than_Every_Row_Gives_Way_To_The_Cell_Widths()
    {
        // fdo76586.docx: one 9444-twip grid column under rows of a 1602 and a
        // 7843-twip cell. writerfilter places such a row's columns by its
        // cells' widths (DomainMapperTableManager::shouldInsertRow).
        DocumentReadResult result = Read(
            "<w:tbl><w:tblPr><w:tblW w:w=\"9445\" w:type=\"dxa\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"9444\"/></w:tblGrid>" +
            "<w:tr>" + WidthCell(1602, "time") + WidthCell(7843, "event") + "</w:tr>" +
            "<w:tr>" + WidthCell(1602, "time") + WidthCell(7843, "event") + "</w:tr></w:tbl>" +
            DocxTestPackage.Paragraph("after"));

        DocumentTable table = Assert.Single(result.Document.Tables);
        Assert.Equal([1602 / 20d, 7843 / 20d], table.ColumnWidths);
    }

    [Fact]
    public void A_Grid_Some_Row_Fits_Is_Kept()
    {
        DocumentReadResult result = Read(
            "<w:tbl><w:tblPr><w:tblW w:w=\"9445\" w:type=\"dxa\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"9444\"/></w:tblGrid>" +
            "<w:tr>" + WidthCell(9444, "whole") + "</w:tr>" +
            "<w:tr>" + WidthCell(1602, "time") + WidthCell(7843, "event") + "</w:tr></w:tbl>" +
            DocxTestPackage.Paragraph("after"));

        Assert.Equal(9444 / 20d, Assert.Single(result.Document.Tables).ColumnWidths[0]);
    }

    [Fact]
    public void A_Grid_Under_Spanning_Cells_Is_Kept()
    {
        // tdf59274.docx: its spanning cells' widths leave the columns between
        // their edges to guess, so its stated grid stands.
        DocumentReadResult result = Read(
            "<w:tbl><w:tblPr><w:tblW w:w=\"9445\" w:type=\"dxa\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"9444\"/></w:tblGrid>" +
            "<w:tr><w:tc><w:tcPr><w:tcW w:w=\"4000\" w:type=\"dxa\"/><w:gridSpan w:val=\"2\"/></w:tcPr><w:p/></w:tc>" +
            WidthCell(5444, "rest") + "</w:tr></w:tbl>" +
            DocxTestPackage.Paragraph("after"));

        Assert.Equal(9444 / 20d, Assert.Single(result.Document.Tables).ColumnWidths[0]);
    }
}
