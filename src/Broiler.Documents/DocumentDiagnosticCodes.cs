namespace Broiler.Documents;

/// <summary>
/// The format-neutral diagnostic codes every codec may emit. Format-specific
/// codes live with their codec (<c>PdfDiagnosticCodes</c>, the <c>rtf.*</c> and
/// <c>docx.*</c> families).
/// </summary>
/// <remarks>
/// Codes are API. A host branches on them, so a code is never renamed or reused
/// for a different condition; messages may change. No diagnostic carries document
/// text, a password, a metadata value, or a local path (ADR 0004).
/// </remarks>
public static class DocumentDiagnosticCodes
{
    /// <summary>
    /// The options handed to a codec were not the type it requires. This is a
    /// structured rejection rather than a silent downcast or a quiet fallback to
    /// defaults: a caller that passes DOCX options to the RTF codec has a bug,
    /// and hiding it would apply settings the caller never asked for.
    /// </summary>
    public const string OptionsInvalid = "document.options.invalid";

    /// <summary>
    /// A cross-format value was supplied twice — once on the request and once on
    /// the format options. There is no precedence rule to fall back on; exactly
    /// one owner is the contract.
    /// </summary>
    public const string OptionsConflict = "document.options.conflict";

    /// <summary>The source exceeded the byte ceiling for this read.</summary>
    public const string InputTooLarge = "document.input.too-large";

    /// <summary>The source could not be read at all.</summary>
    public const string InputUnreadable = "document.input.unreadable";

    /// <summary>The operation was cancelled at a checkpoint.</summary>
    public const string Cancelled = "document.operation.cancelled";

    /// <summary>
    /// A capability the caller asked for is not composed into this codec
    /// instance, so the construct it applies to was skipped rather than guessed.
    /// </summary>
    public const string CapabilityNotComposed = "document.capability.not-composed";

    /// <summary>Source metadata was dropped rather than carried into the result or output.</summary>
    public const string MetadataDropped = "document.metadata.dropped";

    /// <summary>A URI failed the active output policy and stayed inert source data.</summary>
    public const string UriRejected = "document.uri.rejected";

    /// <summary>Diagnostics were capped; the message carries only the suppressed count.</summary>
    public const string DiagnosticsTruncated = "document.diagnostics.truncated";
}
