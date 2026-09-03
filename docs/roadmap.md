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

The register now exists. [ADR 0013](adr/0013-standards-ip-provenance-and-claims-beyond-pdf.md)
makes the ADR 0011 discipline component-wide and gives each format its own
register, its own ID prefix, and its own claim gate; ODT's is
[the ODT IP and licensing register](odt-ip-licensing-register.md), with eleven
rows, primary sources for the OASIS mode and the covenants, and
`OdtClaimGuardTests` binding its inspection findings to the code. **No row is
decided.** What was built is an evidence record and a claims boundary, not a
clearance. Owned here:

- **The reviewer's decision on ODT-IP-001 through ODT-IP-003 and the ODT-IP-007
  label set.** ODT-IP-001 is written as ready: the mode is confirmed from two
  primary sources and nothing further was identified as needed. ODT-IP-002 is the
  one thin row — Sun's covenant carries a reciprocity condition that sits oddly
  beside a TC mode permitting none, and the interaction was not analysed — and is
  where counsel would be worth its cost. ODT-IP-003 is not required for the
  other two and needs primary instruments nobody has obtained.
- Sign off, or reject, the inspection findings on ODT-IP-004 through ODT-IP-006
  and ODT-SRC-001 through ODT-SRC-002. These are repeatable by anyone and three
  of them now fail the build if they stop being true, so the decision is whether
  the record is accepted rather than whether the facts hold.
- **The roadmap's own earlier framing was stale and is not restored.** It asked
  for "a legal reading on the ODF covenants", which contradicts the standard of
  review this register adopted on 2026-09-02 and ADR 0013 made component-wide:
  evidence-based acceptance, with counsel reserved for rows the evidence does not
  settle. ODT-IP-002 is written up as exactly such a row rather than waved
  through, which is the control working as intended.
- Until the rows clear, the [ODT conformance document](odt-conformance.md) and
  the register bound what may be said, and no marketing copy may go further than
  ODT-IP-007's label set — which is itself still proposed. The Writer registers
  the codec on every head (Broiler-Platform/Broiler.Writer#65); shipping a codec
  is not a rights claim, and the guards are what keep it from becoming one.
- RTF, DOCX, HTML, and Markdown have **no register and therefore no cleared
  position** — an unasked question rather than an answered one. ADR 0013 says so
  explicitly and does not answer it for them.

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
