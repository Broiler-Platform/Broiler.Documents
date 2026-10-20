# Three small fidelity fixes — 2026-10-20

The selected defects and corrections are:

1. **RTF font names containing semicolons.** Export wrote a raw `;` inside the
   family name, terminating its font-table entry early. The writer now escapes
   it as `\u59?` within names. Default and explicit run families round-trip with
   the semicolon intact. Body text escaping is unchanged.
2. **RTF control words used as Unicode fallbacks.** The reader did not count
   control words towards the pending fallback count, so it could emit a tab or
   line break, apply bold formatting, and discard the next real text character.
   A skipped control word and its argument now consume one fallback unit without
   applying their effects. This follows the Unicode fallback counting rule in
   [Microsoft's RTF specification, mirrored PDF](https://latex2rtf.sourceforge.net/RTF-Spec-1.7.pdf).
3. **Nonfinite HTML numeric properties.** Float parsing accepted NaN, infinity,
   numeric overflow and lengths whose unit conversion overflowed. These values
   could replace inherited font sizes, margins and line spacing. The parser now
   rejects nonfinite input and conversion results, retaining inherited values.

Fourteen new regression cases were observed failing before the corrections:
one font-family round trip (checking default and explicit families), four RTF
fallback cases and nine HTML numeric cases. All pass after the changes.

Validation: all 292 RTF tests, 148 HTML tests and 235 CLI tests pass (675 total).
`git diff --check` reports no whitespace errors. Tests used the isolated cached
.NET runtime. HTML restore needed command-local
`AllowMissingPrunePackageData=true` for the installed preview SDK; no repository
or global SDK configuration was changed.

These are codec fidelity corrections. They do not reclassify the remaining
office render rows or claim a measured improvement in those fixtures.
