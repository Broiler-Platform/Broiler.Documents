using System;
using System.Collections.Generic;

namespace Broiler.Documents.Model;

/// <summary>
/// The rendering-independent editor state model: a current document, a selection,
/// a bounded transactional undo/redo history, and a pending inline style for the
/// next typed text. Callers drive it with logical editing operations; it is the
/// Phase 1 kernel that Phase 2's <c>UiRichEdit</c> control will host.
/// </summary>
public sealed class RichTextEditor
{
    private readonly List<RichTextTransaction> _undo = [];
    private readonly List<RichTextTransaction> _redo = [];
    private InlineStyleDelta? _pendingInline;

    public RichTextEditor(RichTextDocument? document = null, int maxHistoryDepth = 100)
    {
        if (maxHistoryDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(maxHistoryDepth), "History depth must be at least one.");

        MaxHistoryDepth = maxHistoryDepth;
        Document = document ?? RichTextDocument.Empty;
        Selection = RichTextRange.Caret(Document.Start);
    }

    public RichTextDocument Document { get; private set; }

    public RichTextRange Selection { get; private set; }

    public int MaxHistoryDepth { get; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>The inline style the next typed character would take (pending style applied).</summary>
    public InlineStyle CaretInlineStyle
    {
        get
        {
            InlineStyle style = Document.InlineStyleAt(Selection.Focus);
            return _pendingInline is InlineStyleDelta pending ? pending.Apply(style) : style;
        }
    }

    internal IReadOnlyList<RichTextTransaction> UndoStack => _undo;

    internal IReadOnlyList<RichTextTransaction> RedoStack => _redo;

    internal InlineStyleDelta? PendingInlineStyle => _pendingInline;

    // --- Selection and movement -------------------------------------------

    public void SetSelection(RichTextRange selection)
    {
        RichTextPosition anchor = Document.ClampPosition(selection.Anchor);
        RichTextPosition focus = Document.ClampPosition(selection.Focus);
        Selection = new RichTextRange(anchor, focus);
        _pendingInline = null;
    }

    public void SetCaret(RichTextPosition caret) => SetSelection(RichTextRange.Caret(caret));

    public void SelectAll() => SetSelection(new RichTextRange(Document.Start, Document.End));

    public void MoveTo(RichTextPosition target, bool extend)
    {
        RichTextPosition focus = Document.ClampPosition(target);
        RichTextPosition anchor = extend ? Selection.Anchor : focus;
        Selection = new RichTextRange(anchor, focus);
        _pendingInline = null;
    }

    public void MoveLeft(bool extend) => MoveTo(Document.PositionLeftOf(Selection.Focus), extend);

    public void MoveRight(bool extend) => MoveTo(Document.PositionRightOf(Selection.Focus), extend);

    public void MoveToParagraphStart(bool extend) => MoveTo(Document.ParagraphStart(Selection.Focus), extend);

    public void MoveToParagraphEnd(bool extend) => MoveTo(Document.ParagraphEnd(Selection.Focus), extend);

    public void MoveToDocumentStart(bool extend) => MoveTo(Document.Start, extend);

    public void MoveToDocumentEnd(bool extend) => MoveTo(Document.End, extend);

    // --- Text editing ------------------------------------------------------

    public bool InsertText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        RichTextDocument document = Document;
        var operations = new List<RichTextOperation>(2);
        RichTextPosition at;
        if (!Selection.IsEmpty)
        {
            RichTextEditResult deletion = document.DeleteRange(Selection);
            operations.Add(new DeleteRangeOperation(Selection));
            document = deletion.Document;
            at = deletion.Caret;
        }
        else
        {
            at = document.ClampPosition(Selection.Focus);
        }

        InlineStyle style = document.InlineStyleAt(at);
        if (_pendingInline is InlineStyleDelta pending)
            style = pending.Apply(style);

        RichTextEditResult insertion = document.InsertText(at, text, style);
        operations.Add(new InsertTextOperation(at, text));
        Commit(insertion.Document, RichTextRange.Caret(insertion.Caret), operations);
        return true;
    }

