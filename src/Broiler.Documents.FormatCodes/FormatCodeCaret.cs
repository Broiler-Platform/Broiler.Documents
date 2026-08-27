using Broiler.Documents.Model;

namespace Broiler.Documents.FormatCodes;

/// <summary>A caret within a particular projected token.</summary>
public readonly record struct FormatCodeCaret(
    int TokenIndex,
    int OffsetWithinToken,
    FormatCodeBoundaryAffinity BoundaryAffinity);

/// <summary>The deterministic document mapping for one projected offset.</summary>
public readonly record struct FormatCodeMappedPosition(
    FormatCodeCaret Caret,
    RichTextPosition DocumentPosition,
    RichTextRange? AffectedRange);
