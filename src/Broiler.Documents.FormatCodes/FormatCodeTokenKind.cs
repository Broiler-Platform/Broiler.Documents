using System;

namespace Broiler.Documents.FormatCodes;

/// <summary>Semantic categories emitted by the canonical projector.</summary>
public enum FormatCodeTokenKind
{
    Text = 0,
    InlineCode,
    ParagraphCode,
    StructureCode,
    Escape,
    PendingCode,
    Diagnostic,
}

/// <summary>Chooses a side when several projected tokens share one document boundary.</summary>
public enum FormatCodeBoundaryAffinity
{
    Before = 0,
    After,
}

/// <summary>Actions a future structured UI may offer for a token.</summary>
[Flags]
public enum FormatCodeEditCapabilities
{
    None = 0,
    Navigate = 1 << 0,
    Copy = 1 << 1,
    ChangeFormatting = 1 << 2,
    EditText = 1 << 3,
    Remove = 1 << 4,
}

/// <summary>Severity of a deterministic projection diagnostic.</summary>
public enum FormatCodeDiagnosticSeverity
{
    Information = 0,
    Warning,
    Error,
}
