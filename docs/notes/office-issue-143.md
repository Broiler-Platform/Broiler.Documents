# Issue 143: highest-impact cases, investigation and safe fixes

The ten highest severity scores in
[issue 143](https://github.com/Broiler-Platform/Broiler.Documents/issues/143)
were selected in their reported order. This investigation does **not** claim
that ten original cases were fixed. The initial investigation corrected two reproducible implementation defects,
but the ten original comparisons remain unresolved. Further HTML-table, DOCX table-break, ODT frame/picture and table-clipping fixes and
original-document measurements are documented below.

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
| 10 | 1242.37 | `ooxmlexport/data/tdf127814.docx` | A cell spans 28 rows and states `w:textDirection="btLr"`. Vertical cell text is not represented by the current table model. The later table-break fix below corrects the leading section break: the local pixel check now passes, while the text check remains open. Both renders have two pages. |

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

## Validation at `112e9a4`

The Release build succeeds. All 3,850 executed unit tests pass, with three
additional tests skipped. The CLI corpus passes 2,320/2,320 checks, with 105
additional checks skipped for their documented reasons. No fidelity assertions or comparison thresholds were relaxed. The corpus
baseline records three newly exposed `*.table.flattened` diagnostics for the
authored HTML table: the reader now retains its grid, while those existing
writers report losing it. All previous differences and classifications remain.

At `112e9a4`, the original ten-sample comparison remained at 12/30 passing checks before and
after those changes: ten reads and two text checks pass; ten pixel checks
and eight text checks fail. No original top-ten input was fully fixed in
that change. The improvement demonstrated by the new regression tests must not
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

The original ten-sample comparison is still expected to fail overall. Authored regression
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


## Further cases: section-leading tables and empty ODT frames

### DOCX breaks must belong to the next block

The builder carried section and trailing run breaks to the next paragraph. When
that block was a table, its first cell consumed the flag, but table layout does
not apply a cell paragraph's break to the whole table. The table now takes a
pending block break before its cells are read, retaining it as `PageBreakBefore`
or `ColumnBreakBefore`. Empty malformed tables do not consume it. Breaks inside
cells retain their existing handling and diagnostics.

This corrects two original samples:

| Input | Before | After | Remaining limitations |
| --- | --- | --- | --- |
| Rank 16, `ooxmlexport/data/tdf95033.docx` | 2 pages vs 3; final-page geometry mismatched | 3 vs 3 with the correct landscape/portrait sections; measured rendering similarity 76.95% | Pixel check remains below 80%; font and table geometry differences remain. Text check passes. |
| Rank 10, `ooxmlexport/data/tdf127814.docx` | Pixel check failed; worst-page profile similarity 0.96% | Pixel check passes; worst-page profile similarity 87.63%, blur difference 0.18% | Text check still fails; vertical merged-cell text remains unsupported. |

The original ten-sample comparison improves from **12/30 to 13/30** checks:
10 reads, 2 text checks and 1 pixel check pass. No complete top-ten document is
claimed resolved. The other nine samples' observed rendering metrics match the
previous run exactly.

### Empty positioned ODT text frames must not become body paragraphs

A valid text frame with neither visible paint nor text used to fall through to
the body-content reader, which emitted its empty paragraph into the body. It is
now retained as a positioned frame, including its anchor and wrap properties.
A trailing paragraph-anchored frame still gets an empty body anchor when needed;
a page-anchored frame does not require one. A frame containing an unrepresented
nested drawing is not treated as empty: its existing fallback path is preserved.

In rank 14, `mailmerge/data/missing-default-line-color.ott`, the spurious body
paragraph preceded the first named page style and caused an extra page. The
original now has **1 page vs LibreOffice's 1**, previously 2 vs 1. Its worst-page
profile similarity rises from 8.45% to 38.37%, but pixel and text comparisons
remain open. The blur metric increases from 2.33% to 4.31%; correcting the page
boundary does not imply every pixel metric improves.

Ranks 15 (`tdf123351_UnderlineGroupSapeText.docx`) and 20 (`fdo87488.docx`) were
also checked; their outputs remain unchanged. In the four additional originals,
6/12 checks pass before and after; the improvements above correct pagination
without yet clearing another complete check in that group.

### Verification of this follow-up

Release build succeeds; **3,859 executed unit tests pass**, with 3 additional
skips. The CLI corpus passes **2,320/2,320**, with 105 documented skips. No corpus
baseline or conformance threshold changed in this follow-up. Authored regressions
cover section/run breaks before tables, column breaks, one-time consumption,
empty tables, breaks within cells, empty page/paragraph frames, trailing anchors
and unrepresented nested drawings.

The new four-input before/after runs use identical originals, LibreOffice
26.8.1.1, poppler 26.05.0, 72 dpi, unpinned fonts and the unchanged 80% threshold.
They are recorded outside the checkout as `/workspace/.setup/next143-before.json`
and `/workspace/.setup/next143-after-final.json`; the top-ten recheck is
`/workspace/.setup/next143-top10-final.json`. These references differ from the
versions used in the original issue; results are local reproductions.

## Further case: character-anchored pictures and partially visible table lines

Rank 23, `layout/data/tdf152106.odt`, exposes two independent rendering defects:

- `text:anchor-type="char"` was treated as inline, just like `as-char`. A
  character anchor instead floats beside the text. Only `as-char` is inline;
  missing anchor types retain the reader's previous fallback. Pictures now use
  the existing floating-picture path and diagnostic, preserving page-relative
  positions, size and wrapping. The current model still uses a paragraph anchor
  rather than locating the exact glyph for character-relative offsets.
- A fixed-height table row discarded any line extending below its bottom,
  including an image just slightly taller than the row. Partially intersecting
  lines now carry a visible height and are clipped when painted; wholly hidden
  lines are still omitted. Paragraph shading respects the same visible height.
  A containing text frame checks that visible extent, so it does not discard
  the clipped image a second time. Model text and original image size remain
  intact. This applies to body tables and tables inside text frames.

The original's full-page background picture was previously shrunk to column
width and placed in the text. It now covers the page. Its image in a fixed-height
row is also restored instead of disappearing. The comparison remains open:
font substitution and text wrapping/flow still differ.

| Worst-page metric | Before (`72ab55e`) | After character-anchor fix | After both fixes |
| --- | ---: | ---: | ---: |
| Blur difference | 70.30% | 32.54% | 28.86% |
| Ink profile similarity | 87.51% | 96.34% | 99.51% |
| Ink bounds delta | 81 px | 6 px | 6 px |
| Coverage delta | 15.25% | 0.27% | 2.35% |

Both renders have four pages throughout. Worst-page selection changes from page
4 to page 1; these are the comparison's aggregate worst-page observations, not
paired measurements of one physical page. Rendering similarity improves from
29.70% to 71.14%, still below the unchanged 80% threshold. Coverage is slightly
worse after adding back the table image than after the anchor fix alone, while
remaining substantially better than the starting point. No whole-document pass
is claimed.

Five more originals were checked: ranks 13 (`tdf135056.odt`), 24
(`FDO76248.docx`), 25 (`floattable-split.docx`), 27 (`fdo114306_2.odt`) and 29
(`ooo96040-2.odt`). Their observed rendering metrics and page counts are unchanged.
In particular, changing the character-anchor classification alone does not
resolve rank 27, despite its character-anchored picture markup. Across these six
inputs the read/text/pixel result stays at 6/18, with only the six reads passing.
The top-ten recheck retains **13/30** passes and identical rendering observations
for all ten inputs.

Validation: Release build, **3,862 executed unit tests pass** (3 additional
skips), and **2,320/2,320 CLI corpus checks pass** (105 documented skips). New
coverage verifies the character/inline distinction and rasterizes an oversized
image in a fixed row both in the body and inside a text frame: pixels above the
row edge remain visible, pixels below it are clipped, and clip commands balance.
The existing exact-row test now also retains and clips the partially visible
second line while still excluding the wholly hidden third line. No corpus
baseline or comparison threshold changed.

Before/after comparisons use an isolated CLI published from `72ab55e`, the same
input bytes, LibreOffice 26.8.1.1, poppler 26.05.0, 72 dpi, unpinned fonts and a
40-page cap. Reports are outside the checkout at
`/workspace/.setup/round3-before-all.json`, `round3-after-final.json` and
`round3-top10-final.json`. Source originals were fetched again from LibreOffice's
public `sw/qa/extras` tree; no upstream document binaries are committed.

## Further case: embedded raster alternatives after unsupported vector images

Rank 28, `ooxmlexport/data/testCustomShapePresetExport.odt`, stores a vector SVM
representation and a PNG representation of each screenshot in the same frame.
The loader previously selected only the first `draw:image`, rejected its format,
and discarded the frame's picture even though a supported alternative was
embedded beside it.

The loader now tries representations in source order until it can carry one.
It preserves the preference for an already supported first representation, the
frame's extent/name/description, package caching, and diagnostics for skipped
representations. Alternatives inside a clickable `draw:a` and flat
`office:binary-data` payloads are included. Nested frames are separate objects
and are not searched. Resource admission still happens once after selection;
a denied representation does not trigger further alternatives. No external
resource is fetched.

The original's screenshot column is restored. Against an isolated CLI published
from `e16fa6b`, with identical original bytes and reference settings:

| Worst-page metric | Before | After |
| --- | ---: | ---: |
| Ink profile similarity | 42.80% | 90.25% |
| Blur difference | 24.51% | 21.07% |
| Coverage delta | 13.18% | 6.27% |
| Ink bounds delta | 17 px | 17 px |

Both renders have six pages. Rendering similarity is now 78.93%, still below the
unchanged 80% threshold; the text comparison also remains open. The native-shape
column still has incorrect inline placement, simplified preset geometry and
missing implicit fill handling. A fill-only change was not retained: painting
those shapes at their currently incorrect positions made the overall comparison
worse. This change claims restored embedded pictures, not complete shape support
or resolution of the whole original case.

Rank 29, `ooo96040-2.odt`, was checked again and retains identical observed
rendering metrics and the existing 16-vs-12 page discrepancy. Across these two
inputs the checks remain 2/6, with only the reads passing. The top-ten recheck
retains 13/30 passing checks and identical rendering observations for all ten
inputs.

Validation: Release build; **3,867 executed unit tests pass** (3 additional
skips); **2,320/2,320 CLI corpus checks pass** (105 documented skips). Five new
regression cases cover unsupported preferred images with PNG alternatives,
clickable wrappers, preference for a supported first image, an external
alternative followed by flat embedded data, and resource denial. No corpus
baseline or comparison threshold was changed.

The local comparisons use LibreOffice 26.8.1.1, poppler 26.05.0, 72 dpi, unpinned
fonts, an 80% threshold and a 40-page cap. Reports outside the checkout:
`/workspace/.setup/round4-before.json`, `round4-verified.json` and
`round4-top10-verified.json`. The original was fetched again from LibreOffice's
public `sw/qa/extras` tree; its binary is not committed.

## Further case: hidden ODT sections contributing visible pages

Rank 27, `uiwriter/data/fdo114306_2.odt`, includes numerous sections explicitly
marked `text:display="none"`. The reader previously imported their paragraphs
and tables as ordinary visible body content, including breaks that created
extra pages.

The model now retains unconditional visibility as `DocumentSection.Hidden`.
The ODT reader resolves nested visibility, keeps the text, and isolates hidden
page breaks and page-style selections from the visible flow. The shared
paginator skips hidden body ranges, objects anchored to hidden paragraphs and
hidden note-label contributions. ODT writing retains the flag and keeps
cell-local sections inside cells instead of duplicating their containing table.
Paragraph edits preserve visibility through the existing range-shifting rule.

The original now has **4 pages vs LibreOffice's 4**, previously 7 vs 4:

| Worst-page observation | Before | After |
| --- | ---: | ---: |
| Ink profile similarity | 0% | 90.24% |
| Ink bounds delta | 731 px | 254 px |
| Coverage delta | 5.08% | 3.57% |
| Blur difference | 13.86% | 26.02% |

The blur metric gets worse as visible content moves onto its correct pages;
this is not an improvement in every visual metric. The pixel comparison remains
below threshold at 73.98% rendering similarity, and the text comparison still
reports the two missing heading strings. Plain text remains unchanged by the
visibility fix: hidden content is preserved for editing and ODT round trips,
not removed from the source model. No whole-document pass is claimed. The
floating-table candidates inspected earlier remain open; no speculative table
spacing change was made.

Conditional section visibility is not evaluated. Sections inside text frames
and running parts lack independent section collections and remain outside this
body-section support. DOCX, RTF, HTML and Markdown writers explicitly report
`*.section.hidden` when exporting retained content as visible paragraphs. The
native PDF layout likewise reports its visibility limitation; the shared raster
paginator supports omission of hidden body sections.

Release build succeeds; **3,881 executed unit tests pass** (3 additional
skips), and **2,320/2,320 CLI corpus checks pass** (105 documented skips).
Regressions cover ODT content/visibility round trips, nested inheritance,
page-break isolation and carry, first-page style selection, table-cell/nested
cell round trips, hidden tables and page-anchored objects, visible sibling cells,
and explicit loss diagnostics in the other writers. No corpus baseline or
comparison threshold changed. The ten highest-impact originals retain 13/30
passes and their prior rendering observations.

Reports outside the checkout are `/workspace/.setup/round5-before.json`,
`round5-verified.json` and `round5-top10.json`. The baseline CLI was published
from `e16fa6b`; the intervening image-alternative fix does not affect this
input's single-representation frames. Runs use identical originals, LibreOffice
26.8.1.1, poppler 26.05.0, 72 dpi, unpinned fonts, the unchanged 80% threshold and
40-page cap. These tool versions differ from the original issue's reference.
