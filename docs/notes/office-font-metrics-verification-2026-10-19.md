# Mapped-font vertical metrics verification — 2026-10-19

The selected row is `line-break-within-paragraph/docx`. With explicit font
mappings, the CLI previously measured horizontal advances from the mapped font
but retained approximate vertical metrics (80% ascent and a rounded 125% line
height). Repeated forced breaks accumulated that discrepancy.

The CLI now supplies the mapped face's `hhea` ascent and descent through the
instance-local `LayoutSettings.FontVerticalMetrics` callback. Inspection uses
the existing Graphics font reader and is cached per mapped file. Text runs,
tabs, empty lines and the initial wrap-height estimate use those metrics. The
painter still compensates for the renderer's origin-to-baseline convention, so
the resolved baseline is shared by differently sized runs. No ambient font
discovery, global metrics registration or new dependency was introduced.

Missing mappings or unavailable/invalid metrics retain the existing fallback.
The change does not claim full OpenType line-layout behavior: line gap and
`OS/2` typographic metric selection remain outside this calculation.

Local before/after office evidence uses Windows, LibreOffice 26.2.6.3, Poppler
26.07.0, 144 DPI and explicitly mapped local Liberation fonts in both runs:

| Measurement | Before | After |
|---|---:|---:|
| Page count (both renderers) | 1 | 1 |
| Ink-box difference | 32 px | 14 px |
| Ink-profile similarity | 0.900049 | 0.940642 |
| Blurred difference ratio | 0.053109 | 0.051809 |
| Ink coverage difference | 0.000189 | 0.000316 |

The positioning and profile measures improve; coverage difference increases
slightly. The render still has differences and remains in the same categorical
bands. This is a measured reduction in the selected defect, not complete
agreement with LibreOffice.

Six new pagination regressions cover supplied metrics across text, tabs and
empty lines, invalid values, and isolation between layout instances. All 19
pagination tests and 220 CLI tests pass.

The pinned Linux baseline was not regenerated: the local toolchain and font set
do not match its stamp. The `suspected-defect` row remains open for verification
on that toolchain and assessment of the remaining differences. The office run
used the existing developer-only `--allow-pending-tools` option; no register
approval was changed.
