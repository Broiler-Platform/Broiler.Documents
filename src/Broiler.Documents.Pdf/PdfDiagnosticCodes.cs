namespace Broiler.Documents.Pdf;

/// <summary>
/// The stable diagnostic codes the PDF codec emits. Codes are API: CLI exit
/// codes, host prompts, and the feature matrix all key off them, so a code is
/// never renamed or reused for a different condition. Messages may change.
/// </summary>
/// <remarks>
/// Diagnostics never carry document text, a password, a metadata value, or a
/// local path (ADR 0009 privacy rule). They name the construct and the reason.
/// </remarks>
public static class PdfDiagnosticCodes
{
    // ---- structure and syntax -------------------------------------------------

    /// <summary>The input does not begin with a usable <c>%PDF-</c> header.</summary>
    public const string HeaderMissing = "pdf.header.missing";

    /// <summary>A construct required to locate the cross-reference data is broken.</summary>
    public const string XrefMalformed = "pdf.xref.malformed";

    /// <summary>The file was recovered by scanning for objects because its xref was unusable.</summary>
    public const string XrefRecovered = "pdf.xref.recovered";

    /// <summary>An object could not be parsed and was treated as null.</summary>
    public const string ObjectMalformed = "pdf.object.malformed";

    /// <summary>An indirect reference does not resolve to an object.</summary>
    public const string ObjectMissing = "pdf.object.missing";

    /// <summary>A reference cycle was cut to keep resolution terminating.</summary>
    public const string ObjectCycle = "pdf.object.cycle";

    /// <summary>The document declares more revisions than were interpreted; history is not preserved.</summary>
    public const string RevisionsHistoryDropped = "pdf.revisions.history-dropped";

    /// <summary>The catalog or page tree is missing or unusable.</summary>
    public const string StructureMalformed = "pdf.structure.malformed";

    // ---- version and extensions ----------------------------------------------

    /// <summary>A PDF 2.x declaration was recognized as construct tolerance only, never as conformance.</summary>
    public const string VersionToleratedNotSupported = "pdf.version.tolerated-not-supported";

    /// <summary>A declared version is outside the approved feature matrix.</summary>
    public const string VersionUnsupported = "pdf.version.unsupported";

    /// <summary>A developer extension was inventoried but never enabled any behavior.</summary>
    public const string ExtensionUnsupported = "pdf.extension.unsupported";

    // ---- filters --------------------------------------------------------------

    /// <summary>A filter named by the document is not composed into this codec instance.</summary>
    public const string FilterNotComposed = "pdf.filter.not-composed";

    /// <summary>Filter input was structurally invalid.</summary>
    public const string FilterMalformed = "pdf.filter.malformed";

    /// <summary>A filter stage hit a byte, expansion, chain-depth, or work budget.</summary>
    public const string FilterLimit = "pdf.filter.limit";

    /// <summary>LZW is recognized but not implemented; it awaits its own IP review (IP-010).</summary>
    public const string FilterLzwUnsupported = "pdf.filter.lzw.unsupported";

    /// <summary>CCITT fax data is recognized but not implemented (IP-009).</summary>
    public const string FilterCcittUnsupported = "pdf.filter.ccitt.unsupported";

    /// <summary>JPEG (DCT) data is recognized but no cleared decoder is composed (IP-005).</summary>
    public const string FilterDctUnsupported = "pdf.image.dct.tuple-unsupported";

    /// <summary>JPEG 2000 data is recognized but not implemented (IP-007).</summary>
    public const string FilterJpxUnsupported = "pdf.filter.jpx.unsupported";

    /// <summary>JBIG2 data is recognized but not implemented (IP-008).</summary>
    public const string FilterJbig2Unsupported = "pdf.filter.jbig2.unsupported";

    /// <summary>A crypt filter was named; encrypted documents are rejected in this release.</summary>
    public const string FilterCryptUnsupported = "pdf.filter.crypt.unsupported";

    // ---- security -------------------------------------------------------------

    /// <summary>The document is encrypted; this release rejects it before interpreting content.</summary>
    public const string EncryptionUnsupported = "pdf.encryption.unsupported";

