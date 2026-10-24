# Targeted fixes for office conformance issue 140

[Issue 140](https://github.com/Broiler-Platform/Broiler.Documents/issues/140)
reports whole-document comparisons. This change addresses specific format losses
in ten of those inputs; it does **not** close all ten whole-document differences.

| Issue entry | Upstream sample in `sw/qa/extras` | Format loss addressed |
| --- | --- | --- |
| 1 | `ooxmlexport/data/tdf163894_hidden.docx` | Header tab positions and alignment were replaced by the fixed default tab grid. |
| 3 | `layout/data/tdf142694-1.odt` | Transparent cells did not reveal the table background. |
| 4 | `ooxmlexport/data/tdf146955.odt` | Explicit tab positions in text frames were lost. |
| 11 | `layout/data/ToC-in-linked-frame-on-first-page-style.fodt` | Right-aligned TOC tabs and dot leaders were lost; page-wide right tabs must fit the narrower frame. |
| 12 | `ooxmlexport/data/tdf163894.docx` | Header tab positions and alignment were replaced by the fixed default tab grid. |
| 14 | `uiwriter/data/fdo70807.odt` | An empty legacy background-image element suppressed the cyan page colour. |
| 15 | `uiwriter/data/tdf135056.odt` | TOC tab positions and leaders were lost inside the page-anchored frames. |
| 16 | `uiwriter/data/tdf87922.odt` | An empty legacy background-image element suppressed the black page colour. |
| 25 | `layout/data/tdf152106.odt` | Character-anchored frames ignored horizontal page coordinates and page centring. |
| 27 | `uiwriter/data/fdo114306_2.odt` | Explicit tabs were lost, and empty background-image elements produced false bitmap warnings. |

## Implementation and regression coverage

`ParagraphStyle.TabStops` carries an immutable, value-comparable set of bounded
positions, left/centre/right/decimal alignment, decimal characters and leaders.
DOCX reads merge inherited stops and honour position-specific `clear` entries.
ODT reads replace inherited tab lists, including an explicit empty list. Both
writers preserve supported tab settings, and ODT automatic-style keys include
child elements so two different tab lists cannot share an incorrect style.

The layout measures the text segment following a tab across styled runs. It
respects paragraph indents, manual breaks and right stops at a narrow frame's
edge. The painter draws leaders in the reserved gap. CLI structural comparisons
also report a lost tab-stop list rather than silently treating the styles as
equal. Formats other than DOCX and ODT have not gained tab-stop serialization.

ODT legacy page colours and images paint the content rectangle, leaving margins
clear. Empty image declarations fall through to the colour. Explicit drawing
fills retain their own background-size setting. Table and row backgrounds show
through transparent cells; opaque cell backgrounds still win. Character and
paragraph anchors can use horizontal page coordinates without becoming anchors
to a numbered physical page.

Authored regression tests cover these behaviours without committing any upstream
document binaries. They include inherited and cleared tabs, empty overrides,
round trips, malformed positions, styled segments, decimal alignment, leaders,
frame widths, table/row/cell paint precedence and paragraph/character anchors.

## Validation and remaining differences

The Release build succeeds. All 3,840 executed unit tests pass, with three other
tests skipped. All 2,320 standard CLI corpus checks pass, with 105 additional
checks skipped for their documented reasons. No corpus baselines or conformance
thresholds are relaxed.

The ten original inputs were downloaded outside the checkout and compared before
and after the change on the same machine, using LibreOffice 25.2.3.2, poppler
26.05.0, 72 DPI, an 80% threshold and a 40-page cap. Fonts were not pinned. Issue
140 used a newer LibreOffice, different poppler and 144 DPI, so these measurements
are a local before/after comparison, not a reproduction of its exact scores.

The selected comparison improves from 14/30 to 16/30 passing checks: all ten
reads pass, four text checks pass, and two pixel checks now pass. The remaining
six text checks and eight pixel checks fail and must remain visible.

For `fdo70807.odt`, the worst-page blurred difference falls from 70.40% to 0.00%.
For `tdf87922.odt`, it falls from 71.11% to 3.41%. Both now pass the pixel check.
For `tdf152106.odt`, it falls from 71.43% to 70.30%; the coordinates are corrected
but the rest of the layout still needs work.

The other samples still have pagination, font, field, table or frame-flow
differences. In particular, the TOC sample remains two pages against five, and
`tdf142694-1.odt` remains five against eight. Painting its formerly missing table
background improves the ink profile but increases the worst-page blurred
difference from 53.38% to 59.68% because the underlying geometry is still wrong.
`tdf146955.odt` has mixed metrics: blurred difference improves from 21.25% to
16.92%, while the worst-page ink profile worsens. Its 40-page cap also prevents a
whole-document conclusion. These entries should not be marked fully resolved.

To repeat the upstream comparison, place only the ten selected inputs in an
external directory, retain their relative paths, build Release, then run:

```sh
dotnet run --project src/tests/Broiler.Documents.Office -c Release --no-build -- \
  --extras-dir /absolute/path/to/selected-extras \
  --soffice /absolute/path/to/soffice --jobs 3 --dpi 72 --max-pages 40 \
  --report /absolute/path/outside/checkout/issue-140.json --top-differences 10
```

Expect a nonzero exit while the documented whole-document differences remain.
The cloud setup retains the selected inputs and reference-tool wrapper under
`/workspace/.setup`; its saved start instructions contain the corresponding command.
