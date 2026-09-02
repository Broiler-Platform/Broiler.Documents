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

    /// <summary>
    /// LZW was named and no decoder for it is composed. Retained as API rather
    /// than emitted: IP-010 cleared and retired on 2026-09-01, and
    /// <c>LzwDecodeFilter</c> is built into every graph, so this build always
    /// composes one. A caller who replaces the built-in with a filter of the same
    /// name that declines keeps a code naming LZW specifically.
    /// </summary>
    public const string FilterLzwUnsupported = "pdf.filter.lzw.unsupported";

    /// <summary>
    /// CCITT fax data was found and no decoder for it is composed. IP-009 cleared
    /// and retired the patent position on 2026-09-01 and all three schemes decode,
    /// but through <c>Broiler.Documents.Pdf.Images</c>: a build that composes
    /// nothing still meets this code rather than samples.
    /// </summary>
    public const string FilterCcittUnsupported = "pdf.filter.ccitt.unsupported";

    /// <summary>
    /// A JPEG was not decoded, for one of two reasons the message separates: no
    /// DCT decoder is composed at all, or one is and the frame's tuple falls
    /// outside what IP-005 clears — arithmetic coding, lossless, hierarchical and
    /// differential processes, 12-bit precision, four components, or a colour
    /// declaration the composed decoder cannot honour.
    /// </summary>
    public const string FilterDctUnsupported = "pdf.image.dct.tuple-unsupported";

    /// <summary>
    /// Progressive DCT was recognized and not decoded. Retained as API rather
    /// than emitted: IP-005 was widened to cover progressive on 2026-09-02, so
    /// the filter shipped here no longer refuses it, and a caller who composes a
    /// stricter DCT filter of their own keeps a code that says progressive
    /// specifically instead of collapsing into the general tuple refusal.
    /// </summary>
    public const string FilterDctProgressiveUnsupported = "pdf.image.dct.progressive-unsupported";

    /// <summary>
    /// A JPEG's colour transform could not be established: its Adobe APP14 marker
    /// and its <c>/ColorTransform</c> parameter disagree, or the declared value is
    /// not one the format defines. Distinct from an unsupported tuple, which is a
    /// declaration this build understands and will not decode.
    /// </summary>
    public const string FilterDctColorTransformUncertain = "pdf.image.dct.color-transform-uncertain";

    /// <summary>
    /// JPEG 2000 data was not decoded. The message separates the three reasons,
    /// because they are fixed by different work: nothing is composed; or the
    /// composed reader found a Part 1 codestream, which IP-007 approved on
    /// 2026-09-01 and for which no entropy decoder is written; or it found Part 2
    /// extensions, which sit outside that row. Where the reader is composed the
    /// message carries the codestream's real tuple.
    /// </summary>
    public const string FilterJpxUnsupported = "pdf.filter.jpx.unsupported";

    /// <summary>
    /// JBIG2 data was not decoded. IP-008 approved the technology on 2026-09-01
    /// and the composed filter decodes generic regions coded with MMR; the
    /// arithmetic decoder, and the symbol, text, halftone and refinement regions
    /// that need it, are unwritten. The message names the segment types met, so a
    /// host can tell "nothing composed" from "composed and outside what it does".
    /// </summary>
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

    /// <summary>
    /// A composed filter decoded an image, and the logical model has nowhere to
    /// put it. The samples are reachable through the filter pipeline; the
    /// result document does not carry them, because extracting a resource into
    /// the model awaits the shared resource policy (PDF roadmap §6.2).
    /// </summary>
    public const string ImageDecodedNotProjected = "pdf.image.decoded-not-projected";

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

    /// <summary>
    /// An XMP packet was present but could not be read under the pinned subset —
    /// undecodable, malformed, or over the byte ceiling — so Info alone supplied
    /// the normalized metadata.
    /// </summary>
    public const string MetadataXmpUnusable = "pdf.metadata.xmp-unusable";

    /// <summary>
    /// An XMP packet was read for the normalized allowlist and the raw packet was
    /// then dropped. The packet itself is never preserved and never written back
    /// (PDF roadmap §6.2), so this reports provenance rather than a failure.
    /// </summary>
    public const string MetadataRawDropped = "document.metadata.raw-dropped";
}
