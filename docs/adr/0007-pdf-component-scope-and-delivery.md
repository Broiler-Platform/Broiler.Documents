# ADR 0007: PDF Component Scope And Delivery

**Status:** Accepted for PDF Phase 0
**Date:** 2026-08-22

## Context

PDF support previously existed as a separate application invoked by the CLI. That
code is no longer a supported architecture and cannot define the new component.
The new work must follow the explicit-composition rules in ADR 0001 and keep
format-neutral capabilities in their owning Broiler components.

## Decision

- The deliverable is `Broiler.Documents.Pdf`, an in-process document codec
  package with no dependency on a standalone executable or environment-variable
  discovery.
- PDF import and PDF export are separate capabilities. Import reconstructs a
  logical `RichTextDocument`; export paginates that logical document. The codec
  does not promise layout-preserving or byte-preserving round trips.
- PDF-specific syntax, object models, cross-reference processing, filters,
  security handlers, operators, and serialization remain internal to
  `Broiler.Documents.Pdf`.
- Reusable primitives belong in their neutral owners: drawing and geometry in
  `Broiler.Graphics`, image/container decoding in `Broiler.Media.Image`, document
  semantics in `Broiler.Documents.Model`, and reusable pagination abstractions
  in `Broiler.Documents` or a neutral pagination component selected before use.
  Shared assemblies must not acquire PDF-prefixed public types.
- Composition is explicit. Applications construct catalogs and pass dependencies;
  there is no mutable global registry, module initializer, service locator, UI
  dependency, DOM dependency, or platform-specific backend in the core codec.
- Reader internals remain non-public through Phase 4. Test-facing candidates may
  be proposed in Phase 5. A prerelease package exposes read support first; write
  support follows after pagination and interoperability gates.
- Version 1 emits ordinary, untagged PDF. Tagged PDF, PDF/UA, PDF/A, signatures,
  incremental updates, forms, JavaScript, multimedia, and complex-script shaping
  are out of scope until separate roadmap decisions and evidence gates approve
  them.

## Consequences

- The obsolete external-process CLI path and its tests are removed in Phase 0.
- Any missing shared capability is designed and tested in its owning component,
  rather than hidden behind PDF-specific wrappers.
- Package and platform claims are feature-matrix entries, not implied by the
  existence of the project.

