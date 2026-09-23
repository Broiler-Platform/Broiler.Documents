namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers reading a ruled table back out of the rules that drew it.
/// </summary>
/// <remarks>
/// A PDF says nowhere that a table is a table: it draws lines, and it draws text
/// at coordinates. Everything asserted here is therefore a reconstruction, and
/// the fixtures are built so that a pass which merely counted rules, or merely
/// read text top to bottom, would fail them.
/// </remarks>
public sealed class PdfTableReconstructionTests
{
    [Fact]
    public void A_Fully_Ruled_Grid_Becomes_A_Table()
    {
        DocumentTable table = Assert.Single(Read(Ruled()).Document.Tables);

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(2, table.Rows[0].Cells.Count);
    }

    [Fact]
    public void Each_Cell_Holds_Its_Own_Text()
    {
        // The load-bearing one. A1 and B1 share a baseline, so the line builder
        // joins them into "A1 B1" unless the split happens on fragments first -
        // which would leave the second cell of every row empty and its text in
        // the first.
        PdfReadResult result = Read(Ruled());
        DocumentTable table = Assert.Single(result.Document.Tables);

        Assert.Equal("A1", CellText(result, table, 0, 0));
        Assert.Equal("B1", CellText(result, table, 0, 1));
        Assert.Equal("A2", CellText(result, table, 1, 0));
        Assert.Equal("B2", CellText(result, table, 1, 1));
    }

    [Fact]
    public void The_Cells_Are_Read_Row_Major()
    {
        // Drawn in column order - both left cells, then both right ones - so the
        // order they come back in is the grid's, not the content stream's.
        string text = Text(Read(Ruled(columnOrder: true)));

        Assert.Equal("A1\nB1\nA2\nB2", text);
    }

    [Fact]
    public void The_Cells_Carry_The_Borders_That_Were_Painted()
    {
        DocumentTable table = Assert.Single(Read(Ruled()).Document.Tables);
        CellBorders borders = table.Rows[0].Cells[0].Borders;

        Assert.NotEqual(TableBorder.None, borders.Left);
        Assert.NotEqual(TableBorder.None, borders.Top);
        Assert.NotEqual(TableBorder.None, borders.Right);
        Assert.NotEqual(TableBorder.None, borders.Bottom);
    }

    [Fact]
    public void A_Cell_Shade_Is_Carried_From_The_Fill_Behind_It()
    {
        DocumentTable table = Assert.Single(Read(Ruled(shadeFirstCell: true)).Document.Tables);

        // Shaded cell keeps the fill; its neighbour keeps none, so this cannot
        // pass by painting every cell the same.
        Assert.NotEqual(default, table.Rows[0].Cells[0].Shading);
        Assert.Equal(default, table.Rows[0].Cells[1].Shading);
    }

    [Fact]
    public void The_Grid_Carries_The_Column_Widths_It_Was_Drawn_With()
    {
        DocumentTable table = Assert.Single(Read(Ruled()).Document.Tables);

        Assert.Equal(2, table.ColumnWidths.Count);
        Assert.Equal(150, table.ColumnWidths[0], 1);
        Assert.Equal(150, table.ColumnWidths[1], 1);
    }

    [Fact]
    public void Text_Outside_The_Grid_Stays_Outside_It()
    {
        PdfReadResult result = Read(Ruled(heading: true));
        DocumentTable table = Assert.Single(result.Document.Tables);

        Assert.Equal("Heading", result.Document.Paragraphs[0].Text);
        Assert.True(table.ParagraphIndex > 0, "The table must start after the heading.");
    }

    [Fact]
    public void A_Table_This_Codec_Wrote_Reads_Back_As_A_Table()
    {
        // The complaint this feature answers, end to end: the writer paints a
        // table as rules and shades, and until now the reader met its own output
        // as 372 anonymous dropped paths. Nothing here is a fixture drawn to
        // suit the detector - it is whatever PdfPageLayout emits.
        var document = RichTextDocument.FromParagraphs(
        [
            RichTextParagraph.Create("A1", InlineStyle.Default),
            RichTextParagraph.Create("B1", InlineStyle.Default),
            RichTextParagraph.Create("A2", InlineStyle.Default),
            RichTextParagraph.Create("B2", InlineStyle.Default),
        ]).WithTables(
        [
            new DocumentTable(0, 4,
            [
                new TableRow([Cell(0, 0), Cell(1, 1)]),
                new TableRow([Cell(2, 0), Cell(3, 1)]),
            ]),
        ]);

        using var buffer = new MemoryStream();
        Assert.False(new PdfDocumentCodec().Write(document, buffer).HasErrors);

        PdfReadResult result = Read(buffer.ToArray());
        DocumentTable table = Assert.Single(result.Document.Tables);

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(2, table.Rows[0].Cells.Count);
        Assert.Equal("A1", CellText(result, table, 0, 0));
        Assert.Equal("B1", CellText(result, table, 0, 1));
        Assert.Equal("A2", CellText(result, table, 1, 0));
        Assert.Equal("B2", CellText(result, table, 1, 1));
    }

