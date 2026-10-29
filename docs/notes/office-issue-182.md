# Issue #182: a magazine laid out from line feeds and the wrong section's margins

The first of the [30 ranked differences in issue #182](https://github.com/Broiler-Platform/Broiler.Documents/issues/182)
was `layout/data/tdf123163-1.docx` (severity 1205.69): a sixteen-page
magazine converted from a PDF into thirty-one sections of one to four columns,
its backdrops and panels VML and DrawingML shapes placed from the page, none
of its fourteen pictures in the package. Pages matched (16 against 16), but
the worst page's blur difference was 48.6% and the text check failed with 21
lines, each a LibreOffice line that `broilerdoc` had broken into several
("xxx xx xxxxxx xxxxxx xx", "2", "xxxx xxx …" against one line).

Its path and input SHA-256 are the first line of
`tests/office/selections/issue-182.txt` and `tests/office/selections/issue-182.sha256`,
which also list the samples whose renders this change moved most. No document
is vendored.

Seven earlier notes had looked at this sample (#135, #136, #145, #147, the
columns, marks-and-bands and backgrounds reports) and put its gap down to the
missing pictures, unequal columns and sections. The causes below are none of
those. Every one is `broilerdoc`'s, and the first two reach other documents.

## 1. A line feed in run text is a space

Every `w:t` in the document starts with a line feed:

```xml
<w:r>…<w:t xml:space="preserve">
xxx xx xxxxxx xxxxxx xx</w:t></w:r>
<w:r>…<w:vertAlign w:val="subscript"/>…<w:t xml:space="preserve">
2</w:t></w:r>
```

The reader appended the text as it stood, and the model reads a line feed as
a line break, so each run began a line: the 21 text differences, a taller
title, and paragraphs a line or more longer throughout.

LibreOffice 24.2 (`writerfilter/source/ooxml/OOXMLFastContextHandler.cxx`,
`OOXMLFastContextHandler::text`) replaces every line feed in run text with a
space (tdf#108806: "CRLFs in XML were converted to \n before this point. These
must be converted to spaces"), and where `xml:space="preserve"` is not in scope
it also trims XML white space from both ends and turns tabs into spaces
(tdf#108995). `IsPreserveSpace` inherits the attribute from the nearest
ancestor that states it. Word shows the same. `DocxValueReader.RunText` does
both, for `w:t`, `w:delText` and a custom note mark's text.

A scan of the corpus' 1,807 DOCX samples finds line feeds in run text in four
(this one, `tdf108806.docx`, `tdf134021.docx`,
`tdf135774_numberingCRProps.docx`) and unpreserved edge white space or tabs in
six (`xml_space.docx`, LibreOffice's own test of the rule, among them).

## 2. A shape placed from the page, in a section with other margins

`DocumentShape.OffsetX` is measured from the text column, so the reader turns
an offset stated from the page (`relativeFrom="page"`, VML's
`mso-position-horizontal-relative:page`, the margins) into one from the column
by taking off the page's left margin. It took the document's page, the last
section's, whose left margin here is 5510 twips (275.5 pt); the layout then
adds back the margin of the page the shape is drawn on. The first section's is
134.9 pt, so its full-page backdrop (573 pt wide, at nought) stood 140.6 points
left of the page and ended 140 points short of the right edge, and section 4's
grey page covered the left half only (0 to 298 pt against 4.8 to 569 pt).

What the column is depends on the page: LibreOffice lays a continuous section
out inside the page style of the section that opened the page, keeping that
page's margins (the layout already does the same, `ColumnsOf`). The
reference confirms it here: section 3 states margins of 59 and 169.6 pt, and
its text stands between the first section's, 134.9 to 470 pt. The section
that opened the page is known when the shape is read, so
`DocxSections.CurrentPage` follows it - the last section that started a page,
moved on by `Advance` - and `DocxReadContext.PlacementPage` gives that page,
or the document's where the read is not the body's, to every conversion:
horizontal and vertical frames, VML alignment, diagram blocks, and the nested
reads of text boxes and notes.

Eleven corpus samples have such a shape in a document whose sections differ
in left margin.

## 3. A row of exact height a frame leaves no room beside

The title's paragraph has an exact line height of 98.4 pt, and its missing
picture, wrapped through on both sides, is anchored on it and ends 252.5 pt
down the page, 98.9 points below the top margin. `broilerdoc` started the first row at the
picture's foot. LibreOffice fills a row a frame leaves no room beside with an
empty one and tries the next below it, "by units of line height"
(`SwTextFormatter::CalcFlyWidth`); the second row starts at 252.0 pt, half a
point inside the picture, so the title stands on the third, at 350.4 pt, and
the paragraph's shading reaches up over the two empty rows. LibreOffice
26.2.6.3 draws exactly that (the title's glyphs between 309.8 and 456.3 pt).

The layout now measures a paragraph of exact height by its rows while
wrapping, rounds a move past a frame up to a whole number of rows
(`DocumentLayout.ExactLineSkip`), and the shading covers the rows a line
skipped. A line that runs on to the next column leaves its skip behind.

## 4. An empty paragraph under its own backdrop

Section 4 is one empty paragraph holding four shapes, the first a grey page
(564.5 by 759.35 pt, square wrap) taller than the page's text area. With the
backdrop now where it belongs it covered the column, the empty line moved
below it, off the page, and a blank page followed: 17 pages against 16.
LibreOffice keeps such a paragraph on its page; an authored probe on 26.2 with
"HELLO WORLD" added to the paragraph moves it on, as `broilerdoc` does. The
layout now leaves an empty paragraph first in its column on its page when its
own shapes wrap square and none keeps text above and below it. The first cut
of the rule ignored the wrap and made `tdf138039.odt` lose a page: there a
page-tall picture whose wrapping keeps text above and below it is anchored on
an empty paragraph, and LibreOffice moves the empty line on to
a third page. Its source says the same ("also empty lines need to avoid a Fly
with no wrapping", `SwTextFormatter::CalcFlyWidth`).

## 5. Spaces that open a paragraph

With the line feeds read as spaces, every paragraph opens with one, and
LibreOffice sets it: the title's "x" stands 35 points right of the margin.
The line wrapper dropped white space at the start of every row. It now drops
it only on a row the text wrapped onto, where the space is the one the line
broke at; a paragraph's first row and a row a forced break opened keep theirs.
Tabs are unchanged.

## Verification

Replayed against `office-references-v3` (LibreOffice 24.2.7.2, pdftoppm 24.02,
96 dpi, the reference fonts) with `broilerdoc` built from `76e9234` (main) and
from this change, over all 3,907 samples:

| Checks | Before | After |
| --- | ---: | ---: |
| Pixel passed / failed / skipped | 2,613 / 1,242 / 52 | 2,613 / 1,242 / 52 |
| Text passed / failed / skipped | 3,499 / 363 / 45 | 3,503 / 359 / 45 |
| Read passed / failed | 3,873 / 11 | 3,873 / 11 |

No check that passed fails. Four text checks pass that failed: this sample,
`tdf108806.docx` (LibreOffice's own test of the line feed rule),
`tdf135774_numberingCRProps.docx` and `tdf87533_bidi.docx`. Of the 30 samples
whose worst-page metrics moved, 24 came closer to the reference, among them
both `tdf165359_Sdt*.docx` (blur difference 7.6% -> 0.0%), `tdf123705.docx`
(32.8% -> 29.7%) and `xml_space.docx`. Six moved away, by at most 0.8 points
of blur difference and each still on the same side of the threshold:
`imgshadow.docx` (see below), `table-row-data-displayed-twice.docx`
(10.6% -> 11.0%), `tdf135774_numberingCRProps.docx` (whose profile came
closer, 0.71 -> 0.76), `tdf119952_negativeMargins.docx`, `tdf121045.docx` and
`tdf112448.odt` (profile 0.89 -> 1.00). `tests/office/selections/issue-182.txt`
lists these samples.

The ranked sample, on this host:

| | Before | After |
| --- | ---: | ---: |
| Severity | 1204.67 | 919.63 |
| Pages | 16 / 16 | 16 / 16 |
| Text check | 21 differences | passes |
| Worst page, blur difference | 48.5% (page 3) | 3.6% (page 2) |
| Page 3, mean pixel difference at 48 dpi | 32.6 | 3.8 |
| Page 1 | 22.2 | 14.6 |

`dotnet test Broiler.Documents.slnx -c Release`: 5,037 passed, 3 skipped (the guards that run only in the aggregate repository). One fixture changed: `DocxImageTests` wrote `<w:t>before </w:t>` without `xml:space="preserve"`, whose space Word and LibreOffice drop. New tests:
`DocxRunTextWhitespaceTests`, `DocxSectionShapePlacementTests`,
`DocumentLayoutBlockedLineTests` and `DocumentLayoutLeadingSpaceTests`; run
against `76e9234`'s readers and layout, the cases that pin a change fail.

## Not changed, seen on the way

- **Page 2 stays the worst page.** LibreOffice, 24.2 and 26.2 alike, starts
  page 2 with section 3's third paragraph although page 1 has room for it.
  The paragraph after it anchors a 569 by 304 pt backdrop, square wrap, 371 pt
  down the page, which leaves its own text no room on page 1, and both
  paragraphs move on; the rule in LibreOffice's object positioning that takes
  the earlier one along was not traced. `broilerdoc` lays the page out in one
  forward pass and keeps both on page 1.
  Its pixel similarity (15.9%) is the profile of this page.
- **The title's font.** The paragraph is set in Liberation Sans Narrow, which
  the reference fonts do not hold. The reference's LibreOffice drew it in
  DejaVu Sans (the PDF's font list) and wrapped "x xxxx" on two rows;
  `broilerdoc` drew the host fallback, and LibreOffice 26.2, which has the
  face, keeps one row. Given `--font-file "Liberation Sans Narrow=…/DejaVuSans.ttf"`,
  `broilerdoc` draws page 1's title as the reference does. See the workflow
  note.
- **Missing pictures** are drawn as a grey outline with a rule across, on
  purpose (`DocumentPagePainter.DrawMissingImage`); the reference draws
  nothing for them. On this sample's sparse pages those outlines are much of
  the ink that differs.
- **Inline pictures' effect extents.** `imgshadow.docx` opens its paragraph
  with a space before two shadowed pictures. Setting the space moved the
  pictures 3 pixels right (blur difference 9.6% -> 10.4%, still passing);
  LibreOffice also widens each picture by its `wp:effectExtent`, which the
  reader does not read, so its pictures take 614 pixels at 96 dpi to
  `broilerdoc`'s 586. Now a roadmap item.
- **Section paper sizes** differ by a few twips and the page is drawn at the
  document's, as the roadmap says.

## Environment and reproduction

- Reference set `office-references-v3`, unpacked at
  `D:/scratch/office-references-v3`; LibreOffice 26.2.6.3 on Windows for the
  probes.
- Replay:
  `dotnet run --project src/tests/Broiler.Documents.Office -c Release -- --tool <broilerdoc> --extras-dir <refs>/inputs --references-dir <refs> --accept-reference-mismatch --pdftoppm <pdftoppm> --font-dir <refs>/fonts/0 --dpi 96 --sample-list tests/office/selections/issue-182.txt --report <out>/report.json --workspace <dir> --allow-pending-tools`.
- Positions came from `pdftotext -bbox` on the reference and on 26.2's PDF;
  LibreOffice's rules from the 24.2 sources (`OOXMLFastContextHandler.cxx`,
  `itrform2.cxx`, `txtfly.cxx`, `oox/source/vml/vmlshape.cxx`). The document
  was cut into runs of sections and each run converted by both renderers to
  find which section moved.
