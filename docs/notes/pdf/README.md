# PDF Codec Notes

`Broiler.Documents.Pdf` reads ISO 32000-1 files into the logical
`RichTextDocument` model and writes new PDF 1.7 files from it. It is built only
from what this repository implements itself: no third-party runtime dependency,
and no bundled font, glyph list, metric file, ICC profile or codec asset. Every
other PDF technology is recognized, reported with its own stable diagnostic, and
reachable by composing a reviewed implementation into `PdfCodecServices`.

| Document | Answers |
|---|---|
| This file | Scope, what is implemented, and the design rules the code follows |
| [feature-matrix.md](feature-matrix.md) | What the codec may be *described* as doing. It is the authority for claims |
| [construct-inventory.md](construct-inventory.md) | Exactly which constructs are read, written, recognized without interpretation, or rejected |
| [extension-points.md](extension-points.md) | The base/extension split, each extension point, and how a technology is added |
| [ip/pdf.md](../../ip/pdf.md), [ip/pdf-approved-sources.md](../../ip/pdf-approved-sources.md) | Rights decisions and the sources each piece of code was written from |
| [roadmap.md § PDF](../../roadmap.md#pdf) | Everything still open, including the delivery gates |

Implementation is not a claim. No feature-matrix entry is `Supported`.

## Scope

### What the codec is

- `PdfDocumentCodec.Read`: best-effort logical extraction into `RichTextDocument`.
- `PdfDocumentCodec.Write`: deterministic pagination and PDF 1.7 export.
- Descriptor name `PDF`, MIME type `application/pdf`, extension `.pdf`.
- Explicit registration in a `DocumentCodecCatalog`. There is no global registry,
  module initializer, UI, DOM or platform dependency.

Import and export are separate capability tracks. PDF is fixed-layout, while the
model is normalized paragraphs and runs, so the codec promises neither
source-faithful round trips nor "preserve layout" conversion.

### V1 includes

- Feature-based reading of the declared ISO 32000-1:2008 / PDF 1.7 subset,
  including older PDF 1.x files that use those constructs.
- Recognition of PDF 2.x headers, Catalog `/Version` and `/Extensions`. These are
  inventory and diagnostics, never feature enablement, and not ISO 32000-2
  conformance. The one PDF 2.0 feature read is the revision 6 security handler,
  reviewed on its own under IP-002.
- Logical text, basic styling, links admitted by `PdfUriPolicy`, metadata, and
  safely placeable images.
- Reading encrypted documents (ADR 0015).
- New PDF 1.7 output for the standard-font subset.

### V1 excludes

- Native viewing or page rasterization; source-preserving editing; incremental
  saving; OCR.
- Encrypted output.
- JavaScript, Launch actions, attachments, rich media, external-resource fetching.
- AcroForm editing, signature validation, PDF/A, PDF/UA and accessibility claims.
- Tagged-PDF roles and tagged output, Type 3 glyph execution, PDF 2.0-only
  constructs beyond revision 6 encryption.
- JPEG 2000, JBIG2 and CCITT fax in the base build. They exist only as composed
  extensions.
- Four-component CMYK/YCCK JPEG conversion; arithmetic, lossless, hierarchical,
  JPEG-LS and JPEG XR decoding.
- Writer use of the managed JPEG encoder; HTML/CSS print-to-PDF.

### Semantic and safety boundaries

- **Import is logical extraction.** It is not a visibility-faithful renderer, a
  sanitizer, a redaction engine, an archival conversion or a safe-disclosure
  tool. Invisible text, clipping, optional content, artifacts, overlays, redaction
  annotations, attachments, metadata and incremental history are handled by the
  feature matrix and uncertainty diagnostics. A visual overlay never proves that
  content underneath was removed.
- **Actions and URIs are inert data.** A URI becomes `LinkHref` only after policy
  admission, and every writer revalidates it at the point of output. Parsing and
  validation perform no DNS, file, shell or network access.
- **Writer output is untagged.** No `/Alt`, no structure tree, no PDF/UA or
  accessibility claim. Alt text in the model stays available for other formats.
- **Parsing grants nothing.** Projection, decode, extraction, byte transfer,
  transformation, embedding and redistribution are distinct operations, each
  decided by the caller's resource policy
  ([architecture notes](../architecture.md#resources-and-metadata)).

## What is implemented

### Syntax and cross-references

- Bounded tokenization of all eight object types, indirect references and
  streams, with checked offset, length, generation and allocation arithmetic.
- Classic cross-reference tables, trailers, `startxref` and incremental `/Prev`
  chains; cross-reference streams, object streams and hybrid `/XRefStm` files,
  all resolved through the same production filter pipeline. There is no
  bootstrap decoder.
- The latest revision of each object wins deterministically, and dropped history
  is reported. A scan-based recovery path exists and is reported when used.
- Explicit stacks and cycle detection throughout, with no unbounded recursion.

### Filters

Built in: `FlateDecode` (the runtime's DEFLATE, with every PNG predictor and TIFF
predictor 2 at all component sizes), `LZWDecode` with `EarlyChange`,
`ASCIIHexDecode`, `ASCII85Decode` and `RunLengthDecode`. Every stage is charged
against per-stage and aggregate byte, expansion, chain-depth and work budgets. An
image states its own decoded size, which raises the expansion ceiling to that
size; the absolute byte ceilings still bound the decode.

Composed through `Broiler.Documents.Pdf.Images`, whose filters wrap decoders in
`Broiler.Media.Image.Managed`: DCT (every 8-bit Huffman-coded
process, one or three components), CCITT fax (MH, MR, MMR), JPEG 2000 Part 1 (one
tile, default precincts, LRCP/RPCL) and JBIG2 (generic regions under MMR and
arithmetic coding, arithmetic symbol dictionaries, text regions and refinement).
Details and limits are in [extension-points.md](extension-points.md#4-the-extension-points).

### Encryption

`/Encrypt` is found in the trailers, and the document is opened or rejected
before any object stream, Catalog, metadata, font, image, annotation or content
service runs. A recovery scan that stands in for the trailer still finds the
encryption dictionary.

- The standard security handler at revisions 2, 3, 4 and 6 is in the base build:
  RC4 40–128-bit, AES-128, AES-256, crypt filters and `/EncryptMetadata`. A
  document opens with the caller's password (`PdfReadOptions.WithCredentials`) or
  its own empty user password, never by guessing.
- The copy-and-extract permission is enforced, because everything this codec
  produces is extracted content. The owner password lifts it. The other
  permissions are reported, not enforced.
- The public-key handler's PDF half is built in. Its CMS envelopes are opened by
  a caller-composed `IPdfRecipientDecryptor`, and none ships.
- Diagnostics never carry passwords or document content.

### Document structure and metadata

- Catalog, page tree with inherited attributes, boxes, `/Rotate` and `UserUnit`.
  Each page is read the way a viewer shows it: the crop box clipped to the media
  box, turned and scaled into points.
- The effective version is resolved from header and Catalog. `/Extensions` are
  inventoried as diagnostics only.
- `Info` and the XMP packet (ISO 16684-1:2019 read subset, IP-004) are normalized
  into the shared metadata allowlist. XMP wins per field, `Info` fills gaps,
  disagreement is reported by field name, and the raw packet is dropped.
- Annotations, outlines, forms, actions, embedded files and signatures are
  inventoried and reported, never executed or extracted.

### Text and fonts

- Graphics and text state, every show-text operator, Form XObjects under bounded
  recursion with a visited set, length-bounded inline-image consumption, and
  marked-content `ActualText`.
- Simple-font encodings with `/Differences`; `ToUnicode` CMaps including
  `bfrange` and bounded `usecmap`; composite fonts through `Identity-H`; the
  Symbol font's built-in encoding. ZapfDingbats is recognized in order to be
  refused rather than read as Latin.
- The seven Latin ligature characters U+FB00–U+FB06 are read as the letters they
  join. Nothing else is normalized.
- Subset prefixes are removed from font names using PDF metadata, never by
  guessing bold or italic from substrings.
- Type 3 fonts are read for what they state: `ToUnicode`, `/Differences` names, a
  named `/BaseEncoding`, and `/FontMatrix` advances. Glyph procedures are never
  executed, and a Type 3 font that names no encoding maps nothing.
- With `Broiler.Documents.Pdf.Fonts` composed, a composite font with no
  `ToUnicode` map is recovered from its embedded program: sfnt through its
  character map, bare CFF through its charset's glyph names. The glyph names are
  resolved through the codec's one authored glyph-name table. Type 1 and
  CID-keyed CFF stay unread.
- Text turned against the page (sideways, upside down, mirrored, more than about
  a degree off horizontal) is read along its own baseline into words, lines and
  paragraphs, set upright because the model holds no text direction, and
  reported as `pdf.text.orientation-not-kept` (information). An untagged page
  reads it after the text that runs the page's main way. Letters each turned
  their own way, as along a curve, share no baseline; they are placed where each
  lands and reported as `pdf.text.orientation-unsupported`, so such a read is
  not a clean success.

### Reading order and page structure

- **Declared order.** A tagged document's structure tree is walked for its
  sequence and grouping, never its roles. A page whose runs the tree accounts for
  in full is read in that order. A page it covers only partly falls back to
  geometry as a whole, because mixing declared and inferred order yields a
  sequence nobody asked for. Artifacts are exempt from the coverage test.
- **Inferred order.** Otherwise the page is read by geometry and says so under
  `pdf.import.reading-order-heuristic`: columns split at gutters, lines on shared
  baselines, paragraphs at wider gaps, indents, short lines and list markers. The
  exact rules are in the feature matrix.
- **Optional content.** The catalog's default configuration `/D` is honoured
  (`/BaseState`, `/ON`, `/OFF`, OCMD membership), and content in a group it turns
  off is omitted and reported. Annotations on hidden layers are still classified.
  `IncludeHiddenOptionalContent` takes every layer.
- **Page furniture.** Artifact text that repeats above or below everything else
  on every page becomes `RunningContent`, with `DifferentFirstPage` where page
  one differs. Anything that varies stays in the body.
- **Page geometry.** The document states the size most pages share. Margins are
  read off where the body's ink sits, since PDF declares none.
- **Recovered structure.** Ruled grids become `DocumentTable`s, including merges,
  nested grids and tables continued across a page break. Bars under or through
  text become underline or strikethrough, fills beneath text become backgrounds,
  and a ruled box around text becomes a one-cell table. Each is reported as a
  reconstruction, because PDF never states it.

### Images and links

The approved raw-sample subset: DeviceGray at 1, 2, 4 or 8 bits, DeviceRGB at 8,
and Indexed at 1, 2, 4 or 8 over a bounded DeviceGray or DeviceRGB palette, with
`/Decode` validated against its colour space's interval. Samples are normalized
to RGBA and admitted through the caller's resource policy. A composed codec's
RGBA output is taken as it stands.

- **Masks.** A stencil is painted in its fill colour. Colour-key `/Mask`, explicit `/Mask` and `/SMask` become straight
  alpha, each mapped onto the picture's unit square, and a soft mask's `/Matte`
  is undone over gray and RGB pictures. Nothing is composited.
- **`ICCBased`** (IP-024). With `IccColorProfileReader` composed, the colour is
  converted to sRGB. Without it, a raw-sample image in an ICC space is refused by
  name, while a codec-decoded image keeps the codec's colours.
- **Refused by name:** a stencil painted with a pattern, a mask the build cannot
  decode or that contradicts the format, colour spaces and depths outside the
  subset, an Indexed image that remaps its own indices, and samples that do not
  fill the declaration.
- **Budget.** Projected pixels are charged against the read's decoded-byte
  budget. A charge past a limit drops that image, not the document.
- **Page backgrounds.** A picture covering the page beneath its visible text is
  left out of the flow and the margins and reported, because the model has no
  page background.
- **Inline images** are consumed and inventoried, not decoded.
- **Links.** Only URI actions admitted by `PdfUriPolicy` become `LinkHref`. It is
  stricter than the shared `DocumentLinkTarget`: a bare `#fragment` is refused,
  and `http` and `mailto` need a caller opt-in
  ([extension points §4.3](extension-points.md#43-pdfuripolicy--link-admission)).
  Internal destinations are reported as dropped until a cross-format anchor
  model exists. A page with no extractable text reports `pdf.text.ocr-required`.

### Writer

- Layout is resolved once into an immutable page list that the serializer
  consumes without re-measuring. Lines are broken against a replaceable metrics
  provider; the built-in one is Broiler-authored and approximate, and says so.
- New PDF 1.7 files: catalog, page tree, resources, Flate content streams,
  cross-reference table and trailer.
- The fourteen standard font names with WinAnsi encoding and no embedded program.
  A character outside the encoding is substituted and reported.
- Colour, decorations, alignment, indentation, lists, and link annotations
  revalidated immediately before emission.
- Caller-supplied normalized metadata and a content-derived file identifier.
- Byte-identical output across runs. It reads no clock, machine name, locale or
  installed font.
- No font embedding. The fail-closed preflight (`DocumentFontEmbedding`) and the
  `fsType` reader exist and refuse until IP-012 is re-opened.

## Composition and delivery

- The codec and its two optional providers ship as preview packages.
- **Hosts.** The Windows and Linux Writer heads register the codec for opening
  only. The CLI in this repository composes it read-only: `formats` reports PDF
  as read and never written, and a PDF destination is a usage error. Of the
  optional providers the CLI composes only `IccColorProfileReader`. Guard tests
  keep every other composition root closed and stop any head from registering
  PDF for saving.
- **Optional services.** Image filters and the ICC reader come from
  `Broiler.Documents.Pdf.Images`, the font-program reader from
  `Broiler.Documents.Pdf.Fonts`, and the recipient decryptor from the host. A
  missing optional service yields a detected-and-skipped result. It is never
  discovered from ambient state.

## Design rules the code follows

- **Public surface.** The codec, its options, limits and results,
  `PdfCodecServices`, the extension contracts with their context types, the
  built-in filters, and `PdfDiagnosticCodes`. The PDF object model (objects, xref
  entries, page dictionaries, operator tokens, parser internals) stays internal
  until a real consumer justifies it.
- **Declarations are dispatch inputs only.** A header version, Catalog
  `/Version`, `/Extensions` or tolerance of malformed input never widens the
  approved feature set.
- **Recovery is narrow.** Only documented recovery paths exist; the reader does
  not scan arbitrary input for something that looks like an object.
- **Fonts from a document stay in that document.** An embedded program is parsed
  only through bounded services for the current read. It is never installed,
  cached across documents, exposed, bundled as a fallback, or carried into a
  newly written PDF.
- **No guessed text.** Without a reliable mapping the codec reports
  `pdf.text.mapping-missing-or-uncertain` instead of inventing characters. It
  never guesses from glyph indices.
- **No hidden coordinates in the model.** Source page boundaries are extraction
  boundaries, not layout preservation. `PdfReadOptions.MapPageBreaks` maps them
  to paragraph page breaks on request; re-pagination can still differ.
- **Colour honesty.** DeviceRGB stays uncalibrated RGB and is not relabelled
  sRGB. Sample bytes are never labelled PNG or JPEG. No ambient platform decoder
  is consulted.
- **Resource decisions before construction.** `ExtractToModel` is evaluated
  before an `InlineImage` exists. Without it the image is omitted and reported.
- **Fonts on the write side come from the caller** (path B of the
  [font-path brief](../../ip/briefs/font-path.md)): the caller configures a font
  set, the project bundles none, and text with no configured font fails
  preflight with `pdf.write.no-font-configured`.
- **Limits compose.** `PdfLimits` composes with the shared `DocumentLimits` and
  Media limits, and the stricter remaining budget always wins
  ([architecture notes](../architecture.md#limits-and-link-policy)). Delegated
  work is charged back to the document's counters and never resets them.
