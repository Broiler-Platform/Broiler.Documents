# Issue #152: RTF groups, positioning and retained formatting

Starting from `07073fa`, ten additional reader defects are addressed. This is a
set of independently tested behaviors, not ten fully resolved ranked originals.

| Defect | Correction |
| --- | --- |
| Drawing-group children were lost or used the wrong coordinate space | Expand supported relative-only groups through parent origins/scales, including nested groups and independent child paint |
| Anonymous formatting scopes finished a shape early | Only the owning `shpinst` scope commits the shape, avoiding duplicate paint |
| Modern shape ordering was ignored | Read `dhgt`, while explicit `shpz` (including zero) retains precedence |
| Horizontal shape alignment was ignored | Resolve `posh` left/center/right against the known page or margin area |
| Vertical shape alignment was ignored | Resolve `posv` top/center/bottom for a fixed page-/margin-relative shape |
| Exact-height text frames centred or bottom-aligned at the top | Resolve `posyc` / `posyb` against page/margin height when the frame's exact height is known |
| Repeated clearance assignments could never reduce an earlier value | Keep the last value per edge, then derive the model's maximum clearance |
| Non-default underline controls lost the underline entirely | Retain an explicitly diagnosed single-underline approximation, including off toggles |
| Double-strike text lost its strike entirely | Retain an explicitly diagnosed single-rule approximation, including off toggles |
| Character shading background color was ignored | Read `chcbpat` into the run background; `plain` still resets it |

The group normalizer preserves the existing fallback, with a diagnostic, for
rotated/flipped groups, excessive group nesting, mixed absolute/relative child
bounds, missing coordinate bounds, aggregate group wrapping and unsupported
group-level content. Opaque metadata, running bands and notes are not rewritten
as body drawing groups. The generated child stream retains its source text and
properties and adds canonical twip geometry/reference controls. This is bounded
partial support, not a full RTF drawing-group model.

A text-sized or paragraph-relative frame still cannot be centred/bottom-aligned
from a known fixed page box; its previous approximation/diagnostic remains.
Patterned/word-only underline and double strike are not drawn with their exact
source geometry: the retained simpler decoration is explicitly reported.

## Original document result

In `rtfimport/data/tdf84684.rtf`, two formerly missing grouped shapes are now
retained. The ink-profile similarity improves **0 -> 0.9103**, the ink-box result
changes from blank-versus-inked to a **16-pixel delta**, and the coverage delta
improves **0.3615% -> 0.2218%**.

This original remains visually failing. Its Broiler/reference page sizes still
differ (612x792 versus 596x842 raster pixels at 72 DPI). The blurred difference
is mixed/worse, **0.1930% -> 0.4911%**, because a blank page previously happened
to have a small aggregate blur error while omitting its drawing. No complete
resolution or monotonic improvement of every metric is claimed.

All 30 ranked originals retain **40/89** passing checks, 49 failures and the
same skipped reference. No passing check regresses. The triage comparison records
only the RTF group original above as changed against the previous VML run.
The other ranked RTF original retains its preceding check outcomes/observations.

## Verification

The Release solution build and **4,130 unit tests** pass, with three additional
skips. The RTF project has **561 passing tests**, including **26 new cases**.
Tests cover transforms/origins, independent paint, nested coordinates,
reference/layer inheritance, ownership scopes, z-order precedence, fixed-height
alignment, last-value semantics, decoration toggles/background reset, and
unsupported/opaque-context fallback. Against the isolated pre-change RTF assembly, 23 of the new cases fail and
three unchanged paths pass; all 26 pass with the corrected implementation.

The CLI corpus passes **2,320/2,320**, with 105 documented skips and no baseline
changes.

Eleven authored native-reference controls improve **29/33 -> 32/33** checks.
All 11 read and pixel checks pass after the corrections. The group,
vertical-alignment and nested-group pixel checks newly pass; the one text-order
mismatch remains. Both sides use identical source paths/hashes, LibreOffice
26.8.1.1, poppler 26.05.0, 72 DPI, 80% threshold, a 40-page cap and a 30-second
reference timeout. Fonts are unpinned, so this is not exact reproduction of
issue #152's older native versions and 144-DPI settings.

| Probe | Before blur | After blur |
| --- | ---: | ---: |
| Relative drawing group | 4.142% | 1.509% |
| Duplicate translucent paint from nested scope | 2.588% | 1.438% |
| Modern stack order | 2.706% | 1.493% |
| Horizontal alignment | 4.306% | 1.441% |
| Vertical alignment | 4.094% | 1.468% |
| Exact frame alignment | 3.129% | 1.590% |
| Overridden clearance | 2.094% | 1.776% |
| Retained underline | 2.068% | 1.863% |
| Retained double strike | 1.806% | 1.730% |
| Character background | 1.976% | 1.838% |
| Nested group | 5.955% | 4.615% |

The broad pixel pass does not establish native equality: the nested-group probe
still has different geometry in LibreOffice (the native import paints the outer
200-point width while the model composes the relative 120-point child width).
Flat-group colored pixel bounds agree within one raster pixel. Initial probes
that combined relative and absolute child bounds were rejected and are not the
final comparison.

Local artifacts under `/workspace/.setup`: `make-issue152-rtf-group-probes.py`,
`issue152-rtf-group-probes`, isolated `issue152-rtf-groups-before-bin`,
`issue152-rtf-groups-final-before/after.json` and matching triage/work directories,
`issue152-rtf-groups-originals.json`, focused original reports, old-assembly tests,
and final build/unit/corpus logs. No threshold, baseline classification or native
tool approval changed.
