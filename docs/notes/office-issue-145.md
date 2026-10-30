# Issue 145: ten highest-impact cases

[Issue 145](https://github.com/Broiler-Platform/Broiler.Documents/issues/145)
ranks Office differences by severity. The top ten (nine overlap #143's top
ten) were rerun from the public originals, then ranks 16–30. The fixes below
correct verified defects; **no top-ten visual failure is resolved**. Two
further-ranked originals now pass all checks: `tdf126533_pageBitmap.docx` and
`fill-image-name-missing-definition.odt`.

Local runs use LibreOffice 26.8.1.1, poppler 26.05.0, Linux, 72 DPI, unpinned
fonts, an unchanged 80% threshold and a 40-page cap. The issue used
LibreOffice 26.2.6.3, poppler 26.01.0 and 144 DPI, so these are local
comparisons. Pages are Broiler / LibreOffice; similarity is the runner's
rendering score, not a conformance percentage. The isolated CLI from
`146fe49` is the before baseline for later rounds. No thresholds, baselines
or fidelity assertions are relaxed. Reports, logs, probe sources and rejected
patches (`issue145-tail-reflow-rejected.patch`,
`issue145-pagex-prototype.patch`) were kept outside the checkout in the cloud
workspace's `/workspace/.setup`.

## Local results

Paths are under `sw/qa/extras`. The top ten stay at **12/30** checks
(10 reads, 2 text checks, no pixel check) throughout; the runner exits with
the number of failed checks (18 for the top ten).

| Rank | Severity | Sample | Pages | Similarity | Remaining work |
| --- | ---: | --- | --- | ---: | --- |
| 1 | 1771.69 | `ooxmlexport/data/tdf163894_hidden.docx` | 5 / 4 | 22.64% | The reported 40 reference pages are not reproduced; hidden-text handling is not retuned to that count. |
| 2 | 1367.46 | `layout/data/tdf142694-1.odt` | 5 / 8 | 8.5% | Nested tables, repeated headers, image flow (see [#143](office-issue-143.md)). |
| 3 | 1336.89 | `uiwriter/data/tdf147199.docx` | 5 / 8 | 0% | Inline drawing groups need an inline flow footprint. |
| 4 | 1321.56 | `tiledrendering/data/page-background-link.fodt` | 1 / 1 | 0.11% | External background URL at 192.0.2.1 is not fetched. |
| 5 | 1318.58 | `layout/data/floattable-center-shift-down.docx` | 4 / 4 | 66.15% | Floating-table avoidance and text geometry. |
| 6 | 1303.92 | `layout/data/fdo43573-2-min.docx` | 22 / 15 | 52.76% | Two-column flow, anchored images, tables; column widths differ by one twip only. |
| 7 | 1299.68 | `ww8export/data/tdf91083.odt` | 6 / 7 | 20.97% | Nested tables, merged cells, row pagination. |
| 8 | 1286.87 | `ooxmlexport/data/tdf141173_missingFrames.rtf` | 1 / 2 | 0% | LibreOffice has a blank first page; broad frame/page-break rewrites failed independent probes. |
| 9 | 1279.43 | `ooxmlexport/data/tdf146955.odt` | 40 / 40 | 0% | Complex frames/resources; the #143 header-geometry regression; both capped. |
| 10 | 1235.92 | `layout/data/ToC-in-linked-frame-on-first-page-style.fodt` | 2 / 5 | 61.35% | Index-source diagnostic corrected; unstyled headings paginate differently. |

Further ranks:

| Rank | Sample | Outcome |
| --- | --- | --- |
| 16 | `ooxmlexport/data/tdf132149_pgBreak.odt` | 6 / 5 → 5 / 5; similarity 68.51% → 69.26%, pixel open |
| 19 | `layout/data/tdf170381-split-float-table-in-normal-table.docx` | 1 / 2, 0.51%; open |
| 23 | `tdf126533_pageBitmap.docx` | 2/3 → **3/3** |
| 24 | `layout/data/tdf123163-1.docx` | 16 / 16, 14.98%; drawing order and lines fixed, open |
| 25 | `tdf135623.docx` | 1 / 2; open |
| 26 | `tdf87569_vml.docx` | Pixel passes; text open |
| 27 | `fdo78883.docx` | Text open |
| 28 | `odfimport/data/fill-image-name-missing-definition.odt` | 0.18% → **3/3** |
| 30 | `pageBreakInHiddenSection.fodt` | 2 / 3 with a page-geometry difference; open |

Ranks 24–27 start at 5/12 checks (all read, the VML sample passes pixels,
all four text checks fail). Across all 30 originals the score is **42/89**
(47 failures, one skip: LibreOffice produces no PDF for
`uiwriter/data/tdf170672.docx`), unchanged by the later rounds.

## Cached indexes and generation instructions (rank 10)

`ToC-in-linked-frame-on-first-page-style.fodt` has an index inside a linked
frame. `text:table-of-content-source` holds generation rules and templates;
`text:index-body` holds the cached entries. The reader reported the source as
an unsupported skipped block. It now skips only the matching source element in
each of the seven supported index containers and reads the cached title and
entries; unknown index children still warn. It does not regenerate indexes,
refresh page-number fields or import template text. This is a diagnostic
correction only; check details are unchanged.

A LibreOffice FODT rewrite of the original gives the unstyled body `text:h`
elements an application-generated `Heading_20_1` style (18pt bold, 12pt before,
6pt after, inherited from `Heading`) that the source does not contain; Broiler
reads them at the declared default 12pt. That explains part of the pagination
difference, but injecting LibreOffice-specific heading metrics into every ODF
read is not justified. Open.

## Embedded DOCX page-background images (rank 23)

`tdf126533_pageBitmap.docx` references an embedded JPEG from
`v:background/v:fill` with `type="frame"`. The reader replaced picture fills
with a solid colour; it now loads embedded `frame` and `tile` page-background
fills through the DOCX image loader into an `ImageFill`. `frame` stretches over
the paper; `tile` uses the image's intrinsic size. Relationship resolution,
the per-image size limit, raster-format checks and resource-policy admission
apply; external pictures are not fetched. Missing, unsupported or denied
images keep the colour fallback with diagnostics; a disabled background or
fill does not load the image. VML-specific sizing, offsets, aspect modes and
transparency stay on the colour fallback with a warning. DOCX export still
approximates bitmap fills with `docx.fill.extended`.

Result: one page each, blur 42.01% → 0, profile 0 → 99.992%, coverage delta
0.012%, one-pixel ink-bounds difference.

## Missing ODT bitmap definitions use the declared colour (rank 28)

`fill-image-name-missing-definition.odt` names an absent bitmap but declares
`draw:fill-color="#99ccff"`; LibreOffice paints that blue, the reader left the
page white. An unresolved bitmap fill now uses a valid explicit
`draw:fill-color` and still emits `odt.fill`; without one it keeps no fill.
Resolvable images win; opacity and `draw:background-size` apply to the
fallback; no implicit colour defaults. Hatch behaviour is unchanged. Blur
99.82% → 0.17%, profile 25.68% → 99.99999%, ink bounds still 5 px.

## Page-style sequences update the text width (rank 16)

For `tdf132149_pgBreak.odt` two defects were isolated with authored cases:

- Advancing an automatic page-style sequence updated paper and margins but
  kept the old text-column width. Columns are now refreshed after the
  destination style is selected.
- A paragraph whose first line overflowed to another page/column kept its old
  line composition. A paragraph with no placed lines is now measured against
  the destination page, recalculating line heights and widow/orphan pieces.

Now 5 / 5 pages; worst-page blur 13.62% → 14.59%. A frame already placed on the
preceding page stays there when its anchor text continues (existing
oversized-frame regression).

Rejected: a prototype that carried source offsets through text pieces and
long-token splitting to re-compose only the unplaced tail of a continuing
paragraph regressed the original to 6 / 5 pages and 39.51% similarity (blur
23.81%, geometry mismatch). It was fully reverted; rollback confirms 5 / 5,
69.26%, 14.59% blur. Reflow of an already-started paragraph across different
widths, and tables spanning different paper widths, remain open.

## Signed VML drawing order (rank 24)

In `tdf123163-1.docx` the VML reader reversed negative `z-index` values:
`Math.Abs()` gave a deeply negative background a higher model order than
foreground objects in the same behind-text layer. Negative levels now map
monotonically from `int.MinValue..-1` into `0..int.MaxValue`, keeping
`BehindText=true`; nonnegative levels are unchanged.

Probe `vml-negative-overlap.docx`: red at -1, blue at -2, red first in XML. At
(50,50) LibreOffice and the fixed renderer show red `(255,0,0)`; the old one
showed blue.

The original stays 16 / 16 pages at 14.98%; revealed text moves blur 47.84% →
49.52% while profile rises 99.05% → 99.90%. The grey panel is still
horizontally misplaced; fonts, unequal columns and shape geometry are open.

Rejected: converting VML offsets with the current section's margins, and a
page-relative-X model prototype; both changed the original to 15 vs 16 pages
without a pass and were reverted. Correct page-relative placement must use the
actual page owner across continuous sections, not another static margin.

## DOCX text-frame height growth

The reader ignored explicit auto-fit in DrawingML `wps:bodyPr/a:spAutoFit` and
VML `v:textbox` CSS `mso-fit-shape-to-text:t`. Both now set `GrowsToText`: the
stated height is a floor and the frame grows to its laid-out content. The
DOCX writer emits `a:spAutoFit` for growing frames. No width auto-sizing or
font shrinking; `a:normAutofit` is not supported. Probe
`docx-autofit-probe.docx` (two-line green VML frame, five points high): at
(22,30) LibreOffice and the fixed renderer paint green, the old one white.

Over all 30 originals only `tdf135623.docx` changes: similarity 65.14% →
64.75%, blur 34.86% → 35.25%, better ink profile, still 1 vs 2 pages.

## Nested floating tables and indexes (ranks 19 and 12)

`tdf170381-split-float-table-in-normal-table.docx` has a 13-row floating table
with `w:tblpPr` offsets inside a four-row ordinary table. The reader reports
`docx.table.floating` for cell-nested floating positions and lays the table
out inside its cell. Keeping the offset alone would not implement overflow
beyond the cell; no guessed page break was added.

`tdf135056.odt` reports zero unsupported blocks; its cached index text still
differs from LibreOffice's regenerated one, which is not a reason to discard
or invent model text.

## Ordinary RTF section boundaries

`\sect` only stopped later section geometry from overwriting the first
section's paper; `first\sect\sectd\sbknone second` imported as `firstsecond`
and a next-page section had no page break. Now:

- `\sect` closes pending ordinary body text; the new section's break is
  deferred until its descriptors are known.
- A section with following body content defaults to a page break; `\sbknone`
  keeps a paragraph boundary on the page; `\sbkcol` is a column break (a page
  in one-column documents); `\sectd` resets the mode.
- Odd/even sections keep a page break with `rtf.section.parity`, because a
  parity page is not represented.
- Repeated sections keep empty intermediate sections; a bare terminal `\sect`
  keeps the final empty paragraph without a page; an explicitly described
  final page section keeps its break; a preceding `\par` adds no extra
  paragraph.

Framed-paragraph and in-table section handling is unchanged; later section
paper/margin changes are still not independent sections. Eight authored
inputs checked against LibreOffice FODT/PDF exports improve from 16/24 to
23/24: `continuous.rtf`, `empty-descriptor.rtf`, `initial-empty.rtf`,
`next-page.rtf` and `repeat.rtf` now pass; `page-sect.rtf` keeps a pixel
failure (75.52%, two pages each). `tdf141173_missingFrames.rtf` is unchanged.

## Simple VML lines and cell-relative placement

The DOCX reader imports `v:line` endpoints and two-point comma-separated
`v:polyline` coordinates as line geometry. Endpoints take precedence over CSS
margins (as LibreOffice FODT exports show); disabled strokes draw nothing.
Rotated lines, longer polylines, malformed coordinates and unsupported scaling
are diagnosed, not guessed; non-solid dashes get a diagnosed solid
approximation. No general VML paths.

Lines anchored to a table cell's text area keep that horizontal reference in
the model and raster paginator: the origin includes cell padding and excludes
paragraph indentation. DOCX and ODT round trips keep it; PDF and RTF writers
diagnose it. The ODT writer omitted inherited cell padding (the reader's
fallback hid this in codec round trips), putting a native export at x=140 px
instead of 145; it now writes all four effective padding values, including
explicit zero and per-cell overrides, matching the source's x=145..204 px.
Eleven authored probes improve from 23/33 to 33/33.

Originals: `FDO76248.docx` imports its missing polyline but stays 4 / 3 pages,
similarity 62.16% → 62.14%; table pagination and fonts open.
`tdf123163-1.docx` imports ten previously ignored lines (two dashed
approximations diagnosed), still 16 / 16 at 14.98%; its worst page moves from
3 to 16.

## Embedded pictures in mixed DrawingML groups

The group reader kept drawn boxes and text frames but dropped every `pic:pic`
sibling. It now loads embedded pictures in mixed groups through the DOCX media
loader and resource admission, maps each transform through nested group
coordinate spaces, and keeps child order, anchor offsets, stacking,
crop/mask, alt text and name; the picture uses its mapped child size.
Missing, linked-external, denied or oversized images are diagnosed with no
inline fallback; per-picture rotation/flips and invalid extents are diagnosed
and skipped. Group-level rotation/flips and inline group footprints are open.

Top-level picture-only groups keep the diagnosed first-picture-at-group-size
approximation: when declared child-space bounds exceed the picture bounds,
LibreOffice normalizes the outer box differently, and extending the mixed
mapping dropped authored checks from 12/12 to 9/12. Four mixed-group probes
improve from 8/12 to 12/12; image bounds match within one edge pixel, the
cropped probe exactly at `(60,60)..(159,109)`. No ranked original contains
this pattern.

## Still open

- Inline drawing-group footprint and SmartArt (`tdf147199.docx`).
- Cell-nested floating-table overflow (`tdf170381-...`).
- Continuation reflow across page widths (`tdf132149_pgBreak.odt`).
- Page-relative VML geometry across continuous sections, unequal columns
  (`tdf123163-1.docx`).
- Hidden-section break semantics (`pageBreakInHiddenSection.fodt`).
- Application-generated heading metrics (rank 10).

## Reproduction

```sh
dotnet run --project src/tests/Broiler.Documents.Office -c Release --no-build -- \
  --extras-dir /path/to/issue145 --soffice /path/to/soffice26 \
  --jobs 3 --dpi 72 --max-pages 40 --lo-timeout 30 --top-differences 10 \
  --report /path/outside/checkout/issue145-after.json \
  --workspace /path/outside/checkout/issue145-after-work --keep
```
