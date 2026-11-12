# Office issue #242: ODT and ODS pagination and layout

I reproduced each sample on Linux at 96 dpi with the `broilerdoc-office` runner, the
LibreOffice 24.2.7.2 reference archive (`--accept-reference-mismatch`) and its font
directory. Where I needed to know how Writer behaves beyond the reference, I converted
cut-down copies of a sample with the local LibreOffice 24.2.7.2 (`soffice --convert-to pdf`)
and read the positions with `pdftotext -bbox`. Similarity is the runner's pixel score.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `ww8export/data/tdf127316_autoEscapement2.odt` | As recorded in the issue #238 note: automatic superscript and subscript (`style:text-position` `super`/`sub` at 80%, 50% and 20%) were not modelled, so each run was drawn at 40 pt on the baseline, each paragraph wrapped and the document ran to 6 pages. Writer sets such a run at its proportional size and moves it by the full-size font's metrics: an automatic superscript is raised until its ascent meets the full-size ascent, an automatic subscript lowered until its descent meets the full-size descent, and a stated percentage moves the baseline by that part of the full-size font height (`SwFont::CalcEsc`). The line keeps at least the full-size height (`SwSubFont::CalcEscAscent`, `CalcEscHeight`). | **Fixed.** `InlineStyle.Shift` (`BaselineShift`: automatic superscript, automatic subscript or an offset, and a scale) is a new run property. `OdtReader.TryReadTextPosition` reads it, with LibreOffice's defaults (58% for a moved run that states no size; "0% 100%" is no shift), and `OdtWriter` writes it back. `DocumentLayout.MakeTextPiece` sets the run at its scale with a `Rise`, which the raster and PDF painters already draw. 6 → 2 pages, 77.6% → pass. The roadmap item now lists only the other codecs. |
| `uiwriter/data/fdo114306.odt` | As recorded in the issue #236 and #238 notes: a row holding a cell that spans rows was kept whole, so A1 (three rows) and C1 (two rows), each more than a page of text, ran off page 1. A second cause made it worse: each merged cell's missing height was added to the last row it covers, longest merge first, so A1's deficit was counted before C1 had grown row 2 and the rows came to A1 + C1 tall. That left a page of empty boxes, page 3. | **Fixed.** (1) The deficits are now added in the order the merges end (`ComposeTable`), so row 2 grows for C1 and row 3 only for what A1 still lacks. (2) A group of rows tied by merged cells that is taller than a page, and whose rows may all break, is stacked into one `RowBox` (`StackOversizedSpans`, `RowBox.Stack`): each cell's lines and box keep their offset in it. `SplitStackedAt` cuts each cell that crosses the page foot on its own line boundary and continues it at the top of the next page; the cells and boxes below the cut move up by the highest cut, so row 3 follows the rest of C1 as in Writer's page 2. A group that fits a page breaks between its rows instead (`SplitMergedRows`, measured with 24.2 on DOCX and ODT cut-downs; see office-issue-242-docx-layout.md), and a group with a row that may not break, has an exact height or holds a nested table is left as before. 3 → 2 pages, 41.3% → pass. The A column still wraps "A1a" six to a line where LibreOffice sets seven; the line is 0.2 pt too wide here. |
| `uiwriter/data/hiddenSectionsAroundPageBreak.fodt` | The document has no styles at all: no master page, no default style. The opening paragraph names the page style "Landscape", which it does not define. LibreOffice uses its own defaults: the default page style is A4 with 2 cm margins, text is Liberation Serif 12 pt, and "Landscape" is its built-in landscape page style, which the opening paragraph gives the first page. broilerdoc kept the caller's page (1 in margins), the renderer's sans-serif face, and took a built-in Landscape only after the first paragraph. | **Fixed.** A document with no master page is read on Writer's default page (A4, 2 cm margins). A document whose default style and Standard name no face is set in Liberation Serif (`DocumentStyleDefaults.FontFamily`). The built-in Landscape style also applies when the opening paragraph names it. 71.1% → pass. See below for the samples this also fixes. |
| `layout/data/tdf165094.fodt` | Forty-five paragraphs, then a hidden paragraph that names page style Style1 with page number 5, then a table. The 24.2 reference has one page: the page style change is lost. That is tdf#165094: Writer's first layout pass put an automatic break before paragraph 26 and the manual one before the hidden paragraph; moving content back, `SwFrame::WrongPageDesc` skipped the page with the new style and the break was lost. The fix (`67308193f6ee8a21b20043fbd3e831a8c455f40a`, "do not skip empty page because of wrong page descriptor", master 2025-05-26, first in 25.8) came with this sample and `layout5.cxx testTdf165094`; the table text says it "must be after page break, on page with style Style1 and number 5". Cut-down copies in 24.2 show the defect depends on the page style: with a plain `fo:break-before="page"` on the hidden paragraph instead, 24.2 breaks the page. broilerdoc already draws the fixed layout: two pages, the table under the "first (Style1)" header. | **Recorded as a reference defect** (`tdf165094-hidden-paragraph-page-style-break`, pixel check only, fixed in 25.8). Moving toward the reference would move toward the defect. 53.1% → skipped. The text check passes either way. |
| `layout/data/tdf54465_ColumnsWithFootnoteDoNotOccupyEntirePage_Old.odt` | As recorded in the issue #238 note: `FootnoteInColumnToPageEnd="false"`, the layout from before tdf#139336. The reference shows each column section that holds footnotes running to the page foot, unbalanced, with its footnotes at the foot of the page, and what follows on the next page. Szakasz2 is also cut after its second paragraph, before the one that holds a footnote, which I could not explain from the old layout. | **Not fixed.** A rule for the first part alone would give 4 pages against 5, and I did not trace the second. 69.0%. |
| `layout/data/tdf107209-vert-rtl.fodt` | As recorded in the issue #236 note: vertical right-to-left pages. | **Not fixed.** Laying out vertical pages is still a missing feature. 36.7%. |
| `mailmerge/data/4_v01.ods` | As recorded in the issue #236 and #238 notes: LibreOffice prints the sheet across 3 pages of columns at 10 pt, with the page style's header and footer. broilerdoc projects the sheet as one table squeezed onto a page. | **Not fixed.** The ODS reader still reads no page or cell styles, and paging a sheet across columns is a projection feature on top of that. 28.3%. |

