# Issue #152: ten table grid/style corrections

Starting from `637f2aa`, this change addresses ten DOCX table behaviors. These
are independently verified import/export defects, not ten resolved ranked
originals.

| Problem | Correction |
| --- | --- |
| Leading omitted grid columns were ignored on import | `gridBefore` offsets actual cells into their declared columns |
| Leading omitted columns disappeared on export | Emit `gridBefore` from model cell positions, after merge-continuation cells are synthesized |
| Trailing omitted columns disappeared on export | Emit `gridAfter` against the complete grid; retain trailing extent when importing a grid-less row |
| Horizontal bands always alternated one row at a time | Honor declared `tblStyleRowBandSize`, including inherited declarations |
| Vertical bands always alternated one cell at a time | Honor declared `tblStyleColBandSize` |
| Row table-property exceptions lost their shading | Apply row-local `tblPrEx/shd`, while explicit cell shading retains precedence |
| Row table-property exceptions lost their cell margins | Apply each declared row edge independently, retaining cell-level overrides |
| A missing table grid discarded cell preferred widths | Reconstruct a fallback from positive absolute `tcW`, `wBefore` and `wAfter` declarations |
| Vertical merges joined different grid extents | Require matching start column and column span throughout the continuation chain |
| Omitted edge cells gave their styles to real middle cells | Count omitted cells when selecting first/last-column formats |

The missing-grid inference is a fallback, not a full Word autofit algorithm.
Spanning/omitted widths are distributed across their columns when no other
information is available; percentages, conflicting preferences across rows and
content-dependent sizing are not fully solved. Existing explicit grid widths
are retained. Grid counts are bounded and excessive counts are diagnosed.

Conditional banding after a horizontally merged cell deliberately retains the
native cell-count rule, rather than substituting grid-column parity. Candidate
changes to inherit style row heights and table looks were rejected after native
checks: the height change introduced a newly failing pixel check. Their previous
behavior is retained; no supported tool/baseline threshold was relaxed.

## Regression verification

The Release solution build and **4,146 unit tests** pass, with three additional
skips. The DOCX test project has **666 passing tests**, including **16 new cases**.
Against the isolated pre-change DOCX assembly, **12 new cases fail and four
unchanged paths pass**. Coverage includes import/export gaps, sparse-model round
trips, per-edge local overrides, declared band sizes, missing-grid inference,
matching/mismatching merges, omitted-edge conditions, native merged-cell parity
and extreme grid counts.

The CLI corpus passes **2,320/2,320**, with 105 documented skips and no baseline
changes.

A direct native export test uses the same sparse model (one real cell in column
1 of a three-column 50-point grid) with the old/new writers. The old export has
no gap declarations; the new export declares both `gridBefore=1` and
`gridAfter=1`. LibreOffice places the text at **x=122.1 pt instead of x=72.1 pt**,
preserving the intended second-column position. This test isolates export from
the input-reader changes.

## Native input comparisons and limits

Ten authored DOCX controls retain **30/30 passing checks** before and after;
the broad threshold already passed before. Identical input paths/hashes use
LibreOffice 26.8.1.1, poppler 26.05.0, 72 DPI, an 80% threshold, a 40-page cap
and a 30-second reference timeout. Fonts are unpinned. Native packages include
an explicit styles content type and name; initial incomplete packages and the
rejected candidate cases are not the final comparison.

Some narrower measurements improve:

| Control | Before blur | After blur |
| --- | ---: | ---: |
| Leading grid omission | 0.734% | 0.103% |
| Leading omission after a full row | 0.842% | 0.518% |
| Row-local cell margins | 1.105% | 0.597% |
| Conditional edge formatting | 0.792% | 0.656% |

Other measurements are mixed/worse while retaining the declarations:

| Control | Before blur | After blur |
| --- | ---: | ---: |
| Two-row band groups | 0.170% | 0.228% |
| Two-cell band groups | 0.091% | 0.117% |
| Row-local shading | 0.219% | 0.398% |
| Preferred cell widths without a grid | 0.192% | 0.601% |
| Invalid merge extent | 0.189% | 0.447% |

The tested LibreOffice importer still alternates bands and does not paint the
row exception as the resolved model does. Its autofit and permissive treatment
of malformed merges also differ from this bounded fallback. Retaining requested
formatting and preventing an invalid model merge do not establish exact native
rendering equality. No new native input check is claimed fixed in this set.

The complete 30-original issue #152 group retains **40/89** passes, 49 failures
and the same skipped reference, with **identical recorded observations and check
outcomes**. Row exception margins occur in two ranked originals but their
specific zero top/bottom values do not change the current measurements. No
additional ranked original is claimed fully resolved. These local runs are not
reproduction of the issue's older reference tools and 144-DPI settings.

Local artifacts under `/workspace/.setup`: `make-issue152-table-probes.py`,
`issue152-table-probes`, isolated `issue152-tables-before-bin`,
`issue152-tables-verified-before/after.json` with matching triage/work directories,
`issue152-tables-final-originals.json`, `table-export-probe` (same-model old/new
writer exports and native PDFs), old-assembly tests, and build/unit/corpus logs.
