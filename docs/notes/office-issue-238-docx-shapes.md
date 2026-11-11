# Office issue #238: DOCX charts, WordArt shapes and rotated frames

The DOCX samples of issue #238 whose difference is a chart, a drawn shape or a
rotated text frame, and one address-block page. I rendered each one locally
against the LibreOffice 24.2 reference PDFs (`office-references-v4`), with the
reference fonts (`--font-dir fonts/0`), at 96 dpi. I scored each render as the
number of ink pixels (darker than 50%) that do not match the reference, with a
2 px tolerance; for the light watermark samples I counted every pixel that is
not white (darker than 96%) instead. To check for regressions, I rendered
every corpus sample that uses a changed feature with the build before and
after the change. The three chart samples were analysed in the
[issue #213 note](office-issue-213.md), which drew a placeholder box for
them, and `TextFrameRotation.docx` in the
[issue #234 note](office-issue-234-docx-blank.md).

| Sample | Cause | Outcome |
| --- | --- | --- |
| `layout/data/testAreaChartNumberFormat.docx`, `layout/data/tdf130031.docx`, `layout/data/tdf130380.docx` | Each holds a DrawingML area chart (`c:areaChart`: a percent-stacked one and two standard ones) with no picture fallback. `DocxChartPreview` drew only clustered column charts, so these became an empty placeholder box. LibreOffice draws the chart from the cached values in `word/charts/chart1.xml`. | Fixed. `DocxChartPreview` now draws standard, stacked and percent-stacked area charts from the cached values. Each series is a filled band in the colour its `c:spPr` states, or the document theme's accent for its index (the image loader now gets the theme). Categories sit on the plot edges for `crossBetween="midCat"`. The first series is drawn in front, as LibreOffice draws it. The value axis is scaled the way LibreOffice and Excel scale it: it starts at zero unless every value is within a sixth of the largest (then half the data range below the smallest), it has a twentieth of the range as headroom, and it uses the finest 1-2-5 step that gives at most ten intervals. That puts tdf130380's axis at 4.4 to 6.2 and tdf130031's at 0 to 35, as in the references. Major gridlines and the frame are drawn. Titles, axis labels, data labels and the legend are not, but the plot leaves the room they take. Mismatch went from 58 897 to 1805, 116 143 to 731 and 94 289 to 3601. |
| `ooxmlexport/data/tdf138739.docx` | A floating VML WordArt shape (`v:textpath on="t"`, string "xxxx") whose path, with `@n` formula references, is only the envelope the text is fitted to. The path reader could not render the formulas, so broilerdoc fell back on the box and filled it with the shape's dark blue: a 187 x 73 pt block. Word draws the text in that fill. LibreOffice 24.2 draws only a hairline, because its import collapses this shape. | Improved. A floating shape with an active text path is now drawn as its text: centred lines in the text path's font, sized to fit the box, coloured with the fill (or the stroke), with no box fill, outline or path. This is the reader inline WordArt already used. Mismatch went from 25 295 to 4556. The remainder is the "xxxx" that LibreOffice does not draw, and a floating table that sits 20 px lower. The same change draws text watermarks as their word instead of a rotated grey bar (see Verification). For that to work, `DocxVmlShapeWalker` now merges a shape's `v:textpath` with its type's, as it merges `v:fill` and `v:stroke`, so `on="t"` on the type survives. It also resolves a `type` that names the shape type without the leading `#`, as LibreOffice does (`watermark-shapetype.docx`). |
| `ooxmlexport/data/TextFrameRotation.docx` | A `rotation:-90` VML text box with `layout-flow:vertical` bottom-to-top text. LibreOffice imports it as an unrotated frame partly off the page, so its page is blank. | Fixed by the vertical text frame change in the [DOCX headers note](office-issue-238-docx-headers.md). With it merged, the page matches the reference (mismatch 0). Nothing was changed for this sample here. |
| `odfexport/data/tdf129520.docx` | Two problems. First, an `ADDRESSBLOCK` field writes a `w:cr` between the runs of its instruction for each line of its format string. broilerdoc drew those five breaks, which pushed the address 127 px down the page. Second, the `Normal` style sets `w:contextualSpacing`, which Word applies as "don't add space between paragraphs of the same style" and LibreOffice imports as its context margin. broilerdoc did not read it, so 15 empty paragraphs each kept the default 10 pt after. | Improved. Line breaks and tabs in a field instruction (between the field's begin and its separator) are field code and are no longer drawn. Page and column breaks there are still honoured. `w:contextualSpacing` is now read, from the paragraph or its style chain. Where two neighbouring paragraphs have the same style, a paragraph with the setting loses its space before, and the paragraph before it loses its space after when it has the setting. A table or cell boundary between them stops this (`DocxDocumentBuilder.ApplyContextualSpacing`). The address now starts at the reference's height. Mismatch went from 2150 to 1814. The rest is the face: the document asks for Garamond, which the reference font set does not have. LibreOffice falls back on DejaVu Serif, a serif face as `fontTable.xml` classes Garamond (`w:family roman`). broilerdoc falls back on its sans host face, whose taller lines put the closing sentence 64 px lower. Choosing the fallback face by the font table's family is a font-resolution change I did not make here. |

## Verification

- Mismatch against LibreOffice 24.2, before and after:
  `testAreaChartNumberFormat` 58 897 to 1805, `tdf130031` 116 143 to 731,
  `tdf130380` 94 289 to 3601, `tdf138739` 25 295 to 4556, `tdf129520` 2150
  to 1814, `TextFrameRotation` 1559 to 0 (with the merged vertical-frame
  change).
- Regression sweeps, before and after, scored against the references:
  - All 4 area-chart samples. `ooxmlimport/data/tdf82824.docx` also improved
    (3092 to 2267). The column chart path was not changed.
  - All 15 samples with a floating VML text path, scored on non-white
    pixels. Eleven improved, among them `textWatermark` (130 100 to 29 179),
    `watermark` (78 968 to 40 362), `tdf134902` (219 559 to 85 463),
    `watermark-shapetype` (213 337 to 161 440) and `fdo78659` (127 390 to
    74 425). Three are unchanged. `FDO78590.docx` scores worse (2209 to
    2445): its WordArt banner was two black bars, and they happened to cover
    LibreOffice's black, path-fitted text. broilerdoc now draws the words, in
    straight lines.
  - All 8 samples that name a VML shape type without `#` and have a reference.
    Only `watermark-shapetype` and `uiwriter/data/tdf147199.docx` changed.
    tdf147199's flow diagram is now drawn (352 567 to 314 402, with the
    contextual spacing below). It now has 5 pages to LibreOffice's 6, where
    it had 6: the blank page broilerdoc used to insert is gone, but the
    diagrams do not yet push the text below them, so one continuation page
    is missing.
  - All 379 samples with a reference that use `w:contextualSpacing` or put a
    line break or tab in a field instruction, before and after both changes.
    25 moved closer to the reference, 6 moved further from it, and 348 did
    not change. The total went from 12 456 218 to 12 400 681. Three samples
    now have LibreOffice's page count (`tdf97090.docx`, `tdf157131.docx`,
    `tdf170602_exportContext.docx`) and one lost it (`tdf147199.docx`, above).
    `tdf170602_exportContext` is the largest of the six that scored worse
    (307 246 to 319 915): it now has LibreOffice's 18 pages, but the content
    on them is offset. In `tdf144563.docx` (15 835 to 18 661) the numbered
    items now stack without gaps, as in LibreOffice, but they sit a few
    pixels off the reference's lines. The other four changed by less than
    1100.
- Tests: `DocxOfficeIssue238ShapesTests` (percent-stacked and standard area
  charts, the automatic value axis, a chart with no cache, floating WordArt
  with a bare shape-type reference, line breaks in a field instruction,
  contextual spacing within a style and across a table). The Docx (1012),
  core (326) and Pagination (437) test projects pass.
