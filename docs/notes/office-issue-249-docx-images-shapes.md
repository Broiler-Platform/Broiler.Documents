# Office issue #249: DOCX images, shapes, WordArt and floating tables

The 39 DOCX samples of issue #249's docx-images-shapes cluster: anchored
pictures, drawn shapes, WordArt, floating tables and charts. Each was compared
with LibreOffice 24.2.7.2 locally, with the pinned reference fonts
(`office-fonts-2`), through the conformance harness (threshold 80%) and side by
side at 50 dpi. Where the references did not settle what LibreOffice does, I
converted cut-down copies of the sample with the local `soffice` 24.2.7.2 and
measured the result; those experiments are named in the table. The starting
commit is `8a20ca0`.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `layout/data/Cool15691_headerTextWrapsAroundImage.docx`, `…Largest.docx`, `ooxmlexport/data/n779834.docx`, `ooxmlexport/data/fdo76803.docx` | A header's object placed `relativeFrom="paragraph"` (or `"line"`), and a header VML shape with no vertical frame, were measured from the top of the page: the reader converts every band offset to one from the page, and the paragraph's place in the header is a layout result. LibreOffice measures them from the header paragraph: the Cool15691 picture at nought from its paragraph is drawn 35.4 pt down (the header distance), n779834's full-page picture 28.35 pt down. For n779834 I set `mso-position-vertical-relative` to `page`, `margin` and `text` in turn and removed `w10:wrap`: LibreOffice ignores `w10:wrap`'s `anchory="margin"` and uses the paragraph. LibreOffice 24.2 does not wrap the header's text around the Cool15691 picture (the test that expects it is from a later release), and neither does broilerdoc. | **Fixed.** The reader gives such shapes `DocumentShape.OffsetFromBandParagraph`, their stated offset from the paragraph, and `PlaceRunningShapes` measures it from where that header paragraph lands on the page. When the clusters were merged, this cluster's DrawingML half was replaced by the docx-headers cluster's implementation of the same rule (`BandParagraphOffset`), and the VML case was moved onto it. All four pass. |
| `ooxmlexport/data/tdf156484.docx` (and `tdf169802_hidden_shape.docx`, `tdf119201.docx`) | A drawn shape with `wp:docPr hidden="1"` was drawn. LibreOffice 24.2 neither shows nor prints it (`testTdf156484` asserts `Visible` and `Printable` false), and `tdf169802_hidden_shape.docx` shows its text still wraps around it. A hidden picture it does draw (`tdf162527_hidden_image.docx`), and a VML shape with `visibility:hidden` too (`tdf169802_hidden_shape.docx` with its `mc:Choice` removed). | **Fixed** for `wps:wsp` shapes: read as not printable (laid out and wrapped around, not drawn). `tdf119201.docx` now passes. `tdf156484.docx`'s first page now matches exactly; it still fails on page 2, where LibreOffice keeps a three-line paragraph together (the DOCX widow default, a roadmap item) and broilerdoc leaves one line behind. Its severity score rose (689.9 to 707.2) because the worst page moved from page 1 to page 3, though every page is closer: the share of differing pixels at 50 dpi went from 1.8/3.1/6.3 % to 0.0/1.3/5.1 % on pages 1/2/3. |
| `ooxmlexport/data/tdf159158_zOrder_1and0equalB.docx`, `…_duplicate_compat15.docx`, `…_zIndexWins.docx` (and `tdf137850_compat1[45]ZOrder.docx`, `tdf149546.docx`, `uiwriter/data/AsCharTxBxTest.docx`) | `a:prstGeom` presets other than ellipse and line were drawn as their box: the five-point stars were rectangles. | **Fixed** for `star5`, `triangle`, `rtTriangle`, `diamond` and the four block arrows: `DocxPresetGeometry` works out the polygon from the guide formulas of ECMA-376's `presetShapeDefinitions.xml`, with the `a:avLst` adjust values and the flips, and a half turn as both flips; the painter already fills `DocumentShape.OutlinePaths` (the ODT reader's custom shapes). A shape turned any other way keeps its box, since the reader does not turn a drawn shape's box (`tdf87924.docx`'s quarter-turned arrow and `rot180-flipv.docx` failed when the polygon was drawn unturned; both pass again). The two zOrder samples pass; zIndexWins now fails only its text check (below). The gradient is filled with its middle colour, as before. `tdf149546.docx` and `AsCharTxBxTest.docx` scored higher (831.2 to 888.0, 325.7 to 329.2) although their shapes now have LibreOffice's outline and every page is closer (2.7 % to 2.1 %, and 3.3/1.2 % to 1.1/1.0 % of pixels differing). |
| `ooxmlexport/data/FileWithInvalidImageLink.docx` | A picture linked (`r:link`) to `file:///F:\…\ole9.gif` was drawn as the crossed missing-picture box. LibreOffice looks the file up, does not find it, and prints nothing there. | **Fixed.** A link to an absolute `file:` URL, drive path or UNC path keeps its box and draws no ink (`InlineImage.MarksMissingPayload`), as the ODT reader already does for a picture LibreOffice cannot find. Passes. |
| `ooxmlimport/data/linked-picture.docx` | A picture linked to `linked-picture.png`, which sits beside the sample in the corpus. Converting the sample where it lies, LibreOffice draws the picture; converting a copy elsewhere, it draws nothing. | **Not fixed, by policy** (ADR 0004: broilerdoc does not read files that document content names), as in the issue #249 note for `document_with_linked_graphic.odt`. A relative link keeps the mark: an earlier version that left every unfetched link unmarked failed `tdf59699.docx`, whose `libreoffice.png` LibreOffice finds and draws. |
| `ooxmlexport/data/fdo77727.docx`, `ooxmlexport/data/fdo78300.docx`, `ooxmlexport/data/fdo74110.docx`, `ooxmlimport/data/WordArt.docx` (and `fdo78663.docx`, `testWordArtWithinDraingtool.docx`) | The reader keeps an inline VML WordArt shape's string as styled text in its line, and the text dump wrote it as paragraph text. LibreOffice imports the shape as a drawing set as a character, and its text export writes only the paragraph's own text. | **Fixed** (text): the run carries `InlineStyle.IsShapeText` and `dump --as text` leaves it out. fdo77727, fdo78300 and fdo78663 pass, and `testWordArtWithinDraingtool.docx` now passes its text check (the issue #242 note's "needs the model to tell WordArt runs from text"). fdo74110 and WordArt still fail the pixel check: LibreOffice bends and stretches the glyphs to the path, broilerdoc sets them as plain text. |
| `ooxmlimport/data/tdf60351.docx` (and `ooxmlexport/data/tdf97090.docx`) | A tightly wrapped picture with `behindDoc="1"` was painted under its paragraph's white shading. In cut-down copies LibreOffice draws it over the shading wrapped tight and wrapped square, and under it only with `wp:wrapNone` (Word's "Behind Text"); a pink shading made it visible. | **Fixed.** The reader marks such a shape `DocumentShape.DrawsInFront`, and the layout paints it in the front layer; `BehindText` keeps what the document says, so the writer writes `behindDoc` back unchanged (`DocxFloatingPictureTests` hold that). tdf60351 passes; tdf97090 785.2 to 337.0 (still failing: the text beside its picture wraps further down than LibreOffice's, and a list in a text box differs). |
| `ooxmlimport/data/tdf105143.docx` | A VML freeform path writes `hd` before its curve (`m,57hdc80,…`). The path reader stopped at the unknown command and the shape fell back to its box. LibreOffice reads the bézier coordinates (`testTdf105127`), passing over the `ha` to `hi` tokens. | **Fixed.** `DocxVmlPath` passes over `ha` to `hi` and any numbers after them. Passes. |
| `ooxmlexport/data/tdf78749.docx` | A VML text box with `v:fill type="tile" r:id=…` was drawn in one colour; LibreOffice tiles it with the water-drop picture. | **Fixed.** The picture is loaded as the page background's VML fill already was, and fills the box tiled at its own size (or stretched, for `frame`). Passes. |
| `ooxmlexport/data/watermark-shapetype.docx` | Every header and footer part was read against the last section's page. The first section is portrait and the last landscape, so the first section's watermark, centred in the margin, was centred on the landscape page: 108 pt right and 130 pt up. | **Fixed.** Each section reads its bands against its own `w:pgSz` and `w:pgMar`. Passes. |
| `ooxmlexport/data/position-and-rotation.docx` | A floating picture turned 110° (`pic:spPr/a:xfrm rot`) was drawn unturned. LibreOffice turns it about the centre of the unturned extent its offsets place, in a frame as large as the turned bounding box, and turns Word's wrap polygon (stated in the unturned box) with it. | **Fixed.** The reader gives such a picture the turned bounding box about the same centre, with the turned presentation inline pictures already use, and turns the wrap polygon. Passes. The text beside it wraps on the right only, where LibreOffice uses both sides of a polygon (`wrapText="bothSides"`); that is the wrap engine's, and the sample passes without it. |
| `ooxmlexport/data/fdo77887.docx`, `layout/data/fdo48718-1.docx`, `globalfilter/data/skipimages.docx` | Bold runs in a family the reference fonts lack (Noto Sans here) run their words into each other. The renderer's fallback keeps a regular and a bold face (DejaVu Sans, DejaVu Sans Bold) and draws a bold run with the bold one; the CLI measures it with the regular face, a tenth narrower. Besides that, their body text asks for families LibreOffice replaces with other faces than broilerdoc's sans fallback (the font-family fallback the issue #238 notes describe), and fdo77887's paragraph borders differ. | **Declined after measuring.** Measuring a bold run with the bold fallback face (`FontResolution`, `DocumentFontService`) made the headings read as LibreOffice's, but none of the three passed, and it turned `rtfexport/data/tdf158978.rtf`, which passed, into a failure: its wider bold headings wrap one line more in a table whose Arial Narrow labels are already set wider than LibreOffice sets them, and an empty paragraph went to a second page (2 pages against 1). It also raised `tdf168017.docx` (361.1 to 370.3) and `rtfexport/data/tdf131234.rtf` (707.9 to 709.9). Reverted; it belongs with the font-family fallback for unmapped families. |
| `ooxmlexport/data/tdf105688.docx` (outside the cluster) | The header-paragraph fix moved a black placeholder picture, anchored in a cell of the header's floating table, from 8 pt to its paragraph. LibreOffice draws that picture under the cell's blue shading, and draws the floating table at the top of the page; broilerdoc lays the header's floating table out inline, 78 pt lower. | **Not fixed.** Severity 419.8 to 876.1: the ink box measure followed the picture, which by chance sat where LibreOffice's band is. The share of differing pixels is unchanged (20.3/19.7 % to 20.4/19.7 %). Placing a header's floating table is the feature to build. |
| `ooxmlexport/data/tdf79329.docx`, `ooxmlexport/data/sdt-company-multipara.docx` | A table nested in a cell states `w:tblpPr` with `vertAnchor="page"`. LibreOffice 24.2 draws it as a floating table at its page position and leaves it out of the text export; the reader lays it out in its cell (`docx.table.floating`). | Not fixed: laying a floating table out of a cell onto the page is a layout feature. sdt-company-multipara's text also differs in its data-bound "company" field. |
| `ooxmlexport/data/tdf159158_zOrder_zIndexWins.docx` | Text check only: the body is one empty paragraph, so the dump falls back on the first frame with text, a VML text box. LibreOffice's text export takes only the first entry of its frame list (`SwASCWriter::WriteStream`), a star drawing, and writes nothing. | Not changed: matching that needs the model's shape order to be LibreOffice's frame order, which it is not for DOCX text boxes; the fallback was built for the samples that pass with it. |
| `ooxmlexport/data/tdf153891.docx` | Two 525 pt wide pictures laid out in 262 pt table cells. LibreOffice draws the first 262 by 102 pt where it states 525 by 206 pt: shrunk to its cell with its aspect kept, as Writer clips a graphic that does not fit its layout environment (`SwFlyFreeFrame::CheckClip`). broilerdoc draws them full size over the next cell. | Not fixed: fitting a floating picture to its layout environment is a layout feature. |
| `layout/data/tdf124600.docx` | The header's two text boxes wrap the body text in LibreOffice: the second paragraph is pushed below the first page's header box and then below the body's own text box. broilerdoc does not wrap body text around header shapes. | Not fixed: wrapping the body around a header's shapes is a layout feature. |
| `odfexport/data/tdf130314.docx` | Chinese text in a face with no CJK glyphs (LibreOffice draws empty boxes, broilerdoc filled ones, as the issue #242 note records), and the page number is a `PAGE` field in a header text box, drawn with its cached "2". | Not fixed. Fields in a band's shapes are not evaluated (also `tdf92648.docx`, `tdf81100.docx`). |
| `ooxmlexport/data/fdo76098.docx` | Text check: a `DATE \@"dd/MM/yy"` field. LibreOffice writes today's date (15/11/26 on this run), broilerdoc the cached 3/13/14. | Not changed: the expected text depends on the day the reference is made. |
| `layout/data/tdf123163-1.docx`, `layout/data/fdo43573-2-min.docx`, `layout/data/tdf170477.docx`, `layout/data/tdf134235.docx`, `ooxmlexport/data/tdf143269_zeroSizeEmbeddings.docx`, `uiwriter/data/tdf155685.docx`, `ooxmlexport/data/tdf138739.docx`, `ooxmlexport/data/tdf164065.docx`, `ooxmlimport/data/n751077.docx`, `ooxmlexport/data/tdf104061_tableSectionColumns.docx`, `ooxmlexport/data/tdf160077_layoutInCellD.docx` | As the issue #234 to #242 notes record: the magazine's column and frame drift, the floating tables in cells, charts drawn from their caches (line, 3D column and doughnut charts), the collapsed Fontwork, the picture frame's outline, the Garamond fallback. | Not changed by this round. |

## Verification

- Cluster, harness at `8a20ca0` and at the final commit (same 39 samples,
  `--jobs 2`): 0 to 14 passing. Fourteen pass: `Cool15691_headerTextWrapsAroundImage`,
  `Cool15691_headerTextWrapsAroundImageLargest`, `n779834`, `fdo76803`,
  `tdf159158_zOrder_1and0equalB`, `tdf159158_zOrder_duplicate_compat15`,
  `fdo77727`, `fdo78300`, `FileWithInvalidImageLink`, `tdf60351`,
  `tdf105143`, `tdf78749`, `watermark-shapetype`, `position-and-rotation`.
  No cluster sample's severity rose; among those still failing,
  `tdf97090` 785.2 to 337.0, `tdf159158_zOrder_zIndexWins` 739.8 to 651.8
  (text only now), `fdo74110` 728.3 to 683.3, `WordArt` 704.8 to 689.8,
  `tdf130314` 766.9 to 761.5; the others are unchanged. Page counts are
  unchanged throughout.
- Regression screen. The layout sweep over all 3,847 `.odt`/`.fodt`/`.docx`/`.rtf`
  samples changed 40 against the baseline. It does not see what the painter
  or the CLI decide (shape outlines, hidden shapes, picture fills, missing
  picture marks, front or back layer, the text dump), so I also rendered every
  sample's first three pages at 12 dpi through the CLI with the harness's font
  arguments, at `8a20ca0` and at `592c42c` (all changes but the turned
  pictures, and with the bold-face measurement that was later reverted), and
  compared the bitmaps: 175 changed. The turned-picture commit changed nine
  more in the layout sweep, all already listed. The harness then ran on the
  union with the cluster, 201 samples, at the final commit: 78 of them failed
  at `8a20ca0`; 61 fail now. Fixed outside the cluster: `fdo83057.docx`,
  `tdf119201.docx`, `ooxmlimport/data/text-copy.docx`. No sample that passed
  before fails. Three are `partial` before and after because LibreOffice
  produces no PDF for them (`tdf38575_fullWidthLine.docx` hangs `soffice`
  locally), and two are crash-regression inputs that only check robustness.
- Severity rose for six failing samples outside the cluster, each explained
  in the table: `tdf105688.docx` (419.8 to 876.1), `tdf149546.docx` (831.2
  to 888.0), `tdf156484.docx` (689.9 to 707.2), `AsCharTxBxTest.docx` (325.7
  to 329.2), `floattable-footer-object.docx` (691.7 to 691.8) and
  `floattable-footer-object-nofit.docx` (486.5 to 486.6). Measured page by
  page at 50 dpi as the share of pixels whose blurred grey differs by more
  than an eighth, every page of these is as close to LibreOffice as before or
  closer, except `tdf105688.docx` (20.3/19.7 % to 20.4/19.7 %): the score
  follows the worst page's ink box and profile, which moved with shapes that
  are now where LibreOffice puts them.
- The reverted bold-face measurement, measured the same way: it turned
  `rtfexport/data/tdf158978.rtf` from passing to failing (2 pages against 1),
  raised `tdf168017.docx` and `tdf131234.rtf`, and fixed only
  `tdf134063.docx`, so it is not in this branch.
- Tests: `DocxIssue249ImagesShapesTests` (Broiler.Documents.Docx.Tests: header
  paragraph frames for DrawingML and VML, hidden shapes and pictures, preset
  outlines and their flips and turns, linked pictures, inline WordArt runs,
  wrapped `behindDoc` objects, VML `h` tokens, VML picture fills, a band read
  against its own section's page, turned floating pictures),
  `DocumentLayoutIssue249ImagesShapesTests` (Broiler.Documents.Pagination.Tests:
  header shapes from their paragraph, a wrapped `behindDoc` shape in front),
  `TextDumpShapeTextTests` (Broiler.Documents.Cli.Tests). Each fails at
  `8a20ca0` (most do not compile there: they use the new model properties)
  and passes now. The whole solution's tests pass (`dotnet test
  Broiler.Documents.slnx`: 5,815 passed, 3 skipped).
- Not covered: the PDF writer places a band's shapes by its own code
  (`PdfPageLayout.PlaceRunningShapes`) and still measures a header's
  paragraph-anchored shape from the page's top.
