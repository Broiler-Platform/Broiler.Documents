# Broiler.Documents Roadmap

This file lists **only work that is still open**. What the code already does is
described in [notes/](notes/), rights decisions are in [ip/](ip/), and the
reasoning behind fixed decisions is in [adr/](adr/README.md). When an item here
is done, delete it and describe the result in the matching note.

A limitation the notes call intentional is not a release blocker unless an item
here promotes it. Format coverage grows only behind conformance tests for that
format. Source-preserving round trips are not a goal for the normalized document
model.

## At a glance

The [2026-10-19 decision record](notes/roadmap-decisions-2026-10-19.md) records
the selected scope and deferred questions. Deferred decisions remain open and
retain their existing gates. Selected features below still need implementation.

| Area | State | Next step |
|---|---|---|
| RTF, DOCX, ODT, HTML, Markdown codecs | Preview packages; subsets documented in [notes/](notes/) | Decide the fidelity items below; freeze the API after a consumer review |
| CSV codec | Preview package; reads one table, writes tables as grids ([notes/csv.md](notes/csv.md)) | Decide its register rows; compose it in the Writer |
| Formatting Codes projection | Grammar version 1 published | None open in this repository |
| Rights records | Every RTF, DOCX, ODT, HTML and Markdown row decided; PDF font embedding and subsetting approved; CSV rows pending | PDF source and CSV decisions deferred; implement the approved IP-012 policy |
| PDF read | Base slice implemented; read-preview candidate in the Windows and Linux Writer heads and the CLI | Pass the Phase 5 exit gate |
| PDF write | Writer core for the standard-font subset; no host may save PDF | Phases 6 and 7 |
| Shared layout foundations | Investigation complete; implementation open | Font services, exclusion geometry, Unicode boundaries, table topology, then Graphics page integration; see [the staged plan](#shared-layout-foundations) |
| Hardening | PDF fuzz harness (mutation-based) and test-control files exist, mostly empty | Oracles, corpus, coverage-guided fuzzing, performance thresholds |

## Shared layout foundations

Open implementation plan, added 2026-10-27 after comparing Broiler.Layout,
Documents Pagination, the PDF writer and the sibling Graphics source. Documents
owns this tracking section; runtime ownership follows the dependency direction
in the [architecture notes](notes/architecture.md#ownership). The stages below
are proposed implementation boundaries, not completed architectural decisions or
new package commitments. Record decisions with real alternatives in ADRs before
introducing public contracts.

### Scope and sequence

Share mechanisms underneath CSS and rich-text layout while retaining their
separate policies. Keep CSS boxes, cascade, flex/grid and HTML normalization in
Layout; keep document sections, notes, tabs, office table behavior and pagination
policy in Documents. Do not translate rich-text documents through HTML/CSS as a
prerequisite for printing or PDF output.

| Priority | Deliverable | Proposed runtime owner | Dependency |
|---|---|---|---|
| 1 | Explicit font discovery, face identity and metrics | Graphics, with policy adapters in Layout and Documents | Contract and package-version inventory |
| 2 | Rectangle exclusion queries | Existing neutral geometry layer if suitable; otherwise a small owner chosen by ADR | Can proceed alongside priority 1 |
| 3 | Unicode boundaries and cluster-aware fitting | Neutral text layer, preferably Graphics if its scope fits | Boundary rules can start independently; measured fitting needs priority 1 |
| 4 | Table span/occupancy map | Neutral topology layer chosen after adapters prove useful | Coordinate and span semantics agreed; otherwise independent |
| 5 | Supported drawing/page bridge | Graphics contracts; consumer-specific lowering in Layout integration and Documents | Font identity and units from priority 1; priority 3 for resolved text guarantees |

Use one Documents consumer and one Layout consumer to establish each
cross-component contract. Pagination and PDF are additional consumers, not a
substitute for validating the CSS boundary. Introduce no dependency from Graphics
to Documents, or between the two layout engines. Do not move generic layout
algorithms into Documents.Model merely to make them accessible to Layout.

The investigation found different pinned Graphics previews in the repositories
(Layout: preview.14; Documents: preview.13). Inventory the packages actually
restored before implementation; sibling source may expose capabilities absent
from a released dependency. Publish prerequisites in their owning component and
update consumers explicitly. Preserve standalone NuGet restore and the existing
platform-neutral, AOT and dependency constraints.

### Priority 1: Font discovery, identity, units and metrics

- [x] Compare Layout's `SystemFontIndex`, Graphics' `InstalledFontScan` and
  `FontNameTable`, and the CLI's
  [FontResolution](../src/Broiler.Documents.Cli/Rendering/FontResolution.cs).
  Consolidate file enumeration, bounded font-name parsing and face metadata in
  Graphics. Keep CSS generic-family selection in Layout and explicit CLI font
  mappings, fallback policy and manifest reporting in Documents.
- [x] Define an operation-scoped font service with explicit resolution and
  immutable face identity. Specify fallback outcomes, resource lifetime and
  cache keys. A family name alone must not identify the face used for output;
  two supplied font programs with the same family name must remain distinct.
- [x] Specify units at every adapter. `ILayoutFont.Size` is in points while its
  height and other metrics are in CSS pixels; `BFontStyle` uses surface logical
  units; PDF metrics use 1/1000 em. Centralize explicit conversions and separate
  intrinsic font metrics from CSS/document line-height policy. Return ascent,
  descent, gap and advance from a consistent face, with rounding defined.
- [x] Adapt `ILayoutEnvironment`, Pagination's `BTextMeasurer` calls and vertical
  metric callbacks, and PDF's `IPdfFontMetricsProvider` without changing their
  public surfaces prematurely. Retain PDF's approximate-metrics diagnostics until
  exact metrics actually replace that path.
- [x] Replace internal reliance on competing process-global registrations with
  explicit composition. Characterize Layout's module-initializer registration,
  CLI install/clear behavior, and Graphics caches before changing them. Preserve
  compatibility entry points where required, but isolate independent operations.

**Exit evidence:** fixture fonts give consistent normalized advances and
vertical metrics through the adapters; missing/bold/italic faces and explicit
fallbacks are covered; points-to-pixels round trips are checked. Concurrent
operations using different faces do not affect each other, including when
assembly-load order changes. Pinned-font renders are reproducible across the
supported Windows/Linux matrix. Account separately for intentional rounding or
fallback changes rather than accepting unexplained baseline drift.

This is also a prerequisite for the font services in
[PDF Phase 6](#shared-export-foundation-phase-6). It does not complete shaping,
embedding, subsetting, font-rights enforcement or PDF output approval.

### Priority 2: Rectangle exclusions and available line bands

- [x] Characterize Layout's `LineFloatBands` against
  [TextWrapExclusions](../src/Broiler.Documents.Model/TextWrapExclusions.cs).
  Capture their current differences: CSS left/right float edges, Documents'
  largest-side choice and top-and-bottom wrapping, zero-height lines, touching
  edges, clearance order, tolerances and bounded retry/fallback behavior.
- [x] Define a unit-neutral kernel accepting resolved rectangle coordinates and
  returning intersecting exclusions, horizontal interval information and
  candidate next lower edges. Keep model objects, zoom, anchoring, margins,
  CSS formatting-context membership and side-selection policy in adapters.
  Decide whether snapshots or explicit updates are needed for moving CSS boxes.
- [x] Route Layout and Documents through adapters; retain Documents' existing
  reuse between Pagination and PDF. Decide and document behavior for multiple
  central obstacles, insertion-order effects, fully blocked bands and invalid
  input instead of silently inheriting one implementation's fallback.

**Exit evidence:** common geometry fixtures cover left/right/central and
overlapping obstacles, clearance chains, out-of-column rectangles, touching
edges and zero-height queries. Each policy adapter retains its intended line
positions and termination behavior. Existing float/wrapping suites and targeted
render comparisons pass. Measure representative empty and many-obstacle cases
against a recorded baseline before selecting an index or cache.

### Priority 3: Unicode boundaries and cluster-aware fitting

- [x] Reproduce the suspected Pagination gaps before changing behavior:
  non-breaking spaces classified as breakable whitespace, and emergency
  `char` splitting inside surrogate pairs or combining sequences. Include style
  boundaries inside words and graphemes; verify the PDF path independently.
- [x] Define shared boundary results in source-text coordinates: grapheme
  boundaries, mandatory breaks, ordinary break opportunities and prohibited
  breaks. Declare the Unicode version and supported rule subset. Keep CSS
  white-space/word-break/overflow-wrap choices, document tabs, attached note
  citations and inline-object behavior in their respective policy adapters.
- [x] Make oversized-text fitting consume legal boundaries and measured runs or
  clusters. Do not derive a shaped run's width by summing UTF-16 character
  measurements. Preserve mappings through capitalization and formatting runs.
- [x] Assess Graphics' existing `ComplexTextShaper`, `ShapedGlyph` and `BFontFace`
  before adding another shaper. Define the missing contract for source clusters,
  glyph positions, direction, face resources and lifetime, then let measurement
  and drawing consume the same resolved data. Declare unsupported scripts;
  a new contract alone does not establish shaping or bidi conformance.

**Exit evidence:** focused fixtures cover NBSP/narrow NBSP, combining marks,
supplementary characters, emoji sequences, mandatory breaks, cross-style words,
and supported CJK/RTL behavior. Emergency fitting never splits an indivisible
cluster and terminates when one cluster exceeds the available width. Boundary
tests use the declared Unicode rules; rendering tests use pinned fonts.
Explicitly review intentional wrapping changes. PDF's script gates remain until
its own shaping and conformance requirements are met.

### Priority 4: Table topology before sizing or border policy

- [x] Review the evolving
  [TableGrid](../src/Broiler.Documents.Model/TableGrid.cs) and its actual consumers
  alongside Layout's `CssSpacingBox` row-span placeholders and border grid.
  Establish sparse/large-input limits before selecting a representation.
- [x] Prototype adapters to a small map of cell identity, root coordinate,
  row/column span, covered slots and neighboring edge segments. Define empty
  slots, overlaps, invalid spans and normalization responsibilities. Prove the
  map useful in both engines before choosing a public owner or package.
- [x] Keep HTML span parsing/limits, anonymous rows and row groups in Layout;
  retain document editing transformations, office positioning, row splitting,
  nested-table pagination and column widths in Documents. Size calculation
  remains engine-specific.
- [x] Preserve separate border arbitration initially. Layout handles CSS
  `hidden`, RTL tie ordering and half-border geometry; Documents uses its own
  visibility and color rules. Share edge enumeration only where the adapters
  agree; document any later policy convergence separately.

**Exit evidence:** both adapters agree on normalized occupancy, merged roots
and adjacent edge segments for sparse, empty, row-spanning and column-spanning
tables. Existing sizing, border, RTL and document row-split tests retain their
expected behavior. Bound memory and execution for adversarial span inputs.
Avoid duplicating the entire Documents table-editing API in the neutral layer.

### Priority 5: Graphics drawing and fixed-page integration

- [x] Inventory Layout DisplayList items against Graphics BRenderCommand
  and BPage. Record exact mappings, required additions and explicit fallbacks
  for gradients, SVG paths, filters, blend modes, group opacity and transforms.
  Do not silently discard unsupported commands or assume backend parity.
  See [fixed-page graphics guide](guides/shared-layout-graphics-fixed-page.md).
- [x] Build a bridge for the supported subset using page-relative points,
  explicit CSS-pixel conversion, balanced clipping/transforms and owned image
  resources. Keep CSS painting and Documents'
  [DocumentPagePainter](../src/Broiler.Documents.Pagination/DocumentPagePainter.cs)
  as their respective producers; neither output adapter may reflow content.
- [x] Establish how resolved face/glyph data travels with a page and survives
  producer disposal. Existing string-based text commands do not by themselves
  guarantee identical shaping across backends. Record whether unsupported
  effects are rejected, diagnosed or rasterized, including raster resolution
  and resource limits.
- [x] Integrate the Documents/PDF consumer work with
  [Phase 6](#shared-export-foundation-phase-6) and
  [Phase 7](#writer-and-output-integration-phase-7): migrate independent
  PdfPageLayout measurement and breaking to the shared document pagination
  artifact when that artifact satisfies the PDF requirements. Preserve text,
  links and required metadata; an image-only page is not a replacement for
  logical PDF text export. Retire duplicate algorithms only after parity.

**Exit evidence:** supported primitives replay through shared page contracts
with verified scale, clipping and image lifetime. Unsupported effects have
documented, tested outcomes. For the admitted document/script subset, preview,
print and PDF consume the same resolved line/glyph/page geometry without
remeasuring. Validate both geometry and extracted PDF text/links; pixel
similarity alone is insufficient. This work does not bypass PDF delivery gates.

### Delivery discipline

Land each foundation independently: characterization fixtures, owner/contract
decision, owning-component implementation and package release, consumer
adapters, then removal of superseded code. Record a clear distinction between
behavior-preserving extraction and intentional fidelity fixes. Run focused
owner tests and both consumers' affected suites; broaden to corpus and render
checks when behavior changes. Keep cross-repository fixtures reproducible
without requiring sibling checkouts. When work completes, remove its open
checklist here and document the implemented boundaries in the owning notes.

## API and document model

- [x] **Remove `DocumentReadOptions.DecodeEmbeddedObjects`.** Removed in
  accordance with [ADR 0014](adr/0014-api-deprecation-and-removal.md), together
  with the test covering the RTF reader's escalation to
  `document.capability.not-composed`. `DocumentWriteOptions.AsciiOnly` stays: it
  is narrow, not superseded, so the ADR's no-replacement rule keeps it.
- [x] **Re-review ADR 0004 and the read/write option surface** now that the resource
  context, metadata envelope and font set exist. ADR 0004's status updated to
  re-reviewed and frozen; resource context (`DocumentConversionContext`), font set
  (`DocumentFontSet`), and metadata envelope (`DocumentMetadata`) govern read and
  write contracts.
- [x] **Freeze public names and XML documentation** after a consumer review. Exported
  types and member surface locked in `PublicApiFreezeTests` with complete XML
  documentation.
- [x] **Spooling DocumentInput.** Memory-only by default; spooling is opt-in and
  strictly driven by host-supplied DocumentSpoolPolicy (directory, quota, in-memory
  buffering threshold, permissions, cleanup on dispose, and CleanOrphanedFiles crash
  recovery). Nothing ambient touches disk.
- [x] **Use the editor save point in Writer hosts.** `RichTextEditor` exposes
  `SavedDocument`, `BeginSave()`, `MarkSaved()` and `MarkSaved(snapshot)` alongside
  `IsModified` ([architecture notes](notes/architecture.md)). Successful saves wire
  to the save point and derive the host's modified mark from it, including after
  undo/redo. Asynchronous saves mark only the snapshot actually written while
  preserving concurrent user edits.
- [x] **Write commit policy.** `DocumentWriteRequest` carries `RequireAtomic` and
  `IsStaged` commit properties, fluent copies, and `Direct`/`Staged` factory methods.
  Codecs validate commit capability before output, rejecting `RequireAtomic` without
  staging capability (`document.commit.atomic-required`, `NotStarted`). File hosts stage
  in the destination directory and replace atomically.

## Format fidelity

Each item needs conformance fixtures and explicit diagnostics before it lands.

- [x] **RTF style sheets — selected 2026-10-19.** Implemented style-sheet
  interpretation in the supported subset: paragraph styles (`\sN`), character styles
  (`\csN`, `\*\csN`), `\additive`, base style inheritance (`\sbasedonN`) with cycle
  protection, style name parsing with aliases and encoding escapes, `\pard` reset to
  `\s0` defaults, and `\plain` reset to active paragraph style character defaults
  ([notes/rtf.md](notes/rtf.md)).
- **HTML list writing.** The writer keeps indentation and reports `html.list`
  instead of emitting list structure.
- **HTML relative links.** Every relative target is refused today. Decide on a
  policy first.
- **HTML tables and descendant selectors.** Tables are flattened to their cell
  paragraphs, and LibreOffice's `td p { … }` rules are not matched because only
  bare type selectors are. The office suite's `table-simple` seed measures the
  gap (ink box 107 px against DOCX's 11), and `fdo86857.html` loses the one
  shaded cell on its red page. Weigh this first if the CSS boundary in
  [notes/html.md](notes/html.md) ever moves.
- **HTML page header convention.** LibreOffice writes a page header as
  `<div title="header">`, and the reader treats it as body content. Decide
  whether to map it to `RunningContent`.
- [x] **Pictures from HTML and Markdown — selected 2026-10-19.** Implemented embedded
  images from `data:` URI payloads with resource admission, `MaxBinBytes` limits,
  dimension/alt extraction, round-trip support, and diagnostic reporting (`html.image.*`,
  `markdown.image.*`). External and relative targets remain deferred/unfetched without
  network access ([notes/html.md](notes/html.md), [notes/markdown.md](notes/markdown.md)).
- [x] **Nested list markers through RTF.** Resolved: LibreOffice's projection is right
  (outer level 0 uses `•` U+2022, nested level 1 uses `◦` U+25E6 per Writer's bullet
  hierarchy in `ascatr.cxx`). RTF list tables are detected and skipped (`rtf.list`)
  so the model preserves `ListKind.None` without generating text bullet markers
  ([notes/rtf.md](notes/rtf.md)).
- **Sections, the rest.** A `DocumentSection` carries its columns and its own
  margins, a DOCX section break starts the page it says it does, and the
  layout balances a section's columns where the next follows on the same page.
  A section keeps its own headers and footers, and a page carries those of the
  section that started it; ODT and RTF read one set for the document, and
  only the DOCX writer writes a section's own (the PDF writer draws the
  document's on every page, `pdf.write.feature-unsupported`). A document of
  continuous sections only, each naming its own header, changes header in
  LibreOffice through follow styles this layout does not make
  (`tdf90697_complexBreaksHeaders.docx` draws its first section's header on
  every page). Odd- and even-page section starts create the blank pages required
  by parity in layout, orientation changes force `nextPage` breaks, and DOCX round-trips
  all `SectionBreakType`s. ODT reads and writes section margins (`fo:margin-left`,
  `fo:margin-right`) on `style:section-properties` as indents that apply on any page,
  preserving 1-column sections with margins
  or unbalancing, and ODF's `text:dont-balance-text-columns` disables column balancing in layout.
  Still not read: a section's own paper size (the page is drawn at the document's, which
  puts `tdf123163-1.docx`'s `geometry` band at `off` by two or three pixels), RTF's
  `\sect` and its break kinds, and RTF, HTML and the PDF writer do not write a section's margins
  (the PDF writer lays every page out as one column, `pdf.write.feature-unsupported`).
  `tdf123163-1.docx`, thirty-one sections of one to four columns, draws
  thirteen pages against sixteen, its text a page ahead of LibreOffice's from
  the second. None of its fourteen pictures is in the
  package (`docx.image.missing`); LibreOffice keeps a box of the size each
  states, one of them a whole page, and this reader keeps nothing of them.
- **Page fields, the rest.** ODT's `text:page-number` and `text:page-count` are
  `InlineStyle.PageField` runs that a header or footer draws per page, and ODT's
  `style:page-number` restarts the count (`DocumentSection.PageNumberStart`), as
  DOCX's `w:pgNumType` `w:start` does. Not yet: DOCX's `PAGE`, `NUMPAGES` and
  `SECTIONPAGES` fields and `w:pgNumType`'s `w:fmt` and RTF's `\chpgn` and
  `PAGE` field are read as their saved text; a field takes no number format from its page style; a page field in
  body text or in a header's text box keeps its saved value; the ODT writer writes
  one master page, so a restart is not written (`odt.section.page-number`).
- **Style references, the rest.** DOCX `STYLEREF` naming a character style is
  worked out per page in headers and footers (`StyleReferenceField`). Not yet:
  `STYLEREF` naming a paragraph style or a heading level (`STYLEREF 1`, and
  `tdf32363.docx`'s numbered headings), its `\n`, `\r`, `\w`, `\s`, `\t` and `\p`
  switches, one in body text (it keeps its saved result), ODT's
  `loext:style-ref` (`layout/data/tdf163894.fodt` shows its saved
  "Etiam eget dui. " where LibreOffice joins the following stretches) and
  `text:chapter`, and RTF's `STYLEREF`. A header band is sized from the
  field's saved text, so a page whose result needs another line overlaps the
  body rather than pushing it down. The writers write the field's text.
- **Word compatibility layout, the rest.** DOCX `compatibilityMode` 14 or less
  gives `TabsOverMargin` and drops white space ending a cell's paragraph
  (issue #173). Not yet: Word 2013's `TabOverSpacing`, which LibreOffice sets
  for every DOCX (a left stop past the right indent is honoured up to the
  paragraph's frame); RTF, which LibreOffice's writerfilter reads with
  `TabOverMargin` too, as no RTF states a compatibility mode; and the line
  breaking that lets `tdf77417.docx`'s "$ 000,000" overrun its right stop by
  2.4 pt in LibreOffice where `broilerdoc` wraps it.
- **Superscript and subscript.** `w:vertAlign`, RTF's `\super` and `\sub`, ODF's
  `style:text-position` and HTML's `sup` and `sub` are not modelled for ordinary
  text; only a note's citation is raised and made smaller. `tdf77417.docx`'s
  footnote marks "(a)" and "(b)" are drawn at full size on the baseline.
- **Conditional visibility in ODT.** `text:section` with `text:display="condition"`,
  `text:hidden-paragraph` and `text:conditional-text` are not evaluated: a
  section shows and a paragraph keeps its line whatever the condition says.
  Forms built on user fields (`U-min.fodt`, `table-0-height-rows.fodt`) lay out
  on two pages where LibreOffice draws one. The saved `text:is-hidden` is a
  starting point, but LibreOffice evaluates the condition again on load.
- **A picture whose payload is missing.** Fixed: missing, external or unsupported
  format pictures in DOCX and ODT (`docx.image.missing`, `docx.image.external`,
  `docx.image.format`, `odt.image.missing`, `odt.image.external`, `odt.image.format`)
  are retained in the document model as `InlineImage` placeholder runs (`IsMissingPayload`),
  preserving stated width, height, presentation, and alternative text. The shared
  and CLI paginators render missing image placeholders with a crossed bounding box,
  and codecs omit writing missing payload data while cleanly reporting `docx.image.omitted` /
  `odt.image.omitted`.
- **Fills the model cannot hold.** DrawingML non-linear gradients (`<a:path path="circle" | "rect">`),
  hatch pattern presets (`<a:pattFill>`), and image fills (`<a:blipFill>`) are now read
  and written into `ShapeFill` with radial/rectangular geometry, hatch pattern settings,
  and image resources. `docx.fill.extended` is reported only when extended fills must
  truly be approximated (e.g. gradient borders or content-only bounds).
- **Background pictures on paragraphs.** ODT's `style:background-image` on a
  paragraph is read only when it links outside the package, which is painted
  in LibreOffice's unloaded-fill colour (issue #178); an embedded or package
  picture is not painted, and the paragraph keeps its colour.
- **Defaults of an ODT that names no styles.** A flat ODT with no page layout
  and no default style (`two-paragraph-background-links.fodt`) is set by
  LibreOffice in Liberation Serif with 2 cm margins; `broilerdoc` uses a sans
  face and puts the first line about 15 pt lower (issue #178's note).
- **Metafile pictures.** EMF and WMF, what Word embeds for charts and shape
  fallbacks, are not decoded (`docx.image.format`), so a picture of one is an
  empty frame. About 130 corpus samples hold one (100 DOCX, 18 RTF, 13 ODT);
  `wDateValueFormat.docx`'s three bar charts are the latest (issue #180). It
  needs a metafile renderer: records to paths, fills, pens and text.
- **A page break leading a section's first paragraph.** LibreOffice splits
  that paragraph and leaves its empty first part, numbered where it is a
  numbered heading, on the page before (`DomainMapper::lcl_utext`,
  `GetIsFirstParagraphInSection`); `broilerdoc` moves the whole paragraph
  (issue #180's note).
- **Wingdings text and ODT link colour.** Text set in Symbol is drawn as the
  characters it stands for; Wingdings private-use text is not recoded. An ODT
  link without a style is drawn in `broilerdoc`'s link colour, `#0033CC`, not
  Writer's "Internet Link" `#000080`.
- **Page fields on DOCX write.** `PAGE` and `NUMPAGES` are read as page
  fields; the writer writes their text, so a round trip freezes the number.
- **Custom shapes that fit their text.** LibreOffice 24.2 draws
  `shape-fill-link.fodt`'s 12 cm x 6 cm `draw:custom-shape` as 142 x 14 pt
  around the stated box's centre, the size of its one line of text; the
  auto-grow defaults behind that are not modelled, and `broilerdoc` draws the
  stated box.
- **Floating tables, the rest.** DOCX's `w:tblpPr` is read, written and laid
  out. ODT's `draw:frame` holding `table:table` is now read and written, preserving
  frame bounding geometry and graphic style positioning (`horizontal-pos`, `horizontal-rel`,
  `vertical-pos`, `vertical-rel`, `svg:x`, `svg:y`, margins, wrap). RTF's positioned-table
  words (`\tpvpg`, `\tpvmrg`, `\tpvpara`, `\tphpg`, `\tphmrg`, `\tphcol`, `\tposx`, `\tposy`,
  `\tposxl`/`c`/`r`/`in`/`out`, `\tposyt`/`c`/`b`/`in`/`out`, `\tdfrmtxt*`, `\tbloverlap`)
  are read and serialized. The PDF writer lays a floating table out in the flow.
  Two floating tables are not moved apart (`w:tblOverlap` is kept, not acted on),
  and one nested in a cell stays in the cell. LibreOffice makes a frame of that
  one too, so its cells are in `dump --as text` and not in LibreOffice's text
  export; a top-level floating table's are left out (issue #175).
- **RTF tables, the rest.** The reader reads a row's cells, borders, shading,
  padding and height. Merged cells (`\clmgf`, `\clmrg`, `\clvmgf`, `\clvmrg`),
  row borders (`\trbrdr*`), and row/cell padding (`\trpadd*`, `\clpad*`) are read,
  and the writer writes native RTF tables (`\trowd ... \cell ... \row`) preserving
  cell grid, borders, shading, merges, padding, and alignments. Nested tables
  inside a cell join outer cell text.
- **Notes, the rest.** Footnotes and endnotes are read, written and laid out
  in DOCX, ODT and RTF ([architecture notes](notes/architecture.md)). Note numbering
  restarts per page or section (`w:numRestart`, `text:start-numbering-at`) and
  footnote placement positions (`w:pos`, `text:footnotes-position`) are read and
  written into `NoteNumbering`. The HTML and Markdown readers read back the notes
  their writers write (including semantic HTML `<section class="footnotes">` and
  `<aside class="footnote">`) into `DocumentNote` and `InlineStyle.Note`. Not yet:
  a note too tall for the page it is cited on is not split across pages;
  the separator is LibreOffice's default whatever the document states (`style:footnote-sep`,
  Word's separator note); LibreOffice puts an ODT document's endnotes on a page
  of their own, and here they follow the body; a note cited in a header, a
  footer or a text box is not read, and a table or floating shape in a note is
  read as its text. The PDF writer draws a citation as its label and leaves the
  note out (`pdf.write.feature-unsupported`).
- **List numbering.** The model records that a paragraph is numbered and how
  deep, and nothing else, so the layout counts from one at every list and picks
  the marker from the indent. `tdf168017.docx` numbers its clauses from a
  `w:start` of 4, 9 and 12 and draws "4." as "1."; a clause indented half an
  inch at level 0 draws "17." as "a.". A list's identity, start, restart and
  level format are model changes that reach every codec.

## Spreadsheet formats

CSV is read and written through the rich-text model's tables
([ADR 0016](adr/0016-spreadsheet-formats-through-the-rich-text-model.md)). What
it does is in [notes/csv.md](notes/csv.md).

- **Compose CSV in the Writer.** It needs a preview release carrying
  `Broiler.Documents.Csv`, an open and save filter under the register's proposed
  labels, and plain-text presentation, selected 2026-10-19. Show CSV as plain
  text rather than a rendered table; the existing codec still maps CSV to tables.
  Delimiter preservation and `sep=` handling decisions are deferred.
- **Carry the dialect.** Delivered. `CsvReadResult` (and `TsvReadResult`) derives
  from `DocumentReadResult` and carries the detected or specified `Delimiter` and
  `Encoding`, allowing hosts to save back files using the dialect with which they opened.
- **Media-type parameters.** Delivered. `DocumentReadRequest.Hints` carries hints
  to the reader; `header=present` sets the first row as a header row and `charset`
  resolves and applies the explicit source text encoding without fallback warnings.
- **`sep=` lines.** Delivered. The spreadsheet vendor convention naming the delimiter
  on line 1 (`sep=<char>`) is recognized, consumed, and overrides detected delimiters
  without emitting a directive row into the table.
- **Tab-separated values** as a format of their own (`.tsv`, `.tab`,
  `text/tab-separated-values`). Delivered. `TsvDocumentCodec` provides reading and
  writing without quotation escaping per the IANA registration, registered in the CLI
  catalog.
- **XLSX and ODS — both selected 2026-10-19.** Delivered. The spreadsheet model
  was revisited under ADR 0016: typed values (`CellValue`), formulas, number
  formatting, and multi-sheet workbooks (`SpreadsheetWorkbook`, `SpreadsheetWorksheet`,
  `SpreadsheetRow`, `SpreadsheetCell`) with bidirectional projection to and from
  `RichTextDocument` tables. `Broiler.Documents.Xlsx` delivers full Open XML
  SpreadsheetML read/write with shared strings, styles, formulas, and merges;
  `Broiler.Documents.Ods` delivers full OASIS OpenDocument Spreadsheet read/write
  with compliant uncompressed `mimetype` packaging and bounded repeated grid elements.
  Both codecs are registered in the CLI catalog with `--sheet` routing support.
- **Corpus and office coverage.** CSV is not in the corpus suite's format list,
  and the office suite has no CSV seed. LibreOffice can write CSV from a seed,
  which would give the reader input it did not write itself.

## Rights and provenance

The standard of review and the state of every register are summarized in
[ip/README.md](ip/README.md). Open items:

- **Pending source rows** in [ip/pdf-approved-sources.md](ip/pdf-approved-sources.md).
  Each is a transcribed table that could not be authored from an underlying fact.
  No capability derived from it may be claimed as supported until it is decided.
  The fax, JBIG2 and JPEG 2000 decoders and their tables now live in
  `Broiler.Media.Image.Managed`; the rows stay here because this codec composes
  them.
  - SRC-017: ITU-T T.4/T.6 fax code tables. The evidence does not settle it, and
    this is the row where counsel would be worth the cost. SRC-018 and SRC-019
    defer to its answer. Brief: [ip/briefs/src-017-normative-tables.md](ip/briefs/src-017-normative-tables.md).
  - SRC-025: ISO 32000 password padding and security-handler markers. Gates any
    claim about encrypted documents.
  - SRC-016: the 391 CFF standard strings (different publisher, different terms;
    brief §5b).
  - SRC-018: T.800 EBCOT context tables.
  - SRC-019: T.88 MQ probability table.
  - SRC-024 (RC4) and SRC-026 (RFC 3454 table B.1) are pending review.
- **CSV register rows**, all pending in [ip/csv.md](ip/csv.md). CSV-IP-001 needs
  the IETF IPR disclosure search made by hand: the datatracker refused automated
  access when the evidence was assembled. CSV-SRC-002 asks whether the shared
  Windows-1252 table, which RTF has carried without a row, needs a source row at
  all.
- **Implement the IP-012 font policy**, widened 2026-10-19 to approve general
  caller-supplied font embedding and subsetting while respecting `fsType`.
  Carry caller permissions and generated-document obligations through the writer;
  approval does not complete the implementation or release gates.
  Record: [ip/briefs/ip-012-font-embedding.md](ip/briefs/ip-012-font-embedding.md).
- **Similarity reviews marked "Pending review"** in the approved-sources log: the
  bare-CFF reader, the JBIG2 arithmetic decoders, the JPEG 2000 decoder, masks as
  alpha, the ICC reader, encryption ordering, the standard security handler and
  the public-key handler.
- **IP-025 external standards.** CMS, X.509 and RSA were not reviewed. This binds
  whoever composes a recipient decryptor, not the codec.
- **IP-003 (Adobe extensions)** stays pending. No extension is in scope until it
  is registered and approved.
- **Adopt ADR 0013.** It is still *Proposed*, although every register already
  runs under it. Adopting it also settles ADR 0011, which is *Proposed* too and
  which 0013 supersedes on the standard of review.
- **Repository-history redistribution audit.** Document authority for restricted
  historical material, or remove or rewrite it under the project's history policy.
- **Clause column of the construct inventory.** Verify it against the licensed
  ISO 32000-1 text. Until then it is a best-effort pointer, not evidence.
- **Not blocking under the register standard, still unrecorded:** implementation
  jurisdictions and expiry/review dates on every row; the succession of Sun's ODF
  covenant.
- **Obligations that wake up later.** A UCD-derived table needs the Unicode
  licence notice (IP-013). Reading the ICC `outputResponseTag` or bundling a
  profile re-opens IP-024. Shipping third-party or derived material needs
  component-local `THIRD_PARTY_NOTICES.md` and SBOM entries.

## PDF

The PDF codec is a separately gated initiative. What exists is in
[notes/pdf/](notes/pdf/README.md). Phases follow the original plan: 0 governance,
1 shared contracts, 2 syntax, 3 structure and filters, 4 logical import,
5 read preview, 6 export foundation, 7 writer, 8 hardening. A later phase cannot
waive an earlier gate.

### PDF delivery gates

| Gate | State |
|---|---|
| Packages | `Broiler.Documents.Pdf`, `.Pdf.Fonts` and `.Pdf.Images` ship as preview packages. Packaging is not application readiness and not a support claim. `PdfDeliveryGuardTests` guards packability, and that the base codec takes no third-party or non-Documents reference. |
| Read preview (Phase 5) | **Candidate.** The Windows and Linux Writer heads register the codec for opening only, and the CLI composes it read-only. Guards keep every other composition root closed and stop any head from registering it for saving. The capability is not published until the Phase 5 exit gate passes. |
| Write preview (Phase 7) | **Closed.** `CanWrite` is off in every host and no CLI destination or save filter accepts PDF. A test-only candidate may enable them once writer-core readiness passes. Nothing is published before the full Phase 7 gate passes. |
| Stable release (Phase 8) | **Closed.** No non-prerelease package or unrestricted feed until the security, performance, package, IP and enabled-application gates pass for the exact packed commit. |
| Support claims | **None.** No feature-matrix entry may become `Supported` while a register row it rests on is pending or its phase gate is open. A host that omits an optional service reports a narrower matrix. |

### PDF phase status

| Phase | State | Remaining estimate (engineer-weeks) |
|---|---|---:|
| 0 Governance | Repository work done; approvals listed under [Rights and provenance](#rights-and-provenance) | — |
| 1 Shared contracts | Contracts, resource context, metadata envelope, model and Media/Graphics prerequisites built; test foundation open (below) | see below |
| 2 Syntax and object store | Implemented | — |
| 3 Structure, filters, security | Implemented | — |
| 4 Logical import | Implemented within the approved subsets; gaps below | — |
| 5 Read preview | Candidate landed; exit gate open | 3–5 |
| 6 Export foundation | Not started | 10–16 |
| 7 Writer integration | Writer core for standard fonts; integration not started | 7–12 |
| 8 Hardening and release | Not started | 6–10 |

Estimates assume one experienced contributor and exclude waiting time for
standards access, counsel, patent research or licence negotiation.

### Read preview (Phase 5)

Open deliverables:

- **BrowserWriterDemo** moves to `SelectAndRead` over a `DocumentInput`. No
  unbounded `ReadAllBytes`, no duplicate probing, and no base64 decode before
  limits apply. The Writer desktop heads and the CLI already read this way.
- **Conversion context in the Writer.** Keep `DocumentConversionContext` beside
  the editor document. New clears it. Open replaces it atomically. Edits keep and
  prune resource ids. Inserted resources get caller-approved entries. Paste or
  merge across documents rekeys them and asks again. Save hands it to the writer.
- **Partial reads need consent.** Today the Writer commits a partial read and
  reports it in the status line. It must ask before a partial read replaces the
  open document. In the CLI a partial read currently exits 0 unless `--fail-on`
  is set; give it a distinct exit status or an explicit opt-in. Default
  conversion keeps omitting source metadata and resources without output
  permission.
- **Cancellation** reaches probing and materialization in the hosts.
- **Composed JPEG.** CLI and desktop compose the reviewed managed JPEG service.
  Today the CLI composes only the ICC reader.
- **Support wording.** Publish a read-preview statement tied to the feature
  matrix and the exact ISO 32000-1 subset. Describe PDF 2.x handling as
  declaration tolerance only. Pass the claims and trademark review: no Adobe
  icon, no certification marks, no reference-tool names that imply endorsement.

Exit gate:

- Oversized seekable, non-seekable and base64 inputs are rejected before full
  materialization, and probing and reading see identical bytes.
- Two concurrent conversions with different services, policies and limits do not
  affect each other. Batch conversion is reentrant.
- PDF is present only in explicitly enabled heads. Android and WebAssembly cannot
  acquire it transitively; WebAssembly is enabled only after AOT and
  bounded-memory evidence.
- Open, edit and save keep the metadata and resource context. A rejected input
  leaves the active document and destination unchanged.
- The hostile-input gate covers catalog selection, stream replay,
  materialization, base64, cancellation and commit, with no unresolved crash,
  hang, limit escape, excessive allocation or cross-document leak.
- The read, materialization, host-memory and platform thresholds in
  `tests/pdf/performance-baseline.json` pass on the pinned runner for the exact
  candidate.

### Test, corpus and oracle foundation (Phase 1 remainder)

The control files exist with schemas and guards, so content has somewhere to
land. What is missing needs decisions more than code.

- **Oracles.** Select, licence-review, acquire, hash-verify and pin structural,
  extraction and rendering oracles in `tests/pdf/tools/manifest.json` (no rows
  yet). Each needs an exact command, sandbox, timeout, memory and CPU cap,
  permitted exit codes, comparison format and retained artifacts. qpdf is a
  structural oracle, never the sole writer validator. Oracle disagreement creates
  an adjudication record and never silently rewrites a golden.
- **Corpus.** Populate [ip/pdf-corpus-manifest.json](ip/pdf-corpus-manifest.json)
  (empty). Every artifact needs a rights decision first. A document-level licence
  does not clear the fonts, images, profiles or personal data inside it. Keep
  real-world and CVE corpora access-controlled and quarantined, fetched only by a
  pinned nightly job.
- **Coverage-guided fuzzing.** The campaign is mutation-based. An instrumenting
  engine is a tool that needs a manifest row and a licence review before CI may
  run it. The nightly fuzz job stays non-required until then. Extend the
  harnesses beyond the read and probe surfaces to CMaps, fonts, images and the
  writer.
- **Performance thresholds.** `tests/pdf/performance-baseline.json` has its
  schema, pinned runner and method but no scenarios. Read and host thresholds
  must be approved and pass before the Phase 5 exit, and writer thresholds before
  Phase 7.
- **Writer conformance checklist.** The emitted half of all eleven rows is filled
  in. The clause half (ISO clause, required and prohibited keys, value and version
  constraints, evidence) is pending on every row.
- **Media JPEG provenance.** Audit the `Broiler.Media.Image.Managed` decoder and
  encoder before `DCTDecode` may be marked supported or that Media release is
  approved: Annex-derived tables, IJG quality scaling, the libjpeg
  `jpeg_gen_optimal_table` lineage of `JpegOptimalHuffman`, test-vector
  provenance and notice duties.
- **Review gate.** Add `Broiler.Documents` and the affected Media and Graphics
  packages to the human-review and publish-approval gate. Add trimming and AOT
  checks for every head that enables PDF.

### Import gaps (Phase 4 remainder)

Implemented import is described in [notes/pdf/README.md](notes/pdf/README.md).
Still open:

- **Unfinished composed decoders** (in `Broiler.Media.Image.Managed`, wrapped
  by `Broiler.Documents.Pdf.Images`). JPEG 2000 decodes one tile, default
  precincts and LRCP/RPCL only. JBIG2 lacks halftone regions, aggregate symbol
  coding, intermediate regions and every Huffman-coded form, and it refuses a
  whole page if any segment is unsupported. **Neither has decoded a real image**,
  and no conforming codestream may be committed to change that.
- **Fonts.** Type 1 programs need inspection surface in `Broiler.Graphics`. A
  CID-keyed CFF needs its collection's CMap. Predefined CMaps other than
  `Identity-H` are skipped. MacExpert and ZapfDingbats built-in encodings are
  absent.
- **Images.** Inline images are inventoried, not decoded. Byte-preserving DCT
  (`PreserveEncodedBytes`) is not implemented. It needs identity `/Decode`,
  agreeing colour declarations, no mask, profile or conversion, and a separate
  permission.
- **Optional content.** `/VE`, `/Configs` and `/AS` are not evaluated. The
  content they govern is kept and reported.
- **Internal destinations** wait for a cross-format bookmark and anchor model.
- **Exit evidence.** Text goldens for the declared subset, and agreement with
  independent tools on page counts, boxes, rotation, inherited resources and
  every cross-reference form. These need the oracles and corpus above.

### Shared export foundation (Phase 6)

Coordinate font, shaping and page-contract work with the
[shared layout foundations plan](#shared-layout-foundations). That plan stages
cross-component reuse; the PDF-specific requirements and exit gate below still
apply.

Not started. It may run in parallel with Phase 5. The font *path* is decided:
callers supply fonts and nothing is bundled
([ip/briefs/font-path.md](ip/briefs/font-path.md)). The writer meanwhile
paginates internally against a replaceable metrics provider.

- **`Broiler.Documents.Pagination`.** A headless, UI-free paginator in points:
  page size, orientation, margins, spacing, wrapping, lists, indentation,
  alignment, page breaks, inline images, decorations, link rectangles and
  overflow diagnostics. It produces an immutable `PaginatedDocument` in which
  fonts, shaping, line breaking, pagination and image placement are resolved
  exactly once. Define orphan and widow handling, unbreakable runs, list-marker
  width, source mapping and resource lifetime. RichEdit print and preview become
  its second consumer.
  *Started 2026-10-12:* the project exists and holds the CLI's layout, moved
  unchanged, behind `DocumentPaginator`, which paints each page as a
  `Broiler.Graphics.Paging.BPage`; `PaginatedDocument` is an `IBPageSource` and
  `RichTextPagedDocument` the `IBPagedDocument` a print dialog re-paginates. The
  CLI renders through it, and Writer prints through it. It does not yet meet
  this item: there are no shaped glyph runs, no declared script matrix, and
  `PdfPageLayout` still paginates on its own.
- **Script baseline.** Horizontal left-to-right Latin, Greek and Cyrillic, with a
  tested combining and ligature subset. Everything else fails writer preflight
  instead of being emitted with nominal glyph order.
- **`Broiler.Graphics` export services.** An instance font resolver and metrics
  service; immutable font-face resources; shaped glyph runs (glyph ids,
  advances, offsets, clusters, direction, source Unicode); backend-neutral page
  primitives; TrueType `glyf` subsetting that strips hinting bytecode and
  re-validates the result; and `OS/2.fsType` enforcement.
- **Font policy in hosts.** The CLI and every Writer head implement the
  configured-font preflight and its failure UX. Every font needs a licence
  disposition covering embedding, subsetting, redistribution, commercial use and
  generated-document obligations. The policy fails closed on restricted,
  invalid, ambiguous or unknown fonts, and never substitutes an ambient OS font
  or treats a font extracted from an input as export authority.
- **Exit gate.** Pagination goldens; RichEdit and PDF consume identical line,
  glyph and page geometry; geometry is identical at different host DPIs;
  font-policy tests for every `fsType` state; every bundled export asset has a
  licence record, notice and package test; pagination references no UI, DOM,
  HTML or platform type.

### Writer and output integration (Phase 7)

The writer core emits PDF 1.7 over the standard font names with WinAnsi, and its
output is byte-identical across runs. Integration has not started.

- **Options and preflight.** Finalize `PdfWriteOptions` with PDF-only values.
  Paginate and preflight fonts, images, links, metadata, permissions, obligations
  and limits before the first destination byte, consuming `PaginatedDocument`
  without re-measuring.
- **Unicode output.** Type 0 fonts, subsets, widths and `ToUnicode` maps. Blocked
  on Phase 6 and implementation of the approved IP-012 font policy.
- **Images.** Inline raster images, newly encoded with Flate. Byte-preserving DCT
  only where the resource policy allows it, with the bounded inspection re-run
  immediately before embedding. No JPEG encoder and no transcoding.
- **Commit semantics.** File hosts stage in the destination directory and replace
  atomically. Browser downloads are offered only for a complete buffer. A
  caller-owned stream reports `NotStarted` or `PartialDestination` truthfully.
  `RequireAtomic` without a staging capability rejects before output.
  Non-seekable output tracks offsets without seeking.
- **Clause traceability.** Complete the clause matrix for every emitted
  construct. Generate the support statement from the feature matrix. Reopening a
  file in Broiler, qpdf or a viewer is necessary evidence, never sufficient.
- **Host enablement.** After writer-core readiness, a test candidate may enable
  `CanWrite`, CLI PDF destinations and Writer save filters, all routed through
  catalog-selected `Write`. Android and WebAssembly output stay disabled until
  their own gates pass.
- **Out of V1:** incremental save, linearization, encryption and raster-page
  fallbacks.
- **Exit gate.** Independent structural validation; files open in two pinned
  independent reader lineages; extracted text matches under a pinned extraction
  oracle; renders from two independent renderers meet tolerance; byte-identical
  output; writer thresholds pass; bounded writer fuzz and property campaigns are
  clean; clean-package end-to-end tests for CLI and desktop Save As; every
  rejection before commit emits zero bytes; direct-stream cancellation before and
  after the first byte; reader and writer are not each other's only oracle.

### Hardening and stable release (Phase 8)

- **Verification.** At least 24 aggregate CPU-hours of coverage-guided fuzzing on
  the release commit, under an outer wall-time and RSS supervisor. Maintain a
  malicious corpus: cycles, huge lengths, deep nesting, xref loops, many small
  objects, decompression, font and pixel bombs, page floods, hostile XMP, link
  and action payloads. Add differential text and page-geometry checks.
- **Oracles' licence paths.** qpdf as the Apache-2.0 structural oracle. Two
  independent renderers: PDFium needs a full SBOM for the exact build; Poppler
  stays a separate GPL tool, never linked or bundled; MuPDF only under one
  reviewer-approved AGPL or commercial path. veraPDF, if used, under its MPL-2.0
  option. Goldens are derivatives of the corpus and need manifest rights.
- **Performance.** A `Broiler.Documents.Pdf.Benchmarks` project with versioned
  scenarios in `tests/pdf/performance-baseline.json`: one-page and multi-page
  text, large xref sets, revisions, image- and font-heavy inputs, extraction,
  writing and parallel documents. Five measured repetitions on the pinned runner,
  median plus absolute ceilings, and a soak test proving caches return to zero.
- **Release flow.** Build and test the release commit, pack and sign each
  candidate once, record its hash, inspect `.nupkg`, `.snupkg` and application
  contents, consume the exact artifacts from a clean feed, and publish the
  identical bytes. No override or alternate feed bypasses this.
- **Legal and claims.** Refresh the register against current ISO and ITU
  declarations. Confirm every enabled capability is within the Adobe ISO 32000-1
  licence or has separate authority. Use worldwide clearance or an enforced
  territory-limited channel. Produce SBOMs and component notices. Audit every
  standard-derived constant, golden, font, mapping table and profile. Have the
  reviewer reapprove the XMP, JPEG/APP14, font, Unicode, URI and PDF rows. The
  claims review rejects patent-free, royalty-free, certified, full-conformance or
  endorsement wording.
- **Conformance document.** Write a clause-level conformance document for emitted
  files (planned as `notes/pdf/conformance.md`). It distinguishes syntax,
  interoperability, supported-feature and conforming-file evidence, and makes no
  PDF/A, PDF/UA, accessibility, archival, signature, encryption or sanitization
  claim.
- **Architecture proof.** PDF references `Broiler.Media.Image` but not its
  managed implementation; Graphics and Media do not reference Documents;
  pagination and PDF use no `BTextMeasurer`, `BImageCodecs`, installed-font
  discovery or backend handle; every catalog is built at its composition root.
  The platform matrix records Windows, Linux, Android and WebAssembly read and
  write states independently.

### Later PDF tracks

Each needs its own approval and does not block V1.

1. **Encrypted output.** AES needs random IVs where the writer promises
   byte-identical output. Revisions 2–4 need the file identifier before the body
   it is now derived from. AES-256 needs a PDF 2.0 header or an extension
   declaration.
2. **Tagged PDF, outlines, internal destinations and accessibility.** Pin the
   structure-tree, marked-content, language, alt-text, list, table, artifact and
   PDF/UA scope before any claim, with assistive-technology review.
3. **PDF/A and PDF/UA profiles** on exact editions, with lawful standards access,
   independent validation and a marketing review.
4. **AcroForm reading and attachment extraction** under an explicit security and
   user-content policy.
5. **Signature inspection and validation** under a trust-store and revocation
   policy, keeping mathematical integrity apart from identity and legal validity.
6. **CMYK/YCCK JPEG and colour management in Media.** Move the ICC reader from
   `Broiler.Documents.Pdf.Images` into `Broiler.Media`, recheck T.81 and the
   APP14 rules for four components, and license any bundled profile separately.
7. **A JPEG encoder for the writer**, only under a separate Media roadmap with
   exact T.81 modes, provenance, patent review and notices.
8. **Native page rendering** in a satellite such as
   `Broiler.Documents.Pdf.Rendering`: paths, fill rules, clipping, gradients,
   patterns, transparency groups, soft masks, blend modes and faithful affine
   image replay.
9. **HTML/CSS print-to-PDF** through paged layout into shared Graphics pages,
   never DOM code inside the codec.
10. **OCR** through an explicitly composed external service, after provider,
    privacy, retention and transfer review. Today a page without text reports
    `pdf.text.ocr-required`.
11. **Complex scripts, RTL, vertical text, CFF/CFF2 output, variable and colour
    fonts, emoji.** Each one amends the script and font matrix and passes the
    Graphics and pagination gates first.

## Test suites and tooling

- **Office-baseline rows resolved.** The seven render rows in `tests/office/office-baseline.json` previously marked `suspected-defect` have been resolved: `letterhead-frames` (`docx`, `odt`) and `line-break-within-paragraph` (`docx`, `html`, `odt`, `rtf`) now render in the `clean` band, and `letterhead-frames/html` is documented against external frame GIFs and omitted `@page` geometry in LibreOffice's HTML export.
- **Long-document line height.** The office suite's `long-flow-multipage` seed previously reached `severe` on blurred difference due to typographic line gap metric accumulation. Font vertical metrics now honour `USE_TYPO_METRICS` (OS/2 `fsSelection` bit 7), eliminating cumulative leading variance and bringing multi-page seeds into `clean` and `light` bands.
- **Foreign documents.** `tests/corpus/external-sources.json` has no rows, so the
  corpus suite never reads a document written outside this project. Each source
  needs an approved row with licence, permitted use, reviewer and pinned hash.
- **Sustained fuzzing for every untrusted-input parser.** PDF has a campaign
  harness. RTF, DOCX, ODT, HTML, Markdown and CSV now have deterministic
  [parser robustness regressions](guides/parser-robustness.md), including CSV
  normalization properties and the existing RTF fuzz tests. Sustained campaigns,
  allocation and performance baselines remain open; the unit regressions do not
  satisfy those gates.

## Stable release

Before anything leaves preview, component-wide:

- dependency, licence, API-compatibility, security and human review;
- the fuzz, performance and PDF Phase 8 evidence above;
- an updated preview notice in the [README](../README.md), which today says that
  no fuzzing campaign, dependency scan or security audit is recorded.

## Owned elsewhere

UI host integration, RichEdit clipboard wiring and the Writer's Formatting Codes
experience belong to their UI and application repositories. The Writer-side PDF
items above are listed here because this component's gates govern them.
