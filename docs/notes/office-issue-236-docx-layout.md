# Office conformance issue #236: DOCX layout

Seven DOCX samples of [issue 236](https://github.com/Broiler-Platform/Broiler.Documents/issues/236),
rendered locally against the pinned LibreOffice 24.2 PDFs with the pinned fonts
(`--font-dir`) and the runner's `--sample-list`, `--accept-reference-mismatch`.
Where a question was what LibreOffice does rather than what 24.2 drew, I
converted the sample, or a cut-down copy of it, with a local LibreOffice 26.2.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `ooxmlexport/data/FDO76312.docx` | Three causes. The cover text box is placed with `wp14:pctPosVOffset` (35 % down the page), which the reader resolved for the horizontal axis only, so the box sat at the top of the page. The header is a three-column table, and the reader passed a header's paragraphs and shapes to the running content but not its tables, so the cells were stacked down the page. The table is also `w:bidiVisual` (columns right to left), which the model cannot state. | Partly fixed: the vertical position and header tables are read. Similarity went from 0 % to 72.7 %. The mirrored column order is not fixed. |
| `ooxmlimport/data/tdf108714.docx` | Four things. The body has no `w:sectPr`, so no page was stated and the renderer's A4 stood in for the Letter page LibreOffice uses. A `w:br` between blocks that is not a page break, and one directly in a `w:p` outside any run, were dropped; Word and LibreOffice read both as line breaks that open the next paragraph (tdf#108714). The cell starts with four empty lines. A page break between blocks was read as a break before the next paragraph. Word reads it as the first thing in that paragraph's first run, so a following empty paragraph stays on the page the break ends. | Fixed: the runner passes the sample (42.6 % before). All four pages place their text where LibreOffice does. |
| `ooxmlexport/data/dml-shape-fillpattern.docx` | Each caption sits under a centred paragraph that holds only an inline DrawingML rectangle (`wp:inline`, `wps:wsp`). The rectangles were read as floating shapes at the column's left edge with no line of their own, so each caption was drawn beside its rectangle and the document took 2 pages, not 3. | Fixed: the runner passes the sample. An inline shape that is all its paragraph holds now reserves its line, as a saved diagram already did, and is placed by the paragraph's alignment. The pattern fills are drawn lighter than LibreOffice draws them. |
| `ooxmlexport/data/tdf141231_arabicHebrewNumbering.docx` | The 24.2 reference puts the endnotes on a separate page in its Endnote page style. That page is A4 portrait, not the section's A5 landscape, and has no header. LibreOffice 26.2, like Word, sets them right after the body, on the body's page and with its header, as broilerdoc does. tdf#160984 reimplemented continuous endnotes (core `6885dcd7`) and enabled them for DOCX import (core `1ae5ea3f`). Both commits are in `libreoffice-24.8.0.1` and not in 24.2.7.2. | Recorded as a reference defect, `tdf160984-docx-endnotes-on-own-page`, for the pixel check. The row also covers ten other corpus samples whose 24.2 reference has an endnote-only second page that 26.2 does not have (see below). Hebrew and Arabic glyphs are still drawn as missing-glyph boxes, because Carlito has none and the graphics stack falls back per run, not per glyph. The page number does not use the section's `w:pgNumType w:fmt="hebrew1"`. Neither is fixed here. |
| `uiwriter/data/tdf157131.docx` | Page 1's large "ABC" is a picture whose `a:blip` names a PNG and, through the `asvg:svgBlip` extension, an SVG. The SVG has no size, and LibreOffice 24.2 and 26.2 both draw its text stretched over the 585 pt box. broilerdoc draws the PNG, which is Word's own rendering of that SVG: small, staggered "A B C". Footer tables are now read, which brings the footer (a floating table) and the page number in. | Not fixed. The large letters are how LibreOffice draws an SVG without a size, and the PNG broilerdoc draws is the one Word saved. Similarity went from 8.8 % to 18.4 %. Still open: the footer's floating table sits about 30 pt higher than LibreOffice puts it, the page number is drawn in the colour of the field's separator run, not the white of its result, and there is a 7th page. Body paragraphs come out taller with the local fonts. |
| `uiwriter/data/tdf136778.docx` | The footnote holds a 4 x 2 table. `DocumentNote` holds paragraphs only, so the cells are read as plain paragraphs (`docx.note.content`) and the table and its borders are lost. | Not fixed. A table in a note needs the model, both writers and the note layout to carry tables, which is a feature, not a focused fix. |
| `ooxmlexport/data/tdf102466.docx` | The first-page header holds a text box with a two-cell table, and a floating table. Both are now read. With the local fonts the cover table sits about 15 pt lower than in LibreOffice, and justified lines break differently. | Partly improved, from 34.8 % to 49.7 %. The rest is the table-in-text-box placement and font metrics. |

## What changed

- `DocxReader.VerticalOffset` resolves `wp:align` and `wp14:pctPosVOffset`
  through the existing `AlignedOrRelativeV`, in the body against the
  placement page and in a header or footer against the running page. Issue
  #218 added that function and its tests but never called it.
- `DocxReader.ReadPart` returns the part's tables, and `ReadRunningContent`
  passes them to `RunningContent.WithHeader` and `WithFooter`. The model and
  the running layout already carried header tables for ODT. `WithPageBorder`
  passes them on. It rebuilt each header slot without them, which would have
  dropped a header's table on any page with a border.
- A body without `w:sectPr` is read on the defaults of an empty one: a
  612 x 792 pt Letter page with one-inch margins. All 36 such corpus samples
  have a Letter reference with text at 72 pt.
- `DocxDocumentBuilder.HoldLineBreak` and `HoldBlockPageBreak` hold breaks
  written between blocks until the next paragraph starts, the way
  writerfilter's postponed breaks work: line breaks open that paragraph, and a
  page break becomes a break at the head of its first run. A `w:br` directly
  in a paragraph is a line break.
- `DocxDocumentBuilder.HideEmptyParagraphAfterNestedTable` hides the empty
  paragraph that closes a cell right after a nested table, by hiding its mark.
  LibreOffice gives that paragraph no line. A cut-down copy of
  `n779642.docx` with that paragraph replaced by a 1 pt line, and the same
  copy with the paragraph holding text, put "URGENT" at 38.25 pt and 48.35 pt
  in 26.2. The original, with the empty paragraph, puts it at 38.25 pt.
  `fdo78907.docx`'s footer row is one line tall in the 24.2 reference for the
  same reason.
- `DocxReader.ReadShape` reserves the line of an inline `wps:wsp` that is the
  only content of its paragraph, outside table cells, using the builder's
  existing `ReserveDiagramBlock`.

## Verification

A before/after replay with the runner, built from `HEAD` and from this change,
over 151 DOCX samples: my seven, every sample with a table in a header or
footer (38), with `pctPosVOffset` or a vertical `wp:align` (27), without
`w:sectPr` (36), with an empty paragraph after a nested table at the end of a
cell (19), and with an inline `wps:wsp` (29). 44 pixel checks went from
failed to passed, and no text check changed. 32 of them are samples without
`w:sectPr`; the rest include `tdf108714.docx`, `dml-shape-fillpattern.docx`,
`tdf133045_TestShapeAlignmentRelativeFromTopMargin.docx`,
`tdf137641_RelativeFromTopMargin.docx`, `sdt-header.docx` and
`groupshape-relsize.docx`. Also improved: `090716_Studentische_Arbeit_VWS.docx`
(0.8 % to 78.9 %), `2129393649.docx` (29.6 % to 73.2 %) and `tdf81345.docx`
(51.9 % to 78.6 %).

Two checks went from passed to failed, and three failing samples got worse:

- `fdo78910.docx`, 79.5 % against 80 %. Its footer is now the table it is,
  a rule over three cells, instead of three stacked paragraphs. The rule is
  where LibreOffice draws it but 2 px thick at 96 dpi against LibreOffice's 1 px.
- `n779642.docx`, 69.1 %. A floating table aligned to the page bottom holds a
  nested table and the closing empty paragraph, now hidden. The table is
  shorter, so the bottom-aligned table's text moved 11 pt down, and it now sits
  below LibreOffice's. The cut-down copy above shows LibreOffice gives that
  paragraph no room. The old match came from the extra line making up for an
  error in how the floating table is placed, and that error is still open.
- `tdf105688.docx` (49.5 % to 25.6 %). Its header is a floating table with
  `tblpY` -1554 twips, which LibreOffice draws from the top of the page. The
  running layout places header tables in the band's flow, so the band now
  covers the first body lines. Before, the cells' white text was invisible
  paragraphs.
- `tableCurrupt.docx` (78.5 % to 59.1 %). The header is now a table of one
  row. Page 2's text starts at 60 px against LibreOffice's 57 px, where it used
  to start at 66 px. The score dropped because a reference has moved to page 2,
  which comes from the local fonts.
- `tdf149089.docx` (69.3 % to 64.1 %). Its header is a floating table, now
  laid out in the header's flow with rows more compact than LibreOffice's.
  Locally its CJK text is drawn as missing-glyph boxes, and that dominates
  the comparison.

The new reference-defect row was checked sample by sample. For each, page 2
of the 24.2 reference holds exactly the text of `word/endnotes.xml`; local
LibreOffice 26.2.6.3 renders one page, or two for
`tdf141231_arabicHebrewNumbering.docx`, with the endnotes on its body page; and
broilerdoc renders the same page count. `ooxmlimport/data/tdf129659.docx` also
has the endnote page in 24.2, but broilerdoc does not yet match 26.2 there (its
right-to-left text), so it is not listed.

Tests: `DocxIssue236Tests` (header tables, a page border keeping them, the
vertical percentage position, breaks between blocks, an empty paragraph after a
page break between blocks, a body without `w:sectPr`, the paragraph after a
nested table, inline shapes alone or beside text). Two anchor tests that pinned
the old reading of a body without `w:sectPr` (no page, offsets taken as
column-relative) were updated to the Letter page. The Docx (975), Pagination
(417), Model (343), Office (95) and core `Broiler.Documents` test projects pass.
