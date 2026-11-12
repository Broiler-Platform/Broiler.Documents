# Office conformance issue #240: DOCX layout

Nine DOCX samples of [issue 240](https://github.com/Broiler-Platform/Broiler.Documents/issues/240)
about page and column breaks, borders, tabs, tables and columns, run locally
with `broilerdoc-office --references-dir … --accept-reference-mismatch
--font-dir … --font-set office-fonts-2 --dpi 96` against the pinned
LibreOffice 24.2 PDFs. Where the question was what Writer does rather than what
the 24.2 PDF shows, I converted the sample, or a cut-down DOCX or FODT copy, with
the local LibreOffice 24.2.7.2 (the references' version) and read the word boxes
with `pdftotext -bbox`. Similarity is the runner's pixel similarity at 96 dpi.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `ooxmlexport/data/tdf153964_topMarginAfterBreak14.docx` | Two causes shared by the tdf153964 family. Run breaks that open a paragraph were read without writerfilter's tdf#153964 rules (below): a lone column break was carried to the next paragraph, which put the empty cream paragraph in the wrong column. And body paragraph borders were neither drawn nor given room, so every 3 pt box was missing and the empty paragraphs that LibreOffice pushes onto the next page fitted where they were: 5 pages against 7. | Fixed: passes (34.1 % before), 7 pages. |
| `ooxmlexport/data/tdf153964_topMarginAfterBreak15.docx` | The same family in mode 15 (failing since issue 238 at 26.8 %): a column break opening a paragraph kept its 60 pt above, which mode 15 drops. | Fixed: passes. |
| `ooxmlexport/data/tdf153964_firstIndentAfterBreak14.docx` | The document's first paragraph opens with a column break, which writerfilter turns into an empty paragraph before the break; broilerdoc gave the break to the paragraph itself, so its text sat on page one. "How is first line indent affected by a page break?" lost its 60 pt above, which mode 14 keeps. Borders as above. 3 pages against 4. | Improved: 25.9 % → 79.0 %, 4 pages. What is left: the three-line paragraph in column two of page one has room for one line under its 60 pt and border; LibreOffice moves it whole (two-line widow and orphan control, the DOCX default), broilerdoc leaves a line behind. The body layout does not apply the DOCX widow default yet (roadmap, "The DOCX widow and orphan default"). |
| `ooxmlexport/data/tdf153964_numberingAfterBreak14.docx` | As above: "3. How is numbering affected by a page break?" lost its space above, and the borders were missing. | Improved: 7.3 % → 67.1 %, 4 pages as before. The same widow difference as its twin moves "affected by a column break?" to page 2 alone where LibreOffice moves the whole item. |
| `ooxmlexport/data/tdf168607_tabstopZero.docx` | `w:defaultTabStop="65536"`, which Word reads as zero. LibreOffice 24.2 imports a 3,277 pt tab distance and wraps each of the ten 72 pt tabs onto a line of its own, 3 pages; the fix (tdf#168607, `writerfilter: import PROP_TAB_STOP_DISTANCE 0`, then `as uInt16`, LibreOffice core `5c6b8d25`, `1888a96e`) came with this sample and `testTdf168607_tabstopZero`, which asserts one page. broilerdoc draws one page. | Reference defect, recorded in `tests/office/extras-reference-defects.json` (pixel only). |
| `layout/data/tdf128959.docx` | As issue 238: the cell merged down three exact-height rows is kept whole, where LibreOffice splits the table between those rows. | Not fixed (unchanged, 27.4 %). Needs a merged cell's content to split between the rows it spans. |
| `layout/data/tdf123163-1.docx` | The thirty-one-section magazine of earlier rounds: column and frame drift from page one. | Not changed (15.4 %). |
| `layout/data/fdo43573-2-min.docx` | Two-column text with many pictures; the pages agree to page 7 and then drift by about half a page around the pictures of pages 8 to 13, ending one page longer (16 against 15). | Not fixed (54.2 %); not traced past the first drifting page in this round. |
| `layout/data/tdf170477.docx` | As issues 236 and 238: square-wrapped pictures in table cells and a row holding a floating table. | Not fixed (74.4 %, unchanged). |
| `ooxmlexport/data/n830205.docx` | Tracked changes to list numbering ("[1.]", "2.4[2.5]") that LibreOffice shows as both old and new numbers, plus continuous sections of columns. | Not fixed (11.9 %, unchanged): showing a tracked numbering change is a revisions feature outside this cluster. |

## Breaks that open a paragraph (tdf#153964)

The upstream tests (`ooxmlexport18.cxx`, `testTdf153964_topMarginAfterBreak14`,
`…15`, `…numberingAfterBreak14`, `…firstIndentAfterBreak14`) and the FODT the
local 24.2 writes for each sample give writerfilter's rules:

- text before the break: the paragraph splits, and the part after the break
  has no space above, no first-line indent and no number (broilerdoc already
  did this);
- nothing visible before the break: the paragraph takes the break itself and
  keeps its first-line indent and number; below compatibility mode 15 (or with
  none stated) it also keeps its space above at the top of the page or column
  ("column break2", "How is first line indent affected by a page break?" 60 pt
  down), in mode 15 it does not;
- the document's first paragraph opened by a column break leaves an empty
  paragraph with its properties before the break (page one of
  `firstIndentAfterBreak14` holds only that empty paragraph); a page break there
  already did;
- a paragraph holding nothing but a column break takes that break itself
  (the cream paragraph, 60 pt down in the next column), where one holding
  nothing but a page break passes it to the next paragraph, which loses its
  space above ("page break2", "the w:br was actually at the end of the previous
  w:p");
- a column break followed by a page break in an otherwise empty paragraph is a
  page break that passes on: the last, red paragraph has neither.

`DocxDocumentBuilder` now follows them (`StartParagraphOnNewPage`,
`EnsureBreakApplied`, `FinishParagraph`, `NotePageBreak`).

## Body paragraph borders

`w:pBdr` was read only in headers, footers and frames, and the body layout drew
borders only in text boxes. Cut-down DOCX copies converted with 24.2 (one bordered
paragraph of 12 pt Liberation Serif, 13.8 pt lines, between plain ones; the
borders are `w:sz="24"`, 3 pt, with `w:space="1"` above and below) give the rule:

- d1, d2, d3: the text moves down by the top border's width plus its distance
  (3 + 1 = 4 pt; 0.5 + 1 = 1.5 pt; 3 + 10 = 13 pt), the next paragraph by the
  bottom's; the text does not move across, the border stands outside the
  indents;
- d1, d4: two neighbours with the same border share one box: no room between
  them, and the space between them collapses as anywhere else (23.8 pt pitch
  with 10 pt above and below);
- d5, d8, d9, d10: at the top of a page the first line is 4 pt down, also when
  the paragraph joins one on the page before; shading changes nothing;
- d6, Carlito at 108 %: the same 4 pt;
- d5 with `w:cols w:num="2"`: the first line is 8 pt down. Writer gives the
  section the upper space of its first paragraph, border included, and the
  paragraph adds its own again, which is also why column two of the tdf153964
  samples starts 4 pt below the 60 pt of column one's first paragraph.

`DocumentStyleDefaults.LaysOutBodyParagraphBorders`, set by the DOCX reader,
turns this on; the reader now reads `w:pBdr` everywhere. The body flow adds the
room above the first line and below the last unless the neighbour shares the
border (same border, same left, right and first-line indents), counts it twice
at the top of a page of several columns, and marks the lines with the border;
`DocumentPagePainter.DrawParagraphBorders` draws one box round each run of
joined lines in a column, from the indent (or a hanging number) to the right
indent. ODT paragraph borders stand inside the margins and move the text in,
which the layout does not model, so ODT body borders are still not drawn.
Not done: the room below the last line is not counted when deciding whether a
line fits, and a paragraph split over two pages draws a closed box on each.

Extending the shading over the border's distance, as LibreOffice's boxes look,
lowered the two improved samples by 0.2 points and was dropped.

## Verification

Runner, pixel axis, before and after on the same machine and fonts:

- Breaks: every DOCX sample with `w:br w:type="page"`, `w:type="column"` or
  `w:pageBreakBefore` (148): 109 → 114 passed. Passed now:
  `tdf153964_topMarginAfterBreak14.docx`, `tdf153964_topMarginAfterBreak15.docx`,
  `tdf166544_noTopMargin_fields.docx`, `tdf103975_notPageBreakB.docx`,
  `tdf103975_notPageBreakC.docx`; improved: `tdf153964_firstIndentAfterBreak14.docx`
  (25.9 → 79.0 %), `tdf153964_numberingAfterBreak14.docx` (7.3 → 67.1 %),
  `tdf149089.docx` (45.4 → 47.6 %). Nothing that passed failed and no
  similarity fell. The text axis on the same 148 is unchanged but for
  `firstIndentAfterBreak14`'s new empty first paragraph, which still passes; its
  five failures (`fdo77727.docx` and four tracked ones) dump the same text with
  the changes taken out.
- Borders: every DOCX sample with `w:pBdr` in its document or styles (223):
  175 → 178 passed. Passed now: `custom-styles-TOC-comma.docx` and
  `custom-styles-TOC-semicolon.docx` (lost in issue 238 because their quote
  borders took no room), `tdf153964_topMarginAfterBreak14.docx`; improved:
  `special_styles.docx` (62.1 → 73.7 %), `testOuterShdw.docx` (62.2 → 63.0 %),
  `tdf149089.docx`, the two tdf153964 samples above. No regression.

Tests: `DocxIssue240DocxLayoutTests` (space above after a leading page or column
break by mode, the first paragraph's column break, lone column and page breaks,
a page break after a lone column break, body borders read),
`DocumentLayoutIssue240DocxLayoutTests` (border room, joined neighbours, no room
without the setting, the first line at the page top, the doubled room atop
columns, the drawn box). `DocxColumnTests`' two column-break tests now expect
the empty paragraph writerfilter leaves before a break that opens the document.
Run: `Broiler.Documents.Docx.Tests`, `Broiler.Documents.Pagination.Tests`,
`Broiler.Documents.Model.Tests`, `Broiler.Documents.Tests`,
`Broiler.Documents.Office.Tests`, `Broiler.Documents.Cli.Tests`,
`Broiler.Documents.Pdf.Tests`, `Broiler.Documents.Odt.Tests`,
`Broiler.Documents.Rtf.Tests`, all passing.
