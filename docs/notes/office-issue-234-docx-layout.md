# Office conformance issue #234: DOCX layout

Three DOCX samples of [issue 234](https://github.com/Broiler-Platform/Broiler.Documents/issues/234),
rendered locally against the pinned LibreOffice 24.2 PDFs, with the pinned fonts
(`--font-dir`) and the runner's `--sample`, `--accept-reference-mismatch`.
A Windows machine has faces the CI container does not (Noto Sans, Calibri), so
where the CI result depends on a fallback face the comparison below maps that
family to the face CI falls back to (`--font-file "Noto Sans=DejaVuSans.ttf"`).

| Sample | Cause | Outcome |
| --- | --- | --- |
| `ooxmlexport/data/tdf119054.docx` | Keep-with-next honoured inside a table cell | Fixed: page 3 holds the last heading, as in LibreOffice |
| `ooxmlexport/data/tdf123351_UnderlineGroupSapeText.docx` | Shapes stayed on the page their paragraph left | Fixed: the runner passes the sample (was 0% similarity) |
| `layout/data/tdf123163-1.docx` | Cover title one line higher under a wrap polygon | Not fixed; recorded below |

## `ooxmlexport/data/tdf119054.docx`: fixed

The single cell of the table holds eighteen `Heading 2` paragraphs, and the
style keeps each with the next. The cell is longer than a page, so the row
splits from page two onto page three. `ComposeBlocks` fused paragraphs kept with
the next into one cut piece while the piece fitted a page. Here that piece ran
from the first heading to the last heading's text, and the space after that
last heading (18 pt) took the row past the foot of page two. The row found no cut
and was placed whole. With CI's fallback face, `broilerdoc` drew every heading on
page two and left page three empty: the blank-against-inked worst page of the
issue.

Writer ignores the keep attribute of a frame inside a table cell "for
compatibility reasons" (`SwFlowFrame::IsKeep`, `sw/source/core/layout/flowfrm.cxx`:
keep counts only `!IsInTab() || IsTabFrame()`). A table nested in the cell still
keeps, being the table frame. Cell paragraphs of an inline table now no longer
fuse on keep-with-next, so the last heading moves to page three on its own.

Floating tables are left as they were. `tdf170846_2.docx` has a frame of kept
paragraphs that LibreOffice neither splits nor moves, and Writer lays a
floating table out in a fly of its own rather than in the body's flow
(`DocumentLayoutFloatingContinuationTests`).

What is left is the frame. Its bottom border on page two runs to the foot of
the body, where LibreOffice ends it under the last paragraph it kept. The 0.5 pt
border is also drawn heavier.

Tests: `DocumentLayoutParagraphKeepTests.Table_cell_ignores_keep_with_next_as_writer_does`
(formerly asserted the opposite, without a sample behind it) and
`Table_cell_breaks_between_keep_with_next_paragraphs_when_the_last_ones_space_after_does_not_fit`.

## `ooxmlexport/data/tdf123351_UnderlineGroupSapeText.docx`: fixed

Nine group shapes, each holding two text boxes. Eight hang from the first
paragraph, and the ninth ("fifteen none" / "sixteen words") hangs from
paragraph 31, an empty paragraph, at offset 0. With the pinned Carlito, that
paragraph's line does not fit at the foot of page one and moves to page two.
LibreOffice draws the group at the top of page two. `broilerdoc` drew it at the
paragraph's old top on page one, overlapping the row above and running off the
page, and left page two blank.

The paragraph's top was recorded before its first line was measured, and the
move to the next page made the page it left with that top. It never recorded the
new one, so the page it reached had no top for it. When a paragraph moves on
whole, its old top is now dropped before the break and recorded again on the
new page (`DocumentLayout`). This covers both the plain path and the
widow-and-orphan path. It applies only to shapes text ignores (`wrapNone`): a
wrapping frame keeps its own page through the earlier "move a wrapping frame and
its anchor together" step. Moving one with its anchor would push the line on
again, page after page.

The runner now passes the sample. The underline styles (dotted, dashed, wave,
double, thick, `words`) are all still drawn as a single line. The model has a
single underline flag, so carrying the style through the readers and the
painter is a feature of its own, not part of this fix.

Test: `DocumentLayoutMovedAnchorTests.A_shape_moves_with_the_paragraph_that_moved_to_the_next_page`.

## `layout/data/tdf123163-1.docx`: not fixed

Page one already diverges. The cover title "x xxxx" is a 129.5 pt run in a
paragraph with exact 98.4 pt lines and grey shading. It sits under a full-width
picture anchored to the page with `wrapThrough` and a wrap polygon. The polygon
closes the whole width down to the top margin and only a 55 pt strip, left of
the text column, below that. LibreOffice starts the title two lines down (the
shading is four lines, 394 pt). `broilerdoc` starts it one line down (three
lines, 295 pt). Without the picture, `broilerdoc` starts it on the first line.
So the polygon blocks one line in `broilerdoc` and two in Writer, although by
the polygon's geometry the second line only touches it, 0.5 pt above the
picture's foot and outside the text column. Everything after the cover is one
line higher, so content reaches later pages earlier. Page two is where the
large ink difference shows (1545 against 27831 at 96 dpi): its title block
ended on page one.

Which Writer rule blocks the second line is unknown. Candidates are the
DOCX wrap-polygon correction, `WrapPolygon::correctWordWrapPolygon`, and
measuring the line by its 129.5 pt glyph height rather than its exact height.
Matching the result without that rule would be guessing. The keep change above
moved pages 11 and 12 slightly (20688/27441 against 22877/22694 before); the
page count is unchanged at 16.
