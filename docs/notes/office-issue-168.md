# Issue #168: a form template's sections, hidden text and page fields

The first of the [30 ranked differences in issue #168](https://github.com/Broiler-Platform/Broiler.Documents/issues/168)
was `uiwriter/data/fdo114306_2.odt`: broilerdoc drew eleven pages where
LibreOffice draws four (severity 1570.77). It is a Moscow-government order
template OpenOffice.org 2.2 saved in 2008: a dozen text sections under two page
styles, non-printing editing aids, an approval sheet that restarts the page
count, and hidden text written the way OpenOffice.org 2.x wrote it. The path and
input SHA-256 are in `tests/office/selections/issue-168.txt` and
`tests/office/selections/issue-168.sha256`. The document is not vendored.

Locally all three of its checks now pass: read, text and pixel (78.48% before
the last two corrections below, then over the 80% threshold), at four pages
against four.

## Corrections

Each follows what LibreOffice does, checked against its source or by probing it
with altered copies of the sample.

- **Text sections follow on.** The ODT reader turned every stretch between two
  section edges into a section that starts a page. Only a paragraph or table
  whose style names a master page opens one now. This alone took the sample
  from eleven pages to four.
- **What a template keeps off the page.** A drawing whose style says
  `style:print-content="false"` is laid out and kept clear of but not printed
  (`DocumentShape.Printable`, written back; `LayoutSettings.DrawNonPrintingShapes`
  lets an editing host show it). The sample's crest and heading frame go.
- **Form labels.** A `draw:control` placing a `form:fixed-text` is a shape holding
  its label in the control's text style; `form:printable="false"` makes it
  non-printing. The sample's "Разослать:" prints; its editing hints do not.
- **Section margins are indents.** ODF section `fo:margin-left`/`-right` apply on
  any page (`DocumentSection.LeftIndent`, `RightIndent`); they were page margins
  that a section following on a page ignored. The title is set in the left half
  again.
- **Table spacing and splitting.** `fo:margin-top`/`-bottom` on a table are its
  spacing (`DocumentTable.SpacingBefore`, `SpacingAfter`), read, written and laid
  out; the signature stands 2.2 cm below the text. `style:may-break-between-rows="false"`
  keeps a table together, and a nested table kept with what follows is held to it.
- **Cells.** An ODF cell that states no padding has none, where Word's 5.4 pt
  was used. Text that runs past a cell is cut at its edge, as Writer cuts a text
  portion; a line holding a picture is drawn whole.
- **Page fields.** `text:page-number` and `text:page-count` are runs that keep
  their saved value and say which field they are (`InlineStyle.PageField`). A
  header or footer draws each page's own value; `style:page-number` restarts the
  count (`DocumentSection.PageNumberStart`); a page count comes from a second
  pass; a number adjusted to a page that is not there shows nothing. The sample's
  headers read "- 2 -" on its second and fourth pages, as LibreOffice's do,
  where both said the saved "3". Typing beside a field does not extend it.
- **Hidden text from OpenOffice.org 2.x.** That release wrote `text:display`
  inverted. LibreOffice recognises it by the build id in `meta:generator` and
  reads it the other way round (`txtimppr.cxx`); so does the reader now. A
  paragraph whose mark and text are all hidden takes no room, in every format;
  its page break still applies. The sample's "Скрытый текст" (hidden text) digits
  and the empty paragraph that starts its approval sheet disappear.
- **Fixed header and footer bands.** `svg:height` on `style:header-footer-properties`
  is a fixed band whose height already holds the spacing below it
  (`PageGeometry.FixedHeaderHeight`, `FixedFooterHeight`). The body is not moved
  for its content, and content that overruns it is cut at the band.
- **Hidden sections in running content.** A `text:section text:display="none"` in a
  header or footer is left out; the sample's file-name footer goes.
- **The first master page.** The text of a deletion kept ahead of the body in
  `text:tracked-changes`, or of a note or comment, no longer chooses the master
  page the document starts on (`tdf130680.odt` lost a footer LibreOffice never draws).
- **Text projection.** `dump --as text` leaves out text in shapes, as
  LibreOffice's text export walks the body alone (`wrtasc.cxx`), except for a
  body of one empty paragraph, which exports its first text box. The
  [issue #136 frames note](office-issue-136-floating-frames-textboxes-verification-2026-10-22.md)
  had added shape text and recorded the text checks it broke. The
  [office-conformance guide](../guides/office-conformance.md) already said the
  text projection carries no shape.

## Verification

The unit suite passes: **4,879 passed, three skipped**, with regression tests for
each correction above in the Model, ODT, Pagination and CLI suites, including an
end-to-end flat-ODT form template laid out through the paginator.

The local comparison could not use the CI's prepared references, whose toolchain
stamp is Linux. Instead every corpus sample was rendered and text-dumped with an
isolated build of `468ce62` and with this change; 563 samples differ in either.
LibreOffice references were generated for exactly those, and both builds were
scored against them with the same fonts and settings.

| Axis | Before | After |
| --- | ---: | ---: |
| Text checks passed | 197 | 469 |
| Pixel checks passed | 320 | 332 |
| All checks passed (of 1,660) | 1,080 | 1,364 |
| Summed severity | 316,293 | 296,111 |

Of 563 samples, 343 have a lower severity and 18 a higher one. One check that
passed fails: the pixel check of `uiwriter/data/tdf130680.odt` (78.84%). Its pages
now start exactly where LibreOffice's do and lose the footer LibreOffice does not
draw; what remains is a table taller than LibreOffice's, whose embedded objects
are not decoded, which the wrong footer had been offsetting in the score.

| Sample | Severity before → after | Pages (LibreOffice) | Checks now |
| --- | ---: | --- | --- |
| `uiwriter/data/fdo114306_2.odt` | 1213.2 → 467.0 | 10 → 4 (4) | all pass |
| `ooxmlexport/data/ooo96040-2.odt` (#2) | 1361.8 → 982.2 | 20 → 12 (13) | read |
| `layout/data/tdf119875.odt` | 1140.5 → 643.9 | 9 → 3 (3) | all pass |
| `ooxmlexport/data/tdf101589_dontSplitTable.odt` | 802.4 → 2.0 | 2 → 2 (2) | all pass |

Higher severities, none of them a failing check that passed before:

- `layout/data/U-min.fodt` (+810), `layout/data/table-0-height-rows.fodt` (+64)
  and `layout/data/footer-fly-table.fodt` (+23) have a "continued on page N"
  footer (`text:page-adjust="1"`). Their content depends on conditional
  sections and `text:hidden-paragraph`, which are not evaluated, so they lay out
  on two pages where LibreOffice has one, and the first page now shows the
  "2" a two-page document would.
- `uiwriter/data/tdf39828.fodt` (+203), `cjk/data/tdf115079.odt` (+131),
  `layout/data/min-wtf.rtf` (+45) and `ooxmlexport/data/cell-grid-span.docx`
  (+44, two pages to one against LibreOffice's two after a hidden empty
  paragraph stops taking a line) move within failing pixel checks; the others
  rise by less than six.

These are each run's worst-page observations.

## Environment and reproduction

Windows, LibreOffice 26.2.6.3, Poppler 24.04.0 (pdftoppm), 96 DPI, 80%
threshold, the Liberation, Carlito, Caladea and DejaVu faces pinned on both
sides. The issue uses Linux, LibreOffice 24.2.7.2 and the `office-references-v2`
archive, whose pinned fonts are Carlito, Caladea and DejaVu only.

```powershell
dotnet test Broiler.Documents.slnx
dotnet run --project src/tests/Broiler.Documents.Office -- `
  --extras-dir <sw/qa/extras samples> --sample-list tests/office/selections/issue-168.txt `
  --font-dir <font dir> --dpi 96 --allow-pending-tools
```

Follow-up work this leaves open is in the [roadmap](../roadmap.md): page fields
in DOCX and RTF, a field's number format from its page style, and conditional
visibility.
