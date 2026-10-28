using System;
using System.Collections.Generic;
using System.Globalization;

namespace Broiler.Documents.Model;

/// <summary>
/// The rendering-independent editor state model: a current document, a selection,
/// a bounded transactional undo/redo history, and a pending inline style for the
/// next typed text. Callers drive it with logical editing operations; it is the
/// Phase 1 kernel that Phase 2's <c>UiRichEdit</c> control will host.
/// </summary>
/// <remarks>
/// There is one history per editor, and every host shares it (ADR 0006). Most
/// calls record one transaction each. Keyboard editing is grouped instead:
/// <see cref="TypeText"/>, <see cref="Backspace"/> and <see cref="Delete"/> at a
/// caret extend the transaction they started, so undo takes back a word or a run
/// of deletions rather than a single character. A group ends when the selection
/// changes, any other edit, undo or redo runs, a pending style is set, a typed
/// word follows whitespace, or the host calls <see cref="BreakUndoGroup"/>.
/// </remarks>
public sealed class RichTextEditor
{
    private readonly List<RichTextTransaction> _undo = [];
    private readonly List<RichTextTransaction> _redo = [];
    private InlineStyleDelta? _pendingInline;
    private KeyboardRun _run;
    private RichTextDocument _savedDocument;

    /// <summary>The kind of keyboard edit the newest transaction may still absorb.</summary>
    private enum KeyboardRun
    {
        None,
        Typing,
        DeleteBackward,
        DeleteForward,
    }

    public RichTextEditor(RichTextDocument? document = null, int maxHistoryDepth = 100)
    {
        if (maxHistoryDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(maxHistoryDepth), "History depth must be at least one.");

        MaxHistoryDepth = maxHistoryDepth;
        Document = document ?? RichTextDocument.Empty;
        _savedDocument = Document;
        Selection = RichTextRange.Caret(RichTextDocument.Start);
    }

    public RichTextDocument Document { get; private set; }

    /// <summary>The immutable document snapshot recorded at the most recent save point.</summary>
    public RichTextDocument SavedDocument => _savedDocument;

    /// <summary>Whether the current document differs from the saved snapshot.</summary>
    /// <remarks>
    /// Construction and loading establish an unmodified baseline. Only document
    /// identity is compared: selection and pending typing style do not count as
    /// edits, and independently recreating the same content does not count as
    /// returning to the saved snapshot. Undo and redo can restore that snapshot.
    /// </remarks>
    public bool IsModified => !ReferenceEquals(Document, _savedDocument);

    /// <summary>
    /// Ends the open keyboard undo group and returns the current document snapshot
    /// for saving.
    /// </summary>
    /// <remarks>
    /// Hosts initiating an asynchronous save call this to capture the snapshot to write,
    /// ensuring subsequent keystrokes in the editor start a new undo transaction rather
    /// than coalescing with text captured for saving.
    /// </remarks>
    public RichTextDocument BeginSave()
    {
        BreakUndoGroup();
        return Document;
    }

    /// <summary>Marks the current document as saved and ends the open keyboard undo group.</summary>
    /// <remarks>
    /// Call after successfully saving the current document, not when a save
    /// starts or fails. This does not clear undo or redo history. For asynchronous
    /// saves where concurrent edits may have occurred, use <see cref="MarkSaved(RichTextDocument)"/>
    /// to record the exact snapshot that was written.
    /// </remarks>
    public void MarkSaved() => MarkSaved(Document);

