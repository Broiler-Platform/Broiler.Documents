# Issue #173: a Word 2010 table of amounts

First of the [30 ranked differences in issue #173](https://github.com/Broiler-Platform/Broiler.Documents/issues/173):
`ooxmlexport/data/tdf77417.docx` (severity 1359.96), three pages against the
reference's one and 21 text lines different. A financial statement of three
tables of dollar amounts with footnotes, saved by Word 2010
(`compatibilityMode` 14). Path and input SHA-256:
`tests/office/selections/issue-173.txt` and
`tests/office/selections/issue-173.sha256`; not vendored.

LibreOffice's test, `testTdf77417` in
`sw/qa/extras/ooxmlexport/ooxmlexport26.cxx`: "MSO 2010 compatibility mode:
terminating white spaces are ignored in tables. This was 3 pages with the
first invisible blank page." `broilerdoc` had all three parts; the text check
had a fourth cause.

## White space ending a cell's paragraph

Each amount cell is `$\t00,000\t` followed by `U+00A0` three times. With a
stated `compatibilityMode` from 11 to 14, LibreOffice's DOCX import removes
the spaces, tabs and no-break spaces ending a paragraph in a table cell
(`DomainMapper_Impl::finishParagraph`, tdf#77417; same in 24.2 and master).
Left in, the trailing tab sought a stop past the cell and wrapped, so every
row was two or three lines high.

The DOCX reader now does the same: `DocxDocumentBuilder.TrimsCellParagraphEnds`
is set from the mode, carried into the header, note and text-box builders, and
reported once as info `docx.table.trailingspace`. Mode 15, or none, keeps the
white space, as LibreOffice does.

## Tab stops past the right indent

The cells' paragraphs state a right stop at 1053 twips and a decimal stop at
1116, but their right indent ends the line at 990 twips. LibreOffice reads a
DOCX of mode 14 or less, or none, with `TabOverMargin`
(`SettingsTable::ApplyProperties`): a stated stop keeps its position and
alignment past the right indent (`SwTabPortion::PostFormat`), the text after
it runs on rather than wrapping (`SwTextFormatInfo::GetLineWidth`, up to
55.8 cm), and the line is not cut at the cell's edge
(`SwTextPainter::DrawTextLine`); the reference draws the last column's
amounts at 577.37 pt, 1.37 pt past the cell.

This is now `DocumentStyleDefaults.TabsOverMargin`, set by the DOCX reader for
mode 14 or less and for none; the ODT reader and writer keep LibreOffice's
`TabOverMargin` configuration item, as they keep `JustifyLinesWithShrinking`.
`LineWrapper` leaves such a stop where it stands and lets the row run on, and
a cell line that reached one is not cut. A default stop still wraps, as in
LibreOffice. The PDF writer's layout does not apply it.

## The blank first page

The one section is `w:type="evenPage"` and numbers from 2
(`w:pgNumType w:start="2"`). Word and LibreOffice decide parity by page
number, so page 2 is already even; `broilerdoc` decided by position and
inserted a blank page. The DOCX reader now reads `w:start` into
`DocumentSection.PageNumberStart` (the ODT reader already did, from
`style:page-number`), the writer writes it back, and the layout checks parity
against the restarted number.

## The text check

The 21 text differences were line breaks only. LibreOffice 24.2, the CI
reference, writes each cell's paragraphs on lines of their own; since 22
October 2026 `broilerdoc dump --as text` had joined a row onto one line with
tabs, as LibreOffice 24.8 and later do (tdf#144576), fitted to a newer
developer LibreOffice. Every sample with a table failed the nightly text check.

`dump --as text --cells lines` now writes a cell per line, and the runner
passes it when the reference LibreOffice is older than 24.8
(`TextProjection.DumpArguments`). The
[workflow note for this issue](office-conformance-workflow.md#issue-173) has
the corpus-wide effect and the other workflow findings.

## Verification

Tests: `DocxWord2010CompatibilityTests`, `DocumentLayoutTabStopTests`,
`DocumentLayoutSectionTests` (parity by page number),
`OdtJustificationSettingTests`, `DumpTableTextTests`, `CommandTests`,
`ReferenceDefectTests`.

Against `office-references-v3` (LibreOffice 24.2.7.2) with
`--accept-reference-mismatch`, `tdf77417.docx` passes read, pixel and text.
Every corpus sample was rendered with `db90c69` (main) and with this change:
15 renders changed (10 DOCX, 5 ODT), no exit code. On those 15: pixel passes
8 → 9 (`tdf77417.docx`), text 11 → 15, summed severity 7,249 → 4,813; none
lost. `layout/data/tab-over-margin.odt` (LibreOffice's test of the setting)
and the three `tdf120287*.fodt` samples changed and still pass. The only rise
is 1.5 in `tdf90697_complexBreaksHeaders.docx` (failing before and after): a
word that wrapped after a tab now stays on the line, as in the reference.

Still different in `tdf77417.docx`, both on the [roadmap](../roadmap.md): the
footnote marks "(a)" and "(b)" are superscript but drawn full size, as
`w:vertAlign` is not modelled for ordinary runs; and in one row LibreOffice
lets "$ 000,000" overrun its right stop by 2.4 pt where `broilerdoc` wraps.

## Environment and reproduction

Windows, `office-references-v3` (LibreOffice 24.2.7.2 on Linux, font set
`office-fonts-2`), Poppler 24.04.0 (pdftoppm), 96 DPI, 80% threshold. Replay:
`dotnet run --project src/tests/Broiler.Documents.Office -- --extras-dir
<refs>/inputs --references-dir <refs> --accept-reference-mismatch --font-dir
<refs>/fonts/0 --dpi 96 --sample-list tests/office/selections/issue-173.txt
--allow-pending-tools`.
