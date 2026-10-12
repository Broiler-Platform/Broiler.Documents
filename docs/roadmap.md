# Broiler.Documents Roadmap

This file lists **only work that is still open**. What the code already does is
described in [notes/](notes/), rights decisions are in [ip/](ip/), and the
reasoning behind fixed decisions is in [adr/](adr/README.md). When an item here
is done, delete it and describe the result in the matching note.

A limitation the notes call intentional is not a release blocker unless an item
here promotes it. Format coverage grows only behind conformance tests for that
format. Source-preserving round trips are not a goal for the normalized document
model.

## At a glance

| Area | State | Next step |
|---|---|---|
| RTF, DOCX, ODT, HTML, Markdown codecs | Preview packages; subsets documented in [notes/](notes/) | Decide the fidelity items below; freeze the API after a consumer review |
| Formatting Codes projection | Grammar version 1 published | None open in this repository |
| Rights records | Every RTF, DOCX, ODT, HTML and Markdown row decided; PDF codec rows decided | Close the pending PDF source rows and the IP-012 re-opening |
| PDF read | Base slice implemented; read-preview candidate in the Windows and Linux Writer heads and the CLI | Pass the Phase 5 exit gate |
| PDF write | Writer core for the standard-font subset; no host may save PDF | Phases 6 and 7 |
| Hardening | PDF fuzz harness (mutation-based) and test-control files exist, mostly empty | Oracles, corpus, coverage-guided fuzzing, performance thresholds |

## API and document model

- **Remove `DocumentReadOptions.DecodeEmbeddedObjects`.** It is announced for
  removal under [ADR 0014](adr/0014-api-deprecation-and-removal.md) and still
  works. Take it out in a later release, together with the test that covers the
  RTF reader's escalation to `document.capability.not-composed`.
  `DocumentWriteOptions.AsciiOnly` stays: it is narrow, not superseded, so the
  ADR's no-replacement rule keeps it.
- **Re-review ADR 0004 and the read/write option surface** now that the resource
  context, metadata envelope and font set exist. ADR 0004's own status asks for
  this before the API freeze.
- **Freeze public names and XML documentation** after a consumer review.
- **Spooling `DocumentInput`.** The type is memory-only on purpose. A
  spooling-capable input needs a policy a host supplies: directory, quota,
  permissions, cleanup, crash recovery and privacy. Nothing ambient.
