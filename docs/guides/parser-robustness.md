# Parser robustness regressions

The RTF, HTML, Markdown, CSV, DOCX and ODT unit test projects each run a small
deterministic robustness suite through the codec request API. The shared source
is `src/tests/Shared/ParserRobustnessTests.cs`, linked into those test projects;
each format supplies its own codec and seed. There are no new dependencies or
downloaded fixtures. Text seeds are authored in the tests. Package seeds are
generated from authored text by the existing DOCX and ODT writers.

The suites exercise:

- Short prefixes and evenly spaced truncations of a valid seed, followed by a
  valid read on the same codec instance to check recovery and state isolation.
- 128 deterministic single-byte mutations, comparing status, plain text,
  paragraph count and diagnostic codes/severities with a fresh codec instance.
- 128 deterministic random byte inputs of fewer than 512 bytes.
- Small byte limits, paragraph floods and pre-cancelled requests. Returned
  documents must respect the paragraph cap and their run lengths must match
  their paragraph lengths.
- DOCX/ODT content-XML truncations inside rebuilt valid ZIP packages. Keeping the
  package structure intact ensures these cases exercise XML parsing rather than
  merely failing ZIP discovery. Malformed XML must produce a non-success result
  with diagnostics, and the next valid read must still match the baseline.

`CsvNormalizationPropertyTests` additionally generates 100 three-by-three grids
for each of comma, semicolon and tab. Values include quotes, delimiters, line
feeds, Unicode, empty strings, whitespace and formula-like text. It checks cell
values against the generated input and byte-stable write/read/write output with
an explicitly stated delimiter and the default formula-escaping policy. These
checks cover 300 grids, not arbitrary CSV dialects or source-preserving output.

The existing RTF fuzz tests also retain their structure-biased random inputs,
normalization checks, large nesting input and oversized numeric parameters.

Run all six affected projects using the repository's ordinary test runner, or
run just the new cases in PowerShell:

```powershell
foreach ($format in @('Rtf', 'Html', 'Markdown', 'Csv', 'Docx', 'Odt')) {
    dotnet test "src/tests/Broiler.Documents.$format.Tests/Broiler.Documents.$format.Tests.csproj" --filter 'FullyQualifiedName~ParserRobustness|FullyQualifiedName~CsvNormalizationProperty'
    if ($LASTEXITCODE -ne 0) { throw "Tests failed for $format" }
}
```

These are bounded regression inputs, not proof of bounded parser memory or
running time. No new outer memory/CPU supervisor or allocation threshold is
introduced. They do not replace sustained coverage-guided fuzzing, independent
conformance oracles, approved external corpora, or release performance gates.
