# Office conformance workflow improvements

Each investigation of a nightly `sw/qa/extras` ranking also turned up things
the workflow itself (`.github/workflows/office-*.yml`,
`src/tests/Broiler.Documents.Office`, `eng/`, `tests/office/`) got wrong or
could do better. This note keeps what was changed and why, the proposals still
open, and, at the end, which issue each came from.

## Done

### The reference fonts have no Liberation faces

`office-reference-data.yml` used to install `fonts-liberation2` among the
reference fonts and copy only the `.ttf` files each package lists
(`dpkg-query -L`). On Ubuntu 24.04 that is a transitional dummy package: since
Debian `1:2.1.5-2` the fonts ship in `fonts-liberation` (Debian's
`fonts-liberation` `1:2.1.5-3` control file: "Package: fonts-liberation2 …
Description: transitional dummy package"). `FONTCONFIG_FILE` resets fontconfig
to that directory alone, so the reference LibreOffice never saw Liberation
Serif, Sans or Mono. The `office-references-v2` font directory (`fonts/0`)
held only Caladea, Carlito, DejaVu Sans and DejaVu Serif, and the reference
PDF of `tdf163894_hidden.docx` (Liberation Serif body, Liberation Sans header)
embeds `DejaVuSans-Bold`, `DejaVuSerif-Bold` and `DejaVuSerif` (`pdffonts`).
Every DOCX asking for Times New Roman, Arial or Courier New, and every ODT
asking for Liberation, was drawn in the much wider DejaVu, while `broilerdoc`,
whose `FontPinning.cs` maps those families to Liberation faces that were not
there, fell back on its own terms. Both sides drew faces the document never
asked for.

Now the workflow installs and copies `fonts-liberation`, fails the step when a
package contributes no TrueType file, and names the set `office-fonts-2`;
`tests/office/reference-source.json` names `office-references-v3`. Cached
conformance runs read the font-set name from the archive's manifest instead of
the office image's `BROILER_OFFICE_FONT_SET`, so a new font set needs no new
image. `eng/run-office-conformance.sh` installs `fonts-liberation` too (its
local install was never broken: `fonts-liberation2` depends on it).

### Reference defects are listed, not ranked

The corpus is LibreOffice master at `corpusRevision` (`54dd3de`, 2026); the
references come from Ubuntu 24.04's LibreOffice 24.2.7.2. Upstream adds a
sample with almost every layout fix, so a sample newer than 24.2 often pins a
fix 24.2 lacks, and a `broilerdoc` that draws the fixed layout ranks at the
top. tdf#163894 (fixed on master February 2026, first released in 26.8) led
reports #140, #143, #145, #147 and #170, each concluding "reference-version
dependent" with nowhere to record it.

`tests/office/extras-reference-defects.json` (schema beside it,
`ReferenceDefects.cs`) lists such samples with the bug, the fixing commits, the
upstream tests and `fixedIn`. While the reference LibreOffice is older than
`fixedIn`, the runner skips the named checks but still reads and renders the
sample, and the report lists it under "Known Reference Defects". A row lapses
once the reference is new enough, and a guard fails if the pinned LibreOffice
already has the fix. Two extensions followed:

- `bug` also takes `gerrit#N`, the core review that merged a fix naming no
  bug: `floattable-center-shift-down.docx`'s fix, core `25ba87fd` (review
  205157), was first reviewed on Collabora's Gerrit and cherry-picked (#175).
