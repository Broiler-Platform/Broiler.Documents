# Broiler.Documents

[![CI](https://github.com/Broiler-Platform/Broiler.Documents/actions/workflows/ci.yml/badge.svg)](https://github.com/Broiler-Platform/Broiler.Documents/actions/workflows/ci.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/LICENSE)

`Broiler.Documents` is Broiler's format-independent rich-text document core: the
UI-free document model, headless page layout, deterministic Formatting Codes
projection, and the codec contract, catalog, options, limits, diagnostics, and
resource-policy primitives.

Format-specific codecs (RTF, DOCX, ODT, HTML, Markdown, CSV, XLSX, ODS, PDF)
and the `broilerdoc` command-line tool live in the companion repository
[Broiler.Documents.Codecs](https://github.com/Broiler-Platform/Broiler.Documents.Codecs).

> **Preview release.** `0.1.0-preview.N` packages are previews. Public
> APIs and behaviour are not frozen and may change before `1.0`.

## Installation

Preview packages need an explicit prerelease opt-in:

```bash
dotnet add package Broiler.Documents --prerelease
```

All dependencies (including upstream `Broiler.Graphics`) restore directly from
**NuGet.org** (`https://api.nuget.org/v3/index.json`).

## Packages

Four preview packages are generated. All target `net10.0`, ship XML
documentation and a `.snupkg` symbol package, and are built deterministically
with SourceLink.

| Package | Role |
| --- | --- |
| `Broiler.Documents.Model` | Platform-neutral rich-text document model, promoted out of `Broiler.UI.RichEdit`; depends only on `Broiler.Graphics`. |
| `Broiler.Documents.Pagination` | Headless page layout over the model: lays a document out on pages of a `Broiler.Graphics.Paging` page setup and paints each as a `BPage` for printing, print preview and fixed-page output. References only the model and `Broiler.Graphics`. |
| `Broiler.Documents.FormatCodes` | Deterministic, versioned Formatting Codes projection: typed tokens, source mappings, diagnostics, and resource policy. References only the model. |
| `Broiler.Documents` | Format-neutral codec contract, catalog, descriptors, source hints, read/write options, limits, diagnostics, conversion context, and probe results. References only the model. |

### Dependency direction

```text
Broiler.Documents.Model       -> Broiler.Graphics
Broiler.Documents             -> Broiler.Documents.Model
Broiler.Documents.Pagination  -> Broiler.Documents.Model, Broiler.Graphics
Broiler.Documents.FormatCodes -> Broiler.Documents.Model
```

## Component Constraints

- Target .NET 10 only.
- Do not add third-party runtime dependencies.
- Keep all core assemblies format-independent, platform-neutral, safe-code
  compatible, trimming-friendly, and AOT-friendly.
- Do not add hidden global codec registration or module-initializer side effects.
- `Broiler.Documents.Model` and `Broiler.Documents` must not reference
  `Broiler.UI`, `Broiler.DOM`, `Broiler.Input`, or any `*.Windows` assembly.

Architecture tests in `Broiler.Documents.Tests` and `Broiler.Documents.Model.Tests`
enforce these constraints against the project files and exported assemblies.

## Repository layout

```text
src/                      core runtime assemblies, one directory per package
src/tests/                one xUnit test project per assembly
eng/                      vendored packaging metadata, package icon, build/test scripts
docs/                     core roadmap, architecture notes, guides, ADRs
.github/workflows/        CI and publish pipelines
Directory.Packages.props  centrally pinned Broiler and test package versions
Broiler.Documents.slnx    solution over every core project in src/
```

## Building, testing, and packaging

```bash
dotnet build Broiler.Documents.slnx -c Release
```

Run all unit test projects:

```powershell
pwsh -File ./eng/run-tests.ps1 -Configuration Release
```

Pack and verify the four core packages:

```powershell
pwsh -File ./eng/pack.ps1
```

## License

Broiler.Documents is licensed under the [Apache License 2.0](https://github.com/Broiler-Platform/Broiler.Documents/blob/main/LICENSE).
