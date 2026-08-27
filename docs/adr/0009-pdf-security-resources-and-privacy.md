# ADR 0009: PDF Security, Resources, And Privacy

**Status:** Accepted for PDF Phase 0
**Date:** 2026-08-22

## Context

PDF can contain active actions, file specifications, external references,
embedded files, encrypted objects, metadata, and intentionally expensive object
graphs. Safe behavior must be fixed before a parser exists.

## Decision

- The default resource policy is deny. The codec never performs network access,
  filesystem access, process execution, dynamic code loading, or UI interaction.
- URI values are inert data. No action is executed. JavaScript, launch actions,
  remote go-to actions, submit/import actions, multimedia, and embedded-file
  activation are unsupported and diagnosed without dereferencing their targets.
- Input is rejected as soon as the effective trailer reveals `/Encrypt`. Password
  handling and decryption are not Version 1 features. The parser does not attempt
  object-stream recovery to bypass that decision.
- All object, stream, filter, page, resource, recursion, decompression, image,
  glyph, and diagnostic counts have checked limits. Nested decoding consumes a
  shared budget so composing filters cannot multiply the allowed work.
- Resource acquisition occurs only through a caller-supplied context with
  explicit capabilities and limits. Resources are immutable or have documented
  ownership; returned buffers and streams cannot outlive their declared scope.
- Semantic extraction and visible appearance are distinct. Removing metadata or
  hidden logical content is not redaction. Version 1 makes no redaction or secure
  sanitization claim.
- Raw XMP and unknown metadata are not copied through Version 1 import/export.
  Only an allowlisted, normalized metadata model may be retained. Diagnostics and
  telemetry are content-free by default.
- Malformed cross-reference recovery, if introduced, remains bounded and cannot
  weaken encryption detection, resource policy, or object-count limits.

## Consequences

- Security-policy tests precede broad format coverage.
- A future encryption, attachment, sanitization, or redaction feature requires a
  separate ADR and threat-model update.
- “Converted” never means “safe to disclose” or “securely redacted.”

