# Broiler.Documents.Html Conformance

Status: Delivered 2026-07-07.

`Broiler.Documents.Html` maps HTML documents or fragments through the same
`DocumentCodec` contract as RTF. It uses `Broiler.Dom.Html.HtmlDocumentParser`
for parsing and writes deterministic UTF-8 HTML with `HtmlSerializer`.

## Probe

| Input evidence | Confidence |
|---|---|
| `<!doctype html>`, `<html>`, `<head>`, or `<body>` prefix | High |
| Common fragment tags such as `<p>`, `<div>`, `<span>`, `<a>`, `<strong>`, `<ul>`, `<li>`, headings, or `<br>` | Medium |
| `.html` / `.htm` extension or `text/html` / `application/xhtml+xml` MIME hint only | Low |

## Reader Mapping

| HTML | Model | Notes |
|---|---|---|
| `<p>`, headings, `<li>` | `RichTextParagraph` | Empty paragraph elements are preserved. |
| `<div>`, sections, table cells, blockquote-like containers | Paragraph boundaries | Direct text in a block becomes a paragraph. |
| `<br>` | `U+2028` soft break | Same model convention as RTF line breaks. |
| `<b>`, `<strong>` | `InlineStyle.Bold` | CSS `font-weight:bold` or numeric `>=600` also maps. |
| `<i>`, `<em>` | `InlineStyle.Italic` | CSS `font-style:italic/oblique` also maps. |
| `<u>` | `InlineStyle.Underline` | CSS `text-decoration: underline` also maps. |
| `<s>`, `<strike>`, `<del>` | `InlineStyle.Strikethrough` | CSS `line-through` also maps. |
| CSS `text-transform: uppercase`, `font-variant: small-caps` | `InlineStyle.Capitalization` | Display only; the markup keeps the author's casing. |
| `<a href>` | `InlineStyle.LinkHref` | Absolute `http`, `https` and `mailto`, plus a non-empty `#fragment` whose name carries no whitespace, quote or second `#`; every other scheme, every relative target and a bare `#` are refused, on write as well as on read, with the link written as plain text. One predicate decides it for all five codecs (`DocumentLinkTarget`), which is what stopped them disagreeing. A fragment preserves the reference the source made and not a working jump: no codec here reads or writes a bookmark and the model has nowhere to put one, so the name it points at is not carried. Refusals are reported as `html.link`. |
| `<font face color>` | `FontFamily`, `Foreground` | Legacy compatibility only. |
| CSS `color`, `background-color` | `Foreground`, `Background` | Named colors, `#rgb`, `#rrggbb`, and `rgb(...)`. |
| CSS `font-family`, `font-size` | `FontFamily`, `FontSize` | Points are preserved; px converts using 96 DPI. |
| CSS `text-align` / `align` | `ParagraphStyle.Alignment` | `justify` degrades to left. |
| CSS `line-height` | `ParagraphStyle.LineSpacing` | Unitless/percent preferred; absolute lengths approximate. |
| CSS margins | spacing/indent fields | Left margin or padding converts to discrete indent levels. |
| `<ul>` / `<ol>` + `<li>` | `ListKind`, `IndentLevel` | Reader only; writer reports `html.list` for list kind. |
| CSS `page-break-before` / `break-before` | `ParagraphStyle.PageBreakBefore` | Read from the declarations resolved for the paragraph element - its own inline `style` attribute over a bare type rule from the document's stylesheet. Both spellings are current and both are read: `page-break-before` is the CSS2 property and is what LibreOffice writes, `break-before` is the CSS3 replacement the older one is now defined as an alias for. `always` and `page` are breaks; `auto` is the initial value and is not, nor is `avoid`, nor the column and region values, which name a fragmentation into containers this model does not have. `left`, `right`, `recto` and `verso` are read as a plain break and reported as `html.page-break` — the break is kept because a break is what the document asked for, and the side is not, because the model holds a flag and has no page parity. When the two properties disagree the break wins: CSS settles that by source order and this codec's declaration parser returns a dictionary rather than an order, so both are read as the one question of whether the paragraph states a break at all. A break on a containing `<div>` is not inherited by the paragraphs inside it — alignment descends, a break happens once, where it was stated. Text lying directly in a container rather than in a paragraph element becomes a paragraph carrying no paragraph style at all, a break included, which is what this reader already does with that text's alignment and spacing. |
| A `<style>` type-selector rule, `p { ... }` | The base beneath an element's own `style` attribute | A rule whose selector is a bare element name, or a selector list every item of which is one, contributes its declarations to those elements. The element's own inline `style` attribute wins property by property, so a property the inline style does not state still arrives and a property both state takes the inline value. Whatever this reader already does with a declaration it does with one resolved from here, the page break included: applying the stylesheet to a paragraph's line spacing and not to its page break would be a distinction no document makes. Two type rules stating one property for one element resolve by source order, the later winning, which is what CSS says for two rules of equal specificity. Comments are stripped first, the `<!--` / `-->` pair a word processor wraps a stylesheet in included, and a nested block is removed from a rule body before the declarations are split. A rule this codec does not implement is skipped and reported once as `html.css.rule`. |
| CSS `@page` `size` and margins | `PageGeometry` | The one rule read out of a `<style>` element rather than off an element. `size` takes a CSS page name (`a3`-`a5`, `b4`, `b5`, JIS `b4`/`b5`, `letter`, `legal`, `ledger`), one length, or two, with `portrait`/`landscape` orienting it; margins take the shorthand in all four arities and the four longhands. Only the first unnamed rule is read - a `@page cover` states the page for part of a document and this model holds one. Margin boxes are stepped over. A rule leaving no column to write in is refused rather than honoured. |

