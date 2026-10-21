# The fourth `sw/qa/extras` report: page backgrounds, VML and RTF tables — 2026-10-21

The fourth ranked run over LibreOffice's `sw/qa/extras` tree (issue 122) put ten
documents at the top. Two were the ones the
[third report](office-extras-marks-and-bands-verification-2026-10-20.md)
explained. Eight were new, and every one of them was this component's reading
or layout. Five were a page LibreOffice paints that the model had nowhere to
hold, two were one report whose single table row left page one blank, and one
was an RTF table read as loose text. Those are fixed, along with three things the
fixes brought into view. The rest of this note says what is left and why.

## What was wrong here

1. **The model had no page background.** `fill.docx`, `tdf126533_axialAngle.docx`
   and its twin, `tdf159626_blackPatternFill.docx` and `fdo86857.html` are each a
   page LibreOffice paints - a corner gradient, a mirrored diagonal one, black, red -
   and `broilerdoc` drew white, which the comparison scores as a blank page against
   an inked one. `RichTextDocument.PageBackground` now holds it, every edit
   carries it, and both layouts paint it under everything else on every page.
   - DOCX reads `w:background` (ECMA-376 §17.2.1) only where `w:settings` holds
     `w:displayBackgroundShape` (§17.15.1.25), which is the switch LibreOffice's
     import also waits for. Its VML fill is converted the way LibreOffice converts
     it (`oox/source/vml/vmlformatting.cxx`): the angle a quarter turn from the
     model's, a linear gradient starting from `color2`, a focus near 50% mirrored.
   - ODT reads the master page's drawing-page fill, then the page layout's
     `draw:fill`, then its `fo:background-color`. LibreOffice writes all three.
   - RTF reads the shape in `{\*\background}` where `\viewbksp1` shows it;
     LibreOffice takes `\viewbksp` for the DOCX switch.
   - HTML reads the canvas colour (CSS 2.1 §14.2): the root's background, or the
     body's, with `bgcolor` beneath both.
   Each writer writes it back. HTML and RTF can carry a gradient in principle, but
   LibreOffice writes a gradient page to both as one flattened colour, so the HTML
   writer does the same and says so.
2. **A gradient had no mirrored form, and a diagonal one was drawn along an
   axis.** `tdf126533`'s pages are VML gradients with a focus of 50%: fuchsia in two
   corners, lime along the diagonal between them. `ShapeFill.Axial` holds that.
   It is VML's 50% focus, ODF's `axial` and DrawingML's three stops whose ends
   agree. The page painter and the PDF writer banded every gradient along the axis
   nearer its angle, so a diagonal became a stripe across the page. Both now band
   at the stated angle and clip to the box. ODF puts an axial gradient's *start*
   colour along the middle, which was measured in LibreOffice rather than taken on
   trust.
3. **A VML shape was read as a picture or as nothing.** `tdf126533`'s text box
   is a `v:rect` in a `w:pict`, Word's pre-2010 way of writing one, and the
   reader looked in a `w:pict` for a picture only, so the box and its word went
   together. `v:rect`, `v:roundrect`, `v:oval` and a `v:shape` holding no picture
   are now floating shapes: the box from the CSS `style`, the fill as above, the
   outline from `strokecolor`, the text from `v:textbox`.
