using Broiler.Documents.Model;

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
    public void The_Artwork_Note_Stops_Claiming_Everything_Was_Lost()
    {
        DocumentDiagnostic note = Assert.Single(
            Read(Ruled()).Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.VectorArtworkDropped));

        Assert.Contains("Some of it was not lost", note.Message, StringComparison.Ordinal);
        Assert.Contains("pdf.import.table-reconstructed", note.Message, StringComparison.Ordinal);
    }

    // ---- fixtures -------------------------------------------------------------

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
