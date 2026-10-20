# HTML quoted font-name round-trip verification — 2026-10-20

The selected defect is loss of literal quotes and backslashes in font-family
names during model → HTML → model conversion. The writer escapes these
characters in its quoted CSS string, but the reader previously retained the
backslash escapes and stripped all quote characters from both ends of the name.

Font-family parsing now removes only a matching outer quote pair and decodes
escaped single quotes, double quotes and backslashes inside it. It preserves
literal quote characters within the name, including at its edges. General CSS
hexadecimal escapes remain outside the supported subset.

Four regression cases cover a backslash, embedded double quotes and literal
single/double quotes at the name's edges. All four failed before the fix and
pass after it. All 164 HTML tests and 235 CLI tests pass using the isolated
cached .NET runtime. `git diff --check` reports no whitespace errors.

This is a codec round-trip correction; office render classifications remain
unchanged.
