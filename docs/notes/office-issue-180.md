# Issue #180: a heading split by a comment, and five ordinary defects

The first of the [30 ranked differences in issue #180](https://github.com/Broiler-Platform/Broiler.Documents/issues/180)
was `ooxmlexport/data/wDateValueFormat.docx` (severity 1263.38): a German
clean-room qualification report, 19 pages in `broilerdoc` against
LibreOffice's 18, a blur difference of 25.0% on the worst page and 21 lines of
text difference, every one of them a chapter number one lower than
LibreOffice's ("2 Beschreibung der Räume" against "3 …").

Its path and input SHA-256 are the first line of
`tests/office/selections/issue-180.txt` and `tests/office/selections/issue-180.sha256`,
which also list the samples whose renders this change moved most. No document
is vendored.

The difference had six causes. One is a LibreOffice 24.2 defect, now a row of
`tests/office/extras-reference-defects.json`; the other five were
`broilerdoc`'s, and each reaches well beyond this sample.

## 1. LibreOffice 24.2 splits the heading (gerrit#163053)

The "1.3" heading opens with a page break run, then a comment reference, then
its text:

```xml
<w:p><w:pPr><w:pStyle w:val="berschrift2"/></w:pPr>
  <w:r><w:br w:type="page"/></w:r><w:bookmarkStart …/><w:commentRangeEnd w:id="14"/>
  <w:r>…<w:commentReference w:id="14"/></w:r>
  <w:r><w:t>Status Filter-Strömungstest</w:t></w:r>…</w:p>
```

The reference (`7070ca74…6efc1a.pdf` and `.txt` in `office-references-v3`)
leaves "1.3" alone at the foot of page 4 and starts page 5 with
"2 Status Filter-Strömungstest" in 10 point body text, its label at the left
indent of the heading list's level 0 (x = 92.6 pt) with no hanging indent.
Every later chapter is numbered one higher, which is the whole text
difference (127 lines of the canonical projection).

LibreOffice 24.2's DOCX import accounts for it
(`writerfilter/source/dmapper`):

- `DomainMapper::lcl_utext` holds a page break until the next text and then
  splits the paragraph if `!IsFirstRun()`, that is when text has already
  arrived in it; for the first text it only sets break-before.
- `IsFirstRun` was one member of `DomainMapper_Impl`, shared by every stream.
  The comment's own paragraphs are read where its `w:commentReference` stands,
  and their text goes through the same `lcl_utext`, which clears the flag at its
  end. The body paragraph's text then arrives "not first", and the paragraph
  splits.
- The split pushes the same property context again (`PushProperties` with
  `m_bIsSplitPara`), erasing the list id, list level, start value and first
  line indent, and `lcl_startParagraphGroup` sets the default paragraph style.
  `finishParagraph` had copied the heading style's list onto the context as
  `NumberingStyleName`, which nothing erases, so the second paragraph stays in
  the heading list at level 0: "2".

Michael Stahl's "writerfilter: move even more members to SubstreamContext"
(e6783f42ae706da76fd347790c15ac3397ce4530, review 163053, February 2024) moved
the flag into each substream's state, so a comment no longer touches the
paragraph it is anchored in. `libreoffice-24-8` has it and `libreoffice-24-2`
does not. The change came with no test, so the row's `upstreamTests` is empty
and its `untested` field says what pins the fixed rendering instead:

- LibreOffice 26.2.6.3 draws the sample with the heading whole: "1.3 Status
  Filter-Strömungstest" on page 5, chapter 2 "Beschreibung der Räume" on page 7.
- The table of contents Word saved in the document says the same, with its
  `PAGEREF` results: 1.3 on page 5, chapter 2 on page 7.

Authored probes on 26.2 show the fixed rule: a page break, a comment reference
(with text, empty, or naming no comment) or a footnote reference, then text,
keeps one paragraph; a page break leading the first paragraph of a section
still splits it (see "Not changed" below). A scan of all 1,807 DOCX samples
finds this pattern, a leading page break and a comment or note reference
before the first text, in this sample alone.

The row covers the text and pixel checks. `broilerdoc` keeps the heading
whole, as LibreOffice 26.2 and Word do.

## 2. The size of text that states none (the extra page)

Page 1's tables ran onto page 2. The document's `w:docDefaults` hold a
`w:rPrDefault` naming Times New Roman and no size, and the default paragraph
style names Arial, again with no size (only `w:szCs`). `broilerdoc` fell back
to the model's 12 points; the reference sets that text in 10.

LibreOffice decides by whether `w:rPrDefault` is there (`StyleSheetTable`,
tdf#108350): without one, a DOCX gets Word 2007's Calibri 11; with one, the
defaults go back to Times New Roman 10 and the element overrides what it
states. `DocxStyles.HasRunDefaults` records the element, and the style
defaults fall back accordingly, family included. The same size brought back the header's
second line: its text box, 36.65 pt high with no insets above or below, holds
"Requalifizierungsprotokoll" in 14 point bold and "OP 1" in the default size,
and at 12 points the second line no longer fitted.

## 3. Page numbers in DOCX footers

The footer reads `Seite PAGE von NUMPAGES`, and `broilerdoc` drew the results
Word saved, "Seite 14 von 19", on every page: the DOCX reader made a
`PageField` for nothing, though the layout has redrawn ODT page fields on
every page for a long time.

`DocxFields` now recognises `PAGE` and `NUMPAGES`, with `\* roman`, `ROMAN`,
`alphabetic`, `ALPHABETIC` and `Arabic` (other switches keep the saved
result), in complex fields and in `w:fldSimple`; a field saved without a
result gets the first page's number for the layout to replace. The result is
formatted as the run holding the separator is: LibreOffice inserts the field
there (`CloseFieldCommand`) and never reads the result's runs. Here the field
characters state 12 points and the saved result none, and the reference's page
numbers are 12 point.

## 4. Links: the table of contents, and DOCX and RTF links in general

The table of contents was drawn in blue, underlined links; the reference draws
it black. Two rules:

- Inside a `TOC` field's result, LibreOffice applies no character style
  (`DomainMapper`, `LN_EG_RPrBase_rStyle`: "do not add it elements in TOC,
  they will receive later another style references from TOC"), and gives the
  entry's link the unformatted "Index Link". Word's `Hyperlink` style on each
  entry is dropped. `DocxFields.InTableOfContents` tracks the span, from the
  field's separator to its end, and `ReadRun` skips `w:rStyle` there.
- `broilerdoc render` decorates a link with no colour of its own (blue,
  underlined) unless told `--no-link-style`. Probes on LibreOffice 26.2 show
  that a DOCX link naming no character style is plain, an RTF `HYPERLINK`
  field likewise, and an ODT `text:a` without a style takes Writer's
  "Internet Link" (`#000080`, underlined). `DocumentStyleDefaults.UndecoratedLinks`
  says which: the DOCX and RTF readers set it, and the layout leaves such a
  document's links as their formatting makes them.

## 5. Soft hyphens

The header cells write "Reinraum&#173;klasse" with a `w:softHyphen` and are
too narrow for it. The layout treated U+00AD as a space: it broke there
without drawing a hyphen, and where the line went on it drew and measured the
character (a missing-glyph box in some faces). Because the measured and the
drawn lines disagreed, "Soll-" stood above its cell. A soft hyphen is now a
token of its own with no width that draws nothing, and a line that breaks at
it ends in a hyphen (`LayoutToken.SoftHyphen`, `LineWrapper`).

## 6. Text set in Symbol

The measurement tables head their columns "≥ 0,5µm", the "≥" stored as U+F0B3
in a run set in `Symbol`, Word's private-use spelling of Symbol's slot 0xB3.
No face called Symbol is among the reference fonts, and LibreOffice recodes
such text to the characters it stands for (its OpenSymbol conversion); here
it drew two missing-glyph boxes. `SymbolFontText` holds the font's encoding,
moved from the PDF reader's `PdfEncodings` so both read one table, and the
layout draws a run set in Symbol as the characters its slots stand for, in
the document's face. The text projection keeps U+F0B3, as LibreOffice's does.

## 7. Shapes in table cells

The form's check boxes are groups of a bevelled square and two strokes,
anchored in the middle column of a table (`layoutInCell="1"`, positioned from
the column). Only line shapes were placed from their cell, so the strokes
stood in the middle column and the squares in the first. Every drawn shape in
a cell positioned from the column is now placed from the cell, unless its
anchor says `layoutInCell="0"`.

## Verification

Replayed against `office-references-v3` (LibreOffice 24.2.7.2, pdftoppm 24.02,
96 dpi, the reference fonts) with `broilerdoc` built from `31e9e5f` (main) and
from this change, over all 3,907 samples:

| Checks | Before | After |
| --- | ---: | ---: |
| Pixel passed / failed / skipped | 2,608 / 1,248 / 51 | 2,613 / 1,242 / 52 |
| Text passed / failed / skipped | 3,499 / 364 / 44 | 3,499 / 363 / 45 |
| Read passed / failed | 3,873 / 11 | 3,873 / 11 |

No check that passed fails. Five pixel checks pass that failed:
`ooxmlexport/data/tdf105035_framePrC.docx` (blur difference 26.6% -> 10.8%),
`ooxmlexport/data/table-row-data-displayed-twice.docx`,
`ww8export/data/tdf117503.docx` (1.7% -> 0.2%), `layout/data/tdf134277.docx`
(the shapes in cells) and `uiwriter/data/tdf130680.odt` (profile similarity
0.79 -> 0.89). The ranked sample's text and pixel checks are skipped as a
reference defect. Of the 81 samples whose worst-page metrics moved, 72 came
closer to the reference. One moved clearly away:
`ooxmlexport/data/tdf104713_undefinedStyles.docx` (blur difference 0.5% ->
9.1%, still passing), which has no `w:docDefaults` at all and so now falls
back to Calibri 11 as LibreOffice does; its headings, though, use styles that
name no font and inherit from nothing, and LibreOffice draws them in its own
built-in Heading style's Liberation Sans, which the old sans-serif fallback
matched by chance. The other eight moved by less than 0.4%.
`tests/office/selections/issue-180.txt` lists these samples.

The ranked sample: 18 pages as the reference, page 1 to 4 within a few
percent of it, every footer numbered; its text and pixel checks are now not
compared (the reference defect). Against LibreOffice 26.2.6.3, which draws the
heading whole, the text projections agree line for line.

`dotnet test Broiler.Documents.slnx -c Release`: 5,018 passed, 3 skipped (the guards that run only in the aggregate repository). New tests:
`DocxPageFieldTests` (page fields, switches, the separator's formatting,
simple fields, the table of contents, undecorated links),
`DocumentLayoutGlyphTextTests` (soft hyphens, Symbol),
`DocumentLayoutLinkDecorationTests`, and two cases in `DocxStyleDefaultsTests`
replacing the one that pinned 12 points.

## Not changed, seen on the way

- **EMF pictures.** The report's three charts are EMF, which `broilerdoc` does
  not decode (`docx.image.format`), so it draws empty frames where LibreOffice
  draws bar charts. About 130 corpus samples hold EMF or WMF pictures (100
  DOCX, 18 RTF, 13 ODT); this is now a roadmap item.
- **LibreOffice 26.2 draws the EMF charts larger** than their stated 400 x 266
  pt (about 455 pt high), which moves them a page on and makes that release's
  rendering 19 pages. 24.2 draws them at the stated size, as `broilerdoc`
  places its frames.
- **A page break leading a section's first paragraph** splits it in
  LibreOffice (`GetIsFirstParagraphInSection` in the same condition): the
  first part, empty, stays on the page before, and a numbered heading shows
  its label there. `broilerdoc` moves the whole paragraph. No ranked sample
  needs it yet.
- **ODT links** are drawn in `broilerdoc`'s link colour, `#0033CC`, where
  Writer's "Internet Link" is `#000080`.
- **Wingdings private-use text** (three `w:sym` arrows here) is not recoded.
- **The DOCX writer** writes a page field's saved text, not the field.

## Environment and reproduction

- Reference set `office-references-v3`, unpacked at
  `D:/scratch/office-references-v3`; LibreOffice 26.2.6.3 on Windows for the
  probes and the fixed rendering.
- Replay:
  `dotnet run --project src/tests/Broiler.Documents.Office -c Release -- --tool <broilerdoc> --extras-dir <refs>/inputs --references-dir <refs> --accept-reference-mismatch --pdftoppm <pdftoppm> --font-dir <refs>/fonts/0 --dpi 96 --sample-list tests/office/selections/issue-180.txt --report <out>/report.json --workspace <dir> --allow-pending-tools`.
- The fragment's size and position came from `pdftotext -bbox` on the
  reference PDF, and LibreOffice's split rule from the 24.2 sources
  (`DomainMapper.cxx`, `DomainMapper_Impl.cxx`) checked against the
  `libreoffice-24-8` and master headers.
