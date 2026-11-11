# Office issue #240: DOCX pie and stacked column charts, VML preset guides

The DOCX samples of issue #240 whose difference is a chart or a drawn shape,
and one long document. I rendered each one locally against the LibreOffice
24.2.7.2 reference archive (`/root/refs`), with the reference fonts
(`--font-dir fonts/0`, `--font-set office-fonts-2`), at 96 dpi, and report the
harness's rendering similarity (threshold 80%). To check for regressions I
rendered every corpus sample that uses a changed feature with the build
before and after the change. Earlier chart work is in the
[issue #238 note](office-issue-238-docx-shapes.md) (area charts) and the
issue #213 note (clustered columns, placeholder box).

| Sample | Cause | Outcome |
| --- | --- | --- |
| `layout/data/tdf134866.docx`, `tdf129054.docx`, `tdf137154.docx`, `tdf138018.docx` | Each holds a DrawingML pie chart (`c:pieChart`) with no picture fallback. `DocxChartPreview` drew only clustered columns and area charts, so each pie became an empty placeholder box with a rule across it (the "ink box blank vs inked" difference). LibreOffice draws the pie from the cached values in `word/charts/chart1.xml`. | Fixed. `DocxChartPreview.RenderPie` draws the first series' slices clockwise from twelve o'clock, turned by `c:firstSliceAng`. Each slice takes its `c:dPt` fill, else the series' fill, else (with `c:varyColors`, on by default for a pie) the theme accent for its index, cycling darker (lumMod 60%) and lighter (lumMod 80% + lumOff 20%) after six, as Office does. Each slice is outlined with the `a:ln` its data point or series states (tdf129054's red 1 pt lines, tdf137154/tdf138018's white 1.5 pt separators; a single whole slice shows its edge as a radius, as in LibreOffice). The pie is centred in the plot area, which leaves room for a title (about 34 pt, not a fixed fraction, so small charts like fdo78474 keep their pie small), a side legend sized from its longest category, and labels placed `outEnd`. The chart space's own `c:spPr` now sets the background and border. Labels, titles and legends are not drawn. 52.5% to 100%, 52.8% to 100%, 48.8% to 99.96%, 50.6% to 99.29%. |
| `layout/data/tdf75659.docx` | A percent-stacked column chart (`c:barChart`, `barDir col`, `grouping percentStacked`); only the clustered grouping was drawn, so it was a placeholder too. | Fixed. `RenderStackedColumns` draws stacked and percent-stacked column charts: per category one column (`gapWidth`, default 150), the series' segments stacked up from zero (negative values down), in the series' `c:spPr` fill or the accent for its index, with `c:dPt` overrides. The value axis is 0 to 100% in tenths for percent-stacked, and for stacked the automatic axis of the #238 note over the stack totals. Major gridlines take their `c:spPr` line, the category axis line its own. The plot leaves room for the title and a top, bottom or right legend (or one placed by hand at the right, legend-itemorder-min). 46.0% to 99.90%. |
| `ooxmlexport/data/mso-spt180.docx` | A VML line callout (`o:spt="180"`) whose path names its guides (`m@1@0l@3@2`) and whose stroke is `weight="28440"`. Two problems. The path reader knew no `@n`/`#n`, so the shape fell back on its box. And the bare weight was read as points: a 28 440 pt outline, whose four sides drew a cross over the whole page. LibreOffice reads a unitless VML measure as EMUs (`oox/source/vml/vmlformatting.cxx`, `ConversionHelper::decodeMeasureToEmu`), so the weight is 2.24 pt. | Fixed. `DocxVml.TryStrokeWeight` reads a bare `strokeweight`/`v:stroke weight` as EMUs. `DocxVmlPath` now evaluates `v:formulas` (val, sum, prod, mid, abs, min, max, if, mod, sqrt, sin, cos, tan, atan2, cosatan2, sinatan2, sumangle, ellipse over numbers, `#n`, earlier `@n`, width/height/xcenter/ycenter...) and splits `@1@0` into two parameters. A shape's partial `adj` (`",254405"`) keeps its type's value for an empty field, as LibreOffice does (`DocxVmlShapeWalker.MergeAdjust`). Device measures (`pixelWidth`, `emuWidth`) are left unknown, so the picture frame type 75 stays a box. A preset path whose guides carry it more than 5% outside its box falls back on the box (the picture would cut it off); this callout's leader does, so it is drawn as its filled box with a 2.24 pt outline. The leader and its diamond ends are not drawn. 75.7% to 99.98%. |
| `ooxmlexport/data/tdf170602_exportContext.docx` | 17 pages to LibreOffice's 18; divergence from page 2. Page 2's body matches the reference line for line. The visible differences there are in the running header and footer: the section's `w:pgNumType w:fmt="upperRoman"` is not read (broilerdoc writes "2" for "II"; only `w:start` is read in `DocxSections`), the `STYLEREF "Überschrift 1"` field shows its stale cached result "Inhalt>" where LibreOffice recomputes "Inhalt", and the header/footer paragraph borders of the `Kopfzeile`/`Fußzeile` styles are missing. The lost page further on is a pagination difference I did not trace. | Not fixed. It is a header/field/pagination sample rather than a shape one. A fix needs a page-number format on the section model and in the PAGE field (a shared model and layout change), STYLEREF evaluation against the page's headings, and a trace of where the 18th page's content goes. 11.5% unchanged. |

## Verification

- Sample similarity at 96 dpi, before and after: tdf134866 52.51% to 100%,
  tdf129054 52.75% to 100%, tdf137154 48.81% to 99.96%, tdf138018 50.63% to
  99.29%, tdf75659 46.00% to 99.90%, mso-spt180 75.70% to 99.98%,
  tdf170602_exportContext 11.51% unchanged.
- Chart corpus: all 48 DOCX inputs with a `word/charts/chart*.xml`. Pixel
  checks passed went from 27 to 38. Twelve samples improved, none moved down:
  besides the five above, `legend-itemorder-min` 47.46% to 98.00%,
  `outside_long_data_label` 68.76% to 92.37%, `tdf137116` 68.82% to 97.29%,
  `odfexport/tdf134987` 66.54% to 97.03%, `fdo76098` 65.15% to 99.86%,
  `tdf123873` 59.84% to 99.41%, `fdo78474` 97.15% to 99.12%. The clustered
  column and area paths are unchanged.
- VML corpus: all 186 DOCX inputs with `v:formulas` or a bare stroke weight.
  Pixel checks passed went from 139 to 140. Up: `mso-spt180` (75.70% to
  99.98%), `test76317` (91.92% to 99.82%), `dml-textshapeB` (99.44% to
  99.62%). Down: `dml-textshape` 99.57% to 99.51% (still passing): its elbow
  connector, formerly drawn as a rectangle outline, is now drawn as an elbow,
  as in the reference, but its middle arm sits some pixels left of
  LibreOffice's and the arrowheads are not drawn. `fdo73215` has the same kind
  of elbow connectors and ends where it started (99.88%, 99.84% on a single
  run). Two intermediate versions were refined on this corpus:
  `tdf164065` (72.35% to 71.19%) lost its picture frame's outline until
  device measures were left unknown, and `fdo73215` lost connectors whose
  guides put them outside the box until such paths fell back on the box and
  edge strokes were kept inside the picture.
- Tests: `DocxOfficeIssue240DocxShapesTests` (pie slice order and colours,
  pie slice outline, percent-stacked and stacked columns with their axis,
  parameter splitting at guide references, formula evaluation, callout
  fallback with an EMU stroke weight, elbow connector with a partial `adj`,
  bare stroke weight). The Docx test project passes (1038). The solution
  builds with no errors; its one warning (xUnit2030 in
  `DocumentLayoutIssue236Tests.cs`) predates this change.