- A row may list no upstream test if an `untested` field says why and names
  what pins the fixed rendering instead; the guard requires exactly one of
  the two. The 24.2 defect behind `wDateValueFormat.docx` was fixed by a
  refactoring with no test (#180).

### Scoring against the CI's references on any host

`OfficeReferences.Load` refuses an archive whose platform, font hash, font-set
name or timeout differ from the consumer's. Local triage therefore ran the
host's LibreOffice: 26.2.6.3 draws `tdf163894_hidden.docx` on 67 pages, not 22,
and the notes for issues #135 to #154 recorded 17, 22, 40 (the page cap), 4
and 5 pages for it with four LibreOffice versions.
`--accept-reference-mismatch` (runner and `eng/run-office-conformance.sh`)
loads the archive anyway. It requires `--references-dir`, refuses `--create-github-issue`, and lists each differing
field on the console, in the issue as "Reference Toolchain" and in the JSON
report as `referenceMismatches`. Schema, producer provenance, input hashes,
crash classification and output hashes are still checked.

### Earlier Notes

Each top difference has an "Earlier Notes" line (`PriorNotes.cs`): the notes
directly in `docs/notes/` that name the sample's file name standing alone,
linked at the run's commit in GitHub Actions.

### The text projection follows the reference's release

LibreOffice's text export writes each table cell on its own line up to 24.2
and a row per line, cells tab-separated, from 24.8 (tdf#144576,
`SwASCWriter::WriteTable`). `dump --as text` was switched to rows on
2026-10-22 (`61fe0ef`) to match the host's 26.2, so against the 24.2
references every sample with a table failed its text check on line breaks;
issue #173's 21 text differences were exactly that.
`dump --as text --cells lines` writes a cell per line, and
`TextProjection.DumpArguments` passes it
whenever the reference LibreOffice (from the manifest, or the local one
without an archive) is older than 24.8. Scored against `office-references-v3`,
text passes went from 3,162 to 3,452 of 3,907 (`db90c69` against the change),
none lost.

### Floating tables are left out of the text projection

LibreOffice imports a positioned table (DOCX `w:tblpPr`, RTF `\tposx` and kin,
an ODT table in a `draw:frame`) as a frame, and its text export leaves frames
out. `dump --as text` now leaves out a top-level floating table's cells, with
LibreOffice's exception for a body of one empty paragraph. Text passes went
from 3,452 to 3,499 (`90903ee` against the change): 47 DOCX samples, seven of
issue #175's top 30. A nested floating table stays in its cell (see the
[roadmap](../roadmap.md)). The full-corpus text run (about ten minutes, no
LibreOffice) caught four regressions in a first version that unit tests from
`SwASCWriter`'s description would not have.

### Link decoration is the document's, not the CLI's

`broilerdoc render` underlines and colours links that state no colour, and
the runner did not turn that off, so every DOCX and RTF link without a
character style differed from LibreOffice's plain one.
`DocumentStyleDefaults.UndecoratedLinks`, set by the DOCX and RTF readers, now
decides it (#180). The other display defaults are [still
open](#the-runners-render-carries-screen-defaults).

## Open

Ordered by how much of the ranking they distort or how much triage time they
would save. None is in the [roadmap](../roadmap.md) yet.

### The references are older than the corpus

Raised in #170, #175, #180. The first differences of all three issues were
LibreOffice 24.2 defects, each settled by a newer LibreOffice on the
maintainer's host (26.2.6.3; for #175 only an earlier note's 26.8.1.1 run had
the fix). Each reference-defect row needs a fixed rendering, and each
investigation hunts for one again. Options:

- Pin a newer reference LibreOffice: install a release from The Document
  Foundation's `.deb` archives, version and SHA-256 in `reference-source.json`.
  One close to `corpusRevision` (26.8 today) makes most rows unnecessary; the
  tool register entry needs re-approval.
- Or take `sw/qa/extras` from the `libreoffice-24.2.7.2` tag, losing every
  sample added since.
