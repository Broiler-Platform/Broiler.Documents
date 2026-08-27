namespace Broiler.Documents.Model;

/// <summary>
/// A single document operation recorded in a <see cref="RichTextTransaction"/> for
/// deterministic operation logs and inspection. Undo/redo restores document
/// snapshots (ADR 0014); these records describe what an edit did, they are not
/// replayed to invert it.
/// </summary>
public abstract record RichTextOperation;

public sealed record InsertTextOperation(RichTextPosition At, string Text) : RichTextOperation;

public sealed record DeleteRangeOperation(RichTextRange Range) : RichTextOperation;

public sealed record SplitParagraphOperation(RichTextPosition At) : RichTextOperation;

public sealed record MergeParagraphsOperation(int FirstParagraphIndex) : RichTextOperation;

public sealed record ApplyInlineStyleOperation(RichTextRange Range, InlineStyleDelta Delta) : RichTextOperation;

public sealed record ApplyParagraphStyleOperation(RichTextRange Range, ParagraphStyleDelta Delta) : RichTextOperation;

public sealed record InsertDocumentOperation(RichTextPosition At) : RichTextOperation;
