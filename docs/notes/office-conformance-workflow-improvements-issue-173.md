# Office conformance workflow: improvements found during issue #173

These came up while resolving the first difference of
[issue #173](https://github.com/Broiler-Platform/Broiler.Documents/issues/173)
(`ooxmlexport/data/tdf77417.docx`, see the [issue note](office-issue-173.md)).
They continue the [list from issue #170](office-conformance-workflow-improvements.md)
and are ordered by how much of the ranking they distort. Item 1 is fixed in
the same change. The others are proposals.

## 1. The text check compared against the wrong table projection

**What happened.** LibreOffice's text export writes a table differently
depending on the release. Up to 24.2 it writes each cell's paragraphs on lines
of their own. From 24.8 on (tdf#144576, May 2024, `SwASCWriter::WriteTable`)
it writes a row on one line with its cells separated by tabs. On 2026-10-22
(`61fe0ef`), `broilerdoc dump --as text` was changed to join rows, which
matched the LibreOffice 26.2 installed on the machine the triage ran on. The
CI references come from 24.2. From then on, the text check of every sample
with a table compared a row-per-line projection with a cell-per-line one, and
failed on line breaks alone. `docs/guides/cli.md` described both projections
at once.

**Evidence.** In issue #173 the first ranked sample's 21 text differences were
exactly this: `- broilerdoc: xxxxx $ 00,000 $ 00,000 …` against one
`+ libreoffice:` line per cell. Across the corpus, scored against
`office-references-v3`, the text checks of `db90c69` (main) and of this change
compare as follows. Only the text projection changed for most of them; the
reader changes in this issue touch a handful of samples.

| Text checks (3,907 samples) | Before | After |
| --- | ---: | ---: |
| Passed | 3,162 | 3,452 |
| Failed | 701 | 411 |
| Skipped | 44 | 44 |

290 text checks pass that failed, and none fails that passed. Of the 30
samples ranked in issue #173, 28 failed their text check here before the
change and 21 of those now pass, so most of the ranking's text differences
were this projection and not lost content.

**Fixed here.** `dump --as text --cells lines` writes a cell per line, and
the runner passes it whenever the reference LibreOffice is older than 24.8
(`TextProjection.DumpArguments`, from the reference manifest's version, or the
local LibreOffice's when there is no archive). Both the CI references and a
newer local LibreOffice are now compared with the projection they wrote.

**Proposed next steps.**

- **Tune projections against the CI references, not the host's LibreOffice.**
  `--references-dir` with `--accept-reference-mismatch` (issue #170) now lets
  a developer score against the CI archive on any host. The guide's triage
  recipe should make that the default, and say that host LibreOffice output is
  only a second opinion. The regression came from fitting `dump` to a newer
  LibreOffice than the one the nightly run compares with.
- **Keep the version-dependent rules in one place.** `TextProjection` and
  `ReferenceDefects` both key behaviour on the reference LibreOffice's
  release. A short table in the guide of what changed between 24.2 and the
  corpus revision in LibreOffice's text export (tables so far) would stop the
  next rule from being made the other way round.
- **Say when a text difference is only line breaks.** When the two canonical
  projections are equal once newlines are read as spaces, the report could
  label the difference "line breaks only" and weigh it less. It would have
  shown this regression on the first ranking after it.

## 2. A local render does not reproduce the ranked one

**What happens.** The issue's re-run line for the pixel check elides the font
arguments ("46 mappings elided; the full command is in the JSON report"). The
obvious reproduction, `broilerdoc render <file> --font-dir <fonts>`, maps faces
by file name only, so `Arial`, `Times New Roman` and the other metric aliases
in `FontPinning` fall back on the host's face. In this investigation that drew
every Arial run of `tdf77417.docx` in a Windows face, overlapping in the bold
headings, and looked like a fourth defect until the runner's own render was
inspected.

**Proposal.** Either of:

- The runner writes the font mapping beside its report as a response file
  (`fonts.rsp`), and the re-run line names it, so the printed command is
  complete and short. `broilerdoc` would need to accept `@file` arguments.
- `broilerdoc render` takes `--font-aliases libreoffice`, the same metric
  aliases `FontPinning.MetricAliases` gives, so `--font-dir` plus that flag
  is the whole pinning.

## 3. Compatibility settings caused two issues in a row

Issue #170's largest difference came from Word 2013's justification
(`compatibilityMode` 15). Issue #173's came from Word 2010's: `compatibilityMode`
14 makes LibreOffice drop white space ending a table cell's paragraph and turn
on `TabOverMargin`. Neither the report nor `broilerdoc info` said what mode a
document states. The #170 note proposed a per-sample note of compatibility
settings; this makes it concrete:

- `broilerdoc info --json` reports the layout switches the reader set
  (`DocumentStyleDefaults`: `shrinksJustifiedLines`, `tabsOverMargin`,
  `widensSpaceRuns`) and the stated `compatibilityMode`.
- The ranked entry prints them beside the diagnostics, for example
  "Word compatibility: mode 14 (tabs over margin, cell white space trimmed)".

## 4. The upstream test says what the sample is for

Every corpus sample has a unit test beside it in `sw/qa/extras/**/*.cxx`, and
that test usually names the bug and what used to go wrong.
`testTdf77417` says: "MSO 2010 compatibility mode: terminating white spaces are
ignored in tables. This was 3 pages with the first invisible blank page." That
is the issue's ranked difference, three pages and a blank first one, with its
cause. The reference workflow already clones `sw/qa/extras`. The report could
quote, per ranked sample, the test functions that load it and the comment
lines inside them, the way "Earlier Notes" links prior triage. It would point
the first reader at the mechanism before any rendering is compared.

## 5. Corpus-wide validation is done with ad-hoc scripts

Issues #168, #170 and #173 each validated their change the same way: render
every sample at low resolution with the old and the new build, keep the
samples whose pages changed, prepare or reuse references for those, and score
both builds on them. The scripts live outside the repository and differ a
little each time. On this host the two renders and the two text runs took about an hour together. A runner
mode such as `--changed-against <tool>` that does the low-resolution scan
itself, scores only the samples whose pages or text changed, and prints the
before and after table would make that step the same for every change and
cheaper to review.
