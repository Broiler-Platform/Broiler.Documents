# ADR 0012: PDF Base Implementation Scope And Composed Extensions

**Status:** Accepted
**Date:** 2026-08-25

## Context

ADRs 0007–0011 settled what the PDF component is, how its contracts behave, what
its security and privacy policy is, and how its claims are governed. They did not
settle a question that only arises once someone starts writing the code: which
part of PDF gets implemented first, and what happens at the boundary.

Waiting for every IP-register row to clear before writing anything would leave
the component indefinitely unstarted, and would mean designing its architecture
in the abstract. Implementing everything at once would tie the whole component's
release to the slowest legal review among LZW, JPEG, CCITT, JPEG 2000, and JBIG2
— technologies with entirely unrelated standards, patent positions, and
licensing regimes, where approval of one implies nothing about another.

Three separate concerns happen to point at the same seam:

- **Legal.** Each codec has its own register row and its own review schedule.
- **Security.** Image codecs and font-program parsers are the two largest attack
  surfaces in a PDF reader, and neither is needed to extract text.
- **Diagnostics.** A reader that cannot decode something should say precisely
  what and why, so a host can tell a policy decision from a corrupt file.

## Decision

**A base build limited to what this repository implements itself.** The initial
implementation covers PDF syntax and object stores, classic and stream
cross-references, object streams, incremental revisions, the `FlateDecode`
(with PNG and TIFF predictors), `ASCIIHexDecode`, `ASCII85Decode`, and
`RunLengthDecode` filters, document structure, normalized `Info` metadata,
logical text import through encodings and `ToUnicode` maps, links under the
shared URI policy, and a deterministic PDF 1.7 writer over the fourteen standard
font names. It has no third-party runtime dependency and bundles no font, glyph
list, metric file, ICC profile, or codec asset.

**Every other technology is recognized, not implemented.** LZW, DCT/JPEG, CCITT,
JPEG 2000, JBIG2, embedded font programs, image extraction, and encryption are
each detected and reported with a stable, technology-specific diagnostic code.
The base build knows every filter *exists* so it can name the one it declined;
it does not know how to decode any of them.

**Extensions arrive by composition, never by discovery.** Optional capabilities
are supplied through one immutable service graph, `PdfCodecServices`, handed to
the codec at construction: stream filters (`IPdfStreamFilter`), font metrics
(`IPdfFontMetricsProvider`), and the URI policy. The codec resolves nothing
through statics, module initializers, environment variables, ambient font
resolvers, or platform registries. Adding a technology changes the service graph
and nothing else — not the parser, not the interpreter, not the writer.

**Data authored here, not transcribed.** The encoding tables and the writer's
metric model are authored in this repository from character identities and
letterform proportions (register rows IP-021 and IP-022). Adobe's Standard 14
metric files are not used, and output must never be described as using any
vendor's metrics.

**Implementation is not a claim.** The package is `IsPackable=false`, and its
registration is confined to the composition roots that have been opened to it —
at the time of writing none, and since the roadmap §10.1 read-preview candidate
the Windows and Linux Writer heads, for opening only — enforced by tests.
Nothing in the feature matrix may reach `Supported` while its register row is
pending, regardless of how complete the code is.

**The order for adding a technology is fixed**: the register row clears, the
sources are recorded, the implementation goes behind the interface in its
correct owning component, its limits and corpus follow, both the composed and the
not-composed paths are tested, and only then does the matrix entry move. It is
specified in [PDF extension points](../pdf-extension-points.md).

## Consequences

- The component is real, exercised, and testable now, while the number of live
  IP-register rows stays at four (IP-001, IP-011, IP-013, IP-014) rather than a
  dozen.
- The default build has a materially smaller attack surface than a full reader,
  and that is the shape a host gets unless it deliberately asks for more.
- A document using an uncleared technology still reads: the text comes through
  and the skipped construct is named. Callers can act on the specific code
  instead of treating every failure alike.
- The cost is that the base build is genuinely less capable than a full PDF
  reader — no images, no embedded fonts, no encrypted input — and its writer's
  line breaking is approximate rather than metrically exact. Both are stated in
  diagnostics rather than hidden.
- A capability that a caller composes is that caller's legal responsibility as
  well as its technical one. Composing a decoder does not move an IP-register row.
- This ADR does not supersede ADRs 0007–0011; it sits under 0007's scope decision
  and 0011's claims governance, and adds the sequencing rule they left open.
