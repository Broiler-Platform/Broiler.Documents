# Broiler.Documents ADR Index

ADRs 0001-0005 define the document-format component, model ownership, codec
contract, safety policy, and first RTF subset. The model-placement decision is
mirrored on the UI side by
`Broiler.UI/docs/adr/0018-richedit-document-model-promotion.md`. ADR 0006 freezes
the model-side Formatting Codes projection, grammar, mapping, and edit scope.
ADRs 0007-0011 establish the PDF Phase 0 architecture, API evolution, safety,
pagination, and compliance-governance decisions. ADR 0008 supersedes the
conflicting result, embedded-resource, and sync-first portions of ADRs 0003 and
0004 for the evolved contract. ADR 0012 fixes the base/extension split the
implementation is built on: which part of PDF the repository implements itself,
and the order in which any further technology may be added.
Accepted and partially superseded records remain here for traceability; current
follow-up work is in [the component roadmap](../roadmap.md).

| ADR | Topic |
|---|---|
| [0001](0001-component-topology-and-consumption-policy.md) | Component topology and consumption policy |
| [0002](0002-document-model-ownership-and-promotion.md) | Document model ownership and promotion (Path A) |
| [0003](0003-codec-contract-and-signature-probe.md) | Codec contract and signature probe |
| [0004](0004-document-read-limits-and-rtf-sanitization.md) | Document read limits and RTF sanitization policy |
| [0005](0005-rtf-first-release-subset-and-text-encoding.md) | RTF first-release subset and text encoding |
| [0006](0006-formatting-codes-projection-and-grammar.md) | Formatting Codes projection and grammar |
| [0007](0007-pdf-component-scope-and-delivery.md) | PDF component scope and delivery |
| [0008](0008-pdf-codec-requests-results-and-commit.md) | PDF codec requests, results, and commit semantics |
| [0009](0009-pdf-security-resources-and-privacy.md) | PDF security, resources, and privacy |
| [0010](0010-pdf-pagination-units-fonts-and-platforms.md) | PDF pagination, units, fonts, scripts, and platforms |
| [0011](0011-pdf-standards-ip-provenance-and-claims.md) | PDF standards, IP, provenance, and claims (proposed; legal review pending) |
| [0012](0012-pdf-base-implementation-and-composed-extensions.md) | PDF base implementation scope and composed extensions |
