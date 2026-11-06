# Office Conformance Issue 201

Investigation and fixes for seven high-impact differences identified in [issue #201](https://github.com/Broiler-Platform/Broiler.Documents/issues/201):

1. `ooxmlexport/data/fdo55381.docx` (rank 1, severity 1014.43): 4 vs 3 pages, framed paragraph section consuming trailing section page break.
2. `ooxmlexport/data/cjklist44.docx` (rank 8, severity 969.19): DOCX list numbering format `koreanDigital2`.
3. `ooxmlimport/data/tdf119039_bad_embedded_compound.docx` (rank 10, severity 966.10): `<w:object>` embedded compound object fallback preview image reading.
4. `odfimport/data/dateFormFormats.odt` (rank 16, severity 958.64): ODF `form:date` control value and character-anchored (`as-char`) shapes advancing line height and pagination.
5. `ooxmlexport/data/tdf146984_anchorInShape.docx` (rank 21, severity 946.00): Manual page break immediately preceding a drawing shape in the same paragraph.
6. `rtfexport/data/tdf158586_pageBreak1.rtf` and `rtfexport/data/tdf158586_pageBreak1_header.rtf` (ranks 27 & 28, severity 941.76): 2 vs 1 pages, RTF `\page\sect` at document start before text.
7. `ooxmlexport/data/tdf126287.docx` (rank 30, severity 940.91): 2 vs 3 pages, trailing line breaks (`\u2028`) at section end trimmed.

---

## 1. Rank 1: `ooxmlexport/data/fdo55381.docx`

### Findings and Root Causes
- Upstream test: `ooxmlexport.cxx::testFdo55381` (FreeDesktop bug #55381).
- The document has 4 pages in LibreOffice 24.2. Broilerdoc rendered 3 pages and reported the diagnostic `docx.pagebreak.trailing: "A page break on the document's last paragraph was ignored; the model keeps no trailing blank page."`.
- Section 3 of the document contains only framed paragraphs (`<w:framePr>`), which `DocxFrameAccumulator` redirects out of the body text flow and into a floating `DocumentShape`.
- The preceding section break placed a pending section page break on the builder (`HoldPageBreak(PageBreakScope.Section)`).
- Because all paragraphs in section 3 were framed shapes, no body paragraphs were emitted for section 3. The held page break remained pending until the end of the document, where it was treated as a trailing page break and dropped, leaving the framed shape anchored to page 3 rather than starting page 4.

### Fix
- In `src/Broiler.Documents.Docx/DocxDocumentBuilder.cs`, exposed `HasPendingPageBreak` and `HasShapeAnchoredAt(int index)`.
- In `src/Broiler.Documents.Docx/DocxReader.cs`, after `context.Frames?.Finish(context)`, checked whether `builder.HasPendingPageBreak && builder.HasShapeAnchoredAt(builder.CurrentParagraphIndex)`. If true, synthesized an empty paragraph (`StartParagraph()` / `FinishParagraph()`), consuming the held section page break and ensuring the frame is anchored to page 4.
- Added unit test `Framed_Section_Following_Page_Break_Preserves_Break_And_Anchors_Shape` in `src/tests/Broiler.Documents.Docx.Tests/DocxParagraphFrameTests.cs`.
- Result: `fdo55381.docx` renders exactly 4 pages with 0 diagnostics, resolving the 1014.43 severity difference.

---

## 2. Rank 8: `ooxmlexport/data/cjklist44.docx`

### Findings and Root Causes
- Upstream test: `ooxmlexport.cxx::testCjklist44`.
- The document uses `w:numFmt="koreanDigital2"` for list numbering.
- Previously, `koreanDigital2` was mapped to `ListNumberFormat.IdeographCounting`. In `ListNumberer.cs`, `ChineseCounting` formats numbers using traditional Asian counting with magnitude markers (一, 二, ..., 十, 十一, ..., 二十).
- In ECMA-376 Part 1 §17.18.59 (`ST_NumberFormat`), `koreanDigital2` specifies that each digit is converted directly to its corresponding Hanja numeral (0: 〇, 1: 一, 2: 二, ..., 9: 九) without positional magnitude words. Values 10, 11, and 20 are formatted as "一〇", "一一", and "二〇", not "十", "十一", and "二十".
- This caused 21 semantic text differences against LibreOffice.

### Fix
- Added `KoreanDigital2` to `ListNumberFormat` in `src/Broiler.Documents.Model/ListDefinition.cs`.
- Implemented `KoreanDigital2(int value)` in `src/Broiler.Documents.Model/ListNumberer.cs`: converts each decimal digit of `value` to its Hanja digit `〇一二三四五六七八九`.
- In `src/Broiler.Documents.Docx/DocxNumbering.cs`, mapped `"koreanDigital2" => ListNumberFormat.KoreanDigital2`.
- In `src/Broiler.Documents.Docx/DocxWriter.cs`, mapped `ListNumberFormat.KoreanDigital2 => "koreanDigital2"`.
- Added unit tests `KoreanDigital2_Formats_Digits_Directly_To_Hanja` and round-trip verification in `src/tests/Broiler.Documents.Docx.Tests/DocxNumberingTests.cs`.
- Result: `cjklist44.docx` text output matches LibreOffice exactly with 0 text differences.

---

## 3. Rank 10: `ooxmlimport/data/tdf119039_bad_embedded_compound.docx`

### Findings and Root Causes
- Upstream test: `ooxmlimport.cxx::testTdf119039`.
- The document contains an embedded compound OLE object (`<w:object>`).
- LibreOffice renders an image preview of the embedded object (114,816 non-white pixels). Broilerdoc produced a completely blank page (0 non-white pixels) and reported `docx.skip.embedded: "Embedded DOCX object content was skipped."`.
- In `DocxReader.cs`, `<w:object>` was unconditionally skipped without inspecting its contents. In WordprocessingML, embedded OLE objects contain a fallback preview representation inside `<w:object>` using `<v:shape>/<v:imagedata>`, `<w:pict>`, `<w:drawing>`, or `<mc:AlternateContent>`.

### Fix
- In `src/Broiler.Documents.Docx/DocxReader.cs`, in `ReadRunContent`, updated the `<w:object>` handler:
  - If `<w:drawing>` is present, delegates to `ReadPicture`.
  - If `<w:pict>` is present, delegates to `ReadPicture`.
  - If `<mc:AlternateContent>` is present, processes the `<mc:Choice>` or `<mc:Fallback>` child elements.
  - If descendant `<v:imagedata>` or `<v:shape>` elements are present, delegates to `ReadPicture`.
  - If no visual preview is found, reports `docx.skip.embedded`.
- Added unit test `Reads_Embedded_Object_Fallback_Preview_Image` in `src/tests/Broiler.Documents.Docx.Tests/DocxImageTests.cs`.
- Result: `tdf119039_bad_embedded_compound.docx` renders 114,816 inked pixels with 1 embedded image loaded, matching LibreOffice.

---

## 4. Rank 16: `odfimport/data/dateFormFormats.odt`

### Findings and Root Causes
- Upstream test: `odfimport.cxx::testDateFormFormats`.
- The document contains multiple date form controls (`form:date`) and text paragraphs across 2 pages (3,384 non-white pixels on page 1, 2,452 non-white pixels on page 2 in LibreOffice).
- Broilerdoc rendered 1 completely blank page (0 non-white pixels).
- Investigation revealed three root causes:
  1. In `src/Broiler.Documents.Odt/OdtReader.cs`, `ReadControl` only recognized `form:fixed-text` and skipped all other control types including `form:date` and `form:date-field`.
  2. Character-anchored controls (`text:anchor-type="as-char"`) were not marked as `IsAsCharacter = true` because `IsAsCharacter` required a `<draw:text-box>` descendant.
  3. In `src/Broiler.Documents.Pagination/DocumentLayout.cs`, line layout did not expand `line.Height` for character-anchored shapes. Because `line.Height` remained at default font line height (~12pt) despite the shapes being 20–30pt tall, subsequent lines and paragraphs overlapped and failed to advance the page cursor, preventing page breaks.

### Fix
- In `src/Broiler.Documents.Odt/OdtReader.cs`:
  - Extended `ReadControl` to support `form:date` and `form:date-field`, extracting values from `form:current-value` or `form:value`.
  - Set `IsAsCharacter = (string?)drawing.Attribute(OdtNamespaces.Text + "anchor-type") == "as-char"` regardless of whether a text box is present.
- In `src/Broiler.Documents.Pagination/LayoutTypes.cs` and `DocumentLayout.cs`:
  - Made `LayoutLine.Height` have an internal setter.
  - In paragraph line layout, when character-anchored shapes are present on line 0, updated `line.Height = Math.Max(line.Height, asCharHeight)` so that character-anchored shapes advance the vertical cursor and break pages when exceeding page bounds.
- Added unit tests:
  - `Date_Form_Controls_Are_Read_As_Shapes_With_Current_Value` in `src/tests/Broiler.Documents.Odt.Tests/OdtNonPrintingTests.cs`.
  - `As_Character_Shape_Advances_Line_Height_In_Paragraph` in `src/tests/Broiler.Documents.Pagination.Tests/DocumentLayoutWrappingFrameTests.cs`.
- Result: `dateFormFormats.odt` renders 2 inked pages (Page 1: 3,384 pixels, Page 2: 2,452 pixels), matching LibreOffice.

---

## 5. Rank 21: `ooxmlexport/data/tdf146984_anchorInShape.docx`

### Findings and Root Causes
- Upstream test: `ooxmlexport16.cxx::testTdf146984_anchorInShape` (TDF bug #146984).
- The document has a manual page break followed immediately by a drawing shape within paragraph 0:
  ```xml
  <w:p>
    <w:r><w:br w:type="page"/></w:r>
    <w:r><w:drawing>...</w:drawing></w:r>
  </w:p>
  ```
- LibreOffice renders 2 pages: page 1 is blank, and page 2 holds the drawing shape (142,340 non-white pixels).
- In Broilerdoc, manual inline page breaks are held in `_pageBreakPending` and were only applied when `AppendText()` was called. Because the drawing shape was read via `ReadPicture()` without any preceding text, `_pageBreakPending` remained unconsumed. The shape was anchored to paragraph 0 on page 1, placing all 142,340 inked pixels on page 1.

### Fix
- In `src/Broiler.Documents.Docx/DocxDocumentBuilder.cs`, extracted `EnsureBreakApplied()` from `AppendText()` to consume any pending inline page/column break before subsequent content.
- In `src/Broiler.Documents.Docx/DocxReader.cs`, called `context.Builder.EnsureBreakApplied()` at the start of `ReadPicture()`.
- Added unit test `Drawing_Immediately_Following_Page_Break_Applies_Break_Before_Shape` in `src/tests/Broiler.Documents.Docx.Tests/DocxPageBreakTests.cs`.
- Result: Page 1 is 100% blank (0 non-white pixels) and page 2 has 142,340 inked pixels, matching LibreOffice.

---

## 6. Ranks 27 & 28: `rtfexport/data/tdf158586_pageBreak1.rtf` and `tdf158586_pageBreak1_header.rtf`

### Findings and Root Causes
- Upstream tests: `rtfexport5.cxx::testTdf158586_pageBreak1` and `testTdf158586_pageBreak1_header` (TDF bug #158586).
- Both documents begin with:
  ```rtf
  {\rtf1\ansi
  \page
  \sect\sbknone
  Hello
  \par
  }
  ```
- LibreOffice renders 2 pages (an empty page 1 followed by "Hello" on page 2). Broilerdoc rendered only 1 page.
- In `src/Broiler.Documents.Rtf/RtfReader.cs`, `\page` set `PageBreakPending = true`. When `\sect` occurred before any text was encountered (`_builder.ParagraphCount == 0`), `_builder.EndParagraph(_state.Para)` closed paragraph 0, spending the pending page break on paragraph 0.
- In `DocumentLayout.cs`, `PageBreakBefore` on paragraph 0 is intentionally suppressed when no prior content exists on the page (paragraph 0 is already on the first page). This discarded the break and collapsed the two pages into one.

### Fix
- In `src/Broiler.Documents.Rtf/RtfReader.cs`:
  - Added `RestorePendingBreaks(bool page, bool column)` to `Accumulator`.
  - In `case "sect":`, when `_builder.ParagraphCount == 0`, preserved the pending page/column breaks onto subsequent sections (`_builder.RestorePendingBreaks(heldPageBreak, heldColumnBreak)`).
- When following section content ("Hello") begins, the pending page break is spent on the section's first paragraph, giving it `PageBreakBefore = true` and starting it on page 2.
- Added unit test `Page_Before_Section_Break_At_Document_Start_Breaks_On_Following_Section` in `src/tests/Broiler.Documents.Rtf.Tests/RtfPageBreakTests.cs`.
- Result: Both documents render exactly 2 pages with "Hello" on page 2, matching LibreOffice.

---

## 7. Rank 30: `ooxmlexport/data/tdf126287.docx`

### Findings and Root Causes
- Upstream test: `ooxmlexport13.cxx::testTdf126287` (TDF bug #126287).
- LibreOffice renders 2 pages. Broilerdoc rendered 3 pages (creating an empty page 3).
- In `tdf126287.docx`, the paragraph concluding section 1 ended with an explicit line break `<w:br/>` immediately before `<w:sectPr>`.
- In Word and LibreOffice, trailing line breaks (`\u2028`) at section boundaries are ignored and do not produce an extra line that spills over into a trailing page.
- In Broilerdoc, the trailing line break remained in the paragraph text, taking up vertical space and forcing following content onto a third page.

### Fix
- In `src/Broiler.Documents.Docx/DocxDocumentBuilder.cs`, added `TrimSectionParagraphEnd()` to strip trailing line break characters (`\u2028`) from the paragraph being closed.
- In `src/Broiler.Documents.Docx/DocxReader.cs`, in `EmitParagraph()`, if `DocxSections.EndsSection(paragraph)`, called `context.Builder.TrimSectionParagraphEnd()` prior to `FinishParagraph()`.
- Added unit test `Trailing_Line_Break_At_Section_End_Is_Trimmed` in `src/tests/Broiler.Documents.Docx.Tests/DocxPageBreakTests.cs`.
- Result: `tdf126287.docx` renders exactly 2 pages instead of 3, matching LibreOffice.
