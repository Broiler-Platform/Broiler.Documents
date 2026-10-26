# Issue #154 follow-up: 15 DrawingML color cases

This follow-up to [the first issue #154 fixes](office-issue-154.md) addresses
15 missing color behaviors in the DOCX reader. These are import defects verified
with authored documents, not 15 newly resolved ranked LibreOffice originals.
The preceding implementation is commit `5726ec5`.

## Corrected cases

| # | Previously missing behavior | Result |
| --- | --- | --- |
| 1 | `scrgbClr` base colors | Linear-light integer percentage components are decoded to model RGB. |
| 2 | `hslClr` base colors | Hue angles, saturation and luminance produce a usable color, retaining hue while adjacent transforms operate. |
| 3 | Most `prstClr` names | All 140 DrawingML preset spellings are recognized, including `dk`, `lt` and `med` names; unknown names remain unresolved. |
| 4 | `alphaMod` | Multiply the current opacity rather than ignoring it. |
| 5 | `alphaOff` | Add a signed opacity offset and clamp the result. |
| 6 | `hue` | Replace the hue while retaining saturation/luminance. |
| 7 | `hueOff` | Add the stated angle offset. |
| 8 | `hueMod` | Scale the hue by the stated percentage. |
| 9 | `sat` | Replace saturation. |
| 10 | `satOff` | Add a signed saturation offset. |
| 11 | `lum` | Replace luminance; black/white endpoints become achromatic. |
| 12 | `comp` | Rotate hue by 180 degrees while retaining saturation/luminance. |
| 13 | `inv` | Invert linear RGB components, retaining opacity. |
| 14 | `gray` | Apply the native weighted RGB grayscale conversion. |
| 15 | Gamma conversion | Apply both parameterless `gamma` and `invGamma`, retaining opacity. |

Parameterless transforms no longer require a nonexistent `val` attribute.
Transforms execute in source order. Adjacent HSL operations preserve their HSL
state: desaturating red, changing hue to green, then restoring saturation must
produce green. Repeated RGB/HSL conversion previously could not represent that
sequence. Malformed numeric attributes and foreign/unknown transform elements
retain their existing safe fallback.

Theme palette slots now use the same base-color reader, so HSL, scRGB and preset
slots reach both Word text colors and shape paint. Shapes, gradient stops,
outlines and format-matrix placeholder colors share the conversion path.
Export retains the resolved paint as explicit colors; live theme bindings are
not introduced.

## Verification

- Release solution build passed.
- **4,270 unit tests passed**, three additional skips.
- **59 new cases**, including base colors, preset aliases, opacity arithmetic,
  HSL state/order, native angle boundaries, reversible operations, malformed
  data, theme-slot integration, gradient/outline integration and export.
- **46/59 fail against the isolated preceding DOCX assembly**; 13 protect
  unchanged fallbacks, boundary behavior or round-trip invariants.
- CLI corpus: **2,320/2,320 passed**, 105 documented skips.
- Nineteen authored native controls improve **40/57 -> 57/57** checks. Every
  native pixel check passes after the change (2/19 before, 19/19 after).
- Interior RGB samples at `(200, 300)` agree exactly in 17 controls. Inversion
  yields `(231,231,231)` versus native `(230,230,230)`; gamma yields
  `(189,189,189)` versus native `(188,188,188)`. Maximum channel difference is one.
- The original issue selection retains **38/89**, with 51 failures and the same
  one skipped native reference. All original recorded render observations and
  check outcomes remain unchanged. No ranked original is newly fully resolved.

The source selection contains these color encodings/operations in the SmartArt
parts of `ooxmlimport/data/fdo87488.docx`. Importing the complete SmartArt layout
remains separate work; parsing additional color encodings does not make that
otherwise unsupported diagram render.

## Consumer limits

Arithmetic follows the existing native-compatible color model: gamma 2.3 for
linear RGB conversion and 22%/72%/6% encoded-RGB weights for grayscale. This is
not an ICC-managed conversion pipeline. Integer OOXML percentage and angle
attributes are supported; percent-suffixed lexical forms are not added.
Numeric hue operations clamp at 0/360 degrees, matching the tested LibreOffice
underflow/overflow behavior; complementary hue rotation wraps around the circle.
Small rounding differences remain, as the pixel evidence above records.

The preset palette uses the standard named RGB values. One known reference
exception is `ltGoldenrodYellow`: the retained standard value is `#FAFAD2`, while
the inspected LibreOffice lookup uses `#FAFA78`. The representative native preset
control uses `orange`; passing it does not certify all 140 native preset values.
Unsupported channel-specific red/green/blue transforms and effects remain outside
this change. Existing shape geometry and pagination approximations also remain.

## Reproduction

`tests/office/generators/drawing-colors.py` is a self-contained standard-library
Python generator. It emits 19 minimal DOCX packages, a selection and source
hashes. ZIP timestamps and package metadata are fixed; running it twice in the
same environment produces identical hashes. No downloaded template is needed.

```sh
python3 tests/office/generators/drawing-colors.py /tmp/office-color-probes
(cd /tmp/office-color-probes && sha256sum -c samples.sha256)
dotnet build Broiler.Documents.slnx -c Release --nologo -m:4
pwsh -NoProfile -File eng/run-tests.ps1 -Configuration Release
dotnet run --project src/tests/Broiler.Documents.Office -c Release --no-build -- \
  --extras-dir /tmp/office-color-probes \
  --sample-list /tmp/office-color-probes/samples.txt --soffice "$soffice" \
  --jobs 3 --dpi 72 --max-pages 40 --lo-timeout 30 --top-differences 20 \
  --triage-dir colors-triage --report colors.json --workspace colors-work --keep
```

Compare an independently built `5726ec5` CLI using `--tool`, separate output
paths and the same external tools/fonts. The measured environment is Linux,
LibreOffice 26.8.1.1, Poppler 26.05.0, 72 DPI and the existing 80% threshold, with
unpinned fonts. The original #154 CI used other versions and 144 DPI. Baselines,
tolerances and tool approvals have not changed.
