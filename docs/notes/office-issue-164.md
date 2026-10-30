# Issue 164: image extents, cell objects, table flow and relative grids

[Issue #164](https://github.com/Broiler-Platform/Broiler.Documents/issues/164)
ranked 30 samples. Exact paths and input SHA-256 values are in
`tests/office/selections/issue-164.txt` and
`tests/office/selections/issue-164.sha256`; originals are not vendored. Each
pass below uses the previous pass's final triage as its before state, with
Windows, .NET SDK 10.0.403, LibreOffice 26.2.6.3, Poppler 26.07.0, 96 DPI, the
80% threshold, a 40-page cap, a 120-second reference timeout, four jobs and the
16 Liberation font files in `D:/office-fonts`. The issue used Linux,
LibreOffice 24.2.7.2 and Poppler 24.02.0 with the `office-references-v2`
archive; these are controlled local comparisons, not a rerun of its 2,269
failed checks. No tolerance, reference, baseline or tool approval changed.

```powershell
dotnet run --project src/tests/Broiler.Documents.Office --no-build -- `
  --extras-dir D:/scratch/lo-core/sw/qa/extras `
  --sample-list tests/office/selections/issue-164.txt `
  --workspace D:/scratch/issue164-after `
  --triage-dir D:/scratch/issue164-triage-after `
  --previous-triage D:/scratch/issue164-triage-before/triage.json `
  --font-dir D:/office-fonts --dpi 96 --jobs 4 --keep
```

The before run omits `--previous-triage`; the triage comparison checks input
hashes and environment. Paths are local examples. The selection went from
43/89 to **44/89** (45 failed, one skipped) in the first pass and stays there,
with no regressed check. Observations are each run's worst page; when
pagination or the worst page changes they are not same-page comparisons.

## Image extents, cell objects and positioned RTF breaks

- Explicit inline image dimensions survive wrapping. The line wrapper used to
  shrink every oversized image to the available width, so a deliberately
  oversized picture in a narrow nested cell lost most of its row height. An
  image with either dimension stated keeps its display extent; intrinsically
  sized images still fit; page and fixed-row clipping still apply.
- ODT pictures and text frames in cells keep the model's `OffsetXFromCell` flag
  for paragraph and paragraph-content horizontal references; page and
  page-content references keep the page origin.
- Wrapping cell objects (anchors, objects, captions) reserve cell height and
  constrain pagination cuts; unwrapped or page-relative-vertical objects do not
  stretch the cell. Text inside a shape uses its own paragraph space, so it
  cannot pick up objects anchored to body paragraphs with the same index.
- RTF page breaks after positioned paragraphs reach the body. A frame has no
  separately paginated flow, so a break it consumed used to vanish. Pending
  frames now follow their next unframed anchor across the break, with an empty
  body paragraph preserving the page being left. Single-column `\column` takes
  the same path; shape paint, geometry, size and stack order survive.

| Original sample | Pages before -> after | LibreOffice | Notes |
| --- | ---: | ---: | --- |
| `layout/data/tdf142694-1.odt` | 5 -> 6 | 7 | 389.99 x 315.01 pt picture keeps its extent in the nested cell; worst page is an earlier, unchanged one |
| `odfexport/data/tdf169882.odt` | 4 -> 7 | 7 | Blur 39.21% -> 22.83%, coverage delta 21.17% -> 9.11%, but ink-box delta 7 -> 31 px; still fails |
| `ooxmlexport/data/tdf141173_missingFrames.rtf` | 1 -> 2 | 2 | Empty first page and second-page frames match; pixel check newly passes; text still differs |

`ooxmlexport/data/fdo78420.docx` changes slightly from keeping an explicit
image extent (blur 17.9371% -> 17.8882%); still 26 vs 23 pages.

## Table flow and ODT row/column styles

- A keep-with-next paragraph measures the first row of a following inline
  table instead of stopping at it, so a heading is not stranded. Splittable rows
  use their first legal cut; the whole row need not fit. Explicit table breaks
  and floating tables keep their behavior. Measurement snapshots list counters
  and does not consume numbering.
- ODT table-row and table-column styles resolve parent chains with the bounded,
  cycle-checked style resolver; inherited column widths, row heights and row
  keep-together survive. A child's minimum or exact row height replaces either
  inherited rule.
- An explicitly disabled cell border side overrides the uniform border (a
  zero-width side used to count as missing). A child style's uniform border
  overrides edges inherited from parents; direct side declarations still
  override the child's uniform one.

A FODT fixture exported by LibreOffice (`OpenDocument Text Flat XML` filter,
`writer_pdf_Export`) keeps seven body lines on page one and moves the kept
heading with its table to page two, as the paginator now does. Decision: a
proposed cross-row paragraph keep rule was dropped because native Writer
behaved differently. No top-30 check changed.

## Cell-relative shape alignment

ODT pictures and text frames may state `style:horizontal-pos="left"`,
`"center"` or `"right"` relative to their paragraph inside a cell; the reader
used to keep only `svg:x`. The request is now
`DocumentShape.CellHorizontalAlignment`, resolved by the shared paginator
against the padded cell text area (before paragraph indents) and written back
by the ODT writer. Absent alignment keeps the numeric offset; page-relative
anchors get none. DOCX export reports its alignment fallback; RTF and vector
PDF export already report unsupported cell-relative placement.

`odfexport/data/tdf169882.odt` keeps seven pages; with byte-identical
LibreOffice rasters, mean absolute RGB difference (supporting measure, not the
suite metric) improves on page 2 (8.7609% -> 5.5744%), page 4
(18.0279% -> 3.0608%) and page 5 (8.8720% -> 6.6781%). Page 6 remains worst
(22.8335% blur), so the pixel check still fails and the summary is unchanged.

## Relative column grids and uniform page margins

- ODT grids given only by `style:rel-column-width` keep their proportions
  (they used to become equal unspecified widths), following the weighted-width
  definition in
  [ODF 1.1 section 15.9.1](https://docs.oasis-open.org/office/v1.1/OS/OpenDocument-v1.1.pdf).
  A fixed table width supplies the point basis; an unspecified table fills its
  text area via the percentage-width model; an explicit table percentage wins.
  Grids with absolute widths are unchanged.
- Repeated columns expand their weights; finite weights are normalized before
  summing to avoid overflow. Missing, non-finite, zero, negative or malformed
  weights fall back to the unspecified grid.
- Page layouts honor uniform `fo:margin`; per-side values (including zero)
  override it. The top margin resolves before the header-height calculation;
  ODT round trips keep the geometry.

A 300 x 100 pt FODT page with 10 pt margins and weights `16384*` / `49151*`
renders at 400 x 134 px in both LibreOffice and Broiler, with the left border at
column 13 and the divider at column 106. LibreOffice 26.2.6.3 renders tiny
weights such as `1*` / `3*` differently from the same ratio normalized to
65535; the reader applies the ratio to both, so exact parity is not claimed.
Relative-only styles occur in `ooxmlexport/data/ooo96040-2.odt` and
`uiwriter/data/tdf130680.odt` (partly in hidden or deleted content); neither is
newly resolved. No selected original uses a uniform page margin.

## Still open

- Column and table layout, linked-frame and heading formatting, unsupported
  image formats, running fields and text export differ in the other samples.
- Cell-relative inside/outside alignment and text wrapping beside an object in
  the same cell paragraph; the space reservation is not an object-positioning
  solver.
- Mixed absolute/relative grids and table margin sizing.
- The remote background in `page-background-link.fodt` stays unfetched under
  the external-resource policy.
- The largest `tdf163894` page-count differences depend on LibreOffice version
  and fonts and were not treated as fixed.
