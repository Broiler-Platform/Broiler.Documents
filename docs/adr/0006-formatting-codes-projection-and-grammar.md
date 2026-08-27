# ADR 0006 - Formatting Codes Projection and Grammar

**Status:** Approved for Formatting Codes Phase 1  
**Date:** 2026-07-12

## Context

The public roadmap proposes a bottom pane that exposes the semantic formatting
stored in `RichTextDocument` as readable text such as
`[Bold ON]Hello World![Bold OFF]`. The display must be deterministic, useful in
headless tests, and independent of the RichEdit renderer. It must also avoid
implying that imported document bytes, redundant source controls, or another
product's private file representation are preserved by Broiler.

`RichTextDocument` is an immutable, normalized model. Adjacent equal inline
runs are merged, paragraph boundaries are structural, and format readers may
discard redundant or unsupported source controls. Consequently a reversible
serialization of the original input is neither available nor desirable.

## Decision

### Source of truth and semantics

- `RichTextDocument` remains the sole persisted source of truth. Formatting
  Codes is a canonical semantic projection of a document snapshot, not a
  source-file view and not a second editor model.
- Equivalent normalized documents must produce byte-for-byte identical UTF-8
  projection text for the same grammar version and options.
- The projection reports effective Broiler model state. It does not promise to
  reproduce redundant RTF controls, unsupported import data, or the ordering of
  controls in an input file.
- A snapshot is projected atomically. Results computed from a stale snapshot
  may not replace results for a newer editor document.

### Grammar version 1

- Content is Unicode. Command names and invariant scalar values use a stable
  ASCII vocabulary inside square brackets.
- The signed-off canonical sample is exactly:

  ```text
  [Bold ON]Hello World![Bold OFF]
  ```

- Inline transitions are emitted immediately before the first affected content
  character. At paragraph end, active inline properties are closed in a fixed
  order before the paragraph boundary is emitted. Each new paragraph starts
  from the default inline state.
- Paragraph properties and document structure use distinct canonical commands;
  their complete version-1 vocabulary will be specified and exhaustively tested
  in the Phase 1 projector before public API stabilization.
- Literal `\`, `[`, `]`, tab, newline-like controls, and non-printing characters
  are escaped by the projector so content can never be mistaken for a command.
  Escape spelling is grammar, not localized presentation text.
- Numeric values use invariant culture. Colors use a fixed hexadecimal form.
  URLs and font-family values use a single canonical quoted-string escape rule.
- Projection output carries a grammar-version value in its data contract. The
  visible pane need not display a version banner. A grammar change that could
  alter existing output requires a new version and compatibility tests.

The final command table and close-order table are frozen by
[`Broiler.Documents.FormatCodes/GRAMMAR.md`](../../Broiler.Documents.FormatCodes/GRAMMAR.md).
They do not change the canonical sample above or the rules in this ADR.

### Mapping and assembly boundary

- The headless projector belongs in a new `Broiler.Documents.FormatCodes`
  assembly. It references `Broiler.Documents.Model` and has no UI, DOM, codec,
  clipboard, or platform dependency.
- Projection results contain typed tokens plus mappings from projected spans and
  zero-width boundaries to opaque document positions/ranges. Consumers do not
  recover model coordinates by parsing visible text.
- `Broiler.Documents.Model` may grant the projector a narrow
  `InternalsVisibleTo` entry for its existing opaque position representation.
  Raw paragraph indexes and offsets do not become general public API.
- Resource limits cover output expansion, token count, quoted-value length, and
  cancellation. The projector performs no network or file I/O and does not log
  document content by default.

### Editing scope

- The first release is read-only. Selection and caret synchronization do not
  mutate the document.
- Later structured actions may translate a selected token into existing
  `RichTextEditor` transactions. They must not introduce a second undo stack.
- A raw-source parser/editor is explicitly outside the MVP. If proposed later,
  it requires a separate ADR covering drafts, validation, loss behavior,
  security limits, and one-transaction commit semantics.

## Consequences

- The feature can be tested without constructing UI controls and can be shared
  by desktop and WebAssembly hosts.
- Users see normalized document meaning, not forensic source bytes. The pane
  must say so in its help and accessibility description.
- Grammar stability becomes a compatibility commitment; golden files are
  appropriate once version 1 is implemented.
- Imported constructs absent from `RichTextDocument` cannot appear in the pane.
- Read-only delivery is not blocked on the substantially riskier parser and raw
  editing design.

## Follow-up

Implemented: the complete version-1 grammar is published in
[`Broiler.Documents.FormatCodes/GRAMMAR.md`](../../Broiler.Documents.FormatCodes/GRAMMAR.md)
beside projector tests covering supported style fields, escapes, empty
paragraphs, Unicode, and mapping boundaries.
