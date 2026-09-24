# PDF Support Feature Matrix

**Version:** 1.11 (evidence-based register standard)  
**Updated:** 2026-09-03 (JPEG 2000 decoding; pending provenance rows)  
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
SRC-017, the transcription question that reproducing ITU-T T.4's code tables
raised and that SRC-018 and SRC-019 defer to; SRC-016, adjacent to it rather than
governed by it; the re-opening of IP-012 for font embedding, requested
2026-09-03; and the roadmap's own Phase 5, 7, and 8 exit criteria, which are
engineering gates that no clearance touches. The package also remains neither packed nor registered in any
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
| Classic xref / cross-reference streams / object streams | Implemented | Plan | Plan | — | — | No | Yes | Bounded parser only | IP-001 approved 2026-09-01 | `pdf.xref.malformed` / limit code |
| Effective incremental revision | Implemented | Plan | Reject | — | — | No | Yes | Latest effective revision only | IP-001 approved 2026-09-01 | `pdf.revisions.history-dropped` |
| Standard security handler / encryption | Reject | Reject | Reject | No | No | No | No | None | IP-015 blocked V1 | `pdf.encryption.unsupported` |
| ASCIIHex / ASCII85 / RunLength filters | Implemented | Plan | Plan | Plan | Plan | No | Yes | Bounded filter chain | IP-001 approved 2026-09-01; SRC-001 closed 2026-09-01 | `pdf.filter.limit` / `pdf.filter.malformed` |
| FlateDecode, PNG and TIFF predictors | Implemented; every predictor and component size | Candidate | Candidate | Candidate | Candidate | No | Yes | Bounded shared budget | IP-011 approved 2026-09-01; IP-023 confirmed | `pdf.filter.limit` / `pdf.filter.malformed` |
| LZWDecode, including `EarlyChange` | Implemented | Candidate | Reject | Candidate | No | No | Yes | Base build; bounded filter chain | IP-010 approved and retired 2026-09-01; IP-001 approved 2026-09-01 | `pdf.filter.limit` / `pdf.filter.malformed` |
| CCITTFaxDecode: MH, MR, and MMR (ITU-T T.4/T.6) | Implemented as a composed filter | Candidate | Reject | Candidate | No | No | Candidate | Caller-composed decoder; never in the default graph | IP-009 approved on patents 2026-09-01; code tables pending SRC-017 | `pdf.image.decoded-not-projected` |
| DCT: 8-bit **every Huffman-coded process** (baseline, extended sequential, progressive), 1 or 3 components, YCbCr by declaration or default | Implemented as a composed filter | Candidate | Reject | Candidate | No | No | Candidate | Caller-composed decoder; never in the default graph | IP-005 widened to progressive and extended sequential 2026-09-02; IP-006 approved 2026-09-01 | `pdf.image.decoded-not-projected` |
| DCT: arithmetic, lossless, hierarchical, differential, 12-bit, 4-component | Detect/skip | Detect/skip | Reject | No | No | No | No | None | Outside IP-005; arithmetic carried the historical RAND terms | `pdf.image.dct.tuple-unsupported` |
| JPEG APP14 / `ColorTransform` 1, or absent on 3 components | Implemented as a composed filter | Candidate | Reject | Candidate | No | No | Candidate | Caller-composed decoder | IP-006 approved 2026-09-01 | `pdf.image.decoded-not-projected` |
| JPEG APP14 / `ColorTransform` 0 on 3 components | Implemented as a composed filter | Candidate | Reject | Candidate | No | No | Candidate | Caller-composed decoder | IP-006 approved 2026-09-01; decoder capability added 2026-09-02 | `pdf.image.decoded-not-projected` |
| JPEG APP14 / `ColorTransform` 2 (YCCK), or conflicting declarations | Detect/skip | Detect/skip | Reject | No | No | No | No | None | IP-006 approved; refused by V1 scope (YCCK) or self-contradiction | `pdf.image.dct.tuple-unsupported`, `pdf.image.dct.color-transform-uncertain` |
| JPXDecode / JPEG 2000 Part 1: codestream recognized and reported | Implemented as a composed reader | Candidate | Reject | No | No | No | No | Caller-composed reader; never in the default graph | IP-007 approved for Part 1 2026-09-01 | `pdf.filter.jpx.unsupported` with the tuple |
| JPXDecode / JPEG 2000 Part 1: decoding, for one tile, default precincts and the LRCP/RPCL progressions | Implemented as a composed decoder; **no real image has been decoded through it** | Candidate | Reject | Candidate | No | No | Candidate | Caller-composed decoder; never in the default graph | IP-007 approved; the EBCOT context tables are **pending** in SRC-018 | `pdf.filter.jpx.unsupported` naming the construct refused |
| JPXDecode / JPEG 2000 Part 2 extensions | Detect/skip | Detect/skip | Reject | No | No | No | No | None | Outside IP-007; refused by `Rsiz` | `pdf.filter.jpx.unsupported` |
| JBIG2Decode: segment structure, generic regions coded with MMR or arithmetically, and arithmetic symbol dictionaries, text regions and refinement | Implemented as a composed filter; **no real image has been decoded through it** | Candidate | Reject | Candidate | No | No | Candidate | Caller-composed decoder; never in the default graph | IP-008 approved 2026-09-01; the MQ probability table is **pending** in SRC-019 | `pdf.image.decoded-not-projected` |
| JBIG2Decode: halftone regions, aggregate symbol coding, the intermediate regions, and every Huffman-coded form | Detect/skip with the construct named | Detect/skip | Reject | Later | No | No | No | None | IP-008 approved; the gap is their own decoders rather than a clearance | `pdf.filter.jbig2.unsupported` |
| Standard 14 font-name/metric handling | Implemented (approximate metrics; Extension for real ones) | Plan | Plan | — | — | No | Yes | Deterministic approved data only | IP-012 approved for inspection; metric data pending | `pdf.font.standard14.unavailable` |
| Embedded sfnt font programs, read for glyph-to-text | Implemented as a composed reader | Candidate | Reject | — | — | No | Yes | Caller-composed reader; never in the default graph | IP-012 approved for inspection 2026-09-01 | `pdf.font.program-not-composed` |
| Embedded bare CFF font programs, read for glyph-to-text | Implemented as a composed reader | Candidate | Reject | — | — | No | Yes | Caller-composed reader; never in the default graph | IP-012 approved for inspection; the standard-strings table is **pending** in SRC-016 | `pdf.font.program-not-composed` |
| Embedded Type 1 font programs, and CID-keyed CFF | Detect/skip | Detect/skip | Reject | — | — | No | No | None | IP-012 approved; declined for want of parser surface and, for CID-keyed CFF, for want of a collection's CMap | `pdf.font.program-not-composed` |
| Font embedding and subsetting into output | Rejected; the fail-closed preflight and the `fsType` reader it consults exist and refuse | — | Reject | — | — | No | No | None | **Outside IP-012**, which must be re-opened first; roadmap §11.3's font path is decided (B: caller-configured, [brief](pdf-font-path-brief.md)) and its library half is built, but embedding itself stays outside IP-012 — whose re-opening is now requested and written up in the [embedding brief](pdf-ip-012-embedding-brief.md) | `pdf.write.feature-unsupported` |
| Type 0/CID fonts and `ToUnicode` CMaps | Implemented for `Identity-H` and `ToUnicode`, with a composed reader recovering text where `ToUnicode` is absent | Plan | Plan | — | — | No | Yes | Approved CMap/data only | IP-012 approved for inspection; IP-013 approved | `pdf.text.mapping-missing-or-uncertain` |
| Latin, Greek, Cyrillic text export | Implemented for the WinAnsi repertoire on write; Symbol's Greek readable on import | — | Plan | — | — | No | Yes | Caller-supplied approved font | IP-012 and IP-013 approved | `document.script.unsupported` |
| Complex scripts, bidi shaping, vertical writing, emoji sequences | Detect/skip | Detect/skip | Later | — | — | No | No | None | IP-013 approved; unimplemented by scope, not by clearance | `document.script.unsupported` |
| XMP read into the normalized allowlist (ISO 16684-1:2019, RDF/XML subset, nine `dc`/`xmp`/`pdf` properties) | Implemented | Candidate | — | Yes | No | No | Yes | In-process reader: no I/O, no DTD, no external entity, no schema | IP-004 approved 2026-09-01; IP-001 approved 2026-09-01 | `document.metadata.raw-dropped`, `pdf.metadata.xmp-unusable` |
| Raw XMP packet preservation or XMP output | Rejected | Reject | Reject | — | No | No | No | None | Out of V1 scope by design, not by clearance | `document.metadata.raw-dropped` |
| Allowlisted normalized metadata | Implemented | Plan | Plan | — | — | No | Yes | Explicit caller selection on write | IP-004 approved 2026-09-01; SRC-001 and SRC-012 approved | `document.metadata.dropped`, `pdf.metadata.conflict` |
| URI/link values | Implemented | Plan as inert values | Plan after policy admission | — | — | No | Yes | Never activated by codec | IP-014 approved 2026-09-01 | `document.uri.rejected` |
| Attachments, JavaScript, launch/remote/submit/multimedia actions | Detect/skip | Detect/skip | Reject | No | No | No | No | None | IP-001 and security policy | `pdf.active-content.removed` |
| Tagged PDF / PDF/UA / PDF/A / PDF/X | Detect/skip | Detect/skip | Later | — | — | No | No | No conformance claim | IP-017 blocked V1 | Profile-specific unsupported code |
| Digital signatures | Detect/skip | Detect/skip with invalidation warning | Later | No validation | Later | No | No | No trust claim | IP-016 blocked V1 | `pdf.signature.not-validated` |

