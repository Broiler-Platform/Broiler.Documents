# RTF multiplier line-spacing verification — 2026-10-19

The selected defect is `line-break-within-paragraph/rtf`. Its generated RTF
declares `\sl276\slmult1`, but the reader previously ignored those controls.
The existing paragraph model can represent the requested 1.15 multiplier, making
this a small correction with no new layout policy or model extension.

The reader now imports positive multiplier spacing, respects group restoration
and paragraph resets, and preserves final body paragraph properties when there
is no terminating `\par`. The writer emits multiplier spacing in increments of
1/240. Fixed/at-least heights remain unsupported. The interpretation follows
[Microsoft's RTF paragraph control-word documentation](https://learn.microsoft.com/en-us/windows/win32/controls/about-text-object-model).

Local before/after evidence uses Windows, LibreOffice 26.2.6.3, Poppler 26.07.0,
144 DPI and explicitly mapped Liberation fonts in both runs. Both runs include
the preceding mapped-font vertical-metrics correction.

| Measurement | Before | After |
|---|---:|---:|
| Page count (both renderers) | 1 | 1 |
| Ink-box difference | 58 px | 10 px |
| Ink-profile similarity | 0.796916 | 0.958964 |
| Blurred difference ratio | 0.063473 | 0.056613 |
| Ink coverage difference | 0.004101 | 0.004158 |

Position and profile improve; coverage difference increases slightly. The
ink-box band improves from loose to near and the profile band from poor to fair.
Remaining visual differences mean this is a reduction, not complete resolution.
Reports are `D:\office-rtf-spacing-before.json` and
`D:\office-rtf-spacing-after.json`.

Twelve new regression cases cover multiplier values, read/write round trips,
forced line breaks, group restoration, paragraph resets, control ordering,
unsupported forms, and a final paragraph without `\par`. All 247 RTF tests and
220 CLI tests pass.

The official baseline remains unchanged and the suspected-defect row remains
open: this local toolchain and font set differ from the pinned baseline stamp.
Each office run passed seven checks and skipped the baseline comparison. The
existing developer-only `--allow-pending-tools` option was used; no register
approval was changed.
