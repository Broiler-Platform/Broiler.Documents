# PDF Construct Inventory

- **Status:** Active; regenerated whenever the codec's behavior changes
- **Component:** `Broiler.Documents.Pdf`
- **Updated:** 2026-08-25
- **Purpose:** to scope the IP-001 qualified review by stating exactly which PDF
  constructs the implementation reads, writes, recognizes without interpreting,
  and rejects

## 1. What this document is for

The IP/licensing register's IP-001 row asks a qualified reviewer to determine
whether Broiler's reader and writer fall within Adobe's ISO 32000-1 public
patent licence — a question about *`Compliant Implementation`* and *`Essential
Claim`* coverage, and therefore a question about a concrete artifact rather than
an intention. Before the base slice was implemented that question had no
definite subject. It now does, and this inventory is that subject written down.

A reviewer should be able to work from this document alone to answer "what does
this implementation actually do with the format?" without reading the code, and
to spot-check any row against the named source file when they want to.

**This is not a conformance claim.** It states behavior, not compliance. No entry
here promotes anything in the [feature matrix](pdf-feature-matrix.md), which
remains the sole authority on what Broiler may be *described* as supporting, and
nothing here is `Supported` while its register row is pending.

## 2. Provenance and the clause column

Every behavioral row was derived from the implementation, not from memory or from
any other PDF library: the operator list is the interpreter's dispatch switch,
the key list is the set of dictionary keys the code actually consults, and the
writer list is the set of names the serializer actually emits. The
"Where" column names the file so a reviewer can verify any row directly.

> **The clause column is provisional and must be verified against the licensed
> standard text.** Lawful standards access for implementers is still an open
> Phase 0 exit item ([status record](pdf-phase0-status.md)), so these references
> are a best-effort starting point offered to save the reviewer time — they are
> not themselves evidence, and a mismatch between this column and the standard
> means this column is wrong. Consistent with the roadmap's standards-source
> rule, clauses are cited and no standard prose, table, or diagram is reproduced
> here.

## 3. Reader — constructs interpreted

These are the constructs the codec parses and acts on. This is the set the
IP-001 determination has to cover on the reading side.

