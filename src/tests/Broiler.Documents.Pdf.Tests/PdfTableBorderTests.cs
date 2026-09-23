using System.Globalization;
using System.Text;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Covers what a reconstructed table's borders look like: the colour and the
/// weight the page stroked its rules with.
/// </summary>
/// <remarks>
/// PDF keeps two colours - one it fills with and one it strokes with - and a
/// pen width that only strokes use. A table shaded grey and ruled black is the
/// fixture that separates them: reading the rules in the fill colour gives the
/// borders the grey of the shading, and on a shaded row they vanish into it.
/// </remarks>
public sealed class PdfTableBorderTests
{
    private static readonly BColor Black = new(0, 0, 0);
    private static readonly BColor Grey = new(128, 128, 128);

    [Fact]
    public void A_Border_Is_Read_In_The_Colour_It_Was_Stroked_In()
    {
        DocumentTable table = Assert.Single(Read(ShadedAndRuled(lineWidth: 1.5)).Document.Tables);
        TableCell shaded = table.Rows[0].Cells[0];

        Assert.Equal(Grey, shaded.Shading);
        Assert.Equal(Black, shaded.Borders.Left.Color);
        Assert.Equal(Black, shaded.Borders.Top.Color);
        Assert.Equal(Black, shaded.Borders.Right.Color);
        Assert.Equal(Black, shaded.Borders.Bottom.Color);
    }

    [Fact]
    public void A_Border_Carries_The_Width_It_Was_Stroked_At()
    {
        DocumentTable table = Assert.Single(Read(ShadedAndRuled(lineWidth: 1.5)).Document.Tables);

        Assert.All(
            table.Rows.SelectMany(row => row.Cells),
            cell => Assert.Equal(1.5, cell.Borders.Bottom.Width, 3));
    }

    [Fact]
    public void A_Hairline_Border_Stays_Visible()
    {
        // A pen of width zero draws the thinnest line the device can. Taken at
        // its word it would be a border of no width, which the model does not
        // draw at all.
        DocumentTable table = Assert.Single(Read(ShadedAndRuled(lineWidth: 0)).Document.Tables);

        Assert.All(table.Rows.SelectMany(row => row.Cells), cell => Assert.True(cell.Borders.IsVisible));
    }

    [Fact]
    public void A_Column_Line_Painted_In_Two_Colours_Is_Still_One_Line()
    {
        // Two tables stacked in two greys, sharing their column lines: each line
        // is a piece of one grey above and a piece of the other below. Read in
        // the colours they were painted in, the pieces must still make the lines
        // the lattice is read from - and each cell must still report its own.
        DocumentTable table = Assert.Single(Read(StackedInTwoGreys()).Document.Tables);

        BColor upper = new(153, 153, 153);
        BColor lower = new(191, 191, 191);

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(upper, table.Rows[0].Cells[0].Borders.Left.Color);
        Assert.Equal(upper, table.Rows[0].Cells[0].Borders.Top.Color);
        Assert.Equal(lower, table.Rows[1].Cells[0].Borders.Left.Color);
        Assert.Equal(lower, table.Rows[1].Cells[0].Borders.Bottom.Color);
    }

    // ---- fixtures -------------------------------------------------------------

    private static PdfReadResult Read(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec().ReadPdf(stream, null);
    }

    /// <summary>
    /// A 2x2 table whose top-left cell is shaded grey, ruled in black with the
    /// given pen. The fill colour is left grey when the rules are stroked.
    /// </summary>
    private static byte[] ShadedAndRuled(double lineWidth)
    {
        var content = new StringBuilder();
        content.Append("0.502 0.502 0.502 rg 72 650 150 50 re f\n");
        content.Append(CultureInfo.InvariantCulture, $"0 0 0 RG {lineWidth} w\n");

        foreach (int y in new[] { 600, 650, 700 })
            content.Append(CultureInfo.InvariantCulture, $"72 {y} m 372 {y} l S\n");

        foreach (int x in new[] { 72, 222, 372 })
            content.Append(CultureInfo.InvariantCulture, $"{x} 600 m {x} 700 l S\n");

        content.Append("0 0 0 rg\n");
        content.Append(PdfFileBuilder.ShowText("A1", x: 80, y: 670));
        content.Append(PdfFileBuilder.ShowText("B1", x: 230, y: 670));
        content.Append(PdfFileBuilder.ShowText("A2", x: 80, y: 620));
        content.Append(PdfFileBuilder.ShowText("B2", x: 230, y: 620));

        return PdfFileBuilder.SinglePage(content.ToString());
    }

    /// <summary>
    /// One row ruled in 60% grey stacked on one ruled in 75% grey. The rule
    /// between them is the lower table's, and every column line is painted in
    /// two pieces, one of each grey.
    /// </summary>
    private static byte[] StackedInTwoGreys()
    {
        var content = new StringBuilder();
        content.Append("0.5 w\n");

        content.Append("0.6 0.6 0.6 RG 72 700 m 372 700 l S\n");
        foreach (int x in new[] { 72, 222, 372 })
            content.Append(CultureInfo.InvariantCulture, $"{x} 650 m {x} 700 l S\n");

        content.Append("0.749 0.749 0.749 RG 72 650 m 372 650 l S 72 600 m 372 600 l S\n");
        foreach (int x in new[] { 72, 222, 372 })
            content.Append(CultureInfo.InvariantCulture, $"{x} 600 m {x} 650 l S\n");

        content.Append(PdfFileBuilder.ShowText("A1", x: 80, y: 670));
        content.Append(PdfFileBuilder.ShowText("B1", x: 230, y: 670));
        content.Append(PdfFileBuilder.ShowText("A2", x: 80, y: 620));
        content.Append(PdfFileBuilder.ShowText("B2", x: 230, y: 620));

        return PdfFileBuilder.SinglePage(content.ToString());
    }
}
