# Issue #164, second pass: table flow and ODT styles

This continues [the first pass](office-issue-164.md). It preserves those changes
and uses the same 30 originals and input hashes from
`tests/office/selections/issue-164.txt` and `issue-164.sha256`.

## Corrections

- A paragraph marked keep-with-next now measures the first row of a following
  inline table. Previously the lookahead stopped at the table, allowing a
  heading to be stranded on the previous page. Splittable rows use their first
  legal cut, so the heading can stay with the opening of a row that continues
  onto the next page, without requiring the entire row to fit.
  Explicit table breaks and floating tables retain their separate behavior.
  Measurement uses a snapshot of list counters and does not consume numbering.
- ODT table-row and table-column styles now resolve their parent chains using
  the existing bounded, cycle-checked style resolver. Inherited column widths,
  row heights and row keep-together rules no longer disappear when a child
  style omits them. A child's minimum or exact row height replaces either rule
  inherited from its parent.
- An explicitly disabled cell border side now overrides the uniform border.
  Previously a zero-width side was treated as missing and the uniform border
  was restored. Uniform borders in child styles also override individual edges
  inherited from parents; direct side declarations still override the child's
  uniform declaration.

These changes add 12 regression cases. The two initial heading/table cases
first failed against the preceding implementation, as did inherited
dimensions/keep rules and all three explicit-border override cases. Additional
cases protect row continuations, explicit page breaks, list numbering and
existing direct style overrides.

## Verification

The full unit suite passes: **4,347 passed, three skipped**.

A small independent FODT fixture was exported through LibreOffice's
`OpenDocument Text Flat XML` input filter and `writer_pdf_Export`: seven body
lines remain on page one while a kept heading and its table move to page two,
matching the added paginator regression. Cross-row paragraph keeps behaved
differently in the native Writer export, so that proposed change was removed.
The final implementation does not impose a new cross-row keep rule.

The original-sample replay uses the first pass's final triage as its before
state, with the same Windows / LibreOffice 26.2.6.3 / Poppler 26.07.0 / 96 DPI /
Liberation fonts / 80% threshold / 40-page cap / 120-second reference timeout.
The complete issue's Linux reference archive was not rerun. No baseline or
tolerance was relaxed.

The selected original-sample checks remain **44/89 passed, 45 failed, one
skipped**. No previously passing check regresses. These fixes address supported
format/layout behavior demonstrated by the focused regressions; they do not
newly resolve a check in the issue's top-30 selection.

```powershell
dotnet build src/Broiler.Documents.Cli --no-restore
dotnet test Broiler.Documents.slnx --no-restore
dotnet run --project src/tests/Broiler.Documents.Office --no-build -- `
  --extras-dir D:/scratch/lo-core/sw/qa/extras `
  --sample-list tests/office/selections/issue-164.txt `
  --workspace D:/scratch/issue164-round2-verified `
  --triage-dir D:/scratch/issue164-triage-round2-verified `
  --previous-triage D:/scratch/issue164-triage-final/triage.json `
  --font-dir D:/office-fonts --dpi 96 --jobs 4 --keep
```
