using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Broiler.Documents;

/// <summary>
/// The outcome of writing a document: the number of bytes written, how far the
/// destination got, and any diagnostics (styles or constructs that could not be
/// represented in the target format).
/// </summary>
/// <remarks>
/// Open for derivation for the same reason as <see cref="DocumentReadResult"/>;
/// <c>PdfWriteResult</c> adds the page count.
/// </remarks>
public class DocumentWriteResult
{
    private static readonly ReadOnlyCollection<DocumentDiagnostic> EmptyDiagnostics =
        Array.AsReadOnly(Array.Empty<DocumentDiagnostic>());

    public DocumentWriteResult(
        long bytesWritten,
        IEnumerable<DocumentDiagnostic>? diagnostics = null,
        DocumentResultStatus status = DocumentResultStatus.Success,
        DocumentDestinationState destinationState = DocumentDestinationState.Committed)
    {
        if (bytesWritten < 0)
            throw new ArgumentOutOfRangeException(nameof(bytesWritten));

        BytesWritten = bytesWritten;
        Diagnostics = diagnostics is null
            ? EmptyDiagnostics
            : Array.AsReadOnly(diagnostics.ToArray());
        Status = status;
        DestinationState = destinationState;
    }

    public long BytesWritten { get; }

    public IReadOnlyList<DocumentDiagnostic> Diagnostics { get; }

    public DocumentResultStatus Status { get; }

    /// <summary>
    /// How far the destination got. <see cref="DocumentResultStatus.Success"/>
    /// requires <see cref="DocumentDestinationState.Committed"/>; a rejection
    /// paired with <see cref="DocumentDestinationState.PartialDestination"/> tells
    /// a caller-owned stream that an unusable prefix needs discarding.
    /// </summary>
    public DocumentDestinationState DestinationState { get; }

    public bool HasErrors => Diagnostics.Any(static d => d.Severity == DocumentDiagnosticSeverity.Error);

    /// <summary>
    /// Derives a status from diagnostics, for a write that reached the
    /// destination. See <see cref="DocumentReadResult.StatusFrom"/>.
    /// </summary>
    public static DocumentResultStatus StatusFrom(IEnumerable<DocumentDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        foreach (DocumentDiagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Severity != DocumentDiagnosticSeverity.Info)
                return DocumentResultStatus.Partial;
        }

        return DocumentResultStatus.Success;
    }

    /// <summary>A rejection that never touched the destination.</summary>
    public static DocumentWriteResult Rejected(string code, string message) =>
        new(
            0,
            [DocumentDiagnostic.Error(code, message)],
            DocumentResultStatus.Rejected,
            DocumentDestinationState.NotStarted);

    /// <summary>The write-side counterpart of <see cref="DocumentReadResult.InvalidOptions"/>.</summary>
    public static DocumentWriteResult InvalidOptions(string codecName, Type expected, Type actual) =>
        Rejected(
            DocumentDiagnosticCodes.OptionsInvalid,
            $"The {codecName} codec requires {expected.Name} but was given {actual.Name}. " +
            "Options of the wrong type are rejected rather than ignored, because silently " +
            "falling back to defaults would apply settings the caller did not choose.");
}
