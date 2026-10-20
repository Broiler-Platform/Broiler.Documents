# Three further small fidelity fixes — 2026-10-20

The selected corrections are:

1. **HTML declaration splitting inside quoted font names.** A semicolon in
   `'Alpha;Beta'` previously ended the declaration early. Declaration splitting
   now recognizes quoted strings and keeps the name intact.
2. **HTML family-list splitting inside quoted font names.** A comma in
   `'Alpha,Beta'` previously selected only `Alpha`. Family-list splitting now
   finds the first comma outside quotes, preserving the chosen name while still
   ignoring fallback families as before.
3. **ODT line-spacing float conversion.** A positive percentage could be finite
   as a double but overflow to infinity or underflow to zero in the model's
   float field. The reader now checks the converted value and retains inherited
   spacing when it is unrepresentable, reporting `odt.linespacing.range`.

Six new regression cases failed before the changes: quoted semicolons and
commas in inline styles and type rules, plus ODT overflow and underflow while
inheriting 1.5-line spacing. All pass after the corrections. HTML delimiter
handling also respects escaped quotes while scanning; general CSS escape
decoding remains outside the supported subset.

The new ODT diagnostic is recorded in its conformance document, as required by
the codec's diagnostic-coverage guard. Office render classifications are
unchanged; these fixes address codec fidelity directly.

Final validation: all 152 HTML tests, 252 ODT tests and 235 CLI tests pass
(639 total), including the ODT documentation guard. `git diff --check` reports
no whitespace errors. Tests used the isolated cached .NET runtime, with
command-local `AllowMissingPrunePackageData=true` for the ODT restore under the
installed preview SDK.
