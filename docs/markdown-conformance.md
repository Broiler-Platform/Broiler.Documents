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
| `![description](src)` | The description, as text | No image is built. The description is what CommonMark nominates as the fallback, so it stands in for the picture and the rest of the syntax goes, reported as `markdown.image.dropped`. An empty description is valid and leaves nothing behind. |

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
| `PageBreakBefore` | Nothing, reported as `markdown.page-break` |

The writer escapes the characters that would otherwise be read back as markup:
`\` `` ` `` `*` `_` `[` `]` `(` `)` `#` `+` `-` `.` `!` and `~`. The last was
missed until the corpus suite caught it — doubled, a tilde is this reader's own
strikethrough, so literal tildes went out bare and came back as a struck run with
the tildes gone.

The writer emits UTF-8 Markdown with a trailing newline. Unsupported paragraph
style fields (alignment, line spacing, spacing before/after) produce
`markdown.paragraph-style`. An embedded image is written as CommonMark image
syntax with a base64 data URI (`markdown.image.datauri`); the reader does not
turn it back into a model image, so an image does not survive a Markdown
round-trip — it comes back as its description, and nothing of the syntax is left
in the text. That last part had to be fixed: `!` was not a character the inline
parser knew, so the `[…](…)` after it was taken as a link, the marker stayed in
the prose, and the loss was reported as a dropped hyperlink. Unsupported inline style fields (underline, size,
foreground/background color, and non-monospace font family) produce
`markdown.inline-style`.

A paragraph that starts a new page is written as an ordinary paragraph and
reported as `markdown.page-break`. Markdown has no page, so it has no page break:
there is no syntax to write one in and no fallback that would be honest, since a
horizontal rule is a rule and a form feed is a character in the prose. What is
left is to say so, which is the only thing separating a codec that dropped
something from one that was never given it — and for this construct it is the
whole of the difference, because until the model could carry a page break every
codec here dropped one in silence. It has its own code rather than joining
`markdown.paragraph-style`: that diagnostic names the three fields it drops, and
a reader deciding whether a document can be re-exported to something paginated is
asking a different question than one chasing lost spacing.

## Security And Limits

- The reader never fetches link targets or external resources.
- Links are inert model metadata. Absolute `http`, `https` and `mailto`, plus a non-empty `#fragment` whose name carries no whitespace, quote or second `#`; every other scheme, every relative target and a bare `#` are refused, on write as well as on read, with the link written as plain text. One predicate decides it for all five codecs (`DocumentLinkTarget`), which is what stopped them disagreeing. A fragment preserves the reference the source made and not a working jump: no codec here reads or writes a bookmark and the model has nowhere to put one, so the name it points at is not carried. Refusals are reported as
  `markdown.link`. The same allow-list applies when writing. A target the reader would refuse is written as plain text with the run's other formatting kept, and reported — a link admitted under one policy must not be able to launder itself into output under another, which is the position `PdfUriPolicy` states for the PDF codec and now holds across all five. Reader and writer share one predicate so the two cannot drift apart again.
- `DocumentLimits.MaxDocumentBytes`, `MaxRunLength`, and `MaxParagraphCount` are
  enforced with diagnostics.
- HTML blocks are treated as text by this codec; HTML interchange belongs to
  `Broiler.Documents.Html`.

## Known Limitations

- No full CommonMark block parser: tables, reference links, HTML blocks, setext
  headings, thematic breaks, task lists, and nested container edge cases are
  outside the first subset.
- Inline images are recognized and deliberately not built: the reader replaces
  `![description](src)` with its description and reports
  `markdown.image.dropped`. Recognizing them is what keeps the marker and the
  destination out of the text; building them is a separate decision, and one
  that would have to say what a reader may do with a `data:` payload.
- There is no page break and there will not be one. `ParagraphStyle.PageBreakBefore`
  is written away and reported as `markdown.page-break`; a Markdown round trip
  loses it, and no reader here can put it back, because nothing in the text says
  it was ever there.
- Inline parsing is intentionally simple and best-effort for malformed or deeply
  nested delimiter runs.
- Writer output is semantic model Markdown, not preservation of source markup.


## Standards And Rights

Markdown has no standards body, no IPR mode, and no patent declaration. There is
nothing to negotiate and nothing to read, which is a comfortable position and a
weaker kind of one: it is assessed on the absence of any instrument rather than
on the presence of a good one.

The one live obligation is a copyright licence with a naming clause —
"Neither the name 'Markdown' nor the names of its contributors may be used to
endorse or promote products derived from this software" — which binds products
derived from John Gruber's implementation. Nothing here is: this codec was written
for this repository and imports nothing.

Both are recorded in
[the Markdown IP and licensing register](markdown-ip-licensing-register.md), where
**every row is now decided** — including the reading of that clause, which was
accepted on the strength of an inspection rather than an argument: there is no
imported implementation in this repository for it to attach to.

One wording rule follows from this document rather than from the register. This is
a **CommonMark-oriented subset** and the limits above enumerate what it does not
implement, so nothing may describe it as CommonMark-compliant.
