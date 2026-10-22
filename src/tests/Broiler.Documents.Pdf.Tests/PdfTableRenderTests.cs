using System.Globalization;
using System.Text;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Printing a table. Its cells' paragraphs used to be laid out one under the
/// next down a single column, because that is what the model said they were.
/// </summary>
public sealed class PdfTableRenderTests
{
    [Fact]
    public void Empty_table_continuation_pages_do_not_lose_the_following_text()
    {
        RichTextParagraph[] paragraphs = [RichTextParagraph.Plain("first"),
            .. Enumerable.Repeat(RichTextParagraph.Empty, 10), RichTextParagraph.Plain("last")];
        RichTextDocument document = RichTextDocument.FromParagraphs(paragraphs).WithTables([
            new DocumentTable(0, 12, [.. Enumerable.Range(0, 12).Select(i =>
                new TableRow([new TableCell(i, 1, 0)], minHeight: 30))], [180])])
            .WithPageGeometry(new PageGeometry(200, 100, 10, 10, 10, 10));
        string pdf = Write(document);
        Assert.Equal(6, System.Text.RegularExpressions.Regex.Matches(pdf, @"/Type /Page\b").Count);
        Assert.True(OriginOf(pdf, "last").Y >= 10);
    }

    [Fact]
    public void First_cell_paragraph_spacing_moves_text_and_the_following_block()
    {
        RichTextDocument Document(float before) => RichTextDocument.FromParagraphs([
            RichTextParagraph.Create("cell", InlineStyle.Default, ParagraphStyle.Default with { SpacingBefore = before }),
            RichTextParagraph.Plain("after")]).WithTables([
                new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)])], [180])]);
        string ordinary = Write(Document(0));
        string spaced = Write(Document(7));
        Assert.Equal(7, OriginOf(ordinary, "cell").Y - OriginOf(spaced, "cell").Y, 3);
        Assert.Equal(7, OriginOf(ordinary, "after").Y - OriginOf(spaced, "after").Y, 3);
    }

    [Fact]
    public void A_table_page_break_places_its_text_at_the_top_of_the_next_page()
    {
        RichTextDocument Document(bool pageBreak) => RichTextDocument.FromPlainText("before\ncell").WithTables([
            new DocumentTable(1, 1, [new TableRow([new TableCell(1, 1, 0)])], [180], pageBreakBefore: pageBreak)]);
        string ordinary = Write(Document(false));
        string broken = Write(Document(true));
        Assert.Equal(OriginOf(broken, "before").Y, OriginOf(broken, "cell").Y, 3);
        Assert.True(OriginOf(ordinary, "cell").Y < OriginOf(broken, "cell").Y);
    }

    [Theory]
    [InlineData(TableAlignment.Start, 10)]
    [InlineData(TableAlignment.Center, 50)]
    [InlineData(TableAlignment.End, 90)]
    public void Inline_table_alignment_moves_its_text_with_its_grid(TableAlignment alignment, double left)
    {
        RichTextDocument document = RichTextDocument.FromPlainText("cell").WithTables([
            new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)])], [100], cellPadding: 0, alignment: alignment)])
            .WithPageGeometry(new PageGeometry(200, 100, 10, 10, 10, 10));
        Assert.Equal(left, OriginOf(Write(document), "cell").X, 3);
    }

    [Fact]
    public void Percentage_width_scales_column_positions_in_pdf()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("left\nright").WithTables([
            new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])],
                [40, 60], cellPadding: 0, widthPercent: 50)])
            .WithPageGeometry(new PageGeometry(200, 100, 10, 10, 10, 10));
        Assert.Equal(46, OriginOf(Write(document), "right").X, 3);
    }

    [Theory]
    [InlineData(CellVerticalAlignment.Center, 12.5)]
    [InlineData(CellVerticalAlignment.Bottom, 25)]
    public void Vertical_alignment_uses_the_padded_rows_spare_height(CellVerticalAlignment alignment, double down)
    {
        ParagraphStyle style = ParagraphStyle.Default with
        { LineHeightRule = LineHeightRule.Exact, LineHeight = 10, SpacingAfter = 5 };
        RichTextDocument Document(CellVerticalAlignment align) => RichTextDocument.FromParagraphs([
            RichTextParagraph.Create("cell", InlineStyle.Default, style)]).WithTables([
                new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0,
                    padding: new TableCellPadding(0, 7, 0, 13), verticalAlignment: align)], minHeight: 60)], [180])]);
        Assert.Equal(down, OriginOf(Write(Document(CellVerticalAlignment.Top)), "cell").Y -
            OriginOf(Write(Document(alignment)), "cell").Y, 3);
    }

    [Fact]
    public void Cell_padding_moves_text_inside_its_border_and_grows_the_row()
    {
        RichTextDocument Document(TableCellPadding? padding) => RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain("cell"), RichTextParagraph.Plain("after")]).WithTables([
                new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0, padding: padding)])], [180], cellPadding: 0)]);
        string plain = Write(Document(null));
        string padded = Write(Document(new TableCellPadding(3, 7, 11, 13)));
        Assert.Equal(3, OriginOf(padded, "cell").X - OriginOf(plain, "cell").X, 3);
        Assert.Equal(7, OriginOf(plain, "cell").Y - OriginOf(padded, "cell").Y, 3);
        Assert.Equal(20, OriginOf(plain, "after").Y - OriginOf(padded, "after").Y, 3);
    }

    [Fact]
    public void Table_keep_next_moves_the_table_and_following_text_to_a_fresh_page()
    {
        ParagraphStyle style = ParagraphStyle.Default with
        { LineHeightRule = LineHeightRule.Exact, LineHeight = 10, SpacingAfter = 0.01f };
        RichTextParagraph Para(string text, int lines) => RichTextParagraph.Create(
            string.Join("\u2028", Enumerable.Repeat(text, lines)), InlineStyle.Default, style);
        RichTextDocument Document(bool keep) => RichTextDocument.FromParagraphs([
            Para("filler", 5), Para("cell", 2), Para("after", 2)])
            .WithTables([new DocumentTable(1, 1, [new TableRow([new TableCell(1, 1, 0)])],
                [180], cellPadding: 0, keepWithNext: keep)])
            .WithPageGeometry(new PageGeometry(200, 100, 10, 10, 10, 10));
        string kept = Write(Document(true));
        string ordinary = Write(Document(false));
        Assert.Equal(OriginOf(kept, "filler").Y, OriginOf(kept, "cell").Y, 3);
        Assert.True(OriginOf(ordinary, "cell").Y < OriginOf(kept, "cell").Y);
    }

    [Fact]
    public void Minimum_row_height_moves_the_next_row_in_pdf_output()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
                [RichTextParagraph.Plain("first"), RichTextParagraph.Plain("second")])
            .WithTables([new DocumentTable(0, 2,
                [new TableRow([new TableCell(0, 1, 0)], minHeight: 180),
                 new TableRow([new TableCell(1, 1, 0)])], [200], cellPadding: 0)]);

        string pdf = Write(document);
        Assert.Equal(180, OriginOf(pdf, "first").Y - OriginOf(pdf, "second").Y, 2);
    }

    private static readonly TableBorder Hairline = new(BColor.Black, 1);

    /// <summary>A two-by-two grid over four paragraphs, in two 100-point columns.</summary>
    private static RichTextDocument Grid(BColor shading = default, CellBorders borders = default) =>
        RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain("a1"),
            RichTextParagraph.Plain("b1"),
            RichTextParagraph.Plain("a2"),
            RichTextParagraph.Plain("b2"),
        ]).WithTables([
            new DocumentTable(
                0,
                4,
                [
                    new TableRow([
                        new TableCell(0, 1, 0, shading: shading, borders: borders),
                        new TableCell(1, 1, 1),
                    ]),
                    new TableRow([new TableCell(2, 1, 0), new TableCell(3, 1, 1)]),
                ],
                [100, 100],
                cellPadding: 0),
        ]);

    private static string Write(RichTextDocument document)
    {
        using var stream = new MemoryStream();
        new PdfDocumentCodec().WritePdf(document, stream, new PdfWriteOptions(compressStreams: false));
        return Encoding.Latin1.GetString(stream.ToArray());
    }

    /// <summary>
    /// The text matrix that placed <paramref name="text"/>. The writer emits a
    /// full <c>1 0 0 1 x y Tm</c> before every run, so the numbers on the line
    /// the matrix ends are where that run was placed.
    /// </summary>
    private static (double X, double Y) OriginOf(string content, string text)
    {
        int at = content.IndexOf("(" + text, StringComparison.Ordinal);
        Assert.True(at > 0, $"{text} was not drawn");

        string before = content[..at];
        int tm = before.LastIndexOf(" Tm", StringComparison.Ordinal);
        Assert.True(tm > 0, $"no text matrix preceded {text}");

        string[] parts = before[..tm].Split('\n')[^1].Split(' ');
        return (
            double.Parse(parts[^2], CultureInfo.InvariantCulture),
            double.Parse(parts[^1], CultureInfo.InvariantCulture));
    }

    /// <summary>The content stream's rectangle-fill operators, in order.</summary>
    private static List<string> Fills(string content)
    {
        var fills = new List<string>();
        foreach (string line in content.Split('\n'))
        {
            if (line.EndsWith(" re f", StringComparison.Ordinal))
                fills.Add(line);
        }

        return fills;
    }

    [Fact]
    public void Puts_The_Cells_Of_A_Row_Beside_Each_Other()
    {
        string content = Write(Grid());

        (double X, double Y) = OriginOf(content, "a1");
        (double X, double Y) right = OriginOf(content, "b1");

        Assert.Equal(Y, right.Y, 3);
        Assert.Equal(X + 100, right.X, 3);
    }

    [Fact]
    public void Puts_The_Second_Row_Under_The_First()
    {
        string content = Write(Grid());

        // PDF measures up from the foot of the page, so further down is less.
        Assert.True(
            OriginOf(content, "a2").Y < OriginOf(content, "a1").Y,
            "the second row was not below the first");
        Assert.Equal(OriginOf(content, "a1").X, OriginOf(content, "a2").X, 3);
    }

    [Fact]
    public void Paints_A_Cells_Shading_Before_Its_Text()
    {
        string content = Write(Grid(shading: BColor.FromArgb(0xFF, 0xAE, 0xCF, 0x00)));

        Assert.Single(Fills(content));
        Assert.True(
            content.IndexOf(" re f", StringComparison.Ordinal) < content.IndexOf(" Tm", StringComparison.Ordinal),
            "the cell was painted over its own text");
    }

    [Fact]
    public void Draws_Only_The_Edges_A_Cell_States()
    {
        // Top only. Each edge is its own filled rectangle, so a cell that turns
        // three of them off draws one.
        string content = Write(Grid(borders: new CellBorders(
            TableBorder.None,
            Hairline,
            TableBorder.None,
            TableBorder.None)));

        Assert.Single(Fills(content));
    }

    [Fact]
    public void Draws_All_Four_Edges_When_A_Cell_States_Them()
    {
        Assert.Equal(4, Fills(Write(Grid(borders: CellBorders.All(Hairline)))).Count);
    }

    [Fact]
    public void A_Document_Without_Tables_Paints_Nothing()
    {
        Assert.Empty(Fills(Write(RichTextDocument.FromPlainText("body"))));
    }
}
