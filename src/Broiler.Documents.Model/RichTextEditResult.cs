namespace Broiler.Documents.Model;

/// <summary>
/// The outcome of a document edit: the new immutable <see cref="Document"/> and
/// the <see cref="Caret"/> position that results from the edit.
/// </summary>
public readonly struct RichTextEditResult(RichTextDocument document, RichTextPosition caret)
{
    public RichTextDocument Document { get; } = document;

    public RichTextPosition Caret { get; } = caret;
}
