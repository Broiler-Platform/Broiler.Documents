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

    [Fact]
    public void SavedDocument_Tracks_Baseline_And_Save_Points()
    {
        var initial = RichTextDocument.FromPlainText("initial");
        var editor = new RichTextEditor(initial);
        Assert.Same(initial, editor.SavedDocument);
        Assert.False(editor.IsModified);

        editor.TypeText(" extra");
        Assert.True(editor.IsModified);
        Assert.Same(initial, editor.SavedDocument);

        editor.MarkSaved();
        Assert.False(editor.IsModified);
        Assert.Same(editor.Document, editor.SavedDocument);

        var loaded = RichTextDocument.FromPlainText("loaded");
        editor.LoadDocument(loaded);
        Assert.Same(loaded, editor.SavedDocument);
        Assert.False(editor.IsModified);

        editor.LoadPlainText("plain");
        Assert.Equal("plain", editor.SavedDocument.PlainText);
        Assert.Same(editor.Document, editor.SavedDocument);
        Assert.False(editor.IsModified);
    }

    [Fact]
    public void MarkSaved_Throws_When_Snapshot_Is_Null()
    {
        var editor = new RichTextEditor();
        Assert.Throws<ArgumentNullException>("snapshot", () => editor.MarkSaved(null!));
    }

    [Fact]
    public void Asynchronous_Save_Marks_Snapshot_And_Preserves_Concurrent_Edits()
    {
        var editor = new RichTextEditor(RichTextDocument.FromPlainText("Initial text"));
        RichTextDocument d0 = editor.Document;

        // User edits document before save
        editor.MoveToDocumentEnd(false);
        editor.TypeText(" - first edit");
        RichTextDocument d1 = editor.Document;
        Assert.True(editor.IsModified);

        // Host initiates asynchronous save of current snapshot d1 via BeginSave
        RichTextDocument asyncSnapshot = editor.BeginSave();
        Assert.Same(d1, asyncSnapshot);

        // While async save is running in background, user makes further edits in editor
        editor.TypeText(" - second edit while saving");
        RichTextDocument d2 = editor.Document;
        Assert.NotSame(d1, d2);
        Assert.True(editor.IsModified);

        // Async save completes successfully on the background worker: host marks snapshot as saved
        editor.MarkSaved(asyncSnapshot);

        // The save point now references d1
        Assert.Same(d1, editor.SavedDocument);

        // But because user edited to d2, editor remains modified
        Assert.True(editor.IsModified);
        Assert.Equal("Initial text - first edit - second edit while saving", editor.GetPlainText());

        // Undo the second edit: returns to d1 (the saved snapshot)
        Assert.True(editor.Undo());
        Assert.Same(d1, editor.Document);
        Assert.False(editor.IsModified, "Editor must be unmodified when undo returns to the saved snapshot.");

        // Undo the first edit: returns to d0 (before save point)
        Assert.True(editor.Undo());
        Assert.Same(d0, editor.Document);
        Assert.True(editor.IsModified, "Editor must be modified when undo moves before the saved snapshot.");

        // Redo forward to d1 (the saved snapshot)
        Assert.True(editor.Redo());
        Assert.Same(d1, editor.Document);
        Assert.False(editor.IsModified, "Editor must be unmodified when redo returns to the saved snapshot.");

        // Redo forward to d2 (past save point)
        Assert.True(editor.Redo());
        Assert.Same(d2, editor.Document);
        Assert.True(editor.IsModified, "Editor must be modified when redo advances past the saved snapshot.");
    }

    [Fact]
    public void Asynchronous_Save_Without_Concurrent_Edits_Clears_Modified_And_Ends_Typing_Group()
    {
        var editor = new RichTextEditor();
        editor.TypeText("alpha");
        RichTextDocument snapshot = editor.Document;
        Assert.True(editor.IsModified);

        // Async save completes with no concurrent edits
        editor.MarkSaved(snapshot);
        Assert.False(editor.IsModified);
        Assert.Same(snapshot, editor.SavedDocument);

        // Next typing starts a new transaction because typing group was ended by save
        editor.TypeText(" beta");
        Assert.True(editor.IsModified);
        Assert.True(editor.Undo());
        Assert.Equal("alpha", editor.GetPlainText());
        Assert.False(editor.IsModified);
    }

    [Fact]
    public void Failed_Save_Does_Not_Mark_Saved()
    {
        var editor = new RichTextEditor();
        editor.InsertText("content");
        RichTextDocument snapshot = editor.Document;
        Assert.True(editor.IsModified);

        // Host attempts save, but an error occurs (e.g., disk full, network error).
        // Host does NOT call MarkSaved.
        Assert.True(editor.IsModified);
        Assert.Same(RichTextDocument.Empty, editor.SavedDocument);
    }

    [Fact]
    public void Host_Save_Coordinator_Pattern_Simulation()
    {
        // Simulates a desktop Writer host managing an editor and its save point lifecycle
        var editor = new RichTextEditor();
        bool hostTitleDirtyMark = editor.IsModified;
        Assert.False(hostTitleDirtyMark);

        // User types text
        editor.TypeText("Hello world");
        hostTitleDirtyMark = editor.IsModified;
        Assert.True(hostTitleDirtyMark);

        // Host performs synchronous Save
        editor.MarkSaved();
        hostTitleDirtyMark = editor.IsModified;
        Assert.False(hostTitleDirtyMark);

        // User types more
        editor.TypeText(" from Broiler");
        hostTitleDirtyMark = editor.IsModified;
        Assert.True(hostTitleDirtyMark);

        // Host initiates asynchronous Save As via BeginSave
        RichTextDocument inFlightSnapshot = editor.BeginSave();

        // User types even more while save is in flight
        editor.TypeText("!");
        hostTitleDirtyMark = editor.IsModified;
        Assert.True(hostTitleDirtyMark);

        // Async save finishes
        editor.MarkSaved(inFlightSnapshot);
        hostTitleDirtyMark = editor.IsModified;
        Assert.True(hostTitleDirtyMark, "Host title still shows dirty mark because of the exclamation point.");

        // User undoes the exclamation point
        editor.Undo();
        hostTitleDirtyMark = editor.IsModified;
        Assert.False(hostTitleDirtyMark, "Host title dirty mark cleared when undone back to saved state.");
    }
}

