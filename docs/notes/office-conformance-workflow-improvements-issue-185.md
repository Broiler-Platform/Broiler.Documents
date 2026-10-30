# Office conformance workflow: improvements found during issue #185

These came up while resolving the first difference of
[issue #185](https://github.com/Broiler-Platform/Broiler.Documents/issues/185)
(`ooxmlexport/data/fdo68607.docx`, see the [issue note](office-issue-185.md)).
They continue the lists from
[issue #170](office-conformance-workflow-improvements.md),
[issue #173](office-conformance-workflow-improvements-issue-173.md),
[issue #175](office-conformance-workflow-improvements-issue-175.md),
[issue #178](office-conformance-workflow-improvements-issue-178.md),
[issue #180](office-conformance-workflow-improvements-issue-180.md) and
[issue #182](office-conformance-workflow-improvements-issue-182.md), and are
ordered by how much time they would have saved. All are proposals.

## 1. Read LibreOffice's rules from the reference's own release

**What happened.** The local copies of LibreOffice's sources used for earlier
issues came from `master`. Two of this issue's three rules differ between
`master` and 24.2.7.2, the release that drew the references:

- `DomainMapper_Impl::deferBreak` drops a *column* break inside a table only
  on `master` (tdf#163178); 24.2 splits the paragraph there. The first cut
  of this change followed `master`, and the replay's one text regression,
  `tdf163178_columnbreakInTable.docx`, was that difference.
- `DomainMapperTableHandler` moves a top-level table left by its cell margin
  where `nMode < 15` on `master` - a document stating no mode included - and
  only where `0 < nMode && nMode <= 14` in 24.2.

Implemented from `master`, the second would have moved every table of the
DOCX samples that state no compatibility mode, wrongly against the reference.
It was caught only because the 24.2 files were fetched
(`raw.githubusercontent.com/LibreOffice/core/libreoffice-24.2.7.2/...`) to check.

**Proposal.** Record the reference release's git tag in the reference
manifest (`office-references-v3/manifest.json` has the version string only),
and keep a script under `eng/` that fetches a named source file at that tag
into a scratch directory: `eng/lo-source.sh writerfilter/source/dmapper/DomainMapperTableHandler.cxx`.
Notes then cite the file at the tag, and a reader of a note can check it.

## 2. Switch a rule off in a replay without rebuilding

**What happened.** The first full replay of this change turned twelve
passing pixel checks to failures and fixed nine others. Which of four rules
caused which took temporary environment checks in the reader
(`DBG_NOWIDOW`, `DBG_NOPLACE`), a rebuild, and replays of the 22 flipped
samples with each switch set: the widow default alone caused every failure,
the table placement none. The switches were then taken out again.

**Proposal.** A supported, documented way to turn a named reading or layout
rule off - `BROILER_DOCUMENTS_DISABLE=widow-default,table-placement`, read
once by the codecs and the layout and listed in `broilerdoc version --json` -
for every rule that changes many samples at once. A replay then bisects a
change by rule rather than by commit, and the runner can record which
switches were set in its report. Together with replaying only the samples a
previous comparison flipped (`--sample-list` from the comparison script of
item 7), the loop is two minutes instead of twenty-five.

## 3. Say when a shape's text is not drawn

**What happened.** The title "IGCSE Drama Glossary" was missing from page 1.
`broilerdoc info` counted one shape and one shape paragraph, the render showed
the white box but no text, and nothing said why. Finding it took a throwaway
test that laid the document out and printed the shape's line count (zero):
the one 24-point line did not fit the box's 26.5-point text area and was
dropped silently.

**Proposal.** A layout diagnostic, `layout.shape.text-clipped`, raised when a
shape that holds text draws fewer lines than it has (with the count), carried
into `render --json` and the runner's per-sample diagnostics. The issue would
then have listed it beside `docx.pagebreak.split`, and the cause would have
been one line away.

## 4. Compare where each page starts and ends

**What happened.** After the text check passed, the pixel check still failed
and the worst page moved between runs (8, then 5, then 1). What showed the
cause was a montage of the top and bottom 60 pixels of every page, side by
side: LibreOffice started page 4 with the whole "Fourth wall" row,
`broilerdoc` with its second line. That is a pagination difference, and it
was invisible in the metrics, which compare page with page.

**Proposal.** For every sample whose page counts match, write the first and
last text line of each page for both renderers to the triage directory
(`pdftotext -f N -l N` gives the reference's; `broilerdoc dump` would need a
`--pages` form that splits its text at page boundaries), and report the first
page where they differ: "page 3 ends `Focus` / `Fourth wall` (first line)".
Most pixel failures with matching page counts are such a shift, and this
names the row or paragraph that moved.

## 5. Mark which earlier notes discuss the sample

**What happened.** The issue linked two earlier notes for this sample (the
columns verification of 2026-10-20 and issue #175's note). Neither discusses
it; it appears in their lists of replayed samples. The issue entry suggested
a history that did not exist.

**Proposal.** Link a note only where the sample appears outside a list of
replayed or sampled files (for instance, in a heading or a paragraph of
prose), or print the line that mentions it after the link, so a reader can
tell a finding from a passing mention.

## 6. Let a published runner find the repository

**What happened.** A full replay takes about 25 minutes, and rebuilding the
solution while `dotnet run --no-build` is running replaces the runner's
assemblies under it. A copy of the runner's build output in a scratch
directory refused to start ("Broiler.Documents component root not found"),
because `RepositoryRoot` walks up from the executable's directory; it had to
be copied into an ignored `bin/` folder of the checkout.

**Proposal.** Accept `--repo-root <path>` (or `BROILER_DOCUMENTS_ROOT`) and
fall back to the current walk. The runner can then be published once per
replay and the checkout rebuilt freely while it runs.

## 7. Keep the replay comparison script with the repository

**What happened.** Every issue since #175 compares a replay of `main` with
a replay of the change, and every time the script that lists check outcomes
that flipped and samples whose worst-page metrics moved is rewritten or
copied from the last scratch directory. This change set a document-wide
default (two-line widow and orphan control) that reaches 1,701 of the 1,800
DOCX samples, so that comparison was the evidence that decided it.

**Proposal.** Commit the comparison as `eng/compare-office-reports.py
<before report.json> <after report.json>`: outcome flips by group, then
metric moves sorted by size, then the samples whose page counts changed. The
issue notes' verification tables come straight out of it.

## 8. Running one test class

**What happened.** `dotnet test --filter "FullyQualifiedName~..."` runs no
tests on these projects (exit code 5): they run on Microsoft Testing
Platform with xUnit v3, which does not take that filter. The test executable
takes xUnit's own options:
`src/tests/<project>/bin/Release/net10.0/<project>.exe -class <full class name>`.

**Proposal.** Put that line in `docs/guides/` next to the instructions for
the full run.
