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
- Re-review ADR 0004 and the read/write option surface now that the §6.2
  resource context has landed.
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

Done, and the section is kept for what it records rather than for work it holds.
[ADR 0013](adr/0013-standards-ip-provenance-and-claims-beyond-pdf.md) makes the
ADR 0011 discipline component-wide and gives each format its own register, ID
prefix and claim gate; ODT's is
[the ODT IP and licensing register](odt-ip-licensing-register.md), and **every one
of its eleven rows is decided**, with the position assessed green.

- The two rows this roadmap called the hard ones came apart on inspection rather
  than on argument. **ODT-IP-002**'s reciprocity is *defensive* — Sun's covenant
  is withdrawn only from a party that attacks OpenDocument implementations — so
  it asks nothing of an implementer and cannot subtract from a mode obligation
  that binds Sun either way. The two instruments were never in tension.
  **ODT-IP-003** rested on a false premise: there is no separate IBM ODF
  declaration to obtain, the TC's IPR page carries Sun's two instruments and
  nothing else, and the secondary sources had conflated IBM's general
  interoperability pledge with an ODF-specific one that does not exist.
- **Green is a risk judgement on recorded evidence, not a clearance.** No legal
  review, no patent-freedom claim, no freedom-to-operate determination — nobody
  searched, and a search is what such a determination needs. It unlocks no
  wording either: ODT-IP-007's negative rule stands and `OdtClaimGuardTests`
  fails the build over it.
- **The roadmap's own earlier framing was stale and is not restored.** It asked
  for "a legal reading on the ODF covenants", which contradicts the standard of
  review the register adopted on 2026-09-02 and ADR 0013 made component-wide:
  evidence-based acceptance, with counsel reserved for rows the evidence does not
  settle. It turned out these were not such rows.
- Left unrecorded rather than unresolved: implementation jurisdictions, the
  expiry/review dates, and the succession of Sun's covenant to its acquirer. None
  is blocking under this register's standard, and the rows say so individually.
