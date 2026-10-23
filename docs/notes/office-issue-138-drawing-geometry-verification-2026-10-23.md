# Issue #138: drawing geometry, 2026-10-23

The remaining rankings were fetched directly from [GitHub issue #138](https://github.com/Broiler-Platform/Broiler.Documents/issues/138).
The issue is unchanged since 2026-10-22T23:08:43Z. This follows the
[floating-table and numeric-cell batch](office-issue-138-floating-tables-numeric-cells-verification-2026-10-23.md)
and preserves its uncommitted changes.

## Five corrections

1. **Unfilled outlines are retained.** Empty DrawingML, VML and ODT shapes with
   a visible outline no longer disappear for having neither fill nor text.
   Image frames continue through the image admission path.
2. **Lines retain their paths.** ODT `draw:line` endpoints, DrawingML `line` and
   `straightConnector1` presets, and RTF shape lines retain direction and
   horizontal/vertical degenerate bounds. Diagonal lines paint as one transformed
   stroke band in the raster renderer and a line path in PDF, instead of a
   rectangle. DOCX flips and RTF `fFlipV` retain the opposite slope.
3. **Stroke widths are preserved.** DrawingML EMU widths, VML point widths,
   ODT `svg:stroke-width` and RTF `lineWidth` flow through the model, paginator,
   raster painter, PDF writer and DOCX/ODT/RTF exports. Unspecified source widths
   use 0.75 pt. A stated ODT solid stroke without a color falls back to black.
4. **ODT frame borders are honored.** Uniform `fo:border` supplies the frame's
   outline color and width for text and floating-image frames. Explicit `none`
   clears an inherited graphic stroke. These borders are also written on export.
5. **Ellipses retain their geometry.** DrawingML ellipses, VML ovals, ODT
   ellipses and RTF shape type 3 no longer become rectangles. The raster painter
   uses elliptical fills/outlines; PDF uses a closed four-curve path and clips
   fills to it. Basic geometry survives DOCX, ODT and RTF exports.

The format definitions are documented in [ODF 1.3 part 3](https://docs.oasis-open.org/office/OpenDocument/v1.3/OpenDocument-v1.3-part3-schema.html)
and the [OOXML preset geometry reference](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.drawing.presetgeometry?view=openxml-3.0.1).

## Tests and authored LibreOffice checks

The full solution passes **3,809 tests**, with **three existing skips**. This
batch adds 30 cases across DOCX, ODT, RTF, pagination and PDF, and updates two
old tests that expected newly supported content to be discarded. The Release
CLI build passes without warnings or errors. The full Release solution build
passes with existing NuGet audit and xUnit analyzer warnings.

Authored 200x150 pt documents contain an 80x40 pt unfilled rectangle with a
3 pt outline, a 3 pt ascending diagonal, a red ellipse with a 2 pt black outline,
and a text frame with a 3 pt border. DOCX and ODT files were exported to PDF
through LibreOffice 26.2.6.3; the standalone PDF writer and CLI raster output
were rendered and visually inspected alongside them. The rectangle, line slope,
ellipse silhouette and stroke widths agree. Default body fonts differ locally,
so this is a geometry check rather than a whole-page pixel equality claim.
Visual inspection caught and removed a diagonal seam from an initial two-triangle
raster implementation.

Round-trip tests cover all four model paths and widths, no-fill retention,
zero-height rules, opposing endpoints, automatic line color, and explicit frame
border suppression. Render tests check ellipse paths and clipping, stroke
widths, and balanced transform/clip operations.

## Upstream comparison

The same 30 downloaded inputs were compared before and after at 72 DPI, one
worker, 80% threshold and a 40-page cap. This Windows run uses LibreOffice
26.2.6.3 (build 8221e31b3ac356a1623c672912a3d2b492f7e3d1), Poppler 26.07 and
unpinned fonts; it does not establish the issue's Linux/144 DPI result.

The count remains **43/90**: 30 read, three pixel and 10 text checks pass.
There are no lost passes, skipped checks or harness errors. Recorded page counts
are unchanged for all 30 samples. These five mechanisms do not make five
previously failing documents fully conformant.

| Sample | Observed effect |
| --- | --- |
| `tdf147199.docx` (rank 3) | Retains 56 shapes instead of 32; the 24 previously dropped outlines/lines are placed on pages 3 and 4. Its recorded worst-page metric and failing pixel/text verdicts are unchanged. |
| `missing-default-line-color.ott` | Retains 12 body shapes instead of nine, including the three rules. All 12 are placed; its worst-page metric and verdicts remain unchanged. |
| `tdf116501.odt` | Newly retained running-content rules expose a visual regression: worst blurred difference rises from 37.527% to 38.407%, and profile similarity falls from 0.94824 to 0.94643. It still has two pages against LibreOffice's two and still fails. |

The other 29 samples' recorded render metrics are unchanged. Scores report each
document's worst page; unchanged worst-page values do not imply that newly
retained graphics were absent from other pages. Running-content rule placement
still needs further work; this regression is not presented as an improvement.

The baseline is `%TEMP%/broiler138-r2-complete.json`, generated with the frozen
pre-batch `broiler138-r2-locale-cli`. The full after report is
`%TEMP%/broiler138-r3-final.json`, using `broiler138-r3-final-cli`. A final focused
check of the straight-connector alias in `tdf147199.docx` uses
`broiler138-r3-checked-cli` and `broiler138-r3-connectors.json`.
That focused rerun has identical recorded metrics and verdicts to the full run.

```powershell
dotnet run --project src/tests/Broiler.Documents.Office --no-build -c Release -- `
  --extras-dir "$env:TEMP/broiler138-samples" `
  --tool "$env:TEMP/broiler138-r3-final-cli/broilerdoc.exe" `
  --soffice 'C:\Program Files\LibreOffice\program\soffice.com' `
  --dpi 72 --jobs 1 --report "$env:TEMP/broiler138-r3-final.json"
```

Authored fixtures and their exports are under `%TEMP%/broiler138-r3-authored`.
No downloaded corpus inputs or binary fixtures are added to the repository.

## Limits

Arbitrary preset/enhanced paths, group rotations, VML line endpoints, arrows,
dash patterns and asymmetric frame borders remain outside this subset.
Legacy RTF drawing-object diagonals still report `rtf.shape.line`. Shape text
continues to use its rectangular text area. Solid raster ellipses use native
rounded-rectangle commands; patterned and gradient ellipses use a bounded
128-strip approximation. Zero-width strokes use a 0.25 pt raster hairline and
a PDF device hairline. Standalone PDF bitmap-fill and image composition limits
remain diagnosed as before. Complex drawing text, dynamic fields, table flow
and running-content placement still account for substantial ranked differences.
