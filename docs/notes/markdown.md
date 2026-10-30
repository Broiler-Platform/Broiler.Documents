# Broiler.Documents.Markdown Conformance

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
| `[label](href)` | `InlineStyle.LinkHref` | Targets the shared link predicate admits (see Security And Limits); others are dropped with `markdown.link`. |
| `[^label]` and `[^label]: text` | `InlineStyle.Note`, `DocumentNote` | Inline citations and footnote definitions. |
| `![description](data:...)` | `InlineStyle.Image` (`InlineImage`) | Base64 or percent-encoded `data:` URIs are decoded and admitted through the caller's resource policy, with `AltText` and `Name` set to the description. |
| `![description](src)`, other targets | Description text | External or relative image targets are not fetched: `markdown.image.dropped`. |

An embedded image falls back to its description text when its payload exceeds
`MaxBinBytes` (`markdown.image.limit`), the URI or payload is malformed
(`markdown.image.malformed`), or the resource policy denies it
(`markdown.image.denied`). Images therefore round-trip only as embedded
`data:` URIs read back under a permitting policy.

## Writer Mapping

The writer emits UTF-8 Markdown with a trailing newline, property by property:

| Model | Markdown |
|---|---|
| Paragraphs | Blank-line separated paragraphs |
| Soft breaks | Two-space hard break + newline |
| `ListKind.Bullet` / `Numbered` | `- ` / `1. ` |
| `IndentLevel` | Two spaces per additional level |
| Bold / italic / strike | `**` / `*` / `~~` |
| Monospace font family | Code span |
| `LinkHref` | Inline link |
| `InlineStyle.Note` | `[^label]` in the text and `[^label]: text` after the body, later paragraphs indented, as GitHub and most dialects write a footnote |
| `InlineImage` | `![alt](data:...;base64,...)`, reported as `markdown.image.datauri`; left out with `markdown.image.omitted` when the resource policy withholds its bytes |
| Alignment, line spacing or a stated line height, spacing before/after | Nothing, reported as `markdown.paragraph-style` |
| Underline, size, foreground/background color, non-monospace font family | Nothing, reported as `markdown.inline-style` |
| Tracked changes | The document as accepting them would leave it — deleted text left out, a deleted paragraph break joining its paragraphs, inserted text plain — reported once as `markdown.revision` |
| `PageBreakBefore` | An ordinary paragraph, reported as `markdown.page-break` |
| `ColumnBreakBefore` | An ordinary paragraph, reported as `markdown.column-break` |
| Hidden body section | Visible paragraphs, reported as `markdown.section.hidden`; ODT retains the flag, and the shared raster paginator omits hidden body sections |

The writer escapes the characters that would otherwise be read back as markup:
`\` `` ` `` `*` `_` `[` `]` `(` `)` `#` `+` `-` `.` `!` and `~`. The tilde
matters because doubled it is this reader's own strikethrough.

A list is written as bulleted or numbered items, which is all Markdown states.
A list whose labels such items would not read back as - starting at four,
counting in letters or roman numerals, or counting on past a paragraph between
its items - is reported once as `markdown.list.numbering`; a list numbered 1, 2,
3 loses nothing and is not reported.

`markdown.page-break` and `markdown.column-break` are codes of their own rather
than part of `markdown.paragraph-style`, because a caller deciding whether a
document can be re-exported to something paginated asks a different question
than one chasing lost spacing.

## Security And Limits

- The reader never fetches link targets or external resources.
- Links are inert model metadata. Absolute `http`, `https` and `mailto` are
  admitted, plus a non-empty `#fragment` whose name carries no whitespace,
  quote or second `#`; every other scheme, every relative target and a bare
  `#` are refused, on read and on write, and reported as `markdown.link`. A
  refused target is written as plain text with the run's other formatting
  kept: a link admitted under one policy must not launder itself into output
  under another, which is the position `PdfUriPolicy` states for the PDF codec.
  One predicate decides it for reader and writer in all five codecs
  (`DocumentLinkTarget`). A fragment preserves the reference the source made,
  not a working jump: no codec here reads or writes a bookmark and the model
  has nowhere to put one, so the name it points at is not carried.
- `DocumentLimits.MaxDocumentBytes`, `MaxRunLength`, and `MaxParagraphCount` are
  enforced with diagnostics.
- HTML blocks are treated as text by this codec; HTML interchange belongs to
  `Broiler.Documents.Html`.

## Known Limitations

- No full CommonMark block parser: tables, reference links, HTML blocks, setext
  headings, thematic breaks, task lists, and nested container edge cases are
  outside the first subset.
- There is no page or column break and there will not be one: Markdown has no
  page and no columns, so this is a property the format will not carry, not
  one not yet implemented. There is no syntax for it and no honest fallback,
  since a horizontal rule is a rule and a form feed is a character in the
  prose. `ParagraphStyle.PageBreakBefore` is written away and reported as
  `markdown.page-break` rather than dropped silently; a Markdown round trip
  loses it, and no reader can put it back, because nothing in the text says it
  was ever there.
- Inline parsing is intentionally simple and best-effort for malformed or deeply
  nested delimiter runs.
- Writer output is semantic model Markdown, not preservation of source markup.

## Standards and rights

Markdown has no standards body and no patent instrument. The rights position,
the reading of the licence's naming clause, the approved labels and the
provenance findings are in [the Markdown register](../ip/markdown.md). This is a
CommonMark-oriented subset and the limits above enumerate what it does not
implement, so nothing may describe it as CommonMark-compliant.
