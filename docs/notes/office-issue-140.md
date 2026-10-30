# Targeted fixes for office conformance issue 140

[Issue 140](https://github.com/Broiler-Platform/Broiler.Documents/issues/140)
reports whole-document comparisons. This work fixes specific format losses in
ten of its inputs; it does **not** close the ten whole-document differences.

Local before/after runs: LibreOffice 25.2.3.2, poppler 26.05.0, 72 DPI, 80%
threshold, 40-page cap, unpinned fonts. The issue used a newer LibreOffice, a
different poppler and 144 DPI, so scores are not reproductions of the issue's.

## Samples

Paths are under `sw/qa/extras`.

| Entry | Sample | Format loss addressed | Result |
| --- | --- | --- | --- |
| 1 | `ooxmlexport/data/tdf163894_hidden.docx` | Header tab positions/alignment replaced by the default tab grid | Still differs |
| 3 | `layout/data/tdf142694-1.odt` | Transparent cells hid the table background | 5 vs 8 pages; see below |
| 4 | `ooxmlexport/data/tdf146955.odt` | Explicit tabs in text frames lost | Mixed metrics; see below |
| 11 | `layout/data/ToC-in-linked-frame-on-first-page-style.fodt` | Right TOC tabs and dot leaders lost; page-wide right tabs must fit the narrower frame | 2 vs 5 pages |
| 12 | `ooxmlexport/data/tdf163894.docx` | As entry 1 | Still differs |
| 14 | `uiwriter/data/fdo70807.odt` | Empty legacy background-image element suppressed the cyan page colour | Pixel pass; blur 70.40% → 0.00% |
| 15 | `uiwriter/data/tdf135056.odt` | TOC tabs and leaders lost inside page-anchored frames | Still differs |
| 16 | `uiwriter/data/tdf87922.odt` | Empty legacy background-image element suppressed the black page colour | Pixel pass; blur 71.11% → 3.41% |
| 25 | `layout/data/tdf152106.odt` | Character-anchored frames ignored horizontal page coordinates and page centring | Blur 71.43% → 70.30% |
| 27 | `uiwriter/data/fdo114306_2.odt` | Explicit tabs lost; empty background-image elements gave false bitmap warnings | Still differs |

The selection improves from 14/30 to 16/30 passing checks (all ten reads, four
text checks, two pixel checks).

## What Broiler does now

Tab stops:

- `ParagraphStyle.TabStops` is an immutable, value-comparable set of bounded
  positions with left/centre/right/decimal alignment, decimal characters and
  leaders.
- DOCX reads merge inherited stops and honour position-specific `clear`
  entries. ODT reads replace the inherited list, including an explicit empty
  list.
- DOCX and ODT writers preserve supported tab settings. ODT automatic-style
  keys include child elements, so two different tab lists never share a style.
  Other formats do not serialize tab stops.
- Layout measures the segment after a tab across styled runs, respecting
  paragraph indents, manual breaks and right stops at a narrow frame's edge.
  The painter draws leaders in the reserved gap.
- CLI structural comparisons report a lost tab-stop list instead of treating
  the styles as equal.

Backgrounds and anchors:

- ODT legacy page colours and images paint the content rectangle, leaving
  margins clear. Empty image declarations fall through to the colour. Explicit
  drawing fills keep their own background-size setting.
- Table and row backgrounds show through transparent cells; opaque cell
  backgrounds still win.
- Character and paragraph anchors can use horizontal page coordinates without
  becoming anchors to a numbered physical page.

Authored regression tests cover these behaviours; no upstream binaries are
committed.

## Still open

- Pagination, font, field, table and frame-flow differences remain in most
  samples; the TOC sample stays 2 vs 5 pages, `tdf142694-1.odt` 5 vs 8.
- `tdf142694-1.odt`: painting the formerly missing table background improves
  the ink profile but raises worst-page blur from 53.38% to 59.68% because the
  geometry underneath is still wrong.
- `tdf146955.odt`: blur improves 21.25% → 16.92% while the worst-page ink
  profile worsens; the 40-page cap prevents a whole-document conclusion.
- `tdf152106.odt`: coordinates are corrected but the rest of the layout is not.

None of these entries should be marked fully resolved.

## Reproduction

Place only the selected inputs (keeping relative paths) in an external
directory, build Release, then run (expect a nonzero exit):

```sh
dotnet run --project src/tests/Broiler.Documents.Office -c Release --no-build -- \
  --extras-dir /absolute/path/to/selected-extras \
  --soffice /absolute/path/to/soffice --jobs 3 --dpi 72 --max-pages 40 \
  --report /absolute/path/outside/checkout/issue-140.json --top-differences 10
```
