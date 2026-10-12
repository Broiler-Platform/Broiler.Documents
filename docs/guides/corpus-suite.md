# The corpus suite

`src/tests/Broiler.Documents.Corpus` is a console runner that materialises a
document corpus, drives the built `broilerdoc` executable over it as a child
process, and holds what comes back against a committed baseline.

```bash
dotnet run --project src/tests/Broiler.Documents.Corpus
```

It prints `N/M passed, K failed.` as its last line and exits with the number of
failures. CI runs it on Linux and Windows and reads that line.

## Contents

- [Why it is separate from the CLI tests](#why-it-is-separate-from-the-cli-tests)
- [Where the documents come from](#where-the-documents-come-from)
- [What it checks](#what-it-checks)
- [The baseline](#the-baseline)
- [What it has found](#what-it-has-found)
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
convenience. [ADR 0013](../adr/0013-standards-ip-provenance-and-claims-beyond-pdf.md)
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

Roughly 2,300 checks, in a minute or two depending on how busy the machine is — it spawns a process per check and caps concurrency at the processor count.

| Group | What it asserts |
| --- | --- |
| `materialise` | Every sample writes in every format, and writes bytes. |
| `asset` | The generated PNG a sample embeds was produced. A failure here stops the run rather than degrading the samples that need it to skips. |
| `contract` | The `--json` envelope carries its own exit code; `formats` still composes the five codecs the samples are written in, and still reports PDF as read and never written; `version` still reports its keys; an unknown command, an unknown option, a misspelt `--tolerence` and a missing argument are all usage errors; a document compared with itself reaches the positive verdict. |
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
something or warn about something — 67 rows. Everything absent from it
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
| `documented` | A limitation one of the codec notes states. The row cites it, and a guard checks the cited file exists. | 66 |
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

## What it has found

Each finding below was invisible to a caller, because no diagnostic was emitted.
That is what makes something a defect rather than a limitation here: a codec
that knows it dropped something says so. All of them are fixed or classified.

| Finding | Outcome |
| --- | --- |
| The ODT reader moved a collapsed space into the following styled run | Fixed. [notes/odt.md](../notes/odt.md) states which style a surviving space carries, and `OdtReaderStyleTests` asserts the boundary both ways |
| The ODT writer lost a run of spaces that a style boundary fell inside | Fixed. Every question about a space is asked of the paragraph, not of the run being written |
| The HTML writer let white space collapse; the reader trimmed it under a preserving declaration and folded `&nbsp;` into ordinary spaces | Fixed. [notes/html.md](../notes/html.md) states the rule |
| The Markdown writer did not escape `~` | Fixed |
| The Markdown reader left half an image behind and reported a dropped link | Fixed. Images are recognized and replaced by their description, reported as `markdown.image.dropped` |
| The edit language cut a `PROPS` value at its first colon, so no URL could be written | Fixed. `PROPS` takes the rest of the line ([the edit language](cli.md#the-edit-language)) |
| HTML, RTF and Markdown enforced the link rule on read only, and five private copies of the rule disagreed | Fixed. `DocumentLinkTarget` is the one predicate, used on both sides ([architecture notes](../notes/architecture.md#limits-and-link-policy)). The `emitted` check exists for this case |
| A control character crashed both package writers with exit 70 | Fixed. Reported as `docx.text.control` and `odt.text.control`, held by the `control-characters` sample |
| "The RTF writer drops an inline image silently" | Not a defect. The reader skips `\pict` and reports `rtf.embedded`; the suite had been filtering info diagnostics out of its baseline |

Two link predicates besides `DocumentLinkTarget` remain on purpose,
`PdfUriPolicy` and `FormatCodeEditIntent`; the
[architecture notes](../notes/architecture.md#limits-and-link-policy) say why.

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

[The office conformance suite](office-conformance.md) closes this by one step
without taking a document from outside: LibreOffice rewrites a seed authored here
into markup no writer here produces, and that is what `broilerdoc` is asked to
read. It is a step and not the whole distance — the constructs are still the ones
a seed in this repository asked for.

**Nothing about how a document looks.** The layout engine belongs to the CLI, not
to the component, and the render checks assert that it ran and was reproducible,
not that it was right.

**Nothing about PDF.** The CLI composes `Broiler.Documents.Pdf` to read only,
and the samples are materialised by the five codecs that write. The only PDF
assertion here is that the tool still cannot write one.
