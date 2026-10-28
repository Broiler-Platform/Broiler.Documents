using System;
using System.Linq;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using Xunit;

namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentLayoutFloatingCollisionTests
{
    private static RichTextParagraph Text(string text) => RichTextParagraph.Create(text, InlineStyle.Default,
        ParagraphStyle.Default with { LineHeightRule = LineHeightRule.Exact, LineHeight = 10 });

    private static LayoutResult Layout(RichTextDocument document, double height = 200, double width = 200)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(document, new DocumentPageBox(width, height, 10, 10, 10, 10));
    }

    private static DocumentTable CreateFloatingTable(int paragraphIndex, double x, double y, double width, double height,
        bool allowOverlap = true, int zOrder = 0, bool behindText = false, BColor shading = default, string text = "cell")
    {
        var borders = new CellBorders(TableBorder.None, TableBorder.None, TableBorder.None, TableBorder.None);
        var cell = new TableCell(paragraphIndex, 1, 0, borders: borders, shading: shading);
        var row = new TableRow([cell], minHeight: height, canSplit: false);
        var position = new TablePosition(
            TableAnchor.Page, x,
            TableAnchor.Page, y,
            AllowOverlap: allowOverlap,
            ZOrder: zOrder,
            BehindText: behindText);
        return new DocumentTable(paragraphIndex, 1, [row], [width], cellPadding: 0, position: position);
    }

    [Fact]
    public void Cascading_Collision_Avoidance_When_Three_Tables_Disallow_Overlap()
    {
        var t1 = CreateFloatingTable(0, 20, 20, 80, 30, allowOverlap: false);
        var t2 = CreateFloatingTable(1, 20, 20, 80, 30, allowOverlap: false);
        var t3 = CreateFloatingTable(2, 20, 20, 80, 30, allowOverlap: false);

        var doc = RichTextDocument.FromParagraphs([Text("p1"), Text("p2"), Text("p3")])
            .WithTables([t1, t2, t3]);

        var result = Layout(doc, height: 300);
        var page = Assert.Single(result.Pages);

        Assert.Equal(3, page.FloatingTables.Count);
        Assert.Equal(20, page.FloatingTables[0].Bounds.Top, 3);
        Assert.Equal(50, page.FloatingTables[0].Bounds.Bottom, 3);

        Assert.Equal(50, page.FloatingTables[1].Bounds.Top, 3);
        Assert.Equal(80, page.FloatingTables[1].Bounds.Bottom, 3);

        Assert.Equal(80, page.FloatingTables[2].Bounds.Top, 3);
        Assert.Equal(110, page.FloatingTables[2].Bounds.Bottom, 3);
    }

    [Fact]
    public void Floating_Table_Colliding_With_Wrapping_Shape_Is_Shifted_Down()
    {
        var shape = new DocumentShape(
            paragraphIndex: 0,
            offsetX: 10,
            offsetY: 10,
            width: 80,
            height: 40,
            wrap: ShapeWrap.Square,
            anchorPage: 1);

        var tableNonOverlap = CreateFloatingTable(0, 10, 10, 80, 25, allowOverlap: false);
        var docNonOverlap = RichTextDocument.FromParagraphs([Text("p1")])
            .WithShapes([shape])
            .WithTables([tableNonOverlap]);

        var resultNonOverlap = Layout(docNonOverlap, height: 300);
        var pageNonOverlap = Assert.Single(resultNonOverlap.Pages);
        var placedTable = Assert.Single(pageNonOverlap.FloatingTables);
        Assert.Equal(50, placedTable.Bounds.Top, 3);

        var tableOverlap = CreateFloatingTable(0, 10, 10, 80, 25, allowOverlap: true);
        var docOverlap = RichTextDocument.FromParagraphs([Text("p1")])
            .WithShapes([shape])
            .WithTables([tableOverlap]);

        var resultOverlap = Layout(docOverlap, height: 300);
        var pageOverlap = Assert.Single(resultOverlap.Pages);
        var placedOverlapTable = Assert.Single(pageOverlap.FloatingTables);
        Assert.Equal(10, placedOverlapTable.Bounds.Top, 3);
    }

    [Fact]
    public void Floating_Table_Pushed_Past_ContentBottom_Carries_To_Next_Page()
    {
        // Content area is Top: 10 to Bottom: 90
        var shape = new DocumentShape(
            paragraphIndex: 0,
            offsetX: 10,
            offsetY: 10,
            width: 80,
            height: 75,
            wrap: ShapeWrap.Square,
            anchorPage: 1);

        var table = CreateFloatingTable(0, 10, 10, 80, 20, allowOverlap: false);
        var doc = RichTextDocument.FromParagraphs([Text("p1")])
            .WithShapes([shape])
            .WithTables([table]);

        var result = Layout(doc, height: 100);
        Assert.Equal(2, result.Pages.Count);
        Assert.Empty(result.Pages[0].FloatingTables);

        var carriedTable = Assert.Single(result.Pages[1].FloatingTables);
        Assert.Equal(10, carriedTable.Bounds.Top, 3);
    }

    [Fact]
    public void Inline_Table_Shifts_Down_Below_Wrapping_Shape_And_Floating_Table()
    {
        var shape = new DocumentShape(
            paragraphIndex: 0,
            offsetX: 10,
            offsetY: 10,
            width: 100,
            height: 30,
            wrap: ShapeWrap.Square,
            anchorPage: 1);

        var floatingTable = CreateFloatingTable(0, 10, 40, 100, 20, allowOverlap: false);

        var inlineCell = new TableCell(1, 1, 0);
        var inlineRow = new TableRow([inlineCell], minHeight: 15, canSplit: false);
        var inlineTable = new DocumentTable(1, 1, [inlineRow], [100], cellPadding: 0);

        var doc = RichTextDocument.FromParagraphs([Text("p0"), Text("p1")])
            .WithShapes([shape])
            .WithTables([floatingTable, inlineTable]);

        var result = Layout(doc, height: 300);
        var page = Assert.Single(result.Pages);

        var placedInlineCell = Assert.Single(page.InlineCells);
        Assert.True(placedInlineCell.Bounds.Top >= 60, $"Expected inline table cell top >= 60, but was {placedInlineCell.Bounds.Top}");
    }

    [Fact]
    public void BehindText_Floating_Table_Does_Not_Push_Inline_Table_Down()
    {
        var behindTable = CreateFloatingTable(0, 10, 10, 100, 50, behindText: true);

        var inlineCell = new TableCell(1, 1, 0);
        var inlineRow = new TableRow([inlineCell], minHeight: 15, canSplit: false);
        var inlineTable = new DocumentTable(1, 1, [inlineRow], [100], cellPadding: 0);

        var doc = RichTextDocument.FromParagraphs([Text("p0"), Text("p1")])
            .WithTables([behindTable, inlineTable]);

        var result = Layout(doc, height: 300);
        var page = Assert.Single(result.Pages);

        var placedInlineCell = Assert.Single(page.InlineCells);
        Assert.True(placedInlineCell.Bounds.Top < 50, $"Expected inline table to not be pushed past 50, but was {placedInlineCell.Bounds.Top}");
    }

    [Fact]
    public void Painter_Orders_Overlapping_Floating_Tables_By_ZOrder()
    {
        var red = new BColor(255, 0, 0);
        var blue = new BColor(0, 0, 255);

        var tableA = CreateFloatingTable(0, 10, 10, 80, 40, allowOverlap: true, zOrder: 2, shading: red, text: "TableA");
        var tableB = CreateFloatingTable(1, 10, 10, 80, 40, allowOverlap: true, zOrder: 8, shading: blue, text: "TableB");

        var doc = RichTextDocument.FromParagraphs([Text("TableA"), Text("TableB")])
            .WithTables([tableA, tableB]);

        var box = new DocumentPageBox(200, 200, 10, 10, 10, 10);
        using var images = new ImageStore();
        var settings = new LayoutSettings();
        var page = Assert.Single(new DocumentLayout(settings, images).Layout(doc, box).Pages);

        var list = new BRenderList();
        new DocumentPagePainter(settings, images).Paint(page, box, list, _ => null);

        var fills = list.Commands.OfType<BRenderCommand.FillRect>().ToList();
        var redFillIndex = fills.FindIndex(f => f.Color == red);
        var blueFillIndex = fills.FindIndex(f => f.Color == blue);

        Assert.True(redFillIndex >= 0, "Red fill from TableA must be present");
        Assert.True(blueFillIndex >= 0, "Blue fill from TableB must be present");
        Assert.True(redFillIndex < blueFillIndex, "TableA (ZOrder 2) must be painted before TableB (ZOrder 8)");
    }

    [Fact]
    public void Painter_Renders_BehindText_Floating_Table_Under_Body_Text()
    {
        var red = new BColor(255, 0, 0);
        var behindTable = CreateFloatingTable(0, 10, 10, 80, 40, behindText: true, shading: red, text: "CellText");
        var bodyPara = Text("BodyTextOverTable");

        var doc = RichTextDocument.FromParagraphs([Text("CellText"), bodyPara])
            .WithTables([behindTable]);

        var box = new DocumentPageBox(200, 200, 10, 10, 10, 10);
        using var images = new ImageStore();
        var settings = new LayoutSettings();
        var page = Assert.Single(new DocumentLayout(settings, images).Layout(doc, box).Pages);

        var list = new BRenderList();
        new DocumentPagePainter(settings, images).Paint(page, box, list, _ => null);

        int redFillIndex = -1;
        int bodyTextIndex = -1;
        for (int i = 0; i < list.Commands.Count; i++)
        {
            if (list.Commands[i] is BRenderCommand.FillRect fill && fill.Color == red)
                redFillIndex = i;
            if (list.Commands[i] is BRenderCommand.DrawText text && text.Text.Text.Contains("BodyTextOverTable"))
                bodyTextIndex = i;
        }

        Assert.True(redFillIndex >= 0, "Behind-text table cell shading must be painted");
        Assert.True(bodyTextIndex >= 0, "Body text must be painted");
        Assert.True(redFillIndex < bodyTextIndex, "Behind-text table shading must be painted BEFORE body text lines");
    }

    [Fact]
    public void Painter_Enforces_Five_Stage_Layering()
    {
        var green = new BColor(0, 255, 0);
        var yellow = new BColor(255, 255, 0);
        var orange = new BColor(255, 165, 0);
        var purple = new BColor(128, 0, 128);

        // Stage 1: Behind-text shape
        var behindShape = new DocumentShape(0, 5, 5, 50, 50, fill: new ShapeFill(green, green, 0), behindText: true, anchorPage: 1);

        // Stage 2: Behind-text floating table
        var behindTable = CreateFloatingTable(1, 10, 10, 50, 50, behindText: true, shading: yellow);

        // Stage 3: Body text (paragraph 2)
        var bodyPara = Text("InlineBodyText");

        // Stage 4: In-front floating table
        var frontTable = CreateFloatingTable(3, 15, 15, 50, 50, behindText: false, shading: orange, zOrder: 2);

        // Stage 5: In-front shape
        var frontShape = new DocumentShape(4, 20, 20, 50, 50, fill: new ShapeFill(purple, purple, 0), behindText: false, zOrder: 5, anchorPage: 1);

        var doc = RichTextDocument.FromParagraphs([Text("s1"), Text("t1"), bodyPara, Text("t2"), Text("s2")])
            .WithShapes([behindShape, frontShape])
            .WithTables([behindTable, frontTable]);

        var box = new DocumentPageBox(200, 200, 10, 10, 10, 10);
        using var images = new ImageStore();
        var settings = new LayoutSettings();
        var page = Assert.Single(new DocumentLayout(settings, images).Layout(doc, box).Pages);

        var list = new BRenderList();
        new DocumentPagePainter(settings, images).Paint(page, box, list, _ => null);

        int behindShapeIndex = -1;
        int behindTableIndex = -1;
        int inlineBodyIndex = -1;
        int frontTableIndex = -1;
        int frontShapeIndex = -1;

        for (int i = 0; i < list.Commands.Count; i++)
        {
            if (list.Commands[i] is BRenderCommand.FillRect fill)
            {
                if (fill.Color == green) behindShapeIndex = i;
                else if (fill.Color == yellow) behindTableIndex = i;
                else if (fill.Color == orange) frontTableIndex = i;
                else if (fill.Color == purple) frontShapeIndex = i;
            }
            else if (list.Commands[i] is BRenderCommand.DrawText text && text.Text.Text.Contains("InlineBodyText"))
            {
                inlineBodyIndex = i;
            }
        }

        Assert.True(behindShapeIndex >= 0, "Behind-text shape must be painted");
        Assert.True(behindTableIndex >= 0, "Behind-text table must be painted");
        Assert.True(inlineBodyIndex >= 0, "Inline body text must be painted");
        Assert.True(frontTableIndex >= 0, "In-front table must be painted");
        Assert.True(frontShapeIndex >= 0, "In-front shape must be painted");

        Assert.True(behindShapeIndex < behindTableIndex, "Stage 1 (behind shape) < Stage 2 (behind table)");
        Assert.True(behindTableIndex < inlineBodyIndex, "Stage 2 (behind table) < Stage 3 (body text)");
        Assert.True(inlineBodyIndex < frontTableIndex, "Stage 3 (body text) < Stage 4 (front table)");
        Assert.True(frontTableIndex < frontShapeIndex, "Stage 4 (front table) < Stage 5 (front shape)");
    }
}
