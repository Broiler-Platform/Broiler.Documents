using System;
using System.Collections.Generic;
using Broiler.Documents.Model;
using Broiler.Documents.Pdf.Structure;

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
public sealed class PdfReadResult : DocumentReadResult
{
    public PdfReadResult(
        RichTextDocument document,
        DocumentResultStatus status,
        PdfDocumentMetadata metadata,
        PdfVersion declaredVersion,
        int pageCount,
        IReadOnlyList<PdfExtensionDeclaration> extensions,
        IEnumerable<DocumentDiagnostic>? diagnostics = null,
        DocumentConversionContext? resources = null)
        : base(document, diagnostics, status, resources)
    {
        Metadata = metadata ?? PdfDocumentMetadata.Empty;
        DeclaredVersion = declaredVersion;
        PageCount = pageCount;
        Extensions = extensions ?? Array.Empty<PdfExtensionDeclaration>();
    }

    /// <summary>The normalized metadata allowlist; never raw Info or XMP data.</summary>
    public PdfDocumentMetadata Metadata { get; }

    /// <summary>
    /// The version the file effectively declares, after the Catalog override. A
    /// 2.x value records what the file claims, not what this codec implements.
    /// </summary>
    public PdfVersion DeclaredVersion { get; }

    public int PageCount { get; }

    /// <summary>
    /// Developer extensions the Catalog declared. This is inventory for
    /// diagnostics; no declaration here ever enabled a feature.
    /// </summary>
    public IReadOnlyList<PdfExtensionDeclaration> Extensions { get; }
}

/// <summary>The outcome of writing a PDF.</summary>
public sealed class PdfWriteResult : DocumentWriteResult
{
    public PdfWriteResult(
        long bytesWritten,
        DocumentResultStatus status,
        DocumentDestinationState destinationState,
        int pageCount,
        IEnumerable<DocumentDiagnostic>? diagnostics = null)
        : base(bytesWritten, diagnostics, status, destinationState)
    {
        PageCount = pageCount;
    }

    public int PageCount { get; }
}
