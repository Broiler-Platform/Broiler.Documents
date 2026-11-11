# Office issue #238: RTF page borders, WordArt, ruby, external pictures, group text boxes; HTML page and face

Seven samples, five RTF and two HTML. I reproduced each one locally on Windows at 96 dpi
with `broilerdoc-office --sample-list`, the `office-references` archive (LibreOffice
24.2.7.2, `--accept-reference-mismatch`) and its font directory. Where a sample was
rendered with `broilerdoc render --font-dir` alone, I rasterised the reference PDF with
`pdftoppm -r 96`. Ink is the count of pixels darker than 128 on a page, listed as
broilerdoc / LibreOffice. A grey difference is the mean absolute difference against
the LibreOffice raster, in grey levels.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `rtfexport/data/page-border.rtf` | `\pgbrdrt\brdrs\brdrw10\brsp480` and the same for the left, bottom and right sides, each a different width. The RTF reader read no page border, so the page showed only "hello". LibreOffice reads these into the same page-border properties as DOCX's `w:pgBorders` (`rtfdispatchflag.cxx`, `RTFBorderState::PAGE`): `\brsp` is the side's space in points, measured from the text unless bits 5 to 7 of `\pgbrdropt` are 1, which measures it from the page edge. | **Fixed.** The new `RtfPageBorders` reads the four sides and their border words into `PageGeometry.Borders`/`BorderInsets`, which the painter already draws for ODT. Each side keeps its own width and colour. The line sits its space outside the text and is drawn outward. `\sectd` clears the border, a later section's border is not read (as for the rest of that section's page), and a paragraph border word after a page border closes the page border's side. Ink: 117 → 6836, against 5876, with the same box (63,63 to 754,993). The pixel check now passes. |
| `rtfimport/data/watermark.rtf` | Word's `PowerPlusWaterMarkObject`, a header shape with `fGtext 1`, `gtextUNICODE` "ASAP", `gtextFont` DejaVu Sans, `rotation` 315° and a half-transparent fill. The reader ignored the WordArt properties, so it drew the shape as a filled rotated box across the middle of the page. In a WordArt shape the fill is the colour of the letters, not of a box behind them. | **Improved.** A shape with `fGtext` now draws its `gtextUNICODE` lines in `gtextFont`, coloured with the fill and its opacity, and centred in the box with no fill and no line (`rtf.shape.wordart`). This is how the DOCX reader already draws VML WordArt. A reader has no font metrics, so the size comes from 0.7 em per character, about the advance of a capital in a wide sans face. At the DOCX reader's 0.5 em, "ASAP" wrapped and lost its "P". Grey difference: 21.38 → 8.58. Blur difference: 21.4% → 9.2%. `rtfexport/data/watermark.rtf`, the only other corpus sample with `fGtext`, went from 4.36 to 1.49. **Not done:** LibreOffice stretches each glyph to fill the box, and it places the watermark about 250 px higher than the page centre where broilerdoc puts it. LibreOffice 26.2.6.3 does the same. Its centre sits where a box centred on the page and then rotated about its top-left corner would put it, but I did not confirm that rule on another sample, so the reader's rotation about the centre is kept. |
| `htmlimport/data/emojis16BE.html` | Not a decoding fault: the UTF-16BE file decodes correctly, BOM and surrogate pairs included, and the text check passed. The pixel check failed because LibreOffice lays out HTML that states no page on its `HTML` page style (2 cm at the sides, 1 cm at the top and bottom, `RES_POOLPAGE_HTML`) and sets text that names no family in its default serif. broilerdoc used the render's 1 inch margin and `sans-serif`. The emoji are a third difference: LibreOffice's Liberation Serif `.notdef` draws nothing, and Broiler.Graphics draws a `?` for an unmapped glyph. | **Fixed (pixel check passes).** The new render option `--default-margin` replaces the 1 inch fallback for a document that states no page and leaves a stated page alone. `--margin` still overrides every document's page. The office runner passes `--default-margin 1cm,2cm --font serif` for HTML samples (`ExtrasConformance.HtmlPageArguments`), as it pins fonts, and a document's own `@page` still wins on both sides. Over all 37 HTML samples in the reference archive, the pixel check went from 6 to 30 passing, with no sample regressing. A first try with `--margin` overrode `@page` and broke four samples that had passed (`fdo81276.html`, `meta-changedby.html`, `meta-ISO8601-dates.html`, `text-align-start-end.html`). The `?` glyphs remain, and they belong to Broiler.Graphics. |
| `htmlimport/data/tdf155011.html` | As in #236: three nested tables and a form holding one `<input type="radio"/>`. LibreOffice draws it as an 11 px form-control circle at 81,83. The HTML reader skips every `input`, so broilerdoc's page is blank. | **Not fixed.** The HTML page change above removes the margin offset that defeated the #236 attempt. The other half of that finding still holds: a glyph in the text breaks the text check, because LibreOffice's text export leaves the control out. The HTML reader also produces no shapes, and a shape cannot anchor in a nested table cell. Drawing the control needs a control the model draws but the text projection leaves out. Ink: 0 / 26. |
| `rtfexport/data/tdf49073.rtf` | As in #232, #234 and #236: the ruby annotations (`EQ \o\ad(\s\up …)`) are not modelled (`rtf.field.ruby`), and the CJK base text has no glyphs in the pinned fonts. The reader's text matches LibreOffice's text export, which leaves the ruby out. | **Not fixed.** I reconsidered a minimal ruby rendering, and it would not change this sample. In LibreOffice's raster, base and ruby text alike draw Liberation Serif's empty `.notdef`, so the reference page is blank (ink 0 locally and in CI). broilerdoc's ink is entirely Broiler.Graphics' `?` fallback (15865). Ruby text is also CJK, so rendering it would only add more `?`. The difference is the fallback glyph, which lives in Broiler.Graphics. |
| `rtfimport/data/tdf59699.rtf` | As in #234 and #236: `INCLUDEPICTURE "libreoffice.png" \d` with an empty result, and LibreOffice loads the file from beside the document. The file is beside the sample in the corpus. | **Not fixed, by design.** A reader gets only the document's bytes (`rtf.image.external`). The CLI's opt-in sibling-image resolver (`--allow-local-images`) is HTML's `HtmlReadOptions`. Taking it to RTF would need a format-neutral resolver on `DocumentReadOptions`, because a codec rejects another codec's option type, and the runner would then pass the flag for every format. That is a policy change for every reader, not a fix to this one. Ink: 0 / 855. |
| `rtfimport/data/groupshape.rtf` | As in #236, with a correction: the top is not misplaced. The group's -585 twips is measured from the 567-twip top margin, which puts both LibreOffice and broilerdoc at the page edge. The remaining difference is that LibreOffice draws each `shapeType` 202 child as a draw text shape that shrinks around its text, centred in the child's width, while broilerdoc draws the stated boxes, as Word does. LibreOffice 26.2.6.3 does the same. `rtfexport/data/fdo66040.rtf` is the same group with a table in the first child, and there LibreOffice draws both children as full text frames, the way broilerdoc draws them (grey difference 2.40). | **Not changed.** Shrinking the boxes would copy a LibreOffice import choice that depends on what the group's text holds. The two samples show the switch, but not the rule behind it. Ink: 8593 / 367. |

