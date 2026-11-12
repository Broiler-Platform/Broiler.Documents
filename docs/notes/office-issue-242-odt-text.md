# Office issue #242: ODT fields, cell values, header spacing and custom shapes

I reproduced each sample on Linux at 96 dpi with the `broilerdoc-office` runner, the
LibreOffice 24.2.7.2 reference archive (`--accept-reference-mismatch`) and its font
directory. Where I needed to know how Writer behaves beyond the reference, I converted
cut-down documents with the local LibreOffice 24.2.7.2 (`soffice --convert-to txt` or
`pdf`, en-US system locale, as the references were made) and read the text, or the
positions with `pdftotext -bbox`. Similarity is the runner's pixel score.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `odfimport/data/fdo60842.odt` | The table's cells hold values (`office:date-value`, `office:value`) and empty paragraphs, and one cell holds `office:string-value="USD"` over a paragraph reading "This text should not appear". Writer shows a value cell's value through the cell's data style in place of the cached text, and a string value in place of the paragraph. The date style is `number:automatic-order="true" number:format-source="language"` in fr-LU: Writer replaces it with the locale's system short date, `DD/MM/YYYY` (`aDefaultDateFormats` in xmloff's `xmlnumfi.cxx`). The number style groups thousands with fr-LU's U+00A0. soffice experiments set the limits: a cell of one paragraph of plain text or spans is replaced (its paragraph style and first span kept); several paragraphs, a field or a frame are kept; a cell with no data style or a text style keeps its text; a General cell of value 0 keeps its text, empty or not. | **Fixed.** `OdtDataStyles` formats number, currency (with `style:map` sections), percentage, boolean and date styles; `OdtTableReader.DisplayCellContent` applies the rules above. A style without a language formats in the system locale (the application's culture, en-US when invariant). The locale table holds en-US, en-GB, de-DE, de-AT, fr-FR, fr-LU (and hu-HU numbers), each read back from soffice; any other locale, a General number, a formula cell, scientific or fraction numbers and automatically ordered date-times keep the cached text. Text check fail → pass; pixel pass before and after. |
| `ooxmlexport/data/ooo96040-2.odt` | (1) The header's `text:modification-date` caches "10. Nov 2008" (the document was German). Its style N75 states day, ". ", short textual month, " ", long year, with `number:automatic-order="true"` and no language. Writer evaluates the field from `meta.xml`'s `dc:date` and, as the style has no language, formats it in the system locale; automatic order maps day/short textual month/long year to the locale's `DMMMYYYY` format, en-US "MMM D, YYYY": "Nov 10, 2008". (2) The header style has `fo:min-height="0cm"` and `fo:margin-bottom="0.499cm"`; the one-line header grew past its least height, and the layout only cleared the line, so every page's body sat 0.499 cm too high (see pagecounttest.ott). Left: the header paragraph's 0.002 cm bottom border is drawn too thin to show, the document has line numbering (`text:linenumbering-configuration` without `text:number-lines="false"`), which the ODT reader does not read, and the headings' outline numbering and spacing differ. | **Partly fixed.** (1) Date fields are formatted from their value: a fixed `text:date` from `text:date-value`, a document date (`text:creation-date`, `text:modification-date`, `text:print-date`) from `meta.xml` unless fixed (`OdtReader.DateFieldText`). A `text:date` that is not fixed keeps its cached text: it shows the day the document is opened. Text check fail → pass. (2) is the header fix below. 42.1% → 57.3%. |
| `mailmerge/data/pagecounttest.ott` | (1) The header's spacing, as in ooo96040-2.odt. LibreOffice starts "&lt;Salutation&gt;" at 116 px, broilerdoc drew it at 97 px. (2) The section's custom shapes, a four-pointed star (`star4`, its inner corners equations of `draw:modifiers`), a five-pointed star and a cube, were drawn as filled boxes: the reader knew only ellipses and diamonds. | **Fixed.** (1) `PageGeometry.HeaderSpacing` and `FooterSpacing` hold an ODF band's `fo:margin-bottom` / `fo:margin-top` (not for a fixed band or `style:dynamic-spacing`), and `DocumentLayout.FrameFor` adds it to the band's content when the content grows past the band's least height. A soffice test on a header of two paragraphs with 1 cm below each showed the band ends at its last line: the last paragraph's space below is not part of it, in a header or a footer, whatever `AddParaTableSpacing` says (`DocumentStyleDefaults.RunningBandsEndAtLastLine`, set for ODT). (2) `OdtCustomShapePath` evaluates `draw:enhanced-path` with its equations and modifiers into closed polygons when the path is straight lines (M, L, Z, N), `DocumentShape.OutlinePaths` carries them and `DocumentPagePainter` fills them by ear clipping and strokes them. A path with curves or a value it cannot evaluate keeps the box. Writer shades the cube's faces, which this does not. 65.1% → pass. |
| `uiwriter/data/tdf130685.odt` | As recorded in the issue #240 note (`office-issue-240-odt-layout.md`): the list labels of deleted list items that tracked deletions re-insert at a shared mark. I looked at the three lines again; each is the last item of a one- or two-item deleted list that the next deletion joins, and the rule that decides their label is the one the #240 note left untraced (`XMLRedlineImportHelper`). | **Not fixed.** No change. 71.8%. |

