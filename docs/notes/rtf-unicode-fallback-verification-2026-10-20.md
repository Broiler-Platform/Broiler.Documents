# RTF escaped Unicode fallback verification — 2026-10-20

The selected defect is text corruption when an RTF Unicode escape uses an
escaped control symbol as its fallback character. For example,
`\uc1\u233\{tail` should read as `étail`. The reader previously emitted the
fallback brace and then consumed the first real text character, producing
`é{ail`.

Character-producing control symbols now consume one pending fallback character,
matching the existing handling of literal bytes and hexadecimal escapes. The
change applies to body and running-content destinations. A zero fallback count
retains the escaped character.

Eight regression cases cover braces, backslashes, nonbreaking spaces/hyphens,
mixed escaped/literal fallback characters, zero fallback count and header/body
isolation. Seven failed before the fix; the zero-count case passed already.
All 279 RTF tests and 235 CLI tests pass using the isolated cached .NET runtime.
`git diff --check` reports no whitespace errors.

This is a text-decoding fidelity fix. It does not change the open office render
classifications.
