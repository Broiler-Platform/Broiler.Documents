# ADR 0010: PDF Pagination, Units, Fonts, Scripts, And Platforms

**Status:** Accepted for PDF Phase 0
**Date:** 2026-08-22

## Context

PDF is a fixed-page format while `RichTextDocument` is logical. Export therefore
needs explicit page geometry, line layout, font provisioning, and deterministic
platform behavior rather than relying on a UI renderer.

## Decision

- Public page geometry uses PDF points: 72 points per inch. Conversions from
  pixels or device-independent units require an explicit scale; no ambient DPI is
  assumed.
- The initial default text size is 12 points. Page size, margins, direction, and
  pagination policy are request data and become deterministic test inputs.
- Pagination is a reusable document-layout responsibility. PDF serialization
  consumes a fixed-page artifact; it does not own general line breaking,
  measurement, page breaking, or widow/orphan policy.
- Fonts are supplied by the caller or an explicitly composed provider. The core
  package ships no fallback font until redistribution, embedding-rights, size,
  and platform behavior are separately approved.
- Version 1's committed script envelope is Latin, Greek, and Cyrillic with the
  exact coverage recorded in the feature matrix. Complex scripts, bidirectional
  shaping, vertical writing, emoji sequences, and advanced OpenType layout remain
  gated until a neutral shaping capability and conformance corpus exist.
- Deterministic output depends on font bytes and layout options, not installed
  system fonts. The same inputs must produce stable page geometry across supported
  runtimes.
- Initial candidate validation targets CLI, Windows, and Linux. Android and Web
  Assembly remain gated by trimming/AOT, memory, font-provisioning, and runtime
  tests. No platform is claimed merely because it can compile the project.

## Consequences

- Phase 1 must assign the fixed-page artifact and pagination contracts to neutral
  assemblies before the writer uses them.
- Unsupported scripts produce explicit diagnostics or rejection according to the
  request policy; they are not silently emitted with incorrect glyphs.
- Platform support is advanced only by matrix evidence.

