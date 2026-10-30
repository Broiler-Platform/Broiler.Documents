# Issue #180: a heading split by a comment, and five ordinary defects

First of the [30 ranked differences in issue #180](https://github.com/Broiler-Platform/Broiler.Documents/issues/180):
`ooxmlexport/data/wDateValueFormat.docx` (severity 1263.38), a German
clean-room qualification report: 19 pages against LibreOffice's 18, worst-page
blur difference 25.0%, 21 text lines different, each a chapter number one
lower than LibreOffice's ("2 Beschreibung der Räume" against "3 …"). Path and
input SHA-256: first line of `tests/office/selections/issue-180.txt` and
`tests/office/selections/issue-180.sha256`, which also list the samples this
change moved most; not vendored.

Six causes: a LibreOffice 24.2 defect, now a row of
`tests/office/extras-reference-defects.json`, and five `broilerdoc` defects
that reach well beyond this sample.

## 1. LibreOffice 24.2 splits the heading (gerrit#163053)

The "1.3" heading opens with a page break run, a comment reference, then text:

```xml
<w:p><w:pPr><w:pStyle w:val="berschrift2"/></w:pPr>
  <w:r><w:br w:type="page"/></w:r><w:bookmarkStart …/><w:commentRangeEnd w:id="14"/>
  <w:r>…<w:commentReference w:id="14"/></w:r>
  <w:r><w:t>Status Filter-Strömungstest</w:t></w:r>…</w:p>
```

The reference (`7070ca74…6efc1a.pdf` and `.txt` in `office-references-v3`)
leaves "1.3" alone at the foot of page 4 and opens page 5 with "2 Status
Filter-Strömungstest" in 10 point body text, the label at the heading list's
level 0 left indent (x = 92.6 pt), no hanging indent. Every later chapter is
one higher: the whole text difference (127 lines of the canonical projection).

LibreOffice 24.2's DOCX import (`writerfilter/source/dmapper`):

- `DomainMapper::lcl_utext` holds a page break until the next text, then
  splits the paragraph if `!IsFirstRun()`; for the first text it only sets
  break-before.
- `IsFirstRun` was one `DomainMapper_Impl` member shared by every stream. The
  comment's paragraphs, read where its `w:commentReference` stands, pass their
  text through the same `lcl_utext`, which clears the flag, so the body text
  arrives "not first" and the paragraph splits.
- The split pushes the property context again (`PushProperties` with
  `m_bIsSplitPara`), erasing list id, level, start value and first-line
  indent, and `lcl_startParagraphGroup` sets the default paragraph style. The
  `NumberingStyleName` that `finishParagraph` copied from the heading style is
  not erased, so the second part stays in the heading list at level 0: "2".

Michael Stahl's "writerfilter: move even more members to SubstreamContext"
(e6783f42ae706da76fd347790c15ac3397ce4530, review 163053, February 2024)
moved the flag into each substream's state; `libreoffice-24-8` has it,
`libreoffice-24-2` does not. It added no test, so the row's `upstreamTests` is
empty and `untested` names what pins the fixed rendering: LibreOffice 26.2.6.3
keeps the heading whole ("1.3 Status Filter-Strömungstest" on page 5, chapter
2 "Beschreibung der Räume" on page 7), as do the `PAGEREF` results of the
table of contents Word saved.

Probes on 26.2: a page break, then a comment reference (with text, empty, or
naming no comment) or a footnote reference, then text, keeps one paragraph; a
page break leading a section's first paragraph still splits it (below). Of
all 1,807 DOCX samples only this one has that pattern. The row covers the text
and pixel checks; `broilerdoc` keeps the heading whole, as 26.2 and Word do.

## 2. The size of text that states none (the extra page)

Page 1's tables ran onto page 2. `w:docDefaults` holds a `w:rPrDefault`
naming Times New Roman and no size, and the default paragraph style names
Arial with no size (only `w:szCs`); `broilerdoc` used the model's 12 points,
the reference 10. LibreOffice decides by whether `w:rPrDefault` is present
(`StyleSheetTable`, tdf#108350): without it, Word 2007's Calibri 11; with it,
Times New Roman 10 overridden by what it states. `DocxStyles.HasRunDefaults`
records the element and the style defaults, family included, fall back
accordingly. This also restored the header's second line ("OP 1" under
"Requalifizierungsprotokoll" in 14 point bold, in a 36.65 pt text box with no
vertical insets), which did not fit at 12 points.

## 3. Page numbers in DOCX footers

The footer `Seite PAGE von NUMPAGES` showed the saved "Seite 14 von 19" on
every page: the DOCX reader made no `PageField`, though the layout has long
redrawn ODT page fields per page. `DocxFields` now recognises `PAGE` and
`NUMPAGES` with `\* roman`, `ROMAN`, `alphabetic`, `ALPHABETIC` and `Arabic`
(other switches keep the saved result), in complex fields and `w:fldSimple`;
a field saved without a result gets the first page's number for the layout to
replace. The result takes the formatting of the run holding the separator,
where LibreOffice inserts the field (`CloseFieldCommand`), never reading the
result's runs: here 12 points, as in the reference.

## 4. Links: the table of contents, and DOCX and RTF links in general

The table of contents was blue and underlined; the reference's is black.

- Inside a `TOC` field's result LibreOffice applies no character style
  (`DomainMapper`, `LN_EG_RPrBase_rStyle`: "do not add it elements in TOC,
  they will receive later another style references from TOC") and gives the
  link the unformatted "Index Link", dropping Word's `Hyperlink` style.
  `DocxFields.InTableOfContents` tracks separator to end, and `ReadRun` skips
  `w:rStyle` there.
- `broilerdoc render` decorated any link without its own colour (blue,
  underlined) unless given `--no-link-style`. On 26.2 a DOCX link naming no
  character style and an RTF `HYPERLINK` field are plain; an ODT `text:a`
  without a style takes Writer's "Internet Link" (`#000080`, underlined).
  `DocumentStyleDefaults.UndecoratedLinks`, set by the DOCX and RTF readers,
  makes the layout leave links as their formatting makes them.

## 5. Soft hyphens

The header cells hold "Reinraum&#173;klasse" (`w:softHyphen`) and are too
narrow. The layout treated U+00AD as a space: it broke there without a hyphen
and elsewhere drew and measured it (a missing-glyph box in some faces), so
"Soll-" stood above its cell. A soft hyphen is now a zero-width token drawing
nothing, and a line breaking at it ends in a hyphen (`LayoutToken.SoftHyphen`,
`LineWrapper`).

## 6. Text set in Symbol

Column heads "≥ 0,5µm" store "≥" as U+F0B3 in a `Symbol` run, Word's
private-use spelling of Symbol's slot 0xB3. No Symbol face is among the
reference fonts; LibreOffice recodes such text to the characters it stands for
(its OpenSymbol conversion), where `broilerdoc` drew two missing-glyph boxes.
`SymbolFontText` holds the encoding, moved from the PDF reader's
`PdfEncodings` so both share it, and the layout draws a Symbol run as those
characters in the document's face. The text projection keeps U+F0B3, as
LibreOffice's does.

## 7. Shapes in table cells

The check boxes are groups of a bevelled square and two strokes anchored in a
table's middle column (`layoutInCell="1"`, positioned from the column). Only
line shapes were placed from their cell, parting strokes and squares. Every
drawn shape in a cell positioned from the column is now placed from the cell,
unless its anchor says `layoutInCell="0"`.

## Verification

Tests: `DocxPageFieldTests`, `DocumentLayoutGlyphTextTests`,
`DocumentLayoutLinkDecorationTests`, and two `DocxStyleDefaultsTests` cases
replacing the one that pinned 12 points.

All 3,907 samples, `31e9e5f` (main) → this change: pixel passes 2,608 →
2,613, text failures 364 → 363 (the ranked sample now skipped on both), read
unchanged, none lost. Newly passing pixel checks:
`ooxmlexport/data/tdf105035_framePrC.docx` (blur difference 26.6% → 10.8%),
`ooxmlexport/data/table-row-data-displayed-twice.docx`,
`ww8export/data/tdf117503.docx` (1.7% → 0.2%), `layout/data/tdf134277.docx`
(shapes in cells), `uiwriter/data/tdf130680.odt` (profile 0.79 → 0.89).

Of 81 samples whose worst-page metrics moved, 72 came closer and eight others
by under 0.4%. `ooxmlexport/data/tdf104713_undefinedStyles.docx` moved away
(0.5% → 9.1%, still passing): with no `w:docDefaults` it now falls back to
Calibri 11 as LibreOffice does, but its headings use styles naming no font and
inheriting nothing, which LibreOffice draws in its built-in Heading style's
Liberation Sans, matched before only by the old sans-serif fallback.

The ranked sample now draws 18 pages, pages 1 to 4 within a few percent, every
footer numbered; its text and pixel checks are not compared (reference
defect). Against LibreOffice 26.2.6.3 the text projections agree line for
line.

## Not changed, seen on the way

- **EMF pictures.** The three charts are EMF, not decoded
  (`docx.image.format`): empty frames where LibreOffice draws bar charts.
  About 130 corpus samples hold EMF or WMF (100 DOCX, 18 RTF, 13 ODT); on the
  roadmap.
- **LibreOffice 26.2 draws the EMF charts larger** than their stated 400 x
  266 pt (about 455 pt high), a page later (19 pages); 24.2 uses the stated
  size, as `broilerdoc` does.
- **A page break leading a section's first paragraph** splits it in
  LibreOffice (`GetIsFirstParagraphInSection` in the same condition): the
  empty first part, with a numbered heading's label, stays on the page before.
  `broilerdoc` moves the whole paragraph; no ranked sample needs it yet.
- **ODT links** use `broilerdoc`'s `#0033CC`, where Writer's "Internet Link"
  is `#000080`.
- **Wingdings private-use text** (three `w:sym` arrows here) is not recoded.
- **The DOCX writer** writes a page field's saved text, not the field.

## Environment and reproduction

- References `office-references-v3` (LibreOffice 24.2.7.2, pdftoppm 24.02, 96
  dpi, reference fonts) at `D:/scratch/office-references-v3`; LibreOffice
  26.2.6.3 on Windows for the probes and the fixed rendering.
- Replay:
  `dotnet run --project src/tests/Broiler.Documents.Office -c Release -- --tool <broilerdoc> --extras-dir <refs>/inputs --references-dir <refs> --accept-reference-mismatch --pdftoppm <pdftoppm> --font-dir <refs>/fonts/0 --dpi 96 --sample-list tests/office/selections/issue-180.txt --report <out>/report.json --workspace <dir> --allow-pending-tools`.
- The fragment's position came from `pdftotext -bbox` on the reference PDF;
  the split rule from the 24.2 sources (`DomainMapper.cxx`,
  `DomainMapper_Impl.cxx`) checked against the `libreoffice-24-8` and master
  headers.
