# Issue #164, fourth pass: relative grids and uniform page margins

This continues the [cell-alignment pass](office-issue-164-cell-alignment.md).

## Corrections

- ODT column grids expressed only with `style:rel-column-width` now retain
  their proportions. Previously these columns all became unspecified widths,
  so layout divided the space equally. A fixed table width supplies the point
  basis; an otherwise unspecified table fills its containing text area using
  the existing percentage-width model. An explicit table percentage still
  wins. Grids containing absolute column widths retain their previous behavior.
- Repeated columns expand their weights with their grid positions. Normalizing
  finite weights before summing avoids overflow. Missing, non-finite, zero,
  negative and malformed weights retain the unspecified-grid fallback.
- Page layouts now honor the uniform `fo:margin` declaration. Per-side values
  override it, including explicit zero. The top margin is resolved before the
  existing header-height calculation. ODT round trips preserve the resulting
  page geometry.

The relative-grid calculation follows the weighted-width definition in
[ODF section 15.9.1](https://docs.oasis-open.org/office/v1.1/OS/OpenDocument-v1.1.pdf).
This is a proportional basis for the existing table-width model, rather than a
new model unit or a change to grids that already state absolute dimensions.
Mixed absolute/relative grids and table margin sizing remain subject to the
existing reader/layout limitations.

## Verification

Ten regression cases cover fixed and implicit table widths, ODT round trips,
repeated columns, large weights, invalid weights, uniform margins and side
overrides. The full suite passes: **4,364 passed, three skipped**.

A controlled 300 × 100 pt FODT page with 10 pt margins and column weights
`16384*` / `49151*` was exported through LibreOffice Writer and rendered by
Broiler at 96 DPI. Both rasterizations are 400 × 134 pixels. Both place the left
border at column 13 and the interior divider at column 106, within their stroke
rasterization differences. The uniform-margin fixture previously started at
the paper's left edge; it now starts at the declared margin.

LibreOffice 26.2.6.3 renders very small weights such as `1*` / `3*` differently
from equivalent weights normalized to 65535. The reader uses the ratio rule for
both spellings; exact parity with that native import behavior is not claimed.

The two listed originals containing relative-only styles are
`ooxmlexport/data/ooo96040-2.odt` and `uiwriter/data/tdf130680.odt`; some affected
tables belong to hidden or deleted content. Neither is claimed newly resolved.
The selected originals contain no uniform page-margin declaration. The full
30-sample replay retains **44/89 passed, 45 failed, one skipped**, with no
passing check regressed.

## Replay

Inputs, hashes and environment are the same as the preceding pass: Windows,
LibreOffice 26.2.6.3, Poppler 26.07.0, Liberation fonts, 96 DPI, 80% threshold,
40-page cap and 120-second reference timeout. No baseline or tolerance changed.

```powershell
dotnet build src/Broiler.Documents.Cli --no-restore
dotnet test Broiler.Documents.slnx --no-restore
dotnet run --project src/tests/Broiler.Documents.Office --no-build -- `
  --extras-dir D:/scratch/lo-core/sw/qa/extras `
  --sample-list tests/office/selections/issue-164.txt `
  --workspace D:/scratch/issue164-round4-verified `
  --triage-dir D:/scratch/issue164-triage-round4-verified `
  --previous-triage D:/scratch/issue164-triage-round3-final/triage.json `
  --font-dir D:/office-fonts --dpi 96 --jobs 4 --keep
```
