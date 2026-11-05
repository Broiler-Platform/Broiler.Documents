# Office Conformance Issue 200

Investigation and fixes for high-impact differences identified in [issue #200](https://github.com/Broiler-Platform/Broiler.Documents/issues/200):

1. `layout/data/i84870.fodt` (rank 1, severity 1194.48): 1 vs 2 pages, character-anchored shapes without text boxes.
2. `ooxmlimport/data/cjklist35.docx` (rank 7, severity 973.98): DOCX list numbering format `taiwaneseCountingThousand`.
3. `ooxmlimport/data/cjklist44.docx` (rank 8, severity 973.83): DOCX list numbering format `koreanDigital2`.
4. `ooxmlimport/data/cjklist31.docx` (rank 9, severity 973.02): DOCX list numbering format `ideographTraditional` (Heavenly Stems / TianGan).
5. `ooxmlexport/data/tdf148671.docx` (rank 11, severity 969.14): WordprocessingML `<w:framePr>` paragraph text frames.
6. `ooxmlimport/data/tdf75573_page1frame.docx` (rank 17, severity 963.59): Style-inherited `<w:framePr>` and multi-paragraph frame accumulation.
7. `ooxmlimport/data/cjklist30.docx` (rank 21, severity 957.89): DOCX list numbering format `ideographZodiac` (Earthly Branches / DiZi).
8. `rtfimport/data/fdo52052.rtf` (rank 25, severity 950.13): 4 vs 3 pages, legacy shape re-anchoring across section breaks, empty pagebreak diagnostic.
9. `layout/data/tdf160526.fodt` (rank 27, severity 946.89): 1 vs 2 pages, clearance calculation for wrapping shapes with tall as-char shapes.
10. `ooxmlexport/data/tdf130814.docx` (rank 29, severity 945.67) and `ooxmlexport/data/fdo78907.docx` (rank 30, severity 945.53): 1 vs 2 pages, opening manual page break preserving title/cover page.

---

## 1. Rank 1: `layout/data/i84870.fodt`

### Findings and Root Causes
- Upstream test: `SwLayoutWriter4::test_i84870` (OpenOffice defect #84870).
- The document defines an A4 page with 2 cm margins (content height 25.7 cm).
- Paragraph 1 contains a character-anchored custom shape (`text:anchor-type="as-char"`) with `svg:width="5cm"` and `svg:height="25cm"`.
- Paragraph 2 contains a text paragraph "second page".
- In `src/Broiler.Documents.Odt/OdtReader.cs`, line 998:
  ```csharp
  IsAsCharacter = textBox is not null && (string?)drawing.Attribute(OdtNamespaces.Text + "anchor-type") == "as-char"
  ```
  `IsAsCharacter` was only set to `true` when `textBox is not null`. Shapes without `<draw:text-box>` (such as `draw:custom-shape`, `draw:rect`) had `IsAsCharacter = false`.
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs` (lines 411 and 1312), the layout engine checked `shape.Wraps` when querying line clearance and advancing the vertical cursor, which excluded character-anchored shapes. Because `IsAsCharacter` was false and `shape.Wraps` was false, the 25 cm tall shape did not push the following paragraph to a new page, collapsing the document from 2 pages onto 1 page.

### Fix
- In `src/Broiler.Documents.Odt/OdtReader.cs`, removed the `textBox is not null` requirement for `IsAsCharacter`.
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs`, updated lines 411 and 1312 to test `(shape.Wraps || shape.IsAsCharacter)` when computing clearance and checking for room on the current page.
- Added unit test `As_Character_Shape_Forces_Following_Paragraph_To_Next_Page` in `src/tests/Broiler.Documents.Cli.Tests/DocumentLayoutFramePageTests.cs`.
- Result: `i84870.fodt` renders exactly 2 pages with "second page" on page 2, matching LibreOffice.

---

## 2. Ranks 7, 8, 9, 21: DOCX CJK List Numbering Formats

### Findings and Root Causes
- **Rank 7 (`cjklist35.docx`, severity 973.98):** `w:numFmt="taiwaneseCountingThousand"`.
- **Rank 8 (`cjklist44.docx`, severity 973.83):** `w:numFmt="koreanDigital2"`.
- **Rank 9 (`cjklist31.docx`, severity 973.02):** `w:numFmt="ideographTraditional"`.
- **Rank 21 (`cjklist30.docx`, severity 957.89):** `w:numFmt="ideographZodiac"`.
- In ECMA-376 Part 1, section 17.18.59 (`ST_NumberFormat`), these values specify East Asian counting and cyclical systems:
  - `taiwaneseCountingThousand` and `koreanDigital2`: standard Chinese/Korean ideographic counting numbers (一, 二, 三, ..., 十, ..., 二十...).
  - `ideographTraditional`: the 10 Heavenly Stems (天干, TianGan: 甲, 乙, 丙, 丁, 戊, 己, 庚, 辛, 壬, 癸).
  - `ideographZodiac`: the 12 Earthly Branches (地支, DiZi: 子, 丑, 寅, 卯, 辰, 巳, 午, 未, 申, 酉, 戌, 亥).
- In `DocxNumbering.cs`, these formats were missing from the switch statement in `ReadNumberFormat`, falling back to `ListNumberFormat.Decimal` with the diagnostic `docx.numbering.format: "Unhandled numFmt <name>; rendered as 1."` and producing semantic text diffs against LibreOffice.

### Fix
- Added `IdeographCounting`, `TianGan`, and `DiZi` to `ListNumberFormat` in `src/Broiler.Documents.Model/ListDefinition.cs`.
- Implemented formatting rules in `ListNumberer.cs`:
  - `ChineseCounting(int value)` supporting numbers up to 99,999 (一..二十, 百, 千, 萬).
  - `TianGan(int value)` mapping 1..10 to 甲..癸.
  - `DiZi(int value)` mapping 1..12 to 子..亥.
- Added mappings in `src/Broiler.Documents.Docx/DocxNumbering.cs`:
  - `"taiwaneseCountingThousand"` $\rightarrow$ `ListNumberFormat.IdeographCounting`
  - `"koreanDigital2"` $\rightarrow$ `ListNumberFormat.IdeographCounting`
  - `"ideographTraditional"` $\rightarrow$ `ListNumberFormat.TianGan`
  - `"ideographZodiac"` $\rightarrow$ `ListNumberFormat.DiZi`
- Added unit tests `Cjk_List_Number_Formats_Render_Correct_Labels` and `TianGan_Number_Format_Renders_Ten_Stems` in `src/tests/Broiler.Documents.Docx.Tests/DocxNumberingTests.cs`.
- Result: All four test documents render clean CJK numbering with 0 diagnostics.

---

## 3. Ranks 11 & 17: DOCX Paragraph Text Frames (`<w:framePr>`)

### Findings and Root Causes
- **Rank 11 (`tdf148671.docx`, severity 969.14):** 6 lines of semantic text differences ("CV - Michael W. van der Wilden", "Subject", "Date", "Version", "October 2012", "v1.7").
- **Rank 17 (`tdf75573_page1frame.docx`, severity 963.59):** 2 lines of semantic text differences ("lorem ipsum", "Lorem ipsum dolor sit amet...").
- In ECMA-376 Part 1, section 17.3.1.11, `<w:framePr>` inside `<w:pPr>` (or inherited via paragraph styles such as `Proprietary` in `tdf75573`) marks a paragraph as a floating text frame.
- LibreOffice's `writerfilter` imports framed paragraphs as `SwFlyFrame` objects. Because LibreOffice's plain text export (`--convert-to txt`) traverses body paragraphs and excludes fly frames, LibreOffice produces empty lines for `tdf148671.docx` and only `"SOME TEXT"` for `tdf75573_page1frame.docx`.
- Broilerdoc did not recognize `w:framePr`, reading framed paragraphs as standard body text and dumping them into text projection.
- Furthermore, under ECMA-376 and Word behavior, adjacent paragraphs that share identical frame properties are grouped into a single multi-paragraph frame.

### Fix
- Created `src/Broiler.Documents.Docx/DocxFrameAccumulator.cs` with:
  - `DocxFrameProperties`: captures frame width, height, height rule, coordinates (`x`, `y`), anchors (`hAnchor`, `vAnchor`), alignment, and wrapping.
  - `DocxFrameAccumulator`: groups adjacent paragraphs sharing identical frame formatting into a single `DocumentShape` with `paragraphs: frameDoc.Paragraphs`.
  - Coordinate resolution converting twips to points and offsetting against section paper margins (`offsetFromPage: !fromParagraph`).
- In `src/Broiler.Documents.Docx/DocxReadContext.cs`, added `DocxFrameAccumulator? Frames` to the reading context.
- In `src/Broiler.Documents.Docx/DocxReader.cs`:
  - Updated `ReadParagraph` to detect direct and style-inherited `w:framePr`.
  - When framed in the body flow, paragraphs are routed into `context.Frames` rather than `context.Builder`, omitting them from body paragraph text.
  - Flushed pending frames before unframed paragraphs, section breaks, and at the end of block content.
- Added comprehensive unit tests in `src/tests/Broiler.Documents.Docx.Tests/DocxParagraphFrameTests.cs`:
  - `Direct_FramePr_Creates_DocumentShape_And_Removes_From_Body_Paragraphs`
  - `Style_Inherited_FramePr_Creates_DocumentShape`
  - `Adjacent_Paragraphs_With_Identical_Frame_Are_Combined_Into_Single_Shape`
  - `Different_Frame_Coordinates_Create_Distinct_Shapes`
  - `DropCap_Is_Not_Treated_As_Frame_Shape`
- Result: 0 semantic text differences on both `tdf148671.docx` and `tdf75573_page1frame.docx`.

---

## 4. Rank 25: `rtfimport/data/fdo52052.rtf`

### Findings and Root Causes
- Upstream test: `Test::testFdo52052` (FreeDesktop bug #52052).
- The document has 3 pages in LibreOffice. Broilerdoc rendered 4 pages, with pages 1 and 2 completely blank, and emitted `rtf.pagebreak.empty: "A page break on an empty paragraph started a blank page."`.
- Investigation revealed three issues in `src/Broiler.Documents.Rtf/RtfReader.cs`:
  1. Shapes anchored before a section break were being re-anchored across the section break when `_builder.ParagraphCount` had not yet advanced, pulling background shapes onto later pages.
  2. The RTF sequence `\page\sect` was treated as an explicit manual page break followed by a section break that also started a page, synthesizing an extra empty page.
  3. Pending frames in `_frameText` were not finished before applying a section break.

### Fix
- In `src/Broiler.Documents.Rtf/RtfReader.cs`:
  - Removed cross-break shape re-anchoring.
  - In `case "sect":`, invoked `FinishFrame()` and checked pending shape anchors against the section break.
  - Mapped `\page\sect` sequences to continuous section breaks (`_bodySectionBreak = _builder.PageBreakPending ? BodySectionBreak.Continuous : BodySectionBreak.Page`).
  - Preserved trailing empty paragraphs when `_bodySectionPending` is active.
- Verified on existing test suite `RtfSectionBreakTests` and `RtfFrameTests`.
- Result: `fdo52052.rtf` renders exactly 3 pages with no empty leading pages and 0 diagnostics.

---

## 5. Rank 27: `layout/data/tdf160526.fodt`

### Findings and Root Causes
- Upstream test: `SwLayoutWriter4::testTdf160526` (TDF bug #160526).
- The document has a 12 cm $\times$ 3 cm wrapping shape in paragraph 1, followed by a character-anchored shape of height 25 cm in paragraph 2.
- On an A4 page with 2 cm margins, the available body height is 25.7 cm. The 3 cm wrapping shape leaves only 22.7 cm of clearance.
- Because the character-anchored shape in paragraph 2 requires 25 cm of vertical clearance, it cannot fit in the remaining space and must be pushed to page 2.
- In `src/Broiler.Documents.Model/TextWrapExclusions.cs`, the exclusion model only tracked horizontal intervals at specific vertical points, but did not compute clearance for shapes requiring a minimum vertical height and width.

### Fix
- In `src/Broiler.Documents.Model/TextWrapExclusions.cs`, implemented `Clearance(double top, double height, double requiredWidth, double columnWidth)` to calculate the vertical point where sufficient uninterrupted width is available.
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs`, queried wrap exclusion clearance and pushed lines exceeding remaining page height to page 2.
- Added unit test `As_Character_Shape_Under_Wrapping_Shape_Pushed_To_Next_Page` in `src/tests/Broiler.Documents.Pagination.Tests/DocumentLayoutWrappingFrameTests.cs`.
- Result: `tdf160526.fodt` renders exactly 2 pages, matching LibreOffice.

---

## 6. Ranks 29 & 30: `ooxmlexport/data/tdf130814.docx` and `ooxmlexport/data/fdo78907.docx`

### Findings and Root Causes
- Upstream tests: `ooxmlexport14.cxx::testTdf130814` and `ooxmlexport5.cxx::testfdo78907`.
- Both documents render 2 pages in LibreOffice (a title/cover page followed by content on page 2). Broilerdoc rendered only 1 page.
- Both documents begin with a manual inline page break (`<w:r><w:br w:type="page"/></w:r>`) before any text in the opening paragraph:
  - In `fdo78907.docx`: `<w:p><w:r><w:br w:type="page"/></w:r><w:r><w:t>Table of Contents</w:t></w:r></w:p>`.
  - In `tdf130814.docx`: `<w:p><w:r><w:drawing>...</w:drawing></w:r><w:r><w:br w:type="page"/></w:r><w:r><w:t>Combat</w:t></w:r></w:p>`.
- In `DocxDocumentBuilder.cs`, because no text preceded the page break, `_pageBreakLeadsParagraph` was `true`. It called `StartParagraphOnNewPage()`, setting `PageBreakBefore = true` on paragraph 0.
- In `DocumentLayout.cs` (lines 1255–1263):
  ```csharp
  // A page break between pages breaks paper, and the first paragraph
  // that opens with one is asking to start on a fresh page, and it is
  // already on one.
  bool styleBreaksPage = style.PageBreakBefore || sectionBreaksPage;
  if (styleBreaksPage && !setup.Continuous && (currentLines.Count > 0 || floatLines.Count > 0))
  ```
  `style.PageBreakBefore` on paragraph 0 is intentionally suppressed for paragraph style formatting when no content precedes it, which discarded the manual page break and collapsed page 1 and page 2 into one page.

### Fix
- In `src/Broiler.Documents.Docx/DocxDocumentBuilder.cs` (line 298):
  ```csharp
  if (_pageBreakSeen)
  {
      if (_pageBreakLeadsParagraph && _breakKeepsLeadingLabel &&
          _paragraphStyle.ListKind != ListKind.None)
          SplitAtPageBreak(keepList: true);
      else if (_pageBreakLeadsParagraph && (_breakIsColumn || _paragraphStyle.ListKind != ListKind.None || _paragraphs.Count > 0))
          StartParagraphOnNewPage();
      else
          SplitAtPageBreak();
  }
  ```
  When a manual page break leads paragraph 0 (`_paragraphs.Count == 0`) and is not a column break or list item, `SplitAtPageBreak()` is invoked. This emits the opening cover page and starts the following content on page 2.
- Column breaks (`_breakIsColumn`) and list items (`_paragraphStyle.ListKind != ListKind.None`) continue to use `StartParagraphOnNewPage()`, maintaining compatibility with `DocxColumnTests` and `DocxNumberingTests`.
- Result: Both `tdf130814.docx` and `fdo78907.docx` render exactly 2 pages with 0 regressions across the test suite.
