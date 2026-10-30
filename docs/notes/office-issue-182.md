# Issue #182: a magazine laid out from line feeds and the wrong section's margins

First of the [30 ranked differences in issue #182](https://github.com/Broiler-Platform/Broiler.Documents/issues/182):
`layout/data/tdf123163-1.docx` (severity 1205.69), a sixteen-page magazine
converted from a PDF into thirty-one sections of one to four columns, its
backdrops and panels VML and DrawingML shapes placed from the page, none of
its fourteen pictures in the package. 16 pages against 16, but worst-page
blur difference 48.6% and 21 text lines different, each a LibreOffice line
`broilerdoc` broke into several ("xxx xx xxxxxx xxxxxx xx", "2", "xxxx xxx …"
against one). Path and input SHA-256: first line of
`tests/office/selections/issue-182.txt` and
`tests/office/selections/issue-182.sha256`, which also list the samples this
change moved most; not vendored.

Seven earlier notes (#135, #136, #145, #147, the columns, marks-and-bands and
backgrounds reports) blamed the missing pictures, unequal columns and
sections. The causes are none of those; all are `broilerdoc`'s, and the first
two reach other documents.

## 1. A line feed in run text is a space

Every `w:t` starts with a line feed:

```xml
<w:r>…<w:t xml:space="preserve">
xxx xx xxxxxx xxxxxx xx</w:t></w:r>
<w:r>…<w:vertAlign w:val="subscript"/>…<w:t xml:space="preserve">
2</w:t></w:r>
```

Kept as a line break, it made each run begin a line: the 21 text
differences, a taller title, longer paragraphs throughout. LibreOffice 24.2
(`writerfilter/source/ooxml/OOXMLFastContextHandler.cxx`,
`OOXMLFastContextHandler::text`) turns every line feed in run text into a
space (tdf#108806: "CRLFs in XML were converted to \n before this point. These
must be converted to spaces"), and where `xml:space="preserve"` is not in
scope also trims XML white space at both ends and turns tabs into spaces
(tdf#108995); `IsPreserveSpace` inherits the attribute from the nearest
ancestor stating it. Word agrees. `DocxValueReader.RunText` does both, for
`w:t`, `w:delText` and a custom note mark's text.

Of the 1,807 corpus DOCX samples, four have line feeds in run text (this one,
`tdf108806.docx`, `tdf134021.docx`, `tdf135774_numberingCRProps.docx`) and six
unpreserved edge white space or tabs (among them `xml_space.docx`,
LibreOffice's own test of the rule).

## 2. A shape placed from the page, in a section with other margins

`DocumentShape.OffsetX` is measured from the text column, so the reader turns
an offset from the page (`relativeFrom="page"`, VML's
`mso-position-horizontal-relative:page`, the margins) into one from the column
by subtracting the page's left margin. It used the last section's (5510 twips,
275.5 pt), while the layout adds back that of the page the shape is drawn on,
the first section's 134.9 pt. So the full-page backdrop (573 pt wide, at
nought) stood 140.6 pt left of the page, and section 4's grey page covered
only the left half (0 to 298 pt against 4.8 to 569 pt).

LibreOffice lays a continuous section out in the page style of the section
that opened the page, keeping its margins (the layout already does,
`ColumnsOf`): section 3 states margins of 59 and 169.6 pt, yet its text stands
within the first section's, 134.9 to 470 pt. `DocxSections.CurrentPage` now
follows the last section that started a page (moved on by `Advance`), and
`DocxReadContext.PlacementPage` gives that page, or the document's outside the
body, to every conversion: horizontal and vertical frames, VML alignment,
diagram blocks, and nested text box and note reads. Eleven corpus samples have
such a shape in a document whose sections differ in left margin.

## 3. A row of exact height a frame leaves no room beside

The title's paragraph has an exact line height of 98.4 pt; its missing
picture, wrapped on both sides, is anchored on it and ends 252.5 pt down the
page, 98.9 pt below the top margin. `broilerdoc` started the first row at the
picture's foot. LibreOffice fills a row a frame leaves no room beside with an
empty one and tries the next, "by units of line height"
(`SwTextFormatter::CalcFlyWidth`): row 2 starts at 252.0 pt, half a point
inside the picture, so the title stands on row 3 at 350.4 pt under shading
spanning the empty rows; 26.2.6.3 draws exactly that (glyphs 309.8 to
456.3 pt). The layout now measures an exact-height paragraph by rows while
wrapping, rounds a move past a frame up to whole rows
(`DocumentLayout.ExactLineSkip`) and shades the skipped rows. A line running
on to the next column leaves its skip behind.

## 4. An empty paragraph under its own backdrop

Section 4 is one empty paragraph holding four shapes, the first a grey page
(564.5 by 759.35 pt, square wrap) taller than the text area. With the
backdrop in place the empty line moved below it, off the page, and a blank
page followed: 17 pages against 16. LibreOffice keeps such a paragraph on its
page (a 26.2 probe with "HELLO WORLD" added moves it on, as `broilerdoc`
does). The layout now leaves an empty paragraph first in its column on its
page when its own shapes wrap square and none keeps text above and below it.
A first cut ignoring the wrap made `tdf138039.odt` lose a page: there a
page-tall picture wrapping text above and below is anchored on an empty
paragraph, and LibreOffice moves the line on to a third page ("also empty
lines need to avoid a Fly with no wrapping", `SwTextFormatter::CalcFlyWidth`).

## 5. Spaces that open a paragraph

With line feeds read as spaces every paragraph opens with one, and
LibreOffice sets it (the title's "x" stands 35 pt right of the margin). The
line wrapper now drops leading white space only on a row the text wrapped onto
(the space the line broke at); a paragraph's first row and a row opened by a
forced break keep theirs. Tabs are unchanged.

## Verification

Tests: `DocxRunTextWhitespaceTests`, `DocxSectionShapePlacementTests`,
`DocumentLayoutBlockedLineTests`, `DocumentLayoutLeadingSpaceTests`. A
`DocxImageTests` fixture wrote `<w:t>before </w:t>` without
`xml:space="preserve"`, whose space Word and LibreOffice drop; it was fixed.

All 3,907 samples, `76e9234` (main) → this change: text passes 3,499 →
3,503, pixel and read unchanged, none lost. Newly passing: this sample,
`tdf108806.docx` (LibreOffice's test of the line feed rule),
`tdf135774_numberingCRProps.docx`, `tdf87533_bidi.docx`. Of 30 samples whose
worst-page metrics moved, 24 came closer, among them both
`tdf165359_Sdt*.docx` (blur difference 7.6% → 0.0%), `tdf123705.docx` (32.8%
→ 29.7%) and `xml_space.docx`. Six moved away by at most 0.8 points, none
crossing the threshold: `imgshadow.docx` (below),
`table-row-data-displayed-twice.docx` (10.6% → 11.0%),
`tdf135774_numberingCRProps.docx` (profile closer, 0.71 → 0.76),
`tdf119952_negativeMargins.docx`, `tdf121045.docx`, `tdf112448.odt` (profile
0.89 → 1.00).

The ranked sample: severity 1204.67 → 919.63, text check passes, worst-page
blur difference 48.5% (page 3) → 3.6% (page 2); mean pixel difference at 48
dpi on page 3 32.6 → 3.8, page 1 22.2 → 14.6.

## Not changed, seen on the way

- **Page 2 stays the worst page.** LibreOffice (24.2 and 26.2) starts page 2
  with section 3's third paragraph though page 1 has room: the next paragraph
  anchors a 569 by 304 pt square-wrapped backdrop 371 pt down, leaving its own
  text no room on page 1, and both move on. The LibreOffice rule taking the
  earlier one along was not traced; `broilerdoc`'s single forward pass keeps
  both on page 1 (on the [roadmap](../roadmap.md)). Its pixel similarity
  (15.9%) is this page's profile.
- **The title's font.** Liberation Sans Narrow is not in the reference fonts:
  the reference drew DejaVu Sans (the PDF's font list) and wrapped "x xxxx" on
  two rows, `broilerdoc` the host fallback; LibreOffice 26.2, which has the
  face, keeps one row. With `--font-file "Liberation Sans
  Narrow=…/DejaVuSans.ttf"` `broilerdoc` matches the reference's title. See
  the [workflow note](office-conformance-workflow.md#issue-182).
- **Missing pictures** are drawn on purpose as a grey outline with a rule
  across (`DocumentPagePainter.DrawMissingImage`); the reference draws nothing,
  and on sparse pages the outlines are much of the differing ink.
- **Inline pictures' effect extents.** `imgshadow.docx` opens with a space
  before two shadowed pictures; setting it moved them 3 pixels right (blur
  difference 9.6% → 10.4%, still passing). LibreOffice also widens each by its
  `wp:effectExtent`, which the reader ignores: 614 pixels at 96 dpi against
  586. A roadmap item.
- **Section paper sizes** differ by a few twips; the page is drawn at the
  document's, as the roadmap says.

## Environment and reproduction

- References `office-references-v3` (LibreOffice 24.2.7.2, pdftoppm 24.02, 96
  dpi, reference fonts) at `D:/scratch/office-references-v3`; LibreOffice
  26.2.6.3 on Windows for the probes.
- Replay:
  `dotnet run --project src/tests/Broiler.Documents.Office -c Release -- --tool <broilerdoc> --extras-dir <refs>/inputs --references-dir <refs> --accept-reference-mismatch --pdftoppm <pdftoppm> --font-dir <refs>/fonts/0 --dpi 96 --sample-list tests/office/selections/issue-182.txt --report <out>/report.json --workspace <dir> --allow-pending-tools`.
- Positions from `pdftotext -bbox` on the reference and 26.2's PDF; rules from
  the 24.2 sources (`OOXMLFastContextHandler.cxx`, `itrform2.cxx`,
  `txtfly.cxx`, `oox/source/vml/vmlshape.cxx`). Converting runs of sections
  with both renderers found which section moved.
