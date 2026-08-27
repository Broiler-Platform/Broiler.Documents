# ADR 0001: Component Topology And Consumption Policy

**Status:** Accepted for Phase 1 scaffolding
**Date:** 2026-07-05

## Context

The document-formats proposal that established this component covers reading and
writing rich-text interchange formats — RTF first, then
HTML/Markdown/DOCX — in Broiler. A document format is not new *data*: its decoded
form is the rich-text document model that RichEdit Phases 1-4 already built
(`RichTextDocument`, `RichTextParagraph`, `InlineStyle`, `ParagraphStyle`,
`StyleRun`). The right shape is therefore a *codec* layer mapping serialized
formats to and from that model, mirroring how `Broiler.Media` decodes binary
media formats to and from typed buffers.

Two existing homes were evaluated and rejected as **homes** (see roadmap §2):
`Broiler.Media` is a decode-first binary-media component (a document is not a
`MediaKind`), and `Broiler.DOM` is an HTML/XML tree (RTF is not a DOM tree). The
former's catalog architecture is nonetheless the right template to reuse, and the
latter remains the dependency of the future HTML *codec*, not of RTF.

Phase 0 must fix the component name, the assembly set, the dependency direction,
the third-party-dependency and platform-neutrality rules, and the registration
policy before any codec or model-move code is written.

## Decision

Create `Broiler.Documents` as one root component in the aggregate workspace,
consumed the same way `Broiler.Media` is.

- **Assembly set (first runtime target).**

  - `Broiler.Documents.Model` — the platform-neutral rich-text document model,
    promoted out of `Broiler.UI.RichEdit` (ADR 0002). Depends only on
    `Broiler.Graphics` (for `BColor`).
  - `Broiler.Documents` — the codec contract, catalog, signature probe, format
    descriptors, read/write options, limits, and diagnostics (ADR 0003).
  - `Broiler.Documents.Rtf` — the first codec (RTF reader + writer).

  Later phases add peer codecs — `Broiler.Documents.Html` (references
  `Broiler.DOM` / `Broiler.Dom.Html`) and optionally `Broiler.Documents.Markdown`
  — without reshaping the catalog. Phase 0 adds no runtime assembly yet.

- **Dependency direction (approved).**

  ```text
  Broiler.Documents.Model -> Broiler.Graphics
  Broiler.Documents       -> Broiler.Documents.Model
  Broiler.Documents.Rtf   -> Broiler.Documents, Broiler.Documents.Model
  Broiler.Documents.Html  -> Broiler.Documents, Broiler.Documents.Model,
                             Broiler.DOM, Broiler.Dom.Html   (later)
  Broiler.UI.RichEdit     -> Broiler.Documents.Model          (consumer; ADR 0002)
  ```

  `Broiler.Documents.Model` and `Broiler.Documents` must not reference
  `Broiler.UI`, `Broiler.DOM`, `Broiler.Input`, or any `*.Windows` assembly. DOM
  enters only the HTML codec.

- **Component constraints (mirroring `Broiler.Media`).** Target .NET 10 only. No
  third-party runtime dependencies. Keep abstraction assemblies platform-neutral,
  safe-code-compatible, trimming-friendly, and AOT-friendly. Put any OS-dependent
  code in OS-specific implementation projects only (none is expected for RTF). Do
  **not** add hidden global codec registration or module-initializer side
  effects — codecs are registered explicitly into a `DocumentCodecCatalog`
  (ADR 0003).

- **Consumption and single-checkout policy.** During aggregate development, all
  local project references to Documents point to this single root checkout. No
  component may create an independent editable copy of `Broiler.Documents`.
  Standalone downstream components consume versioned packages once packages exist.

## Consequences

- Phase 1 can scaffold `Broiler.Documents.Model` and `Broiler.Documents` under
  this component and perform the ADR 0002 model move without cutting other
  consumers over.
- RTF (and later HTML/Markdown) are usable headlessly — a CLI converter, server,
  clipboard, or print path can reference `Broiler.Documents.*` without pulling in
  `Broiler.UI`.
- Dependency architecture tests should reject a `Broiler.UI`/DOM/`*.Windows`
  reference in `Broiler.Documents.Model` or `Broiler.Documents`, and reject any
  hidden global codec registration.
- This ADR satisfies the Phase 0 exit-gate item "component dependency graph is
  approved."