The mirrored shape. With polygons drawn, `ooxmlexport/data/flip_and_rotate.odt` (a bent
arrow, `draw:mirror-vertical="true"`, turned by 135°) opened to the right where Writer's
opens upwards: Writer mirrors a turned shape on the page, not in its own box. The reader
turns a mirrored outline back by twice the angle about the box's centre, which once the
painter turns the shape gives the page's mirror. The sample passes before and after.

Data styles in `styles.xml`. A currency style's `<number:text> </number:text>` is the
space between "€" and the number, and `styles.xml` was read without its white space, so
`uiwriter/data/tdf122756.odt` showed "€100,00". `styles.xml` is now read with white space,
which is then dropped everywhere but in paragraph content and in `number:text` and
`number:currency-symbol`. 22 samples in the archive have such white space.

## Effect on other samples

I ran every ODT, OTT and FODT sample in the archive with a reference that exercises the
changed code, before and after, at 96 dpi with every check: value cells and date fields
(37), white space in `styles.xml` (22), header and footer spacing (80 and 65), any header
or footer (171), custom shapes with an enhanced path (61) and line numbering (4): 254
samples in all.

- Pixel checks went from 180 to 186 passing, and text checks from 213 to 216. No check that
  passed before fails now.
- Text fixed: `fdo60842.odt`, `ooo96040-2.odt` and `odfimport/data/incorrectsum.odt`
  (a currency style with no language of its own shows "2,580 €" in en-US, not the cached
  "2.580 €").
- Pixel fixed: `pagecounttest.ott`, `mailmerge/data/tdf128148.odt`,
  `ooxmlexport/data/tdf142404_tabOverSpacingC15.odt`, `uiwriter/data/tdf112741.fodt`,
  `uiwriter/data/tdf168355.odt` and `uiwriter/data/variable-field-table-row-split-header.fodt`.
- Better, still failing: `layout/data/table-0-height-rows.fodt` 50.4 → 90.0% (it fails on
  its page count), `ooo96040-2.odt` 42.1 → 57.3%,
  `ooxmlexport/data/tdf142361ShadowDirection.odt` 62.8 → 71.8%, `ooxmlexport/data/tdf146491.odt`
  76.9 → 78.1%, `uiwriter/data/shape-textbox-autosize.odt` 38.3 → 45.6%.
- Worse, still failing: `uiwriter/data/shape-textbox-vertadjust.odt` 59.1 → 51.8%. Its
  triangle is drawn as a triangle now, but without fill: its graphic style's parent is the
  Writer frame style "Frame", which states `draw:fill="none"`, and LibreOffice fills the
  shape with the default drawing style's blue regardless. Before, its box outline happened
  to cover more of that blue. `odfimport/data/tdf146597.odt` 65.8 → 63.7%: its flowchart
  shapes are drawn as their outlines, but its group sits in the wrong place, as before.
- `layout/data/tdf134472.odt` failed (77.3%) with the header spacing alone, because its
  header's paragraphs have 1 cm below them. With the band ending at its last line it passes
  again, as it did before.

## Verification

- `dotnet build Broiler.Documents.slnx -c Release`: no errors. The one warning, xUnit2030
  in `DocumentLayoutIssue236Tests.cs`, was there before and is in a file this change does
  not touch.
- Test projects run, all passing: Broiler.Documents.Odt.Tests (716),
  Pagination.Tests (451), Model.Tests (343), Cli.Tests (357), Docx.Tests (1,029),
  Rtf.Tests (653), Office.Tests (99), Ods.Tests (16) and Broiler.Documents.Tests (329).
- `OdtIssue242OdtTextTests` (Broiler.Documents.Odt.Tests), 22 cases: a date cell in fr-LU's
  short date; grouped fr-LU numbers; a string value over the cell's text; a style without
  a language in the system locale, with text around the number and a minus; a currency
  style's mapped sections; a zero General cell keeping its label; cells of several
  paragraphs or a field kept; an unknown locale and a formula cell keeping the cache; a
  modification date from `meta.xml` and without one; fixed dates in stated order and in
  six locales' automatic order; a style with no built-in counterpart; white space in a
  `styles.xml` data style.
- `OdtNumericCellTests` asserted three rules no sample stood behind (a zero General cell
  shown as "0", explicit decimals kept as cached, spans kept). They now assert what soffice
  shows.
- `OdtIssue242OdtTextLayoutTests` (Broiler.Documents.Cli.Tests), 8 cases, read and laid
  out end to end: a header growing past its least height keeping its spacing; one within
  it leaving the body at the margin; the band ending at its last line; dynamic spacing;
  `star4`'s polygon from its equations and modifier, carried to the page; a mirrored,
  turned shape; a curved path and an unknown modifier keeping the box.
