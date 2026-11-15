# Office issue #249: tdf138039.odt, document_with_linked_graphic.odt, calc-data-source.ods

Run 161 (commit `2f27f9f`, LibreOffice 24.2.7.2 references) ranked these three
samples highest. I reproduced each one on Linux against a local LibreOffice
24.2.7.2. Samples were taken from LibreOffice core at the pinned corpus revision
`54dd3de`.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `layout/data/tdf138039.odt` (4 vs 3 pages) | An empty paragraph after a one-row table anchors a 790.95 pt picture (`style:wrap` unset, so text goes above and below it). The picture is taller than the 780.3 pt body. LibreOffice draws page 1 with the table, page 2 with the picture, and page 3 with the empty line: the line avoids the frame (`SwTextFormatter::CalcFlyWidth`), as the issue #182 note says. broilerdoc moved the paragraph and its picture to page 2. The frame then pushed the line off that page. ODT paragraphs keep two lines together, so the line took the widow/orphan branch of the line loop. That branch re-anchored the paragraph on page 3 unconditionally. The picture, already drawn on page 2, was drawn again on page 3 and pushed the line on to page 4. The overflow branch beside it already left a wrapping frame on its page ("a wrapping frame keeps its own page above"). | **Fixed.** The widow/orphan branch re-anchors only after a column break on the same page, or when the paragraph has no wrapping shapes of its own (`LeavesWrappingShapesBehind`: one text wraps around, none as-character). Layout is now 3 pages: table; picture; empty line. |
| `uiwriter/data/hash#path/calc-data-source.ods` (8 text differences, blank-vs-inked page) | This regressed in `75550c4`, which read cell formatting. Calc gives every grid column the `Default` cell style (`table:default-cell-style-name`: Calibri 11 pt, bottom aligned) and every row a height. The new used-range test (`HasCellContent`) counted any font, alignment or number format as content, and kept every row with a height. So the issue #212/#234 trimming stopped working. All 1024 columns came back, the CSV export had a thousand commas a row, and the squeezed table drew no glyph. | **Fixed.** `MarksUsedRange` now counts what LibreOffice counts for its print and export range (`ScTable::GetPrintArea`, `ScPatternAttr::IsVisible`): a value, a formula, a span, or a fill or border. A number format, font or alignment alone does not count, wherever it comes from. Neither does a row height, for trailing rows. Kept cells still carry the formatting they inherit. A repeated blank cell with a style of its own is still stored. The CSV matches LibreOffice's byte for byte, and the page draws the six names again. |
| `globalfilter/data/document_with_linked_graphic.odt` | The only picture links `../linked_graphic.jpg`, outside the package. The package has no copy, only `Thumbnails/thumbnail.png`. LibreOffice resolves the link against the document's URL and draws the photograph. | **Not fixed, by policy.** ADR 0004: broilerdoc does not read files that document content names. This is the same decision the issue #236, #238, #240 and #242 notes record. Drawing it would need an explicit opt-in, like HTML's `--allow-local-images`, that also reaches the parent directory. That is a policy change, not a rendering fix. |

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
