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
  metadata, and templates are skipped as non-model content.
- External or embedded content skips produce `html.skip.external` once per read.
- Links are inert model metadata; unsafe schemes are dropped with `html.link`.
- `DocumentLimits.MaxDocumentBytes`, `MaxRunLength`, and `MaxParagraphCount` are
  enforced with diagnostics.

## Known Limitations

- CSS support is declaration-level and intentionally small; no cascade,
  selector matching, external stylesheets, computed style, or layout. The one
  exception is the `@page` rule, which is read out of a `<style>` element because
  the page is a property of the document rather than of its presentation - it is
  where HTML keeps what DOCX keeps in `w:sectPr` and ODF in `style:page-layout`.
  Nothing else in a stylesheet is applied, so a `p { line-height: 115% }` rule of
  the kind a word processor emits does not reach the paragraphs it selects; only
  an inline `style` attribute does.
- An external stylesheet is not fetched, so a `@page` rule in one is not read.
  The reader never makes a network request, and finding out how big a document's
  paper is should not be the thing that changes that.
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
