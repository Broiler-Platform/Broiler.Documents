namespace Broiler.Documents.Model.Tests;

/// <summary>
/// Keyboard editing joins undo transactions; everything else keeps one per call.
/// The UI control types through <see cref="RichTextEditor.TypeText"/> and deletes
/// through Backspace and Delete, so these rules are what a Writer user feels on
/// Ctrl+Z.
/// </summary>
public sealed class RichTextEditorUndoGroupingTests
{
    private static RichTextPosition Pos(int paragraph, int offset) => new(paragraph, offset);

    private static RichTextEditor Editor(string text, int caret)
    {
        var editor = new RichTextEditor(RichTextDocument.FromPlainText(text));
        editor.SetCaret(Pos(0, caret));
        return editor;
    }

    private static void Type(RichTextEditor editor, string keys)
    {
        foreach (char key in keys)
            Assert.True(editor.TypeText(key.ToString()));
    }

    // ---- Typing ----

    [Fact]
    public void Typing_A_Word_Is_One_Undo_Step()
    {
        var editor = new RichTextEditor();

        Type(editor, "hello");

        Assert.Single(editor.UndoStack);
        Assert.True(editor.Undo());
        Assert.Equal(string.Empty, editor.GetPlainText());
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void Redo_Brings_The_Whole_Word_Back()
    {
        var editor = new RichTextEditor();
        Type(editor, "hello");
        editor.Undo();

        Assert.True(editor.Redo());

        Assert.Equal("hello", editor.GetPlainText());
        Assert.Equal(RichTextRange.Caret(Pos(0, 5)), editor.Selection);
    }

    [Fact]
    public void Each_Word_Is_Its_Own_Step_With_Its_Trailing_Space()
    {
        var editor = new RichTextEditor();

        Type(editor, "hello big world");

        Assert.Equal(3, editor.UndoStack.Count);
        editor.Undo();
        Assert.Equal("hello big ", editor.GetPlainText());
        editor.Undo();
        Assert.Equal("hello ", editor.GetPlainText());
        editor.Undo();
        Assert.Equal(string.Empty, editor.GetPlainText());
    }

    [Fact]
    public void A_Typed_Word_Logs_One_Insert_Operation()
    {
        var editor = new RichTextEditor();

        Type(editor, "hi!");

        RichTextOperation operation = Assert.Single(Assert.Single(editor.UndoStack).Operations);
        InsertTextOperation insert = Assert.IsType<InsertTextOperation>(operation);
        Assert.Equal("hi!", insert.Text);
        Assert.Equal(RichTextDocument.Start, insert.At);
    }

    [Fact]
    public void Typing_Over_A_Selection_Undoes_Together_With_The_Replaced_Text()
    {
        var editor = new RichTextEditor(RichTextDocument.FromPlainText("one two"));
        editor.SetSelection(new RichTextRange(Pos(0, 4), Pos(0, 7)));

        Type(editor, "six");

        Assert.Equal("one six", editor.GetPlainText());
        RichTextTransaction transaction = Assert.Single(editor.UndoStack);
        Assert.IsType<DeleteRangeOperation>(transaction.Operations[0]);
        Assert.Equal("six", Assert.IsType<InsertTextOperation>(transaction.Operations[1]).Text);

        Assert.True(editor.Undo());
        Assert.Equal("one two", editor.GetPlainText());
        Assert.Equal(new RichTextRange(Pos(0, 4), Pos(0, 7)), editor.Selection);
    }

    [Fact]
    public void Moving_The_Caret_Ends_The_Group()
    {
        var editor = new RichTextEditor();
        Type(editor, "ab");
        editor.MoveLeft(extend: false);

        Type(editor, "X");

        Assert.Equal("aXb", editor.GetPlainText());
        Assert.Equal(2, editor.UndoStack.Count);
        editor.Undo();
        Assert.Equal("ab", editor.GetPlainText());
    }

    [Fact]
    public void Setting_The_Selection_It_Already_Has_Keeps_The_Group()
    {
        var editor = new RichTextEditor();
        Type(editor, "ab");
        editor.SetSelection(editor.Selection);

        Type(editor, "c");

        Assert.Single(editor.UndoStack);
    }

    [Fact]
    public void Clicking_Back_To_The_Same_Caret_After_Moving_Away_Starts_A_New_Group()
    {
        var editor = new RichTextEditor();
        Type(editor, "ab");
        editor.MoveToDocumentStart(extend: false);
        editor.MoveToDocumentEnd(extend: false);

        Type(editor, "c");

        Assert.Equal(2, editor.UndoStack.Count);
    }

    [Fact]
    public void BreakUndoGroup_Ends_The_Group()
    {
        var editor = new RichTextEditor();
        Type(editor, "ab");

        editor.BreakUndoGroup();
        Type(editor, "cd");

        Assert.Equal(2, editor.UndoStack.Count);
        editor.Undo();
        Assert.Equal("ab", editor.GetPlainText());
    }

    [Fact]
    public void A_Pending_Style_Ends_The_Group_And_Applies_To_The_New_One()
    {
        var editor = new RichTextEditor();
        Type(editor, "ab");

        editor.SetBold(true);
        Type(editor, "cd");

        Assert.Equal(2, editor.UndoStack.Count);
        Assert.True(editor.Document.Paragraphs[0].StyleAt(2).Bold);
        Assert.True(editor.Document.Paragraphs[0].StyleAt(3).Bold);
        editor.Undo();
        Assert.Equal("ab", editor.GetPlainText());
        Assert.False(editor.Document.Paragraphs[0].StyleAt(1).Bold);
    }

    [Fact]
    public void Typing_After_Undo_Starts_A_New_Group_And_Drops_Redo()
    {
        var editor = new RichTextEditor();
        Type(editor, "ab");
        editor.Undo();

        Type(editor, "cd");

        Assert.False(editor.CanRedo);
        Assert.Single(editor.UndoStack);
        Assert.Equal("cd", editor.GetPlainText());
    }

    [Fact]
    public void Another_Edit_Ends_The_Group()
    {
        var editor = new RichTextEditor();
        Type(editor, "ab");
        Assert.True(editor.SplitParagraph());

        Type(editor, "cd");

        Assert.Equal(3, editor.UndoStack.Count);
        editor.Undo();
        Assert.Equal("ab\n", editor.GetPlainText());
    }

    [Fact]
    public void Typed_Text_With_A_Break_Is_A_Step_Of_Its_Own()
    {
        var editor = new RichTextEditor();
        Type(editor, "ab");

        Assert.True(editor.TypeText("c\nd"));
        Type(editor, "e");

        Assert.Equal(3, editor.UndoStack.Count);
        Assert.Equal("abc\nde", editor.GetPlainText());
    }

    [Fact]
    public void InsertText_Never_Joins_Typing()
    {
        var editor = new RichTextEditor();
        Type(editor, "ab");

        Assert.True(editor.InsertText("pasted"));
        Type(editor, "c");

        Assert.Equal(3, editor.UndoStack.Count);
        editor.Undo();
        Assert.Equal("abpasted", editor.GetPlainText());
        editor.Undo();
        Assert.Equal("ab", editor.GetPlainText());
    }

    [Fact]
    public void A_Group_Counts_As_One_Entry_Against_The_History_Depth()
    {
        var editor = new RichTextEditor(maxHistoryDepth: 2);

        Type(editor, "one two three");

        Assert.Equal(2, editor.UndoStack.Count);
        editor.Undo();
        editor.Undo();
        Assert.Equal("one ", editor.GetPlainText());
    }

    // ---- Deleting ----

    [Fact]
    public void Backspaces_In_A_Row_Are_One_Step()
    {
        RichTextEditor editor = Editor("abcdef", caret: 6);

        Assert.True(editor.Backspace());
        Assert.True(editor.Backspace());
        Assert.True(editor.Backspace());

        Assert.Equal("abc", editor.GetPlainText());
        DeleteRangeOperation delete = Assert.IsType<DeleteRangeOperation>(
            Assert.Single(Assert.Single(editor.UndoStack).Operations));
        Assert.Equal(new RichTextRange(Pos(0, 3), Pos(0, 6)), delete.Range);

        Assert.True(editor.Undo());
        Assert.Equal("abcdef", editor.GetPlainText());
        Assert.Equal(RichTextRange.Caret(Pos(0, 6)), editor.Selection);
    }

    [Fact]
    public void Deletes_In_A_Row_Are_One_Step()
    {
        RichTextEditor editor = Editor("abcdef", caret: 1);

        Assert.True(editor.Delete());
        Assert.True(editor.Delete());
        Assert.True(editor.Delete());

        Assert.Equal("aef", editor.GetPlainText());
        DeleteRangeOperation delete = Assert.IsType<DeleteRangeOperation>(
            Assert.Single(Assert.Single(editor.UndoStack).Operations));
        Assert.Equal(new RichTextRange(Pos(0, 1), Pos(0, 4)), delete.Range);

        Assert.True(editor.Undo());
        Assert.Equal("abcdef", editor.GetPlainText());
        Assert.Equal(RichTextRange.Caret(Pos(0, 1)), editor.Selection);
    }

    [Fact]
    public void A_Backspace_Run_Treats_A_Surrogate_Pair_As_One_Character()
    {
        string face = char.ConvertFromUtf32(0x1F600);
        RichTextEditor editor = Editor("a" + face + "b", caret: 4);

        Assert.True(editor.Backspace());
        Assert.True(editor.Backspace());

        Assert.Equal("a", editor.GetPlainText());
        Assert.Single(editor.UndoStack);
        editor.Undo();
        Assert.Equal("a" + face + "b", editor.GetPlainText());
    }

    [Fact]
    public void Backspace_After_Typing_Is_A_Separate_Step()
    {
        var editor = new RichTextEditor();
        Type(editor, "helo");

        Assert.True(editor.Backspace());
        Type(editor, "lo");

        Assert.Equal("hello", editor.GetPlainText());
        Assert.Equal(3, editor.UndoStack.Count);
        editor.Undo();
        Assert.Equal("hel", editor.GetPlainText());
        editor.Undo();
        Assert.Equal("helo", editor.GetPlainText());
    }

    [Fact]
    public void Backspace_And_Delete_Do_Not_Join_Each_Other()
    {
        RichTextEditor editor = Editor("abcdef", caret: 3);

        editor.Backspace();
        editor.Delete();

        Assert.Equal("abef", editor.GetPlainText());
        Assert.Equal(2, editor.UndoStack.Count);
    }

    [Fact]
    public void Joining_Paragraphs_Ends_A_Backspace_Run()
    {
        var editor = new RichTextEditor(RichTextDocument.FromPlainText("ab\ncd"));
        editor.SetCaret(Pos(1, 1));

        editor.Backspace();
        editor.Backspace();
        editor.Backspace();

        Assert.Equal("ad", editor.GetPlainText());
        Assert.Equal(3, editor.UndoStack.Count);
        editor.Undo();
        Assert.Equal("abd", editor.GetPlainText());
        editor.Undo();
        Assert.Equal("ab\nd", editor.GetPlainText());
    }

    [Fact]
    public void Deleting_A_Selection_Is_A_Step_Of_Its_Own()
    {
        RichTextEditor editor = Editor("abcdef", caret: 6);
        editor.Backspace();
        editor.SetSelection(new RichTextRange(Pos(0, 1), Pos(0, 3)));

        editor.Backspace();
        editor.Backspace();

        Assert.Equal("de", editor.GetPlainText());
        Assert.Equal(3, editor.UndoStack.Count);
        editor.Undo();
        Assert.Equal("ade", editor.GetPlainText());
        editor.Undo();
        Assert.Equal("abcde", editor.GetPlainText());
    }
}
