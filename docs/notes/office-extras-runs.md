# Office extras: ranked runs and reporting

The first four ranked comparisons over LibreOffice's `sw/qa/extras` tree, what
each found in this component and what it left open, and two later changes to
how the runner reports and replays results. The ranking and its workflow are
described in [the office guide](../guides/office-conformance.md).

Each ranked run was measured by running the conformance runner over just the
top samples, once per build, at the issue's resolution. Tables give the pages
`broilerdoc` rendered with the severity score in brackets, against
LibreOffice's page count. Severity is dominated by the worst page and the
page-count gap, so a correct fix can raise it: reading a document right often
exposes the next difference, or widens a gap LibreOffice invents. A score that
gets worse after a fix is not necessarily a regression.

## First ranked run: forcepoint

Issue 116. The top three were fuzzer output from `layout/data/forcepoint*`:
`forcepoint91.html`, `forcepoint99.html` and `forcepoint102.rtf`, ranked on
page counts of 3 against 20, 3 against 23 and 4 against 21. Upstream they are
crash regressions whose test only loads the file, under
`//just care it doesn't crash/assert`, so the question was which differences
were misreadings here.
Four were, checked against the RTF grammar and LibreOffice for RTF and against
html5lib (a WHATWG parser) for HTML.

### Fixed

1. **RTF reads nothing after the root group closes.** `forcepoint102.rtf`
   repeats its last block fourteen times, thirteen past the `{\rtf1 ...}`
   close, and each copy drew its footer and its picture data as hex. Word and
   LibreOffice stop at that brace; so does this reader now, reporting non-blank
   trailing content as `rtf.trailing` (informational).
   `{\rtf1 Hello\par} After` reads `Hello` in both.
2. **A destination nested in a skipped one stays skipped.** The same file lost
   the backslash before `\colortbl`, leaving an unbalanced `{\stylesheet`
   holding the body, and every nested header and footer escaped it. In general
   a hyperlink inside a footnote leaked its text into the body: LibreOffice
   reads `A{\footnote ...{\fldrslt link}}B` as `A1B`, this reader read
   `AlinkB` and now reads `AB`. A skipped destination is skipped whole, as the
   list table already was; a picture inside one is still reported as
   `rtf.embedded`.
3. **HTML `noframes` and `noembed` are hidden.** The parser reads them as raw
   text and the reader printed it, tags and all; nearly all of
   `forcepoint99.html` sits inside `<noframes>`. The HTML rendering rules give
   both `display: none`, and they join the skipped list (`area`, `basefont`,
   `datalist`, `param`). `rp` stays readable as the ruby fallback, since the
   model has no ruby.
4. **HTML drops NUL in body text.** The tree builder ignores it, so a
   conforming DOM never holds one; the parser used here kept it and
   `forcepoint91.html` drew a missing glyph in `Cm?aq`.

`forcepoint99.html` now reads as html5lib's tree does once hidden elements are
set aside (the `<textarea>` content), and `Cmaq New Zenand` is html5lib's
spelling of the NUL-damaged line in `forcepoint91.html`.

### LibreOffice's reading of the damage

- **`forcepoint102.rtf`, 21 pages.** LibreOffice also swallows the body into
  the unbalanced stylesheet, but lets the hidden `{\v\xe ...}` index entries out
  as visible `XE "..."` fields inside table cells it also lets out. This
  component reads one page: the literal `Ücolortbl;;;…` text and the one copy of
  picture data inside the document, both of which LibreOffice also shows.
- **`forcepoint99.html`, 23 pages.** LibreOffice lays out the `noframes`
  content as markup (the column of stray `>` on its first page) plus tables
  whose fuzzed attributes draw as black blocks; all but one of those tables
  sit inside `<textarea>`.
- **`forcepoint91.html`, 20 pages.** A `cellpadding` of 65536 and a width of
  2285779150443901191 give page-high grey cells with one character per line.
  This reader flattens HTML tables to paragraphs.

Fonts were not pinned, so absolute numbers differ slightly from the issue's.

