# Office issue 135: page images and table flow

This batch follows the [GitHub report](https://github.com/Broiler-Platform/Broiler.Documents/issues/135),
fetched again from GitHub, and the previous paragraph-pagination batch. It
implements three missing behaviors found in the ranked samples.

## Changes

1. **Page-anchored ODT frames:** bring frames back from beyond the right or
   bottom paper edge. A full-page picture with stale offsets previously rendered
   completely outside the page. Horizontal negative offsets clamp to the page;
   negative vertical offsets retain their bleed above it. Paragraph anchors and
   drawing objects retain their offsets. The conversion to the model still
   accounts for the left margin. This fixes the blank output for
   [tdf116195.odt](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/odfimport/data/tdf116195.odt).
2. **Table keep-with-next:** preserve ODT table styles' `fo:keep-with-next`,
   including inherited styles, through model edits and ODT writes. Both the
   shared paginator and PDF writer move an inline body table with its following
   paragraph when the group fits a fresh page/column. Oversized tables can still
   split; explicit breaks take precedence. Numbering lookahead uses a snapshot.
   This addresses a missing rule in
   [tdf91083.odt](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ww8export/data/tdf91083.odt).
3. **Cell padding:** model separate left, top, right and bottom insets. Read ODT
   uniform/individual padding and inherited named styles, plus DOCX table/style
   margins and direct cell overrides. Both writers preserve the cell insets.
   Both rendering engines position text and nested tables inside them and add
   vertical padding to row height; shared pagination retains legal row splits.
   This removes the old approximation of one horizontal inset for every cell.

Table keeps currently apply to an inline body table followed directly by a body
paragraph in the same section. They do not chain adjacent tables or apply inside
cells or to floating tables. DOCX has no equivalent table property: writing one
reports `docx.table.keepnext` instead of silently claiming to preserve it. Absent
cell padding retains the existing table-horizontal/zero-vertical fallback.

## Verification

2,169 tests passed across Model (156), DOCX (525), ODT (378), Pagination (147),
PDF (709), and CLI (254). Added regressions cover four frame positions, padding
precedence and round trips, explicit zero margins, inherited styles, text/row
placement in both engines, oversized table splits and continuous output.

Four temporary frame variants were exported by LibreOffice 26.2.6.3. The PDF
image transforms confirm inside coordinates remain unchanged, right/bottom
overflow moves to the paper edge, full-page images move to the page origin, and
negative Y remains negative. These checks caught and prevented clamping the
negative Y case incorrectly.

Two authored ODT documents were exported by LibreOffice and the PDF codec.
Page-by-page extracted text matches in both: a kept table and following text
move together onto page two; a padded cell and following text remain on page
one. Relative cell/text spacing matches (7-point top inset and 20 points of
additional total vertical padding). Absolute glyph bounds still differ because
the PDF codec's font metrics/baseline placement differ from LibreOffice's.

Eight downloaded upstream files were compared before and after at 72 DPI, one
job, a 40-page cap, unpinned Windows fonts, LibreOffice 26.2.6.3 and Poppler
26.07.0. These are local measurements, not Linux CI results.

| Sample | Before | After |
| --- | --- | --- |
| `tdf116195.odt` | Blank output; 70.60% blurred difference; pixel failure | Picture visible; 0.48% blurred difference; **passes the 80% pixel check** |
| `tdf91083.odt` | 6 vs 7 pages; 26.40% blurred difference | 6 vs 7 pages; 22.95% blurred difference; still fails |
| `tdf142694-1.odt` | 7 vs 7 pages; 47.12% blurred difference | 5 vs 7 pages; 46.30% blurred difference; **page-count match regresses**, composite severity increases |
| Other five samples | Existing failures or pass | Check outcomes and reported metrics unchanged |

The total changes from 14/24 to 15/24 checks passing, with no skipped checks;
pixel passes increase from one to two. The table changes are verified format
behaviors, **not complete fixes for the two table documents**. In particular,
the padding change must not be described as an overall conformance improvement
for `tdf142694-1.odt`; further table sizing/pagination work remains.

The unchanged FODT background-link sample renders a blue unavailable-image
fallback in LibreOffice, while this reader leaves the background unpainted.
This batch does not reproduce that fallback. Dynamic headers, unequal columns,
floating-table differences and exact-row clipping also remain outside this batch.

No upstream sample bytes or generated integration artifacts were committed.
