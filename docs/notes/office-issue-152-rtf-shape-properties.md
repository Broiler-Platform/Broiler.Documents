# Issue #152 follow-up: ten RTF shape-property corrections

Starting from `17c6c81`, ten additional RTF import/export defects are corrected.
These are ten independently tested behaviors, not ten resolved ranked originals.
The ranked RTF graphics cases motivated inspection of this shared shape path.

| Problem | Corrected behavior |
| --- | --- |
| A later color re-enabled a disabled fill | Explicit `fFilled=0` wins regardless of color-property order |
| Horizontal line mirroring was ignored | `fFlipH` composes with `fFlipV` and signed endpoint orientation |
| Horizontal page-relative coordinates were ignored | `posrelh=1` converts page offsets to the model's text-column origin |
| Text-box padding was discarded on import/export | All four EMU-valued text insets are retained and emitted |
| Vertical text placement was lost | `anchorText` maps to top/center/bottom and is emitted on export |
| Fill alpha became opaque | Independent fill/gradient-end opacity survives reading and writing |
| Outline alpha became opaque | Outline opacity survives reading and writing independently of width/color |
| A shape with omitted fill properties became transparent | Ordinary drawn shapes default to white, matching Word and the native reference; picture frames keep their existing fallback |
| Foreground shapes were read behind text | `shpfblwtxt` / `fBehindDocument` select a layer independently of z-order; the writer emits that layer |
| Text wrapping was discarded | Square/top-and-bottom/none mode, representable side and clearance survive reading and writing |

A candidate change to omit `fHidden` shapes was rejected: LibreOffice's import
still paints the authored object, and omission regressed a previously passing
visual check. The unchanged hidden-shape probe remains in the final control set.
No hidden-object preservation is claimed by this change.

## Validation

There are **24 new unit cases**. Against the isolated pre-change RTF assembly,
17 fail and seven existing/default paths pass; with the corrected implementation
all 24 pass. The RTF project has **535 passing tests**. The complete Release
solution build and **4,078 unit tests** pass, with three additional skips.
The CLI conversion corpus passes **2,320/2,320**, with 105 documented skips,
without changing its baseline.

Eleven authored RTF controls (ten behaviors plus the unchanged hidden-shape
control) use identical paths and input hashes before/after, LibreOffice
26.8.1.1, poppler 26.05.0, 72 DPI, the 80% threshold, a 40-page cap and a
30-second reference timeout. Fonts are unpinned. They improve from **30/33 to
31/33** passing checks, with all **11/11 pixel checks passing** after the change.
No passing check is lost. The two text comparison failures remain; no complete
text-box extraction equivalence is claimed.

Selected observed blur differences, for the same single page:

| Probe | Before | After |
| --- | ---: | ---: |
| Disabled fill followed by color | 6.53% | 2.39% |
| Horizontally flipped diagonal | 3.05% | 2.30% |
| Horizontal page origin | 5.45% | 2.24% |
| Half-transparent fill | 6.36% | 2.31% |
| Outline transparency | 4.25% | 3.71% |
| Wrapping | 3.83% | 2.70% |
| White default fill | 4.21% | 1.85% |

These measurements do not all represent newly passing checks: only the first
probe gains one. The explicit-inset probe has a small mixed visual result
(2.46% to 2.52% blur), while its exact padding values are retained.
Native import of the exported RTF confirms 20/10/20/0-point insets (rounded to
ODF output units) and 50% fill opacity. The tested LibreOffice import does not
retain `anchorText` or `lineOpacity` in its exported FODT: those valid RTF
properties round-trip in Broiler but native-consumer fidelity remains limited.

The full 30-original issue #152 control group retains **40/89** passes,
49 failures and the same skipped PDF reference. Its triage comparison records
matching hashes/environment and **no changed original observations/checks**.
No additional ranked original is claimed resolved.

## Remaining representation limits and artifacts

The model has one wrap distance, so asymmetric clearances use their maximum and
are exported symmetrically. Both-side/contour wrapping is approximated by the
representable side/rectangle modes; contour approximation is diagnosed.
Page-to-column horizontal conversion retains the existing limitation that page
margin and column origins can differ in multi-column flow. Full RTF drawing-group
coordinate mapping, grouped text extraction, hidden objects and native-consumer
support for all shape properties remain outside these corrections.

No Office thresholds, baseline classifications or approved tools were changed.
Local artifacts under `/workspace/.setup` include `make-issue152-ten-probes.py`,
`issue152-ten-probes`, the isolated `issue152-ten-before-bin`,
`issue152-ten-verified-before/after.json` and corresponding triage/work directories,
`issue152-ten-originals.json`, native-export FODT controls, the old-assembly
regression log, and final build/unit/corpus logs. The ten pre-change defects are
also directly reproducible through `RtfShapePropertyTests`.
