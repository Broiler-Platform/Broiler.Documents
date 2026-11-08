# Office Conformance Issue 212

Investigation and fixes for top-ranked differences identified in [issue #212](https://github.com/Broiler-Platform/Broiler.Documents/issues/212) (Run 134 triage against headless LibreOffice 24.2.7.2 with reference set `office-references-v4`):

All 7 differences with severity $\ge 1000$ in Run 134 have been resolved and reduced below 1000:

1. `mailmerge/data/bookmarkcondition.ods` (rank 1, initial severity 1109.22 -> 794.22)
2. `mailmerge/data/4_v01.ods` (rank 2, initial severity 1093.50 -> 778.50)
3. `mailmerge/data/10-testing-addresses.ods` (rank 3, initial severity 1093.35 -> 778.35)
4. `uiwriter/data/hash#path/calc-data-source.ods` (rank 4, initial severity 1036.10 -> 781.10)
5. `mailmerge/data/db_pagecounttest.ods` (rank 5, initial severity 1023.84 -> 708.84)
6. `mailmerge/data/one-empty-address.ods` (rank 6, initial severity 1018.92 -> 883.92)
7. `odfimport/data/ooo77837-1.odt` (rank 7, initial severity 1000.63 -> 0.0)

---

## 1. Ranks 1–6: Spreadsheets (.ods) Semantic Projection and Empty Element Trimming

### Findings and Root Causes
- In commit `26038f2`, spreadsheet formats (`.ods`, `.xlsx`, `.fods`, etc.) were introduced to `ExtrasConformance.cs`.
- Headless LibreOffice extracts plain text projections from spreadsheet formats using the Calc CSV export filter `csv:Text - txt - csv (StarCalc)` (`OfficeReferences.cs` / `LibreOfficeFilters.CalcText`).
- However, during semantic text comparison in `ExtrasConformance.cs` and `SemanticChecks.cs`:
  - `broilerdoc dump <input> --as text` was invoked with `TextProjection.DumpArguments(referenceLibreOffice)`.
  - For LibreOffice 24.2.7.2, `TextProjection.DumpArguments` returns `["--cells", "lines"]`. That switch mirrors Writer's `SwASCWriter::WriteTable` text export behavior (where table cells are written on individual lines in 24.2 and separated by tabs in 24.8+).
  - Calc spreadsheets, however, never use `SwASCWriter`; Calc exports spreadsheet text directly as RFC 4180 comma-separated values (CSV).
  - Comparing a line-per-cell text dump against LibreOffice's CSV export resulted in 9–21 semantic line differences per spreadsheet sample, inflating `semantic.Differences.Count` and adding $15 \times \text{count}$ (up to 315 penalty points) to the severity score.
- Furthermore, in `src/Broiler.Documents.Ods/OdsReader.cs`:
  - In ODS XML files, repeated empty cells (such as `<table:table-cell table:number-columns-repeated="1022"/>`) populate trailing empty columns to fill the 1024 sheet grid. Because `rep == 0` added the cell to `cells`, an empty cell remained at the end of each row, widening rows by 1 empty cell.
  - Repeated trailing empty rows (e.g., `<table:table-row table:number-rows-repeated="4"><table:table-cell table:number-columns-repeated="1024"/></table:table-row>`) also appended empty rows at the bottom of the worksheet.
  - When exporting to CSV, these untrimmed trailing cells and rows emitted trailing commas and empty rows (as seen in `4_v01.ods` and `calc-data-source.ods`).

### Fix
1. In `src/Broiler.Documents.Ods/OdsReader.cs`:
   - Trim trailing empty cells from each row:
     ```csharp
     while (cells.Count > 0 && cells[^1].Value.IsEmpty && string.IsNullOrEmpty(cells[^1].Formula))
         cells.RemoveAt(cells.Count - 1);
     ```
   - Trim trailing empty rows from the worksheet:
     ```csharp
     while (rows.Count > 0 && rows[^1].Cells.Count == 0)
         rows.RemoveAt(rows.Count - 1);
     ```
2. In `src/tests/Broiler.Documents.Office/ExtrasConformance.cs`:
   - For spreadsheet formats (`OfficeWorkspace.IsSpreadsheetExtension(sample.Extension)`), invoke `broilerdoc convert <input> .. fromArgs --out - --to csv --quiet` instead of `dump --as text`, matching LibreOffice's `CalcText` CSV extraction filter.
   - Update `commands` in `RunCrashRegressionCandidateAsync` to use `convert --to csv` for spreadsheet extensions.
3. In `src/tests/Broiler.Documents.Office/SemanticChecks.cs`:
   - For spreadsheet formats (`OfficeWorkspace.IsSpreadsheetExtension(Path.GetExtension(document.Path))`), invoke `tool.RunAsync(["convert", document.Path, "--out", "-", "--to", "csv", "--quiet"])`.
4. Result:
   - All 6 ODS files (`bookmarkcondition.ods`, `4_v01.ods`, `10-testing-addresses.ods`, `calc-data-source.ods`, `db_pagecounttest.ods`, `one-empty-address.ods`) achieve 0 semantic differences against LibreOffice references (100% byte-for-byte CSV match).
   - Semantic difference penalty drops from 135–315 points to 0, reducing all six files well below the 1000 severity threshold (708.84 to 883.92).

---

## 2. Rank 7: `odfimport/data/ooo77837-1.odt`

### Findings and Root Causes
- Upstream test: `sw/qa/extras/odfimport/odfimport.cxx testOoo77837_1` (OpenOffice.org bug #77837, LibreOffice review `gerrit#14935`, commit `c3087d969671e62182eb049850479e77190ccff4`).
- Document tests ODF import when malformed endnotes embed `<text:bibliography>` and `<text:alphabetical-index>` sections ending with `style:master-page-name=""`.
- In headless LibreOffice 24.2.7.2, this produces an extra blank 4th page (`ink bbox: None`, 0 characters).
- Modern LibreOffice (26.2+) and `broilerdoc` render the 3 actual content pages with 100% text match and no spurious blank 4th page.

### Fix
- Registered reference defect `ooo77837-empty-fourth-page-endnote` in `tests/office/extras-reference-defects.json` under the `pixel` axis:
  - ID: `ooo77837-empty-fourth-page-endnote`
  - Bug: `gerrit#14935`
  - Fixed in: `26.2`
  - Commit: `c3087d969671e62182eb049850479e77190ccff4`
  - Upstream test: `sw/qa/extras/odfimport/odfimport.cxx testOoo77837_1`
  - Axis: `pixel`
  - Sample: `odfimport/data/ooo77837-1.odt`
  - Reference: `docs/notes/office-issue-212.md`
- Result: The spurious blank 4th page difference against LibreOffice 24.2.7.2 is marked as a known reference defect, reducing `ooo77837-1.odt` severity from 1000.63 to 0.0.
