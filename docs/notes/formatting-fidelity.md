# Formatting fidelity roadmap

Status as of 2026-11-13. The scope covers the ten document codecs, their
projection through the shared model, and rendering where applicable.
Verification below records the focused checks run for each implementation slice.
The test counts come from those runs and should be read independently.

## Implementation and verification

| Format | Implemented | Verification and remaining work |
|---|---|---|
| [XLSX](xlsx.md) | Imports and exports number formats and solid ARGB fills, reuses identical styles, and preserves styles on blank cells and merge anchors. Exports explicit row heights and column widths. | 19 XLSX tests passed, including package XML assertions and a round trip with a date-formatted numeric formula result. Verify displayed values and fills in an independent office reader. Common horizontal and vertical alignment, cell fonts and side borders round-trip. Indexed colors and tinted theme colors import as RGB. |
| [ODS](ods.md) | Preserves solid fills, explicit dimensions, row and column default cell styles, common alignments, cell fonts, side borders, and the documented number and date formats. | 24 ODS tests passed. Calc reopened and resaved a centered cell with a dashed border and a bold 15 pt font. Other data styles remain fixture-driven and report `ods.number-format`. |
| [HTML](html.md) | Round-trips table grids, nesting, spans, header rows, shading, padding and side borders. Exports nested lists with starts and item restarts. Reads two-type descendant selectors with specificity, source order and inline overrides. Superscript and subscript round-trip. | 249 HTML tests passed after the table, list and selector changes. The LibreOffice `table-simple/html` run passed 8 checks and skipped 6 because the environment differed from the recorded baseline. Rerun with pinned fonts and a compatible baseline, and check the shaded-cell fixture. Custom numbered markers may be approximated with `html.list.marker`. |
| [ODT](odt.md) | Round-trips fixed extra line spacing and lowercase and capitalize display transforms. Pagination applies the formatting while keeping the source text intact. HTML also preserves the transforms. | A LibreOffice fixture confirmed that 8 pt of fixed leading increased baseline spacing by 8 pt. The later run passed 768 ODT tests and exercised capitalization across style runs. Turkish casing was verified against LibreOffice Writer and in the pagination suite. DOCX and RTF warn when dropping the transforms. |
| [DOCX](docx.md) | Preserves column separators per section, including adjacent sections with equal column counts and different separator settings. The shared page painter draws a 0.5 pt rule centered in each gap. | 1,109 DOCX tests passed. Superscript, subscript, baseline offsets, PAGE and NUMPAGES now round-trip. SECTIONPAGES round-trips and shared pagination resolves it in running content. Character-style STYLEREF round-trips as a live field. Paragraph-style and heading targets, additional switches and compatibility layout remain open. The subsequent pagination run passed 491 tests, covering rules across pages and continuous section boundaries. PDF export from a supplied paginated artifact draws the rules. Standalone PDF handles uniform columns and next-page section column changes on shared paper and margins. Continuous column changes and odd/even page starts also work on shared paper and margins. Parity blanks omit running content. Next-page and parity sections can set their own paper and margins. Balanced continuous columns and page-style sequences still need a supplied paginated artifact. Other section behavior is tracked in the [main roadmap](../roadmap.md#format-fidelity). |
| [RTF](rtf.md) | Reads modern list levels, starts and independent instances. Superscript, subscript, baseline offsets and decimal PAGE and NUMPAGES fields round-trip. Character-style STYLEREF and basic section columns and break kinds round-trip. Odd/even section starts round-trip. Per-section paper and margins round-trip. Section headers and footers round-trip with inheritance. Empty overrides and running drawings still need independent verification. Writes semantic lists in the body, tables, running content, notes and text boxes. Each separate story starts its own counters. | 698 tests passed. LibreOffice rendered a nested bullet and the expected 4, 9, 10 and independent 1 labels. Start-only overrides and decimal cached-marker restarts import. Single-character Unicode bullets and printable ASCII numbering templates round-trip. Literal and ANSI hex-encoded bullets import. Supported format overrides import with independent counters. Broader marker encodings remain open. LibreOffice verified the normalized export labels, but its rendering of the source override differs. |
| [Markdown](markdown.md) | ATX heading levels 1–6 round-trip as paragraph semantics. | 61 tests passed, including a fixture distinguishing headings from ordinary bold text. |
| [CSV](csv.md) | Exports cell text and reports character, paragraph and table formatting losses. | Verify text preservation and diagnostics with styled input. CSV has no native formatting fields. |
| [TSV](csv.md) | Preserves text and reports character, paragraph and table formatting loss through the same diagnostic codes as CSV. | The shared CSV/TSV regression checks text and character and paragraph diagnostic parity. The CSV suite passed 142 tests. |
| [PDF](pdf/feature-matrix.md) | Import can infer decorations, backgrounds and table structure from page geometry. Export has explicit unsupported-feature diagnostics. | Follow the [PDF phase gates](../roadmap.md#pdf). Validate inferred import semantics separately from rendered output, including column separators. The artifact path is covered by a coordinate and emitted PDF rectangle regression. The standalone paginator supports uniform page columns and separators for text and inline tables, including merged row groups that fit within a column. Balanced continuous columns, page-style sequences, floating objects and merged row groups taller than a column still require the shared artifact path for fidelity. The CLI composes PDF as read-only under the Phase 7 release gate. Keep public PDF destinations disabled until the existing readiness criteria pass. |

Formatting Codes is a model projection with a versioned grammar. The existing
capitalization tokens cover all caps and small caps.

## Verification rerun

The solution built with zero warnings and errors. VSTest passed all 5,038
tests across 13 suites with zero skips. The run included Model, Formatting
Codes, XLSX, ODS, HTML, ODT, DOCX, RTF, Pagination, PDF, Markdown, CSV and CLI.
The office checks recorded above were from earlier slices. The pinned office
baseline and independent spreadsheet checks remain pending.

## Review findings and implementation tasks

### 1. RTF restart and marker import

- Add producer fixtures for a list override with a level start, a nested
  restart, and a LibreOffice restart stored in a cached marker. Include
  continuation after the restart and an independent list using the same
  definition.
- In `RtfLists`, parse bounded `lfolevel` overrides and decode the supported
  `leveltext` and `levelnumbers` patterns. Map starts and templates to
  `ListLevel` and preserve the distinction between a list definition and its
  counter instances. Validate level indexes and keep malformed groups isolated.
- Unambiguous decimal and Roman cached markers restore the paragraph ordinal. Expand coverage only where the marker format makes the ordinal unambiguous.
- Verify label sequences before and after export. Add negative cases for
  truncated marker groups, out-of-range levels and unknown list references.
  Reopen the output in LibreOffice and compare labels. Round-trip tests now cover numbering in headers, footers, notes and text boxes.

LibreOffice Writer 26.2.6.3 rendered the authored combined start-and-format
override fixture as 1., ii), iii), 4. It rendered the codec's normalized
export as 1., iv), v), 2., matching the model regression. The Writer-generated version includes cached labels and now preserves 1., ii), iii), 4. on re-export. The uncached source still differs between consumers.

