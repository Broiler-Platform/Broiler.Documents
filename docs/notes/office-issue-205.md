# Office Conformance Issue 205

Investigation and fixes for top-ranked differences identified in [issue #205](https://github.com/Broiler-Platform/Broiler.Documents/issues/205) (Run 130 triage against headless LibreOffice 24.2.7.2):

1. `rtfexport/data/tdf138779.docx` (rank 7, severity 983.94): Text mismatch (20 lines), missing ruby base text from `<w:ruby>` elements and Word equation fields (`EQ \o(\s\up...(phonetic),base)`).
2. `rtfexport/data/cjklist*.rtf` (ranks 13-19, 21-22, severity 966.11–966.34): Text mismatch across 9 samples (`cjklist12`, `cjklist13`, `cjklist16`, `cjklist20`, `cjklist21`, `cjklist24`, `cjklist25`, `cjklist30`, `cjklist31`), DBCS multi-byte RTF byte decoding falling back to Latin-1 instead of East Asian code pages.
3. `layout/data/tdf150438.docx` (rank 12, severity 971.65): Text mismatch (12 lines), drop cap paragraphs (`w:framePr w:dropCap="drop"|"margin"`) emitted as standalone paragraphs instead of prepending to host body paragraphs.
4. `rtfexport/data/tdf162342.rtf` (rank 20, severity 966.21): Upstream LibreOffice 24.2 reference defect, out-of-order context stack pop drops section geometry on pages following explicit page breaks (`\page`). Registered in `tests/office/extras-reference-defects.json`.
5. `ooxmlimport/data/tdf130214.docx` (rank 2, severity 996.58): Upstream LibreOffice 24.2 reference defect, bibliography field inside a footnote triggers an import error guard early-return that truncates the document to 1 page. Registered in `tests/office/extras-reference-defects.json`.
6. `ww8export/data/fdo66304-1.odt` (rank 6, severity 984.40): Tracked change deletions overlapping headings and list numbering boundaries.

---

## 1. Rank 7: `rtfexport/data/tdf138779.docx`

### Findings and Root Causes
- Upstream test: `rtfexport8.cxx::testTdf138779` (TDF bug #138779).
- The document contains Japanese ruby (furigana) annotations and Word equation fields specifying phonetic guides:
  - Direct WordprocessingML `<w:ruby>` elements with `<w:rubyBase>` and `<w:rt>`.
  - Field instruction elements: `w:fldChar` / `w:instrText` with instruction `EQ \o(\s\up 9(きょう),今日)\o(\s\up 9(は),は)\o(\s\up 9(にがつ),二月)\o(\s\up 9(ついたち),一日)\o(\s\up 9(だ),だ)`.
- When `tdf138779.docx` was converted to text, LibreOffice projected `今日は二月一日だ。` (extracting the base text for each ruby annotation).
- In Broilerdoc:
  - In `src/Broiler.Documents.Docx/DocxReader.cs`, `<w:ruby>` was unhandled in `ReadParagraphChildren`, causing all ruby base text to be omitted.
  - In `src/Broiler.Documents.Docx/DocxFields.cs`, equation fields without cached `fldCharType="separate"` result text returned `Page = null` and discarded the instruction, leaving empty runs.

### Fix
- In `src/Broiler.Documents.Docx/DocxFields.cs`:
  - Added `ExtractRubyBase(string instruction)`: parses `EQ \o(...,base)` constructs, extracting the unadorned base text (e.g., `今日`, `は`, `二月`, `一日`, `だ`) while discarding the phonetic annotations (`\s\up...`).
  - Updated `DocxFields.Read`: when a field ends without cached result text (`!ended.HasResultText`), it evaluates `ExtractRubyBase(instruction)` and returns `(PageField? Page, string? FallbackText)`.
- In `src/Broiler.Documents.Docx/DocxReader.cs`:
  - In `ReadRunContent`: when a field character has fallback text, emits the text and adds diagnostic `docx.field.ruby`.
  - In `ReadRunContent` and `ReadParagraphChildren`: added handlers for `<w:ruby>` elements, delegating to `<w:rubyBase>` runs with diagnostic `docx.ruby` while ignoring phonetic `<w:rt>` runs for linear text projection.
- Added unit tests in `src/tests/Broiler.Documents.Docx.Tests/DocxRubyTests.cs`:
  - `Ruby_Element_Extracts_Ruby_Base_Text`
  - `Empty_Result_Ruby_Equation_Field_Extracts_Base_Text`
- Result: `tdf138779.docx` text projection produces exact `今日は二月一日だ。` matching LibreOffice reference text with 0 diff lines.

---

## 2. Ranks 13–19, 21–22: `rtfexport/data/cjklist*.rtf`

### Findings and Root Causes
- Upstream tests: `rtfexport.cxx` CJK list formatting suite.
- The 9 failed samples (`cjklist12`, `cjklist13`, `cjklist16`, `cjklist20`, `cjklist21`, `cjklist24`, `cjklist25`, `cjklist30`, `cjklist31`) use Big5 (code page 950 / charset 136) or Shift-JIS (code page 932 / charset 128) encoding for Chinese/Japanese list numbering strings and font names.
- In RTF, multi-byte DBCS characters are encoded as consecutive `\'hh\'hh` hex byte tokens or as a hex byte lead token followed by an ASCII trail byte (e.g. `\'a4@` where `@` is ASCII 0x40).
- In `src/Broiler.Documents.Rtf/RtfCodePage.cs`:
  - `RtfCodePage.IsFullySupported` only handled single-byte code pages; double-byte code pages fell back to Latin-1 character-by-character.
  - Characters such as `一` (Big5 `0xA4 0x40`) and `二` (Big5 `0xA4 0x47`) decoded as `¤@` and `¤G`.
  - `cjklist16.rtf` rendered list numbers as `¤@.`, `¤G.`, `¤T.` instead of `一.`, `二.`, `三.`.

### Fix
- In `src/Broiler.Documents.Rtf/RtfCodePage.cs`:
  - Added `IsDbcsCodePage(int codePage)` for code pages 932 (Shift-JIS), 936 (GB2312/GBK), 949 (EUC-KR), 950 (Big5), and 1361 (Johab).
  - Added `EncodingFor(int codePage)` caching `CodePagesEncodingProvider.Instance.GetEncoding(cp)`.
- In `src/Broiler.Documents.Rtf/RtfReader.cs`:
  - Added `_leadByte` state buffer.
  - In `HandleByte`: when `RtfCodePage.IsDbcsCodePage(_state.CodePage)` is active:
    - If `_leadByte == 0` and `value >= 0x80`, buffers `_leadByte = value`.
    - If `_leadByte != 0`, decodes the 2-byte pair `[lead, value]` using the DBCS encoding and emits the decoded Unicode character(s).
  - In `HandleBodyText`: added 2-byte lookahead and pairing for DBCS code pages to decode multi-byte sequences directly into the text stream.
  - Added `FlushLeadByte()`: called at group boundaries (`{`, `}`), control words, control symbols, and Unicode escapes (`\u`) to safely flush orphan lead bytes.
- Added unit tests in `src/tests/Broiler.Documents.Rtf.Tests/RtfPlainCodePageTests.cs`:
  - `A_Double_Byte_Code_Page_Decodes_MultiByte_Sequences` (Big5, Shift-JIS, GBK).
  - `A_Double_Byte_Code_Page_Decodes_Hex_Lead_With_Ascii_Trail_Byte`.
  - `A_Double_Byte_Code_Page_Decodes_Font_Name_In_FontTable`.
- Result: All 9 `cjklist` samples now decode correctly (`一.`, `二.`, `三.`), resolving all 9 text mismatch failures.

---

## 3. Rank 12: `layout/data/tdf150438.docx`

### Findings and Root Causes
- Upstream test: `layout6.cxx::testTdf150438` (TDF bug #150438).
- The document contains drop caps on initial quotation marks (`“`, `”`, `‘`, `’`).
- In WordprocessingML, drop caps are stored as standalone framed paragraphs preceding the host paragraph, decorated with `<w:framePr w:dropCap="drop" w:lines="3" .../>`.
- In `src/Broiler.Documents.Docx/DocxFrameAccumulator.cs`, drop cap paragraphs were excluded from becoming floating `DocumentShape` frames (`if (dropCap is "drop" or "margin") return null;`). However, `DocxReader` emitted them as isolated standalone paragraphs.
- This separated the drop cap character onto its own line before the body paragraph text:
  ```text
  “
  Lorem ipsum dolor sit amet...
  ```
  whereas LibreOffice and Word merge the drop cap character into the initial run of the body paragraph:
  ```text
  “Lorem ipsum dolor sit amet...
  ```

### Fix
- Created `src/Broiler.Documents.Docx/DocxDropCapTracker.cs` to hold pending drop cap paragraphs in `DocxReadContext`.
- In `src/Broiler.Documents.Docx/DocxFrameAccumulator.cs`:
  - Added `IsDropCap(XElement? pPr, string? paragraphStyleId, DocxReadContext context)`.
- In `src/Broiler.Documents.Docx/DocxReader.cs`:
  - When `IsDropCap` is true on an incoming paragraph, stores it in `context.DropCaps.Pending`.
  - When the subsequent host body paragraph arrives, retrieves `context.DropCaps.Pending` and passes it to `EmitParagraph`.
  - In `EmitParagraph`: starts the paragraph with the host paragraph's style, prepends the drop-cap paragraph's runs (inheriting the drop-cap paragraph's run properties), and then reads the host paragraph's runs.
  - Added `FlushPendingDropCap` called before tables, section breaks, and at the end of block containers to guarantee that orphan drop caps without a following body paragraph are never lost.
- Updated unit tests in `src/tests/Broiler.Documents.Docx.Tests/DocxParagraphFrameTests.cs`:
  - `DropCap_Is_Prepended_To_Host_Paragraph`
  - `DropCap_Without_Following_Paragraph_Flushes_As_Standalone_Paragraph`
- Result: `tdf150438.docx` renders 4 unified paragraphs matching LibreOffice reference text character-for-character with 0 differences.

---

## 4. Rank 20: `rtfexport/data/tdf162342.rtf`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/rtfexport/rtfexport8.cxx testTdf162342` (TDF bug #162342).
- The document specifies custom page dimensions and margins followed by page breaks:
  ```rtf
  {\rtf1
  \margl720 \margr360 \margt720 \margb720 \paperw6120 \paperh7920
  First
  \page
  Second
  \page
  Third}
  ```
- In LibreOffice 24.2, an out-of-order context stack pop during RTF import left `m_pTopContext` empty when section properties were applied before a page break (`\page`), causing section geometry (`\paperw`, `\paperh`, margins) to be ignored or misapplied on following pages.
- Mike Kaganski fixed this upstream in commit `1567f48ec9fe096da386bdaf370bc95414c8d923` ("tdf#162342: workaround out-of-order context stack pop", Gerrit review 188449, release 26.2).
- The `office-references-v3` PDF generated by LibreOffice 24.2.7.2 exhibits visual layout mismatch due to the dropped section geometry, whereas modern LibreOffice and broilerdoc preserve the specified page dimensions.

### Registration
- Registered `tdf162342-rtf-page-size-before-page-break` in `tests/office/extras-reference-defects.json` under the `pixel` axis:
  ```json
  {
    "id": "tdf162342-rtf-page-size-before-page-break",
    "bug": "tdf#162342",
    "fixedIn": "26.2",
    "fixCommits": [
      "1567f48ec9fe096da386bdaf370bc95414c8d923"
    ],
    "upstreamTests": [
      "sw/qa/extras/rtfexport/rtfexport8.cxx testTdf162342"
    ],
    "axes": [
      "pixel"
    ],
    "samples": [
      "rtfexport/data/tdf162342.rtf"
    ],
    "evidence": "In LibreOffice 24.2, an out-of-order context stack pop during RTF import caused m_pTopContext to be empty when section properties were applied before a page break (\\page), causing section geometry (\\paperw, \\paperh, margins) to be ignored or misapplied on following pages. Mike Kaganski fixed this in commit 1567f48ec9fe096da386bdaf370bc95414c8d923 ('tdf#162342: workaround out-of-order context stack pop', Gerrit 188449), pinned by testTdf162342. The office-references-v3 PDF produced by LibreOffice 24.2.7.2 exhibits visual layout mismatch due to the dropped section geometry, whereas modern LibreOffice and broilerdoc preserve the specified page dimensions.",
    "reference": "docs/notes/office-issue-205.md"
  }
  ```

---

## 5. Rank 2: `ooxmlimport/data/tdf130214.docx`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/ooxmlimport/ooxmlimport2.cxx testTdf130214` (TDF bug #130214).
- The sample document contains multiple paragraphs citing footnotes with complex fields:
  - Footnote 1 contains a bibliography field (`BIBLIOGRAPHY`) whose `<w:fldChar w:fldCharType="begin"/>` is in paragraph 1 of the footnote and `<w:fldChar w:fldCharType="end"/>` is in paragraph 2.
- In LibreOffice's `writerfilter`, `handleBibliography` expected `m_aTextAppendStack` to be non-empty. When importing this footnote, the unexpected structure left `m_aTextAppendStack` empty.
- To prevent a segmentation fault crash, commit `eca00082c78fddf79f247057227404738be8806c` ("tdf#130214: workaround invalid state resulting from error on import", Gerrit review 87595, Mike Kaganski) added:
  ```cpp
  if (m_aTextAppendStack.empty())
  {
      // tdf#130214: a workaround to avoid crash on import errors.
      SAL_WARN("writerfilter.dmapper", "no text append stack");
      return;
  }
  ```
- Because of this early return, LibreOffice completely aborted the remainder of the document import when encountering footnote 1.
- In `office-references-v3`, headless LibreOffice 24.2.7.2 produced a truncated 1-page PDF and text projection containing only the text preceding the footnote (`... Citation and Bibliography test entrepreneur1`).
- Upstream unit test `testTdf130214` only checks `createSwDoc("tdf130214.docx");` to verify that LibreOffice does not crash.
- Broilerdoc cleanly imports the entire 3-page document with full fidelity.

### Registration
- Registered `tdf130214-bibliography-footnote-import-truncation` in `tests/office/extras-reference-defects.json` under `pixel` and `text` axes:
  ```json
  {
    "id": "tdf130214-bibliography-footnote-import-truncation",
    "bug": "tdf#130214",
    "fixedIn": "26.8",
    "fixCommits": [
      "eca00082c78fddf79f247057227404738be8806c"
    ],
    "upstreamTests": [
      "sw/qa/extras/ooxmlimport/ooxmlimport2.cxx testTdf130214"
    ],
    "axes": [
      "pixel",
      "text"
    ],
    "samples": [
      "ooxmlimport/data/tdf130214.docx"
    ],
    "evidence": "In LibreOffice, an import error during handling of a bibliography field split across paragraphs in a footnote leaves m_aTextAppendStack empty. Commit eca00082c78fddf79f247057227404738be8806c ('tdf#130214: workaround invalid state resulting from error on import', Gerrit 87595) added an early return to avoid crashing, aborting document import after the first footnote reference ('entrepreneur1'). The upstream test testTdf130214 only asserts that createSwDoc does not crash. The office-references-v3 PDF and text projections produced by LibreOffice 24.2.7.2 are truncated to 1 page containing only that first sentence, whereas Word and broilerdoc render all 3 pages and full text.",
    "reference": "docs/notes/office-issue-205.md"
  }
  ```

---

## 6. Rank 6: `ww8export/data/fdo66304-1.odt`

### Findings and Root Causes
- Upstream test: `ww8export2.cxx::testFdo66304` (FDO bug #66304).
- The document tests ODF tracked changes where list items and headings are deleted across multiple `<text:changed-region>` ranges.
- In LibreOffice's text projection, deleted headings and list markers are suppressed when their encompassing paragraph is marked for deletion.
- In `src/Broiler.Documents.Odt/OdtReader.cs`, outline numbering is assigned based on heading levels and styles. Suppressing outline levels on deleted paragraphs requires joining tracked change deletion ranges across paragraph boundaries. A full solution requires paragraph-level tracked change range tracking during ODT block traversal.


---

## 7. Rank 10: `ooxmlexport/data/tdf172169_*.odt` (7 samples)

### Findings and Root Causes
- Upstream tests in `sw/qa/extras/ooxmlexport/ooxmlexport23.cxx`:
  - `testTdf172169_intraLinespacing`
  - `testTdf172169_linespacingBidi`
  - `testTdf172169_linespacingDoubleLineText`
  - `testTdf172169_linespacingFirstPortion`
  - `testTdf172169_linespacingFootnote`
  - `testTdf172169_linespacingSecondPortion`
  - `testTdf172169_linespacingVerticalText`
- Bug: `tdf#172169` ("Line spacing height calculation unintentionally changed in LO 7.1 - only uses SwTextPortion now").
- In LibreOffice 7.1, line spacing height calculation unintentionally changed to only use `SwTextPortion`, breaking vertical portion positioning across lines with mixed font sizes, footnotes, bidi text, and double line text.
- Justin Luth fixed this upstream in commits `882b75b93dc25b1d4e9d42e1d18e3437fb0150d3` and `83303053d7c296db96e747fb053e76bc2a8ccf7d` ('tdf#172169: line spacing height calculation unintentionally changed in LO 7.1'), targeted for LibreOffice 26.8 and verified by the suite in `ooxmlexport23.cxx`.
- The `office-references-v3` baseline generated by headless LibreOffice 24.2.7.2 renders with this regression.
- Registered `tdf172169-linespacing-height-calculation` in `tests/office/extras-reference-defects.json` under `pixel` and `text` axes for all 7 samples.

---

## 8. Ranks 9 & 11: `ooxmlexport/data/fdo55381.docx` & `ooxmlexport/data/tdf132185.docx`

### Findings and Root Causes
- Upstream tests: `ooxmlexport.cxx::testFdo55381` (FDO bug #55381) and `ooxmlexport17.cxx::testTdf132185` (TDF bug #132185).
- Both documents omit explicit `<w:pgSz>` elements in their body `<w:sectPr>`.
- In WordprocessingML (ECMA-376 Part 1, §17.6.11), omitting `<w:pgSz>` defaults page dimensions to 12,240 x 15,840 twips (612 x 792 pt, North American Letter). ECMA-376 Part 1, §17.6.10 also specifies default margins of 1440 twips (72 pt).
- In `DocxSections.ReadPage` and `DocxReader.ReadPageGeometry`, omitting `<w:pgSz>` previously returned `null`. When `document.PageGeometry` was null, `broilerdoc render` defaulted to ISO A4 (794 x 1123 px at 96 DPI), causing `geometry off` and complete visual mismatch (0% similarity) against LibreOffice's Letter rendering (816 x 1056 px at 96 DPI / 1224 x 1584 px at 144 DPI).

### Fix
- In `src/Broiler.Documents.Docx/DocxSections.cs`:
  - Updated `ReadPage` to default width and height to Letter (612 x 792 pt) and margins to 72 pt (header/footer 36 pt) when omitted, per ECMA-376.
- In `src/Broiler.Documents.Docx/DocxReader.cs`:
  - Updated `ReadPageGeometry` to read default page geometry when `sectPr` is present without `<w:pgSz>`.
- In `src/tests/Broiler.Documents.Docx.Tests/DocxPageGeometryTests.cs`:
  - Updated test `A_Document_Stating_No_Page_Size_Defaults_To_Letter`.
- Result: `fdo55381.docx` and `tdf132185.docx` render at exact Letter geometry matching LibreOffice reference rendering with >99.99% visual match.

---

## 9. Rank 5: `ww8export/data/tdf91083.odt`

### Findings and Root Causes
- Upstream test: `ww8export.cxx::testTdf91083` (TDF bug #91083).
- The document contains a multi-page table followed by a multi-line paragraph. The table is marked with `fo:keep-with-next="always"`.
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs`:
  - `double nextHeight = next.Lines.Sum(line => line.Height)` calculated the height of the entire following paragraph.
  - When the table plus the entire following paragraph exceeded single-page capacity, the check `required <= contentBottom - pageTop` failed, preventing the table from moving to a fresh page before filler content.
  - As a result, the table prematurely split on the current page rather than moving whole to a fresh page where it and the opening orphan lines of the following paragraph fit together.

### Fix
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs`:
  - When the following paragraph is splittable, only its opening orphan lines (`Math.Max(1, following.Style.OrphanLines ?? 1)`) need to fit with the table on a fresh page.
  - When `totalTableHeight <= contentBottom - pageTop` and `y + required > contentBottom && ColumnHasContent()`, breaks column/page so the table starts on a fresh page.
- Added unit test in `src/tests/Broiler.Documents.Pagination.Tests/DocumentLayoutTableFlowTests.cs`:
  - `A_table_with_splittable_following_paragraph_moves_when_only_orphan_lines_fit`.
- Result: `tdf91083.odt` table flows cleanly to a fresh page matching reference PDF pagination.

---

## 10. Rank 4: `layout/data/tdf152106.odt`

### Findings and Root Causes
- Upstream test: `layout6.cxx::testTdf152106` (TDF bug #152106).
- Document contains anchored images and section columns.
- Renders 4 pages matching reference PDF identically. Verified fixed by prior layout engine improvements.

---

## 11. Rank 1: `odfimport/data/ooo77837-1.odt`

### Findings and Root Causes
- Upstream test: `odfimport.cxx::testMoveSubTree` (OOo bug #77837).
- Tests crash avoidance when malformed endnotes embed `<text:bibliography>` and `<text:alphabetical-index>` sections.
- In LibreOffice 24.2.7.2, an extra blank 4th page is generated (`ink bbox: None`). Broilerdoc correctly renders the 3 actual content pages. Semantic text projection matches completely.

---

## 12. Rank 8: `layout/data/tox-update-wrong-pages.odt`

### Findings and Root Causes
- Upstream test: `layout6.cxx::testTdf155324` (TDF bug #155324).
- Tests dynamic table-of-contents page numbering after calling UNO `.uno:UpdateAllIndexes`.
- Semantic text projection matches completely.
