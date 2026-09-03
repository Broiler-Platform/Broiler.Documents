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

Every format this component reads now has a rights record. ODT's is decided and
assessed green; the four written on 2026-09-03 are evidenced and **undecided**.
Each is its own register with its own ID prefix and claim gate, per ADR 0013.

| Format | Register | Rests on | State |
|---|---|---|---|
| ODT | [odt](odt-ip-licensing-register.md) | An OASIS royalty-free mode, plus two covenants | Decided, green; ODT-IP-010 pending |
| DOCX | [docx](docx-ip-licensing-register.md) | The Microsoft OSP, which names Ecma-376 and all three ISO/IEC 29500 editions | All rows pending |
| RTF | [rtf](rtf-ip-licensing-register.md) | The same promise, which names `[RTF]` itself | All rows pending |
| HTML | [html](html-ip-licensing-register.md) | Two independent royalty-free patent policies | All rows pending |
| Markdown | [markdown](markdown-ip-licensing-register.md) | Nothing — assessed on the absence of any instrument | All rows pending |

Owned here, in the order it is worth doing:

- **The reviewer's decisions on all four.** The patent rows are evidence-complete;
  the inspection rows are repeatable and guarded by `FormatClaimGuardTests`.
- **DOCX-IP-006 has a live consequence and the others do not.** Its proposed rule
  forbids naming a vendor or its product in a label, and the hosts ship
  `Word Document (*.docx)`. Approving the rule means changing that string;
  approving the current string means deciding a product name is acceptable as a
  format label. Nothing is enforced against it until one of those happens.
- **MD-IP-002 is the row that needs thought rather than evidence.** Markdown's
  licence carries a naming clause binding products derived from its author's
  software. The register's reading is that nothing here is derived from it, and
  says so explicitly so the reading can be rejected.
- **HTML-IP-004 cannot be closed in this component.** The HTML codec does not
  parse HTML — `Broiler.DOM` does, and that repository has no register. The
  format-rights work left in this project is now someone else's repository.
- Markdown is assessed on absence, which is a weaker kind of position than the
  others even though it feels safer. The register says so rather than trading on
  how unlikely a Markdown patent sounds.

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
