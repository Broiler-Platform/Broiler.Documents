# Issue 143: highest-impact cases, investigation and safe fixes

The ten highest severity scores in
[issue 143](https://github.com/Broiler-Platform/Broiler.Documents/issues/143)
were selected in their reported order. This investigation does **not** claim
that ten original cases were fixed. The initial investigation corrected two reproducible implementation defects,
but the ten original comparisons remain unresolved. A further HTML-table fix
and measurements for ranks 26 and 30 are documented below.

## Selected cases

Paths below are relative to LibreOffice's `sw/qa/extras` directory. Page counts
are from the local comparison with LibreOffice 26.8.1.1, not the issue's 26.2.6.3.

| Rank | Reported severity | Sample | Finding and remaining work |
| --- | ---: | --- | --- |
| 1 | 1771.69 | `ooxmlexport/data/tdf163894_hidden.docx` | Local counts are 5 vs 4. Neither LibreOffice 25.2 nor 26.8 reproduces the issue's 40 reference pages. No change was made to force the renderer toward that unverified count. |
| 2 | 1343.16 | `layout/data/tdf142694-1.odt` | Nested tables, repeated headers and large inline images; 5 vs 8 pages. The general nested-row splitting defect described below is fixed, but this original input's render is unchanged. |
| 3 | 1336.89 | `uiwriter/data/tdf147199.docx` | Inline drawing groups are retained as paragraph-anchored shapes without their inline flow footprint. The local comparison remains 5 vs 8 pages. Correct inline drawing layout needs further model/layout work. |
| 4 | 1324.92 | `ooxmlexport/data/tdf146955.odt` | Many page-anchored text frames and unsupported or missing image/object resources. Both renders are capped at 40 pages; this cannot establish whole-document pagination conformance. |
| 5 | 1321.56 | `tiledrendering/data/page-background-link.fodt` | The background points to `http://192.0.2.1:12345/background.png`, not embedded image bytes. The reader's refusal to fetch document-controlled external resources is retained. No automatic network loading was introduced. |
| 6 | 1318.58 | `layout/data/floattable-center-shift-down.docx` | Reference-version dependent: 4 vs 3 pages with LibreOffice 25.2, but 4 vs 4 with 26.8. Visual differences remain. The upstream test requires inline tables to move past the floating table; the page-count discrepancy alone does not justify another shift. |
| 7 | 1303.92 | `layout/data/fdo43573-2-min.docx` | Two-column section, anchored images and tables; 22 vs 15 pages. The explicit column widths differ by only one twip, so unequal widths are not an adequate explanation. Wrapping and section flow remain open. |
| 8 | 1299.68 | `ww8export/data/tdf91083.odt` | Nested tables and merged cells; 6 vs 7 pages. The general row-splitting fix is covered by authored tests, but this input's render is unchanged. |
| 9 | 1286.87 | `ooxmlexport/data/tdf141173_missingFrames.rtf` | Positioned paragraphs, an ordinary drawing and an explicit page/section break; 1 vs 2 pages. LibreOffice's first page is blank. Broader page-break rewrites were rejected after independent frame/drawing probes contradicted them. The safe final-anchor fix below does not resolve this original input. |
| 10 | 1242.37 | `ooxmlexport/data/tdf127814.docx` | A cell spans 28 rows and states `w:textDirection="btLr"`. Vertical cell text is not represented by the current table model. Both renders have two pages, but visual and text differences remain. |

## Changes retained

### Nested table rows may split when both rows allow it

`ComposeBlocks` previously made each nested row one indivisible cut piece,
regardless of the nested row's `CanSplit` setting. A long nested row could
therefore extend beyond the page instead of continuing on the next one.

Nested rows now expose common legal cut boundaries. Overlapping atomic pieces
from different cells are merged with a sorted interval sweep, so a cut cannot
slice through another cell's line, kept paragraph or protected block. Minimum
heights and vertical alignment are accounted for. A row that forbids splitting,
at either level, still stays whole. New tests check continuation, explicit keeps,
different cell line heights, preservation of every paragraph and page bounds.
The existing no-split test now explicitly states its intended `canSplit: false`.

This is a general pagination fix, not evidence that cases 2 or 8 are now resolved.
Their remaining table behaviour needs separate work.

### A final RTF frame keeps its body anchor

A frame after existing body text can refer to the next body paragraph while
the accumulator has no text for that paragraph. At end of input, `Build` used
to omit this empty paragraph whenever earlier paragraphs already existed.
The shape then referenced an index outside the document and could disappear
from layout. The reader now commits that final anchor before building the model.
A regression test covers body text followed by a final positioned frame.

No change to the interpretation of `\page` was retained. Independent reference
probes showed that a general rewrite would alter unrelated frame and ordinary
drawing behaviour. In particular, correcting case 9's page count while placing
its frames on the wrong page is not a valid conformance fix.

## Validation

The Release build succeeds. All 3,850 executed unit tests pass, with three
additional tests skipped. The CLI corpus passes 2,320/2,320 checks, with 105
additional checks skipped for their documented reasons. No fidelity assertions or comparison thresholds were relaxed. The corpus
baseline records three newly exposed `*.table.flattened` diagnostics for the
authored HTML table: the reader now retains its grid, while those existing
writers report losing it. All previous differences and classifications remain.

The original ten-sample comparison remains at 12/30 passing checks before and
after the retained changes: ten reads and two text checks pass; ten pixel checks
and eight text checks fail. No original top-ten input has been fully fixed in
this change. The improvement demonstrated by the new regression tests must not
be presented as an improved original-corpus score.

The ten original input files were fetched again from upstream and all ten
matched the bytes used for the initial comparison. Their SHA-256 hashes are
recorded outside the checkout at `/workspace/.setup/issue143-inputs.json`.
No upstream document binaries are committed.

Before/after runs use the same inputs, LibreOffice 26.8.1.1, poppler 26.05.0,
72 DPI, unpinned fonts, an 80% threshold and a 40-page cap. A baseline CLI was
built from `ed822c2` in an external source snapshot. An additional baseline
with LibreOffice 25.2.3.2 exposes the reference-version differences above.
Neither reference version exactly reproduces the issue's toolchain.

Reproduction in the prepared cloud workspace:

```sh
source /workspace/.setup/activate.sh
dotnet build Broiler.Documents.slnx -c Release --nologo -m:4
pwsh -NoProfile -File eng/run-tests.ps1 -Configuration Release
dotnet run --project src/tests/Broiler.Documents.Office -c Release --no-build -- \
  --extras-dir /workspace/.setup/issue143 \
  --soffice /workspace/.setup/soffice26 --jobs 3 --dpi 72 --max-pages 40 \
  --top-differences 10 --report /workspace/.setup/office143-recheck.json
```

The original ten-sample comparison is still expected to fail. Authored regression
coverage for the two retained fixes must not be confused with closure of those
whole-document comparisons.

## Further cases: HTML tables (ranks 26 and 30)

The HTML reader previously treated `table`, `tr`, `td` and `th` as plain block
containers. It now retains the row/cell grid, empty cells, nested tables,
row/column spans, captions, header-cell emphasis, legacy `border`, `cellpadding`,
`width` and cell backgrounds. Paragraphs remain in row-major model order.

HTML tables without a stated grid opt into content-based column sizing. The
paginator measures longest unbreakable tokens and unwrapped lines, incorporates
spanning-cell constraints, and interpolates within the available width. Explicit
widths still apply. The new model option defaults off, preserving the existing
behaviour of other readers. Paragraph edits retain the option.

This is basic table import, not full CSS table layout: separate border spacing,
column declarations, CSS table geometry and vertical alignment remain open.
HTML writing still explicitly reports that it flattens tables.

Fresh originals were compared against the pre-change `ed822c2` CLI using the
same LibreOffice 26.8.1.1, fonts, 72 dpi and unchanged 80% similarity threshold:

| Input | Before profile similarity | After | Result |
| --- | ---: | ---: | --- |
| `htmlimport/data/tdf118579.html` | 72.53% | 82.23% | Pixel check now passes; semantic text check still fails. |
| `htmlimport/data/table_border_1px.html` | 22.75% | 58.96% | Improved profile and ink bounds; pixel check remains open. Read/text checks pass. |

Both inputs remain one page on both sides. Across these two originals, checks
improve from 3/6 to 4/6. These are partial conformance improvements: blurred
pixel differences increase slightly (4.78% to 5.08%, and 4.89% to 6.16%), so the
profile gain must not be described as improvement in every visual metric or as
full resolution of either document. The top-ten findings above remain open.
