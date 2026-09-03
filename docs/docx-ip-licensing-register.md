# DOCX IP, Licensing, And Standards Register

**Register version:** 2.0
**Updated:** 2026-09-03 (every row decided; no legal review is claimed)
**Owner:** Broiler.Documents maintainers
**Approval authority:** Maik Ratzmer (GitHub [MaiRat](https://github.com/MaiRat)), project reviewer
**Governance:** [ADR 0013](adr/0013-standards-ip-provenance-and-claims-beyond-pdf.md)

---

## ⚠ NO LAWYER HAS REVIEWED ANY OF THIS, AND NO ROW HERE IS DECIDED YET

The standard of review is an engineer reading published evidence. No legal
opinion was sought, none was given, and none is represented. **No
freedom-to-operate determination has been made** and none is attempted:
patent-freedom is not claimed for OOXML anywhere in this register.

The evidence was gathered by Claude (Anthropic's coding agent, engineering seat)
on 2026-09-03 at the maintainer's direction. **Assembling an evidence record is
not approving it.**

**Every row is now decided**, the patent rows included, all by the project
reviewer on 2026-09-03 on the evidence recorded here.

`Approved` on a row below means "this project judged the recorded evidence
sufficient to proceed" and nothing more. It is not a clearance, not an opinion,
and not a warranty. **Patent-freedom is not claimed for OOXML** and **no
freedom-to-operate determination has been made** — nobody searched, and a search
is what such a determination would take. The promise these rows rest on says the
same thing about itself in its own words, quoted on DOCX-IP-001.

Nothing about the decisions widens what may be said. DOCX-IP-006's negative rule
is unchanged and still enforced by a guard.

## Why this format's register reads differently from ODT's

ODT's rests on a standards body's own licensing mode: the committee obliges its
participants, and the covenant that exists on top of it is voluntary. DOCX rests
on **a single vendor's public promise**, which is the same shape as PDF's IP-001
and a different shape from ODT's. That matters for what the row can conclude: a
promise from one party reaches that party's claims and no further, and this one
says so itself in terms this register does not have to infer.

## Decision fields

Implementation jurisdictions and expiry/review dates are unrecorded on every row,
as they are on every row of the PDF and ODT registers. Under this register's
standard they are not blocking.

| ID | Technology / exact scope | Primary evidence | Current assessment | Status / required action |
|---|---|---|---|---|
| DOCX-IP-001 | The WordprocessingML subset enumerated in [the DOCX conformance document](docx-conformance.md), read and written over Open XML packages. Spreadsheet and presentation parts are outside the row and outside the codec. | [Microsoft Open Specification Promise](https://learn.microsoft.com/en-us/openspecs/dev_center/ms-devcentlp/1c24c7c8-28b0-4ce1-a47d-95fe1ff504bc) (MS-DEVCENTLP), published 12 September 2006, revised 24 February 2023; ECMA-376; ISO/IEC 29500 | The OSP's covered list names **`Office Open XML 1.0 - Ecma-376`**, **`Office Open XML ISO/IEC 29500:2008`, `:2012` and `:2016`**, and **`[MS-DOCX]: Word Extensions to the Office Open XML File Format`** individually. Microsoft "irrevocably promises not to assert any Microsoft Necessary Claims against you for making, using, selling, offering for sale, importing or distributing any implementation to the extent it conforms to a Covered Specification". No royalty is identified. **Three limits the promise states about itself, none of which this register may paper over.** It reaches *Microsoft-owned or Microsoft-controlled* claims only. It reaches those necessary to implement **the required portions described in detail and not merely referenced** — which matters for a format that references a great deal. And it is explicitly "not an assurance ... that a Covered Implementation would not infringe patents or other intellectual property rights of any third party". Risk assessed **low** on the recorded evidence, and third-party claims are neither cleared nor searched. | **Approved 2026-09-03** by the project reviewer, on the evidence recorded on this row and no other basis. The reviewer accepts the "required portions described in detail and not merely referenced" limit as recorded: this codec implements a documented subset of WordprocessingML, the conformance document enumerates it, and a promise scoped to required portions described in detail is the promise this implementation actually stands on. Risk assessed **low**. What the approval does not reach and this row said first: Microsoft-owned claims only, and an express non-assurance about third parties. Still unrecorded: implementation jurisdictions and the expiry/review date, neither blocking under this register's standard. |
| DOCX-IP-002 | The OSP's defensive-termination condition | The same promise text | "If you file, maintain or voluntarily participate in a patent infringement lawsuit against a Microsoft implementation of such Covered Specification, then this personal promise does not apply". That is **defensive** termination: it asks nothing of an implementer that does not sue Microsoft over the format, and it imposes no licence condition, no fee, and no reciprocal grant. It is the same shape as the defensive carve-out ODT-IP-002 records for Sun's covenant, and it is recorded as its own row for the same reason — a condition a reader might mistake for a restriction on ordinary use deserves to be read rather than summarized. | **Approved 2026-09-03.** Defensive termination is accepted as recorded: it asks nothing of an implementer that does not sue Microsoft over the format, and no action is implied for this project, which sues nobody. |
| DOCX-IP-003 | Acquiring and using the specification text: whether this repository may hold, quote, or reproduce ECMA-376 or ISO/IEC 29500 material | ECMA-376, freely published by Ecma International; ISO/IEC 29500, a priced ISO/IEC publication | **The permission is not relied on, because it is not needed.** Inspection finds no specification text, table, figure, or excerpt anywhere in the repository. Clauses are cited by number — the conformance document cites ECMA-376 §17.7.2 for style resolution and §17.4.81 for row heights, and cites rather than quotes. That is the position SRC-001 established for ISO 32000 and it holds identically here, which is what lets this row close without anyone buying a copy of 29500. Risk assessed **very low**. | **Approved 2026-09-03** on the inspection recorded here, which anyone can repeat. Reproducing specification material — a table of normative constants is the realistic case — would reopen this row and require the acquisition terms of whichever edition was used. |
| DOCX-IP-004 | Implementation provenance: whether the codec embeds third-party OOXML code, data, or documents | Inspection of `src/Broiler.Documents.Docx` and the whole tracked tree; `FormatClaimGuardTests` | Four findings, each checkable by repeating the inspection. The codec's directory contains **nothing but `.cs` files and its `.csproj`** — no data file, table, or fixture. The project takes **no package reference at all**; its only references are two sibling projects in this component, so no OOXML toolkit is present to account for. **No `.docx` or `.dotx` file is tracked anywhere** in this repository, so no document is vendored or redistributed. The test packages are constructed in code. Risk assessed **very low**, and the first three are enforced by guard tests that fail the build. | **Approved 2026-09-03** on the inspection recorded here. Three of its four findings are guard tests, so the row fails the build rather than going quietly stale. |
| DOCX-IP-005 | The platform facilities the codec is built on: ZIP container handling and XML parsing | `System.IO.Compression` and `System.Xml.Linq`; the platform's own licence and notices | The codec adds no compression implementation, no XML parser, and no table or test vector of its own; it calls the runtime. A platform dependency rather than a bundled component, carrying no OOXML-specific obligation — the same conclusion IP-023 reached for `FlateDecode` calling `ZLibStream`. Risk assessed **very low**. | **Approved 2026-09-03 under DOCX-IP-004.** A platform dependency carries no OOXML-specific obligation, so nothing is carried forward. |
| DOCX-IP-006 | "DOCX", "Word", "Open XML", "OOXML" naming, and any conformance, certification, or compatibility claim | The label set in [Approved labels](#approved-labels); ADR 0013's claims rule | Descriptive use of the format's name is what the labels are for. The negative rule is the same one IP-018 and ODT-IP-007 enforce, with **one addition this format needs and the others do not: no label may name Microsoft or Word.** "Word Document" is the phrase every file dialog on earth uses for `.docx` and it is a vendor's product name; using it as a format label implies a compatibility relationship with a product this project has never tested against. The format is `DOCX`. Nothing describes this implementation as OOXML-conformant, ECMA-376-conformant, ISO/IEC 29500-conformant, certified, endorsed, patent-free, or royalty-free. | **Approved 2026-09-03** for the label set below and no other wording, and the discrepancy it named is resolved the way the rule requires rather than the way the shipped string did. The Writer's save dialog read `Word Document (*.docx)`; the reviewer approved the rule, so the label changes to `DOCX Document (*.docx)`. **The guard is armed now that the decision exists**, and it lives in the aggregate repository rather than here: `WriterDocumentFormatLabelTests` reads the composed format set and fails the build if a label names a vendor or a vendor's product. It is there because the labels are, and because a guard placed here would not have run — this component's aggregate detector requires a `src/Broiler.Cli` directory the Writer does not have, so every aggregate-scoped guard it owns is permanently skipped. That is a separate defect, recorded in the review record below rather than fixed under a naming decision. Anything outside the table below remains a new decision. |
| DOCX-IP-007 | Office document encryption and password protection | ECMA-376 does not define it; `[MS-OFFCRYPTO]` does | Out of scope and refused rather than partially handled. No cryptographic, export-control, or key-handling review has been done, and none is needed while the answer is refusal. | **Blocked for V1**, as IP-015 blocks the PDF standard security handler and ODT-IP-008 blocks ODF's. |
| DOCX-IP-008 | Third-party or user-supplied DOCX documents used as fixtures or committed to the repository | Per-artifact origin, author, licence, and approval | Possession or public download is not permission to commit or redistribute. None is committed — verified under DOCX-IP-004 and guarded. User-supplied documents a caller opens at run time remain subject to their own rights, and neither the API nor the documentation may imply this component grants any. | **Rejected by default**, per artifact, as IP-020 and ODT-IP-009 handle their cases. |
| DOCX-SRC-001 | ECMA-376 and ISO/IEC 29500 as sources consulted while writing this codec | `src/Broiler.Documents.Docx`, in full; DOCX-IP-003 | Every line was written against the published specification for this repository. The two halves hold separately: nothing was copied, per DOCX-IP-003's finding that no specification text is present; and nothing third-party was consulted for content, there being no OOXML implementation in the tree to have consulted. Structural correspondence to the standard is expected and is not evidence of copying. | **Approved 2026-09-03** on the inspection recorded here. Closed for the freely published ECMA-376 edition; consulting a priced ISO/IEC edition would reopen it. |

## Approved labels

**Approved 2026-09-03 under DOCX-IP-006.**

| Context | Approved label |
|---|---|
| Format list | **DOCX** |
| Save As | **DOCX Document (*.docx)** |
| Import / Export | **DOCX** |
| Tooltip / Help | **Office Open XML text document (DOCX)** |
| Technical documentation | **DOCX (ECMA-376 / ISO/IEC 29500 WordprocessingML)** |

Deliberately absent: any label containing **Word**, **Microsoft**, or **compatible**, and any "OOXML compliant" or "ISO 29500 conformant" form. The first group names a vendor and its product; the second claims a conformance the specification defines and this codec does not meet.

## What still blocks a claim

| Blocker | Kind | State |
|---|---|---|
| DOCX-IP-001, DOCX-IP-002 | The OSP and its defensive condition | **Approved 2026-09-03.** The "required portions described in detail" limit accepted as recorded; defensive termination accepted as recorded |
| DOCX-IP-003 to DOCX-IP-005, DOCX-SRC-001 | What this repository contains and consulted | **Approved 2026-09-03.** Repeatable by anyone; three guarded mechanically so the approval cannot rot quietly |
| DOCX-IP-006 label set | Positive wording | **Approved 2026-09-03**, and the shipped save label changed to match rather than the rule bending to it. Armed as a guard over the heads |
| DOCX-IP-007 | Encrypted packages | **Blocked for V1** by scope, not by evidence |

Two rows are not approvals and must not be read as any: DOCX-IP-007 is blocked for V1 by scope, and DOCX-IP-008 rejects third-party documents by default per artifact. Both stay exactly as they were.

Shipping the codec is not a claim about rights, and nothing here blocks it. Saying things about it is, and that is what these rows govern.

## Review record

| Review | Reviewer | Date | Scope | Result |
|---|---|---|---|---|
| Naming and claims review (DOCX-IP-006) | Project reviewer (Maik Ratzmer) | 2026-09-03 | Format naming across format lists, dialogs, help text, and documentation | **Approved for the recorded label set**, and it is the first decision in this platform that changed something a user sees. The rule is that no format label names a vendor or a vendor's product. `Word Document` had shipped since Broiler-Platform/Broiler.Writer#65 — not by anyone's decision, but because it is the phrase every file dialog uses and nobody had asked whether this project may use it. It may not: the format is DOCX, "Word" is a product this project has never tested against, and a label implying otherwise implies a compatibility relationship that does not exist. The label becomes `DOCX Document (*.docx)`. **A defect found while arming the guard, and left for its own change:** this component's `PdfGuardRoots.FindAggregate` recognises the aggregate only when both `src/Broiler.Writer.Windows` and `src/Broiler.Cli` exist. The Writer has the first and not the second — the CLI lives in this repository as `Broiler.Documents.Cli` — so the detector never fires and **all four aggregate-scoped PDF delivery guards have been silently skipping**, including the one asserting that no head registers the PDF codec for saving. They are not wrong; they have never run. Fixing it may make dead assertions fail and is not something to do under a naming decision. **What this decision does not touch:** the patent rows. Approving what the format may be called says nothing about DOCX-IP-001 or DOCX-IP-002, which remain pending, and nothing here is cleared. |
| Patent and provenance review (DOCX-IP-001 to DOCX-IP-005, DOCX-SRC-001) | Project reviewer (Maik Ratzmer), on the evidence records assembled 2026-09-03 | 2026-09-03 | The Microsoft Open Specification Promise as published; the ECMA-376 and ISO/IEC 29500 publication position; this repository's DOCX source and tracked files | **Approved.** The promise names this format's specifications individually rather than by family, which is what let the row be written as ready and the decision be cheap. The one judgement it asked for is taken: the "required portions described in detail and not merely referenced" limit is accepted as recorded, because a codec implementing a documented subset is exactly the implementation such a promise is scoped to. The provenance rows were settled by inspection before anyone decided anything and three are guarded, so what was approved was a record that fails the build if it stops being true. **Not decided by any of this:** third-party claims, which the promise expressly does not assure and nobody searched for. Jurisdictions and expiry dates were not part of the record. |
| OOXML evidence assembly and register creation | Claude (Anthropic coding agent, engineering seat), at the maintainer's direction — **not legal counsel, and not the approval authority** | 2026-09-03 | The Microsoft Open Specification Promise as published; the ECMA-376 and ISO/IEC 29500 publication position; inspection of this repository's DOCX source, tests, and tracked files | **Evidence recorded; no row decided.** The promise text was read at its source rather than summarized, and the covered entries confirmed to name Ecma-376, all three ISO/IEC 29500 editions, and MS-DOCX individually rather than by family. Four findings settled by inspection and three made mechanical. Two things the record could not do: decide any row, which is the approval authority's; and resolve the `Word Document` label the Writer already ships, which is a naming decision rather than an evidence question and is written up as one. |
