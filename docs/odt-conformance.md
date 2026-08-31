# ODT Conformance

`Broiler.Documents.Odt` reads and writes a dependency-free ODT subset over OASIS
OpenDocument text packages (ODF 1.0 through 1.3, published as ISO/IEC 26300). It
uses nothing but `System.IO.Compression` and `System.Xml.Linq`; there is no ODF
toolkit behind it.

## Supported Read/Write Subset

- Package layout: the `mimetype` entry, `META-INF/manifest.xml`, `content.xml`,
  and `styles.xml`. Part paths are the normative ones, so nothing has to be
  resolved through relationships the way an OPC package does.
- Paragraphs (`text:p`) and headings (`text:h`). A heading is read as an ordinary
  paragraph carrying whatever its named style resolves to; the model has no
  heading rank.
- Block-level containers, walked recursively so their paragraphs are read rather
  than dropped: lists (`text:list`, including nested lists and `text:list-header`),
  tables (`table:table`, including the header-row and row-group wrappers),
  sections (`text:section`), the `text:index-body` of a generated index or table
  of contents, and the body of a `draw:text-box` frame. Tables are flattened into
  their cell paragraphs in row-major order with an `odt.table.flattened`
  diagnostic — `RichTextDocument` has no table shape, and a layout table is how
  a CV or letterhead template holds its entire text.
- White-space processing per ODF 1.3 part 3 §3.17: a run of white space in a text
  node is one space, white space at either edge of a paragraph is nothing, and
  significant spaces arrive as `text:s`. The writer applies the same rule in
  reverse, so a leading, trailing, or repeated space is written as `text:s` and
  survives a round trip.
- Inline constructs: `text:span`, `text:a`, `text:s`, `text:tab`,
  `text:line-break`, and `text:ruby` (the `text:ruby-base` text). Fields and
  marks — `text:date`, `text:page-number`, `text:bookmark-ref`, and the rest —
  contribute the value they last computed, which is the text a reader can plainly
  see on the page.
- Styles from `styles.xml` and `content.xml`, resolved per ODF 1.3 §16.2: the
  family's `style:default-style`, then the `style:parent-style-name` chain from
  its root down to the style the content names, then the next more specific
  style. Automatic styles and named styles resolve through the same chain, and a
  content automatic style wins a name collision with a named style. ODF puts
  almost nothing inline, so this table is not an optimization: without it every
  ODF document reads as undifferentiated body text.
- Character formatting: `fo:font-weight` (including the numeric CSS form),
  `fo:font-style`, `style:text-underline-style`/`-type`,
  `style:text-line-through-style`/`-type`, `style:font-name` resolved through
  `office:font-face-decls`, `fo:font-family` (CSS syntax, quoted names and
  fallback lists), `fo:font-size` (absolute lengths and percentages of the
  inherited size), `fo:color`, and `fo:background-color`.
- Capitalization: `fo:text-transform="uppercase"` and
  `fo:font-variant="small-caps"`, read and written as a style attribute. The text
  keeps the casing the author typed and the capitals are applied when drawing, so
  an open-and-save does not rewrite it. A style declaring both draws all capitals.
- Paragraph formatting: `fo:text-align` (`start`/`left`/`center`/`end`/`right`),
  `fo:line-height` as a percentage, `fo:margin-top`, `fo:margin-bottom`,
  `fo:margin-left`, and the single-value `fo:margin` shorthand. Lengths are read
  in `pt`, `in`, `cm`, `mm`, `pc`, and `px`.
- Lists: the kind comes from the `text:list-style` the list names —
  `text:list-level-style-number` with a `style:num-format` is numbered,
  `text:list-level-style-bullet` and `text:list-level-style-image` are bulleted —
  and the level is the nesting depth. A written document groups each run of
  same-kind list paragraphs into one `text:list`, nesting deeper levels inside the
  `text:list-item` they belong to, so numbering does not restart at every item.
- External hyperlinks for `http`, `https`, and `mailto`, plus internal anchor
  links, under the same URI policy the other codecs use.
- Embedded pictures, read and written as a single object replacement character
  (`U+FFFC`) whose run carries the image. A `draw:frame` holding a `draw:image`
  is read whether the picture is a package entry named by `xlink:href` or an
  inline `office:binary-data` payload, and whether or not a `draw:a` wraps it.
  Display size comes from `svg:width`/`svg:height`, alternative text from
  `svg:title` (falling back to `svg:desc`). A write stores each distinct image
  once under `Pictures`, declares it in the manifest, and anchors the frame
  `as-char`. Raster formats only: PNG, JPEG, GIF, BMP, TIFF, WebP, and ICO.