    private static TableCell Cell(int paragraph, int column) =>
        new(paragraph, 1, column, borders: CellBorders.All(TableBorder.Solid(Broiler.Graphics.Color.BColor.Black)));

    // ---- what is deliberately not claimed -------------------------------------

    [Fact]
    public void A_Grid_Missing_One_Interior_Rule_Is_Not_A_Table()
    {
        // Fully ruled is the whole test that separates a table from an accident
        // of alignment. A grid with a gap is reported as artwork, as before.
        PdfReadResult result = Read(Ruled(dropInteriorRule: true));

        Assert.Empty(result.Document.Tables);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.VectorArtworkDropped);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TableReconstructed);
    }

    [Fact]
    public void A_Single_Rule_Is_Not_A_Table()
    {
        PdfReadResult result = Read(Underline());

        Assert.Empty(result.Document.Tables);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TableReconstructed);
    }

    [Fact]
    public void Text_Alignment_Alone_Is_Not_A_Table()
    {
        // Two columns of text with no rules at all: the shape a recall-hungry
        // detector turns into a table, and the reason this one asks for ink.
        PdfReadResult result = Read(Ruled(withoutRules: true));

        Assert.Empty(result.Document.Tables);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TableReconstructed);
    }

    [Fact]
    public void The_Diagnostic_Says_It_Reconstructed_Rather_Than_Read()
    {
        DocumentDiagnostic note = Assert.Single(
            Read(Ruled()).Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.TableReconstructed));

        Assert.Contains("reconstruction", note.Message, StringComparison.Ordinal);
        Assert.Contains("2x2", note.Message, StringComparison.Ordinal);
        Assert.Equal(DocumentDiagnosticSeverity.Info, note.Severity);
    }

    [Fact]
    public void The_Artwork_Note_Counts_What_Was_Dropped_Not_What_Was_Drawn()
    {
        // Saying "some of it was not lost" while still reporting every path as
        // dropped was the wrong half of the fix: on a real document it read
        // "372 path-painting operations were dropped" on the same page as five
        // tables it had just carried. The number has to move, not only the prose.
        DocumentDiagnostic note = Assert.Single(
            Read(Ruled()).Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.VectorArtworkDropped));

        // Six rules drawn, all six of them the grid's, so none was dropped - and
        // with nothing dropped there is no breakdown to give, rather than a
        // breakdown of everything painted hung off a count of nothing.
        Assert.Contains("All 6 path-painting operations were read as a table's rules and shades; none was dropped", note.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("were dropped:", note.Message, StringComparison.Ordinal);
        Assert.Contains("pdf.import.table-reconstructed", note.Message, StringComparison.Ordinal);
    }

    // ---- rules painted in pieces ----------------------------------------------

    [Fact]
    public void A_Rule_Painted_In_Pieces_Is_Still_One_Rule()
    {
        // Producers are free to stop a column line at every crossing and start
        // it again below, and many do. Requiring a single unbroken segment
        // refused those tables outright, although the page shows exactly the
        // lattice the unbroken version shows.
        PdfReadResult result = Read(RuledInPieces());
        DocumentTable table = Assert.Single(result.Document.Tables);

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(2, table.Rows[0].Cells.Count);

        // Fully ruled, not inferred. The inference finds this shape too - three
        // stacked rules and text between them is exactly what it anchors on -
        // and a test that only counted rows would pass on the weaker reading
        // while the document's own lattice went unrecognized.
        Assert.Contains("fully ruled", Note(result).Message, StringComparison.Ordinal);
        Assert.DoesNotContain("only partly ruled", Note(result).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Pieces_Still_Carry_Their_Border()
    {
        // Joining the pieces has to reach the borders too: a cell edge painted
        // in two halves is a painted edge, and reporting no border there would
        // describe a table the page did not draw.
        DocumentTable table = Assert.Single(Read(RuledInPieces()).Document.Tables);

        Assert.NotEqual(TableBorder.None, table.Rows[0].Cells[0].Borders.Left);
        Assert.NotEqual(TableBorder.None, table.Rows[1].Cells[1].Borders.Right);
    }

    [Fact]
    public void Two_Grids_Sharing_A_Rule_Are_One_Table()
    {
        // The shared rule puts both grids in one region, and within a region the
        // lattice has to be complete - which it is, because the column lines run
        // the whole height between them. Nothing in the ink says where the first
        // table stopped and the second began, so this reads what was drawn: one
        // closed box divided all the way across.
        PdfReadResult result = Read(Stacked());
        DocumentTable table = Assert.Single(result.Document.Tables);

        Assert.Equal(4, table.Rows.Count);
        Assert.Equal(2, table.Rows[0].Cells.Count);
        Assert.DoesNotContain("only partly ruled", Note(result).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Lower_Grids_Text_Lands_In_The_Lower_Rows()
    {
        PdfReadResult result = Read(Stacked());
        DocumentTable table = Assert.Single(result.Document.Tables);

        Assert.Equal("A1", CellText(result, table, 0, 0));
        Assert.Equal("C1", CellText(result, table, 2, 0));
        Assert.Equal("D2", CellText(result, table, 3, 1));
    }

    [Fact]
    public void A_Page_That_Dropped_Nothing_Is_Not_Named_Among_The_Pages_That_Did()
    {
        // The page list of a drop note is a claim that content went missing
        // there. On a document whose tables account for a whole page it named
        // that page anyway, because the set it kept was the pages that drew
        // artwork rather than the pages that lost any.
        DocumentDiagnostic note = Assert.Single(
            Read(TableThenArtwork()).Diagnostics,
            d => d.Code == PdfDiagnosticCodes.VectorArtworkDropped);

        Assert.Contains("On page 2.", note.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("On pages 1, 2", note.Message, StringComparison.Ordinal);
    }

    // ---- fixtures -------------------------------------------------------------

    /// <summary>The one reconstruction note, which says how the grid was read.</summary>
    private static DocumentDiagnostic Note(PdfReadResult result) =>
        Assert.Single(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.TableReconstructed);

    private static PdfReadResult Read(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, null);
    }

    private static string Text(PdfReadResult result) =>
        string.Join("\n", result.Document.Paragraphs.Select(p => p.Text).Where(t => t.Length > 0));

    private static string CellText(PdfReadResult result, DocumentTable table, int row, int column)
    {
        TableCell cell = table.Rows[row].Cells[column];
        return string.Join(
            "\n",
            Enumerable.Range(cell.ParagraphIndex, cell.ParagraphCount)
                .Select(i => result.Document.Paragraphs[i].Text));
    }

    /// <summary>
    /// A 2x2 table at x 72..372, y 600..700: columns at 72, 222 and 372, rows at
    /// 700, 650 and 600, every edge painted as a thin filled bar.
    /// </summary>
    private static byte[] Ruled(
        bool columnOrder = false,
        bool shadeFirstCell = false,
        bool heading = false,
        bool dropInteriorRule = false,
        bool withoutRules = false)
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var content = new System.Text.StringBuilder();

        if (shadeFirstCell)
            content.Append("0.9 0.2 0.2 rg\n72 650 150 50 re f\n0 g\n");

        if (!withoutRules)
        {
            // Verticals at 72, 222, 372 spanning the full height.
            foreach (int x in new[] { 72, 222, 372 })
                content.Append(System.Globalization.CultureInfo.InvariantCulture, $"{x} 600 0.75 100 re f\n");

            // Horizontals at 600, 650, 700 spanning the full width. Dropping the
            // middle one leaves a grid the rules do not fully describe.
            foreach (int y in new[] { 600, 650, 700 })
            {
                if (dropInteriorRule && y == 650)
                    continue;
                content.Append(System.Globalization.CultureInfo.InvariantCulture, $"72 {y} 300 0.75 re f\n");
            }
        }

        if (heading)
            content.Append(PdfFileBuilder.ShowText("Heading", x: 72, y: 730));

        // A1 and B1 share a baseline, as do A2 and B2: the case the line builder
        // would otherwise join across the cell boundary.
        string a1 = PdfFileBuilder.ShowText("A1", x: 80, y: 670);
        string b1 = PdfFileBuilder.ShowText("B1", x: 230, y: 670);
        string a2 = PdfFileBuilder.ShowText("A2", x: 80, y: 620);
        string b2 = PdfFileBuilder.ShowText("B2", x: 230, y: 620);

        content.Append(columnOrder ? a1 + a2 + b1 + b2 : a1 + b1 + a2 + b2);

        int stream = builder.AddStream(string.Empty, content.ToString());

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {stream} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>
    /// The same 2x2 table as <see cref="Ruled"/>, with every vertical painted as
    /// two pieces meeting at the middle rule rather than as one bar.
    /// </summary>
    private static byte[] RuledInPieces()
    {
        var content = new System.Text.StringBuilder();

        foreach (int x in new[] { 72, 222, 372 })
        {
            content.Append(System.Globalization.CultureInfo.InvariantCulture, $"{x} 600 0.75 50 re f\n");
            content.Append(System.Globalization.CultureInfo.InvariantCulture, $"{x} 650 0.75 50 re f\n");
        }

        foreach (int y in new[] { 600, 650, 700 })
            content.Append(System.Globalization.CultureInfo.InvariantCulture, $"72 {y} 300 0.75 re f\n");

        content.Append(PdfFileBuilder.ShowText("A1", x: 80, y: 670));
        content.Append(PdfFileBuilder.ShowText("B1", x: 230, y: 670));
        content.Append(PdfFileBuilder.ShowText("A2", x: 80, y: 620));
        content.Append(PdfFileBuilder.ShowText("B2", x: 230, y: 620));

        return PdfFileBuilder.SinglePage(content.ToString());
    }

    /// <summary>
    /// Two 2x2 grids one above the other, sharing the rule at y 600: the shape a
    /// producer emits for two tables with nothing between them.
    /// </summary>
    private static byte[] Stacked()
    {
        var content = new System.Text.StringBuilder();

        foreach (int x in new[] { 72, 222, 372 })
        {
            content.Append(System.Globalization.CultureInfo.InvariantCulture, $"{x} 600 0.75 100 re f\n");
            content.Append(System.Globalization.CultureInfo.InvariantCulture, $"{x} 500 0.75 100 re f\n");
        }

        foreach (int y in new[] { 500, 550, 600, 650, 700 })
            content.Append(System.Globalization.CultureInfo.InvariantCulture, $"72 {y} 300 0.75 re f\n");

        content.Append(PdfFileBuilder.ShowText("A1", x: 80, y: 670));
        content.Append(PdfFileBuilder.ShowText("A2", x: 230, y: 670));
        content.Append(PdfFileBuilder.ShowText("B1", x: 80, y: 620));
        content.Append(PdfFileBuilder.ShowText("B2", x: 230, y: 620));
        content.Append(PdfFileBuilder.ShowText("C1", x: 80, y: 570));
        content.Append(PdfFileBuilder.ShowText("C2", x: 230, y: 570));
        content.Append(PdfFileBuilder.ShowText("D1", x: 80, y: 520));
        content.Append(PdfFileBuilder.ShowText("D2", x: 230, y: 520));

        return PdfFileBuilder.SinglePage(content.ToString());
    }

    /// <summary>
    /// Two pages: the first draws nothing but a grid, the second nothing but a
    /// panel the model cannot carry.
    /// </summary>
    private static byte[] TableThenArtwork()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int first = builder.Reserve();
        int second = builder.Reserve();
        int font = builder.AddObject(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var grid = new System.Text.StringBuilder();
        foreach (int x in new[] { 72, 222, 372 })
            grid.Append(System.Globalization.CultureInfo.InvariantCulture, $"{x} 600 0.75 100 re f\n");
        foreach (int y in new[] { 600, 650, 700 })
            grid.Append(System.Globalization.CultureInfo.InvariantCulture, $"72 {y} 300 0.75 re f\n");
        grid.Append(PdfFileBuilder.ShowText("A1", x: 80, y: 670));
        grid.Append(PdfFileBuilder.ShowText("B1", x: 230, y: 670));
        grid.Append(PdfFileBuilder.ShowText("A2", x: 80, y: 620));
        grid.Append(PdfFileBuilder.ShowText("B2", x: 230, y: 620));

        int gridStream = builder.AddStream(string.Empty, grid.ToString());
        int panelStream = builder.AddStream(
            string.Empty,
            "72 500 200 100 re f\n" + PdfFileBuilder.ShowText("Second", x: 72, y: 700));

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{first} 0 R {second} 0 R] /Count 2 >>");

        foreach ((int page, int stream) in new[] { (first, gridStream), (second, panelStream) })
        {
            builder.SetObject(
                page,
                $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
                $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {stream} 0 R >>");
        }

        return builder.Build(catalog);
    }

    /// <summary>One horizontal rule under a line of text: a rule, not a grid.</summary>
    private static byte[] Underline()
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int stream = builder.AddStream(
            string.Empty,
            "72 690 200 0.75 re f\n" + PdfFileBuilder.ShowText("Underlined", x: 72, y: 700));

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {stream} 0 R >>");

        return builder.Build(catalog);
    }
}
