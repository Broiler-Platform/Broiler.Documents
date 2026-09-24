# CI, packages, and releases

The Broiler component repositories use a unified workflow structure and release helpers.
`Directory.Packages.props` centrally manages external dependency versions. Release versions
are configured separately: `eng/Broiler.Packaging.props` supplies suite-wide defaults, with
component overrides in `Directory.Build.props`. Packages within this repository share a version;
each component repository advances its own preview sequence.

## Build, test, and pack

Requirements:
- .NET 10 SDK (`10.0.x`)
- Node.js 24 (for release validation scripts)
- PowerShell 7 (`pwsh`) or Windows PowerShell 5.1 (for packaging and feed verification)
- Bash (Git Bash on Windows or Linux bash) for conformance scripts

Run the developer workflow:

```powershell
# Build the solution in Release configuration
dotnet build Broiler.Documents.slnx -c Release

# Run unit tests
pwsh -File ./eng/run-tests.ps1 -Configuration Release

# Run the command-line corpus suite
dotnet run --project src/tests/Broiler.Documents.Corpus -c Release --no-build

# Run release script tests
node --test eng/resolve-preview-version.test.mjs eng/select-restore-feed.test.mjs

# Pack and verify all 11 packages and symbols
pwsh -File eng/pack.ps1
```

Use `Debug` or `Release` configuration. The test runner executes all unit test suites across all 11 assemblies,
requiring at least 1,400 executed tests (with 2,369 executed currently). The corpus suite runs 2,320 checks
against committed baselines via the `broilerdoc` CLI.

`eng/pack.ps1` enumerates **every packable project** in the solution and verifies all 11 packages:
- `Broiler.Documents.Model`
- `Broiler.Documents.FormatCodes`
- `Broiler.Documents`
- `Broiler.Documents.Rtf`
- `Broiler.Documents.Docx`
- `Broiler.Documents.Odt`
- `Broiler.Documents.Html`
- `Broiler.Documents.Markdown`
- `Broiler.Documents.Pdf`
- `Broiler.Documents.Pdf.Images`
- `Broiler.Documents.Pdf.Fonts`

It checks package identities, matching versions, internal dependencies, README, icon, assemblies, XML API documentation,
and symbol packages (`.snupkg`). Tests and `Broiler.Documents.Cli` do not pack. The output directory must contain no previous
packages; specify `-Output <empty-directory>` for a custom run. Optional `-Version 0.1.0-preview.N` stamps the assembly and package versions together.

## Package feeds

All package restore is performed strictly from **NuGet.org** (`https://api.nuget.org/v3/index.json`).
GitHub Packages is not used.

`NuGet.config` explicitly clears inherited sources, disabled sources, and source mappings so machine
or user-level settings cannot silently alter package resolution:

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

Cross-repository dependencies (`Broiler.Dom`, `Broiler.Dom.Html`, `Broiler.Graphics`, `Broiler.Media.Image.Managed`)
are pinned in `Directory.Packages.props` and restore publicly from NuGet.org. No GitHub authentication tokens or
`NuGetPackageSourceCredentials_*` environment variables are required for developer restores or CI builds.

`eng/select-restore-feed.mjs` verifies that NuGet.org hosts every pinned `Broiler.*` package version
before building. Run the test suite for feed selection with:

```sh
node --test eng/select-restore-feed.test.mjs
```

## CI and Publish

### Continuous Integration (CI)

The CI workflow (`.github/workflows/ci.yml`) runs on pushes to `main`, pull requests, and workflow dispatch:
- Builds and runs tests in `Release` configuration across `ubuntu-latest` and `windows-latest`.
- Runs release script tests (`eng/resolve-preview-version.test.mjs` and `eng/select-restore-feed.test.mjs`).
- Verifies package restore from NuGet.org (`node eng/select-restore-feed.mjs`).
- Runs the CLI corpus suite (`broilerdoc-corpus`) against the committed baseline.
- On Ubuntu, packs and validates all 11 packages and symbol packages (`.snupkg`).
- Attaches the validated artifacts as `nuget-packages`.

Publish calls this same CI workflow with the resolved release version and consumes the validated artifacts
without rebuilding.

### Publishing to NuGet.org

Publishing is handled by `.github/workflows/publish.yml`, targeting **NuGet.org** exclusively:

1. **Version Resolution:** `eng/resolve-preview-version.mjs` queries the NuGet.org registration/flat container
   API to find the highest published preview number for the current `0.1.0-preview.*` line and computes the
   next unused preview version (or honours an explicit `version-suffix`).
2. **Validation and Pack:** Invokes the CI workflow with the computed version.
3. **Consumer Verification:** `eng/verify-feed.ps1 -Target nuget` sets up an isolated temporary consumer
   project and restores against NuGet.org and the local package artifacts with `--no-http-cache`. This verifies
   that consumers will be able to restore the packages without missing dependencies.
4. **Push:** When `dry-run` is false, pushes all `.nupkg` and matching `.snupkg` symbol packages to NuGet.org:

```sh
dotnet nuget push 'artifacts/*.nupkg' --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY"
```

Publishing requires the repository secret `NUGET_TOKEN` (NuGet.org API key).
Publishing can also be triggered by pushing a tag matching `v0.1.0-preview.N`.
Dry runs (`dry-run: true`, the default for manual workflow dispatches) perform all validations and attach
packages as artifacts without pushing to NuGet.org.
