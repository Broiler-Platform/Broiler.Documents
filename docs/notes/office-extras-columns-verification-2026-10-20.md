# The second `sw/qa/extras` report: columns, table styles and breaks — 2026-10-20

The second ranked run over LibreOffice's `sw/qa/extras` tree (issue 118) was the
first with the fuzzer files set aside, so its top ten are real documents. Each
was ranked on a page-count mismatch. Six of them were this component reading or
laying the document out wrongly, in four ways, and those are fixed. The harness
was also measuring several of them with the wrong fonts, and that is fixed too.
The other four are differences this component cannot close without a construct
the model does not have yet, or are LibreOffice's own, and the note says which
below rather than leaving a page count to imply otherwise.

## What was wrong here

1. **A DOCX table style's paragraph and run properties were ignored.**
   `fdo68607.docx` is a glossary laid out as one long table under
   `Medium Shading 1 Accent 1`, and `test_segfault_while_save.docx` a worksheet
   under `Table Grid`. Both styles state `w:spacing w:after="0" w:line="240"`,
   which takes Word's default ten points after and 1.15 line back out of every
   cell; read without the style, every cell kept the body's spacing. ECMA-376
   §17.7.2 puts the table style between the document defaults and the paragraph
   style, and LibreOffice's writerfilter applies it there
   (`DomainMapperTableHandler::ApplyParagraphPropertiesFromTableStyle`). The
   reader now does too; banding and conditional formatting are still not
   applied, and `docx.table.style` says so.
2. **The model had no text columns.** `many-paragraphs-per-page-in-6-columns.odt`
   states six columns on its page layout and `fdo43573-2-min.docx` two on its
   section, and both were laid out as one wide column. `PageGeometry` now carries
   a column count and gap, the DOCX, ODT and RTF codecs read and write them, and
   the pagination layout fills the columns in turn. `fdo43573` needed one more
   decision: its producer ended the two columns with a continuous section break
   before one empty paragraph, so the last section - the one the page size comes
   from - has one column and holds nothing. The columns are taken from the
   section holding the most paragraphs.
3. **A column break with no column to move to was a line break.**
   `FDO77715.docx` opens each of its blocks with `w:br w:type="column"` in a
   section of one column. ECMA-376 §17.18.3 says such a break restarts the text
   on the next page, and Word and LibreOffice both start one; this reader read
   it as a line break, and every block ran on from the last. It is now a page
   break exactly where the section it is in has one column. Which section that
   is has to be found before the content is read, because a section's properties
   follow its paragraphs.
4. **A page break that opened its paragraph was demoted to a line break.**
   `FDO77715`'s breaks are the first thing in their paragraphs, and so are the
   page breaks before `wDateValueFormat.docx`'s headings. The builder demoted
   any break with text after it, because a paragraph here cannot be split
   across a page boundary - but a break with nothing before it splits nothing,
   and the whole paragraph belongs on the new page. It now starts that page.

Each was checked against a reference rather than against LibreOffice alone: the
standard's text for the first three, and for the fourth the same rule this
reader already applies to a break at the end of a paragraph, seen from the
other side.

## What the harness measured wrongly

The ranking pins the families its seed corpus names - the Liberation faces and
DejaVu Sans - and nothing else. Documents from Word ask for Times New Roman,
Arial and Calibri. LibreOffice finds none of them and fontconfig's metric aliases
hand it Liberation Serif, Liberation Sans and Carlito, which the workflow
installs for exactly that reason; `broilerdoc` was given no such mapping and drew
every one of them with its host fallback. `fdo43573` is set in Times New Roman,
and was measured as DejaVu Sans against Liberation Serif: wider glyphs, more
lines, more pages. The runner now maps the five metric-compatible pairs
fontconfig itself uses (Times New Roman, Arial and Courier New to the Liberation
faces; Calibri to Carlito; Cambria to Caladea). A family the corpus pins keeps
its own face, so the seed corpus is unaffected; the run below confirms it.

