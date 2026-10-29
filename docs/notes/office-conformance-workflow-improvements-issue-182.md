# Office conformance workflow: improvements found during issue #182

These came up while resolving the first difference of
[issue #182](https://github.com/Broiler-Platform/Broiler.Documents/issues/182)
(`layout/data/tdf123163-1.docx`, see the [issue note](office-issue-182.md)).
They continue the lists from
[issue #170](office-conformance-workflow-improvements.md),
[issue #173](office-conformance-workflow-improvements-issue-173.md),
[issue #175](office-conformance-workflow-improvements-issue-175.md),
[issue #178](office-conformance-workflow-improvements-issue-178.md) and
[issue #180](office-conformance-workflow-improvements-issue-180.md), and are
ordered by how much time they would have saved. All are proposals.

## 1. Classify text differences that are one line split in several

**What happened.** All 21 lines of the text difference had one shape: a
LibreOffice line that is the `broilerdoc` lines before it joined with spaces
("xxx xx xxxxxx xxxxxx xx" + "2" + "xxxx xxx …" against
"xxx xx xxxxxx xxxxxx xx 2 xxxx xxx …"). That pattern points straight at line
breaks the reader makes, here a line feed in `w:t`, but the issue printed the
lines without saying so, and seven earlier notes that linked this sample
looked at pictures, columns and sections instead.

**Proposal.** When the runner diffs two projections, test each hunk for
"the `broilerdoc` lines, joined with single spaces, equal the LibreOffice
line" (and the reverse) and report it as a class: "21 of 21 differences:
LibreOffice keeps on one line what broilerdoc breaks". The test is a few lines
in the diff, and the class names the area to look at (line breaks, white
space, soft breaks) without opening the document.

## 2. Reproduce the reference's font substitution

**What happened.** The title is set in Liberation Sans Narrow, which the
reference fonts do not include. The reference's LibreOffice drew it in
DejaVu Sans (`pdffonts` on the reference PDF) and wrapped it on two rows;
`broilerdoc` got no mapping for the family and drew its host fallback (Segoe
UI on Windows), and LibreOffice 26.2 on the maintainer's host has the real
face and keeps one row. Three renderers, three titles; only a manual
`--font-file "Liberation Sans Narrow=…/DejaVuSans.ttf"` made page 1 match the
reference. `FontPinning` maps the five metric aliases and nothing else, so
every family fontconfig substituted on the reference host is drawn
differently here, and the page-1 difference of this sample was partly the
harness's.

**Proposal.** When the reference set is prepared, record the faces the
reference LibreOffice actually used: per sample, the base fonts of the
reference PDF (cheap, `pdffonts` or the runner's own PDF reader), or once per
corpus, `fc-match` for every family the corpus names. Store them in the
manifest as `family -> face file`, and have the runner pass a `--font-file`
for each family it does not already pin. The render then draws what the
reference drew on any host, and `fonts.unmappedFamilies` lists only families
the reference host lacked too.

## 3. A positioned dump of the layout

**What happened.** The decisive defect - section 1's backdrop drawn 140.6 pt
left of the page - was found by looking at pictures and measuring colour runs
across rows of a 48 dpi PNG. `broilerdoc dump --as json` has no shapes and no
positions, so there was no number to compare with `pdftotext -bbox` on the
reference, which gives every word's box in points.

**Proposal.** `broilerdoc render --layout-json <file>`: per page, each line's
box and text and each shape's bounds, z-order, fill and anchor paragraph, in
points. Next to the reference's word boxes, a shape at x = -140.6 or a title
row at 252.5 instead of 350.4 is a one-line finding, and a script can diff the
two before anyone looks at a page.

## 4. Bisect a document by sections

**What happened.** Which of the 31 sections went wrong was found by cutting
the document into runs of sections (a 40-line script: keep sections a to b,
make the last one's `w:sectPr` the body's) and converting each run with both
renderers. Section 4 alone rendered right and sections 1 to 4 rendered wrong,
which isolated the margin conversion in two runs; sections 4 and 5 then showed
the blank page.

**Proposal.** Ship the cut as a runner option or an `eng/` script
(`--bisect-sections <sample>`): render every single section and every
section with its predecessor through both renderers, and rank the runs by
how much the pair differs. Most of the corpus' long documents are sectioned,
and a page-count or position difference usually starts in one section.

## 5. One summary number hid the progress

**What happened.** The fixes took the worst page's blur difference from 48.5%
to 3.6% and page 3's mean pixel difference from 32.6 to 3.8, yet "rendering
similarity" moved from 15.3% to 15.9%: it follows the worst page's ink
profile, and the worst page became page 2, whose content LibreOffice moves for
an unrelated reason (the issue note's first open item). While the extra page
was in (one intermediate step), the score rose although every page but one
was closer.

**Proposal.** Write each sample's per-page metrics (blur, profile, coverage,
ink box) to the triage directory and report the median page beside the worst.
A fix that brings fifteen of sixteen pages close is then visible as that,
which is what decides whether to keep it. (Issue #180's item 3, aligning pages
by text, is the same problem after a page shift.)

## 6. Count a pattern across the corpus before changing a rule

**What happened.** Each reader rule here was weighed by scanning the 1,807
DOCX samples' `document.xml` with a short script: line feeds in run text in
four samples, unpreserved edge white space in six (one of them
LibreOffice's own `xml_space.docx`), page-placed shapes in documents whose
sections differ in margin in eleven. That said which samples the replay had to
show unchanged or better, in seconds rather than a 25-minute run.

**Proposal.** A runner mode that takes an XPath or regular expression per
package part and lists the samples it matches, kept with the reference set so
that it runs over exactly the corpus being ranked. Its output is also the
natural `--sample-list` for a focused replay.

## 7. Missing pictures draw ink the reference does not

**What happened.** `broilerdoc render` draws a missing picture as a grey
outline with a rule across it, deliberately, so a reader sees the hole.
LibreOffice's PDF export draws nothing. This sample has fourteen missing
pictures, some a page tall, and on its sparse pages those outlines are a large
share of the ink that differs, so the pixel axis measures a presentation
choice.

**Proposal.** Give the runner's renders a switch that draws missing pictures
as LibreOffice prints them (nothing), keep the outline as the CLI's default,
and count the samples with missing pictures in the triage so the switch's
effect is visible.