- Floating pictures: a frame anchored to anything other than a character is read
  as a floating shape carrying the image, boxed by its `svg:x`/`svg:y` against
  the text column and its paragraph and its `svg:width`/`svg:height`, and keeping
  the fill and outline of its graphic style. It is written back as a
  paragraph-anchored `draw:frame` at the same box. A frame standing between
  paragraphs is read this way too, where before it was skipped as holding no body
  text — which lost the picture.
- A written package is deterministic: two writes of one document produce
  byte-identical output. Every entry carries a fixed timestamp, `mimetype` is
  first and stored uncompressed per ODF 1.3 part 2 §3.3, and `meta.xml` names the
  generator and nothing else — a creation date would be the one field that made
  two writes differ.

## Intentional Limits

- **Encrypted packages are rejected.** ODF password protection really encrypts
  the parts; there is nothing to read, and this codec asks for no key.
- Tracked changes are not applied. The document is read as it stands, and
  `text:tracked-changes` — where the deleted text lives — is skipped with a
  diagnostic.
- Comments (`office:annotation`), footnotes and endnotes (`text:note`), headers,
  footers, and page geometry are not part of the body and are not imported.
- Table structure (columns, spans, borders, cell shading) is not represented;
  only the cell text survives flattening.
- A floating picture keeps its position, not its wrapping: text does not flow
  around it, and z-order is not represented — a shape draws under the text. A
  page-anchored frame is placed against its paragraph, since that is the only
  anchor the model has, and a frame that states no box stays in the text. Crops,
  rotation, and frame effects are dropped.
- SVG, EMF, and WMF — what a producer stores for charts, formulas, and object
  replacement images — are not carried, because they cannot be decoded to pixels
  here. They are reported as `odt.image.format` rather than kept as a picture
  that would draw as nothing.
- An image the document links to rather than embeds is not fetched; reading it
  would mean a network or file request driven by document content (ADR 0004).
- Justified paragraphs are read as left-aligned with an `odt.align.justify`
  diagnostic: the model has three alignments and justification is not one of
  them. `style:writing-mode` is not consulted, so `start`/`end` are resolved as
  left-to-right.
- A fixed line height (`fo:line-height` as a length, `style:line-height-at-least`,
  `style:line-spacing`) is reported rather than guessed at: the model stores a
  multiplier of the font size, not a measurement.
- `fo:text-transform` values of `lowercase` and `capitalize` are reported and
  dropped; the model draws upper case only.
- Style resolution covers the attributes `RichTextDocument` models. Everything
  outside it — character spacing and scaling, text position, borders, tab stops,
  drop caps, table and page styles, list label geometry — is ignored.
- Indentation is one level per quarter inch, matching the DOCX codec. A list
  paragraph takes its indent from the list nesting rather than from
  `fo:margin-left`, so a list paragraph written at indent level 0 reads back at
  level 1.
- Block nesting deeper than `DocumentLimits.MaxGroupDepth` is abandoned with an
  `odt.limit.depth` diagnostic.
- ODT packages above `DocumentLimits.MaxDocumentBytes` are not parsed. XML parts
  and pictures above `DocumentLimits.MaxBinBytes` are skipped, with the limit
  applied to what actually decompresses rather than to the size the ZIP header
  claims.
- Inline DTDs are prohibited when parsing any part, so entity expansion is not an
  attack surface.
- Color alpha is not represented by ODF; RGB channels are written with a
  diagnostic. Control characters XML cannot hold are dropped with a diagnostic.
- The flat single-file variants (`.fodt`) and the other OpenDocument document
  types (`.ods`, `.odp`, `.odg`) are out of scope. A text *template* (`.ott`)
  probes as ODT and reads correctly, because its body is the same, but the
  descriptor claims only `.odt`.

## Read Diagnostics

Every read ends with an `odt.read.summary` info diagnostic carrying the
paragraph, flattened-table, style, list-style, image, and skipped-block counts. It
exists so a document that opens blank can be told apart from a document that *is*
blank:

