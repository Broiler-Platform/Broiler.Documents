# Running-content paragraph spacing verification — 2026-10-20

Investigation of the remaining letterhead placement differences found that the
paginator ignored paragraph spacing in header and footer blocks. Their models
contained `SpacingBefore` and `SpacingAfter`, but composition placed every line
immediately after the preceding line and measured only line heights for fit.

Running-content composition now follows the existing body spacing rule:
nonnegative before-spacing is included between paragraphs, the first paragraph's
before-spacing is suppressed, and nonnegative after-spacing follows each
paragraph. Relative line positions preserve these gaps when the complete block
is anchored at its header/footer distance or centred in the margin. The total
height, including spacing, is used to reject blocks that cannot fit.

Four new regression cases cover both headers and footers. They check a 12-point
inter-paragraph gap, header top anchoring, footer anchoring including trailing
space, suppression of the first before-spacing, and rejection when spacing
causes margin overflow. All four failed before the correction and pass after it.
All 235 CLI tests and 23 pagination tests pass.

Local `letterhead-frames` office comparisons used Windows, LibreOffice 26.2.6.3,
Poppler 26.07.0, 144 DPI and explicitly mapped local Liberation fonts. Before and
after aggregate results are identical:

| Format | Ink-box difference | Ink-profile similarity | Blurred difference |
|---|---:|---:|---:|
| DOCX | 29 px | 0.847907 | 0.019846 |
| ODT | 144 px | 0.998436 | 0.001104 |
| RTF | 1202 px | 0.877629 | 0.077538 |
| HTML | 143 px | 0.353663 | 0.058659 |

This fixes a reproduced spacing defect found during the investigation; it does
not resolve the remaining letterhead fixture differences. Reports are
`D:\office-letterhead-next-before.json` and `D:\office-running-spacing-after.json`.
Each run passed 28 checks and skipped four pinned-baseline comparisons. All
official baseline measurements and classifications remain unchanged. The existing
developer-only `--allow-pending-tools` switch was used without changing approvals.
