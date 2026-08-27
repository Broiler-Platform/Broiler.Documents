# Broiler.Documents.Markdown Conformance

Status: Delivered 2026-07-07.

`Broiler.Documents.Markdown` maps a CommonMark-oriented subset through the same
`DocumentCodec` contract as RTF and HTML. It has no DOM, UI, OS, or third-party
dependency.

## Probe

Markdown has no mandatory signature and plain text is valid Markdown, so probing
is intentionally conservative.

| Input evidence | Confidence |
|---|---|
| Obvious block marker such as ATX heading, list item, blockquote, or fenced code | Medium |
| Inline markers such as `**`, code spans, or inline links | Low |
| `.md` / `.markdown` extension or `text/markdown` / `text/x-markdown` MIME hint | Low |
| Unhinted plain text | No match |

## Reader Mapping

| Markdown | Model | Notes |
|---|---|---|
| Blank-line separated paragraphs | `RichTextParagraph` | Wrapped lines join with a space. |
| Two trailing spaces or trailing backslash | `U+2028` soft break | Same model convention as RTF/HTML soft breaks. |
| ATX headings `#` through `######` | Bold + approximate font size | Stored as normal paragraphs with inline style. |
| `-`, `*`, `+` list items | `ListKind.Bullet` | Leading indentation maps to `IndentLevel`. |
| `1.` / `1)` list items | `ListKind.Numbered` | Starting numbers are not preserved. |
| Blockquote `>` | `IndentLevel` | Nested `>` increments indentation. |
| Fenced code blocks | Monospace paragraph | Lines join with soft breaks. |
| `**strong**` / `__strong__` | `InlineStyle.Bold` | Simple toggle parser; complex nested edge cases are best-effort. |
| `*emphasis*` / `_emphasis_` | `InlineStyle.Italic` | |
| `` `code` `` | `FontFamily = "monospace"` | |
| `~~strike~~` | `InlineStyle.Strikethrough` | GitHub-flavored extension included for model symmetry. |
| `[label](href)` | `InlineStyle.LinkHref` | `http`, `https`, and `mailto` only; other schemes are dropped with `markdown.link`. |

## Writer Mapping

| Model | Markdown |
|---|---|
| Paragraphs | Blank-line separated paragraphs |
| Soft breaks | Two-space hard break + newline |
| `ListKind.Bullet` / `Numbered` | `- ` / `1. ` |
| `IndentLevel` | Two spaces per additional level |
| Bold / italic / strike | `**` / `*` / `~~` |
| Monospace font family | Code span |
| `LinkHref` | Inline link |

The writer emits UTF-8 Markdown with a trailing newline. Unsupported paragraph
style fields (alignment, line spacing, spacing before/after) produce
`markdown.paragraph-style`. An embedded image is written as CommonMark image
syntax with a base64 data URI (`markdown.image.datauri`); the reader does not
turn it back into a model image, so an image does not survive a Markdown
round-trip. Unsupported inline style fields (underline, size,
foreground/background color, and non-monospace font family) produce
`markdown.inline-style`.

## Security And Limits

- The reader never fetches link targets or external resources.
- Links are inert model metadata; unsafe schemes are dropped with
  `markdown.link`.
- `DocumentLimits.MaxDocumentBytes`, `MaxRunLength`, and `MaxParagraphCount` are
  enforced with diagnostics.
- HTML blocks are treated as text by this codec; HTML interchange belongs to
  `Broiler.Documents.Html`.

## Known Limitations

- No full CommonMark block parser: tables, reference links, images, HTML blocks,
  setext headings, thematic breaks, task lists, and nested container edge cases
  are outside the first subset.
- Inline parsing is intentionally simple and best-effort for malformed or deeply
  nested delimiter runs.
- Writer output is semantic model Markdown, not preservation of source markup.
