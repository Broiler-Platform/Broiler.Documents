# Office issue #236: i84870.fodt, tdf165089.fodt, tdf107209-vert-rtl.fodt, fdo114306.odt, tdf155011.html, 4_v01.ods

I reproduced each sample locally on Windows at 96 dpi with `broilerdoc-office --sample`,
the `office-references` archive (LibreOffice 24.2.7.2, `--accept-reference-mismatch`)
and its font directory. `mailmerge/data/4_v01.ods` is not in the local archive, so it
was compared with a local LibreOffice 26.2.6.3 export instead. Ink is the count of
pixels darker than 200 on a page, with its bounding box in pixels.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `layout/data/i84870.fodt` | The header holds a paragraph-anchored text frame with no `svg:width` (`fo:min-width` 4 cm), `style:horizontal-pos="right"` against `page-content`, and `svg:y` 1 mm from its paragraph. (1) `ReadParts` gave a header no page geometry; only a footer got one. `PlaceOnPage` therefore saw a content area of width 0 and put the frame its own width left of the margin, so only "der" showed at the page's left edge. (2) A header frame measured from its paragraph took `svg:y` from the top of the page. Writer measures it from the paragraph, and the header band starts at the page's top margin. | **Fixed.** A header part now gets the page geometry too, and the band origin that a footer frame already used (`OdtReadContext.BandOrigin`, formerly `FooterOrigin`) is set to the header's top (`PageGeometry.HeaderDistance`). Frames measured from the page (`vertical-rel="page"` or `page-content`) keep their own branch, so the ODT writer's header shapes (written against the page) still round-trip. Page 1 ink went from 91 at 0,9-19,21 to 476 at 353,66-465,77; LibreOffice 24.2 has 581 at 352,63-486,74. The pixel check now passes. The remaining width is the face: LibreOffice drew the frame text in a serif. |
| `layout/data/tdf165089.fodt` | The page has a printed line text grid (four lines, `layout-grid-print="true"`), and the settings turn on `MsWordCompGridMetrics`. LibreOffice 24.2 centres the grid on the page, so the reference PDF draws "Should be at top of page" and the grid about 4.4 in down. tdf#165089 turned the centring off under Word grid metrics: core `fd7717f81b18faab0e400c9fa3e87c4579f254d1` (master, 2025-02-18, first released in 25.8), test `layout5.cxx testTdf165089`. | **Reference defect, recorded** in `tests/office/extras-reference-defects.json` (pixel axis only; the text export is the same). broilerdoc draws the paragraph at the top of the body, and local LibreOffice 26.2.6.3 does too. **Not done:** broilerdoc does not draw the printed grid lines, and LibreOffice 26.2 fills the whole page with them. |
| `layout/data/tdf107209-vert-rtl.fodt` | The page writes vertically (right to left). LibreOffice 24.2 draws the "A" and "B" paragraphs over each other in one column on page 1 and leaves page 2 blank. That is tdf#107209 (vertical text after a fly portion), fixed by core `c7f1e41ca672f6bb1055b92012fff0d92a5ff805` and cherry-picked to libreoffice-24-8 as `00d6f1f64a6eb93b7a5544fb612118c9ccf1cf50`, with tests `layout3.cxx TestTdf104209VertRTL`/`VertLTR` (two pages, the B paragraph on page 2). broilerdoc has no vertical layout: `PageGeometry.VerticalWriting` only reaches the painter's glyph choice, so the paragraphs are set horizontally on one page. | **Not fixed, not recorded.** The reference is defective, but broilerdoc does not draw what the fixed LibreOffice draws either, so a defect row would hide a real gap. Laying out vertical pages is a feature, not a focused fix. The `-vert-ltr` sibling is the same case. |
| `uiwriter/data/fdo114306.odt` | A three-column table whose A1 spans three rows and C1 two, each holding enough text for more than a page. The layout keeps a row holding a row-spanning cell whole (`RowBox.CanSplit` is false when any cell spans rows), so the merged block runs past the bottom margin to the page's edge, page 2 holds only B3 and C2 with the columns' rules down to the page foot, and a blank page 3 follows. LibreOffice splits the merged cells across the page and closes the table on page 2. | **Not fixed.** Splitting a group of rows tied by a vertically merged cell (each cell cut on its own line boundary, the rows it covers placed in step) is a change to `DocumentLayout`'s table pagination, not a focused fix. The local text faces also differ: the runner maps `serif` to DejaVu Sans. |
| `htmlimport/data/tdf155011.html` | Nested tables and a form holding one `<input type="radio"/>`. LibreOffice imports it as a form control and draws its 11 px circle (44 ink); the HTML reader skips every `input`, so broilerdoc's page is blank. | **Not fixed.** Drawing the radio button as a DejaVu Sans glyph (U+25CB, as the DOCX reader draws an ActiveX option button) was tried: the page got the circle (52 ink), but 20 px lower and to the right, because the HTML page margins are not LibreOffice's, so the pixel check still failed. The glyph also entered the text projection, while LibreOffice's text export is empty, so the text check, which passed, failed. The change was reverted. Fixing this needs a control the model draws but the text projection leaves out. |
| `mailmerge/data/4_v01.ods` | A sheet of 17 used columns at their stated widths. LibreOffice prints it like a spreadsheet: columns that do not fit across the page go on further pages (3 pages: A-G, G-M and the "Domain" column), at 10 pt, with the page style's "Tabelle1" header and "Seite N" footer. broilerdoc projects the sheet as one table squeezed into one page's width at 12 pt, so every column wraps letter by letter. | **Not fixed.** The issue #212 note fixed the text; the #234 note recorded that the ODS reader ignores the sheet's page and cell styles. Paging a sheet across columns is a projection feature on top of that. |

