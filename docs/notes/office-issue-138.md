# Issue 138: page fills, RTF fields, floating tables and drawing geometry

Investigations of
[issue 138](https://github.com/Broiler-Platform/Broiler.Documents/issues/138)
(fetched 2026-10-23; last updated 2026-10-22T23:08:43Z). All 30 ranked samples
were downloaded unchanged from LibreOffice's `sw/qa/extras` tree; the first
baseline is commit `793f46c` (includes issue 137). Each batch compared the same
30 inputs with frozen CLI copies on Windows: LibreOffice 26.2.6.3 (build
`8221e31b3ac356a1623c672912a3d2b492f7e3d1`), Poppler 26.07.0, unpinned fonts,
72 DPI, one job, a 40-page cap, all axes and the default 80% threshold. This
is not the issue's Linux 144-DPI run. Three batches (ten, ten and five
mechanisms) took checks from 40/90 to 43/90 with no lost passes or skips; they
do not claim that many conforming documents. No corpus files, fonts or binary
fixtures were committed. The comparison command:

```powershell
dotnet run --project src/tests/Broiler.Documents.Office --no-build -c Release -- `
  --extras-dir "$env:TEMP/broiler138-samples" `
  --tool "<frozen-cli>/broilerdoc.exe" `
  --soffice 'C:\Program Files\LibreOffice\program\soffice.com' `
  --dpi 72 --jobs 1 --report "<report>.json" --issue-out "<issue>.md"
```

## Page fills and RTF field text

1. **Embedded bitmap fills** load through the picture package, size-limit and
   resource-admission path, keeping their bytes.
2. **Repeated bitmaps** tile at natural or stated size, clipped to the fill
   bounds (raster).
3. **Stretch and single-image** repeat modes are kept, single images aligned
   to the simple reference points; ODT round trips keep modes and sizes.
4. **Hatch fills:** single/double/triple directions keep colour, distance and
   angle; a transparent hatch gets no solid background; raster and PDF clip
   strokes to the bounds.
5. **Centred gradients:** radial, ellipsoid, square and rectangular geometry,
   centre and rotation are kept (they flattened to the border colour). One
   sampler serves raster and PDF.
6. **Gradient controls:** border, endpoint intensity and uniform colour
   opacity affect the paint. ODT writing keeps border/opacity and writes
   intensity-adjusted endpoint colours.
7. **Page-fill bounds:** `draw:background-size="border"` paints inside the
   margins, `full` covers the paper; each page style keeps its own bounds. ODT
   writing puts the setting in a drawing-page style (which LibreOffice uses)
   plus the page-layout fallback.
8. **Legacy `style:background-image`** is read through the same image gate
   when no drawing fill overrides it. External links stay unfetched and are
   reported; `draw:fill="none"` stops the fallback.
9. **RTF field instructions:** `u`/`uc`, escaped bytes, literal text and
   escaped delimiters keep their instruction text; Unicode fallback bytes are
   consumed. Nested fields have independent instruction/result state, so
   non-ASCII hyperlink targets and ruby operands survive.
10. **Empty-result ruby fields:** a recognised `EQ` ruby overlay without a
    cached result contributes its base text; a nonempty cache wins. Fallback
    happens at the field boundary in its header/body destination, without
    evaluating equations. Ruby annotation layout is unsupported and reports
    `rtf.field.ruby`.