## Package and delivery

| Capability | V1 status | Behavior today | Evidence required |
|---|---|---|---|
| In-process `Broiler.Documents.Pdf` codec | Candidate | Preview library packaging enabled; the command-line tool composes it read-only; PDF output from an application remains gated | Architecture tests; package tests |
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
| JPXDecode (JPEG 2000) | Candidate for the implemented subset | Composed decoder: tag trees, packet headers, EBCOT tier-1, inverse wavelets and component transforms, for one tile and the LRCP/RPCL progressions; everything else refused by name | Part 1 cleared; EBCOT context tables pending in SRC-018. **The wavelets are tested by inversion; the entropy coder is tested by nothing** |
| CCITTFaxDecode | Candidate | Composed extension: all three schemes decode, round-tripped against an encoder written in the test suite | Patent history retired; the standard's code tables await a source decision |
| JBIG2Decode | Candidate for generic, symbol, text and refinement regions; Post-V1 for the rest | Composed filter decodes generic regions under both coding methods, arithmetic symbol dictionaries with the text regions that draw from them, and refinement in all three places it may appear; every other segment type is reported. **No real image has been decoded through it** | Patent row cleared; the MQ probability table is pending in SRC-019, the halftone regions, aggregate coding and the Huffman-coded forms are outstanding, and the security review still applies |
| Raw image samples into the model: DeviceGray at 1/2/4/8 bits, DeviceRGB at 8, Indexed at 1/2/4/8 over a bounded DeviceGray/DeviceRGB palette, with validated `/Decode` | Candidate | Implemented; decoded samples are normalized to RGBA and admitted through the resource policy. Anything outside the tuple is refused by the reason met | Roadmap §9.3's approved subset; per-tuple projection tests |
| Image masks / soft masks | Candidate | Refused rather than projected, with one exception: a stencil paints the current fill colour, and a colour-key `/Mask` carries transparency this build does not composite, so carrying either would invent a picture. An `/SMask` is **read** before it is believed — every sample mapped through the mask's own `/Decode`, and where all of them come out at full alpha the mask states no transparency and the image projects unchanged. Producers attach one whether the picture needs it or not, and refusing on the key's presence discarded every logo with an always-attached opaque alpha channel. A mask that is not uniformly opaque, carries `/Matte`, names a space other than `/DeviceGray`, or cannot be decoded as packed samples still refuses its image | Compositing semantics and resource budgets |
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
| Latin ligature characters | Candidate | A glyph whose mapping - `ToUnicode`, a glyph name such as `/fi`, or a composed font program's own map - yields one of the seven Latin ligatures U+FB00–U+FB06 is read as the letters it joins: `ﬁ` as `fi`, `ﬃ` as `ffi`, `ﬅ` as `ſt`. The mapping is right about the glyph, but a word holding the ligature is not the word to a search, a spelling checker, or a hyphenation dictionary, and a document read to be edited meets all three. Marked-content `ActualText` is taken as it stands, and nothing else is normalized - no accent is composed or decomposed, and a letter such as `æ` stays one | Unicode's own one-step compatibility decomposition, for those seven characters only |
| Latin, Greek, Cyrillic export | Candidate | WinAnsi repertoire only; anything else substituted and reported | Caller-supplied font and deterministic shaping tests |
| Complex scripts / bidi shaping / vertical writing | Post-V1 | Not attempted | Neutral shaping component and script corpus required |
| Emoji sequences and color fonts | Post-V1 | Not attempted | Font technology and rendering review required |

