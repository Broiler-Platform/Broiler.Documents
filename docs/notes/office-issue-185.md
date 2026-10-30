# Issue #185: a glossary whose definitions broke at every line

The first of the [30 ranked differences in issue #185](https://github.com/Broiler-Platform/Broiler.Documents/issues/185)
was `ooxmlexport/data/fdo68607.docx` (severity 1112.88): an eight-page drama
glossary, one floating table of 137 rows under a title, eight pages in both
renderers, a blur difference of 32.4% on the worst page and 21 lines of text
difference, each a LibreOffice definition that `broilerdoc` had broken into
several lines ("… describe the" / "feeling of release …" against "… describe
thefeeling of release …").

Its path and input SHA-256 are the first line of
`tests/office/selections/issue-185.txt` and `tests/office/selections/issue-185.sha256`,
which also list LibreOffice's own tests of the rules below and the samples
this change moved most. No document is vendored.

LibreOffice's test of this file (`testFdo68607`, `ooxmlexport8.cxx`) checks
only that the floating table spans more than one page. The difference had
four causes, all `broilerdoc`'s, and three reach well beyond this sample.
The rules were read from LibreOffice 24.2.7.2's sources, the release that drew
the references; two of them differ on `master` (see the workflow note).

## 1. A page break inside a table cell is nothing

The definitions were pasted from a PDF, with a page break where every line
ended:

```xml
<w:r><w:t>A theory advocated by Aristotle … to describe the</w:t></w:r>
<w:r><w:br w:type="page"/><w:t>feeling of release felt by the audience …</w:t></w:r>
```

The builder demoted a page break with text after it in a table cell to a line
break (`docx.pagebreak.split`), so each such definition took a line per
fragment. LibreOffice 24.2's import drops a page break at table depth
(`DomainMapper_Impl::deferBreak`, "page break should be ignored inside
tables", after the binary filter's `SwWW8ImplReader::HandlePageBreakChar`),
and nothing takes its place - not even a space, hence LibreOffice's
"thefeeling". A column break there is not dropped in 24.2 (it is on `master`,
tdf#163178): where something came before it in the paragraph, the import
splits the paragraph there (`DomainMapper`, `m_bIsSplitPara`) and gives the
second part a column break, which Writer's layout ignores in a cell.
`DocxDocumentBuilder.NotePageBreak` now drops a page break inside a cell and
turns a column break with content before it into a paragraph split with no
break, both reported as `docx.pagebreak.table`. `docx.pagebreak.split` and
`docx.break.column` are gone: a break in a body paragraph has been split into
fragments for some time (`docx.pagebreak.fragment`), and the cell was their
last use.

Nine DOCX samples have a page or column break run in a table cell, among
them LibreOffice's `table-pagebreak.docx` and
`tdf163178_columnbreakInTable.docx`.

## 2. Two lines kept together where a paragraph states nothing

With the text fixed, pages 4 and 5 still began differently: `broilerdoc`
broke the "Fourth wall" row after the first line of its two-line definition,
where LibreOffice moved the row whole. The file states `w:widowControl`
nowhere. ECMA-376 leaves it off, but Word keeps two lines, and LibreOffice
imports it so: where `w:docDefaults` holds a `w:pPrDefault`,
`StyleSheetTable::applyDefaults` sets widows and orphans to two
(tdf#87533, "Import with documented defaults"). Writer then applies them in
a table cell only in a floating table of a Word document: `WidowsAndOrphans`
(`widorp.cxx`) turns the control off in a row that may split, "for
compatibility reasons", unless the paragraph is in a fly and the document
has `TABLE_ROW_KEEP`, which the DOCX import sets. This table is floating, so
the two-line definition cannot leave one line behind, and its row moves.

`DocxStyles.HasParagraphDefaults` says whether the `w:pPrDefault` is there,
and the reader sets `DocumentStyleDefaults.WidowOrphanLines` to two rather
than stamping every paragraph, so round trips compare as before.
`DocumentLayout.ComposeTable` and `ComposeBlocks` apply it to the paragraphs
of a floating table's cells, and of tables nested in one, that state nothing
(`OrphanLinesOf`/`WidowLinesOf`).

The body does not take it yet. 1,701 of the corpus' 1,800 DOCX samples have
a `w:pPrDefault`, and applied to every paragraph the default turned twelve
passing pixel checks to failures, then four once the column-balancing marker
was left to stated values. Each was a gap in how the layout keeps lines, not
in the rule - balanced columns, a line pushed down by its own picture, and
inline table cells, where Writer turns the control off and the layout does
not - and each is on the roadmap with its sample. The Melodrama row at the
foot of page 4, a four-line definition with 36 points left, moves whole for
another reason both renderers already share: its minimum height (45.85 pt)
is more than the room (`SwTabFrame::Split`, `RowBox.SplitAt`).

## 3. Where Word 2010 stands a table

The table stood 6 points right of LibreOffice's and its text with it.
LibreOffice 24.2 (`DomainMapperTableHandler::endTableGetTableStyle`) places
a table that is not nested so that, for Word 2010 and earlier
(`compatibilityMode` 1 to 14), the text of its first column is at `w:tblInd`:
the edge is the left cell margin, or half the left border where wider, left
of it. A floating table placed by `w:tblpX` stands its left cell margin left
of the offset below mode 15 - a document stating no mode included - and half
its first cell's left border further in every mode. This file is mode 14,
cell margin 5.4 pt, border 1 pt: the table's edge is at 30.1 pt, not 36.

`DocxTableReader.PlaceAsWord` applies both to `DocumentTable.LeftIndent` and
`TablePosition.X`, which stay the table's left edge for the layouts and the
other writers. The DOCX writer states no compatibility mode, under which an
inline table's `w:tblInd` is its edge, and adds the floating offset back to
`w:tblpX`, so a written table reads back where it was. 174 DOCX samples state
mode 14 or less and have a table; 83 have a floating one.

## 4. A text box too short for its first line

The title "IGCSE Drama Glossary" is a text box anchored in the table's first
body cell, 82.2 points above it, 33.75 points tall with 3.6-point insets. Its
one line, 24-point bold Calibri at 1.15 spacing, needs 33.7 points of the
26.55 inside the insets, and `PlaceShapeContent` left out every line that did
not fit whole, so the box was drawn empty. LibreOffice draws the line: Writer
formats a text frame's first line whatever room it has, and the file states
no `vertOverflow`, which LibreOffice clips only when it says `clip` or
`ellipsis` (`TextBodyPropertiesContext`). The first line of a box now shows,
cut at the box's text area (`LayoutLine.ClipHeight`); the lines after it are
still left out where they do not fit.

## Verification

Replayed against `office-references-v3` (LibreOffice 24.2.7.2, pdftoppm 24.02,
96 dpi, the reference fonts) with `broilerdoc` built from `6826b70` (main) and
from this change, over all 3,907 samples:

| Checks | Before | After |
| --- | ---: | ---: |
| Pixel passed / failed / skipped | 2,612 / 1,242 / 53 | 2,620 / 1,235 / 52 |
| Text passed / failed / skipped | 3,503 / 359 / 45 | 3,504 / 358 / 45 |
| Read passed / failed | 3,873 / 11 | 3,873 / 11 |

No check that passed fails. Nine pass that failed: this sample's pixel and
text checks, `tdf164907_rowHeightAtLeast.docx`, `fdo78474.docx`,
`n792778.docx`, `tdf123898.odt`, `fdo57708.rtf` and
`tdf154129_transparentFrame.rtf` (the ODT and RTF samples gain from the text
box rule, the only one of the four that reaches them), and
`tdf90260-nopar.rtf`, which `main`'s replay skipped because `pdftoppm`
produced no pages for its reference - a replay artefact, not this change.
Of the 116 samples whose worst-page metrics moved, 94 came closer to the
reference, among them `fdo78883.docx` (blur difference 28.7% -> 24.2%) and
`tdf160077_layoutInCell.docx` (8.0% -> 5.2%). 22 moved away, each still on
the same side of the threshold: `fdo80097.docx` by 1.5 points (its whole
page stands lower than the reference's in both builds; its tables now stand
where 24.2 puts them), `floattable-nosection-7in.docx` by 0.6 and
`fdo76249.docx` by 0.4 (whose profile came closer, 0.00 -> 0.26), the rest
by less. `tests/office/selections/issue-185.txt` lists these samples.

The ranked sample, on this host:

| | Before | After |
| --- | ---: | ---: |
| Severity | 1112.88 | passes |
| Pages | 8 / 8 | 8 / 8 |
| Text check | 21 differences | passes |
| Pixel check | 61.82%, fails | passes |
| Worst page, blur difference | 32.4% (page 8) | 9.4% (page 1) |
| Ink profile similarity | 0.784 | 0.999 |

Every page now starts and ends on the same row as the reference.

`dotnet test Broiler.Documents.slnx -c Release`: 5,063 passed, 3 skipped (the guards that run only in the aggregate repository). Three existing tests
pinned the old behaviour and were changed: `DocxPageBreakTests` expected the
line break in a cell, `DocxFloatingTableTests` the unshifted `tblpX` of a
package stating no mode; `DocumentLayoutShapeTests` now allows the clipped
first line. New tests: `DocxPageBreakTests.A_Break_Inside_A_Table_Cell_Starts_No_Page_Or_Column`,
the placement cases in `DocxFloatingTableTests`, the default cases in
`DocxParagraphPaginationTests`, `DocumentLayoutParagraphKeepTests`
(a body paragraph keeps what it states; a floating table's row moves whole,
an inline table's does not),
`DocumentLayoutTextFrameTests.A_Frame_Too_Short_For_Its_First_Line_Shows_It_Cut_At_Its_Text_Area`
and `DocumentStyleDefaultsTests.A_Paragraph_Keeps_Its_Own_Lines_Or_The_Documents`.
The corpus suite passes (2,320 checks).

## Not changed, seen on the way

- **Superscript.** "19th" on page 6 is `w:vertAlign="superscript"`, which the
  model does not hold for ordinary text (a roadmap item, now naming this
  sample). Set at full size, the definition wraps one word earlier than
  LibreOffice's; the row keeps its page.
- **Writing the widow default.** No writer states
  `DocumentStyleDefaults.WidowOrphanLines` yet, so a converted document
  paginates as before; a roadmap item.
- **Rows a few tenths of a point apart.** The rows run within half a point of
  the reference down page 3; the remaining blur difference on page 1 is that
  drift and the title, which stands about 2 pixels (96 dpi) higher.

## Environment and reproduction

- Reference set `office-references-v3`, unpacked at
  `D:/scratch/office-references-v3`; LibreOffice 26.2.6.3 on Windows for the
  probes.
- Replay:
  `dotnet run --project src/tests/Broiler.Documents.Office -c Release -- --tool <broilerdoc> --extras-dir <refs>/inputs --references-dir <refs> --accept-reference-mismatch --pdftoppm <pdftoppm> --font-dir <refs>/fonts/0 --dpi 96 --sample-list tests/office/selections/issue-185.txt --report <out>/report.json --workspace <dir> --allow-pending-tools`.
- Positions came from `pdftotext -bbox` on the reference and from renders at
  288 dpi; LibreOffice's rules from the 24.2.7.2 sources
  (`writerfilter/source/dmapper/DomainMapper_Impl.cxx`,
  `DomainMapperTableHandler.cxx`, `StyleSheetTable.cxx`,
  `oox/source/drawingml/textbodypropertiescontext.cxx`,
  `sw/source/core/layout/tabfrm.cxx`).
