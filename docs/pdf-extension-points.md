# PDF Extension Points

- **Status:** Active
- **Component:** `Broiler.Documents.Pdf`
- **Updated:** 2026-08-25
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

**Legal.** LZW, JPEG (DCT), CCITT fax, JPEG 2000, and JBIG2 each have their own
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
`pdf.filter.lzw.unsupported` rather than "unknown filter", and a host can tell a
policy decision apart from a corrupt file.

## 2. What the base build carries

| Area | Implemented in the base build |
|---|---|
| Syntax | Tokens, all eight object types, indirect references, streams |
| Cross-references | Classic tables, cross-reference streams, object streams, hybrid `/XRefStm`, incremental `/Prev` chains, scan-based recovery |
| Filters | `FlateDecode` (with PNG and TIFF predictors), `ASCIIHexDecode`, `ASCII85Decode`, `RunLengthDecode` |
| Structure | Catalog, page tree with inherited attributes, boxes, rotation, `UserUnit`, effective version, `/Extensions` inventory |
| Metadata | `Info` normalized to the V1 allowlist; XMP detected and dropped |
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
| `LZWDecode` | `pdf.filter.lzw.unsupported` | IP-010 |
| `DCTDecode` (JPEG) | `pdf.image.dct.tuple-unsupported` | IP-005, IP-006 |
| `CCITTFaxDecode` | `pdf.filter.ccitt.unsupported` | IP-009 |
| `JPXDecode` (JPEG 2000) | `pdf.filter.jpx.unsupported` | IP-007 |
| `JBIG2Decode` | `pdf.filter.jbig2.unsupported` | IP-008 |
| Any other named filter | `pdf.filter.not-composed` | — |
| Embedded font programs | `pdf.font.program-not-composed` | IP-012 |
| Type 3 fonts | `pdf.font.type3-unsupported` | — |
| Raster images generally | `pdf.image.not-composed` | IP-005 |
| Encrypted documents | `pdf.encryption.unsupported` (rejection) | IP-015 |
| Raw XMP packets | `document.metadata.raw-dropped` | IP-004 |
| Signatures | `pdf.signature.not-validated` | IP-016 |

Encryption is the one entry that rejects the whole document rather than skipping
a construct, and it does so from the trailers alone, before any content-bearing
object is resolved.

## 4. The extension points

Everything optional arrives through one immutable object,
`PdfCodecServices`, handed to `PdfDocumentCodec` at construction. The codec
discovers nothing: no static registry, no module initializer, no environment
variable, no ambient font resolver, no platform lookup. A capability the
application did not supply is not present, and its absence is reported.

```csharp
var codec = new PdfDocumentCodec(
    PdfCodecServices.Base
        .WithStreamFilters(new ReviewedLzwFilter())
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
third-party glyph-list file is transcribed or shipped. `MacExpertEncoding` and
the symbolic built-in encodings of Symbol and ZapfDingbats are deliberately
absent: mapping them needs font-specific data this build does not carry, so a
font using one reports `pdf.text.mapping-missing-or-uncertain` instead of
guessing.

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
