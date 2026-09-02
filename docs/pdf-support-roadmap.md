# PDF Support Roadmap for Broiler.Documents

- **Status:** Base slice implemented; preview and release gates outstanding
- **Component:** `Broiler.Documents`
- **Target package:** `Broiler.Documents.Pdf`
- **IP/legal review baseline:** 2026-08-11
- **Architecture, security, delivery, and legal-scope review:** 2026-08-22
- **Base implementation landed:** 2026-08-25 (see
  [§2.5 Current implementation state](#25-current-implementation-state))

The IP and licensing requirements below are engineering release controls, not a
legal opinion. Patent freedom-to-operate, reciprocal-license decisions, and
target-jurisdiction questions require approval by the project's qualified legal
reviewer before the affected feature ships.

## 1. Recommended end state

Create `Broiler.Documents.Pdf`, matching the existing `.Docx`, `.Rtf`, and
`.Html` naming, with:

- `PdfDocumentCodec.Read`: best-effort logical extraction into
  `RichTextDocument`;
- `PdfDocumentCodec.Write`: deterministic pagination and PDF export from
  `RichTextDocument`;
- descriptor name `PDF`, MIME type `application/pdf`, and extension `.pdf`;
- explicit registration in `DocumentCodecCatalog`; and
- no third-party runtime dependency, global registry, module initializer, UI
  dependency, DOM dependency, or platform-specific dependency.

PDF import and export are separate capability tracks. PDF is fixed-layout,
while `RichTextDocument` is a normalized paragraph/run model. V1 must therefore
not promise source-faithful round trips or "preserve layout" conversion.

### 1.1 V1 scope

Include:

- feature-based reading of the declared ISO 32000-1:2008/PDF 1.7 subset,
  including older PDF 1.x files that use those constructs;
- recognition of PDF 2.x headers, Catalog `/Version` overrides, and Catalog
  `/Extensions` declarations while processing only explicitly supported ISO
  32000-1:2008 constructs; declarations are inventory and diagnostics, never
  feature enablement, this is not ISO 32000-2 conformance, and V1 implements no
  PDF 2.0-only feature or developer extension;
- logical text, basic styling, links admitted by the shared URI policy, metadata,
  and safely placeable inline images;
- new PDF 1.7 output for broad compatibility, subject to a qualified review that
  the planned reader/writer falls within Adobe's ISO 32000-1 public patent
  license definitions or has separate authority, and to the project's exact
  file-validity and supported-feature statements; and
- Unicode storage and copy/paste mappings for the explicitly declared V1 script
  and shaping matrix, embedded/subset fonts, approved links, inline images,
  lists, paragraph formatting, deterministic pagination, and structured
  diagnostics. This is not a claim of universal-script shaping support.

Explicitly exclude from V1:

- native PDF viewing or page rasterization;
- source-preserving editing or incremental saving;
- OCR;
- password-encrypted input beyond detection and diagnosis;
- JavaScript, Launch actions, attachments, rich media, or external-resource
  fetching;
- AcroForm editing, signature validation, PDF/A, or PDF/UA claims;
- tagged-PDF input reconstruction or output, accessibility/PDF-UA claims, Type 3
  fidelity, PDF 2.0-only constructs,
  JPX/JBIG2/CCITT support in the base build — their rows have all cleared, but
  they arrive only as composed extensions, JPX reports rather than decodes, and
  JBIG2 decodes only its MMR generic regions — four-component CMYK/YCCK JPEG
  conversion, and
  arithmetic-coded, lossless, hierarchical, JPEG-LS, or JPEG XR decoding;
- PDF-writer use or extension of the existing managed JPEG encoder; and
- HTML/CSS print-to-PDF.

### 1.2 V1 semantic and safety boundaries

- V1 import is logical extraction, not a visibility-faithful renderer,
  sanitizer, redaction engine, archival conversion, or safe-disclosure tool.
  Invisible text, clipping, optional content, artifacts, overlays, redaction
  annotations, attachments, metadata, and incremental history are handled by an
  explicit feature matrix and uncertainty diagnostics; a visual overlay never
  proves that underlying content was removed.
- Imported actions and URI values are inert source data. A URI is projected to
  `LinkHref` only after shared policy admission, and every activation path—UI,
  DOM/HTML projection, or writer—revalidates it under the current policy at the
  point of use and requires the host's normal user gesture where applicable.
  Parsing or validation never performs DNS, file, shell, or network access.
- V1 writer output is untagged. Model alt text may be retained for another
  format or a future tagged-PDF writer, but V1 emits no isolated `/Alt` entry,
  structure tree, PDF/UA claim, or accessibility-compliance claim.
- Successful parsing grants no authorization to persist or redistribute
  embedded bytes. Semantic projection, metadata projection, transient decode,
  extraction, byte-preserving transfer, transformation, embedding/subsetting,
  and redistribution are distinct policy operations.

## 2. Historical reset and cleanup

Two retired implementations exist, but neither is a valid baseline:

- `c45df220` through `e7f6bba0`: a third-party PdfSharp HTML-to-PDF adapter
  based on .NET Framework and System.Drawing;
- `3ed7b982` through `12d055b3`: a PdfPig/OpenXML PDF-to-DOCX application. Its
  "native parser" was only a PdfPig adapter, and its generated corpus is not an
  authoritative test oracle.

Do not restore code, APIs, tests, assets, or fixtures from either lineage. No Git
history rewrite is required merely to isolate the new implementation, but Phase
0 must separately audit the continuing redistribution of historical repository
content. Obtain authority or follow repository policy to remove/rewrite any
unlicensed, confidential, or otherwise restricted historical material. History
is not an approved implementation source. Record the lawful source of every new
implementation, table, mapping, fixture, and generated oracle; do not copy from
the retired lineages or from reference renderers merely because they remain
locally accessible.

Phase 0 must remove or rewrite these current remnants:

- external-process conversion, `BROILER_PDF_APP`, `--preserve-layout`, and the
  nonexistent source fallback in `src/Broiler.Cli/Program.cs`;
- environment-dependent
  `src/Broiler.Cli.Tests/PdfToWordConverterTests.cs`;
- the unresolved standalone-app decision in `docs/ROADMAP.md`;
- the obsolete proposal warning in `Broiler.Documents/docs/roadmap.md`; and
- standalone-app assumptions in the multithreading documentation and
  `CLAUDE.md`.

Keep unrelated current infrastructure such as PDF MIME classification,
binary-resource handling, WPT cases, PdfJS benchmarks, and HTML link-rectangle
generation. These may remain test or benchmark infrastructure, but they are not
approved sources for PDF implementation code or data.

## 2.5 Current implementation state

`Broiler.Documents.Pdf` and `Broiler.Documents.Pdf.Tests` exist and are built and
tested by `Broiler.Documents.slnx`. The implemented slice is deliberately the
part this repository can write for itself, with no third-party runtime
dependency and no bundled font, glyph list, metric file, or codec asset. Every
remaining PDF technology is recognized and skipped with its own stable
diagnostic, and arrives later by composing a reviewed implementation into
`PdfCodecServices` — see [PDF extension points](pdf-extension-points.md), which
is authoritative for that boundary.

**Implemented.**

- Phase 2 in full: bounded tokenization of every object type, checked offset and
  length arithmetic, classic cross-reference tables, trailers, `startxref`, and
  incremental `/Prev` chains, with explicit stacks and cycle detection
  throughout.
- Phase 3 for the filters this build owns: a single production pipeline carrying
  `FlateDecode` with PNG and TIFF predictors, `ASCIIHexDecode`, `ASCII85Decode`,
  and `RunLengthDecode`, with per-stage and aggregate byte, expansion, chain-depth
  and work accounting; cross-reference streams, object streams, and hybrid
  `/XRefStm` resolved through that same pipeline with no bootstrap decoder;
  `/Encrypt` detected from the trailers and the document rejected before any
  object stream, Catalog, metadata, font, image, annotation, or content service is
  invoked; Catalog, page tree, inherited attributes, boxes, rotation and
  `UserUnit`; effective version resolution with `/Extensions` inventoried as
  diagnostics only; `Info` and the XMP packet parsed into the normalized
  allowlist, XMP winning per field with `Info` as the fallback and disagreement
  reported by field name, and the raw packet dropped.
- Phase 4 for text: the graphics and text state, all show-text operators, `Do`
  for Form XObjects under bounded recursion with a visited set, length-bounded
  inline-image consumption, marked-content `ActualText`, simple-font encodings
  with `/Differences`, `ToUnicode` CMaps including `bfrange` and bounded
  `usecmap`, composite fonts through `Identity-H`, subset-prefix removal from
  structural metadata, deterministic geometric grouping into columns, lines and
  paragraphs, list-marker recognition, and link annotations admitted by the
  shared URI policy.
- A Phase 7 writer subset: layout resolved exactly once into an immutable page
  list that the serializer consumes without re-measuring; new PDF 1.7 files with
  catalog, page tree, resources, Flate content streams, cross-reference table and
  trailer; the fourteen standard font names with WinAnsi encoding and no embedded
  program; colour, decorations, alignment, indentation and lists; link
  annotations revalidated immediately before emission; caller-supplied normalized
  metadata and a content-derived file identifier. Output is byte-identical across
  runs and reads no clock, machine name, locale, or installed font.

**Deliberately not implemented, and why.**

- Image extraction into the model and encryption (IP-015). **No filter or codec
  row is open any longer**: `JBIG2Decode` was the last, and IP-008 cleared it on
  2026-09-01. A composed filter reads the segment structure and decodes generic
  regions coded with MMR through the T.6 decoder cleared under IP-009; the
  arithmetic decoder, and the symbol, text, halftone, and refinement regions that
  need it, are outstanding work rather than a pending approval — which matters,
  because that is what almost every JBIG2 image in a scanned document actually
  uses. `JPXDecode` half-left this list on 2026-09-01: IP-007
  cleared the JPEG 2000 Part 1 core coding system, and a composed reader now
  reports a codestream's real tuple, but no entropy decoder is written — the
  arithmetic coder, EBCOT, and the wavelet transforms are scoped work, not a
  pending approval. `CCITTFaxDecode` left this list on 2026-09-01: IP-009
  cleared and retired its patent position, and all three fax schemes decode
  through a composed filter — though the code tables it needs are the one
  transcribed normative constant in this repository, and SRC-017 carries that
  question separately. `LZWDecode` left this list outright on 2026-09-01: IP-010
  cleared and retired, and the filter is built in beside Flate rather than
  composed, because once the row was clear there was no outside component to
  compose and no image-codec-scale surface to keep out of the default build.
  Embedded font programs left this list on 2026-09-01:
  IP-012 cleared inspection, and a caller composes `Broiler.Documents.Pdf.Fonts`
  to recover the text of a composite font that supplied no `ToUnicode` map. Type 1
  and bare CFF are still unread, for want of parser surface rather than approval,
  and no font is embedded, subsetted, or re-emitted by anything.
  `DCTDecode` has left this list only partly: IP-005 and
  IP-006 cleared baseline sequential JPEG and its colour declaration on
  2026-09-01, and a caller composes `Broiler.Documents.Pdf.Images` to decode it.
  IP-005 was then widened to progressive DCT on 2026-09-02 — the first row here
  widened rather than opened, and a decision rather than a code change, since
  `Broiler.Media` had decoded SOF2 all along and only the filter's frame-marker
  gate moved. The base build still composes no image decoder; SOF1 extended
  sequential, four-component YCCK, and a declared transform of 0 on three
  components are still refused, the last of those because the composed decoder
  cannot skip its colour conversion rather than because a row is open; and a
  decoded image still reaches no model, because extraction into the model waits
  on the resource policy of §6.2 rather than on a patent row. Each is detected and skipped, or in
  encryption's case rejects the document. A skip reports an inventory of what it
  met — counts, pages, and the declared variants — without decoding anything to
  produce it; see
  [PDF extension points §3.1](pdf-extension-points.md#31-what-a-skip-report-carries).
  Tagged structure (IP-017) is described the same way and remains unconsumed.
- The shared work the roadmap places in other owners: `Broiler.Documents`
  request/result envelopes, `DocumentInput`, the conversion/resource context,
  `Broiler.Documents.Pagination`, the Graphics font inspector and export
  subsetting, and the Media image services. The PDF package composes none of them
  today; its writer paginates internally against a replaceable metrics provider
  rather than pre-empting the shared paginator's design.

**Not advertised.** The package is `IsPackable=false` and reaches an application
only through the Windows and Linux Writer composition roots, for opening, as the
§10.1 read-preview candidate; every other catalog and composition root is still
closed to it, and tests fail the build if any of that changes. Phase 5 and Phase 7
remain the publication boundaries, and no feature-matrix entry may reach
`Supported` while its register row is pending. As of 2026-09-01 every filter,
codec, and construct row is decided, IP-001 included; the rows still open are
provenance and wording ones, listed in the register's
[what still blocks a support claim](pdf-ip-licensing-register.md#what-still-blocks-a-support-claim).

## 3. Component ownership

| Owner | Shared work placed there | Must remain PDF-specific |
|---|---|---|
| `Broiler.Documents` | Typed codec requests/options, replayable/random-access input, result-state taxonomy, cancellation, conversion/resource context, diagnostic locations, common byte/resource budgets, metadata envelope, URI output policy, release-level IP/provenance register | Nothing involving xrefs, PDF objects, operators, CMaps, or encryption |
| `Broiler.Documents.Model` | Only cross-format semantics proven useful to DOCX/RTF/HTML too: page breaks, justification, direction, finite measurement invariants, and `InlineImage` carrying an opaque stable identity plus an immutable Graphics image resource | Resource-policy decisions, PDF color semantics, page coordinates/boxes, annotations, PDF dictionaries, positioned glyphs |
| New `Broiler.Documents.Pagination` | Headless rich-text line layout, page settings, margins, page breaks, list markers, inline images, link rectangles, and an immutable PDF-neutral paged artifact | PDF serialization or backend handles |
| `Broiler.Graphics` | Explicit font-face resources, shaped/positioned glyph runs, deterministic font resolver, technical embedding/subsetting enforcement, generally reusable licensed font/shaping assets, immutable `BImageResource` wrappers, and neutral page-scene resources | `Broiler.Documents` policy types, PDF font dictionaries, PDF encodings, Standard 14 metrics, predefined CMaps, and character collections |
| `Broiler.Media.Image` | PDF-neutral immutable encoded-image/pixel-buffer payloads, pixel/color-format vocabulary, inspection/decode contracts and limits, and caller-composed codec services | `Broiler.Documents` policy types, PDF filter dictionaries, predictors, masks, Decode arrays, and resource resolution |
| `Broiler.Media.Image.Managed` | The approved managed JPEG subset and its code/data provenance, SBOM, notices, tests, and human review; later shared codec implementations only under separately approved tracks | PDF filter dictionaries, predictors, masks, Decode arrays, and PDF resource resolution |
| `Broiler.DOM` | No work required | PDF objects and tagged structure are not DOM nodes |
| Existing `Broiler.Layout`/HTML/CSS | Later HTML print media, fragmentation, `@page`, link rectangles | The document codec must not depend on this DOM/CSS layout engine |
| `Broiler.Documents.Pdf` | Syntax, object store, xrefs, filters, security handler, page tree, resources, content operators, PDF encodings/CMaps/Standard 14 data, extraction heuristics, model projection, serialization, and notices for PDF-only assets | — |

Containment rule: a shared addition must have a PDF-neutral name, owner-local
tests, and a second non-PDF consumer. Otherwise it stays internal to
`Broiler.Documents.Pdf`.

Dependency rule: `Broiler.Graphics` and `Broiler.Media.Image` must not depend on
`Broiler.Documents`. They expose owner-local technical validation and resource
decisions. The `Broiler.Documents` conversion context translates caller policy
into those decisions, and `Broiler.Documents.Pdf` composes them. Each model
resource carries an opaque stable identity that survives immutable `With*`
operations; the corresponding provenance and permissions remain in the
conversion context rather than in Graphics, Media, or the rich-text model.

Image type rule: `Broiler.Media.Image` owns immutable managed-memory payloads.
An encoded payload records media type, dimensions, inspected component/color
model, and owned bytes; a decoded payload records dimensions, stride, pixel
format, explicit Gray/UncalibratedRgb/sRGB interpretation, and owned bytes.
`Broiler.Graphics`
owns `BImageResource`, the immutable discriminated wrapper used by model and page
scenes. `InlineImage` carries `DocumentResourceId` plus `BImageResource`; page
scenes refer to the same typed resource and never create another payload type.
No payload contains a PDF ColorSpace, `/Decode`, mask, pooled/disposable owner,
native/backend handle, or ambient decoder. Ownership lasts with the immutable
document/context, and limits cover retained bytes. The manifest binds resource
identity to payload kind, media type, dimensions, and content digest.

Legal containment follows code containment: the implementation component that
distributes a font, CMap, mapping table, ICC profile, codec, or other external
asset owns its license record, notice file, SBOM entry, and human approval.
Graphics owns only generally reusable font/shaping assets. PDF-only encodings,
Standard 14 metrics, predefined CMaps, and character collections remain owned
and noticed by `Broiler.Documents.Pdf` unless a real non-PDF consumer justifies
promotion. PDF filter semantics remain there, while codec code/data/notices stay
with their Media implementation; legal uncertainty is not a reason to duplicate
shared codec code inside PDF.

```text
Broiler.Documents.Model ──> Broiler.Graphics
Broiler.Graphics       ──> Broiler.Media.Image
Broiler.Documents       ──> Broiler.Documents.Model

Broiler.Documents.Pagination
    ├──> Broiler.Documents.Model
    └──> Broiler.Graphics

Broiler.Documents.Pdf
    ├──> Broiler.Documents
    ├──> Broiler.Documents.Model
    ├──> Broiler.Documents.Pagination   [write track]
    ├──> Broiler.Graphics
    └──> Broiler.Media.Image            [abstraction only]
```

## 4. Phase summary

| Phase | Goal | Dependency | Estimated effort | State |
|---|---|---|---:|---|
| 0 | Reset authority, scope, IP/legal ADRs, cleanup | None | 3–4 engineer-weeks | Repository work done; external approvals outstanding |
| 1 | Shared contracts, read-safe shared services, approved corpus, CI/license foundation | Phase 0 | 7–10 | §6.1 contracts landed (`DocumentInput`, request envelopes, shared result status and destination state, typed-option validation, async overloads, catalog selection-and-read) and the CI workflow created. §6.2 resource/metadata context, §6.4 model review, §6.5 Graphics/Media prerequisites, and the §6.6 oracle/corpus/harness work are outstanding |
| 2 | PDF syntax and object store | Phase 1 | 4–6 | Implemented |
| 3 | Streamed xrefs/object store, structure, filters, security detection | Phase 2 | 6–9 | Implemented for the filters this build owns; the rest detected and skipped |
| 4 | Logical text/image/link import and minimum hostile-input gate | Phase 3 | 8–12 | Text, links and structure implemented; images and embedded font programs detected and skipped; hostile-input gate covered by in-suite truncation and mutation campaigns, not yet by coverage-guided fuzzing |
| 5 | Read-preview integration | Phase 4 and Phase 1 unit/UI gate | 3–5 | Writer integration candidate landed: catalogs are injected from composition roots, the Windows and Linux Writer heads register the codec for opening, and the desktop open path runs through `SelectAndRead`/`DocumentInput`. CLI integration, conversion context, partial-read confirmation, and the §10.2 exit-gate evidence are outstanding; the package stays unpacked and unpublished |
| 6 | Shared pagination/font/export foundation | Phase 1; parallel with 2–5 | 10–16 | Not started; the writer paginates internally against a replaceable metrics provider |
| 7 | Deterministic PDF writer and output integration | Core: Phases 3 and 6; write-preview publication: Phase 5 also | 7–12 | Writer core implemented for the standard-font subset; integration and publication not started |
| 8 | Hardening, packaging, legal and stable-release evidence | Phases 5 and 7 | 6–10 | Not started |

Estimates assume one experienced contributor and are recalibrated after the
Phase 1 option/input/result, script/shaping, image, and font-format decisions.
Cumulative effort is roughly 31–46 engineer-weeks for a read preview, 48–74 for
a read/write preview, and 54–84 for a hardened release. Calendar time may be
lower when Phase 6 runs in parallel with parser work. These estimates exclude
waiting time for standards acquisition, outside legal review, patent-family
research, permissions, or commercial-license negotiation.

### 4.1 Delivery milestones and publication state

- Phases 2–4 produce an internal parser/importer. The PDF project remains
  `IsPackable=false` and excluded from release artifacts, enforced by
  `PdfDeliveryGuardTests`: the guards fail the build if the project becomes
  packable, gains a third-party or non-Documents reference, or if a `.pdf`
  fixture is committed outside the rights-aware corpus.
- Phase 5 is the read-preview boundary. After the Phase 4 reader-core gate, a
  test-only candidate may expose `CanRead=true` and register PDF in open/import
  paths so integration checks can run. Publish that prerelease capability only
  after the complete Phase 5 gate passes on the candidate; `CanWrite` and every
  PDF destination/save filter remain disabled. **This is the state today**: the
  Windows and Linux Writer heads register the codec for opening, and the same
  guards enforce the shape of that registration — only those two composition
  roots (and the tests covering them) may name the codec, the shared
  `Broiler.Writer.Core` and the Android and WebAssembly heads may not acquire it
  even transitively, and no head may register it for saving. The package is not
  packed and the capability is not published.
- Phase 7 is the write-preview boundary. After its writer-core readiness subgate,
  a test-only candidate may enable `CanWrite`, CLI PDF destinations, and selected
  Writer save filters so integration gates can execute. Those capabilities are
  not published until the complete Phase 7 gate passes on that candidate.
  Published packages remain prerelease.
- Phase 8 is the stable-release boundary. A non-prerelease package or unrestricted
  public-feed publication is prohibited until security, performance, package,
  IP/licensing, and enabled-application gates pass for the exact packed commit.
- A feature becomes `supported` only when its implementation, limits,
  diagnostics, corpus, oracle, CI, provenance, and legal entries all pass. A
  later phase cannot waive an earlier gate. If a host omits an optional service,
  its effective matrix is narrower and is reported separately.

## 5. Phase 0 — Scope, standards/IP authority, and re-baseline

Implementation status and external approvals are tracked separately in the
[Phase 0 status record](pdf-phase0-status.md). The boundary between the
implemented base and each not-yet-cleared technology is specified in
[PDF extension points](pdf-extension-points.md), and the exact construct set the
IP-001 review has to cover is enumerated in the
[construct inventory](pdf-construct-inventory.md). The current decision/evidence set
is indexed by the [ADR index](adr/README.md),
[feature matrix](pdf-feature-matrix.md),
[IP/licensing register](pdf-ip-licensing-register.md),
[approved-source record](pdf-approved-sources.md), and
[corpus manifest](pdf-corpus-manifest.json).

### 5.1 Deliverables

- Add ADRs covering:
  - PDF product scope and dependency ownership;
  - codec option typing, input probing/replay, stream ownership, cancellation,
    result states, partial-result usability, and transactional output behavior;
  - security, active-content, resource, and encryption policy;
  - semantic extraction versus visibility, redaction, sanitization, and
    metadata/privacy policy;
  - logical import versus fixed-layout export;
  - measurement units, the immutable paged-artifact boundary, exact V1
    script/bidi/shaping matrix, font-format matrix, deterministic font
    provisioning, and pagination policy;
  - platform composition and the CLI/desktop/Android/WebAssembly preview and
    release matrix;
  - IP, standards access, implementation provenance, asset licensing, patent
    declarations, target distribution jurisdictions, and reciprocal-license
    policy; and
  - conformance, trademark, certification, and non-endorsement claims.
- Establish a versioned IP/licensing register. Each feature entry records:
  - exact standard, edition, part, profile, amendment, operator/filter mapping,
    and whether Broiler reads, writes, decodes, encodes, or only preserves it;
  - the lawfully obtained specification source and its copyright/use terms;
  - code and data provenance, SPDX expression or full terms, required notices,
    asset redistribution rights, generated-document attribution/license-copy/
    naming/source obligations, and any dependency patent grant;
  - the ISO/IEC/ITU declaration-database URL and review date, licensing option,
    reciprocity, known patent-family/status review, and approved distribution
    jurisdictions;
  - responsible component, reviewer, approval date, and pending/approved/rejected
    status.
- Maintain independent register entries for PDF 1.7 implementation, PDF 2.x
  declaration tolerance, developer-extension handling, XMP, each accepted JPEG
  DCT tuple and color-transform rule, each accepted font/container/outline
  format, Unicode/shaping data, active URI output, encryption, signatures, and
  every other separately sourced standard or table. Approval of one entry never
  implies approval of another.
- Pin exact editions rather than a moving `latest` document. For fonts this
  includes the selected OpenType/TrueType, CFF/Type 2, and Unicode specifications;
  for XMP it includes the selected Adobe XMP and/or ISO 16684-1 edition and the
  approved RDF/XML serialization subset. Font-asset embedding permission and
  font-format implementation authority are separate decisions.
- Store confidential commercial-license or patent agreements outside the public
  repository. The public register records a controlled agreement identifier,
  applicable version/hash, approved scope, reviewer, and expiry/review date,
  without publishing confidential terms.
- Require worldwide clearance for an unrestricted public package feed. A
  narrower jurisdiction approval is valid only when the actual distribution
  channel is technically and contractually territory-limited and that limit is
  recorded and enforced; documentation alone is not a distribution control.
- Seed the register from the official
  [ISO patent policy](https://www.iso.org/iso-standards-and-patents.html) and
  current ISO/ITU records. A missing declaration, an old declaration, a
  royalty-free objective, widespread adoption, or an open-source copyright
  license must never be treated as proof of patent freedom.
- Record Adobe's
  [ISO 32000-1:2008 public patent license](https://www.adobe.com/pdf/pdfs/ISO32000-1PublicPatentLicense.pdf),
  including its Adobe-owned-essential-claims scope, exclusion of updated
  specifications, patent-retaliation language, and lack of non-infringement
  warranty. A qualified reviewer must determine whether the planned partial
  reader/writer satisfies the license's `Compliant Implementation` and
  `Essential Claim` coverage conditions; mere project acceptance does not
  establish coverage. If coverage is uncertain, block the affected feature or
  obtain separate authority. The license is not authority for ISO 32000-2/PDF
  2.0.
- Treat Catalog `/Version`, `/Extensions`, tolerance of malformed/recovered
  input, and recognition of newer declarations as dispatch inputs only. They do
  not expand the approved feature matrix or Adobe patent-license scope.
- Give XMP an independent standards, patent-license, copyright, schema/data, and
  implementation-provenance review using the pinned applicable edition of
  [ISO 16684-1](https://www.iso.org/standard/75163.html) and the relevant
  [Adobe XMP Toolkit/specification materials](https://github.com/adobe/XMP-Toolkit-SDK).
  Do not infer XMP authority from PDF clearance or from the copyright license of
  an XMP SDK.
- Establish the standards-source rule: cite clauses rather than reproducing
  standard prose; do not commit ISO/ITU publications, diagrams, sample code,
  official test material, or substantial tables without redistribution rights;
  record the source and approved legal basis for unavoidable normative
  constants.
- Establish the user-content rule: opening or converting a document grants
  Broiler no reuse or republication right. Callers are responsible for authority
  to extract, copy, transform, and preserve input text, metadata, fonts, images,
  profiles, and attachments; Broiler does not automatically republish input
  assets or represent that caller authorization establishes legal ownership.
- Establish the public-surface rule:
  - initially public: `PdfDocumentCodec`, `PdfReadOptions`, `PdfWriteOptions`,
    `PdfLimits`, the format-neutral request/result-status contracts they use,
    and documented diagnostics;
  - keep `PdfObject`, xref entries, page dictionaries, operator tokens, and
    parser internals non-public until another real consumer justifies them.
- Publish a feature matrix with behavior states `supported`,
  `detected-but-skipped`, and `rejected`, plus independent columns for read,
  write, decode, encode, byte preservation, transformation, default exposure,
  and legal clearance. It records the effective-version and extension rules,
  exact JPEG/image/font/script tuples, and user-visible diagnostic. `Supported`
  is invalid while clearance is pending.
- Create a new independent corpus manifest containing:
  - SHA-256, exact source/revision URL and acquisition date, author/rightsholder,
    provenance, SPDX expression or full license terms, and attribution;
  - redistribution, public-CI, modification/fuzzing, and generated-derivative or
    screenshot rights;
  - embedded font, image, CMap, and ICC-profile provenance/rights;
  - PII/privacy classification, malicious/CVE classification, quarantine,
    retention, and redistribution approval; and
  - feature tags, expected text/diagnostics, oracle and modification history.
- Define an approved-source and similarity-review record for new PDF code and
  data. The old PdfSharp/PdfPig lineages, PDF.js, PDFium, Poppler, MuPDF, and
  other independent implementations may be black-box test oracles under their
  terms, but their code, tables, fixtures, and generated data are not sources to
  copy.
- Remove the obsolete CLI/application authority listed above.
- Re-baseline all current Documents tests before implementation.

### 5.2 Exit gate

- Scope, dependency graph, IP/licensing ADR, register schema, and claims policy
  are approved.
- The codec request/options/result contract, stream and output ownership rules,
  partial-result semantics, metadata/privacy behavior, unit model, paged-artifact
  boundary, script/shaping matrix, font-format matrix, product font-provisioning
  plan, and platform-composition matrix are approved; estimates are updated from
  those decisions.
- A qualified reviewer has determined and recorded Adobe ISO 32000-1 patent-
  license coverage for the planned V1 reader/writer, or the affected capability
  remains blocked under separate-authority review; PDF 2.x recognition is
  documented as construct tolerance, not ISO 32000-2 conformance.
- Old code and fixtures are formally classified as non-reusable, and no unclear
  historical artifact remains in the working tree or approved-source list.
- The repository-history redistribution audit is resolved: restricted historical
  material has documented authority or has been removed/rewritten under the
  repository's approved history-rewrite policy.
- Required standards are lawfully accessible to implementers without copying
  restricted publications into the repository.
- The intended distribution channel is worldwide-cleared or has an enforceable,
  approved territory limitation.
- No feature is described as patent-free, unconditionally royalty-free,
  certified, endorsed, or fully conforming without specific recorded authority.
- No capability advances from detected/skipped or rejected into interpretation,
  transformation, generation, or a public support claim without its exact
  specification/IP entry. Newer headers, Catalog versions, and extension
  declarations cannot enable an unapproved feature.
- V1 documentation is approved to state that import is not redaction or
  sanitization and that writer output is untagged and makes no PDF/UA or
  accessibility-compliance claim. The consuming product records whether a
  jurisdiction-specific accessibility obligation changes that release scope.
- No "preserve layout" or standalone-app claim remains.
- The existing Documents/Writer baseline is green. The only approved behavior
  change is the separately versioned, documented resource-context migration in
  Phase 1; no unrelated behavior change is bundled with it.
- Architecture guards prohibit PDF types in shared assemblies.

## 6. Phase 1 — Shared contracts, read-safe services, approved corpus, and engineering foundation

### 6.1 `Broiler.Documents` request, option, input, and result contracts

> **Landed 2026-08-25.** `DocumentInput`, `DocumentReadRequest`/`DocumentWriteRequest`,
> the shared `DocumentResultStatus`/`DocumentDestinationState`, typed-option
> validation, the async overloads, and `DocumentCodecCatalog.SelectAndRead` are
> implemented, and every codec reports a status. Two bullets below were amended by
> what the implementation found; both amendments are marked in place. Not yet
> done: the conversion/resource context (§6.2), which is where the write
> request's resource-manifest and commit-policy members belong, and a
> spooling-capable `DocumentInput` — the type is memory-only by design until a
> host supplies a spooling policy of its own.

- Adopt non-sealed format-option bases with codec-specific immutable options.
  Move Windows-1252/default-code-page, object-decoding, ASCII-only output, group
  depth, and `\bin` payload settings into `RtfReadOptions`, `RtfWriteOptions`, and
  `RtfLimits`; base options/limits contain only behavior genuinely shared by
  multiple codecs.
  **Amended 2026-08-25 by implementation.** Group depth and the `\bin` payload
  limit are *not* RTF-specific: DOCX enforces `MaxGroupDepth` for style-inheritance
  depth and `MaxBinBytes` for part size, so both already have the second consumer
  the containment rule requires and correctly stay on `DocumentLimits` — moving
  them would have forced DOCX to duplicate them. `RtfLimits` is therefore
  unnecessary and was not created. Of the remaining three settings only the
  default code page had any consumer: object-decoding and ASCII-only output had
  none at all, which made them a public contract implying behavior no codec
  implemented. `RtfReadOptions`/`RtfWriteOptions` now name the RTF-specific
  settings, the base members document what they actually do, and a codec asked
  for the unimplemented behavior reports `document.capability.not-composed`
  instead of silently doing something else. `PdfReadOptions`/`PdfWriteOptions` compose `PdfLimits`. Where
  common and format-specific budgets overlap, the effective value is the
  stricter remaining budget. A codec validates the concrete option type before
  touching input or output. A mismatched option object produces a structured
  invalid-options result; it is never silently ignored or downcast
  opportunistically.
- Add format-neutral read and write request envelopes. A read request carries
  `DocumentInput`, typed options, cancellation, limits, and a conversion/resource
  context. A write request carries the document, destination, typed options,
  caller-selected metadata, resource manifest/context, cancellation, and output
  commit policy. Preserve existing overloads as documented compatibility
  adapters until the repository's normal deprecation policy permits removal.
  **Amended 2026-08-25 by implementation:** the metadata, resource-manifest, and
  commit-policy members wait on the §6.2 conversion context that defines them, so
  the write request carries document, destination, typed options, and
  cancellation today. Cancellation moved off `PdfReadOptions`/`PdfWriteOptions`
  onto the request, giving it the single owner this bullet requires.
  Cross-format values have exactly one owner on the request; format options may
  not duplicate or override them. A future accidental duplicate/conflict is an
  invalid-options rejection rather than a precedence rule.
- Make the resource-security migration explicit and versioned. Legacy no-context
  write overloads remain source-compatible for text-only documents but reject a
  document containing binary resources with
  `document.resource.context-required`. They never infer permission from public
  bytes. Provide a request/context builder for caller-created resources that
  records provenance/content binding and an explicit operation grant. Migrate
  every repository caller/test and announce the intentional image-writing
  behavior change under the project's compatibility/major-version policy before
  release; imported bytes cannot be laundered through a legacy overload.
- Freeze an instance-based composition contract. `PdfDocumentCodec` receives one
  immutable service graph at construction and never discovers a decoder, font,
  spool, cache, executable, environment variable, or platform registry through
  statics or ambient state. The PDF package references Media abstractions only;
  concrete managed codecs are selected by application composition roots.
  Read-only composition may omit writer services; a missing optional image
  decoder yields a stable detected/skipped result, while missing required
  pagination/font/output services makes `CanWrite=false` before a request starts.
  Every cache is instance- or document-owned, bounded by count and bytes, and has
  an explicit disposal lifetime.
- Define result status as `Success`, `Partial`, or `Rejected`, independently of
  diagnostic severity:
  - `Success` means the declared supported subset was processed and the result
    is usable under the documented contract;
  - `Partial` means a usable result exists but named content/features were
    skipped or remain uncertain; callers must explicitly opt in before replacing
    an open document or publishing output; and
  - `Rejected` means no document or output is usable or may be accepted by the
    host; a direct-stream write may additionally report that an irreversible
    partial prefix already exists and must be discarded.
  Specify exception versus result behavior for programmer errors, I/O failures,
  cancellation, limit exhaustion, malformed input, unsupported policy, and
  internal faults. CLI exit codes, UI prompts, batch continuation, and telemetry
  derive from this taxonomy rather than from `HasErrors` alone.
- A write result also reports `NotStarted`, `Committed`, or
  `PartialDestination`. `Success` requires `Committed`; `Rejected` plus
  `PartialDestination` tells a caller-owned stream that bytes may need disposal.
  The commit point is the atomic file replace, publication of a complete browser
  buffer, or the first byte written to an unstaged arbitrary stream.
- Add one authoritative catalog selection-and-read path. Selection returns the
  chosen codec together with a replayable `DocumentInput`; probing a
  non-seekable source cannot consume bytes that the codec later needs. Define
  deterministic confidence ties and behavior when no codec matches.
- Add a `DocumentInput` abstraction that:
  - defines stream ownership and the lifetime of caller buffers;
  - replays probe bytes on non-seekable streams;
  - exposes known length without requiring full allocation;
  - supports bounded random-access materialization;
  - allows only caller-provided spooling, with explicit directory, quota,
    permissions, cleanup, crash-recovery, and privacy policy rather than ambient
    temporary-file access; and
  - works in memory-only WebAssembly environments.
- Add async/cancellation overloads without removing the synchronous contract.
  Define cancellation checkpoints for probing, tokenization, filter decode,
  object/page traversal, font/image work, pagination, and writing.
- Capability services expose distinct true synchronous and asynchronous
  operations where the codec retains both entry points; adapters never use
  `.Result`, `.Wait()`, or `GetAwaiter().GetResult()`. Official desktop/CLI
  compositions provide both, while UI and WebAssembly hosts use the async codec
  path. If a caller supplies only an async optional image service, a synchronous
  read detects/skips that image and returns `Partial` with
  `pdf.service.sync-unavailable`; a required synchronous writer service is a
  zero-byte preflight rejection. Sync/async capability differences appear in the
  effective host matrix rather than blocking a UI thread.
- Hosts must apply encoded-size limits before `ReadAllBytes`, `CopyTo`, or base64
  decode. WebAssembly checks the encoded and predicted decoded base64 sizes before
  allocation; CLI and desktop use bounded streaming/materialization.
- Add optional, format-neutral diagnostic source locations and
  diagnostic-count limits. Diagnostics never contain passwords, unbounded
  document content, local spool paths, or sensitive metadata values.

### 6.2 Resource and metadata continuity

- Replace `DecodeEmbeddedObjects` with explicit caller-composed, bounded
  image/resource services and an immutable conversion context.
- `DocumentReadResult` returns the immutable `DocumentConversionContext` produced
  from the caller's policy plus normalized metadata/resource manifest; it is not
  discarded when only the model is displayed. `DocumentWriteRequest` accepts
  that context explicitly. Existing write overloads use a documented empty
  context and therefore cannot redistribute unknown read-origin resources.
- Distinguish semantic projection, metadata projection, transient decoding,
  embedded-byte extraction/persistence, byte-preserving transfer,
  transformation, embedding/subsetting, and redistribution. A read request
  authorizes only its explicitly selected semantic and metadata projections plus
  bounded transient processing; acceptance by a reader grants no later writer
  authorization.
- Give every document resource an opaque stable identity that survives sizing,
  alt-text, and other immutable model transformations. The conversion context
  maps that identity to caller-provided provenance, source disposition, target
  output, permitted operations, generated-document obligations, and the stable
  allow/deny decision for each operation. Unknown operation, provenance, caller
  intent, or disposition defaults to deny.
- Scope identity to one conversion context and bind each entry to payload digest,
  kind, dimensions, and media/pixel format; an ID alone is never authorization.
  Equality includes the context namespace plus local ID. Same-context immutable
  edits preserve it. Copy/paste, clone, merge, import, or deserialization into
  another context rekeys the resource and requires a new caller decision;
  authorization never transfers automatically. Forged IDs, duplicate local IDs,
  payload mismatch, stale entries, and collisions reject or rekey before use
  according to a deterministic rule, never select an arbitrary manifest entry.
- Treat creation of an `InlineImage` with publicly accessible encoded bytes as
  durable extraction into the result model. Without `ExtractToModel` permission,
  do not construct it; omit the image and diagnose the denial. V1 does not invent
  a PDF-only placeholder. A future non-byte-bearing placeholder requires its own
  cross-format model contract and second consumer.
- Retrofit DOCX, RTF, HTML, and Markdown image/resource writers to consume the
  same context so a resource cannot bypass policy merely by changing output
  codec. Adapt the context to owner-local Media/Graphics decisions; do not add a
  reverse dependency from either shared component to `Broiler.Documents`.
- Add a format-neutral document metadata envelope. Read results preserve Info
  and XMP provenance separately and expose normalized values without putting PDF
  objects in the model. Well-formed RDF/XML accepted by the pinned XMP subset
  wins only for a specifically normalized field; Info is the fallback, conflicts
  are diagnosed, and unknown/custom values stay source-labelled rather than
  silently merged. “Accepted” never requires DTD or external schema validation.
- Freeze the V1 normalized fields as title, ordered authors, subject, ordered
  keywords, language, creator application, producer, creation timestamp, and
  modification timestamp. Missing and explicitly empty remain distinct; a
  timestamp retains its stated precision and optional UTC offset, and Broiler
  never invents a zone for a zone-less PDF date. Arbitrary PDF keys, raw
  dictionaries, and raw XMP packets are not exposed through this contract. At
  least one non-PDF codec consumes the same normalized envelope before promotion.
- Metadata supplied to a writer comes from the write request, not the write
  result. V1 always drops raw XMP packets, arbitrary Info/custom properties,
  source trailer IDs, paths, usernames, and unnormalized source values; it has no
  opaque preservation carrier. An explicit transfer policy can select only the
  normalized fields above, after which the caller may override them. PDF file IDs
  are fresh writer controls. The write result reports what was emitted,
  normalized, or stripped.
- “Envelope” is an in-process typed value, not an automatically written sidecar
  file. Conversion contexts, metadata, spool paths, and source values are not
  logged, cached across documents, or persisted by default; ownership, disposal,
  retention, telemetry redaction, and crash-artifact handling are documented.

### 6.3 Limits and shared URI policy

- Add generic encoded/decoded byte and resource budgets only where multiple
  codecs can use them. Put PDF-specific accounting into `PdfLimits`, including:
  - input bytes, token/name/string lengths, array/dictionary entries, object and
    indirect-reference counts;
  - xref sections, incremental revisions, pages, page-tree depth, and total
    containers;
  - per-stream and total decoded bytes, filter-chain depth, per-stage and total
    expansion ratios;
  - operators per content stream/page/document, Form XObject recursion, total
    extracted characters/runs, and reading-order work;
  - resource, annotation, action, outline, CMap entry, and `usecmap` counts;
  - font bytes, tables, glyphs, composite depth, and cache bytes;
  - image dimensions, per-image/aggregate pixels, decoded bytes, marker/table/
    scan counts, and codec work;
  - metadata bytes, XML nodes/depth/namespaces/properties/decoded text;
  - diagnostics, output pages/objects/streams/bytes, and an aggregate work
    budget with cancellation-checkpoint cadence.
- Every limit has a documented default, configurable maximum or process hard
  ceiling, aggregate-accounting rule, checked-arithmetic rule, and stable
  diagnostic/status. Limit exhaustion cannot silently downgrade `Rejected` to a
  successful empty or truncated result.
- Delegated Media/font work receives the minimum of the shared, PDF-specific,
  service-specific, and remaining aggregate budgets and reports charged work
  back to the document counter; delegation never resets cumulative accounting.
  No zero/sentinel means unlimited in the untrusted-input profile, and all size/
  count arithmetic is checked before allocation.
- Add one PDF-neutral URI canonicalizer/activation policy consumed by PDF import
  and writing, editor/UI activation, DOM projection, HTML, and other link-capable
  consumers. Pin [RFC 3986](https://www.rfc-editor.org/rfc/rfc3986)
  grammar/percent-encoding behavior, the selected
  [IDNA framework](https://www.rfc-editor.org/rfc/rfc5890), exact Unicode and
  [UTS #46](https://www.unicode.org/reports/tr46/) versions if compatibility
  processing is used, and [RFC 6068](https://www.rfc-editor.org/rfc/rfc6068)
  behavior for `mailto`. It defines
  maximum length, absolute/relative handling, Unicode/IDN normalization,
  user-info rejection, control/NUL/CRLF rejection, and fragment/query bounds.
  V1 active output allows absolute `https` by default; `http` and `mailto` need
  explicit caller opt-in. `javascript`, `file`, `data`, local/UNC paths,
  shell/protocol-handler schemes, and unknown/custom schemes are rejected. URI
  validation performs no DNS resolution, file access, preview, or network I/O.

### 6.4 Model review

Promote only features with another immediate codec consumer. Phase 1 must add
explicit page breaks, justified alignment, and text direction only after naming
and testing their DOCX/RTF/HTML consumer. Add opaque stable resource identities
for all image-capable writers and reject non-finite as well as negative model
measurements. Absolute coordinates, page boxes, positioned glyphs, and
fixed-layout objects remain prohibited.

- Freeze model typographic and image display dimensions as points in Phase 1,
  not Phase 6. Migrate RichEdit/UI boundaries at the same time to convert points
  explicitly to device-independent/CSS pixels; no control may pass model font
  size directly to a pixels API. Phase 5 cannot display imported PDF content
  until this unit subgate and its existing-codec/UI regression tests pass.
- Add format-neutral document style defaults. A missing inline font size inherits
  an explicit document default of 12 points. A missing inline family inherits the
  document's logical family; if that is also absent, UI may use its documented
  display fallback, but deterministic pagination/write preflight requires an
  explicit caller-approved font binding and never adopts the UI/OS choice.
- Image width/height are nullable auto dimensions, never zero/NaN/infinite
  sentinels. PDF import supplies explicit point dimensions from the approved
  placement transform. For caller-created images, two null dimensions use
  inspected intrinsic pixels at the fixed 96-pixels-per-inch conversion; one
  null dimension preserves inspected aspect ratio. Explicit zero or unavailable
  intrinsic dimensions produce a stable unplaceable-resource diagnostic.
- Evolve `InlineImage` around `DocumentResourceId` and `BImageResource`. Its
  legacy MIME/byte constructor creates only an encoded Media payload after
  bounded inspection; compatibility byte/MIME access is defined only for that
  variant and never fabricates an encoding for decoded pixels. Add an explicit
  discriminant/payload API, document buffer ownership, and deprecate ambiguous
  access under the same versioned resource migration.

### 6.5 Read-safe Graphics and Media prerequisites

- Before PDF font parsing, add an allowlist-based shared font-program inspector
  with checked table-directory arithmetic, overlap/offset/length validation,
  bounded table and glyph access, composite-recursion and cache limits, and
  format discrimination. It never executes hinting bytecode, SVG, or other
  embedded program-like content. Pin the exact accepted format tuple; V1 rejects
  variable fonts, CFF2, color/SVG/bitmap font tables, WOFF/WOFF2, Graphite/AAT,
  and any table not explicitly preserved or ignored safely.
- Separate read-safe inspection from Phase 6 export subsetting. Phase 4 may use
  only the Phase 1 inspector and approved PDF-specific Type 1/CFF mapping logic;
  it cannot instantiate a less-bounded parser while waiting for export work.
- Add shared Media decode limits and cancellation covering encoded bytes,
  marker/table lengths and counts, dimensions, components, sampling factors,
  scans, restart intervals, pixels, coefficient/decoded-memory allocation, and
  work units. Validate dimensions and checked allocation sizes before allocating
  destination or coefficient buffers. `PdfLimits` composes with—not replaces—
  those Media limits.
- Refactor the Media inspection/decode abstraction to expose the true sync/async
  operations required above. The managed JPEG implementation performs sync I/O
  on the sync path and async I/O on the async path, with common CPU decode logic,
  limits, cancellation, and identical admitted-mode/color results.
- Freeze the V1 JPEG tuple as 8-bit Huffman-coded SOF0/SOF2 with one or three
  components and only approved sampling/table/restart behavior. Separately
  review PDF `ColorTransform` and Adobe APP14 interpretation even for
  three-component data, including the source/use terms and Adobe Technical Note
  #5116 if relied upon; if the correct interpretation is ambiguous or the entry
  is unapproved, reject rather than guessing from component count.

### 6.6 Test, corpus, license, and CI foundation

- Create required `.github/workflows/documents-pdf.yml` Windows/Linux checks for
  pull requests and pushes affecting Documents, Graphics, Media, relevant
  CLI/Writer roots, solution generation, packaging scripts, or the workflow.
  **Created 2026-08-25** for the Documents solution plus the Graphics and Media
  image suites, on both platforms, with a guard that fails a run whose executed
  test count collapses — three of those suites are console runners that
  `dotnet test` would exit 0 on having run nothing. The CLI/Writer host paths and
  jobs are deliberately absent until Phase 5 activates them, so the trigger
  covers only what the job actually proves; no oracle is wired in, because each
  needs its licence review and tool-manifest row first.
  They restore locked inputs, build/test `Broiler.Documents/Broiler.Documents.slnx`
  in Release, explicitly run the applicable Graphics and Media test projects/
  console runners, execute host integration plus architecture/package-content
  tests, and upload only approved diagnostics. Add required clean-feed package
  consumption and trimming/AOT checks for affected heads. Packaging/publication
  cannot bypass them through an environment override or alternate feed.
  Phase 1 creates the workflow, existing-component jobs, harness contracts, and
  path filters; PDF parser, package, host, and AOT jobs activate as their phase
  introduces the corresponding project/candidate. A skipped, missing-project,
  empty, or no-op job can never satisfy a later phase gate.
- Select, license-review, acquire, hash/signature-verify, and pin structural,
  extraction, and rendering oracles now, before Phases 2–4 use them. Define the
  exact subprocess command, sandbox, timeout, memory/CPU cap, permitted exit
  codes, normalized comparison format, and retained artifacts for each tool.
  qpdf is a structural/interoperability oracle, not a strict ISO-conformance
  authority and never the sole writer validator.
- Record the tool/build identity, SHA-256/signature, acquisition recipe, enabled
  features, license/SBOM data, and versioned wrapper command in
  `tests/pdf/tools/manifest.json`; CI fails on drift. Oracle disagreement creates
  an adjudication record and never silently rewrites a golden.
- Establish a versioned clause-level writer conformance checklist and a
  `tests/pdf/performance-baseline.json` threshold file. The checklist names the
  applicable ISO clause, required/prohibited keys, value/version constraints,
  and evidence for every emitted construct. The threshold file names fixtures,
  runner profile, absolute memory/work caps, wall-time budgets, and permitted
  regression percentages. Phase 1 approves the schema, scenarios, pinned runner,
  hard resource budgets, and measurement method. Measured read/host thresholds
  are approved and must pass before Phase 5; pagination/writer/output thresholds
  are approved and must pass before Phase 7. Merely recording measurements
  cannot pass a preview or release gate.
- Establish harness infrastructure for syntax/object resolution, filters, CMaps,
  fonts, images, content interpretation, and writing. Pull requests run all
  minimized regressions plus bounded deterministic truncation/mutation; nightly
  jobs run coverage-guided campaigns for at least 30 minutes per enabled harness
  under an outer process time/RSS supervisor. A minimized failure records its
  input hash, seed, harness/tool version, limit profile, failure class, and
  corpus-rights disposition.
- Populate the Phase 0 corpus manifest. Keep only Broiler-authored synthetic or
  explicitly redistributable fixtures in-tree; a document-level license does
  not automatically clear embedded fonts, images, profiles, personal data, or
  rendered golden derivatives.
- Keep larger real-world/CVE corpora access-controlled and quarantined, fetched
  only by a pinned nightly job under their source terms. Do not publish, mirror,
  package, or cache ambiguous samples or their renders; recreate minimal
  synthetic fixtures when rights cannot be established.
- In `tests/pdf/tools/manifest.json`, also record exact source/release, commit or
  version, SHA-256/signature, selected license, dependency/asset SBOM, notices,
  acquisition method, exact build flags, enabled codec/font/CMap/ICC inventory,
  patent-review status, cryptographic provider/features, export-control and
  sanctions review where applicable, approved CI jurisdictions/use scope, and
  whether a binary is cached or redistributed. Disable unneeded codecs and
  assets. This
  tool inventory complements, but never substitutes for, the product feature
  register.
- Require independent tools to execute out-of-process in CI and remain absent
  from product references, NuGet packages, applications, and release containers.
  Process isolation is not a redistribution safe harbor: any distributed CI
  image or cached binary must still satisfy its license and notice obligations.
- Make the existing `Broiler.Media.Image.Managed` JPEG decoder and encoder an
  immediate affected-component release gate, even if PDF V1 uses only decode or
  byte preservation. Audit Annex-derived quantization/Huffman data, IJG quality
  scaling, `JpegOptimalHuffman` and its documented libjpeg
  `jpeg_gen_optimal_table` lineage, implementation/test-vector provenance, and
  IJG/libjpeg attribution or notice applicability before PDF depends on it or
  the Media package is approved with these assets.
- Add component-local `THIRD_PARTY_NOTICES.md` and SBOM entries whenever
  Documents, Graphics, or Media incorporates, derives from, or distributes
  third-party source or generated code, algorithms, constants, tables/data, test
  vectors, dependencies, fonts, CMaps, profiles, or other assets. Inspect the
  resulting `.nupkg`/`.snupkg` contents rather than relying only on project
  metadata.
- Add `Broiler.Documents` and affected Media/Graphics packages to the repository
  human-review and public-publish approval gate.

### 6.7 Exit gate

- Common options/limits contain no RTF- or PDF-specific member, and compatibility
  tests cover legacy overloads, typed requests, and wrong-option use.
- A non-seekable stream can be probed and read once without losing prefix bytes
  or making a second full copy. Cancellation, disposal, spool ownership, and
  synchronous/asynchronous behavior are covered for every adapter.
- Result tests distinguish success, caller-approved partial output, rejection,
  cancellation before/after commit, I/O failure, limit exhaustion, and every
  output commit state. No host commits a rejected or unapproved partial result.
- Every limit has `N-1`, `N`, and `N+1` tests plus aggregate many-small-resource
  cases; rejection occurs before the allocation/delegated decode that would
  exceed it. Page floods, many-small-stream/image attacks, and repeated Form
  invocation cannot bypass aggregate budgets.
- Tests prove that bounded transient parsing for a requested read is distinct
  from durable extraction/reuse and that unknown durable dispositions fail
  closed. Stable image identity and conversion context survive read-edit-write;
  without extraction permission no public image bytes enter the model, and
  without output permission no existing writer emits them.
- Resource tests cover same-context edits, cross-document copy/paste and merge,
  clone/deserialization, forged IDs, duplicate/colliding IDs, content-digest
  substitution, deterministic rekeying, and denial until the destination context
  records a fresh operation decision.
- Image-contract tests cover encoded and decoded `BImageResource` variants,
  defensive/transferred buffer ownership, digest binding, lifetime, retained-byte
  limits, legacy constructor/access behavior, and passage through model, a
  non-PDF writer/UI consumer, and Graphics without a backend handle or fake MIME.
- Graphics and Media reference no Documents assembly or Documents policy type.
  The bounded font inspector and instance image-service contracts pass malformed,
  allocation, work-limit, and cancellation tests before Phase 4 consumes them.
- Two independently constructed service graphs operate concurrently without
  sharing mutable state, caches, resource decisions, or diagnostics. Product
  projects reference concrete Media implementations only at composition roots;
  the PDF package references abstractions only. Missing optional services narrow
  the effective matrix deterministically, and missing writer services prevent
  output before any byte is emitted.
- Page break, justification, direction, resource identity, and finite measurement
  rules each have a named, active non-PDF consumer and owner-local tests.
- Model/UI tests cover 12-point inheritance, missing-family behavior, point-to-
  pixel conversion at multiple host DPIs, explicit/imported image dimensions,
  96-pixels-per-inch auto sizing, aspect-ratio inference, and rejection of zero/
  non-finite/uninspectable dimensions. Static analysis/tests prevent direct use
  of model point values as `SizeInPixels`.
- URI tests pin the selected RFC/IDNA/Unicode profiles and cover percent-
  encoding, normalization order, IDN/confusable/control/CRLF/user-info cases,
  length bounds, `mailto`, denied schemes, and every UI/DOM/writer activation
  path. No validation or activation-preflight path performs I/O.
- Shared additions have non-PDF consumer tests.
- Every in-tree fixture and generated golden has explicit redistribution and
  derivative-work approval.
- The test-tool manifest proves that no oracle is a product dependency and that
  redistributed CI artifacts satisfy notices and source obligations.
- Oracle versions, commands, isolation limits, and comparison roles are pinned;
  preview-required CI/harness definitions, the clause-checklist schema, benchmark
  scenarios, pinned runner profile, and hard resource budgets exist before parser
  implementation advances. Phase-specific measured thresholds activate at the
  Phase 5 and Phase 7 boundaries.
- Every fuzz failure is reproducible from its recorded seed or minimized fixture;
  the outer supervisor turns a hang or resource breach into a failing result.
- Existing managed JPEG decoder/encoder code, tables, algorithms, and test-vector
  provenance and required attribution are resolved before `DCTDecode` is marked
  supported or the affected Media release is approved.
- Documents, Media, and Graphics package notices, SBOMs, provenance records, and
  human-review coverage match all third-party or derived material they
  incorporate or distribute.
- Legacy text-only codec calls and all unaffected conformance suites remain
  green; resource-bearing callers/tests have migrated to explicit requests and
  assert the approved no-context rejection rather than implicit byte output.

## 7. Phase 2 — PDF syntax and object store

### 7.1 Deliverables

- Scaffold `Broiler.Documents.Pdf` and `Broiler.Documents.Pdf.Tests`.
- Add `%PDF-` signature probing, MIME/extension hints, and deterministic
  confidence.
- Parse the file-header version as provisional metadata for diagnostics. It does
  not by itself enable syntax, operators, filters, or writer behavior; Phase 3
  resolves the effective declaration after loading the Catalog.
- Implement bounded tokenization for:
  - whitespace and comments;
  - integers and real numbers;
  - names and `#xx` escapes;
  - literal and hexadecimal strings;
  - arrays, dictionaries, booleans, and null; and
  - indirect objects/references and streams.
- Implement checked offset, length, generation, and allocation arithmetic.
- Implement classic xref tables, trailers, `startxref`, and incremental `/Prev`
  chains. Tokenize stream objects, but defer xref streams, object streams, and
  hybrid-reference files until the production filter pipeline exists in Phase 3.
- Resolve the latest object revision deterministically.
- Use explicit stacks and cycle detection rather than unbounded recursion.
- Permit only narrowly documented recovery; do not scan arbitrary input until
  something resembles an object.
- Record the governing ISO 32000-1 clause and approved implementation provenance
  for each syntax family without reproducing restricted standard text.

### 7.2 Exit gate

- Every syntax construct has positive, truncated, malformed, and boundary
  tests.
- Classic and classically incrementally updated fixtures resolve identically to
  independent tools. Streamed, hybrid, and object-stream fixtures produce a
  specific not-yet-supported structural diagnostic and are not claimed by this
  phase.
- Every truncation point and deterministic byte mutation terminates within
  bounds.
- Parser implementation/provenance records contain no copied renderer code,
  tables, or unclear historical material.
- No model-projection or rendering concerns exist in this layer.

## 8. Phase 3 — Streamed object store, document structure, filters, and safe feature detection

### 8.1 Deliverables

- Implement one bounded PDF-owned production stream-filter pipeline before any
  xref or object stream is resolved:
  - Flate with PNG/TIFF predictors;
  - LZW only after the focused register entry confirms the exact PDF algorithm,
    implementation provenance, historic core-patent status, and absence of an
    unapproved proprietary variant or optimization;
  - ASCIIHex;
  - ASCII85;
  - RunLength; and
  - chained filters with per-stage and total budgets.
- Apply per-stage and aggregate decoded-byte, expansion, work, allocation, and
  cancellation accounting to structural streams. An xref/object stream using an
  unsupported or legally uncleared filter is rejected specifically; it is never
  treated as a generic corrupt object.
- Implement xref streams and hybrid/incremental `/Prev` chains through that
  production pipeline far enough to determine the effective trailer. There is no
  bootstrap or test-only decoder with different security behavior.
- Inspect every effective classic trailer and xref-stream dictionary for
  `/Encrypt` immediately after structural xref discovery. V1 rejects the file at
  that point, before resolving object-stream members or interpreting any
  decrypt-dependent object, string, Catalog, metadata, font, image, annotation,
  or content. Diagnostics expose neither passwords nor document content.
- For unencrypted files, resolve object streams and the latest object revision
  deterministically, including hybrid and incremental interactions.
- Load the Catalog, page tree, inherited resources, MediaBox/CropBox, rotation,
  and UserUnit. Resolve resources lazily and guard page/resource/Form XObject
  cycles.
- Resolve the effective declared version from the header and Catalog `/Version`
  according to ISO 32000-1. Inventory Catalog `/Extensions` prefix, base version,
  and extension level without interpreting extension-defined behavior. Dispatch
  remains exclusively feature-matrix based; a declaration cannot enable PDF 2.0
  or developer-extension semantics.
- Classify image filters before decoding content:
  - `DCTDecode`: only the Phase 1-approved 8-bit, one- or three-component,
    Huffman-coded SOF0/SOF2 tuple is eligible for V1 decode. Admission also checks
    table sources/counts, sampling factors, restart behavior, marker classes,
    PDF ColorSpace and `/Decode`, `DecodeParms/ColorTransform`, and APP14
    presence/value;
  - support only explicitly listed combinations. An unsupported precision,
    ambiguous or uncleared APP14/`ColorTransform`, unsupported PDF color space,
    invalid marker/table/scan boundary, or unsupported component tuple is
    rejected before Media allocation; component count is never used to guess
    color interpretation;
  - four-component CMYK/YCCK conversion and arithmetic-coded, lossless,
    hierarchical, JPEG-LS, and JPEG XR data are rejected in V1; and
  - `JPXDecode`, `JBIG2Decode`, and `CCITTFaxDecode` are detected-but-skipped
    with stable diagnostics pending their separate post-V1 approvals.
- Snapshot the official
  [T.81 declaration record](https://www.itu.int/ITU-T/recommendations/related_ps.aspx?id_prod=2633)
  for the supported JPEG modes.
  PDF `ColorTransform` and Adobe APP14 interpretation receive their own source/
  IP review even for three-component JPEG. JPEG codec approval does not clear
  APP14, `ColorTransform`, or CMYK/YCCK conversion, and approval of those color
  rules does not clear JPEG codec patents. An open-source implementation license
  does not clear third-party standards patents.
- Parse only the V1 Info fields and independently approved XMP/RDF/XML namespaces
  through a bounded, non-resolving reader without a DOM dependency. Disable DTDs,
  entity expansion, external entities, schemas, file access, and network access;
  enforce byte, node, depth, namespace, property, attribute, and decoded-text
  budgets.
- Keep Info and XMP source provenance. Well-formed RDF/XML accepted by the pinned
  XMP subset supplies a normalized field when present, Info fills an absent field,
  and disagreement emits
  `pdf.metadata.conflict` naming only the field. Malformed XMP does not suppress
  valid Info. Unknown keys/namespaces are skipped rather than opaquely carried
  forward. **Done** as of 2026-09-01: IP-004 approved the ISO 16684-1:2019 read
  subset, and `XmpReader` implements it. Preservation stays out of scope, and the
  matrix entry stays `Candidate`: IP-001 has since cleared too, and what holds
  every entry short of `Supported` is now the provenance and wording rows rather
  than any construct question.
- Inventory annotations, outlines, forms, actions, embedded files, and
  signatures after the encryption rejection point.
- Treat URI/action values as inert source-labelled data and never fetch them.
  Classify URI, JavaScript, Launch, GoToR, SubmitForm, ImportData, embedded-file,
  and unknown actions separately; no non-URI action may be projected as a link.
- Detect JavaScript, Launch, rich media, and attachments but never execute or
  instantiate them.

### 8.2 Exit gate

- Page counts, boxes, rotation, and inherited resources match independent
  tools.
- Classic, streamed, hybrid, object-stream, and incrementally updated fixtures
  resolve identically to independent tools through the same filter pipeline.
  Predictor, truncation, unsupported-filter, cancellation, decompression-bomb,
  and per-stage/aggregate expansion tests cover structural and content streams.
- Fixtures cover lower/equal/higher, malformed, indirect, and conflicting header
  and Catalog versions plus known/unknown extension declarations. No declaration
  bypasses the approved feature matrix.
- Flate, LZW, and eligible DCT modes have approved entries in the IP/licensing
  register before their behavior state becomes `supported`. **Met as of
  2026-09-01**: IP-011 (with IP-023 confirmed), IP-010, and IP-005/IP-006 are all
  approved. The rule stands for anything added later.
- The JPEG corpus crosses SOF family, precision, component count, tables,
  sampling, APP14, PDF `ColorTransform`, PDF ColorSpace, `/Decode`, marker
  lengths, scan/restart counts, and truncation. Every accepted tuple has
  standards/IP and color-oracle approval; all other tuples fail deterministically
  without partial-color output. JPX, JBIG2, and CCITT cases remain distinct; no
  generic "corrupt image" result hides a policy rejection.
- Metadata fixtures cover Info-only, XMP-only, agreement/conflict, missing versus
  empty, malformed encoding/XML, invalid and zone-less dates, invalid Unicode,
  oversized data, DTD/entities, and unknown namespaces. Parsing performs no I/O,
  carries no source value into diagnostics, and does not authorize output reuse.
- Cyclic or hostile graphs terminate predictably.
- Encrypted and active-content cases produce specific diagnostics without side
  effects.
- Classic-trailer and xref-stream `/Encrypt` fixtures prove rejection occurs
  before object-stream, Catalog, metadata/XML, font, image, annotation, or
  content services are invoked, including incremental trailers that introduce
  or alter encryption.

## 9. Phase 4 — Logical import into `RichTextDocument`

Phase 4 may start only after the Phase 1 bounded font inspector, Media image
inspection/decode service, resource-context, result-status, and limits gates
pass. PDF code must not route embedded fonts or images through an unbounded
legacy helper, global codec registry, ambient font resolver, or direct reference
to `Broiler.Media.Image.Managed`.

### 9.1 Content interpretation

- Implement the graphics and text state needed for extraction:
  - `q`/`Q` and `cm`;
  - `BT`/`ET`;
  - font selection and text matrices;
  - character/word spacing, horizontal scale, leading, rise, and render mode;
  - `Tj`, `TJ`, quote operators, and positioning operators;
  - `Do` for approved Form and Image XObjects with bounded recursion/resource
    lookup;
  - bounded inline-image `BI`/`ID`/`EI` parsing that cannot terminate by an
    unbounded byte scan; and
  - marked-content `ActualText`.
- Classify invisible text rendering mode, clipping-only text, optional-content
  groups/membership dictionaries, marked-content artifacts, annotation
  appearances, off-page content, and unapplied Redact annotations. V1 does not
  claim a visibility-faithful result: uncertainty makes the result `Partial` with
  a structured diagnostic rather than silently calling content visible/hidden.
  An unapplied Redact annotation emits a high-severity disclosure warning;
  overlays never count as deletion.
- Support the initial font subset:
  - Standard/simple Type 1 dictionaries, widths, and approved encodings without
    executing a Type 1 program;
  - only the exact Phase 0-approved TrueType/OpenType container, table, and
    outline tuples through the Phase 1 bounded inspector;
  - Type 0/CID dictionaries and descendant-font tuples explicitly named in the
    feature matrix; an unsupported font program may not be parsed merely because
    a ToUnicode map exists;
  - Encoding Differences; and
  - ToUnicode CMaps, `bfchar`, `bfrange`, and bounded `usecmap`.
- Register the provenance and redistribution terms of every non-code font asset.
  `Broiler.Documents.Pdf` owns/notices PDF-only Standard 14 metrics, encoding
  vectors, predefined CMaps, and character collections; Graphics owns only
  generally reusable shaping and glyph-mapping data unless another consumer
  justifies promotion. None is implicitly cleared merely because an algorithm
  or identifier is standardized.
- Treat an input PDF's embedded font program as document-scoped content. Parse
  it only through bounded services for the current document; do not install it,
  persist it in a cross-document cache, expose its bytes, bundle it as a
  fallback, or automatically carry it into a newly written PDF.
- Detect Type 3 and unsupported font programs; do not fake reliable text
  without a mapping.

### 9.2 Reading order and model projection

- Prefer valid `ActualText`.
- Group glyphs deterministically into words, lines, blocks, and paragraphs
  using baselines, direction, spacing, and columns.
- Document the heuristic and emit `pdf.import.reading-order-heuristic` whenever
  geometry rather than trustworthy logical information determined order.
- Map font family, size, weight, slant, color, decoration, alignment, spacing,
  links, and lists only when evidence is reliable.
- Remove subset prefixes from font family names using PDF metadata, not
  substring-based bold/italic guessing.
- Never retain hidden coordinate side channels in the rich-text model.
- Source PDF page boundaries remain source-location/extraction boundaries by
  default and do not imply layout preservation. An explicit read option may map
  them to the shared paragraph page-break property; the result and documentation
  state that re-pagination can still differ.

### 9.3 Images and links

- Recognize Image XObjects and inline images. The initial raw-sample subset is
  DeviceGray at 1/2/4/8 bits, DeviceRGB at 8 bits, and Indexed at 1/2/4/8 bits
  over a bounded DeviceGray/DeviceRGB palette, with explicitly validated
  `/Decode` handling. ICCBased, CalGray/CalRGB, Lab, Separation, DeviceN,
  ImageMask, color-key `/Mask`, and `/SMask` are detected-but-skipped in V1
  unless a narrower tuple is separately added to the approved matrix.
- Before placing bytes or pixels in the model, evaluate `ExtractToModel` and add
  the stable resource identity and decision to the conversion context. Without
  permission, do not construct `InlineImage`; omit the resource and emit a stable
  diagnostic.
- Preserve a compatible DCT/JPEG resource byte-for-byte only when
  `PreserveEncodedBytes` is separately allowed, `/Decode` is identity, PDF and
  JPEG color interpretation agree, no mask/profile/conversion is needed, and
  the encoded `BImageResource` variant can retain the form safely. Byte preservation
  neither approves decode nor authorizes later redistribution.
- Decode only the Phase 3-approved JPEG tuple through the reviewed instance Media
  service, passing the stricter remaining PDF/Media limits and cancellation.
  V1 maps one component only to DeviceGray and three components only to
  DeviceRGB under the approved APP14/`ColorTransform` rule and supported
  `/Decode`; ICCBased, calibrated, indexed-DCT, Lab, Separation, DeviceN, and
  four-component mappings remain unsupported.
  Normalize approved raw non-DCT samples to the Media decoded-pixel payload and
  wrap it in `BImageResource`; never label PDF sample bytes as PNG/JPEG or expose
  them through an unrelated encoded MIME contract. Newly encoded V1 raster data
  uses Flate rather than an incidental JPEG encoder.
- Preserve PDF DeviceRGB as explicitly uncalibrated RGB in the shared payload;
  do not relabel it sRGB or imply colorimetric fidelity. UI display and PDF
  re-emission use the documented device-RGB assumption and emit the feature-
  matrix color-approximation diagnostic where fidelity is uncertain.
- Emit specific diagnostics for four-component CMYK/YCCK conversion,
  arithmetic/lossless/hierarchical JPEG, JPEG-LS/XR, JPX, JBIG2, and CCITT
  rather than delegating them to an ambient platform decoder.
- Create `InlineImage` only when an image can be placed meaningfully in reading
  order.
- Diagnose floating/background/vector artwork that cannot be represented.
- Create `LinkHref` only for a URI action that passes the shared active-output
  policy. Denied, malformed, non-URI, relative, or custom-scheme actions remain
  diagnostics/inert source information and cannot become active through a later
  DOCX, HTML, or PDF writer. Every writer revalidates immediately before output.
- Defer internal destinations until a cross-format bookmark/anchor model exists.
- Return an explicit "OCR required" diagnostic for scanned pages without text.

### 9.4 Exit gate

- Unicode text exactly matches owned goldens for the declared supported subset.
- Paragraph/run/style output is deterministic across platforms.
- Every unsupported font, image, action, or ambiguous layout emits a stable
  `pdf.*` diagnostic.
- No empty or partial extraction is silently reported as success.
- Fixtures cover rendering mode 3, clipping, off-page text, optional content,
  artifacts, overlay-only redaction, unapplied Redact annotations, attachments,
  and incremental revisions. Documentation and results never equate logical
  extraction with visible-only extraction, deletion, sanitization, or redaction.
- Standard 14 metrics, glyph-name mappings, predefined CMaps, and other
  distributed font data have approved provenance, license, notices, and package
  placement.
- No input font program survives beyond the document-scoped operation or is
  re-embedded by a later conversion without a new caller-supplied licensed
  resource.
- JPEG fixtures cover the exact supported/rejected mode boundary and carry
  independent rights/provenance records.
- Image fixtures cover XObject/inline forms, each declared bit depth/color space,
  `/Decode`, masks, palettes, resource denial, aggregate pixel limits, and stable
  resource identity. An unapproved extraction returns no accessible bytes.
- Embedded-font parsing uses only the bounded shared Graphics service, and the
  PDF reader uses no `BImageCodecs`, `BTextMeasurer`, installed-font discovery,
  or mutable global service.
- The minimum hostile-input gate runs minimized regressions plus bounded
  deterministic mutation/truncation and fuzz campaigns over syntax,
  xrefs/objects, filters, CMaps/fonts, images, and content interpretation. There
  is no unresolved crash, hang, stack exhaustion, limit escape, excessive
  allocation, nondeterminism, or cross-document state leak before Phase 5 begins.
- No PDF-specific object leaks into `Broiler.Documents.Model`.

## 10. Phase 5 — Read-preview integration and artifact replacement

The complete Phase 4 exit gate is the reader-core readiness subgate. After it
passes, Phase 5 builds a non-public integration candidate with `CanRead=true` and
the selected registrations below. Only that candidate is used for host/package
tests; the capability is published only after the complete Phase 5 exit gate.

### 10.1 Deliverables

The Writer half of this list has landed as the integration candidate: catalog
construction moved to `WriterDocumentFormats`, which each composition root
builds; the Windows and Linux heads register `PdfDocumentCodec` for opening; the
desktop open path runs through `DocumentCodecCatalog.SelectAndRead` over a
`DocumentInput` rather than `File.ReadAllBytes` plus a separate probe; `.pdf` is
in the open filters and in no save filter; and a rejected read — or one that
recovered no text at all — leaves the open document in place and says why.

Outstanding: the CLI and `BrowserWriterDemo` migrations, `DocumentConversionContext`,
explicit confirmation before a partial read replaces the open document (today it
is committed and the status line reports it as partial rather than clean), host
cancellation reaching probing and materialization, the composed managed JPEG
service, the support-wording review, and every §10.2 exit-gate item.

- Move catalog construction out of shared Writer internals and inject catalogs
  and codec services from each platform composition root. Register
  `PdfDocumentCodec` explicitly in CLI, Windows Writer, and Linux Writer;
  register Android only after its package-size/memory/runtime gates and
  WebAssembly only after its encoded-input, peak-heap, trimming, and AOT gates.
  Never add PDF to a shared hard-coded catalog that silently enables every head.
- Add explicit project/package references at enabled composition roots, not only
  solution membership. CLI and desktop compose the reviewed managed JPEG service
  through the Media abstraction. The PDF project retains no concrete Managed,
  UI, platform, or global-registry dependency.
- Migrate CLI, desktop Writer, and BrowserWriterDemo to the shared
  selection-and-`DocumentInput` path. They do not use unbounded `ReadAllBytes`,
  duplicate `MemoryStream` probing, unconditional `CopyTo`, or unrestricted
  base64 decoding before limits apply. Host cancellation reaches probing,
  materialization, parsing, delegated codecs, and projection.
- Retain `DocumentConversionContext` beside the editor document. New clears it;
  Open replaces it atomically with the accepted result; editing preserves and
  prunes resource identities; inserted resources receive caller-approved entries;
  cross-document paste/merge rekeys and requests new decisions; Save supplies it
  to the writer.
- Add `.pdf` to appropriate open filters, but not save filters until Phase 7.
- Use the existing conversion workflow:
  - `--convert-doc input.pdf --output output.docx`;
  - `--convert-doc input.pdf --output output.txt`.
- Do not reintroduce the dedicated external `--convert-pdf` process contract.
- Report diagnostics and unsupported-feature counts in CLI/Writer status.
- A rejected read returns a nonzero CLI status, writes no output, and never
  replaces the current Writer document. A partial read is committed only under
  an explicit CLI option or user confirmation and uses a distinct exit/status;
  empty extraction is never undifferentiated success. Default conversion omits
  source metadata and any resource bytes lacking explicit output permission.
- Publish descriptive support wording tied to the feature matrix and exact
  ISO 32000-1:2008 subset. Do not use Adobe's PDF file icon, certification
  marks, or names of reference tools in a way that implies affiliation,
  endorsement, or full-format certification.
- Document that callers remain responsible for authority to extract, copy, and
  transform ordinary document text, metadata, and images; successful parsing is
  not a license grant and the conversion workflow does not automatically
  republish source assets.
- Add the project/tests to `Broiler.Documents.slnx`, aggregate solution
  generation, packaging, and relevant Writer/CLI solutions.
- Keep preview artifacts prerelease. Package-content tests prove earlier phases
  did not ship the assembly and Phase 5 exposes no PDF save filter, destination,
  or `CanWrite=true` path.

### 10.2 Exit gate

- PDF import works in-process with no external executable.
- Batch conversion is reentrant and has no mutable global parser/font/image
  registry.
- Oversized seekable/non-seekable/base64 inputs are rejected before full
  materialization, and probing plus reading observe identical bytes. Two
  concurrent conversions with different services, policies, and limits cannot
  affect each other.
- Platform tests prove PDF is present only in explicitly enabled heads. Android
  and WebAssembly cannot acquire it transitively through shared Writer code.
- Open-edit-save retains metadata/resource context and cannot bypass policy
  through a static writer helper. Rejected input leaves the active document and
  destination unchanged; partial input follows the explicit host policy.
- Windows and Linux tests pass; WebAssembly is enabled only after AOT and
  bounded-memory evidence.
- The read-preview support statement exactly names the supported subset,
  identifies PDF 2.x handling as header/construct tolerance only, and passes the
  claims/trademark review.
- Host integration extends the Phase 4 hostile-input gate to catalog selection,
  stream replay/materialization, base64 handling, cancellation, and result
  commit. It has no unresolved crash, hang, limit escape, excessive allocation,
  or cross-document state leak. Phase 8 extends this evidence rather than
  introducing fuzzing for the first time.
- The read, materialization, host-memory, and enabled-platform thresholds in
  `tests/pdf/performance-baseline.json` pass on the pinned runner for the exact
  read-preview candidate.

## 11. Phase 6 — Shared export foundation

This phase can run in parallel with parser Phases 2–5 only after the Phase 1
bounded-font, resource-context, and unit/script decisions pass.

Consume the Phase 1 canonical unit/default rule: model typography, pagination,
and backend-neutral page scenes use points; UI conversion has already migrated.
Geometry remains `double` until a documented serializer quantization rule. DPI,
locale, backend, display fallbacks, and host platform never affect pagination.

### 11.1 `Broiler.Documents.Pagination`

Create a headless, UI-free paginator supporting:

- explicit point units, page size, orientation, and margins;
- paragraph spacing and line spacing;
- wrapping, instance-based shaping for only the declared V1 script matrix,
  lists, indentation, alignment, bidi where that matrix permits it, and page
  breaks;
- inline images, highlights, underlines, strikethrough, and link rectangles;
- deterministic page and resource ordering; and
- overflow diagnostics for content too large to place.

The required V1 writing baseline is horizontal left-to-right Latin, Greek, and
Cyrillic with the explicitly tested combining/ligature subset. Arabic, Indic and
other complex-script shaping, bidirectional/RTL output, vertical writing, emoji
sequence shaping, variable fonts, and color glyphs remain rejected unless the
Phase 0 matrix is amended and the same Graphics/pagination/legal gates pass for
the exact addition. Unicode storage and ToUnicode mapping do not imply that a
script can be laid out. Unsupported runs fail writer preflight; they are never
silently emitted with nominal glyph ordering.

- Produce an immutable `PaginatedDocument` containing ordered pages, each
  page's immutable Graphics scene, semantic link regions, source mappings,
  resource table, and diagnostics. Resolve fonts, shape, line-break, paginate,
  and place images exactly once; serializers/renderers cannot remeasure,
  reshape, or re-resolve resources.
- Define orphan/widow behavior, unbreakable-run handling, list-marker width,
  overflow, page-break-before, source mapping, and resource lifetime. A page
  scene contains no disposable backend handle or ambient resource.

Extract only behavior demonstrated to be correct and backend-neutral from
`StandardRichEdit`. Replace its character/whitespace wrapping and static
measurement path with the shared instance line-layout service, then make
RichEdit print/preview or layout tests its second consumer; the legacy algorithm
is not retained merely to describe the work as extraction.

### 11.2 `Broiler.Graphics`

Add the export-relevant shared capabilities:

- explicit instance-based font resolver and metrics service;
- immutable font-face resources with controlled byte ownership;
- shaped glyph runs carrying glyph IDs, positions, clusters, direction, and
  Unicode mapping;
- immutable backend-neutral point-space page primitives for solid
  rectangles/decorations, positioned glyph runs, and references to the same
  `BImageResource` used by the document model;
- reuse of the Phase 1 bounded font inspector plus export-grade TrueType
  sanitization. V1 writer embedding/subsetting is limited to approved
  OpenType/TrueType fonts with `glyf` outlines; CFF/CFF2, variable, color, SVG,
  and bitmap-font output remain post-V1 unless separately added to the matrix;
- strip `fpgm`, `prep`, `cvt `, and simple/composite glyph instructions from V1
  subsets, recompute affected metrics/maxima/checksums, and validate the result.
  Opaque hinting bytecode is never emitted for downstream readers to execute. If
  safe stripping conflicts with `no subsetting`, required byte preservation, the
  font license/policy, or font validity, reject the font before output;
- font subsetting and technical embedding-right checks, including
  [OpenType `OS/2.fsType`](https://learn.microsoft.com/en-us/typography/opentype/spec/os2)
  restricted, preview-and-print, editable, no-subsetting, and bitmap-only cases;
  and
- `BImageResource` preservation through model, pagination, scene, UI/print, and
  writer paths rather than backend-only handles or parallel payload types.

A positioned glyph run carries font-resource identity, glyph IDs, advances,
offsets, clusters, direction, and source Unicode needed for ToUnicode. Inventory
and migrate static text paths: Pagination and its tests use no `BTextMeasurer`,
`FallbackSystemFont`, installed-font discovery, process-global provider, or
family-name-only render command. Any future bidi/complex-shaping support must
either harden the existing `ComplexTextShaper` to its declared matrix or replace
it; its pragmatic subset is not evidence of complete Unicode support.

Do not require the full native PDF rendering vocabulary for V1 export.
Arbitrary paths, path clipping, gradients, soft masks, patterns, and faithful
affine image replay become prerequisites only for the later native-rendering
track.

### 11.3 Font and embedding-license policy

- Never select fonts through ambient installed-font discovery.
- Require caller-supplied deterministic font resources or an explicitly
  licensed shared fallback font.
- Before enabling `CanWrite` in an official host, choose one operational path:
  ship a specifically approved, package-tested fallback font with all required
  notices and generated-document obligations, or require an explicitly
  configured caller font set and present a preflight/UX failure when absent.
  CLI documentation/options and every Writer head implement the same decision;
  a roadmap promise without provisioned fonts is not a working save feature.
- Require an explicit license disposition for each font resource covering the
  intended embedding, subsetting/modification, redistribution, commercial use,
  target platforms, and obligations attached to each generated document. The
  shared resource-use policy carries the caller decision; `fsType` is a
  technical signal and enforcement input, not a substitute for the font EULA or
  other actual grant, and Broiler does not determine the caller's legal title.
- Fail closed on restricted, invalid, ambiguous, bitmap-only-without-bitmaps, or
  legally unknown resources. Honor `no subsetting`; define and document the
  permitted output behavior for preview-and-print and editable embedding rather
  than silently choosing the least restrictive interpretation.
- Produce a stable diagnostic and caller-controlled licensed fallback decision
  when a requested font cannot be embedded. Never substitute an ambient OS font.
- Do not treat a font extracted from an input document as caller-supplied export
  authority; import-to-export conversions resolve a new approved font resource.
- Record and ship the license/attribution required by any bundled fallback font,
  including Reserved Font Name or modified-font naming obligations where
  applicable. Separately record whether each generated PDF must carry
  attribution, a license copy, modified naming, source availability, or another
  notice; the writer must fulfill that obligation or reject the resource. Do not
  assume that a freely downloadable font is redistributable.
- Ensure the same fixed font set produces identical font resources, shaping, and
  page-scene geometry on Windows and Linux and on every additionally enabled
  target. Equivalent WebAssembly evidence is required before that head is
  enabled, not before desktop Phase 6 can exit.

### 11.4 Exit gate

- Pagination goldens cover long paragraphs, multiple pages, lists, every script/
  direction tuple declared supported, rejected complex-script cases, images,
  links, and explicit page breaks.
- RichEdit/print and PDF-facing tests consume the same line/pagination logic.
- Glyph runs and font subsets are deterministic and independently validated.
- `PaginatedDocument` contains no UI, backend image handle, family-name-only text
  command, PDF type, ambient resource, or mutable global dependency. PDF-facing
  and RichEdit-facing tests receive identical line breaks, glyph positions,
  direction, page breaks, and link geometry; paginated scene geometry is
  identical at different host DPIs.
- Font-policy tests cover every `fsType` state, absent/invalid flags,
  no-subsetting, bitmap-only, license rejection, hint-bytecode stripping/conflict,
  and deterministic fallback.
- Resource-policy tests default unknown dispositions to deny and verify that
  generated-document obligations are emitted or cause a stable rejection.
- Every bundled font, shaping table, glyph mapping, and other export asset has
  an approved license record, component notice, and package-content test.
- No UI, DOM, HTML, or platform reference enters the pagination assembly.

## 12. Phase 7 — Deterministic PDF writer

### 12.1 Deliverables

Writer-core readiness is a non-public subgate. It passes when serializer unit/
property tests, preflight rejection, output-limit accounting, deterministic
pagination consumption, structural fixtures, and required services operate
without application registration. Only then may a test-only write-preview
candidate enable the catalog destinations needed to run the remaining Phase 7
integration gate.

- Finalize `PdfWriteOptions` with PDF-only values: point-space page settings,
  PDF 1.7 serialization/quantization and compression choices, deterministic mode,
  caller-controlled PDF file-identifier inputs, and `PdfLimits`. Normalized
  metadata/dates, conversion/resource context and policy, font-resource bindings,
  generated-document obligation sink, destination, cancellation, and staging/
  commit policy remain solely on `DocumentWriteRequest` or the immutable codec
  service graph and cannot be overridden by PDF options. No request, option, or
  default reads the clock, machine, locale, installed fonts, or a global image
  catalog.
- Paginate and preflight all fonts, images, links, metadata, permissions,
  obligations, unsupported model features, and limits before writing the first
  destination byte. The writer consumes `PaginatedDocument` positioned
  glyph/resource data without font resolution, shaping, measurement, image
  decoding, or re-layout.
- Immediately before byte-preserving DCT embedding, rerun the bounded Media/PDF
  inspection of marker lengths, SOF mode, precision, components, tables, scans,
  APP14, `ColorTransform`, ColorSpace, `/Decode`, and dimensions under the current
  limits. Caller-created, edited, or previously admitted bytes are never trusted
  from provenance/policy alone; structural or color mismatch rejects preflight.
- Keep `PdfDocumentCodec.CanWrite=false` in ordinary packages until writer-core
  readiness passes. Set it true only in the fully composed test candidate, and
  only when every required service is present. Publish that capability only
  after the complete Phase 7 exit gate passes on the same candidate.
- Emit new PDF 1.7 files with:
  - header, catalog, page tree, resources, content streams, xref, trailer, and
    EOF;
  - stable object numbering and resource names;
  - Flate-compressed streams;
  - page boxes and only explicitly supplied normalized metadata; missing dates
    are omitted, raw XMP preservation/general XMP writing is outside V1, catalog
    language is emitted where applicable, and deterministic author/keyword
    serialization is documented;
  - Unicode Type 0 fonts, subsets, widths, and ToUnicode maps;
  - text, colors, highlights, decorations, lists, and inline raster images,
    using Flate for newly encoded samples and only approved byte-preserving DCT
    resources allowed by the shared resource-use policy; and
  - external-link annotations that pass the current output URI policy. V1 emits
    no `/Alt`, structure tree, tagged semantics, or accessibility claim.
- Support non-seekable output by tracking byte offsets during emission.
- File-based hosts stage in the destination directory and atomically replace only
  after success; failure/cancellation removes the staged file and preserves any
  existing destination. Browser downloads are offered only after a complete
  bounded output buffer exists. For a caller-owned arbitrary stream, document
  that I/O failure or cancellation after emission starts may leave an unusable
  prefix; stop at the next cancellation checkpoint and return `Rejected` with
  `PartialDestination`, never success. Cancellation before the first byte returns
  `NotStarted` and writes nothing. `RequireAtomic` with no caller-provided staging
  capability rejects before output. Policy/validation rejection always occurs
  before output.
- Make creation/modification dates and identifiers caller-controlled;
  deterministic mode must not read the clock, machine name, locale, or installed
  fonts.
- Preserve original logical text in ToUnicode mappings where presentation-only
  capitalization changes glyph appearance.
- Emit diagnostics for unsupported model features rather than silently
  rasterizing.
- Revalidate every link immediately before emitting an annotation. A reader's
  inert value or earlier acceptance is not output authorization; canonicalization
  cannot turn a denied target into an allowed one.
- Do not add a JPEG encoder, transcode to JPEG, or infer permission to embed a
  font/image from its presence on the machine or in a previously read document.
- Generate the exact support/conformance statement from the approved feature
  matrix. Do not call Broiler an ISO 32000-1-conforming reader or processor on
  the basis of an arbitrary supported subset. Claim only that documented
  feature subset, while separately validating that every emitted file satisfies
  all applicable ISO 32000-1 requirements. Do not market output as
  Adobe-certified, patent-free, or endorsed by an oracle.
- Complete the clause-traceability matrix for every emitted construct: exact
  specification edition/clause, required/prohibited keys, value and version
  constraints, and named structural, semantic, text, color, font, annotation,
  metadata, and interoperability evidence. Reopening with Broiler, qpdf, or a
  common viewer is necessary but never sufficient proof of ISO conformance.
- After writer-core readiness, add `.pdf` to the test candidate's CLI
  destinations and save filters of each independently enabled Writer head so the
  complete integration gate can run. Route all paths through the
  catalog-selected `PdfDocumentCodec.Write`; remove or adapt hard-coded output
  switches/static helpers that would bypass write requests and resource policy.
  Do not publish those paths until the Phase 7 exit gate passes. Android and
  WebAssembly output remain disabled until their own matrix gates pass.
- Do not implement incremental save, linearization, encryption, or a hidden
  raster-page fallback in V1.

### 12.2 Exit gate

- Xref offsets, stream lengths, references, and resource dictionaries pass
  independent structural validation.
- Files open in at least two pinned independent-reader engine lineages from the
  tool manifest under their exact commands and warning/error policy.
- Copy/paste/extracted text matches the source model under fixture-specific,
  versioned normalization using the pinned independent extraction oracle.
- Reference renders from two independent renderers meet declared tolerances.
- The same input, options, and font resources produce byte-identical output.
- The pagination, writer, output-size/memory, and enabled-host thresholds in
  `tests/pdf/performance-baseline.json` pass on the pinned runner for the exact
  write-preview candidate.
- Deterministic model/options/resource mutation regressions and bounded writer
  fuzz/property campaigns pass with no unresolved crash, hang, nondeterminism,
  limit escape, cross-document state leak, or structurally/semantically invalid
  output before `CanWrite` or a save destination is published.
- CLI document-to-PDF conversion, desktop Save As PDF, and every enabled
  platform path pass clean-package end-to-end tests. Save filters and `CanWrite`
  remain disabled when required services or gates are absent.
- A missing/rejected font, denied resource/link, invalid metadata value, output
  limit, unsupported script, or cancellation before commit emits zero
  destination bytes and does not modify an existing file. Open-edit-save
  metadata/resource behavior matches the conversion-context contract.
- Direct-stream tests cancel before and after the first emitted byte and verify
  `NotStarted` versus `PartialDestination`; atomic requests without staging
  reject before output, and staged/file/browser destinations never expose a
  partial artifact.
- Non-seekable output performs no seek and passes structural offset validation;
  deterministic compressed bytes are pinned across supported runtimes or the
  runtime/compressor boundary is explicitly part of the deterministic contract.
- Every emitted font and preserved image has a recorded caller/resource policy
  decision. The writer fulfills or rejects every generated-document attribution,
  license-copy, naming, or source obligation; every package-bundled fallback
  asset has its separate required notice.
- Reader and writer are not each other's only oracle.
- The clause matrix is complete, negative fixtures prove prohibited combinations
  are rejected, and each public claim names its evidence. “Opens in readers” or
  “qpdf reports no structural error” alone cannot pass this gate.

## 13. Phase 8 — Hardening, IP/licensing evidence, and release

### 13.1 Verification

- Continue the Phase 1/5 syntax, xref/object, filter, CMap/font, image, content,
  and writer fuzz/property harnesses; Phase 8 does not introduce fuzzing for the
  first time. Pull requests run minimized regressions plus bounded deterministic
  truncation/mutation campaigns.
- Nightly/release harnesses run out of process under an outer wall-time and RSS/
  CPU supervisor. Record input hash, seed, harness/tool/build identity, limit
  profile, failure class, rights disposition, and minimization status. A hung
  target is killed and fails the run; no in-process timeout is trusted to contain
  a parser fault.
- Before stable release, run at least 24 aggregate CPU-hours of coverage-guided
  fuzzing on the release commit across the enabled harnesses. There may be no
  unresolved crash, hang, stack exhaustion, limit escape, excessive allocation,
  nondeterminism, or cross-document state leak. Every resolved failure becomes a
  deterministic regression subject to corpus rights.
- Maintain a malicious corpus covering cycles, huge lengths, deep containers,
  xref loops, many-small-object/stream/image attacks, decompression bombs, page
  floods, font bombs, image pixel bombs, hostile XML/XMP, link normalization, and
  action payloads.
- Differential text and page-geometry checks.
- Independent structural validation with pinned, test-only, out-of-process
  tools. Prefer [qpdf](https://github.com/qpdf/qpdf) as the Apache-2.0 structural
  oracle, while retaining its license/NOTICE and auditing the providers enabled
  in the exact build. Its structural success is not a strict PDF-validity or ISO
  conformance determination; record the scope and limitation of every oracle.
- Render comparisons using at least two independently approved and pinned
  renderers, with these candidate-specific rules:
  - [PDFium](https://pdfium.googlesource.com/pdfium/+/refs/heads/main/LICENSE)
    requires the complete dependency/asset SBOM and notices for the exact build;
    its top-level license alone is insufficient;
  - [Poppler](https://gitlab.freedesktop.org/poppler/poppler/-/blob/master/README.md)
    remains a separate GPL command-line tool, is never linked or bundled into
    Broiler, and `poppler-data` is audited separately; and
  - [MuPDF](https://mupdf.readthedocs.io/en/latest/license.html) is used only
    under one qualified-reviewer-approved compliance path: an
    organization-installed, unmodified AGPL tool that is not conveyed; an
    approved AGPL-compliant conveyance or service plan covering Corresponding
    Source and any applicable network-use duties; or a suitable commercial
    license. It is never silently introduced by a wrapper package or
    redistributed in a CI image under a notices-only assumption.
- If the [veraPDF apps](https://github.com/veraPDF/veraPDF-apps) CLI or installer
  is used for PDF/A/UA diagnostics, audit that actual distribution and its full
  dependency set, pin it as a separate CLI, and explicitly select the MPL-2.0
  option in the manifest. A redistributed CLI or CI image must retain the
  required notices and make MPL-covered source available as required. No
  conformance claim is made in V1.
- Verify tool release signatures/checksums where available. Do not copy oracle
  source, tables, generated code, or undocumented expected behavior into the
  implementation.
- Treat expected renders, extracted text, screenshots, and other goldens as
  derivatives of the corpus input; generate or retain them only when the
  manifest records the necessary rights.

### 13.2 Performance baselines

- Add `Broiler.Documents.Pdf.Benchmarks` to the aggregate benchmark solution and
  store scenario definitions and pass/fail thresholds in a versioned
  `tests/pdf/performance-baseline.json`. Each entry records commit, SDK/runtime,
  OS/CPU runner profile, corpus hashes, options/limits, fonts/services, cold/warm
  mode, warmup/repetition count, absolute ceilings, and permitted regression.
- Cover one-page text, multi-page text, large object/xref sets, incremental
  revisions, image-heavy and font-heavy inputs, logical extraction,
  pagination/writing, and parallel independent documents. Measure first-page
  discovery, full extraction, wall time/throughput, peak managed/native memory,
  allocations, decoded bytes, cache counts/bytes, output size, package size, and
  enabled WebAssembly payload/heap delta.
- Run threshold comparisons only on a pinned/dedicated runner. Use five measured
  repetitions after the declared warmup and gate on the median plus absolute
  memory/output/work ceilings. A missing threshold, runner identity, or corpus
  hash fails the gate. Read thresholds are populated before Phase 5 and writer
  thresholds before Phase 7; stable release passes every configured threshold.
- Add a repeated-document soak proving document-scoped caches return to zero and
  process-scoped caches never exceed declared count/byte bounds. No cache may
  grow beyond document, font, object, or resource limits.

### 13.3 Release and legal gates

- The required Windows/Linux Release workflow, enabled-platform host tests,
  clean-feed package consumption, oracle/fuzz/performance attestations, and
  package inspection all identify and pass for the exact release commit. Every
  enabled WebAssembly capability also requires its full trimming/AOT, payload,
  and heap evidence; when WebAssembly PDF is disabled, its clean build/AOT must
  instead prove package absence, no transitive reference, and no registration.
  The same enabled/disabled rule applies to Android and other conditional heads.
  A deliberate failing Documents, Graphics, Media, or host integration test must
  fail the required workflow.
- Use a two-stage immutable artifact flow: build/test the release commit; pack
  and finalize/sign each candidate exactly once; record its hash; inspect its
  `.nupkg`, `.snupkg`, application, and container contents; consume those exact
  hashed artifacts from a clean feed; then approve and publish the identical
  bytes without repacking or resigning. Publish automation verifies commit and
  artifact-hash attestations immediately before publication. No
  non-prerelease or unrestricted public package may use an emergency/force
  override or an alternate package-feed path to bypass them.
- Refresh and approve the IP/licensing register against the current ISO/ITU
  declaration records and target distribution jurisdictions; resolve every
  pending entry used by a supported feature.
- Confirm a qualified reviewer's recorded determination that every planned V1
  capability falls within Adobe's ISO 32000-1 public patent-license definitions
  and conditions, including retaliation, scope, and warranty terms, or has
  separate authority. Block capabilities whose coverage remains unresolved.
- Confirm unrestricted public feeds have worldwide clearance for every enabled
  capability, dependency, and shipped asset; otherwise use a technically and
  contractually enforced territory-limited distribution channel.
- Produce SBOMs and component-local notices covering all third-party or derived
  source/generated code, algorithms, constants, tables/data, test vectors,
  dependencies, and assets, plus API compatibility evidence, security review,
  and human approval. Inspect `.nupkg`, `.snupkg`, application, and container
  contents for undeclared code, fonts, CMaps, ICC profiles, sample files, tools,
  native binaries, and license texts.
- Confirm `Broiler.Documents`, the PDF package, and every affected Media/Graphics
  package participate in the repository publish-approval gate.
- Architecture tests prove:
  - PDF references `Broiler.Media.Image` but not its Managed implementation;
  - Graphics and Media do not reference Documents;
  - Pagination/PDF use no `BTextMeasurer`, `BImageCodecs`, installed-font
    discovery, UI/DOM/platform type, or backend handle;
  - shared assemblies contain no PDF-specific type; and
  - every application catalog/service graph is constructed explicitly at its
    platform composition root.
- End-to-end tests cover PDF read → edit → DOCX/HTML/RTF/PDF write, including
  stable resource identity, removed/new resources, metadata transfer choices,
  denial, generated-document obligations, partial/rejected results, and atomic
  destination behavior. API compatibility tests cover legacy stream/RTF calls,
  new request overloads, and incorrect typed options.
- The release matrix records Windows, Linux, Android, and WebAssembly read/write
  states independently; passing a shared Writer suite cannot enable an untested
  platform.
- Audit every shipped standard-derived constant, corpus item, golden, font,
  mapping table, profile, and fallback asset back to its source, rights, notices,
  and approval.
- Confirm that test tools remain absent from product artifacts; any separately
  redistributed CI binary or image carries its own license, notices, SBOM, and
  source obligations.
- Claims review prohibits unsupported `patent-free`, `royalty-free`, `certified`,
  full-conformance, affiliation, or endorsement wording and unauthorized Adobe,
  ISO, or oracle logos/marks.
- The exact clause-level conformance document at
  `Broiler.Documents/docs/pdf-conformance.md` is complete for emitted files and
  distinguishes syntax, interoperability, supported-feature, and conforming-file
  evidence. It makes no PDF/A, PDF/UA, accessibility, archival, signature,
  encryption, or sanitization claim.
- A qualified reviewer reapproves the independent XMP, JPEG/APP14/
  `ColorTransform`, OpenType/TrueType/font-data, Unicode/shaping, URI-output, and
  PDF register entries. Confidential authority is referenced by its controlled
  agreement ID/hash and scope, not copied into public artifacts. Any applicable
  product accessibility review records that V1 output is untagged.
- All unaffected RTF, DOCX, HTML, Markdown, RichEdit, CLI, and Writer suites
  remain green, and the intentionally migrated resource-writer suites pass their
  versioned context/denial expectations.
- No product-time external application, PdfPig/PdfSharp fallback, hidden global
  registration, environmental legacy test, or restricted standards publication
  remains in a release artifact.
- Package inspection also proves that no oracle, test renderer, Managed image
  implementation, ambient platform font, spool implementation, confidential
  agreement, or restricted asset entered the PDF package transitively.

## 14. Post-V1 tracks

Treat these as separately approved roadmaps:

1. Password encryption through the Standard Security Handler, preceded by a
   crypto export-control, sanctions, anti-circumvention, authorized-password,
   permissions-policy, algorithm, and target-jurisdiction review.
2. Full tagged-PDF structure, outlines, internal destinations, and accessibility.
   Pin the structure-tree, marked-content, reading-order, language, alternate-
   text, list/table, artifact, annotation-association, namespace, and exact
   PDF/UA scope before making a claim; include assistive-technology and
   jurisdiction-specific product accessibility review.
3. PDF/A and PDF/UA profiles pinned to exact standards editions and levels, with
   lawful standards access, independent validation, and certification/marketing
   review.
4. AcroForm reading and attachment extraction under an explicit security and
   user-content policy.
5. Signature inspection and later cryptographic validation under an explicit
   trust-store/revocation policy. Broiler must distinguish mathematical
   integrity from identity/trust and never claim that a signature is legally
   valid.
6. Four-component CMYK/YCCK JPEG decode/transcode and advanced color/ICC support
   in Media/Graphics, with the T.81 register rechecked; the source, rights,
   provenance, and Adobe-license scope of the already reviewed V1 APP14/
   `ColorTransform` rules rechecked for four-component conversion and Adobe
   Technical Note #5116; [ISO/IEC 10918-6](https://www.iso.org/standard/59634.html)
   reviewed only if that printing profile is selected; and every bundled ICC
   profile licensed as a separate asset.
7. `CCITTFaxDecode` as a separately scoped, decode-first T.4/T.6 track. Recheck the
   official [T.4](https://www.itu.int/ITU-T/recommendations/related_ps.aspx?id_prod=4597)
   and [T.6](https://www.itu.int/ITU-T/recommendations/related_ps.aspx?id_prod=2613)
   declaration records at approval and release; absence from a declaration
   register is not clearance. Use Flate as the writer fallback.
8. `JPXDecode` as an independent JPEG 2000 track: select the exact T.800/ISO
   15444-1 edition and profile, separate Part 1 core from Part 2 JPX extensions,
   HTJ2K, and other parts, map the official
   [T.800 declarations](https://www.itu.int/ITU-T/recommendations/related_ps.aspx?id_prod=5281),
   and approve the codec's copyright and patent posture before use. If any Part
   2/JPX extension is enabled, separately pin the
   [T.801/ISO 15444-2](https://www.itu.int/ITU-T/recommendations/rec.aspx?lang=en&rec=15653)
   edition and profile and review the official
   [T.801 declaration record](https://www.itu.int/ITU-T/recommendations/related_ps.aspx?id_prod=6123);
   Part 1 review does not clear Part 2.
9. `JBIG2Decode` as an independent T.88 patent, license, and security track.
   Start decode-only after reviewing the official
   [T.88 declarations](https://www.itu.int/ITU-T/recommendations/related_ps.aspx?id_prod=4845)
   and patent-family/status or obtaining an approved vendor license; do not add
   lossy symbol-substitution encoding by default because it can silently change
   document characters and numbers.
10. PDF-writer use or extension of the existing native managed JPEG encoder only
    under a separately justified Media roadmap with exact T.81 modes,
    implementation and data provenance, patent review, and notices; it is not an
    incidental PDF-writer task.
11. Native page rendering in a satellite such as
   `Broiler.Documents.Pdf.Rendering`. This requires canonical Graphics paths,
   fill rules, path clipping, gradients, patterns, transparency groups, soft
   masks, blend modes, and geometrically faithful affine replay.
12. HTML/CSS print-to-PDF through paged CSS/Layout output into the shared Graphics
   page representation, not through DOM code inside the PDF codec.
13. OCR through an explicitly composed external service, never silently inside
    the codec. Before document bytes leave the process, approve provider terms,
    confidentiality, data-processing/privacy, retention, and cross-border
    transfer policy.
14. Complex-script/RTL and vertical-text export, CFF/CFF2 font output, variable
    fonts, color/SVG/bitmap glyphs, and emoji shaping. Each addition amends the
    exact script/font table matrix, pins the relevant OpenType/Unicode sources
    and data licenses, and passes shared Graphics plus pagination gates before it
    becomes a PDF capability.
