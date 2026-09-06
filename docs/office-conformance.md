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
- [The tool register, and why the suite ships inert](#the-tool-register-and-why-the-suite-ships-inert)
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
whole tree to enforce it. So a seed is a fragment of authored HTML held as a
string in `tests/office/office-corpus.json` - the same device `corpus.json`
already uses for RTF control words and ZIP part content - and LibreOffice
manufactures the real documents at run time in a temporary directory that
`Program.Guard` refuses to place inside the repository.

Twelve seeds, four target formats, forty-eight documents. Each seed exercises one
thing and says in its own `why` what that is. The corpus is deliberately small:
the first baseline has to be classified by a person, and a suite whose baseline
nobody has read is a log rather than a control.

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

It ships with both lists empty and every stamp field null, which says plainly
that nothing has been measured against an approved, pinned toolchain yet.
`tests/corpus/external-sources.json` ships with no rows on the same argument.

## The tool register, and why the suite ships inert

`tests/office/tools/manifest.json` holds LibreOffice and poppler, **both
pending**. The rule is the one this component already operates for its PDF
oracles: a tool absent from the register may not run in CI, and no entry in it
may become a product reference.

Note where that rule bites - CI, not a developer's machine. So the runner does
not refuse to exist: it reports one skip per document naming the outstanding
decision and the file the row is in, and `--allow-pending-tools` drives the tools
anyway for local work. CI never passes that flag. **A fully-skipped nightly run
before those two rows are decided is the expected state of this suite, not a
broken one**, and it is worth knowing that before filing a bug about it.

Adopting the suite is therefore three steps, in order:

1. Decide the two rows in `tests/office/tools/manifest.json` - both are
   process-isolated, neither is linked, and poppler's GPL is the one that wants a
   sentence rather than a shrug.
2. Pin a toolchain (the CI workflow installs `libreoffice-writer`,
   `poppler-utils` and four font packages) and run `--update-baseline --restamp`
   on it.
3. Classify every row the regeneration writes. They arrive as
   `suspected-defect` with no reviewer, which is what an unreviewed difference
   should read as.

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

What the fixed page then made visible is a *different* gap, and it is left open:
the HTML rows still score worse than their DOCX and ODT twins - `plain-paragraphs`
at an ink box of 38 pixels against 9 - because LibreOffice states paragraph
spacing in a stylesheet rule, `p { line-height: 115%; margin-bottom: 0.25cm }`,
and this codec applies no rule that is not an inline `style` attribute. That is
a cascade, which is a much larger thing than reading one at-rule, and
[the HTML conformance document](html-conformance.md) says so under its known
limitations rather than leaving a reader to infer it from the numbers.

**An explicit page break does not paginate.** `page-break-explicit` came back
1 page here against 2 in LibreOffice, through all four formats. Four formats
agreeing rules out a codec and points at the layout engine.

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

Everything else was clean. All 48 documents produced, probed and read without a
single exit 70; `links/docx` came back `exact` on the profile and `clean` on the
blurred difference, which is what agreement between two independent layout engines
looks like when there is nothing wrong; and a full generate-then-verify cycle over
both axes reported 667 of 667 checks passing with five skips, each naming its
reason. That cycle is the thing worth running before trusting any of the numbers
above: it proves the baseline is stable, which is a different claim from the
component being correct and the only one a suite can make about itself.

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
codec that differs only in those is reported clean.

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

**A bug both implementations share passes.** That is the standing limitation of
every differential suite, and it is why this one sits beside
[the corpus suite](corpus-suite.md) rather than replacing it.

**It is not a required check.** It is nightly and dispatchable, and it must not
become required while the reference implementation is a floating third-party
binary.
