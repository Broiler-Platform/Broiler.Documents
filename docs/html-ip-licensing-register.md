# HTML IP, Licensing, And Standards Register

**Register version:** 2.0
**Updated:** 2026-09-03 (every row decided; HTML-IP-004 closed by Broiler.DOM gaining a register of its own)
**Owner:** Broiler.Documents maintainers
**Approval authority:** Maik Ratzmer (GitHub [MaiRat](https://github.com/MaiRat)), project reviewer
**Governance:** [ADR 0013](adr/0013-standards-ip-provenance-and-claims-beyond-pdf.md)

---

## ⚠ NO LAWYER HAS REVIEWED ANY OF THIS, AND NO ROW HERE IS DECIDED YET

The standard of review is an engineer reading published evidence. **No
freedom-to-operate determination has been made** and patent-freedom is not
claimed for HTML. The evidence was gathered by Claude (Anthropic's coding agent,
engineering seat) on 2026-09-03 at the maintainer's direction; assembling a
record is not approving it.

## The one thing to read before the rows

**Half of what this register covers is not in this component.**
`Broiler.Documents.Html` does not tokenize or parse HTML: it hands bytes to
`Broiler.Dom.Html.HtmlDocumentParser` and maps the resulting tree onto the
document model.

When this register was written, `Broiler.DOM` had no rights record and HTML-IP-004
existed to say so. **It has one now** - written for exactly the gap this row
named. The scope limit itself has not moved: this register covers the mapping
layer, that one covers the parser, and a claim about Broiler's HTML support needs
both. What changed is that both now exist.

**Every row here is decided**, by the project reviewer on 2026-09-03. `Approved`
means this project judged the recorded evidence sufficient to proceed and nothing
more. **Patent-freedom is not claimed and no freedom-to-operate determination has
been made.**

## Decision fields

| ID | Technology / exact scope | Primary evidence | Current assessment | Status / required action |
|---|---|---|---|---|
| HTML-IP-001 | The HTML subset [the conformance document](html-conformance.md) enumerates: the elements and attributes this codec maps to and from the document model. **Tokenization and parsing are outside the row** — see the note above. | The [W3C Patent Policy](https://www.w3.org/policies/patent-policy/); the [WHATWG IPR Policy](https://whatwg.org/ipr-policy); the HTML Living Standard | Both bodies that have stewarded HTML operate **royalty-free** patent policies, and this is the strongest starting position of any format in this component — stronger than ODT's, because two independent bodies reach the same result, and far stronger than a vendor promise, because no single party's goodwill is load-bearing. W3C requires participants to license Essential Claims "available to all, worldwide" and "may not be conditioned on payment of royalties, fees or other consideration". WHATWG requires a licence that likewise "may not be conditioned on payment of royalties, fees, or other consideration". **What neither establishes.** Both bind *participants* over *Essential Claims* and both permit a participant to **exclude specific patents** during a disclosure window — so the policies are a commitment mechanism, not a finding that no relevant claim exists, and they say nothing about non-participants. Reciprocity and defensive suspension are permitted by both. No implementation royalty is identified from any source. Risk assessed **very low**. | **Approved 2026-09-03** by the project reviewer, on the evidence recorded on this row. Two independent bodies reaching the same royalty-free result is the strongest starting position of any format in this component, and the decision records why it is still not a patent-freedom statement: both policies bind participants over Essential Claims, both permit a participant to exclude specific patents during a disclosure window, and neither reaches anyone who never participated. Risk assessed **very low**. |
| HTML-IP-002 | Acquiring and using the specification text | The HTML Living Standard and the W3C HTML publications, both freely published | **Not relied on, because it is not needed.** Inspection finds no specification text, table, or excerpt in the repository. Element and attribute names are the format's own vocabulary — the same distinction RTF-IP-003 draws for control words — and no entity table, no named-character-reference list, and no parser state table appears in this codec. Anything of that kind that exists at all lives in `Broiler.DOM` and is that repository's question, not this one's. Risk assessed **very low**. | **Approved 2026-09-03** on the inspection recorded here. |
| HTML-IP-003 | Implementation provenance within this component | Inspection of `src/Broiler.Documents.Html` and the whole tracked tree; `FormatClaimGuardTests` | The codec's directory contains **nothing but `.cs` files and its `.csproj`**. It takes **no package reference at all**. **No `.html` or `.htm` file is tracked anywhere** in this repository. Test documents are constructed as strings in code. Risk assessed **very low**, and the first three are enforced by guard tests. | **Approved 2026-09-03** on the inspection recorded here; three of its findings are guard tests. |
| HTML-IP-004 | The `Broiler.DOM` dependency: `Broiler.Dom` and `Broiler.Dom.Html` | The project file's own reference list | **This is the row that is genuinely open, and it is open because of where the code lives rather than what it is.** Unlike every other format in this component, the HTML codec depends on a sibling *component* for the part of the work with the most implementation surface — a tokenizer and a tree builder are where a specification's normative tables and state machines would live if any were transcribed. That component is this project's own code, not a third party's, so nothing is imported and no licence is consumed. What is missing is a register saying so with the same discipline this one applies here. | **Closed 2026-09-03, by the thing it asked for existing.** `Broiler.DOM` now has a rights record of its own, written under the same discipline and reaching the question this row could not: whether the largest normative artifact in HTML - the named character reference table - is transcribed there. It is not; that component delegates to the platform and guards the fact. **Not "approved":** there was never anything here to approve, only a gap to name, and the gap is filled. The scope limit it also stated survives unchanged - this register covers the mapping layer, that one covers the parser, and a claim about Broiler's HTML support as a whole needs both registers rather than either. |
| HTML-IP-005 | The shared URI policy applied to links | This project's own scheme allow-list | Consuming the platform's `System.Uri` and applying this project's own scheme policy, which is the position IP-014 recorded for PDF: there is no URI implementation here to trace, so an implementation-provenance question has nothing to answer. | **Approved 2026-09-03 under HTML-IP-003.** |
| HTML-IP-006 | "HTML" naming, and any conformance or compatibility claim | The label set in [Approved labels](#approved-labels) | HTML is a format name owned by nobody in the sense that matters here — there is no vendor to avoid naming, which is what distinguishes this row from DOCX-IP-006. The negative rule stands unchanged: **nothing describes this implementation as HTML5-conformant, standards-conformant, certified, endorsed, or as a browser**, and the last of those is the one that would mislead. This codec maps a subset of HTML to a text document model; it does not render HTML, and a label implying otherwise would promise something the component cannot do. | **Approved 2026-09-03** for the label set below and no other wording. The Writer already ships `HTML (*.html, *.htm)`, which the table covers, so this approval changes nothing a user sees. The rule against implying this component renders HTML or is a browser is the operative half and is unchanged. |
| HTML-IP-007 | Scripts, styles, and remote references inside HTML | [The conformance document](html-conformance.md); ADR 0004 | Nothing is executed and nothing is fetched. Script and style content is not run, and a remote reference is not resolved — reading one would be a network request driven by document content, which ADR 0004 forbids. There is therefore no question here about anything an HTML document might point at. | **Out of scope by construction.** |
| HTML-IP-008 | Third-party or user-supplied HTML used as fixtures | Per-artifact origin, author, licence, and approval | None is committed — verified under HTML-IP-003 and guarded. Documents a caller opens at run time remain subject to their own rights, which for HTML is worth saying plainly because the web is the obvious place to get one. | **Rejected by default**, per artifact. |
| HTML-SRC-001 | The HTML specifications as sources consulted while writing this codec | `src/Broiler.Documents.Html`, in full; HTML-IP-002 | The mapping layer was written against the published specifications for this repository. Nothing was copied, and no third-party HTML implementation was consulted for content — there is none in this component's tree. The parser this codec calls is `Broiler.DOM`'s and carries its own unanswered version of this row. | **Approved 2026-09-03** on the inspection recorded here, scoped to this component; the parser this codec calls is covered by the Broiler.DOM register's own equivalent row. |

## Approved labels

**Approved 2026-09-03 under HTML-IP-006.**

| Context | Approved label |
|---|---|
| Format list | **HTML** |
| Save As | **HTML (*.html, *.htm)** |
| Import / Export | **HTML** |
| Tooltip / Help | **HTML document** |

Deliberately absent: **HTML5**, which names a version this codec makes no conformance claim about, and anything of the form "web page" or "browser", which would imply rendering this component does not do.

## What still blocks a claim

| Blocker | Kind | State |
|---|---|---|
| HTML-IP-001 | The two royalty-free patent policies | **Approved 2026-09-03.** The exclusion mechanism is recorded as what stops it being a patent-freedom claim |
| HTML-IP-002 to HTML-IP-003, HTML-IP-005, HTML-SRC-001 | What this component contains and consulted | **Approved 2026-09-03**; three guarded mechanically |
| HTML-IP-004 | `Broiler.DOM`'s own register | **Closed 2026-09-03** by that register existing. The scope limit stands: a claim about Broiler's HTML support needs both registers, not either |
| HTML-IP-006 label set | Positive wording | **Approved 2026-09-03.** The shipped label already matched |

## Review record

| Review | Reviewer | Date | Scope | Result |
|---|---|---|---|---|
| Patent, provenance and scope review (all rows) | Project reviewer (Maik Ratzmer), on the evidence record assembled 2026-09-03 | 2026-09-03 | The W3C Patent Policy and WHATWG IPR Policy as published; this component's HTML source, tests and project references; the Broiler.DOM register | **Approved, and one row closed rather than approved.** The patent position is the strongest here and the decision records why it is still not patent-freedom: two royalty-free policies that bind participants, permit patent exclusion during a disclosure window, and reach no one who never participated. HTML-IP-004 is the interesting one. It was written to say that the parsing half of HTML had no rights record anywhere and that this register could not supply one; between its writing and this decision, `Broiler.DOM` got a register of its own, reaching the question this row could not - whether the named character reference table is transcribed there. It is not. The row closes because what it asked for exists, which is a different outcome from being approved, and the scope limit it also stated is unchanged: a claim about Broiler's HTML support needs both registers. |
| HTML evidence assembly and register creation | Claude (Anthropic coding agent, engineering seat), at the maintainer's direction — **not legal counsel, and not the approval authority** | 2026-09-03 | The W3C Patent Policy and the WHATWG IPR Policy as published; inspection of this component's HTML source, tests, tracked files, and project references | **Evidence recorded; no row decided.** Two royalty-free policies from two independent bodies make this the strongest patent position of any format here, and the exclusion windows both permit are what keep the row from overstating it. The finding worth the reviewer's attention is not about patents at all: this codec does not parse HTML, `Broiler.DOM` does, and that component has no register — so a rights record for "Broiler's HTML support" does not exist yet and this one does not pretend to be it. HTML-IP-004 exists to say so rather than to leave it implicit. |
