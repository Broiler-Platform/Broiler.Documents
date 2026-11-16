# Office issue #249: tdf138039.odt, document_with_linked_graphic.odt, calc-data-source.ods

Run 161 (commit `2f27f9f`, LibreOffice 24.2.7.2 references) ranked these three
samples highest. I reproduced each one on Linux against a local LibreOffice
24.2.7.2. Samples were taken from LibreOffice core at the pinned corpus revision
`54dd3de`.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `layout/data/tdf138039.odt` (4 vs 3 pages) | An empty paragraph after a one-row table anchors a 790.95 pt picture (`style:wrap` unset, so text goes above and below it). The picture is taller than the 780.3 pt body. LibreOffice draws page 1 with the table, page 2 with the picture, and page 3 with the empty line: the line avoids the frame (`SwTextFormatter::CalcFlyWidth`), as the issue #182 note says. broilerdoc moved the paragraph and its picture to page 2. The frame then pushed the line off that page. ODT paragraphs keep two lines together, so the line took the widow/orphan branch of the line loop. That branch re-anchored the paragraph on page 3 unconditionally. The picture, already drawn on page 2, was drawn again on page 3 and pushed the line on to page 4. The overflow branch beside it already left a wrapping frame on its page ("a wrapping frame keeps its own page above"). | **Fixed.** The widow/orphan branch re-anchors only after a column break on the same page, or when the paragraph has no wrapping shapes of its own (`LeavesWrappingShapesBehind`: one text wraps around, none as-character). Layout is now 3 pages: table; picture; empty line. |
| `uiwriter/data/hash#path/calc-data-source.ods` (8 text differences, blank-vs-inked page) | This regressed in `75550c4`, which read cell formatting. Calc gives every grid column the `Default` cell style (`table:default-cell-style-name`: Calibri 11 pt, bottom aligned) and every row a height. The new used-range test (`HasCellContent`) counted any font, alignment or number format as content, and kept every row with a height. So the issue #212/#234 trimming stopped working. All 1024 columns came back, the CSV export had a thousand commas a row, and the squeezed table drew no glyph. | **Fixed.** `MarksUsedRange` now counts what LibreOffice counts for its print and export range (`ScTable::GetPrintArea`, `ScPatternAttr::IsVisible`): a value, a formula, a span, or a fill or border. A number format, font or alignment alone does not count, wherever it comes from. Neither does a row height, for trailing rows. Kept cells still carry the formatting they inherit. A repeated blank cell with a style of its own is still stored. The CSV matches LibreOffice's byte for byte, and the page draws the six names again. |
| `globalfilter/data/document_with_linked_graphic.odt` | The only picture links `../linked_graphic.jpg`, outside the package. The package has no copy, only `Thumbnails/thumbnail.png`. LibreOffice resolves the link against the document's URL and draws the photograph. | **Fixed with an explicit opt-in** (odt-images cluster; see `office-issue-249-odt-images.md` and ADR 0004). broilerdoc reads no file a document names unless asked. The CLI's existing `--allow-local-images` flag, which already let HTML load sibling images, now also covers an ODT picture linked as exactly `../<plain file name>`. That is the file beside the document, by the ODF package IRI rule. The same name rules and byte limits apply, and the default is unchanged. The conformance harness passes the flag for ODT/OTT/FODT samples as it does for HTML. The sample passes, and so does `graphic_shape.odt`. |

## Related change

`4_v01.ods` had the same regression. Below its data are 61 rows whose only styled
cell (`ce1`) sets just a number format. LibreOffice's CSV export ends at the last
row of data, and the new rule does the same. Its CSV export now matches
LibreOffice's. Before, it had four extra comma rows.

## Verification

- `OdsIssue249Tests` (Broiler.Documents.Ods.Tests) has four tests:
  - inherited column style and row heights do not widen the used range, and the
    kept cells keep the inherited font;
  - a visible border past the data still widens it;
  - a number format or font alone past the data does not;
  - a blank cell between values keeps its own style.
