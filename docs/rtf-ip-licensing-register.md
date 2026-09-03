# RTF IP, Licensing, And Standards Register

**Register version:** 2.0
**Updated:** 2026-09-03 (every row decided; no legal review is claimed)
**Owner:** Broiler.Documents maintainers
**Approval authority:** Maik Ratzmer (GitHub [MaiRat](https://github.com/MaiRat)), project reviewer
**Governance:** [ADR 0013](adr/0013-standards-ip-provenance-and-claims-beyond-pdf.md)

---

## ⚠ NO LAWYER HAS REVIEWED ANY OF THIS, AND NO ROW HERE IS DECIDED YET

The standard of review is an engineer reading published evidence. **No
freedom-to-operate determination has been made** and patent-freedom is not
claimed. The evidence was gathered by Claude (Anthropic's coding agent,
engineering seat) on 2026-09-03 at the maintainer's direction; assembling a
record is not approving it, and only the approval authority above can.

**Every row is now decided**, by the project reviewer on 2026-09-03 on the
evidence recorded here. `Approved` means this project judged that evidence
sufficient to proceed and nothing more — not a clearance, not an opinion, not a
warranty — and the promise these rows rest on says the same about itself in its
own words. The negative claims rule is unchanged by any of it.

## The shape of this one

RTF is a **vendor format with no standards body at all**. There is no committee,
no IPR mode, and no participants to be obliged — Microsoft published the
specification, last revised it in 2008, and stopped. So the whole rights question
reduces to one instrument and one inspection, which makes this the shortest
register here and not the weakest: a promise that names the format explicitly is
a better artifact than a mode that has to be reasoned about.

## Decision fields

Implementation jurisdictions and expiry/review dates are unrecorded on every row
and are not blocking under this register's standard.

| ID | Technology / exact scope | Primary evidence | Current assessment | Status / required action |
|---|---|---|---|---|
| RTF-IP-001 | The RTF subset [the conformance document](rtf-conformance.md) enumerates and ADR 0005 fixes, read and written as control words over 7-bit ASCII with the escape forms the reader accepts | [Microsoft Open Specification Promise](https://learn.microsoft.com/en-us/openspecs/dev_center/ms-devcentlp/1c24c7c8-28b0-4ce1-a47d-95fe1ff504bc) (MS-DEVCENTLP), published 12 September 2006, revised 24 February 2023; the Microsoft RTF specification | The OSP's covered list names **`[RTF]: Rich Text Format`** individually, under Office File Formats. Microsoft "irrevocably promises not to assert any Microsoft Necessary Claims against you for making, using, selling, offering for sale, importing or distributing any implementation to the extent it conforms to a Covered Specification". No royalty is identified. The same three self-stated limits apply as for DOCX and are not paraphrased away: the promise reaches **Microsoft-owned or Microsoft-controlled** claims only, reaches those necessary for **the required portions described in detail and not merely referenced**, and is expressly "not an assurance ... that a Covered Implementation would not infringe patents or other intellectual property rights of any third party". A second, independent consideration this row may record because the dates are published: the format dates from 1987 and its specification was last revised in 2008, so any patent contemporary with its design would be long expired — the position rests on a live promise and on age, and neither depends on the other. Risk assessed **very low**. | **Approved 2026-09-03** by the project reviewer, on the evidence recorded on this row. The format is named in the promise rather than reasoned into it, and the position rests on a live promise and on age independently — neither depending on the other. Risk assessed **very low**. What the approval does not reach, as this row said first: Microsoft-owned claims only, and an express non-assurance about third parties. |
| RTF-IP-002 | The OSP's defensive-termination condition | The same promise text | Defensive only: the promise lapses for a party that sues Microsoft over a Covered Specification's implementation. It asks nothing of an implementer that does not, imposes no fee and no reciprocal grant. Recorded as its own row on the same reasoning as DOCX-IP-002 and ODT-IP-002 — a condition a reader might mistake for a restriction on ordinary use is worth reading rather than summarizing. | **Approved 2026-09-03.** Defensive termination accepted as recorded; no action implied for this project. |
| RTF-IP-003 | Acquiring and using the specification text | The Microsoft RTF specification as published | **Not relied on, because it is not needed.** Inspection finds no specification text, control-word table, or excerpt in the repository. The control words this codec understands are written as C# `switch` arms and string comparisons against the identifiers the format defines, which are the format's own vocabulary rather than a transcription of a document. That distinction is the one SRC-017 turns on for T.4's code tables, and it falls the other way here: a control word is a name, not a normative constant nobody could re-derive. Risk assessed **very low**. | **Approved 2026-09-03** on the inspection recorded here, and with it the judgement the row turns on: a control word is a name the format defines, not a normative constant nobody could re-derive. That is why this codec has no SRC-017-shaped question where the fax decoder does. |
| RTF-IP-004 | Implementation provenance: whether the codec embeds third-party RTF code or data | Inspection of `src/Broiler.Documents.Rtf` and the whole tracked tree; `FormatClaimGuardTests` | The codec's directory contains **nothing but `.cs` files and its `.csproj`**. The project takes **no package reference at all**; its only references are two sibling projects in this component. **No `.rtf` file is tracked anywhere** in this repository. Test documents are constructed as strings in code. Risk assessed **very low**, and the first three are enforced by guard tests. | **Approved 2026-09-03** on the inspection recorded here; three of its findings are guard tests. |
| RTF-IP-005 | "RTF", "Rich Text Format", and any conformance or compatibility claim | The label set in [Approved labels](#approved-labels); ADR 0013's claims rule | "Rich Text Format" is the format's own name rather than a vendor's product name, which is what separates this row from DOCX-IP-006: there is no "Word" problem here, because the format is not named after a product. The negative rule is unchanged — nothing describes this implementation as RTF-conformant, certified, endorsed, patent-free, or royalty-free, and nothing may claim compatibility with any application that reads RTF. | **Approved 2026-09-03** for the label set below and no other wording. The Writer already ships `Rich Text Format (*.rtf)`, which the table covers, so unlike DOCX this approval changes nothing a user sees — the format is not named after a product and never was. |
| RTF-IP-006 | Embedded objects and pictures inside RTF | [The conformance document](rtf-conformance.md)'s embedded-object limits | RTF can carry OLE objects and metafile pictures. This codec decodes none of them: an embedded object is reported and skipped, and `document.capability.not-composed` is raised where a caller asked for behaviour that does not exist. Nothing is decoded, so nothing about the formats inside an RTF file arises here. | **Out of scope by construction**, and reopens only if embedded-object decoding is implemented. |
| RTF-IP-007 | Third-party or user-supplied RTF documents as fixtures | Per-artifact origin, author, licence, and approval | Possession is not permission to redistribute. None is committed — verified under RTF-IP-004 and guarded. Documents a caller opens at run time remain subject to their own rights. | **Rejected by default**, per artifact. |
| RTF-SRC-001 | The RTF specification as a source consulted while writing this codec | `src/Broiler.Documents.Rtf`, in full; RTF-IP-003 | Written against the published specification for this repository. Nothing was copied, per RTF-IP-003; nothing third-party was consulted for content, there being no RTF implementation in the tree. | **Approved 2026-09-03** on the inspection recorded here. |

## Approved labels

**Approved 2026-09-03 under RTF-IP-005.**

| Context | Approved label |
|---|---|
| Format list | **RTF** |
| Save As | **Rich Text Format (*.rtf)** |
| Import / Export | **Rich Text Format** |
| Tooltip / Help | **Rich Text Format (RTF)** |

Deliberately absent: any label naming **Microsoft**, **Word**, or **WordPad**, and any "RTF compliant" form.

## What still blocks a claim

| Blocker | Kind | State |
|---|---|---|
| RTF-IP-001, RTF-IP-002 | The OSP and its defensive condition | **Approved 2026-09-03.** The format is named in the promise; the position rests on it and on age independently |
| RTF-IP-003, RTF-IP-004, RTF-SRC-001 | What this repository contains and consulted | **Approved 2026-09-03.** Three guarded mechanically |
| RTF-IP-005 label set | Positive wording | **Approved 2026-09-03.** The shipped label already matched, so nothing changed |

## Review record

| Review | Reviewer | Date | Scope | Result |
|---|---|---|---|---|
| Patent and provenance review (all rows) | Project reviewer (Maik Ratzmer), on the evidence record assembled 2026-09-03 | 2026-09-03 | The Microsoft Open Specification Promise as published; this repository's RTF source and tracked files | **Approved.** The cheapest decision in this platform, and cheap for a good reason: `[RTF]: Rich Text Format` appears by name in the promise's covered list, so nothing had to be argued from a family, a mode, or an analogy. The judgement the reviewer did take is RTF-IP-003's — that a control word is the format's own vocabulary rather than a transcribed normative constant — which is what distinguishes this codec from the fax decoder SRC-017 still gates. Two rows are untouched and are not approvals: RTF-IP-006 is out of scope by construction and RTF-IP-007 rejects third-party documents by default. |
| RTF evidence assembly and register creation | Claude (Anthropic coding agent, engineering seat), at the maintainer's direction — **not legal counsel, and not the approval authority** | 2026-09-03 | The Microsoft Open Specification Promise as published; inspection of this repository's RTF source, tests, and tracked files | **Evidence recorded; no row decided.** The finding that mattered was cheap and specific: `[RTF]: Rich Text Format` appears by name in the OSP's covered list, so this format needed no argument from analogy to a family or a mode. The provenance rows are the same four-part inspection every format in this component gets. The one judgement worth flagging for the reviewer is RTF-IP-003's: control words are read as the format's vocabulary rather than as transcribed normative constants, which is why this codec has no SRC-017-shaped question and the fax decoder does. |
