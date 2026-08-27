# ADR 0011: PDF Standards, IP, Provenance, And Claims

**Status:** Proposed; qualified legal review required before implementation clearance
**Date:** 2026-08-22

## Context

Implementing PDF touches copyrighted standards text, patent declarations,
codec-specific intellectual property, trademark/conformance wording, fonts, and
third-party test material. Engineering cannot infer worldwide freedom to operate
from public availability or from a license covering only one specification.

## Decision

- The versioned IP/licensing register is the authoritative inventory of standards,
  codecs, fonts, metadata technologies, encryption, signatures, sample files,
  licenses, patent declarations, jurisdictions, evidence, reviewer, and decision.
- Every implemented feature maps to a register entry and approved source. Unknown,
  expired, or incomplete review is a blocking state, not implicit approval.
- Adobe's ISO 32000-1 public patent license is evaluated only for its stated
  specification and conditions. It is not treated as clearance for ISO 32000-2,
  extensions, XMP, JPEG, fonts, encryption, signatures, or third-party claims.
- Standards documents are obtained and used lawfully. Public summaries, patent
  databases, or declarations do not authorize copying copyrighted standards.
- Repository implementation is clean-room with respect to unapproved legacy or
  third-party code. Authors record the sources consulted and similarity review.
  Old Broiler PDF binaries, source, fixtures, generated tables, and golden files
  are not imported unless separately inventoried and approved.
- User-supplied content, fonts, and samples remain subject to their own rights.
  The API and documentation must not imply that Broiler grants those rights.
- Claims such as “PDF compliant,” “PDF 2.0,” “PDF/A,” “PDF/UA,” or use of
  certification marks require an approved conformance plan and evidence. Product
  documentation includes appropriate non-endorsement wording.

## Consequences

- Phase 0 engineering may create architecture, governance, and empty-corpus
  scaffolding, but feature implementation cannot pass the exit gate until the
  relevant register rows have qualified approval.
- Patent databases are leads for counsel, not evidence of no relevant rights.
- New filters, profiles, metadata, font technology, encryption, or signature work
  reopens the register and claim review before code is merged.

