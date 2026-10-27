# Issue #164: image extents, cell objects and positioned RTF breaks

Further corrections are recorded in the
[second-pass verification](office-issue-164-table-flow-and-styles.md) and
[cell-alignment verification](office-issue-164-cell-alignment.md), followed by
[relative grids and margins](office-issue-164-relative-grids-and-margins.md).

The [30 ranked samples in issue #164](https://github.com/Broiler-Platform/Broiler.Documents/issues/164)
were compared locally before and after these changes. Exact paths and input
SHA-256 values are in `tests/office/selections/issue-164.txt` and
`tests/office/selections/issue-164.sha256`. Original documents are not vendored.

## Corrections

- **Explicit inline image dimensions survive wrapping.** The line wrapper
  previously scaled every oversized image to the available width. In a narrow
  nested cell this turned a deliberately oversized picture into a tiny image
  and removed most of its contribution to row height. An image with either
  dimension stated now retains its measured display extent. Intrinsically sized
  images still fit the available width. Page and fixed-row clipping still apply.
- **ODT paragraph-relative objects use their cell origin.** Pictures and text
  frames inside cells now preserve the existing model's `OffsetXFromCell` flag
  for paragraph and paragraph-content horizontal references. Page and
  page-content references keep their page origin.
- **Wrapping cell objects reserve vertical space.** Their anchors, objects and
  captions participate in cell height and legal pagination cuts. Objects with
  no wrapping or with page-relative vertical positions do not stretch the cell.
  Text inside a shape uses its own paragraph space and cannot accidentally pick
  up objects anchored to the body's paragraphs with the same numeric index.
- **RTF page breaks reach the body even after positioned paragraphs.** A frame
  has no separately paginated flow. A break previously consumed by that flow
  vanished from body pagination. Pending frames now follow their next unframed
  anchor across the break, with a preceding empty body paragraph preserving the
  page being left. Single-column `\column` follows the same path. Shape paint,
  geometry, dimensions and stack order survive reanchoring.

## Verification

The complete unit suite passed: **4,335 passed, three skipped**. This includes
14 added regression cases covering image dimensions in ordinary and nested
cells, cell origins, reserved object height, captions and page cuts, page-relative
objects, positioned RTF breaks and an RTF-reader-to-paginator integration case.

The local original-sample comparison changes **43/89 passed, 46 failed, one
skipped** to **44/89 passed, 45 failed, one skipped**. The newly passing check is
the RTF sample's pixel comparison; its semantic text check still fails. No
original document is claimed fully resolved. No tolerance, reference, baseline
expectation or tool approval was relaxed.

| Original sample | Broiler pages before → after | LibreOffice pages | Other observations |
| --- | ---: | ---: | --- |
| `layout/data/tdf142694-1.odt` | 5 → 6 | 7 | Explicit 389.99 × 315.01 pt picture extents survive the narrow nested cell. The worst-page metric still comes from an earlier, unchanged page. |
| `odfexport/data/tdf169882.odt` | 4 → 7 | 7 | Worst-page blur difference 39.21% → 22.83%; coverage delta 21.17% → 9.11%; ink profile similarity 0.5303 → 0.6948. Ink-box delta worsens 7 → 31 px; the comparison still fails. |
| `ooxmlexport/data/tdf141173_missingFrames.rtf` | 1 → 2 | 2 | The empty first page and frames on the second now match. Pixel check passes; blur difference 9.49% → 8.18%, ink profile 0 → 0.9601, ink box blank-vs-inked → 20 px. Text still differs. |

`ooxmlexport/data/fdo78420.docx` has a small measurement change from retaining an
explicit image extent: blur 17.9371% → 17.8882%, coverage delta
3.3914% → 3.3936%, ink profile 0.52875 → 0.52843. Its page counts remain 26 vs
23. Other original render observations are unchanged.

These are each run's worst-page observations. When pagination or the worst page
changes, they summarize the document-level comparison rather than a comparison
of the same physical page before and after.

## Environment and reproduction

Local comparison: Windows, .NET SDK 10.0.403, LibreOffice 26.2.6.3, Poppler
26.07.0, 96 DPI, 80% threshold, 40-page comparison cap, 120-second reference
timeout, four concurrent documents and the 16 Liberation font files supplied in
`D:/office-fonts`. Both runs use the same inputs, font mappings and environment.
The issue uses Linux, LibreOffice 24.2.7.2 and Poppler 24.02.0 with the prepared
`office-references-v2` archive. These results are a controlled local comparison,
not a rerun of all 2,269 failed checks against that archive.

```powershell
dotnet build src/Broiler.Documents.Cli --no-restore
dotnet test Broiler.Documents.slnx --no-restore
dotnet run --project src/tests/Broiler.Documents.Office --no-build -- `
  --extras-dir D:/scratch/lo-core/sw/qa/extras `
  --sample-list tests/office/selections/issue-164.txt `
  --workspace D:/scratch/issue164-final `
  --triage-dir D:/scratch/issue164-triage-final `
  --previous-triage D:/scratch/issue164-triage-before/triage.json `
  --font-dir D:/office-fonts --dpi 96 --jobs 4 --keep
```

Use a separate workspace and triage directory for the before run, omitting
`--previous-triage`. The triage comparison checks input hashes and environment
compatibility. Paths above are local examples, not build dependencies.

## Remaining gaps

The other samples still expose column and table layout differences, linked-frame
and heading formatting differences, unsupported image formats, running-field
behavior and text-export differences. Cell-relative inside/outside alignment and
wrapping text beside an object within the same cell paragraph remain limited;
the new space reservation does not implement a full object-positioning solver.
The remote background in `page-background-link.fodt` remains unfetched under the
existing external-resource policy. The largest `tdf163894` page-count differences
also depend on LibreOffice version and fonts and were not treated as fixed.
