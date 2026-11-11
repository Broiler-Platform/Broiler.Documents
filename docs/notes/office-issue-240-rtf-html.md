# Office issue #240: RTF page breaks before floating shapes, hidden paragraph marks in headers, group text boxes

Six samples: five RTF and one HTML. I reproduced each one on Linux with the office runner
at 96 dpi (`--accept-reference-mismatch`, `--font-set office-fonts-2`, the archive's
font directory), with LibreOffice 24.2.7.2 as the reference. I ran cut-down RTF
files through the local `soffice` 24.2.7.2 (`--convert-to pdf`, then `pdftoppm -r 96`)
to see what Writer does. Similarity is the runner's pixel score, before → after.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `rtfexport/data/tdf153613_anchoredAfterPgBreak5.rtf` | `1\par \page {\shp…}{\shp…}\par 2\par`: after the page break, the paragraph holds only two floating pictures. The reader spent the `\page` on that paragraph, so both pictures went to page 2. Writer keeps a `\page` waiting until text follows it in the paragraph (tdf#153613, writerfilter's deferred page break, pinned by `rtfexport5.cxx testTdf153613_anchoredAfterPgBreak*`). Floating objects followed by nothing up to the mark stay on the page before the break, and the break starts the next paragraph. When the break interrupted text (`x\page{\shp}\par`, `…Break4`), that paragraph is not split. | **Fixed.** At `\par`, a body paragraph that holds nothing after an explicit `\page` except `\shp` shapes written in the body flow is ended without the break. The break waits for the next paragraph. If the `\page` split a paragraph, the shapes anchor to the text before it and no empty paragraph is added. A section break is never held. Shapes from a frame paragraph, which wait for the next unframed paragraph, are left alone too: in `fdo55504-1-min.rtf`, a first attempt that ignored this lost the document's second page. 19.9% → pass. `tdf153613_anchoredAfterPgBreak.rtf` (34.3%) and `…Break4.rtf` (61.5%) now pass as well. |
| `layout/data/min-wtf.rtf` | The header has a chain of paragraphs whose marks are hidden (`{\v … \par}`), some with visible text. Writer draws a paragraph whose mark is hidden as one with the next paragraph (`layout3.cxx testHiddenParagraphMarkPerLineProperties`: the header has three text frames). broilerdoc dropped only paragraphs whose text and mark were all hidden, so the header kept its six visible-text paragraphs apart, with the `\sa1600\sl480` of the right-aligned one. That pushed the body about 200 px down. soffice experiments fixed the rule: `\qc\sa0 AAA{\v\par}\qr\sa600 BBB\par` draws "AAABBB" centred with no space below. A paragraph whose text is all hidden lends its formatting the same way (`\qc{\v x\par}\qr B\par` centres "B"). An empty paragraph with a hidden mark joins nothing (`\qc{\v\par}\qr B\par` leaves "B" right-aligned). | **Improved: 0.84% → 43.1%.** In the running path (headers and footers without tables), `ComposeRunning` now joins a paragraph that has text and a hidden mark with the paragraphs after it, until a mark is shown. The joined paragraph keeps the first one's paragraph style. The header lines now match LibreOffice's: "a", "n" and "v" centred, then "da⇥c" and "x⇥y" at the same x. **Remaining:** the header style `s15` inherits `\sl276` from Normal in our RTF style sheet. writerfilter gives an RTF style default values for what it does not state (the ODT export shows `Header` at 100%), so each header line is about 2 px taller than LibreOffice's. The tab after "v" also differs. The body path still drops only wholly hidden paragraphs and does not join them. Joining in the body would renumber paragraphs that shapes, tables and page starts refer to, which is beyond this issue's scope. |
| `rtfimport/data/groupshape.rtf` (text check) | broilerdoc's text dump printed "foo". The body is one empty paragraph, so the text projection follows LibreOffice and exports the first text frame. LibreOffice imports a group's text boxes as drawing shapes, so it has no frame to export. In `fdo66040.rtf`, the same group with a table in its first child, the children are frames and their text is exported. | **Text check fixed.** `RtfShapeGroups` marks every child of a group that holds no table (no `\intbl`/`\cell`/`\row`), and the reader sets `DocumentShape.IsCustomShape` on those children. The projection already skips custom shapes that hold no table. Nothing else reads the flag, so the render is unchanged. Pixel: 17.8%, not changed, for the reason in #238. Writer shrinks these drawing text shapes around their text, and the rule behind that is unclear. |
| `rtfexport/data/tdf49073.rtf` | As in #232–#238: ruby (`rtf.field.ruby`) is not modelled, and the CJK text has no glyphs in the pinned fonts. LibreOffice's Liberation Serif `.notdef` is empty, so the reference page is blank. broilerdoc's ink is Broiler.Graphics' `?` fallback. | **Not fixed.** The difference is the fallback glyph, which lives in Broiler.Graphics. Rendering ruby would only add more `?`. 0%. |
| `rtfimport/data/tdf59699.rtf` | `INCLUDEPICTURE "libreoffice.png" \d` with an empty result. I checked what LibreOffice draws: the 64×64 grey document icon is `rtfimport/data/libreoffice.png` itself, loaded from beside the RTF. It is not a broken-link placeholder. The RTF states no size for the picture, so broilerdoc cannot even reserve its box. | **Not fixed, by design (ADR 0004).** The reader does not open files named by document content. Matching LibreOffice would need that policy changed, not a reader fix. 0%. |
| `htmlimport/data/tdf155011.html` | As in #236/#238: the page's only ink is an `<input type="radio">` inside nested tables, which LibreOffice draws as an 11 px form control. The HTML reader skips form controls. | **Not fixed.** It needs a form-control object that the model draws but the text projection leaves out, because LibreOffice's text export omits the control. It also needs that object to anchor in a nested table cell. 0%. |

