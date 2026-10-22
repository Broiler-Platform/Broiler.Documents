# Issue #137: page styles, fixed rows and text-box layout

The rankings and document links were fetched directly from
[GitHub issue #137](https://github.com/Broiler-Platform/Broiler.Documents/issues/137)
on 2026-10-22. All 30 ranked samples were downloaded unchanged from LibreOffice's
`sw/qa/extras` tree. The changes address ten shared conformance defects rather
than claiming ten fully conforming documents:

1. **Explicit ODT page-style switches.** Paragraph and table styles naming a
   master page now retain their paper, margins and page break. Cell paragraph
   styles do not switch the body page. Existing text sections retain their own
   column and colour ranges within the page-style ranges.
2. **Following and alternating page styles.** The reader retains a bounded
   master-page chain and its repeating suffix. The shared paginator changes
   paper size and margins as the body continues through it, rather than keeping
   the first style's paper throughout.
3. **Running content on changing page styles.** Each page gets that style's
   headers and footers, including an empty band where the next style states none.
   Style-specific backgrounds are selected too.
4. **Fixed table rows.** DOCX `hRule="exact"`, ODT `row-height`, and negative
   RTF `trrh` retain a fixed-height rule separately from minimum heights. The
   shared and standalone PDF layouts clip overflow without deleting editable
   model text. Merged cells can still use the combined height of their rows.
   DOCX/ODT writes preserve the rule.
5. **DOCX shared-counter restarts.** A level override restarts its abstract
   numbering family's counters on first use. Switching back to another instance
   continues from that restarted count. The numberer retains overridden starts
   for unused levels; measurement snapshots and nested cell flows keep the same
   semantics. DOCX writing preserves the counter relationship and overrides.
6. **Empty DOCX bullet labels.** An explicitly empty `lvlText` stays empty,
   rather than acquiring a fallback bullet. It survives DOCX round trips.
7. **Hidden character runs.** `vanish`, including inherited values and explicit
   off switches, survives reading and writing. DOCX, ODT and RTF retain hidden
   text; the shared and PDF renderers omit its glyphs and metrics. CLI JSON
   identifies hidden runs. Plain-text dumps retain the source text, matching
   LibreOffice's plain-text export, which differs from its PDF export here.
8. **Text-box insets.** DrawingML `bodyPr` insets, VML `inset`, and ODT graphic
   padding now reduce and offset the text area. Explicit zero is kept. DrawingML
   and VML defaults are 7.2 points horizontally and 3.6 vertically. DOCX/ODT
   writes retain the insets; padded ODT boxes use legal frame text-box content.
9. **Paragraph spacing inside text boxes.** Both layouts use block composition
   for plain text boxes as well as boxes containing tables. Before/after spacing
   contributes to placement and growing-frame height, together with inner padding.
10. **List labels inside text boxes.** The shared paginator now composes their
    labels in an independent flow instead of laying every shape paragraph out
    without a marker. Measuring a growing box does not consume body counters.

## Measured results

The baseline was copied before editing, including the preceding #136 work.
Comparisons used Windows, LibreOffice 26.2.6.3 (build
`8221e31b3ac356a1623c672912a3d2b492f7e3d1`), Poppler 26.07.0, unpinned fonts,
72 DPI, one job and the unchanged default 80% threshold. The final render run
used an isolated frozen CLI copy. After verifying LibreOffice's hidden-text
export convention, all 30 semantic comparisons were rerun with the final CLI;
that final adjustment affects text dumps, not rendering.

These are local before/after measurements, not the issue's Linux 144-DPI run.
Some page-count failures in the issue do not reproduce on this machine.

| Sample (issue rank) | Before | After |
| --- | --- | --- |
| `tdf87569_vml.docx` (25) | 2 vs 1 pages; pixel fails | **1 vs 1; pixel passes**. Blurred difference changes from 14.30% to 15.00%; the complete similarity check passes despite that individual metric worsening. Text still differs. |
| `tdf136952_pgBreak3B.odt` (26) | 5 vs 4 pages; pixel fails; 21.42% blurred difference | **4 vs 4; pixel passes**; 14.90% blurred difference. Text continues to pass. |
| `tdf132149_pgBreakB.odt` (21) | 7 vs 5 pages; 35.90% blurred difference | **5 vs 5 pages**; 22.36% blurred difference. Pixel still fails; text continues to pass. |
| `tdf132149_pgBreak.odt` (23) | 8 vs 5 pages; 35.90% blurred difference | 6 vs 5 pages; 15.16% blurred difference. Pixel still fails; text continues to pass. |
| `mixednumberings.docx` (20) | Text fails; 6 vs 5 pages | **Text passes**; 6 vs 5 pages remains. Pixel still fails. |
| `tdf83309.docx` (4) | Text fails; pixel passes locally | **Text now passes**; pixel continues to pass. |
| `fdo87488.docx` (28) | Two unwanted bullets; text fails | **Text passes**. Rendering remains 1 vs 2 pages and still fails. |
| `tdf121374_sectionHF.odt` (18) | 48.99% blurred difference; 6 vs 6 pages | 44.10%; 6 vs 6 pages. Pixel still fails. |
| `tdf146955.odt` (17) | 18.37% blurred difference; both reach the 40-page cap | 15.28%; both still reach the cap. Pixel/text still fail; this is not a verified complete page count. |
| `tdf142694-1.odt` (3) | 46.21% blurred difference; 5 vs 7 pages | **Regression:** 52.28%; 5 vs 7 pages remains. Pixel/text still fail. |
| `fdo66304-1.odt` (24) | 3.60% blurred difference; 2 vs 2 pages locally | 3.91%; 2 vs 2 pages. Pixel/text still fail. |

Across the 30 samples, final checks improve from **39/90 to 44/90**, with no
skips: 30 read checks, three pixel checks and eleven text checks pass. Two pixel
checks and three text checks are newly passing; no previously passing check is
lost. Three page-count gaps close and another narrows. Individual visual metrics
remain mixed, including the regressions above.

## Verification and remaining limits

**2,749 relevant tests pass:** Model 157, DOCX 546, ODT 405, RTF 479,
Pagination 181, PDF 724 and CLI 257. The solution builds, the Release CLI builds,
and `git diff --check` passes. Tests cover inherited hidden text and off switches,
fixed/minimum row round trips and merged-cell clipping, explicit and repeating
page styles, changing bands and paper, list restarts across nested/snapshot flows,
empty labels, insets, text-box spacing and independent labels.

Authored files were also exported directly through LibreOffice:

- A shared numbering definition with two ordinary instances counts `1, 2`;
  switching to a start override of seven and back gives `7, 8, 9`. The final CLI
  matches. Treating every list ID as independent was rejected by this check.
- An empty bullet label emits no bullet. Hidden `secret` followed by visible
  `visible` exports as `visible` in PDF and `secretvisible` in plain text. The
  model retains both runs and the CLI follows the plain-text convention.
- A text box at x=20 with a seven-point left inset exports its text at x=27.10
  through DOCX and x=27.00 through ODT. The shared and PDF layouts use x=27.
  Baseline/font metrics still differ; this only verifies the horizontal inset.
- A fixed 15-point row retains all three authored lines in the model and does
  not push the next block down to fit them. LibreOffice can emit a partially
  clipped line into its PDF text layer; these renderers omit lines that do not
  fit completely, so partial-glyph clipping is still approximate.
- If a row states both fixed and minimum heights, LibreOffice imports the last
  attribute's rule. Both attribute orders were checked directly and tested.
  None of the 30 ranked samples contains that conflicting combination, so this
  final edge-case correction does not change the corpus measurements above.

The highest-ranked long-paragraph cases, linked-frame text continuation,
as-character text boxes, dynamic header fields and complete grouped-shape affine
layout remain unresolved. `tdf163894_hidden.docx` still renders 4 vs 40 pages
locally despite correct hidden-run handling. Fixing the ten mechanisms does not
establish that those documents conform.

The shared paginator follows page-style sequences; standalone PDF still reports
its separate section/page-style limitations. ODT writing reports
`odt.section.page` for page changes it cannot preserve. DOCX writing reports
`docx.section.page-sequence` when automatic page-style changes are reduced to
each section's initial style. ODT writing reports `odt.list.shared-counter` for
the cross-list restart relationship it cannot retain. RTF table export remains
its existing text approximation, and `rtf.shape.padding` reports omitted text-box
insets. These losses are explicit.

No upstream sample bytes, generated PDFs or rendered images were added to the
repository. The workflow threshold and corpus exclusions were not changed.
