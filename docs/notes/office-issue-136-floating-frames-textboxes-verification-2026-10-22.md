# Issue #136: floating frames and text-box content

The rankings were fetched directly from [GitHub issue #136](https://github.com/Broiler-Platform/Broiler.Documents/issues/136)
on 2026-10-22. Nine unchanged LibreOffice samples were downloaded for this batch.
Five shared defects were selected from the remaining drawing and pagination failures:

1. **ODT graphic defaults.** Missing wrap properties now mean top-and-bottom,
   rather than run-through. Document default graphic properties apply beneath
   named styles, whose explicit values override the defaults.
2. **Frame and anchor placement.** A wrapping frame that cannot fit below
   existing content moves with its paragraph anchor to the next column/page.
   An oversized frame is allowed once on a fresh page to avoid repeated breaks.
3. **Pages containing only wrapping frames.** A frame counts as occupied page
   content even before its paragraph emits a line. Following text can continue
   onto the next page without losing the frame's page.
4. **Tables inside text boxes.** The model retains their tables in the text
   box's own paragraph space. DOCX/ODT readers and writers retain their structure;
   shared and standalone PDF rendering use their cell grids, shading and borders.
   Fixed frames clip vertically; growing frames measure the table. RTF writing
   retains the paragraphs and reports `rtf.shape.table` for the lost grid.
5. **CLI text export.** Anchored text-box text and tables are included, with
   independent list numbering and table cell separators. Previously this content
   disappeared from dumps and text comparisons altogether.

## Measured impact

The baseline is an isolated CLI build taken immediately before this batch,
including the preceding two batches. Both runs used Windows, LibreOffice
26.2.6.3, Poppler 26.07.0, unpinned fonts, 72 DPI, one job and the default 80%
threshold. These results do not establish the Linux corpus result at 144 DPI.

| Sample | Before | Final result |
| --- | --- | --- |
| `tdf128737.odt` (rank 25) | 3 vs 4 pages; 73.11% blurred difference | **Pixel passes**; 4 vs 4 pages; 12.98% blurred difference |
| `tdf138039.odt` (rank 24) | 1 vs 3 pages; 32.73% blurred difference | 3 vs 3 pages; 1.41% blurred difference, but pixel still fails because top text appears on a reference-blank page |
| `tdf66039.docx` (rank 23) | 1 vs 1 pages; 28.05% blurred difference | Table structure retained, but pixel still fails and blurred difference worsens to 42.31% |
| `tdf116501.odt` (rank 27) | 2 vs 2 pages; 37.56% blurred difference | 2 vs 2 pages; 37.53%; pixel still fails |
| `fdo87488.docx` (rank 26) | 1 vs 2 pages | Unchanged rendering; still fails |
| `floattable-center-shift-down.docx` (rank 6) | 4 vs 3 pages | Unchanged rendering; still fails |
| `tdf170381-split-float-table-in-normal-table.docx` (rank 29) | 1 vs 2 pages | Unchanged rendering; still fails |
| `tdf91083.odt` (rank 8) | 6 vs 7 pages | Unchanged rendering; still fails |
| `tdf142694-1.odt` (rank 4, control) | 5 vs 7 pages | Unchanged rendering; earlier regression remains unresolved |

Pixel checks improve from 0/9 to 1/9. Combined checks change from 11/27 to
10/27: nine read checks and one pixel check pass, with no skips. Two previously
passing text checks now expose floating captions/text absent from LibreOffice's
text export (`tdf128737`, `tdf138039`). All nine final text checks fail.

The chess-score-sheet DOCX contains two complete DrawingML text boxes. The CLI
now exports both, whereas LibreOffice's text export returns only one (two title
occurrences and 770 characters versus one and 384). This explains part of its
text disagreement, but does not excuse the remaining visual regression. Group
transforms, font scaling, text-box insets and more complex table geometry still
need work. Five corrected mechanisms do not mean five fully conforming documents.

## Verification and limits

**2,724 tests pass:** Model 156, DOCX 541, ODT 398, RTF 476, Pagination 175,
PDF 722 and CLI 256. Release CLI build and `git diff --check` pass.
Authored tests cover default/named graphic inheritance, fitting and oversized
frames, run-through controls, text-box table read/write round trips, fixed and
growing frames, cell geometry and shading, export ordering, independent lists
and the RTF loss diagnostic.

Authored flat ODT files were also converted directly with LibreOffice: an
unstyled 100-point image places following text at y=110.51; explicit parallel
wrap places it beside the image at x=110.15/y=10.51; run-through places it at
x=10.10/y=10.51. This confirms the changed wrap default independently of scores.

The anchor/page occupancy corrections affect the shared paginator used by CLI
rendering. Standalone PDF now handles text-box tables but retains its separate
floating-frame pagination limitations. Nested shapes inside text boxes, full
affine group rendering and complete horizontal clipping are not added here.
No upstream document bytes or generated render artifacts are committed.