- `DocumentLayoutBlockedLineTests.A_Top_And_Bottom_Frame_Is_Drawn_Once_When_Widow_Control_Moves_Its_Empty_Anchor_On`
  (Broiler.Documents.Pagination.Tests).
- All the new tests fail on `84f1af5` and pass with the change. The full
  solution test run passes.
- Layout sweep over all 3,847 `.odt`/`.fodt`/`.docx`/`.rtf` samples in
  `sw/qa/extras`, each laid out on its own stated page. Compared were page
  count, lines and shapes per page, and shape positions, before and after.
  Only `tdf138039.odt` changed.
- All 10 `.ods` samples in `sw/qa/extras` now export CSV identical to
  LibreOffice 24.2.7.2 (before: `calc-data-source.ods` and `4_v01.ods`
  differed). Their rendered page counts are unchanged.

## Fixing the rest: twelve clusters

The user then asked to fix the other items too. The 800 documents the report
tracked were grouped into twelve clusters by format and failing check. Each
cluster was fixed on a branch of its own from `8a20ca0`, and an independent
verifier checked it. The clusters were then merged. Several clusters had made
the same change independently: the text export's first frame, list number
formats, header shapes placed against their paragraph, the
`w:splitPgBreakAndParaMark` split, and the joining of hidden paragraph marks.
The merge kept one implementation of each, and a review of the merge fixed
what it had dropped. Each cluster's causes, fixes and declined items are in
its own note:

| Cluster | Note |
| --- | --- |
| ODT pixel | `office-issue-249-odt-pixel.md` |
| DOCX pixel | `office-issue-249-docx-pixel.md` |
| RTF layout | `office-issue-249-rtf-layout.md` |
| ODT pagination | `office-issue-249-odt-pagination.md` |
| DOCX pagination | `office-issue-249-docx-pagination.md` |
| DOCX text | `office-issue-249-docx-text.md` |
| ODT text | `office-issue-249-odt-text.md` |
| RTF text | `office-issue-249-rtf-text.md` |
| DOCX headers | `office-issue-249-docx-headers.md` |
| DOCX images and shapes | `office-issue-249-docx-images-shapes.md` |
| ODT images | `office-issue-249-odt-images.md` |
| ODS, HTML, PDF | `office-issue-249-misc-ods-html-pdf.md` |

### Result over the whole corpus

The harness ran on all 3,918 `sw/qa/extras` samples twice, at `8a20ca0` and
at the merged head. Both runs used LibreOffice 24.2.7.2 and the
`office-fonts-2` font set on the same machine.

| | `8a20ca0` | merged |
| --- | --- | --- |
| Failed checks | 844 (pixel 569, text 264, read 11) | 514 (pixel 408, text 95, read 11) |
| Documents not passing | 833 (ODT 327, DOCX 342, RTF 142, HTML 13, ODS 6, PDF 3) | 524 (ODT 206, DOCX 234, RTF 71, HTML 10, PDF 3) |

- 310 documents went from failing to passing, or to partial where a known
  LibreOffice defect excludes the check. All 310 are on the tracked list.
- One document went from passing to failing: `uiwriter/data/tdf139638.odt`
  (pixel). The ODT pixel note explains it. The space-above rule that breaks it
  fixes eleven others, and the root cause is the missing OpenSymbol font.
- 27 documents that still fail scored worse. The cluster notes list them,
  mostly as deliberate trade-offs. The largest are:
  - `tdf167843_tblLook_firstRow_tblHeader.docx`: severity 349 to 731, from
    the DOCX collapsing table borders. Its pixel similarity rose from 76.6 to
    78.2 %, and the score went up because the ink box moved.
  - `tdf137335_whitespaceLineHeight.docx`: tab wrapping (DOCX pagination
    note).
  - `tdf134021.docx` and `fdo53985.docx`: collapsing borders (DOCX pixel
    note).
- 93 documents that still fail scored better.

