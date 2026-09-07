# The office conformance suite

`src/tests/Broiler.Documents.Office` is a console runner that asks LibreOffice to
write the documents, reads them back with the built `broilerdoc`, and holds the
disagreements against a committed baseline.

```bash
dotnet run --project src/tests/Broiler.Documents.Office
```

It prints `N/M passed, K failed.` as its last line and exits with the number of
failures, the same contract the corpus suite has. `eng/run-office-conformance.sh`
provisions the tools first and then runs it; `.github/workflows/office-conformance.yml`
runs that nightly.

## Contents

- [Why it exists](#why-it-exists)
- [Where the documents come from](#where-the-documents-come-from)
- [The semantic axis](#the-semantic-axis)
- [The pixel axis](#the-pixel-axis)
- [The baseline](#the-baseline)
- [The tool register](#the-tool-register)
- [Determinism](#determinism)
- [What it found](#what-it-found)
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

Nothing is committed and nothing is downloaded.

[ADR 0013](adr/0013-standards-ip-provenance-and-claims-beyond-pdf.md) gives every
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

It shipped with both lists empty and every stamp field null, which said plainly
that nothing had been measured against an approved, pinned toolchain yet.
`tests/corpus/external-sources.json` still ships with no rows on the same
argument. This one no longer does: it carries the stamp of the toolchain the CI
workflow provisions and the rows the runs on it produced.

## The tool register

`tests/office/tools/manifest.json` holds LibreOffice and poppler, and **both are
approved**, by the project reviewer on 2026-09-06. The rule is the one this
component already operates for its PDF oracles: a tool absent from the register
may not run in CI, and no entry in it may become a product reference.

The file shipped with both rows pending, which was the point rather than an
oversight - a row exists so a tool's details have somewhere to land while it is
reviewed, and writing one down approves nothing. The evidence each row records is
what the decision was taken on: LibreOffice under MPL-2.0 with nothing conveyed
and no file modified, poppler under GPL-2.0-or-later as separate programs invoked
at arm's length and never linked. Each row also states what its approval does
*not* reach, and those limits are part of the decision rather than caveats around
it - in particular that neither approval authorises conveying the CI image, and
that process isolation is not a redistribution safe harbour.

Note where the rule bites - CI, not a developer's machine. That distinction
mattered while the rows were open, because it let the suite be useful to whoever
was writing it: the runner reported one skip per document naming the outstanding
decision, and `--allow-pending-tools` drove the tools anyway. The flag is still
there and CI still never passes it; it now does nothing that approval has not
already done.

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

## What it found

The suite is new and these are its first results, from a Windows host with
Liberation and DejaVu pinned on both sides. They are recorded here rather than in
the baseline because they were not measured on an approved, pinned toolchain -
which is the distinction the whole `producedWith` stamp exists to keep.

**The ODT reader took the wrong master page.** *Fixed; the row below is what the
suite reported before it was.* `geometry` came back `off` for every single one of
the twelve `odt` documents, and never for `docx` or `rtf`.
LibreOffice's ODT carries two page layouts - `Mpm1` on the master page `Standard` (A4, 2cm margins) and `Mpm2` on
the master page `HTML` (21.59×27.94cm, 2.54cm margins, which is what the seed
asked for). LibreOffice lays the body out on the `HTML` master page; this
component reads `Standard`. So the same file renders 1224×1584 there and
1191×1684 here, and the disagreement is not about typography at all - it is a
page box read from the wrong style. Nothing else in the repository would notice,
because every other render check either states the page on the command line or
compares a document against itself.

ODF states the link from the other end — a master page never says it is the
first, the content says which one it is on, through the
`style:master-page-name` of the style on its first block — and the reader now
follows that, with `Standard` and then the first master page as the fallbacks.
All twelve ODT documents report `geometry: match`, and the metrics downstream of
the page moved with it: `plain-paragraphs/odt` went from an ink box off by 33
pixels and a profile similarity of 0.687 to 9 pixels and 0.981, which is what its
DOCX twin had been scoring all along. That is the shape of a finding worth
having — the pixel axis did not say the renderer was wrong, it said two engines
disagreed about the paper, and the paper turned out to be a reader bug three
layers away from anything anyone would have thought to look at.
[The ODT conformance document](odt-conformance.md) states the resolution rule,
and `OdtMasterPageTests` holds it.

**The HTML codec did not carry the page at all.** *Also fixed.* The twelve `html`
documents came back `off` as well, and that was not the same finding wearing the
same word - two different causes reaching one band, which is why the failure
message names the geometry before the four numbers underneath it. LibreOffice's
HTML carries its page in a CSS `@page` rule, and this codec looked at no
stylesheet: the reader skipped every `<style>` element, correctly, because its
content is not text the document says, and in doing so threw away the one thing
in a stylesheet the model has somewhere to put. The writer emitted no rule
either, so a document arriving from DOCX or ODF stating US Letter left through
HTML stating nothing - a silent loss, because no reader of the result could tell
it from a document that never stated a page.

Both halves are implemented, and the shared CSS length parser learned the
absolute units on the way. It knew `px`, `pt`, `em` and `rem`, which is what a
hand-written page uses; every length LibreOffice writes is in centimetres, and
for those it had been returning false - not a wrong number, but no number, so
any length in centimetres anywhere in an HTML document had been arriving as
nothing at all. All twelve now report `geometry: match`, which makes it 48 of 48
across the four formats.

What the fixed page then made visible is a *different* gap: the HTML rows scored
worse than their DOCX and ODT twins - `plain-paragraphs` at an ink box of 38
pixels against 9 - because LibreOffice states paragraph spacing in a stylesheet
rule, `p { line-height: 115%; margin-bottom: 0.25cm }`, and this codec applied no
rule that was not an inline `style` attribute.

*Narrowed, and the narrowing is the interesting part.* Reading the stylesheet
sounded like implementing the cascade, which is a much larger thing than reading
one at-rule. Measuring what LibreOffice actually emits cut the job down instead.
Converting DOCX to HTML it writes both the rule **and** an inline style on every
paragraph that overrides it, so for those documents the sheet never mattered;
converting HTML to HTML it writes bare `<p>` elements and lets the rule say
everything, and that is the case that was being lost. Every selector in the
sample was `@page` or the bare type selector `p`. So the codec now matches
exactly one selector - the bare element name - with the element's own `style`
attribute beating it property by property, and everything past that stays
unimplemented and is reported as `html.css.rule` rather than dropped in silence.
[The HTML conformance document](html-conformance.md) states what is and is not
applied, and why a document reader draws the line there rather than growing a CSS
engine.

Re-measured afterwards, the HTML rows come back level with their twins:
`plain-paragraphs` at an ink box of 9 pixels and a profile of 0.982 against the
DOCX twin's 9 and 0.981, where before it was 38 and 0.866; `links` at 3 and 0.997
against 4 and 0.997. One seed did not move and it is the one that says where the
line now sits. `table-simple` is still 107 against 11, because a document with a
table makes LibreOffice write `td p { ... }` and `th p { ... }` - descendant
selectors, which are past the boundary - and because HTML tables are flattened to
their cell paragraphs regardless. Two causes, one number, and the smaller of them
is the one this change could have addressed.

**An explicit page break did not paginate.** *Fixed.* `page-break-explicit` came
back 1 page here against 2 in LibreOffice, through all four formats. Four formats
agreeing ruled out a codec - and it turned out to rule out the layout engine too.
Nothing was dropping the break, because there was nowhere to drop it from: the
document model had no page break at all, and no codec here read or wrote one. A
property that does not exist loses nothing and reports nothing, which is why four
independent codecs agreed so precisely.

`ParagraphStyle.PageBreakBefore` exists now, all four formats read and write it -
`w:pageBreakBefore` and the run-level `w:br`, `fo:break-before`, `\pagebb` and
`\page`, `page-break-before` and `break-before` - and Markdown, which has no page
and so no page break, reports `markdown.page-break` rather than dropping it
quietly. The layout engine takes the break, and `--continuous` ignores every break
and counts them in a render note, because a mode that exists to remove pagination
should say when it removed some. All four formats now paginate 2 against
LibreOffice's 2, and the ink profile on that seed went from `poor` to `close` -
0.545 to 0.988 through HTML - because the content is on the pages the document put
it on.

**Line height drifts over a long document.** `long-flow-multipage` is the only
seed that reaches `severe` on the blurred difference (0.28-0.31) while its
`coverage` stays `close` - the same ink, in the wrong places. The heat diff shows
why: the lines agree at the top of page one and smear progressively into doubled
text further down, then re-sync. It is a small per-line leading difference
accumulating, and it is invisible on any one-page seed.

**Plain-text projections differ on lists and tables, and neither side is wrong.**
LibreOffice's text export writes the bullet and the number into the text
(`• first bullet`, `1. ordered one`) and joins a table row onto one line
(`cell a cell b`); `broilerdoc dump --as text` writes neither marker and gives
each cell its own line. That is a projection difference rather than content loss,
and it is what the `documented` and `accepted` baseline states are for. One
detail inside it is a real divergence worth a look: through RTF the two disagree
about the *marker for a nested level* - `•` here against `◦` there.

**The HTML list round trip warns, as it should.** `lists-nested/html/roundtrip`
is the one row carrying a diagnostic code, `html.list`, which is the writer
saying it did not emit list structure. [The roadmap](roadmap.md) already lists
HTML list writing as deliberate and unimplemented, so this is a `documented` row
rather than a finding - and it is the shape a correct codec makes in this suite.

Everything else was clean. Every document produced, probed and read without a
single exit 70; `links/docx` came back `exact` on the profile and `clean` on the
blurred difference, which is what agreement between two independent layout engines
looks like when there is nothing wrong; and a full generate-then-verify cycle over
both axes reported every check passing with five skips, each naming its
reason. That cycle is the thing worth running before trusting any of the numbers
above: it proves the baseline is stable, which is a different claim from the
component being correct and the only one a suite can make about itself.

### What the two later seeds found

`line-break-within-paragraph` and `letterhead-frames` were added after everything
above, and between them they produced eleven new baseline rows. Nine were
suspected defects and none of them was reachable before. All nine are fixed, and
the two that remain are limitations with a document behind them.

**A forced line break was not a break in layout.** *Fixed.* All four targets
carry the construct and all five codecs read and write it as U+2028, the model's
own line-break character - and `DocumentLayout` then classified characters with
`char.IsWhiteSpace`, for which U+2028 is true. So the break arrived as an ordinary
break *opportunity*, the seed's five-line address reflowed onto two, and every
paragraph under it moved up the page. Four targets failing at the same profile
similarity, 0.29, was the signature: a layout defect, not a codec one.

The tokenizer now yields a break of its own and the wrapper ends the line on it.
`PdfPageLayout` had the same defect wearing different clothes and is fixed with
it - its word scan knew only spaces and tabs, so the character was swept into the
word beside it and handed to the content stream as a glyph no standard font has.
The four rows went from an ink box of 175-202 px and a profile of 0.29 to 22-32 px
and 0.90; what is left is per-line leading, which every prose seed here reports.
Justification is deliberately unchanged: LibreOffice stretches the line before a
manual break like any other, which was measured rather than assumed.

Worth keeping in view: the semantic axis passed clean throughout, and that is not
a weakness in the seed. The text canonicalisation maps U+2028 to a newline on both
sides before comparing, so `text` is blind to this construct by construction and
only the pixel axis could ever have reached it.

**A header's shape painted over the body's shape.** *Fixed.* Both of
`letterhead-frames`'s frames are foreground objects; the layout appended
running-content shapes after the body's and the rasterizer drew foreground shapes
in list order, so the header's band was painted last and the bordered box the
document stacks in front of it disappeared underneath. Both files stated the
order - `draw:z-index` 0 and 1, `relativeHeight` 2 and 3 - and neither was read.
`DocumentShape` said so in its own summary: *"Order among shapes is not modelled:
they draw in the order they were read."*

It is modelled now. `DocumentShape.ZOrder` is one order for the whole document,
which is how all three formats state it and what a letterhead needs, since the
band is in the header and the box is in the body. All three readers take it -
`draw:z-index`, `relativeHeight`, `\shpz` - all three writers state it, and both
layout engines order every shape on a page by it before drawing. A shape whose
format says nothing sits at zero and keeps the order it was read in.

**A shape was drawn on every page after its anchor's.** *Fixed, and found while
fixing the one above.* The map of anchor tops was never emptied at a page break,
so the logo box anchored to the first paragraph was drawn again on page two at the
y it had on page one. The wrap exclusions were the same defect twice over: a band
in page-one coordinates pushing text aside on a page with nothing beside it. Both
are page-local and both are cleared when a page ends.

**A gradient angle with a unit on it parsed as zero.** *Fixed.* LibreOffice 24.2
writes `draw:angle="30deg"`; `OdtReader` parsed the attribute as a bare number of
tenths of a degree, `TryParse` failed on the unit, and the false branch yielded
`0`, so the band ran left to right instead of top to bottom. A failed parse and an
absent attribute reached the same branch, so nothing was reported.

Both spellings are read now - ODF 1.2 typed the attribute as tenths and ODF 1.3 as
an angle with `deg`, `grad` or `rad` - and the value is then turned a quarter,
which a unit-aware parse alone would not have done. The two formats do not share a
zero: ODF measures a gradient from *down* the page and the model from *along* it.
That was measured rather than derived from prose - LibreOffice was asked to render
a black-to-white gradient at five ODF angles and the corners were sampled - and
confirmed from the other side, since the same shape LibreOffice writes as `30deg`
in ODF it writes as `ang="3600000"`, sixty degrees, in OOXML.

**`w:titlePg` was not read.** *Fixed.* The seed's DOCX declares a first-page
header, a default footer and the flag. LibreOffice puts no footer on page one;
this component put the default one there, carrying the `PAGE` field's stale cached
result. The flag is what makes a `first` reference mean anything and what makes a
band the first page does not name *empty* there rather than the default one, and
`RunningContent.DifferentFirstPage` is where the model keeps it. The row went from
an ink box of 1154 px to 52, which is a footer distance and not a lost band.

**The ODT master-page chain stopped at the first page.** *Fixed.* The reader
resolved the master page the body starts on, correctly, and filed its header into
the *Default* slot - so `style:next-style-name` was never followed, the band
repeated on page two, and the `Continuation` master's footer was never read. The
chain is followed by one link now: the master page the body begins on supplies the
first page and the one it names supplies the rest. One link and not the whole
chain, because the model holds three selections rather than a sequence of pages.
With the gradient and the stacking, that row went from 1180 px and a `poor`
profile to 31 px and `fair`.

**RTF let a footer's field result into the body.** *Fixed.* The one nobody
predicted. `text/letterhead-frames/rtf` opened with a bare `2` that LibreOffice's
projection did not have: the reader skipped the `{\footer ...}` destination's own
text but kept the text inside `\fldrslt`, so the cached page number landed at the
head of the body while the word `Page` beside it was correctly dropped. A field
result belongs to the destination the field is in, and the state now carries that
destination through the group that holds it. Both the `text` row and the `interop`
row it produced are gone from the baseline.

Two rows are left, and both are limitations rather than defects.

`letterhead-frames/rtf` is `documented`, and it is the one place in this corpus
where following the format costs a band. `\titlepg` is read now, so no footer is
drawn on the first page of a section that states the flag and names no
`\footerf` - which is what Word does. LibreOffice draws the default footer there,
and that 1193 px is the whole of the difference. Matching LibreOffice would have
been the cheaper number and the worse answer: its own DOCX importer honours the
same flag the Word way, so agreeing with it here would have made this component
disagree with itself across two of its own readers.

`letterhead-frames/html` carries two causes. LibreOffice cannot write a frame into
HTML, so it flattens both to GIF files beside the document; the reader never
fetches an external resource and reports `html.skip.external`, which
[the HTML conformance document](html-conformance.md) states in both directions.
The other half arrived with the line-break fix: LibreOffice writes the page's
header as `<div title="header">`, this reader has no notion of that convention and
reads it as body content, and the `<br/>` inside it is now honoured - so the body
opens with a blank line LibreOffice puts nowhere. Honouring the break is right;
reading a header as body is what needs deciding.

One thing the fixes made visible is worth recording on its own, because it will
happen again. Two rows scored *worse* at the point where the page-scoping fix
landed - `letterhead-frames/odt` from `fair` to `poor`, `docx` from 0.930 to 0.922
- because the box wrongly repeated on page two had been overlapping rows
LibreOffice inks there. One defect was flattering the score of another, and the
next round of fixes took both rows well past where they started. A band that gets
worse after a fix is not necessarily a regression; it can be the measurement
finally seeing what was under the thing that just moved.

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
`line-break-within-paragraph` exists because a forced break is lost in layout on
every target, and every one of its `text` rows passes.

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
