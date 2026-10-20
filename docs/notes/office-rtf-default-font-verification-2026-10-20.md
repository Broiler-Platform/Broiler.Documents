# RTF default-font verification — 2026-10-20

The selected defect is the residual `line-break-within-paragraph/rtf` render
difference after the font line-gap correction. The fixture declares `\deff3`,
whose font-table entry is Liberation Serif. Its body paragraphs use `\plain`
without explicit font selections. The reader ignored `\deff`, so these runs
inherited the renderer's fallback family instead of the document's declared one.

The reader now resolves an explicit default-font index into
`DocumentStyleDefaults.FontFamily`. The writer includes that family in its font
table and emits its index in `\deffN`, preserving it across export. Explicit
run families retain precedence; missing, unknown and blank default-font entries
leave the family unspecified.

Local before/after evidence uses Windows, LibreOffice 26.2.6.3, Poppler 26.07.0,
144 DPI and explicitly mapped local Liberation fonts. The before run includes
the previous line-gap correction.

| Measurement | Before | After |
|---|---:|---:|
| Page count (both renderers) | 1 | 1 |
| Ink-box difference | 6 px | 1 px |
| Ink-profile similarity | 0.992545 | 0.999856 |
| Ink coverage difference | 0.004046 | 0.000262 |
| Blurred difference ratio | 0.025866 | 0.000320 |

The profile and coverage bands are now exact and the blurred difference is
clean. Reports are `D:\office-linegap-after.json` (RTF row) and
`D:\office-rtf-default-font-after.json`.

Five new regression cases cover inherited and explicit families, `\plain`,
read/write preservation, unknown/missing/blank defaults and unspecified defaults
beside explicitly styled runs. All 252 RTF tests and 231 CLI tests pass.

The office run passed seven checks and skipped the baseline comparison because
the local toolchain/font stamp differs from the pinned Linux baseline. The
official measurements and suspected-defect classification remain unchanged.
The existing developer-only `--allow-pending-tools` switch was used without
changing tool approvals.
