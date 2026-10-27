# Issue #164, third pass: cell-relative shape alignment

ODT pictures and text frames can state `style:horizontal-pos="left"`,
`"center"` or `"right"` relative to their anchoring paragraph inside a table
cell. Previously the reader retained only the numeric `svg:x`, so these objects
were placed at that offset regardless of the stated alignment.

The model now retains this request as `DocumentShape.CellHorizontalAlignment`.
The shared paginator resolves it against the padded cell text area, before
paragraph indents, and the ODT writer preserves it on round trips. An absent
alignment keeps the existing numeric-offset behavior. Page-relative anchors
do not acquire a cell alignment. DOCX export reports its alignment fallback;
RTF and vector PDF export already report their unsupported cell-relative
placement.

Seven regression cases cover left/center/right placement, padding and paragraph
indents, picture round trips and text-frame round trips. The full test suite
passes: **4,354 passed, three skipped**.

## Original sample evidence

`odfexport/data/tdf169882.odt` retains seven pages, matching LibreOffice. Three
pages improve against the reference; pages 1, 3, 6 and 7 are unchanged. The
seven LibreOffice reference rasters are byte-identical between the before and
after runs.

| Page | Mean absolute RGB difference before | After |
| --- | ---: | ---: |
| 2 | 8.7609% | 5.5744% |
| 4 | 18.0279% | 3.0608% |
| 5 | 8.8720% | 6.6781% |

This supporting measurement averages absolute differences across all RGB
channels and pixels, divided by 255. It is not the suite's blurred-image
metric or a changed acceptance threshold. The sample's worst page remains
page 6, with 22.8335% blur difference, so its pixel check still fails.

The complete 30-sample replay retains **44/89 passed, 45 failed, one skipped**,
with no passing check regressed. Its worst-page observations are unchanged for
all 30 samples; the per-page evidence above captures the improvement hidden by
that summary.

The original selection and input hashes remain in
`tests/office/selections/issue-164.txt` and `issue-164.sha256`. Comparisons use
the [second pass](office-issue-164-table-flow-and-styles.md) as the before state,
with the same Windows / LibreOffice 26.2.6.3 / Poppler 26.07.0 / Liberation fonts
/ 96 DPI / 80% threshold / 40-page cap / 120-second reference timeout.

```powershell
dotnet build src/Broiler.Documents.Cli --no-restore
dotnet test Broiler.Documents.slnx --no-restore
dotnet run --project src/tests/Broiler.Documents.Office --no-build -- `
  --extras-dir D:/scratch/lo-core/sw/qa/extras `
  --sample-list tests/office/selections/issue-164.txt `
  --workspace D:/scratch/issue164-round3-final `
  --triage-dir D:/scratch/issue164-triage-round3-final `
  --previous-triage D:/scratch/issue164-triage-round2-verified/triage.json `
  --font-dir D:/office-fonts --dpi 96 --jobs 4 --keep
```
