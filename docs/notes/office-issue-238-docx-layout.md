# Office conformance issue #238: DOCX layout

Ten DOCX samples of [issue 238](https://github.com/Broiler-Platform/Broiler.Documents/issues/238)
about pagination, tables and notes, run locally with `broilerdoc-office
--references-dir … --accept-reference-mismatch --font-dir … --dpi 96` against the
pinned LibreOffice 24.2 PDFs. Where the question was what LibreOffice does rather
than what 24.2 drew, I converted the sample, or a cut-down copy of it, with a
local LibreOffice 26.2.6.3.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `layout/data/tdf164905.docx` | Three causes. The table of contents entries have 6 pt above and below; Writer stands them 6 pt apart, broilerdoc 12 pt (below). Each entry opens with an 11 pt tab run before 9 pt words, and the tab made the line 11 pt tall. The header spreads three placeholders with `w:ptab`, which was not read, so they ran together. 4 pages against 3. | Fixed: the runner passes the sample (23.2 % before), 3 pages. The empty first body paragraph's bottom border is still not drawn: body paragraph borders are not drawn at all. |
| `layout/data/tdf135035.docx` | Three `w:framePr` frames without `w:w` in a three-column section. Writer imports them at its minimum width: a frame of one paragraph fits its text, no wider than its column; one of several paragraphs takes the column. broilerdoc drew all three as wide as the page's text area. | Fixed: the runner passes the sample (13.9 % before). |
| `uiwriter/data/tdf136778.docx` | The footnote is a label paragraph and a 4 x 2 table. `DocumentNote` held paragraphs only, so the cells were drawn as paragraphs stacked down the footnote area (`docx.note.content`). | Fixed: a note keeps its tables (`DocumentNote.Tables`) and the footnote area lays them out as a cell's content is laid out. The runner passes the sample (13.3 % before). |
| `ooxmlexport/data/tdf170003_bottomSpacing.docx` | Page 2 opens a section whose first paragraph has 50 pt above it and a `PAGE \* ArabicDash` field saved as "- 2 -". Writer keeps the space above at the top of a page a section opens, and LibreOffice has no numbering called ArabicDash, so it leaves the field on the page style's numbers: "2". broilerdoc started the page at its top margin, and drew the saved result because a page field in the body kept the result Word saved. | Pixel side fixed: page 2 now draws as LibreOffice's does. The text check still differs: LibreOffice's text export writes the field as "0" and keeps the tracked deletion "page number –" that its PDF does not draw. |
| `layout/data/Cool16066_addRowsUnderOmittedHeading.docx` | A 10 cm repeated heading row over one unsplittable 40-line row. LibreOffice 24.2 (and 26.2.6.3) keeps the row under the heading and runs it past the page's foot, hiding its last lines. cool#16066 (core `f0e78f28`, gerrit 209269, after 26.2) omits the heading on the page the row needs whole, which broilerdoc already does. | Listed under the existing reference defect `cool16066-omit-repeated-heading`, whose commit added this sample with its upstream test. |
| `ooxmlexport/data/fdo78420.docx` | Body paragraphs with space above and below added both, where Writer takes the larger (the collapse below). | Improved: 23 pages against LibreOffice's 23 (26 in CI); similarity 53.5 % to 60.8 %. The bullets LibreOffice leaves off the table of contents lines are still drawn. |
| `layout/data/tdf128959.docx` | A cell merged down three exact-height rows holds a three-line paragraph; LibreOffice breaks the table between those rows and continues the merged cell's text on page 2 (no widow or orphan control in cells). broilerdoc keeps a merged cell's rows together while they fit a page, so the group moves to page 2 and the document takes 3 pages. | Not fixed. Splitting a merged cell's content between the rows it spans is a table layout feature. |
| `uiwriter/data/tdf104649.docx` | A Persian document: every paragraph is `w:bidi` with `w:jc="left"`, which LibreOffice sets right to left and right aligned. broilerdoc reads no paragraph direction. Its footnotes also hold tables, now read (above). | Not fixed. Right-to-left paragraphs are a model and layout feature. |
| `layout/data/tdf170477.docx` | As recorded for issue 236, the text differences are LibreOffice's text export leaving out floating tables. On the pixel side, pictures with square wrap in table cells overlap the cell text that LibreOffice wraps below them, and Table4, whose cell holds a floating Table5, is moved whole to page 4 where LibreOffice splits its row. | Not fixed: wrapping text around a picture inside a cell, and splitting such a row, are layout features. |
| `layout/data/tdf123163-1.docx` | The thirty-one-section magazine of earlier rounds: page count matches and the worst page shows the column and frame drift from page one. | Not changed by this round. |

## The space between paragraphs

writerfilter sets Writer's `AddParaTableSpacing` from
`w:doNotUseHTMLParagraphAutoSpacing` (`DomainMapper_Impl`, settings import), and
without it `SwFlowFrame::CalcUpperSpace` makes the space between two paragraphs
the larger of the first one's space below and the second one's space above,
rather than both. A cut-down copy of `tdf164905.docx` with three paragraphs of
6 pt above and below, converted with 26.2, stands them 17.5 pt apart (11.5 pt
line plus 6 pt); the same copy with `w:doNotUseHTMLParagraphAutoSpacing` stands
them 23.5 pt apart. Nearly every Word document leaves the flag out, but most
paragraphs have space below only, so the difference shows where a style has
both, such as headings after body text or table of contents entries.

`DocumentStyleDefaults.CollapsesParagraphSpacing` says which rule a document
uses; the DOCX reader sets it unless the flag is on. The layout applies it
between body paragraphs and tables, between paragraphs in table cells, frames,
headers and footers, and in footnotes, and in the keep-with-next and column
balancing estimates. ODT and RTF documents keep adding both.

## A tab beside larger text

A line's height comes from the text on it: a 30 pt tab run before, between or
after 9 pt words leaves the line at the words' height in 26.2. The layout now
sizes a line by its pieces other than tabs, where it has any; a line of nothing
but tabs keeps their height.

## Positional tabs

The OOXML tokenizer reads `w:ptab` as a tab character, and
`DomainMapper_Impl::HandlePTab` turns one aligned left after text into a line
break; the tab stops of the paragraph then place what follows. The reader does
the same.

## Frames without a width

writerfilter imports a frame whose `w:framePr` states no width at Writer's
minimum width (`SizeType::MIN`, width 0). Converting variants of
`tdf135035.docx` with 26.2 shows how Writer sizes it: a frame of one empty
paragraph is a sliver, one of one paragraph of text fits the text and wraps at
the column's width, one of two paragraphs, even empty ones, is as wide as the
column. The reader now makes such a frame as wide as its section's column and
marks one of a single paragraph placed by an offset `ShrinksWidthToText`, which
the layout narrows to its widest unwrapped line. A frame aligned centre or right
keeps the column width, because the reader places it by a width only the layout
knows.

## Notes with tables

The note's tables are read over the note's paragraphs, so its text and every
consumer that knows nothing of tables are unchanged, and the footnote block lays
a table out with `ComposeBlocks`, as a cell or a frame is laid out. Footnotes
set at a section's end and endnotes after the text still draw the cells' text as
paragraphs.

## Page breaks that keep the space above

`SwFlowFrame::HasParaSpaceAtPages` keeps a paragraph's space above at the top
of a page that the frame's own break or page style opens (`IsPageBreak`).
`tdf170003_bottomSpacing.docx` (compatibility mode 14) shows it in the 24.2
reference: the paragraph its `nextPage` section opens page 2 with sits 50 pt
down. Converting variants with 26.2: in mode 14 the paragraph a section break or
its own `w:pageBreakBefore` puts at the top of a page keeps its space above; in
mode 15 `w:pageBreakBefore` loses it; after a `w:br w:type="page"` ending the
paragraph before, it is lost in both. `tdf124637_sectionMargin.docx` (mode 15)
draws its headings after `nextPage` sections at the top margin in the 24.2
reference (26.2 has since changed to 12 pt less the closing mark's 8 pt below).

`tdf165047_consolidatedTopMargin.docx` and its `contextualSpacing` twin (mode
15) do keep the space above in 24.2: their section mark sits in a paragraph with
text, which stays, where `tdf124637_sectionMargin.docx`'s marks are empty
paragraphs LibreOffice drops.

The DOCX reader now sets `ParagraphStyle.PageBreakKeepsSpaceAbove` on the page
break it holds for a section below mode 15, and in mode 15 where the section
mark's paragraph stays; on one it reads from `w:pageBreakBefore` below mode 15
only. A run's break never sets it. The layout keeps the space above only where
it is set, and never on the first page; ODT and RTF breaks are as before. A
first version that kept the space for every section start took
`tdf124637_sectionMargin.docx` and `tdf136929_framesOfParagraph.odt` from
passing to 61 % and 0 %; one that dropped it for every mode 15 break lost
both `tdf165047` samples again. Those runs are how the rule was found.

## Automatic spacing

`w:beforeAutospacing` and `w:afterAutospacing` were ignored, so a paragraph kept
its `w:before`/`w:after` (100 twips as Word writes them). writerfilter
(`DomainMapper`, `DomainMapper_Impl::finishParagraph`) makes them 280 twips
where HTML auto spacing is on and 100 where the settings turn it off, and with
it on takes the space above off the first paragraph of the document, of a text
box and of a cell, and the space below off the last paragraph of a cell. The
reader now does the same, and also takes the automatic space off between
two list items, above an item whose paragraph before is numbered and below one
whose paragraph after is (`finishParagraph`, tdf#133052, tdf#133363). It
matters once spacing collapses: two
auto-spaced paragraphs read at 5 pt each stood 10 pt apart while spacing added
up, and 5 pt apart once it collapsed, against LibreOffice's 14 pt.

## Page numbers in the body

`DocxFields.Page` read a `\*` format it did not know, such as `ArabicDash`, as a
field to keep its saved result. LibreOffice's `lcl_ParseNumberingType` maps such
a name to `PAGE_DESCRIPTOR`, the page style's numbers, so it is now a page field
in numerals. A page number field in a body paragraph used to keep Word's saved
result, because only headers and footers replaced page fields; the layout now
replaces it with the number of the page the paragraph starts on.

## Verification

Runner, every sample of the reference archive with a reference PDF (3,873:
1,792 DOCX, 912 ODT, 563 FODT, 546 RTF and the rest), before and after on the
same machine and fonts, all axes. The layout changes (tabs, spacing, pages,
notes) reach every format, so the whole archive ran; the last reader-only
changes were checked on the 1,792 DOCX samples again.

Pixel checks passed: 3,063 → 3,087 of 3,873 (one more skipped, as the new
reference-defect sample). Text and read checks did not change (3,569 and 3,872).
No ODT, FODT, RTF or HTML check changed outcome.

Pixel checks that went from failed to passed (29, one of them to skipped):
`tdf164905.docx`, `tdf135035.docx`, `tdf136778.docx`,
`tdf170003_bottomSpacing.docx` (0 %), `Cool16066_addRowsUnderOmittedHeading.docx`
(skipped), `tdf124423.docx`, `tdf95806.docx`, `tdf159026.docx` (44.8 %),
`fdo71834.docx`, `fdo73596_AlphaSeparator.docx`, `fdo76316.docx`,
`fdo76597.docx`, `mixednumberings.docx` (6 → 5 pages, as LibreOffice),
`tdf102466.docx`, `tdf107111.docx`, `tdf133052.docx`, `tdf133363.docx`,
`tdf143692_outlineLevelTortureTest.docx`, `tdf154369.docx`, `tdf154478.docx`,
`tdf154481.docx`, `tdf159032.docx`, `tdf165047_consolidatedTopMargin.docx`
(18.2 %), `tdf165047_contextualSpacingTopMargin.docx` (20.1 %),
`tdf166553_paraStyleAfterBreak.docx`, `tdf167082.docx`,
`tdf169986_bottomSpacing.docx`, `tdf32363.docx` and `tdf66039.docx`.
Improved and still failing: `tdf81100.docx` (36.8 % → 74.4 %),
`tdf104649.docx` (23.5 % → 56.2 %), `tdf147199.docx` (9.9 % → 36.9 %),
`fdo78420.docx` (53.5 % → 60.8 %, 23 pages as LibreOffice) and
`tdf153964_topMarginAfterBreak14.docx` (21.6 % → 34.1 %).

Four checks went from passed to failed:

- `custom-styles-TOC-comma.docx` and `custom-styles-TOC-semicolon.docx`
  (72.9 %, 71.6 %). Their quote paragraphs have top and bottom borders, and
  body paragraph borders are neither drawn nor given their room. Adding both
  spaces made up for that room; with the spacing collapsed the text below sits
  higher than LibreOffice's.
- `cell-grid-span.docx` (74.5 %). Its HTML-born paragraphs use automatic
  spacing. Before, both 5 pt spaces added up, and the page matched by chance;
  now the paragraphs stand 14 pt apart and the document takes LibreOffice's 2
  pages (1 without the automatic spacing), but the lower part, which
  LibreOffice sets in a fallback face, sits about one line lower.
- `tdf171299_tableInField.docx` (79.45 % against 80 %): its table header row is
  a fraction shorter; the page is otherwise the same.

Failing samples that got worse: `tdf153964_topMarginAfterBreak15.docx`
(48.2 % → 26.8 %, now 5 pages as LibreOffice, so its pages are compared against
different ones; the 60 pt kept above a column-break paragraph, which mode 15
drops, is older), `tdf135144.docx` (60.1 % → 47.1 %, page one holds more than
LibreOffice's before and after), `tdf149089.docx` (53.6 % → 45.4 %) and
`tdf153891.docx` (75.8 % → 70.1 %).

Tests: `DocxIssue238Tests` (spacing setting, positional tabs, frames without a
width, page breaks keeping the space above by mode and section mark, automatic
spacing at the edges and between list items), `DocxNoteTests` (a table in a
note), `DocxPageFieldTests` (a format LibreOffice has no numbering for),
`DocumentLayoutIssue238Tests` (collapsed spacing in the body and in cells,
tabs beside text, a frame narrowing to its text, a footnote table, a kept page
break, a page number in the body). `DocxPageBreakTests.Round_Trips_Through_The_Package`
now expects the break it writes as `w:pageBreakBefore` to read back as one
that keeps the space above.