    /// <summary>
    /// Replaces an explicit document range as one transaction, independently of
    /// the current selection. Used by synchronized structured editors.
    /// </summary>
    public bool ReplaceText(
        RichTextRange range,
        string text,
        RichTextRange? afterSelection = null)
    {
        text ??= string.Empty;
        range = ClampRange(range);
        if (range.IsEmpty && text.Length == 0)
            return false;

        RichTextDocument document = Document;
        var operations = new List<RichTextOperation>(2);
        RichTextPosition at = range.Start;
        if (!range.IsEmpty)
        {
            RichTextEditResult deletion = document.DeleteRange(range);
            operations.Add(new DeleteRangeOperation(range));
            document = deletion.Document;
            at = deletion.Caret;
        }

        RichTextPosition caret = at;
        if (text.Length > 0)
        {
            InlineStyle style = document.InlineStyleAt(at);
            RichTextEditResult insertion = document.InsertText(at, text, style);
            operations.Add(new InsertTextOperation(at, text));
            document = insertion.Document;
            caret = insertion.Caret;
        }

        RichTextRange resolvedAfter = afterSelection is RichTextRange requested
            ? ClampRange(document, requested)
            : RichTextRange.Caret(caret);
        Commit(document, resolvedAfter, operations);
        return true;
    }

    /// <summary>
    /// Inserts a whole document's rich content at the caret, replacing the current
    /// selection, as one undo transaction. This is the rich-paste primitive.
    /// </summary>
    public bool InsertDocument(RichTextDocument content)
    {
        if (content is null || (content.ParagraphCount == 1 && content.Paragraphs[0].Length == 0))
            return false;

        RichTextDocument document = Document;
        var operations = new List<RichTextOperation>(2);
        RichTextPosition at;
        if (!Selection.IsEmpty)
        {
            RichTextEditResult deletion = document.DeleteRange(Selection);
            operations.Add(new DeleteRangeOperation(Selection));
            document = deletion.Document;
            at = deletion.Caret;
        }
        else
        {
            at = document.ClampPosition(Selection.Focus);
        }

        RichTextEditResult insertion = document.InsertDocument(at, content);
        operations.Add(new InsertDocumentOperation(at));
        Commit(insertion.Document, RichTextRange.Caret(insertion.Caret), operations);
        return true;
    }

    /// <summary>Inserts a soft line break within the current paragraph (Shift+Enter).</summary>
    public bool InsertLineBreak() => InsertText("\u2028");

    /// <summary>Splits the current paragraph at the caret (Enter).</summary>
    public bool SplitParagraph()
    {
        RichTextDocument document = Document;
        var operations = new List<RichTextOperation>(2);
        RichTextPosition at;
        if (!Selection.IsEmpty)
        {
            RichTextEditResult deletion = document.DeleteRange(Selection);
            operations.Add(new DeleteRangeOperation(Selection));
            document = deletion.Document;
            at = deletion.Caret;
        }
        else
        {
            at = document.ClampPosition(Selection.Focus);
        }

        RichTextEditResult split = document.SplitParagraph(at);
        operations.Add(new SplitParagraphOperation(at));
        Commit(split.Document, RichTextRange.Caret(split.Caret), operations);
        return true;
    }

    public bool Backspace()
    {
        if (!Selection.IsEmpty)
            return DeleteSelection();

        RichTextPosition caret = Selection.Focus;
        if (caret.Offset > 0)
            return DeleteBetween(Document.PositionLeftOf(caret), caret);

        if (caret.ParagraphIndex > 0)
            return MergeAt(caret.ParagraphIndex - 1);

        return false;
    }

    public bool Delete()
    {
        if (!Selection.IsEmpty)
            return DeleteSelection();

        RichTextPosition caret = Selection.Focus;
        if (caret.Offset < Document.Paragraphs[caret.ParagraphIndex].Length)
            return DeleteBetween(caret, Document.PositionRightOf(caret));

        if (caret.ParagraphIndex < Document.ParagraphCount - 1)
            return MergeAt(caret.ParagraphIndex);

        return false;
    }

