namespace Broiler.Documents.Model.Tests;

public sealed class RichTextEditorSavePointTests
{
    [Fact]
    public void Construction_Establishes_An_Unmodified_Baseline()
    {
        Assert.False(new RichTextEditor().IsModified);
        Assert.False(new RichTextEditor(RichTextDocument.FromPlainText("loaded")).IsModified);
    }

    [Fact]
    public void Saving_Splits_Typing_And_Undo_Redo_Cross_The_Save_Point()
    {
        var editor = new RichTextEditor();
        editor.TypeText("a");
        Assert.True(editor.IsModified);
        editor.MarkSaved();
        Assert.False(editor.IsModified);
        editor.TypeText("b");
        Assert.True(editor.IsModified);

        Assert.True(editor.Undo());
        Assert.Equal("a", editor.GetPlainText());
        Assert.False(editor.IsModified);
        Assert.True(editor.Undo());
        Assert.True(editor.IsModified);
        Assert.True(editor.Redo());
        Assert.False(editor.IsModified);
        Assert.True(editor.Redo());
        Assert.True(editor.IsModified);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Saving_Splits_Deletion_Groups(bool backward)
    {
        var editor = new RichTextEditor(RichTextDocument.FromPlainText("abcd"));
        if (backward)
            editor.MoveToDocumentEnd(false);
        Delete();
        editor.MarkSaved();
        string saved = editor.GetPlainText();
        Delete();

        Assert.True(editor.Undo());
        Assert.Equal(saved, editor.GetPlainText());
        Assert.False(editor.IsModified);

        void Delete()
        {
            Assert.True(backward ? editor.Backspace() : editor.Delete());
        }
    }

    [Fact]
    public void Saving_After_Undo_Preserves_Redo_And_Replaces_The_Previous_Save_Point()
    {
        var editor = new RichTextEditor();
        editor.InsertText("a");
        editor.MarkSaved();
        editor.InsertText("b");
        editor.MarkSaved();
        editor.Undo();
        Assert.True(editor.IsModified);
        editor.MarkSaved();
        Assert.True(editor.CanRedo);
        Assert.True(editor.Redo());
        Assert.True(editor.IsModified);
        Assert.True(editor.Undo());
        Assert.False(editor.IsModified);
    }

    [Fact]
    public void Recreating_Saved_Text_On_A_New_Branch_Is_Still_Modified()
    {
        var editor = new RichTextEditor();
        editor.InsertText("a");
        editor.MarkSaved();
        editor.Undo();
        editor.InsertText("a");

        Assert.Equal("a", editor.GetPlainText());
        Assert.False(editor.CanRedo);
        Assert.True(editor.IsModified);
    }

    [Fact]
    public void Trimming_History_Does_Not_Create_A_False_Save_Point()
    {
        var editor = new RichTextEditor(maxHistoryDepth: 1);
        editor.InsertText("a");
        editor.MarkSaved();
        editor.InsertText("b");
        editor.InsertText("c");
        Assert.True(editor.Undo());
        Assert.False(editor.CanUndo);
        Assert.True(editor.IsModified);
        editor.MarkSaved();
        Assert.False(editor.IsModified);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Loading_Resets_The_Save_Point_And_History(bool plainText)
    {
        var editor = new RichTextEditor();
        editor.InsertText("old");
        editor.MarkSaved();
        editor.InsertText(" edit");
        if (plainText)
            editor.LoadPlainText("new");
        else
            editor.LoadDocument(RichTextDocument.FromPlainText("new"));

        Assert.False(editor.IsModified);
        Assert.False(editor.CanUndo);
        Assert.False(editor.CanRedo);
        editor.TypeText("x");
        Assert.True(editor.IsModified);
        Assert.True(editor.Undo());
        Assert.False(editor.IsModified);
    }

    [Fact]
    public void Selection_Pending_Style_And_No_Op_Edits_Do_Not_Modify_The_Document()
    {
        var editor = new RichTextEditor(RichTextDocument.FromPlainText("text"));
        editor.MoveToDocumentEnd(false);
        editor.ApplyInlineStyle(new InlineStyleDelta { Bold = true });
        Assert.False(editor.Delete());
        Assert.False(editor.InsertText(string.Empty));
        Assert.False(editor.IsModified);
        editor.TypeText("x");
        Assert.True(editor.IsModified);
    }
}
