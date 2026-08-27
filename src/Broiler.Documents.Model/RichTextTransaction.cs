using System.Collections.Generic;

namespace Broiler.Documents.Model;

/// <summary>
/// One undoable unit of work: the document and selection before and after the
/// change, plus the operations that produced it. Because documents are immutable
/// and structurally shared, holding before/after snapshots is cheap and is the
/// basis for undo/redo (ADR 0014).
/// </summary>
public sealed record RichTextTransaction(
    RichTextDocument Before,
    RichTextRange BeforeSelection,
    RichTextDocument After,
    RichTextRange AfterSelection,
    IReadOnlyList<RichTextOperation> Operations);
