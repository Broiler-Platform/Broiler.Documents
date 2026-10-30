# Issue 137: page styles, fixed rows, text boxes, linked frames and merged cells

Investigations of
[issue 137](https://github.com/Broiler-Platform/Broiler.Documents/issues/137)
(fetched 2026-10-22). All 30 ranked samples were downloaded unchanged from
LibreOffice's `sw/qa/extras` tree. Comparisons ran locally on Windows with
LibreOffice 26.2.6.3 (build `8221e31b3ac356a1623c672912a3d2b492f7e3d1`),
Poppler 26.07.0, unpinned fonts, 72 DPI, one job, all axes and the default 80%
threshold, using frozen CLI copies before and after. This is not the issue's
Linux 144-DPI run; some of its page-count failures do not reproduce locally.
Two batches fixed ten and then five shared mechanisms; neither claims that
many conforming documents. No upstream bytes, renders or fonts were committed;
thresholds and corpus exclusions were unchanged.

## Page styles, fixed rows and text-box layout

1. **Explicit ODT page-style switches:** paragraph and table styles naming a
   master page keep their paper, margins and page break. Cell paragraph styles
   do not switch the body page. Text sections keep their column and colour
   ranges within page-style ranges.
2. **Following and alternating page styles:** the reader keeps a bounded
   master-page chain and its repeating suffix; the shared paginator changes
   paper and margins as the body continues through it.
3. **Running content per page style:** each page gets its style's headers,
   footers (an empty band where the next style has none) and background.
4. **Fixed table rows:** DOCX `hRule="exact"`, ODT `row-height` and negative
   RTF `trrh` keep a fixed rule separate from minimum heights. Shared and
   standalone PDF layouts clip overflow without deleting model text; merged
   cells may use their rows' combined height. DOCX/ODT writes preserve it. If
   a row states both fixed and minimum heights, LibreOffice imports the last
   attribute's rule (both orders checked; no ranked sample has the conflict).
   LibreOffice may emit a partially clipped line into its PDF text layer;
   Broiler omits lines that do not fit, so partial-glyph clipping is
   approximate.
5. **DOCX shared-counter restarts:** a level override restarts its abstract
   numbering family on first use; switching back to another instance continues
   from the restarted count (LibreOffice: `1, 2`, then override start 7 and
   back gives `7, 8, 9`; treating list IDs as independent was rejected). The
   numberer keeps overridden starts for unused levels, also in measurement
   snapshots and nested cell flows; DOCX writes preserve the relationship.
6. **Empty DOCX bullet labels:** an explicitly empty `lvlText` stays empty and
   survives round trips.
7. **Hidden runs:** `vanish` (inherited and explicit off) survives DOCX, ODT
   and RTF. Renderers omit hidden glyphs and metrics; CLI JSON flags hidden
   runs. Plain-text dumps keep the text because LibreOffice's plain-text
   export does (`secret` + `visible` gives `visible` in PDF but
   `secretvisible` in text).
8. **Text-box insets:** DrawingML `bodyPr` insets, VML `inset` and ODT graphic
   padding shrink and offset the text area; explicit zero is kept. DrawingML
   and VML default to 7.2 pt horizontal, 3.6 pt vertical. DOCX/ODT writes
   keep them; padded ODT boxes use legal frame text-box content. A text box at
   x=20 with a 7-point inset puts text at x=27.10 (DOCX) / 27.00 (ODT) in
   LibreOffice, x=27 in Broiler.
9. **Paragraph spacing in text boxes:** both layouts use block composition
   for all text boxes; spacing and padding count towards placement and
   growing-frame height.
10. **List labels in text boxes** are composed in an independent flow;
    measuring a growing box does not consume body counters.

Explicit losses: standalone PDF reports its section/page-style limits; ODT
writing reports `odt.section.page` and `odt.list.shared-counter`; DOCX
reports `docx.section.page-sequence` when automatic page-style changes are
reduced to each section's initial style; RTF reports `rtf.shape.padding` for
omitted insets and keeps its text approximation for tables.

| Sample (rank) | Before | After |
| --- | --- | --- |
| `tdf87569_vml.docx` (25) | 2 vs 1; pixel fails | **1 vs 1; pixel passes** (blur 14.30% → 15.00%); text differs |
| `tdf136952_pgBreak3B.odt` (26) | 5 vs 4; 21.42% | **4 vs 4; pixel passes**; 14.90% |
| `tdf132149_pgBreakB.odt` (21) | 7 vs 5; 35.90% | **5 vs 5**; 22.36%; pixel fails |
| `tdf132149_pgBreak.odt` (23) | 8 vs 5; 35.90% | 6 vs 5; 15.16%; pixel fails |
| `mixednumberings.docx` (20) | Text fails; 6 vs 5 | **Text passes**; 6 vs 5 |
| `tdf83309.docx` (4) | Text fails | **Text passes**; pixel passes |
| `fdo87488.docx` (28) | Two unwanted bullets | **Text passes**; still 1 vs 2 pages |
| `tdf121374_sectionHF.odt` (18) | 48.99%; 6 vs 6 | 44.10%; fails |
| `tdf146955.odt` (17) | 18.37%; both hit 40-page cap | 15.28%; page count unverified |
| `tdf142694-1.odt` (3) | 46.21%; 5 vs 7 | **Regression** 52.28%; 5 vs 7 |
| `fdo66304-1.odt` (24) | 3.60%; 2 vs 2 | 3.91%; fails |

Checks over all 30 samples: 39/90 → 44/90 (30 read, 3 pixel, 11 text); none
lost. `tdf163894_hidden.docx` still renders 4 vs 40 pages despite correct
hidden-run handling.

## Linked frames, text-box sizing and merged cells

1. **Linked ODT body frames:** frame names, next-frame links and empty
   destinations are kept. Raster pagination passes overflowing paragraphs to
   the next frame, splitting when needed and numbering lists independently of
   the body. Chains are bounded against cycles/shared targets; model
   paragraphs stay intact; ODT writes keep links and frame order.
2. **Minimum-width text boxes:** a minimum-width frame without wrapping grows
   horizontally to its text; explicit ODT auto-grow-width is kept. Both
   layouts use the measured width for text, paint and wrap exclusions; ODT
   writes state wrap and growth explicitly. Ordinary wrapping frames stay
   narrow.
3. **Vertical text alignment:** DrawingML top/centre/bottom, VML
   middle/bottom and ODT text-area alignment place text in the padded
   interior (raster and PDF; DOCX/ODT round trips).
4. **Explicit overflow:** DrawingML vertical overflow and VML visible overflow
   draw text outside a fixed frame; DOCX writes keep the rule. LibreOffice
   clips the authored DOCX fixture with either setting, so this is not parity
   for every Word text box.
5. **Merged-cell heights:** spanning content is compared with its rows'
   combined height and only the deficit grows a non-fixed row (it used to add
   the full height to the first row). Raster pagination moves a connected
   merged-row group to a fresh page/column when the whole group fits there.

Explicit losses: LibreOffice clips source-frame text in a linked ODT chain
while placing overflow in the destination; ODT writing reports
`odt.shape.linked` and `odt.shape.overflow`. Standalone PDF does not route
linked frames and reports `pdf.write.feature-unsupported`. DOCX reports
`docx.shape.flow` for linked frames/minimum-width growth; RTF reports
`rtf.shape.flow`.

Focused nine-sample run: 11/27 checks before and after.

| Sample | Pages before → after | Blur | Ink profile |
| --- | --- | --- | --- |
| `tdf95239.fodt` | **5/2 → 2/2** | 0.0473 → 0.0976 | 0.381 → 0.695 |
| `linked_frames_section_bug.odt` | 2/2 | 0.0154 → 0.0216 | 0.000 → 0.653 |
| `ToC-in-linked-frame-on-first-page-style.fodt` | 2/5 | 0.0198 → 0.0209 | 0.722 → 0.728 |
| `tdf123351_UnderlineGroupSapeText.docx` | 2/2 | 0.0526 → 0.0043 | 0.687 → 0.414 |

Unchanged: `tdf81345.docx` (7/5), `tdf142694-1.odt` (5/7), `tdf147199.docx`
(6/6), `tdf132149_pgBreakB.odt` (5/5), `tdf87569_vml.docx` (1/1). The two
sampled merged-table documents do not improve. Across all 30 samples the
checks stay 44/90; other changed metrics: `tdf135056.odt` (17/17) blur
0.1468 → 0.0103, and visual regressions in `tdf127814.docx` (blur 0.1429 →
0.1576, profile 0.185 → 0.013) and `tdf91083.odt` (blur 0.2295 → 0.2579,
profile 0.202 → 0.183). `tdf116501.odt`, the only corpus file with explicit
ODT width growth, is unchanged after that refinement.

## Still open

- Highest-ranked long-paragraph cases, as-character text boxes, dynamic
  header fields, full grouped-shape affine layout.
- `tdf142694-1.odt` page count (5 vs 7) and its blur regression.
- Linked frames containing tables and linked running-content frames are not
  routed; paragraph keeps/orphans across frame boundaries are not
  implemented.
- The DOCX writer needs explicit continuation cells for merged cells; it does
  not synthesize them from implicit ODT-style gaps.
- The PDF writer's default paragraph-after spacing makes its absolute
  merged-row heights differ from LibreOffice.
