# PDF Support Feature Matrix

**Version:** 1.11 (evidence-based register standard)  
**Updated:** 2026-09-02 (aligned with the register's evidence-based standard)  
**Authority:** This matrix defines claims; the roadmap defines planned work.

Status values are `Planned`, `Candidate`, `Supported`, `Rejected`, and
`Post-V1`. Only `Supported` may appear as a product capability. Advancing an
entry requires tests, corpus evidence, documentation, and a decided row in the
IP/licensing register. Those decisions are engineering risk assessments made on
published evidence, not legal clearances — the register says so at the top, in
terms worth reading before treating any word here as a guarantee.

`Broiler.Documents.Pdf` now exists and implements the base slice described in
[roadmap §2.5](pdf-support-roadmap.md#25-current-implementation-state). **No
entry is `Supported`.** Thirteen register rows are now approved: IP-001, the row
under every construct this codec implements; every filter and codec row (IP-004
through IP-010 and IP-012); and the provenance and naming rows IP-011, IP-013,
IP-014, and IP-018. What remains is listed in the register's
[what still blocks a support claim](pdf-ip-licensing-register.md#what-still-blocks-a-support-claim):
SRC-017 — the one genuinely open provenance question, since transcribing ITU-T
T.4's code tables was unavoidable — and the roadmap's own Phase 5, 7, and 8 exit
criteria, which are engineering gates that no clearance touches. The package also remains neither packed nor registered in any
application beyond the read-preview candidate. Implemented behavior is therefore
recorded as `Candidate`: it works, it is tested, and it is not a product claim.
How such an entry may be *named*, once one exists, is settled — see
[approved labels](pdf-ip-licensing-register.md#approved-labels).

The **Behavior today** column states what the code actually does right now, so
this table can be read as a description of the build as well as a statement of
intent. `Implemented` means the base build does it; `Detect/skip` means it is
recognized and reported with the diagnostic in the last column; `Reject` means a
stable rejection; `Extension` means it arrives by composing a reviewed
implementation into `PdfCodecServices`
(see [PDF extension points](pdf-extension-points.md)); `Later` means post-V1;
`—` means not applicable. No entry may become `Supported` while its legal column
is pending.

## Operational and clearance matrix

| Feature / exact subset | Behavior today | V1 read | V1 write | Decode | Encode | Preserve bytes | Transform | Default exposure | Legal row / state | Required diagnostic |
|---|---|---|---|---|---|---|---|---|---|---|
| PDF 1.7 syntax, only subsets below | Implemented | Candidate | Candidate | — | — | No | Yes | In-process codec after gates | IP-001 approved 2026-09-01 | `pdf.version.unsupported` outside approved subset |
| PDF 2.x declaration/header tolerance | Detect/skip | Detect/skip | Reject | — | — | No | No | Never a conformance claim | IP-002 pending | `pdf.version.tolerated-not-supported` |
| Developer extensions | Detect/skip | Detect/skip | Reject | — | — | No | No | None | IP-003 pending | `pdf.extension.unsupported` |
| Classic xref / cross-reference streams / object streams | Implemented | Plan | Plan | — | — | No | Yes | Bounded parser only | IP-001 pending | `pdf.xref.malformed` / limit code |
| Effective incremental revision | Implemented | Plan | Reject | — | — | No | Yes | Latest effective revision only | IP-001 pending | `pdf.revisions.history-dropped` |
| Standard security handler / encryption | Reject | Reject | Reject | No | No | No | No | None | IP-015 blocked V1 | `pdf.encryption.unsupported` |
| ASCIIHex / ASCII85 / RunLength filters | Implemented | Plan | Plan | Plan | Plan | No | Yes | Bounded filter chain | IP-001 plus source review pending | `pdf.filter.limit` / `pdf.filter.malformed` |
| FlateDecode, PNG and TIFF predictors | Implemented; every predictor and component size | Candidate | Candidate | Candidate | Candidate | No | Yes | Bounded shared budget | IP-011 approved 2026-09-01; IP-023 confirmed | `pdf.filter.limit` / `pdf.filter.malformed` |
| LZWDecode, including `EarlyChange` | Implemented | Candidate | Reject | Candidate | No | No | Yes | Base build; bounded filter chain | IP-010 approved and retired 2026-09-01; IP-001 pending | `pdf.filter.limit` / `pdf.filter.malformed` |
| CCITTFaxDecode: MH, MR, and MMR (ITU-T T.4/T.6) | Implemented as a composed filter | Candidate | Reject | Candidate | No | No | Candidate | Caller-composed decoder; never in the default graph | IP-009 approved on patents 2026-09-01; code tables pending SRC-017 | `pdf.image.decoded-not-projected` |
| DCT: 8-bit **every Huffman-coded process** (baseline, extended sequential, progressive), 1 or 3 components, YCbCr by declaration or default | Implemented as a composed filter | Candidate | Reject | Candidate | No | No | Candidate | Caller-composed decoder; never in the default graph | IP-005 widened to progressive and extended sequential 2026-09-02; IP-006 approved 2026-09-01 | `pdf.image.decoded-not-projected` |
| DCT: arithmetic, lossless, hierarchical, differential, 12-bit, 4-component | Detect/skip | Detect/skip | Reject | No | No | No | No | None | Outside IP-005; arithmetic carried the historical RAND terms | `pdf.image.dct.tuple-unsupported` |
| JPEG APP14 / `ColorTransform` 1, or absent on 3 components | Implemented as a composed filter | Candidate | Reject | Candidate | No | No | Candidate | Caller-composed decoder | IP-006 approved 2026-09-01 | `pdf.image.decoded-not-projected` |
| JPEG APP14 / `ColorTransform` 0 on 3 components | Implemented as a composed filter | Candidate | Reject | Candidate | No | No | Candidate | Caller-composed decoder | IP-006 approved 2026-09-01; decoder capability added 2026-09-02 | `pdf.image.decoded-not-projected` |
| JPEG APP14 / `ColorTransform` 2 (YCCK), or conflicting declarations | Detect/skip | Detect/skip | Reject | No | No | No | No | None | IP-006 approved; refused by V1 scope (YCCK) or self-contradiction | `pdf.image.dct.tuple-unsupported`, `pdf.image.dct.color-transform-uncertain` |
| JPXDecode / JPEG 2000 Part 1: codestream recognized and reported | Implemented as a composed reader | Candidate | Reject | No | No | No | No | Caller-composed reader; never in the default graph | IP-007 approved for Part 1 2026-09-01 | `pdf.filter.jpx.unsupported` with the tuple |
| JPXDecode / JPEG 2000 Part 1: entropy decoding | Not written | Later | Reject | Later | No | No | No | None | IP-007 approved; the gap is engineering, not clearance | `pdf.filter.jpx.unsupported` |
| JPXDecode / JPEG 2000 Part 2 extensions | Detect/skip | Detect/skip | Reject | No | No | No | No | None | Outside IP-007; refused by `Rsiz` | `pdf.filter.jpx.unsupported` |
| JBIG2Decode: segment structure, and generic regions coded with MMR or arithmetically | Implemented as a composed filter | Candidate | Reject | Candidate | No | No | Candidate | Caller-composed decoder; never in the default graph | IP-008 approved 2026-09-01; the MQ probability table is **pending** in SRC-019 | `pdf.image.decoded-not-projected` |
| JBIG2Decode: symbol, text, halftone, and refinement regions | Detect/skip with the segment inventory named | Detect/skip | Reject | Later | No | No | No | None | IP-008 approved; the arithmetic decoder they need now exists, so the gap is their own decoders | `pdf.filter.jbig2.unsupported` |
| Standard 14 font-name/metric handling | Implemented (approximate metrics; Extension for real ones) | Plan | Plan | — | — | No | Yes | Deterministic approved data only | IP-012 approved for inspection; metric data pending | `pdf.font.standard14.unavailable` |
| Embedded sfnt font programs, read for glyph-to-text | Implemented as a composed reader | Candidate | Reject | — | — | No | Yes | Caller-composed reader; never in the default graph | IP-012 approved for inspection 2026-09-01 | `pdf.font.program-not-composed` |
| Embedded bare CFF font programs, read for glyph-to-text | Implemented as a composed reader | Candidate | Reject | — | — | No | Yes | Caller-composed reader; never in the default graph | IP-012 approved for inspection; the standard-strings table is **pending** in SRC-016 | `pdf.font.program-not-composed` |
| Embedded Type 1 font programs, and CID-keyed CFF | Detect/skip | Detect/skip | Reject | — | — | No | No | None | IP-012 approved; declined for want of parser surface and, for CID-keyed CFF, for want of a collection's CMap | `pdf.font.program-not-composed` |
| Font embedding and subsetting into output | Rejected; the fail-closed preflight and the `fsType` reader it consults exist and refuse | — | Reject | — | — | No | No | None | **Outside IP-012**, which must be re-opened first; roadmap §11.3's font path is decided (B: caller-configured, [brief](pdf-font-path-brief.md)) and its library half is built, but embedding itself stays outside IP-012 — whose re-opening is now requested and written up in the [embedding brief](pdf-ip-012-embedding-brief.md) | `pdf.write.feature-unsupported` |
| Type 0/CID fonts and `ToUnicode` CMaps | Implemented for `Identity-H` and `ToUnicode`, with a composed reader recovering text where `ToUnicode` is absent | Plan | Plan | — | — | No | Yes | Approved CMap/data only | IP-012 approved for inspection; IP-013 approved | `pdf.text.mapping-missing-or-uncertain` |
| Latin, Greek, Cyrillic text export | Implemented for the WinAnsi repertoire on write; Symbol's Greek readable on import | — | Plan | — | — | No | Yes | Caller-supplied approved font | IP-012 and IP-013 approved | `document.script.unsupported` |
| Complex scripts, bidi shaping, vertical writing, emoji sequences | Detect/skip | Detect/skip | Later | — | — | No | No | None | IP-013 approved; unimplemented by scope, not by clearance | `document.script.unsupported` |
| XMP read into the normalized allowlist (ISO 16684-1:2019, RDF/XML subset, nine `dc`/`xmp`/`pdf` properties) | Implemented | Candidate | — | Yes | No | No | Yes | In-process reader: no I/O, no DTD, no external entity, no schema | IP-004 approved 2026-09-01; IP-001 pending | `document.metadata.raw-dropped`, `pdf.metadata.xmp-unusable` |
| Raw XMP packet preservation or XMP output | Rejected | Reject | Reject | — | No | No | No | None | Out of V1 scope by design, not by clearance | `document.metadata.raw-dropped` |
| Allowlisted normalized metadata | Implemented | Plan | Plan | — | — | No | Yes | Explicit caller selection on write | IP-004 approved 2026-09-01; source review pending | `document.metadata.dropped`, `pdf.metadata.conflict` |
| URI/link values | Implemented | Plan as inert values | Plan after policy admission | — | — | No | Yes | Never activated by codec | IP-014 approved 2026-09-01 | `document.uri.rejected` |
| Attachments, JavaScript, launch/remote/submit/multimedia actions | Detect/skip | Detect/skip | Reject | No | No | No | No | None | IP-001 and security policy | `pdf.active-content.removed` |
| Tagged PDF / PDF/UA / PDF/A / PDF/X | Detect/skip | Detect/skip | Later | — | — | No | No | No conformance claim | IP-017 blocked V1 | Profile-specific unsupported code |
| Digital signatures | Detect/skip | Detect/skip with invalidation warning | Later | No validation | Later | No | No | No trust claim | IP-016 blocked V1 | `pdf.signature.not-validated` |

## Package and delivery

| Capability | V1 status | Behavior today | Evidence required |
|---|---|---|---|
| In-process `Broiler.Documents.Pdf` codec | Candidate | Implemented; unpacked and unregistered | Architecture tests; package tests |
| Standalone `Broiler.Pdf` process | Rejected | Absent | Phase 0 removal guard |
| PDF import to logical document | Candidate | Implemented for text, styling, links, and images inside the approved raw-sample subset | Reader corpus and semantic tests |
| PDF export from logical document | Candidate | Implemented for the standard-font subset | Pagination, writer, and interoperability tests |
| Layout-preserving round trip | Rejected | Not attempted | Not a product claim |
| Byte-preserving or incremental update | Post-V1 | Not attempted | Separate ADR and security review |
| Third-party runtime dependency in the PDF package | Rejected | None; guarded by a delivery test | Project-reference guard |

## Input and syntax

| Capability | V1 status | Behavior today | Notes / gate |
|---|---|---|---|
| PDF 1.7 syntax within enumerated subsets | Candidate | Implemented | ISO 32000-1 clearance and per-feature tests |
| PDF 2.0 tolerance | Candidate | Declaration recorded; no 2.0-only feature implemented | Qualified review; tolerance does not imply PDF 2.0 conformance |
| Classic cross-reference tables | Candidate | Implemented, with a reported scan-based recovery path | Strict and bounded recovery corpus |
| Cross-reference streams | Candidate | Implemented through the production filter pipeline | Filter and object-stream limits |
| Object streams | Candidate | Implemented | Shared object/decompression budgets |
| Linearized files | Candidate | Read as ordinary files | Read as ordinary files; no fast-web-view claim |
| Hybrid-reference files | Candidate | `/XRefStm` entries loaded ahead of the classic section | Must not weaken encryption or duplicate-object rules |
| Incremental revisions | Candidate | Latest effective revision only, reported | Read latest effective revision only; adversarial tests |
| Encrypted input | Rejected | Rejected from the trailers, before any content object resolves | Reject when `/Encrypt` is discovered |
| Digital signatures | Post-V1 | Detected and reported; never validated | No validation, preservation, or signing claim |

## Stream filters and images

| Capability | V1 status | Behavior today | Ownership / gate |
|---|---|---|---|
| ASCIIHexDecode / ASCII85Decode / RunLengthDecode | Candidate | Implemented in this repository | PDF syntax layer; IP row and fuzz tests |
| FlateDecode and the predictors | Candidate | Implemented over the runtime's DEFLATE; TIFF predictor 2 and all five PNG filters | Neutral compression/media capability where reusable |
| LZWDecode | Candidate | Implemented in the base build; round-tripped against an encoder written in the test suite | Patent history retired; bounded decoder tests |
| DCTDecode (JPEG) | Candidate | Composed extension: baseline and progressive with a resolved colour transform decode, everything else is refused by name | `Broiler.Media.Image` decoder; IP-005 and IP-006 approved |
| JPXDecode (JPEG 2000) | Candidate for recognition; Post-V1 for decoding | Composed reader reports the codestream tuple; no entropy decoder | Part 1 cleared; MQ coder, EBCOT, and the wavelets outstanding |
| CCITTFaxDecode | Candidate | Composed extension: all three schemes decode, round-tripped against an encoder written in the test suite | Patent history retired; the standard's code tables await a source decision |
| JBIG2Decode | Candidate for MMR generic regions; Post-V1 for the rest | Composed filter decodes MMR generic regions and reports every other segment type | Patent row cleared; the arithmetic decoder is outstanding, and the security review still applies |
| Raw image samples into the model: DeviceGray at 1/2/4/8 bits, DeviceRGB at 8, Indexed at 1/2/4/8 over a bounded DeviceGray/DeviceRGB palette, with validated `/Decode` | Candidate | Implemented; decoded samples are normalized to RGBA and admitted through the resource policy. Anything outside the tuple is refused by the reason met | Roadmap §9.3's approved subset; per-tuple projection tests |
| Image masks / soft masks | Candidate | Refused rather than projected: a stencil paints the current fill colour, and an `/SMask` or colour-key `/Mask` carries transparency this build does not composite, so carrying either would invent a picture | Compositing semantics and resource budgets |
| ICCBased color | Candidate | Not reached; refused by name with the family it declared | Color-management ownership and profile licensing |

## Text, fonts, and scripts

| Capability | V1 status | Behavior today | Notes / gate |
|---|---|---|---|
| Standard 14 font-name handling | Candidate | Names recognized on read; emitted on write with no embedded program | No assumption that font programs are installed or redistributable |
| Standard 14 vendor metric files | Rejected | Not used; a Broiler-authored approximate model stands in | Would require its own source/licence row |
| Embedded Type 1 / TrueType / OpenType data | Candidate | sfnt and bare CFF read for glyph-to-text through the composed reader; Type 1 detected and skipped | Embedding rights remain the content provider's responsibility |
| Type 3 fonts | Candidate | Read for what the font states — `ToUnicode`, `/Differences` names, a named `/BaseEncoding`, and `/FontMatrix` advances. The glyph procedures are never executed, and a font naming no encoding maps nothing rather than falling back to StandardEncoding | Procedure execution stays out of scope; no glyph is rendered or measured from its drawing |
| Type 0 and CID fonts | Candidate | `Identity-H` implemented; other predefined CMaps skipped; a composed font reader recovers text where the file supplies no `ToUnicode` | Unicode mapping and vertical-writing limits explicit |
| `ToUnicode` CMaps | Candidate | Implemented, including `bfrange` and bounded `usecmap` | Primary semantic extraction route |
| Fallback character inference without `ToUnicode` | Candidate | Declared encoding and `/Differences` only; never a glyph-index guess | Confidence diagnostic; no silent correctness claim |
| Latin, Greek, Cyrillic export | Candidate | WinAnsi repertoire only; anything else substituted and reported | Caller-supplied font and deterministic shaping tests |
| Complex scripts / bidi shaping / vertical writing | Post-V1 | Not attempted | Neutral shaping component and script corpus required |
| Emoji sequences and color fonts | Post-V1 | Not attempted | Font technology and rendering review required |

## Graphics and page content

| Capability | V1 status | Behavior today | Ownership / gate |
|---|---|---|---|
| Paths, fills, strokes, clipping, transforms | Planned | Skipped on read with a reported drop; the writer emits filled rectangles only | Reusable primitives in `Broiler.Graphics` |
| Text positioning and text state | Candidate | Implemented | PDF interpreter plus neutral geometry |
| Form XObjects | Candidate | Implemented under bounded recursion and a visited set | Recursion/resource limits |
| Transparency groups and blend modes | Candidate | Not interpreted | Neutral graphics compositing ownership |
| Patterns and shadings | Candidate | Not interpreted; reported as dropped artwork | Shared graphics capability; bounded evaluation |
| Optional content groups | Candidate | The catalog's default configuration `/D` is read and honoured: content in a group it turns off is omitted and reported with `pdf.import.optional-content-omitted`. `/BaseState`, `/ON`, `/OFF`, and OCMD membership under `/P` are applied; visibility expressions `/VE`, alternate `/Configs`, and usage applications `/AS` are not, and content they govern is kept. `IncludeHiddenOptionalContent` takes every layer and still reports the configuration | Reading a declared configuration, not judging visibility; expression and usage-application evaluation outstanding |
| DeviceN / Separation color | Post-V1 | Not interpreted | Color-management and conformance review |

## Semantics, metadata, and active content

| Capability | V1 status | Behavior today | Notes / gate |
|---|---|---|---|
| Normalized title/author/subject/keywords/dates | Candidate | Implemented on read and write; nothing else crosses | Allowlist only; privacy tests |
| XMP read into the allowlist | Candidate | Implemented; XMP wins per field, `Info` is the fallback, disagreement is reported by field name | IP-004 approved for the read subset; bounded non-resolving reader |
| Raw XMP preservation | Rejected | Read for the allowlist, then dropped | Never preserved and never written; excluded by V1 scope rather than by clearance |
| Links as inert semantic values | Candidate | Implemented; admitted by policy, revalidated before output | Never dereferenced by the codec |
| Annotations | Candidate | Link annotations only; the rest inventoried | Allowlisted non-active subset only |
| AcroForm / XFA | Post-V1 | Detected; signature fields reported | No form execution or fidelity claim |
| Attachments and embedded files | Rejected | Detected and reported; never extracted | No extraction or activation in V1 |
| JavaScript and active actions | Rejected | Detected and reported; never executed | Diagnose and ignore without execution |
| Redaction or secure sanitization | Rejected | An unapplied Redact annotation raises an error-severity warning | Conversion is not redaction |
| Tagged PDF / structure tree, read for reading order only | Candidate | The tree is walked for its sequence and used to order a page whose runs it accounts for in full; a page it covers only partly falls back to geometry whole. Roles, heading levels, lists, tables, and the role map are all ignored | Sequence only; no accessibility or conformance claim follows, and §14.2 still owns the rest |
| Tagged PDF / structure tree, everything but reading order | Post-V1 | Presence reported; roles and semantics not consumed | Separate accessibility architecture |
| PDF/UA, PDF/A, PDF/X conformance | Post-V1 | No claim; writer output is untagged | Profile-specific standards and validation required |

## Platform claims

The codec is platform-neutral managed code with no OS dependency, but it is
registered nowhere, so no platform carries it today.

| Platform | V1 status | Behavior today | Evidence required |
|---|---|---|---|
| .NET CLI | Candidate | Not registered | Full import/export corpus and resource-limit tests |
| Windows | Candidate | Not registered | Runtime, trimming, fonts, and deterministic-layout tests |
| Linux | Candidate | Not registered | Runtime, fonts, globalization, and deterministic-layout tests |
| Android | Post-V1 | Not registered | AOT/trimming, memory, and font provisioning |
| WebAssembly | Post-V1 | Not registered | AOT/trimming, memory, streaming, and font provisioning |
