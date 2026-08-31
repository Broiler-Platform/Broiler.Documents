using System.Globalization;
using System.Text;
using Broiler.Graphics;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Printing a table. Its cells' paragraphs used to be laid out one under the
/// next down a single column, because that is what the model said they were.
/// </summary>
public sealed class PdfTableRenderTests
{
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

    [Fact(Timeout = 600000)]
    public void Puts_The_Cells_Of_A_Row_Beside_Each_Other()
    {
        string content = Write(Grid());

        (double X, double Y) left = OriginOf(content, "a1");
        (double X, double Y) right = OriginOf(content, "b1");

        Assert.Equal(left.Y, right.Y, 3);
        Assert.Equal(left.X + 100, right.X, 3);
    }

    [Fact(Timeout = 600000)]
    public void Puts_The_Second_Row_Under_The_First()
    {
        string content = Write(Grid());

        // PDF measures up from the foot of the page, so further down is less.
        Assert.True(
            OriginOf(content, "a2").Y < OriginOf(content, "a1").Y,
            "the second row was not below the first");
        Assert.Equal(OriginOf(content, "a1").X, OriginOf(content, "a2").X, 3);
    }

    [Fact(Timeout = 600000)]
    public void Paints_A_Cells_Shading_Before_Its_Text()
    {
        string content = Write(Grid(shading: BColor.FromArgb(0xFF, 0xAE, 0xCF, 0x00)));

        Assert.Single(Fills(content));
        Assert.True(
            content.IndexOf(" re f", StringComparison.Ordinal) < content.IndexOf(" Tm", StringComparison.Ordinal),
            "the cell was painted over its own text");
    }

    [Fact(Timeout = 600000)]
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

    [Fact(Timeout = 600000)]
    public void Draws_All_Four_Edges_When_A_Cell_States_Them()
    {
        Assert.Equal(4, Fills(Write(Grid(borders: CellBorders.All(Hairline)))).Count);
    }

    [Fact(Timeout = 600000)]
    public void A_Document_Without_Tables_Paints_Nothing()
    {
        Assert.Empty(Fills(Write(RichTextDocument.FromPlainText("body"))));
    }
}