    /// <summary>Active content (JavaScript, Launch, embedded files, rich media) was found and never instantiated.</summary>
    public const string ActiveContentRemoved = "pdf.active-content.removed";

    /// <summary>A signature was found; it is neither validated nor preserved.</summary>
    public const string SignatureNotValidated = "pdf.signature.not-validated";

    /// <summary>An unapplied Redact annotation was found: an overlay is not deletion.</summary>
    public const string RedactionNotApplied = "pdf.redaction.not-applied";

    // ---- text, fonts and images ----------------------------------------------

    /// <summary>Reading order came from geometry rather than trustworthy logical information.</summary>
    public const string ReadingOrderHeuristic = "pdf.import.reading-order-heuristic";

    /// <summary>Character codes could not be mapped to Unicode with confidence.</summary>
    public const string TextMappingMissing = "pdf.text.mapping-missing-or-uncertain";

    /// <summary>Text was drawn in an invisible or clipping-only render mode; visibility is not judged.</summary>
    public const string TextVisibilityUncertain = "pdf.text.visibility-uncertain";

    /// <summary>A page carried no extractable text; it may be a scan needing OCR, which is out of scope.</summary>
    public const string TextOcrRequired = "pdf.text.ocr-required";

    /// <summary>An embedded font program was detected; no font-program reader is composed.</summary>
    public const string FontProgramNotComposed = "pdf.font.program-not-composed";

    /// <summary>A Type 3 font was detected; its glyph procedures are never executed.</summary>
    public const string FontType3Unsupported = "pdf.font.type3-unsupported";

    /// <summary>An image was detected but no image decoder capable of its filter is composed.</summary>
    public const string ImageNotComposed = "pdf.image.not-composed";

    /// <summary>An image's color space or sample layout is outside the supported subset.</summary>
    public const string ImageUnsupported = "pdf.image.unsupported";

    /// <summary>Vector artwork or a shading was found that the logical model cannot represent.</summary>
    public const string VectorArtworkDropped = "pdf.import.vector-artwork-dropped";

    // ---- limits and lifecycle -------------------------------------------------

    /// <summary>A PDF-specific limit was reached; the result is rejected rather than truncated.</summary>
    public const string Limit = "pdf.limit.exceeded";

    /// <summary>Diagnostics were capped; the message carries only the suppressed count.</summary>
    public const string DiagnosticsTruncated = "pdf.diagnostics.truncated";

    /// <summary>The operation was cancelled at a checkpoint.</summary>
    public const string Cancelled = "pdf.operation.cancelled";

    // ---- writer ---------------------------------------------------------------

    /// <summary>A model feature has no representation in the supported writer subset.</summary>
    public const string WriteFeatureUnsupported = "pdf.write.feature-unsupported";

    /// <summary>An inline image was dropped; no image emitter is composed.</summary>
    public const string WriteImageNotComposed = "pdf.write.image-not-composed";

    /// <summary>Line breaking used the built-in approximate metrics rather than real font metrics.</summary>
    public const string WriteMetricsApproximate = "pdf.write.metrics-approximate";

    /// <summary>A character is outside the writer's supported encoding and was replaced.</summary>
    public const string WriteCharacterUnsupported = "pdf.write.character-unsupported";

    /// <summary>Content overflowed the page box and was clipped to the next page or dropped.</summary>
    public const string WriteOverflow = "pdf.write.overflow";

    /// <summary>Output stopped after bytes had already reached a caller-owned stream.</summary>
    public const string WritePartialDestination = "pdf.write.partial-destination";

    // ---- shared policy codes --------------------------------------------------

    /// <summary>A URI failed the active-output policy and stayed inert source data.</summary>
    public const string UriRejected = "document.uri.rejected";

    /// <summary>Source metadata was dropped rather than carried into the result or output.</summary>
    public const string MetadataDropped = "document.metadata.dropped";

    /// <summary>Info and XMP disagreed on a normalized field; only the field name is reported.</summary>
    public const string MetadataConflict = "pdf.metadata.conflict";

    /// <summary>A raw XMP packet was detected and dropped; XMP awaits its own review (IP-004).</summary>
    public const string MetadataRawDropped = "document.metadata.raw-dropped";
}
