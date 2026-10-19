# Roadmap implementation readiness — 2026-10-19

Investigation against the current working tree, including the uncommitted
[decision record](roadmap-decisions-2026-10-19.md). This is an implementation
assessment, not a new policy decision or release approval. Existing documentation
edits were preserved. No runtime code was changed.

## Work that can start now

| Priority | Work | Safe boundary and acceptance evidence |
|---|---|---|
| 1 | Editor save-point tracking | Add an explicit save-point operation and a modified-state query to `RichTextEditor`. Compare immutable document references and end the typing group when marking saved. Test edit/save/edit/undo/redo, branching after undo, history trimming, and load/reset behavior. Host wiring follows separately; this alone does not fix the Writer UI. |
| 2 | RTF list-table detection | Recognize `listtable` and `listoverridetable` and report unsupported list semantics explicitly while keeping destination text out of the body. The selected decision says detection; do not silently broaden it into complete numbering, restart, or nested-marker fidelity. Add authored fixtures for ordinary and ignorable destinations, malformed tables, nesting and limits. |
| 3 | RTF style-sheet interpretation | Resolve paragraph and character style references into the inline/paragraph properties the model already supports. Define and test inheritance, direct-formatting precedence, reset controls, missing references and cycles. Diagnose unsupported properties. This is a bounded codec change, not a new persistent named-style model. |
| 4 | CSV command-line options | Expose the existing read delimiter/header and write delimiter/BOM/formula-escaping options. Preserve current defaults. Test explicit and detected CSV, invalid arguments, and conversion/roundtrip routing. Keep input and output delimiter choices distinct. Do not implement deferred dialect persistence or `sep=` behavior. Existing CSV register and support-claim gates remain in force. |
| 5 | Parser robustness tests | Extend deterministic mutation/property tests using repository-authored fixtures and existing dependencies. Check bounded failure, truncation, nesting and stable normalization. This does not require choosing a new fuzz engine or importing a foreign corpus, and does not satisfy the sustained campaign release gate. |

Evidence: `RichTextEditor` already restores the exact `Before`/`After` document
references during undo/redo and exposes `BreakUndoGroup`, but holds no saved
snapshot. `RtfReader` currently routes style sheets and list tables to `Skip`.
`CsvReadOptions` and `CsvWriteOptions` implement the requested settings already;
CLI `DocumentOptions` still constructs only shared options. CSV option selection
must account for catalog probing: constructing CSV-specific options for every
input would be inappropriate for mixed-format commands.

## Selected work that needs a staged implementation

**HTML/Markdown embedded images:** begin with bounded embedded-payload parsing,
resource admission and denial tests. Do not enable automatic display as a
reader-only change. `DocumentResourcePolicy.Default` already grants transient
decode and model extraction, and pagination's `ImageStore` decodes model images
without receiving a consent policy. Explicit host consent must precede display;
opening the document or using the default resource policy is not that consent.
Test denial, malformed data URIs, encoded/decoded size limits, pixel limits and
cancellation. Relative targets remain deferred; do not introduce network fetches
as an incidental part of embedded-image support. Host integration is outside
this checkout.

**PDF font embedding/subsetting:** the approval permits implementation, but is
not an end-to-end implementation specification. `DocumentFontEmbedding` already
checks caller permission and declared embedding restrictions, with eight tests.
The remaining pipeline needs trustworthy inspection of the actual font bytes,
caller-supplied provenance, obligation handling, Graphics export/subsetting
services, shaping and shared pagination, followed by PDF font emission and
preflight. Its XML remarks still say embedding is outside IP-012; updating that
stale comment is straightforward. Keep host PDF output disabled until its gates
pass. Do not equate passing the existing permission tests with validating font
programs or implementing subsetting.

**Seven office render defects:** reproduction and targeted fixes are selected.
The baseline points to footer placement for `letterhead-frames` and accumulated
leading for `line-break-within-paragraph`. These are hypotheses from stored
evidence, not freshly reproduced causes. Compare page geometry and font metrics
before changing layout defaults; add focused pagination regressions and rerun
the applicable office evidence. Preserve `suspected-defect` until a real fix and
new evidence justify updating the row. Do not merely widen tolerance or relabel
the differences. Detailed pagination policy remains deferred.

**CSV in Writer:** plain-text presentation is decided, but integration belongs
to the application repository and requires the preview package and applicable
register gates. Preserve the codec's table model. The presentation adapter must
avoid losing CSV quoting and delimiters; converting a table to generic plain
text and saving it back is not automatically a faithful CSV editing path.

**XLSX/ODS:** prepare the model proposal required by ADR 0016. Typed cells,
formulas and cached values, sheet identity, number formats, resource limits and
the rich-text projection need explicit contracts before codec implementation.
Selecting both formats does not settle those contracts.

## Leave gated or deferred

- API freeze and deprecated-member removal: ADR 0014 requires a separate
  announcement release before removal. Verify publication history before
  scheduling `DecodeEmbeddedObjects` removal; do not infer eligibility from its
  presence in source.
- HTML list/table/CSS/header expansion, relative-link policy, CSV dialect
  persistence, `sep=`, TSV, spooling and CLI partial-read behavior remain deferred.
- Pending source/register approvals, ADR adoption, external oracle/corpus/fuzz
  engine selection and performance thresholds cannot be supplied by engineering
  judgment. Preparation is possible; approval is not implied.
- PDF write enablement, support claims and stable publication retain all phase
  gates. Font approval does not waive them.
- Browser/desktop consent, cancellation, conversion-context retention and atomic
  save integration require the owning application checkout. This investigation
  did not verify those hosts.

## Verification and documentation drift

Focused existing tests passed on .NET SDK 10.0.401: 22 editor undo-grouping tests,
27 RTF conformance tests and 8 font-embedding-policy tests. Initial concurrent
builds collided on a shared output DLL; sequential retries passed. These checks
establish a small current baseline, not coverage of proposed features. The full
suite and office render suite were not run.

The roadmap's hardening wording should acknowledge existing `RtfFuzzTests`:
RTF already has deterministic random-input, mutation and roundtrip tests, though
these are not a sustained supervised fuzz campaign. Its long-document layout
item also calls the engine CLI-owned; the implementation now resides in
`Broiler.Documents.Pagination`. These are safe documentation corrections to
include with the corresponding implementation work.

Recommended first batch: save-point tracking, RTF list detection and bounded
style interpretation, then CSV CLI options. Keep each change independently
reviewable with its own fixtures and update the roadmap only for the scope
actually completed.