## Graphics and page content

| Capability | V1 status | Behavior today | Ownership / gate |
|---|---|---|---|
| Paths, fills, strokes, clipping, transforms | Planned | Not drawn on read. Each painted path is classified and read back where the model can carry what it meant - a grid's rules and shades as a table, a bar under or through text as the text's decoration, a fill beneath a run as its background, a closed box around text as a one-cell table - and reported as dropped otherwise. The stroking colour and pen width (`G`/`RG`/`K`/`CS`/`SC`/`SCN`, `w`, and `/LW` through `gs`) are tracked apart from the fill, so a rule is read in the colour and weight it was stroked with. A fill in the paper's colour on bare paper is recognized as painting nothing rather than counted as a loss; a path that repeats a shape already painted in the same place is counted as a repaint. The writer emits filled rectangles only | Reusable primitives in `Broiler.Graphics` |
| Text positioning and text state | Candidate | Implemented for text that runs across the page as displayed. Text turned against it - sideways, upside down, mirrored, or more than about a degree off the horizontal - is placed on horizontal lines wherever each piece lands, often a letter at a time, and reported with `pdf.text.orientation-unsupported` as a skip, so a read carrying scattered letters is no longer a success | PDF interpreter plus neutral geometry |
| Page size, orientation, and margins, stated as the document's page | Candidate | Every page is read the way a viewer displays it: the crop box, clipped to the media box, turned by `/Rotate` and scaled by `/UserUnit` into points, so a landscape page reads across whether its box is wide or a tall one turned. The document states its `PageGeometry`: the size most pages share, pages of other sizes being named under `pdf.import.page-size-mixed`. A PDF declares no margins, so they are read off where the body's runs, tables and pictures sit on the pages of that size - the column everything was drawn in, which is what keeps a table at the width it was drawn. Where that box stops more than a quarter of the page short of the right edge or the foot, the gap is a short line or a page that ended early, and the margin opposite stands in. Running content lifted out of the body sits in the margins, at the distance from the edge it was drawn at | Page tree plus neutral geometry. A reading of the ink, not a statement the file made |
| Form XObjects | Candidate | Implemented under bounded recursion and a visited set | Recursion/resource limits |
| Transparency groups and blend modes | Candidate | Not interpreted | Neutral graphics compositing ownership |
| Patterns and shadings | Candidate | Not interpreted; reported as dropped artwork | Shared graphics capability; bounded evaluation |
| Underline and strikethrough, recovered from painted bars | Candidate | PDF has no text-decoration operator: a producer sets the word and then strokes a bar under or through it. A thin horizontal bar becomes a run's `Underline` or `Strikethrough` when it is none of a grid's own lattice lines, sits within a fraction of the font size of that run's baseline — measured in ems, below it or barely above for an underline and a tenth of the em or more above for a strikethrough — is accounted for along most of its length by the runs it spans, and starts and stops with them - within an em at either end, so a paragraph's bottom border running a column's width under a label and a tab-set value is a rule, not their underline. It is weighed as painted: a stroke's pen counts, and a bar heavier than a tenth of the em is a separator. A bar the text under it cannot account for stays a rule and is reported as dropped artwork | Inference from geometry, like the table reconstruction, and reported as one: the bars read this way are counted as read back rather than dropped |
| Ruled tables, reconstructed from path geometry | Candidate | A grid of painted rules is read back as a `DocumentTable` and reported with `pdf.import.table-reconstructed`: the text inside is arranged into the cells the rules bound, read row-major, and each cell carries the borders and the fill painted behind it. Rules are grouped by the regions that touch, so **several tables on one page** are several tables — but two grids stacked directly on one another share the rule between them, arrive as one region, and are read as the one closed box the page actually drew. A rule the producer painted in **pieces** is joined back into the single line it depicts before any of this is asked, so a column line that stops at every crossing bounds cells exactly as an unbroken one does - whatever colour each piece is, since two tables stacked in two greys share a column line painted half in each. **Borders** carry the colour and the pen width they were stroked with, each cell's edge reporting the piece painted along most of it; a hairline keeps the model's default weight rather than becoming a border that cannot be seen. **Ruled**: a closed outer boundary with at least two columns by two rows; a missing interior edge is read as the **merge** it is, giving column and row spans, with a continuation cell where a merge covers a lower row. **Partly ruled**: claimed only behind an anchor of at least three parallel rules that overlap along their length, which puts one strictly inside the region; the divisions the rules leave out are then read off the text - one row per line, columns from vertical lanes no glyph crosses - and an inferred division carries no border. Alignment with no anchor is never a table. **Framed**: a closed box ruled on all four sides around text, with no run crossing its edge, at least 24 points each way and enclosing no more than half the page, is read as a one-cell table - how every format the model writes holds a bordered note - and reported apart from the grids, as a box rather than a claim of tabular data. **Continued across a page boundary**: joined into one table when the columns match and neither page drew anything between the halves - a mapped page break is then not written inside it, which is what lets the range stay contiguous. Page furniture - text the document marks as an artifact above or below everything else on its page - is not drawn between the halves, so a running footer does not stop the join. Anything above the second half or below the first stops the join, and that is also what keeps two identical tables on consecutive pages apart, since nothing about their columns could. **Nested**: a grid drawn inside a cell is that cell's table, held in `TableCell.Tables` with its paragraphs inside the cell's range, which is the convention the other codecs write; a run inside both grids belongs to the innermost one | Reconstruction, not a declaration the file made: PDF states nowhere that a table is a table. The partly ruled path additionally reads the document's text geometry, and says so |
| Backgrounds, recovered from fills beneath text | Candidate | A filled area painted before a run and lying under it becomes the run's `Background`: the band behind a heading, a highlighter's stroke, the panel behind a note. Only the topmost fill under a run counts, so the words on a white box drawn over a band get no background; a fill in the paper's colour, a cell's shade - already the cell's shading - and a fill covering most of the page, which is the page's colour, are none. A fill painted after the run lies over it and is not its background. White text on a dark band is what makes this matter: without the band it reads white on white | Inference from geometry and paint order, like the decorations, and reported as one: fills read this way are counted as read back rather than dropped |
| Running headers and footers, recovered from page furniture | Candidate | Text the document marks as an artifact (§14.8.2.2) wholly above or below everything else on its page is page furniture. Where it reads the same on every page that drew anything - at least two - it becomes the document's `RunningContent` header or footer; where it does so on every page after the first, which carries its own, it is stated with `DifferentFirstPage`. Anything else - a folio counting the pages, a head that follows the chapter - varies page by page, and stays in the body where it was drawn, never inside a table joined across its page. Artifacts level with the text stay in the body | Repetition is the evidence, so a one-page document lifts nothing; the reading-order note says which way each band went |
| Optional content groups | Candidate | The catalog's default configuration `/D` is read and honoured: content in a group it turns off is omitted and reported with `pdf.import.optional-content-omitted`. `/BaseState`, `/ON`, `/OFF`, and OCMD membership under `/P` are applied; visibility expressions `/VE`, alternate `/Configs`, and usage applications `/AS` are not, and content they govern is kept. What the configuration decides is which content is omitted and whether a link is projected, not how an annotation is classified: an unapplied Redact or an active action on a group it turns off is still reported, because a declaration about presentation does not lift an overlay off the content under it or take an action out of the file. `IncludeHiddenOptionalContent` takes every layer and still reports the configuration | Reading a declared configuration, not judging visibility; expression and usage-application evaluation outstanding |
| DeviceN / Separation color | Post-V1 | Not interpreted | Color-management and conformance review |

