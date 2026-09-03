# ADR 0013: Standards, IP, Provenance, And Claims Beyond PDF

**Status:** Proposed; awaiting the project reviewer's adoption
**Date:** 2026-09-03

## Context

[ADR 0011](0011-pdf-standards-ip-provenance-and-claims.md) built this component's
standards, IP, provenance, and claims discipline, and built all of it for PDF.
Every artifact it names is PDF-scoped: the register, the feature matrix, the
construct inventory, the approved label set, and the guard tests that bind them
to the code.

The other five codecs have no such paperwork at all. That was defensible while
PDF was the only format with a rights question anyone had written down, and it
stopped being defensible when the component roadmap put one in writing for ODT:
`Broiler.Documents.Odt` is implemented from the published OASIS specification,
ODF is standardized under a royalty-free OASIS mode, and none of that is a
cleared position or a register row. RTF, DOCX, HTML, and Markdown are in the same
state and are not addressed here.

Two further things changed after ADR 0011 was written.

The register's **standard of review** changed. ADR 0011 required qualified legal
review before implementation clearance. On 2026-09-02 the project reviewer
recorded that the standard actually applied throughout was evidence-based
acceptance by an engineer reading published evidence, with no legal review
claimed, and the register now says so at the top. ADR 0011's requirement had
coexisted with thirteen rows marked `Approved` without counsel, which was a
contradiction rather than a control.

The **claim surface** changed. The Writer registers the ODT codec on every head
for opening and saving as of Broiler-Platform/Broiler.Writer#65. A format that
ships is a format something will eventually say something about.

## Decision

- The ADR 0011 discipline is **component-wide**, not PDF-specific: standards
  acquisition, patent evidence, implementation provenance, source records,
  approved wording, and a written decision per row apply to every format this
  component implements.
- The **standard of review is the one the register set on 2026-09-02** —
  evidence-based acceptance, no legal review claimed — and it applies to every
  format. This supersedes ADR 0011's "qualified legal review required before
  implementation clearance" for the whole component. Counsel remains available
  for any row where the evidence looks thin, and such a row stays open and says
  so rather than being closed by whoever is nearest.
- **Each format gets its own register file, its own ID prefix, and its own claim
  gate.** ODT's is [the ODT IP and licensing register](../odt-ip-licensing-register.md),
  using `ODT-IP-` and `ODT-SRC-` prefixes. A register is authoritative for its
  own format's claims and for nothing else.
- One format's pending row **must not** gate another format's claims. This is why
  the registers are separate files rather than sections of one: the PDF guard
  tests read their register whole and refuse a `Supported` matrix entry while any
  row in it is pending, so an ODT row in that file would have silenced PDF's
  matrix for a question about ODF.
- **Approval authority is unchanged and is a person.** An engineer, or an agent,
  may assemble an evidence record and write a row to the point where only the
  decision is missing. Recording the decision is the project reviewer's, and a
  row whose evidence is complete is `Pending` until he takes it — not `Approved`
  by whoever gathered the evidence.
- A format's claims are bound to its code **mechanically**, on the PDF pattern:
  every diagnostic code the codec declares is described in the format's public
  documentation, the register names only codes that exist, the shipped package
  description makes no claim the register declines, and the format is named as
  its approved label set says. Prose-only claims rules are rules waiting to be
  broken by whoever writes the next description field.
- Absence of paperwork is **not** a clearance. RTF, DOCX, HTML, and Markdown have
  no register and therefore no cleared position; they have an unasked question,
  and this ADR does not answer it for them.

## Consequences

- ODT's rows can be written, evidenced, and guarded now, and the format's
  claimable wording is bounded now, without waiting on a decision that is not
  the author's to take.
- Shipping a codec is not gated on its register. Claiming things about it is.
  ODT ships with a pending rights row, makes no conformance or certification
  claim, and its documentation stops where the register stops.
- Each further format that acquires a register acquires a guard suite with it.
  The cost is per-format and paid once.
- The two ADRs are read together: 0011 for what a rights row must contain, 0013
  for its scope, its standard of review, and where it lives.
