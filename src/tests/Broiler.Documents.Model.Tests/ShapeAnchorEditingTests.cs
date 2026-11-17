namespace Broiler.Documents.Model.Tests;

public sealed class ShapeAnchorEditingTests
{
    [Fact]
    public void Paragraph_Split_And_Merge_Move_Later_Anchors_Without_Changing_Placement()
    {
        var shape = new DocumentShape(1, 20, 30, 80, 60, wrap: ShapeWrap.Square) { OffsetXFromColumn = true };
        var page = new DocumentShape(1, 0, 0, 10, 10, anchorPage: 2);
        var source = RichTextDocument.FromPlainText("Before\nAnchor").WithShapes([shape, page]);
        var split = source.SplitParagraph(source.PositionAt(0, 3)).Document;
        Assert.Equal(2, split.Shapes[0].ParagraphIndex);
        Assert.Equal(20, split.Shapes[0].OffsetX);
        Assert.True(split.Shapes[0].OffsetXFromColumn);
        Assert.Equal(ShapeWrap.Square, split.Shapes[0].Wrap);
        Assert.Same(page, split.Shapes[1]);
        var merged = split.MergeParagraphs(0).Document;
        Assert.Equal(1, merged.Shapes[0].ParagraphIndex);
    }

    [Fact]
    public void Deleting_An_Anchor_Paragraph_Attaches_To_The_Merged_Survivor_With_Undo()
    {
        var shape = new DocumentShape(1, 10, 20, 30, 40);
        var source = RichTextDocument.FromPlainText("First\nSecond\nLast").WithShapes([shape]);
        var editor = new RichTextEditor(source);
        editor.ReplaceText(new RichTextRange(source.PositionAt(0, 2), source.PositionAt(2, 2)), "");
        Assert.Equal(0, Assert.Single(editor.Document.Shapes).ParagraphIndex);
        Assert.True(editor.Undo()); Assert.Same(source, editor.Document);
        Assert.True(editor.Redo()); Assert.Equal(0, editor.Document.Shapes[0].ParagraphIndex);
    }
}
