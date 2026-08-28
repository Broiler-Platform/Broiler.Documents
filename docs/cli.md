# `broilerdoc` — the Broiler.Documents command line

`src/Broiler.Documents.Cli` is an application head over the codecs in this
repository. It creates, edits, converts, renders, and compares documents, and it
is built to be driven by an automated test system as much as by a person: every
command has a `--json` form, and the exit code is a documented contract.

```bash
dotnet run --project src/Broiler.Documents.Cli -- --help
```

The assembly is named `broilerdoc`, so a `dotnet build` leaves a directly
runnable `broilerdoc` (or `broilerdoc.exe`) in the project's output directory.
The examples below use that name.

## Contents

- [Why it exists](#why-it-exists)
- [Commands](#commands)
- [Exit codes](#exit-codes)
- [Finding codec gaps](#finding-codec-gaps)
- [Rendering, and what makes a render reproducible](#rendering-and-what-makes-a-render-reproducible)
- [The edit language](#the-edit-language)
- [What this tool is not](#what-this-tool-is-not)
- [Shipping it as a tool](#shipping-it-as-a-tool)

## Why it exists

Two jobs, and the second is the one that shaped the design.

The first is ordinary: give someone a way to use these codecs without writing a
program. Read a `.docx`, write a `.rtf`, look at what a document actually
contains, change a word, draw a page.

The second is to find gaps in Broiler.Documents. A codec that meets a construct
it does not implement returns a usable document *and a diagnostic saying what it
dropped*, which means most gaps are already named in the result — you do not have
to infer them from a picture. Where a gap is not named, comparing two exports
will find it. This tool does both, and it keeps the two kinds of finding apart,
because "the RTF writer does not emit list numbering" and "these two pages differ
by 2,836 pixels" are very different sentences to hand a developer.

## Commands

| Command | What it does |
| --- | --- |
| `formats` | List the composed codecs and what each can do. |
| `probe` | Identify a file's format, reporting every codec's verdict. |
| `info` | Read a document and report its structure and diagnostics. |
| `dump` | Print the content as text, JSON, Formatting Codes, or an outline. |
| `new` | Create a document from text and write it in any format. |
| `edit` | Apply edit operations to a document. |
| `convert` | Read one format, write another. |
| `render` | Rasterize to PNG, JPEG, or BMP pages. |
| `compare` | Compare two documents or two images and say how they differ. |
| `roundtrip` | Write, read back, and report what did not survive. |
| `version` | Tool, component, and rendering-environment versions. |

`broilerdoc <command> --help` prints that command's own options and examples.

Every command accepts `--json`, `--quiet`, `--verbose`, and `--help`. In `--json`
mode the whole result — including the exit code — is one object on stdout, so a
caller never has to parse the human-readable form.

`-` as an input or output path means standard input or standard output:

```bash
cat report.docx | broilerdoc convert - --out - --from docx --to markdown --quiet
```

## Exit codes

The split that matters is between `5` and everything above it. A comparison that
finds a difference is a *successful* run that reached a negative verdict; a run
that could not read its input never reached a verdict at all. A harness that
collapses the two reports "the export changed" when what actually happened is
"the file was missing".

| Code | Meaning |
| --- | --- |
| 0 | Success, and any verdict reached was positive. |
| 1 | Usage error: unknown command, unknown option, missing argument. |
| 2 | An input could not be read, or an output could not be written. |
| 3 | A document was rejected by its codec. |
| 4 | A document could not be written or rendered. |
| 5 | A comparison found a difference beyond its tolerance. |
| 6 | Diagnostics reached the `--fail-on` severity. |
| 70 | An unexpected error. Always a defect in this tool. |

Unknown options are a usage error rather than being ignored. A harness that
writes `--tolerence` must fail loudly; silently comparing at the default
tolerance would report a pass nobody earned.

## Finding codec gaps

### Start with `roundtrip`

The shortest path from a corpus to a list of gaps, and it needs no reference
implementation: the document that went in *is* the reference.

```bash
broilerdoc roundtrip report.docx --via docx --via odt --via rtf --via html --via markdown
```

```text
via RTF
  write diagnostics: no diagnostics.
  read diagnostics: no diagnostics.
  431 bytes, plain text same, format codes DIFFERENT, 3 structural difference(s)
    paragraph 2: list Numbered vs None
    paragraph 3: list Numbered vs None
    paragraph 4: list Numbered vs None
```

That is a sentence you can turn into a test. Exit code 5.

A difference is not automatically a defect. `RichTextDocument` is a normalized
model, and [the roadmap](roadmap.md) is explicit that source-preserving round
trips are not a goal — Markdown genuinely has no way to express a highlight
colour, and the conformance documents say so. What this command is good for is
the difference nobody decided on. The diagnostics printed alongside usually tell
you which kind you are looking at: a codec that *knows* it dropped something says
so.

### Then `compare` in document mode

When you have two documents rather than one:

```bash
broilerdoc compare reference.docx produced.docx
```

Paragraphs are aligned by longest common subsequence before anything is compared,
so one inserted paragraph reports as one extra paragraph rather than as every
following paragraph differing.

### Reach for pixels when you need to know how it *looks*

```bash
broilerdoc compare a.docx b.docx --render --continuous --diff diff.png --tolerance 2
```

Both sides go through one render pipeline with one set of options, so every pixel
that differs came from the documents and not from the render. `--continuous`
renders each document as one tall page instead of paginating: with pagination on,
one extra line before a page break shifts every later page and a one-line
difference reads as a whole-document difference.

To compare against an export produced by something else entirely, render one side
and compare the images:

```bash
broilerdoc render report.docx --out ours.png --continuous
broilerdoc compare ours.png theirs.png --diff diff.png --diff-style heat --tolerance 4
```

`--diff-style` picks how the difference image is drawn: `overlay` (the reference
page as a dim ghost with differing pixels marked, so you can see *where* on the
page it is), `mask` (black and white, for feeding into another tool), or `heat`
(coloured by how far apart the pixels are).

### Structural output that diffs cleanly

`dump --as json` emits every paragraph, run, and resolved style in a fixed order,
so two dumps of equal documents are byte-identical and `diff` on them is
meaningful. `dump --as codes` emits the Formatting Codes projection — this
component's own canonical, versioned rendering of a document's semantics
([ADR 0006](adr/0006-formatting-codes-projection-and-grammar.md)).

### Turning diagnostics into a build failure

```bash
broilerdoc convert page.html --out page.docx --fail-on warning
```

Exit code 6 when any diagnostic reaches that severity. `--fail-on` accepts
`never` (the default), `info`, `warning`, or `error`.

## Rendering, and what makes a render reproducible

Two things have to be pinned before two renders are comparable, and both are
reported in the manifest (`--manifest out.json`, or the `render` object in
`--json` output) so that a difference which turns out to be one of them is
identifiable as such.

**The page box.** The document model carries no page geometry at all — it is an
ordered list of paragraphs, and no reader brings section properties across. So
the page is entirely the caller's choice: `--page-size`, `--landscape`,
`--margin`, `--dpi`. Both sides of a comparison must be given the same one.

**The fonts.** Without a font mapping, Broiler.Graphics draws *every* family with
one host face, so two machines with different font sets disagree about a document
neither of them got wrong. Pin them:

```bash
broilerdoc render report.docx --out report.png \
  --font-dir ./test-fonts \
  --font-file "sans-serif=./test-fonts/DejaVuSans.ttf" \
  --font-file "sans-serif:bold=./test-fonts/DejaVuSans-Bold.ttf" \
  --font-file "sans-serif:italic=./test-fonts/DejaVuSans-Oblique.ttf"
```

`--font-dir` scans a directory and maps faces by filename; `--font-file` pins one
explicitly and always wins. Families a document asks for that have no mapping are
listed in the manifest under `fonts.unmappedFamilies`.

Italic deserves a note. Broiler.Graphics does not synthesize a slant, so an
italic run with no italic face mapped would draw upright — and a codec that
silently dropped italic would then produce a pixel-identical page and the
comparison would pass. This tool therefore shears such runs itself. The shear
does not change any advance, so nothing in the layout moves. `--no-synthetic-italic`
turns it off.

## The edit language

`new` and `edit` take repeatable `--op` arguments, or a `--script` file with one
operation per line (`#` starts a comment). Operations apply in order.

```bash
broilerdoc new --out styled.docx --text "Title\nBody text" \
  --op "inline:0:*:bold=on,size=18" \
  --op "para:0:align=center" \
  --op "inline:1:0-4:italic=on,color=#B71C1C"
```

| Operation | Effect |
| --- | --- |
| `append:TEXT` | Add a paragraph at the end. |
| `insert:P:TEXT` | Insert a paragraph before paragraph `P`. |
| `text:P:TEXT` | Replace a paragraph's text, keeping its paragraph style. |
| `delete:PARAGRAPHS` | Delete paragraphs. |
| `merge:P` | Join paragraph `P` with the one after it. |
| `split:P:OFFSET` | Split a paragraph at a character offset. |
| `replace:SEARCH:REPLACEMENT` | Replace literal text everywhere, keeping the style at each hit. |
| `inline:PARAGRAPHS:CHARS:PROPS` | Apply inline formatting to a character range. |
| `clear:PARAGRAPHS:CHARS` | Reset inline formatting on a character range. |
| `para:PARAGRAPHS:PROPS` | Apply paragraph formatting. |
| `image:P:OFFSET:PROPS` | Insert an image file at a character offset. |

`PARAGRAPHS` is `3`, `2-5`, `2-$`, `*` (all), or `$` (last). `CHARS` is `0-5`,
`3-$`, or `*`. Offsets are UTF-16 indices into the paragraph text — the same ones
`dump --as json` reports.

`PROPS` is comma-separated `key=value`; quote a value containing a comma.

- **inline**: `bold`, `italic`, `underline`, `strike` (`on`/`off`); `caps`
  (`none`/`all`/`small`); `color`, `highlight` (`#RRGGBB`, `#RRGGBBAA`, a CSS
  colour name, or `default`); `font` (family or `default`); `size` (points or
  `default`); `link` (URL or `off`).
- **para**: `align` (`left`/`center`/`right`); `list`
  (`none`/`bullet`/`numbered`); `indent` (level); `linespacing` (multiplier);
  `before`, `after` (points).
- **image**: `file` (path, required); `width` and `height` (points, given
  together or not at all — the model reads a zero in either as "no stated size");
  `alt`; `name`.

```bash
broilerdoc new --out illustrated.docx --text "Text around  the image here."   --op "image:0:12:file=./logo.png,width=36,height=36,alt=the company logo"
```

The media type comes from the file's own bytes, falling back to its extension:
the extension is a claim and the bytes are the fact, and a PNG named `.jpg` would
otherwise be written into a DOCX part declaring a content type nothing can
decode.

Verbs whose last field is free text take the whole remainder of the line, colons
included, so `append:meeting at 12:30` appends what it looks like.

The escapes are `\n`, `\t`, `\r`, `\:`, and `\\`. A backslash before anything else
keeps both characters, so an ordinary backslash in prose survives. The `image`
props field is exempt from escaping entirely and is taken exactly as written —
it carries file paths, and on Windows a directory called `temp` or `new` would
otherwise turn into a tab or a newline.

An out-of-range paragraph or offset is an error, not a clamp: a script that
silently styled paragraph 9 because paragraph 12 did not exist would report a
success for work it did not do.

## What this tool is not

**It does not do PDF.** `Broiler.Documents.Pdf` builds and tests in this
solution but is `IsPackable=false` and belongs in no application catalog until
the read-preview and write-preview gates in
[the PDF support roadmap](pdf-support-roadmap.md) §4.1 pass. Composing it in a
CLI would ship the capability those gates exist to hold back, from the one
surface an automated system would then depend on.

**The layout engine is this tool's, not the component's.** It does word wrapping,
alignment, indents, list markers, line and paragraph spacing, inline images, and
pagination. It has no tables, columns, floats, footnotes, headers, footers,
hyphenation, kerning pairs, or bidirectional reordering — mostly because the
document model cannot express them, so there is nothing to lay out. The shared
paginator the PDF roadmap tracks as `Broiler.Documents.Pagination` is where a
component-level version belongs; until it exists, the numbers here are this
tool's own. That is fine for the job: a comparison between two exports run
through the *same* layout isolates the codecs.

**List numbering is reconstructed, not preserved.** The model records that a
paragraph is a numbered item at an indent level and nothing else — no list
identity, no start number, no restart flag. The rule here is the simple one those
facts support: a counter per level, deeper levels reset when a shallower item
appears, every counter resets at the first non-list paragraph.

## Shipping it as a tool

The project is set up to pack as a .NET tool (`PackAsTool`, command name
`broilerdoc`) but has `IsPackable=false`, so `dotnet pack` on the solution still
produces exactly the eight library packages the [README](../README.md) lists.
That is deliberate: adding a ninth package would change what a `v*` tag pushes
to nuget.org, and that should be a decision someone makes rather than a side
effect of adding a CLI.

To ship it, set `IsPackable` to `true` in
`src/Broiler.Documents.Cli/Broiler.Documents.Cli.csproj`, add the package to the
README's table, and it installs the usual way:

```bash
dotnet tool install --global Broiler.Documents.Cli --prerelease
```
