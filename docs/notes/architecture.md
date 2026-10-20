# Architecture Notes

How the component is put together and the contracts every codec shares. The
package list, dependency direction and component constraints are in the
[README](../../README.md#packages). The decisions behind them are in the
[ADRs](../adr/README.md).

## Ownership

The PDF work forced the question of where shared work lives. The answer applies
to every codec.

| Owner | Holds | Must not hold |
|---|---|---|
| `Broiler.Documents` | Codec requests and options, `DocumentInput`, result status, cancellation, the conversion and resource context, diagnostic locations, shared limits, the metadata envelope, the shared link predicate, packaging helpers | Anything about xrefs, PDF objects, operators, CMaps or encryption |
| `Broiler.Documents.Model` | Cross-format semantics with a named non-PDF consumer: page breaks, justification, finite measurements, `PageGeometry`, `DocumentStyleDefaults`, `InlineImage` with an opaque identity and an immutable Graphics image resource | Resource-policy decisions, PDF colour semantics, page coordinates, annotations, positioned glyphs |
| `Broiler.Graphics` (external) | Font-face resources, the bounded font-program inspector, `BImageResource`, and later shaped glyph runs and export subsetting | `Broiler.Documents` policy types, PDF font dictionaries, PDF encodings, Standard 14 data |
| `Broiler.Media.Image` (external) | Immutable encoded and decoded image payloads, decode contracts and limits; the managed JPEG codec in `.Managed` | `Broiler.Documents` policy types, PDF filter dictionaries, predictors, masks, `/Decode` |
| Format codecs | Their format's syntax, mapping, limits and diagnostics | Anything another format needs as well |

Rules that follow from it:

- **Containment.** A shared addition needs a format-neutral name, owner-local
  tests and a second, non-PDF consumer. Otherwise it stays inside the codec that
  needed it. `DocumentMetadata` was promoted only once DOCX and ODT consumed it.
- **Dependency direction.** Graphics and Media never reference
  `Broiler.Documents`. They expose their own technical decisions, the conversion
  context translates caller policy into them, and the codec composes them.
- **Image payloads.** Media owns immutable managed-memory payloads, and Graphics
  owns `BImageResource`, the wrapper the model and page scenes share. No payload
  carries a PDF colour space, `/Decode`, mask, pooled owner, native handle or
  ambient decoder.
- **Legal containment follows code containment.** The component that
  distributes a font, table, profile or codec owns its licence record, notice and
  SBOM entry. Legal uncertainty is no reason to duplicate shared codec code.

## Codec contract

`DocumentCodec` is the one contract every format implements (ADR 0003, amended
by ADR 0008).

- **Probing** reads a bounded prefix and reports a confidence. The highest
  confidence wins, and a tie goes to the codec registered first.
  `DocumentCodecCatalog.SelectAndRead` probes and reads in one
  pass over a replayable `DocumentInput`, so a non-seekable source never loses
  the prefix the codec needs.
- **`DocumentInput`** defines stream ownership and buffer lifetime, replays probe
  bytes, exposes a known length, and materializes within a byte ceiling. It is
  memory-only by design; spooling waits on a host-supplied policy. It keeps
  positions past EOF, rejects negative positions, invalid seek origins and
  overflow, and reports a disposed stream as unreadable. Closing one stream does
  not close the input or another stream.
- **Requests.** `DocumentReadRequest` carries input, options and cancellation.
  `DocumentWriteRequest` carries document, destination, options and cancellation.
  Each cross-format value has exactly one owner, and format options may not
  duplicate it.
- **Typed options.** Format options derive from `DocumentReadOptions` and
  `DocumentWriteOptions` (`RtfReadOptions`, `PdfReadOptions`, and so on). A codec
  validates the concrete type before touching input or output, and a mismatched
  type is an invalid-options result (`document.options.invalid`), never a silent
  downcast. Where a shared and a format budget overlap, the stricter one wins.
  Group depth and binary payload size stay on `DocumentLimits` because DOCX uses
  them as well as RTF.
- **Result status.** `DocumentResultStatus` is `Success`, `Partial` or
  `Rejected`, independent of diagnostic severity. `Partial` needs explicit caller
  approval before it replaces an open document or is published. CLI exit codes
  and host prompts derive from it, not from `HasErrors`.
- **Destination state.** A write reports `NotStarted`, `Committed` or
  `PartialDestination`. `Success` requires `Committed`.
- **Async.** Async overloads exist beside the synchronous contract.
- **Capabilities a codec lacks** are reported as
  `document.capability.not-composed` rather than quietly approximated. That is
  how `DecodeEmbeddedObjects` and `AsciiOnly=false` are answered today.
- **Composition.** A codec receives its services at construction and discovers
  nothing through statics, environment variables or platform registries. There is
  no global registration and no module initializer. Catalogs are built at each
  application's composition root.
- **Diagnostics** carry a code, a severity, a message and an optional location.
  They never contain passwords, unbounded document content, local paths or
  metadata values. Codes are API.

## Resources and metadata

- **`DocumentConversionContext`** is the immutable record of which resources a
  conversion met and what the caller's policy decided for each. A read returns
  it on `DocumentReadResult.Resources`, and a write consumes it through
  `DocumentWriteOptions.Resources`. The DOCX, RTF, HTML, Markdown and ODT writers
  all consume it, so a resource cannot escape policy by changing output format.
- **Operations are separate flags** (`DocumentResourceOperations`): semantic
  projection, metadata projection, transient decode, extraction into the model,
  byte transfer, transformation, embedding, redistribution. Nothing implies
  anything else, and absence is denial.
- **Identity is scoped.** A `DocumentResourceId` belongs to one context
  namespace, and each entry is bound to the payload digest, kind and dimensions.
  A resource moving to another conversion finds no entry and must be decided
  again. The context holds decisions; `DocumentConversionContextBuilder` runs
  the policy while a codec reads, and the result is closed.
- **Policies.** `DocumentResourcePolicy.DenyAll`, `Default` (read into the
  model) and `AllowOwnDocuments` ship. A write with no context uses
  `DocumentConversionContext.Empty`, so a picture is left out with the writer's
  own omission diagnostic, for example `rtf.image.omitted`.
- **Fonts.** `DocumentFontSet` carries the caller's configured fonts, and
  `DocumentFontEmbedding` is the fail-closed embedding preflight: it requires a
  per-font licence disposition and refuses restricted, silent, no-subsetting and
  bitmap-only `fsType` declarations. Nothing embeds a font today.
- **Metadata.** `DocumentMetadata` carries nine normalized fields: title, ordered
  authors, subject, ordered keywords, language, creator application, producer,
  creation and modification date. Missing and explicitly empty stay distinct.
  `DocumentDate` keeps a timestamp's stated precision and optional UTC offset and
  never invents a zone. DOCX reads and writes it through `docProps/core.xml` and
  `docProps/app.xml`, ODT through `meta.xml`, PDF through `Info` and XMP. Each
  codec reconciles its own sources first.
- **Transfer is explicit.** Nothing copies a read result's metadata into a
  write. A caller who wants that does it, and `DocumentMetadata.With` is where
  they correct what should not survive. A write reports each field as emitted,
  narrowed or stripped (`document.metadata.*`), naming fields and never quoting
  values. Raw XMP, arbitrary properties, source IDs, paths and user names are
  never carried.

## Limits and link policy

- **`DocumentLimits`** bounds probe bytes, document bytes, group depth, run
  length, paragraph count and binary payloads. Every reader is total: malformed
  input yields a best-effort document plus diagnostics, never an exception across
  the API (ADR 0004). `PdfLimits` adds PDF-specific accounting and composes with
  the shared limits and Media's own.
- **Packages.** `Packaging/DocumentPackage` gives DOCX and ODT one bounded ZIP and
  XML loader. It counts decompressed bytes, reads at most one byte past the
  remaining budget, prohibits DTDs and disables external resolution.
  `Resources/DocumentImageFormats` gives both codecs one table of image
  extensions, raster signatures and preferred output extensions. Both are
  internal.
- **Text.** `Text/Windows1252` gives RTF and CSV one table for the code page's
  32 positions that differ from Latin-1, so neither codec registers an encoding
  provider, which would be process-wide state. It is internal.
- **Links.** `DocumentLinkTarget` is the one predicate the five interchange
  codecs share, on read and on write: absolute `http`, `https` and `mailto`, plus
  a non-empty `#fragment` without whitespace, quotes or a second `#`. A refused
  target is written as plain text and reported. A fragment preserves the
  reference, not a working jump, because no codec reads or writes bookmarks.
- **Two deliberate exceptions.** `PdfUriPolicy` is configurable and stricter: it
  requires an absolute URI, needs an opt-in for `http` and `mailto`, caps length,
  refuses user information, and stores targets canonicalized.
  `FormatCodeEditIntent` keeps its own predicate because
  `Broiler.Documents.FormatCodes` references only the model. It caps length,
  rejects control characters, and admits the empty string, which the edit
  language uses to remove a link.
- **No I/O.** No reader fetches anything: no remote image, stylesheet, template
  or link target. URI validation performs no DNS lookup.

## Document model rules

- **Units.** Dimensions are points. RichEdit and Writer convert to pixels
  explicitly through `BFontStyle.PointsToPixels`; no control passes a point value
  to a pixels API.
- **Measurements.** Non-finite and negative values are rejected wherever the
  model lets one be stated.
- **Images.** `InlineImage` carries a `DocumentResourceId` and a
  `BImageResource`, with nullable point dimensions. When both are absent, the
  intrinsic pixels at 96 per inch are used, and an unplaceable image is
  reportable rather than a sentinel.
- **Style defaults.** `DocumentStyleDefaults` states the document's size and
  logical family. DOCX reads them from `w:docDefaults` and ODT from
  `style:default-style`. The size always resolves, at 12 points. The family may
  be absent: the renderer then uses its own face, while the PDF writer refuses
  to pick one.
- **Page geometry.** `PageGeometry` is the page a document says it was written
  for: size, margins, header and footer distances, one per document. DOCX, ODT,
  RTF and HTML state it; PDF import infers it. It describes the page and places
  nothing on it. Absolute coordinates, positioned glyphs and fixed-layout objects
  remain outside the model.
- **Promotion.** A feature enters the model only with a named, tested consumer in
  another codec, as justified alignment, page breaks and shape z-order did.
- **Undo.** `RichTextEditor` holds the only history. Controls and structured
  editors drive it and keep no stack of their own (ADR 0006). Each transaction
  stores the document and selection before and after, so undo swaps snapshots
  and never inverts operations. Most calls record one transaction. Keyboard
  editing is grouped instead: `TypeText` joins text typed straight after earlier
  typing, and `Backspace` and `Delete` at a caret join a run of their own kind.
  A typed word that follows whitespace starts a new transaction. So does any
  selection change, other edit, undo, redo, pending style, or a host's
  `BreakUndoGroup` call. `InsertText`, which paste uses, never joins. The depth
  bound (100 by default) counts transactions, not keystrokes.
- **Save point.** `RichTextEditor.MarkSaved()` records the current immutable
  document and ends the keyboard undo group without clearing history.
  `IsModified` compares the current document with that snapshot by reference,
  so undo/redo can return to an unmodified state. Selection and pending typing
  style do not count; independently recreating identical text does. Construction,
  `LoadDocument` and `LoadPlainText` establish an unmodified baseline. Hosts mark
  saved only after a successful save of the current snapshot; asynchronous saves
  must ensure the editor still holds the document that was written. Writer UI
  integration remains open.

Populated line boxes use the maximum ascent and descent of their actual pieces,
including list markers and inline images. An unused default font does not impose
a minimum height. Empty lines use default-font metrics. The paragraph's line
spacing multiplier applies to the resulting height without moving its baseline.
Hosts can supply per-layout ascent and descent through
`LayoutSettings.FontVerticalMetrics`; invalid or absent values retain the
Graphics fallback. Text, tabs and empty lines use the same provider. The CLI
supplies the explicitly mapped font's `hhea` ascent and descent, scaled from font
units to points, caching inspection per file for that render configuration.
Unmapped families retain fallback metrics. The paginator performs no installed
font discovery and holds no global metrics provider. The CLI also supplies the
mapped face's `hhea` line gap as leading below the glyph box. This is not full
OpenType line-spacing selection: `OS/2` typographic metric selection is not
incorporated.

The paginator anchors a header's complete line block at `PageGeometry.HeaderDistance`
below the top edge and a footer's block at `PageGeometry.FooterDistance`
above the bottom edge of the effective page box. Without document page geometry,
it centres each block in its margin. An invalid distance or a block that
cannot fit at that distance is diagnosed and omitted rather than drawn over the
body. Header and footer blocks include paragraph spacing: before-spacing between
paragraphs and after-spacing after each paragraph, matching body layout. The
first paragraph's before-spacing is suppressed. These gaps count towards margin
fit and distance anchoring. Running content does not appear
in continuous layout.

## Graphics and Media prerequisites

- **Font inspection.** `BFontProgramInspector` in `Broiler.Graphics` validates
  the table directory with checked arithmetic, refuses a table overlapping its
  own directory, reads the character map only within its slice, and pins the
  accepted tuple. WOFF, WOFF2, collections, variable fonts, CFF2, colour and
  bitmap glyphs, Graphite and AAT are refused by name. It is deliberately
  separate from the rasterizer's forgiving parser.
- **JPEG.** The accepted tuple is Huffman SOF0, SOF1 and SOF2 at 8 bits with one
  or three components. Every other frame type and precision is refused by name.
- **Media limits.** `MediaLimits` bounds dimensions, components, sampling
  factors, scans, restart intervals, marker segments, coefficient memory and
  total blocks alongside the byte and pixel budgets. The coefficient count is
  computed in `long`, because 65535² at 4×4 sampling wraps an `int` to zero.
- **One decode core.** `ImageCodec` builds both public paths on one CPU
  `DecodeCore`, so admitted modes, colour handling and limits are identical on
  the sync and async paths. The sync path reads synchronously, and a test fails
  if it ever blocks on the async one.

## Guards

Rules that matter are enforced by tests against the tree, not by review.

| Guard | Holds |
|---|---|
| `*ArchitectureTests` (each test project) | Project and package references; no UI, DOM or platform dependency where forbidden; the CLI's composition root |
| `FormatClaimGuardTests`, `OdtClaimGuardTests`, `CsvClaimGuardTests` | No data file beside a codec, no third-party package, no prohibited wording in a package description, a register with approved labels per format, no committed document of a supported format. For ODT and CSV also: every diagnostic code is documented |
| `PdfClaimGuardTests` | Every PDF diagnostic code appears in the PDF notes or register, the register names only codes that exist, no `Supported` entry while a row is pending, approved labels, no data file beside the codec |
| `PdfPhaseZeroGuardTests` | The PDF decision and evidence documents exist, the corpus starts empty, no legacy external-process CLI surface, no PDF type in shared assemblies or packages |
| `PdfDeliveryGuardTests` | Packability, references of the base codec, which composition roots may name the codec, no committed `.pdf` |
| `PdfTestControlGuardTests`, `PdfWriterConformanceGuardTests` | Tool manifest and performance file approvals; every key the writer emits is listed in the conformance checklist |
| `CorpusControlGuardTests`, `OfficeControlGuardTests` | Baseline rows are classified with reviewer and date and cite files that exist; registered tools are never product references |

Three PDF guards inspect application heads in the aggregate repository. They are
skipped in a standalone checkout and run when this component is checked out
inside the aggregate. `src/tests/Shared/RepositoryFiles.cs` is linked into every
test project for repository discovery and reference extraction; it ships in no
runtime assembly.
