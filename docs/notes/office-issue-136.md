# Issue 136: lists, symbols, RTF pages, indents, section colours and frames

Investigations of
[issue 136](https://github.com/Broiler-Platform/Broiler.Documents/issues/136)
(fetched 2026-10-22; 3,878 documents, 30 ranked differences). Three batches
each fixed five shared defects behind ranked samples. Samples were downloaded
unchanged from LibreOffice's GitHub repository and compared locally on Windows
with LibreOffice 26.2.6.3 and Poppler 26.07.0, unpinned fonts, 72 DPI, one job
and the default 80% threshold; this is not the Linux corpus run at 144 DPI. No
upstream bytes or generated renders were committed. Five corrected mechanisms
per batch do not mean five conforming documents; the table/image failures at
the top of the issue remain.

## Lists, symbols and default RTF pages

- **Deleted ODT headings keep outline numbering.** The tracked-deletion reader
  ignored `text:outline-level`; both paths now share the outline resolution.
  Restores headings in `tdf130680.odt` (rank 11) and some labels in
  `fdo66304-1.odt` (rank 20).
- **Only the first paragraph of an ODT list item gets a label**; following
  paragraphs keep the indent without consuming a number. Nested items have
  their own state; applies to deleted content too.
- **Text export includes fallback list labels** for lists without a named
  definition (rendering already drew the bullet). Fixes `tdf163230.fodt` and
  `tdf163285.fodt` (ranks 21/30).
- **DOCX `w:sym`** keeps its character and symbol font with adjacent run
  formatting; invalid hex codes are diagnosed. It was skipped, losing 30
  checkboxes in `tdf123705.docx` (rank 17).
- **RTF without paper dimensions** gets LibreOffice's default: Letter (612 ×
  792 pt) with 72-point margins (confirmed by an RTF → FODT export); stated
  page/section properties override. Fixes geometry for `fdo45183.rtf` and
  `tdf81943.rtf` (ranks 16/28).

Decision: DOCX counter identity is unchanged. LibreOffice exports confirm that
numbering instances sharing an abstract definition count together; making
instances independent would regress that. `mixednumberings.docx` stays a
failing control.

Results: text now passes for `tdf130680.odt`, `tdf163230.fodt`,
`tdf163285.fodt` and `tdf123705.docx`; `fdo66304-1.odt` still fails on complex
deletion/list differences. The RTF samples match geometry (43.73% → 42.58%
and 13.67% → 13.31% blurred difference). Checks 10/24 → 14/24; all pixel
checks still fail and page counts are unchanged. Retaining symbols does not
guarantee correct glyphs under Windows font fallback.

## Section colours and exact indents

- **Section background paint:** ODT section colours were dropped unless the
  section had columns. The model keeps the colour, the shared paginator paints
  its text area across page breaks and the ODT writer exports it.
- **Nested section colours:** closing an outer section discarded the inner
  one. The reader resolves non-overlapping ranges using the innermost opaque
  colour, resolves section style inheritance and merges equivalent adjacent
  ranges; outer-column behaviour is kept.
- **Exact left indents:** DOCX and ODT paragraphs keep point values rather
  than rounded 18-point levels; explicit zero cancels a parent indent;
  negative offsets are allowed.
- **Right indents** reduce wrapping/alignment width, including in cells, and
  survive DOCX/ODT non-list round trips.
- **First-line and hanging indents:** the first line gets its own offset and
  width. DOCX list labels use the resolved hanging position, with style and
  direct attributes overriding numbering-level attributes individually; the
  PDF layout places the marker separately to keep the gap.

Exact values appear in CLI JSON dumps and style comparisons. Editing a legacy
indent level clears the exact values. Positions match LibreOffice within its
0.10-point exporter offset (e.g. 7.5-point indent, ±5.25 first line: LO x =
22.85/12.35, body 17.60; Broiler 22.75/12.25, 17.50; DOCX list marker 28.10
vs 28, body 46.10 vs 46). Font metrics and some wrapping still differ.