## Semantics, metadata, and active content

| Capability | V1 status | Behavior today | Notes / gate |
|---|---|---|---|
| Normalized title/author/subject/keywords/dates | Candidate | Implemented on read and write; nothing else crosses | Allowlist only; privacy tests |
| XMP read into the allowlist | Candidate | Implemented; XMP wins per field, `Info` is the fallback, disagreement is reported by field name | IP-004 approved for the read subset; bounded non-resolving reader |
| Raw XMP preservation | Rejected | Read for the allowlist, then dropped | Never preserved and never written; excluded by V1 scope rather than by clearance |
| Links as inert semantic values | Candidate | Implemented; admitted by `PdfUriPolicy`, revalidated before output. That policy is this codec's own and is configurable; it is not `DocumentLinkTarget`, the fixed predicate the five interchange codecs share. It requires an absolute URI, so a target that is only a same-document `#fragment` — which those five carry — is refused here with `document.uri.rejected`, and the run is written as plain text with its other formatting intact. No opt-in reaches that case: the absolute test runs before the scheme switch. `http` and `mailto` also need an explicit caller opt-in where the five admit them unconditionally. A fragment inside an absolute URI is a different thing and is kept, canonicalized | Never dereferenced by the codec. Refusing the fragment costs nothing a fragment could deliver: this build emits a URI action and no destination, so internal destinations stay deferred ([roadmap §9.3](pdf-support-roadmap.md#93-images-and-links)) |
| Annotations | Candidate | Link annotations only; the rest inventoried on every page and every layer | Allowlisted non-active subset only |
| AcroForm / XFA | Post-V1 | Detected; signature fields reported | No form execution or fidelity claim |
| Attachments and embedded files | Rejected | Detected and reported; never extracted | No extraction or activation in V1 |
| JavaScript and active actions | Rejected | Detected and reported; never executed | Diagnose and ignore without execution |
| Redaction or secure sanitization | Rejected | An unapplied Redact annotation raises an error-severity warning, on a page that yielded no text and on a layer outside the default presentation as much as anywhere else | Conversion is not redaction |
| Reading order and paragraphs, inferred from page geometry | Candidate | A page the structure tree does not order is read by its geometry, and says so under `pdf.import.reading-order-heuristic`. **Columns** are split at vertical gutters at least 24 points wide. A column holds 15% of the page's runs, or sets lines of its own beside the other columns' text - at least two of them between the others' lines rather than level with one, and those no fewer than a third of the lines it sets beside that text - so a narrow panel beside small print, whose producer drew it a letter at a time and left it a few percent of the runs, is read whole after the column beside it instead of line by line into its sentences. Anything else across a gutter is merged back: a running head or a folio stands above or below the text, and a narrow table column or a column of line numbers stands level with the rows it belongs to, so each is read where it sits. **Lines** form on shared baselines. **Paragraphs** break at a gap noticeably wider than the lines' own spacing - weighed against the smaller of two sizes that differ by a quarter or more, so a heading set close above smaller text stands apart, the size of a line being the one most of its letters are set in - at a first-line indent, at a line that stopped short with room for the next word, and at a list marker; only a run counting 1, 2, 3 from one becomes a numbered list | A reading of the ink, not a statement the file made. A run is whatever the producer made it, which is why a share of runs is not the only test of a column |
| Tagged PDF / structure tree, read for reading order only | Candidate | The tree is walked for its sequence, and for which element holds each piece of marked content - its shape, never its roles - and used to order a page whose runs it accounts for in full; a page it covers only partly falls back to geometry whole. Paragraphs break where the tree's blocks do: an element holding text is a block, and one nested inside an element that holds text of its own - a link in a sentence - runs on inside it; within one block no spacing rule splits a paragraph, so a line cut short by a long word that wrapped stays a wrap. Items that continue one line are read as that line. On a page with a ruled table, the text around the table keeps the declared order and each grid takes the place of its first run. Artifacts are exempt from the coverage test — §14.8.2.2 puts them outside the logical content and a tree never places one, so counting them sent every document with a running head or a folio back to geometry — and are kept: as running content where they repeat, and otherwise read above the declared body or below it by their own geometry. Untagged runs are not exempt and still cost the page its declared order. Roles, heading levels, lists, tables, and the role map are all ignored | Sequence and grouping only; no accessibility or conformance claim follows, and §14.2 still owns the rest |
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