| Construct | Clause (provisional) | Behavior | Where |
|---|---|---|---|
| Lexical conventions: whitespace, delimiters, `%` comments | 7.2 | Tokenized | `Syntax/PdfLexer.cs` |
| Boolean, numeric, name (`#xx` escapes), null objects | 7.3.2, 7.3.3, 7.3.5, 7.3.9 | Parsed | `Syntax/PdfLexer.cs`, `Syntax/PdfObjectParser.cs` |
| String objects, literal and hexadecimal forms | 7.3.4 | Parsed, escapes resolved; kept as bytes, not text | `Syntax/PdfLexer.cs` |
| Array and dictionary objects | 7.3.6, 7.3.7 | Parsed on an explicit stack, depth- and entry-bounded | `Syntax/PdfObjectParser.cs` |
| Stream objects and stream extent | 7.3.8 | Parsed; `/Length` honoured, with a bounded `endstream` search when it disagrees | `Structure/PdfObjectStore.cs` |
| Indirect objects and references | 7.3.10 | Parsed; resolved lazily with cycle detection | `Syntax/PdfObjectParser.cs`, `Structure/PdfObjectStore.cs` |
| `ASCIIHexDecode` | 7.4.2 | Decoded | `Filters/BuiltInFilters.cs` |
| `ASCII85Decode` | 7.4.3 | Decoded | `Filters/BuiltInFilters.cs` |
| `FlateDecode` | 7.4.4 | Decoded via the .NET runtime's DEFLATE (IP-023) | `Filters/BuiltInFilters.cs` |
| PNG and TIFF predictors | 7.4.4.4 | Reversed | `Filters/PdfPredictor.cs` |
| `RunLengthDecode` | 7.4.5 | Decoded | `Filters/BuiltInFilters.cs` |
| Chained filters and `DecodeParms` | 7.4.1 | Applied in order, per-stage and aggregate budgets | `Filters/PdfFilterPipeline.cs` |
| File header, including a header not at byte zero | 7.5.2 | Located; version parsed | `Structure/PdfObjectStore.cs` |
| Classic cross-reference table and subsections | 7.5.4 | Parsed; free entries skipped | `Structure/PdfObjectStore.cs` |
| File trailer, `startxref`, `/Root`, `/Size`, `/Info` | 7.5.5 | Parsed; trailers merged newest-first | `Structure/PdfObjectStore.cs` |
| Incremental updates via `/Prev` | 7.5.6 | Chain walked newest-first; latest revision wins by construction | `Structure/PdfObjectStore.cs` |
| Object streams (`/Type /ObjStm`) | 7.5.7 | Members resolved through the production filter pipeline | `Structure/PdfObjectStore.cs` |
| Cross-reference streams (`/Type /XRef`), `/W`, `/Index` | 7.5.8 | Parsed; entry types 1 and 2 honoured | `Structure/PdfObjectStore.cs` |
| Hybrid-reference files (`/XRefStm`) | 7.5.8 | Companion stream loaded ahead of its classic section | `Structure/PdfObjectStore.cs` |
| Document catalog, `/Pages`, `/Lang`, `/Version` | 7.7.2 | Read | `PdfReader.cs`, `Structure/PdfVersion.cs` |
| Page tree, `/Kids`, `/Count`, leaf detection | 7.7.3 | Walked iteratively with a visited set | `Structure/PdfPageTree.cs` |
| Inherited page attributes: `/Resources`, `/MediaBox`, `/CropBox`, `/Rotate` | 7.7.3 | Inherited down the tree; `/UserUnit` carried | `Structure/PdfPageTree.cs` |
| Extensions dictionary: prefix, `/BaseVersion`, `/ExtensionLevel` | 7.12 | Inventoried for diagnostics only; never enables behavior | `Structure/PdfVersion.cs` |
| Content streams, single and as an array | 7.8.2 | Decoded and concatenated | `Text/PdfContentInterpreter.cs` |
| Resource dictionaries: `/Font`, `/XObject`, `/Properties` | 7.8.3 | Resolved lazily | `Text/PdfContentInterpreter.cs` |
| Text string types and the UTF-16 byte-order mark | 7.9.2 | Decoded; PDFDocEncoding otherwise | `Structure/PdfMetadataReader.cs` |
| Date strings `D:YYYYMMDDHHmmSSOHH'mm'` | 7.9.4 | Parsed; a zone-less value keeps its missing offset | `Structure/PdfMetadataReader.cs` |
| Graphics state: `q`, `Q`, `cm` | 8.4 | Interpreted; state stack bounded | `Text/PdfContentInterpreter.cs` |
| Device colour operators: `g`, `rg`, `k`, `sc`, `scn`, `cs` | 8.6 | Interpreted; CMYK converted by the format's device relationship, with no colour-management claim | `Text/PdfContentInterpreter.cs` |
| Form XObjects (`Do` on `/Subtype /Form`), `/Matrix`, `/Resources` | 8.10 | Executed under bounded recursion and a visited set | `Text/PdfContentInterpreter.cs` |
| Text objects `BT`/`ET` | 9.4.1 | Interpreted | `Text/PdfContentInterpreter.cs` |
| Text state: `Tf`, `Tc`, `Tw`, `Tz`, `TL`, `Ts`, `Tr` | 9.3 | Interpreted | `Text/PdfContentInterpreter.cs` |
| Text positioning: `Td`, `TD`, `Tm`, `T*` | 9.4.2 | Interpreted | `Text/PdfContentInterpreter.cs` |
| Text showing: `Tj`, `TJ`, `'`, `"` | 9.4.3 | Interpreted, including `TJ` numeric adjustments | `Text/PdfContentInterpreter.cs` |
| Simple font dictionaries: `/BaseFont`, `/FirstChar`, `/Widths`, `/FontDescriptor` | 9.6 | Read | `Text/PdfFont.cs` |
| Character encoding: `/Encoding` name or dictionary, `/BaseEncoding`, `/Differences` | 9.6.6 | Applied | `Text/PdfFont.cs`, `Text/PdfEncodings.cs` |
| Standard, WinAnsi, MacRoman encodings | Annex D | Applied from Broiler-authored tables (IP-021) | `Text/PdfEncodings.cs` |
| Font subset name prefixes | 9.6 | Stripped structurally, not by guessing at the family name | `Text/PdfFont.cs` |
| Composite fonts: `/Type0`, `/DescendantFonts`, `/DW`, `/W`, `Identity-H`, `Identity-V` | 9.7 | Read | `Text/PdfFont.cs` |
| Font descriptors: `/Flags`, `/ItalicAngle`, `/StemV`, `/FontWeight`, `/MissingWidth` | 9.8 | Read for weight and slant only | `Text/PdfFont.cs` |
| `ToUnicode` CMaps: codespace ranges, `bfchar`, `bfrange`, bounded `usecmap` | 9.10.3 | Parsed; the preferred mapping route | `Text/PdfCMap.cs` |
| Marked content `BDC`/`EMC` and `/ActualText` | 14.6, 14.9.4 | `ActualText` replaces the glyphs it encloses | `Text/PdfContentInterpreter.cs` |
| Annotation dictionaries, `/Subtype /Link`, `/Rect` | 12.5.6.5 | Read | `Text/PdfLinkRegion.cs` |
| URI actions (`/A` with `/S /URI`) | 12.6.4.7 | Admitted by the URI policy, then projected as a link | `Text/PdfLinkRegion.cs`, `PdfUriPolicy.cs` |
| Document information dictionary | 14.3.3 | Projected to the normalized allowlist only | `Structure/PdfMetadataReader.cs` |

