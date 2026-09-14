namespace Broiler.Documents.Model.Tests;

public sealed class RichTextDocumentSliceInsertTests
{
    private static readonly InlineStyle Bold = new() { Bold = true };

    private static RichTextParagraph Para(params (string Text, InlineStyle Style)[] runs)
    {
        RichTextParagraph paragraph = RichTextParagraph.Create(string.Empty, InlineStyle.Default);
        foreach ((string text, InlineStyle style) in runs)
            paragraph = paragraph.InsertText(paragraph.Length, text, style);
        return paragraph;
    }

    private static RichTextPosition Pos(int paragraph, int offset) => new(paragraph, offset);

    // ---- Slice ----

    [Fact]
    public void Slice_Within_A_Paragraph_Preserves_Runs()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(new[]
        {
            Para(("a", InlineStyle.Default), ("B", Bold), ("c", InlineStyle.Default)),
        });

        RichTextDocument slice = document.Slice(new RichTextRange(Pos(0, 1), Pos(0, 2)));

        Assert.Equal("B", slice.PlainText);
        Assert.True(slice.Paragraphs[0].StyleAt(0).Bold);
    }

    [Fact]
    public void Slice_Across_Paragraphs_Keeps_Structure_And_Styles()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(new[]
        {
            Para(("Hello", InlineStyle.Default)),
            Para(("World", Bold)),
        });

        RichTextDocument slice = document.Slice(new RichTextRange(Pos(0, 2), Pos(1, 3)));

        Assert.Equal("llo\nWor", slice.PlainText);
        Assert.Equal(2, slice.ParagraphCount);
        Assert.True(slice.Paragraphs[1].StyleAt(0).Bold);
    }

    [Fact]
    public void Slice_Of_An_Empty_Range_Is_Empty()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("abc");

        RichTextDocument slice = document.Slice(new RichTextRange(Pos(0, 1), Pos(0, 1)));

        Assert.Equal(string.Empty, slice.PlainText);
    }

    // ---- InsertDocument (document level) ----

    [Fact]
    public void InsertDocument_Single_Paragraph_Merges_Into_The_Target()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("ac");
        RichTextDocument content = RichTextDocument.FromParagraphs(new[] { Para(("B", Bold)) });

        RichTextEditResult result = document.InsertDocument(Pos(0, 1), content);

        Assert.Equal("aBc", result.Document.Paragraphs[0].Text);
        Assert.True(result.Document.Paragraphs[0].StyleAt(1).Bold);
    }

    [Fact]
    public void InsertDocument_Multiple_Paragraphs_Splits_The_Target()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("aXc");
        RichTextDocument content = RichTextDocument.FromParagraphs(new[]
        {
            Para(("1", InlineStyle.Default)),
            Para(("2", InlineStyle.Default)),
        });

        RichTextEditResult result = document.InsertDocument(Pos(0, 1), content);

        Assert.Equal(2, result.Document.ParagraphCount);
        Assert.Equal("a1\n2Xc", result.Document.PlainText);
    }

    // ---- RichTextEditor.InsertDocument (transactional) ----

    [Fact]
    public void Editor_InsertDocument_Inserts_At_Caret_And_Is_Undoable()
    {
        var editor = new RichTextEditor(RichTextDocument.FromPlainText("abc"));
        editor.SetCaret(Pos(0, 1));
        RichTextDocument content = RichTextDocument.FromParagraphs(new[] { Para(("X", Bold)) });

        Assert.True(editor.InsertDocument(content));
        Assert.Equal("aXbc", editor.Document.PlainText);
        Assert.True(editor.Document.Paragraphs[0].StyleAt(1).Bold);

        Assert.True(editor.Undo());
        Assert.Equal("abc", editor.Document.PlainText);
    }

    [Fact]
    public void Editor_InsertDocument_Replaces_The_Selection()
    {
        var editor = new RichTextEditor(RichTextDocument.FromPlainText("abc"));
        editor.SetSelection(new RichTextRange(Pos(0, 1), Pos(0, 2)));
        RichTextDocument content = RichTextDocument.FromParagraphs(new[] { Para(("XY", InlineStyle.Default)) });

        Assert.True(editor.InsertDocument(content));
        Assert.Equal("aXYc", editor.Document.PlainText);
    }

    [Fact]
    public void Editor_InsertDocument_Ignores_Empty_Content()
    {
        var editor = new RichTextEditor(RichTextDocument.FromPlainText("abc"));

        Assert.False(editor.InsertDocument(RichTextDocument.Empty));
        Assert.Equal("abc", editor.Document.PlainText);
    }
}
