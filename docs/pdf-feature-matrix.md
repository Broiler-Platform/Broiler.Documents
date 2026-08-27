# PDF Support Feature Matrix

**Version:** 0.2 (base implementation landed)  
**Updated:** 2026-08-25  
**Authority:** This matrix defines claims; the roadmap defines planned work.

Status values are `Planned`, `Candidate`, `Supported`, `Rejected`, and
`Post-V1`. Only `Supported` may appear as a product capability. Advancing an
entry requires tests, corpus evidence, documentation, and any applicable legal
clearance recorded in the IP/licensing register.

`Broiler.Documents.Pdf` now exists and implements the base slice described in
[roadmap §2.5](pdf-support-roadmap.md#25-current-implementation-state). **No
entry is `Supported`**, because every applicable register row is still pending
and the package is neither packed nor registered in any application. Implemented
behavior is recorded as `Candidate`: it works, it is tested, and it is not a
product claim.

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
| PDF 1.7 syntax, only subsets below | Implemented | Plan | Plan | — | — | No | Yes | In-process codec after gates | IP-001 pending | `pdf.version.unsupported` outside approved subset |
| PDF 2.x declaration/header tolerance | Detect/skip | Detect/skip | Reject | — | — | No | No | Never a conformance claim | IP-002 pending | `pdf.version.tolerated-not-supported` |
| Developer extensions | Detect/skip | Detect/skip | Reject | — | — | No | No | None | IP-003 pending | `pdf.extension.unsupported` |
| Classic xref / cross-reference streams / object streams | Implemented | Plan | Plan | — | — | No | Yes | Bounded parser only | IP-001 pending | `pdf.xref.malformed` / limit code |
| Effective incremental revision | Implemented | Plan | Reject | — | — | No | Yes | Latest effective revision only | IP-001 pending | `pdf.revisions.history-dropped` |
| Standard security handler / encryption | Reject | Reject | Reject | No | No | No | No | None | IP-015 blocked V1 | `pdf.encryption.unsupported` |
| ASCIIHex / ASCII85 / RunLength filters | Implemented | Plan | Plan | Plan | Plan | No | Yes | Bounded filter chain | IP-001 plus source review pending | `pdf.filter.limit` / `pdf.filter.malformed` |
| FlateDecode, PNG predictors | Implemented (TIFF predictor too) | Plan | Plan | Plan | Plan | No | Yes | Bounded shared budget | IP-011 pending | `pdf.filter.flate.*` |
| LZWDecode | Detect/skip; Extension | Detect/skip | Reject | Candidate | No | No | No | None until cleared | IP-010 pending | `pdf.filter.lzw.unsupported` |
| CCITTFaxDecode exact modes not yet selected | Detect/skip; Extension | Detect/skip | Reject | Candidate | No | No | No | None until cleared | IP-009 pending | `pdf.filter.ccitt.unsupported` |
| DCT: 8-bit baseline sequential, Huffman, 1/3/4 components | Detect/skip; Extension | Detect/skip until tuple approval; then Plan | Candidate | Candidate | No | No by default | Candidate | Caller-composed decoder | IP-005 pending | `pdf.image.dct.tuple-unsupported` |
| DCT: 8-bit progressive, Huffman, 1/3/4 components | Detect/skip; Extension | Detect/skip | Reject | Candidate | No | No | No | None until separately cleared | IP-005 pending | `pdf.image.dct.progressive-unsupported` |
| DCT: arithmetic, lossless, 12-bit, or other tuples | Detect/skip | Detect/skip | Reject | No | No | No | No | None | IP-005 pending | `pdf.image.dct.tuple-unsupported` |
| JPEG APP14 / `ColorTransform` 0, 1, 2, absent, or conflicting | Detect/skip | Detect/skip | Reject | Candidate per case | No | No | Candidate | None until independently approved | IP-006 pending | `pdf.image.dct.color-transform-uncertain` |
| JPXDecode / JPEG 2000 | Detect/skip; Extension | Detect/skip | Reject | Later | Later | No | No | None | IP-007 blocked V1 | `pdf.filter.jpx.unsupported` |
| JBIG2Decode | Detect/skip; Extension | Detect/skip | Reject | Later | Later | No | No | None | IP-008 blocked V1 | `pdf.filter.jbig2.unsupported` |
| Standard 14 font-name/metric handling | Implemented (approximate metrics; Extension for real ones) | Plan | Plan | — | — | No | Yes | Deterministic approved data only | IP-012 pending | `pdf.font.standard14.unavailable` |
| Embedded Type 1 / TrueType / OpenType / CFF font programs | Detect/skip; Extension | Candidate | Candidate | Candidate | Candidate | No by default | Candidate | Explicit resource permission | IP-012 pending | `document.resource.permission-required` |
| Type 0/CID fonts and `ToUnicode` CMaps | Implemented for `Identity-H` and `ToUnicode` | Plan | Plan | — | — | No | Yes | Approved CMap/data only | IP-012/IP-013 pending | `pdf.text.mapping-missing-or-uncertain` |
| Latin, Greek, Cyrillic text export | Implemented for the WinAnsi repertoire | — | Plan | — | — | No | Yes | Caller-supplied approved font | IP-012/IP-013 pending | `document.script.unsupported` |
| Complex scripts, bidi shaping, vertical writing, emoji sequences | Detect/skip | Detect/skip | Later | — | — | No | No | None | IP-012/IP-013 pending | `document.script.unsupported` |
| Raw XMP packets | Detect/skip then drop | Detect/skip then drop | Reject | No | No | No | No | None | IP-004 pending | `document.metadata.raw-dropped` |
| Allowlisted normalized metadata | Implemented | Plan | Plan | — | — | No | Yes | Explicit caller selection on write | IP-004/source review pending | `document.metadata.dropped` |
| URI/link values | Implemented | Plan as inert values | Plan after policy admission | — | — | No | Yes | Never activated by codec | IP-014 pending | `document.uri.rejected` |
| Attachments, JavaScript, launch/remote/submit/multimedia actions | Detect/skip | Detect/skip | Reject | No | No | No | No | None | IP-001 and security policy | `pdf.active-content.removed` |
| Tagged PDF / PDF/UA / PDF/A / PDF/X | Detect/skip | Detect/skip | Later | — | — | No | No | No conformance claim | IP-017 blocked V1 | Profile-specific unsupported code |
| Digital signatures | Detect/skip | Detect/skip with invalidation warning | Later | No validation | Later | No | No | No trust claim | IP-016 blocked V1 | `pdf.signature.not-validated` |

## Package and delivery

| Capability | V1 status | Behavior today | Evidence required |
|---|---|---|---|
| In-process `Broiler.Documents.Pdf` codec | Candidate | Implemented; unpacked and unregistered | Architecture tests; package tests |
| Standalone `Broiler.Pdf` process | Rejected | Absent | Phase 0 removal guard |
| PDF import to logical document | Candidate | Implemented for text, styling and links | Reader corpus and semantic tests |
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
| FlateDecode and PNG predictors | Candidate | Implemented over the runtime's DEFLATE; TIFF predictor too | Neutral compression/media capability where reusable |
| LZWDecode | Candidate | Detect/skip; extension point | Legal/patent-history review and bounded decoder tests |
| DCTDecode (JPEG) | Planned | Detect/skip; extension point | `Broiler.Media.Image`; JPEG tuple/APP14 register rows |
| JPXDecode (JPEG 2000) | Post-V1 | Detect/skip; extension point | Separate standards, patent, decoder, and licensing review |
| CCITTFaxDecode | Candidate | Detect/skip; extension point | Separate IP review and corpus |
| JBIG2Decode | Post-V1 | Detect/skip; extension point | Separate high-risk security and patent review |
| Image masks / soft masks | Candidate | Not reached; images are skipped before colour is considered | Compositing semantics and resource budgets |
| ICCBased color | Candidate | Not reached | Color-management ownership and profile licensing |

## Text, fonts, and scripts

| Capability | V1 status | Behavior today | Notes / gate |
|---|---|---|---|
| Standard 14 font-name handling | Candidate | Names recognized on read; emitted on write with no embedded program | No assumption that font programs are installed or redistributable |
| Standard 14 vendor metric files | Rejected | Not used; a Broiler-authored approximate model stands in | Would require its own source/licence row |
| Embedded Type 1 / TrueType / OpenType data | Candidate | Detected and skipped; extension point | Embedding rights remain the content provider's responsibility |
| Type 0 and CID fonts | Candidate | `Identity-H` implemented; other predefined CMaps skipped | Unicode mapping and vertical-writing limits explicit |
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
| Optional content groups | Candidate | Not interpreted; content is extracted without a visibility claim | Logical visibility policy required |
| DeviceN / Separation color | Post-V1 | Not interpreted | Color-management and conformance review |

## Semantics, metadata, and active content

| Capability | V1 status | Behavior today | Notes / gate |
|---|---|---|---|
| Normalized title/author/subject/keywords/dates | Candidate | Implemented on read and write; nothing else crosses | Allowlist only; privacy tests |
| Raw XMP preservation | Rejected | Detected and dropped | XMP review is separate; V1 drops raw packets |
| Links as inert semantic values | Candidate | Implemented; admitted by policy, revalidated before output | Never dereferenced by the codec |
| Annotations | Candidate | Link annotations only; the rest inventoried | Allowlisted non-active subset only |
| AcroForm / XFA | Post-V1 | Detected; signature fields reported | No form execution or fidelity claim |
| Attachments and embedded files | Rejected | Detected and reported; never extracted | No extraction or activation in V1 |
| JavaScript and active actions | Rejected | Detected and reported; never executed | Diagnose and ignore without execution |
| Redaction or secure sanitization | Rejected | An unapplied Redact annotation raises an error-severity warning | Conversion is not redaction |
| Tagged PDF / structure tree | Post-V1 | Presence reported; structure not consumed | Separate accessibility architecture |
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
