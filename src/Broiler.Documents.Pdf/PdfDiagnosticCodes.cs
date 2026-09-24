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
    /// A JPEG was not decoded, for one of three reasons the message separates: no
    /// DCT decoder is composed at all; one is and the frame's tuple falls outside
    /// what IP-005 clears — arithmetic coding, lossless, hierarchical and
    /// differential processes, 12-bit precision, four components, or a colour
    /// declaration the composed decoder cannot honour; or one is, the tuple is
    /// inside the row, and this read's allowance for describing images did not
    /// stretch to spending it. The code is the same because what the image would
    /// need decoded is the same; the work that would fix each is not, which is
    /// what the message is for.
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

    /// <summary>
    /// A stream names the <c>/Crypt</c> filter where there is nothing for it to
    /// select: in a document that is not encrypted, or anywhere in a chain but
    /// first. An encrypted document's leading crypt filter is applied by the
    /// security handler and never reaches the filter pipeline.
    /// </summary>
    public const string FilterCryptUnsupported = "pdf.filter.crypt.unsupported";

    // ---- security -------------------------------------------------------------

    /// <summary>
    /// The document is encrypted in a way this build does not open: a security
    /// handler, revision, or crypt-filter method outside IP-015 and IP-025, or
    /// an encryption dictionary that could not be read. It is rejected before
    /// any content is interpreted.
    /// </summary>
    public const string EncryptionUnsupported = "pdf.encryption.unsupported";

    /// <summary>
    /// The document is encrypted with a user password and the read supplied
    /// none. The empty password was tried and did not open it; a host prompts
    /// and reads again with <see cref="PdfReadOptions.WithCredentials"/>.
    /// </summary>
    public const string EncryptionPasswordRequired = "pdf.encryption.password-required";

    /// <summary>
    /// The password the read supplied is neither the document's user password
    /// nor its owner password. Nothing was decrypted.
    /// </summary>
    public const string EncryptionPasswordIncorrect = "pdf.encryption.password-incorrect";

    /// <summary>
    /// The document is encrypted for certificate recipients (the public-key
    /// security handler), and no <see cref="Security.IPdfRecipientDecryptor"/>
    /// is composed to open a recipient envelope.
    /// </summary>
    public const string EncryptionRecipientNotComposed = "pdf.encryption.recipient-not-composed";

    /// <summary>
    /// The composed recipient decryptor holds no key that opens any of the
    /// document's recipient envelopes.
    /// </summary>
    public const string EncryptionRecipientNotFound = "pdf.encryption.recipient-not-found";

    /// <summary>
    /// The document opened without owner authority, and its permissions withhold
    /// copying and extracting content. Every output of this codec is extracted
    /// content, so nothing is read; the owner password lifts the restriction.
    /// </summary>
    public const string EncryptionExtractionNotPermitted = "pdf.encryption.extraction-not-permitted";

    /// <summary>
    /// The document was encrypted and has been decrypted: which handler,
    /// revision, cipher and key length, what authority opened it, and what its
    /// permissions grant. The document read is not encrypted, so anything
    /// written from it is written in the clear.
    /// </summary>
    public const string EncryptionDecrypted = "pdf.encryption.decrypted";

    /// <summary>
    /// A string or stream could not be decrypted cleanly - a length the cipher
    /// cannot have produced, padding that does not check, or a crypt filter the
    /// document does not define - and was kept as far as it decrypted or dropped.
    /// </summary>
    public const string EncryptionObjectMalformed = "pdf.encryption.object-malformed";

    /// <summary>
    /// The permissions a revision 6 document states twice disagree: the
    /// encrypted <c>/Perms</c> entry does not match <c>/P</c>, or does not check
    /// at all. Only what both grant is honoured.
    /// </summary>
    public const string EncryptionPermissionsInconsistent = "pdf.encryption.permissions-inconsistent";

    /// <summary>
    /// The encrypted document declares strings or streams that are not
    /// encrypted - an <c>Identity</c> crypt filter - beyond the metadata
    /// exemption. Anyone can change that part without a key.
    /// </summary>
    public const string EncryptionPartiallyUnencrypted = "pdf.encryption.partially-unencrypted";

    /// <summary>
    /// A construct that would reach outside this document if anything executed it
    /// was found and never instantiated: JavaScript, Launch, GoToR, GoToE,
    /// SubmitForm, ImportData, Named, embedded files, rich media, a URI action an
    /// annotation that is not a link carries, and an action whose kind cannot be
    /// named. A plain GoTo is not one of these; it reports
    /// <see cref="LinkDestinationDropped"/>.
    /// </summary>
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

    /// <summary>
    /// Text was drawn turned against the page as it is displayed - sideways,
    /// upside down, mirrored, or at a slant - and was set on horizontal lines all
    /// the same, so its words may come back scattered or out of order. Raised as
    /// a skip: the text is in the document, and how it was drawn was not read.
    /// A page turned whole by its <c>/Rotate</c> entry is read the way it is
    /// displayed, and its text is not turned against it.
    /// </summary>
    public const string TextOrientationUnsupported = "pdf.text.orientation-unsupported";

    /// <summary>
    /// An embedded font program was detected, and the note says which of four
    /// things became of it: no reader composed to read it; a composed reader
    /// never offered it, because the font's own <c>ToUnicode</c> map already
    /// says what its codes mean; a reader offered it that recovered nothing; or
    /// a reader that read it.
    /// </summary>
    /// <remarks>
    /// The severity separates them into the two that cost the document text and
    /// the two that do not: the first and third are raised as skips and make the
    /// read partial, the second and fourth as information. The name is the one
    /// the first of the four was given, and a code is API - never renamed, never
    /// reused - so it has outlived the sentence that described it. The message
    /// and the severity carry which one happened.
    /// </remarks>
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

    /// <summary>
    /// A decoded image was refused by the caller's resource policy rather than by
    /// this build. Distinct from <see cref="ImageDecodedNotProjected"/>, which is
    /// this codec saying it could not carry the samples: this one is a decision
    /// someone made and can change, and the two are fixed by different work.
    /// </summary>
    public const string ImageExtractionDenied = "pdf.image.extraction-denied";

    /// <summary>Vector artwork or a shading was found that the logical model cannot represent.</summary>
    public const string VectorArtworkDropped = "pdf.import.vector-artwork-dropped";

    /// <summary>
    /// A grid of rules was read as a table and carried into the document. PDF
    /// draws a table as lines and text at coordinates and says nowhere that it
    /// is one, so this reports a reconstruction: the rules were complete enough
    /// to describe a grid, and the text inside it was arranged into the cells
    /// they bound. Only a fully ruled grid qualifies, and the cells' borders and
    /// shading come from the paths that were painted, not from a declaration.
    /// </summary>
    public const string TableReconstructed = "pdf.import.table-reconstructed";

    /// <summary>
    /// Content belonging to an optional-content group the document's own default
    /// configuration turns off was met. Distinct from
    /// <see cref="TextVisibilityUncertain"/>, which is a rendering question this
    /// release will not answer: this is a declaration the catalog makes about
    /// which layers form the default presentation, and the message says whether
    /// the content was omitted or kept.
    /// </summary>
    public const string OptionalContentOmitted = "pdf.import.optional-content-omitted";

    /// <summary>
    /// An annotation named a place inside the same document - a <c>/Dest</c>, or a
    /// GoTo action - and the logical model has no bookmark or anchor for it to
    /// land on, so the text was kept and no link was projected. Distinct from
    /// <see cref="UriRejected"/>, which is a policy refusing a URI it examined:
    /// nothing was refused here and no policy saw it. Distinct from
    /// <see cref="ActiveContentRemoved"/>, which is a construct that would reach
    /// outside the file: this one reaches nothing. The three are fixed by
    /// different work - configuring a policy, nothing at all, and a cross-format
    /// bookmark model that does not exist yet.
    /// </summary>
    public const string LinkDestinationDropped = "pdf.import.link-destination-dropped";

    /// <summary>
    /// The pages are not all one size. The model states one page for a whole
    /// document, so the size most pages share is the one stated, and the note
    /// names the others; their content is set on the stated page like the rest.
    /// Informational, as a DOCX of several sections is: every page's content was
    /// carried, and only the size of the page it was drawn on was not.
    /// </summary>
    public const string PageSizeMixed = "pdf.import.page-size-mixed";

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

    /// <summary>
    /// Text needed a font the caller did not provision, and this project bundles
    /// none. The actionable half of a dropped character: not "these letters
    /// cannot be written" but "nothing was configured to write them with".
    /// </summary>
    /// <remarks>
    /// PDF roadmap §11.3's preflight failure. It is separate from
    /// <see cref="WriteCharacterUnsupported"/> because the two are fixed by
    /// different work — one by provisioning a font, the other by a capability
    /// this build does not have — and a caller who cannot tell them apart cannot
    /// act on either.
    /// </remarks>
    public const string WriteNoFontConfigured = "pdf.write.no-font-configured";

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
