# Issue #154 follow-up: 15 saved-diagram import behaviors

The DOCX reader previously skipped the SmartArt graphic in
`ooxmlimport/data/fdo87488.docx`, even though its package contains a saved
DrawingML rendering. This follow-up imports that saved rendering without
implementing a SmartArt layout engine. The preceding revision is `9935963`.

## Corrected cases

| # | Import behavior | Retained result |
| --- | --- | --- |
| 1 | Resolve each diagram's data/cache relationships | Follow typed embedded parts, including nonconventional paths and data-part-local relationships, instead of guessing a drawing filename. |
| 2 | Multiple cached shape children | Preserve siblings in source order and attach them to the correct body paragraph. |
| 3 | Root coordinate mapping | Apply cached child origins/extents; an explicit cached root extent is already in display EMUs and is independent of the inline flow extent. |
| 4 | Nested groups | Flatten supported nested coordinate transforms through each ancestor, within the depth limit. |
| 5 | Ellipse presets | Preserve elliptical paint geometry. |
| 6 | Line presets and reflections | Preserve line geometry, including horizontal/vertical flip composition. |
| 7 | Shape fills | Retain supported solid/gradient paint and resolved theme colors through the existing shape reader. |
| 8 | Outlines | Retain explicit outline color and pen width. |
| 9 | Cached text | Retain separate paragraphs, soft breaks and field display text inside the shape rather than dropping it or leaking it into the body. |
| 10 | Paragraph formatting | Map alignment, indents, point/percentage line spacing and paragraph spacing into the paragraph model. |
| 11 | Fonts and sizes | Resolve literal/theme Latin fonts and map common point sizes through Word's half-point representation. |
| 12 | Emphasis | Retain bold, italic, underline and strike flags, including inherited defaults and explicit off values. |
| 13 | Text colors | Resolve explicit/theme run colors, with the font reference as a fallback. |
| 14 | Text frames | Retain explicit text insets and vertical alignment. |
| 15 | Standalone inline blocks | Reserve the diagram's height, including an empty cached diagram, and honor paragraph alignment without overwriting an explicit exact line height. |

Quarter-turn rectangle/ellipse paint boxes are mapped around their centers;
text rotation and general affine transforms are not implemented. Cache parts are
loaded with the existing XML parser and part-size limits, parsed once per read,
and each reference counts toward an aggregate XML expansion budget derived from
`MaxDocumentBytes`. Relationship parts participate in the same budget. External
parts are not fetched. Missing, malformed, oversized or unsupported caches
produce diagnostics while supported body content is retained.

## Original-file evidence

The original `fdo87488.docx` improves from a blank **one-page** rendering to
**two pages**, matching the local LibreOffice reference. Its **pixel check now
passes** at the existing 80% threshold:

| Observation | Before | After |
| --- | ---: | ---: |
| Pages, broilerdoc / LibreOffice | 1 / 2 | 2 / 2 |
| Ink box | blank/inked mismatch | 7 px delta |
| Ink-profile similarity | 0 | 0.9997 |
| Coverage delta | 19.33% | 0.5752% |
| Blur difference | 46.95% | 3.2412% |

The retained `text1` and `text2` are visible in the native PDF. A cache-only
marker experiment also confirmed that LibreOffice reads the saved drawing:
changing a cached label, while retaining the data model's original text, changed
the native PDF label.

**The raw text check changes from pass to fail.** LibreOffice's TXT export omits
these cached diagram labels, while broilerdoc now exports their retained text.
The earlier empty/empty match was not evidence that the diagram text was read.
Neither thresholds nor text checks were changed to hide this projection gap.
The complete 30-original selection consequently remains **38/89**, 51 failed
and the same one skipped native reference. One pixel check is gained and one
text check is lost; no other original check outcomes or render observations
change. This is not a claim that the original is fully conformant.

## Native controls and consumer differences

`tests/office/generators/cached-diagrams.py` creates 15 deterministic derived
controls from the hashed public original. It rewrites the saved drawing and body
while preserving the original diagram data/layout graph. This deliberately tests
cache handling; a consumer that regenerates the graph may produce another image.

