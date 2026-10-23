# Issue #138: page fills and RTF field text

[Issue #138](https://github.com/Broiler-Platform/Broiler.Documents/issues/138)
was fetched directly from GitHub on 2026-10-23. All 30 ranked samples were
downloaded unchanged from LibreOffice's `sw/qa/extras` tree. The baseline is
commit `793f46c`, which includes the preceding #137 work.

The high-severity background failures share missing paint operations. The ruby
sample exposes lost editable text. This batch addresses ten shared defects;
it does not claim ten documents newly conform:

1. **Embedded bitmap fill resolution.** Named fill images are loaded through the
   same package, size-limit and resource-admission path as pictures, preserving
   their bytes. They no longer disappear as unsupported fills.
2. **Repeated bitmap patterns.** Raster painting tiles images at their natural or
   stated dimensions, clipped to the fill bounds, instead of omitting them.
3. **Stretch and single-image placement.** Separate repeat modes retain stretched
   backgrounds and single images aligned to the simple reference points. ODT
   round trips preserve these modes and dimensions.
4. **Hatch fills.** Single, double and triple directions retain colour, distance
   and angle. A transparent hatch does not acquire a solid background. Raster
   and PDF painters clip its strokes to the fill bounds.
5. **Centred gradients.** Radial, ellipsoid, square and rectangular geometry,
   centre and rotation are retained instead of flattening to the border colour.
   The common sampler serves raster and PDF. Radial radius and centre placement
   were checked against an authored LibreOffice export.
6. **Gradient controls.** Border, endpoint intensity and uniform colour opacity
   affect the painted colours. ODT writing retains border/opacity and writes the
   intensity-adjusted endpoint colours. Unequal endpoint opacity and bitmap
   opacity limitations are reported rather than claimed as supported.
7. **Page-fill bounds.** `draw:background-size="border"` paints inside margins;
   `full` covers the paper. Each changing page style retains its own bounds.
   ODT writing places this setting in a drawing-page style that LibreOffice
   actually uses, alongside the page-layout fallback.
8. **Legacy embedded backgrounds.** `style:background-image` is read through the
   same image gate when no explicit drawing fill overrides it. External links
   remain unfetched and are reported; `draw:fill="none"` still stops fallback.
9. **Unicode field instructions.** RTF `u`/`uc`, escaped bytes, literal text and
   escaped delimiters retain their instruction text. Unicode fallback bytes are
   consumed. Each nested field has independent instruction/result state, so
   non-ASCII hyperlink targets and ruby operands survive.
10. **Empty-result ruby fields.** A recognised `EQ` ruby overlay with no cached
    result contributes its base text. A nonempty cache wins. Fallback occurs at
    the field boundary, stays in its header/body destination, and does not
    evaluate arbitrary equations. Phonetic annotation layout remains unsupported
    and reports `rtf.field.ruby`.

Fill property definitions were cross-checked with the
[OASIS OpenDocument 1.3 schema](https://docs.oasis-open.org/office/OpenDocument/v1.3/os/part3-schema/OpenDocument-v1.3-os-part3-schema.pdf).
Bitmap and hatch definitions belong in named styles; background bounds belong
in drawing-page properties. Authored LibreOffice renders verify those choices.

## Before/after comparison

Both runs use the same 30 downloaded inputs, frozen CLI copies, Windows,
LibreOffice 26.2.6.3 build `8221e31b3ac356a1623c672912a3d2b492f7e3d1`,
Poppler 26.07.0, unpinned fonts, 72 DPI, one job, all axes and the unchanged
default **80% threshold**. This is a local comparison, not a reproduction of
the issue's Linux 144-DPI run. Page counts are unchanged across the batch.

**40/90 → 42/90 checks pass:** 30 reads, two pixel comparisons and ten text
comparisons. No previously passing check is lost; there are zero skips.

| Sample | Pages, ours/reference | Blur ratio: before → after | Profile similarity: before → after | Result |
| --- | --- | --- | --- | --- |
| `pagestyle_background_lo70.odt` | 6/6 | 1.0000 → 0.1784 | 0.000 → 0.999 | **Pixel comparison newly passes.** |
| `pagestyle_background_lo64.odt` | 6/6 | 1.0000 → 0.0004 | 0.000 → 0.013 | Pixel still fails; small image placement/profile differs. |
| `tdf127862_pageFillStyle.odt` | 5/5 | 1.0000 → 0.2397 | 0.000 → 0.997 | Pixel still fails; reported similarity is 76.03%. |
| `tdf49073.rtf` | 1/1 | 0.0598 → 0.0636 | 0.968 → 0.969 | **Text newly passes**, existing pixel pass retained. Blur worsens slightly. |

Lower blur and higher profile similarity are better. The background samples
select different worst pages before and after: respectively 6→2, 6→4 and 4→2.
The tiny blur ratio in `lo64` therefore does not establish whole-document pixel
equivalence. Other samples' blur ratios stay within 0.00015 of the baseline.
Both RTF inputs were rerun after the final field-boundary guard; their verdicts
and reported visual measurements are identical to the full final run.

The page-count problems in `tdf170672.docx`, `tdf163894_hidden.docx`, floating
tables and the other ranked table cases remain. The linked external image in
`page-background-link.fodt` is not fetched to imitate LibreOffice's output.

## Verification

**2,810 targeted tests pass:** Model 166, DOCX 549, ODT 423, RTF 486,
Pagination 196, PDF 733 and CLI 257. This adds 36 regression cases to the
preceding batch. Another 281 integration/claim checks pass, with three existing
integration tests skipped. The solution build, Release CLI build and whitespace
check pass; existing analyzer/dependency warnings remain.

Authored ODT files were exported by the installed LibreOffice and rasterised at
72 DPI. On 400×240-point paper with 20-point margins, both painters leave the
corners white for a border-only fill and paint the interior. Stretch and
top-left single-image samples agree at the inspected pixel positions. An
off-centre radial fixture with 25% border gives RGB approximately `(109,0,145)`
in LibreOffice and `(109,0,146)` in the raster painter at `(200,120)`; both give
blue at its centre and red at the far corner. The model test retains these
independently measured samples.

Round-trip tests cover all repeat modes, embedded bytes, hatch directions,
centred gradients and controls. A denied bitmap write emits no `Pictures/`
entry. Tests also cover bounded tiny tiles, clipped hatch geometry, page-style
bounds, PDF diagnostics, cached/nested ruby fields and Unicode hyperlink targets.

## Remaining limits

- Centred gradients use bounded 128×128 sampling, not native shading primitives.
  Extremely small bitmap periods are enlarged to bound painting to 16,384 tiles.
- Percentage bitmap dimensions, repeat offsets and staggered tiling are not
  applied. Image placement supports the simple reference points.
- Standalone PDF writing omits bitmap fills with
  `pdf.write.feature-unsupported`. Raster export supports them. The existing PDF
  writer flattens colour alpha against white rather than emitting transparency.
- DOCX and RTF writers report `docx.fill.extended` / `rtf.fill.extended` when
  approximating paint properties their writers do not preserve.
- ODT reports `odt.fill.opacity` for bitmap opacity it cannot paint or unequal
  gradient stop opacities it cannot write. Unknown gradient styles and unresolved
  pattern definitions continue to be diagnosed.
- Ruby annotation placement is not implemented. The fallback preserves base
  text for recognised empty-result overlays, without a general field evaluator.

Raw reports are `%TEMP%/broiler138-before.json`, `broiler138-final.json` and
`broiler138-rtf-final.json`; authored fixtures are under
`%TEMP%/broiler138-authored`. No downloaded corpus files, fonts or binary fixture
assets are added to the repository.