## Verification

- The runner on the seven samples with this branch: `page-border.rtf` and
  `emojis16BE.html` now pass the pixel check. The other five fail as described
  above. The text checks are unchanged: only `groupshape.rtf` differs, and as in #236.
- Page borders: I rendered all 69 reference RTFs that contain `\pgbrdr` with the base
  build and with this branch. Only `page-border.rtf` changed (grey difference
  1.76 → 0.32). The others carry only LibreOffice's empty `\pgbrdrhead\pgbrdrfoot`.
- WordArt: both corpus samples with `fGtext` (above) moved closer.
- HTML page: all 37 HTML samples with a reference PDF, on the runner with the base
  tool and harness and then with this branch. Pixel checks went from 6 to 30 passing,
  with no regression, and read and text checks were unchanged.

## Tests

- `RtfIssue238Tests` (Rtf.Tests): per-side page border widths and insets from the
  text; `\pgbrdropt32` measuring from the page edge; border colour from the colour
  table; `\pgbrdrhead\pgbrdrfoot` alone drawing nothing; a paragraph border after a
  page border; `\sectd` clearing it; a WordArt shape's string, face, colour, size and
  diagnostic; and `gtextUNICODE` without `fGtext` leaving a shape as it was.
- `RenderPagePrecedenceTests` (Cli.Tests): `--default-margin` for a document that
  states no page, a stated page left alone, and `--margin` outranking it.
- `HtmlRenderArgumentTests` (Office.Tests): HTML samples get the HTML page and face
  arguments and no `--margin`; DOCX, RTF and ODT samples get neither.

The solution builds. The Rtf, Cli, Office and Broiler.Documents test projects pass.