- Or add a second reference from a current release: text only (cheap, font
  independent; #180), or page counts for the issue's top 30 (#175), printed in
  the entry ("LibreOffice 24.2: 3 pages; LibreOffice 26.8: 4 pages;
  broilerdoc: 4 pages"). A sample where the newer release agrees with
  `broilerdoc` is a reference-defect candidate on sight.

### Flag samples added after the reference release and quote their test

Raised in #170, #173, #175. 24.2 branched on 2023-12-08 (merge base
`898d5d47`); a sample added after that usually pins behaviour 24.2 lacks. Of
issue #175's top 30, 11 were added later (earliest commit on the path):

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

Not all are reference defects (`page-background-link.fodt` tests an external
link). The upstream unit test in `sw/qa/extras/**/*.cxx` usually says what the
sample is for: `testTdf77417` reads "MSO 2010 compatibility mode: terminating
white spaces are ignored in tables. This was 3 pages with the first invisible
blank page", which is issue #173's ranked difference and its cause. Proposal:
the reference workflow, which already clones `sw/qa/extras`
(`--filter=blob:none`; drop `--depth=1` or ask the API per ranked sample),
records each sample's first commit and date
(`git log --diff-filter=A --format=%H%x09%cI -- <path>`) in the manifest; the
report prints "Added upstream 2026-05-13, after the reference's 24.2 branch"
and the test functions that load the sample with their comment lines. This
only flags; skipping still needs a traced fix.

### Pages are compared by number when content has shifted

Raised in #170, #175, #180, #185. With one extra page, every later page is
compared with the wrong partner: on `floattable-center-shift-down.docx` the
"blank-vs-inked" worst page 3 matched the reference's page 2 (3.6%), and the
one real difference was an extra page 2. On `wDateValueFormat.docx` worst page
9 (25% blur) held different content from the reference's page 9, hiding that
pages 1 to 4 had improved; host 26.2's 19 pages there came from EMF charts drawn
70% larger, so totals mislead too. On `fdo68607.docx`, with equal counts, the
worst page wandered (8, 5, 1) while the cause was LibreOffice starting page 4
with the whole "Fourth wall" row. And a page-count gap moves the score as much
as a missing table. Proposals:

- When counts differ, align pages before scoring: edit distance over per-page
  ink profiles or thumbnails, or pair by greatest text overlap (`pdftotext`
  per page, the render's pieces). Report "extra page 2 in broilerdoc" or
  "content shifted by one page from page 5" as a pagination finding and score
  the matched pairs; the page-count penalty already prices the gap.
- When counts match, write each page's first and last text line for both
  sides (`pdftotext -f N -l N`; `dump` would need a `--pages` form) and report
  the first page where they differ.
- Score the pages both sides share separately from the page-count gap.

### Keep every page's metrics

Raised in #178, #182. `report.json` keeps only the worst page. On
`odfexport/data/pagestyle_background_lo64.odt` the #178 fix put pages 3 and 4
right, yet blur difference rose 0.06% to 12.8% because the worst page moved to
6, a hatch always 12.8% off. On `tdf123163-1.docx` the worst page went 48.5% to
3.6% while "rendering similarity" moved 15.3% to 15.9%. Proposal: write every
page's metrics to `renders[].pages[]` and the triage directory, report the
median page beside the worst, and let `--previous-triage` compare page by page
("pages 3, 4 improved; page 6 unchanged").

### Classify text differences

Raised in #173, #175, #180, #182. Proposals:

- Label a hunk whose `broilerdoc` lines joined with single spaces equal the
  LibreOffice line (or the reverse): "21 of 21 differences: LibreOffice keeps
  on one line what broilerdoc breaks". That was `tdf123163-1.docx` (a line
  feed in `w:t`) and the #173 table projection; seven earlier notes on the
  former looked elsewhere.
- Collapse consequential differences: "first divergence at line 99 (1.3 /
  2 Status Filter-Strömungstest); 125 later lines renumbered by one"
  (`wDateValueFormat.docx`).
- Write each failed check's full canonical difference to
  `triage/text/<sample>.diff`; the report quotes 8 or 20 lines, and #180 had to
  re-write `TextProjection.Canonicalise` in Python to see all 127.
- Say where extra lines come from (floating table, text box, header, cell;
  `dump --as json` knows), since anonymised samples show only `x`.

### Guard the text projection on pull requests

