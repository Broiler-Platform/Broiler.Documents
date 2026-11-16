# Office conformance issue #249: DOCX pagination

The 43 DOCX samples of [issue 249](https://github.com/Broiler-Platform/Broiler.Documents/issues/249)
whose page count differs from LibreOffice 24.2.7.2's, run with
`eng/run-office-conformance.sh --font-dir … --font-set office-fonts-2` on
commit `8a20ca0` and on this branch. Where the question was what Writer does
rather than what the reference shows, I converted the sample, a cut-down copy
or a small DOCX written for the question with the local LibreOffice 24.2.7.2
(the references' version) and read the word boxes with `pdftotext -bbox`.
Those measurements are the evidence quoted below and in the code comments.

15 of the 43 now pass, and 4 samples outside the cluster went from failed to
passed with them. One failing sample of the cluster got worse
(`tdf137335_whitespaceLineHeight.docx`, below); nothing that passed fails.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `ooxmlexport/data/tdf123636_newlinePageBreak.docx` | The only paragraph holds nothing but a page break and the settings set `w:splitPgBreakAndParaMark`. broilerdoc dropped a break in a part's last paragraph (`docx.pagebreak.trailing`). LibreOffice parts the break from the paragraph mark (`DomainMapper`, `m_bIsSplitPara`): two paragraphs, two pages. | Fixed (1 page → 2, as LibreOffice). |
| `ooxmlexport/data/tdf99090_pgbrkAfterTable.docx` | The same flag; the empty paragraph after a table holds only a page break. It moves to the next page itself. | Fixed (1 → 2 pages). |
| `ooxmlexport/data/n780843b.docx`, `tdf153526_commentInNumbering.docx` | Paragraphs ending on a page break under the same flag: LibreOffice opens the next page with an empty paragraph, broilerdoc started the next paragraph at the top. With that, and the proportional spacing below (`tdf153526`), the pages fill as LibreOffice's. | Fixed (5 → 4 and 3 → 2 pages). |
| `ooxmlexport/data/tdf108350_noFontdefaults.docx` | "NoParent" has no `w:basedOn`, so it inherits `w:docDefaults` only, not "Normal". broilerdoc's document defaults include the default paragraph style, and the paragraph took Normal's 32 point Calibri; LibreOffice sets it in 10 point Times New Roman, its fallback for an empty `w:rPrDefault` (11 point Calibri without one, measured on a copy without `w:docDefaults`). | Fixed (2 → 1 page). |
| `ooxmlexport/data/tdf103975_notPageBreakD.docx`, `…E.docx` | The last paragraph of a two-column continuous section ends on a column break; the next section has one column. broilerdoc carried the break into it as a page break. LibreOffice 24.2 imports it as a column break before the next section's first paragraph (the upstream test checks `BreakType_COLUMN_BEFORE`), which its layout ignores outside columns: measured, the next section goes on right below whether it has one column or two; a column break out of a one-column section is still a page break. | Fixed (2 → 1 page each). |
| `ooxmlexport/data/calendar3.docx`, `calendar5.docx`, `tdf90810short.docx` | The space proportional line spacing adds below the last line on a page. Writer needs room only for the text there (below). `calendar3`'s closing empty paragraph is 32 point type set 1.67 and stays below the table on the one page. | Fixed (2 → 1, 2 → 1, 5 → 4 pages). |
| `ooxmlexport/data/tdf137335_whitespaceLineHeight2.docx` | The same rule. | Fixed (4 → 3 pages). |
| `ooxmlexport/data/tdf117988.docx` | 72 point spaces between 12 point words. In a document of Word 2010 or earlier, LibreOffice leaves the line 12 points tall; broilerdoc sized it by the spaces (below). | Fixed (2 → 1 page, 0.1 severity). |
| `ooxmlexport/data/tdf142404_tabSpacing.docx` | The settings hold two `w:compat` elements, LibreOffice's flags in one and `compatibilityMode` 15 in the other. broilerdoc read the first only, took the document for Word 2010 and let its ten tabs run over the margin on one line. With mode 15, a tab whose default stop is past the edge starts a new line (below): five lines of two tabs, as LibreOffice. | Fixed (1 → 2 pages). |
| `ooxmlexport/data/tdf142404_tabOverMarginC15.docx` | Thirteen tabs to a leader stop at the very edge: one line each, then "Was" on the next line. broilerdoc ran the tabs over the margin and set the text after them. | Fixed (1 → 2 pages). |
| `ooxmlexport/data/defaultStyle.docx` | Two paragraph styles are marked `w:default`, "Normal" then "Title"; LibreOffice gives unstyled paragraphs the last (the upstream test checks "Title"). With "Title" (keep with next), the page-opening heading starts a run of kept paragraphs; Writer moves such a run from its first paragraph and leaves it when that one cannot move, so only the last "Title" goes to page 2. broilerdoc moved the two empty headings with it. | Fixed (1 → 2 pages, 794 → 103). |
| `layout/data/tdf171708.docx`, `floattable-footer-object.docx`, `floattable-footer-object-nofit.docx` | A floating table filling three pages and a paragraph after it. LibreOffice 24.2 drops the paragraph: its PDF has no "After the table." The upstream tests (`layout6.cxx`, `testTdf171708`, `testFloatTableOverFooterObject`) expect 4 pages, broilerdoc's count; "Without the fix, the paragraph was dropped". | Not changed: the reference shows a LibreOffice defect fixed after 24.2. It belongs in `extras-reference-defects.json`, but the row needs the fixing commits, which I could not look up from this session (LibreOffice's git web refuses scripted access and the GitHub mirror is not attached). |
| `ooxmlexport/data/tdf165047_contextualSpacingTopMargin.docx` | LibreOffice 24.2 keeps the 100 points above "Don't add space between" at the top of page 2 and needs 3 pages; the upstream test (`ooxmlexport22.cxx`) expects 2 pages and no top margin, broilerdoc's layout. | Not changed, for the same reason (tdf#165047). |
| `ooxmlexport/data/tdf148057_columnBreak.docx` | A column break followed by a line break in a one-column section: the upstream test expects 2 pages ("don't lose the column break"), broilerdoc's count; 24.2 loses it and draws 1. | Not changed (tdf#148057, as above). |
| `ooxmlexport/data/tdf163178_columnbreakInTable.docx` | 24.2 acts on a column break in a cell (issue 185 note), later releases drop it (tdf#163178). | Not changed. |
| `ooxmlexport/data/tdf162746.docx` | LibreOffice lays the body table out about zero wide (issues 240 and 242). | Not changed. |
| `ooxmlexport/data/tdf134609_gridAfter.docx`, `fdo78883.docx` | LibreOffice's DOCX tables give their horizontal cell borders room in the rows. Measured on a table of four one-line rows: a 6 point `insideH` sets the rows 6 points further apart, a 6 point `top` and `bottom` move the first row and what follows the table down 6 points each, and 0.5 point borders add 0.5 per edge. broilerdoc draws the borders over the rows' edges, so its tables are shorter and the paragraph after these tables still fits on page 1. | Declined for now. Giving each row its widest top border (and the last row its bottom border) fixed these and 7 more samples and improved 175 of the 333 samples the layout sweep showed changed, but 34 got worse, among them `uiwriter/data/tdf134021.docx`, whose 318 rows of 0.5 point borders ran 200 points longer than LibreOffice's, where LibreOffice adds almost nothing. The rule is not complete; I left it out. |
| `uiwriter/data/tdf134227.docx` | A table placed beside a floating table (`tblOverlap never`): LibreOffice wraps the second table beside the first, broilerdoc puts it below. | Not fixed: wrapping a table around a floating table is a feature of its own. |
| `ooxmlexport/data/tdf99140.docx`, `uiwriter/data/floattable-split.docx` | Floating tables that LibreOffice 24.2 splits across pages (a page-anchored one starting at the foot of page 1). | Not fixed: splitting floating tables. |
| `uiwriter/data/tdf147199.docx` | The flow diagram canvas and its shapes (issue 238). | Not fixed. |
| `odfexport/data/tdf163575.docx` | Persian text drawn unshaped. | Not fixed: text shaping. |
| `ooxmlexport/data/tdf137335_whitespaceLineHeight.docx` | Lines of nothing but tabs and 72 point spaces: LibreOffice 24.2 sizes them by the spaces (fixed differently later, tdf#137335) and needs 2 pages. Before this round broilerdoc lost the leader tabs past the margin; now it draws them, but on page 1, as it does not size such lines. | Worse: 251.4 → 462.9, still 1 page against 2. |
| `ooxmlexport/data/tdf118947_tableStyle.docx` | A table style's paragraph properties against a paragraph style based on a style that does not exist. | Not fixed: precedence not worked out. |
| `test_segfault_while_save.docx`, `tdf123054.docx`, `tdf119952_negativeMargins.docx`, `tdf107889.docx`, `tdf135343_columnSectionBreak_c14v2.docx`, `tdf164500_framePrBeforeTable.docx`, `FDO78292.docx`, `tight-wrap-fromdoc-C12.docx`, `tdf162541_notLayoutInCell_paraLeft.docx`, `n652364.docx`, `tdf139336_ColumnsWithFootnoteDoNotOccupyEntirePage.docx`, `tdf149649.docx` | Looked at side by side only: drift through long tables, anchored and wrapped pictures, sections of columns with notes, a header taller in LibreOffice. | Not investigated further. |

## A page break ends a paragraph under `w:splitPgBreakAndParaMark`

Measured with small DOCX files (one-line paragraphs, the flag set or not):

- "A", then a paragraph holding only a page break: 2 pages with the flag, 1
  without; the same for "B" + break as the last paragraph.
- "B" + break, then "A": with the flag, page 2 holds an empty line and then
  "A" (the text export lists the empty paragraph).
- The split paragraph keeps the first part's mark (36 point mark, a 36 point
  empty line) and space below, not its space above; an empty paragraph moved
  by its own break keeps its space above below mode 15.

`DocxDocumentBuilder.SplitPageBreakFromParagraphMark` does that for a body
paragraph ending on a page break. Without the flag nothing changes, and the
trailing break is still dropped (`docx.pagebreak.trailing`).

The first version left a paragraph that ends a section alone. The review of
the merged clusters found that the docx-pixel cluster's own implementation
split it, and soffice 24.2.7.2 agrees. With the flag, a section ending "x" and
a page break, then "next", sets "next" one empty line down page 2, in a
continuous section and in a next-page section alike, on two pages. A column
break out of a one-column section, which is a page break there, is split the
same way. Only a section's mark holding nothing but the break is passed on
unsplit. writerfilter does not make that mark a paragraph.

## The last line on a page

Writer needs room only for the text of the last line on a page or in a column;
the space proportional line spacing adds below it may run past the bottom, as
the space below a paragraph may. Measured: a page 197.65 points tall holds
three lines set double in 32 point type (78.1 points apart, the last text
ending at 245 points), as three paragraphs, as three lines of one paragraph, and
in two columns; `fdo43573-2-min.docx`'s first column ends on a line whose
spacing runs 1.75 points past the margin. `fdo71834.docx` shows the other side:
a line whose text would end 0.12 points past the margin moves on. The rule is
`DocumentStyleDefaults.ProportionalSpacingRunsPastBottom`, set for DOCX only:
an ODT LibreOffice wrote earlier can carry the gap above the text of a
paragraph's later lines (`tdf172233_linespacingGap.odt`), where it has to fit.

This rule is broader than Writer's. The cluster's verifier checked it against
LibreOffice 24.2.7.2 with `tdf165047_consolidatedTopMargin.docx`: Liberation
Sans at 24 points, set 1.15, body ending at 225 points. LibreOffice moves the
paragraph's third line (text 196.2 to 223.0 points) to the next page, though
its text fits. In a copy cut so that this line is the paragraph's last,
LibreOffice keeps it. So the spacing may run past the bottom at least for a
paragraph's last line, but not always for a line within the paragraph.
Limiting the rule to last lines restores that sample (severity 659.9 back to
106.9) and `comments-on-load.docx`. It also breaks `tdf90810short.docx` (5 pages
against 4) and `tdf142404_tabSpacing.docx`, and `n780843b.docx` gets worse. So
the real rule lies between the two. The broader rule is kept because it gains
more, and refining it is open.

It moved `layout/data/tdf109137.docx` from passing to failing at first: a line
there fits 0.6 points within the margin, because broilerdoc set the figure's
line 7 points shorter than LibreOffice. A line of nothing but pictures set 1.5
in 12 point type is 6.9 points taller in LibreOffice 24.2 than set single;
broilerdoc gave it no spacing. It now takes the spacing past single of the
paragraph's type, outside text boxes (in `test_msword_hang.docx`'s frame,
already overfilled by its picture, the extra lost the frame's text), and
`tdf109137.docx` passes at 0.7.

## Spaces and line height

In `tdf117988.docx` (no `compatibilityMode`) LibreOffice ignores the 72 point
spaces between 12 point words, and only the "Proof line", whose 72 point run
starts with an "h", is tall. In `tdf137335_whitespaceLineHeight2.docx` (mode
15) 24.2 still sizes such lines by their spaces. So the DOCX reader sets
`IgnoresBlanksInLineHeight` below mode 15 only, and the layout ignores a run of
spaces where the line holds other text: beside nothing but a picture the
spaces still count (`tdf165359_SdtWithInline.docx`).

## Verification

- Harness on the 43 samples: 0 passed before, 15 after (listed in the table as
  fixed). Page counts: 15 of 43 matched LibreOffice's after, none before.
- Harness on the 117 samples that the layout sweep, a base-against-branch
  render comparison of all 1,805 DOCX samples with the harness fonts, or the
  cluster named: no passing sample fails; 19 went from failed to passed (the
  15, `n782345.docx`, `tdf123636_newlinePageBreak3.docx`,
  `tdf142404_tabOverSpacingC15.odt` and `tdf151704_thinColumnHeight.docx`);
  14 others scored better. Worse, still failing:
  `fdo43573-2-min.docx` (784.8 → 828.1, 15 pages as LibreOffice: its pages
  drift from page 7 on, around pictures, and the column ends now follow the
  rule above), `FDO77715.docx` (344.6 → 352.6), `fdo78883.docx` (742.9 →
  749.0), `comments-on-load.docx` (336.0 → 342.7) and
  `tdf137335_whitespaceLineHeight.docx` (above). Worse but passing:
  `tdf165047_consolidatedTopMargin.docx` (106.9 → 659.9: its four-line
  paragraph now leaves one line on page 3 where LibreOffice keeps two lines
  together), `fdo71834.docx` (217.0 → 346.0), `tdf75573_page1frame.docx`,
  `imgshadow.docx`, `fdo61343.docx` (each a few points).
- Layout sweep over all 3,847 `.odt`/`.fodt`/`.docx`/`.rtf` samples: 79
  changed against the baseline, all in the list above.
- Full solution test run: 5,806 passed, 0 failed.
- Unit tests: `DocxIssue249PaginationTests` (Docx.Tests) and
  `DocumentLayoutIssue249PaginationTests` (Pagination.Tests). Run against
  `8a20ca0`, the behaviour tests that compile there fail (6 of 8 and 4 of 6;
  the others are controls), and the tests on the new settings do not compile.
  `DocxReaderStyleTests.Does_Not_Apply_The_Default_Paragraph_Style_To_A_Paragraph_That_Names_One`
  asserted the run states no size, which resolved to the default style's 10
  points it says must not apply; it now checks LibreOffice's 11 point
  Calibri.
