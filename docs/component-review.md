# Component review — 2026-09-13

The immediate build failures came from an incomplete migration from submodules to
NuGet packages. The HTML reader used DOM helpers absent from its pinned package,
the CLI lacked its managed image dependency, and CLI/HTML test source still used
the old graphics namespaces. Workflows and several architecture guards still
expected the removed submodules.

These failures are fixed. Package versions now live in
`Directory.Packages.props`, with the existing pins preserved. The obsolete parent
build import, dependency-root properties, and root metadata are removed. Guards
now enforce local project references, explicitly allowed Broiler packages, and
the public PDF boundary in the dependency assemblies.

## Implemented review findings

1. **Memory stream contract fixed.** `DocumentInput` preserves positions past EOF,
   rejects negative positions, invalid seek origins, and arithmetic overflow, and
   reports disposed streams as unreadable and unseekable. Reads validate buffer
   arguments. The caller's memory remains uncopied, and closing one stream does
   not close its input or another stream. Regression tests cover these behaviors,
   memory slices, and cancellation.

2. **Bounded ZIP/XML loading shared.**
   `src/Broiler.Documents/Packaging/DocumentPackage.cs` counts decompressed bytes,
   reads at most one byte past the remaining budget, prohibits DTDs, and disables
   external XML resolution. DOCX and ODT keep their diagnostic codes/messages and
   request their whitespace handling explicitly. Tests cover exact limits,
   compressed oversized entries, DTDs, invalid XML, and whitespace between spans;
   public-codec tests also verify rejection of malformed and oversized main parts.

3. **Image lookups consolidated.**
   `src/Broiler.Documents/Resources/DocumentImageFormats.cs` supplies extension
   mappings, raster signatures, and preferred output extensions to both codecs.
   The ODT-specific manifest media-type normalization remains in its codec. MIME
   aliases, fallback extensions, and the raster-only support boundary are unchanged.
   These helpers are internal; no new public API or runtime dependency was added.

4. **Reader and layout responsibilities extracted.** DOCX and ODT have separate
   table readers, document builders, and explicit read contexts. DOCX numbering
   and scalar XML parsing are separate collaborators too. Table readers receive
   a callback for nested block content, keeping the existing depth and content
   flow. CLI line wrapping and measured tokens now live in `LineWrapper` and
   `LayoutToken`; pagination, shape placement, and text measurement retain their
   existing behavior. The large files shrank as follows:

   | File | Before | After |
   | --- | ---: | ---: |
   | `DocxReader.cs` | 2,201 lines | 1,383 lines |
   | `OdtReader.cs` | 2,186 lines | 1,561 lines |
   | `DocumentLayout.cs` | 1,520 lines | 1,267 lines |

   The public document model and corpus baselines are unchanged.

5. **Test inspection helpers shared.**
   `src/tests/Shared/RepositoryFiles.cs` centralizes repository discovery, sorted
   project/package reference extraction, and build-output detection. It is linked
   only into test projects through `Directory.Build.targets`; it creates no new
   runtime assembly. Architecture tests keep their explicit dependency allowlists,
   and aggregate-only PDF guards retain their skip behavior.

## CI/CD alignment with Broiler.DOM

- Reusable CI runs Release builds and unit tests on Ubuntu and Windows. Documents'
  CLI corpus stays on both hosts; the executed-test floor remains 1,400.
- Preview selection and its tests, package verification, and consumer-restore
  verification are adapted from Broiler.DOM's `eng` scripts.
- Publishing resolves an unused preview, invokes CI with that version, and pushes
  the exact verified artifacts. Publication is serialized, defaults to dry run
  for manual dispatch, and limits package-write permission to the publish job.
- Both nightly workflows restore packages instead of checking out submodules.
  GitHub dependency authentication is supplied through NuGet's credential
  environment variable. Dependency package settings must grant this repository
  Actions read access.
- The PDF codec/providers and CLI remain excluded from publication.

## Validation

- Release solution build: zero warnings and zero errors.
- Unit tests after refactoring: 2,006 passed; three aggregate-only guards skipped.
  Eighteen new regression cases cover the stream and packaged XML changes.
- CLI corpus after refactoring: 2,320 passed; ten documented checks skipped.
  No baseline changes were needed.
- Preview-version selection: five Node tests passed.
- Eight NuGet packages and their symbol packages passed identity, internal
  dependency, assembly, documentation, README, and icon verification, both at the
  configured preview and with a `0.1.0-preview.123` version override.
- All four workflows passed actionlint; all 14 Bash steps and the PowerShell
  scripts passed syntax checks.
- Fresh consumer restore against GitHub Packages failed with HTTP 401. Local
  builds used the existing package cache; successful authenticated restore and
  hosted Linux/Windows CI runs remain to be verified in GitHub Actions. No
  packages were published. The full nightly fuzz and LibreOffice suites were
  not run locally.