| Sample | Pages before | Pages after | LibreOffice | Severity before | Severity after |
| --- | ---: | ---: | ---: | ---: | ---: |
| `forcepoint91.html` | 3 | 3 | 20 | 2172.65 | 2172.65 |
| `forcepoint99.html` | 4 | 3 | 23 | 1974.37 | 2012.68 |
| `forcepoint102.rtf` | 4 | 1 | 21 | 1858.56 | 1986.50 |

Severity rose because faithful reading widens the page gap LibreOffice invents;
`forcepoint91.html`'s removed NUL glyphs were not on the worst page. Fuzzer
output is therefore set aside from the ranking and checked only for being
handled, as [the office guide](../guides/office-conformance.md#the-upstream-ranking)
describes.

Tests: `RtfSkippedDestinationTests`, `RtfDocumentEndTests`,
`HtmlHiddenContentTests`.

## Second ranked run: columns

Issue 118, the first run with fuzzer files set aside. Six of the top ten were
misread or mislaid out here, in four ways; the harness also used the wrong
fonts.

### Fixed

1. **DOCX table style paragraph and run properties apply.** `fdo68607.docx`
   (a glossary table under `Medium Shading 1 Accent 1`) and
   `test_segfault_while_save.docx` (under `Table Grid`) both state
   `w:spacing w:after="0" w:line="240"`, cancelling Word's default 10 pt after
   and 1.15 lines in every cell. ECMA-376 §17.7.2 puts the table style between
   the document defaults and the paragraph style, as LibreOffice's
   `DomainMapperTableHandler::ApplyParagraphPropertiesFromTableStyle` does.
   Banding and conditional formatting are not applied (`docx.table.style`).
2. **Text columns.** `many-paragraphs-per-page-in-6-columns.odt` (six on its
   page layout) and `fdo43573-2-min.docx` (two on its section) were one wide
   column. `PageGeometry` carries a column count and gap, DOCX, ODT and RTF
   read and write them, and pagination fills columns in turn. `fdo43573` ends
   its columns with a continuous break before one empty paragraph, so the
   columns come from the section holding the most paragraphs.
3. **A column break in a one-column section is a page break.** `FDO77715.docx`
   opens each block with `w:br w:type="column"` in a one-column section;
   ECMA-376 `ST_BrType` restarts the text on the next page, as Word and
   LibreOffice do. The section is found before the content is read, because
   section properties follow their paragraphs.
4. **A page break that opens its paragraph starts the page.** Breaks with text
   after them were demoted to line breaks, since a paragraph cannot split
   across pages; a break with nothing before it splits nothing. Affects
   `FDO77715` and `wDateValueFormat.docx`'s headings.

### Harness fonts

The ranking pins only the seed corpus's families (Liberation, DejaVu Sans).
Word documents ask for Times New Roman, Arial and Calibri; LibreOffice gets
Liberation and Carlito through fontconfig's metric aliases, while `broilerdoc`
used its host fallback (`fdo43573` was measured as DejaVu Sans against
Liberation Serif). The runner now maps fontconfig's five metric-compatible
pairs: Times New Roman, Arial and Courier New to Liberation; Calibri to
Carlito; Cambria to Caladea. A family the corpus pins keeps its own face, so the
seed corpus is unaffected. With faces matched, `test_segfault_while_save` and
`wDateValueFormat` render short of LibreOffice instead of long.

### Left open at the time

- **`tdf163894_hidden.docx`, 4 against 17.** LibreOffice 24.2 expands each
  header `STYLEREF "Main Index Entry"` to the whole paragraph, putting the
  document's text in every header. LibreOffice has since fixed that
  (`testTdf163894_hidden` in `sw/qa/extras/ooxmlexport/ooxmlexport14.cxx`
  checks four pages of two-word headers). This component draws Word's cached
  field results on four pages and does not evaluate `STYLEREF` per page.
- **`tdf134965.odt`, 23 against 29.** Rows longer than a page were placed
  whole. Fixed in [follow-up](#follow-ups) 1. Its two footnotes were skipped as
  `odt.note`.
- **`tdf123163-1.docx`, 12 against 16.** Thirty-one sections, eight starting a
  page, eleven in two to four columns, plus floating pictures and exact row
  heights (`docx.image.shape`, `docx.table.rowheight`). See follow-up 3.
- **`tdf135056.odt`, 4 against 17.** Six frames anchored by page number
  (`text:anchor-type="page"`, `text:anchor-page-number` 1, 4, 5, 17), the last
  sized only by `fo:min-width`. See follow-up 2 and [odt.md](odt.md).

| Sample | Base | Table styles | Columns and breaks | Font aliases | LibreOffice |
| --- | ---: | ---: | ---: | ---: | ---: |
| `fdo43573-2-min.docx` | 27 (1811.62) | 27 | 20 (1432.49) | 17 (1295.64) | 15 |
| `many-paragraphs-per-page-in-6-columns.odt` | 40 (1603.60) | 40 | 11 (758.25) | 11 (758.25) | 12 |
| `tdf134965.odt` | 23 (1558.46) | 23 | 23 | 23 (1558.46) | 29 |
| `tdf123163-1.docx` | 14 (1532.12) | 14 | 15 (1511.79) | 12 (1631.69) | 16 |
| `wDateValueFormat.docx` | 13 (1510.12) | 13 | 18 (1218.45) | 16 (1163.55) | 18 |
| `tdf163894_hidden.docx` | 4 (1464.11) | 4 | 4 | 4 (1464.11) | 17 |
| `FDO77715.docx` | 33 (1452.23) | 33 | 40 (1213.94) | 40 (1134.40) | 40 |
| `tdf135056.odt` | 4 (1446.12) | 4 | 4 | 4 (1446.12) | 17 |
| `fdo68607.docx` | 15 (1445.74) | 10 (1198.42) | 10 | 9 (1187.95) | 8 |
| `test_segfault_while_save.docx` | 13 (1394.48) | 9 (1235.46) | 9 | 7 (1247.15) | 8 |

The base column reproduces the issue exactly (workflow font directory). Six of
ten moved toward LibreOffice; `FDO77715` lands on its count. `tdf123163-1`
moved away once fonts matched: its Liberation/Carlito text takes fewer lines.

Tests: `DocxTableStyleTests`, `DocxColumnTests`, `OdtColumnTests`,
`RtfColumnTests`, `DocumentLayoutColumnTests`.

### Follow-ups

Three model changes made afterwards, measured the same way:

1. **Rows break across pages.** A row that does not fit is cut cell by cell,
   each cell keeping the lines ending above the page foot, and continues in
   the next column or page. `TableRow.CanSplit` holds whether it may (DOCX
   `w:cantSplit`, ODT `fo:keep-together`). A row whose cell spans rows stays
   whole; a row holding a nested table breaks between its rows since the
   [fourth run](#fourth-ranked-run-backgrounds).
2. **Page anchors.** `DocumentShape.AnchorPage` holds an ODF
   `text:anchor-type="page"` frame's page; both layouts place it there and make
   pages up to it. A text box sized by `fo:min-width` is a frame, and graphic
   styles inherit through `style:parent-style-name`, which paints
   `tdf135056`'s boxes.
3. **Sections.** `DocumentSection` holds a paragraph range with its own columns
   and, where stated, margins. A DOCX section break starts a page unless
   `continuous`; a column break in a multi-column section moves to the next
   column (`ParagraphStyle.ColumnBreakBefore`, read and written by DOCX, ODT,
   RTF and HTML); an ODF `text:section` with columns is a section. Each section
   is laid out in its own columns and margins, and columns (rows included) are
   balanced where the next section follows on the same page.

| Sample | Before | Rows | Page anchors | Sections | LibreOffice |
| --- | ---: | ---: | ---: | ---: | ---: |
| `tdf134965.odt` | 23 (1558.46) | 31 (1178.71) | 31 | 31 | 29 |
| `fdo68607.docx` | 9 (1187.95) | 8 (1144.98) | 8 | 8 | 8 |
| `test_segfault_while_save.docx` | 7 (1247.15) | 7 (1259.16) | 7 | 7 | 8 |
| `tdf135056.odt` | 4 (1446.12) | 4 | 17 (926.21) | 17 | 17 |
| `tdf123163-1.docx` | 12 (1631.69) | 12 | 12 | 12 (1619.51) | 16 |

"Before" is the font-aliases column; the other samples did not move.
`tdf123163-1` stays four pages short: none of its fourteen pictures is in the
package (`docx.image.missing`) and LibreOffice keeps a box of each stated size
(one, 562 × 642 pt, is its third page); the inline ones alone total about
1,840 pt. A section carries its own margins because a first cut squeezed every
section into the last one's 241-point-wide page and drew twenty pages.

Tests: `DocumentLayoutRowSplitTests`, `DocumentLayoutPageAnchorTests`,
`OdtPageAnchorTests`, `DocumentLayoutSectionTests`.

## Third ranked run: marks and bands

Issue 120. Two of the top three were explained by the second run; the third,
`tdf168017.docx`, a 29-page services contract, drew on 33 pages with no header
or footer, from four defects here. Each was checked against the standard and,
where it leaves room, LibreOffice's pages.

### Fixed

1. **Unmapped families use the host fallback face's metrics.** The contract
   is in Arial Narrow, which neither side has; both draw DejaVu Sans, but the
   layout measured it with the estimate (80% ascent, rounded 125% line: 13 pt
   at 10 where the face gives 11.64). The CLI now measures the host fallback
   for unmapped families (`FontResolution.MetricPath`).
2. **An empty paragraph takes its mark's size.** Word states it in
   `w:pPr/w:rPr` (ECMA-376 §17.3.1.29). The contract opens with 36 empty
   10-point paragraphs (about 190 in all), which drawn at the 12-point Times
   New Roman default pushed half of page one onto page two.
   `RichTextParagraph.MarkStyle` holds the mark; edits keep it, DOCX reads and
   writes it, ODT and RTF fill it from the paragraph style and the formatting
   at `\par`, and the layout measures empty lines with it.
3. **Sections inherit headers and footers.** A section without a header or
   footer of some type takes the nearest earlier section's (ECMA-376
   §17.10.5); the contract names them in its first of nine sections only. A
   `first` part counts only with `w:titlePg`, an `even` part only with
   `w:evenAndOddHeaders` in `w:settings` (§17.15.1.35), which keeps the
   contract's lone even page-number footer off. The writer emits
   `w:evenAndOddHeaders` whenever it writes an even part, or Word ignores it.
4. **A band overrunning its margin pushes the text.** The contract's last
   sections put header and footer at 36 pt in 36-pt margins. Per ECMA-376
   §17.6.11 the text moves clear of them, as in Word and LibreOffice. Each page
   sets its bands at its own section's distances and margins; only a band
   taller than half the text height is left out and reported.

### Left open at the time

- **`tdf123163-1.docx`, 12 against 16.** The missing pictures above. Keeping a
  box for an image with no payload is a model change (every writer, the
  resource policy, both layouts), on the [roadmap](../roadmap.md#format-fidelity).
  Its sections differ in paper size by a few twips and the page uses the
  document's, putting `geometry` at `off` by two or three pixels.
- **`tdf163894_hidden.docx`.** LibreOffice 24.2's `STYLEREF` expansion. Fix 3
  raised its score (1464.11 → 1474.11) correctly: it names first and even
  headers without `w:titlePg` or `w:evenAndOddHeaders`, so the default header
  belongs on every page.
- **`tdf168017.docx`, 31 against 29.** Pages one to eight match LibreOffice's
  text, header and footer; from about page eleven it falls behind and reaches
  Exhibit D two pages late. The drift start was not found. The model has no
  list start (`w:start` 4, 9, 12 draw from 1) and picks a marker format from
  the indent (a level-0 clause indented half an inch draws "17." as "a."); list
  numbering is on the [roadmap](../roadmap.md#format-fidelity). The 21 text
  lines are list labels, a projection difference per
  [the office guide](../guides/office-conformance.md#what-it-has-found).

LibreOffice ran under a fontconfig file listing only the Liberation, Carlito,
Caladea, DejaVu and LibreOffice directories (the host's Inter otherwise
replaced Arial Narrow); the base column then reproduces the issue exactly.

| Sample | Base | Fallback metrics | Paragraph mark | Bands read | Bands placed | LibreOffice |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `tdf123163-1.docx` | 12 (1619.51) | 12 (1619.51) | 12 (1619.52) | 12 (1619.52) | 12 (1619.52) | 16 |
| `tdf163894_hidden.docx` | 4 (1464.11) | 4 (1464.11) | 4 (1464.11) | 4 (1474.11) | 4 (1474.11) | 17 |
| `tdf168017.docx` | 33 (1382.71) | 32 (1418.13) | 30 (1369.23) | 30 (1374.23) | 31 (1329.33) | 29 |

The two rises are the next difference coming into view: with correct line
heights the first page still overflowed on empty paragraphs, and read bands
were not drawn until fix 4 placed them.

Two seed-corpus RTF rows now agree better than `tests/office/office-baseline.json`
records and want regenerating on the pinned toolchain (a developer host's
numbers differ within a band):

- `lists-nested/rtf`: ink box `near` → `tight` (8 px, on the boundary; this
  host read base 32 px where the baseline says 19), profile `poor` → `fair`
  (0.744 → 0.962). LibreOffice sets its bullets in unmapped Symbol, measured by
  the estimate until fix 1.
- `letterhead-frames/rtf`: profile `poor` → `fair` (0.863 → 0.962), blur
  `heavy` → `light`, from fix 4. Its worst band stays `wild` on the ink box (the
  page-one footer LibreOffice draws and `\titlepg` suppresses here), so the
  runner ranks it as agreeing less well.

Tests: `FontMetricPathTests`, `ParagraphMarkStyleTests`,
`DocxParagraphMarkTests`, `OdtParagraphMarkTests`, `RtfParagraphMarkTests`.

## Fourth ranked run: backgrounds

Issue 122. Eight of the top ten were new and all were this component: five
page backgrounds, one report in two files whose single row left page one
blank, and an RTF table read as loose text. Each fix was checked against the
specification and LibreOffice's pages.

### Fixed

1. **Page background.** `fill.docx`, `tdf126533_axialAngle.docx` and its twin,
   `tdf159626_blackPatternFill.docx` and `fdo86857.html` drew white where
   LibreOffice paints. `RichTextDocument.PageBackground` holds it, edits carry
   it, both layouts paint it under everything, and each writer writes it back.
   - DOCX reads `w:background` (ECMA-376 §17.2.1) only with
     `w:displayBackgroundShape` (§17.15.1.25), as LibreOffice does; the VML
     fill converts as `oox/source/vml/vmlformatting.cxx` does (angle a quarter
     turn off, linear gradients from `color2`, a focus near 50% mirrored).
   - ODT reads the master page's drawing-page fill, then the page layout's
     `draw:fill`, then `fo:background-color`.
   - RTF reads the `{\*\background}` shape where `\viewbksp1` shows it;
     LibreOffice takes `\viewbksp` for the DOCX switch.
   - HTML reads the canvas colour (CSS 2.1 §14.2): root, then body background,
     then `bgcolor`. LibreOffice flattens a gradient page to one colour in HTML
     and RTF, and the HTML writer does too and says so.
2. **Axial gradients at any angle.** `ShapeFill.Axial` is VML's 50% focus,
   ODF's `axial` and DrawingML's three stops with matching ends. The page
   painter and PDF writer band at the stated angle and clip to the box instead
   of using the nearer axis. ODF puts an axial gradient's start colour in the
   middle (measured in LibreOffice).
3. **VML shapes.** `v:rect`, `v:roundrect`, `v:oval` and a picture-less
   `v:shape` in a `w:pict` are floating shapes: box from CSS `style`, fill as
   above, outline from `strokecolor`, text from `v:textbox`.
   `tdf126533`'s text box is one.
4. **Page-relative vertical offsets.** DOCX `relativeFrom="page"`, ODF
   `style:vertical-rel="page"` and RTF `\shpbypage` set
   `DocumentShape.OffsetFromPage`; both layouts stand the shape on its
   paragraph's page. Found through `tdf123163-1.docx`'s full-page panels.
5. **A row with a nested table breaks between nested rows.** `fdo48718-1.docx`
   and `fdo77887.docx` hold a one-cell row of about 1,100 pt (794 pt of text
   height) with a 76-name nested table; kept whole, it left page one blank.
   ECMA-376 §17.4.6 exempts no such row and LibreOffice breaks it. Both now
   draw on two pages.
6. **`w:color w:val="auto"` overrides the style.** ECMA-376 §17.3.2.6; the
   same report's blue Normal style now draws black, as LibreOffice does.
7. **RTF tables and section page.** `fdo66565.rtf` states its page only with
   `\pgwsxn`, `\pghsxn` and section margins, now read over the document's (RTF
   1.9.1). There was no table reader (`\cell` was a tab, `\row` a
   paragraph); `\trowd` to `\row` now reads into a `DocumentTable`, cells
   closed in their own group (`{\b A1 \cell}`) included, with the grid from every edge any
   row states; LibreOffice's `testFdo66565` asserts the second row's first cell
   is 453 of 14,846 twips.

### Left open at the time

- **`tdf123163-1.docx`, 13 against 16.** The missing pictures. Its panels land
  on the right paragraphs, but from page two those fall a page early, so a grey
  panel stands on LibreOffice's white page and the score rose.
- **`tdf163894_hidden.docx`.** `STYLEREF` again; this host's LibreOffice draws
  40 pages.
- **`fdo48718-1.docx`, `fdo77887.docx`.** Right page count. The table is
  floated with `w:tblpPr` 113 pt down, centred, 504 pt wide against 442 pt of
  text; drawn in flow at text width from about 85 pt, page one ends at name 48,
  not 45. Paragraph borders are not drawn. Both on the
  [roadmap](../roadmap.md#format-fidelity). The text lines are LibreOffice's:
  it imports the floating table as a frame and its text export omits frames.
- **`fill.docx`.** A rectangular gradient from a corner, held as its outer
  colour and reported (`docx.background.fill`).
- **`fdo86857.html`.** Red now; its table cell is unshaded because HTML tables
  flatten to paragraphs.
- **`tdf126533`'s text box** draws black, not white, and without the inset:
  neither the frame's paragraph style nor the inset is read.
- **`fdo66565.rtf`'s 21 text differences** are projections: LibreOffice 26.2
  joins cells with tabs, `broilerdoc dump` writes one cell per line, as 24.2
  did, so the nightly run may not report them.

Measured on Windows with LibreOffice 26.2.6.3 and poppler 24.02. The base
matches the issue to a fifth of a point for the five page-colour documents and
to thirteen points for `tdf123163-1` and the attendance report, but not for
`tdf163894_hidden.docx` or `fdo66565.rtf`. Steps in landing order: background,
nested rows, auto colour, RTF section page, RTF tables, page frame, VML shapes.

| Sample | Base | Moved by | Final | LibreOffice |
| --- | ---: | --- | ---: | ---: |
| `tdf123163-1.docx` | 12 (1629.67) | VML shapes 13 (1646.71) | 13 (1646.71) | 16 |
| `tdf163894_hidden.docx` | 4 (1599.86) | none | 4 (1599.86) | 40 |
| `tdf126533_axialAngle.docx` | 1 (1379.65) | background 723.30; VML 432.74 | 1 (432.74) | 1 |
| `tdf126533_axialAngle2.docx` | 1 (1379.65) | background 723.67; VML 432.61 | 1 (432.61) | 1 |
| `fdo86857.html` | 1 (1350.17) | background 421.06 | 1 (421.06) | 1 |
| `fill.docx` | 1 (1350.00) | background 453.34 | 1 (453.34) | 1 |
| `tdf159626_blackPatternFill.docx` | 1 (1349.57) | background 203.92 | 1 (203.92) | 1 |
| `fdo48718-1.docx` | 3 (1338.27) | nested rows 2 (1049.74); auto colour 1050.93 | 2 (1050.93) | 2 |
| `fdo77887.docx` | 3 (1335.41) | nested rows 2 (1045.60); auto colour 1047.01 | 2 (1047.01) | 2 |
| `fdo66565.rtf` | 1 (1039.19) | section page 664.21; tables 521.95 | 1 (521.95) | 1 |

The auto-colour rise is black ink in the wrong place weighing more than blue;
the VML rise on `tdf123163-1` is panels on paragraphs already a page early.

Four `DocxAnchorFrameTests` cases now assert a page-relative offset is measured
from the page (the report stays for mirrored margins). The new RTF table and
VML readers were run (`broilerdoc info`, `dump`, `render`, two-minute limit)
over every RTF and DOCX sample and fuzzed file in `sw/qa/extras`, 2,367 files
including the 23 crash regressions: all ended in a documented exit code, none
hung.

Tests: `DocxPageBackgroundTests`, `OdtPageBackgroundTests`,
`HtmlPageBackgroundTests`, `RtfPageBackgroundTests`, `PageBackgroundTests`,
`DocumentPageBackgroundTests`, `DocxVmlShapeTests`, `RtfShapeFrameTests`,
`RtfTableTests`, `RtfPageGeometryTests`.

## Threshold reporting

Changed 2026-10-23. Issue eligibility now follows the executed checks' failure
verdicts (`ExtrasDifference` keeps them) rather than requiring every
descriptive band to be best, so passing documents with non-best bands or
informational diagnostics are no longer counted. All-page threshold decisions
are kept rather than recomputed from the reported worst page. Failed reads and
renders stay eligible, including renderer failures without measurements;
skipped checks and informational diagnostics are not failures. Crash inputs
and shared refusals keep their own handling, and severity still orders the
eligible failures. Markdown and JSON reports record the configured threshold
(default 80%, overridable through runner options). Reporting-policy tests in
`Broiler.Documents.Office.Tests` run without LibreOffice.

An authored ellipse document at 72 DPI (98.58% similarity, matching pages and
geometry) passes all three checks at threshold 80 with an empty ranking despite
non-best bands; at 100 only the pixel check fails and it is ranked. Reports:
`%TEMP%/broiler-threshold-ellipse-80.json`,
`%TEMP%/broiler-threshold-ellipse-100.json`.

Replaying the saved artifacts of five runs (no rerendering) through the new
eligibility code gives counts of documents with failed checks:

| Issue | GitHub run | Original | Corrected |
| --- | --- | ---: | ---: |
| #135 | [39758202870](https://github.com/Broiler-Platform/Broiler.Documents/actions/runs/39758202870) | 3,443 | 2,466 |
| #136 | [39784497730](https://github.com/Broiler-Platform/Broiler.Documents/actions/runs/39784497730) | 3,271 | 2,217 |
| #137 | [39826675837](https://github.com/Broiler-Platform/Broiler.Documents/actions/runs/39826675837) | 3,260 | 2,015 |
| #138 | [39861440382](https://github.com/Broiler-Platform/Broiler.Documents/actions/runs/39861440382) | 3,260 | 1,996 |
| #139 | [39930307683](https://github.com/Broiler-Platform/Broiler.Documents/actions/runs/39930307683) | 3,253 | 1,968 |

The top 30 and its order are unchanged in all five, and eleven shared refusals
stay excluded. Existing issues were not edited.

## Triage

Focused triage and control-group replay, verified 2026-10-25 on main
`f06830b`; no codec or baseline tolerance changed. Commands and limits are in
[the guide](../guides/office-conformance.md#focused-triage-and-repeatable-control-groups).

The control group is the authored `typed-letters.html`,
`multi-paragraph-item.html` and `direct-centered-block.html` probes from
issue #147, comparing the build before its last HTML fixes (`five-before-bin`) with
merged main, on LibreOffice 26.8.1.1 and poppler 26.05 at 72 DPI (80%
threshold, 40-page limit, 30-second reference timeout, fonts not pinned):

| Control | Before | After | Triage result |
| --- | --- | --- | --- |
| Typed letters | Text failed; read/pixel passed | 3/3 passed | One fixed text check |
| Multi-paragraph list item | Text failed; read/pixel passed | 3/3 passed | One fixed text check |
| Direct centered block | 3/3 passed | 3/3 passed | Measurements changed; no newly passing check |

The comparison recorded equal input hashes and environment, no missing
controls and no regressions (7/9 → 9/9), keeping improved measurements apart
from newly passing checks. A remote exact selection of
`ooxmlexport/data/tdf126533_noPageBitmap.docx` downloaded one file without a
sparse checkout or Trees API enumeration and passed both semantic-axis checks.
The original 30 issue #147 sample IDs are in
[the control list](../../tests/office/selections/issue-147.txt). Focused runs
do not update the global ranking issue. The workflow's tool approval register,
baseline classification and pixel threshold are unchanged.
