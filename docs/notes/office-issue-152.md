# Issue #152: inline boundaries and missing master-page definitions

This change starts from main `f444f29` and investigates the 30 ranked originals
in [issue #152](https://github.com/Broiler-Platform/Broiler.Documents/issues/152).
The [exact selection](../../tests/office/selections/issue-152.txt) retains the
whole control group. Its [SHA-256 manifest](../../tests/office/selections/issue-152.sha256)
records the upstream bytes used locally; it can be checked with `sha256sum -c`
from an extras directory. Source files remain external to the repository.

## Corrections

- DOCX page breaks between two runs of text in one paragraph now preserve the
  page boundary by producing paragraph fragments, instead of a line separator.
- Equivalent column breaks preserve the next-column boundary, or the next page
  in a one-column section. Explicit numbering stays on the first fragment;
  its continuation does not acquire another marker or restart the counter.
- A leading column break in a numbered item retains the empty numbered fragment
  before the boundary and the following item label, matching the native import.
  A leading page break keeps the existing paragraph/item identity.
- An ODT paragraph/table style naming an absent master-page definition still
  requests its page boundary. It retains the active paper and running content,
  ignores such requests inside hidden sections/cells, and respects an explicit
  empty master-page name cancelling inheritance.

Fragments retain inline formatting, paragraph mark formatting and indentation.
Only the outside fragments retain the source's before/after spacing; the first
fragment's keep-with-next is cleared across the explicit boundary. Ordinary
continuations have no first-line indent. `docx.pagebreak.fragment` explicitly
reports that the source paragraph structure has been approximated. This does
not introduce a full model of inline page/column boundaries. Paragraph borders,
paragraph identity and editing semantics can still differ from the source.
Inside table cells the existing line-break approximation and diagnostic remain;
row pagination does not yet honor an internal explicit boundary. Repeated and
trailing empty-page limitations also remain.

## Native verification

The before tool is an isolated copy of the unmodified main CLI
(`/workspace/.setup/issue152-before-bin`). Both sides use identical source
locations, LibreOffice 26.8.1.1, poppler 26.05.0, 72 DPI, an 80% threshold,
40 pages maximum and a 30-second reference timeout. Fonts are unpinned.
Issue #152 used LibreOffice 26.2.6.3, poppler 26.01.0 and 144 DPI, so these
results establish a local before/after comparison, not exact CI reproduction.

Four minimal authored DOCX controls improve from **8/12 to 12/12** checks:

| Control | Before pages | After pages | Reference pages | Newly passing checks |
| --- | ---: | ---: | ---: | --- |
| Two internal page boundaries | 1 | 3 | 3 | Pixel |
| Internal column boundary | 1 | 1 | 1 | None; both pixel checks already passed |
| Numbered continuation across a page | 1 | 2 | 2 | Pixel |
| Column break before numbered text | 1 | 2 | 2 | Text and pixel |

The focused original `pageBreakInHiddenSection.fodt` now has **3 pages, matching
LibreOffice**, versus 2 before. `tdf153964_numberingAfterBreak14.docx` now passes
its semantic text comparison; its page count improves from **2 to 3**, with 4
still produced by LibreOffice. Neither original is claimed fully resolved:
their visual checks remain failing.

Across all 30 originals, the final comparison improves from **39/89 to 40/89**
passing checks, retaining the same one skipped reference. No previously passing
check regresses. The triage comparison confirms equal source hashes and recorded
reference environments; only the two originals above change observations.
Worst-page/page-count changes are not treated as directly comparable metrics.
The CLI conversion corpus passes **2,320/2,320**, with 105 documented skips.

## Validation and reproduction

The Release solution build and **4,054 unit tests** pass, with three additional
skips. Eight added test cases cover numbering before and across boundaries,
DOCX round trips, formatting/spacing preservation, the cell boundary limitation,
and missing/cancelled/hidden master-page requests. Four existing tests now assert
preserved boundaries instead of the previous documented loss.

```bash
dotnet run --project src/tests/Broiler.Documents.Office -c Release -- \
  --extras-dir /path/to/extras --sample-list tests/office/selections/issue-152.txt \
  --soffice /path/to/soffice --jobs 3 --dpi 72 --max-pages 40 --lo-timeout 30 \
  --top-differences 30 --triage-dir /tmp/issue152-before
# Repeat after the fix, with the same inputs and reference environment:
# --triage-dir /tmp/issue152-after --previous-triage /tmp/issue152-before/triage.json
```

Local artifacts are under `/workspace/.setup`: `issue152-inputs.json`,
`issue152-before.json`, `issue152-final.json`, the matching triage directories,
`make-issue152-probes.py`, `issue152-probes`, `issue152-probes-before.json`,
`issue152-probes-after.json`, and build/unit/corpus logs. No global threshold,
Office baseline classification or native-tool approval was changed.
