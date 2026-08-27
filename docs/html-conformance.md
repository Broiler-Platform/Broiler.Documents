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
| `<a href>` | `InlineStyle.LinkHref` | `http`, `https`, and `mailto` only; other schemes are dropped with `html.link`. |
| `<font face color>` | `FontFamily`, `Foreground` | Legacy compatibility only. |
| CSS `color`, `background-color` | `Foreground`, `Background` | Named colors, `#rgb`, `#rrggbb`, and `rgb(...)`. |
| CSS `font-family`, `font-size` | `FontFamily`, `FontSize` | Points are preserved; px converts using 96 DPI. |
| CSS `text-align` / `align` | `ParagraphStyle.Alignment` | `justify` degrades to left. |
| CSS `line-height` | `ParagraphStyle.LineSpacing` | Unitless/percent preferred; absolute lengths approximate. |
| CSS margins | spacing/indent fields | Left margin or padding converts to discrete indent levels. |
| `<ul>` / `<ol>` + `<li>` | `ListKind`, `IndentLevel` | Reader only; writer reports `html.list` for list kind. |

Text and attributes are HTML-decoded. Normal HTML whitespace collapses to single
spaces; `<pre>` preserves whitespace.

## Writer Mapping

| Model | HTML |
|---|---|
| Document | `<!DOCTYPE html><html><head><meta charset="utf-8"></head><body>...` |
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
  selector matching, external stylesheets, computed style, or layout.
- Tables are flattened through their text content and block boundaries.
- Relative links are currently dropped; only absolute `http`, `https`, and
  `mailto` links are retained.
- Writer output is semantic model HTML, not a preservation of source markup.
- Markdown and DOCX are implemented as peer codecs. Their format-specific
  coverage is documented separately; this HTML codec does not delegate to them.