4. **A shape measured from the page was hung from its paragraph.** Reading those
   shapes brought this into view on `tdf123163-1.docx`, which hangs a full-page
   panel at nought from the page's top off the first paragraph of each page. Every
   format can say this (DOCX's `relativeFrom="page"`, ODF's
   `style:vertical-rel="page"`, RTF's `\shpbypage`). But the page a paragraph lands
   on is a layout result, so the DOCX reader kept the offset and measured it from
   the paragraph, and the ODT and RTF readers did not read the frame at all.
   `DocumentShape.OffsetFromPage` now holds it, and both layouts stand the shape
   on its paragraph's page.
5. **A table row holding a nested table was kept whole.** `fdo48718-1.docx` and
   `fdo77887.docx` are one report: a table of one row whose one cell holds the
   whole page, with a nested table of 76 names. The row is about 1,100 points on
   a page with 794 for text. A row breaks across pages, but one holding a nested
   table was kept whole, so it moved past the opening empty paragraph to page two
   and page one was left blank. ECMA-376 §17.4.6 exempts no such row, and
   LibreOffice breaks it between the nested rows. Such a row now breaks between
   the nested table's rows, and both documents draw on two pages, as LibreOffice
   draws them.
6. **`w:color w:val="auto"` was ignored.** The same report's Normal style is blue
   and every run says `auto`. ECMA-376 §17.3.2.6 makes `auto` a colour, which
   LibreOffice draws black; it now overrides the style's colour.
7. **RTF had no tables, and ignored a section's page.** `fdo66565.rtf` states its
   page only with `\pgwsxn`, `\pghsxn` and the section margins, so it had none and
   was drawn portrait. Its table closes every cell inside a group of its own,
   `{\b A1 \cell}`. But there was no table reader for that to defeat: `\cell` was
   a tab and `\row` a paragraph. The first section's page words are now read over
   the document's (RTF 1.9.1). `\trowd` to `\row` is read into a `DocumentTable`,
   with the grid built from every edge any row states. That grid is what
   LibreOffice's own `testFdo66565` asserts: the second row's first cell is 453 of
   its 14,846 twips.

Items 5 to 7 were found and fixed in parallel with 1 to 4 and are separate
commits; each was checked against the specification and against LibreOffice's
pages.

## What was not wrong here, or not yet

- **`tdf123163-1.docx`, 13 pages against 16.** Still the fourteen pictures that
  are not in its package, which LibreOffice keeps as grey boxes and this reader
  keeps nothing of, as the second and third reports found. The panels it now
  reads are drawn on the right paragraphs, but from its second page those
  paragraphs land a page earlier than LibreOffice puts them. So a grey panel
  stands where LibreOffice has a white page, and the score rose with it, as the
  table below shows: one defect was flattering the measurement of another.
- **`tdf163894_hidden.docx`.** LibreOffice's `STYLEREF` expansion, as before. This
  host's LibreOffice draws it on 40 pages, the issue's on 17.
- **`fdo48718-1.docx` and `fdo77887.docx`** are on the right number of pages, and
  the rest is the table model. The table is floated with `w:tblpPr` 113 points
  down the page and centred, and it is 504 points wide against a text width of
  442. This layout draws it in the flow, at the text width, from about 85 points,
  so page one ends at name 48 rather than 45. Its paragraph borders are not drawn
  either. Both are on the [roadmap](../roadmap.md#format-fidelity). The text
  comparison's lines are LibreOffice's: it imports the floating table as a frame,
  and its plain-text export leaves frames out.
- **`fill.docx`** is a rectangular gradient from a corner, which two stops along
  a line cannot draw. It is held as its outer colour, which is most of the page,
  and reported (`docx.background.fill`). Fills the model cannot hold are on the
  roadmap.
- **`fdo86857.html`** is now red. Its one table cell is not shaded, because the
  HTML reader flattens tables to paragraphs. That is already on the roadmap.
- **`tdf126533`'s text box** draws its word in black where LibreOffice draws it in
  white, and without the text box's inset. Neither the frame's paragraph style nor
  a text box inset is read.
- **`fdo66565.rtf`'s 21 text differences on this host are the projections.**
  This host's LibreOffice, 26.2, joins a table row's cells with tabs in its text
  export, where `broilerdoc dump` writes a cell's paragraph per line - the
  difference the [office guide](../guides/office-conformance.md#what-it-has-found)
  already classifies. The issue's LibreOffice 24.2 wrote this file one cell per
  line, which is what the dump writes now, so the nightly run may not report them.

## Measured

The conformance runner was run over the ten documents at the issue's resolution
on a Windows host, with LibreOffice 26.2.6.3 (the issue's is 24.2.7.2 on Linux)
and poppler 24.02, so the numbers are this host's. The base column matches the
issue's severities to a fifth of a point for the five page-colour documents and
to thirteen points for `tdf123163-1` and the attendance report. It does not
match for `tdf163894_hidden.docx`, which this LibreOffice draws on 40 pages, or
for `fdo66565.rtf`, whose text projection this LibreOffice writes differently.
Each column is a run of the build after that commit, in the order they land.

| Sample | Base | Background | Nested rows | Auto colour | RTF section page | RTF tables | Page frame | VML shapes | LibreOffice |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `tdf123163-1.docx` | 12 (1629.67) | 12 (1629.67) | 12 (1629.67) | 12 (1629.67) | 12 (1629.67) | 12 (1629.67) | 12 (1629.67) | 13 (1646.71) | 16 |
| `tdf163894_hidden.docx` | 4 (1599.86) | 4 (1599.86) | 4 (1599.86) | 4 (1599.86) | 4 (1599.86) | 4 (1599.86) | 4 (1599.86) | 4 (1599.86) | 40 |
| `tdf126533_axialAngle.docx` | 1 (1379.65) | 1 (723.30) | 1 (723.30) | 1 (723.30) | 1 (723.30) | 1 (723.30) | 1 (723.30) | 1 (432.74) | 1 |
| `tdf126533_axialAngle2.docx` | 1 (1379.65) | 1 (723.67) | 1 (723.67) | 1 (723.67) | 1 (723.67) | 1 (723.67) | 1 (723.67) | 1 (432.61) | 1 |
| `fdo86857.html` | 1 (1350.17) | 1 (421.06) | 1 (421.06) | 1 (421.06) | 1 (421.06) | 1 (421.06) | 1 (421.06) | 1 (421.06) | 1 |
| `fill.docx` | 1 (1350.00) | 1 (453.34) | 1 (453.34) | 1 (453.34) | 1 (453.34) | 1 (453.34) | 1 (453.34) | 1 (453.34) | 1 |
| `tdf159626_blackPatternFill.docx` | 1 (1349.57) | 1 (203.92) | 1 (203.92) | 1 (203.92) | 1 (203.92) | 1 (203.92) | 1 (203.92) | 1 (203.92) | 1 |
| `fdo48718-1.docx` | 3 (1338.27) | 3 (1338.27) | 2 (1049.74) | 2 (1050.93) | 2 (1050.93) | 2 (1050.93) | 2 (1050.93) | 2 (1050.93) | 2 |
| `fdo77887.docx` | 3 (1335.41) | 3 (1335.41) | 2 (1045.60) | 2 (1047.01) | 2 (1047.01) | 2 (1047.01) | 2 (1047.01) | 2 (1047.01) | 2 |
| `fdo66565.rtf` | 1 (1039.19) | 1 (1039.19) | 1 (1039.19) | 1 (1039.19) | 1 (664.21) | 1 (521.95) | 1 (521.95) | 1 (521.95) | 1 |

Pages `broilerdoc` rendered, with the severity score in brackets. Two moves go
the wrong way, and both are the measurement seeing more. The auto colour turns
the report's text from blue to black without moving it, and black ink in the
wrong place weighs more than blue. The VML shapes put `tdf123163-1`'s panels on
paragraphs that are already a page early.

## Validation

New regressions: `DocxPageBackgroundTests` (18), `OdtPageBackgroundTests` (10),
`HtmlPageBackgroundTests` (11), `RtfPageBackgroundTests` (6),
`PageBackgroundTests` (4), `DocumentPageBackgroundTests` (7),
`DocxVmlShapeTests` (6), `RtfShapeFrameTests` (7), `RtfTableTests` (12),
`RtfPageGeometryTests` (6 more), with cases added to `DocxAnchorFrameTests`,
`OdtShapeTests`, `DocumentLayoutPageAnchorTests`, `DocumentLayoutRowSplitTests` and
`DocxReaderStyleTests`. Four `DocxAnchorFrameTests` cases asserted that a
page-relative vertical offset was kept and reported. They now assert that it is
measured from the page, and the report is kept for the mirrored margins. The full
solution passes: 3,115 tests across the fourteen test projects, 3,112 passing
and three skipped as before. The corpus suite passes 2320/2320.

Because the RTF table reader and the VML reader are new code reading foreign
input, `broilerdoc info`, `dump` and `render` were run over every RTF and DOCX
sample in the `sw/qa/extras` tree and all of its fuzzed files - 2,367 in all,
the 23 crash regressions among them - with a two-minute limit on each. Every
one ended in a documented exit code, and none hung.

The office seed corpus was not run against its baseline here. The baseline is
stamped with the workflow's toolchain, and a run on this host's LibreOffice and
fonts skips rather than compares. The nightly run will report it.
