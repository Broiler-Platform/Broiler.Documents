# Office line-height investigation — 2026-10-19

Follow-up: [mapped-font vertical metrics verification](office-font-metrics-verification-2026-10-19.md)
records a later CLI change that reduces the DOCX row's measured difference.
[Mapped-font line-gap verification](office-font-line-gap-verification-2026-10-20.md)
records the subsequent leading correction.

Investigation of the four `line-break-within-paragraph` render rows found a
separate reproducible paginator bug: populated lines included the default font
in their ascent/descent maxima even when no piece used it. A line entirely in
10-point text could consequently inherit a 24-point default's height. The fix
uses actual pieces for populated lines and default metrics only for empty ones.
Three regression tests cover explicit sizing across forced breaks, empty lines,
and mixed sizes with a line-spacing multiplier. The explicit-sizing test failed
before the change.

This correction did **not** improve the four office rows in the local run.
Before and after runs at 144 DPI on Windows, LibreOffice 26.2.6.3 and Poppler
26.07.0, without pinned fonts, produced identical observations:

| Format | Ink-box difference | Ink-profile similarity |
|---|---:|---:|
| DOCX | 66 px | 0.869914 |
| ODT | 66 px | 0.869913 |
| HTML | 66 px | 0.871518 |
| RTF | 8 px | 0.935313 |

A further DOCX run with local Liberation font files explicitly mapped measured
32 px and 0.900049, close to the stored baseline. It does not establish a fix:
the official Linux toolchain and `office-fonts-1` stamp were not used. All official
baseline comparisons were skipped, and no baseline row was updated or closed.
The developer runs used the existing `--allow-pending-tools` switch; no tool
approval was changed.

Inspection of the Graphics implementation identifies another approximation:
its fallback `GetLineHeight` returns `Math.Ceiling(font.Size * 1.25)`, and the
paginator's text pieces use an ascent of `font.Size * 0.8`. Font mapping supplies
glyph advances but does not turn these vertical approximations into metrics from
the mapped face. This is a candidate explanation for accumulated leading drift,
not proof of the remaining rows' root cause. Replacing it needs consistent
font-derived metrics for layout and painting and controlled-font comparisons;
changing a multiplier to fit one seed would not establish that correctness.