Per the
[ODF 1.3 schema](https://docs.oasis-open.org/office/OpenDocument/v1.3/os/part3-schema/OpenDocument-v1.3-os-part3-schema.pdf),
bitmap and hatch definitions belong in named styles and background bounds in
drawing-page properties; authored LibreOffice renders confirm this. On
400×240-pt paper with 20-pt margins both painters leave corners white for a
border fill. An off-centre radial gradient with 25% border gives RGB
`(109,0,145)` in LibreOffice vs `(109,0,146)` at `(200,120)`.

| Sample (pages 6/6, 6/6, 5/5, 1/1) | Blur | Profile | Result |
| --- | --- | --- | --- |
| `pagestyle_background_lo70.odt` | 1.0000 → 0.1784 | 0.000 → 0.999 | **Pixel passes** |
| `pagestyle_background_lo64.odt` | 1.0000 → 0.0004 | 0.000 → 0.013 | Fails; small image placement |
| `tdf127862_pageFillStyle.odt` | 1.0000 → 0.2397 | 0.000 → 0.997 | Fails (76.03%) |
| `tdf49073.rtf` | 0.0598 → 0.0636 | 0.968 → 0.969 | **Text passes**; pixel still passes |

The background samples' worst pages change (6→2, 6→4, 4→2), so the tiny
`lo64` blur is not whole-document equivalence. Checks 40/90 → 42/90.

## Floating tables and numeric cells

1. Floating tables honor preferred absolute and percentage widths;
   percentages use the page text extents (also in multicolumn sections) per
   the
   [OOXML table-width rule](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.tablewidth?view=openxml-3.0.1);
   oversized tables may extend up to the paper width.
2. The whole floating grid scales to that width, keeping proportions and
   aligned cell edges.
3. Floating header rows repeat on continuation pages when body content fits;
   an oversized header cannot block progress.
4. Repeated headers replay the original list-numbering state without
   advancing body counters (inline tables too).
5. Intersecting floats move apart when either forbids overlap, per the
   [table-overlap rule](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.tableoverlap?view=openxml-3.0.1).
   LibreOffice overlaps authored tables despite `never`; Broiler follows
   OOXML.
6. A non-splittable first floating row that fits a fresh page moves there.
7. Multiple overflowing floats keep all continuations in a queue.
8. Floating continuations finish under their own section's paper and running
   content before the next section opens a page; a float-only page counts as
   occupied. (Raster verified; LibreOffice kept 200×70 pt on all three pages
   of an authored DOCX, so section-paper parity is not claimed.)
9. General-format ODT numeric cells display their stored finite value in the
   application locale; inherited number styles are resolved. The CLI enables
   globalization (protocol and command-line numbers stay invariant); on a
   German host it emits `12,1` like LibreOffice.
10. DOCX export supplies omitted vertical-merge continuation cells, so tables
    read from ODT no longer shift cells into a merged column.

LibreOffice checks on authored DOCX: 140-pt width with a 1:3 grid gives 35-pt
column separation, left edge 30.1; 50% of 180 pt gives 22.45, left edge 55.1;
implicit continuation cells both start at x=80.1.

| Sample | Before | After |
| --- | --- | --- |
| `tdf170672.docx` (rank 1) | 31/40, geometry off, 13.196%, profile 0.454 | **40/40, pixel passes**, 8.859%, 0.856 (both at cap) |
| `floattable-split.docx` | 2/3, 36.055% | 2/3, 36.031%; coverage 1.678% → 1.710%; fails |
| `abi11870-2.odt` | Text passes on cached German decimals | Passes; values follow the locale |

Checks 42/90 → 43/90. The test-only `System.Security.Cryptography.Pkcs`
10.0.12 dependency raises NuGet audit warnings NU1902/NU1903.

## Drawing geometry

1. **Unfilled outlines** of DrawingML, VML and ODT shapes are kept.
2. **Lines keep their paths:** ODT `draw:line`, DrawingML `line` and
   `straightConnector1`, and RTF shape lines keep direction and degenerate
   bounds; diagonals paint as one transformed stroke band (raster) or a line
   path (PDF). DOCX flips and RTF `fFlipV` keep the opposite slope.
3. **Stroke widths** (DrawingML EMU, VML points, ODT `svg:stroke-width`, RTF
   `lineWidth`) flow through model, paginator, painters and DOCX/ODT/RTF
   export. Unspecified width is 0.75 pt; an ODT solid stroke without colour
   is black.
4. **ODT frame borders:** uniform `fo:border` gives text and image frames
   their outline; `none` clears an inherited stroke; written on export.
5. **Ellipses** (DrawingML, VML ovals, ODT, RTF shape type 3) stay ellipses;
   PDF uses a closed four-curve path and clips fills to it.

References:
[ODF 1.3 part 3](https://docs.oasis-open.org/office/OpenDocument/v1.3/OpenDocument-v1.3-part3-schema.html),
[OOXML preset geometry](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.drawing.presetgeometry?view=openxml-3.0.1).
Authored rectangle/diagonal/ellipse/border fixtures agree in geometry with
LibreOffice exports (fonts differ).

Checks stay 43/90; page counts unchanged. `tdf147199.docx` (rank 3) keeps 56
shapes instead of 32 (pages 3–4) and `missing-default-line-color.ott` 12
instead of 9, with worst-page metrics unchanged. `tdf116501.odt` regresses
(37.527% → 38.407%, profile 0.94824 → 0.94643; 2/2 pages) because newly kept
running-content rules are misplaced.

## Still open

- Page counts of `tdf170672.docx` beyond the cap, `tdf163894_hidden.docx`
  and the other floating/table cases; `page-background-link.fodt`'s external
  image is not fetched.
- Fills: gradients use bounded 128×128 sampling, not native shadings; tiny
  bitmap periods are enlarged to cap painting at 16,384 tiles; percentage
  bitmap sizes, repeat offsets and staggered tiling are not applied.
  Standalone PDF omits bitmap fills (`pdf.write.feature-unsupported`) and
  flattens alpha against white. DOCX/RTF report `docx.fill.extended` /
  `rtf.fill.extended`; ODT reports `odt.fill.opacity` for bitmap opacity and
  unequal gradient stop opacity. Unknown gradients and unresolved patterns
  are diagnosed.
- Floating tables: nested floats stay in their cells; standalone PDF lays
  floats in the flow (`pdf.write.feature-unsupported`) and does not repeat
  headers.
- Numeric cells: rich/multiparagraph cells, explicit formats, strings and
  invalid values keep cached text; no formula evaluation; numeric metadata is
  not written. Requires .NET globalization on the host.
- Drawing: arbitrary preset/enhanced paths, group rotations, VML line
  endpoints, arrows, dashes, asymmetric frame borders; RTF drawing-object
  diagonals report `rtf.shape.line`; shape text uses its rectangular area.
  Solid raster ellipses use rounded-rectangle commands, patterned/gradient
  ones a 128-strip approximation; zero-width strokes are 0.25-pt raster or
  device hairlines. Running-content rule placement (`tdf116501.odt`).
- Dynamic fields, complex drawing text and text ordering.
