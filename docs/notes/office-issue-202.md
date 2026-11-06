# Office Conformance Issue 202

Investigation and fixes for five high-impact differences identified in [issue #202](https://github.com/Broiler-Platform/Broiler.Documents/issues/202):

1. `uiwriter/data/tdf104425.odt` (rank 16, severity 952.98): 4 vs 3 pages, table row with large minimum height exceeding available page height splitting across pages and trailing soft page break.
2. `ooxmlexport/data/tdf172169_intraLinespacing.odt` (rank 17, severity 949.54): Semantic text difference, ODT `loext:content-control` inline and block elements skipped.
3. `rtfexport/data/tdf158586_pageBreak0.rtf` (rank 23, severity 941.02): 2 vs 1 pages, RTF explicit page break (`\page`) inside a text frame (`\wraptight`) breaking the host body page.
4. `rtfexport/data/fdo81892.rtf` (rank 25, severity 940.52): 1 vs 2 pages, RTF opening page break (`\page`) before paragraph text failing to open an empty first page.
5. `ooxmlexport/data/section_break_after_table.docx` (rank 26, severity 940.25): 3 vs 2 pages, empty paragraph holding next-page section break following a table preserved as an extra blank page.

---

## 1. Rank 16: `uiwriter/data/tdf104425.odt`

### Findings and Root Causes
- Upstream test: `uiwriter.cxx::testTdf104425` (TDF bug #104425, LibreOffice commit `6f5024de2e1a5cc533527e45b33d9a415467c48d`).
- The document contains a top-level single-cell table whose row declares a minimum height of 70 cm (`style:min-row-height="70cm"`), enclosing three nested tables of heights 20 cm, 25 cm, and 5 cm on an A4 page with 2 cm margins (available body height of 25.7 cm per page).
- In LibreOffice, this table splits across exactly 3 pages: Page 1 takes 25.7 cm (Table 2), Page 2 takes 25.7 cm (Table 4), and Page 3 takes the remaining 18.6 cm (Table 3), with the total row height summing to $25.7 + 25.7 + 18.6 = 70.0\text{ cm}$.
- In Broilerdoc, two issues prevented correct layout:
  1. In `src/Broiler.Documents.Pagination/DocumentLayout.cs`, `RowBox.SplitAt` previously aborted when `room < MinimumHeight && !allowStranding`, refusing to split a row whose minimum height alone exceeded available page height. Furthermore, when row content completed before the page cut but the remaining minimum height exceeded available room, `SplitAt` did not split the remaining floor.
  2. In `src/Broiler.Documents.Odt/OdtReader.cs`, the document concludes with an empty trailing paragraph `<text:p text:style-name="Standard"><text:soft-page-break/></text:p>`. Because `context.UsesSoftPageBreaks` was true and the paragraph contained only `<text:soft-page-break/>`, `OdtReader` unconditionally set `paragraphStyle.PageBreakBefore = true`, which forced a new page (Page 4) after the table on Page 3.

### Fix
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs`:
  - Allowed row splitting when `room < MinimumHeight` if `allowStranding` is true (i.e., at column or page start).
  - In `RowBox.SplitAt`, passed the remaining minimum height to `rest`: `rest.MinimumHeight = Math.Max(0, MinimumHeight - room)`.
  - When all cell content pieces fit on the current page (`!carried`) but `MinimumHeight > room`, split the empty floor so the fitted part takes `room` and the remainder continues to the next page with `MinimumHeight - room`.
  - Maintained `rest.Height = Math.Max(rest.Height, Math.Max(remaining, rest.MinimumHeight))`.
- In `src/Broiler.Documents.Odt/OdtReader.cs`:
  - Restricted setting `PageBreakBefore = true` on empty paragraphs containing only `<text:soft-page-break/>` to non-trailing paragraphs (`paragraph.ElementsAfterSelf().Any()`), avoiding spurious blank pages at document end after flowing tables.
- Added unit tests:
  - `A_row_with_minimum_height_exceeding_page_height_splits_across_pages` in `src/tests/Broiler.Documents.Pagination.Tests/DocumentLayoutRowSplitTests.cs`.
  - `Soft_Page_Break_On_Trailing_Empty_Paragraph_Does_Not_Force_Page_Break` in `src/tests/Broiler.Documents.Odt.Tests/OdtReaderTests.cs`.
- Result: `tdf104425.odt` renders exactly 3 pages (Page 1: 6,010 inked pixels, Page 2: 5,752 inked pixels, Page 3: 3,704 inked pixels), matching LibreOffice.

---

## 2. Rank 17: `ooxmlexport/data/tdf172169_intraLinespacing.odt`

### Findings and Root Causes
- Upstream test: `ooxmlexport.cxx::testTdf172169_intraLinespacing` (TDF bug #172169).
- Paragraph 5 in the document contains:
  ```xml
  <text:p text:style-name="P1">... test for content controls, because they are marked as PORGRP_TOXREF, and <loext:content-control><text:span text:style-name="T1">I</text:span></loext:content-control> can imagine ...</text:p>
  ```
- Broilerdoc dumped: `"... and  can imagine ..."` — the word "I" was missing.
- In `src/Broiler.Documents.Odt/OdtReader.cs`, neither `ReadBlockContent` nor `ReadInlineContent` handled elements in the `urn:org:documentfoundation:names:experimental:office:xmlns:loext:1.0` namespace with local name `content-control`. Unknown elements fell through and were ignored, dropping all text inside LibreOffice content controls.

### Fix
- In `src/Broiler.Documents.Odt/OdtReader.cs`:
  - In `ReadBlockContent`, added a handler for `OdtNamespaces.LibreOffice + "content-control"` and `name.LocalName == "content-control"`, recursively delegating to `ReadBlockContent(element.Elements(), context, list, depth + 1)`.
  - In `ReadInlineContent`, added a handler for `content-control` which resolves span styles via `text:style-name` and recursively delegates to `ReadInlineContent(element, context, contentStyle, depth + 1)`.
- Added unit tests `Reads_Inline_Content_Control` and `Reads_Block_Content_Control` in `src/tests/Broiler.Documents.Odt.Tests/OdtReaderTests.cs`.
- Result: `tdf172169_intraLinespacing.odt` extracts the full text `"... and I can imagine ..."` with 0 missing characters.

---

## 3. Rank 23: `rtfexport/data/tdf158586_pageBreak0.rtf`

### Findings and Root Causes
- Upstream test: `rtfexport.cxx::testTdf158586_pageBreak0` (TDF bug #158586).
- The document defines a tight-wrapped text frame followed by an explicit page break inside the frame:
  `\pard\plain \wraptight \page \sect \sectd \sbknone \pard\plain First page\par`
- LibreOffice renders 1 page. Broilerdoc rendered 2 pages.
- In RTF, `\page` inside a text frame (`\wraptight`) was received by `RtfReader.PageBreak()`. While the frame itself is extracted as a floating `DocumentShape` that does not paginate, `PageBreak()` invoked `FinishFrame()` and broke the main document body flow, pushing "First page" onto Page 2.
- Additionally, `\wraptight` and `\wrapdefault` keywords were unhandled in `RtfReader.ApplyParagraphProperty`, defaulting to `RtfFrameWrap.Auto`.

### Fix
- In `src/Broiler.Documents.Rtf/RtfFrame.cs`, added `Tight` to `RtfFrameWrap`.
- In `src/Broiler.Documents.Rtf/RtfReader.cs`:
  - Mapped `\wraptight` to `RtfFrameWrap.Tight` and `\wrapdefault` to `RtfFrameWrap.Auto`.
  - In `PageBreak()`, checked `if (_state.Frame.IsFrame) return;`. Explicit page breaks occurring inside a text frame are suppressed because text frames have no paginated body flow.
- Added unit test `A_Page_Break_Inside_A_Frame_Is_Lost` in `src/tests/Broiler.Documents.Rtf.Tests/RtfFrameTests.cs`.
- Result: `tdf158586_pageBreak0.rtf` renders exactly 1 page, matching LibreOffice.

---

## 4. Rank 25: `rtfexport/data/fdo81892.rtf`

### Findings and Root Causes
- Upstream test: `rtfexport.cxx::testFdo81892` (FreeDesktop bug #81892).
- The document begins with an explicit page break before any text:
  `{\rtf1 ... \pard \qc \page Performance \par ...}`
- LibreOffice renders 2 pages: Page 1 is an empty title page, and "Performance" begins Page 2.
- Broilerdoc rendered 1 page: `\page` was set as `PageBreakPending = true` on `_paragraphs[0]`. In `DocumentLayout.cs`, a leading `PageBreakBefore` on the document's very first paragraph is ignored by design (`setup.Continuous || (currentLines.Count > 0 || floatLines.Count > 0)`), collapsing the empty opening page.

### Fix
- In `src/Broiler.Documents.Rtf/RtfReader.cs`, in `Accumulator.BreakPage`, checked if `_current.Length > 0 || (_paragraphs.Count == 0 && !PageBreakPending)`.
- When an explicit `\page` break occurs before any text has been emitted and no prior paragraph exists, `EndParagraph(style)` is called to close and emit the empty opening paragraph (Page 1). The pending page break is then assigned to the following paragraph ("Performance"), which starts on Page 2.
- Updated existing unit test `A_Page_Before_Any_Text_Opens_A_Blank_Page_And_Breaks_On_The_Following_Paragraph` and added `Opening_Page_Break_Before_Paragraph_Renders_Two_Pages` in `src/tests/Broiler.Documents.Rtf.Tests/RtfPageBreakTests.cs`.
- Result: `fdo81892.rtf` renders exactly 2 pages, matching LibreOffice.

---

## 5. Rank 26: `ooxmlexport/data/section_break_after_table.docx`

### Findings and Root Causes
- Upstream test: `ooxmlexport17.cxx::testSectionBreakAfterTable`.
- The document contains a table followed immediately by an empty paragraph holding a next-page section break (`<w:p><w:pPr><w:sectPr><w:type w:val="nextPage"/></w:sectPr></w:p>`), followed by another section on page 2.
- LibreOffice renders 2 pages. Broilerdoc rendered 3 pages.
- In WordprocessingML, Word requires a trailing `<w:p>` after a table to anchor the section properties (`w:sectPr`). In `src/Broiler.Documents.Docx/DocxReader.cs`, `IsDroppableSectionMark` preserved any empty paragraph holding `sectPr` if preceded by a table, in order to allow continuous section breaks after tables to close the section.
- For next-page section breaks (`nextPage`, `oddPage`, `evenPage`), keeping this empty paragraph between the table and the new section generated an extra blank page containing only that empty paragraph.

### Fix
- In `src/Broiler.Documents.Docx/DocxReader.cs`, refined `IsDroppableSectionMark`:
  - When preceded by a table (`tbl`), checked `sectPr/w:type/@w:val`.
  - Only continuous section breaks (`type is not ("nextPage" or "oddPage" or "evenPage")`) are preserved after a table. Next-page section breaks drop the empty paragraph so that the new page begins directly with the following section content.
- Added unit test `A_NextPage_Section_Mark_After_A_Table_Is_Dropped` in `src/tests/Broiler.Documents.Docx.Tests/DocxColumnTests.cs`.
- Result: `section_break_after_table.docx` renders exactly 2 pages with 0 blank pages, matching LibreOffice.
