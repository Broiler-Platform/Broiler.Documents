# Issue #175: a centred floating table above body tables

First of the [30 ranked differences in issue #175](https://github.com/Broiler-Platform/Broiler.Documents/issues/175):
`layout/data/floattable-center-shift-down.docx` (severity 1341.36), four
pages against the reference's three, the worst page blank against inked, and
21 text lines different, all `broilerdoc`'s. A landscape form: a floating
table positioned on the page and centred (`w:tblpPr w:vertAnchor="page"
w:tblpXSpec="center" w:tblpY="3012"`), two body tables (the second after a few
paragraphs), and a page break before the last, empty paragraph. Path and input
SHA-256: `tests/office/selections/issue-175.txt` and
`tests/office/selections/issue-175.sha256`; not vendored.

The notes for issues [#143](office-issue-143.md), [#145](office-issue-145.md)
and [#147](office-issue-147.md) had found it "reference-version dependent" (4
against 3 pages with LibreOffice 25.2, 4 against 4 with 26.8) without tracing
why. This note traces it, records it as a reference defect, and fixes the
text check's own cause.

## The reference: LibreOffice 24.2 lays the body table under the floating one

`testInlineTableShiftDown` in `sw/qa/extras/layout/layout6.cxx`: "Without the
fix, an inline table was laid out at its natural position on page 1 and
overlapped the floating table. With the fix, the inline tables are shifted to
subsequent pages." It asserts page 1 holds the floating table and no body
table.

The fix is commit `25ba87fd5fe5fb9c3b4634933982c85ccf0df6aa`, "sw: Layout -
avoid horizontal orientation check shifting down tables" (Collabora, April
2026; core review [205157](https://gerrit.libreoffice.org/c/core/+/205157),
cited as gerrit#205157). It names no tdf bug, and Bugzilla has none
mentioning it. `SwTabFrame::CalcFlyOffsets` decides whether a body table
overlapping a fly moves below it; since tdf#154775's commit `7b1c03ed` (2023)
it did so only for a fly oriented none, left or right ("TODO: why not just
ignore HoriOrient?"). `tblpXSpec="center"` imports as `HoriOrientation::CENTER`,
so the body table stayed and ran underneath. The fix drops the orientation
test. It is on master and `libreoffice-26-8`, not `libreoffice-26-2`: 26.8 is
the first release with it.

The reference (`office-references-v3`, LibreOffice 24.2.7.2) shows the
defect: page 1 has the first body table from the top margin with the floating
table over it, page 2 the paragraphs and second body table, page 3 the blank
page the break asks for. LibreOffice 26.2.6.3 draws the same three; issues
#143 and #145 measured four with 26.8.1.1.

`broilerdoc` already drew the fixed layout: the floating table alone on page 1
(matching the reference's in place, size and cells), the first body table on
page 2, paragraphs and second table on page 3, blank page 4. Paired by
content, its pages 3 and 4 match the reference's 2 and 3 (3.6% and 0% of a
blurred, downscaled page differ), so the extra page is the moved body table.
Moving the renderer toward the reference would move it toward the defect.

The sample is the second row of `tests/office/extras-reference-defects.json`
(`centered-floating-table-shift-down`, pixel only, fixed in 26.8): while the
references come from an older release its pixel check is skipped and it is
listed under "Known Reference Defects"; it must still be read and rendered
with a documented exit code. The file's `bug` field, which took only `tdf#N`,
now also takes `gerrit#N` (the core review that merged a fix naming no bug) in
the schema, the guard and the policy text.

## The text check: a floating table is a frame

The 21 text differences were the floating table's cells, which `dump --as
text` wrote first and LibreOffice's text export omits. LibreOffice imports
every positioned table as a text frame holding it
(`DomainMapperTableHandler::endTable` in 24.2 converts any top-level table
with `w:tblpPr`; the RTF tokenizer turns `\tposx`, `\tposy` and the other
positioned-table words into the same `tblpPr` properties; in ODT it is a frame
in the file). Its text export walks the body's paragraphs and nothing
anchored to them (`SwASCWriter::WriteStream`); `dump` already followed that
for text boxes and frames but missed floating tables.

`dump --as text` now leaves a top-level floating table's cells out, keeping
LibreOffice's one exception: a body of only one empty paragraph exports its
first frame in its place, the first floating table ahead of any text box. A
floating table nested in a cell is still laid out in the cell, so its text is
still written, though LibreOffice makes a frame of it too.

A first version preferred a text box over a floating table in that exception,
which broke `tdf170516_drawingBeforePlainText.docx`, `tdf78727.docx`,
`tdf116256.docx` and `missing-path.docx`: each a floating table with a text
box in a cell, then one empty paragraph. Of the 20 corpus DOCX samples built
that way, LibreOffice writes the table's cells, not the text box's text,
whenever it writes anything; on those 20 the text checks are as before (18
pass; `fdo68607.docx` and `n779642.docx` failed before too).

## Verification

Tests: `DumpTableTextTests` (cells left out in both table projections, a lone
empty paragraph exporting its floating table, the table before a text box);
`OfficeControlGuardTests` checks the row.

Against `office-references-v3` with `--accept-reference-mismatch`,
`floattable-center-shift-down.docx` passes read and text, its pixel check is
skipped as a known reference defect, and it no longer ranks. Text checks over
all 3,907 samples, `90903ee` (main) → this change: 3,452 → 3,499 passed, 411 →
364 failed, 44 skipped. All 47 newly passing are DOCX samples with a floating
table, none lost; seven are in issue #175's top 30 (ranks 1, 5, 7, 12, 13, 21,
22: this one, `floattable-split.docx`, `tdf97090.docx`, `FDO76248.docx`,
`fdo78883.docx`, `fdo77887.docx`, `fdo48718-1.docx`). Read checks and the
renderer are unchanged.

The [workflow note for this issue](office-conformance-workflow.md#issue-175)
records the other workflow findings.

## Environment and reproduction

Windows, `office-references-v3` (LibreOffice 24.2.7.2 on Linux, font set
`office-fonts-2`), Poppler 24.04.0 (pdftoppm), 96 DPI, 80% threshold;
LibreOffice 26.2.6.3 on the host only to confirm 26.2 still draws three pages.
Replay: `dotnet run --project src/tests/Broiler.Documents.Office --
--extras-dir <refs>/inputs --references-dir <refs> --accept-reference-mismatch
--font-dir <refs>/fonts/0 --dpi 96 --sample-list
tests/office/selections/issue-175.txt --allow-pending-tools`.
