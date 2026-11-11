# Office issue #236: DOCX pagination and page counts

Seven DOCX samples of [issue 236](https://github.com/Broiler-Platform/Broiler.Documents/issues/236),
run locally with `broilerdoc-office --references-dir … --accept-reference-mismatch
--font-dir … --dpi 96` against the pinned LibreOffice 24.2 PDFs, and rendered
with `broilerdoc render` and the runner's font mappings. Similarity is the
runner's rendering similarity; page counts are broilerdoc / LibreOffice.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `layout/data/tdf150200.docx` | Four drop caps (`w:framePr w:dropCap="drop" w:lines="3"`, a 130 pt dash). The reader prepended the drop paragraph's runs to the paragraph it opens, as an inline run, so each first line was as tall as the drop and the page ran onto a second. | Fixed: drop caps are laid out (below). 2 → 1 page, as LibreOffice; similarity 46.3% → passes. |
| `ooxmlexport/data/tdf123243.docx` | Two text boxes hang from a paragraph about 480 pt down a US Letter page; the left one, grown to its text, runs past the page's foot. A wrapping shape whose box passed the foot of the text area took its paragraph to the next page. LibreOffice keeps both boxes and the paragraph on page one and draws nothing of what passes the page edge. Its twin `tdf115719.docx` (two more objects, one behind the text) has LibreOffice put the boxes on page two, through an import-time imitation of a Word layout bug. | Fixed for DrawingML shapes outside tables, with the Word-bug imitation alongside so the twins keep their pages (below). 2 → 1 page, as LibreOffice; similarity 18.4% → passes. |
| `ooxmlexport/data/tdf170119_bottomSpacing.docx` | LibreOffice 24.2 defect tdf#170119: after a page break, an empty continuous-section mark with 200 pt space below started page two with a 150 pt gap and pushed the last paragraph's second line onto a third page. broilerdoc draws two pages, the last paragraph at the top of page two. | Reference defect, recorded in `tests/office/extras-reference-defects.json` (pixel only). broilerdoc unchanged at 2 pages; the fixed LibreOffice draws 2. |
| `layout/data/tdf170477.docx` | The text differences are the floating tables (`Table3`, `Table5`, `Table9`): LibreOffice lays each out in a fly frame, and its text export leaves fly frames out. Its PDF draws all nine tables. The pixel difference is the floating tables' placement. | Not fixed. The text side is LibreOffice's export, not lost or invented text; the pixel side was out of scope for a focused fix. |
| `ooxmlexport/data/tdf81345.docx` | Page one's header is a five-row, four-column table of merged cells (a photo spanning three rows, the first of exact height; the logo and the title side by side below it). broilerdoc draws the photo, the logo and the title one below the other at the left, as if the cells did not stand side by side, so the header ends about 110 pt lower than LibreOffice's and every page's body starts lower: 7 pages against 5. Removing the header's VML shapes, its `w:vMerge`s or its table indent changes nothing. | Not fixed. Why the header table's cells stack is not established; it is the header path, not the body pagination. |
| `ooxmlexport/data/fdo78420.docx` | 26 against 23 pages in CI; locally 28 against 26 because the run's bold Arial headings fall back to a host face. Body lines are about 15 % further apart than LibreOffice's from page one on (a 15 pt pitch against 13 pt). | Not fixed. The difference is the line pitch of the fallback faces; nothing to fix without CI's faces locally. |
| `layout/data/tdf123163-1.docx` | The thirty-one-section magazine of earlier rounds ([office-issue-234-docx-layout.md](office-issue-234-docx-layout.md)): page count matches, and the worst page is a symptom of the column and frame drift from page one. | Not changed by this round (similarity unchanged). |

## Drop caps

`ParagraphStyle.DropCap` (`DropCap`: lines, character count, distance, and
whether it hangs in the margin) is new in the model. The DOCX reader keeps the
drop paragraph's runs in the paragraph they open, as before, so the text export
still reads "-(dash)Lorem" as LibreOffice's does, and states how many of its
characters are the drop, with `w:lines` and `w:hSpace` from the frame properties
and the style chain.

The layout tokenizes the drop's characters apart from the rest. The lines the
drop spans start past it (its advance plus the distance); a margin drop hangs
left of the text and leaves the lines in place. The drop's pieces join the first
line after it is measured, so no line grows, lowered to the baseline of the last
line it spans. A paragraph with fewer lines than the drop is made as tall as the
lines it spans. The drop is drawn at its runs' own size, which is the size Word
chose and wrote, rather than LibreOffice's computed size, so the drawn drop is
somewhat larger than LibreOffice's for the dash glyphs of `tdf150200.docx`.

A numbered paragraph's number comes first and is sized by the text after the
drop: it took the first run's style, so `tdf109308_dropCapNumbered.docx` had a
57 pt "1.".

The other corpus samples with drop caps all pass the pixel check now:
`tdf150438.docx`, `tdf109308_dropCapNumbered.docx`, `tdf109308_dropCapSdt.docx`
and `tdf109316_dropCaps.docx` (51.2%, 40.2%, 33.6% and 64.4% before).

## Shapes bounded by their page

The layout's "move a wrapping frame and its anchor together when it cannot fit"
rule moved a paragraph to the next page when a shape hanging from it ended below
the text area. Writer's DOCX import makes no drawing follow the text flow outside
a table, so the object's layout environment is the page
(`SwEnvironmentOfAnchoredObject::GetVertEnvironmentLayoutFrame`), and
`tdf123243.docx` shows LibreOffice leaving a box that runs past the page's foot
where it is. `DocumentShape.BoundedByPage` marks such shapes; the DOCX reader sets
it on DrawingML pictures, shapes and grouped pictures outside table cells. For
them the anchor moves only when the shape's top is already past the page's foot.
VML shapes, `w:framePr` frames and ODT and RTF shapes keep the earlier rule.

That change alone moved `tdf115719.docx` and `tdf115719b.docx` (both
`ooxmlexport` and `ooxmlimport` copies) from 2 pages to 1, against LibreOffice's
2: they are `tdf123243.docx` with two wrap-none objects added, one of them behind
the text. Their pages come from somewhere else. Word 2013 and later leave room
above a paragraph that four or more objects hang from, and LibreOffice's import
imitates it (`SectionPropertyMap::HandleIncreasedAnchoredObjectSpacing`, added
for tdf#115719 and narrowed for tdf#123243 and tdf#131446): in compatibility
mode 15 or later, with at least four `wp:anchor` objects, one of them behind the
text, and the wrapping ones with their `distL`/`distR` at least as wide as the
text area, the paragraph's space above becomes at least the objects' heights
together. The DOCX reader now does the same for body paragraphs
(`DocxDocumentBuilder.ApplyIncreasedAnchoredObjectSpacing`).

The layout then needed one more step: a paragraph whose space above alone runs
past the foot of the column now starts the next one before its top is recorded,
when shapes hang from it, so the shapes go with it. Before, the line moved on and
the shapes stood on the page it left.

## Verification

Runner, pixel axis, every DOCX sample with a reference PDF (1,788), before and
after this round on the same machine and fonts: 1,335 → 1,342 pixel checks
passed, one skipped as the new reference defect, and no check that passed
fails. Pixel checks that changed:

| Sample | Before | After |
| --- | --- | --- |
| `layout/data/tdf150200.docx` | 46.3%, 2 / 1 pages | passes, 1 / 1 |
| `layout/data/tdf150438.docx` | 51.2% | passes |
| `ooxmlexport/data/tdf109308_dropCapNumbered.docx` | 40.2% | passes |
| `ooxmlexport/data/tdf109308_dropCapSdt.docx` | 33.6% | passes |
| `ooxmlimport/data/tdf109316_dropCaps.docx` | 64.4% | passes |
| `ooxmlexport/data/tdf123243.docx` | 18.4%, 2 / 1 pages | passes, 1 / 1 |
| `ooxmlimport/data/tdf115719b.docx` | 46.2% | passes, 2 / 2 |
| `ooxmlexport/data/tdf137655.docx` | 32.6%, 3 / 2 pages | 78.8%, 2 / 2 |
| `ooxmlexport/data/tdf170119_bottomSpacing.docx` | 0%, 2 / 3 pages | skipped: reference defect |

`ooxmlexport/data/tdf115719.docx` and `tdf115719b.docx` pass before and after.
`globalfilter/data/skipimages.docx` still passes (blur 10.0% → 16.5%).
An intermediate version that also let top-and-bottom pictures stay put took
`fdo43573-2-min.docx` from 19 pages to 18 against LibreOffice's 19, which is why
they keep the earlier rule.

The issue's seven samples and the five drop-cap and four anchor samples, all
axes: 33 → 40 of 48 checks pass (one now skipped). `tdf170477.docx`'s text check
fails before and after.

Tests: `DocumentLayoutDropCapTests` (new, five), `DocumentLayoutWrappingFrameTests`
(four new), `DocxParagraphFrameTests` (two new, one extended),
`DocxAnchoredObjectSpacingTests` (new, five), `DocxShapeTests` (one new).
Release build; all pass: Pagination 427, Docx 972, Model 343, Documents 320
(3 skipped), Office 95, Odt 666, Rtf 640, Cli 343.