| Code | Severity | Meaning |
| --- | --- | --- |
| `odt.read.summary` | Info | Paragraph, table, style, list-style, image, and skipped-block counts for the read. |
| `odt.document.empty` | Warning | The body held block-level content but produced no paragraphs — a reader gap, not an empty file. |
| `odt.package.encrypted` | Error | The package is encrypted; nothing was read. |
| `odt.package.content` | Error | The package has no `content.xml`. |
| `odt.package.zip` | Error | The bytes are not a readable ZIP package. |
| `odt.document.body` | Error | `content.xml` has no `office:text` body. |
| `odt.limit.bytes` | Error | Input exceeded `MaxDocumentBytes` and was not parsed. |
| `odt.content.xml` / `odt.styles` / `odt.manifest` | Error | A part could not be parsed, or exceeded `MaxBinBytes` (`.limit` suffix). |
| `odt.table.flattened` | Warning | At least one table was flattened into its cell paragraphs. |
| `odt.block.unsupported` | Warning | A block-level element was not understood; the message names the element. Reported once per distinct name. |
| `odt.frame.textbox` | Warning | A text box was read as body content; its frame position is not represented. |
| `odt.frame.block` | Warning | A page-anchored frame held neither body text nor a picture and was skipped. |
| `odt.annotation` | Warning | Comment content is not part of the body and was skipped. |
| `odt.note` | Warning | Footnote and endnote bodies are not part of the paragraph and were skipped. |
| `odt.revision.tracked` | Warning | Tracked changes were not applied. |
| `odt.link` | Warning | A hyperlink with a disallowed or relative target was dropped. |
| `odt.align.justify` | Warning | A justified paragraph was read as left-aligned. |
| `odt.linespacing.fixed` | Warning | A fixed line height was not represented. |
| `odt.text.transform` | Warning | A `lowercase`/`capitalize` transform was dropped. |
| `odt.styles.unknown` | Warning | A style reference named an undefined style. Once per family and name. |
| `odt.styles.cycle` | Warning | A `style:parent-style-name` chain was cyclic and was cut short. |
| `odt.styles.depth` | Warning | A `style:parent-style-name` chain exceeded `MaxGroupDepth` and was cut short. |
| `odt.styles.list-unknown` | Warning | A `text:list` named an undefined list style; it was read as a bullet list. |
| `odt.image.missing` | Warning | A picture referenced a package entry that is not there. |
| `odt.image.external` | Warning | A picture linked to an image outside the package, which is not fetched. |
| `odt.image.format` | Warning | A picture used an image format this codec does not carry (SVG, EMF, WMF and the like). |
| `odt.image.limit` | Warning | A picture exceeded `MaxBinBytes` and was skipped. |
| `odt.image.binary` | Warning | An inline picture payload was not valid base64. |
| `odt.image.shape` | Warning | A frame held no embedded picture. |
| `odt.image.anchored` | Warning | A floating picture was anchored to its paragraph; wrapping and z-order are not represented. |
| `odt.limit.depth` | Warning | Block or inline nesting hit `MaxGroupDepth`; the deepest content was skipped. |
| `odt.limit.run` | Warning | A paragraph hit `MaxRunLength` and was truncated. |
| `odt.limit.spaces` | Warning | A `text:s` count exceeded `MaxRunLength` and was clamped. |
| `odt.limit.paragraphs` | Warning | Input exceeded `MaxParagraphCount`; remaining paragraphs were dropped. |

Write diagnostics are `odt.link`, `odt.image.placeholder`, `odt.image.size`,
`odt.color.alpha`, and `odt.text.control`.

`broilerdoc info <file>` prints all of them, which is the quickest way to see
what a problem document lost.

## Probe Policy

ODF makes probing unusually cheap, because the format was designed to be
identifiable from its first bytes:

- A leading `mimetype` entry — first in the archive, stored uncompressed, per
  ODF 1.3 part 2 §3.3 — holding the OpenDocument text media type is **certain**
  confidence, with no filename or MIME hint needed at all.
- The text *template* media type is high confidence: a template holds the same
  body a document does.
- Any other OpenDocument media type is **not** claimed, even with an `.odt`
  filename hint. The package says what it is.
- A ZIP holding both `content.xml` and `META-INF/manifest.xml` but no `mimetype`
  entry is high confidence.
- A ZIP with an `.odt` filename or MIME hint and nothing else is high confidence;
  a generic ZIP with no hint is not claimed.
- Non-ZIP bytes with an `.odt` hint are low confidence.

## Standards And Rights

ODF is an OASIS standard, republished as ISO/IEC 26300, and the specification is
freely available. The OASIS ODF technical committee works under a royalty-free
IPR mode, and the principal contributors published patent covenants covering
conforming implementations. That is a materially better starting position than a
format governed by a vendor licence, and it is why an ODF codec needs no licence
negotiation to be written.

It is **not** a statement that the format, or this implementation, is free of
third-party patent claims. No one can establish that from a specification, and
this component does not attempt to. The controls that exist for exactly this
question — the IP and licensing register, and the rule that no feature-matrix
entry reaches `Supported` while its rights row is pending — are the PDF ones
today ([ADR 0011](adr/0011-pdf-standards-ip-provenance-and-claims.md),
[the PDF IP and licensing register](pdf-ip-licensing-register.md)). Extending
them to cover ODT, and getting a legal reading on the covenants above, is
[roadmap](roadmap.md) work, not something this document settles.

What is settled here is provenance: every line of this codec was written against
the published specification for this repository. It embeds no third-party ODF
code, takes no runtime dependency, and vendors no sample document — the test
packages are all constructed in code.
