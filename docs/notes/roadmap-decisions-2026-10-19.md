# Roadmap decisions — 2026-10-19

Recorded at the project reviewer's direction. Numbers refer to the twelve-question
roadmap summary reviewed in chat. These are scope and policy decisions; they do
not report implementation or satisfaction of release gates.

| Question | Decision |
|---|---|
| 2 — PDF font embedding | Approve the general functionality for both embedding and subsetting caller-supplied fonts, respecting the `OS/2.fsType` flags. Existing caller permission and generated-document obligation checks remain applicable. See the [IP-012 record](../ip/briefs/ip-012-font-embedding.md). |
| 7 — Format fidelity | Select RTF list-table detection and style-sheet interpretation for implementation. Defer the HTML list, table/CSS and page-header scope choices. |
| 8 — Embedded images | Support embedded images in HTML and Markdown, including `data:` payloads, but ask the user before displaying them. Hosts must enforce consent before display; resource limits still apply. Relative-link policy is deferred. |
| 9 — CSV in Writer | Show CSV as plain text, rather than rendering it as a table. This is a Writer presentation decision; the existing codec's table model remains current implemented behaviour. Delimiter preservation and `sep=` handling are deferred. |
| 10 — Spreadsheet formats | Select both ODS and XLSX for development. This triggers the model review in ADR 0016; the workbook model and codec details still need a proposal. TSV is deferred. |
| 12 — Office baselines | Pursue further fixes for the seven `suspected-defect` render rows: three `letterhead-frames` and four `line-break-within-paragraph`. Do not close them as accepted differences or documented limitations. |

All remaining unanswered choices from the summary are deferred: ADR 0013
adoption (1); PDF source approvals (3); CSV register approvals (4); verification
tools, corpus and fuzz engine selection (5); performance thresholds (6); and CLI
partial-read policy (11). API freeze, spooling policy and detailed pagination
rules also remain deferred. Deferral leaves existing restrictions and release
gates in force; it is neither approval nor rejection.

Implementation work remains tracked in the [roadmap](../roadmap.md).
