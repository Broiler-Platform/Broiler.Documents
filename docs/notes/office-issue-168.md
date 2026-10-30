# Issue #168: a form template's sections, hidden text and page fields

First of the [30 ranked differences in issue #168](https://github.com/Broiler-Platform/Broiler.Documents/issues/168):
`uiwriter/data/fdo114306_2.odt` (severity 1570.77), eleven pages against
LibreOffice's four. It is a Moscow-government order template OpenOffice.org
2.2 saved in 2008: a dozen text sections under two page styles, non-printing
editing aids, an approval sheet that restarts the page count, and hidden text
written the OpenOffice.org 2.x way. Path and input SHA-256:
`tests/office/selections/issue-168.txt` and
`tests/office/selections/issue-168.sha256`; not vendored.

Locally all three checks now pass (read, text, pixel), four pages against four.

## Corrections

Each follows LibreOffice, checked against its source or by probing altered
copies of the sample.

- **Text sections follow on.** The ODT reader made every stretch between two
  section edges a section starting a page. Only a paragraph or table whose
  style names a master page opens one now; this alone took the sample from
  eleven pages to four.
- **Non-printing drawings.** A drawing whose style says
  `style:print-content="false"` is laid out and kept clear of but not printed
  (`DocumentShape.Printable`, written back; `LayoutSettings.DrawNonPrintingShapes`
  lets an editing host show it). The crest and heading frame go.
- **Form labels.** A `draw:control` placing a `form:fixed-text` is a shape
  holding its label in the control's text style; `form:printable="false"`
  makes it non-printing. "Разослать:" prints; the editing hints do not.
- **Section margins are indents.** ODF section `fo:margin-left`/`-right` apply
  on any page (`DocumentSection.LeftIndent`, `RightIndent`), not as page
  margins a following-on section ignored.
- **Table spacing and splitting.** `fo:margin-top`/`-bottom` on a table are its
  spacing (`DocumentTable.SpacingBefore`, `SpacingAfter`), read, written and
  laid out. `style:may-break-between-rows="false"` keeps a table together, and
  a nested table kept with what follows is held to it.
- **Cells.** An ODF cell stating no padding has none (Word's 5.4 pt was used).
  Text running past a cell is cut at its edge, as Writer cuts a text portion;
  a line holding a picture is drawn whole.
- **Page fields.** `text:page-number` and `text:page-count` are runs keeping
  their saved value and naming their field (`InlineStyle.PageField`). A header
  or footer draws each page's own value; `style:page-number` restarts the count
  (`DocumentSection.PageNumberStart`); a page count comes from a second pass; a
  number adjusted to a page that is not there shows nothing. Headers now read
  "- 2 -" on pages 2 and 4, not the saved "3". Typing beside a field does not
  extend it.
- **Hidden text from OpenOffice.org 2.x.** That release wrote `text:display`
  inverted; LibreOffice recognises it by the build id in `meta:generator` and
  reads it the other way round (`txtimppr.cxx`), as the reader now does. A
  paragraph whose mark and text are all hidden takes no room, in every format;
  its page break still applies. The "Скрытый текст" digits and the empty
  paragraph opening the approval sheet disappear.
- **Fixed header and footer bands.** `svg:height` on
  `style:header-footer-properties` is a fixed band already holding the spacing
  below it (`PageGeometry.FixedHeaderHeight`, `FixedFooterHeight`). The body is
  not moved for its content; overrunning content is cut at the band.
- **Hidden sections in running content.** A `text:section
  text:display="none"` in a header or footer is left out (the file-name footer).
- **The first master page.** Deleted text kept ahead of the body in
  `text:tracked-changes`, or a note or comment, no longer chooses the starting
  master page (`tdf130680.odt` lost a footer LibreOffice never draws).
- **Text projection.** `dump --as text` leaves out shape text, as LibreOffice's
  text export walks the body alone (`wrtasc.cxx`), except a body of one empty
  paragraph, which exports its first text box. The
  [issue #136 frames note](office-issue-136.md#floating-frames-and-text-box-content)
  had added shape text and recorded the text checks it broke; the
  [office-conformance guide](../guides/office-conformance.md) already said the
  text projection carries no shape.

## Verification

The CI's prepared references carry a Linux toolchain stamp, so every corpus
sample was rendered and dumped with `468ce62` and with this change; the 563
samples differing in either got host-made LibreOffice references:

- text checks passed 197 → 469, pixel 320 → 332, all 1,080 → 1,364 of 1,660;
  summed severity 316,293 → 296,111; 343 samples lower, 18 higher;
- one check lost: the pixel check of `uiwriter/data/tdf130680.odt` (78.84%).
  Its pages now start where LibreOffice's do and drop the footer LibreOffice
  does not draw; what remains is a table taller than LibreOffice's (embedded
  objects not decoded), which the wrong footer had offset in the score.

Notable samples (severity, pages against LibreOffice's):

- `uiwriter/data/fdo114306_2.odt`: 1213.2 → 467.0, 10 → 4 (4), all pass.
- `ooxmlexport/data/ooo96040-2.odt` (#2): 1361.8 → 982.2, 20 → 12 (13), read
  passes.
- `layout/data/tdf119875.odt`: 1140.5 → 643.9, 9 → 3 (3), all pass.
- `ooxmlexport/data/tdf101589_dontSplitTable.odt`: 802.4 → 2.0, 2 (2), all
  pass.

Higher severities, none a lost check (worst-page observations):

- `layout/data/U-min.fodt` (+810), `layout/data/table-0-height-rows.fodt`
  (+64), `layout/data/footer-fly-table.fodt` (+23): a "continued on page N"
  footer (`text:page-adjust="1"`) whose content depends on conditional
  sections and `text:hidden-paragraph`, not evaluated; they lay out on two
  pages against one, so page 1 now shows "2".
- `uiwriter/data/tdf39828.fodt` (+203), `cjk/data/tdf115079.odt` (+131),
  `layout/data/min-wtf.rtf` (+45), `ooxmlexport/data/cell-grid-span.docx`
  (+44; two pages to one against LibreOffice's two, once a hidden empty
  paragraph stops taking a line) move within failing pixel checks; the rest by
  less than six.

## Environment and reproduction

Windows, LibreOffice 26.2.6.3, Poppler 24.04.0 (pdftoppm), 96 DPI, 80%
threshold, Liberation, Carlito, Caladea and DejaVu pinned on both sides (the
issue used Linux, LibreOffice 24.2.7.2 and `office-references-v2`, pinning
Carlito, Caladea and DejaVu only). Replay: `dotnet run --project
src/tests/Broiler.Documents.Office -- --extras-dir <samples> --sample-list
tests/office/selections/issue-168.txt --font-dir <fonts> --dpi 96
--allow-pending-tools`.

Open, in the [roadmap](../roadmap.md): page fields in DOCX and RTF, a field's
number format from its page style, and conditional visibility.