Raised in #173, #175. Both projection regressions came from fitting `dump` to
the host's LibreOffice. Proposals: run `--axis semantic` against the cached
reference archive on pull requests touching `dump`, a reader or
`TextProjection`, failing on any text check that passed on main (no
LibreOffice needed; it would have caught #173's ~290 and #175's four); make
`--references-dir` with `--accept-reference-mismatch` the guide's default
triage recipe, host output a second opinion; and keep a table in the guide of
what LibreOffice's text export changed between 24.2 and `corpusRevision`, the
rules `TextProjection` and `ReferenceDefects` key on.

### Make a render reproduce the reference's fonts

Raised in #173, #182. `FontPinning` maps the five metric aliases and nothing
else. `tdf123163-1.docx`'s title in Liberation Sans Narrow was DejaVu Sans in
the reference (`pdffonts`), Segoe UI in `broilerdoc`, the real face in host
26.2: three renderers, three titles. Proposal: when preparing references,
record the faces actually used (`pdffonts` per sample, or `fc-match` per
corpus family) as `family -> face file` and pass a `--font-file` for each, so
`fonts.unmappedFamilies` lists only families the reference lacked too. Also,
the re-run line elides the font arguments, and `render --font-dir` alone drew
`tdf77417.docx`'s Arial in a Windows face: write them as a response file
(`fonts.rsp`, needs `@file` support) or add `--font-aliases libreoffice`
(`FontPinning.MetricAliases`).

### Compare numbers, not pictures

Raised in #178, #182. `page-background-link.fodt` was settled from the
reference PDF's content stream (the `rg` and rectangle before the first `BT`),
`tdf123163-1.docx`'s backdrop 140.6 pt left of the page by measuring colour
runs in a 48 dpi PNG. Proposals:

- For a failed pixel check, print each side's dominant non-white colours on
  the worst page ("LibreOffice: 99.7% `#729FCF`; broilerdoc: 0.3% `#000000`";
  `#729FCF` is LibreOffice's default fill).
- Keep a vector summary per reference page beside the manifest (fills and
  rectangles before the first text object, `Do` image draws with matrices,
  text origins), extracted with `broilerdoc`'s PDF reader, and the same from
  `render --format pdf`.
- `broilerdoc render --layout-json <file>`: each line's box and text and each
  shape's bounds, z-order, fill and anchor, in points, to diff against
  `pdftotext -bbox`.

### Show compatibility settings and the producer

Raised in #170, #173, #178. Word 2013 justification (`compatibilityMode` 15,
LibreOffice's `JustifyLinesWithShrinking`) and Word 2010 (`compatibilityMode`
14: cell-end white space dropped, `TabOverMargin`) caused #170's and #173's
largest differences; the #178 page fill depends on `meta:generator`
(`isGeneratorVersionOlderThan` runs throughout `xmloff` and the filters).
Proposals: `broilerdoc info --json` reports the stated `compatibilityMode` and
the `DocumentStyleDefaults` switches (`shrinksJustifiedLines`,
`tabsOverMargin`, `widensSpaceRuns`); the manifest records the producer (ODF
`meta:generator`, DOCX `docProps/app.xml` `Application`/`AppVersion`, RTF
`\generator`) and ODF `config:config-item` layout flags; the ranked entry
prints them ("Word compatibility: mode 14 (tabs over margin, cell white space
trimmed)") and the triage breaks failures down by producer family.

### A before/after replay as a committed tool

Raised in #173, #178, #180, #185. Every fix since #168 validated itself by
replaying `main` and the branch and diffing with scripts kept outside the
repository (about an hour on this host for #173; 35 minutes with `--jobs 6`
each for #180; three minutes for 372 samples with `--references-dir` and
`--sample-list` in #178). #185's widow default reached 1,701 of 1,800 DOCX
samples, and the comparison decided it. Proposal: commit
`eng/compare-office-reports.py <before report.json> <after report.json>`
(outcome flips by group, metric moves by size, page-count changes), or a
runner mode (`--changed-against <tool>`, `--tool-before`/`--tool-after`) that
scans at low resolution, scores only changed samples and prints the table; and
document the recipe in the guide.

### Make Earlier Notes say more

Raised in #175, #178, #185. `floattable-center-shift-down.docx` was triaged in
the notes for #136, #143, #145 and #147 (#143 already said "4 vs 3 pages with
LibreOffice 25.2, but 4 vs 4 with 26.8"); `page-background-link.fodt`'s "image
not fetched" was repeated by #138, #143, #145, #147 and #164 though
`192.0.2.1` is TEST-NET-1; for `fdo68607.docx` both linked notes only listed
it among replayed samples. Proposals: sweep `docs/notes/` once for
"reference-version", "26.8", "LibreOffice 25" and "upstream test" beside a
sample name and trace or dismiss each; let a note carry a verdict
(`<!-- verdict: floattable-center-shift-down.docx reference-defect -->`) that
"Earlier Notes" prints; mark a sample named in three or more notes that still
ranks; and link only prose mentions, or print the mentioning line.

### Find a fix's siblings

Raised in #178, #182. #178's rule also covered `background-image-link.fodt`
(twice), `paragraph-style-background-link.fodt`,
`two-paragraph-background-links.fodt` and `shape-fill-link.fodt`; #182 counted
line feeds in run text (four samples), unpreserved edge white space (six, one
LibreOffice's `xml_space.docx`) and page-placed shapes across differing
margins (eleven) over 1,807 DOCX samples. All by ad-hoc scripts. Proposals:
list under each ranked sample the failing samples sharing its rarer
diagnostic codes (here `odt.image.external`, `odt.fill`); keep a feature index
of elements, parts and control words per sample beside the manifest, from a
committed generator (earlier ones lived in `D:/scratch/tools`); and a runner
mode taking an XPath or regex per package part that lists matching samples,
usable as `--sample-list`.

### Silent drops need diagnostics

Raised in #180, #185. `wDateValueFormat.docx` drew U+F0B3 (Symbol) and U+00AD
as missing-glyph boxes, found only in 110 dpi crops; `fdo68607.docx`'s title
"IGCSE Drama Glossary" (one 24 pt line in a 26.5 pt text area) was dropped
without a word. Proposals: `render --json` counts missing glyphs by code point
and family, listed per sample ("U+F0B3 x30 in Symbol; U+00AD x12"); a
`layout.shape.text-clipped` diagnostic carried into the runner's per-sample
diagnostics beside codes like `docx.pagebreak.split`.

### The runner's render carries screen defaults

Raised in #180, #182. After `UndecoratedLinks`, the runner still leaves on
`--font` `sans-serif`, the 11 point fallback in `RenderPipeline`,
`--text-color` and link colour `#0033CC` (Writer uses `#000080` for ODT); move
each into the readers or pass LibreOffice's value. Missing pictures draw a
grey outline with a rule (fourteen in `tdf123163-1.docx`) where LibreOffice
draws nothing; give the runner a switch, keep the outline as the CLI default,
and count affected samples.

### Switch a rule off in a replay

Raised in #185. The first replay turned twelve passing pixel checks to
failures and fixed nine; finding which of four rules did it took temporary
`DBG_NOWIDOW`/`DBG_NOPLACE` checks, rebuilds and replays of the 22 flipped
samples (the widow default caused every failure). Proposal:
`BROILER_DOCUMENTS_DISABLE=widow-default,table-placement`, read once by
codecs and layout, listed in `broilerdoc version --json` and the report.

### Read LibreOffice's sources at the reference's tag

Raised in #185. `DomainMapper_Impl::deferBreak` drops a column break in a
table only on master (tdf#163178; `tdf163178_columnbreakInTable.docx` was the
replay's one regression), and `DomainMapperTableHandler` shifts tables by the
cell margin where `nMode < 15` on master but `0 < nMode && nMode <= 14` in 24.2.
The second, taken from master, would have moved every table of the DOCX
samples stating no mode; it was caught only by fetching the 24.2 files
(`raw.githubusercontent.com/LibreOffice/core/libreoffice-24.2.7.2/...`).
Proposal: record the release's git tag in the manifest
(`office-references-v3/manifest.json` holds the version only) and add a script
fetching a file at that tag into a scratch directory:
`eng/lo-source.sh writerfilter/source/dmapper/DomainMapperTableHandler.cxx`.

### Probe and bisect harnesses

Raised in #178, #182. #178's rules came from about 20 authored documents
converted with host 26.2 and read back; #182 found the bad section among 31 by
cutting the document into section runs. Proposals: an `eng/` probe harness
(template plus variants, convert with a given `soffice`, print fills, image
placements and text origins; runs on both 24.2 and a newer release), and
`--bisect-sections <sample>` ranking single sections and pairs by difference.

### Smaller items

- **Headers and footers are only on the pixel axis** (#170): LibreOffice's
  text export omits running content (`wrtasc.cxx`); a `dump` mode plus the
  reference PDF's header and footer bands (`pdftotext -layout`, crop boxes)
  would expose them, as tdf163894's whole difference was.
- **Word's saved layout is evidence** (#180): TOC `PAGEREF` results ("1.3 …
  5", "2 Beschreibung der Räume … 7") and `NUMPAGES` give Word's pagination;
  extract them into the manifest for DOCX samples (with care: here `NUMPAGES`
  said 19 from an older state).
- **A published runner can't find the repository** (#185): `RepositoryRoot`
  walks up from the executable ("Broiler.Documents component root not
  found"); accept `--repo-root` or `BROILER_DOCUMENTS_ROOT`.
- **Guide gaps** (#178, #185): name `Broiler.Documents.slnx`
  (`dotnet test Broiler.Documents.sln` reports 17 failures, none passed).
  `dotnet test --filter "FullyQualifiedName~..."` runs nothing under
  Microsoft Testing Platform with xUnit v3 (exit code 5); run one class with
  `src/tests/<project>/bin/Release/net10.0/<project>.exe -class <full class name>`.
- **Host quirks** (#175, #178, #180): Python's `zipfile` rejects some samples
  ("Bad magic number for file header") that `unzip`, LibreOffice and
  `broilerdoc` read, so build variants from the unpacked folder; Git Bash
  heredocs lose backslashes (`'\\'`, `"\\*"`) and `sed -i` rewrites CRLF as LF.

## Issue #170

[Issue #170](https://github.com/Broiler-Platform/Broiler.Documents/issues/170),
[issue note](office-issue-170.md), `ooxmlexport/data/tdf163894_hidden.docx`.
Done: [Liberation fonts](#the-reference-fonts-have-no-liberation-faces),
[reference defects](#reference-defects-are-listed-not-ranked),
[mismatch flag](#scoring-against-the-cis-references-on-any-host),
[Earlier Notes](#earlier-notes). Open:
[older references](#the-references-are-older-than-the-corpus),
[added-upstream flag](#flag-samples-added-after-the-reference-release-and-quote-their-test),
[page shifts](#pages-are-compared-by-number-when-content-has-shifted),
[compatibility settings](#show-compatibility-settings-and-the-producer),
[headers and footers](#smaller-items).

## Issue #173

[Issue #173](https://github.com/Broiler-Platform/Broiler.Documents/issues/173),
[issue note](office-issue-173.md), `ooxmlexport/data/tdf77417.docx`. Done:
[table projection](#the-text-projection-follows-the-references-release).
Open: [projection guard](#guard-the-text-projection-on-pull-requests),
[line-break class](#classify-text-differences),
[render fonts](#make-a-render-reproduce-the-references-fonts),
[compatibility settings](#show-compatibility-settings-and-the-producer),
[upstream test](#flag-samples-added-after-the-reference-release-and-quote-their-test),
[replay tool](#a-beforeafter-replay-as-a-committed-tool).

## Issue #175

[Issue #175](https://github.com/Broiler-Platform/Broiler.Documents/issues/175),
[issue note](office-issue-175.md),
`layout/data/floattable-center-shift-down.docx`. Done:
[floating tables](#floating-tables-are-left-out-of-the-text-projection),
[`gerrit#`](#reference-defects-are-listed-not-ranked). Open:
[projection guard](#guard-the-text-projection-on-pull-requests),
[note sweep](#make-earlier-notes-say-more),
[added-upstream table](#flag-samples-added-after-the-reference-release-and-quote-their-test),
[page alignment](#pages-are-compared-by-number-when-content-has-shifted),
[newer LibreOffice](#the-references-are-older-than-the-corpus).

## Issue #178

[Issue #178](https://github.com/Broiler-Platform/Broiler.Documents/issues/178),
[issue note](office-issue-178.md),
`tiledrendering/data/page-background-link.fodt`. All open:
[dominant colours, vector summary](#compare-numbers-not-pictures),
[recurring samples](#make-earlier-notes-say-more),
[per-page metrics](#keep-every-pages-metrics),
[siblings](#find-a-fixs-siblings),
[producer](#show-compatibility-settings-and-the-producer),
[probe harness](#probe-and-bisect-harnesses),
[replay recipe](#a-beforeafter-replay-as-a-committed-tool).

## Issue #180

[Issue #180](https://github.com/Broiler-Platform/Broiler.Documents/issues/180),
[issue note](office-issue-180.md), `ooxmlexport/data/wDateValueFormat.docx`.
Done: [`untested` rows](#reference-defects-are-listed-not-ranked),
[link decoration](#link-decoration-is-the-documents-not-the-clis). Open:
[newer text reference](#the-references-are-older-than-the-corpus),
[full diffs, renumbering](#classify-text-differences),
[page alignment](#pages-are-compared-by-number-when-content-has-shifted),
[missing glyphs](#silent-drops-need-diagnostics),
[display defaults](#the-runners-render-carries-screen-defaults),
[Word's saved layout](#smaller-items).

## Issue #182

[Issue #182](https://github.com/Broiler-Platform/Broiler.Documents/issues/182),
[issue note](office-issue-182.md), `layout/data/tdf123163-1.docx`. All open:
[split-line class](#classify-text-differences),
[font substitution](#make-a-render-reproduce-the-references-fonts),
[layout JSON](#compare-numbers-not-pictures),
[section bisect](#probe-and-bisect-harnesses),
[per-page metrics](#keep-every-pages-metrics),
[pattern search](#find-a-fixs-siblings),
[missing pictures](#the-runners-render-carries-screen-defaults).

## Issue #185

[Issue #185](https://github.com/Broiler-Platform/Broiler.Documents/issues/185),
[issue note](office-issue-185.md), `ooxmlexport/data/fdo68607.docx`. All
open: [sources at the tag](#read-libreoffices-sources-at-the-references-tag),
[rule switches](#switch-a-rule-off-in-a-replay),
[clipped shape text](#silent-drops-need-diagnostics),
[page starts and ends](#pages-are-compared-by-number-when-content-has-shifted),
[prose mentions](#make-earlier-notes-say-more),
[`--repo-root`, single test class](#smaller-items),
[comparison script](#a-beforeafter-replay-as-a-committed-tool).
