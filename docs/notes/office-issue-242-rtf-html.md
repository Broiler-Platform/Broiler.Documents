# Office issue #242: RTF pages with no body, header watermarks, group text boxes

Six samples: five RTF and one HTML. I reproduced each one on Linux with the office runner
at 96 dpi (`--accept-reference-mismatch`, `--font-set office-fonts-2`, the archive's
font directory), with LibreOffice 24.2.7.2 as the reference. I ran cut-down files
through the local `soffice` 24.2.7.2 (`--convert-to pdf`, then `pdftoppm -r 96`, and
`--convert-to fodt` to read Writer's geometry) to see what Writer does. Similarity is
the runner's pixel score, before → after.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `rtfexport/data/dppolyline.rtf` | Four `\do` polylines and nothing else. LibreOffice drew them on an A4 page with 2 cm margins; broilerdoc used RTF's Letter page with 1-inch margins, so the ink was 21 px lower and the page a different size ("geometry off"). writerfilter applies the document's page (`\paperw` and the rest, Letter when omitted) only when it ends a section, and it opens one only at the first run of text, paragraph mark or picture (`checkFirstRun`). A document with none keeps Writer's default page, whatever it states. soffice cut-downs: a lone `\do` rectangle, a lone `\shp` rectangle, and an empty document all come out A4 with 2 cm margins, even with `\paperw8000\paperh9000\margl2000\margt2000`. Adding `\par`, a letter of text, a picture shape, text in a `\shptxt` or a `\dptxbx` gives Letter. One quirk counts as text: writerfilter collects the text of `\author` and the other info fields, but text between them in `{\info …}` is a body run (`tdf115153.rtf`'s `{\info {\author Oracle Reports} }` gives it its stated 11340-twip page; without the space it is A4). Text after the root group closes does not count (`tdf84684.rtf`). | **Fixed: 64.1% → pass.** The reader records whether anything reached the body, a frame, a note, a shape's text or a text box (an accumulator write), a shape picture, or text directly in `\info`. If nothing did, the document is read again with Writer's default page, A4 with 1134-twip margins, and the page and margin control words are ignored. Geometry is turned into shape positions as it is read, so a second read is the simple way to place them on that page. Also fixed: `dprect-anchor.rtf` (77.1%), `custom-doc-props.rtf` and `tdf151370.rtf` (blank pages, 100% but on the wrong paper), `tdf84684.rtf` (88.8%), `tdf90097.rtf` (72.6%). `ink.rtf` improved 73.2% → 77.7%. Every reference RTF's page size now matches the reference PDF's, except `tdf162342.rtf` and `lndscpsxn.rtf`, which differed before. |
| `rtfimport/data/watermark.rtf` | Word's `PowerPlusWaterMarkObject` in the header: WordArt (`fGtext`, shapeType 136) "ASAP", 424.2 × 212.1 pt, turned 315°, `posh 2`/`posv 2` (centred) with `posrelh 0`/`posrelv 0` (the margin). Three things differed. (1) In #238 the reader drew the word as plain text in its box, at a guessed size. (2) broilerdoc centred it on the page's text area; LibreOffice measures `posrelv 0`, and `\shpbymargin` too, from the anchor paragraph, here the header paragraph. The FODT export says `style:vertical-rel="paragraph"`, and soffice cut-downs confirm it in the body: a shape anchored to the ninth paragraph with `\shpbymargin`, or with `posrelv 0`, is placed from that paragraph, by offset or alignment; `\shpbypage`/`posrelv 1` stay on the page. (3) Writer aligns a turned shape by its bounding box, and keeps a header drawing on the page: a 533 px tall box centred on a one-line header paragraph starts at the page's top edge, and a 45° shape aligned to the top of its header paragraph has its bounding square's top there. The watermark's 450 pt square, centred on the header line, would start above the page, so it is moved down to the top edge. | **Fixed: 40.6% → pass.** A WordArt shape of a VML text shape type (136–175) is drawn with `StretchesTextToBox`, which the DOCX reader's VML WordArt already uses: each line is stretched across the box, sized only so it stays on one line. `posrelv 0` and `\shpbymargin` now measure from the paragraph. An aligned shape is aligned by the box its rotation sweeps. A header shape aligned with the header's first paragraph is placed from the header distance, with the paragraph a line of its font tall (1.15 em: 11 pt Calibri's header line is 12.4 pt in LibreOffice). A header or footer shape whose turned box crosses the page's top or bottom edge is moved inside it. The ink box now matches LibreOffice's exactly. `rtfexport/data/watermark.rtf` (44.5%) and `rtfexport/data/fdo83464.rtf` (67.7%, a `\shpbymargin` shape below the first paragraph) now pass too. |
| `rtfimport/data/groupshape.rtf` | As in #236–#240: LibreOffice draws each `shapeType` 202 child of the group as a drawing text shape. #240 found the switch (a group with no table in its children gives drawing shapes, `IsCustomShape`), but not the size rule. The FODT export shows it: each child is a `draw:custom-shape` with `fo:min-width` and `fo:min-height` 0 and auto-grow on, 0.2335 × 0.2024 in, centred across the child's stated width, at its stated top. soffice cut-downs settle the rule: with 540 words in the first child, the shape is one unwrapped line 38 in wide, centred on the child; with a second paragraph it is two lines tall and as wide as the wider line. | **Fixed: 17.8% → pass.** A group child that is a drawing shape, of shapeType 202, with text, is now `FitsText`: the box fits its unwrapped text and is centred in the box it states, which is what the ODT reader already does for a custom shape with words in it. A child with a table (`fdo66040.rtf`) keeps its stated frame. |
| `rtfexport/data/tdf49073.rtf` | As in #232–#240. A closer look at the fonts: the CJK runs state `\dbch\af6`, an East Asian font of Liberation Serif, and `\loch\f5` Century for Latin text. LibreOffice draws CJK text in the East Asian font, so it draws Liberation Serif's `.notdef`, which has no outline: a blank page. The model has no East Asian font, so broilerdoc draws everything in Century, which resolves to the host fallback DejaVu Sans, and Broiler.Graphics draws its own `?` for every character. | **Not fixed.** I tried leaving out, in the painter, characters no available face maps when the resolved face's `.notdef` has no outline. It did nothing here, because broilerdoc's face is DejaVu Sans (a boxed `.notdef`), and it broke `rtfexport/data/fdo77996.rtf` (pass → 0%): its CJK-named font resolves to Liberation in broilerdoc, but LibreOffice falls back to a face that draws boxes. Which `.notdef` is drawn depends on font substitution that broilerdoc does not share with LibreOffice, so the change was reverted. A fix needs an East Asian font in the model, read from `\dbch\af`/`w:eastAsia`/`style:font-name-asian`, and chosen per script in layout. 0%. |
| `rtfimport/data/tdf59699.rtf` | As in #234–#240: `INCLUDEPICTURE "libreoffice.png" \d` with an empty result. #240 checked what LibreOffice draws: the 64 × 64 icon is `libreoffice.png` itself, loaded from beside the RTF, not a placeholder. | **Not fixed, by policy (ADR 0004).** broilerdoc does not read files the document names. 0%. |
| `htmlimport/data/tdf155011.html` | As in #236–#240: the page's only ink is LibreOffice's 13 px radio-button control at (81, 83), inside three nested tables. The HTML reader skips `input`. New this round: `dump --as text` leaves inline pictures out, so a control drawn as an inline picture would keep the text check passing. I tried a 13 px circle as an `<img>` in the input's place, rendered with the runner's HTML arguments: it lands at (79, 41). The 42 px gap is table layout, not the control: soffice cut-downs (`<table><tr><td>Z` alone, and inside a second table) put a table that holds a nested table about 40 px lower than the same table without one (Z at y 43 vs 83), where broilerdoc moves it 1 px. The FODT export shows no extra paragraph, so the offset comes from Writer's HTML table layout. | **Not fixed.** Drawing the control alone would leave it 42 px from LibreOffice's, so the pixel check would still fail. The nested-table offset is a separate HTML table-layout difference and belongs with that work. 0%. |

## Verification

All runs use the office runner at 96 dpi with the archive's fonts, first with the base
build (`git archive` of the branch point) and then with this branch.

- **The six samples:** pixel 0 → 3 passing (`dppolyline.rtf`, `watermark.rtf`,
  `groupshape.rtf`); text 6/6 before and after.
- **Pages without a body:** the 24 reference RTFs whose page broilerdoc now reads as
  Writer's A4 default. Pixel checks went from 15 to 21 passing; text 21/21 before and
  after. Nothing newly fails. A page-size survey of all 557 reference RTFs (`broilerdoc
  info` against the reference PDFs' page size) found two mismatches, both present before.
- **Shape frames, alignment, WordArt, header clamping:** every reference RTF with
  `\shpbymargin`, `posrelv`, `posv`, `fGtext` or `rotation`, and every one with a shape
  in a header or footer (37 samples). Pixel checks 20 → 25 passing, text 29/29 before
  and after. Nothing newly fails.
- **Group text boxes:** every reference RTF with `\shpgrp` (14 samples). Pixel 5 → 8,
  text 8/8 before and after.
- **All 557 reference RTFs**, base build vs this branch: pixel checks 474 → 484
  passing, text checks 464 → 464. Comparing the failure lists, nothing newly fails.
  Fixed: `dppolyline`, `dprect-anchor`, `custom-doc-props`, `tdf151370`, `fdo83464`
  and `watermark` (rtfexport); `groupshape`, `tdf84684`, `tdf90097` and `watermark`
  (rtfimport). Improved: `ink.rtf` 73.2% → 77.7%. Every other score is identical.
- The reverted painter experiment for `tdf49073.rtf` was measured on the 19 reference
  RTF, HTML and FODT inputs with CJK text: pixel 17 → 16 (`fdo77996.rtf` failed).

## Tests

`RtfIssue242Tests` (Rtf.Tests): a document with only a drawing, only a shape, or
nothing opens on A4 with 2 cm margins and places a margin-relative drawing from them;
body text, a paragraph mark, shape text, a text box's text, a shape picture and text
in `{\info}` apply the stated page; a `\title` and text after the root group do not;
the header watermark is centred across the margins, kept on the page and stretched;
a turned shape aligned to the top of its header paragraph aligns its bounding box; an
unturned one is centred on its header line; a turned shape centred on the page has its
centre there; a header shape past the page's foot is moved onto the page; a group's
drawing text box fits its text and a group with a table keeps its frames.
`RtfLegacyDrawingTests` now ends its documents with `\par`, so the page they state
applies. `RtfShapeFrameTests` and `RtfShapeGroupAndFormatTests` now expect
`\shpbymargin` and `posrelv 0` to measure from the paragraph.

The solution builds with no warnings. Rtf.Tests (676), Pagination.Tests (466),
Cli.Tests (357), Broiler.Documents.Tests (329, 3 skipped) and Office.Tests (99) pass.
