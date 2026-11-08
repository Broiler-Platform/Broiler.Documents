# Office Conformance Issue 214

Investigation and fixes for all 12 differences with severity $\ge 930$ identified in [issue #214](https://github.com/Broiler-Platform/Broiler.Documents/issues/214) (Run 136 triage against headless LibreOffice):

1. `layout/data/tdf152106.odt` (rank 1, severity 991.02 $\to$ 360.07, 4 vs 4 pages): Multi-column section with keep-with-next paragraph/table chains exceeding column capacity and `form:button` form controls.
2. `ooxmlexport/data/tdf136929_framesOfParagraph.odt` (rank 2, severity 947.20 $\to$ 0.00, 1 vs 1 page): As-character anchored frames mistakenly treated as floating wrap exclusions (`DocumentShape.Wraps`).
3. `uiwriter/data/tdf132321.odt` (rank 3, severity 940.92 $\to$ 0.00, 1 vs 1 page): As-character custom shape with `style:wrap="top-and-bottom"` creating a floating exclusion zone in its own paragraph.
4. `ooxmlexport/data/tdf141173_missingFrames.rtf` (rank 4, severity 938.36 $\to$ 0.00, 2 vs 2 pages): RTF paragraph-relative shapes in an empty opening paragraph preceding `\page \sect` were left on page 1 instead of moving with the following paragraph to page 2.
5. `ooxmlexport/data/tdf142407.docx` (rank 5, severity 937.30 $\to$ 825.64, 1 vs 1 page): Opening table `SpacingBefore` suppressed on page 1 despite `PreserveOpeningParagraphSpacing = true`.
6. `odfimport/data/tdf92586.odt` (rank 6, severity 936.66 $\to$ 0.00, 1 vs 1 page): Page background `<style:background-image style:repeat="stretch">` overridden by outer `<style:page-layout-properties style:repeat="no-repeat">`.
7. `ooxmlexport/data/tdf153613_anchoredAfterPgBreak.docx` (rank 7, severity 935.78 $\to$ 0.00, 2 vs 2 pages): Floating `<wp:anchor>` shape following an inline `<w:br w:type="page"/>` in the same paragraph consumed the page break prematurely and landed on page 2 instead of anchoring to page 1.
8. `odfimport/data/tdf165156.odt` (rank 8, severity 933.40 $\to$ 360.33, 3 vs 3 pages): ODF table cell paragraphs with `fo:keep-with-next="always"` did not propagate `KeepWithNext` to the enclosing `TableRow`, nor did `DocumentLayout` chain consecutive `KeepWithNext` table rows when measuring a preceding `KeepWithNext` heading.
9. `ooxmlexport/data/hyperlink-field.odt` (rank 9, severity 933.33 $\to$ 928.91, 3 vs 3 pages): Opening table `SpacingBefore` alignment with `PreserveOpeningParagraphSpacing`.
10. `ooxmlexport/data/tdf153964_numberingAfterBreak14.docx` (rank 10, severity 932.06 $\to$ 834.92, 4 vs 4 pages): Leading column break in a numbered list paragraph dropped `SpacingBefore`, failed to align subsequent section columns with the section's opening top margin, and did not break an overflowing second column to a new page.
11. `layout/data/tdf122607_leerzeile.odt` (rank 11, severity 931.00 $\to$ 748.90, 2 vs 1 page): Form button controls (`<form:button>`) referenced by `<draw:control>` were ignored by `OdtReader`.
12. `tiledrendering/data/shape-fill-link.fodt` (rank 12, severity 930.99 $\to$ 729.59, 1 vs 1 page): Custom shapes (`<draw:custom-shape>`) with text were exported as fallback body text in `DocumentCommands.LabelledText`, did not shrink/center their box to fit text, and did not reset `y = 0` when `style:vertical-pos="top"` on paragraph-relative anchors.

---

## 1. Ranks 2 & 3: `ooxmlexport/data/tdf136929_framesOfParagraph.odt` & `uiwriter/data/tdf132321.odt`

### Findings and Root Causes
- Both documents contain frames or custom shapes anchored `as-char` (`IsAsCharacter = true`) whose graphic styles also declare `style:wrap` (such as `parallel` or `top-and-bottom`).
- In `src/Broiler.Documents.Model/DocumentShape.cs`:
  - `Wraps` was defined as `Wrap != ShapeWrap.None` without checking `!IsAsCharacter`.
  - Consequently, `DocumentLayout` treated inline as-character shapes as floating wrap exclusions as well, pushing the paragraph's own text lines below or around the shape and producing page count mismatches and blank regions.

### Fix
- In `src/Broiler.Documents.Model/DocumentShape.cs`:
  - Updated `Wraps` to `public bool Wraps => !IsAsCharacter && Wrap != ShapeWrap.None;`.
- In `src/tests/Broiler.Documents.Model.Tests/ExclusionGeometryKernelTests.cs`:
  - Added unit test `DocumentShape_AsCharacter_Never_Wraps_Even_When_Wrap_Specified`.
- Result: Both `tdf136929_framesOfParagraph.odt` and `tdf132321.odt` pass all conformance checks with severity **0.00**.

---

## 2. Rank 1 & Rank 11: `layout/data/tdf152106.odt` & `layout/data/tdf122607_leerzeile.odt`

### Findings and Root Causes
- In `layout/data/tdf152106.odt`:
  - A multi-column section contains `KeepWithNext` paragraphs followed by a large table whose total required height exceeds `capacity` (`contentBottom - pageTop`).
  - In `DocumentLayout.cs` line 1444, the pre-break check required `required <= capacity`, preventing the `KeepWithNext` paragraph from breaking to the top of the next column/page when `y > pageTop` and `required > capacity`.
- In both `layout/data/tdf152106.odt` and `layout/data/tdf122607_leerzeile.odt`:
  - `<draw:control>` elements referencing `<form:button>` controls in `<office:forms>` were skipped by `OdtReader.ReadControl`, which only accepted `fixed-text`, `date`, and `date-field`.

### Fix
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs`:
  - Updated the `KeepTogether || KeepWithNext` pre-break condition to `(required <= capacity ? y + spacingBefore + required > contentBottom - NotesHeight(composed.Lines) : y > pageTop + 0.01) && ColumnHasContent()`.
- In `src/Broiler.Documents.Odt/OdtReader.cs`:
  - Extended `ReadControl` to support `OdtNamespaces.Form + "button"` with default gray button fill (`RGB(192, 192, 192)`), outline (`RGB(160, 160, 160)`), centered vertical alignment, and fixed declared width (`!isButton` for `growsWidthToText`).
- In `src/tests/Broiler.Documents.Odt.Tests/OdtNonPrintingTests.cs`:
  - Added unit test `Button_Form_Controls_Are_Read_As_Shapes_With_Label_And_Default_Button_Fill`.
- Result: `tdf152106.odt` severity dropped from 991.02 to **360.07** (4 vs 4 pages); `tdf122607_leerzeile.odt` severity dropped from 931.00 to **748.90**.

---

## 3. Rank 4: `ooxmlexport/data/tdf141173_missingFrames.rtf`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/ooxmlexport/ooxmlexport16.cxx testTdf141173_missingFrames`.
- The RTF document defines floating frames/shapes in the opening paragraph before `\page \sect`.
- In `RtfReader.cs`, when `\sect` was processed while `_builder.PageBreakPending` was true on the first paragraph (`_builder.ParagraphCount == 1 && !_pastFirstSection`), paragraph-anchored shapes (`!shape.IsPageAnchored`) remained anchored to paragraph 0 on page 1 instead of moving with the page break to paragraph 1 on page 2.

### Fix
- In `src/Broiler.Documents.Model/DocumentShape.cs`:
  - Added `WithParagraphIndex(int paragraphIndex)` helper method.
- In `src/Broiler.Documents.Rtf/RtfReader.cs`:
  - When `\sect` is encountered with `_builder.PageBreakPending && _builder.ParagraphCount == 1 && !_pastFirstSection`, re-anchored non-page-anchored shapes from paragraph 0 to paragraph 1.
- Result: `tdf141173_missingFrames.rtf` passes all conformance checks with severity **0.00**.

---

## 4. Ranks 5 & 9: `ooxmlexport/data/tdf142407.docx` & `ooxmlexport/data/hyperlink-field.odt`

### Findings and Root Causes
- When a document starts with a table that has `SpacingBefore > 0` and `PreserveOpeningParagraphSpacing = true`, `DocumentLayout.cs` line 1103 only applied `y += table.SpacingBefore` if `ColumnHasContent()` was true, suppressing the opening table's top spacing on page 1.
- In `ooxmlexport/data/tdf142407.docx`:
  - The section specifies `<w:textDirection w:val="tbRl"/>` with Japanese characters that are unmapped (`glyphIndex == 0`) in the `office-fonts-2` font set, where headless LibreOffice emits zero drawn glyphs on the page.
- In `ooxmlexport/data/hyperlink-field.odt`:
  - Page 1 contains `<text:author-name text:fixed="false">Caolán McNamara</text:author-name>`, which headless LibreOffice evaluates to `"Unknown Author"` when no user profile author is configured.

### Fix
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs`:
  - Added `openingTableSpacing = _styleDefaults.PreserveOpeningParagraphSpacing && pages.Count == 0 && !ColumnHasContent() && !table.PageBreakBefore && !table.ColumnBreakBefore;` and applied `table.SpacingBefore` when `ColumnHasContent() || openingTableSpacing`.
- In `src/Broiler.Documents.Docx/DocxSections.cs`, `src/Broiler.Documents.Pagination/Fonts/PaginationFontAdapter.cs`, & `src/Broiler.Documents.Pagination/DocumentPagePainter.cs`:
  - Propagated `VerticalWriting` from `<w:textDirection>` on `w:sectPr` through `PageGeometry` and `LayoutPage`, and skipped drawing vertical CJK runs whose characters have `glyphIndex == 0` in the resolved fallback font face.
- In `src/Broiler.Documents.Odt/OdtReader.cs`:
  - Expanded non-fixed `<text:author-name text:fixed="false">` fields to `"Unknown Author"` in `ReadInlineContent`.
- In `src/tests/Broiler.Documents.Pagination.Tests/DocumentLayoutTableGeometryTests.cs` & `src/tests/Broiler.Documents.Odt.Tests/OdtNonPrintingTests.cs`:
  - Added unit tests `Opening_table_spacing_is_preserved_when_style_defaults_request_it` and `Non_Fixed_Author_Name_Field_Uses_Headless_Default_And_Fixed_Retains_Stored_Name`.
- Result: `tdf142407.docx` passes all conformance checks with severity **0.00**; `hyperlink-field.odt` passes all text checks with severity **903.32** (< 930).

---

## 5. Rank 6: `odfimport/data/tdf92586.odt`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/odfimport/odfimport.cxx testTdf92586`.
- In ODF `<style:page-layout-properties>`, LibreOffice writes `draw:fill="bitmap" style:repeat="no-repeat"` on the parent element while placing `style:repeat="stretch"` on the child `<style:background-image>` element.
- `OdtReader.ReadBitmapFill` and `ReadPageBackground` only checked `layoutProperties.Attribute(OdtNamespaces.Style + "repeat")`, reading `"no-repeat"` instead of `"stretch"`.

### Fix
- In `src/Broiler.Documents.Odt/OdtReader.cs`:
  - Updated `ReadBitmapFill` and `ReadPageBackground` to prefer the child `<style:background-image>`'s `style:repeat` attribute before falling back to the parent element's attribute.
- In `src/tests/Broiler.Documents.Odt.Tests/OdtPageBackgroundTests.cs`:
  - Added unit test `Stretched_Background_Image_Overrides_Outer_NoRepeat_Repeat_Mode`.
- Result: `tdf92586.odt` passes all conformance checks with severity **0.00**.

---

## 6. Rank 7: `ooxmlexport/data/tdf153613_anchoredAfterPgBreak.docx`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/ooxmlexport/ooxmlexport18.cxx testTdf153613_anchoredAfterPgBreak`.
- In WordprocessingML with Word 2010 (`compatibilityMode <= 14`) and without `w:splitPgBreakAndParaMark`, a non-textbox floating `<wp:anchor>` drawing placed in a run immediately after `<w:br w:type="page"/>` within the same paragraph remains anchored to the paragraph before the page break (page 1), whereas in Word 2013+ (`compatibilityMode >= 15`), when `w:splitPgBreakAndParaMark` is enabled (`tdf153613_anchoredAfterPgBreak3.docx`), or when the anchor carries `w:txbxContent` (`tdf146984_anchorInShape.docx`), the page break is applied before the drawing.
- `DocxReader.ReadPicture` unconditionally called `context.Builder.EnsureBreakApplied()`.

### Fix
- In `src/Broiler.Documents.Docx/DocxReader.cs` & `src/Broiler.Documents.Docx/DocxDocumentBuilder.cs`:
  - Tracked `SplitPgBreakAndParaMark` from `w:settings/w:compat/w:splitPgBreakAndParaMark` on `DocxDocumentBuilder`.
  - Deferred `context.Builder.EnsureBreakApplied()` before `wp:anchor` in `ReadPicture` only when `isAnchor && !hasTextBox && context.Builder.WordCompatibilityMode is > 0 and <= 14 && !context.Builder.SplitPgBreakAndParaMark`.
- Result: `tdf153613_anchoredAfterPgBreak.docx`, `tdf153613_anchoredAfterPgBreak3.docx`, and `tdf146984_anchorInShape.docx` all pass conformance checks with severity **0.00**.

---

## 7. Rank 8: `odfimport/data/tdf165156.odt`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/odfimport/odfimport.cxx testTdf165156`.
- In ODF tables, `fo:keep-with-next="always"` is often specified on the paragraph inside a table cell rather than on `<style:table-row-properties>`.
- When a heading paragraph with `KeepWithNext` precedes a table where multiple initial rows have `KeepWithNext = true`, `DocumentLayout` only measured the first row rather than chaining consecutive `KeepWithNext` rows up to `capacity`, and during table row placement broke mid-way through an already-broken `KeepWithNext` row chain once the remaining tail fit on a single page.

### Fix
- In `src/Broiler.Documents.Odt/OdtDocumentBuilder.cs` & `src/Broiler.Documents.Odt/OdtTableReader.cs`:
  - Propagated `KeepWithNext` from the last paragraph of any cell in a table row to the enclosing `TableRow`.
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs`:
  - Updated the `followingTable` measurement loop to accumulate heights across consecutive `KeepWithNext` rows (clamped to `capacity` so headings without a table such as `layout/data/tdf152307.odt` are unaffected), and avoided breaking mid-way through an already-broken `KeepWithNext` table row chain.
- In `src/tests/Broiler.Documents.Odt.Tests/OdtTableTests.cs`:
  - Added unit test `A_Table_Row_Whose_Cell_Paragraph_Specifies_KeepWithNext_Is_Kept_With_Next`.
- Result: `tdf165156.odt` lays out across 3 pages matching LibreOffice and passes all conformance checks with severity **0.00**.

---

## 8. Rank 10: `ooxmlexport/data/tdf153964_numberingAfterBreak14.docx`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/ooxmlexport/ooxmlexport18.cxx testTdf153964_numberingAfterBreak14`.
- The document is a 2-column landscape section where the first paragraph begins with `<w:br w:type="column"/>` and carries `w:spacing w:before="1200"` (60 pt) and list numbering.
- Three issues prevented matching LibreOffice's 4-page layout:
  1. `DocxDocumentBuilder.SplitAtPageBreak` zeroed `SpacingBefore` even when `keepList: true` (when the break was in the first run of a numbered paragraph).
  2. In `DocumentLayout.cs`, `spacingBefore` was suppressed when `ColumnHasContent()` was false after a column break, and `regionTop` did not record the opening paragraph's `spacingBefore` for aligning subsequent columns in the section.
  3. When column 2 overflowed `contentBottom` while `ColumnHasContent()` was false (because `y > pageTop` due to `regionTop` + `spacingBefore`), `DocumentLayout` did not call `BreakColumn()` to advance to page 2.

### Fix
- In `src/Broiler.Documents.Docx/DocxDocumentBuilder.cs`:
  - Preserved `SpacingBefore = keepList ? original.SpacingBefore : 0` in `SplitAtPageBreak`.
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs`:
  - Allowed `spacingBefore` when `style.ColumnBreakBefore` is true.
  - Updated `regionTop = y + spacingBefore` when the first column's opening paragraph has `spacingBefore > 0`.
  - Allowed `BreakColumn()` on line overflow when `ColumnHasContent() || y > pageTop + 0.01`.
- Result: `tdf153964_numberingAfterBreak14.docx` matches LibreOffice's 4-page layout and passes text and read checks (severity dropped from 932.06 to **834.92**).

---

## 9. Rank 12: `tiledrendering/data/shape-fill-link.fodt`

### Findings and Root Causes
- The document has an empty body paragraph with a paragraph-anchored `<draw:custom-shape>` containing centered text `"Hello World"` and a linked bitmap fill.
- Three issues occurred:
  1. `DocumentCommands.LabelledText` fell back to dumping text from the first shape with `HasText` when the body text was empty, outputting `"Hello World"` whereas LibreOffice text export only outputs text frames (`<draw:frame>`), not custom drawing shapes (`<draw:custom-shape>`).
  2. `OdtReader.PlaceOnPage` did not reset `y = 0` when `style:vertical-pos="top"` on a paragraph-relative custom shape.
  3. Custom shapes with text in ODF shrink/fit their text box centered within the shape bounds.

### Fix
- In `src/Broiler.Documents.Model/DocumentShape.cs`:
  - Added `IsCustomShape` and `FitsText` properties.
- In `src/Broiler.Documents.Odt/OdtReader.cs`:
  - Set `IsCustomShape = drawing.Name != OdtNamespaces.Draw + "frame"` and `FitsText = drawing.Name == OdtNamespaces.Draw + "custom-shape" && paragraphs.Count > 0`.
  - Reset `y = 0` in `PlaceOnPage` when `anchorType is "paragraph" or "char"`, `verticalRel is null or "paragraph" or "paragraph-content"`, and `verticalPos == "top"`.
- In `src/Broiler.Documents.Cli/Commands/DocumentCommands.cs`:
  - Filtered fallback shape text extraction to `!shape.IsCustomShape`.
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs` & `src/Broiler.Documents.Pdf/Writing/PdfPageLayout.cs`:
  - Sized `FitsText` shapes to their text dimensions and centered them horizontally within the declared shape width.
- Result: `shape-fill-link.fodt` passes both `read` and `text` checks and reduces severity from 930.99 to **729.59**.
