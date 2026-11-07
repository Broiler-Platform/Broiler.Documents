# Office Conformance Issue 207

Investigation and fixes for top-ranked differences identified in [issue #207](https://github.com/Broiler-Platform/Broiler.Documents/issues/207) (Run 131 triage against headless LibreOffice 24.2.7.2):

1. `odfimport/data/ooo77837-1.odt` (rank 1, severity 1000.63, 3 vs 4 pages): Upstream LibreOffice 24.2 reference defect, extra blank 4th page produced when endnotes contain index/bibliography sections; modern LibreOffice (26.2+) and broilerdoc render 3 pages with 100% text match.
2. `layout/data/tdf152106.odt` (rank 2, severity 991.02, 3 vs 4 pages): Multi-column section with floating frames straddling columns, forward-pass paragraph-anchored shape wrapping.
3. `layout/data/tdf142694-1.odt` (rank 3, severity 988.80 -> 812.66, 6 vs 7 pages -> 7 vs 7 pages): Repeating table header rows splitting across pages cutting nested table; fixed by forbidding header rows from splitting across pages.
4. `layout/data/tdf106390.odt` (rank 4, severity 970.15 -> 0.0, 2 vs 3 pages -> 3 vs 3 pages): Oversized unsplittable row exceeding single page height truncated on page 1; fixed by permitting oversized rows exceeding page capacity to split.
5. `ooxmlexport/data/tdf146955.odt` (rank 5, severity 967.06, 40 vs 40 pages): Upstream broken test fixture with 320 page-anchored frames (up to page 367) where body text ends on page 28, leaving pages 29-30 blank in LibreOffice.
6. `ooxmlexport/data/tdf128207.docx` (rank 6, severity 965.73, 1 vs 1 page): DrawingML `<c:chart>` without bitmap fallback emits diagnostic `docx.image.shape`.
7. `layout/data/tdf130031.docx` (rank 7, severity 964.92, 1 vs 1 page): DrawingML `<c:chart>` without bitmap fallback emits diagnostic `docx.image.shape`.
8. `layout/data/testAreaChartNumberFormat.docx` (rank 8, severity 961.90, 1 vs 1 page): DrawingML `<c:chart>` without bitmap fallback emits diagnostic `docx.image.shape`.
9. `layout/data/tox-update-wrong-pages.odt` (rank 9, severity 961.68, 15 vs 13 pages): 81 Table of Contents entries on ISO A5 with footer clearance and font line height differences spilling 5 entries to page 5.
10. `layout/data/tdf130380.docx` (rank 10, severity 953.78, 1 vs 1 page): DrawingML `<c:chart>` without bitmap fallback emits diagnostic `docx.image.shape`.
11. `ooxmlexport/data/tdf169024_verticalTopFrame.odt` (rank 11, severity 950.97 -> 0.0, 1 vs 1 page): ODT frame anchored `as-char` with `vertical-rel="line"` and `vertical-pos="top"` positioned 0.85" below line in broilerdoc; fixed in `OdtReader.cs` by detecting line-top relative frames and mapping to line top alignment.
12. `odfexport/data/tdf134987.docx` (rank 12, severity 943.67, 1 vs 1 page): DrawingML `<c:chart>` without bitmap fallback emits diagnostic `docx.image.shape`.
13. `layout/data/tdf129054.docx` (rank 13, severity 943.57, 1 vs 1 page): DrawingML `<c:chart>` without bitmap fallback emits diagnostic `docx.image.shape`.
14. `layout/data/tdf134866.docx` (rank 14, severity 943.18, 1 vs 1 page): DrawingML `<c:chart>` without bitmap fallback emits diagnostic `docx.image.shape`.
15. `uiwriter/data/tdf132321.odt` (rank 15, severity 940.38, 1 vs 1 page): Document page count matches (1 vs 1 page); visual blur difference (13.4%) caused by slight font rendering / tracking differences on Liberation Serif.
16. `rtfimport/data/section-pagebreak.rtf` (rank 16, severity 940.22 -> 0.0, 4 vs 4 pages): RTF `\sbknone` (continuous section) was reset on subsequent section breaks, and `\page\sect` (page break followed by section break) caused duplicate blank page insertions; fixed by retaining sticky section break mode and synchronizing pending page breaks.
17. `layout/data/2024-01-19_merged-cells-on-separate-pages-vertical-alignment.odt` (rank 17, severity 940.14 -> 0.0, 3 vs 2 pages -> 2 vs 2 pages): ODT table cell with `rowspan=51` but short text forced 51 rows to move together to page 2 leaving page 1 blank; fixed in `DocumentLayout.cs` by making merged-cell row grouping content-aware based on cell content height.
18. `layout/data/U-min.fodt` (rank 18, severity 940.03 -> 0.0, 1 vs 1 page): FODT conditional section `<text:section text:display="condition" text:condition="ooow:userVar == 1">` was unconditionally hidden; fixed by adding variable parsing (`user-field-decl`, `variable-decl`) and condition evaluation to `OdtReader.cs`.
19. `ooxmlexport/data/tdf58944-repeating-table-header.docx` (rank 19, severity 938.93 -> 0.0, 3 vs 2 pages -> 2 vs 2 pages): DOCX table with all rows marked `<w:tblHeader/>` erroneously forced a page break before the table in `DocxTableReader.cs`; fixed by removing the spurious page break (matching Word and LO `tdf#138020` where repeating header is simply suppressed).
20. `rtfimport/data/tblrepeat.rtf` (rank 20, severity 915.73 -> 0.0018 blur diff, 2 vs 2 pages): RTF cell border keyword `\brdrtbl` was unhandled, falling through to `border.Drawn = true` and drawing unwanted borders around 122 cells; fixed by setting `border.Drawn = false` for `brdrtbl`. Upstream LO 24.2 reference has a defect omitting repeating headers on page 2 (fixed in LO 26.2).

---

## 1. Rank 4: `layout/data/tdf106390.odt`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/layout/layout.cxx testTdf106390` (TDF bug #106390).
- The document contains a multi-page table where an oversized row has `may-break-between-rows="false"` (`cantSplit`), but the row's height exceeds the full content height of an entire page.
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs`:
  - Previously, `bool canSplitRow = row.CanSplit && !(table.KeepTogether && totalTableHeight <= contentBottom - pageTop);`.
  - When `row.CanSplit` was false, the row could never be split, even when its height exceeded the entire page height (`contentBottom - pageTop`).
  - As a result, the oversized row was placed whole on page 1, running off the bottom of the page, while subsequent rows and pages were truncated, resulting in 2 pages in broilerdoc versus 3 pages in LibreOffice (severity 970.15).

### Fix
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs`:
  - Relaxed `canSplitRow` so that rows marked unsplittable that exceed full page capacity (`!hasNestedTables && row.Height > contentBottom - pageTop && !row.ExactHeight`) are permitted to split across pages, matching LibreOffice and Word behavior:
    ```csharp
    bool hasNestedTables = row.Contents.Any(c => c.Cell is not null && c.Cell.Tables.Count > 0);
    bool canSplitRow = !inHeader
        && (row.CanSplit || (!hasNestedTables && row.Height > contentBottom - pageTop && !row.ExactHeight))
        && !(table.KeepTogether && totalTableHeight <= contentBottom - pageTop);
    ```
- In `src/tests/Broiler.Documents.Pagination.Tests/DocumentLayoutRowSplitTests.cs`:
  - Updated test `A_Row_That_May_Not_Split_And_Cannot_Fit_A_Page_Splits_Across_Pages` to assert that an oversized unsplittable row splits across pages (placing lines up to page capacity on page 0 and remaining lines on page 1) instead of overflowing off the page.
- Result: `tdf106390.odt` now lays out on 3 pages matching LibreOffice reference page count identically with 100% pass (severity reduced from 970.15 to 0.0).

---

## 2. Rank 3: `layout/data/tdf142694-1.odt`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/layout/layout.cxx testTdf142694_1` (TDF bug #142694).
- The document contains complex nested tables inside repeated table headers (`Table66`).
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs`:
  - Table header rows (`inHeader`) were previously eligible for row splitting.
  - When header rows exceeded available vertical space on page 3, the header row split mid-nested-table across pages 3 and 4, stranding partial cells and corrupting layout on pages 4–6, leading to a total of 6 pages instead of 7 pages (severity 988.80, rank 3).
  - In LibreOffice, repeated table header rows are never split across pages: if a header row cannot fit in the remaining page space, the entire table continuation moves to the next page.

### Fix
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs`:
  - Enforced `!inHeader` in `canSplitRow`:
    ```csharp
    bool canSplitRow = !inHeader
        && (row.CanSplit || (!hasNestedTables && row.Height > contentBottom - pageTop && !row.ExactHeight))
        && !(table.KeepTogether && totalTableHeight <= contentBottom - pageTop);
    ```
  - This ensures that table headers are always placed intact and never split across pages.
- Result: `tdf142694-1.odt` page count now matches LibreOffice exactly (7 vs 7 pages). Visual mismatch score dropped from 991.42 to 812.66 (rank 3 to rank 9, blur diff down to 3.6%).

---

## 3. Rank 1: `odfimport/data/ooo77837-1.odt`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/odfimport/odfimport.cxx testMoveSubTree` (OOo bug #77837).
- Upstream test fixture verifies crash avoidance during `MoveSubTree` when endnotes embed `<text:bibliography>` and `<text:alphabetical-index>` sections.
- In LibreOffice 24.2.7.2 (the reference runner baseline), an extra blank 4th page is generated (`ink bbox: None`, 0 characters, 0 text lines, 100% blank white pixels).
- In modern LibreOffice (tested with 26.2.6.3), headless export produces **3 pages**, which matches broilerdoc's 3 pages identically.
- Plain text projection matches 100% across all 3 content pages (0 diff lines).

---

## 4. Rank 2: `layout/data/tdf152106.odt`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/layout/layout6.cxx testTdf152106` (TDF bug #152106).
- The document contains a 2-column section (`Sect1`) across 25 paragraphs with floating frames `Frame1` (style `fr2`, width 6.015 cm, height 11.396 cm, x=6.833 cm, y=1.302 cm) and `Frame2` (style `fr1`).
- `Frame1` is horizontally anchored at `x=193.69 pt` with width `170.5 pt`, spanning across the column boundary (`x=306 pt`) and straddling both Column 1 and Column 2 from `y=36.9 pt` to `y=360 pt`.
- In LibreOffice:
  - Both Column 1 and Column 2 on page 1 wrap text around `Frame1`, narrowing text lines and producing 86 lines on page 1, which pushes subsequent text across 4 pages.
- In Broilerdoc:
  - `Frame1` is anchored to paragraph 4. Under broilerdoc's single forward pass layout architecture, paragraph-anchored shape exclusions are only registered when the layout reaches paragraph 4 (`shape.ParagraphIndex == paragraphIndex`), so paragraphs 0–3 do not wrap around `Frame1`.
  - In `src/Broiler.Documents.Odt/OdtReader.cs`, absent `style:run-through` previously set `BehindText = true`, which prevented floating exclusions from being registered in `_wrap`.

---

## 5. Rank 5: `ooxmlexport/data/tdf146955.odt`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/ooxmlexport/ooxmlexport17.cxx testTdf146955` (TDF bug #146955).
- Upstream test comment in LibreOffice core explicitly notes: `// broken test document?`.
- The document contains 320 frames with `text:anchor-type="page"` stating `text:anchor-page-number` up to 367.
- The body text in `<office:text>` finishes around page 28.
- In LibreOffice 24.2.7.2:
  - Pages 29 and 30 contain no body text and no page-anchored frames; only the running header (`x \n x`) appears on pages 29 and 30 (`ink bbox: y=[60, 65]`).
  - Frames anchored to page numbers above 30 resume on page 31.
- In Broilerdoc:
  - Minor layout spacing differences cause the remaining body paragraphs to spill slightly into page 29 (`ink bbox: y=[59, 771]`), creating a pixel mismatch on page 29 (worst page 29).

---

## 6. Rank 9: `layout/data/tox-update-wrong-pages.odt`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/layout/layout6.cxx testTdf155324` (TDF bug #155324).
- The document tests dynamic table of contents page numbering with 81 TOC entries on ISO A5 paper (419.75 x 595.45 pt) with 20 mm margins (56.69 pt) and a 0.4 in (28.8 pt) footer with 0.2 in (14.4 pt) top margin.
- In LibreOffice:
  - Available page height is ~438.87 pt. Exactly 20–21 TOC entries fit per page, so all 80 TOC entries complete cleanly on page 4.
  - Page 5 begins the body chapters with explicit page breaks, totaling exactly 13 pages.
- In Broilerdoc:
  - Font line height for Liberation Serif 12 pt includes line gap (13.8 pt vs 13.29 pt intrinsic), which over 21 lines adds ~10.7 pt of height.
  - Exactly 18–19 entries fit per page, pushing 5 TOC entries onto page 5.
  - Consequently, the body chapter page break occurs on page 6 instead of page 5, shifting all subsequent pages and resulting in 15 pages (a 2-page shift).

---

## 7. Ranks 6, 7, 8, 10, 12, 13, 14: DrawingML Charts (`.docx`)

### Samples
- `ooxmlexport/data/tdf128207.docx` (rank 6, severity 965.73, 1 vs 1 page)
- `layout/data/tdf130031.docx` (rank 7, severity 964.92, 1 vs 1 page)
- `layout/data/testAreaChartNumberFormat.docx` (rank 8, severity 961.90, 1 vs 1 page)
- `layout/data/tdf130380.docx` (rank 10, severity 953.78, 1 vs 1 page)
- `odfexport/data/tdf134987.docx` (rank 12, severity 943.67, 1 vs 1 page)
- `layout/data/tdf129054.docx` (rank 13, severity 943.57, 1 vs 1 page)
- `layout/data/tdf134866.docx` (rank 14, severity 943.18, 1 vs 1 page)

### Findings and Root Causes
- Upstream tests:
  - `ooxmlexport14.cxx::testTdf128207`
  - `layout.cxx::testTdf130031`
  - `layout.cxx::testAreaChartNumberFormat`
  - `layout.cxx::testTdf130380`
  - `odfexport14.cxx::testTdf134987`
  - `layout.cxx::testTdf129054`
  - `layout.cxx::testTdf134866`
- All 7 documents contain DrawingML `<c:chart>` chart definitions without embedded raster fallback images (`a:blip`).
- In `src/Broiler.Documents.Docx/DocxImages.cs`:
  - `FindDescendant(wrapper, DocxNamespaces.Drawing + "blip")` returns null because chart elements do not contain raster bitmaps.
  - Emits diagnostic `docx.image.shape` ("A DOCX drawing held no embedded picture and was skipped.").
- In all 7 samples:
  - Document page count matches LibreOffice identically (1 vs 1 page).
  - Semantic text projection matches 100%.
  - Visual pixel difference is entirely due to DrawingML vector chart rendering not being supported in broilerdoc.

---

## 8. Rank 11: `ooxmlexport/data/tdf169024_verticalTopFrame.odt`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/ooxmlexport/ooxmlexport21.cxx testTdf169024_verticalTopFrame` (TDF bug #169024).
- The document contains an inline frame (`text:anchor-type="as-char"`) with `style:vertical-pos="top"` and `style:vertical-rel="line"`.
- Previously, in `src/Broiler.Documents.Odt/OdtReader.cs`, inline frame alignment mapped only `"middle"` to `BaselineAlignment.Center` and default to `BaselineAlignment.Baseline`.
- As a result, the frame was aligned relative to the font baseline, causing the frame to sit ~0.85 inches below the line top, resulting in severity 950.97.

### Fix
- In `src/Broiler.Documents.Odt/OdtReader.cs`:
  - Inspected `style:vertical-rel` and `style:vertical-pos`. When `vertical-rel="line"` and `vertical-pos="top"`, mapped to top alignment:
    ```csharp
    string? verticalRel = (string?)graphicProps?.Attribute(OdtNamespaces.Style + "vertical-rel");
    string? verticalPos = (string?)graphicProps?.Attribute(OdtNamespaces.Style + "vertical-pos");
    BaselineAlignment alignment = (verticalRel == "line" && verticalPos == "top")
        ? BaselineAlignment.Top
        : verticalPos == "middle" ? BaselineAlignment.Center : BaselineAlignment.Baseline;
    ```
- Result: 100% PASS in `Broiler.Documents.Office` (severity dropped from 950.97 to 0.0).

---

## 9. Rank 15: `uiwriter/data/tdf132321.odt`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/uiwriter/uiwriter6.cxx testTdf132321` (TDF bug #132321).
- The document contains 1 page with 2 short lines of text in Liberation Serif.
- Broilerdoc and LibreOffice both render 1 page with identical text lines.
- Visual blur difference is 13.4% due to subtle font hinting/rasterization antialiasing differences on the letterforms.

---

## 10. Rank 16: `rtfimport/data/section-pagebreak.rtf`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/rtfimport/rtfimport.cxx testSectionPagebreak`.
- The document contains a sequence of RTF section breaks with `\sbknone` (continuous section break), followed by `\page\sect` (explicit page break immediately followed by a section break).
- In `src/Broiler.Documents.Rtf/RtfReader.cs`:
  - When encountering `\sect`, `_bodySectionBreak` was previously overwritten unconditionally to `_builder.PageBreakPending ? BodySectionBreak.Continuous : BodySectionBreak.Page;`, wiping out the sticky continuous section break state `\sbknone`.
  - Furthermore, when `\page` was immediately followed by `\sect`, `_builder.PageBreakPending` interacted with `BreakPage()` in `ApplyBodySectionBreak()`, leading to duplicate empty page breaks or misaligned section breaks (resulting in 5 pages instead of 4 pages).

### Fix
- In `src/Broiler.Documents.Rtf/RtfReader.cs`:
  - Track sticky `_sectionBreak` across sections, defaulting to `BodySectionBreak.Page`, set by `\sbknone` (Continuous), `\sbkcol` (Column), `\sbkpage` / `\sbkodd` / `\sbkeven` (Page).
  - In `\sect` handler, preserve `_bodySectionBreak = _sectionBreak;`.
  - In `ApplyBodySectionBreak()`, if `_builder.PageBreakPending` is true before a page section break, flush the current paragraph before executing the page break, avoiding duplicate empty pages.
- Result: 100% PASS in `Broiler.Documents.Office` (4 vs 4 pages, pixel match, severity dropped from 940.22 to 0.0).

---

## 11. Rank 17: `layout/data/2024-01-19_merged-cells-on-separate-pages-vertical-alignment.odt`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/layout/layout.cxx testMergedCellsOnSeparatePagesVerticalAlignment`.
- The document contains a 75-row table where the first cell has `table:number-rows-spanned="51"`, but its content is only a single 1-line paragraph ("hello", height 13.8 pt).
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs`:
  - Table row grouping previously calculated `spanEnd = Math.Max(spanEnd, r + cell.RowSpan);` for any cell with `RowSpan > 1`, unconditionally grouping all 51 rows together as a keep-together block.
  - The 51 rows totaled 703.7 pt in height. Together with the two preceding paragraphs on page 1, the total exceeded the page capacity (788.0 pt vs 785.2 pt).
  - Because the whole 51-row block was treated as inseparable, the paginator moved the entire table to Page 2, leaving Page 1 completely blank (3 pages total in broilerdoc vs 2 pages in LibreOffice, severity 940.14).

### Fix
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs`:
  - Made merged-cell span grouping content-aware: calculate the actual number of rows required to accommodate `cellContent.Height`:
    ```csharp
    if (cell.RowSpan > 1 && cell.Content is DocumentCellContent cellContent && cellContent.Height > 0)
    {
        double accumulated = 0;
        for (int r = i; r < Math.Min(composedRows.Count, i + cell.RowSpan); r++)
        {
            accumulated += composedRows[r].Height;
            spanEnd = Math.Max(spanEnd, r + 1);
            if (accumulated >= cellContent.Height - 0.01)
                break;
        }
    }
    ```
- Result: 100% PASS in `Broiler.Documents.Office` (2 vs 2 pages, pixel match to within 1 px, severity dropped from 940.14 to 0.0).

---

## 12. Rank 18: `layout/data/U-min.fodt`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/layout/layout.cxx testUmin`.
- The document contains a conditional section `<text:section text:display="condition" text:condition="ooow:userVar == 1">` containing body content.
- A preceding `<text:user-field-decl office:value-type="float" office:value="1" text:name="userVar"/>` declares `userVar` with value `1`.
- In `src/Broiler.Documents.Odt/OdtReader.cs`:
  - Previously, any section with `text:display="condition"` was unconditionally treated as hidden (`_hiddenSectionDepth++`), so the section and its contents were skipped entirely, leaving an empty document (severity 940.03).

### Fix
- In `src/Broiler.Documents.Odt/OdtReadContext.cs` & `OdtReader.cs`:
  - Added `ReadVariables()` to extract all `<text:user-field-decl>` and `<text:variable-decl>` name/value pairs into context.
  - Implemented `EvaluateCondition()` supporting boolean literals (`"true"`, `"false"`, `"1"`, `"0"`) and binary comparison operators (`==`, `!=`, `<`, `<=`, `>`, `>=`) with variable resolution and numeric/string evaluation.
  - In section processing, when `text:display="condition"` is specified, evaluate the condition: if true, the section is displayed (`_hiddenSectionDepth` is not incremented).
- Result: 100% PASS in `Broiler.Documents.Office` (1 vs 1 page, 100% pixel match, severity dropped from 940.03 to 0.0).

---

## 13. Rank 19: `ooxmlexport/data/tdf58944-repeating-table-header.docx`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/ooxmlexport/ooxmlexport.cxx testTdf58944_repeatingTableHeader` (TDF bug #58944).
- The document contains a 17-row table where every row is marked with `<w:tblHeader/>`.
- In `src/Broiler.Documents.Docx/DocxTableReader.cs`:
  - A previous change added: `pageBreak |= !inCell && position is null && rows.Count > 1 && rows.All(row => row.IsHeader);`.
  - This forced a spurious page break before the table, placing only the single word "Test" on Page 1 and pushing the table across Pages 2 and 3 (3 pages vs LO 2 pages, severity 938.93).
  - In Word and LibreOffice (per LO bug `tdf#138020` / commit `f431a2b02fbe85bd6e83491e5bcb26149df325c3`), an all-header table does NOT force a fresh page; instead, header repetition on continuation pages is suppressed (`HeaderRowCount` returns 0), which `DocumentLayout.cs` already handles (`return count == table.Rows.Count ? 0 : count;`).

### Fix
- In `src/Broiler.Documents.Docx/DocxTableReader.cs`:
  - Removed the spurious `pageBreak |= ... rows.All(row => row.IsHeader)` check.
- In `src/tests/Broiler.Documents.Docx.Tests/DocxPageBreakTests.cs`:
  - Updated `A_Table_Marked_Entirely_As_Headers_Starts_A_Fresh_Page` to `A_Table_Marked_Entirely_As_Headers_Does_Not_Force_A_Fresh_Page`, asserting `Assert.False(table.PageBreakBefore)`.
- Result: 100% PASS in `Broiler.Documents.Office` (2 vs 2 pages, ink bboxes match LO within 1–2 px, severity dropped from 938.93 to 0.0).

---

## 14. Rank 20: `rtfimport/data/tblrepeat.rtf`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/rtfimport/rtfimport.cxx testTblrepeat`.
- The document contains a 2-page table with 122 cells and repeating headers using RTF border keyword `\brdrtbl` (e.g. `\clbrdrt\brdrtbl`).
- In `src/Broiler.Documents.Rtf/RtfTableBuilder.cs`:
  - `ApplyBorderWord` and `ApplyRowBorderWord` did not handle `brdrtbl`.
  - Per the RTF specification, `\brdrtbl` specifies no border for that cell side. Missing this keyword caused it to fall through to `default: border.Drawn = true;`, painting 0.5pt black border lines around all 122 cells on both pages (severity 915.73, blur diff 21.7%).

### Fix
- In `src/Broiler.Documents.Rtf/RtfTableBuilder.cs`:
  - Added `case "brdrtbl": border.Drawn = false; break;` to both `ApplyBorderWord` and `ApplyRowBorderWord`.
- Result:
  - Spurious borders removed; Page 1 is an exact pixel match (0 diff).
  - Blur diff dropped from 21.7% to 0.18% (0.0018, "blurred clean", "coverage exact").
  - The remaining 0.18% difference on Page 2 is an upstream LibreOffice 24.2 reference defect: LO 24.2 failed to render the 3 repeating header rows on page 2 (leaving page 2 100% blank), whereas modern LibreOffice 26.2 and broilerdoc correctly render the repeating header rows on page 2.
