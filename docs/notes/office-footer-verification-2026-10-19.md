# DOCX letterhead footer verification — 2026-10-19

The paginator centred footer text in the bottom margin despite the DOCX reader
supplying `PageGeometry.FooterDistance`. It now anchors the bottom of the footer
line block at that distance from the effective page's bottom edge. The fallback
for a document with no page geometry remains centred. This change applies to
the shared paginator, including other codecs that supply footer distances;
header placement and paragraph leading are unchanged.

Nine focused regression cases cover DOCX write/read/layout with distances of
0, 12 and 36 points, multiline footer anchoring, insufficient margin space,
invalid distances and reuse of the same layout object for another document.
The original five positioning/fit cases were observed failing before the fix.

The office suite was run before and after for `letterhead-frames/docx` at 144 DPI
on Windows, using LibreOffice 26.2.6.3 and Poppler 26.07.0, without pinned fonts.
Both runs produced two pages, with matching page geometry.

| Measurement | Before | After |
|---|---:|---:|
| Worst-page ink-box difference | 52 px | 32 px |
| Ink-profile similarity | 0.892125 | 0.893072 |
| Blurred difference ratio | 0.024102 | 0.023547 |
| Broiler page-two footer ink bottom | 1490 px | 1434 px |
| LibreOffice page-two footer ink bottom | 1438 px | 1438 px |

Footer ink was inspected in the bottom 20% of page two using a threshold of
any RGB channel below 200. The footer's ink-bottom difference consequently
decreased from 52 to 4 pixels. Font metrics and other layout differences remain;
the complete render is not identical.

The official baseline comparison was skipped because its stamp requires Linux,
LibreOffice 24.2.7.2, Poppler 24.02.0 and `office-fonts-1`. Local runs used the
documented developer-only `--allow-pending-tools` switch. No tool approvals,
baseline measurements or classifications were changed. The `suspected-defect`
row stays open pending evidence on the pinned toolchain and assessment of the
remaining differences.
