# PDF Extension Points

- **Status:** Active
- **Component:** `Broiler.Documents.Pdf`
- **Updated:** 2026-09-01 (every filter and codec register row now decided)
- **Companion documents:** [PDF support roadmap](pdf-support-roadmap.md),
  [construct inventory](pdf-construct-inventory.md),
  [feature matrix](pdf-feature-matrix.md),
  [IP/licensing register](pdf-ip-licensing-register.md),
  [approved-source record](pdf-approved-sources.md)

The base PDF codec implements only what this repository writes itself and can
therefore ship without depending on, or clearing, an outside component. Every
remaining PDF technology is *recognized* by the base build and *implemented* by a
separately reviewed component that a caller composes in.

This document is the contract between those two halves: what the base build
does, where each further technology plugs in, and what has to be true before one
is switched on.

## 1. Why the split exists

Three different concerns happen to land on the same seam.

**Legal.** JPEG (DCT), JPEG 2000, JBIG2, LZW, and CCITT fax each had their own
standards, patent, and licensing position, tracked as separate rows in the
IP/licensing register. Approval of one never implies approval of another. Keeping
each behind a composition boundary means a row can clear on its own schedule
without a parser change.

**Security.** An image codec and a font-program parser are the two largest
attack surfaces in a PDF reader, and neither is needed to extract text. A build
that composes neither has a materially smaller surface, and it is the default.

**Honesty.** A reader that cannot decode something should say so with a specific
code, not fail generically or silently produce nothing. Because the base build
knows every filter *exists* — see `PdfFilterNames` — it can report
`pdf.filter.jbig2.unsupported` rather than "unknown filter", and a host can tell
a policy decision apart from a corrupt file.

A fourth thing follows from the first, and IP-010 is the worked example: when a
row clears, the boundary is re-asked rather than assumed. LZW cleared and moved
*into* the base build, because none of the three reasons survived — there was no
outside component, and a bounded byte-stream decompressor is not an image codec.
JPEG and the font reader cleared and stayed composed, because both of those
reasons still hold for them. Clearing a row answers the legal question and only
the legal question.

## 2. What the base build carries

| Area | Implemented in the base build |
|---|---|
| Syntax | Tokens, all eight object types, indirect references, streams |
| Cross-references | Classic tables, cross-reference streams, object streams, hybrid `/XRefStm`, incremental `/Prev` chains, scan-based recovery |
| Filters | `FlateDecode` (with PNG and TIFF predictors), `LZWDecode` (with `EarlyChange`), `ASCIIHexDecode`, `ASCII85Decode`, `RunLengthDecode` |
| Structure | Catalog, page tree with inherited attributes, boxes, rotation, `UserUnit`, effective version, `/Extensions` inventory |
| Metadata | `Info` and the XMP read subset (ISO 16684-1:2019) normalized to the V1 allowlist, XMP winning per field and disagreement reported; the raw packet is never preserved |
| Text | Graphics and text state, all show-text operators, Form XObjects, marked-content `ActualText`, simple-font encodings with `/Differences`, `ToUnicode` CMaps, composite fonts through `Identity-H` |
| Semantics | Geometric reading-order assembly, list detection, link annotations under the URI policy |
| Writer | New PDF 1.7 files, standard font names with WinAnsi encoding, Flate content streams, colour, decorations, alignment, lists, link annotations, normalized metadata |

Nothing in that list needs a third-party runtime dependency, a bundled font, a
glyph list, a metric file, or a codec asset. `FlateDecode` uses the .NET
runtime's DEFLATE implementation; the encoding tables and the approximate metric
model are Broiler-authored (see §6).

## 3. What the base build recognizes but does not do

Each of these is *detected and skipped* with a stable diagnostic. The document
still reads; the affected construct is reported rather than guessed at.

