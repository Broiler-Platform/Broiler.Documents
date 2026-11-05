# Issue #197: top differences

Investigation and fixes for the top differences in [issue #197](https://github.com/Broiler-Platform/Broiler.Documents/issues/197):
1. `layout/data/tdf170381-split-float-table-in-normal-table.docx` (rank 1, severity 1110.06): 1 vs 2 pages, 10 lines semantic text difference.
2. `ooxmlexport/data/tdf132149_pgBreak.odt` (rank 2, severity 1075.19): continuation reflow across page widths.
3. `ooxmlexport/data/tdf127814.docx` (rank 3, severity 1047.83): tracked-change zero-width joiners (`U+200D`) in text projection.
4. `ooxmlimport/data/n820504.docx` (rank 4, severity 1045.66): spurious bullet on subtitle with `ilvl` but no `numId`.
5. `layout/data/Cool16066_headlineAndUnsplittableRow.fodt` (rank 5, severity 1032.42): unsplittable row under repeated header reference defect (`gerrit#209269`).
6. `ooxmlimport/data/tdf108714.docx` (rank 6, severity 1025.16): 1 vs 4 pages, block-level page breaks (`<w:br w:type="page"/>`) directly under `w:body`.

---

## 1. Rank 1: `layout/data/tdf170381-split-float-table-in-normal-table.docx`

### Findings
- The document has an outer 4-row table whose second row contains a 13-row floating table (`w:tblpPr` with `tblpX` and `tblpY`).
- In `DocxTableReader.cs`: nested tables at `depth > 1` have their floating position suppressed (`depth <= 1 ? ReadPosition(properties) : null`) and emit `docx.table.floating: "A DOCX table nested in a cell stated a floating position; it was laid out in its cell."`
- **Text Difference (10 lines):** LibreOffice imports any positioned table as an `SwFlyFrame` (text frame). LibreOffice's plain text export (`SwASCWriter::WriteStream`) walks body paragraphs and omits fly frames. In broilerdoc, `dump --as text` leaves top-level floating tables out (issue #175), but because nested tables have their position suppressed in the model, their text is dumped, creating 10 lines of semantic differences (the 10 non-empty rows of the nested table).
- **Pagination (1 vs 2 pages):** Upstream bug **tdf#170381** (*"Some documents with floating tables hang on opening"*, commit `fb203eb57a1ba0accf1894672d35ddf9430d8b05` by Mike Kaganski, Gerrit 197524, Jan 2026) added handling for split-but-not-yet-moved floating tables nested in normal tables across pages. The first paragraph has `w:spacing w:before="9018"` (450 pt), pushing the table to the bottom of the page and causing the nested floating table to split across 2 pages in LibreOffice. In broilerdoc, cell-nested floating-table overflow and splitting across pages remains an open layout roadmap item (as noted in `docs/roadmap.md` and `docs/notes/office-issue-145.md`).

---

## 2. Rank 2: `ooxmlexport/data/tdf132149_pgBreak.odt`

### Findings
- Covers paragraph continuation across page breaks where the destination page style defines a different page geometry and narrower content width (issues [#137](office-issue-137.md), [#145](office-issue-145.md), [#147](office-issue-147.md)).
- Advancing page style sequences in ODT refreshes paper and margins, but paragraphs continuing across page breaks currently keep the text-column width computed at the start of the paragraph.
- In issue #145, an experimental prototype for re-composing remaining paragraph tails across alternating page widths was tested (`issue145-tail-reflow-rejected.patch`), but caused visual regressions on overall pagination (5 vs 5 regressing to 6 vs 5 pages). The prototype was reverted and remains documented as an architectural roadmap topic ("Continuation reflow across page widths").

---

## 3. Rank 3: `ooxmlexport/data/tdf127814.docx`

### Findings and Root Causes
- The document contains a 28-row table with tracked cell insertions (`<w:cellIns>`).
- In LibreOffice, Writer's change tracking architecture only supports text-range-based changes. Commits `tdf#142700` and `tdf#142701` (László Németh) added an internal workaround that inserts zero-width joiners (`U+200D`) as invisible text anchors around textless tracked-change items (such as empty cells inserted under change tracking).
- When exported to plain text via `soffice --convert-to txt`, these synthetic `U+200D` characters were written on lines of their own.
- In `TextProjection.Canonicalise`, `U+00AD` (soft hyphen), `U+200B` (zero-width space), and `U+FEFF` were stripped, but `U+200C` (zero-width non-joiner) and `U+200D` (zero-width joiner) were not, causing the diff engine to treat 21 empty-cell lines containing only `U+200D` as non-empty lines in LibreOffice's output and missing from broilerdoc.
- Visually, broilerdoc renders both pages cleanly with a blur difference of only 0.15% and identical page count (2 vs 2).

### Fix
- Updated `src/tests/Broiler.Documents.Office/TextProjection.cs` to include `U+200C` and `U+200D` in the zero-width character stripping step in `Canonicalise`.
- Result: 0 semantic text differences on `tdf127814.docx`.

---

## 4. Rank 4: `ooxxmlimport/data/n820504.docx`

### Findings and Root Causes
- **Semantic Text Difference (`◦ Certificate` vs `Certificate`):** In `n820504.docx`, the `Subtitle` paragraph style defined `<w:pPr><w:numPr><w:ilvl w:val="1" /></w:numPr></w:pPr>` without a `<w:numId>`. In `DocxReader.ApplyParagraphProperties`, when `numPr` had an `ilvl` but no `numId`, it defaulted `ListKind` to `ListKind.Bullet`, causing `ListNumberer` to synthesize a level-1 white bullet (`◦`). Under ECMA-376, paragraphs without a resolved `w:numId` are not list items.
- **Visual Differences:** Upstream bug **bnc#820504** (*"VML path import: get back missing points"*). The document has a full-page VML group containing a freeform plaque outline (`Freeform 3` with fill `#3da7bb`). Because non-oval/non-line VML shapes are approximated as `ShapeGeometry.Rectangle` in `DocxReader.cs`, broilerdoc fills a solid 10 in $\times$ 540 pt cyan rectangle covering 80% of the page, whereas LibreOffice renders the contoured plaque path.

### Fix
- Updated `DocxReader.ApplyParagraphProperties` in `src/Broiler.Documents.Docx/DocxReader.cs` to only apply `numbering.KindFor(numId)` when `hasNumId` is true, and preserve the existing `style.ListKind` (defaulting to `ListKind.None`) when `numId` is absent.
- Added unit test `An_Ilvl_Without_NumId_Does_Not_Synthesize_A_Bullet_List` in `src/tests/Broiler.Documents.Docx.Tests/DocxNumberingTests.cs`.
- Result: Semantic text diff `◦ Certificate` vs `Certificate` is resolved.

---

## 5. Rank 5: `layout/data/Cool16066_headlineAndUnsplittableRow.fodt`

### Findings
- Upstream review: **gerrit#209269** (commit `f0e78f28ee6e8586d66fa8d988366a1ae02aae77`, *"cool#16066 sw: omit the repeated heading where a row needs the full page"*, Mike Kaganski, August 18, 2026; upstream test `testCool16066_headlineAndUnsplittableRow` in `layout6.cxx`).
- In LibreOffice 24.2.7.2 (the reference baseline), an unsplittable row under a repeated table header that exceeded the remaining page height reached past the bottom of the page and was truncated, keeping the document on 2 pages with dropped text (*"The setup never worked - StarOffice 5.2 drops the same text"*).
- In Microsoft Word and modern LibreOffice 26.8+, when an unsplittable row is taller than the remaining page height under the repeated header row, the repeated header is omitted on that page so the row gets the full page to itself (resulting in 2 clean pages).
- In broilerdoc, `ContinueTable` in `DocumentLayout.cs` unconditionally placed the repeated header on page 2. Because page 2 had the header and could not fit the unsplittable row, the row was placed under the header, overflowing the page and pushing trailing content to page 3.

### Fix
- Updated `ContinueTable(bool inHeader, RowBox? nextRow = null)` in `src/Broiler.Documents.Pagination/DocumentLayout.cs`:
  - When `nextRow is not null && !nextRow.CanSplit && y + headerHeight + nextRow.Height > contentBottom`, the repeated header is omitted for that page so the unsplittable row gets the entire page height.
  - On subsequent pages where following rows fit underneath the repeated header, the header repeats normally.
- Added unit test `Repeated_Header_Is_Omitted_When_Unsplittable_Row_Cannot_Fit_Under_It` in `src/tests/Broiler.Documents.Pagination.Tests/DocumentLayoutHeaderRowTests.cs`.
- Recorded as a Known Reference Defect in `tests/office/extras-reference-defects.json` under `axes: ["pixel"]` (`cool16066-omit-repeated-heading`) because the reference baseline (LibreOffice 24.2.7.2) truncated the row past the page bottom, whereas modern LibreOffice 26.8+ and Word omit the repeated heading.

---

## 6. Rank 6: `ooxmlimport/data/tdf108714.docx`

### Findings and Root Causes
- Upstream bug **tdf#108714** (*"Also support paragraph-level (line) breaks"*, commit `Ida55015363cac3ae29b82a60a9b9a5f1b39086a2` by Mike Kaganski).
- In DOCX, breaks (`<w:br>`) normally appear within text runs (`<w:r><w:br/></w:r>`). However, Word and document generators also place `<w:br w:type="page"/>` directly at block level as siblings of `<w:p>` under `<w:body>` or inside `<w:tc>`.
- In `tdf108714.docx`, three `<w:br w:type="page"/>` elements exist directly under `<w:body>` separating paragraphs across 4 distinct pages.
- In `DocxReader.ReadBlockContent`, `w:br` was not recognized among block-level elements and fell through to `builder.AddUnsupportedBlock(name)`, skipping all three page breaks and collapsing 4 pages onto 1 page.

### Fix
- Updated `DocxDocumentBuilder.HoldPageBreak(bool column = false)` in `src/Broiler.Documents.Docx/DocxDocumentBuilder.cs`.
- Updated `DocxReader.ReadBlockContent` in `src/Broiler.Documents.Docx/DocxReader.cs` to check for `w:br` and call `builder.HoldPageBreak(...)` when `IsPageBreak(...)` or `IsColumnBreak(...)` is true.
- Added unit test `Block_Level_Page_Break_Starts_Next_Paragraph_On_A_New_Page` in `src/tests/Broiler.Documents.Docx.Tests/DocxPageBreakTests.cs`.
- Result: `tdf108714.docx` now renders exactly **4 pages** with status `Success` and 0 diagnostics.
