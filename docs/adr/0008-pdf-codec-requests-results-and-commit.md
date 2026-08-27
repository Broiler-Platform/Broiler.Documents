# ADR 0008: PDF Codec Requests, Results, And Commit Semantics

**Status:** Accepted for PDF Phase 0; supersedes conflicting portions of ADR 0003 and ADR 0004
**Date:** 2026-08-22

## Context

PDF decoding can discover encryption, malformed structures, resource-policy
violations, or recoverable page errors after parsing has begun. A result that
always contains a document, a boolean embedded-resource switch, and a synchronous
stream-only contract cannot express those outcomes safely.

## Decision

- New codec evolution uses typed request objects. Input is represented by a
  `DocumentInput` abstraction able to describe owned bytes, streams, and future
  random-access sources without format-specific overload growth.
- Read results have an explicit disposition: `Success`, `Partial`, or `Rejected`.
  A rejected result contains no usable document. A partial result identifies the
  committed content and diagnostics; it must not expose speculative mutations.
- Parsing is transactional. Content is accumulated outside the caller-visible
  document and is committed only at documented boundaries. Discovery of a fatal
  condition before commit produces `Rejected`; after commit it may produce
  `Partial` only where the request policy permits partial results.
- Resource-bearing reads and writes require an explicit resource context and
  limits. Legacy no-context entry points reject operations requiring external or
  embedded resources; they do not silently fetch, drop, or decode them.
- Cancellation and asynchronous I/O are part of the evolved contract. Synchronous
  convenience APIs may wrap completed in-memory operations but are not the sole
  extensibility surface.
- Probing remains bounded and non-destructive. Selection never requires full PDF
  parsing and never consumes an unseekable input without preserving the probed
  prefix for the selected codec.
- Diagnostics are stable machine-readable codes plus privacy-safe context. They
  do not include document text, URLs, keys, passwords, raw metadata, or embedded
  payloads by default.

## Consequences

- ADR 0003's “result always has a document,” base embedded-object boolean, and
  sync-first constraints do not apply to the PDF contract or future shared API
  evolution.
- ADR 0004's opt-in boolean is replaced, for new resource-bearing paths, by an
  explicit capability context with independent byte/count/depth/time limits.
- Phase 1 must freeze names, ownership, and compatibility adapters before public
  implementation is added.

