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
- [The suite that drives this tool](#the-suite-that-drives-this-tool)
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
| `tables` | Inspect tabular and spreadsheet structures in a document. |
| `extract` | Extract tables into structured data (CSV, TSV, JSON, HTML, Markdown). |
| `dump` | Print the content as text, JSON, Formatting Codes, outline, or grid/table. |
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

`--from` matters most for CSV. CSV has no signature, so the codec recognizes a
file by its `.csv` name and never by content alone; read from standard input, a
CSV needs `--from csv`. A CSV file reads as one table, and writing CSV writes
every table as its grid and every other paragraph as a one-field record. See
[the CSV note](../notes/csv.md).

CSV options apply only when a CSV codec reads or writes; they do not force format
selection or change other codecs:

| Option | Meaning |
| --- | --- |
| `--csv-read-delimiter <separator>` | Override input detection. |
| `--csv-header` | Mark the first CSV input record as a header row. |
| `--csv-write-delimiter <separator>` | Output separator; defaults to comma. |
| `--csv-bom` | Write a UTF-8 byte-order mark; off by default. |
| `--csv-escape-formulas` | Prefix formula-like output fields with an apostrophe; off by default because it changes values. |
| `--dialect <preset>` | CSV dialect preset: `rfc4180`, `excel`, `unix`, `tsv`, `semicolon`, or `pipe`. |
| `--delimiter <separator>` | Override field delimiter for CSV output. |
| `--sheet <name|index>` | Select a specific sheet/table by name or 0-based index. |
| `--table <index>` | Select a specific table by 0-based index. |

A separator is one character other than a quote, CR or LF, or the name `comma`,
`semicolon`, `tab`, or `pipe`. Quote shell-sensitive values such as `';'` or `'|'`.
Input and output separators are independent; the detected input dialect is not
preserved automatically.

Dialect presets configure delimiters, UTF-8 BOM, and formula escaping in one step:
- `rfc4180` / `default`: comma delimiter, no BOM, no formula escaping.
- `excel`: comma delimiter, UTF-8 BOM, formula escaping enabled.
- `unix`: comma delimiter, no BOM, no formula escaping.
- `tsv`: tab delimiter (`\t`), no BOM, no formula escaping.
- `semicolon`: semicolon delimiter (`;`), common in European locales.
- `pipe`: pipe delimiter (`|`).

Explicit options like `--csv-write-delimiter` and `--csv-bom` override preset defaults.
`--to tsv`, `--via tsv`, `--from tsv`, and files ending in `.tsv` map to the CSV codec
with a tab delimiter.

`roundtrip --via csv` or `--via tsv` rereads with the exact output dialect and delimiter
and the `--csv-header` setting, preventing false diffs across intermediate hops. When
converting or roundtripping multi-table documents, `--sheet` and `--table` isolate a
single table so each sheet can be converted or tested in isolation.

```bash
broilerdoc convert input.csv --out output.csv --csv-read-delimiter semicolon --csv-write-delimiter comma --csv-bom
broilerdoc convert report.docx --out sheet0.csv --sheet 0 --dialect excel
broilerdoc convert report.docx --out sheet1.tsv --sheet "Sheet 2"
broilerdoc roundtrip report.docx --table 0 --via csv --dialect tsv --ignore-table-layout
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
model, and [the roadmap](../roadmap.md) is explicit that source-preserving round
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
([ADR 0006](../adr/0006-formatting-codes-projection-and-grammar.md)).

`dump --as text` is the characters, with structural separators stated the way LibreOffice's
own text export states them, so the two can be compared. **A paragraph in a list
its document defined projects with its label first**: `4. alpha`, the label, one
space, the text. A bullet is written as LibreOffice writes one — its default
bullet for the item's level, `•`, `◦`, `▪` and round again — not the glyph the
document draws, so a list bulleted with `o`, `◆` or a Symbol-font character still
reads `• alpha`. A list known only by its kind has no label anybody stated and
gets none; `--as outline` is where the list kind and depth are shown, and
`--as json` where they are machine readable. **A note's citation is its label**,
and the note's own text is not part of the body's. **Text a tracked change
deleted is written** with the rest, as LibreOffice's export writes it whether or
not the document shows its changes; `--as json` says which change marks each run.
**Table cells are separated by tabs and rows by newlines**. Paragraphs inside a
cell keep their newline boundaries; nested tables preserve their own separators.
Image objects contribute no text, as in LibreOffice's export; the pixel checks
remain responsible for detecting missing images. **Text in a shape is not the
body's either**: a text box, a frame or a form label contributes nothing, as
LibreOffice's export walks the body alone. The one exception is LibreOffice's
too — a document whose whole body is one empty paragraph exports its first text
box instead. A page field is written with the value the document saved.

`dump --as grid` (or `--as table`) emits a tabular ASCII/Unicode box-drawing grid representation of all tables and spreadsheet sheets in the document with row and column headers.

### Inspecting tables and spreadsheet sheets with `tables`

`broilerdoc tables <input>` inspects tabular and spreadsheet structures across documents (CSV, TSV, XLSX, ODS, DOCX, ODT, HTML, Markdown, PDF):

```bash
broilerdoc tables sheet.csv
broilerdoc tables report.docx --cells
broilerdoc tables budget.xlsx --formulas --evaluate
broilerdoc tables document.odt --table 0 --json
```

- **Dimensions & bounds**: row and column counts, total width, column widths, bounds (`A1:D10`).
- **Slots & merges**: root cells, continuation slots, empty slots, merged regions (`A1:B2 (span 2x2)`).
- **Headers & nesting**: header row presence and nested table counts.
- **Layout & styling**: alignment, padding, auto-fit, preferred/percent width, floating table positions.
- **Data type classification**: inferred logical types (Number, Text, Boolean, Date, Time, Currency, Percentage, Formula, Empty, Error).
- **Formulas & DAG**: formula expressions, cell dependency DAG, circular reference detection, and evaluated/formatted values with `--evaluate`.
- **Per-cell grid details**: coordinate, spans, text, format, and value with `--cells`.

### Extracting tables with `extract`

`broilerdoc extract <input>` (aliases: `extract-tables`, `extract-table`) extracts tables from any supported document format into structured data:

```bash
broilerdoc extract report.docx --to csv
broilerdoc extract sheet.csv --table 0 --range B2:D10 --to json --headers
broilerdoc extract budget.xlsx --evaluate --formatted --to markdown
broilerdoc extract data.docx --all --out "tables/table_{index}.csv"
broilerdoc extract report.pdf --to html --out tables.html
```

- **Output formats (`--to` / `--as`)**: `csv`, `tsv`, `json`, `html`, `markdown`, `text` (grid). Automatically inferred from `--out` file extension if not specified.
- **Selection (`--table`, `--all`)**: extract a single table by 0-based index or all tables.
- **Sub-setting (`--range`)**: extract a rectangular subgrid, column slice, or row slice (e.g. `A1:D10`, `B:D`, `2:5`).
- **Formulas & formatting**: `--evaluate` recalculates formulas into concrete values; `--formatted` applies cell number formats.
- **Headers & records (`--headers`)**: treats the first row as column headers, emitting JSON records (`[{"Item": "Apple", ...}]`).
- **Merged cell handling (`--fill-merged`)**: repeats root cell values across covered continuation slots.
- **Customization (`--delimiter`, `--empty`)**: configure CSV delimiter and empty cell representation.

**A table projects as one line per cell**, in row order, because that is how the
model holds a table once it is flattened; a projection that joined a row onto one
line would be inventing a column separator the document never stated. That is not
a loss and not the only defensible answer — newer LibreOffice text exports join a
row onto one line — so a harness comparing the two projections has to expect it
and say so rather than record it as a finding.

### Turning diagnostics into a build failure

```bash
broilerdoc convert page.html --out page.docx --fail-on warning
```

Exit code 6 when any diagnostic reaches that severity. `--fail-on` accepts
`never` (the default), `info`, `warning`, or `error`.

### Reading an encrypted PDF

```bash
broilerdoc dump statement.pdf --password-file ~/.secrets/statement.txt
```

Every command that reads a document takes `--password-file`: the password is
the first line of that file, tried as the document's owner password and then
its user password. It is a file rather than an argument so that it reaches no
process list and no shell history, and it never appears in the output,
`--json` included.

Without it, a PDF protected only by its permissions still opens, because its
user password is empty. One that needs a password exits 3 with
`pdf.encryption.password-required`; a wrong one, with
`pdf.encryption.password-incorrect`. A document whose permissions withhold
copying and extracting is refused with `pdf.encryption.extraction-not-permitted`
unless the password given is its owner's - everything this tool does with a
PDF extracts it. What opened the document is reported under
`pdf.encryption.decrypted`, and whatever is written from it is not encrypted.

A PDF encrypted for certificate recipients is refused with
`pdf.encryption.recipient-not-composed`: this tool composes no recipient
decryptor (see [PDF extension points §4.6](../notes/pdf/extension-points.md)).

## Rendering, and what makes a render reproducible

Two things have to be pinned before two renders are comparable, and both are
reported in the manifest (`--manifest out.json`, or the `render` object in
`--json` output) so that a difference which turns out to be one of them is
identifiable as such.

**The page box.** Two things can decide it. A document may state the page it was
written for — DOCX, ODT, RTF and PDF all do, and the reader brings size, margins and
header and footer distances across — and a render that was given no page of its
own adopts it rather than inventing one. Ask for a page and the caller's wins
outright: `--page-size`, `--landscape`, `--margin`. `--dpi` is always the
caller's, since resolution is a property of the render and not of the document.
So a comparison is pinned either by giving both sides the same flags or by both
reading a document that states the same page — and because the two paths look
identical in the output, the geometry is echoed into the manifest whichever one
produced it.

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
listed in the manifest under `fonts.unmappedFamilies`. Such a family is drawn with
the host fallback face (`fonts.hostFallback`), and its lines are measured from that
face too, so a document set in a family the mapping leaves out breaks its lines
and pages where the face it is drawn in puts them.

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

`PROPS` is comma-separated `key=value`; quote a value containing a comma. It
is the last field of its verb, so it takes the rest of the line, colons and
all — which is what lets `link=https://example.org/` be written the way
anybody would write it. The `\:` escape still works and is never needed here.

- **inline**: `bold`, `italic`, `underline`, `strike` (`on`/`off`); `caps`
  (`none`/`all`/`small`); `color`, `highlight` (`#RRGGBB`, `#RRGGBBAA`, a CSS
  colour name, or `default`); `font` (family or `default`); `size` (points or
  `default`); `link` (URL or `off`). A link target is stored as written and
  policed by each codec on the way out, so a scheme one format refuses may still
  be what another writes — the diagnostics say which.
- **para**: `align` (`left`/`center`/`right`); `list`
  (`none`/`bullet`/`numbered`); `indent` (level); `linespacing` (multiplier);
  `lineheight` (points, exact) and `minlineheight` (points, the least a line
  is), each replacing the multiplier, which in turn replaces them;
  `before`, `after` (points); `pagebreak` (`on`/`off`), which starts the
  paragraph on a new page. It is stated *before* the paragraph, because a break
  after paragraph five and a break before paragraph six are the same break and
  admitting both spellings would let a document say it twice and mean once. A
  break on the first paragraph draws no empty page in front of it, and
  `--continuous` ignores every break and says so in the render notes — there are
  no pages to break between.
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

**It reads PDF and writes none.** `Broiler.Documents.Pdf` is composed
read-only: `probe`, `info`, `dump`, `render`, `compare`, and `convert` from a PDF
all read one, and any command asked to produce a PDF - `convert --out x.pdf`,
`roundtrip --via pdf` - refuses with a usage error before anything is written.
The [PDF delivery gates](../roadmap.md#pdf-delivery-gates) let an application
read PDF before they let one write it, and writing still has to pass the
write-preview gate; a CLI is the one surface an automated system would come to depend on, so
it holds to that.

Of the codec's optional providers, only the ICC colour-profile reader
(`IccColorProfileReader`, IP-024) is composed. A picture in an `ICCBased` colour
space is converted to sRGB, so `render` draws it and `convert` carries it into the
destination format. Without the reader, both would come out without that picture.
The image decoders — JPEG, JPEG 2000, JBIG2 and fax — and the font-program reader
are not composed. A picture or font they would have decoded is reported as skipped
rather than read.

A PDF's pictures reach the model as decoded samples, but every writer needs
encoded bytes. The resource gate won't create them on a writer's behalf. So before
`convert`, `edit` or `new` writes anything, this tool encodes such pictures as PNG.
PNG is lossless, so the samples written are the samples read, including
transparency. The step runs only where the read's resource policy permits
transforming the picture; this tool reads with `AllowOwnDocuments`, which does.
The encoding is admitted into the same conversion under its own id. The JSON
result's `picturesEncoded` counts the pictures encoded. `roundtrip` doesn't take
this step, because it measures the codecs rather than this tool.

**The layout engine is this tool's, not the component's.** It does word wrapping,
alignment, indents, list markers, line and paragraph spacing, inline images,
tables (a row breaks across a page cell by cell unless it says it may not),
text columns filled one after another, sections in their own columns and
margins with a section's columns balanced where the next follows on the page,
running headers and footers - each at its own section's distance, with the text
moved clear of one that runs past its margin - anchored shapes in their stated
stacking order,
footnotes at the foot of the page that cites them and endnotes after the body,
and pagination. It has no floats, hyphenation, kerning pairs, or
bidirectional reordering — mostly
because the document model cannot express them, so there is nothing to lay
out. The shared paginator planned as
`Broiler.Documents.Pagination` ([roadmap](../roadmap.md#shared-export-foundation-phase-6))
is where a component-level version belongs; until it exists, the numbers here
are this tool's own. That is fine for the job: a comparison between two exports run
through the *same* layout isolates the codecs.

**List numbering is reconstructed, not preserved.** The model records that a
paragraph is a numbered item at an indent level and nothing else — no list
identity, no start number, no restart flag. The rule here is the simple one those
facts support: a counter per level, deeper levels reset when a shallower item
appears, every counter resets at the first non-list paragraph.

## The suite that drives this tool

Everything above is written for an automated caller, and two of them live in
this repository. `src/tests/Broiler.Documents.Corpus` materialises a document
corpus, runs this tool over it as a child process, and compares the result with a
committed baseline; CI runs it on Linux and Windows on every push.

```bash
dotnet run --project src/tests/Broiler.Documents.Corpus
```

It is the reason several of the promises on this page are promises rather than
descriptions. The exit codes are asserted against a real process rather than a
return value. The `-` paths are exercised through a real pipe, which is the only
way to reach them - they open the process's own standard streams. And the claim
that two dumps of equal documents are byte-identical is checked on every document
in the corpus rather than assumed.

What it finds and what it deliberately does not assert - no pixel, no page count,
no version string - is in [the corpus suite](corpus-suite.md).

The second is `src/tests/Broiler.Documents.Office`, which drives the same command
line over documents LibreOffice wrote rather than documents this component wrote,
and which is where `render`, `compare` and the font-pinning options are actually
held to the promises made above about reproducibility. It is nightly rather than
per-push, because it depends on a third-party binary. See
[the office conformance suite](office-conformance.md).

## Shipping it as a tool

The project is set up to pack as a .NET tool (`PackAsTool`, command name
`broilerdoc`) but has `IsPackable=false`, so `dotnet pack` on the solution still
produces exactly the twelve library packages the [README](../../README.md) lists.
That is deliberate: adding a thirteenth package would change what a `v*` tag pushes
to nuget.org, and that should be a decision someone makes rather than a side
effect of adding a CLI.

To ship it, set `IsPackable` to `true` in
`src/Broiler.Documents.Cli/Broiler.Documents.Cli.csproj`, add the package to the
README's table, and it installs the usual way:

```bash
dotnet tool install --global Broiler.Documents.Cli --prerelease
```
