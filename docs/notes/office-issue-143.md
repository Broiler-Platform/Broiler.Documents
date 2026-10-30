# Issue 143: highest-impact cases, investigation and safe fixes

[Issue 143](https://github.com/Broiler-Platform/Broiler.Documents/issues/143)
ranks Office differences by severity. The top ten were investigated in order,
then further ranks. Each fix below corrects a reproducible defect; **none of the
ten top-ranked originals is fully resolved**. The top-ten comparison moves
from 12/30 to 13/30 passing checks (the DOCX table-break fix) and stays there
through every later round, with unchanged page counts.

Local runs use LibreOffice 26.8.1.1, poppler 26.05.0, 72 DPI, unpinned fonts,
an 80% threshold and a 40-page cap; an extra baseline with LibreOffice 25.2.3.2
exposes reference-version differences. The issue used LibreOffice 26.2.6.3, so
these are local reproductions, not its exact scores. Before runs use isolated
CLIs from `ed822c2`, `72ab55e` and `e16fa6b`. Originals are refetched from
upstream for each round (SHA-256 hashes and reports outside the checkout in
the cloud workspace's `/workspace/.setup`); no upstream binaries are
committed.

## Selected cases

Paths are under LibreOffice's `sw/qa/extras`. Pages are Broiler vs LibreOffice
26.8.1.1.

| Rank | Severity | Sample | Finding and remaining work |
| --- | ---: | --- | --- |
| 1 | 1771.69 | `ooxmlexport/data/tdf163894_hidden.docx` | 5 vs 4. Neither LibreOffice 25.2 nor 26.8 reproduces the issue's 40 reference pages; the renderer is not forced toward that unverified count. |
| 2 | 1343.16 | `layout/data/tdf142694-1.odt` | Nested tables, repeated headers, large inline images; 5 vs 8. The nested-row fix below does not change its render. |
| 3 | 1336.89 | `uiwriter/data/tdf147199.docx` | Inline drawing groups become paragraph-anchored shapes without an inline flow footprint; 5 vs 8. Needs model/layout work. |
| 4 | 1324.92 | `ooxmlexport/data/tdf146955.odt` | Many page-anchored frames, unsupported or missing image/object resources. Both capped at 40 pages, so pagination cannot be judged. |
| 5 | 1321.56 | `tiledrendering/data/page-background-link.fodt` | Background is `http://192.0.2.1:12345/background.png`, not embedded. The reader keeps refusing to fetch document-controlled external resources. |
| 6 | 1318.58 | `layout/data/floattable-center-shift-down.docx` | 4 vs 3 with LibreOffice 25.2, 4 vs 4 with 26.8. The upstream test requires inline tables to move past the floating table; the page count alone does not justify another shift. Visual differences remain. |
| 7 | 1303.92 | `layout/data/fdo43573-2-min.docx` | Two-column section, anchored images, tables; 22 vs 15. Column widths differ by only one twip, so unequal widths do not explain it. Wrapping and section flow open. |
| 8 | 1299.68 | `ww8export/data/tdf91083.odt` | Nested tables, merged cells; 6 vs 7. Render unchanged by the nested-row fix. |
| 9 | 1286.87 | `ooxmlexport/data/tdf141173_missingFrames.rtf` | Positioned paragraphs, a drawing and a page/section break; 1 vs 2, LibreOffice's first page is blank. Broad page-break rewrites were rejected (see below). |
| 10 | 1242.37 | `ooxmlexport/data/tdf127814.docx` | A cell spans 28 rows with `w:textDirection="btLr"`; vertical cell text is not modelled. The table-break fix makes its pixel check pass; text check open. 2 vs 2. |

Further ranks examined:

| Rank | Sample | Outcome |
| --- | --- | --- |
| 13 | `uiwriter/data/tdf135056.odt` | Unchanged |
| 14 | `mailmerge/data/missing-default-line-color.ott` | 2 vs 1 → 1 vs 1 (empty ODT frame) |
| 15 | `ooxmlexport/data/tdf123351_UnderlineGroupSapeText.docx` | Unchanged |
| 16 | `ooxmlexport/data/tdf95033.docx` | 2 vs 3 → 3 vs 3 (table break) |
| 17 | `ww8export/data/fdo66304-1.odt` | Header geometry improved |
| 18 | `tdf81345.docx` | Unchanged, 7 vs 5 |
| 19 | `layout/data/tdf116501.odt` | Header geometry improved |
| 20 | `fdo87488.docx` | Unchanged |
| 23 | `layout/data/tdf152106.odt` | Character anchors and clipped rows improved |
| 24 | `FDO76248.docx` | Unchanged |
| 25 | `floattable-split.docx` | Unchanged |
| 26, 30 | `htmlimport/data/tdf118579.html`, `htmlimport/data/table_border_1px.html` | HTML tables imported |
| 27 | `uiwriter/data/fdo114306_2.odt` | 7 vs 4 → 4 vs 4 (hidden sections) |
| 28 | `ooxmlexport/data/testCustomShapePresetExport.odt` | Screenshots restored |
| 29 | `ooo96040-2.odt` | Unchanged, 16 vs 12 |

## Nested table rows may split when both rows allow it

`ComposeBlocks` made each nested row one indivisible cut piece regardless of
its `CanSplit`, so a long nested row could run off the page. Nested rows now
expose common legal cut boundaries: overlapping atomic pieces from different
cells are merged with a sorted interval sweep, so a cut never slices another
cell's line, kept paragraph or protected block. Minimum heights and vertical
alignment are accounted for; a row that forbids splitting at either level
stays whole. This is a general fix; ranks 2 and 8 still need separate work.

## A final RTF frame keeps its body anchor

A frame after body text can refer to the next body paragraph while the
accumulator has no text for it. At end of input `Build` omitted that empty
paragraph when earlier paragraphs existed, leaving the shape anchored outside
the document. The reader now commits the final anchor before building.

No change to `\page` is retained: independent probes showed a general rewrite
alters unrelated frame and drawing behaviour, and fixing rank 9's page count
while placing its frames on the wrong page is not a valid fix.

## HTML tables (ranks 26 and 30)

The HTML reader treated `table`, `tr`, `td` and `th` as plain blocks. It now
keeps the row/cell grid, empty cells, nested tables, row/column spans,
captions, header-cell emphasis, legacy `border`, `cellpadding`, `width` and
cell backgrounds; paragraphs stay in row-major order. Tables without a stated
grid opt into content-based column sizing (longest unbreakable token,
unwrapped lines, spanning-cell constraints, interpolated within the available
width); explicit widths still apply. The model option defaults off for other
readers and survives paragraph edits. The corpus baseline records three newly
exposed `*.table.flattened` diagnostics: writers report losing the grid the
reader now keeps.

| Input | Profile similarity | Blur | Result |
| --- | --- | --- | --- |
| `tdf118579.html` | 72.53% → 82.23% | 4.78% → 5.08% | Pixel passes; text fails |
| `table_border_1px.html` | 22.75% → 58.96% | 4.89% → 6.16% | Pixel open; read/text pass |

Both stay one page; checks 3/6 → 4/6. Open: separate border spacing, column
declarations, CSS table geometry, vertical alignment. HTML writing still
reports flattening.

## DOCX breaks before a table belong to the table

The builder carried section and trailing run breaks to the next paragraph;
when the next block was a table, its first cell consumed the flag, but table
layout ignores a cell paragraph's break. The table now takes a pending block
break before its cells are read, as `PageBreakBefore` or `ColumnBreakBefore`.
Empty malformed tables do not consume it; breaks inside cells keep their
existing handling and diagnostics.

- Rank 16 `tdf95033.docx`: 2 → 3 pages vs 3, correct landscape/portrait
  sections; similarity 76.95% (pixel still below 80%, text passes).
- Rank 10 `tdf127814.docx`: worst-page profile 0.96% → 87.63%, blur 0.18%;
  pixel passes, text fails (vertical merged-cell text).

## Empty positioned ODT text frames stay frames

A text frame with neither paint nor text fell through to the body reader and
emitted an empty body paragraph. It is now a positioned frame with its anchor
and wrap properties. A trailing paragraph-anchored frame still gets an empty
body anchor when needed; a page-anchored one does not. A frame holding an
unrepresented nested drawing is not empty and keeps its fallback path.

In rank 14 `missing-default-line-color.ott` the spurious paragraph preceded the
first named page style and added a page: now 1 vs 1 (was 2 vs 1). Profile 8.45%
→ 38.37%, blur 2.33% → 4.31%; pixel and text checks remain open.

## Character-anchored pictures and partly visible table lines (rank 23)

`tdf152106.odt` exposes two defects:

- `text:anchor-type="char"` was treated as inline like `as-char`. Only
  `as-char` is inline; `char` floats, so pictures take the floating-picture
  path and diagnostic with page-relative position, size and wrap. Missing
  anchor types keep the old fallback. The model still uses a paragraph anchor
  rather than the exact glyph for character-relative offsets.
- A fixed-height row discarded any line extending below its bottom, including
  an image slightly taller than the row. Partly visible lines now carry a
  visible height and are clipped when painted; wholly hidden lines are still
  omitted. Paragraph shading and a containing text frame use the same visible
  extent. Applies in body tables and tables inside frames.

The full-page background picture, formerly shrunk to column width, now covers
the page, and the image in the fixed row reappears. Four pages both sides.

| Worst-page metric | Before (`72ab55e`) | Anchor fix | Both fixes |
| --- | ---: | ---: | ---: |
| Blur | 70.30% | 32.54% | 28.86% |
| Ink profile | 87.51% | 96.34% | 99.51% |
| Ink bounds delta | 81 px | 6 px | 6 px |
| Coverage delta | 15.25% | 0.27% | 2.35% |

Worst page moves from 4 to 1, so the columns are not one physical page.
Similarity 29.70% → 71.14%, below threshold; fonts and text flow still differ.
The character-anchor change alone does not resolve rank 27 despite its
character-anchored picture markup.

## Raster alternatives after unsupported vector images (rank 28)

`testCustomShapePresetExport.odt` stores an SVM and a PNG for each screenshot
in one frame. The loader took only the first `draw:image`, rejected SVM and
dropped the picture. It now tries representations in source order until one
is carried, keeping preference for a supported first one, the frame's
extent/name/description, package caching and diagnostics for skipped ones.
Alternatives inside a clickable `draw:a` and flat `office:binary-data` count;
nested frames are separate objects. Resource admission happens once after
selection, and a denied representation does not try further alternatives.
Nothing external is fetched.

Six pages both sides; profile 42.80% → 90.25%, blur 24.51% → 21.07%, coverage
13.18% → 6.27%; similarity 78.93%, still below 80%; text open. The native-shape
column still has wrong inline placement, simplified preset geometry and no
implicit fill. A fill-only change was rejected: painting the shapes at their
wrong positions made the comparison worse.

## Hidden ODT sections (rank 27)

`fdo114306_2.odt` has many sections with `text:display="none"`, imported as
visible content whose breaks created pages. The model keeps unconditional
visibility as `DocumentSection.Hidden`. The ODT reader resolves nested
visibility, keeps the text, and isolates hidden page breaks and page-style
selections from the visible flow. The shared paginator skips hidden body
ranges, objects anchored to hidden paragraphs and hidden note labels. ODT
writing keeps the flag and keeps cell-local sections inside cells instead of
duplicating their table. Paragraph edits keep visibility via range shifting.

Now 4 vs 4 pages (was 7 vs 4); profile 0% → 90.24%, ink bounds 731 → 254 px,
coverage 5.08% → 3.57%, blur 13.86% → 26.02% as content moves to its correct
pages. Similarity 73.98%; the text check still reports two missing heading
strings. Plain text is unchanged: hidden content stays in the model for
editing and round trips. No speculative table spacing change was made for
the floating-table candidates.

Not covered: conditional visibility; sections in text frames and running
parts (no section collections there). DOCX, RTF, HTML and Markdown writers
report `*.section.hidden` when exporting hidden content as visible; native
PDF layout reports its limitation.

## ODT header bands and header distance (ranks 17 and 19)

`tdf116501.odt` and `fdo66304-1.odt` had the header's outer margin and
reserved band conflated with the body's top margin, so header text sat at the
page edge. The reader now separates the outer top margin (`HeaderDistance`)
from the minimum body offset below the declared header area (`MarginTop`);
the writer reverses this via `style:header-style`, keeping explicit header
positions on round trip. The first master-page lookup also skips paragraphs
inside positioned drawings, so a frame's text style does not select the body's
first page (authored regression only).

| Original | Profile | Blur | Pages |
| --- | --- | --- | --- |
| `tdf116501.odt` | 57.53% → 62.04% | 23.83% → 21.86% | 2 vs 2 |
| `fdo66304-1.odt` | 64.44% → 68.28% | 6.42% → 5.91% | 2 vs 2 |

`tdf116501.odt` ink bounds 20 → 8 px; pixel and text checks open for both.
Footer band conversion, header padding and fixed-height header clipping are
outside this change.

Reference probe: an ODF source with outer top margin 18pt and minimum header
band 54pt, exported by LibreOffice, puts HEADER at 18.508449pt and BODY at
72.508449pt (`pdftotext -bbox`); a page-content-anchored 10pt rectangle starts
at (30, 72) in LibreOffice and the raster layout. A 10pt header bottom gap
leaves the body coordinate unchanged, so the gap is not added twice.

## Known visual tradeoffs in the top ten

- `tdf142694-1.odt`: after the header change blur improves 59.68% → 55.77% but
  profile drops 12.74% → 8.50%; still 5 vs 8 pages.
- **`tdf146955.odt` gets visually worse in the capped comparison** after the
  header change: blur 16.94% → 28.27%, profile 6.17% → 4.29%, coverage 6.32% →
  8.89%, ink bounds 501 → 542 px. The independently verified header mapping is
  kept; the frame/layout discrepancy remains unresolved and is not pagination
  evidence.

## Still open

- Inline drawing-group footprint (`tdf147199.docx`), nested table pagination
  (`tdf142694-1.odt`, `tdf91083.odt`), two-column flow
  (`fdo43573-2-min.docx`), framed RTF content with a blank first page
  (`tdf141173_missingFrames.rtf`), vertical cell text (`tdf127814.docx`).
- External backgrounds are never fetched (`page-background-link.fodt`).
- Native custom-shape geometry and fills (`testCustomShapePresetExport.odt`).
- Conditional section visibility; CSS table layout; footer bands.

## Reproduction

```sh
dotnet run --project src/tests/Broiler.Documents.Office -c Release --no-build -- \
  --extras-dir /path/to/issue143 \
  --soffice /path/to/soffice26 --jobs 3 --dpi 72 --max-pages 40 \
  --top-differences 10 --report /path/outside/checkout/office143.json
```

The comparison still fails overall.
