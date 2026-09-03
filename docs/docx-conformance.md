# DOCX Conformance

`Broiler.Documents.Docx` reads and writes a dependency-free DOCX subset using
Open XML WordprocessingML package parts.

## Supported Read/Write Subset

- Main document package discovery through `_rels/.rels` with fallback to
  `word/document.xml`.
- Paragraphs, empty paragraphs, tabs, and soft line breaks.
- Block-level containers, walked recursively so their paragraphs are read rather
  than dropped: tables (`w:tbl`/`w:tr`/`w:tc`, including nested tables),
  structured document tags (`w:sdt`), accepted revisions (`w:ins`, `w:moveTo`),
  `w:customXml`/`w:smartTag` wrappers, and `mc:AlternateContent` (first
  `mc:Choice`, else `mc:Fallback`). A container's paragraphs are read in
  document order, which for a table is row-major.
- Tables, as a `DocumentTable` over the paragraphs of their cells: the
  `w:tblGrid` column widths, `w:gridSpan` and `w:vMerge` spans, per-cell and
  per-table borders (`w:tcBorders`, `w:tblBorders`, including the `insideH` and
  `insideV` edges), cell shading (`w:shd`), the `w:tblCellMar` left margin as the
  cell padding, header rows (`w:tblHeader`), the height a row states
  (`w:trHeight`), and tables nested in a cell. The
  cells hold no text of their own: a cell names a range of the document's
  paragraphs, so a caret, a selection, a style, and every codec's text handling
  go on working through one flat list. Written back as `w:tbl`.
- Direct inline formatting: bold, italic, underline, strikethrough, font
  family, font size, foreground color, and background shading.
- Capitalization: `w:caps` and `w:smallCaps`, read and written as a style
  attribute. The text keeps the casing the author typed and the capitals are
  applied when drawing, so an open-and-save does not rewrite it. An explicit
  `w:val="0"` clears only the kind it names; a run declaring both draws all caps,
  as Word does.
- Named styles from `word/styles.xml`, resolved per ECMA-376 §17.7.2: document
  defaults (`w:docDefaults`), then the `w:basedOn` chain from its root down to
  the style named by `w:pStyle` (paragraphs) or `w:rStyle` (runs), then direct
  formatting. The default style (`w:default="1"`) applies only to content that
  names no style of its own. Template documents carry nearly all of their
  formatting here rather than inline.
- Theme fonts from `word/theme/theme1.xml`: `w:rFonts` theme references such as
  `w:asciiTheme="majorHAnsi"` resolve to the theme's major/minor latin typeface.
  An explicit font name on the same element wins.
- Paragraph formatting: left/center/right alignment, line spacing, spacing
  before/after, indentation, bullet lists, and numbered lists.
- External hyperlinks for `http`, `https`, and `mailto`, plus internal anchor
  links.
- Embedded pictures, read and written as a single object replacement character
  (`U+FFFC`) whose run carries the image. Both shapes Word writes are read:
  DrawingML (`w:drawing`, inline and anchored, including a picture inside
  `mc:AlternateContent`) and the legacy VML shape (`w:pict`/`v:imagedata`). The
  display size comes from `wp:extent` in EMUs, or from the VML shape's CSS
  `width`/`height`; alternative text comes from `wp:docPr@descr`. A write stores
  each distinct image once under `word/media`, with its relationship and a
  content-type default for its extension. Raster formats only: PNG, JPEG, GIF,
  BMP, TIFF, WebP, and ICO.
- A header's and a footer's shapes, read onto the running content rather than
  into the body. They are placed against the page: `svg`-style horizontal offsets
  stay measured from the text column, while the vertical one is measured from the
  top of the page, because running content repeats and has no paragraph of the
  body to hang from. Every vertical `relativeFrom` therefore converts for these —
  `page` and `topMargin` from the page's top edge, `margin` and `bottomMargin`
  through the geometry — and they are written back into the header part with
  `positionV relativeFrom="page"`. A part that holds only a stripe and no words
  is still a part. They were anchored to a body paragraph before, which put a
  letterhead on page one only and dropped it entirely when the body was shorter
  than its header.
- Floating pictures: an anchored (`wp:anchor`) picture is read as a floating
  shape carrying the image, placed at the `posOffset` of its `wp:positionH` and
  `wp:positionV`, converted from whichever frame the anchor names into the text
  column and its paragraph — the same box a `wps:wsp` shape gets, because it is
  the same anchor. Horizontally every frame converts exactly: a stripe stated at
  nothing from the left margin and one stated at minus the margin from the
  column are the same stripe, and only the second used to arrive in the right
  place. Its `behindDoc` is read
  and written back, so a stripe the letter is written on top of and a stamp
  meant to cover it each stay on the side of the text they were authored on.
  The picture is written back as an anchored picture. A logo hung over a
  letterhead therefore stays over it rather than being pushed into the first
  line, which moved the whole letter down by the height of the picture.

