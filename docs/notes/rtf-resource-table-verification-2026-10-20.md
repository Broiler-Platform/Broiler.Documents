# RTF non-body resource-table verification — 2026-10-20

The selected defect is loss of fonts and colours used only in headers, footers,
or body-shape text during RTF export. The writer built its font and colour tables
from body paragraphs alone. It then emitted running-content and shape runs using
the same tables; missing entries resolved to index zero, silently selecting the
default font or automatic colour and losing highlights.

Resource collection now scans body paragraphs, body-shape paragraphs and every
header/footer paragraph variant before writing the tables. The existing table
interning keeps shared entries unique and preserves body-resource ordering.

Seven regression cases cover the default, first and even variants of both
headers and footers, plus body-shape text. Each uses a family, foreground colour
and highlight absent from the body. All seven failed before the correction and
pass after it, including checks that body formatting stays independent.

All 267 RTF tests and 235 CLI tests pass. This is verified through model → RTF →
model comparisons; it does not claim to resolve the open office letterhead
render rows or change their baseline classifications.
