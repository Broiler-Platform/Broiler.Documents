# Broiler.Documents.Html Conformance

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
| `<div>`, sections, blockquote-like containers | Paragraph boundaries | Direct text in a block becomes a paragraph, keeping the container's resolved alignment, spacing and indentation, including text after a child paragraph. Containers with `title="header"` or `title="footer"` (LibreOffice's HTML export convention for running content) go to `RichTextDocument.RunningContent`, not the body. |
| `<table>`, `<tr>`, `<td>`, `<th>` | `DocumentTable`, `TableRow`, `TableCell` | Retains nested grids, spans, empty cells and captions; `th` defaults to bold and centered. Legacy `border`, `cellpadding`, `width` and cell backgrounds are read. Tables without a grid use content-based column sizing. CSS table geometry, separate border spacing and column declarations are unsupported. |
| `<br>` | `U+2028` soft break | Same model convention as RTF line breaks. |
| `<b>`, `<strong>` | `InlineStyle.Bold` | CSS `font-weight:bold` or numeric `>=600` also maps. |
| `<i>`, `<em>` | `InlineStyle.Italic` | CSS `font-style:italic/oblique` also maps. |
| `<u>` | `InlineStyle.Underline` | CSS `text-decoration: underline` also maps. |
| `<s>`, `<strike>`, `<del>` | `InlineStyle.Strikethrough` | CSS `line-through` also maps. |
| CSS `text-transform: uppercase`, `font-variant: small-caps` | `InlineStyle.Capitalization` | Display only; the markup keeps the author's casing. |
| `<a href>` | `InlineStyle.LinkHref` | Absolute `http`, `https` and `mailto`, plus a non-empty `#fragment` whose name carries no whitespace, quote or second `#`. Every other scheme, every relative target and a bare `#` are refused, on read and on write (the link written as plain text), and reported as `html.link`. One predicate decides it for all five codecs (`DocumentLinkTarget`). A fragment preserves the reference, not a working jump: no codec reads or writes a bookmark and the model has nowhere to put one. |
| `<img>` with `data:` URI | `InlineStyle.Image` (`InlineImage`) | Base64 or percent-encoded payloads are decoded and admitted via caller resource policy. CSS or attribute `width`/`height` map to points; `alt` maps to `AltText`, `title` to `Name`. Images inside `<a>` keep `LinkHref`. Payloads exceeding `MaxBinBytes` report `html.image.limit`, malformed URIs `html.image.malformed`, policy denials `html.image.denied`. External/relative images are skipped with `html.skip.external`. |
| `<font face color>` | `FontFamily`, `Foreground` | Legacy compatibility only. |
| CSS `color`, `background-color` | `Foreground`, `Background` | Named colors, `#rgb`, `#rrggbb`, and `rgb(...)`. A background with zero alpha — `transparent`, or an `rgba(...)` resolving to it — is read as *no* background (the model's distinct sentinel), not as an invisible colour that would highlight every run. `color: transparent` is kept: invisible text is something a document can mean. |
| CSS `font-family`, `font-size` | `FontFamily`, `FontSize` | Points are preserved; px converts using 96 DPI. |
| CSS `text-align` / `align` | `ParagraphStyle.Alignment` | `justify` degrades to left. |
| CSS `line-height` | `ParagraphStyle.LineSpacing`, or an at-least `LineHeight` | A number or percentage is a multiple; a length is the least a line is, as LibreOffice's HTML import reads it (`ParseCSS1_line_height`). |
| CSS margins | spacing/indent fields | Left margins keep exact point offsets, accumulating through containing blocks; canonical offsets still use the equivalent legacy indent level. Padding keeps the older approximate handling; this is not a general CSS box model. |
| `<ul>` / `<ol>` + `<li>` | `ListKind`, `ListReference`, `IndentLevel` | Independent list definitions restart counters; `ol type`, `start` and `li value` are retained. Nested ordered lists default to decimal. Only the first paragraph of each item has a label; nested items count separately. Logical nesting is separate from physical container margins. Reversed counting is unsupported. The writer reports `html.list` and writes no list markup. |
| CSS `page-break-before` / `break-before` | `ParagraphStyle.PageBreakBefore` | See [Page breaks](#page-breaks). |
| A `<style>` type-selector rule, `p { ... }` | The base beneath an element's own `style` attribute | See [Stylesheets](#stylesheets). |
| `<body bgcolor>`, CSS `background-color` and `background` on `<html>` or `<body>` | `RichTextDocument.PageBackground` | The canvas colour, CSS 2.1 §14.2: the root element's background, or the body's when the root states none. The property wins over the `bgcolor` attribute, which HTML maps beneath any author style. A transparent background is none. It is the page's, not the runs': on them it would highlight every line and leave the margins white. |
| CSS `@page` `size` and margins | `PageGeometry` | The one rule read out of a `<style>` element rather than off an element. `size` takes a CSS page name (`a3`-`a5`, `b4`, `b5`, JIS `b4`/`b5`, `letter`, `legal`, `ledger`), one length, or two, with `portrait`/`landscape` orienting it; margins take the shorthand in all four arities and the four longhands. Only the first unnamed rule is read - a `@page cover` states the page for part of a document and this model holds one. Margin boxes are stepped over. A rule leaving no column to write in is refused. |

### Text

Text and attributes are HTML-decoded once by the DOM parser and not again, so
literal entity spellings survive read/write round trips and link targets and
font names keep their authored values. Stylesheet text is raw CSS and is not
entity-decoded.

Normal HTML whitespace collapses to single spaces; `<pre>`, and any element
declaring `white-space: pre`, `pre-wrap` or `break-spaces`, preserves it. What
collapses is the five characters CSS names — space, tab, line feed, carriage
return, form feed — and a non-breaking space is deliberately not one of them,
so `&nbsp;` beside an ordinary space keeps both. Under a preserving
declaration nothing is trimmed either, including at the start of the
paragraph.

A NUL is not text. The tree builder ignores one in body content, so a
conforming DOM never holds it there; the parser this codec uses keeps it, so
the reader drops it rather than draw a missing glyph mid-word. Where the
standard turns a NUL into U+FFFD instead - `&#0;`, and inside a `textarea` -
it is dropped as well, since the reader cannot tell the two cases apart. Other
control characters are left alone: CSS asks for them to be drawn visibly.

Elements whose content is not the document's text are skipped with everything
inside them: those the HTML rendering rules hide (`area`, `base`, `basefont`,
`datalist`, `head`, `link`, `meta`, `noembed`, `noframes`, `param`, `script`,
`style`, `template`, `title`), and external or embedded content this reader
neither fetches nor draws, of which an image, an object, an embed or an iframe
is reported as `html.skip.external`. The parser reads `noframes` and `noembed`
as raw text, so without the skip their markup would come out as prose, tags
and all. `rp` is on the rendering list but kept on purpose: it holds the
parentheses shown around a ruby annotation by a reader with no ruby support,
which this model is.

### CSS values

Numeric CSS lengths and line-height multipliers must be finite. NaN, infinity,
numeric overflow and overflowing unit conversions are ignored, keeping the
inherited model value (`HtmlFiniteLengthTests` covers font sizes, paragraph
margins and line height).

Declaration semicolons and font-family list commas are recognised outside
quoted strings, so a quoted family such as `'Alpha;Beta'` or `'Alpha,Beta'`
stays one name, in inline styles and supported type rules alike. Quoted family
names lose only their matching outer quote pair and decode escaped
quotes/backslashes, including those the writer emits. General CSS hexadecimal
escape decoding is outside this subset.

### Stylesheets

A `<style>` element's text is never prose; the element is read separately, for
its `@page` rule and its type-selector rules and nothing else. A rule whose
selector is a bare element name, or a selector list every item of which is
one, contributes its declarations to those elements. The element's own inline
`style` attribute wins property by property: a property it does not state
still arrives, and one both state takes the inline value. Two type rules
stating one property for one element resolve by source order, the later
winning, as CSS says for equal specificity. Whatever the reader does with a
declaration it does with one resolved from a type rule, the page break
included. Comments are stripped first, including the `<!--` / `-->` pair a word
processor wraps a stylesheet in, and a nested block is removed from a rule body
before the declarations are split.

**Exactly one selector is matched: the bare element name.** A
`p { line-height: 115% }` rule reaches the paragraphs it selects, and so does
`h1, h2 { ... }`. **Nothing else is matched** - not a class, an id, an
attribute, a pseudo-class, a combinator, a descendant selector, or `*`; and
there is no `!important`, no `@media`, no external stylesheet, no computed
style, and no layout. A selector list is all or nothing: `h1, .lead { ... }`
applies to neither, not to the `h1` alone, because two elements an author styled together coming back
one styled and one not, with nothing to say which, is worse than not applying
the rule. A skipped rule - an unimplemented selector, or any at-rule other than
`@page` - is reported once per read as `html.css.rule`: it was read, understood
to be formatting aimed at this document, and dropped, a loss the codec can see
and so has to name.

A type rule contributes to the element its selector names and nothing else; it
is not a computed-style inheritance pass. What follows is the reader's
existing descent, which carries the model style fields it understands from a
container into the blocks inside it, exactly as for a `style` attribute on
that container: `td { font-size: 8pt }` makes a cell's text small, and
`div { margin-bottom: 12pt }` reaches the paragraphs in the `div` as
`<div style="margin-bottom:12pt">` does.

**Why the line sits there: everything past it is the cascade, and the cascade
is a browser.** Specificity here is one comparison with one answer - the
element's own `style` attribute beats the sheet - applicable without knowing
the rest of the document. A class rule, an id rule and a descendant rule aimed
at one paragraph must be ordered against each other before any applies, and
building that is not smaller than building the engine. This is a document
reader wanting the formatting a producer stated, and in the LibreOffice sample
that prompted it every selector was `@page` or `p`. `@page` is the one at-rule
read because a page is a property of the document rather than its
presentation: HTML keeps there what DOCX keeps in `w:sectPr` and ODF in
`style:page-layout`.

**The measurable gap.** For a document with a table LibreOffice also writes
`td p { ... }` and `th p { ... }`, descendant selectors, so paragraphs in cells
get none of it. In the office conformance suite the seeds without tables score
as their DOCX twins do (`plain-paragraphs` at an ink box of 9 pixels against
the twin's 9), while `table-simple` sat at 107 against 11 - partly this, and
partly table flattening, measured when the reader still flattened tables (it
now keeps basic grids, see [issue 143](office-issue-143.md); the writer still
flattens). It is the case to weigh first if the line ever moves.

An external stylesheet is not fetched, so neither a `@page` rule nor a type
rule in one is read: the reader never makes a network request.

### Page breaks

`page-break-before` and `break-before` are read from the declarations resolved
for the paragraph element - its own inline `style` attribute over a bare type
rule - so `p { page-break-before: always }` reaches the paragraphs it names,
while a class rule a word processor writes for the paragraphs it wants broken
selects nothing. Both spellings are current: `page-break-before` is the CSS2
property and what LibreOffice writes; `break-before` is the CSS3 replacement
the older one is defined as an alias for.

- `always` and `page` are breaks; `auto` (the initial value), `avoid` and
  `region` (a fragmentation into containers this model lacks) are not.
- `break-before: column`, and the older `column-break-before: always`, are a
  column break, `ParagraphStyle.ColumnBreakBefore`, unless the paragraph also
  states a page break, which goes further and is the one kept.
- `left`, `right`, `recto` and `verso` are read as a plain break and reported
  as `html.page-break`: the model holds a flag and has no page parity.
- When the two properties disagree the break wins: CSS settles it by source
  order, and this codec's declaration parser returns a dictionary, not an order.
- A break on a containing `<div>` is not inherited by the paragraphs inside it
  (alignment descends; a break happens once, where stated), and container break
  declarations are not transferred to direct text.

## Writer Mapping

A paragraph whose text would not survive HTML's collapsing - a tab, a repeated
space, or a space at either edge - carries `white-space: pre-wrap`. Only such a
paragraph carries it, since the declaration changes how a browser lays the
paragraph out.

| Model | HTML |
|---|---|
| Document | `<!DOCTYPE html><html><head><meta charset="utf-8"></head><body>...` |
| `PageGeometry` | `<style type="text/css">@page { size: Wpt Hpt; margin: T R B L; }</style>` in the head, and nothing when the document states no page. Points, because the model is in points, so a round trip converts nothing; four margin values rather than the shortest shorthand, so a diff of two exports shows a margin change where the margin changed. |
| `RichTextDocument.PageBackground` | `background-color` on `<body>`. A gradient is written as its middle colour, as LibreOffice writes one, and reported as `html.background.gradient`. |
| Paragraphs | `<p>` elements |
| Soft breaks | `<br>` |
| Inline style fields | CSS on `<span>` or `<a>` |
| `LinkHref` | `<a href="...">` |
| `InlineStyle.Image` | `<img src="data:...;base64,...">` with `alt` and a CSS size |
| `InlineStyle.Revision` | Inserted text in `<ins>` and deleted text in `<del>`, with a `datetime` where the change states one. A document that hides its changes is written as it is drawn, its deletions left out. A change of formatting, and a deleted paragraph break, have no element and are not written. |
| `InlineStyle.Note` | The note's label in `<sup>`, and the note's paragraphs after the body, under an `<hr>`, the first opened with the label. HTML has no notes; reported once as `html.note`. The reader reads back notes written by this codec as well as semantic footnote containers into `DocumentNote` and `InlineStyle.Note`. |
| Paragraph alignment, line spacing, indent, spacing | CSS on `<p>`. A stated line height is written as a `line-height` length, which reads back as the least a line is; an exact one is reported once as `html.lineheight.exact`. |
| `ParagraphStyle.ColumnBreakBefore` | `break-before: column`, where the paragraph starts no page. There is no CSS2 spelling to prefer for it. |
| `ParagraphStyle.PageBreakBefore` | `page-break-before: always`, in the paragraph's own `style` attribute beside its other declarations. The CSS2 property rather than the CSS3 `break-before: page`, because every browser and word processor has read it for twenty years - CSS Fragmentation keeps it as an alias for that reason. Not both: that states one break twice, and a consumer resolving them in the wrong order would be entitled to honour the second. |

- Text columns, the page's or a section's, and a section's own margins are not
  written and are reported as `html.columns`: a browser balances CSS columns
  over the whole body rather than filling a page at a time, which is another
  layout than the document's.
- `ListKind` is not written; the writer preserves exact left indentation and
  reports `html.list`.
- An embedded image is written inline as a base64 data URI
  (`html.image.datauri`), keeping an exported page a single file; the reader
  decodes it back into an `InlineImage` when the caller's resource policy
  permits, so the image round-trips.
- Tables are written as cell paragraphs with `html.table.flattened`; their grid
  does not survive an HTML round trip.
- Embedded objects, scripts, stylesheets, forms, and metadata are not
  serialized from the model.
- Hidden body-section visibility is not written; the retained content is
  exported as visible paragraphs, with `html.section.hidden`. ODT retains the
  flag; the shared raster paginator omits hidden body sections.
- Writer output is semantic model HTML, not a preservation of source markup.

## Security And Limits

- The reader never fetches external resources.
- `script`, `style`, `iframe`, `object`, `embed`, SVG/canvas, form inputs,
  metadata, and templates are skipped as non-model content.
- External or embedded content skips produce `html.skip.external` once per read.
- Skipped style rules produce `html.css.rule` once per read.
- Links are inert model metadata; unsafe schemes are dropped with `html.link`.
- `DocumentLimits.MaxDocumentBytes`, `MaxRunLength`, and `MaxParagraphCount` are
  enforced with diagnostics.

## Known Limitations

- CSS support is declaration-level and intentionally small: bare type selectors
  and `@page` only ([Stylesheets](#stylesheets)).
- Table writing flattens cell content; reading retains basic grids.
- Relative links are dropped ([Reader Mapping](#reader-mapping)).
- Markdown and DOCX are peer codecs, documented separately; this HTML codec
  does not delegate to them.

## Standards and rights

The rights position (two independent royalty-free patent policies), the approved
labels and the provenance findings are in [the HTML register](../ip/html.md).
This codec does not tokenize or parse HTML: `Broiler.Dom.Html` does, and that
repository keeps its own register. A claim about Broiler's HTML support as a
whole needs both.
