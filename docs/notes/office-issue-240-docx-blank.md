# Office issue #240: DOCX pages one side draws blank

The DOCX samples of issue #240 where one renderer drew a page blank, or nearly
blank, and the other inked it. I rendered each one locally against the
LibreOffice 24.2 references (`office-fonts-2`, `--font-dir fonts/0`, the
harness's metric aliases) at 96 dpi. I scored each render as the number of ink
pixels that do not match the reference, with a 2 px tolerance. The earlier
notes used a 50% ink threshold. Watermarks are light grey or 50% opaque, so for
these sweeps a pixel counts as ink below 220 of 255. To check for regressions,
I rendered every corpus sample that uses a changed feature with the build
before and after the change. Four samples were analysed in the
[issue #234](office-issue-234-docx-blank.md),
[#236](office-issue-236-docx-blank.md) and
[#238](office-issue-238-docx-headers.md) notes. I re-checked the blocker
recorded there for each one.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `ooxmlexport/data/tdf114308.docx` | The default header holds a VML "DRAFT" watermark (`_x0000_t136`, `fitpath`), 412 by 247 points, with no `fillcolor` and no `stroked`. VML's defaults then give a white fill and a black 0.75 pt stroke, and LibreOffice draws hollow black letters stretched to fill the box. broilerdoc painted the glyphs in the fill colour, white on a white page, and drew no stroke. It also set the text at a size that fits the box, not stretched to it. That second difference also applies to every other text watermark. The `docx.header.even`/`first` diagnostics are not the cause (see the #238 note). | Fixed. The VML text shape types (`o:spt` 136 to 175, from the shape, its `v:shapetype`, or Word's `#_x0000_tNNN` type) are now read as `DocumentShape.StretchesTextToBox`, with the shape's stroke as `TextOutline`/`TextOutlineWidth`. `DocumentPagePainter.DrawStretchedText` takes each glyph's outline from the resolved TrueType face. It stretches each line's ink across the box and the ink of all lines down it, which is what LibreOffice's Fontwork does for these types. It then strokes the outlines. When no face with outlines is known, the text is drawn as laid out, as before. LibreOffice's Fontwork that it writes back as a custom shape (`o:spt="100"` with a text path: `tdf138739.docx`, `drawinglayer-pic-pos.docx`) is not stretched, because LibreOffice 24.2 reads it as a plain shape. Mismatch 5814 to 2248, with the "DRAFT" outlines where LibreOffice draws them. |
| `ooxmlimport/data/groupshape-fontname.docx` | The body is one `mc:AlternateContent` whose `mc:Choice Requires="wpc"` holds a drawing canvas (`wpc:wpc`) of 79 shapes. broilerdoc does not lay out a canvas. It read the canvas's first text box as a line of text ("AVANT L'OPERATION", the failing text check) and lost the rest. The `mc:Fallback` holds the same diagram as a VML group, which broilerdoc draws. | Fixed. `DocxAlternateContent.Branch` skips an `mc:Choice` that requires the canvas namespace when a fallback exists. ECMA-376 part 3 §10.2.1 lets a consumer that does not understand a required namespace do this. It is used where the reader reads alternate content in blocks, in runs and in `w:object`. The diagram is now drawn: mismatch 11615 to 106 at the light-ink threshold once the fallback's curves (next row) are drawn, and the page passes the pixel check. The text check also failed for a second reason. The text dump follows LibreOffice's text export, which lists the first text frame's text when the body is one empty paragraph, and the fallback's grouped text boxes counted as frames. LibreOffice reads a text box inside a group as a drawing shape. `DocxVmlShapeWalker` now marks the shapes it yields from a group, and the reader sets `IsCustomShape` on them, so the dump is empty like LibreOffice's export and the text check passes. That also fixes the text checks of `fdo61343.docx`, `fdo74401.docx`, `groupshape-smarttag.docx` and `tdf97618_testVmlShapeTextWordWrap.docx`. |
| `ooxmlexport/data/FDO74774.docx` (found by the canvas sweep), and the fallbacks above | Its canvas fallback draws two chart frames as VML freeforms rounded with `c` and `v` (cubic Bezier) segments. `DocxVmlPath` rejected those commands, so each frame fell back to its box filled grey. | Fixed. `DocxVmlPath.Render` flattens `c` (absolute) and `v` (relative) cubic segments. The frame's inner contour then leaves the chart visible, as LibreOffice draws it (mismatch 18999 to 105). |
| `ooxmlexport/data/tdf162746.docx` | The reference is one page whose only body ink is a 4 px sliver of the table's header cell at the left margin: LibreOffice 24.2 lays the body table out about zero wide, and the rest of the page is blank. The table is `w:tblW w:type="pct" w:w="5000"` with `tblLayout fixed`, and the header holds a page-anchored floating table (`tblpY="1248"`). This is the layout tdf#162746 is about. broilerdoc lays the table out across the text column and needs two pages for it, as Word does. | Not changed. The reference shows the LibreOffice defect the sample was filed for. I could not identify the fixing commit or release, so there is no reference-defect row yet. One broilerdoc difference is real: the header's floating table is set in the band's flow (its rule above "bar") instead of at `tblpY` on the page, where LibreOffice puts it (its rule below "bar"). That is a separate gap for floating tables in headers. |
| `ooxmlexport/data/shape-in-floattable.docx` | An inline `wpg` group in a floating table's cell holds one text box at its child offset (`a:off 21117,10828` in `chOff 2886,3580 / chExt 29425,18206`). LibreOffice 24.2's flat ODF export shows the child imported over the whole group (`svg:x="0in" svg:y="0in"`, 3.263 x 2.587 in), so "Hello" sits at the cell's top left. broilerdoc places it at its child offset, where Word puts it. | Not changed. This is LibreOffice import behaviour for a group in a floating table, and the upstream test checks only the export. |
| `ooxmlexport/data/fdo78384.docx`, `ooxmlimport/data/tdf103664.docx` | `w:sym` characters in Wingdings, which is not in the reference font set (issue #238 note). | Not changed. The reference draws the fallback face's empty `.notdef`. Matching it needs Broiler.Graphics to draw `.notdef` instead of "?". |
| `ooxmlimport/data/tdf125038b.docx` | An `IF` field whose condition LibreOffice's import garbles (issue #234 note). | Not changed. |
| `ooxmlexport/data/tdf104115.docx` | A VML arc that LibreOffice imports as an empty path (issue #234 note). | Not changed. |

Also changed, because the watermark work exposed it:

- A VML angle may be written in fixed-point degrees, 65536 to the degree
  (`rotation:-2041671fd`, tdf168017's "EXAMPLE ONLY" watermark).
  `DocxVmlShapeWalker.Rotation` read that as no rotation. It now reads the
  `fd` suffix.
- Applying that rotation showed that a stroked rectangle drawn under a rotation
  transform is not turned. `tdf76446.docx`'s rectangle, turned 31 degrees, was
  outlined as an upright box. `DocumentPagePainter` now outlines a rotated
  rectangle as its four edges (mismatch 1696 to 0). A rotated rectangle's
  fill is still drawn upright. All the rotated rectangles in the corpus are
  white or unfilled, so none shows this.

## Verification

- The issue's samples, run through the harness: `tdf114308.docx` (0%) and
  `groupshape-fontname.docx` (41.4%, text check failing) now pass both their
  pixel and their text checks. `tdf162746` (36.1%), `shape-in-floattable`,
  `fdo78384`, `tdf103664`, `tdf125038b` and `tdf104115` (0%) are unchanged.
  The harness went from 15/24 to 18/24 checks passed. Light-ink mismatch:
  `tdf114308` 5814 to 2248; `groupshape-fontname` 11615 to 106.
- Feature corpus: 164 DOCX samples. These are every sample with a VML
  `v:textpath` (21), a drawing canvas (9), a VML or DrawingML rotation (66),
  a VML angle in `fd` (6), a VML path with `c` or `v` segments (10), or a VML
  group (94), some in several groups. I ran each through the harness with the
  build before and after: 434/487 to 451/487 checks passed. Pixel went from
  123 to 135 passed and text from 147 to 152, with no check newly failing.
  Pixel checks newly passed: `FDO74774`, `fdo65833`, `fdo74401`,
  `groupshape-picture`, `tdf100751_arrowBothFlip`, `tdf114308`,
  `tdf97618_testVmlShapeTextWordWrap`, `fdo75722-vml`, `groupshape-fontname`,
  `ink`, `tdf134902` and `uiwriter/watermark`. Text checks newly passed:
  `fdo61343`, `fdo74401`, `groupshape-smarttag`,
  `tdf97618_testVmlShapeTextWordWrap` and `groupshape-fontname`.
- The same corpus, scored by light-ink mismatch: 22 samples are closer to the
  reference, 139 are unchanged and 2 are further away. Among the closer
  ones: `fdo74401` 113093 to 0, `fdo65833` 23376 to 108, `textWatermark`
  29110 to 18351, `watermark-font` 10963 to 1023, `fdo78659` 53349 to 39197,
  `uiwriter/watermark` 20139 to 14833, and `tdf76446` 1696 to 0. The two
  further away are WordArt that is now drawn the way LibreOffice draws it
  but a few pixels from where LibreOffice puts it.
  `fdo79540.docx` (8010 to 9247): "February 2013" is now stretched and
  outlined like LibreOffice's, but the arched "Pay $12 Dues!" (`_x0000_t175`)
  is still 40 px lower than LibreOffice places it, and it now carries more
  ink there. `tdf168017.docx` (1042189 to 1042519 over 29 pages): page 20's
  "EXAMPLE ONLY" was white text, invisible. It is now drawn hollow and turned
  as in LibreOffice, and its strokes land a few pixels from LibreOffice's
  because the page's text sits differently. Neither sample's checks change.
- Tests: `DocxOfficeIssue240DocxBlankTests` (canvas fallback, a choice that
  does not require the canvas, WordArt fill, stroke and stretch, Fontwork
  written as a custom shape, `fd` angles, cubic freeform segments) and
  `DocumentLayoutIssue240DocxBlankTests` (stretched WordArt outlines fill the box, which
  uses the host's DejaVu Sans when it has one; no outlines without a face; a
  rotated box's outline). Docx (1042), Pagination (454), Model (343), core
  (329, 3 skipped), Cli (349), Odt (694), Rtf (653), Pdf (749), Office (99),
  Html (244) and Markdown (60) test projects all pass. The solution builds
  with no warnings. That needed a one-line fix to an existing analyzer warning
  (xUnit2030) in `DocumentLayoutIssue236Tests`.
- Not covered: the PDF writer does not stretch or outline WordArt. Curved
  WordArt paths (arches, waves) are stretched to their box, not bent. A
  rotated rectangle's fill is drawn upright.