Two smaller changes came out of the regression run. Both are Writer built-ins a
document can rely on without defining:

- A `text:h` that names no style, in a document without a `Heading` style, now takes
  Writer's built-in Heading as the parent of its built-in `Heading N`: Liberation Sans
  14 pt, kept with the next paragraph. Heading 1 is then 18.2 pt bold sans, as in
  `IndexElementsInHiddenSections.fodt`. Before the default face was serif, the
  renderer's sans-serif default had hidden this.
- An index (`text:table-of-content` and the like) with no `text:index-body`, never
  generated, is one empty paragraph, as Writer keeps a section with one paragraph. A
  cut-down test in 24.2 shows each such index takes one line.

## Effect on other samples

I ran three targeted sets, before (a copy of the base commit) and after, at 96 dpi with
every check.

- Every ODT, FODT and OTT sample with no master page or no paragraph default style, every
  one that states a `style:text-position` other than "0% 100%", every one with an index,
  and the seven samples above: 387 samples. Pixel checks went from 223 to 297 passing (81
  newly pass, 7 now fail), and text checks stayed at 347. The newly passing samples are
  mostly short documents with no styles, such as `odfexport/data/bookmark_order.fodt`,
  `uiwriter/data/lorem.fodt`, `layout/data/three_sections.fodt` and
  `tiledrendering/data/dummy.fodt`, which LibreOffice draws in Liberation Serif inside
  2 cm margins. No sample that states a `style:text-position` changed its result.
- The 7 that fail now passed narrowly against a wrong face and margins, and each holds
  something broilerdoc does not draw yet: ruby text (`ooxmlexport/data/rubyhyperlink.fodt`,
  `tdf109308_dropCapRuby.fodt`), two-line warichu text (`layout/data/center_fly_warichu_tab.fodt`,
  `rotate_warichu_interrupted.fodt`), Writer's built-in Footnote and Contents 1 styles
  (`layout/data/wideBoxInFootnote.fodt` at 79.9%, `uiwriter/data/tdf147507.fodt`) and a
  zero-width frame (`odfimport/data/tdf120677.fodt`). In each, the text is now in the face
  and place the reference has it; the score fell because the feature the sample is about
  is still missing. `uiwriter/data/FrameInHiddenSection.fodt` and `layout/data/tdf167326.fodt`
  fail before and after, with lower scores for the same reason.
- Every sample with a merged cell over rows (ODT `table:number-rows-spanned`, DOCX
  `w:vMerge`, RTF `\clvmgf`) and the seven samples: 68 samples. Pixel checks went from 41
  to 44 passing, with no check that passed before failing; text checks stayed at 63.
  `layout/data/tdf127118.docx` improved from 50.9% to 54.0%.

## Verification

- `dotnet build -c Release` of the CLI: no warnings or errors. The test build shows the
  existing xUnit2030 warning in `DocumentLayoutIssue236Tests.cs`, a file this change does
  not touch.
- Test projects run, all passing: Broiler.Documents.Odt.Tests (708), Pagination.Tests
  (457), Model.Tests (343), Cli.Tests (349), Docx.Tests (1,029), Rtf.Tests (653),
  Office.Tests (99), Ods.Tests (16), Html.Tests (244) and Broiler.Documents.Tests (329).
- `OdtShapeTests.A_Block_Level_Drawing_Group_Is_Read_With_Children_And_Inherits_Anchor`
  now expects the page-anchored group's x offset from the text area of Writer's default
  page, which the document falls back to.
- `OdtIssue242OdtLayoutTests` (Broiler.Documents.Odt.Tests), 14 cases: `super` and `sub`
  with and without a size, stated offsets, "0% 100%" as no shift and a span undoing its
  paragraph style's shift; Writer's default page and face, and a default style's face
  kept; the built-in Landscape style on the opening paragraph; the built-in Heading; an
  index with and without a body.
- `DocumentLayoutIssue242OdtLayoutTests` (Broiler.Documents.Pagination.Tests), 6 cases: a
  superscript smaller and raised with the line's height kept, its ascent meeting the
  full-size ascent, a subscript lowered, a stated offset; rows tied by a merged cell taller
  than a page broken across it, the merged cell's box continued on the next page; and such
  rows that fit a page broken where the page ends (rewritten from "still moved whole" after
  the 24.2 measurements in office-issue-242-docx-layout.md).