- RTF, DOCX, HTML and Markdown now have registers of their own, and **every row
  in all four is pending a decision**. See [Format rights](#format-rights).

## Format rights

Every format this component reads has a rights record, and **every row in all
five is decided**. Each is its own register with its own ID prefix and claim gate,
per ADR 0013. This section is now a record rather than a list of work.

| Format | Register | Rests on | State |
|---|---|---|---|
| ODT | [odt](odt-ip-licensing-register.md) | An OASIS royalty-free mode, plus two covenants | Decided; assessed green |
| DOCX | [docx](docx-ip-licensing-register.md) | The Microsoft OSP, which names Ecma-376 and all three ISO/IEC 29500 editions individually | Decided |
| RTF | [rtf](rtf-ip-licensing-register.md) | The same promise, which names `[RTF]` itself | Decided |
| HTML | [html](html-ip-licensing-register.md) | Two independent royalty-free patent policies | Decided |
| Markdown | [markdown](markdown-ip-licensing-register.md) | Nothing - assessed on the absence of any instrument | Decided |

**Decided is not cleared, and the distinction is the whole point of the exercise.**
No lawyer reviewed any of it, patent-freedom is claimed nowhere, and no
freedom-to-operate determination has been made anywhere - nobody searched, and a
search is what one would take. What each row records is that this project judged
the evidence in front of it sufficient to proceed, and what evidence that was.

Four things worth keeping visible now that the work is done:

- **One decision changed something a user sees.** DOCX-IP-006 forbids a format
  label naming a vendor or its product, so `Word Document (*.docx)` became
  `DOCX Document (*.docx)`. A guard in the aggregate repository enforces it.
- **One decision was a reading rather than a finding.** MD-IP-002's naming clause
  binds products derived from its author's software, and nothing here is - which
  is a claim about this tree, and the tree could be inspected. A licence question
  settled by looking is the cheapest kind, and this was the only one.
- **One row closed instead of being approved.** HTML-IP-004 existed to say that
  the parsing half of HTML had no rights record anywhere. `Broiler.DOM` has one
  now. The scope limit stands: a claim about Broiler's HTML support needs both
  registers, not either.
- **The registers are strongest where they say least.** Every patent row records
  what its instrument does not reach, and Markdown's records that a position
  assessed on absence is weaker in kind than one assessed on a mode or a promise,
  however much safer it feels.

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
stores, cross-reference tables and streams, object streams, the Flate, LZW,
ASCIIHex, ASCII85 and RunLength filters, logical text import through encodings
and `ToUnicode` maps, metadata from both `Info` and XMP, links under the shared
URI policy, and a deterministic PDF 1.7 writer over the standard font names.

Three further technologies are implemented but never composed by default, so a
build that does not ask for them does not link them: JPEG (baseline and
progressive) and CCITT fax
(`Broiler.Documents.Pdf.Images`) and embedded font-program inspection
(`Broiler.Documents.Pdf.Fonts`). Two more are cleared and written but unproven:
JPEG 2000 decodes one tile of a Part 1 codestream and has never decoded a real
image, and JBIG2 decodes generic regions under both coding methods, the symbol
dictionaries and text regions a scanned page is made of, and the refinement that
corrects them, reporting the halftone regions, aggregate coding and every
Huffman-coded form. In both cases what remains is
engineering and evidence, not an approval. Raw image samples reach the model
within the approved DeviceGray, DeviceRGB and Indexed subset; everything outside
it — masks, the other colour spaces, encryption — is detected and skipped with
its own diagnostic. See [PDF extension points](pdf-extension-points.md) for which
of the three states a given technology is in and why.

Residual work owned here:

- The package stays `IsPackable=false` and unregistered until the roadmap's
  read-preview and write-preview gates pass. Implementation is not a capability
  claim, and no feature-matrix entry may reach `Supported` while its
  IP/licensing row is pending.
- The Phase 1 §6.1 contracts are built: `DocumentInput` (replayable probing over
  non-seekable sources, bounded memory-only materialization, explicit ownership),
  the read/write request envelopes, the shared result status and destination
  state, typed-option validation, async overloads, and one catalog
  selection-and-read path. The §6.2 resource context is built as well:
  `DocumentConversionContext` is immutable, gives every resource an opaque
  identity scoped to a context namespace, decides each operation separately and
  fail-closed against the payload digest, rides on `DocumentReadResult.Resources`
  and `DocumentWriteOptions.Resources`, and is consumed by the DOCX, RTF, HTML,
  Markdown and ODT writers, so a resource cannot bypass policy merely by changing
  output codec. The metadata envelope is built too: `DocumentMetadata` carries the
  nine frozen fields with missing and explicitly empty kept distinct, and
  `DocumentDate` keeps a timestamp's stated precision and optional offset without
  inventing a zone the source never gave. It is promoted rather than PDF-shaped —
  DOCX reads and writes it through `docProps/core.xml` and `docProps/app.xml`,
  ODT through `meta.xml`, which is the non-PDF consumer §6.2 gates promotion on,
  and each codec reconciles its own sources before the envelope sees them. The
  transfer policy is that nothing copies a read result's metadata into a write:
  a caller who wants that performs it, and `DocumentMetadata.With` is where they
  correct what should not survive. A write reports all three of §6.2's outcomes —
  emitted, narrowed, stripped — naming fields and never quoting their values.
  One piece of §6.2 is left: `DecodeEmbeddedObjects` still has to give way to the
  caller-composed image-import path, which waits on the deprecation policy
  recorded under [API contract cleanup](#api-contract-cleanup).
- §6.4's model review is nearly done. Dimensions are points throughout and the
  RichEdit and Writer boundaries convert explicitly through
  `BFontStyle.PointsToPixels` rather than handing a point value to a pixels API;
  `InlineImage` is built on `DocumentResourceId` and `BImageResource`, with
  nullable point dimensions, intrinsic pixels at 96 per inch when both are
  absent, and a reportable unplaceable case instead of a sentinel; non-finite
  and negative measurements are rejected wherever the model lets one be stated;
  and justified alignment arrived with named DOCX, ODT and HTML consumers rather
  than ahead of them. The format-neutral document style defaults landed too:
  `DocumentStyleDefaults` states a document's size and logical family, DOCX and
  ODT read theirs from `w:docDefaults` and `style:default-style`, and the two
  halves of §6.4's rule are now visible in the two consumers — the renderer falls
  back to its own face when a document names none, because what it draws is
  looked at rather than published, and the PDF writer does not. The size always
  resolves, at twelve points; the family may be absent, and absent is an answer
  rather than a licence to pick one.
- §6.5 is answered in parts. The V1 JPEG tuple is frozen and enforced — Huffman
  SOF0, SOF1 and SOF2 at 8 bits, every other frame type and precision refused by
  name — and the font inspector exists: `BFontProgramInspector` in
  `Broiler.Graphics` validates the table directory in checked arithmetic, refuses
  a table that overlaps the directory describing it, reads the character map only
  within its own slice, and pins the accepted tuple, so WOFF, WOFF2, collections,
  variable fonts, CFF2, colour and bitmap glyphs, Graphite and AAT are refused by
  name. It is separate from the rasteriser's parser on purpose: that one repairs
  what it can, which is right for a face a caller provisioned and wrong for a
  program that arrived inside somebody's document. Media's limits followed: `MediaLimits` now
  bounds image dimensions, components, sampling factors, scans, restart
  intervals, marker segments, coefficient memory and total blocks alongside the
  byte and pixel budgets it already had, and the JPEG decoder validates the
  declared numbers before allocating from them — the coefficient count is
  computed in `long`, because at 65535 square with 4×4 sampling the int product
  is exactly 2³², which wraps to zero and lets a frame asking for 17 GB satisfy
  any budget. Two of those budgets only bite below the decoder's own scope, which
  admits 1 or 3 components and sampling factors of 1 to 4 and refuses the rest
  first; they matter to a caller stricter than the codec, not as holes closed.
  §6.5's sync/async split closed it: `ImageCodec` declares the CPU half as
  `DecodeCore` and builds both public paths on it, so the admitted modes, the
  colour handling and the limits are identical on the two by construction rather
  than by review. The sync path reads synchronously rather than blocking on the
  async one, which is the shape that deadlocks where continuations run on a
  single thread, and there is a test that fails if that is reintroduced. §6.5 is
  done.
- `Broiler.Documents.Pagination` does not exist; the PDF writer paginates
  internally against a replaceable metrics provider until the shared paginator
  does.
- §6.6's controls are built and its content is not, which is the honest split.
  `tests/pdf/tools/manifest.json` and `tests/pdf/performance-baseline.json` exist
  with their schemas and guards and no rows — the Phase 0 corpus pattern, where a
  control is built so content has somewhere to land under review. A fuzz harness
  runs the read and probe surfaces on a bounded campaign in every pull request
  and a half-hour-per-target campaign nightly, recording each failure's input
  hash, seed, harness version, limit profile, failure class and corpus-rights
  disposition. Correcting one claim while doing it: §6.6 said a
  `documents-pdf.yml` with a test-count guard and explicit Graphics and Media
  runs was created on 2026-08-25, and it never came across the repository split —
  `ci.yml` had neither until now.
  What stays open needs decisions rather than code. No oracle is pinned, because
  each needs a licence review and a manifest row; the corpus stays empty, because
  each artifact needs a rights decision; and the campaign is mutation-based
  rather than coverage-guided, because an instrumenting engine is itself a tool
  that needs the first of those before CI may run it. The nightly job is
  deliberately not a required check while that is true.

## Stabilization

- Add sustained fuzz/property coverage and allocation/performance baselines for
  every parser and writer that accepts untrusted input.
- Validate package consumption from a feed without the aggregate repository.
- Complete dependency, license, API-compatibility, and human review before a
  stable release.

UI host integration, RichEdit clipboard wiring, and the Writer Formatting Codes
experience are owned by their UI/application layers rather than this component.
