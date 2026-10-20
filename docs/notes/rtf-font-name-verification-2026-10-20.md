# RTF font-name decoding verification — 2026-10-20

The selected defect is loss of non-ASCII and escaped characters in font-table
family names. The writer emits Unicode escapes for non-ASCII characters, but
the reader previously ignored those controls inside the font table. A name such
as `München` consequently came back as `M?nchen`, changing the logical family
used by the document. This also affected the recently supported default family.

Font-table decoding now handles Unicode code units and fallback counts,
hexadecimal bytes, and escaped literal braces/backslashes. The existing
group-scoped Unicode skip count is respected and decoded names remain metadata.
No font file is loaded by this decoding change.

Eight regression cases cover default and explicit run-family round trips for
accented Latin, Japanese, supplementary Unicode and literal escape characters,
plus zero/two-character fallback counts and hex-byte fallbacks. All eight were
observed failing before the fix and pass after it. All 260 RTF and 235 CLI tests
pass; `git diff --check` reports no whitespace errors.

This fixes the documented font-name round-trip defect. It does not change the
remaining letterhead render classifications; their fixture font names are ASCII.