| Technology | Diagnostic | Register row |
|---|---|---|
| `DCTDecode` (JPEG) | `pdf.image.dct.tuple-unsupported` or `pdf.image.dct.color-transform-uncertain`. `pdf.image.dct.progressive-unsupported` is retained as API and emitted by nothing here since IP-005 was widened on 2026-09-02 | IP-005 and IP-006 (both **approved**; see §4.1.1) |
| `JPXDecode` (JPEG 2000) | `pdf.filter.jpx.unsupported`, carrying the codestream's tuple where the reader is composed | IP-007 (**approved** for Part 1; no decoder written) |
| `JBIG2Decode` | `pdf.filter.jbig2.unsupported`, carrying the stream's segment inventory where the filter is composed | IP-008 (**approved**; MMR generic regions decode, the arithmetic decoder is unwritten) |
| Any other named filter | `pdf.filter.not-composed` | — |
| Embedded font programs | `pdf.font.program-not-composed` | IP-012 (**approved** for inspection; see §4.4) |
| Type 3 fonts | `pdf.font.type3-unsupported` | — |
| Inline images, and images naming a filter with no composed implementation | `pdf.image.not-composed` | IP-005 |
| A decoded image the caller's policy refused | `pdf.image.extraction-denied` | — |
| Text needing a font the caller did not provision | `pdf.write.no-font-configured` | §11.3's chosen path: the caller supplies fonts, this project bundles none |
| Encrypted documents | `pdf.encryption.unsupported` (rejection) | IP-015 |
| Signatures | `pdf.signature.not-validated` | IP-016 |

Encryption is the one entry that rejects the whole document rather than skipping
a construct, and it does so from the trailers alone, before any content-bearing
object is resolved.

### 3.1 What a skip report carries

A code says *that* something was skipped. It is not enough on its own to decide
what to do about it, and the decisions in §5 need more than a name.

So each of these reports an inventory of what it met, gathered while reading and
emitted once per document:

| Report | What the note carries beyond the code |
|---|---|
| Images | How many, how many were inline, the pages, and each distinct declared tuple — pixel size, bits per component, colour-space family, filter chain. Where a decoder is composed: whether each image decoded, the tuple it was refused for, and whether the dictionary's declared size matches the samples |
| Embedded font programs | How many, in which formats (`FontFile` Type 1, `FontFile2` TrueType, `FontFile3` with its subtype), how many are symbolic, and how many have no `ToUnicode` map |
| Vector artwork | How many painting operations, classified by the shape they had — thin axis-aligned bars, axis-aligned areas, shadings, general paths — and the pages |
| XMP | The packet's size in bytes, its filter chain, how many normalized fields it supplied, how many properties fell outside the allowlist, and whether an `Info` dictionary stood behind it |
| Structure tree | How many top-level elements, whether the catalog marks the document as tagged, whether a `/ParentTree` exists, and the size of any role map |

Three properties hold for all of them.

**The codes do not change.** They are API, per
[`PdfDiagnosticCodes`](../src/Broiler.Documents.Pdf/PdfDiagnosticCodes.cs).
Detail is added to the message and the location; a host keying off a code is
unaffected.

**Nothing is decoded to produce them.** Every field is read from a dictionary
that was parsed anyway. The image tuple comes from the image dictionary, never
from sample data; the font format comes from the descriptor key, never from the
program. A build that composes no decoder still composes no decoder — which is
the point, since the tuple is what an IP-005 approval has to enumerate and the
descriptor key is what selects the part of IP-012 an inspector would sit under.

**Nothing added is a value.** A count, a page number, a pixel dimension, and a
name the format itself defines are constructs. A font's name, a metadata field's
contents, and a URI are not, and none appears. The ADR 0009 rule is unchanged:
a diagnostic names the construct and the reason.

Repeats are aggregated rather than dropped. The diagnostic sink keeps one entry
per code, and that entry carries the occurrence count and the pages it was seen
on, so a construct that appears four hundred times says four hundred instead of
looking like one.

## 4. The extension points

Everything optional arrives through one immutable object,
`PdfCodecServices`, handed to `PdfDocumentCodec` at construction. The codec
discovers nothing: no static registry, no module initializer, no environment
variable, no ambient font resolver, no platform lookup. A capability the
application did not supply is not present, and its absence is reported.

```csharp
var codec = new PdfDocumentCodec(
    PdfCodecServices.Base
        .WithStreamFilters(new JpegStreamFilter())      // Broiler.Documents.Pdf.Images
        .WithFontMetrics(new MeasuredMetrics())
        .WithUriPolicy(new PdfUriPolicy(allowHttp: true)));
```

