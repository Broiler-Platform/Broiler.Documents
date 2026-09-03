# RTF IP, Licensing, And Standards Register

**Register version:** 1.0
**Updated:** 2026-09-03 (evidence assembled; no row is decided and no legal review is claimed)
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
| RTF-IP-001 | The RTF subset [the conformance document](rtf-conformance.md) enumerates and ADR 0005 fixes, read and written as control words over 7-bit ASCII with the escape forms the reader accepts | [Microsoft Open Specification Promise](https://learn.microsoft.com/en-us/openspecs/dev_center/ms-devcentlp/1c24c7c8-28b0-4ce1-a47d-95fe1ff504bc) (MS-DEVCENTLP), published 12 September 2006, revised 24 February 2023; the Microsoft RTF specification | The OSP's covered list names **`[RTF]: Rich Text Format`** individually, under Office File Formats. Microsoft "irrevocably promises not to assert any Microsoft Necessary Claims against you for making, using, selling, offering for sale, importing or distributing any implementation to the extent it conforms to a Covered Specification". No royalty is identified. The same three self-stated limits apply as for DOCX and are not paraphrased away: the promise reaches **Microsoft-owned or Microsoft-controlled** claims only, reaches those necessary for **the required portions described in detail and not merely referenced**, and is expressly "not an assurance ... that a Covered Implementation would not infringe patents or other intellectual property rights of any third party". A second, independent consideration this row may record because the dates are published: the format dates from 1987 and its specification was last revised in 2008, so any patent contemporary with its design would be long expired — the position rests on a live promise and on age, and neither depends on the other. Risk assessed **very low**. | **Pending the project reviewer's decision.** Evidence complete: the format is named in the promise rather than reasoned into it. Nothing further was identified as needed. |
| RTF-IP-002 | The OSP's defensive-termination condition | The same promise text | Defensive only: the promise lapses for a party that sues Microsoft over a Covered Specification's implementation. It asks nothing of an implementer that does not, imposes no fee and no reciprocal grant. Recorded as its own row on the same reasoning as DOCX-IP-002 and ODT-IP-002 — a condition a reader might mistake for a restriction on ordinary use is worth reading rather than summarizing. | **Pending.** No action implied for this project. |
| RTF-IP-003 | Acquiring and using the specification text | The Microsoft RTF specification as published | **Not relied on, because it is not needed.** Inspection finds no specification text, control-word table, or excerpt in the repository. The control words this codec understands are written as C# `switch` arms and string comparisons against the identifiers the format defines, which are the format's own vocabulary rather than a transcription of a document. That distinction is the one SRC-017 turns on for T.4's code tables, and it falls the other way here: a control word is a name, not a normative constant nobody could re-derive. Risk assessed **very low**. | **Inspection finding recorded 2026-09-03; awaiting sign-off.** |
| RTF-IP-004 | Implementation provenance: whether the codec embeds third-party RTF code or data | Inspection of `src/Broiler.Documents.Rtf` and the whole tracked tree; `FormatClaimGuardTests` | The codec's directory contains **nothing but `.cs` files and its `.csproj`**. The project takes **no package reference at all**; its only references are two sibling projects in this component. **No `.rtf` file is tracked anywhere** in this repository. Test documents are constructed as strings in code. Risk assessed **very low**, and the first three are enforced by guard tests. | **Inspection finding recorded 2026-09-03; awaiting sign-off.** |
| RTF-IP-005 | "RTF", "Rich Text Format", and any conformance or compatibility claim | The label set in [Approved labels](#approved-labels); ADR 0013's claims rule | "Rich Text Format" is the format's own name rather than a vendor's product name, which is what separates this row from DOCX-IP-006: there is no "Word" problem here, because the format is not named after a product. The negative rule is unchanged — nothing describes this implementation as RTF-conformant, certified, endorsed, patent-free, or royalty-free, and nothing may claim compatibility with any application that reads RTF. | **Pending the reviewer's approval of the label set.** The Writer ships `Rich Text Format (*.rtf)` (Broiler-Platform/Broiler.Writer#65), which the table below covers as proposed — unlike DOCX, this format has no live discrepancy to resolve. |
| RTF-IP-006 | Embedded objects and pictures inside RTF | [The conformance document](rtf-conformance.md)'s embedded-object limits | RTF can carry OLE objects and metafile pictures. This codec decodes none of them: an embedded object is reported and skipped, and `document.capability.not-composed` is raised where a caller asked for behaviour that does not exist. Nothing is decoded, so nothing about the formats inside an RTF file arises here. | **Out of scope by construction**, and reopens only if embedded-object decoding is implemented. |
| RTF-IP-007 | Third-party or user-supplied RTF documents as fixtures | Per-artifact origin, author, licence, and approval | Possession is not permission to redistribute. None is committed — verified under RTF-IP-004 and guarded. Documents a caller opens at run time remain subject to their own rights. | **Rejected by default**, per artifact. |
| RTF-SRC-001 | The RTF specification as a source consulted while writing this codec | `src/Broiler.Documents.Rtf`, in full; RTF-IP-003 | Written against the published specification for this repository. Nothing was copied, per RTF-IP-003; nothing third-party was consulted for content, there being no RTF implementation in the tree. | **Inspection finding recorded 2026-09-03; awaiting sign-off.** |

## Approved labels

**Proposed 2026-09-03 under RTF-IP-005. Not yet approved.**

| Context | Proposed label |
|---|---|
| Format list | **RTF** |
| Save As | **Rich Text Format (*.rtf)** |
| Import / Export | **Rich Text Format** |
| Tooltip / Help | **Rich Text Format (RTF)** |

Deliberately absent: any label naming **Microsoft**, **Word**, or **WordPad**, and any "RTF compliant" form.

## What still blocks a claim

| Blocker | Kind | State |
|---|---|---|
| RTF-IP-001, RTF-IP-002 | The OSP and its defensive condition | **Pending a decision only.** The format is named in the promise; evidence complete |
| RTF-IP-003, RTF-IP-004, RTF-SRC-001 | What this repository contains and consulted | **Findings recorded, awaiting sign-off.** Three guarded mechanically |
| RTF-IP-005 label set | Positive wording | **Pending approval.** The shipped label matches the proposal, so nothing changes on approval |

## Review record

| Review | Reviewer | Date | Scope | Result |
|---|---|---|---|---|
| RTF evidence assembly and register creation | Claude (Anthropic coding agent, engineering seat), at the maintainer's direction — **not legal counsel, and not the approval authority** | 2026-09-03 | The Microsoft Open Specification Promise as published; inspection of this repository's RTF source, tests, and tracked files | **Evidence recorded; no row decided.** The finding that mattered was cheap and specific: `[RTF]: Rich Text Format` appears by name in the OSP's covered list, so this format needed no argument from analogy to a family or a mode. The provenance rows are the same four-part inspection every format in this component gets. The one judgement worth flagging for the reviewer is RTF-IP-003's: control words are read as the format's vocabulary rather than as transcribed normative constants, which is why this codec has no SRC-017-shaped question and the fax decoder does. |
