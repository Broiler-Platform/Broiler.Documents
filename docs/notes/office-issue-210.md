# Office Conformance Issue 210

Investigation and fixes for top-ranked differences identified in [issue #210](https://github.com/Broiler-Platform/Broiler.Documents/issues/210) (Run 132 triage against headless LibreOffice 24.2.7.2):

1. `odfimport/data/ooo77837-1.odt` (rank 1, severity 1000.63, 3 vs 4 pages): Upstream LibreOffice 24.2 reference defect, extra blank 4th page produced when endnotes contain index/bibliography sections; modern LibreOffice (26.2+) and broilerdoc render 3 pages with 100% text match.
2. `layout/data/tdf152106.odt` (rank 2, severity 991.02, 3 vs 4 pages): Multi-column section with floating frames straddling columns, forward-pass paragraph-anchored shape wrapping.
3. `ooxmlexport/data/tdf146955.odt` (rank 3, severity 967.06, 40 vs 40 pages): Upstream broken test fixture with 320 page-anchored frames (up to page 367) where body text ends on page 28, leaving pages 29-30 blank in LibreOffice.
4. `ooxmlexport/data/tdf128207.docx` (rank 4, severity 965.73, 1 vs 1 page): DrawingML `<c:chart>` without bitmap fallback emits diagnostic `docx.image.shape`.
5. `layout/data/tdf130031.docx` (rank 5, severity 964.92, 1 vs 1 page): DrawingML `<c:chart>` without bitmap fallback emits diagnostic `docx.image.shape`.
6. `layout/data/testAreaChartNumberFormat.docx` (rank 6, severity 961.90, 1 vs 1 page): DrawingML `<c:chart>` without bitmap fallback emits diagnostic `docx.image.shape`.
7. `layout/data/tox-update-wrong-pages.odt` (rank 7, severity 961.68, 15 vs 13 pages): 81 Table of Contents entries on ISO A5 with footer clearance and font line height differences spilling 5 entries to page 5.
8. `layout/data/tdf130380.docx` (rank 8, severity 953.78, 1 vs 1 page): DrawingML `<c:chart>` without bitmap fallback emits diagnostic `docx.image.shape`.
9. `odfexport/data/tdf134987.docx` (rank 9, severity 943.92, 1 vs 1 page): DrawingML `<c:chart>` without bitmap fallback emits diagnostic `docx.image.shape`.
10. `layout/data/tdf129054.docx` (rank 10, severity 943.79, 1 vs 1 page): DrawingML `<c:chart>` without bitmap fallback emits diagnostic `docx.image.shape`.
11. `uiwriter/data/tdf132321.odt` (rank 11, severity 940.92, 1 vs 1 page): Document page count matches (1 vs 1 page); visual blur difference (13.6%) caused by subtle font glyph rasterisation differences on Liberation Serif.
12. `ooxmlexport/data/tdf141173_missingFrames.rtf` (rank 12, severity 938.36, 2 vs 2 pages): RTF shape positions and header frame flow.
13. `ooxmlexport/data/tdf142407.docx` (rank 13, severity 937.30, 1 vs 1 page): Visual blur difference (10.2%) from DrawingML vector diagram shapes without raster fallback.
14. `odfimport/data/tdf92586.odt` (rank 14, severity 936.66, 1 vs 1 page): Visual blur difference (18.4%) from complex embedded shape formatting.
15. `ooxmlexport/data/tdf153613_anchoredAfterPgBreak.docx` (rank 15, severity 935.78, 2 vs 2 pages): Floating image anchoring across manual page break.
16. `odfimport/data/tdf165156.odt` (rank 16, severity 933.40, 2 vs 3 pages): Section-level page style break interaction.
17. `ooxmlexport/data/hyperlink-field.odt` (rank 17, severity 933.33, 3 vs 3 pages): Field author metadata projection ("Caolán McNamara" vs LO cached "Unknown Author").
18. `ooxmlexport/data/tdf153964_numberingAfterBreak14.docx` (rank 18, severity 932.06, 3 vs 4 pages): Trailing empty paragraph after page break in numbered list.
19. `layout/data/tdf122607_leerzeile.odt` (rank 19, severity 931.00, 1 vs 1 page): Font line leading metrics on empty line (`odt.linespacing.leading`).
20. `tiledrendering/data/shape-fill-link.fodt` (rank 20, severity 930.99, 1 vs 1 page): Shape with remote/external fill image (`odt.image.external`).
21. `rtfexport/data/tdf158409.rtf` (rank 21, severity 930.22 -> 0.0, 1 vs 1 page): Missing semantic `DocTitle` text lines from `{\field{\*\fldinst TITLE}}` without cached result; fixed in `RtfReader.cs` by capturing `\info \title` metadata and using it as fallback.
22. `htmlimport/data/tdf122789.html` (rank 22, severity 929.52, 1 vs 1 page): External HTTP stylesheet/image loading skipped per security policy (`html.skip.external`).
23. `uiwriter/data/tdf143574.odt` (rank 23, severity 929.51, 1 vs 1 page): Visual blur difference (6.7%) from shape gradient fill rendering.
24. `layout/data/i84870.fodt` (rank 24, severity 929.43 -> 0.0, 3 vs 2 pages -> 2 vs 2 pages): As-character anchored 18cm shape in empty paragraph caused false-positive `ColumnHasContent()` on fresh column, triggering an extra column break; fixed in `DocumentLayout.cs`.
25. `ooxmlexport/data/mce-nested.docx` (rank 25, severity 927.35 -> 0.0, 1 vs 1 page): `wp:positionV` and `wp:positionH` nested inside `<mc:AlternateContent>` were ignored in `DocxReader.cs`, placing shapes at (0, 0); fixed by extracting positioning and wrap elements across AlternateContent choices and fallbacks.
26. `ooxmlexport/data/tdf145542_image_size.fodt` (rank 26, severity 926.95 -> 0.0, 1 vs 1 page): Inline base64 payload in `<office:binary-data>` had missing `'='` padding, throwing `FormatException`; fixed in `OdtImages.cs` by padding unpadded base64 data.
27. `uiwriter/data/tdf156560.docx` (rank 27, severity 926.75, 3 vs 3 pages): Header even/first page differentiation (`docx.header.even`, `docx.header.first`).
28. `ooxmlexport/data/kde216114-1.odt` (rank 28, severity 926.52 -> 0.0, 1 vs 1 page): Block-level `<draw:g>` drawing groups were unhandled in `OdtReader.cs`, emitting `odt.block.unsupported` and dropping 40 child shapes; fixed by implementing recursive `ReadDrawGroup`.
29. `layout/data/shape-anchored-in-follow.docx` (rank 29, severity 925.51, 3 vs 3 pages): Multi-column section shape flow (`docx.section.columns`).
30. `odfimport/data/tdf120677.fodt` (rank 30, severity 925.42 -> 0.0, 1 vs 1 page): Text box frame stating `svg:width="0cm"` was dropped due to `width <= 0`; fixed in `OdtReader.cs` and `DocumentLayout.cs` by allowing zero-width text boxes to grow width to text.

---

## 1. Rank 24: `layout/data/i84870.fodt`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/layout/layout.cxx testI84870` (Apache OpenOffice / LibreOffice bug #84870).
- The document contains an 18cm tall frame anchored as-character inside an empty paragraph following preceding body text.
- Because the tall shape does not fit on page 1, the paragraph moves to page 2.
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs`:
  - When starting to lay out page 2, `currentLines.Count == columnStart` (0 lines placed).
  - Line 1336 executed `_paragraphTops.TryAdd(paragraphIndex, y)`, adding paragraph 1 to `_paragraphTops`.
  - Line 410 previously checked:
    ```csharp
    bool ColumnHasContent() =>
        currentLines.Count > columnStart || (column == 0 && (regionTop > pageTop || floatBoxes.Count > 0 ||
            _documentShapes.Any(shape => (shape.Wraps || shape.IsAsCharacter) && (shape.AnchorPage == pages.Count + 1 ||
                (!shape.IsPageAnchored && _paragraphTops.ContainsKey(shape.ParagraphIndex))))));
    ```
  - Because `shape.IsAsCharacter` was checked and `_paragraphTops.ContainsKey(1)` was true, `ColumnHasContent()` returned `true` on an empty column.
  - When placing the line at line 1543, `y + line.Height > contentBottom` was true, `ColumnHasContent()` was true, and `heldByOwnShapes` required `line.LeadingSkip > 0` (which was 0 on a fresh page).
  - As a result, `BreakColumn()` was invoked on an empty column, creating a blank page 2 and moving the shape to page 3 (3 pages vs 2 pages in LibreOffice).

### Fix
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs`:
  - Removed `shape.IsAsCharacter` from `ColumnHasContent()`: an inline as-character shape is part of a text line and never occupies a column prior to the line being added to `currentLines`.
  - Updated `heldByOwnShapes` in line 1535 so that an empty paragraph containing an as-character shape on a fresh column (`currentLines.Count == columnStart`) stays on the page rather than breaking into an empty page:
    ```csharp
    bool heldByOwnShapes = first && paragraph.Length == 0 &&
        ((currentLines.Count == columnStart && _documentShapes.Any(shape => shape.ParagraphIndex == paragraphIndex && shape.IsAsCharacter)) ||
         (line.LeadingSkip > 0 &&
          currentLines.Count == columnStart &&
          _documentShapes.Any(shape => shape.ParagraphIndex == paragraphIndex && shape.Wrap == ShapeWrap.Square) &&
          !_documentShapes.Any(shape => shape.ParagraphIndex == paragraphIndex && shape.Wrap == ShapeWrap.TopAndBottom)));
    ```
- In `src/tests/Broiler.Documents.Pagination.Tests/DocumentLayoutShapeSizeTests.cs`:
  - Added unit test `An_As_Character_Tall_Shape_Does_Not_Create_Spurious_Extra_Pages` verifying that a tall as-character shape moving to a fresh page does not break into a spurious 3rd page.
- Result: `i84870.fodt` lays out on 2 pages identically matching LibreOffice reference page count (severity reduced from 929.43 to 0.0).

---

## 2. Rank 21: `rtfexport/data/tdf158409.rtf`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/rtfexport/rtfexport7.cxx testTdf158409` (TDF bug #158409).
- The document defines document metadata in `{\info {\title DocTitle}}` and references it via fields `{\field{\*\fldinst TITLE}}` without a cached `\fldrslt`.
- In `src/Broiler.Documents.Rtf/RtfReader.cs`:
  - The destination `\info` was unconditionally skipped (`RtfDestination.Skip`).
  - When the `TITLE` field was encountered with `Characters == 0`, no fallback was provided, dropping the title and producing 2 missing lines of text in broilerdoc compared to LibreOffice.

### Fix
- In `src/Broiler.Documents.Rtf/RtfDestination.cs`:
  - Added destinations `Info`, `InfoTitle`, `InfoAuthor`, and `InfoSubject`.
- In `src/Broiler.Documents.Rtf/RtfReader.cs`:
  - Handled `\info` and nested `\title`, `\author`, `\subject` destinations to capture document metadata into `_infoTitle`, `_infoAuthor`, and `_infoSubject`.
  - In `HandleGroupEnd()` when closing `RtfDestination.Field` with `Characters == 0`:
    Added `ResolveFieldFallback(instruction)` checking for `TITLE`, `AUTHOR`, and `SUBJECT` field keywords and appending the captured info text when present.
- In `src/tests/Broiler.Documents.Rtf.Tests/RtfRubyFieldTests.cs`:
  - Added unit tests `Title_Field_Without_Result_Falls_Back_To_Info_Title` and `Title_Field_With_Cached_Result_Prefers_Cached_Result`.
- Result: Semantic text comparison now matches 100% with LibreOffice (severity reduced from 930.22 to 0.0).

---

## 3. Rank 25: `ooxmlexport/data/mce-nested.docx`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/ooxmlexport/ooxmlexport12.cxx testMceNested` (nested Markup Compatibility in Word drawing).
- The document has a `<wp:anchor>` containing `<mc:AlternateContent>` with `<mc:Choice Requires="wps">` wrapping `<wp:positionH>` and `<wp:positionV>`.
- In `src/Broiler.Documents.Docx/DocxReader.cs`:
  - `PositionOffset` and `RelativeFrom` checked only direct children: `anchor.Element(DocxNamespaces.WordDrawing + axis)`.
  - Because `wp:positionV` was wrapped inside `<mc:AlternateContent>`, vertical offset returned 0 and relative-from returned null, misplacing the cover shape at y=0 instead of y=198 pt (`posOffset=2514600` EMU), resulting in a severe blur diff of 78.62%.

### Fix
- In `src/Broiler.Documents.Docx/DocxReader.cs`:
  - Introduced helper `PositionElement(anchor, axis)` searching direct children, `<mc:AlternateContent>` choice and fallback branches, and descendants for `positionH` and `positionV`.
  - Updated `PositionOffset(anchor, axis)` and `RelativeFrom(anchor, axis)` to resolve through `PositionElement`.
  - Updated `ReadWrap(anchor)` to inspect wrap definitions nested inside `<mc:AlternateContent>` choice and fallback branches.
  - Updated `extent` lookup to check descendants when not directly on `anchor`.
- In `src/tests/Broiler.Documents.Docx.Tests/DocxShapeTests.cs`:
  - Added unit test `AlternateContent_Wrapped_Position_And_Extent_Are_Read`.
- Result: Shapes inside nested `mc:AlternateContent` are placed with exact coordinates and extents (severity reduced from 927.35 to 0.0).

---

## 4. Rank 26: `ooxmlexport/data/tdf145542_image_size.fodt`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/ooxmlexport/ooxmlexport17.cxx testTdf145542ImageSize` (TDF bug #145542).
- The document contains an inline base64 image inside `<office:binary-data>` whose length is not a multiple of 4 (`len % 4 == 3`, missing `'='` padding).
- In `src/Broiler.Documents.Odt/OdtImages.cs`:
  - `Convert.FromBase64String` threw `FormatException`, triggering `odt.image.binary` diagnostic and skipping the picture.

### Fix
- In `src/Broiler.Documents.Odt/OdtImages.cs`:
  - In `LoadFromBinaryData`, filtered out whitespace and padded unpadded base64 strings with `'='` where `rem == 2` or `rem == 3` before passing to `Convert.FromBase64String`.
- In `src/tests/Broiler.Documents.Odt.Tests/OdtImageTests.cs`:
  - Added unit test `Reads_A_Picture_Held_Inline_As_Unpadded_Base64`.
- Result: Inline unpadded base64 images decode cleanly without errors (severity reduced from 926.95 to 0.0).

---

## 5. Rank 28: `ooxmlexport/data/kde216114-1.odt`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/ooxmlexport/ooxmlexport4.cxx testKde216114_1` (KDE bug #216114).
- The document contains block-level `<draw:g>` shape groups grouping 40 child shapes.
- In `src/Broiler.Documents.Odt/OdtReader.cs`:
  - `ReadBlockContent` had no handler for `<draw:g>`, falling through to `builder.AddUnsupportedBlock(name)` (`odt.block.unsupported:g`).
  - All 40 shapes were discarded, leaving an empty document.

### Fix
- In `src/Broiler.Documents.Odt/OdtReader.cs`:
  - Implemented `ReadDrawGroup` method that parses `<draw:g>` groups recursively, accumulating group-level `svg:x` and `svg:y` offsets, inheriting group `text:anchor-type` and `text:anchor-page-number`, and dispatching child elements (`draw:g`, `draw:custom-shape`, `draw:rect`, `draw:ellipse`, `draw:line`, `draw:frame`).
  - Added `<draw:g>` dispatch in `ReadBlockContent` and `ReadParagraphChildren`.
  - Updated `ReadShape`, `ReadShapeLine`, `ReadFloatingPicture`, and `PlaceOnPage` to support parent coordinate offsets and inherited anchor metadata.
- In `src/tests/Broiler.Documents.Odt.Tests/OdtShapeTests.cs`:
  - Added unit test `A_Block_Level_Drawing_Group_Is_Read_With_Children_And_Inherits_Anchor`.
- Result: Block-level and paragraph-level drawing groups are read with all child shapes accurately positioned (severity reduced from 926.52 to 0.0).

---

## 6. Rank 30: `odfimport/data/tdf120677.fodt`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/odfimport/odfimport.cxx testTdf120677` (TDF bug #120677).
- The document contains an ODT frame with `<draw:text-box>` and `svg:width="0cm"`, containing text paragraphs (`. `).
- In `src/Broiler.Documents.Odt/OdtReader.cs`:
  - Line 894 rejected drawings where `!TryReadFrameWidth(drawing, out double width) || width <= 0`, rejecting zero-width text box frames.
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs`:
  - Line 3070 had `if (!shape.GrowsToText || ShapeParagraphs(shape).Count == 0 || shape.Width <= 0 ...)` which prevented computing grown height when `shape.Width == 0`, even when `GrowsWidthToText` had computed a positive width.

### Fix
- In `src/Broiler.Documents.Odt/OdtReader.cs`:
  - Permitted `width <= 0` when `textBox is not null`, setting `growsWidthToText = true` so the box sizes to fit its text.
- In `src/Broiler.Documents.Pagination/DocumentLayout.cs`:
  - Updated line 3070 to check `BoxWidth(shape) <= 0` instead of `shape.Width <= 0`.
- In `src/tests/Broiler.Documents.Odt.Tests/OdtShapeTests.cs`:
  - Added unit test `A_Text_Box_With_Zero_Width_Is_Read_And_Grows_Width_To_Text`.
- In `src/tests/Broiler.Documents.Pagination.Tests/DocumentLayoutShapeSizeTests.cs`:
  - Added unit test `A_Box_With_Zero_Stated_Width_That_Grows_Width_To_Text_Computes_Height`.
- Result: Zero-width text boxes grow to fit their text, rendering and outputting text matching LibreOffice (severity reduced from 925.42 to 0.0).
