# Issue 145: ten highest-impact cases

The ten highest severity scores in [issue 145](https://github.com/Broiler-Platform/Broiler.Documents/issues/145) were selected in reported order and rerun from the original public LibreOffice sample files. Nine overlap the top ten of #143. This change corrects index-source classification; **it does not resolve the ten visual conformance failures**.

## Local results

LibreOffice 26.8.1.1, poppler 26.05.0, Linux, 72 DPI, unpinned fonts, unchanged 80% threshold and a 40-page cap. The issue used LibreOffice 26.2.6.3, poppler 26.01.0 and 144 DPI; these are local comparisons, not exact reproductions of that environment. Page counts below are Broiler / LibreOffice. Similarity is the runner's rendering score, not an overall conformance percentage.

| Rank | Reported severity | Sample under `sw/qa/extras` | Pages | Similarity | Investigation / remaining work |
| --- | ---: | --- | --- | ---: | --- |
| 1 | 1771.69 | `ooxmlexport/data/tdf163894_hidden.docx` | 5 / 4 | 22.64% | The reported 40 LibreOffice pages are not reproduced locally. Existing hidden-text handling is not retuned to this count. |
| 2 | 1367.46 | `layout/data/tdf142694-1.odt` | 5 / 8 | 8.5% | Nested tables, repeated headers and image flow remain open; see the investigation for #143. |
| 3 | 1336.89 | `uiwriter/data/tdf147199.docx` | 5 / 8 | 0% | Inline drawing groups lack an inline flow footprint in the model/layout. A proper inline-drawing representation remains necessary. |
| 4 | 1321.56 | `tiledrendering/data/page-background-link.fodt` | 1 / 1 | 0.11% | The background is a document-controlled external URL at 192.0.2.1. It is not fetched automatically. |
| 5 | 1318.58 | `layout/data/floattable-center-shift-down.docx` | 4 / 4 | 66.15% | Page counts match with the local reference version; floating-table avoidance and text geometry still differ. |
| 6 | 1303.92 | `layout/data/fdo43573-2-min.docx` | 22 / 15 | 52.76% | Two-column flow, anchored images and tables remain open. Column widths differ by only one twip. |
| 7 | 1299.68 | `ww8export/data/tdf91083.odt` | 6 / 7 | 20.97% | Nested tables, merged cells and row pagination remain open. |
| 8 | 1286.87 | `ooxmlexport/data/tdf141173_missingFrames.rtf` | 1 / 2 | 0% | LibreOffice has a blank first page. Earlier broad frame/page-break experiments failed independent probes and remain rejected. |
| 9 | 1279.43 | `ooxmlexport/data/tdf146955.odt` | 40 / 40 | 0% | Complex frame/resource layout remains open, including the known header-geometry visual regression documented for #143. Both counts hit the 40-page cap. |
| 10 | 1235.92 | `layout/data/ToC-in-linked-frame-on-first-page-style.fodt` | 2 / 5 | 61.35% | Cached index entries and linked-frame names survive. Generation-source metadata was incorrectly reported as lost block content; corrected here. Unstyled body headings still paginate differently. |

## Cached indexes and generation instructions

`ToC-in-linked-frame-on-first-page-style.fodt` contains an index inside a linked text frame. Its `text:table-of-content-source` describes generation rules and templates, while `text:index-body` holds the displayed cached entries. The reader previously reported the source as an unsupported, skipped content block despite retaining the actual entries.

The reader now skips only the matching source element inside each of the seven supported index containers, then reads the cached title and entries normally. Unknown index children still produce the existing unsupported-content warning. It does not regenerate indexes, refresh page-number fields, or import template text as displayed paragraphs. Tests cover all seven index types, linked-frame content/names, and unknown children.

The source-metadata warning disappears from the original input. The before/after runs confirm that all ten pixel and semantic-text check details and page counts remain unchanged. This is a diagnostic correction, not a visual rendering fix.

An independent LibreOffice FODT rewrite of this original gives the unstyled body `text:h` elements an application-generated `Heading_20_1` style: 18pt bold, 12pt before and 6pt after, inherited from `Heading`. That style and those values are absent from the source file. The Broiler model reads these headings with the declared default paragraph text size (12pt). This explains a concrete source of different pagination, but does not justify silently injecting LibreOffice-specific heading metrics into every ODF reader result. The linked-frame pagination case remains open.

## Validation and artifacts

Reports and original input downloads are outside the checkout under `/workspace/.setup`:

- `issue145-inputs.json` records SHA-256 hashes of the downloaded originals.
- `issue145-before.json`, `issue145-before.log`, `issue145-before-work`.
- `issue145-after.json`, `issue145-after.log`, `issue145-after-work`.
- `issue145-build.log`, `issue145-tests-final.log`, `issue145-corpus.json`.
- `issue145-normalized/ToC-in-linked-frame-on-first-page-style.fodt` (independent LibreOffice rewrite used for the heading investigation).

The Release solution build succeeds. All 3,896 executed unit tests pass (3 additional skips); the ODT project includes 485 passing tests. The CLI corpus passes 2,320/2,320 checks with 105 documented skips. The original ten comparisons remain at **12/30** checks (10 reads and 2 text checks; all 10 pixel checks remain open). No thresholds or corpus baselines are relaxed. Prior fixes and the known #143 visual tradeoffs are unchanged; see [the #143 investigation](office-issue-143.md).

Reproduce the original-document run after activating the environment and building Release:

```sh
dotnet run --project src/tests/Broiler.Documents.Office -c Release --no-build -- \
  --extras-dir /workspace/.setup/issue145 --soffice /workspace/.setup/soffice26 \
  --jobs 3 --dpi 72 --max-pages 40 --lo-timeout 30 --top-differences 10 \
  --report /workspace/.setup/issue145-after.json \
  --workspace /workspace/.setup/issue145-after-work --keep
```

The Office runner exits with 18 for the 18 remaining failed checks; those are recorded conformance differences, not a successful comparison run.

## Further cases: embedded DOCX page-background images

Investigated ranks 16 (`tdf132149_pgBreak.odt`), 23 (`tdf126533_pageBitmap.docx`), 28 (`fill-image-name-missing-definition.odt`) and 30 (`pageBreakInHiddenSection.fodt`). The embedded DOCX bitmap defect is corrected; the other three visual comparisons remain open.

### Import correction

`tdf126533_pageBitmap.docx` references an embedded JPEG from `v:background/v:fill` with `type="frame"`. The reader previously replaced picture fills with a solid fallback colour. It now loads embedded page-background `frame` and `tile` fills using the existing DOCX image loader and stores an `ImageFill` on the page background. `frame` stretches across the paper; `tile` uses the image's intrinsic size. This change targets ordinary rectangular backgrounds; VML-specific sizing, offsets, aspect modes and transparency remain on the existing colour-fallback path with a warning.

The existing relationship resolution, per-image size limit, raster-format checks and resource-policy admission apply. External pictures are not fetched. Missing, unsupported or denied images retain the existing colour fallback and emit diagnostics. A disabled background or disabled fill does not load the image. DOCX export still approximates bitmap fills and emits its existing `docx.fill.extended` warning; this is an import/render fix, not image-fill DOCX round-trip support.

### Original-document result

The original bitmap document now passes **3/3** checks (read, text and pixels), compared with **2/3** before. Both renderers produce one page. The independently rerun pixel comparison has zero measured blur difference, ink-profile similarity 99.992%, coverage delta 0.012% and a one-pixel ink-bounds difference. The prior comparison had 42.01% blur difference and zero profile similarity. These metrics use the same local versions/settings listed above.

The other investigated cases remain open:

- `tdf132149_pgBreak.odt`: six pages vs five, rendering similarity 68.51%.
- `fill-image-name-missing-definition.odt`: one page each, rendering similarity 0.18%; the named bitmap definition is absent.
- `pageBreakInHiddenSection.fodt`: two pages vs three, including a page-geometry difference. Hidden-section break semantics and surrounding flow need further investigation.

Regression tests cover embedded bytes/repeat mode, missing and external relationships, resource denial, image size limits and disabled display/fills. The full Release build succeeds and all **3,905 executed unit tests** pass, with three additional skips. No comparison threshold or baseline was relaxed.

Artifacts outside the checkout: `issue145-more-before.json`, `issue145-more-final.json`, `issue145-bitmap-verified.json`, their retained `*-work` directories (the final guarded run uses `issue145-more-guard-work`), and `issue145-bitmap-final-build.log`, `issue145-bitmap-final-tests.log`, `issue145-bitmap-corpus.json`. The four-case final comparison improves from **8/12 to 9/12** checks; only the bitmap pixel outcome changes. The CLI corpus passes **2,320/2,320** checks with 105 documented skips.

## Further case: missing ODT bitmap definitions retain the declared colour

Rank 28, `odfimport/data/fill-image-name-missing-definition.odt`, now passes **3/3** checks. The file names a bitmap definition that is absent, but explicitly declares `draw:fill-color="#99ccff"`. LibreOffice paints that blue colour; the reader previously discarded the entire fill and left a white page.

An unresolved bitmap fill now uses its explicitly declared, valid `draw:fill-color`. It still emits `odt.fill`, explaining that the bitmap could not be resolved and the fallback colour was drawn. With no valid declared colour it keeps the previous no-fill result. Resolvable images retain priority. The shared fill wrapper still applies opacity and `draw:background-size` to the fallback; no external image loading or implicit colour defaults were added. This applies to bitmap fills only; unresolved hatch behaviour is unchanged.

Under the same local comparison settings, the original's blur difference falls from **99.82% to 0.17%**, and profile similarity rises from **25.68% to 99.99999%**. Both renders have one page; ink-bounds delta remains five pixels. The four-case comparison improves from **9/12 to 10/12** checks. The DOCX bitmap correction stays passing, and the two remaining pagination cases have unchanged outcomes.

The Release build succeeds and all **3,908 executed unit tests** pass (three additional skips). Three new regressions cover explicit vs absent fallback colours, opacity/content bounds, and external-reference diagnostics without fetching. Comparison thresholds and baselines remain unchanged.

Artifacts outside the checkout: `issue145-odt-fallback-before.json`, `issue145-odt-fallback-after.json`, `issue145-odt-fallback-work`, `issue145-odt-fallback-build.log`, `issue145-odt-fallback-tests.log` and `issue145-odt-fallback-corpus.json`.

The final CLI corpus passes **2,320/2,320** checks, with 105 documented skips. The top-ten #145 rerun (`issue145-odt-fallback-top10.json`) stays at 12/30; all pixel/text check details and outcomes are unchanged from the preceding top-ten run. The existing frame-heavy-document tradeoffs remain as documented above.

## Further case: update text width when a page-style sequence advances

Rank 16, `ooxmlexport/data/tdf132149_pgBreak.odt`, now renders **five pages vs LibreOffice's five**, rather than six vs five. This remains a partial correction: the pixel check is still below the unchanged 80% threshold (68.51% before, 69.26% after), while read/text checks remain passing. Worst-page blur difference changes from 13.62% to 14.59%, so this is not claimed as an across-the-board pixel improvement.

Two pagination defects were isolated with authored page-style cases:

- Advancing an automatic page-style sequence updated paper and margins, but kept the old computed text-column width. Columns are now refreshed after the destination style is selected.
- If a paragraph's first line overflowed onto another page/column, the ordinary overflow path placed its old line composition there. A paragraph with no placed lines is now measured against the destination page, with line heights and widow/orphan segmentation recalculated.

Tests cover a narrow-to-wide automatic overflow (the paragraph fits on a single destination-page line) and an explicit wide-to-narrow page break (all line pieces fit the narrower text area). They check content and positions, not only page counts. The existing oversized-frame regression also ensures that a frame already placed on the preceding page stays there when its anchor text continues. Reflow of the remainder of a paragraph that already has lines placed on a previous page, and recomposition of tables already spanning different paper widths, remain outside this change.

Four more original DOCX samples from #145 were downloaded and investigated: rank 24 `tdf123163-1.docx`, rank 25 `tdf135623.docx`, rank 26 `tdf87569_vml.docx`, rank 27 `fdo78883.docx`. Their local baseline is 5/12 checks: all four read, the VML sample already passes pixels, and all four text checks remain open. Their after-run details are unchanged. The original top ten also retain all pixel/text outcomes and measured check details at 12/30. Among the previous four additional originals, only `tdf132149_pgBreak.odt` changes; both corrected bitmap cases stay passing and `pageBreakInHiddenSection.fodt` remains open.

Artifacts outside the checkout: baselines `issue145-odt-fallback-after.json` and `issue145-round4-before.json`; final comparisons `issue145-remeasure-eight-final.json` and `issue145-remeasure-top10-final.json`; their retained workspaces; `issue145-remeasure-build.log`, `issue145-remeasure-tests.log`, and `issue145-remeasure-corpus-final.json`. These use the local LibreOffice/poppler/DPI settings described above. No baseline or threshold was relaxed.

The final Release build and all **3,910 executed unit tests** pass, with three additional skips. The pagination project passes all 236 tests, including the oversized-frame guard and the two new width regressions.

The final CLI corpus passes **2,320/2,320** checks, with 105 documented skips. The final 18-original comparison changes only the pagination/profile/blur details of `tdf132149_pgBreak.odt`; all other check details and outcomes remain unchanged.

## Further case: preserve signed VML drawing order

Investigation of rank 24, `layout/data/tdf123163-1.docx`, found that the VML reader reversed the order of negative `z-index` values. `Math.Abs()` assigned a deeply negative background a higher model drawing order than foreground objects in the same behind-text layer, so a background could cover text and other shapes. The model requires nonnegative order values; negative VML levels are now translated monotonically from `int.MinValue..-1` into `0..int.MaxValue`, retaining `BehindText=true`. Nonnegative VML levels remain unchanged.

The importer regression checks relative ordering across `int.MinValue`, the original's large negative background index, adjacent negative indices, and -1, as well as unchanged nonnegative handling. An independent two-rectangle DOCX probe declares red at -1 and blue at -2, with red first in XML to ensure document order cannot mask the defect. At the overlap pixel (50,50), LibreOffice and the corrected renderer both return red `(255,0,0)`; the baseline renderer returned blue `(0,0,255)`. Outside the red rectangle, both new/reference outputs are blue. Probe artifacts: `/workspace/.setup/vml-negative-overlap.docx`, `vml-negative-lo/reference.png`, `vml-negative-before-001.bmp`, `vml-negative-ours-001.bmp`.

**The ranked original remains unresolved.** Both exports still have 16 pages and rendering similarity remains 14.98%. The corrected order reveals previously covered text, but the measured worst-page blur increases from 47.84% to 49.52%, while profile similarity improves from 99.05% to 99.90%. The grey panel is still horizontally misplaced; fonts, unequal columns and other shape geometry remain open. This is a confirmed drawing-order correction, not a claim of overall visual improvement or a newly passing ranked case.

A second experiment used the current section's margins to convert VML offsets. Its original comparison changed to 15 vs 16 pages and introduced geometry differences. It was rejected and fully reverted, including its test and temporary section accessor; only the independently verified drawing-order change is retained. Correct page-relative horizontal placement needs to account for the actual page owner across continuous sections, rather than substituting another static margin.

Final validation artifacts outside the checkout: `issue145-vml-final-build.log`, `issue145-vml-final-tests.log`, `issue145-vml-eight-final.json`, `issue145-vml-top10-final.json`, and `issue145-vml-corpus-final.json`. Intermediate `vml-section` and `vml-horizontal` reports describe rejected experiments and are not the retained implementation. Comparison settings and thresholds remain unchanged.

The retained implementation passes the Release build, all **3,911 executed unit tests** (three additional skips) and **2,320/2,320 CLI corpus checks** (105 documented skips). Final comparison of 18 originals changes only the pixel details of `tdf123163-1.docx`; all pass/fail outcomes and the other 17 documents remain unchanged.

## Further investigation: explicit DOCX text-frame height growth

The reader ignored explicitly requested text-frame auto-fit in both supported DOCX representations: DrawingML `wps:bodyPr/a:spAutoFit` and VML `v:textbox` CSS `mso-fit-shape-to-text:t`. These now set the model's existing `GrowsToText` flag. A text frame keeps its stated height as a floor and may grow to its laid-out paragraphs/tables, using the existing shared layout machinery. The DOCX writer now emits `a:spAutoFit` for growing text frames, preserving the setting on round trip. No width auto-sizing or font-shrinking support is implied; `a:normAutofit` remains outside this height-growth support. Fixed/no-auto-fit frames keep their existing behaviour.

Nine new importer/round-trip checks cover DrawingML shape/no/font-auto-fit modes and true/false VML spellings. The Release build and all **3,920 executed unit tests** pass (three additional skips).

An independent authored DOCX has a two-line green VML text frame initially only five points high. LibreOffice and the corrected raster renderer paint the expanded background green at (22,30); the exact pre-change baseline paints white there because it keeps the fixed five-point box. Artifacts outside the checkout: `docx-autofit-probe.docx`, `docx-autofit-lo/reference.png`, `docx-autofit-before-001.bmp`, `docx-autofit-after-001.bmp`.

The separate page-relative-X model prototype was tested on `tdf123163-1.docx` but changed pagination to 15 vs 16 pages without making the original pass. It is fully reverted; only its patch remains outside the checkout as `issue145-pagex-prototype.patch`. It is not part of this auto-fit change.

All 30 ranked originals were downloaded to `issue145-all`, with SHA-256 hashes in `issue145-autofit-inputs.json`. An isolated CLI published from commit `146fe49` is the exact pre-auto-fit baseline (`issue145-autofit-baseline-bin`), so all 30 before/after results use identical input bytes, references and thresholds. Reports: `issue145-autofit-all-before.json`, `issue145-autofit-all-after.json`; unit/build logs: `issue145-autofit-build.log`, `issue145-autofit-tests.log`; corpus: `issue145-autofit-corpus.json`. These are outside the checkout under `/workspace/.setup` and use the local LibreOffice/poppler/DPI settings listed above.

The final 30-original before/after comparison stays at **42/89 passing checks**, with 47 failures and one skipped pixel check: LibreOffice produces no PDF for `uiwriter/data/tdf170672.docx` in either run. All 30 reads pass. Only `tdf135623.docx` changes measured pixel details: rendering similarity 65.14% → 64.75%, blur difference 34.86% → 35.25%, with an improved ink profile. Pagination remains one vs two pages. The other 29 documents' check details and outcomes are unchanged. **No additional ranked original is fully resolved by this change.** The retained fix is explicit auto-fit import/export fidelity, independently verified against native LibreOffice growth, with the original's small visual regression reported rather than hidden.

The final CLI corpus passes **2,320/2,320** checks with 105 documented skips. No assertion, baseline or threshold was relaxed.

## Further investigation: continuation reflow and nested floating tables

A prototype addressed the remaining limitation in paragraphs that continue across differently sized page styles. It carried source offsets through text pieces and long-token splitting, then re-composed only the not-yet-placed tail when the destination width changed. Two authored tests covered alternating widths, hidden runs, styled text and a long unbroken token; all 238 pagination tests passed with the prototype.

However, the original `tdf132149_pgBreak.odt` regressed from five vs five pages and 69.26% similarity to six vs five pages and 39.51% similarity (worst-page blur 23.81%, geometry mismatch). The prototype was rejected and **all source and test additions were reverted**. No threshold or baseline was changed. Its patch remains outside the checkout at `/workspace/.setup/issue145-tail-reflow-rejected.patch`; the experimental report is `issue145-tail-reflow-probe.json`. This work does not establish that the original's continuation problem is solved.

After rebuilding the unchanged retained CLI, `issue145-tail-reflow-rollback.json` confirms the previous five vs five pages, 69.26% similarity and 14.59% worst-page blur. The rollback build is recorded in `issue145-tail-reflow-rollback-build.log`. No new production fix or passing original case is claimed in this round.

Rank 19, `layout/data/tdf170381-split-float-table-in-normal-table.docx`, was also inspected. It contains a 13-row floating table inside a four-row ordinary table, with `w:tblpPr` offsets on the inner table. The reader currently reports `docx.table.floating` for cell-nested floating positions and lays that table out inside its cell. The original remains one vs two pages (0.51% local rendering similarity). Preserving the offset alone would not implement floating-table overflow beyond its containing cell; proper nested floating-table placement/pagination is still required. No guessed page break was added.

The current `tdf135056.odt` import was checked for additional lost index/frame blocks and reports zero unsupported blocks. Its cached index text still differs from LibreOffice's regenerated reference, and this investigation does not treat that as evidence to discard or invent model text.
