using System.Collections.Generic;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Documents.Resources;

namespace Broiler.Documents.Csv;

/// <summary>
/// The outcome of reading CSV: the extracted document plus the detected or
/// specified delimiter and text encoding.
/// </summary>
public class CsvReadResult(
    RichTextDocument document,
    char delimiter,
    Encoding? encoding = null,
    IEnumerable<DocumentDiagnostic>? diagnostics = null,
    DocumentResultStatus status = DocumentResultStatus.Success,
    DocumentConversionContext? resources = null,
    DocumentMetadata? metadata = null)
    : DocumentReadResult(document, diagnostics, status, resources, metadata)
{
    /// <summary>The field delimiter character used or detected during reading.</summary>
    public char Delimiter { get; } = delimiter;

    /// <summary>The text encoding used to decode the input, or null when not determined.</summary>
    public Encoding? Encoding { get; } = encoding;
}

/// <summary>
/// The outcome of reading TSV: the extracted document plus the encoding used.
/// </summary>
public sealed class TsvReadResult(
    RichTextDocument document,
    Encoding? encoding = null,
    IEnumerable<DocumentDiagnostic>? diagnostics = null,
    DocumentResultStatus status = DocumentResultStatus.Success,
    DocumentConversionContext? resources = null,
    DocumentMetadata? metadata = null)
    : CsvReadResult(document, '\t', encoding, diagnostics, status, resources, metadata);
