# Issue 154: legacy RTF drawings, DrawingML colors and cached diagrams

[Issue #154](https://github.com/Broiler-Platform/Broiler.Documents/issues/154)
ranked 30 cases. Exact paths and input SHA-256 values are in
`tests/office/selections/issue-154.txt` and
`tests/office/selections/issue-154.sha256`; sources are not vendored. The first
pass is compared against `6e5082e`, the color pass against `5726ec5` and the
diagram pass against `9935963`. Local runs use Linux, LibreOffice 26.8.1.1,
Poppler 26.05.0, 72 DPI, the 80% threshold, a 40-page cap, a 30-second
LibreOffice timeout and unpinned fonts. The issue used LibreOffice 26.2.6.3 /
Poppler 26.01.0 at 144 DPI, so its ranking is not reproduced exactly - e.g. the
hidden `tdf163894` reference has four pages locally instead of the issue's
40-page capped result; do not read that as a codec improvement. Passing checks
are tolerance checks, not pixel identity. No baselines, tolerances or tool
approvals changed.

The 30 originals stay at **38/89** (51 failed, one skipped:
`uiwriter/data/tdf170672.docx` has no native PDF) through all three passes;
no check goes from pass to fail except the text check noted under cached
diagrams.

```sh
(cd "$extras" && sha256sum -c "$repo/tests/office/selections/issue-154.sha256")
dotnet run --project src/tests/Broiler.Documents.Office -c Release --no-build -- \
  --extras-dir "$extras" --sample-list tests/office/selections/issue-154.txt \
  --soffice "$soffice" --jobs 3 --dpi 72 --max-pages 40 --lo-timeout 30 \
  --top-differences 30 --triage-dir triage-154 --report office-154.json \
  --workspace office-154-work --keep
```

For the before side, pass an independently built earlier CLI with `--tool` and
separate report/workspace/triage paths, then `--previous-triage`.

## Legacy RTF drawing objects

The reader used to skip the whole ignorable `\do` destination. It now keeps one
rectangle (`\dprect`), ellipse (`\dpellipse`) or line (`\dpline`):

- Twip offsets/extents with page/margin/column horizontal and
  page/margin/paragraph vertical origins map to model coordinates.
- RGB fill/outline, transparent fill, hollow outline, pen width and stack order
  survive. Outlines without an explicit color use the native legacy blue
  default (modern shapes keep black).
- Zero-height/width lines and reversed extents keep their geometry.
- Nested formatting does not end a drawing early; unknown ignorable groups,
  skipped destinations, notes and running content cannot emit body drawings.
- A modern `\shpinst` suppresses its `\shprslt` fallback (body text or legacy
  geometry is not imported twice); a shape whose only result is a legacy
  drawing keeps it.

Aggregates, polygons, text-box primitives and other legacy objects are skipped
with `rtf.drawing.legacy`; rounded corners, patterns and line decoration are
approximated under the same code. Running-content shapes are unsupported.
LibreOffice imports legacy pen widths differently from the stated value; the
model keeps the stated value.

Evidence: eight RTF controls 17/24 -> 24/24. `rtfexport/data/dprect-anchor.rtf`
keeps its formerly missing rectangle (ink profile 0 -> 0.8417, coverage delta
0.4619% -> 0.0347%) but still fails: paper geometry differs, corners are
approximated, blur 0.6184% -> 0.7677%. `ooxmlexport/data/tdf141173_missingFrames.rtf`
keeps a legacy fallback line; blur 11.3381% -> 12.2093%, no pass lost.

## DrawingML style references

A `wps:style` can reference the theme's `fillStyleLst`, `bgFillStyleLst` and
`lnStyleLst` instead of carrying paint in `wps:spPr`. The reader resolves the
one-based indices (background fills start at 1001), substitutes the reference
color for `phClr` and applies the matrix's transforms. Solid fills, supported
gradients, outline colors and widths reach the shape reader. Direct properties
win: explicit `noFill` cancels inherited paint; a width-only line keeps the
referenced color and a direct color keeps the referenced width; bad indices
invent nothing. Export writes explicit colors. Covers WordprocessingML shapes,
including those inside drawing groups.

Evidence: eight DOCX controls 18/24 -> 24/24; native pixels confirm reference
blue `#4F81BD`, transformed `#376092`, explicit red and the background-fill
green. `uiwriter/data/tdf147199.docx` keeps theme paint but blur worsens
14.0465% -> 14.2737%; no pass lost.

## DrawingML colors

Fifteen DOCX color behaviors (base `5726ec5`):

- Base colors `scrgbClr` (linear-light percentages), `hslClr`, and all 140
  `prstClr` spellings including `dk`/`lt`/`med` names (unknown stay
  unresolved).
- Transforms `alphaMod`, `alphaOff` (clamped), `hue`, `hueOff`, `hueMod`, `sat`,
  `satOff`, `lum` (black/white endpoints achromatic), `comp` (180-degree
  rotation), `inv` (linear RGB, opacity kept), `gray` (native weighted
  grayscale), and parameterless `gamma` / `invGamma`.
- Parameterless transforms need no `val`. Transforms run in source order and
  adjacent HSL operations keep HSL state (desaturate red, set hue green, restore
  saturation -> green). Malformed numbers and unknown elements keep the safe
  fallback.
- Theme palette slots use the same base-color reader, so these encodings reach
  Word text colors and shape paint; shapes, gradient stops, outlines and
  format-matrix placeholders share the path. Export writes explicit colors.

Rules: gamma 2.3 for linear RGB and 22%/72%/6% grayscale weights (not ICC).
Integer percentage/angle attributes only (no percent-suffixed forms). Numeric
hue operations clamp at 0/360 like LibreOffice; `comp` wraps. Presets use the
standard RGB values; known exception `ltGoldenrodYellow` is `#FAFAD2` here but
`#FAFA78` in LibreOffice's lookup. Only `orange` is natively tested.

Evidence: `tests/office/generators/drawing-colors.py` (standard-library Python,
deterministic hashes) emits 19 DOCX controls; they go 40/57 -> 57/57. Interior
RGB matches exactly in 17; `inv` and `gamma` differ by one channel unit. These
encodings occur in the SmartArt parts of `ooxmlimport/data/fdo87488.docx`.

```sh
python3 tests/office/generators/drawing-colors.py /tmp/office-color-probes
# then run the suite with --extras-dir /tmp/office-color-probes
#   --sample-list /tmp/office-color-probes/samples.txt --top-differences 20
```

## Cached SmartArt diagrams

The DOCX reader skipped the SmartArt in `ooxmlimport/data/fdo87488.docx`
although the package holds a saved DrawingML rendering. It now imports that
cache (base `9935963`) without a SmartArt layout engine:

- Data/cache parts are found through typed relationships (including
  nonconventional paths and data-part-local relationships), not a guessed name.
- Cached children stay in source order on the right body paragraph. Child
  origins/extents apply; an explicit cached root extent is already display EMUs,
  independent of the inline extent. Nested groups flatten within the depth
  limit.
- Ellipse and line presets (with flip composition), solid/gradient/theme fills
  and outline color/width are kept. Quarter-turn rectangles/ellipses rotate
  around their centers.
- Cached text keeps paragraphs, soft breaks and field display text inside the
  shape; alignment, indents, point/percentage line spacing, paragraph spacing,
  literal/theme Latin fonts, half-point sizes, bold/italic/underline/strike
  (including inherited and off values), explicit/theme run colors (font
  reference fallback), text insets and vertical alignment map to the model.
- A standalone inline diagram reserves its height (even if empty) and honors
  paragraph alignment without overwriting an exact line height.
- Cache parts use the XML parser and part-size limits, are parsed once per read
  and count toward an aggregate expansion budget derived from
  `MaxDocumentBytes` (relationship parts too). External parts are not fetched.
  Missing, malformed, oversized or unsupported caches are diagnosed.

Evidence: `fdo87488.docx` goes from a blank one-page rendering to two pages
like LibreOffice and its **pixel check passes** (blur 46.95% -> 3.2412%). A
cache-only marker test confirmed LibreOffice renders the saved drawing. Its
**text check now fails**: LibreOffice's TXT export omits cached diagram labels
while broilerdoc exports them; the earlier empty/empty match proved nothing.
Thresholds were not changed to hide this.

`tests/office/generators/cached-diagrams.py` derives 15 controls from the hashed
original (rewriting cache and body, keeping the data/layout graph): pixel
checks 0/14 -> 12/14; totals stay 29/43 because 12 text checks fail for the same
TXT reason. The relocated-cache control yields no native reference; the
root-child-space control fails because LibreOffice's first rendering ignores
the cached child-space mapping; the nested-group control's reference shows the
regenerated `text1`/`text2`, not the cached label. Exported as plain shapes,
all three match natively (to one pixel).

```sh
python3 tests/office/generators/cached-diagrams.py "$template" /tmp/diagram-probes
```

## Still open

- Legacy RTF aggregates, polygons, text boxes and running-content shapes.
- Picture-style references, SmartArt layout (no diagram is synthesized from the
  graph without a cache), effects, arbitrary paths, live theme editing, group
  rotation/reflection, text rotation and general affine transforms.
- Channel-specific red/green/blue color transforms; small rounding differences.
- Cached diagrams: unsupported presets/custom paths become rectangles; multi-stop
  fills, pictures, dynamic fields, list markers, links and DrawingML-only
  formatting are not supported; text opacity is flattened with a diagnostic;
  mixed inline text and table-cell placement are approximated; export flattens
  to ordinary shapes (no editable SmartArt). The TXT projection gap leaves the
  `fdo87488.docx` text check failing.
- Other high-impact groups were investigated without speculative fixes:
  floating/nested table pagination, linked text-frame overflow, SmartArt and
  custom VML geometry. The remote-background case points at an external HTTP
  image and is not a self-contained paint fixture.
