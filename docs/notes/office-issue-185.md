# Issue #185: a glossary whose definitions broke at every line

First of the [30 ranked differences in issue #185](https://github.com/Broiler-Platform/Broiler.Documents/issues/185):
`ooxmlexport/data/fdo68607.docx` (severity 1112.88), an eight-page drama
glossary, one floating table of 137 rows under a title. Eight pages in both
renderers, but worst-page blur difference 32.4% and 21 text lines different,
each a LibreOffice definition `broilerdoc` broke into several ("… describe
the" / "feeling of release …" against "… describe thefeeling of release …").
Path and input SHA-256: first line of `tests/office/selections/issue-185.txt`
and `tests/office/selections/issue-185.sha256`, which also list LibreOffice's
own tests of the rules below and the samples this change moved most; not
vendored.

LibreOffice's test of the file (`testFdo68607`, `ooxmlexport8.cxx`) checks only
that the floating table spans more than one page. Four causes, all
`broilerdoc`'s; three reach well beyond this sample. The rules come from
LibreOffice 24.2.7.2, the release that drew the references; two differ on
`master` (see the [workflow note](office-conformance-workflow.md#issue-185)).

## 1. A page break inside a table cell is nothing

The definitions were pasted from a PDF, with a page break at every line end:

```xml
<w:r><w:t>A theory advocated by Aristotle … to describe the</w:t></w:r>
<w:r><w:br w:type="page"/><w:t>feeling of release felt by the audience …</w:t></w:r>
```

The builder demoted a page break followed by text in a cell to a line break
(`docx.pagebreak.split`). LibreOffice 24.2 drops a page break at table depth
(`DomainMapper_Impl::deferBreak`, "page break should be ignored inside
tables", after the binary filter's `SwWW8ImplReader::HandlePageBreakChar`)
with nothing in its place, not even a space: "thefeeling". A column break
there is kept in 24.2 (dropped on `master`, tdf#163178): with content before
it, the import splits the paragraph (`DomainMapper`, `m_bIsSplitPara`) and
gives the second part a column break, which Writer's layout ignores in a cell.

`DocxDocumentBuilder.NotePageBreak` now drops a page break in a cell and turns
a column break with content before it into a paragraph split without a break,
both reported as `docx.pagebreak.table`. `docx.pagebreak.split` and
`docx.break.column` are gone: body breaks have long been split into fragments
(`docx.pagebreak.fragment`), and the cell was their last use. Nine DOCX
samples have a page or column break run in a cell, among them LibreOffice's
`table-pagebreak.docx` and `tdf163178_columnbreakInTable.docx`.

## 2. Two lines kept together where a paragraph states nothing

`broilerdoc` broke the "Fourth wall" row after the first line of its two-line
definition; LibreOffice moved the row whole to page 5. The file states
`w:widowControl` nowhere. ECMA-376 leaves it off, but Word keeps two lines,
and LibreOffice imports so: where `w:docDefaults` holds a `w:pPrDefault`,
`StyleSheetTable::applyDefaults` sets widows and orphans to two (tdf#87533,
"Import with documented defaults"). In a table cell Writer applies them only
in a floating table of a Word document: `WidowsAndOrphans` (`widorp.cxx`)
turns the control off in a row that may split, "for compatibility reasons",
unless the paragraph is in a fly and the document has `TABLE_ROW_KEEP`, which
the DOCX import sets.

`DocxStyles.HasParagraphDefaults` records the `w:pPrDefault`; the reader sets
`DocumentStyleDefaults.WidowOrphanLines` to two rather than stamping every
paragraph, so round trips compare as before. `DocumentLayout.ComposeTable` and
`ComposeBlocks` apply it to paragraphs stating nothing in a floating table's
cells and tables nested in one (`OrphanLinesOf`/`WidowLinesOf`).

The body does not take it yet: 1,701 of the 1,800 corpus DOCX samples have a
`w:pPrDefault`, and applied to every paragraph the default lost twelve passing
pixel checks, four once the column-balancing marker was left to stated values.
Each was a gap in how the layout keeps lines, not in the rule (balanced
columns, a line pushed down by its own picture, inline table cells where
Writer turns the control off), each on the roadmap with its sample. The
Melodrama row at the foot of page 4, four lines with 36 pt left, moves whole
for a reason both renderers share: its minimum height (45.85 pt) exceeds the
room (`SwTabFrame::Split`, `RowBox.SplitAt`).

## 3. Where Word 2010 stands a table

The table stood 6 pt right of LibreOffice's. LibreOffice 24.2
(`DomainMapperTableHandler::endTableGetTableStyle`) places a non-nested table
so that, for `compatibilityMode` 1 to 14 (Word 2010 and earlier), its first
column's text is at `w:tblInd`: the edge is the left cell margin, or half the
left border where wider, left of it. A floating table placed by `w:tblpX`
stands its left cell margin left of the offset below mode 15 (no mode
included), and half its first cell's left border further in every mode. Here
mode 14, cell margin 5.4 pt, border 1 pt: edge at 30.1 pt, not 36.

`DocxTableReader.PlaceAsWord` applies both to `DocumentTable.LeftIndent` and
`TablePosition.X`, which remain the left edge for the layouts and other
writers. The DOCX writer states no mode, under which an inline table's
`w:tblInd` is its edge, and adds the floating offset back to `w:tblpX`, so a
written table reads back in place. 174 DOCX samples state mode 14 or less and
have a table; 83 a floating one.

## 4. A text box too short for its first line

The title "IGCSE Drama Glossary" is a text box anchored in the table's first
body cell, 82.2 pt above it, 33.75 pt tall with 3.6 pt insets; its line
(24-point bold Calibri, 1.15 spacing) needs 33.7 of the 26.55 pt inside, and
`PlaceShapeContent` dropped lines not fitting whole. Writer formats a text
frame's first line whatever room it has, and the file states no
`vertOverflow`, which LibreOffice clips only for `clip` or `ellipsis`
(`TextBodyPropertiesContext`). A box's first line now shows, cut at the text
area (`LayoutLine.ClipHeight`); later lines that do not fit are left out.

## Verification

Changed tests: `DocxPageBreakTests`, `DocxFloatingTableTests` (the unshifted
`tblpX` of a package stating no mode), `DocumentLayoutShapeTests`. New:
`DocxPageBreakTests.A_Break_Inside_A_Table_Cell_Starts_No_Page_Or_Column`,
placement cases in `DocxFloatingTableTests`, default cases in
`DocxParagraphPaginationTests`, `DocumentLayoutParagraphKeepTests` (a
floating table's row moves whole, an inline table's does not),
`DocumentLayoutTextFrameTests.A_Frame_Too_Short_For_Its_First_Line_Shows_It_Cut_At_Its_Text_Area`,
`DocumentStyleDefaultsTests.A_Paragraph_Keeps_Its_Own_Lines_Or_The_Documents`.

All 3,907 samples, `6826b70` (main) → this change: pixel passes 2,612 →
2,620, text 3,503 → 3,504, read unchanged, none lost. Nine newly passing: this
sample's pixel and text, `tdf164907_rowHeightAtLeast.docx`, `fdo78474.docx`,
`n792778.docx`, `tdf123898.odt`, `fdo57708.rtf`,
`tdf154129_transparentFrame.rtf` (ODT and RTF only through the text box rule),
and `tdf90260-nopar.rtf`, which `main`'s replay skipped because `pdftoppm`
gave no pages for its reference (a replay artefact).

Of 116 samples whose worst-page metrics moved, 94 came closer, among them
`fdo78883.docx` (blur difference 28.7% → 24.2%) and
`tdf160077_layoutInCell.docx` (8.0% → 5.2%). 22 moved away, none crossing the
threshold: `fdo80097.docx` by 1.5 points (its page stands lower than the
reference's in both builds; its tables now stand where 24.2 puts them),
`floattable-nosection-7in.docx` by 0.6, `fdo76249.docx` by 0.4 (profile 0.00
→ 0.26), the rest by less.

The ranked sample now passes text (was 21 differences) and pixel (was
61.82%), 8 / 8 pages, worst-page blur difference 32.4% (page 8) → 9.4% (page
1), ink profile similarity 0.784 → 0.999; every page starts and ends on the
reference's row.

## Not changed, seen on the way

- **Superscript.** "19th" on page 6 is `w:vertAlign="superscript"`, not held
  for ordinary text (roadmap item, now naming this sample); at full size the
  definition wraps a word earlier, but the row keeps its page.
- **Writing the widow default.** No writer states
  `DocumentStyleDefaults.WidowOrphanLines` yet, so a converted document
  paginates as before; a roadmap item.
- **Rows a few tenths of a point apart.** Rows run within half a point of the
  reference down page 3; page 1's remaining blur difference is that drift and
  the title, about 2 pixels (96 dpi) higher.

## Environment and reproduction

- References `office-references-v3` (LibreOffice 24.2.7.2, pdftoppm 24.02, 96
  dpi, reference fonts) at `D:/scratch/office-references-v3`; LibreOffice
  26.2.6.3 on Windows for the probes.
- Replay:
  `dotnet run --project src/tests/Broiler.Documents.Office -c Release -- --tool <broilerdoc> --extras-dir <refs>/inputs --references-dir <refs> --accept-reference-mismatch --pdftoppm <pdftoppm> --font-dir <refs>/fonts/0 --dpi 96 --sample-list tests/office/selections/issue-185.txt --report <out>/report.json --workspace <dir> --allow-pending-tools`.
- Positions from `pdftotext -bbox` on the reference and renders at 288 dpi;
  rules from the 24.2.7.2 sources
  (`writerfilter/source/dmapper/DomainMapper_Impl.cxx`,
  `DomainMapperTableHandler.cxx`, `StyleSheetTable.cxx`,
  `oox/source/drawingml/textbodypropertiescontext.cxx`,
  `sw/source/core/layout/tabfrm.cxx`).
