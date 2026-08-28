# Broiler.Documents Roadmap

**Status:** Active preview. The codec family, RTF/DOCX/ODT/HTML/Markdown support,
Formatting Codes projection, and package projects are implemented. Only current
residual work is tracked here.

## API contract cleanup

- `DocumentReadOptions.DecodeEmbeddedObjects` and `DocumentWriteOptions.AsciiOnly`
  no longer imply behavior that does not exist. Both are documented as what they
  actually are, and a codec asked for the unimplemented behavior reports
  `document.capability.not-composed` rather than quietly doing something else —
  the RTF reader escalates its embedded-object note to that code when, and only
  when, the caller asked for decoding *and* the document really contains a
  picture or object. Removing the members outright still waits on the
  repository's deprecation policy, and the bounded caller-composed image-import
  path is Phase 1 §6.2 work.
- Re-review ADR 0004 and the read/write option surface once the §6.2 resource
  context lands.
- Freeze public names and XML documentation after a consumer review.

## Format fidelity

- Decide whether RTF list-table detection and style-sheet interpretation belong
  in the next supported subset. They are currently deliberate, documented
  limitations rather than partially supported behavior.
- Consider HTML list writing and relative-link policy only with conformance
  fixtures and explicit diagnostics.
- Extend Markdown, DOCX, or ODT coverage only behind format-specific conformance
  tests; source-preserving round trips are not a goal for the normalized document
  model.

## ODT standards and rights

`Broiler.Documents.Odt` is implemented from the published OASIS specification and
embeds no third-party code, and ODF is standardized royalty-free (OASIS, and
ISO/IEC 26300) with patent covenants from its principal contributors. None of
that is a cleared rights position, and this component has no register saying it
is. Owned here:

- Extend the ADR 0011 claims discipline and the IP/licensing register beyond PDF
  so ODT has a rights row of its own, and get a legal reading on the ODF
  covenants rather than inferring one from the standard's licensing mode.
- Until that lands, the [ODT conformance document](odt-conformance.md) states the
  provenance and stops short of a rights claim, and no marketing copy may go
  further than it does.

Intentional limitations in the conformance documents are not release blockers
unless they are explicitly promoted into this roadmap.

## Native PDF support

PDF is an active, separately gated initiative. The detailed
[PDF support roadmap](pdf-support-roadmap.md) is authoritative for scope,
component ownership, phases, security, conformance claims, provenance, and legal
review. Phase 0 removed the obsolete standalone-process assumptions and created
the decision and evidence controls; it did not revive old parser code or grant
implementation clearance.

`Broiler.Documents.Pdf` now implements the base slice: PDF syntax and object
stores, cross-reference tables and streams, object streams, the Flate, ASCIIHex,
ASCII85 and RunLength filters, logical text import through encodings and
`ToUnicode` maps, links under the shared URI policy, and a deterministic PDF 1.7
writer over the standard font names. Every remaining technology — LZW, JPEG,
CCITT, JPEG 2000, JBIG2, embedded font programs, image extraction, encryption —
is detected and skipped with its own diagnostic and arrives by composing a
reviewed implementation into `PdfCodecServices`; see
[PDF extension points](pdf-extension-points.md).

Residual work owned here:

- The package stays `IsPackable=false` and unregistered until the roadmap's
  read-preview and write-preview gates pass. Implementation is not a capability
  claim, and no feature-matrix entry may reach `Supported` while its
  IP/licensing row is pending.
- The Phase 1 §6.1 contracts are built: `DocumentInput` (replayable probing over
  non-seekable sources, bounded memory-only materialization, explicit ownership),
  the read/write request envelopes, the shared result status and destination
  state, typed-option validation, async overloads, and one catalog
  selection-and-read path. The conversion and resource context (§6.2), the model
  unit review (§6.4), the Graphics font inspector and Media image services (§6.5),
  and `Broiler.Documents.Pagination` remain outstanding; the PDF writer paginates
  internally against a replaceable metrics provider until the shared paginator
  exists.
- Coverage-guided fuzzing and the pinned oracle, corpus and performance
  infrastructure remain outstanding; the current suite covers bounded truncation
  and mutation campaigns only.

## Stabilization

- Add sustained fuzz/property coverage and allocation/performance baselines for
  every parser and writer that accepts untrusted input.
- Validate package consumption from a feed without the aggregate repository.
- Complete dependency, license, API-compatibility, and human review before a
  stable release.

UI host integration, RichEdit clipboard wiring, and the Writer Formatting Codes
experience are owned by their UI/application layers rather than this component.
