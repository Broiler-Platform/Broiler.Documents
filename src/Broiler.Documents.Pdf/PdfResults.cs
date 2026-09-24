using System.Collections.Generic;
using Broiler.Documents.Model;
using Broiler.Documents.Pdf.Structure;
using Broiler.Documents.Resources;

namespace Broiler.Documents.Pdf;

/// <summary>
/// The outcome of reading a PDF: the extracted document plus what the reader
/// learned about the file.
/// </summary>
/// <remarks>
/// <see cref="DocumentReadResult.Status"/> is the load-bearing field.
/// <see cref="DocumentResultStatus.Rejected"/> means the document is a
/// placeholder that no host may present, and a rejected read never replaces an
/// open document or produces an output file. The status lives on the shared base
/// so a host that only knows about <see cref="DocumentReadResult"/> still sees
/// it — a PDF-local copy would shadow it and quietly report success.
/// </remarks>
public sealed class PdfReadResult(
    RichTextDocument document,
    DocumentResultStatus status,
    DocumentMetadata metadata,
    PdfVersion declaredVersion,
    int pageCount,
    IReadOnlyList<PdfExtensionDeclaration> extensions,
    IEnumerable<DocumentDiagnostic>? diagnostics = null,
    DocumentConversionContext? resources = null) : DocumentReadResult(document, diagnostics, status, resources, metadata)
{

    /// <summary>
    /// The version the file effectively declares, after the Catalog override. A
    /// 2.x value records what the file claims, not what this codec implements.
    /// </summary>
    public PdfVersion DeclaredVersion { get; } = declaredVersion;

    public int PageCount { get; } = pageCount;

    /// <summary>
    /// Developer extensions the Catalog declared. This is inventory for
    /// diagnostics; no declaration here ever enabled a feature.
    /// </summary>
    public IReadOnlyList<PdfExtensionDeclaration> Extensions { get; } = extensions ?? [];

    /// <summary>
    /// What the document was encrypted with and what opening it granted, or null
    /// for a document that is not encrypted or could not be opened.
    /// </summary>
    /// <remarks>
    /// Set on a rejection too when the rejection came after the document opened:
    /// a document whose permissions withhold extraction says so here, so a host
    /// can tell the user that its owner password is what would read it.
    /// </remarks>
    public PdfEncryptionInfo? Encryption { get; internal init; }
}

/// <summary>The outcome of writing a PDF.</summary>
public sealed class PdfWriteResult(
    long bytesWritten,
    DocumentResultStatus status,
    DocumentDestinationState destinationState,
    int pageCount,
    IEnumerable<DocumentDiagnostic>? diagnostics = null) : DocumentWriteResult(bytesWritten, diagnostics, status, destinationState)
{
    public int PageCount { get; } = pageCount;
}
