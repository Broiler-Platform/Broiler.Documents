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

> **Preview release.** `0.1.0-preview.N` packages are previews. Public
> APIs and behaviour are not frozen and may change before `1.0`. The codecs parse
> untrusted input — RTF control words, Open XML and OpenDocument packages, HTML,
> CSV records, and PDF object graphs — and must be treated as security-sensitive; no fuzzing campaign,
> dependency scan, or independent security audit is recorded for this revision.
> See the [roadmap](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/roadmap.md) for what is still open.

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

### Package feeds and restore

All dependencies (including upstream Broiler packages `Broiler.Dom`, `Broiler.Dom.Html`, `Broiler.Graphics`, and `Broiler.Media.Image.Managed`) restore directly from **NuGet.org** (`https://api.nuget.org/v3/index.json`).
GitHub Packages is not used.

`NuGet.config` in the repository root pins NuGet.org and clears machine-level configurations:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <disabledPackageSources>
    <clear />
  </disabledPackageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

Cross-repository dependencies are pinned in `Directory.Packages.props` and restore publicly from NuGet.org. No GitHub authentication tokens or `NuGetPackageSourceCredentials_*` environment variables are required for developer restores or CI builds.

## Packages

Twelve preview packages are generated. All target `net10.0`, ship XML
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
| `Broiler.Documents.Csv` | CSV codec: reads comma-separated values into a table in the model and writes tables back out. |
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
write-preview gates; see the
[PDF delivery gates](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/roadmap.md#pdf-delivery-gates) and the
[PDF extension points](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/notes/pdf/extension-points.md).

### Dependency direction

```text
Broiler.Documents.Rtf      -> Broiler.Documents -> Broiler.Documents.Model -> Broiler.Graphics
Broiler.Documents.Docx     -> Broiler.Documents -> Broiler.Documents.Model
Broiler.Documents.Odt      -> Broiler.Documents -> Broiler.Documents.Model
Broiler.Documents.Markdown -> Broiler.Documents -> Broiler.Documents.Model
Broiler.Documents.Csv      -> Broiler.Documents -> Broiler.Documents.Model
Broiler.Documents.Html     -> Broiler.Documents -> Broiler.Documents.Model
Broiler.Documents.Html     -> Broiler.Dom, Broiler.Dom.Html
Broiler.Documents.FormatCodes                   -> Broiler.Documents.Model
Broiler.Documents.Pdf      -> Broiler.Documents -> Broiler.Documents.Model
Broiler.Documents.Pdf.Fonts  -> Broiler.Documents.Pdf, Broiler.Graphics
Broiler.Documents.Pdf.Images -> Broiler.Documents.Pdf, Broiler.Media.Image, Broiler.Media.Image.Managed
```

`Broiler.Graphics`, `Broiler.Dom`, `Broiler.Dom.Html`, and `Broiler.Media.Image.Managed`
are packaged by their own repositories and appear as package dependencies at the
versions pinned here (`Directory.Packages.props`), restoring publicly from NuGet.org.

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

The head composes `Broiler.Documents.Pdf` **read-only**: it opens, converts from
and renders PDF files, and writes none until PDF output passes its write-preview
gate.
It is itself `IsPackable=false`, so the package table above is unchanged. See the
[CLI guide](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/guides/cli.md) for the full command reference, the exit codes, the edit
language, and what makes a render reproducible across machines.

CI drives that tool over a document corpus on every push on Linux and Windows,
and holds what comes back against a committed baseline of what each
format is expected to lose. See [the corpus suite](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/guides/corpus-suite.md) for what
it checks, where the documents come from and why none of them is downloaded, and
what it has found so far.

A second suite runs nightly rather than on every push, and answers the question
the first one cannot: whether this component reads documents an unrelated
implementation wrote. LibreOffice manufactures them from seeds authored here, and
both what they say and how they look are compared. See
[the office conformance suite](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/guides/office-conformance.md), which is also candid
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
docs/                    roadmap, notes on the code, IP registers, guides, ADRs
.github/workflows/       CI and publish pipelines
Directory.Packages.props  centrally pinned Broiler and test package versions
Broiler.Documents.slnx   solution over every project in src/
```

Cross-component dependencies are NuGet packages. Project references remain inside
this repository; sibling checkouts and parent build properties are not required.

## Building and testing

Clone the repository:

```bash
git clone https://github.com/Broiler-Platform/Broiler.Documents.git
cd Broiler.Documents
```

All dependencies restore directly from NuGet.org without private feeds, tokens, or credentials.

The solution defines six configurations. Every project here is platform-neutral
`net10.0`, so all six build the same projects; the `-Windows` and `-Linux`
variants exist to line this component up with the rest of the suite and map onto
plain `Debug`/`Release`.

```bash
dotnet build Broiler.Documents.slnx -c Release
```

Run all unit test projects:

```powershell
pwsh -File ./eng/run-tests.ps1 -Configuration Release
# or on Windows PowerShell:
powershell -File ./eng/run-tests.ps1 -Configuration Release
```

Run the document corpus suite over the command-line head:

```bash
dotnet run --project src/tests/Broiler.Documents.Corpus -c Release --no-build
```

Run the release script tests:

```bash
node --test eng/resolve-preview-version.test.mjs eng/select-restore-feed.test.mjs
```

Three PDF guards in `Broiler.Documents.Tests` assert on application heads
(`src/Broiler.Writer.*`), which live in the aggregate repository
rather than here. They report as **skipped** in a standalone checkout and run in
full when this component is checked out inside the aggregate.

## Packaging

Every project is platform-neutral, so one run produces the whole set:

```powershell
pwsh -File eng/pack.ps1
# or on Windows PowerShell:
powershell -File eng/pack.ps1
```

The script checks package identities, internal dependency versions, documentation,
icons, and symbols, and rejects an output directory containing stale packages.
Test projects and `Broiler.Documents.Cli` never pack.
The CLI is wired to pack as a .NET tool and is held at `IsPackable=false` so that
adding a command line does not silently change what a `v*` tag pushes to
nuget.org; [the CLI guide](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/guides/cli.md#shipping-it-as-a-tool) says how to turn it on. `eng/Broiler.Packaging.props`
is a vendored copy of the suite-wide packaging metadata and holds the default
preview version. Publishing resolves an unused preview and passes it as an
MSBuild override without editing this file or changing dependency versions.
See [Packaging and Releases](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/guides/packaging.md) for full details.

## Continuous integration and releases

`.github/workflows/ci.yml` builds and tests Release and runs the CLI corpus on
Ubuntu and Windows:
- Tests release scripts (`eng/resolve-preview-version.test.mjs` and `eng/select-restore-feed.test.mjs`).
- Verifies that NuGet.org hosts all pinned Broiler packages (`eng/select-restore-feed.mjs`).
- Builds and runs all unit tests in Release configuration. `eng/run-tests.ps1` requires at least 1,400 executed tests (with 2,514 executed currently) and terminates a test host if a test hangs. Unit and corpus reports are uploaded even on failure.
- Runs the CLI corpus suite (`broilerdoc-corpus`) against the committed baseline (2,320 checks).
- On Ubuntu, packs and verifies all twelve preview packages and `.snupkg` symbol packages, uploading them as `nuget-packages`.

`.github/workflows/publish.yml` publishes exclusively to **NuGet.org**:
- Queries NuGet.org via `eng/resolve-preview-version.mjs` to automatically resolve the next unused `X.Y.Z-preview.N` across all shipping packages (or accepts an explicit `version-suffix` or `v*` tag).
- Validates the version by calling the reusable CI workflow, downloading those exact validated artifacts instead of rebuilding.
- Verifies consumer restore against NuGet.org using an isolated cache (`eng/verify-feed.ps1 -Target nuget`) before pushing.
- Pushes packages and symbol packages to NuGet.org using the `NUGET_TOKEN` secret.
- Every run pushes; there is no dry-run mode. CI performs the no-push pack and consumer-restore check on every push and pull request.

## Documentation

The documentation is split by kind rather than by format;
[docs/README.md](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/README.md) is the map.

- **[Roadmap](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/roadmap.md)** - everything still open, and nothing else,
  including the PDF delivery gates.
- **Notes on the code as it is** - [architecture and shared contracts](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/notes/architecture.md),
  and one note per codec stating exactly what it reads, writes, approximates and
  drops: [RTF](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/notes/rtf.md), [DOCX](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/notes/docx.md),
  [ODT](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/notes/odt.md), [HTML](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/notes/html.md),
  [Markdown](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/notes/markdown.md), [CSV](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/notes/csv.md) and [PDF](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/notes/pdf/README.md).
  For PDF, the [feature matrix](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/notes/pdf/feature-matrix.md) is the
  authority for what the codec may be described as doing, and the
  [construct inventory](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/notes/pdf/construct-inventory.md) lists every
  construct it reads, writes, recognizes without interpreting, and rejects. The
  [Formatting Codes grammar](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/src/Broiler.Documents.FormatCodes/GRAMMAR.md)
  is version 1.
- **[IP, licensing and provenance](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/ip/README.md)** - one register per
  format, the PDF approved sources and corpus manifest, and the review briefs.
  Decided is not cleared: no legal review, no patent-freedom claim, no
  freedom-to-operate determination.
- **Guides** - the [command line](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/guides/cli.md),
  [packaging and releases](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/guides/packaging.md), the
  [corpus suite](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/guides/corpus-suite.md) and the
  [office conformance suite](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/guides/office-conformance.md).
- **[Architecture decision records](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/docs/adr/README.md)** - ADRs 0001 to 0016.

## License

Broiler.Documents is licensed under the [Apache License 2.0](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/LICENSE). Third-party
material, if present, retains the license identified with that material. The
license provides the software on an "AS IS" basis, without warranties or
conditions.
