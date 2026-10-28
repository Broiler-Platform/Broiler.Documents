# IP, Licensing And Provenance Notes

This folder holds the rights record for every format the component reads or
writes, and the provenance record for the code that implements them. Open items
are tracked in the [roadmap](../roadmap.md#rights-and-provenance).

| File | Contents |
|---|---|
| [rtf.md](rtf.md), [docx.md](docx.md), [odt.md](odt.md), [html.md](html.md), [markdown.md](markdown.md), [csv.md](csv.md), [xlsx.md](xlsx.md), [ods.md](ods.md), [pdf.md](pdf.md) | One register per format: every decision, its evidence, the approved labels, and the review record |
| [pdf-approved-sources.md](pdf-approved-sources.md) | Sources PDF code may be written from, and the similarity-review log |
| [pdf-corpus-manifest.json](pdf-corpus-manifest.json), [schema](pdf-corpus-manifest.schema.json) | Provenance and rights for every PDF fixture. Empty: every test PDF is generated in code |
| [briefs/src-017-normative-tables.md](briefs/src-017-normative-tables.md) | Whether normative code tables (T.4/T.6, CFF strings, T.88, T.800) may be reproduced |
| [briefs/ip-012-font-embedding.md](briefs/ip-012-font-embedding.md) | IP-012 embedding and subsetting decision, approved 2026-10-19 with fsType enforcement |
| [briefs/font-path.md](briefs/font-path.md) | How the writer gets a font: caller-configured, nothing bundled (decided 2026-09-02) |

## Standard of review

**No lawyer has reviewed any of this.** Every decision was made by an engineer
reading published evidence (licences, patent declarations, standards front
matter, expiry records, and this repository's own source), then taken by the
project reviewer, Maik Ratzmer. `Approved` means "the project judged the
recorded evidence sufficient to proceed", nothing more. It is not a clearance,
an opinion or a warranty. No freedom-to-operate search has been made, and
patent-freedom is claimed nowhere.

| Question | Settled by |
|---|---|
| What this repository contains, imports or reproduces | Inspection anyone can repeat, made mechanical by a guard test where possible |
| Third-party licence and patent positions | The published evidence named on the row, decided by the project reviewer |
| Product, scope, naming and wording | The project reviewer's decision, recorded with its reasoning |
| Anything the evidence does not settle | Not decided. It stays a pending row stating what a decision would need |

A pending row still blocks: no implementation claim or public statement may rest
on it. [ADR 0013](../adr/0013-standards-ip-provenance-and-claims-beyond-pdf.md)
sets this standard for the whole component and replaces ADR 0011's demand for
qualified legal review before clearance. Every register already runs under it,
though the ADR itself is still marked *Proposed*.

## Where each format stands

| Format | Rests on | State |
|---|---|---|
| [ODT](odt.md) | An OASIS royalty-free mode, plus Sun's covenant and the Microsoft OSP | Every row decided; position assessed green |
| [DOCX](docx.md) | The Microsoft Open Specification Promise, which names Ecma-376 and all three ISO/IEC 29500 editions | Every row decided |
| [RTF](rtf.md) | The same promise, which names `[RTF]` itself, and the format's age | Every row decided |
| [HTML](html.md) | Two independent royalty-free patent policies (W3C, WHATWG); parsing is covered by Broiler.DOM's own register | Every row decided |
| [Markdown](markdown.md) | No instrument at all; assessed on absence | Every row decided |
| [CSV](csv.md) | An Informational RFC with no obligations, and conventions with no owner | Evidence assembled 2026-10-12; **every row pending**, including an IPR disclosure search still to be made by hand |
| [XLSX](xlsx.md) | The Microsoft Open Specification Promise, which names Ecma-376 and ISO/IEC 29500 | Evidence assembled 2026-10-28; **every row pending** |
| [ODS](ods.md) | OASIS royalty-free mode, Sun covenant, and Microsoft OSP | Evidence assembled 2026-10-28; **every row pending** |
| [PDF](pdf.md) | Adobe's ISO 32000-1 public patent licence, plus a row per referenced technology | Every construct, filter and codec row decided; IP-012 embedding and subsetting approved 2026-10-19; source rows SRC-016 to SRC-019 and SRC-024 to SRC-026 pending (decisions deferred) |

Worth keeping in view:

- **One decision changed what users see.** DOCX-IP-006 forbids a format label
  naming a vendor or its product, so `Word Document (*.docx)` became
  `DOCX Document (*.docx)`. A guard in the aggregate repository enforces it.
- **One decision was a reading, not a finding.** MD-IP-002's naming clause binds
  products derived from its author's software, and nothing here is derived from
  it. That rests on an inspection of the tree.
- **The weakest position looks the safest.** Markdown's is assessed on the
  absence of any instrument, which is weaker in kind than a mode or a promise.
- **A royalty-free policy is not patent freedom.** Every patent row records what
  its instrument does not reach.

## Rules every format follows

- **Own register, own prefix, own claim gate** (ADR 0013), so a pending row in
  one format cannot silence another format's claims.
- **Negative wording rule.** Nothing describes an implementation as patent-free,
  royalty-free, certified, endorsed, compliant or conformant, and no label names
  a vendor's product. Guard tests check every package description, and the
  approved labels are in each register.
- **Authored, not copied.** No data file sits beside a codec, and no
  specification text, table or test material is committed. Clauses are cited by
  number. The few normative constants that could not be authored from an
  underlying fact are transcribed openly and carried as pending source rows.
- **No third-party documents.** Possession is not permission to redistribute.
  Every register rejects third-party documents as fixtures by default, per
  artifact, and a guard refuses committed `.rtf`, `.docx`, `.odt`, `.fodt`,
  `.html`, `.csv` and `.pdf` files.
- **User content grants nothing.** Opening or converting a document gives
  Broiler no right to reuse what is in it. Callers are responsible for their
  authority to extract, copy and transform text, metadata, fonts, images and
  profiles.
- **Provenance per change.** A PDF change records the source IDs it used, what
  it added, whether anything was adapted, and a similarity review against
  unapproved code ([pdf-approved-sources.md](pdf-approved-sources.md#contributor-provenance-declaration)).
  Old Broiler PDF code and third-party PDF libraries are never sources (IP-019,
  SRC-008, SRC-009). Independent implementations may serve as black-box oracles
  under their own terms.

## Reading older references

Review records quote section numbers such as "roadmap §9.3" or "§14.1". They
refer to the PDF support roadmap as it stood on the day of the decision. That
document has been split; [docs/README.md](../README.md#older-section-references)
maps each old section to where its content lives now.
