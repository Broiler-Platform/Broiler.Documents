# Issue 135: section paper, pagination controls and table layout

Investigations of
[issue 135](https://github.com/Broiler-Platform/Broiler.Documents/issues/135),
whose report covers 3,901 upstream samples. Five batches each picked reusable
missing behaviors from the ranked samples. Unless stated otherwise, samples
were compared locally on Windows with LibreOffice 26.2.6.3 and Poppler 26.07.0
at 72 DPI, one job, a 40-page comparison cap and unpinned fonts. These are
local observations, not Linux CI results; no upstream bytes were committed.
None of the batches claims that a ranked document is fixed in full.

## Section paper, row heights and text projection

Changes:

- The shared pagination engine switches paper size when a section opens a
  page, keeps it on continuation pages and restores the document paper
  afterwards (before, every page used the last section's paper; only section
  margins were applied). Explicit caller paper overrides and continuous output
  are unchanged. Affects raster output, preview and printing; the PDF codec
  still reports its section-layout limitation.
- Both pagination engines apply `TableRow.MinHeight` (readers already kept
  it). A row whose floor does not fit below the preceding content moves to a
  fresh page. Taller content still grows; exact-height clipping remains
  unsupported and diagnosed.
- Plain-text export puts tabs between cells and newlines between rows, keeping
  paragraph boundaries inside cells and nested tables. Image object
  replacement markers contribute no text, as in LibreOffice; literal U+FFFC
  stays text. ODF index titles are read, keeping headings such as `Inhalt` in
  `tdf152307.odt`. Comparison does not ignore arbitrary whitespace; missing
  pictures remain the pixel axis's job.

LibreOffice exports an authored DOCX with a multi-paragraph first cell as
`before\nA1\nA2\tB\nC\tD\nafter`, matching the CLI apart from the final
newline.

| Sample | Observation |
| --- | --- |
| `layout/data/tdf123163-1.docx` | Worst page 93.50% → 73.57% (page 2 → 3); 11 vs 16 pages. Missing pictures, unequal columns, exact row clipping remain. |
| `uiwriter/data/tdf170672.docx` | 14.24% → 12.85%, but 33 → 31 pages vs 40 measured; composite severity rises. Floating-table document, not fixed. |
| `ooxmlexport/data/tdf163894_hidden.docx` | Unchanged: 4 vs 40 pages. Running header uses STYLEREF; the CLI keeps cached text. A LibreOffice DOCX round trip gives 67 pages, so 40 is the cap. |
| `layout/data/tdf152307.odt` | Text check reached the 21-difference cap before; now passes. 15 pages in both. |
| `ooxmlexport/data/ooo96040-2.odt` | Real text differences remain (cached date formatted differently by LibreOffice); 16 vs 13 pages. |

## Paragraph pagination

Keep with next (`w:keepNext` / `fo:keep-with-next`), keep lines together
(`w:keepLines` / `fo:keep-together`) and widow/orphan control
(`w:widowControl` / `fo:widows`, `fo:orphans`) occur in the styles of
`tdf95033.docx`, `tdf83309.docx`, `tdf147199.docx`, `tdf142694-1.odt` and
`tdf91083.odt`.

- `ParagraphStyle` holds the keep flags and nullable widow/orphan counts: null
  means no explicit policy, zero disables. DOCX and ODT readers apply them
  through style chains, including off/auto/zero overrides; writers preserve
  them. DOCX reports custom ODF counts it can only reduce to its two-line
  on/off control. Unspecified counts keep the renderer's existing policy.
  Other codecs do not preserve these properties yet.
- The shared engine and the PDF writer honor the rules in body text. A keep
  chain that fits on a fresh page or column moves as a group; a heading can
  stay with the start of a paragraph longer than a page. Oversized
  keep-together paragraphs and impossible counts relax instead of producing
  blank pages. Explicit breaks win; continuous output ignores the rules.
- Lookahead snapshots list counters; paragraph-anchored wrapping shapes are
  re-anchored when a paragraph moves. Multi-column sections with explicit
  constraints are left unbalanced rather than splitting a kept paragraph.
- The shared engine groups cut positions inside table cells too; the PDF
  writer places whole rows. Keep chains stop at section changes, explicit
  breaks and table boundaries.

LibreOffice and the PDF codec produce identical page-by-page text for four
authored DOCX cases (80-point area, exact 10-point lines, 5.5-point spacing).
The five samples above showed **no change** in pages, worst-page bands or check
outcomes: the rules are implemented, not a demonstrated score reduction.

References:
[keepNext](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.keepnext),
[widowControl](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.widowcontrol),
[Word line and page breaks](https://support.microsoft.com/en-us/word/line-and-page-breaks).

## Page images and table flow

- **Page-anchored ODT frames** beyond the right or bottom paper edge are
  brought back: negative horizontal offsets clamp to the page, negative
  vertical offsets keep their bleed above it. Paragraph anchors and drawing
  objects keep their offsets. Fixes the blank output of
  [`odfimport/data/tdf116195.odt`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/odfimport/data/tdf116195.odt).
  LibreOffice exports of four frame variants confirmed the rule, including
  negative Y staying negative.
- **Table keep-with-next:** ODT table styles' `fo:keep-with-next` (also
  inherited) is preserved through edits and ODT writes. Both engines move an
  inline body table with the directly following body paragraph in the same
  section when the group fits a fresh page/column; oversized tables still
  split, explicit breaks win. No chaining of adjacent tables, no effect inside
  cells or on floating tables. DOCX has no such property: writing one reports
  `docx.table.keepnext`. Targets
  [`ww8export/data/tdf91083.odt`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ww8export/data/tdf91083.odt).
- **Cell padding:** separate left/top/right/bottom insets, read from ODT
  uniform/individual padding and named styles and from DOCX table/style
  margins and direct overrides; both writers preserve them. Both engines
  place text and nested tables inside them and add vertical padding to row
  height. Absent padding keeps the table-horizontal/zero-vertical fallback.
  Relative spacing matches LibreOffice; absolute glyph bounds differ through
  font metrics.

| Sample | Before | After |
| --- | --- | --- |
| `tdf116195.odt` | Blank; 70.60% | Picture visible; 0.48%; **passes pixel check** |
| `tdf91083.odt` | 6 vs 7 pages; 26.40% | 6 vs 7; 22.95%; fails |
| `tdf142694-1.odt` | 7 vs 7 pages; 47.12% | **5 vs 7** (regression); 46.30% |

## Inline table geometry

- **Preferred widths:** DOCX `tblW` in points or fiftieths of a percent
  (including Strict's percent spelling), ODT `style:width` /
  `style:rel-width`. Percentages refer to the containing text area, including
  nested cells. An explicit width scales the grid either way; unspecified
  columns share the remaining space; auto/nil cancels inherited widths.
  Bounded by the containing area, this is not Word's automatic fitting.
- **Horizontal placement:** DOCX `jc` and `tblInd`, ODT alignment and left
  margin. Inline tables can be centered, right aligned or indented; repeated
  headers keep width and alignment.
- **Cell vertical alignment:** DOCX `vAlign` (style inheritance and direct
  overrides), ODT `style:vertical-align`. Both engines align within the space
  left after padding; merged cells use the combined row height; split-row
  fragments are aligned when placed.

Both codecs write the geometry. The ODT writer adds a physical grid-width
fallback so aligned/indented tables do not stretch on import. DOCX cell
properties are written borders, shading, margins, vertical alignment. The
DOCX style diagnostic no longer reports this geometry. Floating tables are
unchanged.

LibreOffice positions agree with the PDF codec up to a ~0.1-point border
offset (e.g. centered 100-point table in a 180-point area: text X 50.1/90.1 vs
50/90); vertical alignment offsets match exactly.

| Sample | Before | After |
| --- | --- | --- |
| `layout/data/tdf150606-1-min.odt` | 45.57%, worst page 1 | 7.96%, worst page 5; ink profile 0.996 → 0.490, severity 787.77 → 804.96 |
| `ooxmlexport/data/tdf127814.docx` | 16.43% | 16.33%; severity rises slightly |
| `layout/data/tdf142694-1.odt` | 5 vs 7; 46.30% | 5 vs 7; 46.21% |

Unchanged: `ooxmlexport/data/FDO76312.docx` (2 vs 2),
`odfimport/data/tdf103025.odt` (3 vs 5), `odfimport/data/tdf165156.odt`
(2 vs 3), `layout/data/tdf122607.odt` (3 vs 3). All seven pixel checks still
fail (12/21 checks pass before and after).

## Empty cells, cell spacing and table breaks

- **Empty ODT cells** (`table:table-cell` without a paragraph) get one empty
  paragraph through the normal reader path, keeping font defaults, limits and
  line height; covered cells do not. Before, empty tables could vanish and
  spacer rows collapse. `tdf103025.odt` has 216 such cells. PDF row
  pagination now checks occupied space rather than drawn glyphs, so pages of
  only empty rows no longer push later text off the page.
- **`SpacingBefore` on the first cell paragraph** is applied inside the
  padding and counted in row height by both engines (it was dropped,
  including in nested flows). LibreOffice moves cell text down exactly 7
  points for 7 points of spacing, even when the table opens the page.
- **ODT table breaks:** the reader keeps a table's `fo:break-before`, moves a
  preceding block's `fo:break-after` onto the table and a table's trailing
  break onto the next block. The model and ODT writer preserve them. The
  shared paginator handles page/column breaks, ignores them in continuous
  output and avoids a blank page for a break on the opening table; the PDF
  writer treats column breaks as page breaks. DOCX has no table-level break:
  writing one reports `docx.table.break`. Breaks before nested tables are not
  laid out.

| Sample | Before | After |
| --- | --- | --- |
| `tdf103025.odt` | 3 vs 5; 1.58%; profile 0.548 | 6 vs 5 (overshoots); 1.38%; profile 0.390 |
| `tdf150606-1-min.odt` | profile 0.490 | profile 0.612; 7.99% |
| `tdf165156.odt` | profile 0.232 | profile 0.178; 0.54% |

Still 12/21 checks and no pixel passes. Inherited row/column styles were
probed as a cause for `tdf103025.odt` and did not change it; no such change was
kept.

## Still open

- `tdf142694-1.odt` page-count regression (5 vs 7) since the cell-padding
  change; its styles did not reveal a fix.
- Dynamic header fields (STYLEREF), unequal section columns, floating-table
  differences, exact-row clipping, merged-row height allocation, automatic
  table fitting/minimum-content sizing, default-font differences, nested
  table breaks.
- The FODT background-link sample: LibreOffice paints a blue
  unavailable-image fallback that Broiler left unpainted. Resolved in
  [issue 178](office-issue-178.md).
- A full Linux corpus rerun to measure the aggregate effect.
