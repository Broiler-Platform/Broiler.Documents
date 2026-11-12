# Office issue #242: DOCX frames, watermarks, VML groups, WMF and turned pictures

The DOCX samples of issue #242 whose difference is a drawn shape, WordArt, a
watermark or a picture. I rendered each one locally against the LibreOffice
24.2.7.2 reference archive, with the reference fonts (`--font-dir fonts/0`,
`--font-set office-fonts-2`), at 96 dpi, and report the harness's rendering
similarity (threshold 80%) and its text check. LibreOffice's behaviour where
the references did not settle it was checked on cut-down documents with the
local `soffice` 24.2.7.2. To check for regressions I rendered every corpus
sample that uses a changed feature with the build before and after the
change.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `ooxmlexport/data/fdo60957-2.docx` | Paragraph F, a one-cell shaded table G and paragraph H all state the same `w:framePr`. LibreOffice draws the three in one frame and its text export leaves them out. The reader finished the frame at the table, so the table was drawn in the body at the top of the page, F and H were drawn on top of each other in a frame, and `dump --as text` wrote the "G" LibreOffice does not. | Fixed. A table whose first cell's paragraph states the frame that the framed paragraphs before it opened is read into that frame (`DocxFrameAccumulator.ReadTableProperties`), and the frame shape carries the table. A table never opens a frame, and a floating (`w:tblpPr`) table never joins one: LibreOffice keeps those in the body (`tdf75573_lostTable.docx`, `tdf115094.docx`, `tdf164474.docx`, which a first version that let a table open a frame broke). Text check passes; 17.45% to pass. |
| `ooxmlexport/data/pictureWatermark.docx` | The header picture watermark is a VML picture with `gain="19661f" blacklevel="22938f"`, Word's "Washout". LibreOffice imports exactly that pair as its watermark colour mode (luminance +50%, contrast -70%); broilerdoc drew the picture at full colour. A cut-down test showed LibreOffice ignores any other gain or black level, and grayscale/bilevel too. | Fixed. `DocxImageLoader.ReadVmlPresentation` gives that pair a presentation of brightness 0.5 and contrast -0.7, the existing adjustment that matches LibreOffice's formula pixel for pixel (black to 217, red to 255/217/217). 73.24% to pass. |
| `ooxmlexport/data/tdf133473.docx` | An inline picture with `a:outerShdw sx="200000" sy="200000" algn="tl"`: a black copy twice its size from its top left corner, which LibreOffice draws (the black area of the reference). Shadows were not read. | Fixed. `ImagePresentation.Shadow` (`ImageShadow`: colour with alpha, offset from `dist`/`dir`, scale from `sx`/`sy` about the `algn` point, bottom by default) is read for DrawingML pictures and drawn under the picture, inline or floating. It is drawn unblurred; LibreOffice blurs by `blurRad`. 68.79% to pass. |
| `ooxmlexport/data/tdf103001.docx` | Three inline pictures of one WMF, turned by `a:xfrm rot` (90°, 270° and 251.6°). No WMF reader existed, so each was a missing-picture box, and turned pictures were drawn unturned. | Partly fixed. (1) `WmfRasterizer` (in `Broiler.Documents/Resources`) translates WMF records into their EMF counterparts and draws them with `EmfRasterizer`: the placeable box and window mapping, SAVEDC/RESTOREDC, the WMF object table (lowest free slot) with pens, brushes and fonts, polygons, polylines, poly-polygons, rectangles, ellipses, MOVETO/LINETO, PATBLT, TEXTOUT/EXTTEXTOUT and DIB blits. Arcs, regions, clipping and pattern brushes are not drawn. The DOCX loader uses it for a WMF media part. (2) An inline turned picture is presented turned (`ImagePresentation.Rotation`, the bitmap turned in `Apply`) and laid out as LibreOffice lays it out, measured on cut-down documents: its box is as tall as the turned picture's bounding box and as wide as Word's swapped extent (the height for a turn nearer a quarter than a half), and the `wp:effectExtent` is added to its margins when the picture is turned or has an effect; LibreOffice ignores it on a plain picture. 32.09% to 72.27%. The footer's picture is still left out: with its effect extent the footer overruns its margin by more than half the page's text height, which the layout refuses (`DocumentLayout.FrameFor`); LibreOffice lets it grow. Before the effect extent was read the sample scored 76.33%. |
| `ooxmlimport/data/n751077.docx` | A page-wide VML group of four text boxes. (1) Its `coordorigin=",1440"` leaves x empty, which the walker refused as invalid, so nothing was drawn. (2) The children keep Word's "centred in the margin" for when they are ungrouped; LibreOffice places them by the group's coordinates alone. (3) LibreOffice centres the group by the box its children cover, not by the 612 pt the group states, which puts it 26 pt left and 47 pt lower. | Mostly fixed. An empty field of a VML pair is zero, as LibreOffice reads it; a grouped child drops its own alignment, relative frame and relative size; a top-level group with an alignment is aligned by its children's bounding box (`DocxVmlShapeWalker.ChildBounds`). 52.47% to 72.55%. Left: TEXT1 asks for Garamond, which falls back on a sans face rather than a serif one (the font-family fallback noted in the issue #238 note), and LibreOffice sizes the empty paragraphs of a grouped text box by the style's font, not by the paragraph mark's 36 pt, so TEXT3 and TEXT4 sit 112 pt lower in the reference. |
| `ooxmlexport/data/testWordArtWithinDraingtool.docx` | A DrawingML text box holding an inline VML WordArt (`o:spt="172"`, text slant up). LibreOffice draws the string sheared along the path and its text export writes nothing for it (the body is one empty paragraph, and the text box it then exports holds only a drawing). broilerdoc draws the string as small text lines in the box (the issue #238 WordArt reader), so `dump --as text` writes "Watch LIVE / program and / ask questions!". The centred footer frame is drawn at the left margin. | Not fixed. Leaving the text out of the dump needs the model to tell WordArt runs from text (they are ordinary runs today), and drawing it as LibreOffice does needs glyphs fitted to the slanted envelope, which the renderer has no primitive for. 51.94% unchanged. |

## Verification

- Sample similarity at 96 dpi, before and after: `fdo60957-2` 17.45% to
  pass (text check passes), `pictureWatermark` 73.24% to pass, `tdf133473`
  68.79% to pass, `tdf103001` 32.09% to 72.27%, `n751077` 52.47% to 72.55%,
  `testWordArtWithinDraingtool` 51.94% unchanged (text check still fails).
- Regression corpus: 156 DOCX inputs, every one that has a table cell
  paragraph with `w:framePr`, a VML group, a washout picture, a WMF media
  part, an inline picture that is turned or has an effect, or an
  `a:outerShdw`. Pixel checks passed went from 107 to 112, text checks from
  139 to 140. Up besides the samples above: `groupshape-picture` (55.52% to
  pass, its WMF pictures), `tdf133448` (50.29% to pass, WMF),
  `tdf143475_rotatedWord2007imageInline` (58.64% to 76.80%), `fdo70457`
  (50.07% to 63.73%), `2129393649` (78.70% to 78.96%). Down:
  `testOuterShdw` 62.17% to 59.41%. Its header washout watermark now has
  LibreOffice's colours exactly (sampled pixels match), but the picture is
  centred some 55 px right of LibreOffice's, and the pale ink overlaps less
  than the full-colour picture did. No text check was lost.
- Tests: `DocxOfficeIssue242ShapesTests` (a table continuing a frame and one
  ending it, the washout pair and pairs left alone, a VML group with an empty
  origin field aligned by its children, turned inline pictures and their
  effect extent, a plain picture's extent ignored, outer shadows) and
  `WmfRasterizerTests` (polygons in their brushes with a reused object slot,
  non-WMF data refused). The Docx (1037), Documents (331), Model (343),
  Pagination (451), Cli (349) and Odt (694) test projects pass. The solution
  builds with no errors; its one warning (xUnit2030 in
  `DocumentLayoutIssue236Tests.cs`) predates this change.
