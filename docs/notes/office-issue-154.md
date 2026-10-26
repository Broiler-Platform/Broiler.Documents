# Issue #154: legacy RTF drawings and DOCX shape style references

The [30 ranked cases in issue #154](https://github.com/Broiler-Platform/Broiler.Documents/issues/154)
were downloaded and compared against commit `6e5082e`. The exact paths and input
SHA-256 values are recorded in `tests/office/selections/issue-154.txt` and
`tests/office/selections/issue-154.sha256`. Source documents are not vendored.

## Corrections

### Legacy RTF drawing objects

The reader previously skipped the entire ignorable `\do` destination. It now
retains one rectangle (`\dprect`), ellipse (`\dpellipse`) or line (`\dpline`):

- Twip offsets/extents, page/margin/column horizontal origins and
  page/margin/paragraph vertical origins are mapped to model coordinates.
- RGB fill/outline components, transparent fill, hollow outlines, declared pen
  width and drawing stack order survive. Legacy outlines without an explicit
  color use the native blue default, independently of modern shapes' black.
- Zero-height/width lines and reversed extents retain their line geometry.
- Nested formatting does not finish a drawing early. Unknown ignorable groups,
  skipped destinations, notes and running content cannot emit body drawings.
- A modern `\shpinst` suppresses its `\shprslt` fallback. The fallback's body
  text or legacy geometry must not be imported a second time. A shape containing
  only a legacy drawing result can still retain that result.

Aggregates, polygons, text-box primitives and other unsupported legacy objects
are skipped with `rtf.drawing.legacy`. Rounded corners, patterns and line
decoration are approximated with the same diagnostic. This is deliberately not
an implementation of the complete legacy drawing language. Running-content
shapes remain unsupported. Native legacy pen-width import differs from the
stated value in the tested LibreOffice; the model preserves the stated value.

### DOCX DrawingML format matrix

A `wps:style` can reference the theme's `fillStyleLst`, `bgFillStyleLst` and
`lnStyleLst` instead of carrying paint directly in `wps:spPr`. Those references
were ignored. The reader now resolves the one-based indices (background fills
start at 1001), substitutes the reference color for `phClr`, and applies the
matrix's color transforms to it. Solid fills, supported gradients, outline
colors and outline widths reach the existing shape reader.

Direct properties take precedence. Explicit `noFill` cancels inherited paint;
a line stating only a width retains its referenced color, and a direct color
retains the referenced width. Missing/invalid indices do not invent paint.
Resolved paint survives DOCX export as explicit colors, without live theme
bindings. This change covers WordprocessingML shapes, including those already
walked inside drawing groups; it does not implement picture-style references,
SmartArt layout, effects, arbitrary paths, live theme editing or group rotation.
Existing gradient/pattern and outline-rendering approximations still apply.

## Verification

- Release solution build passed.
- **4,211 unit tests passed**, three additional tests skipped.
- **38 new regression cases**: 21 RTF and 17 DOCX, including export round trips.
  Against isolated assemblies built from `6e5082e`, 17 RTF and seven DOCX cases
  fail; the remaining 14 protect unchanged fallbacks and precedence.
- CLI corpus: **2,320/2,320 passed**, 105 documented skips.
- Eight authored RTF native controls improve **17/24 -> 24/24** checks.
- Eight authored DOCX native controls improve **18/24 -> 24/24** checks.
  Native color pixels confirm reference blue `#4F81BD`, transformed blue
  `#376092`, explicit red and the background-fill entry's green. Passing checks
  are tolerance checks, not pixel identity: outline placement, antialiasing and
  legacy pen widths still differ.
- All 30 originals retain **38/89** checks, 51 failed and the same one skipped
  reference (`uiwriter/data/tdf170672.docx`, no native PDF). No check changes
  from pass to fail. No original is claimed newly fully resolved.

The original `rtfexport/data/dprect-anchor.rtf` now retains its formerly missing
rectangle: blank/inked mismatch becomes a **16 px** ink-box delta, ink profile
**0 -> 0.8417**, and coverage delta **0.4619% -> 0.0347%**. Paper geometry remains
different, rounded corners are approximated, and blur difference increases
**0.6184% -> 0.7677%**. It remains a failing comparison.

Two other originals have worse narrow measurements without losing a pass:
`uiwriter/data/tdf147199.docx` retains theme paint but blur changes
**14.0465% -> 14.2737%** and coverage delta **4.4947% -> 4.7034%**;
`ooxmlexport/data/tdf141173_missingFrames.rtf` retains a legacy fallback line,
while blur changes **11.3381% -> 12.2093%** and coverage delta
**3.7387% -> 3.9571%**. Their pagination and remaining layout failures persist.
Other original render observations are unchanged.

## Environment and reproduction

The local comparisons use LibreOffice **26.8.1.1**, Poppler **26.05.0**, Linux,
**72 DPI**, 80% threshold, 40-page cap and a 30-second LibreOffice timeout.
Fonts are not pinned. The issue used LibreOffice 26.2.6.3 / Poppler 26.01.0 at
144 DPI; its ranking is not an exact local reproduction. For example, the
hidden `tdf163894` reference has four pages locally rather than the issue's
40-page capped result. Do not infer a codec improvement from that difference.
No baseline tolerances, baseline expectations or tool approvals changed.

After fetching the listed public LibreOffice inputs under `$extras`, verify
input identity and run both revisions with the same external tools and fonts:

```sh
(cd "$extras" && sha256sum -c "$repo/tests/office/selections/issue-154.sha256")
dotnet build Broiler.Documents.slnx -c Release --nologo -m:4
pwsh -NoProfile -File eng/run-tests.ps1 -Configuration Release
dotnet run --project src/tests/Broiler.Documents.Corpus -c Release --no-build -- \
  --report corpus.json --junit corpus.junit.xml
dotnet run --project src/tests/Broiler.Documents.Office -c Release --no-build -- \
  --extras-dir "$extras" --sample-list tests/office/selections/issue-154.txt \
  --soffice "$soffice" --jobs 3 --dpi 72 --max-pages 40 --lo-timeout 30 \
  --top-differences 30 --triage-dir triage-154 --report office-154.json \
  --workspace office-154-work --keep
```

Use `--tool` to select an independently built earlier CLI and distinct report,
workspace and triage paths; use `--previous-triage` for the subsequent comparison.
The committed in-memory package tests exercise the import and export behavior
without depending on external Office documents.

Remaining high-impact groups include floating/nested table pagination, linked
text-frame overflow, SmartArt and custom VML geometry. The remote-background
case points to an external HTTP image and cannot be treated as a self-contained
paint fixture. These were investigated but not assigned speculative fixes.
