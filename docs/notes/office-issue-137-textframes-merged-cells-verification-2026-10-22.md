# Issue #137: linked frames, text-box sizing and merged cells

This continues the preceding ten-fix batch. The ranked samples were checked
directly against [GitHub issue #137](https://github.com/Broiler-Platform/Broiler.Documents/issues/137)
on 2026-10-22. Five further shared defects were selected from the linked-frame,
minimum-width frame, grouped-shape text and merged-table cases. These are five
layout corrections, not five documents newly meeting conformance.

1. **Linked ODT body frames.** Keep frame names, next-frame links and empty
   destinations. Raster pagination passes overflowing styled paragraphs to the
   next frame, splitting a paragraph when necessary and continuing list counters
   independently of the body. Chains are bounded against cycles/shared targets;
   original model paragraphs remain intact. ODT writes retain the links and
   preserve frame order.
2. **Minimum-width text boxes.** A minimum-width frame with no wrapping grows
   horizontally to its text rather than wrapping every character at the minimum
   width. Explicit ODT auto-grow-width is also retained. Both layouts use the
   measured width for text, paint and wrapping exclusions. ODT writes state the
   wrapping and growth rules explicitly. Ordinary wrapping frames stay narrow.
3. **Vertical text alignment.** DrawingML top/centre/bottom and VML middle/bottom
   anchors, plus ODT text-area alignment, place text within the padded interior.
   Both raster and PDF layouts support it; DOCX and ODT round trips retain it.
4. **Explicit text overflow.** DrawingML vertical overflow and VML visible
   overflow retain text outside a fixed frame in the two layouts. Clipped boxes
   retain their existing behavior. DOCX writes preserve the explicit rule.
5. **Merged-cell height allocation.** Spanning cell content is compared with the
   combined heights of its rows; only a remaining deficit grows a non-fixed row.
   It no longer adds its full height to the first row. Raster pagination moves a
   connected merged-row group to a fresh page/column when the complete group fits
   there. Ordinary row splitting and fixed-row clipping remain covered.

## Local comparison

The baseline is the preceding batch's completed work, including its ten fixes.
The nine focused samples were run before and after with frozen CLI copies:
Windows, LibreOffice 26.2.6.3 build
`8221e31b3ac356a1623c672912a3d2b492f7e3d1`, Poppler 26.07.0, unpinned fonts,
72 DPI, one job, all axes, default 80% threshold. No input was skipped. These
measurements are not a reproduction of the issue's Linux 144-DPI environment.

Both focused runs pass **11/27 checks**: nine reads, one text and one pixel.
There are no newly passing or lost checks. Individual metrics remain mixed:

| Sample | Pages, ours/reference: before → after | Blur ratio: before → after | Ink-profile similarity: before → after |
| --- | --- | --- | --- |
| `tdf95239.fodt` | **5/2 → 2/2** | 0.0473 → 0.0976 | 0.381 → 0.695 |
| `linked_frames_section_bug.odt` | 2/2 → 2/2 | 0.0154 → 0.0216 | 0.000 → 0.653 |
| `ToC-in-linked-frame-on-first-page-style.fodt` | 2/5 → 2/5 | 0.0198 → 0.0209 | 0.722 → 0.728 |
| `tdf123351_UnderlineGroupSapeText.docx` | 2/2 → 2/2 | 0.0526 → 0.0043 | 0.687 → 0.414 |
| `tdf81345.docx` | 7/5 → 7/5 | 0.2947 → 0.2947 | 0.760 → 0.760 |
| `tdf142694-1.odt` | 5/7 → 5/7 | 0.5228 → 0.5228 | 0.068 → 0.068 |
| `tdf147199.docx` | 6/6 → 6/6 | 0.1470 → 0.1470 | 0.000 → 0.000 |
| `tdf132149_pgBreakB.odt` | 5/5 → 5/5 | 0.2236 → 0.2236 | 0.903 → 0.903 |
| `tdf87569_vml.docx` | 1/1 → 1/1 | 0.1500 → 0.1501 | 0.964 → 0.964 |

Lower blur and higher profile similarity are better. Each report selects its
worst page, which can change between runs. The corrected page count in
`tdf95239.fodt` comes with a worse blur ratio; the grouped-shape text case has a
better blur ratio but a worse profile score. Neither is a new pixel pass.
The table fixes have focused regression coverage but do not improve the two
sampled merged-table documents' reported metrics.

The expanded check covers all **30 ranked samples**, retaining **44/90 passing
checks**: 30 reads, three pixel comparisons and eleven text comparisons. No
previous pass is lost and none is added; there are zero skips. The preceding
batch's final pixel and semantic reports supply the baseline outside the nine
focused inputs. Additional changed visual metrics are:

| Sample | Pages, ours/reference (unchanged) | Blur ratio: before → after | Ink-profile similarity: before → after |
| --- | --- | --- | --- |
| `tdf135056.odt` | 17/17 | 0.1468 → 0.0103 | 0.000 → 0.048 |
| `tdf127814.docx` | 2/2 | **0.1429 → 0.1576** | **0.185 → 0.013** |
| `tdf91083.odt` | 6/7 | **0.2295 → 0.2579** | **0.202 → 0.183** |

The latter two are visual regressions despite unchanged pass/fail verdicts.
The first two rows select different worst pages before and after. The remaining
samples' blur ratios stay within 0.00015 of the baseline. After refining explicit
ODT width-growth flags, the only corpus input containing that attribute,
`tdf116501.odt`, was rerun against the checked CLI: its page counts and all four
reported visual measurements are identical to the expanded run.

## Validation and limits

**2,774 tests pass:** Model 157, DOCX 549, ODT 409, RTF 479, Pagination 193,
PDF 730 and CLI 257. There are 25 additional tests since the preceding batch.
The solution build, Release CLI build and whitespace check pass. Existing
analyzer/dependency warnings remain in the solution build.

Authored DOCX/ODT fixtures were exported by the installed LibreOffice and their
PDF text positions inspected. ODT minimum-width/no-wrap text stays on one line
once explicit width growth is written. Top/middle/bottom placement changes by
approximately 20/40 points in a padded 60-point frame. The ODT merged-cell fixture
uses 30 points for the two combined rows instead of charging 30 points to the
first row. The PDF writer retains its existing default paragraph-after spacing,
so its absolute heights differ; tests with explicit spacing verify allocation.

The fixtures also exposed limits which remain explicit:

- LibreOffice clips the authored DOCX overflow fixture with either overflow
  setting. Preserving that setting and drawing overflow does not establish
  LibreOffice parity for every Word text box.
- In the authored ODT chain, LibreOffice places overflow in the destination but
  clips source-frame text. Model round trips retain all text. ODT writing reports
  `odt.shape.linked`, so the exported chain is not claimed to render identically.
- Linked frames containing tables, and linked running-content frames, are not
  routed. Paragraph keeps/orphans across frame boundaries are not implemented.
- Standalone PDF export does not route text between linked frames and reports
  `pdf.write.feature-unsupported`; raster export handles the supported body chains.
- DOCX writing reports `docx.shape.flow` for linked frames/minimum-width growth;
  RTF reports `rtf.shape.flow` for unsupported frame properties. ODT reports
  `odt.shape.overflow` for explicit overflow it cannot write.
- The authored DOCX merged-cell export also exposes the existing requirement
  for explicit continuation cells; implicit ODT-style gaps are not synthesized
  by the DOCX writer. This batch does not fix that separate serialization issue.

Local raw reports are `%TEMP%/broiler137-r2-before.json` and
`%TEMP%/broiler137-r2-final.json`; the expanded run is
`%TEMP%/broiler137-r2-all30.json`. Authored fixtures are under
`%TEMP%/broiler137-authored/r2-*`. No fixtures or font data are vendored.