## Intentional Limits

- Tracked deletions, embedded objects, fields, comments, headers, footers,
  footnotes, and section layout are skipped or approximated with diagnostics
  where applicable.
- A floating picture keeps its position, its layer and its wrapping. `behindDoc`
  is the whole of the stacking that is represented — order *among* shapes on the
  same side of the text is not, and they draw in the order they were read.
  `wrapSquare`, `wrapTight` and `wrapThrough` all wrap around the shape's box:
  the outline the last two follow is not, so text clears the frame rather than
  the picture inside it. A wrapped line keeps one span — it runs down whichever
  side has more room, which is `wrapText="largest"` — so `bothSides` gets the
  larger side rather than text down both. `distL` and `distR` become one
  clearance, applied on both sides. A shape anchored to a later paragraph does
  not narrow a line above it, and an indented paragraph's band is measured from
  its own left edge rather than the column's, so a shape overlapping one is out
  by the indent. A picture whose anchor states no
  `wp:extent` has no box to float at and stays in the text. Rotation and picture
  effects are dropped.
- A picture's source crop (`a:srcRect`) and its `a:prstGeom` are read, and the
  ellipse is the only preset represented. That is a deliberate choice rather than
  a stopping point: the round portrait is what every CV and letterhead template
  puts its photograph in, and drawn square it is the first thing anyone notices.
  Every other preset draws as the rectangle it is inscribed in, which is what
  this codec did for all of them before. A crop is stated as fractions of the
  source, so it survives a resize; crops that meet or cross leave no rectangle to
  draw and are ignored rather than losing the picture. The crop is applied before
  the mask, so the ellipse is inscribed in the part actually drawn.
- A vertical `relativeFrom` other than `paragraph` or `line` is not converted.
  Those frames measure from the page, and where a paragraph sits on a page is a
  layout result the reader does not have — a shape on the fortieth paragraph
  could land on any page. The offset is kept as stated and measured from the
  anchoring paragraph instead, with `docx.anchor.relativefrom`. `insideMargin`
  and `outsideMargin` name a side that depends on the page's parity, so
  horizontally they are read as an odd page's, reported with the same code.
- EMF/WMF metafiles — what Word embeds for charts, SmartArt, and shape fallbacks
  — are not carried, because they cannot be decoded to pixels here. They are
  reported as `docx.image.format` rather than kept as a picture that would draw
  as nothing.
- An image linked with `r:link` rather than embedded is not fetched; reading it
  would mean a network or file request driven by document content (ADR 0004).
- Style resolution covers the attributes `RichTextDocument` models. Style
  features outside it — character spacing/scaling, table styles, numbering-level
  overrides, conditional table formatting — are ignored, as are theme colors
  (`w:themeColor`); Word writes the computed RGB into `w:val` alongside them,
  which is what the reader uses.
- Table styles are not applied. A `w:tblStyle` names formatting held in the
  styles part — banding, conditional first-row and first-column formatting, and
  the borders a style states — and only what the table and its cells state
  directly is read, with `docx.table.style`. Cell vertical alignment, text direction, and
  table indent and alignment are not represented either; a table starts at the
  left margin.
- A table's cells are paragraph ranges, so an edit that adds or removes
  paragraphs moves them and one that spans out of a cell into the body does not
  keep the grid over what it merged. The ranges are moved through the one place
  paragraph counts change, so typing in a cell, splitting its paragraph, and
  deleting inside it all keep the table around the text.
- A row's `w:trHeight` is a **minimum** and never a ceiling: the row is at least
  that tall and grows for content that does not fit. An `w:hRule="exact"` is
  therefore read as a floor too, with `docx.table.rowheight`, because a row that
  clipped its own text would lose content the document has. An explicit
  `w:hRule="auto"` asks for nothing and gets nothing. A height carrying **no**
  rule is read as a minimum as well, which is a deliberate departure from the
  specification's stated default of `auto`: no producer means `auto` by omission,
  Word renders a bare height as a floor, and the page-layout tables that CV and
  letterhead templates are built from state a tall empty row precisely to place
  the block beneath it. Read as `auto` those rows collapse and the layout falls
  in on itself.
- Block nesting deeper than `DocumentLimits.MaxGroupDepth` is abandoned with a
  `docx.limit.depth` diagnostic.
