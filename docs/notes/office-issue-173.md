# Issue #173: a Word 2010 table of amounts

The first of the [30 ranked differences in issue #173](https://github.com/Broiler-Platform/Broiler.Documents/issues/173)
was `ooxmlexport/data/tdf77417.docx` (severity 1359.96): `broilerdoc` drew
three pages where the reference drew one, and 21 text lines differed. The
document is a financial statement: three tables of dollar amounts and their
footnotes, saved by Word 2010 (`compatibilityMode` 14). Its path and input
SHA-256 are in `tests/office/selections/issue-173.txt` and
`tests/office/selections/issue-173.sha256`. No document is vendored.

LibreOffice's own test for it, `testTdf77417` in
`sw/qa/extras/ooxmlexport/ooxmlexport26.cxx`, says what it is for: "MSO 2010
compatibility mode: terminating white spaces are ignored in tables. This was 3
pages with the first invisible blank page." `broilerdoc` had all three parts of
that, and the text check had a fourth cause of its own.

## White space ending a cell's paragraph

Each amount cell is "$", a tab, the amount, then a tab and some no-break
spaces: `$\t00,000\t` followed by `U+00A0` three times. With a stated
`compatibilityMode` from 11 to 14, LibreOffice's DOCX import removes the
spaces, tabs and no-break spaces that end a paragraph in a table cell
(`DomainMapper_Impl::finishParagraph`, tdf#77417; the same in 24.2 and on
master). Left in, the trailing tab looks for a stop past the cell and wraps
onto a line of its own, so every row of every table was two or three lines
high.

The DOCX reader now does the same: `DocxDocumentBuilder.TrimsCellParagraphEnds`
is set from the compatibility mode, carried into the builders for headers,
notes and text boxes, and reported once with the info diagnostic
`docx.table.trailingspace`. A mode of 15, or none, keeps the white space, as
LibreOffice does.

## Tab stops past the right indent

The cells' paragraphs state a right stop at 1053 twips and a decimal stop at
1116, but their right indent ends the line 990 twips from the cell's text edge. A DOCX
of mode 14 or less, or of none, is read by LibreOffice with `TabOverMargin`
(`SettingsTable::ApplyProperties`). A stop the paragraph states then keeps its
own position and alignment past the right indent (`SwTabPortion::PostFormat`),
and the text after it runs on rather than wrapping
(`SwTextFormatInfo::GetLineWidth`, up to 55.8 cm). Such a line isn't cut at
the cell's edge either (`SwTextPainter::DrawTextLine`): the reference draws the
last column's amounts at 577.37 pt, 1.37 pt past the cell.

This is now `DocumentStyleDefaults.TabsOverMargin`. The DOCX reader sets it
for mode 14 or less and for no mode. The ODT reader and writer keep
LibreOffice's `TabOverMargin` configuration item, as they keep
`JustifyLinesWithShrinking`. In layout, `LineWrapper` leaves such a stop
where it stands and lets the rest of the row run on, and a cell's line that
reached one is not cut at the cell's edge. A default stop still wraps, as in
LibreOffice. The PDF writer's layout doesn't apply it.

## The blank first page

The document's one section is `w:type="evenPage"` and numbers its pages from
2 (`w:pgNumType w:start="2"`). Word and LibreOffice decide odd and even by the
page's number, so the first page, page 2, is already even. `broilerdoc`
decided by the page's place and put a blank page first. The DOCX reader now
reads `w:start` into `DocumentSection.PageNumberStart` (the ODT reader already
did, from `style:page-number`) and the writer writes it back. The layout
checks parity against the page number the section's restart gives.

## The text check

The 21 text differences were not about content. LibreOffice 24.2, the CI
reference, writes each table cell's paragraphs on lines of their own.
`broilerdoc dump --as text` has joined a table row onto one line with tabs
since 22 October 2026, as LibreOffice 24.8 and later do (tdf#144576). That
change was fitted to the newer LibreOffice on a developer's machine, so every
sample with a table failed the nightly text check on line breaks alone.

`dump --as text --cells lines` now writes a cell per line, and the runner passes
it when the reference LibreOffice is older than 24.8
(`TextProjection.DumpArguments`). The [workflow note for this
issue](office-conformance-workflow-improvements-issue-173.md) has the
corpus-wide effect and what else the investigation found about the workflow.

## Verification

The unit suite passes. There are regression tests for the reader
(`DocxWord2010CompatibilityTests`: trimming by mode, only in cells, the flag by
mode, `w:pgNumType` read and written), the layout (`DocumentLayoutTabStopTests`:
stops past the indent with and without the flag, a default stop, an uncut cell
line; `DocumentLayoutSectionTests`: parity by page number), the ODT setting
(`OdtJustificationSettingTests`), the dump (`DumpTableTextTests`,
`CommandTests`) and the runner's choice of projection (`ReferenceDefectTests`).

Scored against `office-references-v3` (LibreOffice 24.2.7.2) with
`--accept-reference-mismatch`, `tdf77417.docx` now passes all three checks
(read, pixel, text).

Every corpus sample (3,896) was rendered with `db90c69` (main) and with this
change, using the same fonts. 15 renders changed (10 DOCX, 5 ODT), none in
exit code. Both builds were scored on those 15:

| Axis | Before | After |
| --- | ---: | ---: |
| Pixel checks passed (of 15) | 8 | 9 |
| Text checks passed (of 15) | 11 | 15 |
| Summed severity | 7,249 | 4,813 |

The pixel check that now passes is `tdf77417.docx`. No check that passed
fails. `layout/data/tab-over-margin.odt`, LibreOffice's own test of the
setting, and the three `tdf120287*.fodt` samples changed and still pass. The
only rise in severity is 1.5, in `tdf90697_complexBreaksHeaders.docx`, whose
pixel check failed before and after: a word that used to wrap after a tab now
stays on the line, as it does in the reference, and the page's other
differences weigh slightly more.

What still differs in `tdf77417.docx`, within the passing pixel check: the
footnote marks "(a)" and "(b)" are superscript in the document and drawn at full
size on the baseline, because `w:vertAlign` isn't modelled for ordinary runs;
and in one row LibreOffice lets "$ 000,000" overrun its right stop by 2.4 pt
where `broilerdoc` wraps it. Both are in the [roadmap](../roadmap.md).

## Environment and reproduction

Windows, `office-references-v3` (LibreOffice 24.2.7.2 on Linux, font set
`office-fonts-2`), loaded with `--accept-reference-mismatch`, Poppler 24.04.0
(pdftoppm), 96 DPI, 80% threshold.

```powershell
dotnet test Broiler.Documents.slnx
dotnet run --project src/tests/Broiler.Documents.Office -- `
  --extras-dir <office-references-v3>/inputs --sample-list tests/office/selections/issue-173.txt `
  --references-dir <office-references-v3> --accept-reference-mismatch `
  --font-dir <office-references-v3>/fonts/0 --dpi 96 --allow-pending-tools
```
