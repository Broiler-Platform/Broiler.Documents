# ADR 0003: Codec Contract And Signature Probe

**Status:** Accepted for Phase 1 scaffolding
**Date:** 2026-07-05

## Context

`Broiler.Documents` must support multiple interchange formats (RTF first, then
HTML/Markdown/DOCX) behind one contract so that consumers pick a codec by format
descriptor or by content sniffing, not by hard-coding a parser. `Broiler.Media`
already proves this shape: `MediaCodec` + `MediaCodecCatalog` +
`MediaFormatDescriptor`, with `SelectAsync` reading a bounded byte prefix and
choosing the highest-confidence match. Documents should mirror it, adapted from
"decode to buffers" to "decode to a `RichTextDocument`."

Phase 0 must fix the codec contract, the catalog/registration model, the probe
mechanism, and the read/write result shape — without committing implementation.

## Decision

- **Codec contract.** An `IDocumentCodec` (or abstract `DocumentCodec`) carries a
  `DocumentFormatDescriptor` and exposes capability flags (`CanRead`/`CanWrite`),
  a `Probe` method, and `Read`/`Write` methods:

  ```text
  DocumentFormatDescriptor  Descriptor        // name + MIME types + file extensions
  bool                      CanRead, CanWrite
  DocumentProbeResult       Probe(DocumentProbeRequest)          // confidence from a bounded prefix
  DocumentReadResult        Read(stream/bytes, DocumentReadOptions)
  DocumentWriteResult       Write(RichTextDocument, DocumentWriteOptions)
  ```

  RTF's descriptor is `name = "RTF"`, MIME `application/rtf` and `text/rtf`,
  extension `.rtf`.

- **Catalog and explicit registration.** A `DocumentCodecCatalog` is constructed
  from an explicit set of codecs (as `MediaCodecCatalog` is), rejects duplicate
  format ids, and offers `FindByExtension`, `FindByMimeType`, and a
  confidence-ranked `Select(...)` over a bounded content prefix. There is **no**
  hidden global registry and **no** module-initializer registration (ADR 0001):
  the composing application decides which codecs are present.

- **Signature probe.** `Select` reads at most `DocumentLimits.MaxProbeBytes` from
  the input, hands each codec a `DocumentProbeRequest` (prefix + optional hints
  such as a filename/MIME), and returns the highest-confidence match. RTF probes
  the `{\rtf` prefix. Probing restores the stream position for seekable streams
  and never consumes the whole input.

- **Result shape is total, not exceptional.** `Read`/`Write` return a result
  object (`DocumentReadResult { RichTextDocument Document; IReadOnlyList<DocumentDiagnostic> Diagnostics; }`
  and the write analogue) rather than throwing on malformed-but-recoverable
  input. Unsupported or skipped constructs surface as diagnostics; hard I/O or
  limit-exceeded conditions are the only failure paths. This mirrors the
  best-effort philosophy in ADR 0004.

- **Options are format-neutral at the base.** `DocumentReadOptions` and
  `DocumentWriteOptions` carry the shared knobs (limits, default code page,
  whether to attempt embedded-object decode — off by default per ADR 0004);
  format-specific options derive from these. Streaming/large-input handling and
  `CancellationToken` acceptance follow the Media precedent (Media ADR 0002).

- **Sync-first, async-capable.** Unlike media, documents are typically small and
  fully materialized; the first contract is synchronous `Read`/`Write` over a
  stream or `ReadOnlySpan<byte>`/`string`. An async surface may be added later
  without breaking the sync one.

## Consequences

- A second codec (HTML) drops into the same catalog with no catalog changes,
  proving the contract generalizes (roadmap Phase 6 exit gate).
- "Convert RTF → HTML" is codec composition at the catalog level (RTF `Read` →
  model → HTML `Write`), not special-case code, and needs no DOM dependency in
  the RTF codec.
- Architecture tests assert the catalog has no static mutable registry and that
  codecs are only reachable through an explicitly constructed catalog.
- This ADR satisfies the Phase 0 exit-gate item requiring the codec contract and
  probe to be fixed before Phase 1 scaffolding.
