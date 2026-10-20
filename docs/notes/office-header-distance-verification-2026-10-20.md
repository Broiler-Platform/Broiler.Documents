# Header distance verification — 2026-10-20

Investigation of the remaining letterhead placement differences found a small
paginator defect: `PageGeometry.HeaderDistance` was available but ignored.
Headers were centred in the top margin regardless of their declared distance.
The paginator now anchors the top of the complete header line block at that
distance from the effective page's top edge.

Without document page geometry, headers retain centred placement. Invalid
distances or blocks that cannot fit inside the top margin are diagnosed and
omitted, following the existing footer policy. Header shapes retain their
independent coordinates. This does not add support for LibreOffice's HTML header
convention or change ODT's geometry mapping.

Nine regression cases cover DOCX write/read/layout at 0, 12 and 36 points,
multiple lines, insufficient margin space, negative/nonfinite distances, and
layout reuse without document geometry. Eight placement/validation cases were
observed failing before the fix. All 229 CLI tests and 19 pagination tests pass.

The local `letterhead-frames/docx` office comparison used Windows, LibreOffice
26.2.6.3, Poppler 26.07.0, 144 DPI and explicitly mapped local Liberation fonts.
It includes the preceding font-metrics and footer corrections.

| Measurement | Before | After |
|---|---:|---:|
| Pages (both renderers) | 2 | 2 |
| Ink-box difference | 29 px | 29 px |
| Ink-profile similarity | 0.832161 | 0.832161 |
| Blurred difference ratio | 0.023630 | 0.023630 |
| Ink coverage difference | 0.000289 | 0.000289 |

The aggregate measurements did not improve. This is a verified header-distance
bug fix found during investigation, not a resolution of the remaining office
row. Reports are `D:\office-header-before.json` and `D:\office-header-after.json`.
Each office run passed seven checks and skipped the pinned-baseline comparison.
No official baseline measurement or classification was changed.

The local SDK initially failed because installed workload manifests have
inconsistent Emscripten dependency versions. These ordinary .NET builds/tests
ran with command-local `MSBuildEnableWorkloadResolver=false`; no SDK installation
or repository configuration was changed. Office runs used the existing
developer-only `--allow-pending-tools` option, without changing tool approvals.
