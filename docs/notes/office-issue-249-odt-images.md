# Office issue #249: ODT pictures, metafiles and formulas

These 33 samples were flagged with `odt.image.*` diagnostics. I reproduced each on
Linux with the `run-office-conformance.sh` runner (LibreOffice 24.2.7.2, office-fonts-2,
144 dpi, two jobs) at commit `8a20ca0`. Where I needed to know what Writer does beyond
the reference, I converted cut-down documents with the same local LibreOffice and
rasterized the PDF with `pdftoppm`. Similarity is the runner's pixel score; "sev" is its
severity score. Earlier rounds are in the issue #236, #238, #240 and #242 notes.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `globalfilter/data/document_with_linked_graphic.odt`, `globalfilter/data/graphic_shape.odt` | The picture links `../linked_graphic.jpg`. By ODF 1.2 part 3, §3.7 a relative link in a package resolves against the package as a folder, so `../name` is a file in the document's own directory (the corpus has it there). Earlier rounds declined it as a policy question (ADR 0004). | **Fixed by an explicit opt-in**, 0% → passing and 57.3% → passing. The user asked for this item. `OdtReadOptions` (new) carries a host resolver; the ODT reader asks it only for a package link of exactly the form `../<name>`, by the bare, percent-decoded name: no deeper path, no other traversal, never a flat document's link. `broilerdoc render --allow-local-images` supplies it with HTML's sibling-image resolver, so the same name rules and byte limits apply. Without the flag nothing changes, and a link the resolver cannot answer reads as before (an unmarked empty frame). `DocumentIo.Load` now takes per-codec options, so each codec is handed only its own option type: the flag used to give `HtmlReadOptions` to every codec, and a CSV or PDF render with it was refused. The runner passes the flag for ODT/OTT/FODT as it does for HTML (`ExtrasConformance.RenderArguments`). ADR 0004 and `odt.md` record the opt-in. |
| `odfimport/data/fdo68839.odt` | `graphicsXXX`'s SVG (preferred over its PNG since #242) is `<svg>` with a circle and no `width`, `height` or `viewBox`. The rasterizer needed a size and declined it, so the PNG (a page with a dot in its corner) was drawn. A cut-down showed LibreOffice takes such a picture's range from what it draws, strokes included, and stretches that to the frame. | **Fixed**, 11.3% → passing. `SvgRasterizer` renders an SVG with no size and no view box over its content's bounds. Only one other corpus SVG has that form (`odfexport/data/tdf103567.odt`, passing before and after: 85.7 → 85.3% profile, severity 652 → 649). |
| `odfimport/data/tdf96113.odt`, `odfexport/data/tdf144319.odt`, `layout/data/tdf40260.odt`, `tiledrendering/data/large-chart-labels.odt` (also `tdf105481.odt`, `tdf130680.odt`, `tdf159443.odt`) | A formula's replacement SVM is nothing but text actions (FONT, TEXTALIGN, TEXTCOLOR, STRETCHTEXT), and a chart's labels are TEXTARRAYs. `SvmRasterizer` skipped text: a formula drew as the marked missing-picture box and charts had no labels. | **Fixed**: tdf96113, tdf144319, tdf40260 and large-chart-labels pass. `SvmRasterizer` reads FONT (vcl's `ReadImplFont`: family, height, weight, italic, orientation, character set), TEXTCOLOR, TEXTALIGN (a FONT resets it to top) and TEXT, TEXTARRAY (DX array) and STRETCHTEXT (spread over its width), strings as `ReadUniOrByteString` streams them (UTF-16 with a u32 count in the Unicode character set) with the version-2 UTF-16 copy preferred, PUSH/POP of font, text colour and alignment. Text is drawn through the renderer onto a layer, as `EmfRasterizer` draws EMF text. tdf105481, tdf130680 and tdf159443 improve but still fail (see below). |
| (same samples) | Metafile text is drawn while the document is read, but `broilerdoc render` installed the font mapping after reading, so it was drawn in the host face (tdf96113's Liberation Serif italic came out as DejaVu Sans). | **Fixed** in its own commit: `RenderCommand` creates the pipeline, which installs the mapping, before it reads. This takes tdf96113 from 76.8% to passing. It also changes EMF text in DOCX and RTF previews; see "Effect on other samples". |
| `odfexport/data/tdf144319.odt` | `Formula-14` has an object folder but no `ObjectReplacements/Formula-14`. broilerdoc drew an empty frame (the #234 rule for a missing entry). LibreOffice 24.2 prints its object placeholder icon there. Removing the replacement from tdf96113.odt, with or without the object folder, shows the same icon; a Calc object without its replacement is rendered afresh, as charts are. | **Fixed** with the SVM text above. A formula (`draw:object` naming a folder the manifest declares `application/vnd.oasis.opendocument.formula`) whose replacement is missing gets the placeholder icon. Charts and other objects keep the empty frame. |
| `ooxmlexport/data/math-bold-italic-export.fodt` (passing; regressed by an intermediate version) | A flat document's `draw:object` holds its formula as MathML, with an SVM replacement in the `draw:image` beside it. LibreOffice 24.2 prints neither (also `uiwriter/data/tdf157533.fodt`). With SVM text, the replacement's text appeared. | **Fixed**: such a frame (inline MathML, no `xlink:href`, a replacement present) is an unmarked empty frame. 80.9% before, 99.99% after. One with no replacement at all is skipped as before, so `text-with-formula*.fodt` and `embedded_formula.fodt` keep their layout. |
| `odfimport/data/tdf109228.odt` | The OLE replacement SVM is one BMPSCALE (a 24-bit DIB). Bitmap actions were skipped. | **Fixed**, 41.3% → passing. `SvmRasterizer` draws BMPSCALE and BMPEXSCALE without transparency, reading vcl's DIB with its file header, plain or `ZCOMPRESS` (zlib behind coded size, uncoded size and real compression), and composites it with `EmfRasterizer`'s blit. A BitmapEx with a mask or alpha is not drawn. `ooo96040-2.odt`'s two SVMs (BMPEXSCALE, ZCOMPRESS) now draw too. |
| `htmlexport/data/tdf162282.odt` | An EMF of SETARCDIRECTION, ARC and PIE, none of which `EmfRasterizer` knew, so the picture was the marked box. | **Fixed**, 62.4% → passing. ARC, CHORD, PIE and ARCTO are drawn as GDI defines them: from the radial through the start point to the one through the end point, counterclockwise on the page unless SETARCDIRECTION says clockwise; equal radials make the whole ellipse; a chord closes with a line, a pie through the centre. It is the only corpus metafile with these records. |
| `ooxmlexport/data/bnc834035.odt`, `ooxmlexport/data/tdf131537.odt` (also `tdf112169.odt`) | WMF pictures and OLE replacements. `WmfRasterizer` (#242, translating WMF records to EMF) was wired into DOCX only; the ODT loader reported `odt.image.format`. | **Fixed** for bnc834035 (47.0% → passing) and tdf131537 (77.5% → passing, with the next row). The loader rasterizes WMF and ranks it as LibreOffice does (1010, between SVM and EMF). tdf112169's WMF now draws its 424 rectangles and 131 text runs (53.2% → 64.7%); the sample still fails on its body text. |
| `ooxmlexport/data/tdf131537.odt` | Its WMF draws the OLE icon as Windows draws an icon: a monochrome mask with SRCAND, then the colour picture with SRCINVERT over the same place. Only SRCCOPY blits were drawn, so the icon was missing. | **Fixed**. LibreOffice merges such a pair into one picture shown where the mask is black (`MtfTools::ResolveBitmapActions`); `EmfRasterizer` now does the same for STRETCHDIBITS (SRCAND followed by SRCINVERT or SRCPAINT at the same place). An AND alone leaves the bitmap's non-white pixels, as on a white page; an XOR or OR alone is still not drawn. |
| `odfimport/data/draw-image-link.fodt`, `odfimport/data/draw-object-link.fodt` | A picture and an object linked to `http://192.0.2.1:12345/…`, a TEST-NET address. LibreOffice draws nothing because the fetch fails. With a reachable server it draws the picture (checked with a local HTTP server); a file link it cannot find also draws nothing, and one it finds draws the object's icon. | **Not fixed, by policy.** What LibreOffice draws depends on whether the URL answers, which broilerdoc never asks (ADR 0004). It keeps the marked box (picture) and the placeholder icon (object). Special-casing the address would fit the sample, not the behaviour. |
| `ooxmlexport/data/croppedAndRotated.odt` | The frame turns its picture by 270° (`draw:transform`) and crops it (`fo:clip`). The ODT reader applies neither to a picture. | **Not fixed.** Picture cropping and rotation for ODT frames is a feature of its own (10 corpus samples carry a non-zero clip or a turned picture, 7 of them passing). |
| `odfexport/data/tdf118502.odt` | The frame's first image is an EPS without a preview (with `loext:mime-type="image/x-svm"`), the second a PNG. LibreOffice keeps the EPS and draws its placeholder: a red frame with the `%%Creator`, `%%CreationDate` and `%%LanguageLevel` comments. broilerdoc draws the PNG. | **Not fixed.** It needs an EPS placeholder renderer; one sample. |
| `uiwriter/data/tdf130680.odt` (68.4% → 72.4%) | Formulas now draw, but (1) their StarSymbol private-use characters (U+E0C6, π) need OpenSymbol's glyphs, which neither the pinned fonts nor a Unicode table here provide, and (2) as-character formula frames are aligned by `style:vertical-pos="middle"`/`vertical-rel="text"`, which the layout does not apply to pictures. | **Not fixed.** (2) is an inline-object alignment feature in the layout. |
| `layout/data/tdf105481.odt` (29.6% → 29.9%), `layout/data/tdf159443.odt` (71.7% → 74.8%) | tdf105481: the caption frame sits lower than LibreOffice's and its text is missing from the text export. tdf159443: the stored chart replacement has a data table LibreOffice's live rendering does not (#236 note). | **Not fixed.** Frame layout and live chart rendering. |
| `ooxmlexport/data/ooo96040-2.odt` (37.9%), `ooxmlexport/data/tdf112169.odt` (64.7%) | Their pictures now draw; the pages still differ in line numbering, form controls and body text. | **Not fixed** (other features). |
| `uiwriter/data/FrameInHiddenSection.fodt`, `layout/data/footer-fly-table.fodt`, `layout/data/tdf148897.odt`, `layout/data/toc-inline-heading-order.fodt`, `uiwriter/data/fdo114306_2.odt`, `uiwriter/data/redline_fly_duplication_at_para_start_outside.fodt`, `odfexport/data/tdf104254_noHeaderWrapping.odt` | Frames in hidden sections, a footer fly table, footnote pagination, an as-char frame in a heading, form templates, tracked-change frames and a header text box. The pictures in them already draw as LibreOffice's. | **Not fixed.** Layout features (odt-layout territory). |
| `odfexport/data/tdf152710.odt`, `ww8export/data/tdf132726.odt` (79.0%) | Body text: tdf152710's text is in a face the pinned set lacks; tdf132726 differs by highlighting coverage. The pictures match. | **Not fixed** (text rendering). |
| `ooxmlexport/data/tdf134951_duplicates.odt` (99.91%) | LibreOffice puts the endnote on its own page in the `Endnote` page style (portrait); broilerdoc keeps the landscape page. | **Not fixed** (endnote pages). |
| `odfimport/data/tdf107392.odt`, `odfimport/data/tdf151375.ott` (text) | LibreOffice's text export includes a page-anchored text frame's "hello" before an otherwise picture-only paragraph; tdf151375 is a date field's format. | **Not fixed** (text export and fields, odt-text territory). |
| `htmlexport/data/tdf161979_metafile.fodt` (79.3%) | As the #236 note says: Image2 is EMF+ records in SVM comments. | **Not fixed** (needs an EMF+ player). |

## Effect on other samples

I ran 156 samples before (a clean export of `8a20ca0`) and after, with every check:
this cluster's 33; every ODT/OTT/FODT/DOCX/RTF whose metafiles draw text (40 SVMs with
text actions, 76 EMFs with EXTTEXTOUT, 3 RTFs with EMF pictures); every ODT with a WMF
(6); every flat document with an inline formula (6); every metafile with SRCAND, SRCINVERT
or SRCPAINT blits (13); every SVG with no size or view box (2); `htmlexport/data/URLs.odt`
and `ooxmlexport/data/hyperlink-field.odt` (other `../` links); `linked_ole.fodt`.

- Passing went from 106 to 117. Newly passing are the 11 samples of this cluster listed
  in the Verification section. No check that passed before fails now.
- 46 samples score better without changing outcome. Among the passing ones, charts whose
  replacements have labels (`tdf108021`, `tdf122800`, `tdf97601`, `tdf117225`,
  `charttable`, `fdo51550`, …) gained their text; `math-bold-italic-export.fodt` went
  from 80.9% to 99.99% (its inline formulas are now empty frames, as in LibreOffice).
- The font mapping before the read moved EMF text in DOCX and RTF previews from the
  host face to the mapped one. Most of these got closer (`fdo80410.docx` 84.8 → 90.0%
  profile, `embedded-xlsx.docx`, `fdo79969_*`, `tdf122594`, `ole-anchor`). Five scored
  slightly worse:
  - `ooxmlexport/data/mathtype.docx` (failing, severity 697.3 → 705.1),
    `rtfexport/data/mathtype.rtf` (failing, 705.6 → 712.7) and
    `ooxmlexport/data/fdo77759.docx` (failing, 353.0 → 356.2). LibreOffice 24.2 does not
    draw these OLE previews at all (an Equation.DSMT4 object, and an Excel object it shows
    as text only). broilerdoc draws the preview either way; now in the mapped face instead
    of the host's. The gap is the preview being drawn, which is a DOCX/RTF OLE question.
    The change is a separate commit so it can be reviewed on its own; without it
    `tdf96113.odt` stays at 76.8%.
  - `ooxmlexport/data/tdf99631.docx` and `odfexport/data/tdf99631.docx` (passing,
    severity 319.8 → 320.4).
- `ooxmlexport/data/embedded_chart.odt` still passes, but its severity rose from 416 to
  645 (profile 99.87 → 99.87%): the stored replacement's labels now draw, and the #236
  note's 0.7 cm offset between the stored chart and LibreOffice's live one widens the ink
  box. `odfexport/data/tdf103567.odt` (85.7 → 85.3% profile, severity 652 → 649) is the
  other SVG with no size.
- Layout sweep over all 3,847 `.odt`/`.fodt`/`.docx`/`.rtf` samples: no sample's layout
  changed. (An intermediate version that gave a replacement-less inline formula an empty
  frame changed `text-with-formula.fodt`, `text-with-formula-one-paragraph.fodt` and
  `embedded_formula.fodt`; that case keeps its old behaviour now.)

## Verification

- This cluster, before → after: 0 → 11 of 33 pass. Fixed:
  `document_with_linked_graphic.odt` (0% →), `graphic_shape.odt` (57.3% →),
  `fdo68839.odt` (11.3% →), `tdf109228.odt` (41.3% →), `tdf162282.odt` (62.4% →),
  `large-chart-labels.odt` (68.9% →), `tdf131537.odt` (77.5% →), `tdf96113.odt` (76.2% →),
  `bnc834035.odt` (47.0% →), `tdf40260.odt` (69.5% →), `tdf144319.odt` (61.7% →).
  Improved but failing: `tdf112169.odt` 53.2 → 64.7%, `tdf130680.odt` 68.4 → 72.4%,
  `tdf159443.odt` 71.7 → 74.8%, `tdf105481.odt` 29.6 → 29.9%, `ooo96040-2.odt`
  (severity 803 → 798). The other 17 are unchanged.
- Tests (each fails before the change and passes after, except the guards noted):
  - `OdtIssue249OdtImagesTests` (Broiler.Documents.Odt.Tests): a `../name` link asked of
    the resolver once, by its bare name; no resolver keeps the empty frame; deeper,
    escaped, backslash, `./../` and in-package links never asked (guard); an escaped
    name decoded; a host answer over `MaxBinBytes` not drawn; a flat document never
    asked; a formula with no replacement gets the icon, a chart or spreadsheet keeps the
    empty frame; a WMF picture rasterized; an inline MathML formula an empty frame.
  - `SvmRasterizerIssue249OdtImagesTests` (Broiler.Documents.Tests): STRETCHTEXT drawn at
    its baseline; UTF-16 strings and DX placement; POP restoring font and colour; text
    before any FONT still not drawn (guard); BMPSCALE plain and ZCOMPRESS; BMPEXSCALE only
    without transparency.
  - `SvgRasterizerIssue249OdtImagesTests`: an SVG with no size or view box sized to its
    content (strokes included), moved to its corner; a stated size or view box still
    wins (guard); a picture of nothing still nothing (guard).
  - `EmfRasterizerIssue249OdtImagesTests`: PIE counterclockwise by default and clockwise
    after SETARCDIRECTION; ARC with equal radials; CHORD; a SRCAND mask then SRCINVERT
    picture; an XOR alone not drawn (guard).
  - Broiler.Documents.Cli.Tests: `ImageAndDiffTests.Odt_Render_Only_Loads_Pictures_Linked_Beside_The_Package_With_Explicit_Opt_In`
    (mirrors the HTML test), `Allow_Local_Images_Leaves_Other_Formats_Their_Own_Options`
    (a CSV render with the flag was refused), and
    `MetafileTextFontTests.Metafile_Text_Drawn_While_Reading_Goes_Through_The_Font_Mapping`.
  - Broiler.Documents.Office.Tests: `HtmlRenderArgumentTests.Only_Html_And_Odt_Samples_May_Load_Pictures_Beside_Themselves`.
- `dotnet test Broiler.Documents.slnx`: 5,827 tests, 5,824 passed, 3 skipped (as before),
  none failed.
