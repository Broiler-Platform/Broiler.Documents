# Issue #195: top differences

Investigation and fixes for the top differences in [issue #195](https://github.com/Broiler-Platform/Broiler.Documents/issues/195):
1. `layout/data/tdf170381-split-float-table-in-normal-table.docx` (rank 1, severity 1110.06): 1 vs 2 pages, 10 lines semantic text difference.
2. `ooxmlexport/data/tdf126533_pageBitmap.docx` (rank 2, severity 1079.61): blank-vs-inked page background reference defect.
3. `ooxmlexport/data/tdf78902.docx` (rank 3, severity 1079.19): 4 vs 2 pages, floating VML image with extreme negative top margin.
4. `ooxmlexport/data/tdf132149_pgBreak.odt` (rank 4, severity 1075.19): continuation reflow across page widths.
5. `rtfexport/data/dppolyline.rtf` (rank 5, severity 1055.08): legacy RTF polyline drawing primitives skipped.
6. `ooxmlexport/data/cloud.docx` (rank 6, severity 1051.33): empty object range path geometry reference defect.

---

## 1. Rank 1: `layout/data/tdf170381-split-float-table-in-normal-table.docx`

### Findings
- The document has an outer 4-row table whose second row contains a 13-row floating table (`w:tblpPr` with `tblpX` and `tblpY`).
- In `DocxTableReader.cs`: nested tables at `depth > 1` have their floating position suppressed (`depth <= 1 ? ReadPosition(properties) : null`) and emit `docx.table.floating: "A DOCX table nested in a cell stated a floating position; it was laid out in its cell."`
- **Text Difference (10 lines):** LibreOffice imports any positioned table (even one inside a cell) as an `SwFlyFrame` (text frame). LibreOffice's plain text export (`SwASCWriter::WriteStream`) walks body paragraphs and omits fly frames. In broilerdoc, `dump --as text` leaves top-level floating tables out (issue #175), but because nested tables have their position suppressed in the model, their text is dumped, creating 10 lines of semantic differences (the 10 non-empty rows of the nested table).
- **Pagination (1 vs 2 pages):** Upstream bug **tdf#170381** (*"Some documents with floating tables hang on opening"*, commit `fb203eb57a1ba0accf1894672d35ddf9430d8b05` by Mike Kaganski, Gerrit 197524, Jan 2026) added handling for split-but-not-yet-moved floating tables nested in normal tables across pages. The first paragraph has `w:spacing w:before="9018"` (450 pt), pushing the table to the bottom of the page and causing the nested floating table to split across 2 pages in LibreOffice. In broilerdoc, cell-nested floating-table overflow and splitting across pages remains an open layout roadmap item (as noted in `docs/roadmap.md` and `docs/notes/office-issue-145.md`).

---

## 2. Rank 2: `ooxmlexport/data/tdf126533_pageBitmap.docx`

### Findings
- The sample document contains a page background bitmap fill defined via VML (`w:background/v:background/v:fill type="frame"`).
- **The Reference Defect:** LibreOffice before 24.8 did not import page background bitmap fills from VML, leaving the page background completely blank.
  - Upstream bug: **tdf#126533** (*"FILEOPEN/FILESAVE DOCX: bitmap background image not saved with document"*).
  - Fixing commit: `c14574e7ad2d3ec2ada40808be3332fac71e9767` (*"tdf#126533 docx import: page background vml fill"*, Justin Luth, April 2024).
  - Upstream test: `sw/qa/extras/ooxmlexport/ooxmlexport21.cxx testTdf126533_pageBitmap`.
  - First release with fix: **LibreOffice 24.8.0**.
- **The Reference Baseline:** The CI reference toolchain uses **LibreOffice 24.2.7.2**, which predates this fix. The reference PDF renders the page completely blank (`blank-vs-inked`, blur diff 40.89%, coverage delta 37.97%).
- `broilerdoc` already reads and renders the page bitmap background correctly (`background: bitmap stretch`). Comparing against the 24.2 baseline penalizes correct rendering.
- **Resolution:** Recorded as a Known Reference Defect in `tests/office/extras-reference-defects.json` under `axes: ["pixel"]`, fixed in 24.8.

---

## 3. Rank 3: `ooxmlexport/data/tdf78902.docx`

### Findings and Root Causes
- `tdf78902.docx` has a full-page VML shape (595.1 x 841.7 pt) with embedded `<v:imagedata>` and `style="position:absolute;...;margin-top:-1006.4pt;..."`.
- **Root Cause 1:** In `DocxReader.ReadVmlShape`, any shape with descendants of type `v:imagedata` was skipped (`shape.Descendants(DocxNamespaces.Vml + "imagedata").Any()`). This caused the VML shape to fall through to `ReadPicture`. In `ReadPicture`, because `anchor` was null, the picture was treated as an inline image in the paragraph text (`Runs[0].Style.Image`). The full-page inline image could not fit on page 1, breaking to page 2, pushing the remaining text to page 3, and the next section to page 4 (producing 4 pages instead of 2).
- **Root Cause 2:** Upstream bug **tdf#78902** (*"VML import: workaround for extreme top margin"*, commit `37b5f1ed31398863f6fb39f60a9c6fe31c2ec512` by Miklos Vajna, July 2014) established that when `marginTop < -1000 pt` (-1006.4 pt in `tdf78902.docx`), `w10:wrap type="through"` must be treated as `ShapeWrap.None` (no wrapping), preventing text from wrapping around an off-page graphic.

### Fix
- Made `DocxImageLoader.ReadVmlPicture` internal in `src/Broiler.Documents.Docx/DocxImages.cs`.
- In `src/Broiler.Documents.Docx/DocxReader.cs`:
  - `ReadVmlShape`: Only skip `imagedata` when `position` is NOT `absolute`.
  - `ReadVmlShapeElement`: Read `imagedata` via `context.Images.ReadVmlPicture(...)`, emit diagnostic `docx.image.anchored`, and pass the image to `DocumentShape(..., image: image, ...)`.
  - `ReadVmlShapeElement`: If `marginTop < -1000`, set `wrap = ShapeWrap.None`.
- Result: `broilerdoc render tdf78902.docx` now outputs exactly **2 pages** (matching LibreOffice's `testTdf78902`), resolving the page count and blank-vs-inked mismatch.

---

## 4. Rank 4: `ooxmlexport/data/tdf132149_pgBreak.odt`

### Findings
- Covers paragraph continuation across page breaks where the destination page style defines a different page geometry and narrower content width (issues [#137](office-issue-137.md), [#145](office-issue-145.md), [#147](office-issue-147.md)).
- Advancing page style sequences in ODT refreshes paper and margins, but paragraphs continuing across page breaks currently keep the text-column width computed at the start of the paragraph.
- In issue #145, an experimental prototype for re-composing remaining paragraph tails across alternating page widths was tested (`issue145-tail-reflow-rejected.patch`), but caused visual regressions on overall pagination (5 vs 5 regressing to 6 vs 5 pages). The prototype was reverted and remains documented as an architectural roadmap topic ("Continuation reflow across page widths").

---

## 5. Rank 5: `rtfexport/data/dppolyline.rtf`

### Findings and Root Causes
- `dppolyline.rtf` contains four legacy RTF drawing objects using `\dppolyline` with `\dppolycount2` and vertex coordinates `\dpptx`, `\dppty`.
- In `src/Broiler.Documents.Rtf/RtfLegacyDrawing.cs`, `\dppolyline` was categorized under unsupported drawing primitives (`_unsupported = true`), dropping all shapes with warning diagnostic `rtf.drawing.legacy: An unsupported legacy RTF drawing primitive or aggregate was skipped.`
- Because all four drawings were dropped, the document rendered as a completely blank page (`blank-vs-inked`).
- In RTF drawing markup, a 2-point polyline (`\dppolyline\dppolycount2\dpptx X1\dppty Y1\dpptx X2\dppty Y2`) defines a line connecting `(X1, Y1)` to `(X2, Y2)`. When `(X2 - X1) * (Y2 - Y1) < 0`, the line ascends (`ShapeGeometry.LineAscending`); otherwise it is descending (`ShapeGeometry.Line`).

### Fix
- Updated `src/Broiler.Documents.Rtf/RtfLegacyDrawing.cs` to accept `dppolyline`, record vertex coordinates from `\dpptx` and `\dppty`, and construct a `DocumentShape` with line geometry (`Line` or `LineAscending`) when `count == 2`.
- Diagnoses polylines with `count != 2` as unsupported legacy RTF drawings.
- Added unit tests in `src/tests/Broiler.Documents.Rtf.Tests/RtfLegacyDrawingTests.cs`.
- Result: `broilerdoc info dppolyline.rtf` now loads all 4 shapes with status `Success` and 0 diagnostics. Rendering produces the inked page matching LibreOffice.

---

## 6. Rank 6: `ooxmlexport/data/cloud.docx`

### Findings
- The document consists of a VML group canvas containing a cloud shape with path data (`<v:shape id="Cloud" ...>`).
- **The Reference Defect:**
  - Upstream review: **gerrit#167965** (*"Handle empty range correctly"*, commit `08da27fc377f443d1d6698f4a061fa462849062d` by Mike Kaganski, May 22, 2024).
  - Upstream test: `sw/qa/extras/ooxmlexport/ooxmlexport21.cxx testEmptyObjectRange`.
  - First release with fix: **LibreOffice 24.8.0**.
  - In LibreOffice 24.2, `SdrPathObj::TRGetBaseGeometry` failed when handling an empty polygon range in path calculation: *"Otherwise, the translation gets MAX_DBL value, and scaling becomes 0."*
- **The Reference Baseline:** The CI reference toolchain uses **LibreOffice 24.2.7.2**, which predates this fix. The reference PDF renders the cloud with scale 0 at `(0, 0)`, leaving the page without the shape (coverage delta 0.09%, ink box delta 508 px from origin).
- `broilerdoc` correctly parses the VML group and renders the cloud shape on the page.
- **Resolution:** Recorded as a Known Reference Defect in `tests/office/extras-reference-defects.json` under `axes: ["pixel"]`, fixed in 24.8.
