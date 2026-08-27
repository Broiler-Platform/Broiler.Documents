# ADR 0004: Document Read Limits And RTF Sanitization Policy

**Status:** Accepted for Phase 1 scaffolding; revisit before API freeze
**Date:** 2026-07-05

> **Implementation note:** The default skip policy is implemented. The public
> `DecodeEmbeddedObjects` option is not yet connected to an image-import path;
> resolving or removing that misleading option is tracked in
> [the current roadmap](../roadmap.md).

## Context

RTF is a historically heavy exploit and denial-of-service surface: deeply nested
groups overflow recursive parsers, `\binN` and `\uN` floods exhaust memory,
`\object`/`\*\objdata` carry OLE payloads, `\pict` carries images that have fed
downstream decoder exploits, and fields such as `INCLUDEPICTURE`/`HYPERLINK` and
remote templates invite phone-home/SSRF. Documents codecs consume untrusted input
and must be safe by default. This mirrors `Broiler.Media` ADR 0002 (buffer
ownership and limits) and RichEdit ADR 0016 (sanitize, then fall back).

## Decision

- **Hard limits on every read.** `DocumentReadOptions` carries a
  `DocumentLimits` record enforced by the tokenizer/reader:

  | Limit | Guards against |
  |---|---|
  | `MaxProbeBytes` | unbounded sniffing (ADR 0003) |
  | `MaxDocumentBytes` | oversized input |
  | `MaxGroupDepth` | `{ … }` nesting bombs / stack overflow |
  | `MaxRunLength` / `MaxParagraphCount` | pathological single runs / paragraph floods |
  | `MaxBinBytes` | `\binN` resource exhaustion |

  Group nesting is handled iteratively or with an explicit depth guard, never by
  unbounded recursion. Size/stride/count arithmetic uses checked operations
  (Media ADR 0002).

- **Skip unknown destinations safely.** Unknown `\*\…` destinations are skipped
  as opaque groups. Destinations skipped by default include `\pict`, `\object` /
  `\objdata`, `\bin`, `\info`, `\*\datastore`, and any `\field` that is not a
  hyperlink. Skipping is content-neutral and bounded by the limits above.

- **Never fetch, never instantiate.** Reading RTF performs **no** network or file
  access (no `INCLUDEPICTURE`, no remote templates) and **never** instantiates,
  executes, or deserializes OLE objects, macros, or `\*\objdata` payloads.
  Hyperlink targets are stored as inert `LinkHref` text (ADR 0005); URL policy
  reuses RichEdit ADR 0016 — only `http`, `https`, and `mailto` survive; other
  schemes are dropped.

- **Embedded images are opt-in and delegated.** `\pict` decoding is **off by
  default**. When a caller opts in via `DocumentReadOptions`, the payload is
  routed through `Broiler.Media.Image` with *its own* limits — the RTF codec never
  decodes image bytes itself. This is the only sanctioned cross-component tie-in
  and is additive, never a default dependency of `Broiler.Documents.Rtf`.

- **Best-effort, total API.** Malformed input (truncated groups, bad control
  words, invalid hex, bad `\uc` counts) never throws across the API boundary. The
  reader returns a best-effort `RichTextDocument` plus `DocumentDiagnostic`
  entries (ADR 0003). Only limit-exceeded or hard I/O conditions fail the read.

- **Privacy.** Diagnostics must not log document text, clipboard payloads, or URL
  contents by default (RichEdit ADR 0016; Media privacy posture).

## Consequences

- Fuzzing the tokenizer and reader (random bytes, nesting bombs, huge parameters,
  truncation) is a first-class exit gate (roadmap Phase 4): no crash, no unbounded
  memory, no hang.
- The clipboard paste path (roadmap Phase 5) reuses this same policy before RTF
  becomes document operations, closing the "clipboard accepts unsafe content"
  risk the way ADR 0016 did for HTML.
- `Broiler.Documents.Rtf` has no mandatory dependency on `Broiler.Media`;
  image decode is a caller-enabled, limit-bounded delegation only.
- This ADR satisfies the Phase 0 exit-gate item "safety limits are explicit."