This moves numbers in both directions, and that is the point of it. With the
faces matched, `test_segfault_while_save` and `wDateValueFormat` now render a
page or two *short* of LibreOffice instead of long, because the differences that
remain in them are no longer hidden behind the wider fallback.

## What was not wrong here

- **`tdf163894_hidden.docx`, 4 pages against 17.** The headers hold
  `STYLEREF "Main Index Entry"` fields. The LibreOffice this suite pins (24.2)
  expands each one to the whole paragraph the style is used in, so its header
  holds the document's entire text in small print and the body gets four lines a
  page. LibreOffice has since fixed that, and the test that came with the fix
  (`testTdf163894_hidden` in `sw/qa/extras/ooxmlexport/ooxmlexport14.cxx`)
  checks the headers of four pages, each holding two single words. This component
  draws the field results Word cached when it saved, on four pages; it does not
  evaluate `STYLEREF` per page, so its headers show the cached words rather than
  each page's own.
- **`tdf134965.odt`, 23 pages against 29.** A two-column table of comments on a
  draft law, some of whose rows are longer than a page. LibreOffice breaks such a
  row across pages; the layout here places a row whole, so a row taller than the
  page runs off its bottom and the pages its continuation would have filled are
  never made. That is a table layout this component does not have, and the
  [roadmap](../roadmap.md#test-suites-and-tooling) carries it. The document's two
  footnotes are skipped as `odt.note`; the model has no footnotes.
- **`tdf123163-1.docx`, 12 pages against 16.** A magazine laid out in
  thirty-one sections: eight of them start a new page, and eleven are in two,
  three or four columns. Section breaks do not paginate here and the model holds
  one page and one set of columns, so the document is laid out as one flow in
  the columns of the section that holds the most paragraphs. The floating
  pictures and the exact row heights it is also read with
  (`docx.image.shape`, `docx.table.rowheight`) add to the gap.
- **`tdf135056.odt`, 4 pages against 17.** A magazine template whose text is
  cut down to single letters. Six of its frames are anchored to a page by number
  (`text:anchor-type="page"`, `text:anchor-page-number` 1, 4, 5 and 17), and
  LibreOffice makes pages until there is a seventeenth to put the last one on.
  The model anchors a shape to a paragraph only, so those frames are placed
  against the paragraph they are read beside, as [notes/odt.md](odt.md) already
  says, and nothing asks for a seventeenth page. A page anchor is a model
  change that reaches both layouts and every writer, and the frame on page 17
  states its width only as the text box's `fo:min-width`, which is not read
  either; both are on the [roadmap](../roadmap.md#format-fidelity) rather than
  in this change.

## Measured

The conformance runner was run over just these ten files, at the resolution and
with the LibreOffice and poppler versions the issue names, with the workflow's
font directory. Each column is a separate run on the same host; only the build,
and for the last column the harness's font mapping, differ. The first column
reproduces the issue's numbers exactly.

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

Pages `broilerdoc` rendered, with the severity score in brackets where it
moved. Six of the ten moved toward LibreOffice's page count; with the codec
fixes alone, `FDO77715` and `wDateValueFormat` landed on it, and `FDO77715` stays
there once the fonts match. `tdf123163-1` moved away once the fonts matched:
drawn in Liberation and Carlito its text takes fewer lines, and what is left of
the gap is the sections it cannot lay out, above.

## Validation

New regressions: `DocxTableStyleTests` (9 cases), `DocxColumnTests` (26),
`OdtColumnTests` (12), `RtfColumnTests` (8) and `DocumentLayoutColumnTests` (10).
Run against the base commit's readers, six of the table-style cases fail; the
column cases need the new model properties to compile. The full solution passes:
2,895 tests across the fourteen test projects, 2,892 passing and three skipped
as before. The corpus suite passes 2320/2320.

The office suite's seed corpus, run with the workflow's font arguments on a host
whose font directory holds more than the pinned set, fails the same 47 of 779
checks on the base commit and with these changes, with and without the font
aliases - the identical rows, 46 of them pixel rows that the extra fonts account
for and the forty-seventh `text/lists-nested/rtf` - so nothing here moves the
seed baseline.
