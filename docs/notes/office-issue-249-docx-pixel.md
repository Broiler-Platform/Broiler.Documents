# Office conformance issue #249: DOCX pixel cluster

The 131 DOCX samples of [issue 249](https://github.com/Broiler-Platform/Broiler.Documents/issues/249)
whose page count matches LibreOffice's but whose worst page scores under the
80 % pixel threshold, with no header or picture diagnostic. Measured with
`eng/run-office-conformance.sh --font-set office-fonts-2` (144 dpi, the pinned
fonts) against a local LibreOffice 24.2.7.2, the references' version, from
commit `8a20ca0`. Where the question was what Writer does rather than what a
sample's PDF shows, I converted small DOCX files written for the question with
the same soffice and read the line boxes with `pdftotext -bbox-layout`, and
read the writerfilter and sw sources of the `libreoffice-24.2.7.2` tag.

Similarity is the runner's pixel similarity: the weakest metric of the worst
page.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `docbookexport/data/tdf91095.docx` (8 pages) | Bordered tables: rows took no room for their rules, so every row stood 0.5 pt high (below). | **Fixed:** 25.91 % → pass. |
| `ooxmlexport/data/1-table-1-page.docx`, `fdo80097.docx`, `tdf106690-cell.docx`, `tdf64264.docx`, `uiwriter/data/tdf128375.docx` | The same table row room. | **Fixed:** 69.47, 74.03, 73.82, 72.69, 10.52 % → pass. |
| `ooxmlexport/data/tdf132802.docx` | Table row room, and the automatic space under the last numbered paragraph of a cell was taken off (below). | **Fixed:** 79.32 % → pass. |
| `ooxmlexport/data/fdo80902.docx`, `tdf103651.docx`, `tdf146171.docx`, `tdf149089.docx`, `tdf107359-char-pitch.docx`, `chtoutline.docx` | `w:docGrid` line grids were not read (below); chtoutline also its triple spacing on the grid. | **Fixed:** 69.02, 78.29, 78.19, 41.18, 63.29, 58.07 % → pass. |
| `ooxmlexport/data/tdf131728.docx` (and its `htmlexport`, `uiwriter` copies), `layout/data/hidden-para-separator.docx`, `layout/data/tdf164903.docx` | Body paragraphs whose mark is hidden were not joined with the next (below). | **Fixed:** 57.95 % (×3), 58.71, 75.71 % → pass. |
| `layout/data/merge_hidden_redline.docx` | The same joins; empty hidden-mark paragraphs and deleted text still differ. | Improved: 57.46 → 62.92 %. |
| `ooxmlexport/data/n782345.docx`, `tdf123636_newlinePageBreak3.docx`, `tdf151704_thinColumnHeight.docx` | `w:splitPgBreakAndParaMark` was not applied (below). | **Fixed:** 6.73, 51.45, 77.90 % → pass. |
| `ooxmlexport/data/fdo76586.docx` | A one-column grid under rows of two cells (below). | **Fixed:** 50.95 % → pass. |
| `ooxmlexport/data/tdf137642_Vertical_Alignment_toppage.docx` | VML shapes aligned in the top margin band were drawn at the page's top (below). | **Fixed:** 77.85 % → pass. |
| `ooxmlexport/data/gutter-top.docx` | The gutter was not read (below). | **Fixed:** 39.05 % → pass. |
| `ooxmlimport/data/unbalanced-columns-compat.docx` | `w:noColumnBalance` was not read (below). | **Fixed:** 70.03 % → pass. |
| `ooxmlexport/data/FDO76248.docx`, `TCTagMisMatch.docx`, `floating-tables-anchor.docx`, `tdf167843_tblLook_firstRow_tblHeader.docx`, `all_gaps_word.docx`, `uiwriter/data/tdf104492.docx`, `tdf135623.docx`, `layout/data/tdf153136.docx`, `tdf170846_2.docx`, `ooxmlexport/data/tdf97371.docx` | Table row room; what is left is table styles, header rows, floating tables and text flow. | Improved: 71.60 → 74.19, 73.66 → 76.04, 73.50 → 76.66, 76.57 → 78.17, 78.28 → 79.15, 66.99 → 68.47, 71.06 → 74.77, 61.11 → 62.78, 78.55 → 79.00, 36.87 → 42.08 %. |
| `ooxmlexport/data/fdo73596_RunInStyle.docx`, `tdf134063.docx`, `tdf105035_framePrC.docx`, `tiledrendering/data/comments-on-load.docx`, `ooxmlexport/data/fdo77812.docx`, `layout/data/shape-anchored-in-follow.docx` | Their ink profiles now match (table rows, grid), but the score is held by the blurred-difference metric: glyph shapes, frames and comment margins. | Unchanged (70.40, 70.01, 76.83, 77.21, 75.82, 75.19 %). |
| `odfexport/data/fdo58949.docx`, `2_MathType3.docx`, `ooxmlimport/data/tdf119200.docx`, `ooxmlexport/data/mathtype.docx` | Their only content is `Equation.3` OLE objects. The reference soffice has no Math module (the workflow installs `libreoffice-writer` only), so LibreOffice draws nothing where Word's preview WMF stands, and broilerdoc draws the preview. | **Declined:** matching a missing module is not LibreOffice behaviour. |
| `ooxmlexport/data/plausable-border.docx` | `w:pgBorders w:display="firstPage"`, and LibreOffice 24.2 draws the border on page 2 as well: `SectionPropertyMap::ApplyBorderToPageStyles` ignores its `BorderApply` and sets the border on the section's page style. | **Declined here:** `DocxIssue218Tests.First_Page_Border_Is_Drawn_On_The_First_Page_Only` pins the first-page-only reading (from tdf156560.docx, whose second page is another section and so proves nothing either way); changing it means changing that test. |
| `ooxmlexport/data/tdf112118.docx` | Page border sides of four widths (4.5, 6, 1, 3 pt) drawn as one rectangle at the widest. | **Declined:** the single-stroke shape is pinned by `DocxIssue218Tests`. |
| `ooxmlexport/data/fdo53985.docx` | The empty paragraph holding a continuous `w:sectPr` right after a table is kept; writerfilter removes it (`DomainMapper::lcl_utext`'s `bRemove` does not look at the table before it). | **Declined:** `DocxColumnTests.A_Section_Ending_In_A_Table_Is_Closed_After_It` pins the kept paragraph. Got worse (below). |
| `uiwriter/data/tdf104649.docx`, `ooxmlexport/data/tdf155707.docx` | Persian text drawn without Arabic shaping. | Not fixed: shaping is a separate feature. |
| `ooxmlexport/data/n830205.docx` | Tracked numbering changes shown as both numbers. | Not fixed (revisions feature). |
| `layout/data/tdf127118.docx`, `ooxmlexport/data/fdo76979.docx` | Vertical (`btLr`) text in a cell and in a header frame. | Not fixed. |
| `odfimport/data/tdf76322_columnBreakInHeader.docx` | Columns in a header. | Not fixed. |
| `ooxmlimport/data/tdf103931.docx` | Three columns ended by an empty last section: LibreOffice balances them (only the last section group is left unbalanced), the model drops the empty section, and broilerdoc does not balance at the document's end. | Not fixed (62.08 %). |
| `ooxmlimport/data/text-copy.docx` | Header chosen for page 2 after a continuous section with `w:titlePg`. | Not fixed. |
| `ooxmlexport/data/tdf119800.docx` | A 1.15 pt tall text frame holding a table, anchored to a hidden paragraph; LibreOffice draws none of it. | Not fixed. |
| `ooxmlexport/data/fdo80897.docx` | WordArt warp (`prstTxWarp`). | Not fixed. |
| `ooxmlexport/data/IndexFieldFlagF.docx` | Empty section-mark paragraphs around column sections take a line each; LibreOffice drops them. | Not fixed (as fdo53985). |
| `layout/data/tdf164499.docx` | Page 4 opens 12 pt lower than LibreOffice's after a table-of-contents page break. | Not explained. |

## Table borders take room in their rows

broilerdoc laid a DOCX table's rows out as tall as their text and cell
margins, and drew the borders over them. Writer gives each horizontal rule its
width: soffice 24.2 sets two-column tables of 12 pt Liberation Serif lines
(spacing 0, line 240 auto) with

| Rules | Before → first row | Row to row | Last row → after |
| --- | --- | --- | --- |
| `w:sz="4"` (0.5 pt) everywhere | 14.3 pt | 14.3 pt | 14.3 pt |
| `w:sz="24"` (3 pt) everywhere | 16.8 pt | 16.8 pt | 16.8 pt |
| 3 pt outer rules, no `w:insideH` | 16.8 pt | 13.8 pt | 16.8 pt |

so a rule between two rows takes its width once, and the outer rules above the
first row and below the last. That is the row model of Writer's collapsing
borders (`SwRowFrame::Format`: the row's text starts below
`lcl_GetTopSpace` — the widest top line and top distance of its cells — or the
row above's `lcl_GetBottomLineSize` plus `lcl_GetTopLineDist`, whichever is
lower; the table's bottom margin holds the last row's bottom line).
`DocumentStyleDefaults.CollapsingTableBorders`, set by the DOCX reader, turns
it on in `DocumentLayout.ComposeTable`: each row's text starts at that row-wide
top and ends at the widest bottom distance, and the last row grows by its
bottom line. tdf132802.docx's numbered rows now stand 27.95 pt apart, as in
LibreOffice (27.43 before). The same rows also put every cell's text at the
row's widest top distance, as Writer does: tdf162551's "-anchor point-" (no
top margin) now stands level with "Cell text" (18 pt top margin).

ODT and RTF tables are left as they were: the flag is the DOCX reader's.

This is a deliberate trade-off. The cluster's verifier ran 463 samples and
found 18 failing DOCX samples whose pixel similarity went down. All 18 return
exactly to their earlier figures with the flag off. The worst are fdo53985
(75.4 to 65.6 %, where an extra empty line broilerdoc keeps is now exposed) and
tdf134227 (63.0 to 59.0 %). No passing sample fails. Five fixes depend on the
flag: tdf91095, 1-table-1-page, fdo80097, tdf64264 and tdf134609_gridAfter.
The flag stays on because it is Writer's row model and it wins five passes.

## Automatic spacing under a numbered cell paragraph

`w:afterAutospacing` was taken off the last paragraph of every cell. writerfilter's
`DomainMapper_Impl::ClearPreviousParagraph` does that "except in paragraphs with
numbering" (tdf#132802): a numbered cell paragraph keeps its 14 pt. The list is
now resolved before the automatic spacing, which reads it.

## The DOCX line grid

`w:docGrid w:type="lines"` (and `linesAndChars`, `snapToChars`) was not read.
writerfilter (`SectionPropertyMap::CloseSectionGroup`) makes it a Writer text
grid of `w:linePitch` rows, no ruby, as many lines as the text area holds, and
switches the squared page mode off (`DefaultPageMode`). `DocxSections.ReadTextGrid`
reads it into the model's `TextGrid`, which the ODF reader already fills, with
`SnapsParagraphsToRows = false` for the standard page mode, where
`SwFlowFrame::CalcUpperSpace` does not round a paragraph's space above to a row
(it does only `IsSquaredPageMode`). `w:snapToGrid w:val="0"` sets
`IgnoresTextGrid`. Lines are whole rows tall, centred in the body as for ODF
(fdo80902.docx's 10.5 pt lines stand 18 pt apart, from 116.7 pt, as
LibreOffice's).

Proportional spacing on a standard-mode grid follows `SwTextFormatter::CalcRealHeight`:
a paragraph's first line is not spaced (`IsParaLine`), later ones are their rows
times the spacing, and the next paragraph opens with the last line's text height
times the spacing past single (`SwTextFrame::GetLineSpace`, added in
`CalcUpperSpace`), held below the last line here. chtoutline.docx's triple-spaced
one-line headings stand 36 + 2 × 21.2 pt apart (LibreOffice 78.4 pt; broilerdoc
77.4 pt, its CJK fallback face a point shorter), not 108. The squared-mode grid of
an ODF document keeps every line its rows times the spacing:
`layout/data/tdf144450.fodt` measures 57.5, 75 and 100 pt between all three lines
of its 115 %, 150 % and 200 % paragraphs on a 50 pt grid, which the first-line rule
would break (64.8 % → 55.0 % when it applied there; it does not).

## A body paragraph with a hidden mark runs on into the next

`ComposeRunning` joined a header paragraph whose mark is hidden with the next one
(issue 240); the body only dropped wholly hidden paragraphs, and issue 240 left
joining there out because it renumbers paragraphs. `JoinBodyHiddenMarks` joins
in the body loop without renumbering: the joined paragraph is laid out under the
first one's index and the loop moves past the ones it took. It stops before a
table, a section's start, a paragraph that breaks a page or column, or one an
object is anchored to, and a paragraph with no text shown starts no join (it is
hidden on its own, as before). hidden-para-separator.docx's "C", "D" and "E"
stand on one line, and tdf131728.docx's style separators run each heading on
into its paragraph. LibreOffice also gives the joined text the first
paragraph's paragraph-style character formatting (tdf131728's "shall mean a
piece of fruit" is bold); broilerdoc keeps each run's own.

## `w:splitPgBreakAndParaMark`

With Word 2003's `w:splitPgBreakAndParaMark` (writerfilter's `DomainMapper::lcl_utext`,
tdf#123636) a paragraph that ends in a page break leaves its mark on the new page:
one with text before the break is split there, the mark becoming an empty
paragraph with the same properties and no number that opens the next page; one
holding nothing but the break goes to the new page itself unless it is its
section's first paragraph, which is split too. broilerdoc passed the break on to
the next paragraph, which is right without the setting.
The docx-pagination cluster fixed the same samples with its own
implementation, `DocxDocumentBuilder.SplitPageBreakFromParagraphMark`, which
the reader calls before it finishes a paragraph that does not end a section.
When the clusters were merged, that implementation was kept and this cluster's
copy in `FinishParagraph` was dropped. Every sample either cluster claimed
still passes.

## Smaller causes

- **A grid narrower than every row.** fdo76586.docx states one 9444-twip grid
  column for rows of a 1602 and a 7843-twip cell. writerfilter places a row whose
  cells do not match the grid by the cells' `w:tcW`
  (`DomainMapperTableManager::endOfRowAction`, `shouldInsertRow`). When every row
  has more columns than the grid, the grid is now inferred from the cell widths
  as for a missing grid; the second column had no width and wrapped its text a
  letter a line.
- **VML shapes aligned in a margin band.** `mso-position-vertical` top, center and
  bottom were applied only against the page and the margins;
  `top-margin-area` and `bottom-margin-area` are now the bands above and below
  the margins (tdf137642_Vertical_Alignment_toppage.docx).
- **A picture centred on its line.** `wp:positionV relativeFrom="line"` with
  `wp:align center` was placed at the paragraph's top. LibreOffice centres the
  picture on its line; the line is taken as its run's single-spaced height,
  1.15 times the type size (tdf162551_notLayoutInCell_charLeft_fromTop.docx,
  whose picture had stood at the paragraph's top only by luck before the row
  margins above moved the paragraph down).
- **The gutter.** `w:pgMar w:gutter` was not read. Writer adds it to the left
  margin, to the right one under `w:rtlGutter`, and to the top one with the
  settings' `w:gutterAtTop` (gutter-left, rtl-gutter, gutter-top.docx).
- **`w:noColumnBalance`.** writerfilter leaves every section's columns
  unbalanced under it (`SectionPropertyMap::ApplyColumnProperties`); the DOCX
  sections now say `DontBalanceColumns` (unbalanced-columns-compat.docx).

## Declined or left

- **The Math module.** See the table: four samples are blank in the reference
  only because the reference soffice has no `libreoffice-math`.
- **Page border display and widths.** LibreOffice 24.2 draws a section's page
  border on all its pages whatever `w:display` says, and each side at its own
  width; two issue-218 tests pin the current reading, so I left it.
- **Section-mark paragraphs after a table.** writerfilter removes an empty
  paragraph holding only a `w:sectPr` without regard to a table before it; an
  existing round-trip test pins the kept paragraph.
- **The ODF squared-mode grid.** The first-line rule for proportional spacing
  (above) is applied only to the standard page mode, because
  `tdf144450.fodt`'s measured lines contradict it there; I did not find the
  reason in the sources.
- **Cell table widths with spans.** Inferring the grid from `w:tcW` is limited to
  rows of single cells: on `tdf59274.docx`, whose rows span grid columns, the
  inferred grid put its last column at a few points and scored 72.12 %.

## Verification

Harness (`eng/run-office-conformance.sh --font-set office-fonts-2 --jobs 2`)
on the 131 samples plus every sample whose layout the sweep (`sweep.sh`, all
3,847 Writer samples) saw change, 463 in all. The final numbers are from one run
of the last code for every sample (the run was split in three for time; the
samples a late refinement touched were run again).

- Baseline (commit `8a20ca0`) reproduced: 131 of 131 failing, every similarity as
  in the baseline results.
- 25 of the 131 now pass. Five samples outside the cluster that failed now pass
  too: `ooxmlexport/data/n780843b.docx` (5 pages vs 4 → 4),
  `tdf123636_newlinePageBreak.docx` (1 page vs 2 → 2),
  `tdf134609_gridAfter.docx`, `tdf99090_pgbrkAfterTable.docx` (1 vs 2 → 2) and
  `rtfimport/data/hidden-para-separator.rtf`. `odfexport/data/tdf130314.docx`'s
  pixel check passes (45.42 % before); its text check still fails, as before.
- No sample that passed fails. Two did on the way and were refined:
  `tdf162551_notLayoutInCell_charLeft_fromTop.docx` (the line-centred picture,
  98.6 % on the quick metric) and `tdf97648_relativeWidth.docx` (the section
  mark holding only a break).
- 34 failing samples score higher; no page count moved further from
  LibreOffice's.
- 18 failing samples score lower, all from the table row room, whose row pitch
  now matches LibreOffice's where I measured it (fdo77887.docx's rows: 10.98 pt,
  LibreOffice 11.0, before 10.48): fdo53985.docx 75.40 → 65.64 % (the empty
  section-mark paragraph above, now further off), tdf134227.docx 63.01 → 58.96,
  2129393649.docx 79.36 → 77.19, tdf164500_framePrBeforeTable.docx 74.61 → 72.56,
  tdf136850.docx 74.99 → 73.11, tdf160077_layoutInCellD.docx 57.30 → 55.55,
  fdo48718-1.docx 75.31 → 73.88, fdo77887.docx 75.21 → 73.82,
  tdf59274.docx 75.71 → 75.05, tdf170477.docx 75.14 → 74.48,
  test_segfault_while_save.docx 62.54 → 61.90, tdf134021.docx 63.65 → 62.97,
  tdf153891.docx 72.52 → 72.12, n779642.docx 64.78 → 64.40, n816593.docx
  63.82 → 63.45, tdf118812_tableStyles-comprehensive.docx 61.83 → 61.47,
  tdf97090.docx 58.71 → 58.59, tdf119800.docx 33.73 → 33.63. Their tables'
  rows are where LibreOffice puts them; what moved is other content whose
  position was already wrong (floating tables, header tables that wrap a word
  more, the kept section-mark paragraph).
- Unit tests: `DocxIssue249DocxPixelTests` (Broiler.Documents.Docx.Tests: the
  numbered cell's automatic space, the grid types and pitch, `w:snapToGrid`,
  the table-border flag, VML margin bands, the line-centred picture, the split
  page break and its section-mark and lone-break cases, the gutter three ways,
  `w:noColumnBalance`, the narrow grid and the grid under spans) and
  `DocumentLayoutIssue249DocxPixelTests` (Broiler.Documents.Pagination.Tests:
  row room with and without inside rules and without the flag, the standard
  and squared page modes, proportional spacing on a grid, hidden-mark joins).
  `dotnet test Broiler.Documents.slnx`: 5,818 tests, 0 failed, 3 skipped.
