# Issue 135: section paper, table heights and text projection

Investigation of [issue 135](https://github.com/Broiler-Platform/Broiler.Documents/issues/135).
The report now covers 3,901 upstream samples rather than a run starved of temporary
storage. This change addresses reusable causes of differences; it does not claim
that its three highest-ranked documents now pass.

## Changes

1. The shared pagination engine switches paper size when a section opens a page,
   retains it on continuation pages, and restores the document paper afterwards.
   Previously only section margins were used, leaving every page on the last
   section's paper. Explicit caller paper overrides and continuous output keep
   their existing behavior. This affects raster output, preview and printing;
   the independent PDF codec still reports its section-layout limitation.
2. Both pagination engines now apply `TableRow.MinHeight`, which the readers
   already preserved but neither renderer used. A row whose floor will not fit
   below the preceding content moves to a fresh page. Content taller than the
   floor still grows; exact-height clipping remains unsupported and diagnosed.
3. Plain-text export uses tabs between table cells and newlines between rows,
   preserving paragraph boundaries inside cells and nested tables. Image object
   replacement markers contribute no text, matching LibreOffice; literal U+FFFC
   characters remain text. ODF index titles are now read rather than dropped,
   preserving headings such as `Inhalt` in the report's `tdf152307.odt`.

These projection changes preserve real text comparisons rather than making the
comparison ignore arbitrary whitespace or missing characters. Missing pictures
remain the pixel axis's responsibility.

## Verification

All 1,964 tests passed across Pagination (127), CLI (254), DOCX (515), ODT (365)
and PDF (703). New authored regressions cover mixed portrait/landscape paper,
continuation-page height, explicit paper overrides, minimum row heights in both
engines, nested table text, image markers and all seven generated ODF index types.

A small authored DOCX was also converted to text by LibreOffice 26.2.6.3. Its
multi-paragraph first cell and two table rows export as
`before\nA1\nA2\tB\nC\tD\nafter`, matching the new CLI projection apart from the
reference export's final newline.

Five upstream documents were downloaded to the system temporary directory and
checked locally with LibreOffice 26.2.6.3 and Poppler 26.07.0 at 72 DPI, one job,
40-page comparison limit. They were not committed. Fonts were not pinned on
Windows, so these numbers are local observations, not replacements for Linux CI.

| Sample | Observation |
| --- | --- |
| `layout/data/tdf123163-1.docx` | Worst-page blurred difference fell from 93.50% to 73.57%; the worst page moved from 2 to 3. Pagination remains 11 versus 16 locally. Missing pictures, unequal columns and exact row clipping remain. |
| `uiwriter/data/tdf170672.docx` | Worst-page blurred difference fell from 14.24% to 12.85%, but pagination changed from 33 to 31 against 40 reference pages measured. Its composite severity therefore increased. Mixed-paper regressions pass, but this floating-table document is not fixed in full. |
| `ooxmlexport/data/tdf163894_hidden.docx` | Unchanged: 4 pages against 40 reference pages measured. The running header uses STYLEREF fields; the CLI retains their cached text. A separate LibreOffice DOCX round trip and PDF export produced 67 pages, confirming that 40 is the comparison cap, not the full reference page count. |
| `layout/data/tdf152307.odt` | Before the projection fixes, text comparison reached the 21-difference cap; table separator changes left only an image marker, now removed. The index heading is preserved and the document now passes its text check. Both renderers produced 15 pages locally, with remaining pixel differences. |
| `ooxmlexport/data/ooo96040-2.odt` | Real text differences remain, including a cached date formatted differently by LibreOffice. Its local pagination remains 16 versus 13. |

All five documents still fail the 80% pixel verdict. A full Linux corpus rerun is
needed to measure the aggregate effect. The remaining dynamic header-field,
floating-table and unequal-column work must not be represented as completed by
these changes.