- Fourteen controls have a native pixel reference: **0/14 -> 12/14** pixel checks.
- The relocated-cache control produces no native PDF or TXT reference in the
  tested LibreOffice. Its relationship lookup is covered by unit tests and a
  native export check; it is not counted as a native source-render pass.
- The root-child-space control and nested-group control remain failing native
  source comparisons. The first native source rendering does not apply the
  cached child-space mapping used by the model. The nested control's native PDF
  contains the original graph's `text1`/`text2`, rather than its cached `nested`
  label: this reference uses regenerated content.
- Exporting these three model results as ordinary DOCX shapes gives LibreOffice
  the intended retained content. Native export ink boxes agree with broilerdoc:
  relocated cache `(90,72)-(390,272)` exactly; nested group
  `(150,122)-(230,202)` exactly; mapped root `(165,122)-(202,147)` versus
  broilerdoc `(165,122)-(203,147)`, a one-pixel edge-rounding difference.
- Native PDFs for the other text controls contain the authored cache labels,
  including `left/right`, `first/cached/second`, `Serif 24` and both `block`
  labels on separate pages. Their TXT projections omit the cache labels.

The raw native-control total remains **29/43** with two skipped checks: 12 pixel
checks improve, while 12 text checks change to failure. Eleven of those text
losses have the authored labels in the native PDF but not its TXT projection;
the nested-group control also has the regeneration difference described above.
Passing pixel checks remain tolerance checks, not exact visual equivalence.

## Automated validation

- Release solution build passed.
- **4,299 unit tests passed**, three additional skips.
- **29 new regression cases**, all 29 failing against the preceding isolated
  DOCX assembly. Coverage includes each supported behavior, export retention,
  bad relationships, malformed parts, group/object/size limits and repeated
  reference expansion.
- CLI corpus: **2,320/2,320 passed**, 105 documented skips.
- No baseline expectations, tolerances or external-tool approvals changed.

The measured environment is Linux, LibreOffice 26.8.1.1, Poppler 26.05.0,
72 DPI, 80% threshold, 40-page cap and a 30-second native timeout. Fonts are
unpinned; issue #154 used other external-tool versions at 144 DPI.

## Reproduction and limits

Fetch `ooxmlimport/data/fdo87488.docx` from LibreOffice's public `sw/qa/extras`
corpus. The generator checks its SHA-256 against the committed issue-154 manifest
and requires only Python's standard library; no binary template is committed.

```sh
python3 tests/office/generators/cached-diagrams.py "$template" /tmp/diagram-probes
(cd /tmp/diagram-probes && sha256sum -c samples.sha256)
dotnet run --project src/tests/Broiler.Documents.Office -c Release --no-build -- \
  --extras-dir /tmp/diagram-probes --sample-list /tmp/diagram-probes/samples.txt \
  --soffice "$soffice" --jobs 3 --dpi 72 --max-pages 40 --lo-timeout 30 \
  --top-differences 20 --triage-dir diagrams-triage --report diagrams.json \
  --workspace diagrams-work --keep
```

Use an independently built `9935963` CLI through `--tool` for the before run,
with separate report/workspace/triage paths. Repeated generator runs in the same
environment produce identical package hashes.

No diagram is synthesized from its graph when a usable saved drawing is absent.
Unsupported presets/custom paths are approximated by rectangles, multi-stop
fills retain existing approximations, and effects, pictures, group reflections,
arbitrary rotations, separate text transforms and dynamic field evaluation are
not implemented. List-marker synthesis, DrawingML-only formatting variants and
links remain outside this subset. Text opacity is flattened with a diagnostic;
font sizes are quantized to half points. Mixed inline text and table-cell
placement remain diagnosed approximations. The bridge uses the existing Word
paragraph/style model; full DrawingML typography and layout equivalence are not
claimed. Export flattens the retained drawing to ordinary shapes and does not
preserve an editable SmartArt graph.