- **Write commit policy.** `DocumentWriteRequest` carries document, destination,
  options and cancellation. It has no staging or `RequireAtomic` member yet. The
  PDF writer integration needs one (see [Phase 7](#writer-and-output-integration-phase-7)).

## Format fidelity

Each item needs conformance fixtures and explicit diagnostics before it lands.

- **RTF lists and style sheets.** Decide whether list-table detection and
  style-sheet interpretation join the supported subset. Today both are skipped
  destinations ([notes/rtf.md](notes/rtf.md)).
- **HTML list writing.** The writer keeps indentation and reports `html.list`
  instead of emitting list structure.
- **HTML relative links.** Every relative target is refused today. Decide on a
  policy first.
- **HTML tables and descendant selectors.** Tables are flattened to their cell
  paragraphs, and LibreOffice's `td p { … }` rules are not matched because only
  bare type selectors are. The office suite's `table-simple` seed measures the
  gap (ink box 107 px against DOCX's 11). Weigh this first if the CSS boundary in
  [notes/html.md](notes/html.md) ever moves.
- **HTML page header convention.** LibreOffice writes a page header as
  `<div title="header">`, and the reader treats it as body content. Decide
  whether to map it to `RunningContent`.
- **Pictures from HTML and Markdown.** Neither reader builds a model image:
  `<img>` is skipped and `![description](src)` becomes its description. Building
  them needs a decision on what a reader may do with a `data:` payload. That is a
  resource-policy question, not a parsing one.
- **Nested list markers through RTF.** In the office suite, LibreOffice's text
  projection and `broilerdoc dump` disagree on the marker of a nested level
  (`◦` against `•`). Find out which side is right.

## Rights and provenance

The standard of review and the state of every register are summarized in
[ip/README.md](ip/README.md). Open items:

- **Pending source rows** in [ip/pdf-approved-sources.md](ip/pdf-approved-sources.md).
  Each is a transcribed table that could not be authored from an underlying fact.
  No capability derived from it may be claimed as supported until it is decided.
  The fax, JBIG2 and JPEG 2000 decoders and their tables now live in
  `Broiler.Media.Image.Managed`; the rows stay here because this codec composes
  them.
  - SRC-017: ITU-T T.4/T.6 fax code tables. The evidence does not settle it, and
    this is the row where counsel would be worth the cost. SRC-018 and SRC-019
    defer to its answer. Brief: [ip/briefs/src-017-normative-tables.md](ip/briefs/src-017-normative-tables.md).
  - SRC-025: ISO 32000 password padding and security-handler markers. Gates any
    claim about encrypted documents.
  - SRC-016: the 391 CFF standard strings (different publisher, different terms;
    brief §5b).
  - SRC-018: T.800 EBCOT context tables.
  - SRC-019: T.88 MQ probability table.
  - SRC-024 (RC4) and SRC-026 (RFC 3454 table B.1) are pending review.
- **IP-012 re-opening for font embedding**, requested 2026-09-03. No code writes
  a font program into a PDF until it moves, which blocks the writer's non-Latin
  path. Brief: [ip/briefs/ip-012-font-embedding.md](ip/briefs/ip-012-font-embedding.md).
- **Similarity reviews marked "Pending review"** in the approved-sources log: the
  bare-CFF reader, the JBIG2 arithmetic decoders, the JPEG 2000 decoder, masks as
  alpha, the ICC reader, encryption ordering, the standard security handler and
  the public-key handler.
- **IP-025 external standards.** CMS, X.509 and RSA were not reviewed. This binds
  whoever composes a recipient decryptor, not the codec.
- **IP-003 (Adobe extensions)** stays pending. No extension is in scope until it
  is registered and approved.
- **Adopt ADR 0013.** It is still *Proposed*, although every register already
  runs under it. Adopting it also settles ADR 0011, which is *Proposed* too and
  which 0013 supersedes on the standard of review.
- **Repository-history redistribution audit.** Document authority for restricted
  historical material, or remove or rewrite it under the project's history policy.
- **Clause column of the construct inventory.** Verify it against the licensed
  ISO 32000-1 text. Until then it is a best-effort pointer, not evidence.
- **Not blocking under the register standard, still unrecorded:** implementation
  jurisdictions and expiry/review dates on every row; the succession of Sun's ODF
  covenant.
- **Obligations that wake up later.** A UCD-derived table needs the Unicode
  licence notice (IP-013). Reading the ICC `outputResponseTag` or bundling a
  profile re-opens IP-024. Shipping third-party or derived material needs
  component-local `THIRD_PARTY_NOTICES.md` and SBOM entries.

## PDF

The PDF codec is a separately gated initiative. What exists is in
[notes/pdf/](notes/pdf/README.md). Phases follow the original plan: 0 governance,
1 shared contracts, 2 syntax, 3 structure and filters, 4 logical import,
5 read preview, 6 export foundation, 7 writer, 8 hardening. A later phase cannot
waive an earlier gate.

### PDF delivery gates

| Gate | State |
|---|---|
| Packages | `Broiler.Documents.Pdf`, `.Pdf.Fonts` and `.Pdf.Images` ship as preview packages. Packaging is not application readiness and not a support claim. `PdfDeliveryGuardTests` guards packability, and that the base codec takes no third-party or non-Documents reference. |
| Read preview (Phase 5) | **Candidate.** The Windows and Linux Writer heads register the codec for opening only, and the CLI composes it read-only. Guards keep every other composition root closed and stop any head from registering it for saving. The capability is not published until the Phase 5 exit gate passes. |
| Write preview (Phase 7) | **Closed.** `CanWrite` is off in every host and no CLI destination or save filter accepts PDF. A test-only candidate may enable them once writer-core readiness passes. Nothing is published before the full Phase 7 gate passes. |
| Stable release (Phase 8) | **Closed.** No non-prerelease package or unrestricted feed until the security, performance, package, IP and enabled-application gates pass for the exact packed commit. |
| Support claims | **None.** No feature-matrix entry may become `Supported` while a register row it rests on is pending or its phase gate is open. A host that omits an optional service reports a narrower matrix. |

### PDF phase status

| Phase | State | Remaining estimate (engineer-weeks) |
|---|---|---:|
| 0 Governance | Repository work done; approvals listed under [Rights and provenance](#rights-and-provenance) | — |
| 1 Shared contracts | Contracts, resource context, metadata envelope, model and Media/Graphics prerequisites built; test foundation open (below) | see below |
| 2 Syntax and object store | Implemented | — |
| 3 Structure, filters, security | Implemented | — |
| 4 Logical import | Implemented within the approved subsets; gaps below | — |
| 5 Read preview | Candidate landed; exit gate open | 3–5 |
| 6 Export foundation | Not started | 10–16 |
| 7 Writer integration | Writer core for standard fonts; integration not started | 7–12 |
| 8 Hardening and release | Not started | 6–10 |

Estimates assume one experienced contributor and exclude waiting time for
standards access, counsel, patent research or licence negotiation.

### Read preview (Phase 5)

Open deliverables:

- **BrowserWriterDemo** moves to `SelectAndRead` over a `DocumentInput`. No
  unbounded `ReadAllBytes`, no duplicate probing, and no base64 decode before
  limits apply. The Writer desktop heads and the CLI already read this way.
- **Conversion context in the Writer.** Keep `DocumentConversionContext` beside
  the editor document. New clears it. Open replaces it atomically. Edits keep and
  prune resource ids. Inserted resources get caller-approved entries. Paste or
  merge across documents rekeys them and asks again. Save hands it to the writer.
- **Partial reads need consent.** Today the Writer commits a partial read and
  reports it in the status line. It must ask before a partial read replaces the
  open document. In the CLI a partial read currently exits 0 unless `--fail-on`
  is set; give it a distinct exit status or an explicit opt-in. Default
  conversion keeps omitting source metadata and resources without output
  permission.
- **Cancellation** reaches probing and materialization in the hosts.
- **Composed JPEG.** CLI and desktop compose the reviewed managed JPEG service.
  Today the CLI composes only the ICC reader.
- **Support wording.** Publish a read-preview statement tied to the feature
  matrix and the exact ISO 32000-1 subset. Describe PDF 2.x handling as
  declaration tolerance only. Pass the claims and trademark review: no Adobe
  icon, no certification marks, no reference-tool names that imply endorsement.

Exit gate:

- Oversized seekable, non-seekable and base64 inputs are rejected before full
  materialization, and probing and reading see identical bytes.
- Two concurrent conversions with different services, policies and limits do not
  affect each other. Batch conversion is reentrant.
- PDF is present only in explicitly enabled heads. Android and WebAssembly cannot
  acquire it transitively; WebAssembly is enabled only after AOT and
  bounded-memory evidence.
- Open, edit and save keep the metadata and resource context. A rejected input
  leaves the active document and destination unchanged.
- The hostile-input gate covers catalog selection, stream replay,
  materialization, base64, cancellation and commit, with no unresolved crash,
  hang, limit escape, excessive allocation or cross-document leak.
- The read, materialization, host-memory and platform thresholds in
  `tests/pdf/performance-baseline.json` pass on the pinned runner for the exact
  candidate.

### Test, corpus and oracle foundation (Phase 1 remainder)

The control files exist with schemas and guards, so content has somewhere to
land. What is missing needs decisions more than code.

- **Oracles.** Select, licence-review, acquire, hash-verify and pin structural,
  extraction and rendering oracles in `tests/pdf/tools/manifest.json` (no rows
  yet). Each needs an exact command, sandbox, timeout, memory and CPU cap,
  permitted exit codes, comparison format and retained artifacts. qpdf is a
  structural oracle, never the sole writer validator. Oracle disagreement creates
  an adjudication record and never silently rewrites a golden.
- **Corpus.** Populate [ip/pdf-corpus-manifest.json](ip/pdf-corpus-manifest.json)
  (empty). Every artifact needs a rights decision first. A document-level licence
  does not clear the fonts, images, profiles or personal data inside it. Keep
  real-world and CVE corpora access-controlled and quarantined, fetched only by a
  pinned nightly job.
- **Coverage-guided fuzzing.** The campaign is mutation-based. An instrumenting
  engine is a tool that needs a manifest row and a licence review before CI may
  run it. The nightly fuzz job stays non-required until then. Extend the
  harnesses beyond the read and probe surfaces to CMaps, fonts, images and the
  writer.
- **Performance thresholds.** `tests/pdf/performance-baseline.json` has its
  schema, pinned runner and method but no scenarios. Read and host thresholds
  must be approved and pass before the Phase 5 exit, and writer thresholds before
  Phase 7.
- **Writer conformance checklist.** The emitted half of all eleven rows is filled
  in. The clause half (ISO clause, required and prohibited keys, value and version
  constraints, evidence) is pending on every row.
- **Media JPEG provenance.** Audit the `Broiler.Media.Image.Managed` decoder and
  encoder before `DCTDecode` may be marked supported or that Media release is
  approved: Annex-derived tables, IJG quality scaling, the libjpeg
  `jpeg_gen_optimal_table` lineage of `JpegOptimalHuffman`, test-vector
  provenance and notice duties.
- **Review gate.** Add `Broiler.Documents` and the affected Media and Graphics
  packages to the human-review and publish-approval gate. Add trimming and AOT
  checks for every head that enables PDF.

### Import gaps (Phase 4 remainder)

Implemented import is described in [notes/pdf/README.md](notes/pdf/README.md).
Still open:

- **Unfinished composed decoders** (in `Broiler.Media.Image.Managed`, wrapped
  by `Broiler.Documents.Pdf.Images`). JPEG 2000 decodes one tile, default
  precincts and LRCP/RPCL only. JBIG2 lacks halftone regions, aggregate symbol
  coding, intermediate regions and every Huffman-coded form, and it refuses a
  whole page if any segment is unsupported. **Neither has decoded a real image**,
  and no conforming codestream may be committed to change that.
- **Fonts.** Type 1 programs need inspection surface in `Broiler.Graphics`. A
  CID-keyed CFF needs its collection's CMap. Predefined CMaps other than
  `Identity-H` are skipped. MacExpert and ZapfDingbats built-in encodings are
  absent.
- **Images.** Inline images are inventoried, not decoded. Byte-preserving DCT
  (`PreserveEncodedBytes`) is not implemented. It needs identity `/Decode`,
  agreeing colour declarations, no mask, profile or conversion, and a separate
  permission.
- **Optional content.** `/VE`, `/Configs` and `/AS` are not evaluated. The
  content they govern is kept and reported.
- **Internal destinations** wait for a cross-format bookmark and anchor model.
- **Exit evidence.** Text goldens for the declared subset, and agreement with
  independent tools on page counts, boxes, rotation, inherited resources and
  every cross-reference form. These need the oracles and corpus above.

### Shared export foundation (Phase 6)

Not started. It may run in parallel with Phase 5. The font *path* is decided:
callers supply fonts and nothing is bundled
([ip/briefs/font-path.md](ip/briefs/font-path.md)). The writer meanwhile
paginates internally against a replaceable metrics provider.

- **`Broiler.Documents.Pagination`.** A headless, UI-free paginator in points:
  page size, orientation, margins, spacing, wrapping, lists, indentation,
  alignment, page breaks, inline images, decorations, link rectangles and
  overflow diagnostics. It produces an immutable `PaginatedDocument` in which
  fonts, shaping, line breaking, pagination and image placement are resolved
  exactly once. Define orphan and widow handling, unbreakable runs, list-marker
  width, source mapping and resource lifetime. RichEdit print and preview become
  its second consumer.
- **Script baseline.** Horizontal left-to-right Latin, Greek and Cyrillic, with a
  tested combining and ligature subset. Everything else fails writer preflight
  instead of being emitted with nominal glyph order.
- **`Broiler.Graphics` export services.** An instance font resolver and metrics
  service; immutable font-face resources; shaped glyph runs (glyph ids,
  advances, offsets, clusters, direction, source Unicode); backend-neutral page
  primitives; TrueType `glyf` subsetting that strips hinting bytecode and
  re-validates the result; and `OS/2.fsType` enforcement.
- **Font policy in hosts.** The CLI and every Writer head implement the
  configured-font preflight and its failure UX. Every font needs a licence
  disposition covering embedding, subsetting, redistribution, commercial use and
  generated-document obligations. The policy fails closed on restricted,
  invalid, ambiguous or unknown fonts, and never substitutes an ambient OS font
  or treats a font extracted from an input as export authority.
- **Exit gate.** Pagination goldens; RichEdit and PDF consume identical line,
  glyph and page geometry; geometry is identical at different host DPIs;
  font-policy tests for every `fsType` state; every bundled export asset has a
  licence record, notice and package test; pagination references no UI, DOM,
  HTML or platform type.

### Writer and output integration (Phase 7)

The writer core emits PDF 1.7 over the standard font names with WinAnsi, and its
output is byte-identical across runs. Integration has not started.

- **Options and preflight.** Finalize `PdfWriteOptions` with PDF-only values.
  Paginate and preflight fonts, images, links, metadata, permissions, obligations
  and limits before the first destination byte, consuming `PaginatedDocument`
  without re-measuring.
- **Unicode output.** Type 0 fonts, subsets, widths and `ToUnicode` maps. Blocked
  on Phase 6 and the IP-012 re-opening.
- **Images.** Inline raster images, newly encoded with Flate. Byte-preserving DCT
  only where the resource policy allows it, with the bounded inspection re-run
  immediately before embedding. No JPEG encoder and no transcoding.
- **Commit semantics.** File hosts stage in the destination directory and replace
  atomically. Browser downloads are offered only for a complete buffer. A
  caller-owned stream reports `NotStarted` or `PartialDestination` truthfully.
  `RequireAtomic` without a staging capability rejects before output.
  Non-seekable output tracks offsets without seeking.
- **Clause traceability.** Complete the clause matrix for every emitted
  construct. Generate the support statement from the feature matrix. Reopening a
  file in Broiler, qpdf or a viewer is necessary evidence, never sufficient.
- **Host enablement.** After writer-core readiness, a test candidate may enable
  `CanWrite`, CLI PDF destinations and Writer save filters, all routed through
  catalog-selected `Write`. Android and WebAssembly output stay disabled until
  their own gates pass.
- **Out of V1:** incremental save, linearization, encryption and raster-page
  fallbacks.
- **Exit gate.** Independent structural validation; files open in two pinned
  independent reader lineages; extracted text matches under a pinned extraction
  oracle; renders from two independent renderers meet tolerance; byte-identical
  output; writer thresholds pass; bounded writer fuzz and property campaigns are
  clean; clean-package end-to-end tests for CLI and desktop Save As; every
  rejection before commit emits zero bytes; direct-stream cancellation before and
  after the first byte; reader and writer are not each other's only oracle.

### Hardening and stable release (Phase 8)

- **Verification.** At least 24 aggregate CPU-hours of coverage-guided fuzzing on
  the release commit, under an outer wall-time and RSS supervisor. Maintain a
  malicious corpus: cycles, huge lengths, deep nesting, xref loops, many small
  objects, decompression, font and pixel bombs, page floods, hostile XMP, link
  and action payloads. Add differential text and page-geometry checks.
- **Oracles' licence paths.** qpdf as the Apache-2.0 structural oracle. Two
  independent renderers: PDFium needs a full SBOM for the exact build; Poppler
  stays a separate GPL tool, never linked or bundled; MuPDF only under one
  reviewer-approved AGPL or commercial path. veraPDF, if used, under its MPL-2.0
  option. Goldens are derivatives of the corpus and need manifest rights.
- **Performance.** A `Broiler.Documents.Pdf.Benchmarks` project with versioned
  scenarios in `tests/pdf/performance-baseline.json`: one-page and multi-page
  text, large xref sets, revisions, image- and font-heavy inputs, extraction,
  writing and parallel documents. Five measured repetitions on the pinned runner,
  median plus absolute ceilings, and a soak test proving caches return to zero.
- **Release flow.** Build and test the release commit, pack and sign each
  candidate once, record its hash, inspect `.nupkg`, `.snupkg` and application
  contents, consume the exact artifacts from a clean feed, and publish the
  identical bytes. No override or alternate feed bypasses this.
- **Legal and claims.** Refresh the register against current ISO and ITU
  declarations. Confirm every enabled capability is within the Adobe ISO 32000-1
  licence or has separate authority. Use worldwide clearance or an enforced
  territory-limited channel. Produce SBOMs and component notices. Audit every
  standard-derived constant, golden, font, mapping table and profile. Have the
  reviewer reapprove the XMP, JPEG/APP14, font, Unicode, URI and PDF rows. The
  claims review rejects patent-free, royalty-free, certified, full-conformance or
  endorsement wording.
- **Conformance document.** Write a clause-level conformance document for emitted
  files (planned as `notes/pdf/conformance.md`). It distinguishes syntax,
  interoperability, supported-feature and conforming-file evidence, and makes no
  PDF/A, PDF/UA, accessibility, archival, signature, encryption or sanitization
  claim.
- **Architecture proof.** PDF references `Broiler.Media.Image` but not its
  managed implementation; Graphics and Media do not reference Documents;
  pagination and PDF use no `BTextMeasurer`, `BImageCodecs`, installed-font
  discovery or backend handle; every catalog is built at its composition root.
  The platform matrix records Windows, Linux, Android and WebAssembly read and
  write states independently.

### Later PDF tracks

Each needs its own approval and does not block V1.

1. **Encrypted output.** AES needs random IVs where the writer promises
   byte-identical output. Revisions 2–4 need the file identifier before the body
   it is now derived from. AES-256 needs a PDF 2.0 header or an extension
   declaration.
2. **Tagged PDF, outlines, internal destinations and accessibility.** Pin the
   structure-tree, marked-content, language, alt-text, list, table, artifact and
   PDF/UA scope before any claim, with assistive-technology review.
3. **PDF/A and PDF/UA profiles** on exact editions, with lawful standards access,
   independent validation and a marketing review.
4. **AcroForm reading and attachment extraction** under an explicit security and
   user-content policy.
5. **Signature inspection and validation** under a trust-store and revocation
   policy, keeping mathematical integrity apart from identity and legal validity.
6. **CMYK/YCCK JPEG and colour management in Media.** Move the ICC reader from
   `Broiler.Documents.Pdf.Images` into `Broiler.Media`, recheck T.81 and the
   APP14 rules for four components, and license any bundled profile separately.
7. **A JPEG encoder for the writer**, only under a separate Media roadmap with
   exact T.81 modes, provenance, patent review and notices.
8. **Native page rendering** in a satellite such as
   `Broiler.Documents.Pdf.Rendering`: paths, fill rules, clipping, gradients,
   patterns, transparency groups, soft masks, blend modes and faithful affine
   image replay.
9. **HTML/CSS print-to-PDF** through paged layout into shared Graphics pages,
   never DOM code inside the codec.
10. **OCR** through an explicitly composed external service, after provider,
    privacy, retention and transfer review. Today a page without text reports
    `pdf.text.ocr-required`.
11. **Complex scripts, RTL, vertical text, CFF/CFF2 output, variable and colour
    fonts, emoji.** Each one amends the script and font matrix and passes the
    Graphics and pagination gates first.

## Test suites and tooling

- **Classify the open office-baseline rows.** Seven render rows in
  `tests/office/office-baseline.json` are still `suspected-defect`: three for
  `letterhead-frames`, four for `line-break-within-paragraph`. Their notes say the
  defects behind them are fixed and what remains is ordinary disagreement between
  two layout engines. Each needs a reviewer's `documented` or `accepted` decision,
  or a fix.
- **Long-document line height.** The office suite's `long-flow-multipage` seed
  reaches `severe` on the blurred difference while coverage stays `close`: a
  small per-line leading difference accumulates over pages. This is in the CLI's
  layout engine.
- **Foreign documents.** `tests/corpus/external-sources.json` has no rows, so the
  corpus suite never reads a document written outside this project. Each source
  needs an approved row with licence, permitted use, reviewer and pinned hash.
- **Sustained fuzzing for every untrusted-input parser.** Only PDF has a fuzz
  harness. RTF, DOCX, ODT, HTML and Markdown need property or fuzz coverage, plus
  allocation and performance baselines.

## Stable release

Before anything leaves preview, component-wide:

- dependency, licence, API-compatibility, security and human review;
- the fuzz, performance and PDF Phase 8 evidence above;
- an updated preview notice in the [README](../README.md), which today says that
  no fuzzing campaign, dependency scan or security audit is recorded.

## Owned elsewhere

UI host integration, RichEdit clipboard wiring and the Writer's Formatting Codes
experience belong to their UI and application repositories. The Writer-side PDF
items above are listed here because this component's gates govern them.
