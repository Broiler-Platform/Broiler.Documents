# Office issue 135: inline table geometry

The [GitHub issue](https://github.com/Broiler-Platform/Broiler.Documents/issues/135)
was fetched again before this batch. Seven ranked documents were downloaded
directly from LibreOffice's GitHub repository and compared locally before edits.
Three missing table behaviors were selected: preferred width, horizontal
placement, and cell vertical alignment.

## Changes

1. **Preferred table widths.** Read DOCX `tblW` in points or fiftieths of a
   percent (including Strict's percent spelling), and ODT `style:width` /
   `style:rel-width`. Explicit percentage width is relative to the containing
   text area, including nested cells. An explicit width scales the stated grid
   in either direction; unspecified columns share only the space remaining.
   Automatic/nil declarations cancel inherited preferred widths.
2. **Horizontal table placement.** Read DOCX `jc` and `tblInd`, plus ODT table
   alignment and left margin. Inline tables can be centered, right aligned or
   indented. Borders, text and nested cell content move together. Repeated
   headers retain their table's preferred width and alignment.
3. **Cell vertical alignment.** Read DOCX `vAlign` (including table-style
   inheritance and direct overrides), and ODT `style:vertical-align`. Both the
   shared paginator and PDF writer place content at the top, center or bottom
   of the space left after padding. Merged cells use their combined row height;
   shared pagination aligns split-row fragments when placed.

Both codecs write the added geometry, and model paragraph edits preserve it.
The ODT writer supplies a physical grid-width fallback when an aligned or
indented table would otherwise stretch on import. DOCX cell properties are
written with borders before shading, then margins and vertical alignment.
The DOCX style diagnostic no longer reports the geometry that is now applied.

These changes concern inline tables. Floating placement retains its existing
rules. Preferred widths are bounded by the containing area; this is not a full
implementation of Word's automatic table fitting or minimum-content sizing.
Existing merged-row height allocation, unequal section columns, dynamic header
fields and exact-row clipping remain separate work.

## Verification

All 2,198 tests passed: Model 156, DOCX 532, ODT 381, Pagination 160, PDF 715,
and CLI 254. Authored regressions cover scaling in both directions, percentage
widths, mixed specified/unspecified columns, alignment/indent placement,
padding-aware vertical alignment, merged cells, repeated headers, codec round
trips, inherited values and explicit cancellation.

Four authored documents were written as both DOCX and ODT, then exported to PDF
by LibreOffice 26.2.6.3. Their horizontal positions agree with the PDF codec,
apart from LibreOffice's approximately 0.1-point table border offset:

| Case (180-point text area) | LibreOffice text X | PDF codec text X |
| --- | --- | --- |
| Centered 100-point table, columns 40/60 | 50.1, 90.1 | 50, 90 |
| Table at 50%, columns in ratio 40/60 | 10.1, 46.1 | 10, 46 |
| 100-point table indented 18 points | 28.1, 68.1 | 28, 68 |

For a 60-point row with a 10-point line and 5-point paragraph spacing, both
export paths place centered content 22.5 points below top-aligned content and
bottom-aligned content 45 points below it. Absolute glyph baselines retain the
existing roughly 4-point difference between the PDF codec and LibreOffice.

The seven upstream samples were rerun at 72 DPI, one job, unpinned Windows
fonts and a 40-page comparison cap, using LibreOffice 26.2.6.3 and Poppler
26.07.0. These measurements do not replace the Linux workflow results.

| Sample | Before | After |
| --- | --- | --- |
| `layout/data/tdf150606-1-min.odt` | 5 vs 5 pages; worst page 1; 45.57% blurred difference | 5 vs 5 pages; worst page 5; 7.96% blurred difference |
| `ooxmlexport/data/tdf127814.docx` | 2 vs 2 pages; 16.43% blurred difference | 2 vs 2 pages; 16.33% blurred difference |
| `layout/data/tdf142694-1.odt` | 5 vs 7 pages; 46.30% blurred difference | 5 vs 7 pages; 46.21% blurred difference |
| `ooxmlexport/data/FDO76312.docx` | 2 vs 2 pages; 19.35% blurred difference | Unchanged |
| `odfimport/data/tdf103025.odt` | 3 vs 5 pages; 1.58% blurred difference | Unchanged |
| `odfimport/data/tdf165156.odt` | 2 vs 3 pages; 0.51% blurred difference | Unchanged |
| `layout/data/tdf122607.odt` | 3 vs 3 pages; 12.81% blurred difference | Unchanged |

There are **no additional passing checks** in this seven-document set: both
runs pass 12/21 checks, with zero skips and all seven pixel checks failing.
The centered table's large blurred-difference reduction is not an overall
conformance pass: its worst-page ink-profile similarity changes from 0.996 to
0.490 when the worst page moves, and its composite severity increases slightly
(787.77 to 804.96). The other DOCX table's composite severity also increases
slightly despite its lower blurred difference. The behaviors are reproduced and
verified, but these complete documents must not be described as fixed.

The previous batch's `tdf142694-1.odt` page-count regression remains present
(5 vs 7 pages). Reviewing its styles did not establish a correction in this
batch; it remains explicitly outstanding. No upstream sample bytes or generated
integration documents were added to the repository.