### 4.1 `IPdfStreamFilter` — stream decoders

The main extension point. An implementation states its PDF filter name, its
inline-image abbreviation, and whether its output is a byte stream or image
samples, and decodes one stage of a filter chain.

A caller-supplied filter with the same name as a built-in *replaces* it, so a
reviewed implementation can supersede one of ours deliberately.

Requirements on an implementation:

- respect `PdfFilterContext.MaxDecodedBytes` **before** allocating an output
  buffer — the ceiling handed in is already the stricter of the per-stream limit
  and the document's remaining aggregate allowance, and it is never a fresh
  allowance;
- observe `PdfFilterContext.CancellationToken`;
- be pure and instance-owned, with no ambient or static state;
- return `PdfFilterResult.Malformed`, `.LimitExceeded`, or `.Unsupported` rather
  than throwing; and
- report `ProducesByteStream = false` for an image codec, so the object layer
  never tries to parse pixel data as PDF syntax.

Adding a filter changes no other code. The pipeline, the object store, the
content interpreter, and the writer are all unaware of which filters exist.

A caller writing their own filter can build the parameter set their tests need
with `PdfFilterParameters.From`; the type is otherwise only constructed by the
codec, which would leave an outside implementation with no way to exercise its
own `DecodeParms` handling. One parameter is a stream rather than a scalar —
`JBIG2Globals` — and it arrives already decoded through
`PdfFilterParameters.GetBytes`, so a filter never has to run the pipeline itself
or be handed something undecoded.

#### 4.1.1 The ones that ship: `JpegStreamFilter` and `CcittFaxStreamFilter`

`Broiler.Documents.Pdf.Images` holds the reviewed implementations of this
interface, and they are the worked examples of §5 rather than special cases.
`CcittFaxStreamFilter` joined `JpegStreamFilter` there when IP-009 cleared,
decoding all three fax schemes of ITU-T T.4 and T.6, and `JpxStreamFilter`
followed when IP-007 cleared — though that one reports rather than decodes, and
the distinction is worth keeping in view.

`Jbig2StreamFilter` completed the set. It decodes one region type for real —
generic regions coded with MMR, reusing the T.6 decoder that arrived with
`CcittFaxStreamFilter`, which is what a cleared row next to another cleared row
buys you — and refuses any page whose segments are not all supported, rather than
compositing the parts that decoded. Half a page is not a worse picture but a
misleading one: the text a symbol region would have drawn is exactly the content
a reader would assume was absent from the original.

A filter that never succeeds still earns its place, for a reason peculiar to
JPEG 2000. A `JPXDecode` image may legally omit `/ColorSpace` and
`/BitsPerComponent` from its PDF dictionary, because the codestream is the
authority for them — so the dictionary-derived tuple every other image reports is,
for this one, frequently blank. Reading the codestream header is the only way to
say what the image is, and what it is happens to be exactly what a decision about
writing the decoder needs.

That it is composed rather than built in is the interesting half. LZW cleared and
went straight into the base build (§1), and fax did not, because the two are not
the same kind of thing once the legal question is settled: a byte-stream
decompressor produces bytes whose size the data itself bounds, while a fax
decoder is a bit-level entropy parser producing a pixel buffer whose dimensions
come from the *dictionary* rather than from the data. That is the attack surface
§1 keeps out of the default build, and a cleared row does not change it.

It exists as a separate assembly on purpose. The codec's own dependency rule —
tested, not merely written down — is that `Broiler.Documents.Pdf` references the
codec framework and the model and nothing else. Keeping the adapter out of it is
what makes "not composed" mean *not linked*: a host that never mentions
`JpegStreamFilter` has no JPEG decoder in its process, which is the security
position of §1 and the reason an approved patent row did not move the decoder
into the base build.

What the adapter owns is the PDF half:

- it reads the JPEG's marker segments to learn the frame's tuple **before**
  decoding, because the byte ceiling has to be honoured before an output buffer
  exists and an image's output size is knowable only from its frame header;
- it resolves the colour transform from the Adobe `APP14` marker, the
  `/ColorTransform` parameter, or the format's default, and refuses every tuple
  outside the cleared rows by name — a
  self-contradicting pair of declarations under its own code, and a declaration
  it understands but cannot honour under the tuple code; and
