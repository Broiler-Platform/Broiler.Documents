# Office conformance workflow: improvements found during issue #180

These came up while resolving the first difference of
[issue #180](https://github.com/Broiler-Platform/Broiler.Documents/issues/180)
(`ooxmlexport/data/wDateValueFormat.docx`, see the
[issue note](office-issue-180.md)). They continue the lists from
[issue #170](office-conformance-workflow-improvements.md),
[issue #173](office-conformance-workflow-improvements-issue-173.md),
[issue #175](office-conformance-workflow-improvements-issue-175.md) and
[issue #178](office-conformance-workflow-improvements-issue-178.md), and are
ordered by how much time they would have saved. All are proposals except the
one marked as done.

## 1. A newer LibreOffice would have named the defect in one step

**What happened.** The text difference (127 lines, all chapter numbers off by
one) and the page count looked like a `broilerdoc` bug in heading numbering.
Converting the sample with the host's LibreOffice 26.2 settled it at once:
26.2 agrees with `broilerdoc` line for line and keeps the heading whole, so
the 24.2 reference was the odd one out. Finding the upstream commit then took
a few minutes. This is the third issue running (after #170 and #175) whose
first difference was a LibreOffice 24.2 defect, and each time the evidence
came from a newer LibreOffice on the maintainer's host.

**Proposal.** Add a second, text-only reference column to the reference set,
made by a current LibreOffice release (26.2 now). Text projections are cheap
and do not depend on fonts. The report can then mark a sample "broilerdoc
agrees with LibreOffice 26.2, not with 24.2" before anyone opens it; such a
sample is a reference-defect candidate, to be traced to a fix, rather than a
`broilerdoc` difference to rank at the top. (Issue #175's item 6 asked for the
same evidence for pixel checks; text alone would have been enough here.)

## 2. The whole text difference is in no file

**What happened.** The issue and `report.json` quote 8 or 20 lines of the
difference ("… and 11 more line(s)"), and the triage files hold none of it.
To see all 127 lines, and that every one after the first two was the same
renumbering, `TextProjection.Canonicalise` had to be re-written in Python.

**Proposals.**

- Write each failed text check's full canonical difference (or both canonical
  projections) next to the report, e.g. `triage/text/<sample>.diff`.
- Collapse consequential differences. When the remaining lines differ only in
  a leading number that is off by the same amount, report "first divergence at
  line 99 (1.3 / 2 Status Filter-Strömungstest); 125 later lines renumbered by
  one". The first divergence is the only place to look, and the issue would
  have pointed straight at the split heading.

## 3. Pixel checks after a page shift compare the wrong pages

**What happened.** The worst page was 9, with a blur difference of 25%, but
page 9 of each side held different content: the reference was a page further
on from page 5. Every page after the first shift scores badly whatever else
is right, so the metrics could not show that pages 1 to 4 had improved
(page 1's mean pixel difference at 40 dpi fell by a sixth, and its tables no
longer spilled onto page 2).

**Proposal.** Align pages by their text before comparing them: pair each
`broilerdoc` page with the reference page whose text projection overlaps it
most (both sides' text per page is available, from `pdftotext` and from the
render's pieces), and compute the pixel metrics per pair. Report "content
shifted by one page from page 5" as a pagination finding of its own, and keep
the visual metrics for pages that hold the same content.

## 4. Missing glyphs are silent

**What happened.** Two of the six causes were characters `broilerdoc` drew as
missing-glyph boxes: U+F0B3 in a run set in Symbol, and U+00AD where a line
went on. Both were found by looking at 110 dpi crops of one page; neither
left a trace in the report.

**Proposal.** Let `broilerdoc render --json` count the characters its fonts
had no glyph for, by code point and family, and let the triage list them per
sample ("U+F0B3 x30 in Symbol; U+00AD x12"). Private-use code points and
format characters in that list are nearly always a mapping bug, not a font
problem, and the counts would cluster samples across the corpus.

## 5. The CLI's display defaults reached the conformance run

**What happened.** `broilerdoc render` underlines and colours links that state
no colour, a sensible default for HTML and Markdown on screen, and the runner
did not turn it off. Every DOCX and RTF link without a character style was
therefore drawn differently from LibreOffice, which draws them plain. The fix
moved the decision into the document (`DocumentStyleDefaults.UndecoratedLinks`,
set by the DOCX and RTF readers), so the CLI default no longer matters there.

**Proposal.** Audit the other display defaults the runner leaves on (`--font`
`sans-serif`, the 11 point fallback in `RenderPipeline`, `--text-color`, the
link colour `#0033CC` against Writer's `#000080` for ODT) and either move each
into the readers as here or have the runner pass the LibreOffice value
explicitly. The runner's render command should hold no setting that only
makes sense for screen display.

## 6. A fix that came without a test could not be a reference defect (done)

**What happened.** `extras-reference-defects.json` required at least one
upstream test pinning the fixed rendering. The 24.2 defect here was fixed by a
refactoring with no test of its own, so the sample could not be listed, though
the evidence (the fixed code, LibreOffice 26.2's output, Word's saved table of
contents) is as strong as for the two earlier rows.

**Done in this change.** A row may list no upstream test if a new `untested`
field says why and names what pins the fixed rendering instead; the schema
describes it, the guard requires exactly one of the two, and the policy text
says when it applies.

## 7. The producer's own saved layout is free evidence

**What happened.** The document's table of contents, with Word's `PAGEREF`
results, said where Word put each heading: "1.3 … 5" and "2 Beschreibung der
Räume … 7". That is Word's own pagination, saved in the file, and it decided
which of the two LibreOffice releases was right.

**Proposal.** For DOCX samples with a TOC field, extract the saved entries and
page numbers into the manifest (or a side file) and report where
`broilerdoc`'s and LibreOffice's headings land against them. A heading
LibreOffice places on another page than Word did is worth knowing before
ranking a page-count mismatch; `NUMPAGES` results in headers and footers give
Word's page count the same way (here 19, from an older state of the
document, so it needs care).

## 8. Small things met on the way

- **Host LibreOffice page counts can mislead.** 26.2 draws this sample's EMF
  charts about 70% larger than their stated extent and ends on 19 pages, so its
  page count could not confirm `broilerdoc`'s 18; the 24.2 reference draws
  them at the stated size. Compare page by page (item 3), not totals.
- **Git Bash's `sed -i` rewrote CRLF files as LF**, and a heredoc turned
  `"\\*"` in a C# literal into `"\*"`, a compile error only by luck. The
  Edit tool and byte-level Python helpers were safe.
- **A full-corpus replay of both builds side by side** took about 35 minutes
  with `--jobs 6` each, on 16 cores; worth doing whenever a change touches the
  shared layout, as soft hyphens and Symbol text did here.
