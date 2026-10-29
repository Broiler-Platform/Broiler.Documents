# Issue #175: a centred floating table above body tables

The first of the [30 ranked differences in issue #175](https://github.com/Broiler-Platform/Broiler.Documents/issues/175)
was `layout/data/floattable-center-shift-down.docx` (severity 1341.36):
`broilerdoc` drew four pages where the reference drew three, the pixel check
found the worst page blank against inked, and 21 text lines differed, all of
them `broilerdoc`'s. The document is a landscape form: a floating table
positioned on the page and centred (`w:tblpPr w:vertAnchor="page"
w:tblpXSpec="center" w:tblpY="3012"`), then two body tables, the second after
a few paragraphs, and a page break before the last, empty paragraph. Its path
and input SHA-256 are in `tests/office/selections/issue-175.txt` and
`tests/office/selections/issue-175.sha256`. No document is vendored.

The sample had been ranked before. The notes for issues
[#143](office-issue-143.md), [#145](office-issue-145.md) and
[#147](office-issue-147.md) already found it "reference-version dependent":
4 against 3 pages with LibreOffice 25.2, 4 against 4 with 26.8. None of them
traced why, and there was no way yet to take a defective reference out of the
ranking. This note does both. It also finds that the text check had a cause of
its own, which `broilerdoc` fixes.

## The reference: LibreOffice 24.2 lays the body table under the floating one

LibreOffice's own test for the sample, `testInlineTableShiftDown` in
`sw/qa/extras/layout/layout6.cxx`, says what it is for: "Without the fix, an
inline table was laid out at its natural position on page 1 and overlapped the
floating table. With the fix, the inline tables are shifted to subsequent
pages." It asserts that page 1 holds the floating table and no body table.

The fix is commit `25ba87fd5fe5fb9c3b4634933982c85ccf0df6aa`, "sw: Layout -
avoid horizontal orientation check shifting down tables" (Collabora,
April 2026; merged as LibreOffice core review
[205157](https://gerrit.libreoffice.org/c/core/+/205157), cited as
gerrit#205157). It names no tdf bug; LibreOffice's Bugzilla has none that
mentions it. `SwTabFrame::CalcFlyOffsets` decides whether a body table that
overlaps a fly moves below it. Since tdf#154775's commit `7b1c03ed` (2023) it
did so only when the fly's horizontal orientation was none, left or right,
with the comment "TODO: why not just ignore HoriOrient?". A floating table
with `tblpXSpec="center"` is imported as a fly with `HoriOrientation::CENTER`,
so the body table stayed where it was and ran underneath. The fix drops the
orientation test. It is on master and `libreoffice-26-8`, not on
`libreoffice-26-2`, so 26.8 is the first release with it.

The CI reference, made by LibreOffice 24.2.7.2 (`office-references-v3`), shows
the defect. Page 1 has the first body table drawn from the top margin and the
floating table drawn over it. Page 2 has the paragraphs and the second body
table, and page 3 is the blank page the page break asks for. LibreOffice
26.2.6.3 on the development host draws the same three pages. The notes for
issues #143 and #145 measured four pages with 26.8.1.1.

`broilerdoc` already laid the document out as the fixed LibreOffice does:
the floating table alone on page 1, the first body table moved to page 2, the
paragraphs and second table on page 3, and the blank page 4. Its floating
table matches the reference's in place, size and cells. Paired by content
rather than by number, `broilerdoc`'s pages 3 and 4 match the reference's
pages 2 and 3 (3.6% and 0% of a blurred, downscaled page differ), so the
extra page is the moved body table. Moving the renderer toward the reference
would mean moving it toward the defect.

The sample is now the second row of `tests/office/extras-reference-defects.json`
(`centered-floating-table-shift-down`, pixel only, fixed in 26.8). While the
references come from an older release, its pixel check is skipped and the
issue lists it under "Known Reference Defects". It still has to be read and
rendered with a documented exit code. The file's `bug` field took only
`tdf#N`. It now also takes `gerrit#N`, the core review that merged a fix that
names no bug, in the schema, the guard and the policy text alike.

## The text check: a floating table is a frame

All 21 text differences were the floating table's cells: `broilerdoc dump
--as text` wrote them first, and LibreOffice's text export has none of them.
LibreOffice imports every table with a position as a text frame holding it
(`DomainMapperTableHandler::endTable` in 24.2 converts any top-level table
with `w:tblpPr`; the RTF tokenizer turns `\tposx`, `\tposy` and the other
positioned-table words into the same `tblpPr` properties, and an ODT one is a
frame in the file). Its text export walks the body's
paragraphs and nothing anchored to them (`SwASCWriter::WriteStream`), which
`dump` already followed for text boxes and frames. Floating tables were the
case it missed.

`dump --as text` now leaves the cells of a top-level floating table out, and
keeps LibreOffice's one exception: a body that is nothing but one empty
paragraph exports its first frame in its place. That is the first floating
table, ahead of any text box. A floating table nested in a cell is still laid out in the cell, so its
text is still written; LibreOffice makes a frame of that one too.

## Verification

The unit suite passes. There are regression tests for the dump
(`DumpTableTextTests`: a floating table's cells left out in both table
projections, a body of one empty paragraph exporting its floating table, and
the table taking precedence over a text box).
`OfficeControlGuardTests` checks the new reference-defect row.

Scored against `office-references-v3` (LibreOffice 24.2.7.2) with
`--accept-reference-mismatch`, `floattable-center-shift-down.docx` now passes
its read and text checks, and its pixel check is skipped as a known reference
defect. It no longer ranks.

The text check of every corpus sample (3,907) was scored with `90903ee` (main)
and with this change:

| Text checks | Before | After |
| --- | ---: | ---: |
| Passed | 3,452 | 3,499 |
| Failed | 411 | 364 |
| Skipped | 44 | 44 |

47 text checks pass that failed, all of them DOCX samples with a floating
table, and none fails that passed. Seven of issue #175's top 30 are among them:
ranks 1, 5, 7, 12, 13, 21 and 22 (`floattable-split.docx`, `tdf97090.docx`,
`FDO76248.docx`, `fdo78883.docx`, `fdo77887.docx` and `fdo48718-1.docx`
besides this one). Read checks are unchanged (3,873 passed, 11 failed). The
renderer is not changed, so no pixel check moves except this sample's, which
is now skipped.

A first version gave a text box precedence over a floating table when the body
was one empty paragraph. That broke four checks: `tdf170516_drawingBeforePlainText.docx`,
`tdf78727.docx`, `tdf116256.docx` and `missing-path.docx`. Each is a floating
table with a text box in a cell, then one empty paragraph. Of the 20 corpus
DOCX samples built that way, LibreOffice writes the table's cells and not the
text box's text whenever it writes anything. The table now comes first, and on
those 20 the text checks are as before (18 pass; `fdo68607.docx` and
`n779642.docx` failed before as well).

The [workflow note for this issue](office-conformance-workflow-improvements-issue-175.md)
records what else the investigation found about the workflow.

## Environment and reproduction

Windows, `office-references-v3` (LibreOffice 24.2.7.2 on Linux, font set
`office-fonts-2`), loaded with `--accept-reference-mismatch`, Poppler 24.04.0
(pdftoppm), 96 DPI, 80% threshold. LibreOffice 26.2.6.3 on the host was used
only to confirm that 26.2 still draws three pages.

```powershell
dotnet test Broiler.Documents.slnx
dotnet run --project src/tests/Broiler.Documents.Office -- `
  --extras-dir <office-references-v3>/inputs --sample-list tests/office/selections/issue-175.txt `
  --references-dir <office-references-v3> --accept-reference-mismatch `
  --font-dir <office-references-v3>/fonts/0 --dpi 96 --allow-pending-tools
```