    // --- Formatting --------------------------------------------------------

    public bool ApplyInlineStyle(InlineStyleDelta delta)
    {
        if (Selection.IsEmpty)
        {
            _pendingInline = _pendingInline is InlineStyleDelta existing ? Compose(existing, delta) : delta;
            return false;
        }

        RichTextDocument document = Document.ApplyInlineStyle(Selection, delta);
        Commit(document, Selection, new[] { (RichTextOperation)new ApplyInlineStyleOperation(Selection, delta) });
        return true;
    }

    /// <summary>Applies an inline delta to an explicit range as one transaction.</summary>
    public bool ApplyInlineStyle(
        RichTextRange range,
        InlineStyleDelta delta,
        RichTextRange? afterSelection = null)
    {
        range = ClampRange(range);
        if (range.IsEmpty)
        {
            SetSelection(range);
            _pendingInline = _pendingInline is InlineStyleDelta existing
                ? Compose(existing, delta)
                : delta;
            return false;
        }

        RichTextDocument document = Document.ApplyInlineStyle(range, delta);
        RichTextRange resolvedAfter = afterSelection is RichTextRange requested
            ? ClampRange(document, requested)
            : range;
        Commit(document, resolvedAfter,
            new[] { (RichTextOperation)new ApplyInlineStyleOperation(range, delta) });
        return true;
    }

    public bool SetBold(bool on) => ApplyInlineStyle(InlineStyleDelta.ToggleBold(on));

    public bool SetItalic(bool on) => ApplyInlineStyle(InlineStyleDelta.ToggleItalic(on));

    public bool SetUnderline(bool on) => ApplyInlineStyle(InlineStyleDelta.ToggleUnderline(on));

    public bool SetStrikethrough(bool on) => ApplyInlineStyle(InlineStyleDelta.ToggleStrikethrough(on));

    public bool ClearFormatting() => ApplyInlineStyle(InlineStyleDelta.Clear);

    public bool ApplyParagraphStyle(ParagraphStyleDelta delta)
    {
        if (delta.IndentLevel is int indent && indent < 0)
            delta = delta with { IndentLevel = 0 };

        RichTextDocument document = Document.ApplyParagraphStyle(Selection, delta);
        Commit(document, Selection, new[] { (RichTextOperation)new ApplyParagraphStyleOperation(Selection, delta) });
        return true;
    }

    /// <summary>Applies a paragraph delta to an explicit range as one transaction.</summary>
    public bool ApplyParagraphStyle(
        RichTextRange range,
        ParagraphStyleDelta delta,
        RichTextRange? afterSelection = null)
    {
        range = ClampRange(range);
        RichTextDocument document = Document.ApplyParagraphStyle(range, delta);
        RichTextRange resolvedAfter = afterSelection is RichTextRange requested
            ? ClampRange(document, requested)
            : range;
        Commit(document, resolvedAfter,
            new[] { (RichTextOperation)new ApplyParagraphStyleOperation(range, delta) });
        return true;
    }

    public bool SetAlignment(TextAlignment alignment) =>
        ApplyParagraphStyle(ParagraphStyleDelta.WithAlignment(alignment));

    public bool SetListKind(ListKind kind) =>
        ApplyParagraphStyle(ParagraphStyleDelta.WithListKind(kind));

    public bool Indent()
    {
        int current = Document.Paragraphs[Selection.Focus.ParagraphIndex].Style.IndentLevel;
        return ApplyParagraphStyle(ParagraphStyleDelta.WithIndentLevel(current + 1));
    }

    public bool Outdent()
    {
        int current = Document.Paragraphs[Selection.Focus.ParagraphIndex].Style.IndentLevel;
        return ApplyParagraphStyle(ParagraphStyleDelta.WithIndentLevel(Math.Max(0, current - 1)));
    }

    // --- Undo / redo -------------------------------------------------------

