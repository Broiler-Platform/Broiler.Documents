# Office issue #234: tdf163230.fodt, hyperlink-field.odt, calc-data-source.ods

I reproduced each sample locally on Windows at 96 dpi. Each broilerdoc render
was compared with the LibreOffice 24.2 reference PDF where the local reference
archive has one, and with a local LibreOffice 26.2.6.3 export. Ink is the count
of pixels darker than 200 on each page. The harness drops parity blank pages
(`parityBlankPages`) before it pairs pages.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `layout/data/tdf163230.fodt` | The table's first row is marked keep-together (`fo:keep-together="always"`) but is taller than the 19 cm page. LibreOffice 24.2 draws two pages. Page 1 clips the row at the page foot, and page 2 holds only the second row, so the rest of the first row is never drawn (tdf#163230). The fix is commit `d5c0b3f9`, cherry-picked to libreoffice-24-8 as `b387a283`. The upstream test `layout4.cxx TestTdf163230` asserts three pages. | **Reference defect, recorded** in `tests/office/extras-reference-defects.json` (fixed in 24.8; pixel axis only, because the text export is complete). broilerdoc already splits the row over three pages. Local LibreOffice 26.2 exports the same three pages line for line, list bullets included. Ink per page: broilerdoc 36986 / 35677 / 13947; LibreOffice 24.2 30488 / 8513. |
| `ooxmlexport/data/hyperlink-field.odt` | Page 5 (page 3 once parity blanks are dropped) holds a paragraph-anchored frame that links `../../../backup/horde/sxw/lozenge.wmf`, a path outside the package. (1) broilerdoc drew its missing-picture mark, a grey box with a middle line. LibreOffice prints nothing there: its replacement graphic is not part of the exported page, and LibreOffice 24.2 and 26.2 both leave the page empty apart from the footer `v`. (2) The frame also got a black outline. `ReadFloatingPicture` took the outline from `svg:stroke-color`, which the document's default graphic style sets to `#000000` for drawing shapes. A Writer picture frame's edge is its `fo:border`. | **Fixed.** (1) `InlineImage.MarksMissingPayload` (default true) is set false by the ODT reader for a picture the package does not hold (`LoadResult.Missing`). `DocumentPagePainter` then keeps the box in the layout and draws no ink in it. A URL (`odt.image.external`) or an undecodable format keeps the mark, because LibreOffice may draw a picture there. Other codecs are unchanged. (2) A picture frame outside a `draw:g` takes its outline only from `fo:border`; a picture inside a group is a drawing object and keeps its stroke. Ink on that page went from 1005 to 28; the LibreOffice 24.2 reference has 28. Locally the remaining pages pair 683/601 and 34479/31851. That gap is fonts: this machine has no Liberation Serif for Times New Roman. |
| `uiwriter/data/hash#path/calc-data-source.ods` | The sheet declares all 1024 grid columns (`table:number-columns-repeated="1024"`) and fills two. The ODS reader kept a width for every declared column, at a hard-coded inch each, so the projected table had 1024 columns. Squeezed onto one A4 page, they left no room for any glyph, and the page rendered blank. LibreOffice prints the used range A1:B3. | **Fixed:** the page has ink again. `OdsReader` reads each column's `style:column-width` from the automatic column styles (0.6634 in here) and keeps widths for the used columns only. broilerdoc now draws the six names in two 47.8 pt columns at LibreOffice's column positions. The reference archive has no entry for this sample, so the ink was checked against a local LibreOffice 26.2 export: 1531 against 1067. **Not done:** the ODS reader still ignores the sheet's page style and cell styles. LibreOffice prints with `PageStyle_Sheet1` (0.7 in left and right margins, no header or footer) and the `Default` cell style (Calibri 11 pt). It also lets text that does not fit run into the next empty cell. broilerdoc uses its own page defaults and 12 pt text, and wraps `firstname` within its column. Carrying spreadsheet page and cell styles into the projection is a larger feature than this fix. |

## Verification

- `OdtIssue234Tests` (Broiler.Documents.Odt.Tests) covers four cases:
  - links out of the package, and absent entries, lose the missing mark;
  - a URL keeps it;
  - the default graphic stroke does not outline a picture frame;
  - `fo:border` still does.
- `OdsIssue234Tests` (Broiler.Documents.Ods.Tests): only the used columns are
  kept, at their stated width.
- `DocumentPaginatorTests.Missing_Payload_Image_Without_A_Mark_Draws_Nothing`
  (Broiler.Documents.Pagination.Tests): no ink for an unmarked placeholder, and
  `With*` copies carry the flag.
- Projects run: Odt, Ods, Pagination, Model, Docx, Html, Office and
  Broiler.Documents.Tests (reference-defect guard).
