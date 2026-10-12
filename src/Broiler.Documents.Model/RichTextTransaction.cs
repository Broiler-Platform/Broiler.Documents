using System.Collections.Generic;

namespace Broiler.Documents.Model;

/// <summary>
/// One undoable unit of work: the document and selection before and after the
/// change, plus the operations that produced it, in the order they ran. Because
/// documents are immutable and structurally shared, holding before/after
/// snapshots is cheap and is the basis for undo/redo (Broiler.UI ADR 0014). A run
/// of typing or of deletions at the caret is one transaction whose last operation
/// covers the whole run (see <see cref="RichTextEditor.TypeText"/>).
/// </summary>
public sealed record RichTextTransaction(
    RichTextDocument Before,
    RichTextRange BeforeSelection,
    RichTextDocument After,
    RichTextRange AfterSelection,
    IReadOnlyList<RichTextOperation> Operations);
