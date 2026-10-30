# ADR 0016: Spreadsheet Formats Through The Rich-Text Model

**Status:** Proposed; awaiting the project reviewer's adoption
**Date:** 2026-10-12

## Roadmap follow-up — 2026-10-19

The project reviewer selected both ODS and XLSX for development. This activates
the model-revisit trigger below: prepare a workbook model proposal for typed
values, formulas and multiple sheets before implementing those codecs. It does
not select a model design or adopt this proposed ADR wholesale.

The reviewer also selected plain-text CSV presentation in the Writer. The
existing CSV codec still maps records to rich-text tables; adapting Writer
presentation is outstanding work, and the original table-rendering expectation
for Writer below is superseded. Delimiter preservation, `sep=` handling and TSV
are deferred. See the [decision record](../notes/roadmap-decisions.md).

## Context

Every codec here reads into and writes from one model, `RichTextDocument`, and
`DocumentCodec` is the only contract a format implements (ADRs 0002 and 0003).
The request was to add spreadsheet formats, starting with CSV.

A spreadsheet is a grid of values, and the model already has a grid:
`DocumentTable` arranges runs of body paragraphs into rows and cells, and DOCX,
ODT and PDF import already fill it. What a spreadsheet has that a table does not
is everything beyond the text of a cell: more than one sheet, typed values
(numbers, dates, booleans), formulas with their cached results, number formats,
and a grid addressed by column letter and row number rather than by paragraph.

CSV has none of that. It is text in records and fields, and no more.

There is also a spreadsheet application in the platform. Broiler.Cells keeps a
sheet as a bounded array of strings, and its roadmap puts file storage, import
and export out of scope. So there is a likely future consumer that would want
values rather than paragraphs, and it has not asked for anything yet.

Three shapes were possible:

1. **Map spreadsheet formats onto the rich-text model's tables.** CSV becomes one
   more `DocumentCodec`, and every host that composes a catalog gets it.
2. **A spreadsheet model and a second contract.** A workbook of sheets of typed
   cells in `Broiler.Documents.Model`, with its own codec base and catalog.
3. **A record API outside the contract.** The CSV package exposes rows of strings
   and leaves the model alone.

## Decision

- **CSV is a `DocumentCodec`.** A file reads as one `DocumentTable` over the whole
  body: a row per record, a cell per field, a paragraph per line of a field.
  Values stay text exactly as written; nothing is parsed as a number or date and
  nothing that looks like a formula is evaluated.
- **Writing walks the body in order.** A table is written as its grid. Any other
  paragraph becomes a record of one field, so saving a document that is not a
  table loses no text. Everything CSV cannot hold - formatting, pictures,
  merges, nested grids, headers and footers, metadata - is reported by code.
- **No spreadsheet model now.** The promotion rule in
  [the architecture notes](../notes/architecture.md#document-model-rules) applies
  to a model as much as to a feature: CSV needs nothing a table lacks, and a model
  designed for XLSX and ODS before either codec exists would be designed from
  guesses.
- **Option 3 is not taken either.** A public record API would be a second surface
  with no consumer, and the parser behind it stays internal until one exists.
- **Revisit when either of these happens:** a reader for a spreadsheet format
  with typed values and formulas (XLSX or ODS), or a spreadsheet host such as
  Broiler.Cells asking this component for file storage. Either one names the
  consumer Option 2 needs. This codec would then gain a second read path into
  that model, and the rich-text mapping would stay for word-processing hosts and
  the command line.

## Consequences

- The Writer and the CLI get CSV through their catalogs, with no change beyond
  composing the codec. `convert`, `roundtrip` and `render` work on CSV, and a CSV
  table survives DOCX and ODT. RTF, HTML and Markdown flatten it, as they flatten
  every table today.
- A spreadsheet host reading through this codec gets cells as paragraphs and
  values as text, and needs an adapter. That is the cost of not building a model
  ahead of its consumer, and it is the first thing a revisit would remove.
- A CSV round trip keeps the grid and its text exactly, and the writer's diagnostics
  say what any other source document lost on the way into CSV.
- A further spreadsheet format added under this decision would read each sheet as
  a table and its cached values as text, and report formulas and number formats
  as dropped. That stretches the decision as far as it goes. A format whose value
  is mostly in those things is the trigger above, not an exception to it.