**Recovery behavior.** When the declared cross-reference data cannot produce a
catalog, the reader rebuilds the object map by scanning for `n g obj` headers and
reports it (`pdf.xref.recovered`). This is the only recovery path, it never runs
speculatively mid-parse, and it interprets no construct the table above omits.

## 4. Reader — recognized but not interpreted

These constructs are identified so the reader can name what it declined. **No
data of these kinds is decoded, executed, fetched, or projected**, which is the
distinction that matters for the register rows they belong to.

| Construct | Clause (provisional) | Diagnostic | Register row |
|---|---|---|---|
| `LZWDecode` | 7.4.4 | `pdf.filter.lzw.unsupported` | IP-010 |
| `CCITTFaxDecode` | 7.4.6 | `pdf.filter.ccitt.unsupported` | IP-009 |
| `JBIG2Decode` | 7.4.7 | `pdf.filter.jbig2.unsupported` | IP-008 |
| `DCTDecode` | 7.4.8 | `pdf.image.dct.tuple-unsupported` | IP-005, IP-006 |
| `JPXDecode` | 7.4.9 | `pdf.filter.jpx.unsupported` | IP-007 |
| `Crypt` filter | 7.4.10 | `pdf.filter.crypt.unsupported` | IP-015 |
| Image XObjects and inline images (`BI`/`ID`/`EI`) | 8.9 | `pdf.image.not-composed` | IP-005 |
| Embedded font programs (`/FontFile`, `/FontFile2`, `/FontFile3`) | 9.8 | `pdf.font.program-not-composed` | IP-012 |
| Type 3 fonts | 9.6.4 | `pdf.font.type3-unsupported` | — |
| `MacExpertEncoding` and symbolic built-in encodings | Annex D | `pdf.text.mapping-missing-or-uncertain` | IP-013 |
| Predefined CMaps other than the Identity pair | 9.7.5 | `pdf.text.mapping-missing-or-uncertain` | IP-013 |
| Metadata streams (XMP) | 14.3.2 | `document.metadata.raw-dropped` | IP-004 |
| Path painting and shading operators | 8.5, 8.7 | `pdf.import.vector-artwork-dropped` | — |
| JavaScript, Launch, GoToR, SubmitForm, ImportData actions | 12.6.4 | `pdf.active-content.removed` | — |
| Embedded files, screen, movie, rich media, 3D annotations | 12.5.6, 7.11 | `pdf.active-content.removed` | — |
| AcroForm `/SigFlags`, signature fields | 12.7, 12.8 | `pdf.signature.not-validated` | IP-016 |
| Structure tree (`/StructTreeRoot`), tagged PDF | 14.7, 14.8 | Reported; structure not consumed | IP-017 |
| Optional content, artifacts, invisible and clipping render modes | 8.11, 9.3.6 | `pdf.text.visibility-uncertain`; extracted without a visibility claim | — |
| Unapplied `/Redact` annotations | 12.5.6 | `pdf.redaction.not-applied` (error severity) | — |
| PDF 2.x version declarations | 7.5.2, 7.7.2 | `pdf.version.tolerated-not-supported` | IP-002 |
| Developer extension declarations | 7.12 | `pdf.extension.unsupported` | IP-003 |

