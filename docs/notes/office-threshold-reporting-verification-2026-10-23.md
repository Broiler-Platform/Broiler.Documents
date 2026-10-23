# Office issue threshold reporting fix, 2026-10-23

The workflow's configured rendering threshold was used for pixel pass/fail
checks, but issue candidate selection independently required every descriptive
band to be best. It also treated informational diagnostics as failures even
when the text check passed. Issue totals therefore included passing documents.

`ExtrasDifference` now retains the executed check results and uses their failure
verdicts for issue eligibility. All-page threshold decisions are preserved rather
than recomputed from the single reported worst page. Failed reads/renders remain
eligible, including renderer failures without measurements; skipped checks and
informational diagnostics do not create failures. Crash inputs and documents
refused by both implementations keep their separate handling. Severity scoring
still orders eligible failures using the existing measurements.

Both issue Markdown and JSON reports now record the configured threshold. The
default remains 80%, with custom thresholds passed through from runner options.

## Verification

Ten new reporting-policy tests run without LibreOffice, in
`Broiler.Documents.Office.Tests`. They cover passing non-best bands, failed text
and pixel checks, read/render failures, skips, informational diagnostics, crash
inputs, shared refusals, all-page verdict retention, and custom threshold output.
The full solution passes **3,819 tests**, with **three existing skips**.
The Release solution build passes with zero errors and existing NuGet audit and
xUnit analyzer warnings; the office runner's Release build has no warnings.

An authored ellipse document was also compared through the real CLI/LibreOffice
pipeline at 72 DPI. Its rendering similarity is 98.58%, with matching page count
and geometry. At threshold 80, all three checks pass, the issue difference count
is zero, and the ranked list is empty despite non-best descriptive bands. At
threshold 100, only the pixel check fails, the difference count is one, and the
sample is ranked. Markdown and JSON both record the respective threshold.
The temporary reports are `%TEMP%/broiler-threshold-ellipse-80.json` and
`%TEMP%/broiler-threshold-ellipse-100.json`, with corresponding Markdown/log files.

The saved artifacts for the following GitHub runs were downloaded directly and
replayed through the updated eligibility, severity and Markdown generation code.
The original measurements and verdicts were reused; documents were not rerendered.

| Issue | GitHub run | Original documents with differences | Corrected count |
| --- | --- | ---: | ---: |
| #135 | [39758202870](https://github.com/Broiler-Platform/Broiler.Documents/actions/runs/39758202870) | 3,443 | 2,466 |
| #136 | [39784497730](https://github.com/Broiler-Platform/Broiler.Documents/actions/runs/39784497730) | 3,271 | 2,217 |
| #137 | [39826675837](https://github.com/Broiler-Platform/Broiler.Documents/actions/runs/39826675837) | 3,260 | 2,015 |
| #138 | [39861440382](https://github.com/Broiler-Platform/Broiler.Documents/actions/runs/39861440382) | 3,260 | 1,996 |
| #139 | [39930307683](https://github.com/Broiler-Platform/Broiler.Documents/actions/runs/39930307683) | 3,253 | 1,968 |

For all five runs, the corrected top 30 list and order are unchanged. Eleven
shared refusals remain excluded in each run. These corrected totals exclude
passing documents with non-best bands and passing documents with diagnostics;
they are counts of documents with failed checks, not the number of failed checks.

Downloaded artifacts and replayed Markdown are under
`%TEMP%/broiler-threshold-audit`; the temporary replay program is under
`%TEMP%/broiler-threshold-replay`. Existing GitHub issues were not edited. Future
workflow runs generate reports using the new logic after this change is merged.
