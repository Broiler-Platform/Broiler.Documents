# Office issue #240: ODT and ODS pagination and text layout

I reproduced each sample on Linux at 96 dpi with the `broilerdoc-office` runner, the
LibreOffice 24.2.7.2 reference archive (`--accept-reference-mismatch`) and its font
directory. Where I needed to know how Writer behaves beyond the reference, I converted
cut-down copies of a sample with the local LibreOffice 24.2.7.2 (`soffice --convert-to pdf`
or `txt`). Then I read the positions with `pdftotext -bbox`. Similarity is the runner's
pixel score.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `layout/data/tdf167583.fodt`, `layout/data/tdf88752.fodt` | The page layout states a line text grid (`style:layout-grid-mode="line"`: 8 lines, base 1.111 in, ruby 0.139 in). broilerdoc had no text grid. soffice experiments on copies of the sample give Writer 24.2's rules. **(a)** The grid's rows (base + ruby, 90 pt here) fill the body with as many whole rows as fit, at most `layout-grid-lines`, and the rows are centred in the body (`SwBodyFrame::Format`): the first row starts 4.25 pt below the margin. **(b)** A body line is as many rows tall as its text needs (`SwTextFormatter::CalcRealHeight`). Its text is centred in the rows below the ruby: the top of the text box is at (rows − text height + ruby) / 2, which I measured at 22.4 pt for 48 pt text and 43.4 pt for 12 pt. Only proportional spacing above 100% changes that height (200% → two rows). An exact or at-least height is ignored. **(c)** Every line starts on a row. Space above a paragraph (36 pt), or a paragraph with `style:snap-to-layout-grid="false"` before it, moves the next line to the next row. **(d)** In a table cell the rows count from the top of the row, so text below a cell's top padding starts one row down. Each 0.04 in-padded row of the sample is therefore two rows (180 pt) tall, and D and E go to page 2. The two samples differ only by `AdjustTableLineHeightsToGridHeight="false"`, which 24.2 ignores: both references are the same. | **Fixed.** `TextGrid` (Model) on `PageGeometry.TextGrid` is read from `line` and `both` page layouts (`OdtReader.ReadTextGrid`). `ParagraphStyle.IgnoresTextGrid` is read from `style:snap-to-layout-grid`. `DocumentLayout` applies (a) in `BandOf`, (b) in `PlaceLine`, (c) in the body flow and (d) in `ComposeBlocks`. All of it applies to body text only, as `SwTextSizeInfo::SnapToGrid` does: headers, footers, notes and shape text are composed `OffBody`. Both 53.6% → pass. |
| `layout/data/tdf144450.fodt` | The same grid, line mode, 12 lines at 50 pt. Each page holds a 40 pt "Noto Sans" paragraph with different line spacing. As the issue #238 note says, the 24.2 reference sets every line on the grid. | **Improved**, 0% → 69.5%. Lines are now on the grid's rows. What is left is the face: the archive has no Noto Sans, and the fallback wraps four "AAAA" to a line where LibreOffice's fallback wraps three. |
| `layout/data/tdf54465_ColumnsWithFootnoteDoNotOccupyEntirePage_Old.odt` | As recorded in the issue #238 note: the `FootnoteInColumnToPageEnd="false"` compatibility layout of column sections with footnotes. | **Not fixed.** No change, for the reason the #238 note gives. 69.0%. |
| `ww8export/data/tdf127316_autoEscapement2.odt` | As recorded in the issue #238 note: automatic superscript and subscript (`style:text-position`). The model has no raised or lowered text. | **Not fixed.** It is still the open roadmap item (`docs/roadmap.md`, superscript and subscript). 77.6%. |
| `odfexport/data/tdf133507_contextualSpacingSection.odt` | Two causes. (1) The paragraphs set `style:contextual-spacing="true"` (Writer's "Don't add space between paragraphs of the same style"), and the ODT reader did not read it. Each paragraph kept 2 cm above and below. (2) The settings turn off `AddParaTableSpacing` and `AddParaTableSpacingAtStart` (Writer's `PARA_SPACE_MAX` and `PARA_SPACE_MAX_AT_PAGES`). Under them, the opening paragraph has no space above, and the space between two paragraphs is the larger of the two spaces, not their sum. I checked this on a copy of the sample: with `AddParaTableSpacing` set to true, the gap between two paragraphs grows from 2 cm to 4 cm. I then measured contextual spacing on a test document. A paragraph with the setting loses its space below when the next paragraph has the same named style. It loses its space above only when the previous paragraph has that style and the setting too. A section edge stops both. | **Fixed.** `OdtDocumentBuilder.ApplyContextualSpacing` uses the named style of each paragraph: an automatic style is resolved to its parent (`OdtStyles.NamedParagraphStyle`). Paragraphs on either side of a `text:section` edge, styled or not (`NoteSectionEdges`), or in different table cells are not treated as neighbours. The two settings map onto the existing `DocumentStyleDefaults.CollapsesParagraphSpacing` and `PreserveOpeningParagraphSpacing`. Each has an effect only when it is stated and false, because the reader leaves a setting that is not stated at its current value. 47.3% → pass. |
| `uiwriter/data/fdo114306.odt` | As recorded in the issue #236 and #238 notes: rows tied by a cell that spans rows are kept whole. | **Not fixed.** No change. Doing this means splitting a row group in table pagination. 41.3%. |
| `layout/data/tdf107209-vert-rtl.fodt` | As recorded in the issue #236 note: vertical right-to-left pages. | **Not fixed.** Vertical page layout is still a missing feature. 36.7%. |
| `uiwriter/data/tdf130685.odt` | The text check fails on three lines that LibreOffice starts with "• ". The document is a stack of tracked deletions that hold list items (`ShowRedlineChanges=false`), and dozens of `text:change` marks share one list-header paragraph. The text matches, but the list labels of the paragraphs where a deletion joins the text around its mark do not. I tested a smaller case in soffice. A deleted list item that the text after the mark joins keeps its bullet. The first deleted paragraph, which joins the text before the mark, takes the style of the paragraph the mark is in. I changed `ReadDeletion` to that rule, and it fixed these three lines. It broke two others in the same file, though. Where the mark sits in a list header, LibreOffice numbers the first deleted item ("• Past president…"). Where marks follow each other, a one-paragraph deletion that joins the previous deletion's last item is not numbered. | **Not fixed**, and the change was reverted. A rule that matches this sample has to model how Writer re-inserts consecutive deletions at one position (`XMLRedlineImportHelper`) and whether a node is counted in its list. I did not trace either. 71.8%. |
| `odfexport/data/table-in-frame-in-table-in-header-base.odt` | The header is a table, and its second row holds an as-character text frame that contains a paragraph and a table. (1) broilerdoc placed running shapes at the offsets the reader gave them, so the frame was drawn at the top of the page, outside its row, and the row did not grow to hold it. (2) The body is one empty paragraph. LibreOffice's text export then writes the first frame's text: "As char frame." and "TABLE". `LabelledText` already does this for a frame in the body (tdf170516), but it never looked at the header's frames. | **Fixed.** (1) In a running band's table, the line of an as-character frame's anchor grows to the frame's height, and the frame is drawn at that line's top and the cell's text left (`_asCharLines`, `PlaceRunningShapes`). (2) If the body has no frame with text, the text projection falls back to an as-character frame in the default header, then in the footer. It does not fall back to a floating header shape. A first version that also took floating header shapes broke the text check of six watermark samples, for example `watermark.docx`, `watermark.rtf` and `tdf114308.docx`: LibreOffice's export treats a text watermark as a drawing, not a frame. 8.4% → pass, and the text check passes. |
| `mailmerge/data/4_v01.ods` | As recorded in the issue #236 and #238 notes: LibreOffice prints the sheet across 3 pages of columns, with the page style's header and footer. | **Not fixed.** No change. The ODS reader still reads no page or cell styles. 28.3%. |

The printed grid. `style:layout-grid-print` together with `-display` draws the grid in
`style:layout-grid-color` (`SwPageFrame::PaintGrid`). Each row gets a rule above it and a
rule between its ruby and its text, and the grid has a rule below it and one on each side.
In `both` mode, character cells divide each row's text strip. A cell is as wide as the text
row is tall, unless the grid is in standard mode, which states the width. The cells are
centred across the body. `GridLines` adds these rules as page cells under the text. This
fixes `uiwriter/data/tdf106736-grid.odt` (0% before), `tdf107362.odt` (22.8%) and
`tdf149089.odt` (27.7%). broilerdoc only draws the character cells. It does not space East
Asian characters across them.

Word grid metrics. `layout/data/tdf149089.fodt` (2.2% → 39.2%) and
`tdf171275-grid-no-extra-leading.fodt` (58.1% → 59.1%) set `MsWordCompGridMetrics`.
LibreOffice 24.2 centres their grids. Since 25.8, tdf#165089 (`fd7717f8`) starts the grid
at the top of the body, and the `tdf165089-word-grid-centred` reference-defect row records
this. broilerdoc follows the fixed behaviour (`TextGrid.CentredInBody`), so both samples
still fail against 24.2. With centring forced on as a test, both passed. I did not add them
to the defect row, because no LibreOffice 25.8 or later was available to confirm how it
draws them. That confirmation is what adding them would need.

## Effect on other samples

I ran all 1,475 ODT and FODT samples in the archive that have a reference PDF, before and
after, at 96 dpi with every check.

- Pixel checks went from 1,158 to 1,167 passing, and text checks from 1,371 to 1,373. No
  check that passed before fails now.
- Also fixed: `uiwriter/data/tdf139638.odt` (24.8% → pass; contextual spacing and the
  collapsed-spacing settings) and `odfexport/data/tdf77961.odt` (14.0% → pass; grid).
  The full run used the first version of the text fallback, which also fixed the text
  check of `odfexport/data/user-field-decl-fly.odt`. That sample's header frame floats, so
  with the narrowed fallback its text check fails again, as it did before.
- Better, still failing: `ooxmlexport/data/ooo72950-1.odt` 68.4 → 77.3% (spacing settings).
- Worse, still failing: `uiwriter/data/tdf134965.odt` 65.2 → 61.7%. It is a 29-page
  document that uses contextual spacing, at 30 pages against 29 both before and after. Its
  profile similarity rose from 0.808 to 0.926, so the spacing is closer. The score fell
  because of drift on later pages (worst page 23).

The changes to the header frame and the text projection also apply to DOCX. Two feature
runs covered them:
- the 9 DOCX samples whose header or footer holds a table and an inline drawing;
- the text check of all 2,399 samples that are not ODT and have a reference text.

- The header-table DOCX samples: 8 of 9 pass the pixel check before and after.
  `2129393649.docx` is the one that fails, at 78.7% both times. All 9 pass the text check.
- The text check of the 2,399 samples: 2,201 pass and 195 fail, both before and after the
  narrowed fallback. Before it was narrowed, the six watermark samples failed. Six other
  DOCX samples that had been failing, whose only text frame floats in the header (for
  example `tdf120760_ZOrderInHeader.docx`), passed. I rechecked the twelve samples and the
  ODT samples with the final code: each gives the same result as before the change, except
  that `table-in-frame-in-table-in-header-base.odt` now passes.

## Verification

- `dotnet build Broiler.Documents.slnx -c Release`: no errors. There is one warning:
  xUnit2030 in `DocumentLayoutIssue236Tests.cs`, a file this change does not touch. It was
  there before this change, and I left it alone to avoid clashing with other clusters.
- Test projects run, all passing: Broiler.Documents.Odt.Tests (706),
  Pagination.Tests (451), Model.Tests (343), Cli.Tests (357), Docx.Tests (1,029),
  Rtf.Tests (653), Office.Tests (99), Ods.Tests (16) and Broiler.Documents.Tests (329).
- `OdtIssue240OdtLayoutTests` (Broiler.Documents.Odt.Tests), 12 cases: reading a line grid
  and a page without one; a printed character grid with square or stated cells, and its
  colour; Word grid metrics turning off centring; `snap-to-layout-grid`; contextual spacing
  for one style, for each paragraph's own setting, for named styles that differ and across
  a section edge; reading the two paragraph spacing settings, and their defaults.
- `OdtIssue240OdtLayoutLayoutTests` (Broiler.Documents.Cli.Tests), 8 cases, read and laid
  out end to end:
  - body lines on the rows of a centred grid, with the text centred below the ruby;
  - space above moving a paragraph to the next row;
  - a two-row line, and the grid ending the page;
  - a paragraph kept off the grid;
  - a header kept off the grid;
  - printed and unprinted grid rules;
  - a header table's as-character frame standing in its row;
  - the text projection of an empty body with a header frame.
