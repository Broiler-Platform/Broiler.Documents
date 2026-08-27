# Broiler.Documents

Broiler.Documents is Broiler's document-format codec component. It reads and
writes rich-text interchange formats to and from the UI-free
`Broiler.Documents.Model` rich-text document model.

The component mirrors the `Broiler.Media` pattern: applications explicitly
compose a `DocumentCodecCatalog`, codecs probe bounded byte prefixes, and reads
return best-effort `RichTextDocument` values plus diagnostics for skipped or
approximated constructs. There is no hidden global codec registration.

## Runtime Projects

- `Broiler.Documents.Model` - platform-neutral rich-text document model,
  promoted out of `Broiler.UI.RichEdit`; depends only on `Broiler.Graphics`.
- `Broiler.Documents.FormatCodes` - deterministic, versioned Formatting Codes
  projection, typed tokens, source mappings, diagnostics, and resource policy;
  references only `Broiler.Documents.Model`.
- `Broiler.Documents` - codec contract, catalog, descriptors, source hints,
  read/write options, limits, diagnostics, and probe results.
- `Broiler.Documents.Rtf` - RTF reader/writer for the documented first-release
  subset.
- `Broiler.Documents.Docx` - DOCX reader/writer for a safe Open XML
  WordprocessingML subset.
- `Broiler.Documents.Html` - HTML document/fragment codec over
  `Broiler.Dom` and `Broiler.Dom.Html`.
- `Broiler.Documents.Markdown` - Markdown codec for a safe CommonMark-oriented
  subset.
- `Broiler.Documents.Pdf` - base PDF codec: logical text import from ISO 32000-1
  files and a deterministic PDF 1.7 writer. Built only from what this repository
  implements itself, with every remaining PDF technology detected, skipped, and
  reachable through a composed extension point. Not packed and not registered in
  any application; see the
  [PDF support roadmap](docs/pdf-support-roadmap.md) and
  [PDF extension points](docs/pdf-extension-points.md).

Matching headless test projects live beside each runtime project.

## Component Constraints

- Target .NET 10 only.
- Do not add third-party runtime dependencies.
- Keep abstraction assemblies platform-neutral, safe-code compatible,
  trimming-friendly, and AOT-friendly.
- Put any OS-dependent code in OS-specific implementation projects only.
- Do not add hidden global codec registration or module-initializer side effects.
- `Broiler.Documents.Model` and `Broiler.Documents` must not reference
  `Broiler.UI`, `Broiler.DOM`, `Broiler.Input`, or any `*.Windows` assembly.
- Format codecs may depend on their format engines: the HTML codec references
  `Broiler.Dom` and `Broiler.Dom.Html`; RTF and DOCX have no DOM/UI dependency.

## Supported Subsets

- [RTF conformance](docs/rtf-conformance.md)
- [DOCX conformance](docs/docx-conformance.md)
- [HTML conformance](docs/html-conformance.md)
- [Markdown conformance](docs/markdown-conformance.md)
- [PDF feature matrix](docs/pdf-feature-matrix.md) - the authority for what the
  PDF codec does today and what it may be described as.
- [PDF construct inventory](docs/pdf-construct-inventory.md) - exactly which PDF
  constructs the codec reads, writes, recognizes without interpreting, and
  rejects, derived from the implementation.
- [Formatting Codes grammar version 1](Broiler.Documents.FormatCodes/GRAMMAR.md)

## Records

- [Current roadmap](docs/roadmap.md)
- [ADR Index](docs/adr/README.md)
  - [ADR 0001: Component Topology And Consumption Policy](docs/adr/0001-component-topology-and-consumption-policy.md)
  - [ADR 0002: Document Model Ownership And Promotion (Path A)](docs/adr/0002-document-model-ownership-and-promotion.md)
  - [ADR 0003: Codec Contract And Signature Probe](docs/adr/0003-codec-contract-and-signature-probe.md)
  - [ADR 0004: Document Read Limits And RTF Sanitization Policy](docs/adr/0004-document-read-limits-and-rtf-sanitization.md)
  - [ADR 0005: RTF First-Release Subset And Text Encoding](docs/adr/0005-rtf-first-release-subset-and-text-encoding.md)
  - [ADR 0006: Formatting Codes Projection And Grammar](docs/adr/0006-formatting-codes-projection-and-grammar.md)
  - [ADR 0012: PDF Base Implementation Scope And Composed Extensions](docs/adr/0012-pdf-base-implementation-and-composed-extensions.md)
