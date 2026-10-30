# Roadmap decisions, 2026-10-19

Recorded at the project reviewer's direction. Numbers refer to the
twelve-question roadmap summary reviewed in chat. These are scope and policy
decisions; they do not report implementation or satisfaction of release gates.

| Question | Decision |
|---|---|
| 2 — PDF font embedding | Approve the general functionality for both embedding and subsetting caller-supplied fonts, respecting the `OS/2.fsType` flags. Existing caller permission and generated-document obligation checks remain applicable. See the [IP-012 record](../ip/briefs/ip-012-font-embedding.md). |
| 7 — Format fidelity | Select RTF list-table detection and style-sheet interpretation for implementation. Defer the HTML list, table/CSS and page-header scope choices. |
| 8 — Embedded images | Support embedded images in HTML and Markdown, including `data:` payloads, but ask the user before displaying them. Hosts must enforce consent before display; resource limits still apply. Relative-link policy is deferred. |
| 9 — CSV in Writer | Show CSV as plain text, rather than rendering it as a table. This is a Writer presentation decision; the existing codec's table model remains current implemented behaviour. Delimiter preservation and `sep=` handling are deferred. |
| 10 — Spreadsheet formats | Select both ODS and XLSX for development. This triggers the model review in ADR 0016; the workbook model and codec details still need a proposal. TSV is deferred. |
| 12 — Office baselines | Pursue further fixes for the seven `suspected-defect` render rows: three `letterhead-frames` and four `line-break-within-paragraph`. Do not close them as accepted differences or documented limitations. |

All remaining unanswered choices from the summary are deferred: ADR 0013
adoption (1); PDF source approvals (3); CSV register approvals (4);
verification tools, corpus and fuzz engine selection (5); performance
thresholds (6); and CLI partial-read policy (11). API freeze, spooling policy
and detailed pagination rules also remain deferred. Deferral leaves existing
restrictions and release gates in force; it is neither approval nor rejection.
Pending source and register approvals, ADR adoption and tool selection need
the reviewer; engineering judgment can prepare them but not supply them.

Implementation work is tracked in the [roadmap](../roadmap.md).

## Constraints on the work still open

An implementation assessment of the same date checked what each decision
needed. Since done: editor save-point tracking (`RichTextEditor.MarkSaved`,
`IsModified`), RTF list-table detection (`rtf.list`) and style-sheet reading,
the CSV command-line options, the parser robustness tests, embedded HTML and
Markdown pictures, the XLSX and ODS model, the seven office render rows, and
the roadmap wording on `RtfFuzzTests` and the pagination engine's home. What
still bounds the open work:

- **Picture display needs host consent.** `DocumentResourcePolicy.Default`
  grants transient decoding and extraction into the model, and pagination's
  `ImageStore` decodes model images without a consent policy. Opening a
  document or using the default policy is not the consent decision 8 asks
  for; the host must ask before display. Relative targets stay unfetched, and
  picture support must not bring network fetches with it.
- **The IP-012 approval is not an implementation specification.**
  `DocumentFontEmbedding` checks caller permission and declared restrictions
  only. Still needed: inspection of the actual font bytes, caller-supplied
  provenance, obligation handling, Graphics export and subsetting, shaping and
  shared pagination, then PDF font emission and preflight. Passing the
  permission tests does not validate font programs. Keep host PDF output
  disabled until its gates pass. The type's XML remarks still say embedding is
  outside IP-012, which the widening made stale.
- **Plain-text CSV in the Writer must not lose the dialect.** The presentation
  adapter keeps the codec's table model; turning a table into generic plain
  text and saving it back is not a faithful CSV editing path, since quoting and
  delimiters can be lost.