- DOCX packages above `DocumentLimits.MaxDocumentBytes` are not parsed.
- XML parts above `DocumentLimits.MaxBinBytes` are skipped.
- Color alpha is not represented by DOCX; RGB channels are written with a
  diagnostic.

## Read Diagnostics

Every read ends with a `docx.read.summary` info diagnostic carrying the
paragraph, table, style, image, and skipped-block counts. It exists so
a document that opens blank can be told apart from a document that *is* blank:

| Code | Severity | Meaning |
| --- | --- | --- |
| `docx.read.summary` | Info | Paragraph, table, style, image, and skipped-block counts for the read. |
| `docx.document.empty` | Warning | The body held block-level content but produced no paragraphs — a reader gap, not an empty file. |
| `docx.table.style` | Warning | A table named a table style; banding and conditional formatting are not applied. |
| `docx.table.rowheight` | Warning | A row stated an exact height; it was applied as a minimum so its text is not clipped. |
| `docx.block.unsupported` | Warning | A block-level element was not understood; the message names the element. Reported once per distinct name. |
| `docx.limit.depth` | Warning | Block nesting hit `MaxGroupDepth`; the deepest content was skipped. |
| `docx.styles.missing` | Warning | Content named styles but the package has no styles part. Reported once. |
| `docx.styles.unknown` | Warning | A `w:pStyle`/`w:rStyle` named a style the table does not define. Once per id. |
| `docx.styles.cycle` | Warning | A `w:basedOn` chain was cyclic and was cut short. |
| `docx.styles.depth` | Warning | A `w:basedOn` chain exceeded `MaxGroupDepth` and was cut short. |
| `docx.part.headerfooter` | Info | The package has headers or footers, which are not part of the body. |
| `docx.image.missing` | Warning | A picture referenced a media part the package does not contain. |
| `docx.image.relationship` | Warning | A picture named a relationship the package does not define. |
| `docx.image.external` | Warning | A picture linked to an image outside the package, which is not fetched. |
| `docx.image.format` | Warning | A picture used an image format this codec does not carry (EMF/WMF and the like). |
| `docx.image.limit` | Warning | An image part exceeded `MaxBinBytes` and was skipped. |
| `docx.image.shape` | Warning | A drawing held no embedded picture. |
| `docx.image.anchored` | Warning | A floating picture was anchored to its paragraph. |
| `docx.anchor.relativefrom` | Warning | A frame an anchor stated its offset against was approximated: a mirrored margin read as an odd page's, or a page-relative vertical offset kept as stated. |

`Broiler.Cli --convert-doc <in> --output <out>` prints all of them, which is the
quickest way to see what a problem document lost. In the Writer, set
`BROILER_WRITER_DOCUMENT_LOG=1` to have the same list written to stderr on every
open; the status bar always reports a read that produced no text.

## Probe Policy

DOCX probing is conservative because DOCX is a ZIP-based OPC package:

- ZIP signature plus DOCX filename/MIME hint is high confidence.
- A visible `word/document.xml` local ZIP entry is high confidence.
- Generic ZIP files are not claimed without a DOCX hint or WordprocessingML
  package evidence.

## Standards And Rights

DOCX is ECMA-376, republished as ISO/IEC 29500. The **Microsoft Open Specification
Promise** names `Office Open XML 1.0 - Ecma-376`, all three ISO/IEC 29500 editions,
and `[MS-DOCX]` individually in its covered list: an irrevocable non-assertion of
Microsoft Necessary Claims against a conforming implementation, with defensive
termination as its only condition.

The promise states three limits about itself and this document repeats none of
them loosely: it reaches Microsoft-owned or Microsoft-controlled claims only, it
reaches the required portions **described in detail and not merely referenced**,
and it is expressly not an assurance that an implementation avoids third-party
rights. All of that is recorded in
[the DOCX IP and licensing register](docx-ip-licensing-register.md), where **every
row is now decided**. Decided is not cleared: patent-freedom is not claimed, no
freedom-to-operate determination has been made, and the promise expressly does not
assure that an implementation avoids third-party rights.

**The naming rule is decided and applied.** No format label may name a vendor or
its product, so `.docx` is offered as `DOCX Document (*.docx)` rather than as
`Word Document` — the phrase this component's hosts used until the rule was taken.
A guard in the aggregate repository fails the build on a label that names one.

Settled by inspection: this codec embeds no third-party OOXML code, takes no
package reference, reproduces no specification text, and no `.docx` or `.dotx`
file is committed anywhere in the repository.
