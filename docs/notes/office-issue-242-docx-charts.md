# Office issue #242: DOCX bar, 3D column and line charts, locked canvases

The DOCX samples of issue #242 whose difference is a DrawingML chart or a
drawing canvas (diagnostics `docx.image.shape` and `docx.diagram.object`). I
rendered each one locally against the LibreOffice 24.2.7.2 reference archive,
with the reference fonts (`--font-dir fonts/0`, `--font-set office-fonts-2`),
at 96 dpi, and report the harness's rendering similarity (threshold 80%). To
see the similarity of passing samples too, I ran the harness with
`--threshold 100`. To check for regressions I rendered every corpus DOCX that
holds a chart, a diagram or a locked canvas with the build before and after
the change. Earlier chart work is in the
[issue #240 note](office-issue-240-docx-shapes.md) (pies, stacked columns) and
the [issue #238 note](office-issue-238-docx-shapes.md) (area charts).

| Sample | Cause | Outcome |
| --- | --- | --- |
| `layout/data/tdf128996.docx`, `layout/data/tdf126244.docx` | Each holds a clustered horizontal bar chart (`c:barChart`, `barDir bar`) with no picture fallback. `DocxChartPreview` drew only vertical columns, so each became an empty placeholder box with a rule across it ("ink box blank vs inked"). tdf128996 states a manual inner plot layout; tdf126244 lays its plot out automatically around two-level category labels ("Categoria 1 Categoria 1 Categoria 4" under "2011"/"2012" and "FIRST LEVEL"), a title and a bottom legend. | Fixed. A new bar renderer (`DocxChartBars.cs`) draws clustered bars in either direction: the categories run bottom to top for bars, the first series lowest in its category; the slots follow LibreOffice's `CategoryPositionHelper` (`gapWidth`, `overlap`); the value gridlines take their `c:spPr` line; the category axis line sits at zero. The plot is placed as LibreOffice places it: a manual `edge` inner layout as stated, otherwise the chart less a 2% margin, the title (2.5 times its font size, with extra lines when a rich title breaks at four fifths of the width), the legend (by position, from its longest entry) and the axis labels, whose room is estimated from the label texts and Carlito's average advances (one extra rotated line per further category level). The legend keys are drawn, in reverse series order for horizontal bars, as LibreOffice lists them. Text is not drawn. 50.37% to 99.56%, 47.58% to 99.14%. |
| `ooxmlexport/data/chart-prop.docx`, `ooxmlexport/data/chart-dupe.docx` | Clustered 3D column charts (`c:bar3DChart`, right-angled axes, cylinders and boxes): placeholders as above. | Fixed. The bar renderer draws a 3D column chart as LibreOffice's right-angled projection shows it: the plot keeps room right and up for the scene's depth, which follows `BarChart::getPreferredDiagramAspectRatio` (one category's depth against the categories and the series' slots) seen under `rotX`/`rotY`; the back wall carries the value gridlines, the left wall their continuation to the front axis, and the floor's edges close the box. A box column shows a lit front (1.1 times its colour), a darker top and a shaded right side (0.875); a cylinder a shaded round body with an elliptic top. Columns stand in the middle of the depth (`gapDepth`). A 3D axis takes no headroom: LibreOffice widens only a 2D chart's automatic axis, so chart-prop's runs 0 to 5 in halves, not 0 to 6. Series with no fill take the theme accent for their `c:idx`. 52.07% to 98.68%, 46.49% to 98.84%. |
| `ooxmlexport/data/fdo76249.docx` | Not SmartArt: a floating text box holds an inline `lc:lockedCanvas` with a picture and two text shapes ("ATHENA", "Proyecto"). The canvas group is 5 875 373 EMU wide but its frame is 1 007 745: LibreOffice scales the children to the frame and keeps their font sizes, so each text box is a few points wide and its 66 pt and 54 pt text breaks a letter per line, beside a small picture. broilerdoc drew the canvas at the group's own size (the text on one line, off the page's right edge) and skipped the `a:pic` (`docx.diagram.object`). | Partly fixed. A locked canvas is now scaled from its group's child space to the graphic frame's extent (`DocxReader.ReadDiagram`), and `DocxDiagram.ConvertTree` turns its `a:pic` into the `pic:pic` a drawing group holds, so the picture is drawn. The letters now stack in the narrow boxes as in the reference. What remains: LibreOffice grows the centre-anchored, auto-fit "ATHENA" box about its middle and then places the inline group by its grown bounding box, which moves the whole canvas some 265 px lower; broilerdoc grows the box downwards from its top. 42.28% to 82.69% (passes). |

Along the way, for the regression corpus:

- 2D clustered column charts now go through the bar renderer instead of the
  old fixed-margin column drawing (Office 2007 colours, gridlines in a fixed
  grey, no room for labels). The new path also scales the chart's text as
  LibreOffice does: text sizes are relative to a 16 x 9 cm page and a chart
  drawn at another size scales them by the smaller ratio
  (`RelativeSizeHelper::calculate`); a small chart in a footer
  (`chart-in-footer`) has 3 pt labels. It allows no more value intervals than
  the axis has room for labels (`VCartesianAxis::estimateMaximumAutoMainIncrementCount`),
  which gives `xaxis-labelbreak` its 0 to 5 in units on a short axis, and room
  for category labels rotated by their `a:bodyPr rot`.
- `AutoScale` measures a single value (or equal values) from zero; such charts
  (`long_legendentry`, `tdf122014`, `tdf134146`, `tdf134676`) were placeholders.
- Line charts (`c:lineChart`, standard grouping) and column-and-line
  combinations are drawn: each group on the value axis its `c:axId` names, the
  lines in their `a:ln` (2.25 pt unless stated), broken at missing values.
  Markers are not drawn.
- Strict OOXML writes colour modulations as percentages (`lumMod val="15%"`);
  `DocxDrawingColor` read them as nothing, so `strict.docx`'s light grey
  gridlines and frame were black.
- Horizontal and vertical chart rules are filled as whole pixels at least a
  point wide, so that none falls between two sampled rows when the picture is
  scaled to the page (tdf115557's chart lost one gridline).

## Verification

- Sample similarity at 96 dpi, before and after: chart-prop 52.07% to 98.68%,
  chart-dupe 46.49% to 98.84%, tdf126244 47.58% to 99.14%, tdf128996 50.37% to
  99.56%, fdo76249 42.28% to 82.69%. All five pass.
- Regression corpus: the 66 DOCX inputs with a `word/charts` or
  `word/diagrams` part or a chart or diagram graphic in `document.xml`, and the
  7 more with an `lc:lockedCanvas` (73). Pixel checks passed went from 58 to
  67. 30 samples improved, among them `tdf143476_lockedCanvas_image_line`
  (0% to 99.89%), `tdf143476_lockedCanvas_twoShapes` (68.75% to 99.33%),
  `xaxis-labelbreak` (56.33% to 99.00%), `tdf138773` (61.91% to 95.07%, a
  column-and-line chart), `tdf122225`, `tdf129095`, `tdf130969` (86-88% to
  98.6-99.7%) and `Chart_Plot_BorderLine_Style` (85.41% to 90.57%). Down:
  - `tdf115557` 91.21% to 87.26% (still passing). LibreOffice draws the chart,
    which is in a footnote, as its "missing object" icon; the old drawing
    happened to be closer to the icon.
  - `chart-in-footer` 99.43% to 98.67% and `tdf132956` 94.08% to 93.70% (still
    passing). tdf132956's legend is placed by hand as a row across the chart;
    its keys are drawn stacked at the left.
  - `tdf134235` 60.57% to 56.85% (failing before and after). Its line chart has
    one point and no line, so the page is the frame, the gridlines and text:
    a long title that LibreOffice breaks into two lines. The plot now starts
    where LibreOffice's does, below both title lines, but without the title
    text drawn the ink profile matched better when the gridlines started
    higher.
- Still failing in the corpus: `tdf143269_zeroSizeEmbeddings` (a 3D column
  chart with `grouping standard`, series one behind the other in depth),
  `tdf124398_groupshapeChart` (scatter charts), `uiwriter/tdf155685`
  (doughnut charts), `fdo43641` and `fdo78658` (unchanged), `tdf134235`.
- Tests: `DocxIssue242ChartTests` (horizontal bar order and extent, 3D box
  shading, 3D axis without headroom, single value from zero with fewer
  intervals, text scale, column layout room, line on a secondary axis, Strict
  percentages, locked canvas scaled to its frame). The Docx test project passes
  (1069) and the Pagination tests pass (466). The solution builds with no
  warnings.