## Effect of the header change on other samples

The header change moves every header frame that is placed against the content area or
measured from its paragraph. I ran the 24 ODT samples whose headers hold drawings,
before and after, against the 24.2 references:

- Now pass (similarity before in brackets): `i84870.fodt` (2.0%), `odfexport/data/user-field-decl-fly.odt` (17.5%),
  `odfexport/data/z-index-header.odt` (75.2%), `odfimport/data/fdo82165.odt` (0%).
- Better: `tdf143793_noBodyWrapping.odt` 71.2 → 85.1% (still failing on blur), `tdf118242.odt`
  49.2 → 78.4%, `tdf160549.fodt` 58.8 → 69.7%, `fdo47267-3.odt` 77.2 → 77.8%,
  `tdf135595_HFtableWrap.odt` 82.8 → 83.9%, `tdf116501.odt` and `tdf139843.odt`
  slightly.
- Worse: `odfexport/data/tdf104254_noHeaderWrapping.odt` passed and is now at 76.3%.
  Its header picture now sits where LibreOffice draws it (top 105 px against 103 px;
  before, 30 px). The header's text is white on a `#bf819e` paragraph fill, and
  broilerdoc draws neither in the header band, so the page is a lone picture either
  way. That missing paragraph fill is the gap to fix, not the picture's position.
- Pixels changed but the verdict did not: `allow-overlap.odt`, `tdf159158_zOrder_headerBehind.odt`,
  `header-bg.odt` and `testTdf106663.odt` still pass. The other eight rendered the same.

## Verification

- `OdtIssue236Tests` (Broiler.Documents.Odt.Tests) covers three cases:
  - a header frame right-aligned to the content area ends at its right edge;
  - a header frame measured from its paragraph is offset from the header's top;
  - a header frame measured from the page keeps its offset.
- Projects run: Broiler.Documents.Odt.Tests (669), Broiler.Documents.Tests (323,
  including the reference-defect guard), Broiler.Documents.Pagination.Tests (417) and
  Broiler.Documents.Office.Tests (95). All pass.
