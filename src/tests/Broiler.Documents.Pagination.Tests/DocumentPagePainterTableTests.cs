using Broiler.Documents.Model;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;

namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentPagePainterTableTests
{
    private static BRenderList Paint(RichTextDocument document, DocumentPageBox? box = null)
    {
        box ??= new DocumentPageBox(300, 300, 10, 10, 10, 10);
        using var images = new ImageStore();
        var settings = new LayoutSettings();
        var page = Assert.Single(new DocumentLayout(settings, images).Layout(document, box).Pages);
        var list = new BRenderList();
        new DocumentPagePainter(settings, images).Paint(page, box, list, _ => null);
        return list;
    }

    [Fact]
    public void Dashed_And_Dotted_Borders_Emit_Segmented_Fills()
    {
        var borders = new CellBorders(
            Left: TableBorder.None,
            Top: TableBorder.Dashed(BColor.Black, width: 2),
            Right: TableBorder.None,
            Bottom: TableBorder.Dotted(BColor.Black, width: 2));

        var cell = new TableCell(0, 1, 0, borders: borders);
        var table = new DocumentTable(0, 1, [new TableRow([cell])], [100], cellPadding: 0);
        var document = RichTextDocument.FromPlainText("content").WithTables([table]);

        var list = Paint(document);
        var fills = list.Commands.OfType<BRenderCommand.FillRect>().ToList();

        // Top is dashed along 100pt, Bottom is dotted along 100pt
        // Each should emit multiple segmented fills along their edges
        Assert.True(fills.Count > 10, $"Expected multiple dash/dot fills, got {fills.Count}");
    }

    [Fact]
    public void Double_Border_Emits_Two_Parallel_Stripe_Fills()
    {
        var borders = new CellBorders(
            Left: TableBorder.None,
            Top: TableBorder.Double(BColor.Black, width: 3),
            Right: TableBorder.None,
            Bottom: TableBorder.None);

        var cell = new TableCell(0, 1, 0, borders: borders);
        var table = new DocumentTable(0, 1, [new TableRow([cell])], [100], cellPadding: 0);
        var document = RichTextDocument.FromPlainText("content").WithTables([table]);

        var list = Paint(document);
        var fills = list.Commands.OfType<BRenderCommand.FillRect>().ToList();

        // Double border with width 3 produces two stripes each with stripe = 3 / 3 = 1.0 height
        var topStripes = fills.Where(f => Math.Abs(f.Rect.Width - 100) < 0.1 && Math.Abs(f.Rect.Height - 1.0) < 0.1).ToList();
        Assert.Equal(2, topStripes.Count);
        Assert.True(topStripes[0].Rect.Top < topStripes[1].Rect.Top);
    }

    [Fact]
    public void Groove_Border_Emits_Dual_Shaded_Fills()
    {
        var borders = new CellBorders(
            Left: TableBorder.None,
            Top: TableBorder.Groove(new BColor(100, 100, 100), width: 4),
            Right: TableBorder.None,
            Bottom: TableBorder.None);

        var cell = new TableCell(0, 1, 0, borders: borders);
        var table = new DocumentTable(0, 1, [new TableRow([cell])], [100], cellPadding: 0);
        var document = RichTextDocument.FromPlainText("content").WithTables([table]);

        var list = Paint(document);
        var fills = list.Commands.OfType<BRenderCommand.FillRect>().ToList();

        // Groove has 2 halves of height 2.0 with different shades (darker outer, lighter inner)
        var grooveHalves = fills.Where(f => Math.Abs(f.Rect.Width - 100) < 0.1 && Math.Abs(f.Rect.Height - 2.0) < 0.1).ToList();
        Assert.Equal(2, grooveHalves.Count);
        Assert.NotEqual(grooveHalves[0].Color, grooveHalves[1].Color);
    }

    [Fact]
    public void Border_Collapse_Adjacent_Cells_Produce_Single_Centered_Seam()
    {
        // 2 adjacent cells in a 1-row table, each with 1pt black borders
        var borders = CellBorders.All(new TableBorder(BColor.Black, 1.0));
        var cell0 = new TableCell(0, 1, 0, borders: borders);
        var cell1 = new TableCell(1, 1, 1, borders: borders);
        var table = new DocumentTable(0, 2, [new TableRow([cell0, cell1])], [50, 50], cellPadding: 0);
        var document = RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain("a"),
            RichTextParagraph.Plain("b")
        ]).WithTables([table]);

        var list = Paint(document);
        var fills = list.Commands.OfType<BRenderCommand.FillRect>().ToList();

        // Seam between col 0 and col 1 is at contentLeft + 50 = 10 + 50 = 60
        // Half-width arbitration: centered at X=60 with width 1.0 -> Left = 59.5
        var sharedSeam = fills.Where(f => Math.Abs(f.Rect.Left - 59.5) < 0.1 && Math.Abs(f.Rect.Width - 1.0) < 0.1).ToList();
        Assert.Single(sharedSeam);
    }

    [Fact]
    public void Border_Collapse_Precedence_Wider_Border_Wins()
    {
        // Cell 0 has 2pt red right border; Cell 1 has 1pt blue left border
        var red2 = new TableBorder(new BColor(255, 0, 0), 2.0);
        var blue1 = new TableBorder(new BColor(0, 0, 255), 1.0);

        var borders0 = new CellBorders(TableBorder.None, TableBorder.None, red2, TableBorder.None);
        var borders1 = new CellBorders(blue1, TableBorder.None, TableBorder.None, TableBorder.None);

        var cell0 = new TableCell(0, 1, 0, borders: borders0);
        var cell1 = new TableCell(1, 1, 1, borders: borders1);
        var table = new DocumentTable(0, 2, [new TableRow([cell0, cell1])], [60, 60], cellPadding: 0);
        var document = RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain("left"),
            RichTextParagraph.Plain("right")
        ]).WithTables([table]);

        var list = Paint(document);
        var fills = list.Commands.OfType<BRenderCommand.FillRect>().ToList();

        // At seam X = 10 + 60 = 70:
        // Wider border (2pt red) wins over 1pt blue! Centered at 70 - 1.0 = 69
        var seam = Assert.Single(fills, f => Math.Abs(f.Rect.Left - 69.0) < 0.1);
        Assert.Equal(2.0, seam.Rect.Width, 2);
        Assert.Equal(new BColor(255, 0, 0), seam.Color);
    }

    [Fact]
    public void Border_Collapse_Precedence_Double_Beats_Solid_When_Widths_Equal()
    {
        var doubleB = TableBorder.Double(BColor.Black, 1.5);
        var solidB = new TableBorder(BColor.Black, 1.5, TableBorderStyle.Solid);

        var borders0 = new CellBorders(TableBorder.None, TableBorder.None, solidB, TableBorder.None);
        var borders1 = new CellBorders(doubleB, TableBorder.None, TableBorder.None, TableBorder.None);

        var cell0 = new TableCell(0, 1, 0, borders: borders0);
        var cell1 = new TableCell(1, 1, 1, borders: borders1);
        var table = new DocumentTable(0, 2, [new TableRow([cell0, cell1])], [50, 50], cellPadding: 0);
        var document = RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain("left"),
            RichTextParagraph.Plain("right")
        ]).WithTables([table]);

        var list = Paint(document);
        var fills = list.Commands.OfType<BRenderCommand.FillRect>().ToList();

        // Double has higher precedence than Solid: double border wins and emits 2 stripes at seam!
        // Seam is at X = 10 + 50 = 60. Stripe width = 1.5 / 3 = 0.5.
        var stripes = fills.Where(f => Math.Abs(f.Rect.Width - 0.5) < 0.1 && f.Rect.Left >= 59.0 && f.Rect.Right <= 61.0).ToList();
        Assert.Equal(2, stripes.Count);
    }

    [Fact]
    public void Diagonal_Borders_Emit_Angled_Transforms_And_Clipping()
    {
        var borders = new CellBorders(
            Left: TableBorder.None, Top: TableBorder.None, Right: TableBorder.None, Bottom: TableBorder.None,
            DiagonalDown: TableBorder.Solid(BColor.Black),
            DiagonalUp: TableBorder.Dashed(new BColor(255, 0, 0), width: 1.0));

        var cell = new TableCell(0, 1, 0, borders: borders);
        var table = new DocumentTable(0, 1, [new TableRow([cell])], [80], cellPadding: 0);
        var document = RichTextDocument.FromPlainText("content").WithTables([table]);

        var list = Paint(document);

        // Both diagonals should push and pop clips of cell bounds
        var clips = list.Commands.OfType<BRenderCommand.PushClip>().ToList();
        Assert.True(clips.Count >= 2, $"Expected at least 2 PushClips for diagonals, got {clips.Count}");

        // Diagonal lines are drawn via PushTransform / PopTransform
        var transforms = list.Commands.OfType<BRenderCommand.PushTransform>().ToList();
        Assert.True(transforms.Count >= 2, $"Expected at least 2 PushTransforms for diagonals, got {transforms.Count}");
    }

    [Fact]
    public void Cell_Overflow_Clipping_On_ExactHeight_Row_Prevents_Bleeding()
    {
        // Fixed row with minHeight 20, exactHeight = true
        // Contains 3 paragraphs that would otherwise take ~40-60pt
        var cell = new TableCell(0, 3, 0);
        var row = new TableRow([cell], minHeight: 20, exactHeight: true);
        var table = new DocumentTable(0, 3, [row], [100], cellPadding: 0);

        var document = RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain("First line inside cell"),
            RichTextParagraph.Plain("Second line inside cell"),
            RichTextParagraph.Plain("Third line overflowing cell")
        ]).WithTables([table]);

        var box = new DocumentPageBox(200, 200, 10, 10, 10, 10);
        using var images = new ImageStore();
        var settings = new LayoutSettings();
        var page = Assert.Single(new DocumentLayout(settings, images).Layout(document, box).Pages);

        // In layout, the cell height must be exactly 20
        var layoutCell = Assert.Single(page.Cells);
        Assert.Equal(20, layoutCell.Bounds.Height, 3);

        // Lines that overflow the exact row height are either not placed or clipped
        foreach (var line in page.Lines)
        {
            double lineBottom = line.Top + (line.ClipHeight ?? line.Height);
            Assert.True(lineBottom <= layoutCell.Bounds.Bottom + 0.01,
                $"Line bottom {lineBottom} exceeded cell bottom {layoutCell.Bounds.Bottom}");
        }

        var list = new BRenderList();
        new DocumentPagePainter(settings, images).Paint(page, box, list, _ => null);

        // Clips must be balanced
        Assert.Equal(
            list.Commands.OfType<BRenderCommand.PushClip>().Count(),
            list.Commands.OfType<BRenderCommand.PopClip>().Count());
    }
}
