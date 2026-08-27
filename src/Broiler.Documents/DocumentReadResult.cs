using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Broiler.Documents.Model;

namespace Broiler.Documents;

/// <summary>
/// The outcome of reading a document: a best-effort <see cref="RichTextDocument"/>,
/// a <see cref="Status"/> saying whether a host may use it, and any diagnostics.
/// Reads do not throw on malformed-but-recoverable input (ADR 0003/0004);
/// unsupported or skipped constructs surface as diagnostics.
/// </summary>
/// <remarks>
/// <para>
/// Open for derivation so a codec can return the same result through the shared
/// <see cref="DocumentCodec.Read(System.IO.Stream, DocumentReadOptions)"/> signature while adding format-specific detail
/// (<c>PdfReadResult</c> adds the page count, declared version, and normalized
/// metadata). Format-specific state never moves into this base until a second
/// consumer exists (PDF roadmap §3 containment rule).
/// </para>
/// <para>
/// <see cref="Status"/> is the load-bearing field, not <see cref="HasErrors"/>:
/// a document can carry warnings and still be complete, and it can carry none and
/// still be missing a page.
/// </para>
/// </remarks>
public class DocumentReadResult
{
    private static readonly ReadOnlyCollection<DocumentDiagnostic> EmptyDiagnostics =
        Array.AsReadOnly(Array.Empty<DocumentDiagnostic>());

    public DocumentReadResult(
        RichTextDocument document,
        IEnumerable<DocumentDiagnostic>? diagnostics = null,
        DocumentResultStatus status = DocumentResultStatus.Success)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Diagnostics = diagnostics is null
            ? EmptyDiagnostics
            : Array.AsReadOnly(diagnostics.ToArray());
        Status = status;
    }

    public RichTextDocument Document { get; }

    public IReadOnlyList<DocumentDiagnostic> Diagnostics { get; }

    /// <summary>Whether a host may use <see cref="Document"/>, and on what terms.</summary>
    public DocumentResultStatus Status { get; }

    /// <summary>True when a host may present the document at all.</summary>
    public bool IsUsable => Status != DocumentResultStatus.Rejected;

    public bool HasErrors => Diagnostics.Any(static d => d.Severity == DocumentDiagnosticSeverity.Error);

    /// <summary>
    /// Derives a status from diagnostics, for a read that <em>did</em> produce a
    /// document.
    /// </summary>
    /// <remarks>
    /// Anything above informational means content was skipped, clamped, or
    /// recovered, which is exactly what <see cref="DocumentResultStatus.Partial"/>
    /// tells a host to ask about before committing. It never returns
    /// <see cref="DocumentResultStatus.Rejected"/>: that is a statement about
    /// there being no usable document at all, which only the caller with the
    /// document in hand can make.
    /// </remarks>
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

    /// <summary>
    /// A rejection carrying one diagnostic and an empty document. Used for the
    /// failures that are decided before, or instead of, parsing — invalid
    /// options, an oversized source, cancellation.
    /// </summary>
    public static DocumentReadResult Rejected(string code, string message) =>
        new(
            RichTextDocument.Empty,
            [DocumentDiagnostic.Error(code, message)],
            DocumentResultStatus.Rejected);

    /// <summary>
    /// The rejection a codec returns when it was handed options of the wrong
    /// type. It names both types so the caller can see what it passed.
    /// </summary>
    public static DocumentReadResult InvalidOptions(string codecName, Type expected, Type actual) =>
        Rejected(
            DocumentDiagnosticCodes.OptionsInvalid,
            $"The {codecName} codec requires {expected.Name} but was given {actual.Name}. " +
            "Options of the wrong type are rejected rather than ignored, because silently " +
            "falling back to defaults would apply settings the caller did not choose.");
}
