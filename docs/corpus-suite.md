# The corpus suite

`src/tests/Broiler.Documents.Corpus` is a console runner that materialises a
document corpus, drives the built `broilerdoc` executable over it as a child
process, and holds what comes back against a committed baseline.

```bash
dotnet run --project src/tests/Broiler.Documents.Corpus
```

It prints `N/M passed, K failed.` as its last line and exits with the number of
failures. CI runs it on both legs and reads that line.

## Contents

- [Why it is separate from the CLI tests](#why-it-is-separate-from-the-cli-tests)
- [Where the documents come from](#where-the-documents-come-from)
- [What it checks](#what-it-checks)
- [The baseline](#the-baseline)
- [What it found](#what-it-found)
- [Adding a sample](#adding-a-sample)
- [What it cannot tell you](#what-it-cannot-tell-you)

## Why it is separate from the CLI tests

`src/tests/Broiler.Documents.Cli.Tests` already covers the command line
thoroughly, and it is deliberately better at what it does: it calls
`Program.Run` in-process, so a failing assertion breaks in the code that caused
it, and it does not depend on the build having produced an executable at a path
it has to guess.

That design has one blind spot, and it is the one a caller depends on. In-process
there is no process, so nothing can observe that the build produced a runnable
executable, that the integer the tool returns actually reaches the operating
system as an exit code, or that stdout carries the JSON and nothing else. The
`-` paths are unreachable in-process for a concrete reason: `DocumentIo` opens
the real standard input and output rather than the writers the harness passes in.

So this suite spawns the real thing. It is a console runner rather than an xunit
project for two reasons. It needs `broilerdoc` to exist before it runs, which is
a build-ordering requirement no test-discovery pass expresses. And CI floors the
`dotnet test` count to catch a build that silently stopped producing test
assemblies — folding several hundred process-level checks into that number would
make the floor say less, not more.

## Where the documents come from

Nothing is downloaded and no document file is committed. That is not
convenience. [ADR 0013](adr/0013-standards-ip-provenance-and-claims-beyond-pdf.md)
gives every format its own rights record, each of those records rejects
third-party document artifacts by default and per artifact, and
`FormatClaimGuardTests.No_Document_Of_A_Supported_Format_Is_Committed` walks the
whole tree to enforce it. Downloading a sample at test time is the same act with
the evidence deleted.

The corpus therefore arrives two ways, and the difference between them is the
interesting part.

**Generated samples** are recipes. `tests/corpus/corpus.json` states the
paragraphs and the edit operations, and the runner writes each one in all five
formats with `broilerdoc new`. Cheap, reproducible from the manifest by anyone,
and limited in a way worth saying out loud: a document written by this
component's own writers can only contain constructs those writers emit, so on
its own it shows the readers exactly the shapes they already chose.

**Authored documents** close most of that gap. They are markup the manifest
states outright — RTF control words, HTML structure, Markdown block forms — and,
for the two package formats, the XML parts of a `.docx` and an `.odt` that the
runner zips. That is how the corpus reaches a nested list, a table, a stylesheet
destination, a `HYPERLINK` field, a `text:s`, a numbered paragraph with no
numbering part, and a setext heading, none of which any writer here produces.

Neither is a document somebody outside this project wrote, and neither pretends
to be. `tests/corpus/external-sources.json` is the door such a document would
have to come through: a row per source with a licence, a permitted use, a
reviewer, a date and a pinned SHA-256, and `--fetch` refuses anything that is not
approved and anything whose bytes do not match its digest. It ships with no rows,
so the suite is hermetic and `--fetch` says so rather than passing quietly.
`CorpusControlGuardTests` fails if a row appears without that being a deliberate
change.

## What it checks

Roughly 2,200 checks over 90 documents, in a minute or two depending on how busy the machine is — it spawns a process per check and caps concurrency at the processor count.

| Group | What it asserts |
| --- | --- |
| `materialise` | Every sample writes in every format, and writes bytes. |
| `asset` | The generated PNG a sample embeds was produced. A failure here stops the run rather than degrading the samples that need it to skips. |
| `contract` | The `--json` envelope carries its own exit code; `formats` still composes five codecs and still reports `pdfComposed` false; `version` still reports its keys; an unknown command, an unknown option, a misspelt `--tolerence` and a missing argument are all usage errors; a document compared with itself reaches the positive verdict. |
| `malformed` | Each malformed input produces its documented exit code — and, checked separately on every row, not exit 70, which `docs/cli.md` defines as always a defect in the tool. |
| `document` | `probe` selects the right format; `info` reads a document back; all four `dump` projections work and the two diffable ones are byte-identical across runs; writing the same document twice produces identical bytes; the `-` stdin and stdout paths carry a real document through a real pipe. |
| `roundtrip` | Every document through every format, compared against the baseline. |
| `render` | Rendering reaches a verdict, the manifest describes what it drew, the files exist with a positive size, rendering twice on one host is byte-identical, and a document rendered against itself compares equal. |
| `emitted` | For a sample that declares `mustNotAppear`: what the writer actually put in the file. The only check here that reads a document as bytes rather than through the model. |

Two properties are worth calling out because they are cheap to break and nothing
else in the repository watches them.

**Write determinism.** Every writer produces identical bytes for identical input,
including the two ZIP-based ones. Nothing else here would notice if that stopped
being true — every comparison in this suite goes through the model, and the model
would be identical — but a consumer diffing two exports would see a change that
is not one.

**Absence of exit 70.** Checked on every malformed input rather than only on the
ones that fail, because a run that produced the right exit code for the wrong
reason is still wrong.

**What a writer emitted, for the few things a round trip cannot see.** A sample
may declare `mustNotAppear`, and the entry names the formats that write the
string anyway. Both halves are asserted: a format not on the list must not emit
it, and a format on it must — so a writer that gets fixed fails the suite until
the row is updated. That is the same bargain the baseline makes, and it exists
because a refused URI scheme that every reader also refuses is invisible from the
model side: the document reads back without it whether the writer dropped it or
wrote it out.

## What it does not assert

No pixel, page count, or image dimension. Without a font mapping the renderer
draws every family with whatever face the host has, so two machines disagree
about a document neither of them got wrong — CI's own comment says the two legs
are expected to look different, and it is right. A suite that asserted a width in
pixels would fail on the leg that was working perfectly. Render determinism is
asserted only as repeat-and-compare within one run on one host, which is the
property that makes `compare --render` mean anything.

No version string, host font path, or byte count. Those are facts about the
machine and the commit, and a baseline carrying one would fail on the next
commit.

## The baseline

`tests/corpus/corpus-baseline.json` records only the round trips that lose
something or warn about something — 63 rows out of 455. Everything absent from it
is asserted to be lossless and silent, so **a new loss fails by not being
listed**. That way round keeps the file readable and keeps the check that matters
from depending on somebody having written a row for the case that broke.

A row that stops reproducing fails too. That reads as harsh for a fix and it is
deliberate: the point of separating a documented limitation from a suspected
defect is to watch the second list shrink, and a shrink nobody records is a
shrink nobody can show.

Every row carries a state:

| State | Meaning | Count |
| --- | --- | --- |
| `documented` | A limitation one of the conformance documents states. The row cites it, and a guard checks the cited file exists. | 62 |
| `suspected-defect` | A difference nobody decided on — which the [CLI guide](cli.md) says is exactly what round-tripping is good for. | 0 |
| `accepted` | The normalized model's own behaviour, looked at and accepted. | 1 |

`documented` and `accepted` both need a reviewer and a date.
`CorpusControlGuardTests.No_Baseline_Row_Is_Classified_Merely_By_Being_Written_Down`
enforces that, the way `PdfTestControlGuardTests` does for the tool manifest — a
row is not classified by being written down.

To regenerate after a deliberate change:

```bash
dotnet run --project src/tests/Broiler.Documents.Corpus -- --update-baseline
```

It keeps the classification and prose of every row already there, and writes new
ones as `suspected-defect` with no reviewer, which is the state an unreviewed
difference should read as.

## What it found

The suite is new and these are its first results. Five of them were behaviours
nobody had decided on, and all five are invisible to a caller because no
diagnostic is emitted — which is what makes them defects rather than
limitations. This component's stated position is that a codec which knows it
dropped something says so.

### Fixed

**The ODT reader absorbed a space into the following styled run.** 21 rows, now
none. `OdtDocumentBuilder` deferred a collapsed space — which the ODF white-space
rule requires — and then flushed it with the style of the text that *followed*
it, rather than the style the space itself had. A space before a `<text:span>`
therefore ended up inside that span. The package the writer produced was correct,
with the space outside both spans in `content.xml`, so this was the reader moving
a boundary.

It is worth recording how it hid. It changed no character of any document, so
every test that asserted paragraph text passed — including the four whitespace
tests in `OdtReaderTests` that exist for exactly this part of the reader. What
noticed it was comparing a document with itself through a format, which is the
one check that looks at text and style together. The fix carries the style
alongside the deferred flag; `OdtReaderStyleTests` now asserts the boundary in
both directions, and
[odt-conformance.md](odt-conformance.md) states which style a surviving space
carries, so it is a documented property rather than an implementation detail.

That one defect accounted for half of the suspected-defect rows in the first
baseline: `emphasis`, `typography`, `colour` and `whitespace` all failed through
ODT at exactly the character before the styled word, and the `image` sample's
picture landed one character off.

**The edit language could not express a URL.** No baseline rows, because no
sample could be written to produce any: a `PROPS` value was cut at its first
colon, so `--op "inline:0:0-4:link=https://example.org/"` stored the href
`https`, and neither the documented `\:` escape nor quoting got past it. Every
writer then refused that as a relative target and reported a link diagnostic, so
the tool looked like it was behaving correctly about a value nobody had written.
`PROPS` is the last field of its verb and now takes the rest of the line, the
same rule the text-tail verbs already followed. The `links` and `links-rejected`
samples exist again because of it.

**The HTML writer let white space collapse.** 9 rows, now none, and two defects
rather than one. `HtmlWriter` asked for `white-space: pre-wrap` only when a
paragraph held a tab, so a doubled space or a space at either edge was written
out bare and read back one character shorter. Widening that condition was half
the fix: `HtmlReader` still trimmed a paragraph's leading white space even under
a preserving declaration, which is exactly what the declaration exists to
prevent. Fixing the writer alone left the leading-space case still failing, and
the test says so.

Fixing it surfaced a third, in the same mechanism: `NormalizeText` asked
`char.IsWhiteSpace`, which is a Unicode answer to a CSS question. CSS names five
characters and a non-breaking space is deliberately not among them, so a
`&nbsp;` beside an ordinary space was folded into it. That is now the HTML set.

**The Markdown writer did not escape `~~`.** 4 rows, now none. It escaped every
other delimiter it emits — ``\`` `` ` `` `*` `_` `[` `]` `(` `)` `#` `+` `-` `.`
`!` — and missed the tilde, which doubled is this reader's own strikethrough. So
literal tildes went out bare and came back as a struck run with the tildes gone.

**The ODT writer lost a space run that a style boundary fell inside.** 4 rows,
now none. It did encode repeated spaces as `text:s`, but decided run by run: two
spaces split by a style boundary looked like two single spaces, both were written
bare, and ODF folds two adjacent literal spaces back into one on read. `a  b`
with the second space styled became `a b`, with no diagnostic, while RTF, DOCX
and Markdown all kept it.

Every question the writer asks about a space is now asked of the paragraph rather
than of the run being written — whether the run began earlier, and whether it
reaches the end — which is the same correction the reader needed. The two were
the same mistake in opposite directions, and the second was only visible because
the first had been fixed and the corpus had gained a sample with a *styled*
space.

**The Markdown reader left half an image behind.** 2 rows, now documented rather
than suspect. The writer was right all along — it emits proper CommonMark,
`![description](data:…)`. The reader had no case for `!` at all, so the
`[…](…)` after it went through the link path: the marker stayed in the prose,
the description arrived as a link label, and because a `data:` destination is not
an allowed scheme the loss was reported as a dropped hyperlink, which it was not.

It now recognizes image syntax in order to drop it deliberately — the description
stands in for the picture, which is what CommonMark nominates it for, and
`markdown.image.dropped` says so. An empty description is valid and leaves
nothing behind, which is where images and links part company: a link with no text
has nothing to click, so that one keeps its label requirement.

Building the image is a separate decision and still not taken. It would have to
say what a reader may do with a `data:` payload, which is a resource-policy
question rather than a parsing one.

**The URI policy was enforced on read and not on write.** No baseline rows, and
that absence was the whole difficulty. Every reader here refuses a scheme outside
`http`, `https` and `mailto`, so a document that wrote such a link and one that
dropped it both read back without it and both round trip equal — no comparison
could tell them apart. DOCX and ODT refused on write too; HTML, RTF and Markdown
wrote the target out, `<a href="javascript:alert(1)">` and a `HYPERLINK` field
among them, with no diagnostic. It was reachable through the edit language and
through any application building a document against the library directly.

All five now apply their own reader's predicate on the way out, degrade the run
to plain text, and report it. Reader and writer share one predicate per codec
rather than a copy each, so the two cannot drift apart again — which they had:
the rule existed in seven private copies with five different answers about
`#anchor` targets alone.

This is the finding the `mustNotAppear` check was built for, and it is the only
check in the suite that reads what a writer emitted rather than what the model
round trips to.

**The DOCX writer crashed on a control character.** No baseline rows, because
the corpus loader refused to hold a sample that would produce any — which is
itself part of the finding. XML 1.0 has no representation for most control
characters, not even an escape, and the writer handed them to the serializer,
which threw; the tool reported exit 70, its own definition of a defect in
itself. It arrives from ordinary input: the HTML reader decodes `&#7;` into the
model and the RTF reader passes `\u7` through, so a document that read cleanly
could not be written.

Looking for the other text-bearing elements turned up a second one the report
did not have: a picture's description reaches an attribute, and that path was
unguarded in **both** package writers — ODT threw on it too, despite having
guarded run text all along. Every string that comes from the model now passes
the same filter in each writer, valid surrogate pairs intact.

The loader restriction is gone and a `control-characters` sample replaces it, so
the case is held by the corpus through every writer at once rather than by a
rule in the runner.

**The allow-list was five copies and they disagreed.** 6 rows, now none. Each
codec carried its own predicate. They agreed on absolute `http`, `https` and
`mailto` and on nothing else: DOCX admitted any `#anchor` including a bare `#`,
ODT required a non-empty one, HTML and Markdown admitted none, and RTF did no URI
parsing at all — it matched three case-insensitive prefixes. So an internal link
survived a DOCX or ODT round trip and vanished through the other three.

`DocumentLinkTarget` in `Broiler.Documents` is now the only copy, and all five
interchange codecs ask it on both sides. The rule went the other way from the one this document
previously implied: a non-empty fragment is **admitted** everywhere rather than
refused everywhere. Refusing would have made the majority behaviour the rule at
the cost of discarding something the source document said, and it would have
regressed two published codecs to fix a divergence.

That decision needed an argument I got wrong first. Every fragment written today
is a dangling reference — no codec reads or writes a bookmark, the model has
nowhere to put one, so the name it points at is never carried — and I read that
as a reason to drop the href. It is not: it is a reason to say "reference" rather
than "working link" in the conformance documents, which they now do. Dropping it
would lose information the document contained; keeping it loses nothing and names
the gap.

Two things came out of doing it properly. RTF could not read Word's own internal
links: the `\l` switch that spells one is written with an escaped backslash, and
the reader discarded escapes inside a field instruction, so the bookmark name
arrived alone and was reported as a hyperlink with an unsupported scheme — of
which it had none. Both halves are implemented now. And a control character in a
target crashed both package writers with exit 70, reachable from an HTML `&#7;`;
the shared predicate refuses one, so the crash is a diagnostic.

Admitting the fragment then had to be paid for once more. A name is not a URL and nothing parsed it, so a quote inside one reached the RTF field argument — which is quoted, and has no escape for a quote — and truncated the target there, reporting success. The predicate now constrains the name: no whitespace, no quote, no second `#`. That is the same class of silent loss this pass exists to remove, reintroduced by the fix for a different one, which is the argument for the corpus and against trusting a green suite that never had a case for the thing you just changed.

### Still duplicated

`FormatCodeEditIntent` keeps its own predicate. It is in
`Broiler.Documents.FormatCodes`, which references only the model and cannot see
`Broiler.Documents` without changing the package graph, and its rule genuinely
differs — it caps length, rejects control characters, and admits the empty string
because that is how the edit language spells "remove this link". It was never one
of the five.

`PdfUriPolicy` keeps its own too, and was never one of the five either. It is
configurable where the shared rule is fixed, and it decides more: an absolute URI
is required, `http` and `mailto` need a caller opt-in, there is a length cap, user
information is refused, and an admitted target is stored canonicalized rather than
as written. The refusal a reader will actually meet is a target that is only a
fragment, and it costs nothing a fragment could have delivered — the PDF writer
emits a URI action and no destination, so a `#chapter` that survives a DOCX round
trip lands nowhere in a PDF either. It is stated in
[the PDF feature matrix](pdf-feature-matrix.md) so a reader of the PDF documents
finds it without reading this one.

### Not a defect after all

**"The RTF writer replaces an inline image with a space and says nothing"** was
wrong on every count, and it is worth recording why rather than quietly deleting
it. The RTF writer emits the picture correctly, as a `\pict` destination carrying
the PNG. The RTF *reader* skips it — `rtf-conformance.md` lists `\pict` among the
skipped destinations — and it does report that, as `rtf.embedded`, which
escalates to the shared capability code when a caller actually asks for embedded
decoding. `RtfWriterTests` and `RtfEmbeddedObjectReportTests` cover both halves.

It looked silent because this suite was filtering every info diagnostic out of
the baseline. That filter was written to keep the per-format read summaries from
filling the file, which was reasonable, but it also hid the codec doing precisely
what this component asks of it. The filter now excludes the read summaries and
nothing else, and the row is classified as documented.

## Adding a sample

Add a row to `tests/corpus/corpus.json` — `samples` for a recipe, `authored` for
markup you write yourself — then regenerate the baseline and classify whatever
rows appear.

One constraint the schema and the loader enforce rather than leave to memory:

- **Quote a path** substituted into an `image` operation. The `PROPS` field is
  split on commas outside quotes, and the workspace is a temporary directory this
  project does not choose the name of.

## What it cannot tell you

**Nothing about foreign documents.** Every document in the corpus was written
either by this component or by somebody adding a row to its manifest. The suite
is evidence about round-trip fidelity and about the command contract. It is not
evidence that a file produced by Word, LibreOffice or a browser reads correctly,
and no amount of adding samples changes that — it would take a document from
outside, which needs a row in the external register and a decision behind it.

**Nothing about how a document looks.** The layout engine belongs to the CLI, not
to the component, and the render checks assert that it ran and was reproducible,
not that it was right.

**Nothing about PDF.** `Broiler.Documents.Pdf` is not composed by the CLI, by
design. The only PDF assertion here is that it stays that way.