    /// <summary>
    /// Marks a specific document snapshot as saved, enabling asynchronous saves to
    /// record the exact snapshot written without overwriting concurrent edits.
    /// </summary>
    /// <param name="snapshot">The document snapshot that was saved. Cannot be null.</param>
    /// <remarks>
    /// <para>
    /// Call after successfully saving <paramref name="snapshot"/> to storage.
    /// If the editor's current <see cref="Document"/> is still <paramref name="snapshot"/>,
    /// <see cref="IsModified"/> becomes false and the open keyboard undo group ends.
    /// </para>
    /// <para>
    /// If subsequent edits occurred while the save was in flight, <see cref="IsModified"/>
    /// remains true for the new edits. However, undoing back to <paramref name="snapshot"/>
    /// will return <see cref="IsModified"/> to false.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    public void MarkSaved(RichTextDocument snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _savedDocument = snapshot;
        if (ReferenceEquals(Document, snapshot))
        {
            BreakUndoGroup();
        }
    }

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
        Select(new RichTextRange(anchor, focus));
    }

    public void SetCaret(RichTextPosition caret) => SetSelection(RichTextRange.Caret(caret));

    public void SelectAll() => SetSelection(new RichTextRange(RichTextDocument.Start, Document.End));

    public void MoveTo(RichTextPosition target, bool extend)
    {
        RichTextPosition focus = Document.ClampPosition(target);
        RichTextPosition anchor = extend ? Selection.Anchor : focus;
        Select(new RichTextRange(anchor, focus));
    }

    public void MoveLeft(bool extend) => MoveTo(Document.PositionLeftOf(Selection.Focus), extend);

    public void MoveRight(bool extend) => MoveTo(Document.PositionRightOf(Selection.Focus), extend);

    public void MoveToParagraphStart(bool extend) => MoveTo(Document.ParagraphStart(Selection.Focus), extend);

    public void MoveToParagraphEnd(bool extend) => MoveTo(Document.ParagraphEnd(Selection.Focus), extend);

    public void MoveToDocumentStart(bool extend) => MoveTo(RichTextDocument.Start, extend);

    public void MoveToDocumentEnd(bool extend) => MoveTo(Document.End, extend);

    // --- Text editing ------------------------------------------------------

    /// <summary>
    /// Inserts text at the caret, replacing the selection, as one transaction of
    /// its own. Paste and programmatic insertion use this; typing uses
    /// <see cref="TypeText"/>.
    /// </summary>
    public bool InsertText(string text) => InsertText(text, KeyboardRun.None);

    /// <summary>
    /// Inserts text the user typed. It does what <see cref="InsertText(string)"/>
    /// does, but text typed straight after earlier typing joins that transaction,
    /// so one undo removes a word, not a character. A word that starts after
    /// whitespace begins a new transaction, and so does text holding a line or
    /// paragraph break.
    /// </summary>
    public bool TypeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        if (ContainsBreak(text))
            return InsertText(text, KeyboardRun.None);

        if (!Continues(KeyboardRun.Typing) || _pendingInline is not null ||
            _undo[^1].Operations[^1] is not InsertTextOperation typed ||
            (char.IsWhiteSpace(typed.Text[^1]) && !char.IsWhiteSpace(text[0])))
        {
            return InsertText(text, KeyboardRun.Typing);
        }

        RichTextPosition at = Selection.Focus;
        RichTextEditResult insertion = Document.InsertText(at, text, Document.InlineStyleAt(at));
        Extend(insertion, typed with { Text = typed.Text + text });
        return true;
    }

    private bool InsertText(string text, KeyboardRun run)
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
        Commit(insertion.Document, RichTextRange.Caret(insertion.Caret), operations, run);
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

    /// <summary>
    /// Deletes the selection, or the character before the caret, or the break
    /// before the paragraph. Character deletions in a row form one transaction.
    /// </summary>
    public bool Backspace()
    {
        if (!Selection.IsEmpty)
            return DeleteSelection();

        RichTextPosition caret = Selection.Focus;
        if (caret.Offset > 0)
        {
            RichTextPosition from = Document.PositionLeftOf(caret);
            if (Continues(KeyboardRun.DeleteBackward) &&
                _undo[^1].Operations[^1] is DeleteRangeOperation earlier)
            {
                // Each press deletes just before the last one, so the run is one
                // range from here to where the first press began.
                Extend(Document.DeleteRange(new RichTextRange(from, caret)),
                    new DeleteRangeOperation(new RichTextRange(from, earlier.Range.End)));
                return true;
            }

            return DeleteBetween(from, caret, KeyboardRun.DeleteBackward);
        }

        if (caret.ParagraphIndex > 0)
            return MergeAt(caret.ParagraphIndex - 1);

        return false;
    }

    /// <summary>
    /// Deletes the selection, or the character after the caret, or the break
    /// after the paragraph. Character deletions in a row form one transaction.
    /// </summary>
    public bool Delete()
    {
        if (!Selection.IsEmpty)
            return DeleteSelection();

        RichTextPosition caret = Selection.Focus;
        if (caret.Offset < Document.Paragraphs[caret.ParagraphIndex].Length)
        {
            RichTextPosition to = Document.PositionRightOf(caret);
            if (Continues(KeyboardRun.DeleteForward) &&
                _undo[^1].Operations[^1] is DeleteRangeOperation earlier)
            {
                // The caret stays put and the text closes up behind it, so in the
                // document the run started from, this press takes the characters
                // right after the run so far.
                RichTextPosition end = new(caret.ParagraphIndex, earlier.Range.End.Offset + to.Offset - caret.Offset);
                Extend(Document.DeleteRange(new RichTextRange(caret, to)),
                    new DeleteRangeOperation(new RichTextRange(earlier.Range.Start, end)));
                return true;
            }

            return DeleteBetween(caret, to, KeyboardRun.DeleteForward);
        }

        if (caret.ParagraphIndex < Document.ParagraphCount - 1)
            return MergeAt(caret.ParagraphIndex);

        return false;
    }

    // --- Formatting --------------------------------------------------------

    public bool ApplyInlineStyle(InlineStyleDelta delta)
    {
        if (Selection.IsEmpty)
        {
            // Text typed after this takes another style, so it is another edit.
            _pendingInline = _pendingInline is InlineStyleDelta existing ? Compose(existing, delta) : delta;
            _run = KeyboardRun.None;
            return false;
        }

        RichTextDocument document = Document.ApplyInlineStyle(Selection, delta);
        Commit(document, Selection, [(new ApplyInlineStyleOperation(Selection, delta))]);
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
            _run = KeyboardRun.None;
            return false;
        }

        RichTextDocument document = Document.ApplyInlineStyle(range, delta);
        RichTextRange resolvedAfter = afterSelection is RichTextRange requested
            ? ClampRange(document, requested)
            : range;
        Commit(document, resolvedAfter,
            [(new ApplyInlineStyleOperation(range, delta))]);
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
        Commit(document, Selection, [(new ApplyParagraphStyleOperation(Selection, delta))]);
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
            [(new ApplyParagraphStyleOperation(range, delta))]);
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
        _run = KeyboardRun.None;
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
        _run = KeyboardRun.None;
        return true;
    }

    /// <summary>
    /// Ends the current keyboard group, so the next keystroke starts a transaction
    /// of its own. The editor ends groups itself when the selection changes; a
    /// host calls this for what the editor cannot see, such as focus leaving the
    /// control or the document being saved.
    /// </summary>
    public void BreakUndoGroup() => _run = KeyboardRun.None;

    // --- Plain text --------------------------------------------------------

    public string GetPlainText() => Document.PlainText;

    /// <summary>Replaces the document, resetting selection, history and the saved baseline.</summary>
    public void LoadDocument(RichTextDocument document)
    {
        Document = document ?? RichTextDocument.Empty;
        _savedDocument = Document;
        Selection = RichTextRange.Caret(RichTextDocument.Start);
        _undo.Clear();
        _redo.Clear();
        _pendingInline = null;
        _run = KeyboardRun.None;
    }

    /// <summary>Replaces the document with plain text and resets selection, history and the saved baseline.</summary>
    public void LoadPlainText(string? text)
    {
        Document = RichTextDocument.FromPlainText(text);
        _savedDocument = Document;
        Selection = RichTextRange.Caret(RichTextDocument.Start);
        _undo.Clear();
        _redo.Clear();
        _pendingInline = null;
        _run = KeyboardRun.None;
    }

    // --- Internals ---------------------------------------------------------

    private bool DeleteSelection()
    {
        RichTextRange range = Selection;
        RichTextEditResult deletion = Document.DeleteRange(range);
        Commit(deletion.Document, RichTextRange.Caret(deletion.Caret), [(new DeleteRangeOperation(range))]);
        return true;
    }

    private bool DeleteBetween(RichTextPosition from, RichTextPosition to, KeyboardRun run)
    {
        var range = new RichTextRange(from, to);
        RichTextEditResult deletion = Document.DeleteRange(range);
        Commit(deletion.Document, RichTextRange.Caret(deletion.Caret), [(new DeleteRangeOperation(range))], run);
        return true;
    }

    private bool MergeAt(int firstParagraphIndex)
    {
        RichTextEditResult merge = Document.MergeParagraphs(firstParagraphIndex);
        Commit(merge.Document, RichTextRange.Caret(merge.Caret), [(new MergeParagraphsOperation(firstParagraphIndex))]);
        return true;
    }

    private void Commit(
        RichTextDocument after,
        RichTextRange afterSelection,
        IReadOnlyList<RichTextOperation> operations,
        KeyboardRun run = KeyboardRun.None)
    {
        var transaction = new RichTextTransaction(Document, Selection, after, afterSelection, operations);
        _undo.Add(transaction);
        TrimHistory();
        _redo.Clear();
        Document = after;
        Selection = afterSelection;
        _pendingInline = null;
        _run = run;
    }

    /// <summary>
    /// True when the newest transaction is an open <paramref name="run"/> and the
    /// caret is still where it left it.
    /// </summary>
    private bool Continues(KeyboardRun run) =>
        _run == run && _undo.Count > 0 && Selection == _undo[^1].AfterSelection;

    /// <summary>
    /// Folds a keyboard edit into the open transaction: it keeps its before state
    /// and takes the new after state, and <paramref name="last"/> replaces its
    /// final operation so the log stays one operation per run.
    /// </summary>
    private void Extend(RichTextEditResult edit, RichTextOperation last)
    {
        RichTextTransaction open = _undo[^1];
        var operations = new List<RichTextOperation>(open.Operations);
        operations[^1] = last;
        var caret = RichTextRange.Caret(edit.Caret);
        _undo[^1] = open with { After = edit.Document, AfterSelection = caret, Operations = operations };
        Document = edit.Document;
        Selection = caret;
    }

    private void Select(RichTextRange selection)
    {
        if (selection != Selection)
            _run = KeyboardRun.None;
        Selection = selection;
        _pendingInline = null;
    }

    private static bool ContainsBreak(string text)
    {
        foreach (char c in text)
        {
            if (c is '\r' or '\n' ||
                char.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                return true;
            }
        }

        return false;
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