- it converts a decoder fault into a skipped image, so a malformed picture costs
  the picture rather than the document.

One refusal is worth singling out, because it is the shape of thing this
boundary exists to make visible, and it is worth reading now that it has an
ending. A JPEG declaring colour transform 0 on three components is saying its
samples are already RGB. IP-006 cleared reading that declaration on 2026-09-01,
and the adapter read it — and then refused the image anyway, because the composed
decoder applied the YCbCr conversion unconditionally and would have reported
colours the document does not contain.

"We may not" and "we cannot" are different answers, they are fixed by different
work, and saying which one a host had hit is what made the fix findable. It was
the second: on 2026-09-02 the decoder gained a parameter for the resolved value,
and the declaration is now honoured. No register row moved, because none had
been in the way. A refusal recorded only as "refused" would have sent someone
looking for an approval that had been there all along.

The decoder itself stays in `Broiler.Media`, per §5 step 3. One condition travels
with it: that component's own human review records its managed image codecs as
security-sensitive and asks for resource limits and further review before they
process untrusted input. The adapter supplies the limits. It does not discharge
the rest, which is the second reason this is opt-in.

### 4.2 `IPdfFontMetricsProvider` — glyph advance widths

Supplies the widths the writer's line breaking uses and the reader's word-gap
estimation falls back to. The base build ships
`PdfApproximateFontMetrics`, which is deterministic and platform-independent but
not metrically exact, and says so through `pdf.write.metrics-approximate`.

Compose your own to replace it with a cleared metric set or with metrics
measured from a real font program. The provider reports `IsApproximate`, and the
writer stops emitting the approximation notice when it is false.

### 4.3 `PdfUriPolicy` — link admission

Decides which URI values may become active links, on both the read and the write
side. `https` is admitted by default; `http` and `mailto` need an explicit
opt-in; everything else is rejected.

Two properties hold regardless of how it is configured:

- validation performs no I/O — no DNS, no file probing, no preflight request; and
- a URI a reader admitted is not thereby authorized for output. The writer
  revalidates every link under the policy in force at the moment it emits the
  annotation, so a document read under a permissive policy cannot launder a link
  into one written under a strict one.

### 4.4 `IPdfFontProgramReader` — what the glyphs mean

The extension point for the one failure that a PDF's own structures cannot fix.
A file that embeds a subsetted font, marks it symbolic, and supplies no
`ToUnicode` map has said where to draw glyphs and nothing at all about what they
say. The encodings do not apply, the glyph names are inside the program, and the
codec extracts either nothing or a guess. It extracts nothing, and reports it.

A composed reader is handed one decoded program and the descriptor key it arrived
under, and returns glyph-to-text. `Broiler.Documents.Pdf.Fonts` is the reviewed
implementation, composing the sfnt parser from `Broiler.Graphics`:

```csharp
var codec = new PdfDocumentCodec(
    PdfCodecServices.Base.WithFontProgramReader(new GraphicsFontProgramReader()));
```

It is used only where a code **is** a glyph index by definition — a composite
font on an identity encoding — and only where `ToUnicode` is absent, because the
producer's own statement outranks anything recovered from a program. A simple
font reaches its glyphs through the program's own `cmap` under rules that depend
on which subtable it selected, and recovering text from one would be a guess
where the composite case is a lookup. The codec does not guess.

Two limits are worth stating plainly, because neither is a register question any
more:

- **Type 1 and bare CFF are declined.** The composed parser is a renderer. It
  answers "which glyph draws this character" and exposes no glyph names, no
  `post` table, and no CFF charset — a renderer never needs to know what a glyph
  is called. Recovering text from those formats needs inspection surface that
  does not exist yet, and adding it belongs in `Broiler.Graphics` under §5 step 3.
- **The map is built by probing.** The parser exposes no way to enumerate its
  character map, so the reader inverts it by asking the forward question once per
  code point in the BMP. That is a bounded loop, once per font, never a function
  of document size — and it is deliberately preferred to writing a second,
  unreviewed font parser.

Nothing composed here authorizes anything on the write side. Reading a program to
recover text is not embedding it; this release embeds no fonts, and an individual
font's embedding permissions (the OpenType `OS/2` `fsType` flags) are an
obligation on writer work that does not exist yet (IP-012).

