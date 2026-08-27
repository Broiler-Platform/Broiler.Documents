namespace Broiler.Documents.Model;

/// <summary>
/// The outcome of a document edit: the new immutable <see cref="Document"/> and
/// the <see cref="Caret"/> position that results from the edit.
/// </summary>
public readonly struct RichTextEditResult
{
    public RichTextEditResult(RichTextDocument document, RichTextPosition caret)
    {
        Document = document;
        Caret = caret;
    }

    public RichTextDocument Document { get; }

    public RichTextPosition Caret { get; }
}
