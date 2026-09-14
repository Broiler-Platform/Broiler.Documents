# Broiler.Documents

[![CI](https://github.com/Broiler-Platform/Broiler.Documents/actions/workflows/ci.yml/badge.svg)](https://github.com/Broiler-Platform/Broiler.Documents/actions/workflows/ci.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/LICENSE)

Broiler.Documents is Broiler's document-format codec component. It reads and
writes rich-text interchange formats to and from the UI-free
`Broiler.Documents.Model` rich-text document model.

The component mirrors the `Broiler.Media` pattern: applications explicitly
compose a `DocumentCodecCatalog`, codecs probe bounded byte prefixes, and reads
return best-effort `RichTextDocument` values plus diagnostics for skipped or
approximated constructs. There is no hidden global codec registration.

> **Preview release.** `0.1.0-preview.1` is the first published preview. Public
> APIs and behaviour are not frozen and may change before `1.0`. The codecs parse
> untrusted input — RTF control words, Open XML and OpenDocument packages, HTML,
> and PDF object graphs — and must be treated as security-sensitive; no fuzzing campaign,
> dependency scan, or independent security audit is recorded for this revision.
> See the [roadmap](docs/roadmap.md) for what is still open.

## Installation

Preview packages need an explicit prerelease opt-in:

```bash
dotnet add package Broiler.Documents --prerelease
```

`Broiler.Documents` is the codec contract and catalog; it carries no format
support on its own. Add a package per format you actually read or write — none is
pulled in automatically, and there is no meta-package:

```bash
dotnet add package Broiler.Documents.Rtf --prerelease
```

### Consuming Broiler packages from GitHub Packages

`NuGet.config` in the repository root pins two sources — nuget.org and the
Broiler-Platform GitHub Packages feed — and clears whatever the machine has
configured. Package source mapping sends `Broiler.*` to GitHub Packages and
everything else to nuget.org. Versions are pinned in `Directory.Packages.props`.

That mapping is load-bearing. GitHub Packages requires authentication **even for
public packages** and answers `401` to an anonymous request, so an unmapped source
would be queried for every package and break the restore. This repository now
uses Broiler package dependencies, so a fresh restore needs feed credentials.

To actually pull `Broiler.*` from GitHub Packages you need a personal access token
with the `read:packages` scope. Put it in your **user-level** config, never in the
committed one:

```bash
dotnet nuget update source broiler-github --username <github-user> --password <pat> --store-password-in-clear-text --configfile "$APPDATA/NuGet/NuGet.Config"
```

In GitHub Actions the workflows supply `secrets.GITHUB_TOKEN` through
`NuGetPackageSourceCredentials_broiler-github`. The dependency packages must grant
this repository Actions read access; `packages: read` alone does not grant access
to packages owned by another repository.

## Packages

Eleven preview packages are generated. All target `net10.0`, ship XML
documentation and a `.snupkg` symbol package, and are built deterministically
with SourceLink.

| Package | Role |
| --- | --- |
| `Broiler.Documents.Model` | Platform-neutral rich-text document model, promoted out of `Broiler.UI.RichEdit`; depends only on `Broiler.Graphics`. |
| `Broiler.Documents.FormatCodes` | Deterministic, versioned Formatting Codes projection: typed tokens, source mappings, diagnostics, and resource policy. References only the model. |
| `Broiler.Documents` | Codec contract, catalog, descriptors, source hints, read/write options, limits, diagnostics, and probe results. |
| `Broiler.Documents.Rtf` | RTF reader/writer for the documented first-release subset. |
| `Broiler.Documents.Docx` | DOCX reader/writer for a safe Open XML WordprocessingML subset. |
| `Broiler.Documents.Odt` | ODT reader/writer for a safe OASIS OpenDocument text subset. |
| `Broiler.Documents.Html` | HTML document/fragment codec over `Broiler.Dom` and `Broiler.Dom.Html`. |
| `Broiler.Documents.Markdown` | Markdown codec for a safe CommonMark-oriented subset. |
| `Broiler.Documents.Pdf` | Preview PDF codec for logical text import and deterministic PDF 1.7 writing. |
| `Broiler.Documents.Pdf.Fonts` | Optional PDF font-program reader over `Broiler.Graphics`. |
| `Broiler.Documents.Pdf.Images` | Optional PDF image filters over the managed `Broiler.Media` image codecs. |

`Broiler.Documents.Pdf` is a **preview package**. It is a base PDF codec —
logical text import from ISO 32000-1 files and a deterministic PDF 1.7 writer,
built only from what this repository implements itself, with every remaining PDF
technology detected, skipped, and reachable through a composed extension point. It
and its optional font and image providers are included in the normal pack and
publish workflows. Package generation does not enable application features or
certify PDF conformance. Application integration retains the read-preview and
write-preview gates; see
the [PDF support roadmap](docs/pdf-support-roadmap.md) §4.1 and the
[PDF extension points](docs/pdf-extension-points.md).

### Dependency direction

```text
Broiler.Documents.Rtf      -> Broiler.Documents -> Broiler.Documents.Model -> Broiler.Graphics
Broiler.Documents.Docx     -> Broiler.Documents -> Broiler.Documents.Model
Broiler.Documents.Odt      -> Broiler.Documents -> Broiler.Documents.Model
Broiler.Documents.Markdown -> Broiler.Documents -> Broiler.Documents.Model
Broiler.Documents.Html     -> Broiler.Documents -> Broiler.Documents.Model
Broiler.Documents.Html     -> Broiler.Dom, Broiler.Dom.Html
Broiler.Documents.FormatCodes                   -> Broiler.Documents.Model
Broiler.Documents.Pdf      -> Broiler.Documents -> Broiler.Documents.Model
Broiler.Documents.Pdf.Fonts  -> Broiler.Documents.Pdf, Broiler.Graphics
Broiler.Documents.Pdf.Images -> Broiler.Documents.Pdf, Broiler.Media.Image, Broiler.Media.Image.Managed
```

`Broiler.Graphics`, `Broiler.Dom`, `Broiler.Dom.Html`, `Broiler.Media.Image`, and
`Broiler.Media.Image.Managed` are packaged by their own
repositories and appear as package dependencies at the versions pinned here, so
they must be on the feed a consumer restores from.

## Command line

`src/Broiler.Documents.Cli` is an application head over these codecs: it creates,
edits, converts, renders, and compares documents. It is built to be driven by an
automated test system as much as by a person — every command has a `--json` form,
and the exit code is a documented contract.

```bash
dotnet run --project src/Broiler.Documents.Cli -- --help
```

The two commands the rest of it exists to support are `roundtrip`, which writes a
document out and reads it straight back and reports what did not survive, and
`compare`, which puts two documents or two rendered pages side by side and says
precisely how they differ. Both are aimed at the same question — where are the
gaps in these codecs — and both answer it structurally first and in pixels only
when that is what you actually need to know.

```bash
broilerdoc roundtrip report.docx --via docx --via odt --via rtf --via html --via markdown
```

```bash
broilerdoc compare a.docx b.docx --render --continuous --diff diff.png --tolerance 2
```

The head does **not** compose `Broiler.Documents.Pdf`: CLI PDF integration still
has to pass the application delivery gates.
It is itself `IsPackable=false`, so the package table above is unchanged. See the
[CLI guide](docs/cli.md) for the full command reference, the exit codes, the edit
language, and what makes a render reproducible across machines.

CI drives that tool over a document corpus on every push on Linux and Windows,
and holds what comes back against a committed baseline of what each
format is expected to lose. See [the corpus suite](docs/corpus-suite.md) for what
it checks, where the documents come from and why none of them is downloaded, and
what it has found so far.

A second suite runs nightly rather than on every push, and answers the question
the first one cannot: whether this component reads documents an unrelated
implementation wrote. LibreOffice manufactures them from seeds authored here, and
both what they say and how they look are compared. See
[the office conformance suite](docs/office-conformance.md), which is also candid
about what a pixel comparison between two independent layout engines can and
cannot assert.

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
- `Broiler.Documents.Cli` is an application head, not an abstraction assembly, so
  the trimming and AOT rules above do not bind it. It composes its catalog in one
  readable place and takes the rendering dependencies — `Broiler.Graphics` for the
  software renderer and font faces, `Broiler.Media.Image.Managed` for the PNG
  encoder — that no library here may take. It still adds no third-party
  dependency.

Architecture tests in each test project enforce these constraints against the
project files themselves, so a stray reference fails the build rather than the
review.

## Repository layout

```text
src/                     runtime assemblies, one directory per package
src/Broiler.Documents.Cli/  the broilerdoc command line; an application head, not a package
src/tests/               one xUnit test project per assembly
eng/                     vendored packaging metadata and package icon
docs/                    roadmap, conformance documents, ADRs, PDF registers
.github/workflows/       CI and publish pipelines
Directory.Packages.props  centrally pinned Broiler and test package versions
Broiler.Documents.slnx   solution over every project in src/
```

Cross-component dependencies are NuGet packages. Project references remain inside
this repository; sibling checkouts and parent build properties are not required.

## Building and testing

Clone the repository, then configure feed credentials as described above:

```bash
git clone https://github.com/Broiler-Platform/Broiler.Documents.git
```

The solution defines six configurations. Every project here is platform-neutral
`net10.0`, so all six build the same projects; the `-Windows` and `-Linux`
variants exist to line this component up with the rest of the suite and map onto
plain `Debug`/`Release`.

```bash
dotnet build Broiler.Documents.slnx -c Release
```

```bash
dotnet test Broiler.Documents.slnx -c Release --no-build
```

Three PDF guards in `Broiler.Documents.Tests` assert on application heads
(`src/Broiler.Writer.*`), which live in the aggregate repository
rather than here. They report as **skipped** in a standalone checkout and run in
full when this component is checked out inside the aggregate.

## Packaging

Every project is platform-neutral, so one run produces the whole set:

```bash
pwsh -File eng/pack.ps1
```

The script checks package identities, internal dependency versions, documentation,
icons, and symbols, and rejects an output directory containing stale packages.
Test projects, the PDF codec and providers, and `Broiler.Documents.Cli` never pack.
The CLI is wired to pack as a .NET tool and is held at `IsPackable=false` so that
adding a command line does not silently change what a `v*` tag pushes to
nuget.org; [the CLI guide](docs/cli.md) says how to turn it on. `eng/Broiler.Packaging.props`
is a vendored copy of the suite-wide packaging metadata and holds the default
preview version. Publishing resolves an unused preview and passes it as an
MSBuild override without editing this file or changing dependency versions.

## Continuous integration and releases

`.github/workflows/ci.yml` builds and tests Release and runs the CLI corpus on
Ubuntu and Windows. It packs and verifies all eleven preview packages only on Ubuntu.
`eng/run-tests.ps1` also requires at least 1,400 executed tests and terminates a
test host if a test hangs for ten minutes. Synchronous tests use this runner
limit because xUnit's `Timeout` attribute only supports async tests. Unit and corpus
reports are uploaded even on failure. Dependency projects run their own suites in
their own repositories.

`.github/workflows/publish.yml` publishes. Run it manually to choose a feed (GitHub
Packages or nuget.org); it defaults to a dry run that packs and attaches the
packages without pushing. Like Broiler.DOM, publication selects the next unused
`X.Y.Z-preview.N` across all shipping packages, using the configured preview as a
floor. A manual suffix or `v*` tag must be unused and at least that next preview;
tags publish to nuget.org. The reusable CI workflow validates that version, and
publication downloads those exact artifacts instead of rebuilding. An isolated
consumer restore checks dependencies against the destination feed before any push,
including during dry runs. Publishing to nuget.org needs a `NUGET_API_KEY`
repository secret; GitHub Packages uses the built-in `GITHUB_TOKEN`. Runs are
serialized to avoid concurrent version selection.

## Supported Subsets

- [RTF conformance](docs/rtf-conformance.md)
- [DOCX conformance](docs/docx-conformance.md)
- [ODT conformance](docs/odt-conformance.md)
- [HTML conformance](docs/html-conformance.md)
- [Markdown conformance](docs/markdown-conformance.md)
- [PDF feature matrix](docs/pdf-feature-matrix.md) - the authority for what the
  PDF codec does today and what it may be described as.
- [PDF construct inventory](docs/pdf-construct-inventory.md) - exactly which PDF
  constructs the codec reads, writes, recognizes without interpreting, and
  rejects, derived from the implementation.
- [Formatting Codes grammar version 1](src/Broiler.Documents.FormatCodes/GRAMMAR.md)

## Records

- [Command-line guide](docs/cli.md)
- [Current roadmap](docs/roadmap.md)
- [PDF Phase 0 status](docs/pdf-phase0-status.md)
- [RTF IP and licensing register](docs/rtf-ip-licensing-register.md),
  [DOCX](docs/docx-ip-licensing-register.md),
  [HTML](docs/html-ip-licensing-register.md) and
  [Markdown](docs/markdown-ip-licensing-register.md) rights records - every row
  decided. Decided is not cleared: no legal review, no patent-freedom claim, no
  freedom-to-operate determination.
- [ODT IP and licensing register](docs/odt-ip-licensing-register.md) - the ODF
  rights record. Every row is decided and the position is assessed green, which is
  an engineering risk judgement on recorded evidence rather than a clearance: no
  legal review, no patent-freedom claim, no freedom-to-operate determination.
- [PDF IP and licensing register](docs/pdf-ip-licensing-register.md)
- [PDF approved sources](docs/pdf-approved-sources.md)
- [PDF corpus manifest](docs/pdf-corpus-manifest.json) and its
  [schema](docs/pdf-corpus-manifest.schema.json) - every PDF the tests use is
  generated in code; a committed `.pdf` needs a manifest entry with its
  provenance and rights first.
- [ADR Index](docs/adr/README.md)
  - [ADR 0001: Component Topology And Consumption Policy](docs/adr/0001-component-topology-and-consumption-policy.md)
  - [ADR 0002: Document Model Ownership And Promotion (Path A)](docs/adr/0002-document-model-ownership-and-promotion.md)
  - [ADR 0003: Codec Contract And Signature Probe](docs/adr/0003-codec-contract-and-signature-probe.md)
  - [ADR 0004: Document Read Limits And RTF Sanitization Policy](docs/adr/0004-document-read-limits-and-rtf-sanitization.md)
  - [ADR 0005: RTF First-Release Subset And Text Encoding](docs/adr/0005-rtf-first-release-subset-and-text-encoding.md)
  - [ADR 0006: Formatting Codes Projection And Grammar](docs/adr/0006-formatting-codes-projection-and-grammar.md)
  - [ADR 0007: PDF Component Scope And Delivery](docs/adr/0007-pdf-component-scope-and-delivery.md)
  - [ADR 0008: PDF Codec Requests, Results, And Commit Semantics](docs/adr/0008-pdf-codec-requests-results-and-commit.md)
  - [ADR 0009: PDF Security, Resources, And Privacy](docs/adr/0009-pdf-security-resources-and-privacy.md)
  - [ADR 0010: PDF Pagination, Units, Fonts, Scripts, And Platforms](docs/adr/0010-pdf-pagination-units-fonts-and-platforms.md)
  - [ADR 0011: PDF Standards, IP, Provenance, And Claims](docs/adr/0011-pdf-standards-ip-provenance-and-claims.md) (proposed; legal review pending)
  - [ADR 0012: PDF Base Implementation Scope And Composed Extensions](docs/adr/0012-pdf-base-implementation-and-composed-extensions.md)

## License

Broiler.Documents is licensed under the [Apache License 2.0](LICENSE). Third-party
material, if present, retains the license identified with that material. The
license provides the software on an "AS IS" basis, without warranties or
conditions.