## 5. Adding a technology, step by step

The order matters: the register row comes first, and the capability comes last.

1. **Clear the register row.** The row in
   [pdf-ip-licensing-register.md](pdf-ip-licensing-register.md) records the exact
   standard, edition, and subset; the source and its use terms; code and data
   provenance; the declaration record and review date; jurisdictions; and the
   reviewer and decision. Until the row is approved, the capability is blocked
   whatever the code does.
2. **Register the sources.** Add rows to
   [pdf-approved-sources.md](pdf-approved-sources.md) for anything consulted,
   with the permitted use for each. An independent implementation may be a
   black-box oracle under its own terms; its code, tables, fixtures, and
   generated data are not sources to copy.
3. **Implement behind the interface.** A new assembly, or a new type in an
   existing one — but not inside `Broiler.Documents.Pdf` unless the construct is
   genuinely PDF-only. A JPEG decoder belongs to `Broiler.Media.Image`; a font
   inspector belongs to `Broiler.Graphics`; the PDF package owns only the
   dictionary, filter-parameter, and resource-resolution semantics around them.
4. **Bring its own limits.** An extension enforces the budget it is handed, and
   charges work back rather than restarting the accounting.
5. **Add the corpus.** Fixtures go in the manifest with provenance and rights, or
   are generated in code as this suite's are. A document-level licence does not
   clear the fonts, images, profiles, or personal data inside it.
6. **Prove the boundary.** Tests must cover the composed path *and* the
   not-composed path: the diagnostic that the base build emits is part of the
   contract and must keep working.
7. **Move the matrix entry.** Only now does
   [pdf-feature-matrix.md](pdf-feature-matrix.md) change, and only to the state
   the evidence supports. `Supported` is invalid while the row's legal column is
   pending.

## 6. Data provenance in the base build

Two pieces of data in the base build deserve naming, because "no third-party
dependency" has to mean the data too.

**Encoding tables** (`PdfEncodings`). `StandardEncoding`, `WinAnsiEncoding`, and
`MacRomanEncoding` are expressed as code-point tables authored from the character
identity each slot denotes, and glyph names are mapped through a Latin repertoire
authored the same way plus the algorithmic `uniXXXX`/`uXXXX` forms. No
third-party glyph-list file is transcribed or shipped. The Symbol font's
built-in encoding joined them when IP-013 cleared, authored the same way.
`MacExpertEncoding` and ZapfDingbats' built-in encoding stay absent: mapping them
needs font-specific data this build does not carry, so a font using one reports
`pdf.text.mapping-missing-or-uncertain` instead of guessing. ZapfDingbats is
*recognized* in order to be refused — left to the Latin fallback it extracted
"ab" for two ornaments, and confident nonsense is worse than a gap.

**The XMP subset** (`XmpReader`, in `Broiler.Documents`) adds no data at all,
which is the point worth recording. It carries four namespace URIs and nine
property names — identifiers the format defines, not a table copied from
anywhere — and reads them with the platform's own XML reader. There is no schema
file, no namespace registry, no glyph or character data, and no third-party code
path. It lives in the shared package rather than the PDF one because XMP is ISO
16684-1, not a PDF construct; the PDF package owns only locating the `/Metadata`
stream, decoding it through the ordinary filter pipeline and budget, and
reconciling the result against `Info`.

**Metric model** (`PdfApproximateFontMetrics`). A small table of proportion
classes scaled per family, authored from the relative proportions of Latin
letterforms and erring slightly wide so a line measured as fitting still fits.
It is not any vendor's metrics and must not be described as such. Adobe's
Standard 14 metric files are *not* used; a build that wants real metrics composes
a provider for them under IP-012.

## 7. What this document does not do

It does not grant clearance. The base build is scoped to what this repository
implements itself, and that scope was chosen to keep the legal surface small —
but "small" is not "cleared", and no wording here or in the code may be read as a
patent-freedom, royalty-free, certification, conformance, or endorsement claim.
The register is the only place a capability becomes approved, and the
[roadmap's](pdf-support-roadmap.md) preview and release gates are the only path
to advertising one.