Explicit limits: ODT list levels use the existing list layout and writing
exact per-paragraph list offsets reports `odt.list.indent`; the standalone
PDF layout reports unsupported section colours; DOCX reports
`docx.section.background` when a section colour cannot be represented.

| Sample | Before | After |
| --- | --- | --- |
| `section-nested-with-pagebreaks.fodt` (rank 13) | 70.77% | **All checks pass**; 3.81%; 4 vs 4 pages |
| `tdf123163-1.docx` (rank 3) | 11 vs 16 pages | 12 vs 16; still fails |
| `tdf127814.docx` (rank 12) | 16.33%; profile 0.168 | 14.29%; 0.185; still fails |
| `tdf83309.docx` (rank 5) | Pixel passes; 18.15%; 0.943 | Passes; 18.19%; 0.905 (slightly worse); text fails |

Unchanged: `tdf95033.docx` (rank 18, 3 vs 3) and the control
`tdf142694-1.odt` (rank 4, 5 vs 7). Checks 9/18 → 10/18.

## Floating frames and text-box content

- **ODT graphic defaults:** missing wrap means top-and-bottom, not
  run-through. Document default graphic properties apply beneath named styles.
  LibreOffice confirms: an unstyled 100-point image puts following text at
  y=110.51; parallel wrap at x=110.15/y=10.51; run-through at x=10.10/y=10.51.
- **Frame placement:** a wrapping frame that does not fit below existing
  content moves with its paragraph anchor to the next column/page; an
  oversized frame is placed once on a fresh page.
- **Frame-only pages:** a frame counts as page content before its paragraph
  emits a line, so following text can move on without losing the frame.
- **Tables inside text boxes** are kept in the text box's paragraph space.
  DOCX/ODT readers and writers preserve them; shared and standalone PDF
  rendering draw grids, shading and borders. Fixed frames clip vertically,
  growing frames measure the table. RTF writing keeps the paragraphs and
  reports `rtf.shape.table` for the lost grid.
- **CLI text export** includes anchored text-box text and tables, with
  independent list numbering and cell separators.

The anchor/occupancy changes affect only the shared paginator; the standalone
PDF keeps its floating-frame pagination limits.

| Sample | Before | After |
| --- | --- | --- |
| `tdf128737.odt` (rank 25) | 3 vs 4; 73.11% | **Pixel passes**; 4 vs 4; 12.98% |
| `tdf138039.odt` (rank 24) | 1 vs 3; 32.73% | 3 vs 3; 1.41%; fails: top text on a reference-blank page |
| `tdf66039.docx` (rank 23) | 28.05% | Table kept, but worse: 42.31% |
| `tdf116501.odt` (rank 27) | 2 vs 2; 37.56% | 37.53%; fails |

Unchanged and failing: `fdo87488.docx` (rank 26, 1 vs 2),
`floattable-center-shift-down.docx` (rank 6, 4 vs 3),
`tdf170381-split-float-table-in-normal-table.docx` (rank 29, 1 vs 2),
`tdf91083.odt` (rank 8, 6 vs 7), `tdf142694-1.odt` (rank 4, 5 vs 7).

Pixel checks 0/9 → 1/9, combined 11/27 → 10/27: `tdf128737` and `tdf138039`
now export floating captions/text that LibreOffice's text export omits. The
chess-score-sheet DOCX has two DrawingML text boxes; the CLI exports both,
LibreOffice only one (770 vs 384 characters), which explains part of its text
disagreement but not its visual regression.

## Still open

- `tdf142694-1.odt` page-count regression (5 vs 7) from issue 135.
- Group transforms (full affine rendering), font scaling, text-box insets,
  nested shapes in text boxes, horizontal clipping and complex table geometry.
- ODT master-page chains, alternating page sizes, grouped drawing table
  content and the remaining table/image pagination failures (investigated,
  not fixed).
- Complex tracked-change formatting, missing shape/image support, Windows
  font fallback.
