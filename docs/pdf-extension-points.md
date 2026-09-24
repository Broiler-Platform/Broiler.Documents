# PDF Extension Points

- **Status:** Active
- **Component:** `Broiler.Documents.Pdf`
- **Updated:** 2026-09-24 (`IPdfColorProfileReader` added under IP-024; encrypted documents opened, and `IPdfRecipientDecryptor` added, under IP-015 and IP-025)
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
| Images | Samples from a filter chain the build can run, normalized to RGBA within roadmap §9.3's approved tuple — DeviceGray at 1/2/4/8 bits, DeviceRGB at 8, Indexed at 1/2/4/8 over a bounded palette, `/Decode` validated — with their stencil, colour-key, explicit and soft masks carried as alpha, and admitted through the caller's resource policy |
| Semantics | Reading order from a tagged document's structure tree where it covers the page, geometric assembly otherwise, list detection, link annotations under the URI policy, and the default optional-content configuration — content in a layer the catalog turns off is omitted |
| Writer | New PDF 1.7 files, standard font names with WinAnsi encoding, Flate content streams, colour, decorations, alignment, lists, link annotations, normalized metadata |
| Security | Encrypted documents opened with the standard security handler at revisions 2, 3, 4 and 6 - RC4, AES-128 and AES-256, crypt filters, `/EncryptMetadata` - with the caller's password or the document's empty one; the copy-and-extract permission enforced and the rest reported; and the PDF half of the public-key handler, whose envelopes a composed decryptor opens (§4.6). Nothing is ever encrypted |

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
| `JPXDecode` (JPEG 2000) | `pdf.filter.jpx.unsupported`, carrying the codestream's tuple and the construct refused | IP-007 (**approved** for Part 1; a decoder exists for one tile and the LRCP/RPCL progressions, and its EBCOT context tables are **pending** in SRC-018) |
| `JBIG2Decode` | `pdf.filter.jbig2.unsupported`, carrying the stream's segment inventory where the filter is composed | IP-008 (**approved**; generic regions decode under both coding methods, and symbol dictionaries, text regions and refinement arithmetically; the halftone regions, aggregate coding, the intermediate regions and every Huffman-coded form do not; the MQ probability table is **pending** in SRC-019) |
| Any other named filter | `pdf.filter.not-composed` | — |
| ICC profiles (`ICCBased`) | `pdf.image.decoded-not-projected`, naming the colour space. A composed reader converts to sRGB; see §4.5 | IP-024 (**approved**) |
| Embedded font programs | `pdf.font.program-not-composed`. A composed reader handles sfnt through its character map and bare CFF through its charset; Type 1 and CID-keyed CFF stay unread | IP-012 (**approved** for inspection; see §4.4). The CFF standard-strings table is pending in SRC-016 |
| Type 3 fonts — the glyph procedures only | `pdf.font.type3-unsupported`. The font's own encoding, `ToUnicode`, and `/FontMatrix` advances are read; only the procedures that draw the glyphs go unexecuted | — |
| Inline images, and images naming a filter with no composed implementation | `pdf.image.not-composed` | IP-005 |
| A decoded image the caller's policy refused | `pdf.image.extraction-denied` | — |
| Text needing a font the caller did not provision | `pdf.write.no-font-configured` | §11.3's chosen path: the caller supplies fonts, this project bundles none |
| Encrypted documents the build cannot open | `pdf.encryption.unsupported` for a handler, revision, or method outside the approved scope; `pdf.encryption.password-required` and `pdf.encryption.password-incorrect` for the standard handler; `pdf.encryption.extraction-not-permitted` where the permissions withhold what this codec produces. Each rejects the document | IP-015 (**approved**; see ADR 0015) |
| Documents encrypted for certificate recipients | `pdf.encryption.recipient-not-composed` without a recipient decryptor; `pdf.encryption.recipient-not-found` with one that opens no envelope | IP-025 (**approved**, the external standards' review recorded as not done; see §4.6) |
| Signatures | `pdf.signature.not-validated` | IP-016 |

Encryption is the one entry that rejects the whole document rather than skipping
a construct. It decides from the trailers alone, and opens the document -
authenticating it and checking the one permission this codec depends on -
before any content-bearing object is resolved, so a document it cannot open is
refused with nothing behind the trailer read (ADR 0015).

### 3.1 What a skip report carries

A code says *that* something was skipped. It is not enough on its own to decide
what to do about it, and the decisions in §5 need more than a name.

So each of these reports an inventory of what it met, gathered while reading and
emitted once per document:

| Report | What the note carries beyond the code |
|---|---|
| Images | How many, how many were inline, the pages, and each distinct declared tuple — pixel size, bits per component, colour-space family, filter chain. Where a decoder is composed: whether each image decoded, the tuple it was refused for, and whether the dictionary's declared size matches the samples. For one that decoded and did not reach the model, the distinct constructs that stopped it — a stencil painted with a pattern, a mask this build cannot read, a colour space or depth outside the subset — since composing a decoder, widening the subset, and fixing a self-contradicting document are answered by different work |
| Embedded font programs | How many, in which formats (`FontFile` Type 1, `FontFile2` TrueType, `FontFile3` with its subtype), how many are symbolic, and how many have no `ToUnicode` map |
| Vector artwork | How many painting operations across the document, how many of them were read back — as a table's rules and shades, a run's underline or strikethrough, or a run's background — how many were fills in the paper's colour on bare paper, which paint nothing, and the rest classified by the shape they had — thin axis-aligned bars, axis-aligned areas, shadings, general paths — with how many of those repaint a shape already painted, and the pages that lost something |
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
        .WithColorProfileReader(new IccColorProfileReader())  // Broiler.Documents.Pdf.Images
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
followed when IP-007 cleared — though that one only reported until its decoder
was written on 2026-09-03, and what it decodes has still never been checked
against a real image, a distinction worth keeping in view.

`Jbig2StreamFilter` completed the set, and has since grown into the shape a
scanned page actually has. Generic regions decode under MMR by reusing the T.6
decoder that arrived with `CcittFaxStreamFilter`, which is what a cleared row
next to another cleared row buys you, and under the arithmetic coder since
2026-09-03; the symbol dictionaries and text regions that carry the text of a
scanned page decode since the same day, and so does the refinement that corrects
them — a region over the page, an instance before it is drawn, or a dictionary
symbol defined as a correction of another. It still refuses any page whose
segments are not all supported, rather than compositing the parts that decoded.
Half a page is not a worse picture but a misleading one: what a halftone region
would have drawn is exactly the content a reader would assume was absent from
the original.

Refinement is also why the filter composites in stream order rather than
collecting regions and drawing them at the end. A refinement region's reference
is the page beneath it, so the page has to exist, and to hold everything earlier
in the stream, before the segment correcting it is read. That in turn is why the
combination operators are now distinguished: OR can only add black, and a
correction that clears a pixel has no way to say so without REPLACE.

Reading a header earns its place even where nothing decodes, for a reason
peculiar to JPEG 2000. A `JPXDecode` image may legally omit `/ColorSpace` and
`/BitsPerComponent` from its PDF dictionary, because the codestream is the
authority for them — so the dictionary-derived tuple every other image reports is,
for this one, frequently blank. Reading the codestream header is the only way to
say what the image is, and what it is happens to be exactly what a decision
about writing the decoder needed.

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
opt-in; everything else is rejected. An absolute URI is required, so a target that
is only a same-document `#fragment` falls under "everything else" — the five
interchange codecs carry one under `DocumentLinkTarget` and this policy does not,
and no configuration changes that, because the absolute test runs before the
scheme switch. A fragment inside an absolute URI is a different thing and is kept.

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
under, and returns glyph-to-text — or, for a program that names its glyphs rather
than mapping them from characters, glyph-to-name, which the codec resolves
through the one glyph-name table it authors so that no reader carries a second
copy. `Broiler.Documents.Pdf.Fonts` is the reviewed implementation, composing the
sfnt parser from `Broiler.Graphics` and reading a bare CFF's charset itself:

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

- **Type 1 and CID-keyed CFF are declined.** `BFontProgramInspector` answers one
  question — what a glyph spells — and exposes no glyph names, no `post` table
  and no CFF charset, because nothing that reads an untrusted program needs to
  know what a glyph is called. A bare CFF is therefore read here instead, by
  walking its charset for the names it gives its glyphs. Type 1 still needs
  inspection surface that does not exist, and adding it belongs in
  `Broiler.Graphics`; a CID-keyed CFF's charset holds character identifiers from
  a collection rather than names, so resolving them as names would invent glyphs
  the font never mentioned.
- **The accepted tuple is narrower than "an sfnt".** The inspector takes a bare
  sfnt at version `0x00010000` or `OTTO` and refuses, by name, WOFF and WOFF2
  containers, font collections, variable fonts, CFF2, colour and bitmap glyph
  tables, Graphite and AAT (PDF roadmap §6.5). A program outside that tuple is
  reported as uninspected exactly as a malformed one is: this side of the
  boundary refuses rather than repairs, and the renderer's parser — which
  follows a WOFF container and reads a short table as zeros so a font a caller
  chose still draws — is deliberately not what runs here.

Nothing composed here authorizes anything on the write side. Reading a program to
recover text is not embedding it; this release embeds no fonts, and an individual
font's embedding permissions (the OpenType `OS/2` `fsType` flags) are an
obligation on writer work that does not exist yet (IP-012).

### 4.5 `IPdfColorProfileReader` — what a colour value is

An `/ICCBased` colour space states what its values mean only through the ICC
profile it carries, and a reader without the profile either refuses the image
or draws it in colours the document never stated. The base build refuses it,
by name, and a composed reader converts it.

A reader is handed one decoded profile, the component count the colour space
declares, and the rendering intent in force, and returns a
`PdfColorTransform`: one colour value in, an sRGB triple out, built once and
used for every image that names the profile. `Broiler.Documents.Pdf.Images`
supplies `IccColorProfileReader`, an independent implementation from the
structure of ICC.1 (IP-024, SRC-022):

```csharp
var codec = new PdfDocumentCodec(
    PdfCodecServices.Base.WithColorProfileReader(new IccColorProfileReader()));
```

The codec owns what is PDF: resolving the space through the resource
dictionary, `/N` and `/Range`, decoding the profile stream through the shared
pipeline and budget under `PdfLimits.MaxColorProfileBytes`, the image's
`/Intent` and the graphics state's `ri` and `/RI`, and converting an Indexed
palette entry by entry. The reader owns the profile: the header and tag table,
matrix profiles and the `A2B` tables, the connection space, and the
destination. A picture a composed codec decoded is converted after it, and
without a reader keeps the codec's colours, which is the alternate PDF
32000-1 8.6.5.5 has a reader without profiles use.

Three limits are worth stating plainly:

- **The destination is sRGB, computed rather than stored.** The matrix from
  the connection space is derived from sRGB's chromaticities and the linear
  Bradford adaptation (SRC-023), so a picture tagged with an sRGB profile
  comes back as it stored its values — the case almost every document is.
- **Declined by name**: device links, abstract and named-colour profiles,
  version 5, colour spaces other than Gray, RGB and CMYK, tables with more than
  four inputs, and any structure past the profile's bytes. The reason goes into
  the image note, which is why a reader must never put a value from the profile
  in it.
- **Optional tags are never read.** In particular the `outputResponseTag`,
  which carries the one patent declaration IP-024 records, has no part in a
  conversion and no code path here.

The reader belongs with the other colour work in `Broiler.Media` in the end,
as the JPEG 2000 and JBIG2 decoders moved there; it is in the image satellite
until then, behind the same composition boundary.

### 4.6 `IPdfRecipientDecryptor` - opening a certificate recipient's envelope

A document encrypted for certificates stores, for each list of recipients, a CMS
`EnvelopedData` object (RFC 5652) whose content is a 20-byte seed and the list's
permissions. The codec does everything that is PDF: it finds the lists - in the
encryption dictionary, or in each crypt filter - hands them in order to the
decryptor, derives each crypt filter's key from the seed the first opened
envelope yields and every list's bytes, and applies the permissions it carries.
The decryptor does the one thing the codec cannot: undo an envelope with a
recipient's private key.

No implementation ships here, deliberately. The ready one is `EnvelopedCms` in
`System.Security.Cryptography.Pkcs`, which the dependency rule keeps out of
every shipped assembly (ADR 0001), and the cryptographic message syntax is an
external standard of its own, whose review IP-025 records as not done. A host
composes one in a few lines, with the certificates of whoever it runs for:

```csharp
sealed class CertificateRecipients(X509Certificate2 certificate) : IPdfRecipientDecryptor
{
    public byte[]? Open(ReadOnlySpan<byte> envelope, PdfRecipientContext context)
    {
        var cms = new EnvelopedCms();
        cms.Decode(envelope);
        foreach (RecipientInfo recipient in cms.RecipientInfos)
        {
            if (recipient.RecipientIdentifier.Value is not X509IssuerSerial id ||
                id.IssuerName != certificate.Issuer || id.SerialNumber != certificate.SerialNumber)
                continue;

            using RSA? key = certificate.GetRSAPrivateKey();
            if (key is null)
                return null;
            cms.Decrypt(recipient, key);          // with this key: never a store search
            return cms.ContentInfo.Content;
        }

        return null;
    }
}

var codec = new PdfDocumentCodec(
    PdfCodecServices.Base.WithRecipientDecryptor(new CertificateRecipients(certificate)));
```

The contract is the one the other readers have: return null for an envelope no
held key opens and for one that cannot be parsed, never a partial or guessed
content; the codec treats a throw the same way. It never searches a certificate
store itself, and neither should a decryptor - `EnvelopedCms.Decrypt()` without
a key does, which is why the example passes one.

A password is not composed here. It belongs to one document, not to the
application, and travels in the read options instead
(`PdfReadOptions.WithCredentials`).

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
