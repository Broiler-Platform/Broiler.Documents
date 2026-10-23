# Issue #138: floating tables and numeric cells, 2026-10-23

This continues the [first ten fill and field corrections](office-issue-138-fills-fields-verification-2026-10-23.md).
The [GitHub issue](https://github.com/Broiler-Platform/Broiler.Documents/issues/138)
was fetched directly; its last update was 2026-10-22T23:08:43Z. The next batch
targets shared causes behind the remaining table and text differences rather
than treating ten corpus filenames as ten complete fixes.

## Ten corrections

1. Floating tables honor preferred absolute and percentage widths. Percentages
   use the page text extents, including in multicolumn sections, and an oversized
   table may extend beyond the text area up to the paper width.
2. A complete floating table grid scales consistently to that selected width,
   preserving column proportions and aligned cell edges.
3. Floating header rows repeat on continuation pages when body content can fit.
   An oversized repeated header cannot prevent progress.
4. Repeated table headers replay the original list-numbering state without
   advancing the following body's counters. This also fixes inline tables.
5. Intersecting floats move apart when either table forbids overlap.
6. A non-splittable first floating row that fits a fresh page moves there instead
   of extending past the current page's foot.
7. Multiple overflowing floats retain all their continuations in a queue;
   a later float no longer replaces an earlier float's remaining content.
8. Floating continuations finish under their owning section's paper and running
   content before a following section opens a page. A page containing only
   floating content counts as occupied when selecting the section owner.
9. Plain General-format ODT numeric cells display their stored finite value
   using the application locale. Inherited number styles are resolved. The CLI
   enables globalization instead of forcing invariant culture; its protocol and
   command-line numbers still use invariant formatting.
10. DOCX export supplies omitted vertical-merge continuation cells. Model tables
    read from ODT no longer shift later cells into a merged column. Explicit
    continuations and horizontal spans remain intact.

The percentage reference follows the documented [OOXML table-width rule](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.tablewidth?view=openxml-3.0.1).
Non-overlap follows the documented [table-overlap rule](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.tableoverlap?view=openxml-3.0.1).

## Verification

The full solution test run passed **3,779 tests**, with **three existing skips**.
This batch adds 26 regression cases: 13 pagination, 10 ODT and three DOCX cases.
The full Release solution build passed with zero errors and four NuGet audit
warnings for the existing test-only `System.Security.Cryptography.Pkcs` 10.0.12
dependency (NU1902/NU1903). The Release CLI build had no warnings or errors.

Authored DOCX files were exported through LibreOffice 26.2.6.3 on this machine:

| Check | LibreOffice observation |
| --- | --- |
| Preferred width 140 pt, 1:3 grid | Text-column separation 35 pt; centered left edge 30.1 pt |
| Width 50% of a 180 pt text area, 1:3 grid | Separation 22.45 pt; centered left edge 55.1 pt |
| Floating repeated header | Three pages, header at the same position on every page |
| Implicit vertical continuation | Upper and lower unmerged cells both start at x=80.1 pt; following paragraph remains below the table |
| Non-overlap | LibreOffice overlaps the two authored tables despite `never`; the shared raster layout follows the OOXML rule |

The paper-ownership regression is verified directly in the raster layout: two
200x70 pt pages carry the floating table, followed by a 100x200 pt page for the
next section. An authored DOCX exported by LibreOffice kept 200x70 pt on all
three pages, so this is not claimed as verified section-paper export parity.
English and German numeric displays are covered independently; the actual CLI
on this German machine emits `12,1`, matching LibreOffice.

## Corpus comparison

The same 30 downloaded upstream inputs as the previous batch are compared with
80% threshold, 72 DPI, one worker and a 40-page limit. This Windows run uses
LibreOffice 26.2.6.3 (build 8221e31b3ac356a1623c672912a3d2b492f7e3d1),
Poppler 26.07 and unpinned local fonts. It is not equivalent to the issue's
Linux/144 DPI run. The frozen pre-batch CLI scored **42/90** checks.

The final run scores **43/90**, with **no lost passes**, no skips and no harness
errors: 30 reads, three pixel comparisons and 10 text comparisons pass.

| Sample | Before | After |
| --- | --- | --- |
| `tdf170672.docx` (issue rank 1) | 31/40 pages, geometry mismatch, blur difference 13.196%, pixel check failed | 40/40 pages, geometry matches, blur difference 8.859%, pixel check passes |
| `floattable-split.docx` | 2/3 pages, blur difference 36.055% | 2/3 pages, blur difference 36.031%; still fails |
| `abi11870-2.odt` | Text check passes using the cached German decimals | Text check still passes; General values now follow the application's German locale |

For `tdf170672.docx`, ink-profile similarity increases from 0.454 to 0.856 and
the ink-box delta falls from 527 to 280 px. Both renderers reach the 40-page
comparison cap, so this does not establish uncapped page-count equality.
`floattable-split.docx` has a small blur improvement but a slightly worse
coverage delta (1.678% to 1.710%). The other 28 rendered samples have unchanged
page counts and recorded metrics. No threshold or comparison tolerance changed.

The comparison command is:

```powershell
dotnet run --project src/tests/Broiler.Documents.Office --no-build -c Release -- `
  --extras-dir "$env:TEMP/broiler138-samples" `
  --tool "$env:TEMP/broiler138-r2-locale-cli/broilerdoc.exe" `
  --soffice 'C:\Program Files\LibreOffice\program\soffice.com' `
  --dpi 72 --jobs 1 --report "$env:TEMP/broiler138-r2-complete.json" `
  --issue-out "$env:TEMP/broiler138-r2-complete.md"
```

The baseline uses the frozen `broiler138-r2-before-cli` and writes
`broiler138-r2-before.json`. Authored interoperability probes are under
`%TEMP%/broiler138-r2-authored`. No downloaded corpus files or binary fixtures
are added to the repository.

## Remaining limits

These changes do not make ten documents fully conformant. Nested floating
tables still stay in their cells, and standalone PDF export lays floats in the
flow and reports `pdf.write.feature-unsupported`; the float placement fixes
apply to the shared raster paginator. Header repetition also remains absent
from the standalone PDF writer. The numeric fallback preserves cached content
for rich or multiparagraph cells, explicit formats, strings and invalid values;
it does not evaluate formulas or retain numeric metadata on write. Locale
support requires the normal .NET globalization facilities on the target host.
Dynamic fields, complex drawing text and text ordering remain separate sources
of differences in the ranked corpus.