### 2. Spreadsheet interoperability

- Calc reopened styled XLSX and ODS exports and preserved the currency formula, two-column merge, blank-cell fill and 24 pt row height. Its resaved ODS files store the blank fill as a column default, now handled by the ODS reader. Calc retained approximately 100 pt columns from ODS and approximately 74 pt from XLSX. The Calibri 11 digit-metric conversion improved the XLSX fixture to approximately 105 pt in Calc. The remaining difference depends on consumer font metrics. Imported workbooks with other Normal fonts use the same default metric.
- The ODS date subset covers yyyy-mm-dd, dd.mm.yyyy and mm/dd/yyyy. XLSX calendar cells honor date1904, and both readers normalize supported dates to the shared 1900 system. Additional data styles remain fixture-driven.
- XLSX import scales the default digit metric by the Normal font size. Exact family-specific digit measurement and broader SpreadsheetML styling remain outside the verified subset.

### 3. HTML and DOCX layout verification

- Rerun `table-simple/html` and the shaded-cell fixture with the pinned
  baseline. Check cell spans and shading in the model before comparing
  rendered output. Ordered lists now resume their ordinal after intervening blocks. Verify the generated start value with an independent reader.
- Local DOCX raster comparison at 144 DPI confirmed the separator is
  centered at 306 pt and starts at 72 pt in both renderers. With 12 exact
  14 pt lines, the bottom edges agreed within one pixel. The 14 pt
  difference on the default-spacing fixture tracked the body text extent.
  Track that difference under text metrics and retain multipage and
  continuous-section office comparisons.
- Exact-height DOCX fixtures now match Writer separator endpoints within one pixel at 144 DPI on continuous sections and both pages of the multipage fixture. Overflow rules reach the bottom margin. A regression confirms that short pages with explicit breaks retain content-height rules. Standalone PDF remains under the existing PDF gates.

### 4. Remaining format checks

- **ODT.** Language and country round-trip and drive display casing. LibreOffice Writer and the pagination regression both produce İstanbul İzmir for the Turkish fixture.
- **PDF.** Exercise import inference and rendered output independently under
  the [PDF phase gates](../roadmap.md#pdf).

A slice is verified when representative input reaches the intended model
fields, the writer emits the expected native properties, and an independent
reader or renderer confirms the behavior where applicable. For intentional
loss, verify that content survives and callers receive the documented
diagnostic.
