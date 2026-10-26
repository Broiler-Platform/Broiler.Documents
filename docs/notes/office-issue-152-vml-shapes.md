# Issue #152 follow-up: ten VML shape-reading corrections

Starting from `f30762f`, this change addresses ten independently tested DOCX/VML
behaviors. These are import defects, not ten fully resolved ranked originals.

| Problem | Correction |
| --- | --- |
| Only the first drawn object in one `w:pict` survived | Read every supported sibling, retaining the existing first-image fallback in mixed blocks |
| VML groups were skipped | Map child coordinates through group origin, nonuniform scale and nested groups into point-based shapes |
| Referenced shape definitions were ignored | Resolve local type chains and inherit paint/geometry defaults, with instance attribute and fill/stroke overrides |
| Fill opacity was discarded | Read decimal, percentage and 65536-based opacity, including independent gradient-end alpha |
| Stroke opacity was discarded | Retain outline alpha independently of color and width |
| Polyline CSS translation was ignored | Apply its left/top and margins in addition to explicit point coordinates |
| Wrapping lost its side and clearance | Retain the representable side and maximum clearance |
| Named horizontal placement was ignored | Resolve left/center/right against page or text area |
| Named vertical placement was ignored | Resolve top/center/bottom against page or margin area |
| Line flips were ignored | Compose horizontal/vertical mirroring; normalize unitless line endpoints as pixels, while explicit points remain points |

Native probes establish an important distinction: VML `line` endpoints already
state the position and do not receive the CSS translation used by `polyline`.
Shape-type layout dimensions are also not inherited as though they were instance
CSS: the shape states its own dimensions. Both details are kept in the tests.

Type cycles, unusable group dimensions/origins, excessive nesting and unsupported
group transforms are bounded or diagnosed. Rotated/mirrored groups, grouped lines
and physical-unit child coordinates remain unsupported and are not silently
flattened with a guessed transform. Images retain the existing first-image
fallback; this is not full grouped-image support. Group-level named alignment
and precise VML path geometry remain limitations. A single model wrap distance
uses the maximum of asymmetric clearances; both-side/contour wrapping remains an
approximation. Export uses the existing DrawingML shape writer.

## Verification

The Release solution build and **4,104 unit tests** pass, with three additional
skips. The DOCX test project has **650 passing tests**, including **26 new cases**.
Against the isolated old DOCX assembly, 22 of those cases fail and four unchanged
paths pass. Coverage includes mixed image/shape blocks, nested origins/scales,
inheritance/overrides, opacity encodings, placement, line/polyline differences,
cycle/depth guards and DrawingML export round trips.

The CLI corpus passes **2,320/2,320**, with 105 documented skips and no baseline
changes.

Eleven minimal DOCX controls use explicit `position:absolute`, identical source
paths/hashes, LibreOffice 26.8.1.1, poppler 26.05.0, 72 DPI, the 80% threshold,
a 40-page cap and a 30-second reference timeout. Fonts are unpinned. The results
improve from **32/33 to 33/33**: the vertical-placement pixel check newly passes.
All read and text checks pass on both sides. Most geometric improvements were
already inside the broad threshold, so they are not counted as newly passing
checks.

| Probe | Before blur | After blur |
| --- | ---: | ---: |
| Sibling objects | 5.872% | 4.520% |
| Group coordinates | 3.631% | 3.507% |
| Inherited type paint | 4.288% | 3.284% |
| Fill alpha | 4.467% | 3.329% |
| Stroke alpha | 4.121% | 3.806% |
| Polyline translation | 3.617% | 3.468% |
| Horizontal placement | 5.840% | 3.247% |
| Vertical placement | 6.141% | 3.511% |
| Line mirroring/units | 3.758% | 3.478% |

The wrapping probe has a mixed measurement (3.759% to 4.100% blur), with its
mode/side/distance retained by the model and export. The additional absolute
polyline control improves 3.626% to 3.567% blur; native pixel bounds support
retaining its CSS margin rather than ignoring it.

## Original control group and remaining differences

All 30 issue #152 originals retain **40/89** passing checks, 49 failures and the
same skipped reference. No passing check is lost, and no further ranked original
is claimed resolved. These local results are not reproduction of the issue's
older LibreOffice/poppler versions and 144-DPI settings.

One original, `ooxmlexport/data/tdf81345.docx`, has mixed/worse visual observations
on the same worst page: ink-box delta 22 to 43 px, profile 0.77043 to 0.76800,
coverage delta 3.9201% to 4.1412%, and blur 29.8011% to 29.8040%. Its page count
remains 7 versus the reference's 5, and its check outcomes are unchanged. The
observations reproduce in an isolated before/after rerun. Public document-model
serialization (including shape properties and reference sharing) is identical
for the old/new readers; the exact rendering cause is not established. This
residual difference is recorded rather than weakening a threshold or claiming
that unchanged verdicts imply identical rendering.

Local artifacts under `/workspace/.setup`: `make-issue152-vml-probes.py`,
`issue152-vml-probes`, the isolated `issue152-vml-before-bin`,
`issue152-vml-final-before/after.json` and matching triage/work directories,
`issue152-vml-final-originals.json`, the single-original control reruns,
old-assembly regression tests, model-inspection/isolation outputs, and final
build/unit/corpus logs. Earlier probes lacking absolute positioning are not the
final comparison. No baseline tolerance, classification or tool approval changed.
