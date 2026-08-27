# ADR 0002: Document Model Ownership And Promotion (Path A)

**Status:** Accepted for Phase 1 scaffolding
**Date:** 2026-07-05

## Context

The rich-text document model built by RichEdit Phases 1-4 currently lives inside
the `Broiler.UI.RichEdit` assembly, alongside the `UiRichEdit : UiElement`
control. The model types themselves use only `Broiler.Graphics` (for `BColor`);
they do **not** use any `Broiler.UI` type. They are packaged in a UI assembly for
historical reasons (RichEdit ADR 0013 placed the whole family there), not because
of a real dependency.

Document formats (RTF first) decode to and encode from exactly this model. RTF
import/export is a headless concern — CLI converters, servers, the clipboard, and
print pipelines should not have to reference `Broiler.UI` to parse a `.rtf` file.
The roadmap (§9 Phase 0) framed this as a fork:

- **Path A — Promote** the pure model into a UI-free `Broiler.Documents.Model`.
- **Path B — Adapter** — keep the model in `Broiler.UI.RichEdit` and ship RTF as
  a UI-side adapter (`Broiler.UI.RichEdit.Rtf`).

**Path A is chosen.** It gives one canonical model (no parallel DTO to drift),
frees every current and future codec from a UI dependency, and — because
RichEdit Phase 5 ("DOM/HTML adapter") is not yet built — lets HTML land as a peer
codec (`Broiler.Documents.Html`) instead of a UI-side adapter that would later
need moving.

## Decision

- **Promote the pure model.** The following types move from
  `Broiler.UI.RichEdit` into the new `Broiler.Documents.Model` assembly
  (namespace `Broiler.Documents.Model`), which depends only on
  `Broiler.Graphics`:

  `RichTextDocument`, `RichTextParagraph`, `InlineStyle`, `InlineStyleDelta`,
  `ParagraphStyle`, `ParagraphStyleDelta`, `StyleRun`, `RichTextPosition`,
  `RichTextRange`, `RichTextOperation`, `RichTextTransaction`,
  `RichTextEditResult`, `RichTextEditor`, `ListKind`, `TextAlignment`.

- **The control stays in UI.** `UiRichEdit`, `RichEditCommand`,
  `RichEditCommandState`, the event-args types, `RichEditScrollPolicy`, and the
  other control-surface types remain in `Broiler.UI.RichEdit`, which now
  references `Broiler.Documents.Model` instead of owning it. The public control
  API is unchanged.

- **Design is preserved, only placement changes.** The promotion is a *move*, not
  a redesign. The ADR 0014 guarantees carry over verbatim: immutable/copy-on-write
  snapshots; opaque, stable `RichTextPosition`/`RichTextRange` (no public raw
  indexes; no UTF-16 movement promise); transaction undo/redo; and the
  first-release inline/paragraph style subset. The internal ctor and
  `ParagraphIndex`/`Offset` of `RichTextPosition` stay `internal`.

- **`InternalsVisibleTo` retargets to the model assembly.** The Standard renderer
  legitimately reads the internal position indexing for layout, hit-testing, and
  caret geometry. `Broiler.Documents.Model` therefore carries
  `InternalsVisibleTo` for `Broiler.UI.RichEdit.Standard` and the model's own
  test assembly. Codecs (`Broiler.Documents.Rtf`) build documents through the
  **public** navigation/edit API and do not require internal access; if a codec
  ever needs to emit positions in bulk, it is granted `InternalsVisibleTo`
  explicitly rather than by widening the public surface.

- **Namespace.** Promoted types adopt the `Broiler.Documents.Model` namespace. A
  compatibility decision (type-forwarding shims vs. a hard namespace change with
  updated `using`s in `Broiler.UI.RichEdit`/`.Standard`) is a Phase 1
  implementation detail; either way the *public* control API and behavior are
  preserved and the 129 existing RichEdit tests (74 + 55) must stay green.

- **Supersedes placement only.** This ADR supersedes the assembly *placement* of
  the model in RichEdit ADR 0013 (§Assemblies) and ADR 0014, and is mirrored by
  `Broiler.UI` ADR 0018. It does **not** change the model design, the command
  model (ADR 0015), the clipboard/sanitization policy (ADR 0016), or the
  accessibility semantics (ADR 0017).

## Consequences

- RTF, HTML, and Markdown codecs depend on `Broiler.Documents.Model`, never on
  `Broiler.UI`.
- RichEdit Phase 5's HTML work is re-pointed at `Broiler.Documents.Html`; the
  planned `Broiler.UI.RichEdit.Dom` assembly is not created.
- Phase 1 is a mechanical move guarded by the existing RichEdit test suite as a
  regression baseline (129/129 green as of 2026-07-05); a green run before and
  after the move is the Phase 1 acceptance evidence for the promotion.
- Architecture tests must assert `Broiler.Documents.Model` references only
  `Broiler.Graphics` and that `Broiler.UI.RichEdit` still exposes the same public
  control surface.
- This ADR satisfies the Phase 0 exit-gate item "model-placement path chosen and
  recorded."