Text and attributes are HTML-decoded. Normal HTML whitespace collapses to single
spaces; `<pre>`, and any element declaring `white-space: pre`, `pre-wrap` or
`break-spaces`, preserves it. What collapses is the five characters CSS names —
space, tab, line feed, carriage return, form feed — and a non-breaking space is
deliberately not one of them, so `&nbsp;` beside an ordinary space keeps both.
Under a preserving declaration nothing is trimmed either, including at the start
of the paragraph, which is what the declaration is for.

## Writer Mapping

A paragraph whose text would not survive HTML's collapsing carries
`white-space: pre-wrap` so that it does: a tab, a repeated space, or a space at
either edge. Only such a paragraph carries it — the declaration changes how a
browser lays the paragraph out, so it is not spent on paragraphs that do not need
it. This condition used to name the tab alone, and a doubled space was written
out bare, collapsed on read, and reported nowhere.

| Model | HTML |
|---|---|
| Document | `<!DOCTYPE html><html><head><meta charset="utf-8"></head><body>...` |
| `PageGeometry` | `<style type="text/css">@page { size: Wpt Hpt; margin: T R B L; }</style>` in the head, and nothing when the document states no page. Written in points because the model is in points, so a round trip converts nothing; the margins are written as four values rather than folded to the shortest shorthand, so a diff of two exports shows a margin change where the margin changed. |
| Paragraphs | `<p>` elements |
| Soft breaks | `<br>` |
| Inline style fields | CSS on `<span>` or `<a>` |
| `LinkHref` | `<a href="...">` |
| `InlineStyle.Image` | `<img src="data:...;base64,...">` with `alt` and a CSS size |
| Paragraph alignment, line spacing, indent, spacing | CSS on `<p>` |
| `ParagraphStyle.PageBreakBefore` | `page-break-before: always`, in the paragraph's own `style` attribute beside whatever else that paragraph declares. The CSS2 property rather than the CSS3 `break-before: page`, even though the reader takes either: what is written has to be understood by whoever opens the file, and this is the spelling every browser and every word processor has read for twenty years — CSS Fragmentation keeps it as an alias for exactly that reason. Writing both spellings was the other option and was rejected, because it states one break twice and a consumer resolving them in the wrong order would be entitled to honour the second. |

`ListKind` is not written in the first subset; the writer preserves indentation
and emits an `html.list` diagnostic. An embedded image is written inline as a
base64 data URI (`html.image.datauri`), which keeps an exported page a single
file; the reader does not turn `<img>` back into a model image, so an image does
not survive an HTML round-trip. Non-model constructs such as tables, embedded
objects, scripts, stylesheets, forms, and metadata are not serialized from the
model.

## Security And Limits

- The reader never fetches external resources.
- `script`, `style`, `iframe`, `object`, `embed`, `img`, SVG/canvas, form inputs,
  metadata, and templates are skipped as non-model content. The text of a `style`
  element is never prose and that skip stays; the element is read separately, for
  its `@page` rule and its type-selector rules and nothing else.
- External or embedded content skips produce `html.skip.external` once per read.
- A style rule whose selector this codec does not implement, and any at-rule
  other than `@page`, is skipped and reported as `html.css.rule` once per read.
- Links are inert model metadata; unsafe schemes are dropped with `html.link`.
- `DocumentLimits.MaxDocumentBytes`, `MaxRunLength`, and `MaxParagraphCount` are
  enforced with diagnostics.

## Known Limitations

- CSS support is declaration-level and intentionally small. **Exactly one
  selector is matched: the bare element name.** A `p { line-height: 115% }` rule
  reaches the paragraphs it selects, and so does `h1, h2 { ... }`, in which every
  item of the list is such a name. **Nothing else is matched** - not a class, an
  id, an attribute, a pseudo-class, a combinator, a descendant selector, or `*`;
  and there is no `!important`, no `@media`, no external stylesheet, no computed
  style, and no layout. A selector list is all or nothing, so `h1, .lead { ... }`
  applies to neither rather than to the `h1` alone: two elements an author styled
  together coming back one styled and one not, with nothing in the result to say
  which happened to which, is a worse answer than not applying the rule.
