# RTF Unicode fallback group-boundary verification — 2026-10-20

The selected defect is loss of text when an unfinished Unicode fallback count
crosses a group boundary. For example, `\uc2\u233?{tail}` previously skipped
the `t` after the opening brace. A Unicode escape at the end of a header could
similarly consume the first character of the body.

The reader now clears pending fallback skipping at opening and closing group
delimiters. This follows the Unicode RTF rules in the
[Microsoft RTF 1.7 specification, mirrored PDF](https://latex2rtf.sourceforge.net/RTF-Spec-1.7.pdf):
scope delimiters end the skippable data before the delimiter. Escaped literal
braces remain character tokens and retain the existing fallback-count behavior.

Four new regression cases cover opening and closing boundaries, missing or
partial fallback text, and header/body isolation. All four failed before the
correction and pass after it. All 287 RTF tests and 235 CLI tests pass using the
isolated cached .NET runtime. `git diff --check` reports no whitespace errors.

This corrects text decoding and leaves office render classifications unchanged.
