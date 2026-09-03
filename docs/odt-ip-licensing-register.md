# ODT IP, Licensing, And Standards Register

**Register version:** 1.2
**Updated:** 2026-09-03 (the inspection findings signed off; the covenant rows remain open)
**Owner:** Broiler.Documents maintainers
**Approval authority:** Maik Ratzmer (GitHub [MaiRat](https://github.com/MaiRat)), project reviewer
**Governance:** [ADR 0013](adr/0013-standards-ip-provenance-and-claims-beyond-pdf.md),
which extends the [ADR 0011](adr/0011-pdf-standards-ip-provenance-and-claims.md)
discipline beyond PDF and adopts the evidence-based standard of review

---

## ⚠ NO LAWYER HAS REVIEWED ANY OF THIS

**Read this before relying on a single row below.**

Most of this register is now decided. The OASIS licensing mode (ODT-IP-001), the
label set (ODT-IP-007), and every row inspection settles — what this repository
contains, imports, reproduces, and consulted — were approved by the project
reviewer on 2026-09-03 on the evidence recorded here.

**The covenant rows are not, and they are the ones a rights position would rest
on.** ODT-IP-002 and ODT-IP-003 remain open, so nothing about ODF is cleared.

Two separate disclaimers apply, and neither substitutes for the other.

- **No legal review.** As in the PDF register, the standard of review is an
  engineer reading published evidence. No legal opinion was sought, none was
  given, and none is represented here. `Approved` on a row below means "this
  project judged the recorded evidence sufficient to proceed" and nothing more —
  not a clearance, not an opinion, not a warranty. **No freedom-to-operate
  determination has been made**, and none is attempted.
- **Who decided, and who did not.** The evidence on the patent rows was gathered
  by Claude (Anthropic's coding agent, engineering seat) on 2026-09-03 at the
  maintainer's direction. Assembling an evidence record is not approving it: the
  two approvals above are the approval authority's own, taken on that record, and
  every remaining row stays pending until he takes those too.

**What that means for you.** Nothing in this register clears anything. **No
freedom-to-operate determination has been made**, and none is attempted. Where a
row reads that a licensing mode is royalty-free, that is a reading of a published
policy, not a search and not advice. Anyone shipping this where the answer
matters should take their own advice.

**Patent-freedom is not claimed for ODF, anywhere in this register.** Neither is
unconditional royalty-freedom. See ODT-IP-007 for what may actually be said.

## Who decides, and on what

The division is [ADR 0013](adr/0013-standards-ip-provenance-and-claims-beyond-pdf.md)'s,
and matches the PDF register's:

| Question | Settled by |
|---|---|
| What this repository contains, imports, or reproduces | Inspection of the source tree, which anyone can repeat — and, where it can be made mechanical, a guard test |
| Third-party licence and patent positions | The published evidence named on the row, read by an engineer, with the risk stated in plain words — **then decided by the project reviewer** |
| Product, scope, naming and wording | The project reviewer's decision, recorded with its reasoning |
| Anything where the evidence does not settle the question | **Not decided.** Written up as a pending row with what a decision would need |

The rows split cleanly along that line, and the split is why this register is
useful before it is signed. ODT-IP-004 through ODT-IP-006 and ODT-SRC-001 through
ODT-SRC-002 are answerable by looking at this repository, and looking is what was
done. ODT-IP-001 through ODT-IP-003 are about other parties' rights and are not
answerable that way at all.

## Decision fields

Each row must eventually identify the exact feature/subset, specification
edition, source and acquisition right, implementation jurisdictions, patent
evidence, copyright/license conditions, reviewer, decision date, expiry/review
date, and obligations. Changes to scope reopen the row. Implementation
jurisdictions and expiry/review dates are unrecorded on every row below, as they
are on every row of the PDF register; under this standard they are not blocking.

| ID | Technology / exact scope | Primary evidence | Current assessment | Status / required action |
|---|---|---|---|---|
| ODT-IP-001 | The OpenDocument text-document format as implemented: the read/write subset enumerated in [the ODT conformance document](odt-conformance.md), over ODF 1.0 through 1.3 packages. Spreadsheet, presentation, drawing, and flat-XML document types are outside the row and outside the codec. | [Open Document Format for Office Applications (OpenDocument) Version 1.3](https://docs.oasis-open.org/office/OpenDocument/v1.3/OpenDocument-v1.3-part1-introduction.html), OASIS Standard, 27 April 2021; the [OASIS IPR Policy](https://www.oasis-open.org/policies-guidelines/ipr/); the [OASIS OpenDocument TC IPR statement](https://www.oasis-open.org/committees/office/ipr.php) | The TC operates under the **"RF on Limited Terms Mode"** of the OASIS IPR Policy, stated both on the TC's IPR page and in the specification's own front matter. Under that mode an Obligated Party must grant a nonexclusive, worldwide, non-sublicensable, perpetual patent licence over its Essential Claims, without royalties or fees, to make, use, import, offer to sell, sell, and distribute Licensed Products — and, unlike the RAND modes, may not impose further conditions beyond those the policy enumerates. That is a materially better starting position than a format governed by a single vendor's licence, and it is why an ODF codec needed no licence negotiation to be written. **What it does not establish.** The obligation runs to Obligated Parties over their Essential Claims. It says nothing about the patents of anyone who never participated in the TC, exactly as IP-001 records that Adobe's licence reaches Adobe-owned claims and no further. Patent-freedom is not claimed. Risk assessed **low** on the recorded evidence. | **Approved 2026-09-03** by the project reviewer, on the evidence recorded on this row and no other basis. The mode is confirmed from two primary sources and the licence elements are quoted from the policy; nothing further was identified as needed, which is why the row was written as ready rather than as open. **What the approval does not reach:** anything ODT-IP-002 and ODT-IP-003 carry. The mode binds the committee's participants over their own Essential Claims, and this row decides that and only that — the covenants are separate instruments on separate rows and both are still pending. Still unrecorded: implementation jurisdictions and the expiry/review date, neither blocking under this register's standard. |
| ODT-IP-002 | The patent covenants published by contributors to the format, as they bear on implementing the subset in ODT-IP-001 | [Sun OpenDocument Patent Statement](https://www.oasis-open.org/committees/office/ipr.php), 29 September 2005; Sun Microsystems IPR Statement, 11 December 2002 — both as linked from the TC's IPR statement | Sun covenants not to enforce its U.S. or foreign patents against OpenDocument implementations, **subject to reciprocity**: the covenant excludes a party that asserts patents against OpenDocument implementations. The 2002 statement separately offers royalty-free licences under Essential Claims for the OpenOffice.org XML File Format Specification, also reciprocally. **The thin spot on this row, stated plainly:** the TC's mode as quoted in ODT-IP-001 permits no reciprocity condition, while this separately published covenant carries one. They are different instruments — a TC obligation and a voluntary covenant — and how they interact was **not analysed here**, because that analysis is not an inspection and not a reading of a single document. Nor was the succession of these covenants to Sun's acquirer traced. Neither point is an obstacle anyone has identified; both are simply unexamined. | **Pending.** This is the row on which counsel would be worth its cost, on the same reasoning the PDF register applies to SRC-017: not because the position looks bad, but because it is the one question here that published evidence read by an engineer does not settle. A decision should say whether the reciprocity condition is accepted as recorded, and whether succession needs tracing. |
| ODT-IP-003 | Patent covenants by contributors **other than** Sun — IBM's Interoperability Specifications Pledge and any comparable instrument | Named in secondary sources only: the Wikipedia articles on the Microsoft Open Specification Promise and on non-assertion covenants, and the Library of Congress format description for ODF 1.1 / ISO/IEC 26300:2006. **The primary instruments were not obtained.** | **Not relied on, and deliberately so.** A pledge described accurately in a tertiary source is still a pledge nobody here has read, and this register does not take licence positions from encyclopaedias. Recorded because its absence corrects something: [the ODT conformance document](odt-conformance.md) has said that "the principal contributors published patent covenants covering conforming implementations", in the plural. Only Sun's is primary-sourced. The plural overstated what this project had read, and the conformance document is corrected to match this row rather than the other way round. | **Pending, and not required.** ODT-IP-001 and ODT-IP-002 do not depend on this row: the mode obligation and Sun's covenant stand on their own primary sources. Clearing it would mean locating IBM's pledge text and any other contributor's instrument and reading what each actually covers. Until then nothing may be said about "the contributors' covenants" as a set. |
| ODT-IP-004 | Acquiring and using the specification text: whether this repository may hold, quote, or reproduce ODF specification material | The ODF 1.3 copyright and licence notice, quoted from the specification's own front matter | The notice permits copying the document and preparing derivative works "that comment on or otherwise explain it or assist in its implementation ... without restriction of any kind", provided the copyright notice and that section travel with every copy, and provides that the document itself may not be modified except in OASIS TC work. That is **more permissive than the ISO publications PDF depends on**, where SRC-001 had to close by establishing that no standard text was committed anywhere. **The permission is nevertheless not relied on**, because the same is true here: inspection finds no specification text, table, figure, or excerpt in the repository. Clauses are cited by number — the conformance document cites ODF 1.3 part 3 §3.17 for white-space processing and part 2 §3.3 for the `mimetype` entry, and cites rather than quotes. Risk assessed **very low**: a permission this project does not need cannot be exceeded. | **Approved 2026-09-03** on the inspection recorded here, which anyone can repeat. Should specification text ever be reproduced — a table of normative constants is the realistic case, as it was for T.4 under SRC-017 — this row is what would then be relied on, and the notice's attribution requirement would attach. Nothing triggers it today. |
| ODT-IP-005 | Implementation provenance: whether the codec embeds third-party ODF code, data, or documents | Inspection of `src/Broiler.Documents.Odt` and the whole tracked tree; `OdtClaimGuardTests`; ODT-SRC-001 | Four findings, each checkable by repeating the inspection. **One:** the codec's directory contains nothing but `.cs` files and its `.csproj` — no data file, table, or fixture of any kind. **Two:** the project takes no package reference at all; its only references are two sibling projects in this component, so no third-party ODF toolkit is present to account for. **Three:** no `.odt`, `.ott`, `.fodt`, `.ods`, `.odp`, or `.odg` file is tracked anywhere in this repository, so no sample document is vendored or redistributed. **Four:** the test packages are constructed in code, in `OdtTestPackage.cs`, rather than committed as files — which is what makes the third finding hold without the suite losing coverage. Risk assessed **very low**, and the position is a property of the tree rather than a claim about it: the first three findings are now enforced by guard tests that fail the build. | **Approved 2026-09-03** on the inspection recorded here. What makes this row cheap to trust is that it is not a promise: three of its four findings are guard tests, so the tree fails the build rather than the row going quietly stale. Adding any data file beside the codec, any package reference, or any committed ODF document reopens this row, and the guards will say so before a reviewer has to. |
| ODT-IP-006 | The platform facilities the codec is built on: ZIP container handling and XML parsing | `System.IO.Compression` and `System.Xml.Linq`; the platform's own licence and notices | The codec adds no compression implementation, no XML parser, and no table or test vector of its own; it calls the runtime. This is a platform dependency rather than a bundled third-party component, and it carries no ODF-specific obligation — the obligation travels with the runtime's own notices, as it does for every other framework API this codec calls. Confirmed on the same reasoning as IP-023, which reached the identical conclusion for `FlateDecode` calling `ZLibStream`. Risk assessed **very low**. One security property is worth recording next to the provenance one: inline DTDs are prohibited when parsing any part, so entity expansion is not an attack surface. | **Approved 2026-09-03 under ODT-IP-005.** A platform dependency carries no ODF-specific obligation, so nothing is carried forward from this row. |
| ODT-IP-007 | "ODT", "OpenDocument", "ODF" naming, and any conformance, certification, or compatibility claim | The label set in [Approved labels](#approved-labels); ADR 0013's claims rule; the negative rule this register's preamble states | Descriptive use of the format's name is what the labels are for, and none of them names a vendor, asserts a certification, or implies sponsorship or endorsement by OASIS. The rule they enforce is the negative one, and it is the same rule the PDF register enforces under IP-018: **nothing describes this implementation as ODF-conformant, standards-conformant, certified, endorsed, patent-free, or unconditionally royalty-free.** Two specific temptations are ruled out by name. The format's licensing mode being royalty-free is a fact about the OASIS mode and **not** a property of this implementation, so "royalty-free" may not appear as a description of what this component gives anyone. And implementing a documented subset is not conformance: the specification defines conformance, this codec implements a subset, and [the conformance document](odt-conformance.md) is titled for the questions it answers rather than for a claim it makes. | **Approved 2026-09-03** for the label set below and no other wording. The negative rule was enforceable before the approval and is unchanged by it: `OdtClaimGuardTests` fails the build on a prohibited claim in any shipped package description, on the codec's format descriptor drifting from the approved short label, and on this register going missing. What the approval adds is authority for the positive labels, which the Writer had already shipped in Broiler-Platform/Broiler.Writer#65 — `ODT` in the format list and `OpenDocument Text (*.odt)` in the save dialog, both of which the table below now covers. Anything else remains a new decision. |
| ODT-IP-008 | ODF package encryption and password protection | ODF 1.3 part 3, the `manifest:encryption-data` element and the package's encryption provisions | Out of scope and refused in code rather than partially handled. ODF password protection genuinely encrypts the parts, so there is nothing to read without a key, and this codec asks for none: an encrypted package is rejected with `odt.package.encrypted` and nothing is decoded. No cryptographic, export-control, or key-handling review has been done, and none is needed while the answer is refusal. | **Blocked for V1, as IP-015 blocks the PDF standard security handler.** Implementing it would require its own architecture and its own rows, and would reopen this one. |
| ODT-IP-009 | Third-party or user-supplied ODF documents used as fixtures or committed to the repository | Per-artifact origin, author, licence, and approval | Possession or public download is not permission to commit or redistribute. None is committed — verified under ODT-IP-005 and guarded — and purpose-built packages constructed in code are the established alternative here, so the row has no live artifact to decide. User-supplied documents a caller opens at run time remain subject to their own rights, and neither the API nor the documentation may imply this component grants any. | **Rejected by default**, per artifact, as IP-020 handles the PDF case. Import only after an independent provenance record and approval. |
| ODT-SRC-001 | The OASIS specification as a source consulted while writing this codec | `src/Broiler.Documents.Odt`, in full; ODT-IP-004 | Every line of the codec was written against the published OASIS specification for this repository. Inspection supports the two halves separately: nothing was copied, per ODT-IP-004's finding that no specification text is present; and nothing third-party was consulted for content, there being no ODF implementation in the tree to have consulted. Structural correspondence to the specification is expected and is not evidence of copying — a reader of ODF that did not follow the standard's element structure would be a reader of something else. | **Approved 2026-09-03** on the inspection recorded here. Closed for the freely published OASIS editions, and for those only. |
| ODT-SRC-002 | ISO/IEC 26300, the ISO/IEC republication of ODF | The ISO/IEC catalogue entry; not obtained, not consulted | **Not consulted and not relied on.** The implementation is written from the OASIS text, whose acquisition right is ODT-IP-004's, so the ISO edition's own terms do not arise. This mirrors how SRC-001 is closed for the freely published ISO 32000-1 rather than for "ISO 32000 editions" at large. The republication remains a true fact about the format and may be stated as one; it may not be cited as a source this project used, and consulting it would reopen this row. | **Approved 2026-09-03 as an unconsulted source**, which is the only thing there is to approve: the row records what this project did *not* rely on. No action while it stays that way, and consulting the ISO edition reopens it. |

## Approved labels

**Approved 2026-09-03 under ODT-IP-007.** These are the wordings this component
and any application built on it may use for the format. Anything else is a new
decision.

| Context | Approved label |
|---|---|
| Format list | **ODT** |
| Save As | **OpenDocument Text (*.odt)** |
| Import | **OpenDocument Text** |
| Export | **OpenDocument Text** |
| Tooltip / Help | **OpenDocument Text (ODT)** |
| Technical documentation | **OpenDocument Text (ODF 1.3, OASIS)** |
| The format family, where it must be named | **OpenDocument Format (ODF)** |

Two things follow that the table does not say outright. The labels are
*descriptive*: each names the format, none names a vendor, and none asserts a
certification or a compatibility relationship with any product. And there is
deliberately **no** approved label of the form "ODF compliant", "OpenDocument
conformant", or "ISO 26300" — the first two are claims ODT-IP-007 forbids, and
the third would cite a source ODT-SRC-002 records as unconsulted.

## What still blocks a claim

What is left is the covenants, and nothing else. Every other row is decided; the
two that are not are the two a rights position would actually rest on.

| Blocker | Kind | State |
|---|---|---|
| ODT-IP-001 | The OASIS mode and what it obliges | **Approved 2026-09-03.** Decides the mode and nothing else; the covenants are ODT-IP-002's and ODT-IP-003's and remain pending |
| ODT-IP-002 | Sun's covenant, its reciprocity condition, and succession | **Pending, and the one thin row.** The reciprocity condition sits oddly beside the TC mode, and the interaction was not analysed. Where counsel would be worth its cost |
| ODT-IP-003 | Other contributors' covenants | **Pending, and not required.** Primary instruments not obtained; nothing may be said about the covenants as a set until they are |
| ODT-IP-004 to ODT-IP-006, ODT-SRC-001, ODT-SRC-002 | What this repository contains and consulted | **Approved 2026-09-03.** Settled by inspection, repeatable by anyone, and three of them guarded mechanically so the approval cannot rot quietly |
| ODT-IP-007 label set | Positive wording for the format | **Approved 2026-09-03** for the recorded labels and no other wording. The negative rule was enforced before the approval and is unchanged by it |
| ODT-IP-008 | Encrypted packages | **Blocked for V1** by scope, not by evidence |

Approving everything answerable by looking does not approve the format. What
inspection settles is what *this repository* did; what ODT-IP-002 asks is what
*other parties* hold, and no amount of looking at this tree answers it. It is
still the one row the evidence does not settle. Nothing here may be described as
cleared.

None of this blocks the codec from shipping, and it is worth being exact about
why, because the PDF discipline's rule sounds like it should. That rule bars a
**`Supported` feature-matrix entry** while a row is pending. ODT has no feature
matrix and makes no support claim: it has a conformance document that describes a
subset and enumerates its limitations, and the Writer offers the format without
describing it as conformant, certified, or cleared. Shipping a codec is not a
claim about rights. Saying things about it is, and that is what these rows and
the guards bound to them govern.

## Review record

| Review | Reviewer | Date | Scope | Result |
|---|---|---|---|---|
| Inspection findings sign-off (ODT-IP-004 to ODT-IP-006, ODT-SRC-001, ODT-SRC-002) | Project reviewer (Maik Ratzmer, engineering seat) | 2026-09-03 | What this repository contains, imports, reproduces, and consulted | **Approved.** Five rows in one decision, because they are one question asked five ways and the answer to each is a fact about this tree rather than a judgement about anyone's rights: no specification text is reproduced, no data file sits beside the codec, no package reference exists at all, no OpenDocument file is committed anywhere, the test packages are constructed in code, and the ISO republication was never consulted. Each is repeatable by anyone in a minute. Three are better than repeatable — `OdtClaimGuardTests` fails the build if they stop being true, which is why signing them off is cheap: the decision cannot rot without something going red. What the sign-off deliberately does not do is make the format any clearer than it was. These rows were always the answerable half; the covenants are the half that matters and they stay open. |
| OASIS licensing-mode review (ODT-IP-001) | Project reviewer (Maik Ratzmer), on the evidence record assembled 2026-09-03 | 2026-09-03 | The OASIS IPR Policy, the OpenDocument TC's IPR statement, and the ODF 1.3 specification front matter | **Approved.** First row in this register to clear, and it clears on a reading of two primary sources that agree: the TC operates under RF on Limited Terms, and the licence elements are the policy's own words rather than a summary of them. Recorded as deciding the mode and nothing else. The row had been written to the point where only the decision was missing, which is what made the decision cheap — no new evidence was sought and none was needed. What it deliberately does not reach: the covenants. Those are separate instruments, they carry a reciprocity condition the mode does not permit, and ODT-IP-002 stays open over exactly that. Jurisdictions and the expiry/review date were not part of the record. |
| Naming and claims review (ODT-IP-007) | Project reviewer (Maik Ratzmer) | 2026-09-03 | Format naming across format lists, dialogs, help text, and documentation | **Approved for the recorded label set.** The negative rule — nothing describes this implementation as ODF-conformant, standards-conformant, certified, endorsed, patent-free, or royalty-free — was already enforced mechanically before this decision and is unchanged by it, which is the point of having built the guard first: the approval widens what may be said rather than starting the control. Two labels were already in front of users, `ODT` in the Writer's format list and `OpenDocument Text (*.odt)` in its save dialog, and the decision covers exactly those. Deliberately absent from the table: any "ODF compliant" or "ISO 26300" form, the first being a claim the row forbids and the second citing a source ODT-SRC-002 records as unconsulted. |
| ODF evidence assembly and register creation | Claude (Anthropic coding agent, engineering seat), at the maintainer's direction — **not legal counsel, and not the approval authority** | 2026-09-03 | The OASIS IPR mode and policy, the ODF 1.3 specification notice, the covenants linked from the TC's IPR statement, and inspection of this repository's ODT source, tests, and tracked files | **Evidence recorded; no row decided.** Primary sources located and read for the mode, the licence elements, the specification's copyright notice, and Sun's two statements. Three findings settled by inspection and made mechanical as guard tests. Two things the record could not do: decide any row, which is the approval authority's; and obtain IBM's pledge text, which is recorded as unobtained rather than summarized from secondary sources. Two corrections fell out of the work — the conformance document's plural claim about contributors' covenants, which overstated what had been read, and four diagnostic codes the codec declares that no document described. |