- A type rule contributes to the element its selector names and to nothing else.
  It is not a computed-style inheritance pass: what happens after that is the
  descent this reader already had, which carries the model style fields it
  understands from a container into the blocks inside it, exactly as it does for
  a `style` attribute written on that container. So `td { font-size: 8pt }` makes
  the text in a cell small, and `div { margin-bottom: 12pt }` reaches the
  paragraphs in the `div` for the same reason `<div style="margin-bottom:12pt">`
  always did. That descent is a separate, older decision; a type rule feeds it
  rather than changing it.
- **The line is there because everything past it is the cascade, and the cascade
  is a browser.** Specificity here is one comparison with one answer - the
  element's own `style` attribute beats the sheet - which can be stated in a
  sentence and applied to an element without knowing anything about the rest of
  the document. Class matching is not: a class rule, an id rule and a descendant
  rule all aimed at one paragraph have to be ordered against each other before
  any of them can be applied, and building the thing that orders them is not a
  smaller job than building the whole engine. This is a document reader. It wants
  the formatting a producer stated, in the way that producer states it - and in
  the sample that prompted this, every selector LibreOffice wrote was either
  `@page` or `p`. `@page` remains the one at-rule read, because a page is a
  property of the document rather than of its presentation: it is where HTML
  keeps what DOCX keeps in `w:sectPr` and ODF in `style:page-layout`.
- **That sample was not the whole story, and the gap it hid is measurable.** A
  document containing a table makes LibreOffice write `td p { ... }` and
  `th p { ... }` as well, and those are descendant selectors, so the paragraphs
  inside cells get none of it. It is visible in the office conformance suite: the
  seeds without tables now score as their DOCX twins do - `plain-paragraphs` at
  an ink box of 9 pixels against the twin's 9, where before this it was 38 - and
  `table-simple` still sits at 107 against 11. Two things are behind that number
  and only one of them is this: HTML tables are flattened to their cell
  paragraphs regardless. It is recorded here rather than left for the next reader
  to rediscover from a number, and it is the case to weigh first if the line ever
  moves.
- A page break follows the same line as every other declaration. `p {
  page-break-before: always }` now reaches the paragraphs it names; a class rule
  written for the paragraphs a word processor wants broken still selects nothing,
  because working out which paragraphs it picked out is the cascade. This is a
  change of position: the break used to be read from the paragraph element alone,
  on the reasoning that a rule selecting paragraphs was a cascade this codec did
  not have. That was true while no selector at all was matched. Once one is, a
  document whose line spacing came from its stylesheet and whose page breaks did
  not would be arbitrary, and nothing in the code or the output could say why.
- A rule that is skipped is reported. `html.css.rule` is raised once per read
  when any rule was passed over for a selector this codec does not implement, or
  for being an at-rule other than `@page`. This is the opposite of the old
  position on a break in a stylesheet, and for a reason: that break was never
  parsed, so there was genuinely nothing to notice. A rule skipped now was read,
  understood to be formatting aimed at this document, and dropped anyway - a loss
  the codec can see is a loss it has to name.
- An external stylesheet is not fetched, so neither a `@page` rule nor a type
  rule in one is read. The reader never makes a network request, and finding out
  how a document's paragraphs are spaced should not be the thing that changes
  that.
- Tables are flattened through their text content and block boundaries.
- Relative links are currently dropped; only absolute `http`, `https`, and
  `mailto` links are retained.
- Writer output is semantic model HTML, not a preservation of source markup.
- Markdown and DOCX are implemented as peer codecs. Their format-specific
  coverage is documented separately; this HTML codec does not delegate to them.


## Standards And Rights

Both bodies that have stewarded HTML — the W3C and the WHATWG — operate
**royalty-free** patent policies, which is the strongest starting position of any
format this component reads. Both bind participants over Essential Claims and
both let a participant exclude specific patents during a disclosure window, so
neither is a statement that no relevant claim exists.

That is recorded in
[the HTML IP and licensing register](html-ip-licensing-register.md), where **every
row is now decided**. Decided is not cleared: patent-freedom is not claimed and no
freedom-to-operate determination has been made. Both policies let a participant
exclude patents during a disclosure window, which is what keeps a royalty-free
commitment from being a finding that no claim exists.

**One limit belongs here as much as there.** This codec does not tokenize or parse
HTML — `Broiler.Dom.Html.HtmlDocumentParser` does, in a separate repository. That
repository now has a rights register of its own, so the parsing half is recorded
rather than unasked; the scope limit is unchanged, and a claim about Broiler's
HTML support as a whole needs **both** registers rather than either.