## Verification

All runs use the office runner at 96 dpi with the archive's fonts, first with the base
build and then with this branch.

- **Page break before shapes:** every reference RTF that has both `\page` and `\shp`
  (13 samples, including `n818997*.rtf`, `fdo55504-1-min.rtf` and all six
  `tdf153613_*`). Pixel checks went from 7 to 10 passing, with 1 skipped. Read and text
  checks were unchanged (12/12 and 12/12). The two that still fail
  (`tdf141173_missingFrames.rtf` 0%, `165717.rtf` 50.8%) have the same scores as before.
- **Hidden paragraph marks in running content:** every reference RTF with `\v` and a
  header or footer, every DOCX with `w:vanish` in a header or footer part, and every
  ODT with `text:display="none"` in its styles (19 samples). Pixel checks were 13/19
  passing before and after. Only `min-wtf.rtf` changed (0.84% → 43.09%), and every
  other score is identical. The text check was 18/19 before and after
  (`FWDP90_min.rtf` fails on both).
- **Group text boxes:** every reference RTF with `\shpgrp` (14 samples). Text checks
  went from 7 to 8 passing (`groupshape.rtf`), with `fdo66040.rtf` failing before and
  after. Pixel checks were 5 passing before and after, with identical scores.
- **All 557 reference RTFs**, base build vs this branch: pixel checks 471 → 474
  passing, text checks 463 → 464, read 546 = 546. Comparing the failure lists, nothing
  newly fails. Fixed: the pixel checks of the three `tdf153613_anchoredAfterPgBreak*`
  samples, and the text check of `groupshape.rtf`.

## Tests

- `RtfIssue240RtfHtmlTests` (Rtf.Tests): shapes alone after a `\page` stay before the
  break, and the break moves to the next paragraph; a `\page` that interrupted text
  followed only by a shape does not split the paragraph; text after the shape takes the
  break with it; a section break is not held; a group's text boxes are drawing shapes
  unless one holds a table; a lone text box stays a text frame.
- `DocumentLayoutIssue240RtfHtmlTests` (Pagination.Tests): a header paragraph with a
  hidden mark joins the next one with the first paragraph's alignment; a paragraph
  whose text is all hidden still lends its formatting; an empty paragraph with a
  hidden mark joins nothing.

The solution builds with no warnings. Rtf.Tests (659), Pagination.Tests (454) and
Cli.Tests (349) pass.
