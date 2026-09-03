# Markdown IP, Licensing, And Standards Register

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
record is not approving it.

## The shape of this one

Markdown has **no standards body, no IPR mode, and no patent declaration** —
there is no committee to oblige anyone and no vendor to promise anything. That
makes the patent rows here shorter than any other format's, and it moves the
weight somewhere the other registers barely have to look: **a copyright licence
with a naming condition in it.**

The one real obligation in this register is a clause about what a product may be
called, not a question about what it may implement.

## Decision fields

| ID | Technology / exact scope | Primary evidence | Current assessment | Status / required action |
|---|---|---|---|---|
| MD-IP-001 | The CommonMark-oriented subset [the conformance document](markdown-conformance.md) enumerates. It is explicitly **not** a full CommonMark implementation: tables, reference links, HTML blocks and the rest are absent, and the document says so. | The [Markdown licence](https://daringfireball.net/projects/markdown/license), Copyright © 2004 John Gruber; the CommonMark specification | There is no patent instrument to read because there is no body or vendor that issued one, and no implementation royalty is identified from any source. A syntax in which emphasis is an asterisk and a heading is a hash is not the shape of thing patents are asserted over, and forty years of plain-text markup conventions precede it. **That is a risk assessment and not a finding of freedom:** the absence of a declaration is the absence of evidence either way, which is a weaker position in kind than ODT's mode or DOCX's promise even though it feels safer. This register says so rather than letting "nobody has ever claimed one" read as "nobody could". Risk assessed **very low**. | **Pending the project reviewer's decision.** There is little to decide beyond accepting that a format with no rights instrument is assessed on absence, and recording that this is a different kind of knowing from the other registers. |
| MD-IP-002 | The Markdown licence's naming condition | The licence text, quoted: "Neither the name 'Markdown' nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission." | **The one live obligation in this register, and it needs reading rather than obeying reflexively.** The clause is a BSD-3 term and it binds *products derived from that software* — Gruber's Perl implementation and the material distributed with it. Nothing here is derived from it: this codec was written for this repository, imports nothing, and MD-IP-003 records that no line of it came from anywhere else. On that reading the condition does not attach at all, and the format's name may be used descriptively as every editor on earth uses it. The reading is stated so a reviewer can disagree with it, because if it were wrong the consequence would be immediate — the format list says "Markdown". | **Pending, and it is the row to actually think about.** A decision should say whether the derived-from-software reading is accepted. If it is not, the label set below is wrong and the Writer's shipped `Markdown (*.md)` label would have to change. |
| MD-IP-003 | Implementation provenance | Inspection of `src/Broiler.Documents.Markdown` and the whole tracked tree; `FormatClaimGuardTests` | The codec's directory contains **nothing but `.cs` files and its `.csproj`**. It takes **no package reference at all**; its only references are two sibling projects in this component, so neither Gruber's implementation, nor CommonMark's reference implementation, nor any other Markdown library is present to account for. Test documents are constructed as strings in code. Risk assessed **very low**, and the first two findings are enforced by guard tests. **This row is what MD-IP-002's reading rests on**, which is why it is not merely routine here: "not derived from that software" is a claim about this tree, and the tree can be inspected. | **Inspection finding recorded 2026-09-03; awaiting sign-off.** |
| MD-IP-004 | The CommonMark specification as a source | The CommonMark specification, freely published | Consulted for the syntax this codec implements a subset of, and not reproduced: inspection finds no specification text, no test-suite case, and no excerpt in the repository. CommonMark publishes an extensive conformance test suite, and **none of it is committed here** — the tests are written for this codec rather than taken from it, which is the same position ODT-IP-009 and DOCX-IP-008 take about vendored fixtures and matters more here because the suite is so easy to copy. | **Inspection finding recorded 2026-09-03; awaiting sign-off.** Importing the CommonMark suite would reopen this row and require its licence to be read and recorded. |
| MD-IP-005 | "Markdown" and "CommonMark" naming, and any conformance claim | The label set in [Approved labels](#approved-labels); MD-IP-002 | Two distinct rules, and the second is the one this format gets wrong most easily. **Nothing describes this implementation as CommonMark-compliant**, because it is explicitly a subset and the conformance document lists what it omits — this is the only format in this component whose own documentation names a specification it deliberately does not meet, so the temptation to claim it is real. And nothing describes this implementation as endorsed by, or derived from, Markdown's author or contributors, which is MD-IP-002's clause read as a wording rule regardless of whether it legally binds. | **Pending the reviewer's approval of the label set.** |
| MD-IP-006 | Third-party or user-supplied Markdown used as fixtures | Per-artifact origin, author, licence, and approval | Possession is not permission to redistribute. None is committed — verified under MD-IP-003 and guarded, with the CommonMark suite specifically in view per MD-IP-004. | **Rejected by default**, per artifact. |
| MD-SRC-001 | Sources consulted while writing this codec | `src/Broiler.Documents.Markdown`, in full; MD-IP-003, MD-IP-004 | Written against the published syntax description and the CommonMark specification for this repository. Nothing was copied and no third-party Markdown implementation was consulted for content, there being none in the tree. | **Inspection finding recorded 2026-09-03; awaiting sign-off.** |

## Approved labels

**Proposed 2026-09-03 under MD-IP-005. Not yet approved, and conditional on MD-IP-002's reading.**

| Context | Proposed label |
|---|---|
| Format list | **Markdown** |
| Save As | **Markdown (*.md)** |
| Import / Export | **Markdown** |
| Tooltip / Help | **Markdown text document** |

Deliberately absent: **CommonMark** in any form, because this codec implements a subset its own conformance document enumerates the gaps in; and anything of the form "compatible with" or "as specified by", which would claim a conformance nobody has tested.

## What still blocks a claim

| Blocker | Kind | State |
|---|---|---|
| MD-IP-001 | The absence of any patent instrument | **Pending a decision only**, and the decision is to accept a position assessed on absence rather than on evidence |
| **MD-IP-002** | **The licence's naming condition** | **Pending, and the row that actually needs thought.** Everything else here is routine; this one has a consequence if the reading is rejected |
| MD-IP-003 to MD-IP-004, MD-SRC-001 | What this repository contains and consulted | **Findings recorded, awaiting sign-off.** Two guarded mechanically |
| MD-IP-005 label set | Positive wording | **Pending approval**, and downstream of MD-IP-002 |

## Review record

| Review | Reviewer | Date | Scope | Result |
|---|---|---|---|---|
| Markdown evidence assembly and register creation | Claude (Anthropic coding agent, engineering seat), at the maintainer's direction — **not legal counsel, and not the approval authority** | 2026-09-03 | The Markdown licence as published; the CommonMark specification's position; inspection of this repository's Markdown source, tests, and tracked files | **Evidence recorded; no row decided.** This was the shortest search and the one with the most interesting result. There is no patent instrument for Markdown at all, which is not the same as a clean one — a position assessed on absence is weaker in kind than ODT's mode or DOCX's promise, and the row says so rather than trading on how safe the format feels. The obligation that does exist is a naming clause in a BSD-3 licence, and whether it reaches this project turns on a question inspection can answer: nothing here derives from the software that licence covers. That reading is written out so the reviewer can reject it, because rejecting it changes what the format list is allowed to say. |
