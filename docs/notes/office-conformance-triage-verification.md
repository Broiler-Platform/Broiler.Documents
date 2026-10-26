# Office conformance triage verification

Verified on 2026-10-25 against main `f06830b` with the new Office-runner and CI
changes. No document codec or baseline tolerance was changed by this work.

The control group uses the authored `typed-letters.html`,
`multi-paragraph-item.html` and `direct-centered-block.html` probes from the
issue #147 work. The before executable was the isolated build preceding the
last HTML fixes (`five-before-bin` from the issue #147 verification); the after
executable was the merged main build.
Both used LibreOffice 26.8.1.1, poppler 26.05, 72 DPI, an 80% threshold, a
40-page limit and a 30-second reference timeout. Fonts were not pinned, so this
is a local workflow verification, not reproduction of the original CI ranking.

| Control | Before | After | Triage result |
| --- | --- | --- | --- |
| Typed letters | Text failed; read/pixel passed | 3/3 passed | One fixed text check |
| Multi-paragraph list item | Text failed; read/pixel passed | 3/3 passed | One fixed text check |
| Direct centered block | 3/3 passed | 3/3 passed | Measurements changed; no newly passing check |

The generated comparison recorded equal input hashes and environment fields,
no missing controls and no regressed checks: **7/9 to 9/9 passed**. It retained
the before/after rendering observations instead of interpreting every improved
measurement as a newly passing check.

A remote exact-selection run requested only
`ooxmlexport/data/tdf126533_noPageBitmap.docx`. It downloaded one source file
without a sparse checkout or GitHub Trees API enumeration; both semantic-axis
checks passed. The shell wrapper was also exercised with an exact selection.
The original 30 issue #147 sample IDs are available in
[the control list](../../tests/office/selections/issue-147.txt).

Unit coverage includes input and environment changes, worst-page shifts,
skipped/new/missing coverage, separate crash/refusal groups, schema validation,
invalid and missing IDs, replay-list ranking, and prevention of focused runs
updating the global GitHub ranking issue. The full unit suite passed 4,046 tests (three skipped); the Office test project
contains 32 passing tests. The Release solution build passed. Shell syntax and
workflow YAML parsing also passed.

The CI workflow's existing tool
approval register, baseline classification and pixel threshold remain in force.

See [the guide](../guides/office-conformance.md#focused-triage-and-repeatable-control-groups)
for replay commands, CI dispatch and interpretation limits.