## 5. Reader — constructs that reject the document

| Construct | Clause (provisional) | Behavior |
|---|---|---|
| `/Encrypt` in any effective trailer or cross-reference stream dictionary | 7.6 | The document is rejected with `pdf.encryption.unsupported` **before any object stream, catalog, metadata, font, image, annotation, or content is resolved**. No decrypt-dependent object is ever interpreted, and no password or document content reaches a diagnostic. |

## 6. Writer — constructs emitted

The writer creates new files only. It never rewrites an input, saves
incrementally, or carries a source document's objects, fonts, images,
identifiers, or raw metadata forward.

| Construct | Clause (provisional) | Emitted |
|---|---|---|
| `%PDF-1.7` header and binary comment | 7.5.2 | Always |
| Indirect objects, dictionaries, arrays, names, numbers, literal strings | 7.3 | Always |
| Stream objects with `/Length` | 7.3.8 | Content streams |
| `FlateDecode` on content streams | 7.4.4 | By default; disableable |
| Classic cross-reference table, trailer, `startxref`, `%%EOF` | 7.5.4, 7.5.5 | Always |
| Trailer `/Size`, `/Root`, `/Info`, `/ID` | 7.5.5 | `/Info` only when the caller supplies metadata; `/ID` derived from content when not caller-set |
| Document catalog, `/Lang` | 7.7.2 | `/Lang` only when supplied |
| Page tree, page objects, `/MediaBox`, `/Parent`, `/Count` | 7.7.3 | Always |
| Resource dictionary with `/Font` | 7.8.3 | Always |
| Text objects, `Tf`, `Tm`, `Tj` | 9.3, 9.4 | Always |
| Simple `/Type1` fonts by standard base name with `/Encoding /WinAnsiEncoding` | 9.6.2, 9.6.6 | Always; **no font program is ever embedded** |
| Device RGB fill (`rg`) and filled rectangles (`re f`) | 8.5.3, 8.6.3 | Colour, highlights, underline and strikethrough |
| Link annotations with `/A`, `/S /URI`, `/Border` | 12.5.6.5, 12.6.4.7 | Only for targets the URI policy re-admits at emission time |
| Document information dictionary | 14.3.3 | Only the normalized allowlist the caller supplied |
| Date strings | 7.9.4 | A zone-less value is written back without a zone |

**Never emitted:** embedded or subset font programs, composite/Type 0 fonts,
`ToUnicode` maps, raster images of any kind, any filter other than
`FlateDecode`, encryption, incremental updates, linearization, `/Alt`, a
structure tree, tagged semantics, XMP, or any profile conformance identifier.

## 7. Inputs relied on that are not ISO 32000-1

| Dependency | Use | Register row |
|---|---|---|
| The .NET runtime's DEFLATE/zlib implementation | `FlateDecode` and content-stream compression | IP-023, under IP-011 |
| Unicode character identities | The authored encoding tables and glyph-name repertoire | IP-013, IP-021 |
| RFC 3986 URI grammar | Link admission on read and write; no copied test vectors | IP-014 |
| Broiler-authored letterform proportions | Writer line breaking; not any vendor's metrics | IP-022 |

## 8. Questions this inventory puts to the reviewer

1. Does the read set in §3 constitute a `Compliant Implementation` for the
   purposes of the Adobe ISO 32000-1 public patent licence, and does that licence
   reach it? If coverage is partial, which rows fall outside?
2. Does the write set in §6 — a strict subset of §3's constructs — raise any
   question §3 does not?
3. Does the recognize-without-interpreting posture in §4 carry any exposure of
   its own? The codec identifies these constructs by name in order to decline
   them; it decodes nothing.
4. Do the four non-ISO dependencies in §7 close as source/licence reviews, and
   does IP-023 satisfy IP-011's implementation-provenance requirement?

## 9. Keeping this true

This document describes behavior, so it goes stale the moment behavior changes.
Any change that adds, removes, or moves a construct between §3, §4, §5, and §6
updates this file in the same commit, and any change that moves one *into* §3 or
§6 must first clear its register row per
[PDF extension points §5](pdf-extension-points.md#5-adding-a-technology-step-by-step).