    public bool Undo()
    {
        if (_undo.Count == 0)
            return false;

        RichTextTransaction transaction = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(transaction);
        Document = transaction.Before;
        Selection = transaction.BeforeSelection;
        _pendingInline = null;
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
            return false;

        RichTextTransaction transaction = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(transaction);
        TrimHistory();
        Document = transaction.After;
        Selection = transaction.AfterSelection;
        _pendingInline = null;
        return true;
    }

    // --- Plain text --------------------------------------------------------

    public string GetPlainText() => Document.PlainText;

    /// <summary>Replaces the document, resetting selection and history.</summary>
    public void LoadDocument(RichTextDocument document)
    {
        Document = document ?? RichTextDocument.Empty;
        Selection = RichTextRange.Caret(Document.Start);
        _undo.Clear();
        _redo.Clear();
        _pendingInline = null;
    }

    /// <summary>Replaces the document with plain text and resets selection and history.</summary>
    public void LoadPlainText(string? text)
    {
        Document = RichTextDocument.FromPlainText(text);
        Selection = RichTextRange.Caret(Document.Start);
        _undo.Clear();
        _redo.Clear();
        _pendingInline = null;
    }

    // --- Internals ---------------------------------------------------------

    private bool DeleteSelection()
    {
        RichTextRange range = Selection;
        RichTextEditResult deletion = Document.DeleteRange(range);
        Commit(deletion.Document, RichTextRange.Caret(deletion.Caret), new[] { (RichTextOperation)new DeleteRangeOperation(range) });
        return true;
    }

    private bool DeleteBetween(RichTextPosition from, RichTextPosition to)
    {
        var range = new RichTextRange(from, to);
        RichTextEditResult deletion = Document.DeleteRange(range);
        Commit(deletion.Document, RichTextRange.Caret(deletion.Caret), new[] { (RichTextOperation)new DeleteRangeOperation(range) });
        return true;
    }

    private bool MergeAt(int firstParagraphIndex)
    {
        RichTextEditResult merge = Document.MergeParagraphs(firstParagraphIndex);
        Commit(merge.Document, RichTextRange.Caret(merge.Caret), new[] { (RichTextOperation)new MergeParagraphsOperation(firstParagraphIndex) });
        return true;
    }

    private void Commit(RichTextDocument after, RichTextRange afterSelection, IReadOnlyList<RichTextOperation> operations)
    {
        var transaction = new RichTextTransaction(Document, Selection, after, afterSelection, operations);
        _undo.Add(transaction);
        TrimHistory();
        _redo.Clear();
        Document = after;
        Selection = afterSelection;
        _pendingInline = null;
    }

    private void TrimHistory()
    {
        while (_undo.Count > MaxHistoryDepth)
            _undo.RemoveAt(0);
    }

    private RichTextRange ClampRange(RichTextRange range) => ClampRange(Document, range);

    private static RichTextRange ClampRange(RichTextDocument document, RichTextRange range) =>
        new(document.ClampPosition(range.Anchor), document.ClampPosition(range.Focus));

    private static InlineStyleDelta Compose(InlineStyleDelta baseDelta, InlineStyleDelta over) => new()
    {
        Bold = over.Bold ?? baseDelta.Bold,
        Italic = over.Italic ?? baseDelta.Italic,
        Underline = over.Underline ?? baseDelta.Underline,
        Strikethrough = over.Strikethrough ?? baseDelta.Strikethrough,
        Foreground = over.Foreground ?? baseDelta.Foreground,
        Background = over.Background ?? baseDelta.Background,
        SetFontFamily = over.SetFontFamily || baseDelta.SetFontFamily,
        FontFamily = over.SetFontFamily ? over.FontFamily : baseDelta.FontFamily,
        SetFontSize = over.SetFontSize || baseDelta.SetFontSize,
        FontSize = over.SetFontSize ? over.FontSize : baseDelta.FontSize,
        SetLink = over.SetLink || baseDelta.SetLink,
        LinkHref = over.SetLink ? over.LinkHref : baseDelta.LinkHref,
    };
}
