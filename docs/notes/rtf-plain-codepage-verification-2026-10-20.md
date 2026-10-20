# RTF plain-reset code-page verification — 2026-10-20

The selected defect is stale byte decoding after `\plain`. Selecting a font
updates the active code page from its charset, but the reset previously cleared
only the inline style. Subsequent text could inherit a different font family
while continuing to decode bytes with the previous font's code page.

The reader now keeps the document code page separately from the active font's
code page. `\plain` restores the declared default font's charset when available,
otherwise the document/caller code page. Both values follow the reader's group
state. Existing support and fallback diagnostics for code pages are unchanged.

Four regression cases cover default-font restoration, absence of a default font,
a default font using another charset and preservation of the document code page
after a font override. All four failed before the fix and pass after it. The
tests use the existing Latin-1 fallback for unsupported code pages to distinguish
the two states; this change does not claim new Windows-1251 decoding support.

All 283 RTF tests and 235 CLI tests pass using the isolated cached .NET runtime.
`git diff --check` reports no whitespace errors. Office render classifications
remain unchanged.
