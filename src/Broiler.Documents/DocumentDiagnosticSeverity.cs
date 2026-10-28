namespace Broiler.Documents;

/// <summary>Severity of a <see cref="DocumentDiagnostic"/>.</summary>
public enum DocumentDiagnosticSeverity
{
    /// <summary>Informational note; content was neither skipped nor compromised.</summary>
    Info = 0,
    /// <summary>Warning; content was skipped, clamped, or recovered with possible fidelity loss.</summary>
    Warning = 1,
    /// <summary>Error; a construct or operation failed or could not be processed.</summary>
    Error = 2,
}
