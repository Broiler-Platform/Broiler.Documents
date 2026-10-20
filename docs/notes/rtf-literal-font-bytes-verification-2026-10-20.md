# RTF literal font-name bytes verification — 2026-10-20

The selected defect is inconsistent code-page decoding inside font-table names.
Hexadecimal escapes used the active code page, but literal high bytes were copied
unchanged. A Windows-1252 smart quote byte (`0x92`) in a family name therefore
became a control character instead of U+2019, changing the font family selected.

Literal font-name bytes now use the same decoder and diagnostic policy as hex
bytes and body text, after consuming Unicode fallback characters. ASCII names
and Unicode escapes retain their existing handling.

A regression constructs raw RTF bytes and verifies both the imported default
family and body-text isolation. It failed before the fix. Verification uses a
temporary .NET 10 runtime assembled from existing cached package files and the
installed SDK, because the installed x64 .NET 10 runtime remains unavailable.
No machine installation or global configuration was changed.

All 271 RTF tests and 235 CLI tests pass. The CLI result also completes the
previous inherited-font-size change's blocked validation. This decoder fix does
not change the remaining office render classifications, whose font names use
ASCII characters.
