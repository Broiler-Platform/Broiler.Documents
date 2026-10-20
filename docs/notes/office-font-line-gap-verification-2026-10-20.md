# Mapped-font line-gap verification — 2026-10-20

The selected defect is the remaining per-line vertical drift in the four
`line-break-within-paragraph` render rows. With explicit font mappings, the CLI
already used the mapped face's `hhea` ascender and descender, but omitted its
line gap. The local Liberation Serif regular face declares an 87-unit gap at
2048 units per em, about 0.51 points at 12 points. At 144 DPI this accounts for
roughly one pixel of leading on each line, matching the observed drift.

The CLI now reads the first mapped face's `hhea` line gap from TTF, OTF or TTC
files and supplies it separately to the paginator. The gap increases the line
box below the glyph baseline, so the first baseline stays fixed while subsequent
lines advance by the declared leading. Missing, malformed, negative or unmapped
metrics keep the zero-gap fallback. Mixed-font lines use the largest gap present,
as they already do for ascent and descent.

Local before/after evidence uses Windows, LibreOffice 26.2.6.3, Poppler 26.07.0,
144 DPI and explicitly mapped local Liberation fonts in both runs:

| Format | Ink box before | Ink box after | Profile before | Profile after | Blur before | Blur after |
|---|---:|---:|---:|---:|---:|---:|
| DOCX | 14 px | 1 px | 0.940642 | 0.999856 | 0.051809 | 0.000003 |
| ODT | 14 px | 1 px | 0.940559 | 0.999854 | 0.051799 | 0.000003 |
| HTML | 14 px | 0 px | 0.940074 | 0.999817 | 0.052162 | 0.000003 |
| RTF | 10 px | 6 px | 0.958964 | 0.992545 | 0.056613 | 0.025866 |

DOCX, ODT and HTML now have tight ink boxes, exact ink profiles and clean blurred
differences. RTF also improves, while its remaining coverage and spacing
difference stays visible. Reports are `D:\office-next-before.json` and
`D:\office-linegap-after.json`.

Six new regression cases cover applying the supplied gap without moving the
baseline, rejecting invalid gap values, reading an `hhea` table, and safely
ignoring a malformed font. All 23 pagination tests and 231 CLI tests pass. The
office run passed 28 checks and skipped four pinned-baseline comparisons.

The official baseline remains unchanged because this toolchain and font set do
not match its stamp. The suspected-defect rows remain open for verification on
the pinned Linux toolchain. The existing developer-only
`--allow-pending-tools` option was used; no register approval was changed.
