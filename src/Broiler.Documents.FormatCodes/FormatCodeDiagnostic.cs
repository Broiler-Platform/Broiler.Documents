using Broiler.Documents.Model;

namespace Broiler.Documents.FormatCodes;

/// <summary>A stable diagnostic produced for unusual but projectable model state.</summary>
public sealed record FormatCodeDiagnostic(
    string Code,
    FormatCodeDiagnosticSeverity Severity,
    string Message,
    RichTextRange AffectedRange);
