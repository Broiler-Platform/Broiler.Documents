# Office conformance workflow: improvements found during issue #175

These came up while resolving the first difference of
[issue #175](https://github.com/Broiler-Platform/Broiler.Documents/issues/175)
(`layout/data/floattable-center-shift-down.docx`, see the
[issue note](office-issue-175.md)). They continue the lists from
[issue #170](office-conformance-workflow-improvements.md) and
[issue #173](office-conformance-workflow-improvements-issue-173.md) and are
ordered by how much of the ranking they distort. Items 1 and 2 are fixed in
the same change. The others are proposals.

## 1. The text check counted a floating table's cells as body text

**What happened.** LibreOffice imports every table with a position (DOCX
`w:tblpPr`, RTF `\tposx` and its kin, an ODT table in a `draw:frame`) as a text
frame holding the table, and its text export writes the body without frames.
`broilerdoc dump --as text` already left text boxes and frames out, but wrote a
floating table's cells where the table stands. Every sample with a floating
table failed its text check on lines LibreOffice never writes. The issue showed
them as 21 lines of `- broilerdoc: x` and none from LibreOffice.

**Evidence.** Scored against `office-references-v3`, the corpus's text checks
compare as follows between `90903ee` (main) and this change:

| Text checks (3,907 samples) | Before | After |
| --- | ---: | ---: |
| Passed | 3,452 | 3,499 |
| Failed | 411 | 364 |
| Skipped | 44 | 44 |

47 DOCX samples with a floating table pass, and none fails that passed. Seven
of them are in issue #175's top 30 (ranks 1, 5, 7, 12, 13, 21, 22). Like
issue #173's table rows, this was a projection rule that pulled samples
into the ranking with text that was not missing.

The four checks a first version broke are a reminder of how this has to be
checked. The full-corpus text run takes about ten minutes without
LibreOffice, and it found the four at once. Unit tests built from the
description of `SwASCWriter` alone would not have found them.

**Proposal.** Run `--axis semantic` against the reference archive on pull
requests that touch `dump`, a reader or `TextProjection`, and fail on any text
check that passed on main and fails now. It needs no LibreOffice, only the
archive the nightly workflow already caches. It would have caught issue
#173's regression, which broke about 290 text checks, and the four checks this
change's first version broke.

**Fixed here.** `dump --as text` leaves out a top-level floating table's cells
and keeps LibreOffice's exception for a body of one empty paragraph. A
floating table nested in a cell is still laid out and written in the cell (see
the [roadmap](../roadmap.md)).

## 2. A traced defect could not be listed without a tdf number

**What happened.** The reference-defects file took only `bug: "tdf#N"`. The
fix this sample tests, LibreOffice core `25ba87fd` (review 205157), names no
bug; it was first reviewed on Collabora's Gerrit and cherry-picked. The rule as written would have
kept a fully traced reference defect in the ranking, at the top, for want of a
Bugzilla number.

**Fixed here.** `bug` also takes `gerrit#N`, the core review that merged a fix
that names no bug, in the schema, the guard and the policy text.

## 3. The same sample was triaged four times

**What happened.** The notes for issues #136, #143, #145 and #147 each looked at
`floattable-center-shift-down.docx`. #143 already wrote "reference-version
dependent: 4 vs 3 pages with LibreOffice 25.2, but 4 vs 4 with 26.8", and #147
still listed it under "floating-table avoidance and text flow". The reference
defects list arrived with #170, after all of them, and nobody went back over
the earlier notes. "Earlier Notes" links them, but the reader still has to open
four files to find the one sentence that matters.

**Proposals.**

- **Sweep the notes once.** Search `docs/notes/` for "reference-version",
  "26.8", "LibreOffice 25" and "upstream test" next to a sample name, and for
  each hit either trace it to a fixed upstream commit and add a row, or write
  down why it is not one. Other ranked samples the notes call
  reference-version dependent may be in the same state.
- **Let a note carry a verdict.** A short machine-readable line in a note, for
  example `<!-- verdict: floattable-center-shift-down.docx reference-defect -->`
  or `... open: floating-table avoidance`, would let "Earlier Notes" print the
  latest verdict next to the links instead of only the file names.

## 4. A sample added upstream after the reference release is a likely defect

**What happens.** The corpus is LibreOffice's master tree and the references
come from 24.2, which branched on 2023-12-08 (merge base `898d5d47`). Upstream
adds a sample with the fix it tests, so a sample added after that date usually
pins behaviour 24.2 does not have. Of issue #175's top 30, 11 were added to
`sw/qa/extras` after 24.2 branched (earliest commit on the path, through the
GitHub API):

| Rank | Sample | Added upstream |
| ---: | --- | --- |
| 1 | `layout/data/floattable-center-shift-down.docx` | 2026-05-13 |
| 2 | `tiledrendering/data/page-background-link.fodt` | 2026-05-22 |
| 3 | `ooxmlexport/data/wDateValueFormat.docx` | 2025-11-25 |
| 5 | `uiwriter/data/floattable-split.docx` | 2025-09-03 |
| 8 | `layout/data/tdf170381-split-float-table-in-normal-table.docx` | 2026-01-19 |
| 9 | `ooxmlexport/data/tdf126533_pageBitmap.docx` | 2024-02-21 |
| 15 | `ooxmlexport/data/cloud.docx` | 2024-05-22 |
| 18 | `layout/data/Cool16066_headlineAndUnsplittableRow.fodt` | 2026-08-18 |
| 22 | `layout/data/fdo48718-1.docx` | 2024-06-26 |
| 27 | `layout/data/pageBreakInHiddenSection.fodt` | 2024-02-14 |
| 30 | `layout/data/tdf161368.fodt` | 2024-06-02 |

Not every one is a reference defect (`page-background-link.fodt` tests an
external link, and a sample may be added for an export or a crash). Three of
them are floating-table layout tests. That date and the upstream test's
"Without the fix ..." comment are the fastest route to a verdict. In this
investigation they gave the answer before any page was compared.

**Proposal.** The reference workflow already has the corpus checkout. It could
record each sample's first upstream commit and date in the manifest
(`git log --diff-filter=A --format=%H%x09%cI -- <path>`, which needs a full
clone of `sw/qa/extras` history or one API call per ranked sample). The report
would then print under each ranked sample "Added upstream 2026-05-13, after
the reference's 24.2 branch", with the test function and its comment lines
(issue #173's item 4). This would only flag samples for triage. Nothing would
be skipped because of it: the defect list still needs a traced fix.

## 5. Pages are compared by number when the counts differ

**What happens.** With one page more than the reference, every page after the
extra one is compared with the wrong partner. The issue reported worst page 3
"blank-vs-inked" and an ink box "wild". Paired by content, `broilerdoc`'s
pages 3 and 4 match the reference's pages 2 and 3 (3.6% and 0% of a blurred,
downscaled page differ), and the one real difference is that page 2, the body
table, is extra. The metrics described the misalignment, not the rendering.

**Proposal.** When the page counts differ, align the two page sequences before
scoring: a small edit-distance alignment over per-page ink profiles or blurred
thumbnails, insertions and deletions costed as blank pages. Report "extra page
2 in broilerdoc; other pages close" and score the matched pairs. The page-count
penalty already prices the mismatch, so the visual metrics should not price it
a second time.

## 6. No fixed LibreOffice to confirm a defect on

**What happens.** The host's LibreOffice is 26.2.6.3, and this fix is only in
26.8. The claim that 26.8 draws four pages rests on an earlier note's run with
26.8.1.1, on a machine and a font setup this investigation could not repeat.
Each reference-defect row needs a fixed rendering as evidence, and each
investigation has to find one again.

**Proposal.** Have the reference workflow also render the current issue's top
30 with the newest LibreOffice release (a second, small artefact next to
`office-references-vN`), and print its page count in the ranked entry:
"LibreOffice 24.2: 3 pages; LibreOffice 26.8: 4 pages; broilerdoc: 4 pages".
A sample where the newer release agrees with `broilerdoc` and the pinned one
does not is a reference-defect candidate on sight. That is what took four
issues to see here.

## 7. Small things met on the way

- **The text diff of an anonymised sample says little.** Many corpus samples
  replace their words with `x`, so 21 lines of `- broilerdoc: x` say only that
  something is extra. The report could add where the lines come from (a
  floating table, a text box, a header, a cell), which `dump --as json` knows.
  It could also say "all 21 lines are broilerdoc's only".
- **Python's `zipfile` rejects the sample** ("Bad magic number for file
  header") while `unzip`, LibreOffice and `broilerdoc` read it. Making variants
  of a sample works from the unpacked folder, not by rewriting the archive.
