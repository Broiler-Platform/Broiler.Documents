# Formatting Codes canonical grammar, version 1

This file freezes the canonical public text emitted by
`Broiler.Documents.FormatCodes`. The projector is a semantic view of a
normalized `RichTextDocument`; it is not a reconstruction of imported file
controls. Version 1 is output-only. A future parser must accept this grammar but
cannot redefine its canonical spelling.

## General rules

- Canonical text is a .NET UTF-16 string and encodes as Unicode without loss.
- Delimiters and command labels are printable ASCII. State keywords are
  uppercase. Command-label capitalization is fixed exactly as shown below.
- Default inline and paragraph values are implicit.
- Each paragraph begins with default inline state. Active inline state is closed
  before a paragraph boundary.
- At a run boundary, changed active properties close in reverse order, then new
  active properties open in this order: Bold, Italic, Underline, Strike, Font,
  Size, Text Color, Highlight, Link.
- Integer and finite floating-point values use invariant culture. Floating-point
  values use the shortest round-trippable representation.
- `NAN`, `POSITIVE_INFINITY`, and `NEGATIVE_INFINITY` preserve non-finite model
  values and produce diagnostics (`FC1004` for paragraph metrics and `FC1006`
  for inline font sizes).
- Colors are straight-alpha `#RRGGBBAA` values.
- No whitespace is permitted inside a command except where shown or inside a
  quoted value.

The canonical signed-off example is exactly:

```text
[Bold ON]Hello World![Bold OFF]
```

## Inline commands

| Property | Set/open | Reset/close |
|---|---|---|
| Bold | `[Bold ON]` | `[Bold OFF]` |
| Italic | `[Italic ON]` | `[Italic OFF]` |
| Underline | `[Underline ON]` | `[Underline OFF]` |
| Strikethrough | `[Strike ON]` | `[Strike OFF]` |
| Font family | `[Font "value"]` | `[Font DEFAULT]` |
| Font size | `[Size number]` | `[Size DEFAULT]` |
| Foreground | `[Text Color #RRGGBBAA]` | `[Text Color DEFAULT]` |
| Highlight | `[Highlight #RRGGBBAA]` | `[Highlight NONE]` |
| Link | `[Link "value"]` | `[Link OFF]` |

Changing one active scalar value to another emits its reset followed by its new
value. Empty link strings have the same effective state as no link.

## Paragraph commands

Non-default paragraph properties appear at paragraph start in this order:

| Property | Canonical command |
|---|---|
| Alignment | `[Align CENTER]`, `[Align RIGHT]` |
| List | `[List BULLET]`, `[List NUMBERED]` |
| Indent | `[Indent integer]` |
| Line spacing | `[Line Spacing number]` |
| Spacing before | `[Space Before number]` |
| Spacing after | `[Space After number]` |

Left alignment, no list, indent zero, line spacing one, and zero paragraph
spacing are omitted. Unknown enum values remain inspectable as
`[Align UNKNOWN integer]` or `[List UNKNOWN integer]` and produce diagnostics.
Out-of-domain metrics are preserved rather than silently corrected.

## Structure and content escaping

| Model content or boundary | Canonical output |
|---|---|
| Tab (`U+0009`) | `[Tab]` |
| Soft line break (`U+2028`) | `[Line Break]` |
| Embedded image (`U+FFFC`) | `[Image]` |
| Boundary between paragraphs | `[Paragraph Break]` followed by LF |
| Paragraph with no characters | `[Empty Paragraph]` |
| Literal backslash | `\\` |
| Literal `[` | `\[` |
| Literal `]` | `\]` |
| Other control, format, line-separator, or paragraph-separator code unit | `\u{XXXX}` |
| Unpaired UTF-16 surrogate | `\u{XXXX}` plus diagnostic `FC1005` |

Printable Unicode, including valid surrogate pairs and combining marks, remains
literal. Within quoted values, `\`, `[`, `]`, and `"` receive a leading
backslash; non-printing code units use `\u{XXXX}`.

## Mappings and overlays

Canonical tokens concatenate exactly to `FormatCodeProjection.Text`. Every
projected UTF-16 boundary maps to either the source position before or after its
token. Literal text maps linearly. Expanded escapes and structure tokens map the
first half to `SourceBefore` and the second half to `SourceAfter`; exact middle
ties map before. Multiple zero-width codes at one source position are ordered by
the transition tables above, and before/after affinity selects the earliest or
latest projected caret.

Pending caret formatting is a separate, non-canonical overlay. It uses display
labels such as `[Pending Bold ON]`, appears only in `PendingTokens`, contributes
zero characters to canonical text, and is excluded from canonical copy/export.

## Resource behavior

Projection has explicit output-character, token-count, and quoted-value limits
and accepts cancellation. A limit throws `FormatCodeProjectionLimitException`
before the configured bound is exceeded. Projection performs no file, network,
clipboard, UI, or telemetry operation.

Phase 1 measurements freeze a conservative host recommendation: project away
from the UI path above 100,000 source UTF-16 characters or 10,000 combined
paragraph/run structural units. `FormatCodeProjectionPolicy` exposes this rule;
the synchronous projector does not create threads or publish stale snapshots.
