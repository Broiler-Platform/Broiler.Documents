# The office conformance suite

`src/tests/Broiler.Documents.Office` is a console runner that asks LibreOffice to
write the documents, reads them back with the built `broilerdoc`, and holds the
disagreements against a committed baseline.

```bash
dotnet run --project src/tests/Broiler.Documents.Office
```

It prints `N/M passed, K failed.` as its last line and exits with the number of
failures capped at 98; exit 99 is reserved for usage errors or harness failures. `eng/run-office-conformance.sh`
provisions the tools first and then runs it; `.github/workflows/office-conformance.yml`
runs that nightly inside `ghcr.io/broiler-platform/broiler-sdk-office:latest`,
built and published by [Broiler.Code](https://github.com/Broiler-Platform/Broiler.Code/tree/main/docker).
The image supplies the .NET SDK, LibreOffice, poppler, fonts and a bundled
`sw/qa/extras` corpus. CI uses `--skip-install` and the image's font directory
and font-set stamp. The default corpus URL selects the bundled corpus through
`--extras-dir`; dispatching with a different `extrasUrl` downloads that source.
Updating the image can change the toolchain or corpus; seed baseline stamp
checks still apply, and baseline regeneration remains an explicit dispatch option.

## Contents

- [Why it exists](#why-it-exists)
- [Where the documents come from](#where-the-documents-come-from)
- [The semantic axis](#the-semantic-axis)
- [The pixel axis](#the-pixel-axis)
- [The baseline](#the-baseline)
- [The tool register](#the-tool-register)
- [Determinism](#determinism)
- [The upstream ranking](#the-upstream-ranking)
- [What it has found](#what-it-has-found)
- [What it cannot tell you](#what-it-cannot-tell-you)

## Why it exists

[The corpus suite](corpus-suite.md) says plainly what it cannot do:

> **Nothing about foreign documents.** Every document in the corpus was written
> either by this component or by somebody adding a row to its manifest. [...] It
> is not evidence that a file produced by Word, LibreOffice or a browser reads
> correctly, and no amount of adding samples changes that.

That is the gap this suite closes, by exactly one step. The seeds are still
authored here, but the `.docx`, `.odt`, `.rtf` and `.html` that `broilerdoc` is
asked to read were written by LibreOffice - a mature implementation that knows
nothing about this one, and whose markup no writer here produces. A DOCX from
LibreOffice is not a DOCX from `DocxWriter`, and until now nothing checked that
this component could read one.

It closes a second gap too, and this one no suite here touched at all. Every
existing check reads a document back through this component's own reader, which
can tell you that an export round-trips *here* and cannot tell you whether
anybody else can read it. The `interop` checks hand our export to LibreOffice and
ask what it made of it.

It is modelled on the Web Platform Tests runner in the Broiler browser
repository: a shell script that provisions the external implementation, a runner
project that drives it, committed expectations, one workflow, one page of prose
about what the numbers mean. The differences from that model are all downstream
of one fact - WPT ships a corpus and a specification, and this has neither.

## Where the documents come from

Nothing is committed, and for the seed corpus nothing is downloaded. (The
[upstream ranking](#the-upstream-ranking) is the one mode that fetches
documents, and it fetches them at run time into the workspace, never into the
tree.)

[ADR 0013](../adr/0013-standards-ip-provenance-and-claims-beyond-pdf.md) gives every
format its own rights record, each rejects third-party document artifacts by
default and per artifact, and
`FormatClaimGuardTests.No_Document_Of_A_Supported_Format_Is_Committed` walks the
whole tree to enforce it. So a seed is a fragment of authored markup held as a
string in `tests/office/office-corpus.json` - the same device `corpus.json`
already uses for RTF control words and ZIP part content - and LibreOffice
manufactures the real documents at run time in a temporary directory that
`Program.Guard` refuses to place inside the repository.

Fourteen seeds, four target formats, fifty-six documents. Each seed exercises one
thing and says in its own `why` what that is; where a construct can only go wrong
in combination with another - which of two frames is drawn on top is unaskable of
either frame alone - the seed carries the combination and its `why` says that is
what it is doing. The corpus is deliberately small: the first baseline has to be
classified by a person, and a suite whose baseline nobody has read is a log
rather than a control.

### Two seed languages

Thirteen seeds are HTML and one is flat ODF, and the second language exists
because the first has a ceiling that was measured rather than argued about.
LibreOffice's HTML import can only manufacture the constructs HTML can state, and
across the twenty-four ODT and DOCX documents the HTML seeds produce there is not
one header, footer, anchored frame, shape fill, field or placeholder. Every one of
those is something a letterhead is built from, so a difference in any of them
could not fail this suite - not because the checks were too weak, but because no
document in the corpus had one.

A `.fodt` is a single XML file, so it is still a string in the manifest, still
authored here, and still nobody else's document: the bargain above is unchanged,
and the two guards now refuse a committed `.fodt` alongside the extensions they
already refused. LibreOffice's export writes the DOCX, RTF and HTML targets from
it in its own markup exactly as before. Only the ODT target ends up close to its
own source, and that one target's weakness is what the other three cost - without
it the construct is not in the corpus at all.

The two languages differ in what the schema can hold them to, and the difference
is not arbitrary. The HTML form states its page in an `@page` rule and its family
on the `<body>`, which every run inherits from. ODF has no element that plays the
body's part, so the flat form pins the page box on the page layout, requires a
pinned family on a style, and refuses `fo:font-family` outright - the loader reads
families out of `style:font-name` and `svg:font-family` only, and a family it
skipped is a family nobody checked against the pinned set.

## The semantic axis

Six checks per document, in ascending order of how much they prove. The first belongs to
the workspace that manufactures the document; the rest are what broilerdoc makes of it.

| Check | What it asserts |
| --- | --- |
| `produce` | LibreOffice exited 0, wrote a file above a per-format floor, **and reported using the filter it was asked for**. |
| `probe` | `broilerdoc probe` selects the format LibreOffice was asked to write. |
| `read` | `broilerdoc info` reads it, and does not exit 70 - which `docs/cli.md` defines as always a defect in the tool. |
| `text` | LibreOffice's own plain-text projection of the file and `broilerdoc dump --as text` agree. |
| `interop` | LibreOffice's reading of **our** export of the document agrees with its reading of its own. |
| `roundtrip` | The existing round-trip discipline, applied to a document this component did not write. |
| `expect` | The document reached the paragraph floor its own seed states. |

`expect` is the one that looks redundant and is not. A document that lost most of
its content still reads, still probes, and still agrees with LibreOffice about the
little that survived - the three checks above it all pass on a nearly empty file.
The floor is deliberately coarse, because whether a list item or a table cell is a
paragraph is answered differently by every target, so a seed states the number
every target has to reach and no more.

`text` is the headline, and it is worth saying why it is trustworthy. It asks
LibreOffice to read back a file LibreOffice itself produced moments earlier,
which is the strongest available statement of what that file is supposed to
contain. No formatting difference can produce a divergence: if the two projections
differ, one of the two readers dropped, duplicated, reordered or invented
content.

The third part of `produce` is not defensive padding. Given bytes it cannot
parse, LibreOffice does not fail - it falls back to the plain-text importer,
converts happily, exits 0 and leaves a plausible file behind. For a suite whose
value is catching content that went missing quietly, being taken in by exactly
that failure mode would be a poor joke, so the filter it actually chose is parsed
out of its own stdout and compared with the one it was given.

Both projections are canonicalised before they are compared: BOM, line endings,
the line-separator family, non-breaking and zero-width spaces, tabs, runs of
spaces, blank lines, and Unicode normal form. **That is a policy, and a codec bug
that only manifests inside it is invisible to this suite.** The answer to a noisy
seed is a declared per-seed exception carrying a written reason, not a wider
global rule.

## The pixel axis

LibreOffice exports the document to PDF, `pdftoppm` rasterises every page, and
`broilerdoc render` draws the same document at the same resolution. The two
bitmaps are compared in the harness, over raw PPM and BMP bytes, with no image
library on either side.

**A pixel-match threshold is not available here, at any value.** Measured on one
PDF at one resolution: two backends *inside poppler itself* disagreed on 8.39% of
the pixels, and LibreOffice's own PNG export disagreed with poppler on 14.44%. A
"99% of pixels match" gate of the kind the WPT reftest runner uses would be
measuring anti-aliasing. Broiler's layout engine and LibreOffice Writer are
independent implementations with their own line breaking, justification, font
fallback and page-break policy.

What survived that same measurement is what the axis is built on: the inked first
and last rows were **identical** across all three renderings. Text baselines land
on the same rows; only glyph rasterisation differs. So five derived metrics are
recorded, each chosen because it was measured to survive a change of rasteriser:

| Metric | Bands |
| --- | --- |
| `geometry` - page size agreement | `match` (within 2 px) · `off` |
| `inkBox` - largest edge disagreement of the inked bounding box | `tight` ≤8 px · `near` ≤32 · `loose` ≤128 · `wild` |
| `inkProfile` - cosine similarity of the smoothed vertical ink distribution | `exact` ≥0.995 · `close` ≥0.97 · `fair` ≥0.90 · `poor` |
| `coverage` - difference in inked fraction of the page | `exact` ≤0.002 · `close` ≤0.01 · `fair` ≤0.05 · `poor` |
| `blurDiff` - differing fraction after a blur that removes the rasteriser | `clean` ≤0.005 · `light` ≤0.05 · `heavy` ≤0.20 · `severe` |

Four checks sit beside the bands and are not banded, because none of them is a
matter of degree. `pdf` and `fonts` police LibreOffice: that it used the export
filter it was asked for, and that it embedded nothing outside the pinned set.
`mapping` polices this side: the render manifest's `unmappedFamilies` must be
empty. `pages` compares the two page counts, and `multipage` holds them against
what the seed claims about itself. And `ink` refuses a page that came out blank on
both sides - two blank pages agree on every metric in the table above, which is
true and useless, and no seed in this corpus renders to an empty page.

The ink profile is smoothed, and finding out why is worth recording. Unsmoothed,
two renderings of one page with the same fonts, the same page box and a coverage
difference of five parts in a hundred thousand - the same ink, in the same amount
- scored a cosine similarity of 0.11. A page of text is a sparse, almost periodic
signal of inked lines separated by blank leading, so shifting it half a line puts
every peak against a trough. The metric was reporting a few pixels of vertical
offset as total disagreement about content. Smoothed over about a line height,
the same page scores 0.98, and what still registers is a paragraph that is
missing or content on the wrong page - which is what it was always meant to
catch, and which `inkBox` already reports better for a pure shift.

A band rather than a number, for the reason the baseline's own policy gives: a
recorded number would fail on every poppler point release and every font package
update. The raw numbers are kept beside the band so a reviewer can see whether a
row is comfortably classified or sitting on a boundary.

Two things are deliberately *not* done here that the corpus suite does. It does
not pass `--continuous`: pagination is part of the measurement, so a difference
in where a page ends has to be visible rather than smoothed away. And it does not
pass `--page-size` or `--margin`, so both sides take the document's own page box
and a disagreement about the page becomes a finding instead of being hidden by a
flag. When `geometry` comes back `off`, the other four numbers were measured over
pages of different sizes and the failure message says so.

`broilerdoc compare --mode image` is used exactly once, to draw the heat diff on
a failing page for a human to look at. It is never consulted for a verdict: the
comparator is exact-pixel with a per-channel tolerance and cannot express "blur,
then threshold", and using the tool under test as the instrument that judges it
is a circularity worth avoiding where avoiding it is cheap.

## The baseline

`tests/office/office-baseline.json`, on the same discipline as
`corpus-baseline.json`: it records only what is not clean, so **a new difference
fails by not being listed**, and a row that stops reproducing fails too -
including when it stops because somebody fixed it, because an improvement nobody
records is an improvement nobody can show.

It carries one thing the corpus baseline does not: a `producedWith` stamp naming
the LibreOffice build, the poppler version, the font set, the platform and the
resolution. A run whose own toolchain does not match it **skips every comparison
and says which field differs**, rather than failing. That is what lets a
developer on Windows, or a machine with a newer LibreOffice, run the suite
usefully instead of reading a wall of red. `--update-baseline` refuses to change
the stamp without `--restamp`, because a baseline regenerated on the wrong
LibreOffice is worse than no baseline - it looks like evidence.

It carries the stamp of the toolchain the CI workflow provisions and the rows
the runs on it produced. Seven render rows are still `suspected-defect`
(`letterhead-frames` and `line-break-within-paragraph`); their notes say what is
left is ordinary disagreement between two layout engines, and classifying them
is on the [roadmap](../roadmap.md#test-suites-and-tooling).

## The tool register

`tests/office/tools/manifest.json` holds LibreOffice and poppler, and **both are
approved**, by the project reviewer on 2026-09-06. The rule is the one this
component already operates for its PDF oracles: a tool absent from the register
may not run in CI, and no entry in it may become a product reference.

A row exists so a tool's details have somewhere to land while it is reviewed,
and writing one down approves nothing. The evidence each row records is
what the decision was taken on: LibreOffice under MPL-2.0 with nothing conveyed
and no file modified, poppler under GPL-2.0-or-later as separate programs invoked
at arm's length and never linked. Each row also states what its approval does
*not* reach, and those limits are part of the decision rather than caveats around
it - in particular that neither approval authorises conveying the CI image, and
that process isolation is not a redistribution safe harbour.

The rule bites in CI, not on a developer's machine: with a pending row the
runner reports one skip per document naming the outstanding decision, and
`--allow-pending-tools` drives the tools anyway. CI never passes that flag.

## Adding a seed

The baseline is what makes this more work than editing one file, and the order
matters:

1. Write the seed. HTML unless the construct is one HTML cannot state, in which
   case flat ODF - and the `why` has to say which and why, because the second
   language costs the ODT target most of its independence.
2. Pin a toolchain (the CI workflow installs `libreoffice-writer`,
   `poppler-utils` and four font packages) and run `--update-baseline` on it. It
   has to be that toolchain and not a developer's: the stamp records the
   platform, and a baseline made on Windows makes every comparison on the Linux
   leg a skip. `--restamp` is needed only when the toolchain itself has moved,
   and regenerating on the wrong one without it is refused rather than silently
   allowed.
3. Classify every row the regeneration writes. They arrive as
   `suspected-defect` carrying a placeholder `why`, which is what an unreviewed
   difference should read as, and they stay `suspected-defect` until somebody
   has an argument for `documented` or `accepted`. Replacing the placeholder
   with a real sentence is not the same act as classifying the row, and a
   finding worth keeping is worth both.

A run whose toolchain does not match the stamp does not fail. It runs the
toolchain-independent floor - the documents are produced, probed and read, and
the renders are measured - and skips every comparison, naming the field that
differs rather than passing quietly.

## Determinism

The font guards need two escape hatches, and both are declared rather than
inferred. `toolFonts` in the seed manifest names the faces the office suite brings
with it - LibreOffice draws a list bullet from a symbol font it ships itself, and
a pinned directory assembled from font packages cannot contain it. A seed may also
declare `substitutesFonts`, which says it knowingly asks for glyphs no pinned
family has; `non-latin-text` is the only one that does, and its `why` says which
line and why. Both degrade the guard to a **skip that names the reason**, never to
a pass: the page really was drawn by two different faces, and the numbers beside
it are worth that much less.

**Fonts, above everything.** LibreOffice substitutes a family it does not have
without a word - a made-up name came back as `DejaVuSans` with exit 0 and an empty
stderr. Four independent guards exist because one silent failure here poisons
every number downstream: the seed manifest constrains family names to the pinned
set by schema `enum` and a guard test; `pdffonts` checks what LibreOffice
actually embedded; the render manifest's `fonts.unmappedFamilies` must be empty,
which is a **failure** here where the corpus suite makes it a skip; and both
sides are given the same directory.

`--font-dir` alone is not enough, and that was found rather than assumed. It maps
faces by file name, so `LiberationSerif-Regular.ttf` arrives as the family
`LiberationSerif` and a document asking for `Liberation Serif` does not match. A
document that names no family at all - which is what LibreOffice writes into HTML
and RTF for body text - draws in the renderer's default `sans-serif`, a CSS
generic that needs a mapping of its own. `FontPinning` builds the explicit
`--font-file` list for both cases.

**One LibreOffice profile per concurrent job.** Concurrent jobs sharing a profile
lose exactly one per round, and the loser exits 1 with completely empty stdout
*and* stderr - a signature indistinguishable from a codec that produced nothing.
`--nolockcheck` does not help. The runner classifies that exact signature as
suspected profile contention rather than blaming the document.

**Never compare LibreOffice's bytes.** Its `.odt` and `.pdf` output is not
reproducible run to run; `SOURCE_DATE_EPOCH` is not honoured. Its rasterised
output *is* byte-identical, which is why the pixel axis is stable on one host.

**Environment.** The child gets `SAL_USE_VCLPLUGIN=svp` and `LC_ALL=C.UTF-8`, and
`PYTHONHOME`, `PYTHONPATH`, `URE_BOOTSTRAP` and every `SAL_*` and `OOO_*` are
scrubbed - they were verified to leak in from the host and be acted on. No xvfb:
the headless path has used the in-memory backend since 4.x. On Windows the binary
is `soffice.com`; `soffice.exe` is a GUI-subsystem program that detaches, never
terminates and prints nothing.

Conversions are **not** batched. Ten documents in one invocation cost about a
fifth of ten invocations, and one unreadable document takes the batch with it
while the per-document exit codes are lost. Correct attribution of a failure to a
document is worth more than the wall clock on a nightly job.

## The upstream ranking

Given `--extras-url` or `--extras-dir`, the runner does something else: it
takes every supported sample under LibreOffice's `sw/qa/extras` tree, renders
each with LibreOffice and with `broilerdoc`, scores the disagreement, and
writes the top few as an issue (`--issue-out`, `--create-github-issue`). The
nightly workflow runs it that way. There is no baseline: the ranking is a list
of places to look, not a verdict.

The score is dominated by the page-count gap, and that is why one family of
samples is kept out of it. **Fuzzer output is set aside.** LibreOffice keeps
about two dozen fuzzed files in that tree as crash regressions (23 of the
supported ones on 2026-10-20), and the upstream test for each
asserts only that LibreOffice survives it. What LibreOffice draws from one is
its handling of the damage - twenty pages of a table whose `cellpadding` the
fuzzer set to 65536 - which is not a reference. The first ranked run was three
of them, and every fix that read them more faithfully pushed them higher,
because the pages LibreOffice adds are the ones it invents; the
[note on that run](../notes/office-extras-forcepoint-verification-2026-10-20.md)
has the numbers.

The families are in `tests/office/extras-crash-regressions.json`, each a
pattern on the file name with its origin and the upstream evidence for it:
`forcepoint`, `ofz` (OSS-Fuzz) and `fuzzer-testcase` (`LIBREOFFICE-XXXXXXXX`).
A sample in one is not converted by LibreOffice and never enters the ranking.
It is not ignored either. `broilerdoc` is asked to read, dump and render it as
it would be for a document, and each of those has to end in a documented exit
code (0 to 6, [the CLI guide](cli.md#exit-codes)). Refusing a damaged file is
an answer; exit 70, a crash or a hang is a defect on any input. Such a failure
fails the run as `robust/<sample>`, is listed in full above the ranking in the
issue, and gets an issue filed even on a run that ranked nothing.

The line is fuzzed against written, not "tested for crashing". A minimised bug
document such as `crashHyphen.fodt` is also tested upstream only for not
crashing, but it is a document LibreOffice lays out on purpose, so it stays
ranked. The file lists such names under `stillRanked`, and
`OfficeControlGuardTests` fails if a pattern starts matching one, if a family
loses its evidence, or if a listed example stops matching its own family. A new
family needs the upstream test and what in the file shows it was generated.

**A document both refuse is not ranked either.** Ten samples produce
neither text nor pages in LibreOffice: the encrypted packages
(`Encrypted_*_abc.docx`, `sha1_correct.odt`), a PDF with a user password, and
two DOCX files with a part that is not well-formed XML. Opened without a
password, LibreOffice converts nothing, and `broilerdoc` exits 3. That is
agreement, but a read failure scores 1200, which put ten of them in the
seventh ranking's top thirty. The issue lists them under "Refused By Both" with
the reason `broilerdoc` gave; a crash or exit 70 on one is still ranked.

**Each tool is told a sample's format only when the other is.** LibreOffice
chooses the filter for a DOCX, ODT, RTF or PDF from the bytes, not the name,
and four samples rely on it: `tdf132596.docx` and three more are ODT packages
saved as `.docx`. The runner used to pass `--from docx` for them, and
`broilerdoc` refused what LibreOffice read. Now it passes `--from` only for
HTML and flat ODF, where it names LibreOffice's input filter as well; for the
rest `broilerdoc` probes the bytes as LibreOffice does. On the corpus of
2026-10-21 the probe picks the extension's format for every other sample.

## What it has found

| Finding | Outcome |
| --- | --- |
| The ODT reader took the page box from the wrong master page, so `geometry` was `off` for every ODT document | Fixed. The master page is the one the first block's style names; [notes/odt.md](../notes/odt.md), `OdtMasterPageTests` |
| The HTML codec carried no page, and the shared CSS length parser knew no absolute units | Fixed. `@page` is read and written, and centimetres and the other absolute units parse |
| LibreOffice states paragraph spacing in a `p { … }` rule that the reader ignored | Fixed by matching bare type selectors, and nothing more; [notes/html.md](../notes/html.md) says why the line sits there |
| The model had no page break, so no codec read or wrote one | Fixed. `ParagraphStyle.PageBreakBefore` exists in every format, and Markdown reports `markdown.page-break` |
| A forced line break (U+2028) was a break opportunity in both layout engines | Fixed in the CLI layout and in the PDF writer's page layout |
| Shape stacking order was not modelled | Fixed. `DocumentShape.ZOrder` is read and written by DOCX, ODT and RTF |
| A shape was drawn again on every page after its anchor's | Fixed. Anchor positions and wrap exclusions are page-local |
| An ODF gradient angle with a unit parsed as zero | Fixed. Both spellings are read, and the model's different zero direction is applied |
| `w:titlePg` was not read | Fixed. `RunningContent.DifferentFirstPage` holds it |
| The ODT master-page chain stopped at the first page | Fixed. One link of `style:next-style-name` is followed |
| RTF let a footer's field result into the body | Fixed. A field result belongs to the destination the field is in |
| A DOCX table style's paragraph and run properties were ignored, so every cell kept the body's spacing (`fdo68607.docx`, 15 pages against 8) | Fixed. They apply between the document defaults and the paragraph style; [notes/docx.md](../notes/docx.md), `DocxTableStyleTests` |
| The model had no text columns (`many-paragraphs-per-page-in-6-columns.odt`, 40 pages against 12) | Fixed. `PageGeometry.ColumnCount` and `ColumnGap`, read and written by DOCX, ODT and RTF, filled in turn by the layout |
| A DOCX column break in a section of one column was a line break (`FDO77715.docx`, 33 pages against 40) | Fixed. It is a page break where there is no next column, as ECMA-376 says of `ST_BrType` |
| A DOCX page break that opened its paragraph was demoted to a line break | Fixed. Such a paragraph starts the page |
| The extras ranking drew Times New Roman, Arial and Calibri with the host fallback while LibreOffice drew Liberation and Carlito | Fixed in the runner. The metric-compatible faces are mapped for both sides |
| A table row was placed whole, so one longer than a page ran off it (`tdf134965.odt`, 23 pages against 29) | Fixed. A row breaks across pages cell by cell unless it says it may not (`TableRow.CanSplit`) |
| A frame anchored to a page by number was placed beside a paragraph (`tdf135056.odt`, 4 pages against 17) | Fixed. `DocumentShape.AnchorPage`, and both layouts make the pages up to it |
| Sections were one flow in one set of columns (`tdf123163-1.docx`) | Fixed. `DocumentSection` carries its columns and margins, a DOCX section break starts its page, a column break moves to the next column, and a section's columns are balanced where the next follows on the page. The document stays four pages short for its fourteen missing pictures |
| A family the font mapping does not name was drawn with the host fallback and measured with an estimate of 125% (`tdf168017.docx`, set in Arial Narrow, 33 pages against 29) | Fixed in the CLI. Such a family is measured from the host fallback face it is drawn with |
| An empty paragraph was as tall as the document's default rather than its paragraph mark | Fixed. `RichTextParagraph.MarkStyle`, read from DOCX's `w:pPr/w:rPr`, ODF's paragraph text properties and RTF's formatting at `\par`, and written back by DOCX |
| A DOCX section that named no header continued nothing, and `first` and `even` parts were read whether or not `w:titlePg` and `w:evenAndOddHeaders` turned them on | Fixed. Each type the last section leaves out continues from the nearest section before it (ECMA-376 §17.10.5); a part the document does not turn on is not read, and the writer turns on the even parts it writes |
| A header or footer that ran past its margin was left out | Fixed. It is set at its declared distance and the text moves clear of it (ECMA-376 §17.6.11), each page at its own section's distances and margins |
| The model had no page background, so a page LibreOffice paints came out white (`fill.docx`, both `tdf126533_axialAngle` documents, `tdf159626_blackPatternFill.docx`, `fdo86857.html`) | Fixed. `RichTextDocument.PageBackground`, read and written by DOCX (`w:background`, shown by `w:displayBackgroundShape`), ODT (the master page's fill), RTF (`\background`, shown by `\viewbksp`) and HTML (the canvas colour), and painted first on every page. A VML fill is converted as LibreOffice converts it |
| A diagonal gradient was banded along the nearer axis, and no format's mirrored gradient was read | Fixed. Both painters band at the stated angle, clipped to the box; `ShapeFill.Axial` holds VML's 50% focus, ODF's `axial` and DrawingML's three symmetric stops |
| A VML shape in a `w:pict` was read only as a picture, so a text box written the pre-2010 way was lost with its text (`tdf126533_axialAngle.docx`) | Fixed. `v:rect`, `v:roundrect`, `v:oval` and a `v:shape` holding no picture are floating shapes |
| A shape positioned from the top of the page was hung that far below its paragraph | Fixed. `DocumentShape.OffsetFromPage`, read and written by DOCX, ODT and RTF; both layouts place the shape on its paragraph's page |
| A table row holding a nested table was kept whole, so a one-row report taller than a page left page one blank (`fdo48718-1.docx`, `fdo77887.docx`, 3 pages against 2) | Fixed. Such a row breaks between the nested table's rows (ECMA-376 §17.4.6) |
| A run's `w:color w:val="auto"` was ignored, leaving its style's colour | Fixed. `auto` is a colour (ECMA-376 §17.3.2.6), the default one |
| RTF had no tables, and ignored the page a section states (`fdo66565.rtf`) | Fixed. `\trowd` to `\row` is read into a `DocumentTable`, cells closed inside groups included, and the first section's `\pgwsxn` and margins over the document's. The writer still writes a table as its paragraphs |

The [note on the second ranked run](../notes/office-extras-columns-verification-2026-10-20.md)
has the numbers for the eight rows before the last four, and the documents in
that run that were not this component's reading; the
[note on the third](../notes/office-extras-marks-and-bands-verification-2026-10-20.md)
has the last four, and why its other two documents still stand where they do;
the [note on the fourth](../notes/office-extras-backgrounds-verification-2026-10-21.md)
has the last eight.

Still open, and tracked in the [roadmap](../roadmap.md#test-suites-and-tooling):
the per-line leading drift on `long-flow-multipage`, the nested-list marker
disagreement through RTF, LibreOffice's `<div title="header">` read as body
text, and `table-simple` through HTML.

Classified, not open:

- `letterhead-frames/rtf` is `documented`. With `	itlepg` and no `ooterf`,
  page one gets no footer, as in Word; LibreOffice draws the default one. Matching
  LibreOffice would make this component disagree with its own DOCX reader.
- List labels, note citations and table cell separators follow LibreOffice's
  text export, so those projection differences no longer masquerade as lost text.
- `lists-nested/html/roundtrip` carries `html.list`, which is the writer saying
  it did not emit list structure.

One lesson worth keeping: a band that gets worse after a fix is not necessarily a
regression. One defect can flatter the score of another, and fixing the first
lets the measurement see the second.

## What it cannot tell you

**LibreOffice is not a specification.** For `.docx` the reference is Word, for
`.odt` the OASIS standard, for `.rtf` Microsoft's own. A disagreement here names
a difference, not a defect, and this component may be the one that is right. That
is what a `documented` row citing a conformance document is for.

**The pixel axis cannot assert typography.** Not glyph shapes, anti-aliasing,
sub-pixel positioning, hinting, exact line-break positions, justification
spacing, hyphenation points, or colour fidelity below a few units per channel. It
catches content that is missing, blank, on the wrong page, or shifted by a
paragraph. It does not catch a kerning change and is not trying to.

**The bands are a judgement.** A document sitting on a boundary will flap, and
the answer is to widen the band and write down why, not to argue with the metric.

**The text canonicalisation can hide a bug.** Whitespace, soft hyphens,
non-breaking spaces and line separators are normalised away on both sides. A
codec that differs only in those is reported clean. That is not hypothetical:
`line-break-within-paragraph` was added because a forced break was lost in layout
on every target while every one of its `text` rows passed.

**Neither text projection carries a shape or a running band.** LibreOffice's
`txt` filter drops the text inside a frame and the text in a header or footer,
and so does `broilerdoc dump --as text`. So for a document built out of those -
which is what `letterhead-frames` is - the semantic axis compares two projections
that both omit the content in question and agrees. The whole assertion of such a
seed is on the pixel axis, and a reader taking a green `text` row as evidence
about a letterhead would be reading it backwards.

**A guard that fires is reporting the guard.** LibreOffice's silent font
substitution and its silent plain-text fallback are both policed here, and both
policing checks fail as `pixel/.../fonts` and `produce/...` rather than as a
finding about a codec.

**Results are comparable only within one pinned toolchain.** The stamp exists so
that a run on anything else skips rather than lying.

**The pixel axis is weakest on Windows.** LibreOffice enumerates the system font
store there and there is no fontconfig lever to restrict it, so the two sides
cannot be guaranteed to draw with the same faces even when `--font-dir` is given.
The semantic axis is unaffected and worth running on both legs: path handling
and UTF-8 decoding of a foreign file differ between the two hosts, and those are
what the Windows leg is for. It does not exercise the `-` stdin and stdout paths -
[the corpus suite](corpus-suite.md) is where those are covered.

**The corpus is still ours.** LibreOffice reshapes seeds authored in this
repository into markup no writer here produces, which closes the corpus suite's
stated gap by one step. It does not introduce constructs from real-world
documents and never will while third-party document artifacts are rejected by
default, per artifact, by every format's IP register.

**And a construct nobody wrote a seed for is not covered, whatever the numbers
say.** That reads as a truism and was not treated as one: for the corpus's first
twelve seeds the limit was not what anybody chose to write but what the seed
language could hold, and the whole family of constructs a letterhead is built
from - header, footer, anchored frame, shape fill, field, placeholder - was
absent from all forty-eight documents because HTML cannot state any of it. The
flat ODF form lifts that particular ceiling and does not change the shape of the
limitation. Reading a green run as coverage of a construct requires knowing that
some seed carries it, and the seed list is the only place that is written down.

**A bug both implementations share passes.** That is the standing limitation of
every differential suite, and it is why this one sits beside
[the corpus suite](corpus-suite.md) rather than replacing it.

**It is not a required check.** It is nightly and dispatchable, and it must not
become required while the reference implementation is a floating third-party
binary.

## Rendering pass threshold

The workflow dispatch input `threshold` and runner option `--threshold <percent>`
set the minimum rendering similarity, defaulting to **80** percent. Values from
0 to 100 are accepted, including decimals. Scheduled runs also use 80 percent.
For example, `bash eng/run-office-conformance.sh --threshold 90` requires 90%.

Each compared page must meet the threshold. Similarity is 100 times the lowest
of the ink-profile cosine similarity, one minus the blurred pixel difference
ratio, and one minus the ink-coverage difference. This is a rendering metric,
not a percentage of identical document bytes or exact pixels. Page counts must
agree and page dimensions must match within the existing geometry tolerance.
The page limit (`--max-pages`, default 40) still bounds the pages inspected.

This threshold replaces the fixed best-band verdict for upstream samples and
the pixel baseline-band verdict for seed documents. Baseline stamps, tool and
font guards, semantic checks and baseline regeneration retain their existing
rules. Reports keep the measured bands and severity ranking for investigation;
failed pixel checks include the measured similarity and required threshold.
The issue's difference counts and ranking use actual failed check results,
including the configured rendering threshold. Non-best descriptive bands or
informational diagnostics alone do not make a passing document an issue entry.
Skipped axes are not failures. The issue summary and JSON report record the
configured threshold. Renderer failures without measurements are still reported.
The [threshold-reporting verification note](../notes/office-threshold-reporting-verification-2026-10-23.md)
records artifact replays and the 80%/100% end-to-end check for this policy.
The upstream workflow continues to publish a ranking; its existing policy does
not fail the job for individual upstream sample differences.

## Temporary storage in CI

The workflow sets `TMPDIR` to GitHub's disk-backed runner temporary directory,
mounted into the job container, for .NET and its child tools. Retaining corpus
downloads and rasterized pages on a quota-limited temporary filesystem can
exhaust that quota and prevent LibreOffice from producing comparison output. Each run logs
filesystem capacity and mount information for both locations. The runner removes
its workspace at the end unless `--keep` was requested.

Upstream samples use separate workspaces, removed immediately after each sample
finishes (including on exceptions). Only failure images for the current top
`--top-differences` samples are retained until the report is written. This bounds
intermediate storage by the concurrent documents rather than the entire corpus.
`--keep` explicitly disables this cleanup for local investigation.


## Paragraph pagination rules

The [paragraph-pagination verification note](../notes/office-issue-135-pagination-verification-2026-10-22.md)
records keep-with-next, keep-together and explicit widow/orphan controls read
from DOCX and ODT. Authored page assignments match LibreOffice, while the five
sampled issue #135 scores remain unchanged. The note describes the supported
body and table-cell cases and the remaining limitations.

The [table/image verification note](../notes/office-issue-135-table-image-verification-2026-10-22.md)
records the subsequent page-frame positioning, table keep-with-next and cell
padding changes. One sampled blank-image failure now passes the pixel check;
the table results are mixed, including a page-count regression documented there.

The [table-geometry verification note](../notes/office-issue-135-table-geometry-verification-2026-10-22.md)
records preferred widths, horizontal placement and cell vertical alignment.
Authored geometry agrees with LibreOffice; the seven ranked samples still fail
their pixel checks despite lower blurred differences in several cases.

The [empty-cell and table-break verification note](../notes/office-issue-135-empty-cells-breaks-verification-2026-10-22.md)
records preservation of empty ODT cells, first-paragraph cell spacing and table
page/column breaks. It also covers the PDF fix for text lost after blank table
continuation pages, and the remaining mixed results on upstream samples.

The [issue #136 verification note](../notes/office-issue-136-verification-2026-10-22.md)
records five list, symbol and RTF page corrections. Four sampled text comparisons
now pass and two RTF page geometries match; the eight sampled pixel comparisons
still fail. The note includes the selected samples, authored checks and limits.

The [section-colour and indent verification note](../notes/office-issue-136-indents-backgrounds-verification-2026-10-22.md)
records five further corrections. The nested-section sample now passes the
visual comparison, one page-count gap narrows, and other sampled results remain
mixed. Exact non-list indents round trip through DOCX and ODT; the note records
the remaining list and standalone PDF limitations.

The [floating-frame and text-box verification note](../notes/office-issue-136-floating-frames-textboxes-verification-2026-10-22.md)
records five further corrections to graphic defaults, frame pagination, text-box
tables and text export. One sampled visual check now passes and another page
count matches; the note also records a visual regression and text-export
differences exposed by retaining previously omitted content.

The [issue #137 verification note](../notes/office-issue-137-verification-2026-10-22.md)
records ten corrections to page styles, fixed rows, numbering, hidden text and
text-box layout. All 30 ranked samples were compared before and after; the note
records newly passing checks, corrected page counts and remaining regressions.

The [linked-frame and merged-cell verification note](../notes/office-issue-137-textframes-merged-cells-verification-2026-10-22.md)
records five further corrections. A five-versus-two page mismatch closes, while
the focused pass count stays unchanged; the note records mixed visual metrics,
regression tests and the remaining export limitations.

The [issue #138 fill and field verification note](../notes/office-issue-138-fills-fields-verification-2026-10-23.md)
records ten shared corrections to background painting and RTF field text. The
30-sample comparison improves from 40/90 to 42/90 passing checks, without lost
passes; it includes authored LibreOffice checks and the remaining limits.

The [floating-table and numeric-cell verification note](../notes/office-issue-138-floating-tables-numeric-cells-verification-2026-10-23.md)
records the next ten corrections: preferred widths, grid scaling, repeated
headers and numbering, non-overlap, continuation preservation and section
ownership, locale-aware numeric display, and vertical-merge export.

The [drawing-geometry verification note](../notes/office-issue-138-drawing-geometry-verification-2026-10-23.md)
records five further corrections to outline retention, line paths, stroke
widths, frame borders and ellipses, with authored LibreOffice comparisons.

## Focused triage and repeatable control groups

Extras runs through `eng/run-office-conformance.sh` also write
`test-results/triage/triage.json`, `triage.md`, `top-samples.txt` and
`all-samples.txt`. The JSON includes every selected sample's SHA-256, diagnostic
codes, check outcomes and available rendering observations. Observation groups
separate text, pagination and visual differences, incomplete references,
crash regressions and documents refused by both tools. These are investigation
hints, not automatic root-cause diagnoses. Only actual failed checks enter the
ranked replay list; passing checks with imperfect visual measurements stay in
the complete control group.

Use an exact list to repeat a ranking or retain passing controls while developing
a fix. IDs are relative to the extras root, one per line. Empty lines and lines
beginning with `# ` are ignored; duplicates are removed. Missing or invalid IDs
fail the run instead of silently reducing coverage. An empty ranking file means
there are no ranked failures; use `all-samples.txt` to rerun that control group.
Exact selection cannot be combined with `--only` or `--max-files`.
Remote selections download only the requested files without enumerating or
cloning the entire upstream corpus.

```bash
bash eng/run-office-conformance.sh \
  --sample-list tests/office/selections/issue-147.txt \
  --output-dir test-results/issue-147-before

# Repeat with the same inputs and reference environment after changing broilerdoc.
bash eng/run-office-conformance.sh \
  --sample-list test-results/issue-147-before/triage/all-samples.txt \
  --previous-triage test-results/issue-147-before/triage/triage.json \
  --output-dir test-results/issue-147-after
```

The checked-in issue #147 list contains the original 30 samples, including
subsequently fixed cases. It is a control group, not a statement that all cases
remain defective. For reproducibility, point `--extras-url` at an immutable
LibreOffice commit, or use an archived local corpus with `--extras-dir`.
A list alone does not pin upstream bytes; recorded hashes reveal changed inputs.
The runner executable also accepts `--sample-list`, `--triage-dir` and
`--previous-triage` directly. Seed/baseline update mode does not use these options.

The before/after report counts a check as fixed only when the same input bytes
and recorded environment change from failed to passed. It distinguishes new
samples, missing samples, changed coverage, changed diagnostics and changed
measurements. Changing DPI, threshold, axis, page limit, reference timeout,
platform or recorded reference-tool/font labels disables fix/regression
inference. Changing the Broiler tool is intentional and does not disable it.
Worst-page metrics are comparable only when the worst-page number and both page
counts also match. The JSON retains the measurements on both sides.
Environment labels do not prove identical installed fonts; keep the actual font
set and native tools pinned. A passing raster check does not prove full Office
conformance or native export fidelity; retain codec regression tests and authored
native-tool probes for problems such as style retention and exported image URIs.

For a manual GitHub Actions run, set the existing **only** input to
`@tests/office/selections/issue-147.txt` (or another committed list). The workflow
shows the triage Markdown in the job summary and uploads it with the existing
results artifact. Exact-list runs have independent concurrency and never update
the global ranking issue, even if **createIssue** retains its default. The CLI
rejects `--sample-list --create-github-issue` for the same reason. Scheduled/full
runs retain their serialized global issue reporting. To compare CI artifacts
locally, extract a previous `triage.json` and pass it via `--previous-triage`.

### Recovering a .NET download failure

If `actions/setup-dotnet` fails, the Office workflow tries
`eng/install-office-dotnet-fallback.sh` against Microsoft's public
`https://builds.dotnet.microsoft.com/dotnet` feed. This handles the observed case
where `aka.ms` resolves to `dotnetorigin.blob.core.windows.net` and runtime/SDK
downloads return HTTP 409 despite the packages being available on the public
builds feed. The script resolves the current stable .NET 10 SDK and passes its
explicit version to the official installer, bypassing the `aka.ms` redirect.
The SDK supplies its matching runtime. The recovered installation is placed in
runner temporary storage and exported through `GITHUB_PATH` and `DOTNET_ROOT`.
If recovery fails too, the job fails; no conformance checks are reported as run.

The [issue #152 verification note](../notes/office-issue-152.md) records inline
DOCX boundary and ODT master-page fallback corrections, with a complete 30-case
control list and before/after native-reference results.

The [RTF shape-property follow-up](../notes/office-issue-152-rtf-shape-properties.md)
records ten further import/export corrections, authored native comparisons and
remaining LibreOffice consumer limitations.

The [VML follow-up](../notes/office-issue-152-vml-shapes.md) records ten DOCX
shape-reading corrections, group/type handling, native line/polyline distinctions,
and the remaining mixed measurements in the original control group.

The [RTF group/formatting follow-up](../notes/office-issue-152-rtf-groups-formatting.md)
records retained graphics in a ranked original, ten reader corrections, native
controls and the remaining paper-size, nested-group and decoration approximations.

The [table grid/style follow-up](../notes/office-issue-152-table-grids-styles.md)
records ten import/export corrections, a native sparse-cell export check and
remaining native/autofit differences without a newly resolved ranked original.

The [WordprocessingML theme-color follow-up](../notes/office-issue-152-word-theme-colors.md)
records five corrected color contexts, cache-versus-theme precedence and native
export checks, including the tested LibreOffice importer's cache preference.

The [issue #154 verification note](../notes/office-issue-154.md) records legacy
RTF drawings and DOCX shape format-matrix references, a new hashed 30-case
selection, native controls and mixed observations in the original files.

The [DrawingML color follow-up](../notes/office-issue-154-drawing-colors.md)
records 15 additional color behaviors, a reusable native-probe generator and
before/after checks, including unchanged original cases and rounding limits.

The [saved-diagram follow-up](../notes/office-issue-154-cached-diagrams.md)
records 15 SmartArt cache-import behaviors, a newly passing original pixel check,
the native PDF/TXT projection gap and reproducible derived controls.
